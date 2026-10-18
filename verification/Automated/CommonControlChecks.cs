namespace HenLib.WinFormsTheming.Verification;

internal static class CommonControlChecks
{
    internal static int Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ThemeMode startup = args.Contains("--dark") ? ThemeMode.Dark : ThemeMode.Light;
        using var manager = new ThemeManager(startup);
        using var form = new CommonControlsForm(manager);
        using var registration = manager.Attach(form);
        var log = new CheckLog("Native controls " + startup);
        Control[] samples = form.Samples.Where(control => control is TrackBar or ScrollBar or ListBox).ToArray();
        var originals = samples.ToDictionary(control => control, control => control.Handle);
        var backgrounds = new Dictionary<string, int>();
        async Task Sample(string stage)
        {
            await Task.Delay(180);
            foreach (Control control in samples)
            {
                using var image = new Bitmap(control.Width, control.Height);
                control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                log.Check(control.Handle == originals[control], stage + ": native handle changed for " + control.Name);
                if (control is TrackBar slider)
                {
                    log.Check(image.GetPixel(3, image.Height - 3).ToArgb() == slider.BackColor.ToArgb(), stage + ": stale slider background");
                    log.Check(slider.Value == 37 && slider.Maximum == 100, stage + ": slider state changed");
                }
                else
                {
                    // Compare to the same-theme startup sample, avoiding assumptions
                    // about Windows accent colors or the installed visual style.
                    int pixel = image.GetPixel(image.Width - 7, image.Height / 2).ToArgb();
                    string key = control.Name + ":" + manager.Context.IsDark;
                    if (backgrounds.TryGetValue(key, out int expected))
                    {
                        log.Check(pixel == expected, stage + ": stale native scrollbar for " + control.Name);
                    }
                    else
                    {
                        backgrounds[key] = pixel;
                    }
                    if (!manager.Context.IsHighContrast && backgrounds.TryGetValue(control.Name + ":" + !manager.Context.IsDark, out int otherMode))
                    {
                        log.Check(pixel != otherMode, stage + ": native scrollbar retained the other theme for " + control.Name);
                    }
                }
            }
        }
        form.Shown += async (_, _) =>
        {
            try
            {
                await Sample("Startup");
                foreach (ThemeMode mode in new[] { startup == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light, startup,
                    startup == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light, startup })
                {
                    manager.SetMode(mode);
                    await Sample("Switched " + mode);
                }
                form.Hide();
                await Task.Delay(100);
                form.Show();
                await Sample("Reshown");
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
}
