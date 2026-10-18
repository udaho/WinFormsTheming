using System.Reflection;
using HenLib.WinFormsTheming;

namespace HenLib.WinFormsTheming.Verification;

internal static class LifecycleChecks
{
    internal static int Run()
    {
        ApplicationConfiguration.Initialize();
        var log = new CheckLog("Lifecycle");
        void Check(bool value, string message) => log.Check(value, message);
        using var manager = new ThemeManager(ThemeMode.Dark);
        using (var defaultTip = new ToolTip())
        using (var customTip = new ToolTip { BackColor = Color.LightPink, ForeColor = Color.DarkBlue })
        using (var ownedTip = new ToolTip { OwnerDraw = true, BackColor = Color.Lime, ForeColor = Color.Purple })
        using (manager.AttachToolTip(defaultTip))
        using (manager.AttachToolTip(customTip))
        using (manager.AttachToolTip(ownedTip))
        {
            foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.Light, ThemeMode.Dark, ThemeMode.Light })
            {
                manager.SetMode(mode);
                bool dark = manager.Context.IsDark;
                Check(defaultTip.OwnerDraw == dark && customTip.OwnerDraw == dark, "Tooltip rendering policy incorrect in " + mode);
                Check(defaultTip.BackColor == (dark ? manager.Palette.Surface : SystemColors.Info) &&
                    defaultTip.ForeColor == (dark ? manager.Palette.Text : SystemColors.InfoText), "Default tooltip colors incorrect in " + mode);
                Check(customTip.BackColor == (dark ? manager.Palette.Surface : Color.LightPink) &&
                    customTip.ForeColor == (dark ? manager.Palette.Text : Color.DarkBlue), "Explicit tooltip colors incorrect in " + mode);
                Check(ownedTip.OwnerDraw && ownedTip.BackColor == Color.Lime && ownedTip.ForeColor == Color.Purple,
                    "Application-owned tooltip rendering/colors changed in " + mode);
            }
            var entries = (System.Collections.IEnumerable)typeof(ThemeManager).GetField("toolTips", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
            object defaultRegistration = entries.Cast<object>().Single(t => ReferenceEquals(t.GetType().GetProperty("ToolTip", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(t), defaultTip));
            using var host = new Form();
            using var label = new Label();
            var popup = new PopupEventArgs(host, label, false, new Size(199, 37));
            defaultRegistration.GetType().GetMethod("Popup", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(defaultRegistration, [null, popup]);
            Check(popup.ToolTipSize == new Size(199, 37), "Light tooltip size was overridden by the Dark renderer");
            manager.SetMode(ThemeMode.Dark);
            defaultTip.SetToolTip(label, "Tooltip body text");
            object MeasureTip() => defaultRegistration.GetType().GetMethod("Measure", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(defaultRegistration, ["Tooltip body text", 96])!;
            Size bodyOnly = (Size)MeasureTip().GetType().GetProperty("Size")!.GetValue(MeasureTip())!;
            defaultTip.ToolTipTitle = "Important title";
            defaultTip.ToolTipIcon = ToolTipIcon.Warning;
            object layout = MeasureTip();
            Size fullSize = (Size)layout.GetType().GetProperty("Size")!.GetValue(layout)!;
            Rectangle titleBounds = (Rectangle)layout.GetType().GetProperty("Title")!.GetValue(layout)!;
            Rectangle iconBounds = (Rectangle)layout.GetType().GetProperty("Icon")!.GetValue(layout)!;
            Check(fullSize.Height > bodyOnly.Height && titleBounds.Height > 0 && iconBounds.Width > 0,
                "Dark tooltip did not reserve space for title/icon");
            using (var bitmap = new Bitmap(fullSize.Width, fullSize.Height))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var draw = new DrawToolTipEventArgs(graphics, host, label, new Rectangle(Point.Empty, fullSize),
                    "Tooltip body text", defaultTip.BackColor, defaultTip.ForeColor, SystemFonts.MessageBoxFont!);
                defaultRegistration.GetType().GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(defaultRegistration, [null, draw]);
                bool HasContent(Rectangle bounds) => Enumerable.Range(bounds.Left, bounds.Width).Any(x =>
                    Enumerable.Range(bounds.Top, bounds.Height).Any(y => bitmap.GetPixel(x, y).ToArgb() != manager.Palette.Surface.ToArgb()));
                Check(HasContent(titleBounds), "Dark tooltip title was not drawn");
                Check(HasContent(iconBounds), "Dark tooltip icon was not drawn");
            }
            defaultTip.ToolTipTitle = "";
            defaultTip.ToolTipIcon = ToolTipIcon.None;
            manager.SetMode(ThemeMode.Light);
            ThemeContext previous = manager.Context;
            var contextProperty = typeof(ThemeManager).GetProperty("Context")!;
            try
            {
                contextProperty.SetValue(manager, new ThemeContext(ThemeMode.Light, false, true));
                foreach (object entry in entries)
                    entry.GetType().GetMethod("RefreshColors", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(entry, null);
                Check(!defaultTip.OwnerDraw && defaultTip.BackColor == SystemColors.Info && defaultTip.ForeColor == SystemColors.InfoText,
                    "Contrast tooltip did not use native Windows colors");
                Check(ownedTip.OwnerDraw && ownedTip.BackColor == Color.Lime && ownedTip.ForeColor == Color.Purple,
                    "Contrast policy took over an application-owned tooltip renderer");
            }
            finally
            {
                contextProperty.SetValue(manager, previous);
                foreach (object entry in entries)
                    entry.GetType().GetMethod("RefreshColors", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(entry, null);
            }
            Check(customTip.BackColor == Color.LightPink && customTip.ForeColor == Color.DarkBlue, "Explicit tooltip colors not restored after contrast");
        }

        using (var rootControl = new Form())
        {
            Control[] explicitControls = [new Label { ForeColor = SystemColors.GrayText },
                new Panel { BackColor = SystemColors.ActiveCaption }, new TreeView { BackColor = SystemColors.Control },
                new TextBox { BackColor = SystemColors.ActiveCaption },
                new RichTextBox { BackColor = SystemColors.ControlLight, ForeColor = Color.DarkRed }];
            foreach (Control control in explicitControls)
                rootControl.Controls.Add(control);
            var expected = explicitControls.Select(c => (c.BackColor, c.ForeColor)).ToArray();
            using var registration = manager.Attach(rootControl);
            foreach (ThemeMode mode in new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.Light })
            {
                manager.SetMode(mode);
                if (manager.Context.IsDark)
                {
                    continue;
                }

                for (int i = 0; i < explicitControls.Length; i++)
                    Check(explicitControls[i].BackColor.ToArgb() == expected[i].BackColor.ToArgb() && explicitControls[i].ForeColor.ToArgb() == expected[i].ForeColor.ToArgb(),
                        "Explicit designer colors changed for " + explicitControls[i].GetType().Name);
            }
        }

        using (var rootControl = new Form())
        using (var input = new TextBox { BackColor = Color.Pink, ForeColor = Color.DarkBlue })
        {
            rootControl.Controls.Add(input);
            using var registration = manager.Attach(rootControl);
            using var first = manager.BindColors(input, ThemeColorRole.ErrorBackground, ThemeColorRole.ErrorText);
            using var replacement = manager.BindColors(input, ThemeColorRole.Input);
            Check(input.ForeColor == Color.DarkBlue, "Replacing a binding retained its released foreground role");
            first.Dispose();
            Check(input.BackColor == manager.Palette.Input, "Disposing the superseded binding removed its replacement");
            replacement.Dispose();
            Check(input.BackColor == Color.Pink && input.ForeColor == Color.DarkBlue, "Binding removal lost explicit originals");
            using var backgroundOnly = manager.BindColors(input, ThemeColorRole.Surface);
            input.ForeColor = Color.Green;
            using var newBackgroundOnly = manager.BindColors(input, ThemeColorRole.Input);
            Check(input.ForeColor == Color.Green, "Replacement changed a foreground the binding never owned");
            newBackgroundOnly.Dispose();
            Check(input.ForeColor == Color.Green, "Disposal changed a foreground the binding never owned");
        }

        manager.SetMode(ThemeMode.Light);
        using (var delayedRoot = new Form())
        using (var delayedButton = new Button { Text = "Cached while Light" })
        {
            delayedRoot.Controls.Add(delayedButton);
            delayedButton.GetPreferredSize(Size.Empty);
            FieldInfo adapterField = typeof(ButtonBase).GetField("_adapter", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? cached = adapterField.GetValue(delayedButton);
            Check(cached is not null, "Delayed-attachment test did not cache a managed button painter");
            manager.SetMode(ThemeMode.Dark);
            using var registration = manager.Attach(delayedRoot);
            delayedButton.GetPreferredSize(Size.Empty);
            Check(!ReferenceEquals(cached, adapterField.GetValue(delayedButton)), "Late attachment retained an earlier-mode button painter");
            Check(delayedButton.FlatStyle == FlatStyle.Standard, "Late attachment changed the configured button style");
        }

        Type stateType = typeof(ThemeManager).Assembly.GetType("HenLib.WinFormsTheming.ControlThemeState")!;
        var method = stateType.GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using (var image = new Bitmap(16, 16))
        using (var button = new Button { Text = "Runtime image" })
        {
            object state = Activator.CreateInstance(stateType, button)!;
            var nativeOptions = new ThemeOptions { ButtonRendering = ThemeButtonRendering.NativeTextButtons };
            void ApplyButton() => method.Invoke(state, [manager.Context, manager.Palette, nativeOptions, null, null]);
            ApplyButton();
            Check(button.FlatStyle == FlatStyle.System, "Eligible text button did not use native drawing");
            button.Image = image;
            ApplyButton();
            Check(button.FlatStyle == FlatStyle.Standard, "Adding an image did not restore configured drawing");
            button.Image = null;
            ApplyButton();
            Check(button.FlatStyle == FlatStyle.System, "Removing an image did not restore native text-button eligibility");
            button.BackgroundImage = image;
            ApplyButton();
            Check(button.FlatStyle == FlatStyle.Standard, "Adding a background image did not restore configured drawing");
        }
        manager.SetMode(ThemeMode.Dark);
        using (var paddedLabel = new Label
        {
            Text = "Padded text",
            Padding = new Padding(30, 4, 0, 0),
            BackColor = Color.Black,
            Size = new Size(180, 40),
            Enabled = false
        })
        using (var bitmap = new Bitmap(180, 40))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            typeof(ThemeManager).GetMethod("DisabledLabelPaint", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(manager, [paddedLabel, new PaintEventArgs(graphics, new Rectangle(0, 0, 180, 40))]);
            Check(!Enumerable.Range(0, 30).Any(x => Enumerable.Range(0, 40).Any(y => bitmap.GetPixel(x, y).ToArgb() != Color.Black.ToArgb())),
                "Dark disabled-label repaint ignored left padding");
            Check(Enumerable.Range(30, 150).Any(x => Enumerable.Range(4, 36).Any(y => bitmap.GetPixel(x, y).ToArgb() != Color.Black.ToArgb())),
                "Disabled-label regression did not draw any text");
        }
        using var page = new TabPage { UseVisualStyleBackColor = true };
        object pageState = Activator.CreateInstance(stateType, page)!;
        using var explicitColor = new Panel { BackColor = Color.CornflowerBlue, ForeColor = Color.Red };
        object explicitState = Activator.CreateInstance(stateType, explicitColor)!;
        foreach (ThemeContext context in new[] { new ThemeContext(ThemeMode.Dark, true, false),
            new ThemeContext(ThemeMode.Dark, false, true), new ThemeContext(ThemeMode.Dark, true, false),
            new ThemeContext(ThemeMode.Light, false, false) })
        {
            ThemePalette palette = ThemePalette.Create(context);
            method.Invoke(pageState, [context, palette, new ThemeOptions(), null, null]);
            method.Invoke(explicitState, [context, palette, new ThemeOptions(), null, null]);
            Check(page.UseVisualStyleBackColor == !context.IsDark, "Tab policy incorrect through simulated contrast transition");
            if (!context.IsHighContrast)
            {
                Check(explicitColor.BackColor == Color.CornflowerBlue && explicitColor.ForeColor == Color.Red, "Explicit colors not restored after contrast override");
            }
            else
            {
                Check(explicitColor.BackColor == SystemColors.Control && explicitColor.ForeColor == SystemColors.ControlText, "Contrast colors do not take precedence");
            }
        }
        return log.Finish();
    }
}
