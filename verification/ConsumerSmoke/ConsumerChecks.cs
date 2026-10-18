namespace HenLib.WinFormsTheming.ConsumerSmoke;

internal sealed class ConsumerChecks(ThemeMode startup)
{
    private int count;
    private readonly List<string> failures = new();
    internal void Check(bool condition, string message)
    {
        count++;
        if (!condition)
        {
            failures.Add(message);
        }
    }
    internal int Finish()
    {
        Console.WriteLine($"Consumer {startup}: {count} checks, 20 switches; {failures.Count} failures.");
        foreach (string failure in failures)
        {
            Console.Error.WriteLine(failure);
        }
        return failures.Count == 0 ? 0 : 1;
    }
}

internal sealed class SampleAdapter : IThemeAdapter
{
    private Color original;
    private bool captured;
    internal int Applies, Detaches;
    public bool CanHandle(Control control) => control.Name == "AdapterSubject";
    public void Apply(Control control, ThemeContext context, ThemePalette palette)
    {
        if (!captured)
        {
            original = control.BackColor;
            captured = true;
        }
        control.BackColor = palette.ErrorBackground;
        Applies++;
    }
    public void Detach(Control control)
    {
        control.BackColor = original;
        Detaches++;
    }
}

internal sealed class ThrowingAdapter : IThemeAdapter
{
    public bool CanHandle(Control control) => true;
    public void Apply(Control control, ThemeContext context, ThemePalette palette)
    {
    }
    public void Detach(Control control) => throw new InvalidOperationException("Synthetic cleanup failure");
}