using System.Reflection;
using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.Verification;

internal static class VisibilityChecks
{
    internal static int Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ThemeMode startup = args.Contains("--dark") ? ThemeMode.Dark : ThemeMode.Light;
        using var manager = new ThemeManager(startup);
        using var form = new ProgressFixtureForm(manager, startup);
        using var registration = manager.Attach(form);
        var log = new CheckLog("Visibility " + startup);
        void Check(bool value, string message) => log.Check(value, message);
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        var value = form.ValueSlider;
        var bars = form.Bars;
        var visibilityTargets = form.VisibilityTargets;
        var toggleControls = form.ToggleControls;
        var hideForm = form.HideFixture;
        var showForm = form.ShowFixture;
        var restoreForm = form.RestoreWindow;
        bool motion = VerificationSupport.MotionAllowed(manager);
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(250);
                nint sliderHandle = value.Handle;
                int sliderValue = value.Value;
                int[] values = bars.Select(bar => bar.Value).ToArray();
                bool TimerEnabled()
                {
                    object controller = typeof(ThemeManager).GetField("progressBars", Private)!.GetValue(manager)!;
                    return ((System.Windows.Forms.Timer)controller.GetType().GetField("timer", Private)!.GetValue(controller)!).Enabled;
                }
                Check(TimerEnabled() == motion, "Interactive startup did not animate without input");
                foreach (ThemeMode mode in new[] { startup, startup == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light, startup })
                {
                    toggleControls.PerformClick();
                    await Task.Delay(100);
                    Check(visibilityTargets.All(control => !control.Visible), "Hide controls left sample controls visible");
                    Check(!TimerEnabled(), "Hidden fixture controls kept animation running");
                    manager.SetMode(mode);
                    toggleControls.PerformClick();
                    await Task.Delay(180);
                    Check(visibilityTargets.All(control => control.Visible), "Show controls did not restore samples");
                    Check(TimerEnabled() == motion, "Showing fixture controls did not resume motion");
                    hideForm.PerformClick();
                    await Task.Delay(100);
                    Check(!form.Visible && restoreForm.Visible, "Hide form did not leave a visible restore popup");
                    Check(!TimerEnabled(), "Hidden form kept animation running");
                    manager.SetMode(mode == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light);
                    showForm.PerformClick();
                    await Task.Delay(180);
                    Check(form.Visible && !restoreForm.Visible, "Restore popup did not show the original form");
                    Check(visibilityTargets.All(control => control.Visible), "Restoring form lost sample visibility");
                    Check(TimerEnabled() == motion, "Restoring fixture form did not resume motion");
                    Check(value.Handle == sliderHandle && value.Value == sliderValue, "Visibility or switching replaced/reset slider");
                    Check(bars.Select(bar => bar.Value).SequenceEqual(values), "Visibility or switching reset progress values");
                    using var sliderImage = Capture(value);
                    Check(sliderImage.GetPixel(3, value.Height - 3).ToArgb() == value.BackColor.ToArgb(), "Slider background is stale after restoring");
                }
                toggleControls.PerformClick();
                hideForm.PerformClick();
                await Task.Delay(100);
                restoreForm.Close();
                await Task.Delay(100);
                Check(form.Visible && !restoreForm.Visible && !restoreForm.IsDisposed, "Closing restore popup did not safely restore fixture");
                Check(visibilityTargets.All(control => !control.Visible), "Restoring form changed the controls' hidden state");
                toggleControls.PerformClick();
                await Task.Delay(180);
                Check(TimerEnabled() == motion, "Second controls restore did not resume motion");
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

    private static Bitmap Capture(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }
}
