using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace HenLib.WinFormsTheming.Verification;

internal static class SpinnerChecks
{
    private const int ArrowEdgeTolerance = 12;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

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
        string? baselinePath = null;
        int baselineArgument = Array.IndexOf(args, "--baseline");
        if (baselineArgument >= 0)
        {
            baselinePath = args[baselineArgument + 1];
        }
        var fingerprints = new Dictionary<string, string>();
        if (baselinePath is not null && File.Exists(baselinePath))
        {
            fingerprints = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(baselinePath))!;
        }
        bool customPalette = false;
        using var manager = new ThemeManager(startup, new ThemeOptions
        {
            PaletteFactory = context =>
            {
                ThemePalette palette = ThemePalette.Create(context);
                if (customPalette)
                {
                    return palette with { Input = Color.DarkSlateBlue, Text = Color.Yellow, DisabledText = Color.LightGray };
                }
                return palette;
            }
        });
        using var form = new SpinnerFixtureForm(manager);
        var log = new CheckLog("Spinners " + startup);
        var diagnostics = new List<string>();
        manager.Diagnostic += (_, diagnostic) => diagnostics.Add(diagnostic.Operation + ": " + diagnostic.ErrorCode);
        var originals = form.Samples.ToDictionary(sample => sample,
            sample => (sample.Text, sample.ReadOnly, sample.Enabled, sample.RightToLeft, sample.UpDownAlign, sample.BackColor, sample.ForeColor,
                ExplicitBack: Serializes(sample, nameof(Control.BackColor)), ExplicitFore: Serializes(sample, nameof(Control.ForeColor))));
        using var registration = manager.Register(form);
        var handles = new Dictionary<Control, nint>();
        int valueChanges = 0;
        bool checkSelection = true;
        var accessibleValues = new Dictionary<Control, string?>();
        foreach (NumericUpDown numeric in form.Samples.OfType<NumericUpDown>())
        {
            numeric.ValueChanged += (_, _) => valueChanges++;
        }

        async Task Sample(string stage, bool compare = true)
        {
            await Task.Delay(70);
            form.ActiveControl = null;
            foreach (UpDownBase sample in form.Samples)
            {
                var original = originals[sample];
                log.Check(sample.Text == original.Text && sample.ReadOnly == original.ReadOnly && sample.Enabled == original.Enabled,
                    stage + ": value/read-only/enabled state changed for " + sample.Name);
                log.Check(sample.RightToLeft == original.RightToLeft && sample.UpDownAlign == original.UpDownAlign,
                    stage + ": layout changed for " + sample.Name);
                if (!sample.Visible)
                {
                    continue;
                }
                TextBox edit = Editor(sample);
                if (checkSelection && sample.Enabled)
                {
                    log.Check(edit.SelectionStart == 1 && edit.SelectionLength == 2, stage + ": text selection changed for " + sample.Name);
                }
                string? accessibleValue = sample.AccessibilityObject.Value;
                if (accessibleValues.TryGetValue(sample, out string? previousValue))
                {
                    log.Check(accessibleValue == previousValue, stage + ": accessible value changed for " + sample.Name);
                }
                else
                {
                    accessibleValues[sample] = accessibleValue;
                }
                foreach (Control part in sample.Controls.Cast<Control>().Prepend(sample))
                {
                    if (handles.TryGetValue(part, out nint handle))
                    {
                        log.Check(part.Handle == handle, stage + ": handle changed for " + sample.Name + "/" + part.GetType().Name);
                    }
                    else
                    {
                        handles[part] = part.Handle;
                    }
                    log.Check(IsWindowEnabled(part.Handle) == sample.Enabled,
                        stage + ": native enabled state changed for " + sample.Name);
                }
                using var image = Capture(edit);
                Color expectedBack = edit.BackColor;
                if (!sample.Enabled && !manager.Context.IsDark &&
                    !TypeDescriptor.GetProperties(edit)[nameof(Control.BackColor)]!.ShouldSerializeValue(edit))
                {
                    expectedBack = SystemColors.Control;
                }
                log.Check(image.GetPixel(image.Width - 3, image.Height - 3).ToArgb() == expectedBack.ToArgb(),
                    stage + ": stale editor background for " + sample.Name);
                Color text = edit.ForeColor;
                if (!sample.Enabled && !manager.Context.IsDark)
                {
                    text = ColorTranslator.FromWin32(GetSysColor(17));
                }
                if (manager.Context.IsDark && !sample.Enabled)
                {
                    text = DisabledForeground(manager.Palette, edit.BackColor);
                }
                log.Check(HasColor(image, text), stage + ": unreadable editor text for " + sample.Name);
                if (sample.Name.Contains("fixed colors"))
                {
                    log.Check(sample.BackColor == Color.LightPink && edit.BackColor == Color.LightPink &&
                        sample.ForeColor == Color.DarkBlue && edit.ForeColor == Color.DarkBlue,
                        stage + ": explicit spinner colors were overwritten");
                }
                if (sample.Name.Contains("system background"))
                {
                    log.Check(sample.BackColor == SystemColors.ActiveCaption && edit.BackColor == SystemColors.ActiveCaption,
                        stage + ": symbolic system color was lost");
                }
                if (compare)
                {
                    using var whole = Capture(sample);
                    CompareFingerprint(sample, manager.Context.IsDark, whole, fingerprints, log, stage);
                }
            }
        }
        form.Shown += async (_, _) =>
        {
            try
            {
                foreach (UpDownBase sample in form.Samples)
                {
                    Editor(sample).Select(1, 2);
                }
                await Sample("Startup");
                foreach (ThemeMode mode in new[] { opposite, startup, opposite, startup })
                {
                    manager.SetMode(mode);
                    await Sample("Switched " + mode);
                }
                manager.Refresh();
                manager.SetMode(startup);
                await Sample("Redundant requests");
                log.Check(valueChanges == 0, "Theme changes raised numeric ValueChanged");
                form.Pages.SelectedIndex = 1;
                await Sample("Initially hidden tab");
                manager.SetMode(opposite);
                await Sample("Hidden tab switched");
                form.Pages.SelectedIndex = 0;
                form.SamplePanel.Hide();
                manager.SetMode(startup);
                form.SamplePanel.Show();
                await Sample("Sample panel reshown");
                form.Hide();
                manager.SetMode(opposite);
                form.Show();
                await Sample("Form reshown");
                checkSelection = false;
                foreach (UpDownBase sample in form.Samples.Where(sample => sample.Enabled && sample.Visible))
                {
                    CheckArrows(sample, log);
                }
                await Sample("Arrow interaction restored");
                CheckChildBindings(form.Samples[0], manager, log);
                CheckChildBindings(form.Samples[1], manager, log);
                UpDownBase primary = form.Samples[0];
                using (manager.BindColors(primary, ThemeColorRole.WarningBackground, ThemeColorRole.ErrorText))
                {
                    log.Check(primary.BackColor == manager.Palette.WarningBackground && Editor(primary).BackColor == manager.Palette.WarningBackground,
                        "Spinner background binding was overwritten by its internal editor");
                    log.Check(primary.ForeColor == manager.Palette.ErrorText && Editor(primary).ForeColor == manager.Palette.ErrorText,
                        "Spinner foreground binding was overwritten by its internal editor");
                    manager.SetMode(startup);
                    log.Check(primary.BackColor == manager.Palette.WarningBackground && primary.ForeColor == manager.Palette.ErrorText,
                        "Spinner binding did not survive a theme change");
                }
                customPalette = true;
                manager.Refresh();
                log.Check(primary.BackColor == Color.DarkSlateBlue && primary.ForeColor == Color.Yellow,
                    "Default spinner colors did not use the custom palette");
                log.Check(Editor(primary).BackColor == primary.BackColor && Editor(primary).ForeColor == primary.ForeColor,
                    "Custom palette disagrees with the internal editor");
                customPalette = false;
                manager.Refresh();
                CheckKeyboard(primary, log);
                CheckContrast(form.Samples.Single(sample => sample.Name == "Numeric / fixed colors"), manager, log);
                primary.Enabled = false;
                manager.SetMode(ThemeMode.Dark);
                using (var disabled = Capture(Editor(primary)))
                {
                    log.Check(HasColor(disabled, DisabledForeground(manager.Palette, primary.BackColor)),
                        "Runtime disabling did not use readable text");
                }
                primary.Enabled = true;
                manager.Refresh();
                typeof(Control).GetMethod("RecreateHandle", Hidden)!.Invoke(primary, null);
                foreach (Control part in primary.Controls.Cast<Control>().Prepend(primary))
                {
                    handles.Remove(part);
                }
                await Sample("Recreated spinner", compare: false);
                int beforeDynamic = manager.RegisteredControlCount;
                using (var late = SpinnerFixtureForm.Numeric())
                {
                    late.Enabled = false;
                    late.Location = new Point(10, 120);
                    form.Pages.TabPages[1].Controls.Add(late);
                    form.Pages.SelectedIndex = 1;
                    await Task.Delay(80);
                    using (var image = Capture(Editor(late)))
                    {
                        log.Check(HasColor(image, DisabledForeground(manager.Palette, late.BackColor)),
                            "Dynamically added disabled spinner has unreadable text");
                    }
                    form.Pages.TabPages[0].Controls.Add(late);
                    form.Pages.SelectedIndex = 0;
                    await Task.Delay(80);
                    using var moved = Capture(Editor(late));
                    log.Check(HasColor(moved, DisabledForeground(manager.Palette, late.BackColor)),
                        "Reparenting lost disabled spinner text rendering");
                }
                log.Check(manager.RegisteredControlCount == beforeDynamic, "Dynamic spinner disposal retained registrations");
                registration.Dispose();
                foreach (UpDownBase sample in form.Samples)
                {
                    var original = originals[sample];
                    bool restoredBack = !Serializes(sample, nameof(Control.BackColor));
                    if (original.ExplicitBack)
                    {
                        restoredBack = sample.BackColor == original.BackColor;
                    }
                    bool restoredFore = !Serializes(sample, nameof(Control.ForeColor));
                    if (original.ExplicitFore)
                    {
                        restoredFore = sample.ForeColor == original.ForeColor;
                    }
                    log.Check(restoredBack && restoredFore, "Detach did not restore spinner color ownership for " + sample.Name);
                    log.Check(RendererFor(Editor(sample), manager) is null, "Detach retained a spinner edit subclass");
                }
                using (manager.Register(form))
                {
                    await Sample("Reattached", compare: false);
                }
                log.Check(diagnostics.Count == 0, "Unexpected native diagnostics: " + string.Join("; ", diagnostics));
                if (baselinePath is not null)
                {
                    File.WriteAllText(baselinePath, JsonSerializer.Serialize(fingerprints));
                }
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

    private static void CheckChildBindings(UpDownBase spinner, ThemeManager manager, CheckLog log)
    {
        TextBox edit = Editor(spinner);
        using (manager.BindColors(spinner, ThemeColorRole.WarningBackground, ThemeColorRole.ErrorText))
        {
            using (manager.BindColors(edit, ThemeColorRole.Input, ThemeColorRole.WarningText))
            {
                log.Check(edit.BackColor == manager.Palette.Input && edit.ForeColor == manager.Palette.WarningText,
                    spinner.Name + ": direct editor binding was ignored");
                Control arrows = spinner.Controls.Cast<Control>().Single(part => part is not TextBox);
                using (manager.BindColors(arrows, ThemeColorRole.Selection))
                {
                    log.Check(arrows.BackColor == manager.Palette.Selection, spinner.Name + ": arrow binding was ignored");
                }
                log.Check(edit.BackColor == manager.Palette.Input && edit.ForeColor == manager.Palette.WarningText,
                    spinner.Name + ": releasing an arrow binding overwrote a separate editor binding");
            }
            log.Check(spinner.BackColor == manager.Palette.WarningBackground && spinner.ForeColor == manager.Palette.ErrorText,
                spinner.Name + ": disposing editor bindings lost the spinner's active palette");
            using var first = manager.BindColors(edit, ThemeColorRole.Input, ThemeColorRole.WarningText);
            using (manager.BindColors(edit, foreground: ThemeColorRole.SecondaryText))
            {
                log.Check(edit.BackColor == manager.Palette.WarningBackground && edit.ForeColor == manager.Palette.SecondaryText,
                    spinner.Name + ": replacing an editor binding did not restore the unbound spinner background");
                first.Dispose();
                log.Check(edit.BackColor == manager.Palette.WarningBackground && edit.ForeColor == manager.Palette.SecondaryText,
                    spinner.Name + ": a superseded binding affected its replacement");
            }
            log.Check(spinner.BackColor == manager.Palette.WarningBackground && spinner.ForeColor == manager.Palette.ErrorText,
                spinner.Name + ": replacement cleanup lost spinner-owned colors");
        }
    }
    private static void CheckKeyboard(UpDownBase spinner, CheckLog log)
    {
        var numeric = (NumericUpDown)spinner;
        decimal value = numeric.Value;
        TextBox edit = Editor(spinner);
        MethodInfo keyDown = typeof(Control).GetMethod("OnKeyDown", Hidden)!;
        keyDown.Invoke(edit, [new KeyEventArgs(Keys.Up)]);
        log.Check(numeric.Value == value + numeric.Increment, "Keyboard Up stopped changing numeric values");
        keyDown.Invoke(edit, [new KeyEventArgs(Keys.Down)]);
        log.Check(numeric.Value == value && numeric.Minimum == -100 && numeric.Maximum == 1000 && numeric.DecimalPlaces == 2,
            "Keyboard Down, numeric range, or decimal formatting changed");
    }

    private static void CheckContrast(UpDownBase spinner, ThemeManager manager, CheckLog log)
    {
        Type stateType = typeof(ThemeManager).Assembly.GetType("HenLib.WinFormsTheming.ControlThemeState")!;
        object state = Activator.CreateInstance(stateType, [spinner, null])!;
        MethodInfo apply = stateType.GetMethod("Apply", Hidden)!;
        var contrast = manager.Context with { IsDark = false, IsHighContrast = true };
        apply.Invoke(state, [contrast, ThemePalette.Create(contrast), new ThemeOptions(), null, null]);
        log.Check(spinner.BackColor == SystemColors.Window && spinner.ForeColor == SystemColors.WindowText,
            "Simulated contrast did not override explicit spinner colors");
        log.Check(Editor(spinner).BackColor == spinner.BackColor && Editor(spinner).ForeColor == spinner.ForeColor,
            "Simulated contrast did not reach the spinner editor");
        apply.Invoke(state, [manager.Context, manager.Palette, new ThemeOptions(), null, null]);
        log.Check(spinner.BackColor == Color.LightPink && spinner.ForeColor == Color.DarkBlue,
            "Leaving simulated contrast did not restore explicit spinner colors");
    }
    private static bool Serializes(Control control, string property)
    {
        return TypeDescriptor.GetProperties(control)[property]!.ShouldSerializeValue(control);
    }

    private static object? RendererFor(Control control, ThemeManager manager)
    {
        var states = (System.Collections.IDictionary)typeof(ThemeManager).GetField("controls", Hidden)!.GetValue(manager)!;
        object? state = states[control];
        return state?.GetType().GetField("disabledTextBox", Hidden)!.GetValue(state);
    }

    internal static TextBox Editor(UpDownBase sample) => sample.Controls.OfType<TextBox>().Single();

    private static void CheckArrows(UpDownBase sample, CheckLog log)
    {
        Control buttons = sample.Controls.Cast<Control>().Single(part => part is not TextBox);
        string text = sample.Text;
        decimal numericValue = 0;
        int domainIndex = 0;
        if (sample is NumericUpDown numeric)
        {
            numericValue = numeric.Value;
        }
        if (sample is DomainUpDown domain)
        {
            domainIndex = domain.SelectedIndex;
        }
        using var normal = Capture(buttons);
        var mouse = new MouseEventArgs(MouseButtons.None, 0, buttons.Width / 2, buttons.Height / 4, 0);
        buttons.GetType().GetMethod("OnMouseMove", Hidden)!.Invoke(buttons, [mouse]);
        using var hot = Capture(buttons);
        var down = new MouseEventArgs(MouseButtons.Left, 1, mouse.X, mouse.Y, 0);
        try
        {
            buttons.GetType().GetMethod("OnMouseDown", Hidden)!.Invoke(buttons, [down]);
            using var pressed = Capture(buttons);
            log.Check(Fingerprint(normal) != Fingerprint(hot), sample.Name + ": arrow hover was not rendered");
            log.Check(Fingerprint(hot) != Fingerprint(pressed), sample.Name + ": pressed arrow was not rendered");
            log.Check(sample.Text != text, sample.Name + ": arrow did not change its value (including read-only editors)");
        }
        finally
        {
            buttons.GetType().GetMethod("OnMouseUp", Hidden)!.Invoke(buttons, [down]);
            buttons.GetType().GetMethod("OnMouseLeave", Hidden)!.Invoke(buttons, [EventArgs.Empty]);
            if (sample is NumericUpDown number)
            {
                number.Value = numericValue;
            }
            if (sample is DomainUpDown choices)
            {
                choices.SelectedIndex = domainIndex;
            }
        }
    }

    private static Bitmap Capture(Control control)
    {
        var image = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        return image;
    }

    private static void CompareFingerprint(UpDownBase sample, bool dark, Bitmap image,
        Dictionary<string, string> fingerprints, CheckLog log, string stage)
    {
        string name = sample.Name;
        string key = name + ":" + dark + ":" + image.Width + "x" + image.Height;
        byte[] actual = PixelBytes(image);
        if (fingerprints.TryGetValue(key, out string? expected))
        {
            byte[] reference = Convert.FromBase64String(expected);
            Control buttons = sample.Controls.Cast<Control>().Single(part => part is not TextBox);
            log.Check(SameAppearance(reference, actual, image.Width, buttons.Bounds), stage + ": rendered editor/arrows/border differ from the same-mode reference for " + name);
        }
        else
        {
            fingerprints[key] = Convert.ToBase64String(actual);
        }
    }

    private static string Fingerprint(Bitmap image) => Convert.ToHexString(SHA256.HashData(PixelBytes(image)));

    private static byte[] PixelBytes(Bitmap image)
    {
        var rect = new Rectangle(Point.Empty, image.Size);
        var data = image.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            byte[] bytes = new byte[Math.Abs(data.Stride) * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            // Native GDI does not define alpha. Compare the visible RGB channels.
            for (int index = 3; index < bytes.Length; index += 4)
            {
                bytes[index] = 255;
            }
            return bytes;
        }
        finally
        {
            image.UnlockBits(data);
        }
    }

    private static bool SameAppearance(byte[] expected, byte[] actual, int width, Rectangle arrows)
    {
        if (expected.Length != actual.Length)
        {
            return false;
        }
        for (int index = 0; index < expected.Length; index++)
        {
            int difference = Math.Abs(expected[index] - actual[index]);
            if (difference == 0)
            {
                continue;
            }
            int x = index / 4 % width - arrows.Left;
            int y = index / 4 / width - arrows.Top;
            bool inButtons = x >= 0 && x < arrows.Width && y >= 0 && y < arrows.Height;
            bool verticalEdge = x < 2 || x >= arrows.Width - 2;
            bool horizontalEdge = y < 2 || Math.Abs(y - arrows.Height / 2) < 2 || y >= arrows.Height - 2;
            // Native cached-arrow antialiasing can settle by a few levels along its edges.
            // All text, backgrounds, borders, and arrow interiors must match exactly.
            if (!inButtons || !(verticalEdge || horizontalEdge) || difference > ArrowEdgeTolerance)
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasColor(Bitmap image, Color expected)
    {
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).ToArgb() == expected.ToArgb())
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static Color DisabledForeground(ThemePalette palette, Color background)
    {
        Type renderer = typeof(ThemeManager).Assembly.GetType("HenLib.WinFormsTheming.ThemedDisabledTextBoxWindow")!;
        MethodInfo resolve = renderer.GetMethod("ResolveForeground", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? result = resolve.Invoke(null, [palette, background]);
        return (Color)result!;
    }

    [DllImport("user32.dll")]
    private static extern int GetSysColor(int index);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(nint window);
}