using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HenLib.WinFormsTheming.Verification;

internal static class DisabledTextChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static int Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ThemeMode startup = ThemeMode.Light;
        if (args.Contains("--dark"))
        {
            startup = ThemeMode.Dark;
        }
        ThemeMode opposite = ThemeMode.Dark;
        if (startup == ThemeMode.Dark)
        {
            opposite = ThemeMode.Light;
        }
        bool customPalette = false;
        using var manager = new ThemeManager(startup, new ThemeOptions
        {
            PaletteFactory = context =>
            {
                ThemePalette palette = ThemePalette.Create(context);
                if (customPalette)
                {
                    return palette with { DisabledText = Color.Lime };
                }
                return palette;
            }
        });
        using var form = new DisabledTextFixtureForm(manager);
        using var registration = manager.Attach(form);
        var log = new CheckLog("Disabled text " + startup);
        var originals = form.Samples.ToDictionary(sample => sample,
            sample => (sample.Text, sample.ForeColor, sample.ReadOnly, sample.RightToLeft));
        var handles = new Dictionary<Control, nint>();
        int enabledChanges = 0;
        foreach (TextBoxBase sample in form.Samples)
        {
            sample.EnabledChanged += (_, _) => enabledChanges++;
        }
        async Task Sample(string stage)
        {
            await Task.Delay(80);
            foreach (TextBoxBase sample in form.Samples)
            {
                var original = originals[sample];
                log.Check(!sample.Enabled && sample.ReadOnly == original.ReadOnly && sample.Text == original.Text,
                    stage + ": content or disabled/read-only state changed for " + sample.Name);
                log.Check(sample.ForeColor == original.ForeColor && sample.RightToLeft == original.RightToLeft,
                    stage + ": foreground property or reading direction changed for " + sample.Name);
                if (!sample.Visible)
                {
                    continue;
                }
                log.Check(!IsWindowEnabled(sample.Handle), stage + ": native edit became enabled for " + sample.Name);
                if (handles.TryGetValue(sample, out nint handle))
                {
                    log.Check(handle == sample.Handle, stage + ": native handle changed for " + sample.Name);
                }
                else
                {
                    handles[sample] = sample.Handle;
                }
                using var image = Capture(sample);
                if (manager.Context.IsDark)
                {
                    Color expected = ExpectedForeground(manager.Palette, sample.BackColor);
                    log.Check(Contrast(expected, sample.BackColor) >= 4.5, stage + ": disabled color lacks contrast");
                    log.Check(CountColor(image, expected) > 4, stage + ": readable text was not drawn for " + sample.Name);
                }
                CheckNativePolicy(sample, manager, image, log, stage);
            }
        }
        form.Shown += async (_, _) =>
        {
            try
            {
                await Sample("Startup");
                SendMessage(form.ScrollSample.Handle, 0x00B6, 8, 6);
                nint firstLine = SendMessage(form.ScrollSample.Handle, 0x00CE, 0, 0);
                foreach (ThemeMode mode in new[] { opposite, startup, opposite, startup })
                {
                    manager.SetMode(mode);
                    await Sample("Switched " + mode);
                    log.Check(SendMessage(form.ScrollSample.Handle, 0x00CE, 0, 0) == firstLine,
                        "Theme change reset native scrolling");
                }
                form.Pages.SelectedIndex = 1;
                await Sample("Previously hidden tab");
                manager.SetMode(opposite);
                await Sample("Hidden tab switched");
                form.Pages.SelectedIndex = 0;
                form.Hide();
                manager.SetMode(startup);
                form.Show();
                await Sample("Form switched while hidden");
                log.Check(enabledChanges == 0, "Painting or switching changed Enabled");

                form.DisabledParent.Enabled = true;
                log.Check(form.InheritedSample.Enabled && IsWindowEnabled(form.InheritedSample.Handle),
                    "Disabled parent permanently changed its child's local enabled state");
                form.DisabledParent.Enabled = false;
                form.ReadOnlySample.Enabled = true;
                using (var image = Capture(form.ReadOnlySample))
                {
                    CheckNativePolicy(form.ReadOnlySample, manager, image, log, "Enabled/read-only");
                }
                form.ReadOnlySample.Enabled = false;
                form.ReadOnlySample.Text = "Changed text\r\nWrap and resize these words 0123456789";
                originals[form.ReadOnlySample] = (form.ReadOnlySample.Text, form.ReadOnlySample.ForeColor,
                    form.ReadOnlySample.ReadOnly, form.ReadOnlySample.RightToLeft);
                using var font = new Font(FontFamily.GenericSansSerif, 13);
                form.ReadOnlySample.Font = font;
                manager.SetMode(ThemeMode.Dark);
                await Sample("Font and text updated");
                customPalette = true;
                manager.Refresh();
                await Sample("Custom disabled-text palette");
                customPalette = false;
                manager.Refresh();
                typeof(Control).GetMethod("RecreateHandle", Hidden)!.Invoke(form.ReadOnlySample, null);
                handles.Remove(form.ReadOnlySample);
                await Sample("Recreated edit handle");
                using (var dynamicInput = new TextBox
                {
                    Text = "Dynamic disabled input", Enabled = false, BackColor = SystemColors.ActiveCaption,
                    Multiline = true, Size = new Size(250, 70)
                })
                {
                    form.Pages.TabPages[0].Controls.Add(dynamicInput);
                    await Task.Delay(80);
                    using var image = Capture(dynamicInput);
                    log.Check(CountColor(image, ExpectedForeground(manager.Palette, dynamicInput.BackColor)) > 4,
                        "Dynamic disabled input was not rendered");
                    form.Pages.TabPages[1].Controls.Add(dynamicInput);
                    form.Pages.SelectedIndex = 1;
                    await Task.Delay(80);
                    using var moved = Capture(dynamicInput);
                    log.Check(CountColor(moved, ExpectedForeground(manager.Palette, dynamicInput.BackColor)) > 4,
                        "Reparenting lost disabled text rendering");
                }
                form.Pages.SelectedIndex = 0;
                using (var ownerPaint = new OwnerPaintTextBox())
                {
                    form.Pages.TabPages[0].Controls.Add(ownerPaint);
                    await Task.Delay(80);
                    using var image = Capture(ownerPaint);
                    log.Check(RendererFor(ownerPaint, manager) is null && CountColor(image, Color.Gold) > 4,
                        "Custom owner drawing was recolored by the disabled edit renderer");
                }
                using (var earlyHandle = new TextBox { Text = "Early native handle", Enabled = false })
                {
                    earlyHandle.HandleCreated += (_, _) => manager.Refresh();
                    form.Pages.TabPages[0].Controls.Add(earlyHandle);
                    _ = earlyHandle.Handle;
                    await Task.Delay(80);
                    log.Check(RendererFor(earlyHandle, manager) is NativeWindow window && window.Handle == earlyHandle.Handle,
                        "Handle-created refresh lost the native subclass");
                }
                using (var disposedDuringRefresh = new TextBox { Enabled = false })
                {
                    _ = disposedDuringRefresh.Handle;
                    disposedDuringRefresh.BackColorChanged += (_, _) => disposedDuringRefresh.Dispose();
                    form.Pages.TabPages[0].Controls.Add(disposedDuringRefresh);
                    await Task.Delay(80);
                    log.Check(disposedDuringRefresh.IsDisposed && RendererFor(disposedDuringRefresh, manager) is null,
                        "Disposal during theme refresh retained a native subclass");
                }
                nint parent = GetParent(form.ReadOnlySample.Handle);
                NativeWindow? originalWindow = NativeWindow.FromHandle(parent);
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                int resources = GetGuiResources(process.Handle, 0);
                for (int index = 0; index < 40; index++)
                {
                    using var image = Capture(form.ReadOnlySample);
                }
                log.Check(GetGuiResources(process.Handle, 0) <= resources + 2, "Disabled painting leaked GDI resources");
                log.Check(ReferenceEquals(NativeWindow.FromHandle(parent), originalWindow), "Paint scope left the parent subclassed");
                registration.Dispose();
                foreach (TextBoxBase sample in form.Samples)
                {
                    log.Check(!sample.Enabled && RendererFor(sample, manager) is null, "Detach retained disabled renderer/state");
                }
                using (manager.Attach(form))
                {
                    await Sample("Reattached");
                }
            }
            catch (Exception exception)
            {
                log.Check(false, exception.ToString());
            }
            finally
            {
                form.Close();
            }
        };
        Application.Run(form);
        return log.Finish();
    }

    private static void CheckNativePolicy(TextBoxBase sample, ThemeManager manager, Bitmap image, CheckLog log, string stage)
    {
        object? renderer = RendererFor(sample, manager);
        if (renderer is null)
        {
            log.Check(false, stage + ": missing disabled edit renderer");
            return;
        }
        MethodInfo refresh = renderer.GetType().GetMethod("Refresh", Hidden)!;
        var contrast = manager.Context with { IsDark = false, IsHighContrast = true };
        refresh.Invoke(renderer, [contrast, manager.Palette]);
        try
        {
            using var native = Capture(sample);
            if (!manager.Context.IsDark || sample.Enabled)
            {
                log.Check(EqualPixels(image, native), stage + ": native Light/enabled rendering changed for " + sample.Name);
            }
            else
            {
                Rectangle before = TextBounds(native, sample);
                Rectangle after = TextBounds(image, sample);
                log.Check(Math.Abs(before.Left - after.Left) <= 1 && Math.Abs(before.Top - after.Top) <= 1 &&
                    Math.Abs(before.Right - after.Right) <= 1 && Math.Abs(before.Bottom - after.Bottom) <= 1,
                    stage + ": native text placement changed for " + sample.Name + $" ({before} -> {after})");
            }
        }
        finally
        {
            refresh.Invoke(renderer, [manager.Context, manager.Palette]);
        }
    }

    private static object? RendererFor(Control sample, ThemeManager manager)
    {
        var states = (System.Collections.IDictionary)typeof(ThemeManager).GetField("controls", Hidden)!.GetValue(manager)!;
        object? state = states[sample];
        return state?.GetType().GetField("disabledTextBox", Hidden)!.GetValue(state);
    }

    private static Bitmap Capture(Control control)
    {
        var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        return image;
    }

    private static int CountColor(Bitmap image, Color color)
    {
        int count = 0;
        int expected = color.ToArgb();
        int[] data = ReadPixels(image);
        for (int y = 4; y < image.Height - 4; y++)
        {
            for (int x = 4; x < image.Width - 4; x++)
            {
                if (data[y * image.Width + x] == expected)
                {
                    count++;
                    if (count >= 5)
                    {
                        return count;
                    }
                }
            }
        }
        return count;
    }

    private static bool EqualPixels(Bitmap first, Bitmap second)
    {
        return ReadPixels(first).AsSpan().SequenceEqual(ReadPixels(second));
    }

    private static int[] ReadPixels(Bitmap image)
    {
        var pixels = new int[image.Width * image.Height];
        BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * image.Width, image.Width);
            }
        }
        finally
        {
            image.UnlockBits(data);
        }
        return pixels;
    }

    private static Rectangle TextBounds(Bitmap image, TextBoxBase input)
    {
        int left = image.Width, top = image.Height, right = 0, bottom = 0;
        int width = Math.Min(image.Width - 5, input.ClientSize.Width - 3);
        int height = Math.Min(image.Height - 5, input.ClientSize.Height - 3);
        Color background = input.BackColor;
        int[] data = ReadPixels(image);
        for (int y = 5; y < height; y++)
        {
            for (int x = 5; x < width; x++)
            {
                Color pixel = Color.FromArgb(data[y * image.Width + x]);
                int difference = Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) + Math.Abs(pixel.B - background.B);
                if (difference > 12)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x + 1);
                    bottom = Math.Max(bottom, y + 1);
                }
            }
        }
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static Color ExpectedForeground(ThemePalette palette, Color background)
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
        double light = Luminance(first);
        double other = Luminance(second);
        return (Math.Max(light, other) + 0.05) / (Math.Min(light, other) + 0.05);
    }

    private static double Luminance(Color color)
    {
        double Linear(byte channel)
        {
            double value = channel / 255.0;
            if (value <= 0.04045)
            {
                return value / 12.92;
            }
            return Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private sealed class OwnerPaintTextBox : TextBox
    {
        internal OwnerPaintTextBox()
        {
            Enabled = false;
            Multiline = true;
            Size = new Size(160, 70);
            SetStyle(ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Navy);
            using var brush = new SolidBrush(Color.Gold);
            e.Graphics.FillRectangle(brush, 8, 8, 80, 30);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);
    [DllImport("user32.dll")]
    private static extern int GetGuiResources(nint process, int flags);
}
