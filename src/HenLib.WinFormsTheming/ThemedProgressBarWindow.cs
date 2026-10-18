using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HenLib.WinFormsTheming;

/// <summary>Paints the existing native control; its managed object and accessibility stay intact.</summary>
internal sealed class ThemedProgressBarWindow : NativeWindow, IDisposable
{
    private const int WmPaint = 0x000F, WmEraseBackground = 0x0014, WmShowWindow = 0x0018;
    private const int WmPrint = 0x0317, WmPrintClient = 0x0318;
    private const int PbmSetMarquee = 0x040A, PbmGetState = 0x0411;
    private const int PbmSetRange = 0x0401, PbmSetPosition = 0x0402, PbmDeltaPosition = 0x0403;
    private const int PbmStep = 0x0405, PbmSetRange32 = 0x0406, PbmSetBarColor = 0x0409;
    private const int PbmSetState = 0x0410, PbmSetBackgroundColor = 0x2001;
    private const int PbmGetRange = 0x0407, PbmGetPosition = 0x0408;
    private const int PbmGetBackgroundColor = 0x040E, PbmGetBarColor = 0x040F;
    private readonly ProgressBarAnimationController owner;
    private readonly ProgressBar control;
    private Bitmap? buffer;
    private Form? form;
    private bool disposed, stoppingNativeMarquee, marqueeRunning, stateCheckQueued;
    private int nativeState = 1, marqueeInterval;
    private int minimum, maximum = 100, position, nativeFill = -1, nativeTrack = -1;
    private double frozenMarqueePhase = 0.35;

    internal ThemedProgressBarWindow(ProgressBarAnimationController owner, ProgressBar control)
    {
        this.owner = owner;
        this.control = control;
        control.HandleCreated += HandleCreated;
        control.HandleDestroyed += HandleDestroyed;
        control.VisibleChanged += PresentationChanged;
        control.EnabledChanged += PresentationChanged;
        control.ParentChanged += PresentationChanged;
        control.SizeChanged += PresentationChanged;
        control.ForeColorChanged += PresentationChanged;
        control.BackColorChanged += PresentationChanged;
        control.DpiChangedAfterParent += PresentationChanged;
        Refresh();
    }

    private int BorderWidth => Math.Max(1, (int)Math.Round(control.DeviceDpi / 96.0));

    internal bool NeedsAnimation => !disposed && Handle != 0 && control.Visible && IsWindowVisible(Handle) && control.Enabled &&
        control.ClientSize.Width > BorderWidth * 2 && control.ClientSize.Height > BorderWidth * 2 && form?.WindowState != FormWindowState.Minimized &&
        owner.MotionEnabled && nativeState == 1 && (control.Style == ProgressBarStyle.Marquee
            ? marqueeRunning : position > minimum && position < maximum);

    internal void Refresh()
    {
        if (disposed)
        {
            return;
        }

        BindForm();
        if (control.IsHandleCreated && Handle != control.Handle)
        {
            if (Handle != 0)
            {
                ReleaseHandle();
            }
            AssignHandle(control.Handle);
            nativeState = (int)SendMessage(Handle, PbmGetState, 0, 0);
            marqueeRunning = control.Style == ProgressBarStyle.Marquee && control.MarqueeAnimationSpeed > 0;
            marqueeInterval = control.MarqueeAnimationSpeed;
            // Painting is ours in both modes. Stop the native marquee timer without
            // changing the public Style, speed, value, or native accessibility provider.
            int result = SetWindowTheme(Handle, " ", " ");
            if (result != 0)
            {
                owner.ReportDiagnostic("SetWindowTheme(progress)", result);
            }
            stoppingNativeMarquee = true;
            try
            {
                SendMessage(Handle, PbmSetMarquee, 0, 0);
            }
            finally
            {
                stoppingNativeMarquee = false;
            }
        }
        if (Handle != 0)
        {
            // Theme application owns property colors; direct native overrides remain
            // visible until the next theme application, as with the native renderer.
            SendMessage(Handle, PbmSetBackgroundColor, 0, ColorTranslator.ToWin32(control.BackColor));
            SendMessage(Handle, PbmSetBarColor, 0, ColorTranslator.ToWin32(control.ForeColor));
            ReadRangeAndPosition();
        }
        Invalidate();
        owner.StateChanged();
        QueueStateCheck();
    }

    internal void Invalidate()
    {
        if (!disposed && control.IsHandleCreated && !control.IsDisposed)
        {
            InvalidateRect(control.Handle, 0, false);
        }
    }

    private void HandleCreated(object? sender, EventArgs e) => Refresh();

    private void HandleDestroyed(object? sender, EventArgs e)
    {
        stateCheckQueued = false;
        if (Handle != 0)
        {
            ReleaseHandle();
        }
        owner.StateChanged();
    }

