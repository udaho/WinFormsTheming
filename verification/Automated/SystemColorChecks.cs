using System.ComponentModel;
using System.Reflection;
using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.Verification;

internal static class SystemColorChecks
{
    internal static int Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        ThemeMode startup = ThemeMode.Light;
        if (args.Contains("--dark"))
        {
            startup = ThemeMode.Dark;
        }
        ThemeMode opposite = ThemeMode.Dark;
        if (startup == ThemeMode.Dark)
        {
            opposite = ThemeMode.Light;
        }
        using var manager = new ThemeManager(startup, new ThemeOptions
        {
            PaletteFactory = context => ThemePalette.Create(context) with { WarningBackground = Color.LightCyan }
        });
        using var form = new SystemColorFixtureForm(manager);
        using var registration = manager.Attach(form);
        var log = new CheckLog("System colors " + startup);
        var defaults = form.Inputs.ToDictionary(input => input, input => input.BackColor);
        var handles = new Dictionary<Control, nint>();
        var texts = form.Inputs.ToDictionary(input => input, input => input.Text);
        var diagnostics = new List<string>();
        manager.Diagnostic += (_, message) => diagnostics.Add($"{message.Operation}: {message.ErrorCode}");

        async Task Sample(string stage)
        {
            await Task.Delay(120);
            foreach (TextBoxBase input in form.Inputs)
            {
                log.Check(input.Text == texts[input], stage + ": text changed for " + texts[input]);
                if (!input.Visible)
                {
                    continue;
                }
                if (handles.TryGetValue(input, out nint originalHandle))
                {
                    log.Check(input.Handle == originalHandle, stage + ": handle changed for " + texts[input]);
                }
                else
                {
                    handles[input] = input.Handle;
                }
                CheckBackground(input, log, stage);
            }
            log.Check(form.ReadOnlyInput.BackColor == SystemColors.ActiveCaption, stage + ": system-color identity changed");
            log.Check(form.ReadOnlyInput.ReadOnly && form.ReadOnlyInput.Enabled && !form.ReadOnlyInput.TabStop,
                stage + ": read-only interaction policy changed");
            log.Check(!form.DisabledInput.Enabled, stage + ": disabled input became enabled");
            log.Check(form.FixedInput.BackColor == Color.LightPink && form.FixedInput.ForeColor == Color.DarkBlue,
                stage + ": fixed Designer colors changed");
            log.Check(form.Inherited.BackColor == form.Accent.BackColor &&
                !TypeDescriptor.GetProperties(form.Inherited)[nameof(Control.BackColor)]!.ShouldSerializeValue(form.Inherited),
                stage + ": inherited color became explicit");
            form.MixedInput.Select(0, 1);
            log.Check(form.MixedInput.SelectionColor == Color.Red, stage + ": first rich-text format changed");
            form.MixedInput.Select(21, 1);
            log.Check(form.MixedInput.SelectionColor == Color.Blue, stage + ": second rich-text format changed");
            form.MixedInput.Select(0, 0);
        }

