namespace HenLib.WinFormsTheming;

/// <summary>The saved preference; System is resolved again when Windows changes.</summary>
public enum ThemeMode
{
    System, Light, Dark
}

/// <summary>Policy for ordinary Standard text buttons without images.</summary>
public enum ThemeButtonRendering
{
    NativeTextButtons, PreserveConfiguredStyle
}

/// <summary>Managed painting keeps progress appearance and animation consistent across modes.</summary>
public enum ThemeProgressBarRendering
{
    Animated, Native
}

/// <summary>Colors applications can bind to business states without embedding theme logic.</summary>
public enum ThemeColorRole
{
    Window, Surface, Input, Text, SecondaryText, DisabledText, Border, Accent,
    Selection, SelectionText, WarningBackground, WarningText, ErrorBackground,
    ErrorText, PausedText, ValidationBackground, ValidationText,
    ProgressTrack, ProgressFill, ProgressBorder, ProgressErrorFill, ProgressPausedFill
}

public readonly record struct ThemeContext(ThemeMode RequestedMode, bool IsDark, bool IsHighContrast);

public sealed class ThemeOptions
{
    public ThemeButtonRendering ButtonRendering { get; init; } = ThemeButtonRendering.PreserveConfiguredStyle;
    public ThemeProgressBarRendering ProgressBarRendering { get; init; } = ThemeProgressBarRendering.Animated;
    public bool AnimateProgressBars { get; init; } = true;
    public Func<Control, bool>? ExcludeControl
    {
        get; init;
    }
    public Func<ThemeContext, ThemePalette>? PaletteFactory
    {
        get; init;
    }
}

/// <summary>Adapter implementations must restore any properties they change in Detach.</summary>
public interface IThemeAdapter
{
    bool CanHandle(Control control);
    void Apply(Control control, ThemeContext context, ThemePalette palette);
    void Detach(Control control);
}

public sealed class ThemeChangedEventArgs(ThemeContext context, ThemePalette palette, long revision) : EventArgs
{
    public ThemeContext Context { get; } = context;
    public ThemePalette Palette { get; } = palette;
    public long Revision { get; } = revision;
}

public sealed class ThemeDiagnosticEventArgs(string operation, int errorCode) : EventArgs
{
    public string Operation { get; } = operation;
    public int ErrorCode { get; } = errorCode;
}
