using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HenLib.WinFormsTheming;

/// <summary>Recolors native disabled edit text without changing enabled state, content, or layout.</summary>
internal sealed class ThemedDisabledTextBoxWindow : NativeWindow, IDisposable
{
    private const int WmPaint = 0x000F, WmEraseBackground = 0x0014;
    private const int WmPrint = 0x0317, WmPrintClient = 0x0318, PrfClient = 0x0004;
    private readonly TextBoxBase control;
    private ThemeContext context;
    private ThemePalette? palette;
    private Bitmap? buffer;
    private int[] pixels = [];
    private bool disposed, renderingNative;

    internal ThemedDisabledTextBoxWindow(TextBoxBase control)
    {
        this.control = control;
        control.HandleCreated += HandleCreated;
        control.HandleDestroyed += HandleDestroyed;
        control.EnabledChanged += EnabledChanged;
    }

    internal void Refresh(ThemeContext context, ThemePalette palette)
    {
        if (disposed)
        {
            return;
        }
        this.context = context;
        this.palette = palette;
        BindHandle();
        control.Invalidate();
    }

    private void BindHandle()
    {
        if (!disposed && control.IsHandleCreated && Handle != control.Handle)
        {
            ReleaseHandle();
            AssignHandle(control.Handle);
        }
    }

    private void HandleCreated(object? sender, EventArgs e) => BindHandle();

    private void HandleDestroyed(object? sender, EventArgs e) => ReleaseHandle();

    private void EnabledChanged(object? sender, EventArgs e) => control.Invalidate();

    protected override void WndProc(ref Message message)
    {
        bool paintDisabled = !disposed && !renderingNative && !control.Enabled &&
            context.IsDark && !context.IsHighContrast && palette is not null;
        if (!paintDisabled || message.Msg is not (WmPaint or WmPrint or WmPrintClient or WmEraseBackground) ||
            GetParent(message.HWnd) == 0)
        {
            base.WndProc(ref message);
            return;
        }
        if (message.Msg == WmEraseBackground)
        {
            message.Result = 1;
            return;
        }
        if (message.Msg == WmPrint)
        {
            // Keep the native border/scrollbars; render the client once below.
            Message nativePrint = message;
            nativePrint.LParam = (nint)(message.LParam.ToInt64() & ~PrfClient);
            base.WndProc(ref nativePrint);
            message.Result = nativePrint.Result;
            if ((message.LParam.ToInt64() & PrfClient) != 0 && !disposed && Handle != 0)
            {
                PaintWindowDc(message.WParam);
            }
            return;
        }
        if (message.Msg == WmPrintClient || message.WParam != 0)
        {
            Paint(message.WParam);
        }
        else
        {
            nint dc = BeginPaint(message.HWnd, out PaintState state);
            try
            {
                Paint(dc);
            }
            finally
            {
                EndPaint(message.HWnd, ref state);
            }
        }
        message.Result = 0;
    }

    private void PaintWindowDc(nint dc)
    {
        if (dc == 0 || !GetWindowRect(Handle, out NativeRect window))
        {
            return;
        }
        Rectangle client = control.RectangleToScreen(control.ClientRectangle);
        int saved = SaveDC(dc);
        try
        {
            SetLayout(dc, 0);
            OffsetViewportOrgEx(dc, client.Left - window.Left, client.Top - window.Top, 0);
            Paint(dc);
        }
        finally
        {
            RestoreDC(dc, saved);
        }
    }

    private void Paint(nint dc)
    {
        Size size = control.ClientSize;
        if (dc == 0 || size.Width <= 0 || size.Height <= 0 || palette is null)
        {
            return;
        }
        if (buffer?.Size != size)
        {
            buffer?.Dispose();
            buffer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            pixels = new int[size.Width];
        }
        // Native EDIT uses the OS GrayText value, which differs from .NET Dark SystemColors.
        Color nativeText = ColorTranslator.FromWin32(GetSysColor(17));
        Color mask = Color.FromArgb(MaskChannel(nativeText.R), MaskChannel(nativeText.G), MaskChannel(nativeText.B));
        RenderNative(mask);
        Color background = control.BackColor;
        Color foreground = ResolveForeground(palette, background);
        Recolor(mask, nativeText, background, foreground);
        int saved = SaveDC(dc);
        try
        {
            SetLayout(dc, 0);
            IntersectClipRect(dc, 0, 0, size.Width, size.Height);
            using Graphics target = Graphics.FromHdc(dc);
            target.DrawImageUnscaled(buffer, Point.Empty);
        }
        finally
        {
            RestoreDC(dc, saved);
        }
    }

