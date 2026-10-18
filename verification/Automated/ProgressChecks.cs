using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.Verification;

internal static class ProgressChecks
{
    private static readonly List<string> failures = new();
    private static int checks;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    internal static int Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ThemeMode startup = args.Contains("--dark") ? ThemeMode.Dark : ThemeMode.Light;
        bool noMotion = args.Contains("--no-motion");
        bool native = args.Contains("--native");
        using var manager = new ThemeManager(startup, new ThemeOptions
        {
            AnimateProgressBars = !noMotion,
            ProgressBarRendering = native ? ThemeProgressBarRendering.Native : ThemeProgressBarRendering.Animated
        });
        manager.Diagnostic += (_, e) => failures.Add("Native diagnostic: " + e.Operation + ":" + e.ErrorCode);
        using var form = new Form { Text = "Local progress animation verification", Width = 570, Height = 350 };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        ProgressBar Bar(string name, int value = 65) => new()
        {
            Name = name,
            Width = 500,
            Height = 22,
            Value = value
        };
        var normal = Bar("Determinate");
        var marquee = Bar("Marquee");
        marquee.Style = ProgressBarStyle.Marquee;
        marquee.MarqueeAnimationSpeed = 30;
        var rtl = Bar("RTL", 40);
        rtl.RightToLeft = RightToLeft.Yes;
        rtl.RightToLeftLayout = true;
        var custom = Bar("Designer colors", 55);
        custom.ForeColor = Color.Magenta;
        custom.BackColor = Color.LightPink;
        var disabled = Bar("Disabled", 55);
        disabled.Enabled = false;
        var small = Bar("Compact", 50);
        small.Size = new Size(96, 12);
        layout.Controls.AddRange([normal, marquee, rtl, custom, disabled, small]);
        form.Controls.Add(layout);
        using var registration = manager.Attach(form);
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(100);
                if (native)
                {
                    Check(typeof(ThemeManager).GetField("progressBars", Private)!.GetValue(manager) is null, "Native option still creates animator");
                    Check(normal.Value == 65 && marquee.Style == ProgressBarStyle.Marquee && marquee.MarqueeAnimationSpeed == 30, "Native option changed semantics");
                    return;
                }
                object controller = typeof(ThemeManager).GetField("progressBars", Private)!.GetValue(manager)!;
                object Window(ProgressBar bar)
                {
                    var map = (System.Collections.IDictionary)controller.GetType().GetField("bars", Private)!.GetValue(controller)!;
                    return map[bar]!;
                }
                bool Needs(ProgressBar bar) => (bool)Window(bar).GetType().GetProperty("NeedsAnimation", Private)!.GetValue(Window(bar))!;
                bool TimerEnabled() => ((System.Windows.Forms.Timer)controller.GetType().GetField("timer", Private)!.GetValue(controller)!).Enabled;
                bool motion = (bool)controller.GetType().GetProperty("MotionEnabled", Private)!.GetValue(controller)!;
                Check(motion == (!noMotion && VerificationSupport.MotionAllowed(manager)), "Unexpected animation policy (Windows motion preference may be disabled)");
                using var observer = new PaintObserver(normal.Handle);
                var handles = layout.Controls.Cast<Control>().ToDictionary(c => c, c => c.Handle);
                for (int round = 0; round < 3; round++)
                {
                    foreach (ThemeMode mode in new[] { startup, startup == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light })
                    {
                        manager.SetMode(mode);
                        await Task.Delay(80);
                        Check(normal.ForeColor == manager.Palette[ThemeColorRole.ProgressFill], "Default fill did not change with mode");
                        Check(custom.ForeColor == Color.Magenta && custom.BackColor == Color.LightPink, "Designer progress colors were lost");
                        Check(handles.All(p => p.Key.Handle == p.Value), "A theme switch recreated a progress handle");
                        Check(normal.Value == 65 && normal.Minimum == 0 && normal.Maximum == 100 && marquee.MarqueeAnimationSpeed == 30,
                            "Theme switch changed progress state");
                        using var image = Capture(normal);
                        Check(image.GetPixel(0, 0).ToArgb() == manager.Palette[ThemeColorRole.ProgressBorder].ToArgb(), "Progress border is missing or wrong");
                        Check(image.GetPixel(image.Width - 3, image.Height / 2).ToArgb() == normal.BackColor.ToArgb(), "Unfilled track has wrong color");
                        using (var smallImage = Capture(small))
                        {
                            Check(smallImage.GetPixel(0, 0).ToArgb() == manager.Palette[ThemeColorRole.ProgressBorder].ToArgb(), "Compact progress has no border");
                            Check(smallImage.GetPixel(90, 6).ToArgb() == small.BackColor.ToArgb(), "Compact progress track is wrong");
                        }
                        var normalFrames = new HashSet<string>();
                        var marqueeFrames = new HashSet<string>();
                        for (int frame = 0; frame < 12; frame++)
                        {
                            await Task.Delay(200);
                            using var a = Capture(normal);
                            using var b = Capture(marquee);
                            normalFrames.Add(Hash(a));
                            marqueeFrames.Add(Hash(b));
                        }
                        Check(normalFrames.Count > 1 == motion, "Determinate shimmer motion is wrong in " + mode);
                        Check(marqueeFrames.Count > 1 == motion, "Marquee motion is wrong in " + mode);
                    }
                }
                Check(!motion || observer.Paints > 5, "Timer did not drive real WM_PAINT messages");
                using (var unattached = Bar("Unparented", 50))
                using (manager.Attach(unattached))
                {
                    _ = unattached.Handle;
                    Check(!Needs(unattached), "An unparented hidden HWND needs animation");
                }
                Check(normal.AccessibilityObject.Role == AccessibleRole.ProgressBar && normal.AccessibilityObject.Value is not null,
                    "Native progress accessibility was lost");
                var previousContext = manager.Context;
                var previousPalette = manager.Palette;
                var contrast = new ThemeContext(manager.RequestedMode, false, true);
                typeof(ThemeManager).GetProperty("Context")!.SetValue(manager, contrast);
                typeof(ThemeManager).GetProperty("Palette")!.SetValue(manager, ThemePalette.Create(contrast));
                controller.GetType().GetMethod("RefreshPolicy", Private)!.Invoke(controller, null);
                using (var image = Capture(custom))
                {
                    Check(image.GetPixel(0, 0).ToArgb() == SystemColors.WindowText.ToArgb(), "Contrast border is wrong");
                    Check(image.GetPixel(50, 10).ToArgb() == SystemColors.Highlight.ToArgb(), "Contrast did not override designer fill");
                    Check(image.GetPixel(490, 10).ToArgb() == SystemColors.Window.ToArgb(), "Contrast did not override designer track");
                }
                Check(!TimerEnabled(), "Contrast policy did not stop animation");
                typeof(ThemeManager).GetProperty("Context")!.SetValue(manager, previousContext);
                typeof(ThemeManager).GetProperty("Palette")!.SetValue(manager, previousPalette);
                controller.GetType().GetMethod("RefreshPolicy", Private)!.Invoke(controller, null);

