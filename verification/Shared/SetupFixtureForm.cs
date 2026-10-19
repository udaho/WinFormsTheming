using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.Verification;

internal sealed class SetupFixtureForm : Form
{
    internal ThemeRegistration Registration { get; }
    internal Form Secondary { get; } = new() { Text = "Registered secondary window", Size = new Size(380, 170) };
    internal ToolTip Tips { get; } = new();
    internal TextBox Input { get; } = new() { Text = "Select some text", BackColor = Color.LightPink, Width = 270 };
    internal Panel Accent { get; } = new() { BackColor = Color.Gold, Size = new Size(270, 45) };
    internal Label Inherited { get; } = new() { Text = "Inherited Designer color", ForeColor = Color.Black, AutoSize = true };
    internal ProgressBar Progress { get; } = new() { Value = 65, AccessibleName = "Sample progress", Size = new Size(270, 22) };
    internal ToolStripMenuItem Light { get; } = new("Light");
    internal ToolStripMenuItem Dark { get; } = new("Dark");
    internal ToolStripMenuItem SystemMode { get; } = new("System Mode");
    internal Button DarkButton { get; } = new() { Text = "Dark", AutoSize = true };
    internal Button HideWindow { get; } = new() { Text = "Hide this window", AutoSize = true };
    internal Button RestoreWindow { get; } = new() { Text = "Show main window", AutoSize = true };
    internal int Presentations { get; private set; }
    internal List<string> Diagnostics { get; } = new();

    internal SetupFixtureForm(ThemeManager manager)
    {
        Text = "Registration lifecycle fixture";
        ClientSize = new Size(560, 300);
        var menu = new MenuStrip();
        var appearance = new ToolStripMenuItem("Appearance");
        appearance.DropDownItems.AddRange([Light, Dark, SystemMode]);
        menu.Items.Add(appearance);
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(15) };
        Accent.Controls.Add(Inherited);
        var toggle = new Button { Text = "Toggle sample visibility", AutoSize = true };
        toggle.Click += (_, _) => Input.Visible = Progress.Visible = !Input.Visible;
        var show = new Button { Text = "Show secondary window", AutoSize = true };
        show.Click += (_, _) => Secondary.Show(this);
        HideWindow.Click += (_, _) =>
        {
            // An owned window would hide along with its owner, preventing restoration.
            Secondary.Owner = null;
            Hide();
            Secondary.Show();
        };
        content.Controls.AddRange([Input, Accent, Progress, toggle, show, HideWindow]);
        Controls.Add(content);
        Controls.Add(menu);
        var secondaryContent = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        var lightButton = new Button { Text = "Light", AutoSize = true };
        var systemButton = new Button { Text = "System Mode", AutoSize = true };
        RestoreWindow.Click += (_, _) =>
        {
            Show();
            Secondary.Hide();
        };
        secondaryContent.Controls.AddRange([lightButton, DarkButton, systemButton, RestoreWindow]);
        Secondary.Controls.Add(secondaryContent);
        Secondary.FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing && !IsDisposed)
            {
                e.Cancel = true;
                Secondary.Hide();
                Show();
            }
        };
        Tips.SetToolTip(Input, "Tooltip registered with the form");

        Registration = manager.Prepare(this, setup => setup
            .Include(Secondary)
            .ToolTip(Tips)
            .BindColors(Input, ThemeColorRole.Input)
            .ModeCommand(Light, ThemeMode.Light)
            .ModeCommand(Dark, ThemeMode.Dark)
            .ModeCommand(SystemMode, ThemeMode.System)
            .ModeCommand(lightButton, ThemeMode.Light)
            .ModeCommand(DarkButton, ThemeMode.Dark)
            .ModeCommand(systemButton, ThemeMode.System)
            .OnDiagnostic((_, error) => Diagnostics.Add(error.Operation + ":" + error.ErrorCode))
            .OnThemeChanged((_, change) =>
            {
                Presentations++;
                Text = $"Registration lifecycle fixture | {change.Context.RequestedMode} | callbacks: {Presentations}";
            }));
        // Simulate initialization after Designer defaults were captured.
        Input.BackColor = Color.Yellow;
        Registration.Activate();
        Disposed += (_, _) =>
        {
            Secondary.Dispose();
            Tips.Dispose();
        };
    }
}
