using System.ComponentModel;
using System.Drawing;

namespace HenLib.WinFormsTheming;

internal sealed class ControlThemeState(Control control, ThemeControlDefaults? captured)
{
    internal Control Control { get; } = control;
    internal ThemeControlDefaults Defaults { get; } = captured ?? new ThemeControlDefaults(control);
    internal bool ExplicitBack => Defaults.HasExplicitBackColor;
    internal bool ExplicitFore => Defaults.HasExplicitForeColor;
    private bool originalTabBackground => Defaults.UseVisualStyleBackColor == true;
    private FlatStyle? originalButtonStyle => Defaults.ButtonStyle;
    private bool wasHighContrast;
    private bool usingNativeButtonStyle;
    private ThemedDisabledTextBoxWindow? disabledTextBox;
    private Color refreshedSystemBackground;
    private int refreshedSystemBackgroundArgb;
    private nint refreshedSystemBackgroundHandle;
    internal bool? LastNativeDark
    {
        get; set;
    }
    internal nint LastNativeHandle
    {
        get; set;
    }

    internal void Apply(ThemeContext context, ThemePalette palette, ThemeOptions options,
        ThemeColorRole? background, ThemeColorRole? foreground)
    {
        bool dark = context.IsDark && !context.IsHighContrast;
        // UpDownBase owns the colors exposed by its internal edit and buttons.
        // Applying textbox defaults again would overwrite spinner bindings/colors.
        if (IsFrameworkSpinnerChild(Control))
        {
            if (!context.IsHighContrast)
            {
                if (background is ThemeColorRole childBack)
                {
                    Control.BackColor = palette[childBack];
                }
                if (foreground is ThemeColorRole childFore)
                {
                    Control.ForeColor = palette[childFore];
                }
            }
            RefreshSystemBackground();
            return;
        }
        if (wasHighContrast && !context.IsHighContrast)
        {
            RestoreColors();
        }

        wasHighContrast = context.IsHighContrast;
        if (Control is Button button && originalButtonStyle == FlatStyle.Standard &&
            options.ButtonRendering == ThemeButtonRendering.NativeTextButtons)
        {
            bool eligible = button.Image is null && button.BackgroundImage is null && button.ImageList is null;
            if (eligible)
            {
                button.FlatStyle = FlatStyle.System;
                usingNativeButtonStyle = true;
            }
            else if (usingNativeButtonStyle)
            {
                button.FlatStyle = originalButtonStyle.Value;
                usingNativeButtonStyle = false;
            }
        }

        if (context.IsHighContrast)
        {
            Control.BackColor = Control is TextBoxBase or UpDownBase or ComboBox or ListView or TreeView
                ? SystemColors.Window : SystemColors.Control;
            Control.ForeColor = Control is TextBoxBase or UpDownBase or ComboBox or ListView or TreeView
                ? SystemColors.WindowText : SystemColors.ControlText;
        }
        else if (background is ThemeColorRole backRole)
        {
            Control.BackColor = palette[backRole];
        }
        else if (!ExplicitBack)
        {
            switch (Control)
            {
                case TextBoxBase text:
                    if (dark || (text.ReadOnly ? palette.Surface != SystemColors.Control : palette.Input != SystemColors.Window))
                    {
                        text.BackColor = text.ReadOnly ? palette.Surface : palette.Input;
                    }
                    else
                    {
                        text.ResetBackColor();
                    }

                    break;
                case UpDownBase:
                    if (dark || palette.Input != SystemColors.Window)
                    {
                        Control.BackColor = palette.Input;
                    }
                    else
                    {
                        Control.ResetBackColor();
                    }

                    break;
                case ComboBox:
                    if (dark || palette.Input != SystemColors.Window)
                    {
                        Control.BackColor = palette.Input;
                    }
                    else
                    {
                        Control.ResetBackColor();
                    }

                    break;
                case ListView or TreeView:
                    if (dark)
                    {
                        Control.BackColor = palette.Window;
                    }
                    else
                    {
                        Control.ResetBackColor();
                    }

                    break;
                case ProgressBar:
                    if (options.ProgressBarRendering == ThemeProgressBarRendering.Animated)
                    {
                        Control.BackColor = palette[ThemeColorRole.ProgressTrack];
                    }
                    else if (dark)
                    {
                        Control.BackColor = palette.Surface;
                    }
                    else
                    {
                        Control.ResetBackColor();
                    }

                    break;
                case Form or TabPage or ToolStrip:
                    if (dark || palette.Window != SystemColors.Control)
                    {
                        // SystemColors changes its ARGB value with the application mode,
                        // but remains the same Color. Pin Dark to a value so returning
                        // to Light raises BackColorChanged and refreshes inherited brushes.
                        Control.BackColor = Color.FromArgb(palette.Window.ToArgb());
                    }
                    else
                    {
                        Control.ResetBackColor();
                    }

                    break;
                    // Other containers and labels keep inherited colors, including intentional parent accents.
            }
        }
        if (!context.IsHighContrast)
        {
            if (foreground is ThemeColorRole foreRole)
            {
                Control.ForeColor = palette[foreRole];
            }
            else if (!ExplicitFore && Control is ProgressBar)
            {
                Control.ForeColor = options.ProgressBarRendering == ThemeProgressBarRendering.Animated
                    ? palette[ThemeColorRole.ProgressFill] : palette.Accent;
            }
            else if (!ExplicitFore && Control is not RichTextBox)
            {
                if (palette.Text != SystemColors.ControlText)
                {
                    Control.ForeColor = palette.Text;
                }
                else
                {
                    Control.ResetForeColor();
                }
            }
        }
        RefreshSystemBackground();
        // BackColor setters can themselves clear this flag, so apply the saved policy last.
        if (Control is TabPage page)
        {
            page.UseVisualStyleBackColor = dark ? false : originalTabBackground;
        }
    }

