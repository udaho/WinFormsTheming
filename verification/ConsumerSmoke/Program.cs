using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.ConsumerSmoke;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        if (SystemInformation.HighContrast)
        {
            Console.Error.WriteLine("Start automated checks in normal contrast; use the fixtures for Windows contrast-theme acceptance.");
            return 2;
        }
        ThemeMode startup = args.Contains("--light") ? ThemeMode.Light : ThemeMode.Dark;
        var checks = new ConsumerChecks(startup);
        using var themes = new ThemeManager(startup);
        themes.Diagnostic += (_, error) => checks.Check(false, $"Native API failure: {error.Operation} ({error.ErrorCode})");
        using var host = new Form { Text = "Theme API consumer", ClientSize = new Size(700, 420) };
        using var secondary = new Form { Text = "Secondary surface", ClientSize = new Size(260, 100), ShowInTaskbar = false };
        using var tips = new ToolTip();
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        var input = new TextBox { Text = "alpha beta gamma", Width = 220 };
        var choices = new ListBox { Width = 180, Height = 130 };
        choices.Items.AddRange(Enumerable.Range(0, 60).Select(index => (object)("Choice " + index)).ToArray());
        var slider = new TrackBar { Minimum = -50, Maximum = 150, Value = 24, TickFrequency = 20, Width = 220 };
        var action = new Button { Text = "Action", AutoSize = true };
        var pages = new TabControl { Size = new Size(280, 130) };
        foreach (string caption in new[] { "Page A", "Page B" })
        {
            var page = new TabPage(caption) { UseVisualStyleBackColor = true };
            page.Controls.Add(new Label { Text = caption, AutoSize = true });
            pages.TabPages.Add(page);
        }
        var accent = new Panel { BackColor = Color.Gold, Size = new Size(140, 40) };
        accent.Controls.Add(new Label { Text = "Inherited color", AutoSize = true });
        flow.Controls.AddRange([input, choices, slider, action, pages, accent]);
        host.Controls.Add(flow);
        secondary.Controls.Add(new CheckBox { Text = "Option", Checked = true, AutoSize = true });
        tips.SetToolTip(input, "Synthetic input");
        using var menu = new ContextMenuStrip();
        var submenu = new ToolStripMenuItem("Commands");
        submenu.DropDownItems.Add("Nested command");
        menu.Items.Add(submenu);
        input.ContextMenuStrip = menu;
        int presentations = 0;
        using var hostRegistration = themes.Register(host, setup => setup
            .Include(secondary)
            .ToolTip(tips)
            .OnThemeChanged((_, _) => presentations++));
        checks.Check(presentations == 1, "Registration did not apply initial presentation");
        host.Shown += async (_, _) =>
        {
            try
            {
                secondary.Show(host);
                host.Activate();
                input.Focus();
                input.Select(2, 5);
                choices.SelectedIndex = 18;
                choices.TopIndex = 12;
                pages.SelectedIndex = 1;
                await Task.Delay(100);
                int registered = themes.RegisteredControlCount;
                int top = choices.TopIndex;
                var handles = new Control[] { input, choices, slider, pages }.ToDictionary(control => control, control => control.Handle);
                var colors = new Control[] { host, secondary, input, choices, slider, action, pages.TabPages[0], pages.TabPages[1] }
                    .ToDictionary(control => control, control => (Back: control.BackColor.ToArgb(), Fore: control.ForeColor.ToArgb()));
                long revision = themes.Revision;
                themes.SetMode(startup);
                checks.Check(themes.Revision == revision, "Same-mode request refreshed controls");
                using (themes.Attach(host))
                {
                    using (themes.Attach(flow))
                    {
                        checks.Check(themes.RegisteredControlCount == registered, "Overlapping roots duplicated registrations");
                    }
                }

                checks.Check(themes.RegisteredControlCount == registered, "Overlapping-root disposal removed live registrations");
                for (int index = 0; index < 20; index++)
                {
                    bool focused = input.Focused;
                    themes.SetMode(themes.Context.IsDark ? ThemeMode.Light : ThemeMode.Dark);
                    checks.Check(!focused || input.Focused, "Input lost focus inside SetMode");
                    await Task.Delay(40);
                    checks.Check(input.Text == "alpha beta gamma" && input.SelectionStart == 2 && input.SelectionLength == 5, "Text or selection changed");
                    checks.Check(choices.SelectedIndex == 18 && choices.TopIndex == top, "ListBox state changed");
                    checks.Check(slider.Value == 24 && slider.Minimum == -50 && slider.Maximum == 150, "Slider state changed");
                    checks.Check(pages.SelectedIndex == 1 && pages.TabPages.Cast<TabPage>().All(page => page.UseVisualStyleBackColor == !themes.Context.IsDark), "Page state changed");
                    checks.Check(secondary.Visible && secondary.Owner == host && host.Visible, "Window state changed");
                    checks.Check(handles.All(pair => pair.Key.Handle == pair.Value), "A native control was replaced");
                    checks.Check(action.FlatStyle == FlatStyle.Standard, "Configured button style changed");
                    checks.Check(accent.BackColor == Color.Gold && accent.Controls[0].BackColor == Color.Gold, "Inherited accent changed");
                    checks.Check(themes.RegisteredControlCount == registered, "Registration count drifted");
                }
                checks.Check(colors.All(pair => pair.Key.BackColor.ToArgb() == pair.Value.Back && pair.Key.ForeColor.ToArgb() == pair.Value.Fore),
                    "Round-trip colors differ from startup");
                action.Focus();
                for (int index = 0; index < 4; index++)
                {
                    bool focused = action.Focused;
                    themes.SetMode(themes.Context.IsDark ? ThemeMode.Light : ThemeMode.Dark);
                    checks.Check(!focused || action.Focused, "Focused button lost focus");
                }
                using (var added = new TextBox { Text = "Dynamic", Name = "AdapterSubject", BackColor = Color.Tomato })
                {
                    flow.Controls.Add(added);
                    checks.Check(themes.RegisteredControlCount == registered + 1, "Dynamic control was not discovered");
                    using (themes.BindColors(added, ThemeColorRole.WarningBackground))
                    {
                        checks.Check(added.BackColor == themes.Palette.WarningBackground, "Binding was not applied");
                    }
                    checks.Check(added.BackColor == Color.Tomato, "Binding did not restore explicit color");
                    var adapter = new SampleAdapter();
                    using (themes.AddAdapter(adapter))
                    {
                        checks.Check(adapter.Applies > 0, "Adapter was not applied");
                    }
                    checks.Check(added.BackColor == Color.Tomato && adapter.Detaches > 0, "Adapter did not restore its changes");
                    flow.Controls.Remove(added);
                    checks.Check(themes.RegisteredControlCount == registered, "Removed control remained registered");
                }
                EventHandler<ThemeChangedEventArgs> reentrant = (_, change) =>
                {
                    if (change.Context.RequestedMode == ThemeMode.Dark)
                    {
                        themes.SetMode(ThemeMode.Light);
                    }
                };
                themes.SetMode(ThemeMode.Light);
                themes.ThemeChanged += reentrant;
                themes.SetMode(ThemeMode.Dark);
                await Task.Delay(100);
                themes.ThemeChanged -= reentrant;
                checks.Check(themes.RequestedMode == ThemeMode.Light, "Reentrant request was not drained");
                themes.Refresh();
                checks.Check(themes.Revision > revision, "Refresh did not advance revision");
                themes.SetMode(ThemeMode.Dark);
                using (var closeDialog = new System.Windows.Forms.Timer { Interval = 100 })
                {
                    closeDialog.Tick += (_, _) =>
                    {
                        Form? dialog = Application.OpenForms.Cast<Form>().SingleOrDefault(form => form.Text == "Sample confirmation");
                        if (dialog is null)
                        {
                            return;
                        }
                        checks.Check((dialog.AcceptButton as Button)?.DialogResult == DialogResult.No, "Dialog default result changed");
                        checks.Check((dialog.CancelButton as Button)?.DialogResult == DialogResult.Cancel, "Dialog cancel result changed");
                        dialog.DialogResult = DialogResult.No;
                        dialog.Close();
                        closeDialog.Stop();
                    };
                    closeDialog.Start();
                    checks.Check(ThemeMessageBox.Show(host, "Choose a result", "Sample confirmation", MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.No, "Dialog returned the wrong result");
                }
                themes.Dispose();
                checks.Check(ThemeManager.Current is null && themes.RegisteredControlCount == 0, "Manager retained registrations after disposal");
                checks.Check(!tips.OwnerDraw && pages.TabPages.Cast<TabPage>().All(page => page.UseVisualStyleBackColor), "Disposal did not restore original policies");
                using (var replacement = new ThemeManager(ThemeMode.Light))
                {
                    using (var cleanupRoot = new Form())
                    {
                        cleanupRoot.Controls.Add(new Button());
                        using var registration = replacement.Attach(cleanupRoot);
                        using var adapter = replacement.AddAdapter(new ThrowingAdapter());
                        try
                        {
                            replacement.Dispose();
                            checks.Check(false, "Cleanup exception was not reported");
                        }
                        catch (AggregateException)
                        {
                        }
                        checks.Check(ThemeManager.Current is null && replacement.RegisteredControlCount == 0, "Cleanup failure leaked manager state");
                    }
                }
            }
            catch (Exception exception)
            {
                checks.Check(false, exception.ToString());
            }
            finally
            {
                secondary.Close();
                host.Close();
            }
        };
        Application.Run(host);
        return checks.Finish();
    }
}
