using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.Verification;

internal sealed class CommonControlsForm : Form
{
    internal IReadOnlyList<Control> Samples
    {
        get;
    }
    internal CommonControlsForm(ThemeManager manager)
    {
        Text = "Common WinForms controls — includes known theming gaps";
        Size = new Size(1220, 940);
        Form form = this;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 7 };
        for (int index = 0; index < 4; index++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }
        for (int index = 0; index < 7; index++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 7));
        }
        form.Controls.Add(layout);
        var controls = new List<Control>();
        void Add(Control control)
        {
            control.Name = control.GetType().Name;
            var cell = new Panel { Dock = DockStyle.Fill };
            cell.Controls.Add(new Label { Text = control.Name, Dock = DockStyle.Top, Height = 20 });
            control.Dock = DockStyle.Fill;
            var holder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(3) };
            holder.Controls.Add(control);
            cell.Controls.Add(holder);
            holder.BringToFront();
            layout.Controls.Add(cell);
            controls.Add(control);
        }
        Add(new Button { Text = "Button" });
        Add(new CheckBox { Text = "Check box", Checked = true });
        Add(new RadioButton { Text = "Radio", Checked = true });
        Add(new Label { Text = "Label", TextAlign = ContentAlignment.MiddleLeft });
        Add(new LinkLabel { Text = "Link label" });
        Add(new TextBox { Text = "Editable text" });
        Add(new MaskedTextBox { Text = "Masked text" });
        Add(new RichTextBox { Text = "Rich text", DetectUrls = false });
        Add(new ComboBox { Items = { "First", "Second" }, SelectedIndex = 0 });
        Add(new ListBox { Items = { "First", "Second" }, SelectedIndex = 0 });
        Add(new CheckedListBox { Items = { "First", "Second" } });
        Add(new NumericUpDown { Value = 37 });
        Add(new DomainUpDown { Items = { "First", "Second" }, SelectedIndex = 0 });
        Add(new TrackBar { Maximum = 100, Value = 37 });
        Add(new HScrollBar { Maximum = 100, Value = 37 });
        Add(new VScrollBar { Maximum = 100, Value = 37 });
        Add(new DateTimePicker { Value = DateTime.Today });
        Add(new MonthCalendar { SelectionStart = DateTime.Today, SelectionEnd = DateTime.Today });
        var list = new ListView { View = View.Details };
        list.Columns.Add("Header", 120);
        list.Items.Add("Sample");
        Add(list);
        var tree = new TreeView();
        tree.Nodes.Add("Sample").Nodes.Add("Child");
        tree.ExpandAll();
        Add(tree);
        Add(new GroupBox { Text = "Group", Controls = { new Label { Text = "Content", Location = new Point(10, 25) } } });
        var tabs = new TabControl();
        tabs.TabPages.Add("First");
        tabs.TabPages.Add("Second");
        Add(tabs);
        Add(new SplitContainer { Orientation = Orientation.Vertical });
        Add(new PictureBox());
        var grid = new DataGridView { AllowUserToAddRows = false, RowHeadersVisible = false };
        grid.Columns.Add("First", "First");
        grid.Rows.Add("Sample");
        Add(grid);
        Add(new PropertyGrid { SelectedObject = new SampleProperties(), ToolbarVisible = false, HelpVisible = false });
        Add(new ToolStrip { Items = { new ToolStripButton("Toolbar") } });
        Add(new StatusStrip { Items = { new ToolStripStatusLabel("Status") } });
        foreach (ListBox listBox in controls.OfType<ListBox>())
        {
            for (int index = 2; index < 40; index++)
            {
                listBox.Items.Add("Item " + index);
            }
        }
        grid.Rows.Add("Second");
        grid.Rows.Add("Third");
        Samples = controls;
        var modes = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36 };
        foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System })
        {
            var button = new Button { Text = mode == ThemeMode.System ? "System Mode" : mode.ToString(), AutoSize = true };
            button.Click += (_, _) => manager.SetMode(mode);
            modes.Controls.Add(button);
        }
        Controls.Add(modes);
        modes.BringToFront();
    }
    public sealed class SampleProperties
    {
        public string Name { get; set; } = "Sample";
        public int Count { get; set; } = 37;
    }
}