    private void RefreshSystemBackground()
    {
        Color background = Control.BackColor;
        if (!background.IsSystemColor || !Control.IsHandleCreated)
        {
            return;
        }
        int argb = background.ToArgb();
        nint handle = Control.Handle;
        if (background == refreshedSystemBackground && argb == refreshedSystemBackgroundArgb &&
            handle == refreshedSystemBackgroundHandle)
        {
            return;
        }
        var property = TypeDescriptor.GetProperties(Control)[nameof(Control.BackColor)]!;
        if (!property.ShouldSerializeValue(Control))
        {
            return;
        }

        // System colors retain their identity when their RGB value changes. Passing
        // through RGB clears WinForms' cached native brush; restore the symbolic color
        // so later mode changes and application assignments keep their normal meaning.
        refreshedSystemBackground = background;
        refreshedSystemBackgroundArgb = argb;
        refreshedSystemBackgroundHandle = handle;
        Color resolved = Color.FromArgb(argb);
        Control.BackColor = resolved;
        if (!Control.IsDisposed && !Control.Disposing && Control.BackColor == resolved)
        {
            Control.BackColor = background;
        }
    }

    internal void RefreshDisabledText(ThemeContext context, ThemePalette palette)
    {
        if (Control.IsDisposed || Control.Disposing)
        {
            return;
        }
        Type type = Control.GetType();
        bool spinnerEdit = Control is TextBox && IsFrameworkSpinnerChild(Control);
        if (type == typeof(TextBox) || type == typeof(MaskedTextBox) || spinnerEdit)
        {
            disabledTextBox ??= new ThemedDisabledTextBoxWindow((TextBoxBase)Control);
            disabledTextBox.Refresh(context, palette);
        }
    }

    internal static bool IsFrameworkSpinnerChild(Control control)
    {
        Type type = control.GetType();
        Type? parent = control.Parent?.GetType();
        bool stockParent = parent == typeof(NumericUpDown) || parent == typeof(DomainUpDown);
        bool frameworkChild = type.Assembly == typeof(UpDownBase).Assembly && type.DeclaringType == typeof(UpDownBase);
        return stockParent && frameworkChild;
    }

    internal void DisposeDisabledText()
    {
        disabledTextBox?.Dispose();
        disabledTextBox = null;
    }

    internal void Restore()
    {
        if (Control.IsDisposed || Control.Disposing)
        {
            return;
        }

        RestoreColors();
        if (Control is TabPage page)
        {
            page.UseVisualStyleBackColor = originalTabBackground;
        }

        if (Control is ButtonBase button && originalButtonStyle is FlatStyle style)
        {
            button.FlatStyle = style;
        }
    }

    internal void RestoreColors()
    {
        RestoreBackground();
        RestoreForeground();
    }

    internal void RestoreBackground() => Defaults.RestoreBackground(Control);

    internal void RestoreForeground() => Defaults.RestoreForeground(Control);
}
