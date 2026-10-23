namespace HenLib.WinFormsTheming.Verification;

internal sealed class SpinnerFixtureForm : Form
{
    internal IReadOnlyList<UpDownBase> Samples
    {
        get;
    }
    internal TabControl Pages
    {
        get;
    } = new() { Dock = DockStyle.Fill };
    internal Panel DisabledParent
    {
        get;
    } = new() { Enabled = false, Dock = DockStyle.Fill };
    internal Panel SamplePanel
    {
        get;
    }

    internal SpinnerFixtureForm(ThemeManager manager)
    {
        Text = "Spinner controls | started " + manager.RequestedMode;
        ClientSize = new Size(820, 680);
        var modes = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
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
        var visible = new TabPage("Samples");
        var hidden = new TabPage("Initially hidden");
        Pages.TabPages.Add(visible);
        Pages.TabPages.Add(hidden);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        SamplePanel = new Panel { Dock = DockStyle.Fill };
        SamplePanel.Controls.Add(layout);
        visible.Controls.Add(SamplePanel);
        var samples = new List<UpDownBase>();
        void Add(string caption, UpDownBase sample, Control? host = null)
        {
            sample.Name = caption;
            sample.Width = 250;
            sample.Location = new Point(8, 26);
            samples.Add(sample);
            var holder = new Panel { Dock = DockStyle.Fill, Height = 70 };
            var label = new Label { Text = caption, AutoSize = true, Location = new Point(8, 4) };
            holder.Controls.Add(label);
            if (host is not null)
            {
                host.Dock = DockStyle.None;
                host.Location = new Point(0, 24);
                host.Size = new Size(300, 40);
                sample.Location = new Point(8, 2);
            }
            holder.Controls.Add(host ?? sample);
            label.BringToFront();
            layout.Controls.Add(holder);
        }
        Add("Numeric / editable", Numeric());
        Add("Domain / editable", Domain());
        var numericReadOnly = Numeric();
        numericReadOnly.ReadOnly = true;
        Add("Numeric / read-only", numericReadOnly);
        var domainReadOnly = Domain();
        domainReadOnly.ReadOnly = true;
        Add("Domain / read-only", domainReadOnly);
        var numericDisabled = Numeric();
        numericDisabled.Enabled = false;
        Add("Numeric / disabled", numericDisabled);
        var domainDisabled = Domain();
        domainDisabled.Enabled = false;
        Add("Domain / disabled", domainDisabled);
        var numericFixed = Numeric();
        numericFixed.BackColor = Color.LightPink;
        numericFixed.ForeColor = Color.DarkBlue;
        Add("Numeric / fixed colors", numericFixed);
        var domainFixed = Domain();
        domainFixed.BackColor = Color.LightPink;
        domainFixed.ForeColor = Color.DarkBlue;
        Add("Domain / fixed colors", domainFixed);
        var numericSystem = Numeric();
        numericSystem.BackColor = SystemColors.ActiveCaption;
        Add("Numeric / system background", numericSystem);
        var domainSystem = Domain();
        domainSystem.BackColor = SystemColors.ActiveCaption;
        Add("Domain / system background", domainSystem);
        var numericRtl = Numeric();
        numericRtl.RightToLeft = RightToLeft.Yes;
        numericRtl.UpDownAlign = LeftRightAlignment.Right;
        numericRtl.TextAlign = HorizontalAlignment.Right;
        Add("Numeric / RTL and left buttons", numericRtl);
        var domainRtl = Domain();
        domainRtl.RightToLeft = RightToLeft.Yes;
        domainRtl.UpDownAlign = LeftRightAlignment.Left;
        domainRtl.TextAlign = HorizontalAlignment.Right;
        Add("Domain / RTL and reversed alignment", domainRtl);
        var inherited = Numeric();
        DisabledParent.Controls.Add(inherited);
        Add("Numeric / disabled parent", inherited, DisabledParent);
        var borderless = Domain();
        borderless.BorderStyle = BorderStyle.None;
        Add("Domain / borderless", borderless);
        var hiddenNumeric = Numeric();
        hiddenNumeric.Name = "Initially hidden numeric";
        hiddenNumeric.Location = new Point(15, 20);
        hidden.Controls.Add(hiddenNumeric);
        var hiddenDomain = Domain();
        hiddenDomain.Name = "Initially hidden domain";
        hiddenDomain.Location = new Point(15, 65);
        hiddenDomain.Enabled = false;
        hidden.Controls.Add(hiddenDomain);
        samples.Add(hiddenNumeric);
        samples.Add(hiddenDomain);
        Samples = samples;
        var toggles = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 };
        var enable = new Button { Text = "Toggle disabled pair", AutoSize = true };
        enable.Click += (_, _) =>
        {
            numericDisabled.Enabled = !numericDisabled.Enabled;
            domainDisabled.Enabled = numericDisabled.Enabled;
        };
        var visibility = new Button { Text = "Show / hide samples", AutoSize = true };
        visibility.Click += (_, _) => SamplePanel.Visible = !SamplePanel.Visible;
        var hideForm = new Button { Text = "Hide form…", AutoSize = true };
        hideForm.Click += (_, _) => ShowRestoreWindow(manager);
        toggles.Controls.AddRange([enable, visibility, hideForm]);
        Controls.Add(Pages);
        Controls.Add(modes);
        Controls.Add(toggles);
    }

    internal static NumericUpDown Numeric()
    {
        return new NumericUpDown
        {
            Minimum = -100, Maximum = 1000, DecimalPlaces = 2, Increment = 0.25m,
            Value = 37.25m, ThousandsSeparator = true
        };
    }

    internal static DomainUpDown Domain()
    {
        return new DomainUpDown { Items = { "Alpha", "Beta", "Gamma" }, SelectedIndex = 1, Wrap = true };
    }

    private void ShowRestoreWindow(ThemeManager manager)
    {
        using var restore = new Form { Text = "Restore spinner fixture", ClientSize = new Size(320, 110) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            var change = new Button { Text = mode.ToString(), AutoSize = true };
            change.Click += (_, _) => manager.SetMode(mode);
            buttons.Controls.Add(change);
        }
        var show = new Button { Text = "Show fixture", AutoSize = true };
        show.Click += (_, _) => restore.Close();
        buttons.Controls.Add(show);
        restore.Controls.Add(buttons);
        using var registration = manager.Register(restore);
        Hide();
        try
        {
            restore.ShowDialog();
        }
        finally
        {
            if (!IsDisposed)
            {
                Show();
            }
        }
    }
}