using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.Verification;

internal sealed class SystemColorFixtureForm : Form
{
    internal TextBox ReadOnlyInput
    {
        get;
    } = CreateInput("Read-only system background", SystemColors.ActiveCaption, true);
    internal TextBox EditableInput
    {
        get;
    } = CreateInput("Editable system background", SystemColors.ControlLight, false);
    internal TextBox DisabledInput
    {
        get;
    } = CreateInput("Disabled system background", SystemColors.ActiveCaption, true);
    internal TextBox FixedInput
    {
        get;
    } = CreateInput("Fixed RGB background", Color.LightPink, true);
    internal TextBox RuntimeInput
    {
        get;
    } = CreateInput("Application changes this color", SystemColors.ActiveCaption, true);
    internal TextBox HiddenInput
    {
        get;
    } = CreateInput("Initially hidden system background", SystemColors.ActiveCaption, true);
    internal RichTextBox MixedInput
    {
        get;
    } = new()
    {
        Text = "Formatted red text; formatted blue text",
        BackColor = SystemColors.ControlLight,
        ForeColor = Color.Black,
        ReadOnly = true,
        Dock = DockStyle.Fill
    };
    internal Panel Accent
    {
        get;
    } = new() { BackColor = SystemColors.ActiveCaption, Dock = DockStyle.Fill };
    internal Label Inherited
    {
        get;
    } = new() { Text = "Inherited system background", AutoSize = true };
    internal TabControl Pages
    {
        get;
    } = new() { Dock = DockStyle.Fill };
    internal IReadOnlyList<TextBoxBase> Inputs
    {
        get;
    }

    internal SystemColorFixtureForm(ThemeManager manager)
    {
        Text = "System-color backgrounds — compare startup and runtime switching";
        ClientSize = new Size(920, 620);
        StartPosition = FormStartPosition.CenterScreen;
        var modes = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System })
        {
            string caption = mode.ToString();
            if (mode == ThemeMode.System)
            {
                caption = "System Mode";
            }
            var button = new Button { Text = caption, AutoSize = true };
            button.Click += (_, _) => manager.SetMode(mode);
            modes.Controls.Add(button);
        }
        var colors = new TabPage("Colors");
        var hidden = new TabPage("Initially hidden");
        Pages.TabPages.Add(colors);
        Pages.TabPages.Add(hidden);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int row = 0; row < 4; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        }
        void Add(string caption, Control sample)
        {
            var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };
            holder.Controls.Add(sample);
            holder.Controls.Add(new Label { Text = caption, Dock = DockStyle.Top, Height = 24 });
            layout.Controls.Add(holder);
        }
        DisabledInput.Enabled = false;
        FixedInput.ForeColor = Color.DarkBlue;
        ReadOnlyInput.TabStop = false;
        Accent.Controls.Add(Inherited);
        Add("Read-only / ActiveCaption", ReadOnlyInput);
        Add("Editable / ControlLight", EditableInput);
        Add("Disabled (state must stay disabled)", DisabledInput);
        Add("Fixed colors must stay unchanged", FixedInput);
        Add("Runtime color assignments", RuntimeInput);
        Add("Mixed rich-text formatting", MixedInput);
        Add("Parent with an inherited child", Accent);
        colors.Controls.Add(layout);
        hidden.Controls.Add(HiddenInput);
        MixedInput.Select(0, 19);
        MixedInput.SelectionColor = Color.Red;
        MixedInput.Select(20, MixedInput.TextLength - 20);
        MixedInput.SelectionColor = Color.Blue;
        MixedInput.Select(0, 0);
        Inputs = new TextBoxBase[] { ReadOnlyInput, EditableInput, DisabledInput, FixedInput, RuntimeInput, HiddenInput, MixedInput };
        Controls.Add(Pages);
        Controls.Add(modes);
    }

    private static TextBox CreateInput(string text, Color background, bool readOnly)
    {
        return new TextBox
        {
            Text = text,
            BackColor = background,
            Multiline = true,
            ReadOnly = readOnly,
            Dock = DockStyle.Fill
        };
    }
}
