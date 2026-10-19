using System.Drawing;

namespace HenLib.WinFormsTheming;

internal sealed class ThemedToolTipRegistration : IDisposable
{
    private readonly ThemeManager manager;
    private readonly bool originalOwnerDraw;
    private readonly Color originalBack, originalFore;
    private bool disposed, tooltipDisposed;
    internal ToolTip ToolTip
    {
        get;
    }

    internal ThemedToolTipRegistration(ThemeManager manager, ToolTip toolTip)
    {
        this.manager = manager;
        ToolTip = toolTip;
        originalOwnerDraw = toolTip.OwnerDraw;
        originalBack = toolTip.BackColor;
        originalFore = toolTip.ForeColor;
        // Respect existing application-owned tooltip renderers.
        if (!originalOwnerDraw)
        {
            toolTip.Draw += Draw;
            toolTip.Popup += Popup;
        }
        toolTip.Disposed += ToolTipDisposed;
        RefreshColors();
    }

    internal void RefreshColors()
    {
        if (disposed || tooltipDisposed || originalOwnerDraw)
        {
            return;
        }

        bool dark = manager.Context.IsDark && !manager.Context.IsHighContrast;
        ToolTip.OwnerDraw = dark;
        if (dark)
        {
            ToolTip.BackColor = manager.Palette.Surface;
            ToolTip.ForeColor = manager.Palette.Text;
        }
        else if (manager.Context.IsHighContrast)
        {
            ToolTip.BackColor = SystemColors.Info;
            ToolTip.ForeColor = SystemColors.InfoText;
        }
        else
        {
            ToolTip.BackColor = originalBack;
            ToolTip.ForeColor = originalFore;
        }
    }

    private void Popup(object? sender, PopupEventArgs e)
    {
        if (!ToolTip.OwnerDraw || !manager.Context.IsDark || manager.Context.IsHighContrast)
        {
            return;
        }

        string text = e.AssociatedControl is { } control ? ToolTip.GetToolTip(control) ?? string.Empty : string.Empty;
        e.ToolTipSize = Measure(text, e.AssociatedControl?.DeviceDpi ?? 96).Size;
    }

    private void Draw(object? sender, DrawToolTipEventArgs e)
    {
        if (!ToolTip.OwnerDraw || !manager.Context.IsDark || manager.Context.IsHighContrast)
        {
            return;
        }

        using var back = new SolidBrush(manager.Palette.Surface);
        using var border = new Pen(manager.Palette.Border);
        e.Graphics.FillRectangle(back, e.Bounds);
        e.Graphics.DrawRectangle(border, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
        var layout = Measure(e.ToolTipText ?? string.Empty, e.AssociatedControl?.DeviceDpi ?? 96);
        Rectangle Offset(Rectangle bounds) => new(bounds.X + e.Bounds.X, bounds.Y + e.Bounds.Y, bounds.Width, bounds.Height);
        Font font = SystemFonts.MessageBoxFont!;
        using var titleFont = new Font(font, font.Style | FontStyle.Bold);
        Icon? icon = GetIcon();
        if (icon is not null)
        {
            e.Graphics.DrawIcon(icon, Offset(layout.Icon));
        }
        if (!string.IsNullOrEmpty(ToolTip.ToolTipTitle))
        {
            TextRenderer.DrawText(e.Graphics, ToolTip.ToolTipTitle, titleFont, Offset(layout.Title), manager.Palette.Text,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
        TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, Offset(layout.Body), manager.Palette.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    private ToolTipLayout Measure(string text, int dpi)
    {
        int padding = (int)Math.Ceiling(8 * dpi / 96.0);
        int iconSize = GetIcon() is null ? 0 : (int)Math.Ceiling(16 * dpi / 96.0);
        int iconSpace = iconSize == 0 ? 0 : iconSize + padding;
        Font font = SystemFonts.MessageBoxFont!;
        using var titleFont = new Font(font, font.Style | FontStyle.Bold);
        const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        Size body = TextRenderer.MeasureText(text, font, new Size(padding * 50, 0), flags);
        Size title = string.IsNullOrEmpty(ToolTip.ToolTipTitle) ? Size.Empty
            : TextRenderer.MeasureText(ToolTip.ToolTipTitle, titleFont, new Size(padding * 50 - iconSpace, 0), flags);
        int headerHeight = Math.Max(iconSize, title.Height);
        int bodyTop = padding + (headerHeight == 0 ? 0 : headerHeight + padding);
        int width = Math.Max(body.Width, title.Width + iconSpace) + padding * 2;
        int height = bodyTop + body.Height + padding;
        var size = new Size(width, height);
        var iconBounds = new Rectangle(padding, padding, iconSize, iconSize);
        var titleBounds = new Rectangle(padding + iconSpace, padding, title.Width, title.Height);
        var bodyBounds = new Rectangle(padding, bodyTop, body.Width, body.Height);
        return new ToolTipLayout(size, iconBounds, titleBounds, bodyBounds);
    }

    private Icon? GetIcon() => ToolTip.ToolTipIcon switch
    {
        ToolTipIcon.Info => SystemIcons.Information,
        ToolTipIcon.Warning => SystemIcons.Warning,
        ToolTipIcon.Error => SystemIcons.Error,
        _ => null
    };

    private sealed record ToolTipLayout(Size Size, Rectangle Icon, Rectangle Title, Rectangle Body);

    private void ToolTipDisposed(object? sender, EventArgs e)
    {
        tooltipDisposed = true;
        Dispose();
        manager.ForgetToolTip(this);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ToolTip.Draw -= Draw;
        ToolTip.Popup -= Popup;
        ToolTip.Disposed -= ToolTipDisposed;
        if (!tooltipDisposed)
        {
            ToolTip.OwnerDraw = originalOwnerDraw;
            ToolTip.BackColor = originalBack;
            ToolTip.ForeColor = originalFore;
        }
    }
}
