using System.ComponentModel;
using System.Drawing;

namespace HenLib.WinFormsTheming;

/// <summary>A control's captured property values, before theme application.</summary>
public sealed class ThemeControlDefaults
{
    internal ThemeControlDefaults(Control control)
    {
        BackColor = control.BackColor;
        ForeColor = control.ForeColor;
        HasExplicitBackColor = TypeDescriptor.GetProperties(control)[nameof(Control.BackColor)]!.ShouldSerializeValue(control);
        HasExplicitForeColor = TypeDescriptor.GetProperties(control)[nameof(Control.ForeColor)]!.ShouldSerializeValue(control);
        ButtonStyle = (control as ButtonBase)?.FlatStyle;
        UseVisualStyleBackColor = (control as TabPage)?.UseVisualStyleBackColor;
    }

    public Color BackColor { get; }
    public Color ForeColor { get; }
    public bool HasExplicitBackColor { get; }
    public bool HasExplicitForeColor { get; }
    public FlatStyle? ButtonStyle { get; }
    public bool? UseVisualStyleBackColor { get; }

    /// <summary>Restore on the control's UI thread, preserving inherited color behavior.</summary>
    public void RestoreBackground(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.IsDisposed || control.Disposing)
        {
            return;
        }
        if (HasExplicitBackColor)
        {
            control.BackColor = BackColor;
        }
        else
        {
            control.ResetBackColor();
        }
    }

    /// <summary>Restore on the control's UI thread, preserving inherited color behavior.</summary>
    public void RestoreForeground(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.IsDisposed || control.Disposing)
        {
            return;
        }
        if (HasExplicitForeColor)
        {
            control.ForeColor = ForeColor;
        }
        else
        {
            control.ResetForeColor();
        }
    }
}
