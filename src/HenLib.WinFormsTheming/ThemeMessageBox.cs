using System.Drawing;

namespace HenLib.WinFormsTheming;

/// <summary>Message-box-compatible common overloads, using a registered managed dialog in Dark.</summary>
public static class ThemeMessageBox
{
    public static DialogResult Show(string text, string caption = "", MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        => Show(null, text, caption, buttons, icon, defaultButton);

    public static DialogResult Show(IWin32Window? owner, string text, string caption = "",
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        ThemeManager? theme = ThemeManager.Current;
        if (theme is null || !theme.Context.IsDark || theme.Context.IsHighContrast)
        {
            return MessageBox.Show(owner, text, caption, buttons, icon, defaultButton);
        }

        var choices = GetButtons(buttons);
        if (!Enum.IsDefined(defaultButton))
        {
            throw new ArgumentOutOfRangeException(nameof(defaultButton));
        }

        int defaultIndex = Math.Clamp((int)defaultButton / 256, 0, choices.Length - 1);
        using var dialog = new Form
        {
            Text = caption,
            Font = SystemFonts.MessageBoxFont,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(360, 140),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            StartPosition = owner is null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
            ControlBox = choices.Any(c => c.Result == DialogResult.Cancel) || buttons == MessageBoxButtons.OK
        };
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 2
        };
        var message = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(12, 0, 0, 18),
            UseMnemonic = false
        };
        Icon? messageIcon = icon switch
        {
            MessageBoxIcon.Error => SystemIcons.Error,
            MessageBoxIcon.Warning => SystemIcons.Warning,
            MessageBoxIcon.Information => SystemIcons.Information,
            MessageBoxIcon.Question => SystemIcons.Question,
            _ => null
        };
        using Bitmap? image = messageIcon?.ToBitmap();
        if (image is not null)
        {
            layout.Controls.Add(new PictureBox { Image = image, SizeMode = PictureBoxSizeMode.AutoSize, Margin = Padding.Empty }, 0, 0);
        }

        layout.Controls.Add(message, 1, 0);
        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = Padding.Empty
        };
        var actionButtons = choices.Select(choice => new Button
        {
            Text = choice.Text,
            DialogResult = choice.Result,
            AutoSize = true,
            MinimumSize = new Size(82, 27),
            Margin = new Padding(8, 0, 0, 0),
            UseVisualStyleBackColor = true
        }).ToArray();
        foreach (Button button in actionButtons.Reverse())
            actions.Controls.Add(button);
        layout.Controls.Add(actions, 0, 1);
        layout.SetColumnSpan(actions, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = actionButtons[defaultIndex];
        dialog.CancelButton = actionButtons.FirstOrDefault(b => b.DialogResult == DialogResult.Cancel)
            ?? (buttons == MessageBoxButtons.OK ? actionButtons[0] : null);
        dialog.Shown += (_, _) => actionButtons[defaultIndex].Focus();
        dialog.FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing &&
                dialog.DialogResult is DialogResult.None or DialogResult.Cancel)
            {
                if (buttons == MessageBoxButtons.OK)
                {
                    dialog.DialogResult = DialogResult.OK;
                }
                else if (!choices.Any(c => c.Result == DialogResult.Cancel))
                {
                    e.Cancel = true;
                    dialog.DialogResult = DialogResult.None;
                }
                else
                {
                    dialog.DialogResult = DialogResult.Cancel;
                }
            }
        };
        using var registration = theme.Attach(dialog);
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private static (string Text, DialogResult Result)[] GetButtons(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.OK => [("OK", DialogResult.OK)],
        MessageBoxButtons.OKCancel => [("OK", DialogResult.OK), ("Cancel", DialogResult.Cancel)],
        MessageBoxButtons.YesNo => [("&Yes", DialogResult.Yes), ("&No", DialogResult.No)],
        MessageBoxButtons.YesNoCancel => [("&Yes", DialogResult.Yes), ("&No", DialogResult.No), ("Cancel", DialogResult.Cancel)],
        MessageBoxButtons.RetryCancel => [("&Retry", DialogResult.Retry), ("Cancel", DialogResult.Cancel)],
        MessageBoxButtons.AbortRetryIgnore => [("&Abort", DialogResult.Abort), ("&Retry", DialogResult.Retry), ("&Ignore", DialogResult.Ignore)],
        MessageBoxButtons.CancelTryContinue => [("Cancel", DialogResult.Cancel), ("&Try Again", DialogResult.TryAgain), ("&Continue", DialogResult.Continue)],
        _ => throw new ArgumentOutOfRangeException(nameof(buttons))
    };
}
