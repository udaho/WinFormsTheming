using HenLib.WinFormsTheming;
using System.Runtime.InteropServices;

namespace HenLib.WinFormsTheming.Verification;

internal sealed class ProgressFixtureForm : Form
{
    private readonly ThemeManager manager;
    private readonly Form restoreForm;
    private readonly IDisposable restoreRegistration;
    private readonly EventHandler<ThemeChangedEventArgs> titleChanged;
    internal ProgressBar[] Bars
    {
        get;
    }
    internal Button ToggleControls
    {
        get;
    }
    internal Button HideFixture
    {
        get;
    }
    internal Button ShowFixture
    {
        get;
    }
    internal TrackBar ValueSlider
    {
        get;
    }
    internal Control[] VisibilityTargets
    {
        get;
    }
    internal Form RestoreWindow => restoreForm;

    internal ProgressFixtureForm(ThemeManager manager, ThemeMode startup, bool noMotion = false, bool native = false)
    {
        this.manager = manager;
        Form form = this;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        ProgressBar Bar(string name, int value = 65) => new()
        {
            Name = name,
            Width = 500,
            Height = 22,
            Value = value
        };
        var normal = Bar("Determinate");
        var marquee = Bar("Marquee");
        marquee.Style = ProgressBarStyle.Marquee;
        marquee.MarqueeAnimationSpeed = 30;
        var rtl = Bar("RTL", 40);
        rtl.RightToLeft = RightToLeft.Yes;
        rtl.RightToLeftLayout = true;
        var custom = Bar("Designer colors", 55);
        custom.ForeColor = Color.Magenta;
        custom.BackColor = Color.LightPink;
        var disabled = Bar("Disabled", 55);
        disabled.Enabled = false;
        var small = Bar("Compact", 50);
        small.Size = new Size(96, 12);
        layout.Controls.AddRange([normal, marquee, rtl, custom, disabled, small]);
        form.Controls.Add(layout);
        ProgressBar[] bars = [normal, marquee, rtl, custom, disabled, small];
        Bars = bars;
        var area = Screen.PrimaryScreen!.WorkingArea;
        form.StartPosition = FormStartPosition.Manual;
        form.Width = Math.Min(580, (area.Width - 60) / 2);
        form.Height = Math.Min(720, area.Height - 60);
        form.Location = new Point(area.Left + 20 + (startup == ThemeMode.Dark ? form.Width + 20 : 0), area.Top + 25);
        layout.WrapContents = false;
        layout.AutoScroll = true;
        layout.Padding = new Padding(12);
        foreach (var bar in bars.Take(5))
        {
            bar.Width = form.ClientSize.Width - 45;
        }
        var modes = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
        foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System })
        {
            var button = new Button { Text = mode == ThemeMode.System ? "System Mode" : mode.ToString(), AutoSize = true };
            button.Click += (_, _) => manager.SetMode(mode);
            modes.Controls.Add(button);
        }
        layout.Controls.Add(modes);
        layout.Controls.SetChildIndex(modes, 0);
        string[] labels = ["Determinate shimmer", "Marquee", "Right-to-left", "Designer colors preserved", "Disabled", "Compact size: 96 × 12"];
        for (int i = 0; i < bars.Length; i++)
        {
            var caption = new Label { Text = labels[i], AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
            layout.Controls.Add(caption);
            layout.Controls.SetChildIndex(caption, 1 + i * 2);
        }
        var valueLabel = new Label { Text = "Value: 65%", AutoSize = true, Margin = new Padding(0, 16, 0, 0) };
        var value = new TrackBar { Minimum = 0, Maximum = 100, Value = 65, TickFrequency = 10, Width = form.ClientSize.Width - 45 };
        value.ValueChanged += (_, _) =>
        {
            bars[0].Value = value.Value;
            bars[5].Value = value.Value;
            valueLabel.Text = "Value: " + value.Value + "%";
        };
        layout.Controls.Add(valueLabel);
        layout.Controls.Add(value);
        var states = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        foreach (var state in new[] { ("Normal", 1), ("Error", 2), ("Paused", 3) })
        {
            var button = new Button { Text = state.Item1, AutoSize = true };
            button.Click += (_, _) => SendMessage(bars[0].Handle, 0x410, state.Item2, 0);
            states.Controls.Add(button);
        }
        layout.Controls.Add(states);
        var speedRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        speedRow.Controls.Add(new Label { Text = "Marquee interval (0 stops):", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        var speed = new NumericUpDown { Minimum = 0, Maximum = 500, Value = 30, Increment = 10, Width = 75 };
        speed.ValueChanged += (_, _) => bars[1].MarqueeAnimationSpeed = (int)speed.Value;
        speedRow.Controls.Add(speed);
        layout.Controls.Add(speedRow);
        layout.Controls.Add(new Label { Text = "Synthetic control states; changes stay in memory.", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
        Control[] visibilityTargets = layout.Controls.Cast<Control>().Where(control => !ReferenceEquals(control, modes)).ToArray();
        var visibilityRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 5) };
        var toggleControls = new Button { Text = "Hide controls", AutoSize = true };
        var hideForm = new Button { Text = "Hide form…", AutoSize = true };
        visibilityRow.Controls.AddRange([toggleControls, hideForm]);
        layout.Controls.Add(visibilityRow);
        layout.Controls.SetChildIndex(visibilityRow, 1);
        bool controlsVisible = true;
        toggleControls.Click += (_, _) =>
        {
            controlsVisible = !controlsVisible;
            layout.SuspendLayout();
            try
            {
                foreach (Control target in visibilityTargets)
                {
                    target.Visible = controlsVisible;
                }
            }
            finally
            {
                layout.ResumeLayout(true);
            }
            toggleControls.Text = controlsVisible ? "Hide controls" : "Show controls";
        };
        restoreForm = new Form
        {
            Text = "Show progress fixture",
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(390, 175),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };
        var restoreLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10)
        };
        restoreLayout.Controls.Add(new Label { Text = "The fixture is hidden. Choose a theme, then show it.", AutoSize = true });
        var hiddenModes = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System })
        {
            var button = new Button { Text = mode == ThemeMode.System ? "System Mode" : mode.ToString(), AutoSize = true };
            button.Click += (_, _) => manager.SetMode(mode);
            hiddenModes.Controls.Add(button);
        }
        restoreLayout.Controls.Add(hiddenModes);
        var showForm = new Button { Text = "Show fixture", AutoSize = true };
        restoreLayout.Controls.Add(showForm);
        restoreForm.Controls.Add(restoreLayout);
        restoreForm.AcceptButton = showForm;
        restoreRegistration = manager.Attach(restoreForm);
        bool fixtureClosing = false;
        void RestoreFixture()
        {
            if (!fixtureClosing && !form.IsDisposed)
            {
                form.Show();
                restoreForm.Hide();
                form.Activate();
            }
        }
        showForm.Click += (_, _) => RestoreFixture();
        hideForm.Click += (_, _) =>
        {
            form.Hide();
            restoreForm.Show();
            restoreForm.Activate();
        };
        restoreForm.FormClosing += (_, e) =>
        {
            if (!fixtureClosing)
            {
                e.Cancel = true;
                RestoreFixture();
            }
        };
        form.FormClosing += (_, _) =>
        {
            fixtureClosing = true;
            restoreForm.Close();
        };
        void UpdateTitle() => form.Text = $"Progress fixture | started {startup} | {manager.RequestedMode}" +
            (native ? " | native" : noMotion ? " | motion disabled" : " | animated");
        titleChanged = (_, _) => UpdateTitle();
        manager.ThemeChanged += titleChanged;
        UpdateTitle();
        ToggleControls = toggleControls;
        HideFixture = hideForm;
        ShowFixture = showForm;
        ValueSlider = value;
        VisibilityTargets = visibilityTargets;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            manager.ThemeChanged -= titleChanged;
            restoreRegistration.Dispose();
            restoreForm.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
