namespace HenLib.WinFormsTheming.Verification;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ThemeMode startup = args.Contains("--dark") ? ThemeMode.Dark : ThemeMode.Light;
        bool native = args.Contains("--native");
        bool noMotion = args.Contains("--no-motion");
        using var manager = new ThemeManager(startup, new ThemeOptions
        {
            AnimateProgressBars = !noMotion,
            ProgressBarRendering = native ? ThemeProgressBarRendering.Native : ThemeProgressBarRendering.Animated
        });
        Form form;
        if (args.Contains("--setup"))
        {
            form = new SetupFixtureForm(manager);
        }
        else if (args.Contains("--disabled"))
        {
            form = new DisabledTextFixtureForm(manager);
        }
        else if (args.Contains("--colors"))
        {
            form = new SystemColorFixtureForm(manager);
        }
        else if (args.Contains("--gallery"))
        {
            form = new CommonControlsForm(manager);
        }
        else
        {
            form = new ProgressFixtureForm(manager, startup, noMotion, native);
        }
        using var formLifetime = form;
        using var registration = form is SetupFixtureForm ? null : manager.Register(form);
        Application.Run(form);
    }
}
