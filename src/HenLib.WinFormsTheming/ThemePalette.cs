using System.Drawing;

namespace HenLib.WinFormsTheming;

/// <summary>Immutable palette. A custom factory can use a record with-expression to override roles.</summary>
public sealed record ThemePalette
{
    public required Color Window
    {
        get; init;
    }
    public required Color Surface
    {
        get; init;
    }
    public required Color Input
    {
        get; init;
    }
    public required Color Text
    {
        get; init;
    }
    public required Color SecondaryText
    {
        get; init;
    }
    public required Color DisabledText
    {
        get; init;
    }
    public required Color Border
    {
        get; init;
    }
    public required Color Accent
    {
        get; init;
    }
    public required Color Selection
    {
        get; init;
    }
    public required Color SelectionText
    {
        get; init;
    }
    public required Color WarningBackground
    {
        get; init;
    }
    public required Color WarningText
    {
        get; init;
    }
    public required Color ErrorBackground
    {
        get; init;
    }
    public required Color ErrorText
    {
        get; init;
    }
    public required Color PausedText
    {
        get; init;
    }
    public required Color ValidationBackground
    {
        get; init;
    }
    public required Color ValidationText
    {
        get; init;
    }
    public Color ProgressTrack { get; init; } = Color.Empty;
    public Color ProgressFill { get; init; } = Color.Empty;
    public Color ProgressBorder { get; init; } = Color.Empty;
    public Color ProgressErrorFill { get; init; } = Color.Empty;
    public Color ProgressPausedFill { get; init; } = Color.Empty;

    public Color this[ThemeColorRole role] => role switch
    {
        ThemeColorRole.Window => Window,
        ThemeColorRole.Surface => Surface,
        ThemeColorRole.Input => Input,
        ThemeColorRole.Text => Text,
        ThemeColorRole.SecondaryText => SecondaryText,
        ThemeColorRole.DisabledText => DisabledText,
        ThemeColorRole.Border => Border,
        ThemeColorRole.Accent => Accent,
        ThemeColorRole.Selection => Selection,
        ThemeColorRole.SelectionText => SelectionText,
        ThemeColorRole.WarningBackground => WarningBackground,
        ThemeColorRole.WarningText => WarningText,
        ThemeColorRole.ErrorBackground => ErrorBackground,
        ThemeColorRole.ErrorText => ErrorText,
        ThemeColorRole.PausedText => PausedText,
        ThemeColorRole.ValidationBackground => ValidationBackground,
        ThemeColorRole.ValidationText => ValidationText,
        ThemeColorRole.ProgressTrack => ProgressTrack.IsEmpty ? Surface : ProgressTrack,
        ThemeColorRole.ProgressFill => ProgressFill.IsEmpty ? Accent : ProgressFill,
        ThemeColorRole.ProgressBorder => ProgressBorder.IsEmpty ? Border : ProgressBorder,
        ThemeColorRole.ProgressErrorFill => ProgressErrorFill.IsEmpty ? ErrorBackground : ProgressErrorFill,
        ThemeColorRole.ProgressPausedFill => ProgressPausedFill.IsEmpty ? WarningBackground : ProgressPausedFill,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static ThemePalette Create(ThemeContext context)
    {
        if (context.IsHighContrast)
        {
            return new()
            {
                Window = SystemColors.Control,
                Surface = SystemColors.Window,
                Input = SystemColors.Window,
                Text = SystemColors.WindowText,
                SecondaryText = SystemColors.ControlText,
                DisabledText = SystemColors.GrayText,
                Border = SystemColors.WindowText,
                Accent = SystemColors.Highlight,
                Selection = SystemColors.Highlight,
                SelectionText = SystemColors.HighlightText,
                WarningBackground = SystemColors.Window,
                WarningText = SystemColors.WindowText,
                ErrorBackground = SystemColors.Window,
                ErrorText = SystemColors.WindowText,
                PausedText = SystemColors.WindowText,
                ValidationBackground = SystemColors.Window,
                ValidationText = SystemColors.WindowText,
                ProgressTrack = SystemColors.Window,
                ProgressFill = SystemColors.Highlight,
                ProgressBorder = SystemColors.WindowText,
                ProgressErrorFill = SystemColors.Highlight,
                ProgressPausedFill = SystemColors.Highlight
            };
        }
        bool dark = context.IsDark;
        return new()
        {
            Window = SystemColors.Control,
            Surface = dark ? SystemColors.ControlLight : SystemColors.Control,
            Input = dark ? SystemColors.ControlLight : SystemColors.Window,
            Text = SystemColors.ControlText,
            SecondaryText = dark ? Color.FromArgb(195, 195, 195) : Color.FromArgb(80, 80, 80),
            DisabledText = dark ? Color.FromArgb(165, 165, 165) : SystemColors.GrayText,
            Border = dark ? Color.FromArgb(100, 100, 100) : SystemColors.ControlDark,
            Accent = SystemColors.Highlight,
            Selection = SystemColors.Highlight,
            SelectionText = SystemColors.HighlightText,
            WarningBackground = dark ? Color.FromArgb(75, 58, 20) : Color.FromArgb(255, 243, 205),
            WarningText = dark ? Color.FromArgb(255, 221, 140) : Color.FromArgb(105, 69, 0),
            ErrorBackground = dark ? Color.FromArgb(92, 31, 36) : Color.Maroon,
            ErrorText = Color.White,
            PausedText = dark ? Color.FromArgb(255, 157, 204) : Color.MediumVioletRed,
            ValidationBackground = dark ? Color.FromArgb(92, 31, 36) : Color.MistyRose,
            ValidationText = dark ? Color.FromArgb(255, 200, 200) : Color.DarkRed,
            ProgressTrack = dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(230, 230, 230),
            ProgressFill = dark ? SystemColors.Highlight : Color.Green,
            ProgressBorder = dark ? Color.FromArgb(115, 115, 115) : Color.FromArgb(160, 160, 160),
            ProgressErrorFill = Color.FromArgb(210, 50, 45),
            ProgressPausedFill = Color.FromArgb(215, 160, 15)
        };
    }
}