    private void PresentationChanged(object? sender, EventArgs e)
    {
        BindForm();
        Invalidate();
        owner.StateChanged();
        QueueStateCheck();
    }

    private void QueueStateCheck()
    {
        if (disposed || control.IsDisposed || !control.IsHandleCreated || stateCheckQueued)
        {
            return;
        }

        // Visibility events can precede the parent HWND becoming visible. Check
        // again after the current show/restore operation, without waiting for input.
        stateCheckQueued = true;
        try
        {
            control.BeginInvoke((Action)(() =>
            {
                stateCheckQueued = false;
                if (!disposed && !control.IsDisposed)
                {
                    owner.StateChanged();
                }
            }));
        }
        catch (InvalidOperationException) when (control.IsDisposed || !control.IsHandleCreated)
        {
            stateCheckQueued = false;
        }
    }

    private void BindForm()
    {
        Form? current = control.FindForm();
        if (ReferenceEquals(current, form))
        {
            return;
        }

        if (form is not null)
        {
            form.Resize -= PresentationChanged;
            form.Shown -= PresentationChanged;
            form.VisibleChanged -= PresentationChanged;
        }
        form = current;
        if (form is not null)
        {
            form.Resize += PresentationChanged;
            form.Shown += PresentationChanged;
            form.VisibleChanged += PresentationChanged;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (!disposed)
        {
            if (m.Msg == WmPaint)
            {
                if (m.WParam != 0)
                {
                    Paint(m.WParam);
                }
                else
                {
                    nint dc = BeginPaint(m.HWnd, out PaintState paint);
                    try
                    {
                        Paint(dc);
                    }
                    finally
                    {
                        EndPaint(m.HWnd, ref paint);
                    }
                }
                m.Result = 0;
                return;
            }
            if (m.Msg is WmPrintClient or WmPrint)
            {
                Paint(m.WParam);
                m.Result = 0;
                return;
            }
            if (m.Msg == WmEraseBackground)
            {
                m.Result = 1;
                return;
            }
            if (m.Msg == PbmSetMarquee)
            {
                if (!stoppingNativeMarquee)
                {
                    marqueeRunning = m.WParam != 0;
                    marqueeInterval = (int)m.LParam;
                }
                // Let native processing stop its own timer, including one started
                // before attachment. The shared managed timer drives the drawing.
                m.WParam = 0;
                base.WndProc(ref m);
                Invalidate();
                owner.StateChanged();
                return;
            }
        }

        int message = m.Msg;
        base.WndProc(ref m);
        if (!disposed && message == WmShowWindow)
        {
            Invalidate();
            owner.StateChanged();
            QueueStateCheck();
        }
        if (!disposed && message is PbmSetRange or PbmSetPosition or PbmDeltaPosition or PbmStep or
            PbmSetRange32 or PbmSetBarColor or PbmSetState or PbmSetBackgroundColor)
        {
            ReadRangeAndPosition();
            if (message == PbmSetState && Handle != 0)
            {
                nativeState = (int)SendMessage(Handle, PbmGetState, 0, 0);
            }
            if (message == PbmSetBarColor && Handle != 0)
            {
                nativeFill = (int)SendMessage(Handle, PbmGetBarColor, 0, 0);
            }
            if (message == PbmSetBackgroundColor && Handle != 0)
            {
                nativeTrack = (int)SendMessage(Handle, PbmGetBackgroundColor, 0, 0);
            }
            Invalidate();
            owner.StateChanged();
        }
    }

    private void ReadRangeAndPosition()
    {
        if (Handle != 0)
        {
            minimum = (int)SendMessage(Handle, PbmGetRange, 1, 0);
            maximum = (int)SendMessage(Handle, PbmGetRange, 0, 0);
            position = (int)SendMessage(Handle, PbmGetPosition, 0, 0);
        }
    }

    private static Color NativeColor(int value, Color fallback) =>
        (value & unchecked((int)0xFF000000)) == 0 ? ColorTranslator.FromWin32(value) : fallback;

    private void Paint(nint dc)
    {
        Size size = control.ClientSize;
        if (dc == 0 || size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        if (buffer?.Size != size)
        {
            buffer?.Dispose();
            buffer = new Bitmap(size.Width, size.Height);
        }
        using (Graphics graphics = Graphics.FromImage(buffer))
        {
            Draw(graphics, size);
        }
        // A mirrored HWND must not mirror our already RTL-aware bitmap a second time.
        uint previousLayout = SetLayout(dc, 0);
        try
        {
            using Graphics target = Graphics.FromHdc(dc);
            target.DrawImageUnscaled(buffer, Point.Empty);
        }
        finally
        {
            if (previousLayout != uint.MaxValue)
            {
                SetLayout(dc, previousLayout);
            }
        }
    }

    private void Draw(Graphics graphics, Size size)
    {
        ThemePalette palette = owner.Palette;
        bool contrast = owner.Context.IsHighContrast;
        Color border = palette[ThemeColorRole.ProgressBorder];
        Color track = contrast ? SystemColors.Window : NativeColor(nativeTrack, control.BackColor);
        Color fill = !control.Enabled ? palette.DisabledText : contrast ? SystemColors.Highlight : nativeState switch
        {
            2 => palette[ThemeColorRole.ProgressErrorFill],
            3 => palette[ThemeColorRole.ProgressPausedFill],
            _ => NativeColor(nativeFill, control.ForeColor)
        };
        graphics.Clear(border);
        int edge = BorderWidth;
        var inside = new Rectangle(edge, edge, Math.Max(0, size.Width - edge * 2), Math.Max(0, size.Height - edge * 2));
        if (inside.Width == 0 || inside.Height == 0)
        {
            return;
        }
        using var trackBrush = new SolidBrush(track);
        graphics.FillRectangle(trackBrush, inside);
        bool rtl = control.RightToLeft == RightToLeft.Yes && control.RightToLeftLayout;
        Rectangle filled;
        if (control.Style == ProgressBarStyle.Marquee)
        {
            if (NeedsAnimation)
            {
                double period = Math.Max(300.0, (marqueeInterval <= 0 ? 30.0 : marqueeInterval) * 40.0);
                frozenMarqueePhase = owner.ElapsedMilliseconds % period / period;
            }
            else
            {
                // An indeterminate operation remains visible when motion is disabled.
                frozenMarqueePhase = 0.5;
            }
            int width = Math.Max(1, inside.Width / 3);
            int offset = (int)Math.Round(frozenMarqueePhase * (inside.Width + width)) - width;
            filled = new Rectangle(rtl ? inside.Right - offset - width : inside.Left + offset, inside.Top, width, inside.Height);
            filled.Intersect(inside);
        }
        else
        {
            double range = (double)maximum - minimum;
            double fraction = range <= 0 ? 0 : Math.Clamp(((double)position - minimum) / range, 0, 1);
            int width = (int)Math.Round(inside.Width * fraction);
            filled = new Rectangle(rtl ? inside.Right - width : inside.Left, inside.Top, width, inside.Height);
        }
        if (filled.Width <= 0)
        {
            return;
        }

        using var fillBrush = new SolidBrush(fill);
        graphics.FillRectangle(fillBrush, filled);
        if (contrast || !control.Enabled)
        {
            return;
        }
        using var gloss = new LinearGradientBrush(filled, Color.FromArgb(40, Color.White), Color.FromArgb(0, Color.White), 90f);
        graphics.FillRectangle(gloss, filled);
        if (NeedsAnimation && control.Style != ProgressBarStyle.Marquee)
        {
            float bandWidth = Math.Max(16 * control.DeviceDpi / 96f, inside.Width * 0.28f);
            double phase = owner.ElapsedMilliseconds % 2200.0 / 2200.0;
            float offset = (float)(phase * (filled.Width + bandWidth)) - bandWidth;
            float x = rtl ? filled.Right - offset - bandWidth : filled.Left + offset;
            var band = new RectangleF(x, inside.Top, bandWidth, inside.Height);
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.SetClip(filled);
                using var shimmer = new LinearGradientBrush(band, Color.Transparent, Color.Transparent, 0f)
                {
                    InterpolationColors = new ColorBlend
                    {
                        Colors = [Color.FromArgb(0, Color.White), Color.FromArgb(105, Color.White), Color.FromArgb(0, Color.White)],
                        Positions = [0, 0.5f, 1]
                    }
                };
                graphics.FillRectangle(shimmer, band);
            }
            finally
            {
                graphics.Restore(saved);
            }
        }
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
        control.VisibleChanged -= PresentationChanged;
        control.EnabledChanged -= PresentationChanged;
        control.ParentChanged -= PresentationChanged;
        control.SizeChanged -= PresentationChanged;
        control.ForeColorChanged -= PresentationChanged;
        control.BackColorChanged -= PresentationChanged;
        control.DpiChangedAfterParent -= PresentationChanged;
        if (form is not null)
        {
            form.Resize -= PresentationChanged;
            form.Shown -= PresentationChanged;
            form.VisibleChanged -= PresentationChanged;
        }
        if (Handle != 0)
        {
            ReleaseHandle();
        }
        buffer?.Dispose();
        buffer = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintState
    {
        public nint DeviceContext;
        public int Erase;
        public NativeRect UpdateRectangle;
        public int Restore, IncrementalUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [DllImport("user32.dll")] private static extern nint BeginPaint(nint window, out PaintState paint);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EndPaint(nint window, ref PaintState paint);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool InvalidateRect(nint window, nint rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(nint window, string application, string classes);
    [DllImport("gdi32.dll")] private static extern uint SetLayout(nint dc, uint layout);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
}
