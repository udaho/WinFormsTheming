namespace HenLib.WinFormsTheming.Verification;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "isolated")
        {
            return IsolatedDesktopRunner.Run(args.Skip(1).ToArray());
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        if (SystemInformation.HighContrast)
        {
            Console.Error.WriteLine("Start automated checks in normal contrast; use the fixtures for Windows contrast-theme acceptance.");
            return 2;
        }
        try
        {
            return args.FirstOrDefault() switch
            {
                "progress" => ProgressChecks.Run(args),
                "visibility" => VisibilityChecks.Run(args),
                "startup" => StartupChecks.Run(args),
                "setup" => SetupChecks.Run(args),
                "lifecycle" => LifecycleChecks.Run(),
                "controls" => CommonControlChecks.Run(args),
                "colors" => SystemColorChecks.Run(args),
                "disabled" => DisabledTextChecks.Run(args),
                "spinners" => SpinnerChecks.Run(args),
                _ => Usage()
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("Scenarios: progress | visibility | startup | lifecycle | controls | setup | colors | disabled | spinners");
        Console.WriteLine("Options: --dark; progress also accepts --no-motion or --native.");
        return 2;
    }
}