        form.Shown += async (_, _) =>
        {
            try
            {
                form.ReadOnlyInput.Select(2, 5);
                await Sample("Startup");
                foreach (ThemeMode mode in new[] { opposite, startup, opposite, startup })
                {
                    manager.SetMode(mode);
                    await Sample("Switched " + mode);
                    log.Check(form.ReadOnlyInput.SelectionStart == 2 && form.ReadOnlyInput.SelectionLength == 5,
                        "Switch changed read-only selection");
                }
                int backgroundNotifications = 0;
                EventHandler countChanges = (_, _) => backgroundNotifications++;
                form.ReadOnlyInput.BackColorChanged += countChanges;
                try
                {
                    manager.Refresh();
                    manager.SetMode(startup);
                    await Sample("Redundant refresh");
                    log.Check(backgroundNotifications == 0, "An unchanged system background raised redundant color notifications");
                }
                finally
                {
                    form.ReadOnlyInput.BackColorChanged -= countChanges;
                }
                form.Pages.SelectedIndex = 1;
                await Sample("Previously hidden tab");
                manager.SetMode(opposite);
                await Sample("Hidden tab switched");
                form.Pages.SelectedIndex = 0;
                await Sample("Original tab restored");
                form.Hide();
                manager.SetMode(startup);
                form.Show();
                await Sample("Form switched while hidden");

                form.RuntimeInput.BackColor = Color.PaleGreen;
                manager.SetMode(opposite);
                await Sample("Runtime RGB");
                log.Check(form.RuntimeInput.BackColor == Color.PaleGreen, "Theme refresh overwrote runtime RGB");
                form.RuntimeInput.BackColor = SystemColors.Info;
                manager.SetMode(startup);
                await Sample("Runtime system color");
                log.Check(form.RuntimeInput.BackColor == SystemColors.Info, "Theme refresh restored an obsolete Designer color");

                using (manager.BindColors(form.RuntimeInput, ThemeColorRole.Input))
                {
                    manager.SetMode(opposite);
                    await Sample("Bound color");
                    log.Check(form.RuntimeInput.BackColor == manager.Palette.Input, "System refresh overrode a binding");
                }
                await Sample("Binding removed");
                log.Check(form.RuntimeInput.BackColor == defaults[form.RuntimeInput], "Binding removal did not restore Designer system color");

                using (manager.BindColors(form.RuntimeInput, ThemeColorRole.WarningBackground))
                {
                    manager.SetMode(startup);
                    await Sample("Custom palette binding");
                    log.Check(form.RuntimeInput.BackColor == Color.LightCyan, "System refresh overrode a custom palette binding");
                }

                EventHandler changeColor = (_, _) =>
                {
                    if (!form.RuntimeInput.BackColor.IsSystemColor)
                    {
                        form.RuntimeInput.BackColor = Color.PaleGoldenrod;
                    }
                };
                form.RuntimeInput.BackColorChanged += changeColor;
                try
                {
                    manager.SetMode(opposite);
                    await Sample("Application color-change handler");
                    log.Check(form.RuntimeInput.BackColor == Color.PaleGoldenrod, "System refresh overwrote an application callback's color");
                }
                finally
                {
                    form.RuntimeInput.BackColorChanged -= changeColor;
                }

                using (var lateInput = new TextBox
                {
                    Text = "Dynamically added input",
                    BackColor = SystemColors.ActiveCaption,
                    Multiline = true,
                    Size = new Size(250, 70)
                })
                {
                    form.Pages.TabPages[1].Controls.Add(lateInput);
                    manager.SetMode(opposite);
                    form.Pages.SelectedIndex = 1;
                    await Task.Delay(150);
                    CheckBackground(lateInput, log, "Dynamically added control");
                    manager.SetMode(startup);
                    await Task.Delay(150);
                    CheckBackground(lateInput, log, "Dynamically added control switched");
                    form.Pages.TabPages[0].Controls.Add(lateInput);
                    form.Pages.SelectedIndex = 0;
                    await Task.Delay(150);
                    CheckBackground(lateInput, log, "Reparented control");
                }
                CheckContrast(form.ReadOnlyInput, manager.Context, log);

                typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(form.ReadOnlyInput, null);
                handles.Remove(form.ReadOnlyInput);
                await Sample("Recreated handle");
                manager.SetMode(startup);
                await Sample("Recreated handle switched");
                registration.Dispose();
                foreach (TextBoxBase input in form.Inputs)
                {
                    log.Check(input.BackColor == defaults[input], "Detach did not restore Designer color for " + texts[input]);
                }
                using (manager.Attach(form))
                {
                    await Sample("Reattached");
                }
                log.Check(diagnostics.Count == 0, "Unexpected native diagnostics: " + string.Join("; ", diagnostics));
            }
            catch (Exception exception)
            {
                log.Check(false, exception.ToString());
            }
            finally
            {
                form.Close();
            }
        };
        Application.Run(form);
        return log.Finish();
    }

    private static void CheckBackground(Control control, CheckLog log, string stage)
    {
        using var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        int expected = control.BackColor.ToArgb();
        int actual = image.GetPixel(image.Width - 10, image.Height - 10).ToArgb();
        log.Check(actual == expected,
            $"{stage}: stale native background for {control.Text}: expected {expected:X8}, actual {actual:X8}");
    }

    private static void CheckContrast(TextBox input, ThemeContext originalContext, CheckLog log)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type stateType = typeof(ThemeManager).Assembly.GetType("HenLib.WinFormsTheming.ControlThemeState")!;
        object state = Activator.CreateInstance(stateType, [input, null])!;
        MethodInfo apply = stateType.GetMethod("Apply", flags)!;
        var contrast = new ThemeContext(originalContext.RequestedMode, false, true);
        apply.Invoke(state, [contrast, ThemePalette.Create(contrast), new ThemeOptions(), null, null]);
        log.Check(input.BackColor == SystemColors.Window && input.ForeColor == SystemColors.WindowText,
            "Simulated high contrast did not override explicit colors");
        CheckBackground(input, log, "Simulated high contrast");
        apply.Invoke(state, [originalContext, ThemePalette.Create(originalContext), new ThemeOptions(), null, null]);
        log.Check(input.BackColor == SystemColors.ActiveCaption, "Leaving simulated contrast lost system-color identity");
        CheckBackground(input, log, "Leaving simulated contrast");
    }
}