                normal.Minimum = 50;
                normal.Maximum = 150;
                normal.Value = 100;
                normal.Step = 7;
                normal.PerformStep();
                Check(normal.Value == 107 && normal.Step == 7, "PerformStep semantics changed");
                normal.Value = 100;
                using (var image = Capture(normal))
                {
                    Check(image.GetPixel(350, 10).ToArgb() == normal.BackColor.ToArgb(), "Nonzero minimum range fill is wrong");
                    Check(image.GetPixel(100, 10).ToArgb() != normal.BackColor.ToArgb(), "Nonzero minimum range is empty");
                }
                normal.Minimum = normal.Maximum = normal.Value = 100;
                using (var image = Capture(normal))
                    Check(image.GetPixel(100, 10).ToArgb() == normal.BackColor.ToArgb(), "Zero-length range is not empty");
                normal.Minimum = 0;
                normal.Maximum = int.MaxValue;
                normal.Value = int.MaxValue / 2;
                using (var image = Capture(normal))
                    Check(image.GetPixel(350, 10).ToArgb() == normal.BackColor.ToArgb(), "Large range overflowed");
                normal.Minimum = 0;
                normal.Maximum = 100;
                normal.Value = 65;
                SendMessage(normal.Handle, 0x402, 25, 0);
                using (var image = Capture(normal))
                {
                    Check(image.GetPixel(100, 10).ToArgb() != normal.BackColor.ToArgb() && image.GetPixel(300, 10).ToArgb() == normal.BackColor.ToArgb(),
                        "Direct native position does not match painted progress");
                }
                Check(normal.Value == 65, "Native position changed the managed Value property");
                SendMessage(normal.Handle, 0x406, 10, 110);
                SendMessage(normal.Handle, 0x402, 60, 0);
                SendMessage(normal.Handle, 0x409, 0, ColorTranslator.ToWin32(Color.Orange));
                using (var image = Capture(normal))
                    Check(image.GetPixel(100, 10).R > 200, "Direct native fill color was ignored");
                SendMessage(normal.Handle, 0x2001, 0, ColorTranslator.ToWin32(Color.Pink));
                using (var image = Capture(normal))
                    Check(image.GetPixel(490, 10).ToArgb() == Color.Pink.ToArgb(), "Direct native track color was ignored");
                manager.Refresh();
                using (var image = Capture(normal))
                    Check(image.GetPixel(490, 10).ToArgb() == normal.BackColor.ToArgb(), "Theme refresh did not restore property track color");
                normal.Maximum = 101;
                normal.Maximum = 100;
                normal.Value = 66;
                normal.Value = 65;

