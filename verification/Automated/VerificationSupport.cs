using System.Runtime.InteropServices;

namespace HenLib.WinFormsTheming.Verification;

internal static class VerificationSupport
{
    internal static bool MotionAllowed(ThemeManager manager) => !manager.Context.IsHighContrast &&
        SystemParametersInfo(0x1042, 0, out int animations, 0) && animations != 0;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
}

internal sealed class CheckLog(string name)
{
    private int count;
    private readonly List<string> failures = new();

    internal void Check(bool value, string message)
    {
        count++;
        if (!value)
        {
            failures.Add(message);
        }
    }

    internal int Finish()
    {
        Console.WriteLine($"{name}: {count} checks; {failures.Count} failures.");
        foreach (string failure in failures)
        {
            Console.Error.WriteLine(failure);
        }
        return failures.Count == 0 ? 0 : 1;
    }
}