    private void RenderNative(Color mask)
    {
        // A contrasting temporary paint brush exposes glyph coverage even when the
        // real background equals native GrayText. It never changes a control property.
        using var background = new NativeBackgroundScope(GetParent(Handle), Handle, mask);
        using Graphics graphics = Graphics.FromImage(buffer!);
        graphics.Clear(mask);
        nint dc = graphics.GetHdc();
        renderingNative = true;
        try
        {
            Message print = Message.Create(Handle, WmPrintClient, dc, (nint)(PrfClient | 0x8));
            base.WndProc(ref print);
        }
        finally
        {
            renderingNative = false;
            graphics.ReleaseHdc(dc);
        }
    }

    private void Recolor(Color mask, Color nativeText, Color background, Color foreground)
    {
        BitmapData data = buffer!.LockBits(new Rectangle(Point.Empty, buffer.Size),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < data.Height; y++)
            {
                nint row = data.Scan0 + y * data.Stride;
                Marshal.Copy(row, pixels, 0, data.Width);
                for (int x = 0; x < data.Width; x++)
                {
                    Color source = Color.FromArgb(pixels[x]);
                    int red = RecolorChannel(source.R, mask.R, nativeText.R, background.R, foreground.R);
                    int green = RecolorChannel(source.G, mask.G, nativeText.G, background.G, foreground.G);
                    int blue = RecolorChannel(source.B, mask.B, nativeText.B, background.B, foreground.B);
                    pixels[x] = Color.FromArgb(red, green, blue).ToArgb();
                }
                Marshal.Copy(pixels, 0, row, data.Width);
            }
        }
        finally
        {
            buffer.UnlockBits(data);
        }
    }

    private static int MaskChannel(byte value)
    {
        if (value < 128)
        {
            return 255;
        }
        return 0;
    }

    private static int RecolorChannel(byte source, byte mask, byte nativeText, byte background, byte foreground)
    {
        double coverage = (source - mask) / (double)(nativeText - mask);
        coverage = Math.Clamp(coverage, 0, 1);
        return (int)Math.Round(background + coverage * (foreground - background));
    }

    internal static Color ResolveForeground(ThemePalette palette, Color background)
    {
        if (Contrast(palette.DisabledText, background) >= 4.5)
        {
            return palette.DisabledText;
        }
        if (Contrast(palette.Text, background) >= 4.5)
        {
            return palette.Text;
        }
        if (Contrast(Color.Black, background) >= Contrast(Color.White, background))
        {
            return Color.Black;
        }
        return Color.White;
    }

    private static double Contrast(Color first, Color second)
    {
        double firstLight = Luminance(first);
        double secondLight = Luminance(second);
        return (Math.Max(firstLight, secondLight) + 0.05) / (Math.Min(firstLight, secondLight) + 0.05);
    }

    private static double Luminance(Color color)
    {
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        if (value <= 0.04045)
        {
            return value / 12.92;
        }
        return Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        control.HandleCreated -= HandleCreated;
        control.HandleDestroyed -= HandleDestroyed;
        control.EnabledChanged -= EnabledChanged;
        ReleaseHandle();
        buffer?.Dispose();
        buffer = null;
        pixels = [];
        if (!control.IsDisposed && !control.Disposing)
        {
            control.Invalidate();
        }
    }

    private sealed class NativeBackgroundScope : NativeWindow, IDisposable
    {
        private readonly nint edit;
        private readonly Color color;
        private readonly nint brush;

        internal NativeBackgroundScope(nint parent, nint edit, Color color)
        {
            this.edit = edit;
            this.color = color;
            brush = CreateSolidBrush(ColorTranslator.ToWin32(color));
            if (brush == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Creating the disabled edit paint brush failed.");
            }
            try
            {
                AssignHandle(parent);
            }
            catch
            {
                DeleteObject(brush);
                throw;
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg is 0x0133 or 0x0138 && message.LParam == edit)
            {
                SetBkColor(message.WParam, ColorTranslator.ToWin32(color));
                message.Result = brush;
            }
        }

        public void Dispose()
        {
            ReleaseHandle();
            DeleteObject(brush);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintState
    {
        public nint Dc;
        public int Erase;
        public NativeRect Bounds;
        public int Restore, Update;
        public long Reserved1, Reserved2, Reserved3, Reserved4;
    }

    [DllImport("user32.dll")]
    private static extern int GetSysColor(int index);
    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);
    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint window, out PaintState state);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint window, ref PaintState state);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("gdi32.dll")]
    private static extern int SaveDC(nint dc);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RestoreDC(nint dc, int saved);
    [DllImport("gdi32.dll")]
    private static extern uint SetLayout(nint dc, uint layout);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OffsetViewportOrgEx(nint dc, int x, int y, nint previous);
    [DllImport("gdi32.dll")]
    private static extern int IntersectClipRect(nint dc, int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateSolidBrush(int color);
    [DllImport("gdi32.dll")]
    private static extern int SetBkColor(nint dc, int color);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
}