                using (var image = Capture(rtl))
                {
                    Check(image.GetPixel(10, 10).ToArgb() == rtl.BackColor.ToArgb() && image.GetPixel(480, 10).ToArgb() != rtl.BackColor.ToArgb(), "RTL fill direction is wrong");
                }
                foreach (ProgressBar bar in layout.Controls)
                    bar.Visible = false;
                await Task.Delay(70);
                Check(!TimerEnabled(), "Hidden bars keep timer running");
                small.Size = new Size(1, 1);
                small.Visible = true;
                Check(!TimerEnabled(), "A border-only progress bar keeps the timer running");
                small.Visible = false;
                normal.Visible = true;
                Check(TimerEnabled() == motion, "Showing an incomplete bar did not resume animation");
                normal.Value = normal.Maximum;
                Check(!TimerEnabled(), "A completed bar keeps timer running");
                normal.Value = 30;
                normal.Enabled = false;
                Check(!TimerEnabled(), "A disabled bar keeps timer running");
                normal.Enabled = true;
                SendMessage(normal.Handle, 0x410, 2, 0);
                Check(!TimerEnabled() && !Needs(normal), "Error progress still shimmers");
                SendMessage(normal.Handle, 0x410, 3, 0);
                Check(!TimerEnabled() && !Needs(normal), "Paused progress still shimmers");
                SendMessage(normal.Handle, 0x410, 1, 0);
                Check(TimerEnabled() == motion, "Normal state did not resume shimmer");
                normal.Visible = false;
                marquee.Visible = true;
                marquee.MarqueeAnimationSpeed = 0;
                Check(!TimerEnabled(), "Stopped marquee keeps timer running");
                using (var image = Capture(marquee))
                    Check(image.GetPixel(250, 10).ToArgb() != marquee.BackColor.ToArgb(), "Static marquee segment is missing");
                marquee.MarqueeAnimationSpeed = 20;
                Check(TimerEnabled() == motion, "Marquee restart did not resume motion");
                nint oldHandle = marquee.Handle;
                marquee.Style = ProgressBarStyle.Continuous;
                marquee.Value = 40;
                Check(marquee.Handle != oldHandle && Needs(marquee) == motion, "Recreated progress handle was not registered");
                marquee.Style = ProgressBarStyle.Marquee;
                marquee.MarqueeAnimationSpeed = 30;
                Check(Needs(marquee) == motion, "Marquee was not restored after style recreation");
                form.WindowState = FormWindowState.Minimized;
                await Task.Delay(80);
                Check(!TimerEnabled(), "Minimized form keeps animation timer running");
                form.WindowState = FormWindowState.Normal;
                await Task.Delay(80);
                Check(TimerEnabled() == motion, "Restored form did not resume animation");

                var dynamicBar = Bar("Dynamic", 50);
                layout.Controls.Add(dynamicBar);
                _ = dynamicBar.Handle;
                Check(Needs(dynamicBar) == motion, "Dynamic progress was not attached");
                layout.Controls.Remove(dynamicBar);
                dynamicBar.Dispose();
                registration.Dispose();
                Check(!TimerEnabled(), "Detach did not stop the timer");
                Check(normal.Value == 30 && normal.Style == ProgressBarStyle.Blocks && marquee.MarqueeAnimationSpeed == 30,
                    "Detach changed managed semantics");
                using (manager.Attach(form))
                    Check(Needs(marquee) == motion, "Reattachment failed");
                manager.Dispose();
                Check(ThemeManager.Current is null, "Manager still retained after disposal");
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                Console.WriteLine($"{startup}: checks {checks}; failures {failures.Count}");
                foreach (string failure in failures)
                    Console.WriteLine(failure);
                form.Close();
            }
        };
        Application.Run(form);
        return failures.Count == 0 ? 0 : 1;
    }

    private static Bitmap Capture(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }
    private static string Hash(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
        {
            failures.Add(message);
        }
    }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int msg, nint wParam, nint lParam);
    private sealed class PaintObserver : NativeWindow, IDisposable
    {
        internal int Paints
        {
            get; private set;
        }
        internal PaintObserver(nint handle) => AssignHandle(handle);
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0xF)
            {
                Paints++;
            }

            base.WndProc(ref message);
        }
        public void Dispose() => ReleaseHandle();
    }
}
