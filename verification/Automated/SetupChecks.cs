using System.Reflection;
using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.Verification;

internal static class SetupChecks
{
    internal static int Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ThemeMode startup = args.Contains("--dark") ? ThemeMode.Dark : ThemeMode.Light;
        var log = new CheckLog("Registration " + startup);
        using var manager = new ThemeManager(startup);
        CheckFailures(manager, log);
        manager.SetMode(startup);
        using (var fixture = new SetupFixtureForm(manager))
        {
            var setup = fixture.Registration;
            log.Check(fixture.Presentations == 1, "Initial presentation was not delivered exactly once");
            log.Check(setup.DefaultsFor(fixture.Input).BackColor == Color.LightPink, "Prepare captured post-initialization color");
            log.Check(!setup.DefaultsFor(fixture.Inherited).HasExplicitBackColor, "Inherited color became explicit");
            log.Check(fixture.Input.BackColor == manager.Palette.Input, "Configured binding was not applied");
            int presentations = fixture.Presentations;
            setup.Activate();
            manager.SetMode(startup);
            log.Check(fixture.Presentations == presentations, "Repeated activation/mode request repeated presentation");
            using (var extraTips = new ToolTip())
            {
                Expect<InvalidOperationException>(() => setup.ToolTip(extraTips), "Active setup allowed reconfiguration", log);
            }
            int registered = manager.RegisteredControlCount;
            var inheritedDefaults = setup.DefaultsFor(fixture.Inherited);
            fixture.Shown += async (_, _) =>
            {
                try
                {
                    fixture.Input.Focus();
                    fixture.Input.Select(2, 5);
                    nint inputHandle = fixture.Input.Handle;
                    nint progressHandle = fixture.Progress.Handle;
                    for (int index = 0; index < 20; index++)
                    {
                        ThemeMode next = manager.RequestedMode == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light;
                        bool focused = fixture.Input.Focused;
                        manager.SetMode(next);
                        log.Check(!focused || fixture.Input.Focused, "Setup callback lost input focus");
                        await Task.Delay(35);
                        log.Check(fixture.Light.Checked == (next == ThemeMode.Light) && fixture.Dark.Checked == (next == ThemeMode.Dark), "Menu checks did not follow requested mode");
                        log.Check(fixture.Input.BackColor == manager.Palette.Input, "Binding did not refresh");
                        log.Check(fixture.Input.SelectionStart == 2 && fixture.Input.SelectionLength == 5, "Text selection changed");
                        log.Check(fixture.Input.Handle == inputHandle && fixture.Progress.Handle == progressHandle, "Setup recreated native controls");
                        log.Check(fixture.Accent.BackColor == Color.Gold && fixture.Inherited.BackColor == Color.Gold, "Designer/inherited colors changed");
                        log.Check(manager.RegisteredControlCount == registered, "Registration count drifted");
                    }
                    fixture.SystemMode.PerformClick();
                    log.Check(manager.RequestedMode == ThemeMode.System && fixture.SystemMode.Checked && !fixture.Dark.Checked && !fixture.Light.Checked,
                        "System Mode command/checks use resolved appearance instead of preference");
                    fixture.Secondary.Show(fixture);
                    fixture.DarkButton.PerformClick();
                    log.Check(manager.RequestedMode == ThemeMode.Dark && fixture.Dark.Checked, "Detached button mode command failed");
                    fixture.Input.Hide();
                    fixture.HideWindow.PerformClick();
                    await Task.Delay(50);
                    log.Check(!fixture.Visible && fixture.Secondary.Visible && fixture.Secondary.Owner is null,
                        "Hide left no independent restore window visible");
                    fixture.DarkButton.PerformClick();
                    manager.SetMode(ThemeMode.Light);
                    fixture.RestoreWindow.PerformClick();
                    log.Check(fixture.Visible && !fixture.Secondary.Visible, "Restore-window command did not restore the owner");
                    log.Check(!fixture.Input.Visible, "Form restore changed hidden control visibility");
                    fixture.Input.Show();
                    log.Check(fixture.Input.BackColor == manager.Palette.Input, "Hidden control retained stale theme");
                    typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Input, null);
                    await Task.Delay(100);
                    log.Check(fixture.Input.BackColor == manager.Palette.Input, "Handle recreation lost binding");
                    log.Check(fixture.Diagnostics.Count == 0, "Native diagnostics: " + string.Join(",", fixture.Diagnostics));
                    setup.Dispose();
                    log.Check(!setup.IsActive && manager.RegisteredControlCount == 0, "Disposal retained roots");
                    log.Check(fixture.Input.BackColor == Color.LightPink, "Disposal lost early Designer color");
                    log.Check(!fixture.Tips.OwnerDraw && fixture.Tips.BackColor == SystemColors.Info, "Tooltip not restored");
                    log.Check(!fixture.Light.Checked && !fixture.Dark.Checked && !fixture.SystemMode.Checked, "Menu originals not restored");
                    presentations = fixture.Presentations;
                    manager.SetMode(ThemeMode.Light);
                    fixture.Dark.PerformClick();
                    log.Check(manager.RequestedMode == ThemeMode.Light && fixture.Presentations == presentations, "Disposed callbacks/commands remain wired");
                    inheritedDefaults.RestoreBackground(fixture.Inherited);
                    log.Check(!System.ComponentModel.TypeDescriptor.GetProperties(fixture.Inherited)["BackColor"]!.ShouldSerializeValue(fixture.Inherited), "Restore pinned inherited color");
                }
                catch (Exception error)
                {
                    log.Check(false, error.ToString());
                }
                finally
                {
                    fixture.Close();
                }
            };
            Application.Run(fixture);
        }
        CheckOwnership(manager, log);
        return log.Finish();
    }

    private static void CheckFailures(ThemeManager manager, CheckLog log)
    {
        using var root = new Form();
        using var input = new TextBox { BackColor = Color.Pink };
        root.Controls.Add(input);
        using var tips = new ToolTip();
        using var command = new ToolStripMenuItem("Mode");
        using var unattached = new TextBox();
        Expect<ArgumentException>(() => manager.Register(root, setup => setup.ToolTip(tips).ModeCommand(command, ThemeMode.Dark)
            .BindColors(unattached, ThemeColorRole.Input)), "Bad binding did not fail", log);
        log.Check(manager.RegisteredControlCount == 0 && !tips.OwnerDraw, "Activation rollback retained roots/tooltip");
        manager.SetMode(ThemeMode.Light);
        command.PerformClick();
        log.Check(manager.RequestedMode == ThemeMode.Light, "Rollback retained command handler");
        Expect<InvalidOperationException>(() => manager.Prepare(root, _ => throw new InvalidOperationException("Configuration")), "Bad configuration did not fail", log);
        Expect<InvalidOperationException>(() => manager.Register(root, setup => setup.OnThemeChanged((_, _) => throw new InvalidOperationException("Initial callback"))), "Initial callback did not fail", log);
        log.Check(manager.RegisteredControlCount == 0, "Initial callback failure retained roots");
        var badApply = new ProbeAdapter(input, () => throw new InvalidOperationException("Apply"));
        Expect<InvalidOperationException>(() => manager.Register(root, setup => setup.AddAdapter(badApply)), "Adapter application did not fail", log);
        log.Check(badApply.Detaches > 0 && manager.RegisteredControlCount == 0, "Failed adapter was not detached");
        manager.Refresh();
        int diagnostics = 0;
        var report = typeof(ThemeManager).GetMethod("ReportDiagnostic", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var reporter = new ProbeAdapter(input, () => report.Invoke(manager, ["Synthetic setup check", 123]));
        using (manager.Register(root, setup => setup.AddAdapter(reporter).OnDiagnostic((_, error) =>
        {
            if (error.Operation == "Synthetic setup check" && error.ErrorCode == 123)
            {
                diagnostics++;
            }
        })))
        {
            log.Check(diagnostics == 1, "Activation diagnostic was missed");
        }
        int delivered = diagnostics;
        report.Invoke(manager, ["Synthetic setup check", 123]);
        log.Check(diagnostics == delivered, "Disposed diagnostic callback retained");
        int callbacks = 0;
        using (var setup = manager.Register(root, theme => theme.ToolTip(tips).OnThemeChanged((_, _) => callbacks++)))
        {
            log.Check(callbacks == 1, "Failed setup left callbacks behind");
        }
        using var child2 = new TextBox();
        root.Controls.Add(child2);
        var badDetach = new ProbeAdapter(input, null, true);
        var cleanupSetup = manager.Register(root, theme => theme.AddAdapter(badDetach).ToolTip(tips));
        Expect<AggregateException>(cleanupSetup.Dispose, "Adapter detach error was not reported", log);
        log.Check(manager.RegisteredControlCount == 0 && !tips.OwnerDraw, "Cleanup error prevented remaining resources releasing");
        log.Check(badDetach.Detaches == 2, "Cleanup did not visit every matching control");
        cleanupSetup.Dispose();
        using (var disposedSecondary = new Form())
        {
            var lateFailure = manager.Prepare(root, setup => setup.Include(disposedSecondary).ToolTip(tips));
            disposedSecondary.Dispose();
            Expect<ObjectDisposedException>(lateFailure.Activate, "Disposed included root was accepted", log);
            log.Check(manager.RegisteredControlCount == 0 && !tips.OwnerDraw, "Disposed-root rollback retained resources");
        }
        using (var disposedDuringActivation = new Form())
        {
            var target = new TextBox();
            disposedDuringActivation.Controls.Add(target);
            var disposeAdapter = new ProbeAdapter(target, disposedDuringActivation.Dispose);
            Expect<ObjectDisposedException>(() => manager.Register(disposedDuringActivation, setup => setup.AddAdapter(disposeAdapter)),
                "Owner disposal during activation was accepted", log);
            log.Check(manager.RegisteredControlCount == 0, "Owner disposal during activation leaked resources");
            manager.Refresh();
        }
        using (var preparingOwner = new Form())
        {
            var inactive = manager.Prepare(preparingOwner);
            preparingOwner.Dispose();
            Expect<ObjectDisposedException>(inactive.Activate, "Disposed prepared owner was accepted", log);
        }
        using var prepared = manager.Prepare(root);
        var threadError = Task.Run(() =>
        {
            try
            {
                prepared.Activate();
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }).GetAwaiter().GetResult();
        log.Check(threadError && !prepared.IsActive, "Cross-thread activation was accepted");
    }

    private static void CheckOwnership(ThemeManager manager, CheckLog log)
    {
        using var root = new Form();
        using var secondary = new Form();
        using var tips = new ToolTip();
        var prepared = manager.Prepare(root, setup => setup.Include(secondary).ToolTip(tips));
        log.Check(manager.RegisteredControlCount == 0 && !prepared.IsActive, "Prepare applied theming");
        prepared.Activate();
        int registered = manager.RegisteredControlCount;
        using (var overlap = manager.Register(root))
        {
            log.Check(manager.RegisteredControlCount == registered, "Overlapping scopes duplicate controls");
        }
        log.Check(prepared.IsActive && manager.RegisteredControlCount == registered, "Overlapping disposal detached live scope");
        root.Dispose();
        log.Check(!prepared.IsActive && manager.RegisteredControlCount == 0 && !secondary.IsDisposed && !tips.OwnerDraw,
            "Owner disposal retained setup or disposed included components");
        prepared.Dispose();
        using var managerRoot = new Form();
        using var managerTips = new ToolTip();
        var owned = manager.Register(managerRoot, setup => setup.ToolTip(managerTips));
        manager.Dispose();
        log.Check(!owned.IsActive && manager.RegisteredControlCount == 0 && !managerTips.OwnerDraw, "Manager disposal did not release setup");
        owned.Dispose();
    }

    private static void Expect<T>(Action action, string failure, CheckLog log) where T : Exception
    {
        try
        {
            action();
            log.Check(false, failure);
        }
        catch (T)
        {
            log.Check(true, "Expected exception");
        }
    }

    private sealed class ProbeAdapter(Control target, Action? apply = null, bool throwDetach = false) : IThemeAdapter
    {
        internal int Detaches { get; private set; }
        public bool CanHandle(Control control) => throwDetach ? control is TextBox : ReferenceEquals(control, target);
        public void Apply(Control control, ThemeContext context, ThemePalette palette) => apply?.Invoke();
        public void Detach(Control control)
        {
            Detaches++;
            if (throwDetach)
            {
                throw new InvalidOperationException("Detach");
            }
        }
    }
}
