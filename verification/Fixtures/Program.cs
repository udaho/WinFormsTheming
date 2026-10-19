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
        using Form form = args.Contains("--setup")
            ? new SetupFixtureForm(manager)
            : args.Contains("--gallery")
            ? new CommonControlsForm(manager)
            : new ProgressFixtureForm(manager, startup, noMotion, native);
        using var registration = form is SetupFixtureForm ? null : manager.Register(form);
        Application.Run(form);
    }
}
