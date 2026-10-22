using System.Runtime.InteropServices;

namespace HenLib.WinFormsTheming.Verification;

internal sealed class DisabledTextFixtureForm : Form
{
    internal IReadOnlyList<TextBoxBase> Samples
    {
        get;
    }
    internal TextBox ReadOnlySample
    {
        get;
    }
    internal TextBox InheritedSample
    {
        get;
    }
    internal Panel DisabledParent
    {
        get;
    }
    internal TextBox ScrollSample
    {
        get;
    }
    internal TextBox HiddenSample
    {
        get;
    }
    internal TabControl Pages
    {
        get;
    } = new() { Dock = DockStyle.Fill };

    internal DisabledTextFixtureForm(ThemeManager manager)
    {
        Text = "Disabled native text | started " + manager.RequestedMode;
        ClientSize = new Size(980, 740);
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
        var page = new TabPage("Samples");
        var hidden = new TabPage("Initially hidden");
        Pages.TabPages.Add(page);
        Pages.TabPages.Add(hidden);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
        for (int index = 0; index < 3; index++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
        }
        var samples = new List<TextBoxBase>();
        void Add(string caption, TextBoxBase input, Control? host = null)
        {
            input.Name = caption;
            samples.Add(input);
            var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            holder.Controls.Add(host ?? input);
            holder.Controls.Add(new Label { Text = caption, Height = 24, Dock = DockStyle.Top });
            layout.Controls.Add(holder);
        }
        ReadOnlySample = Input("Read-only native text", SystemColors.ActiveCaption);
        ReadOnlySample.ReadOnly = true;
        Add("Read-only / system background", ReadOnlySample);
        Add("Disabled editable", Input("Editable native text", SystemColors.ControlLight));
        var bright = Input("Fixed bright background", Color.LightPink);
        bright.ForeColor = Color.DarkBlue;
        Add("Fixed bright background", bright);
        var masked = new MaskedTextBox("000-000")
        {
            Text = "123456", Enabled = false, Dock = DockStyle.Fill, AutoSize = false,
            BackColor = SystemColors.ControlLight
        };
        Add("Masked input", masked);
        var password = Input("Sample password", SystemColors.ControlLight);
        password.Multiline = false;
        password.AutoSize = false;
        password.UseSystemPasswordChar = true;
        Add("Password remains masked", password);
        var rtl = Input("نص تجريبي ABC 123", SystemColors.ActiveCaption);
        rtl.RightToLeft = RightToLeft.Yes;
        rtl.TextAlign = HorizontalAlignment.Right;
        Add("Right-to-left / right aligned", rtl);
        Add("Native gray background", Input("Text must still be visible", ColorTranslator.FromWin32(GetSysColor(17))));
        DisabledParent = new Panel { Dock = DockStyle.Fill, Enabled = false };
        InheritedSample = Input("Disabled through parent", SystemColors.ActiveCaption);
        InheritedSample.Enabled = true;
        DisabledParent.Controls.Add(InheritedSample);
        Add("Disabled parent", InheritedSample, DisabledParent);
        ScrollSample = Input(string.Join("\r\n", Enumerable.Range(0, 40).Select(index =>
            $"Line {index:00}: scroll this long line horizontally and vertically 0123456789")), SystemColors.ControlLight);
        ScrollSample.WordWrap = false;
        ScrollSample.ScrollBars = ScrollBars.Both;
        Add("Native scrolling", ScrollSample);
        HiddenSample = Input("Initially hidden disabled text", SystemColors.ActiveCaption);
        HiddenSample.Name = "Hidden disabled input";
        hidden.Controls.Add(HiddenSample);
        samples.Add(HiddenSample);
        page.Controls.Add(layout);
        Samples = samples;
        var toggle = new Button { Text = "Enable / disable samples", AutoSize = true };
        toggle.Click += (_, _) =>
        {
            bool enable = !ReadOnlySample.Enabled;
            foreach (TextBoxBase sample in Samples)
            {
                if (!ReferenceEquals(sample, InheritedSample))
                {
                    sample.Enabled = enable;
                }
            }
            DisabledParent.Enabled = enable;
        };
        modes.Controls.Add(toggle);
        Controls.Add(Pages);
        Controls.Add(modes);
    }

    private static TextBox Input(string text, Color background)
    {
        return new TextBox
        {
            Text = text, BackColor = background, Enabled = false, Multiline = true,
            Dock = DockStyle.Fill
        };
    }

    [DllImport("user32.dll")]
    private static extern int GetSysColor(int index);
}
