using System.Reflection;
using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.Verification;

internal static class StartupChecks
{
    [STAThread]
    internal static int Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ThemeMode mode = args.Contains("--dark") ? ThemeMode.Dark : ThemeMode.Light;
        using var manager = new ThemeManager(mode);
        using var form = new Form { Text = "Passive progress startup check", Width = 540, Height = 140 };
        var bar = new ProgressBar { Value = 65, Width = 480, Height = 24, Location = new Point(15, 15) };
        form.Controls.Add(bar);
        using var registration = manager.Attach(form);
        int paints = 0;
        bool initiallyEnabled = false, finallyEnabled = false;
        bool motion = VerificationSupport.MotionAllowed(manager);
        bool passed = true;
        string? error = null;
        PaintObserver? observer = null;
        bool TimerEnabled()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object controller = typeof(ThemeManager).GetField("progressBars", flags)!.GetValue(manager)!;
            return ((System.Windows.Forms.Timer)controller.GetType().GetField("timer", flags)!.GetValue(controller)!).Enabled;
        }
        form.Shown += async (_, _) =>
        {
            try
            {
                observer = new PaintObserver(bar.Handle);
                initiallyEnabled = TimerEnabled();
                // No input, mode changes, value changes, snapshots, or invalidation.
                await Task.Delay(1200);
                paints = observer.Paints;
                finallyEnabled = TimerEnabled();
                passed &= finallyEnabled == motion && (!motion || paints > 5);

                async Task Observe(string stage, Action action, bool shouldAnimate)
                {
                    int before = observer.Paints;
                    action();
                    await Task.Delay(shouldAnimate ? 900 : 250);
                    int count = observer.Paints - before;
                    bool enabled = TimerEnabled();
                    shouldAnimate &= motion;
                    passed &= enabled == shouldAnimate && (!shouldAnimate || count > 5);

                }
                await Observe("Form hidden", () => form.Hide(), false);
                await Observe("Form shown again", () => form.Show(), true);
                await Observe("Form minimized", () => form.WindowState = FormWindowState.Minimized, false);
                await Observe("Form restored", () => form.WindowState = FormWindowState.Normal, true);
                await Observe("Control hidden", () => bar.Hide(), false);
                await Observe("Control shown again", () => bar.Show(), true);
            }
            catch (Exception ex)
            {
                error = ex.ToString();
            }
            finally
            {
                observer?.Dispose();
                form.Close();
            }
        };
        Application.Run(form);
        Console.WriteLine($"{mode}: timer initially={initiallyEnabled}, after={finallyEnabled}, passive paints={paints}");
        Console.WriteLine(error ?? (passed ? "Passive startup/lifecycle passed." : "Passive startup/lifecycle failed."));
        return error is null && passed ? 0 : 1;
    }

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
