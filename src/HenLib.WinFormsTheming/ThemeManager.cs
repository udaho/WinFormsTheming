using System.Drawing;
using Microsoft.Win32;

namespace HenLib.WinFormsTheming;

/// <summary>
/// One application-scoped manager, created on the STA UI thread before forms.
/// Attach roots; keep the returned registrations alive and dispose them to detach.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private static ThemeManager? activeManager;
    private readonly int uiThread = Environment.CurrentManagedThreadId;
    private readonly ThemeOptions options;
    private readonly Control dispatcher;
    private readonly ProgressBarAnimationController? progressBars;
    private readonly Dictionary<Control, int> roots = new();
    private readonly Dictionary<Control, ControlThemeState> controls = new();
    private readonly Dictionary<Control, ColorBinding> bindings = new();
    private readonly Dictionary<ToolStripDropDownItem, EventHandler> menuOpenHandlers = new();
    private readonly HashSet<ThemeRegistration> registrations = new();
    private readonly List<IThemeAdapter> adapters = new();
    private readonly List<ThemedToolTipRegistration> toolTips = new();
    private volatile bool applying, reconciling, disposed;
    private bool pendingForce;
    private ThemeMode? pendingMode;
    private int refreshQueued;
    private int refreshRequested;
    private SystemColorMode? nativeMode;

    public ThemeManager(ThemeMode mode = ThemeMode.System, ThemeOptions? options = null)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("Create ThemeManager on the application's STA UI thread.");
        }

        if (Interlocked.CompareExchange(ref activeManager, this, null) is not null)
        {
            throw new InvalidOperationException("WinForms color mode is application-wide; use one ThemeManager per application.");
        }

        this.options = options ?? new ThemeOptions();
        if (!Enum.IsDefined(this.options.ButtonRendering) || !Enum.IsDefined(this.options.ProgressBarRendering))
        {
            activeManager = null;
            throw new ArgumentOutOfRangeException(nameof(options));
        }
        RequestedMode = mode;
        try
        {
            // Resolve the palette before even our hidden message-dispatch control is constructed.
            UpdateApplicationPalette();
            dispatcher = new Control();
            dispatcher.CreateControl();
            if (this.options.ProgressBarRendering == ThemeProgressBarRendering.Animated)
            {
                progressBars = new ProgressBarAnimationController(this, this.options.AnimateProgressBars);
            }
            SystemEvents.UserPreferenceChanged += WindowsPreferencesChanged;
        }
        catch
        {
            progressBars?.Dispose();
            dispatcher?.Dispose();
            Interlocked.CompareExchange(ref activeManager, null, this);
            throw;
        }
    }

    public ThemeMode RequestedMode
    {
        get; private set;
    }
    public static ThemeManager? Current => activeManager;
    public ThemeContext Context
    {
        get; private set;
    }
    public ThemePalette Palette { get; private set; } = null!;
    public long Revision
    {
        get; private set;
    }
    public int RegisteredControlCount => controls.Count;
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
    public event EventHandler<ThemeDiagnosticEventArgs>? Diagnostic;

    internal void ForgetToolTip(ThemedToolTipRegistration registration) => toolTips.Remove(registration);

    /// <summary>Capture configured defaults now; Activate later after application initialization.</summary>
    public ThemeRegistration Prepare(Control root, Action<ThemeRegistration>? configure = null)
    {
        VerifyRegistrationActivation();
        ArgumentNullException.ThrowIfNull(root);
        ObjectDisposedException.ThrowIf(root.IsDisposed, root);
        var registration = new ThemeRegistration(this, root);
        registrations.Add(registration);
        try
        {
            configure?.Invoke(registration);
            return registration;
        }
        catch (Exception error)
        {
            try
            {
                registration.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Theme configuration and cleanup failed.", error, cleanup);
            }
            throw;
        }
    }

    /// <summary>Capture defaults, attach resources, and run initial presentation in one call.</summary>
    public ThemeRegistration Register(Control root, Action<ThemeRegistration>? configure = null)
    {
        var registration = Prepare(root, configure);
        registration.Activate();
        return registration;
    }

    internal void ForgetRegistration(ThemeRegistration registration) => registrations.Remove(registration);

    internal Dictionary<Control, ThemeControlDefaults> CaptureDefaults(Control root)
    {
        VerifyThread();
        var reachable = new HashSet<Control>();
        Discover(root, reachable, new HashSet<ToolStripDropDownItem>());
        var snapshots = new Dictionary<Control, ThemeControlDefaults>();
        foreach (Control control in reachable)
        {
            if (controls.TryGetValue(control, out var state))
            {
                snapshots.Add(control, state.Defaults);
            }
            else
            {
                snapshots.Add(control, new ThemeControlDefaults(control));
            }
        }
        return snapshots;
    }

    public IDisposable Attach(Control root) => Attach(root, null);

    internal IDisposable Attach(Control root, IReadOnlyDictionary<Control, ThemeControlDefaults>? defaults)
    {
        VerifyThread();
        ArgumentNullException.ThrowIfNull(root);
        ObjectDisposedException.ThrowIf(root.IsDisposed, root);
        roots[root] = roots.GetValueOrDefault(root) + 1;
        try
        {
            Reconcile(defaults);
        }
        catch (Exception error)
        {
            if (roots.TryGetValue(root, out int count))
            {
                if (count <= 1)
                {
                    roots.Remove(root);
                }
                else
                {
                    roots[root] = count - 1;
                }
            }
            try
            {
                Reconcile();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Theme attachment and rollback failed.", error, cleanup);
            }
            throw;
        }
        return new Registration(() => Detach(root));
    }

    public IDisposable BindColors(Control control, ThemeColorRole? background = null, ThemeColorRole? foreground = null)
    {
        VerifyThread();
        if (!controls.ContainsKey(control))
        {
            throw new ArgumentException("Attach the control's root before binding colors.", nameof(control));
        }

        if (background is ThemeColorRole back && !Enum.IsDefined(back) || foreground is ThemeColorRole fore && !Enum.IsDefined(fore))
        {
            throw new ArgumentOutOfRangeException(nameof(background));
        }

        var binding = new ColorBinding(background, foreground);
        // A replacement can release one of the previously bound properties.
        // Restore its designer/default value before applying the new roles.
        if (bindings.TryGetValue(control, out var previous))
        {
            if (previous.Background.HasValue && !background.HasValue)
            {
                controls[control].RestoreBackground();
            }
            if (previous.Foreground.HasValue && !foreground.HasValue)
            {
                controls[control].RestoreForeground();
            }
        }
        bindings[control] = binding;
        ApplyControl(controls[control], refreshPainter: false);

        void RemoveBinding()
        {
            if (disposed)
            {
                return;
            }

            VerifyThread();
            if (!bindings.TryGetValue(control, out var current) || !ReferenceEquals(current, binding))
            {
                return;
            }

            bindings.Remove(control);
            if (controls.TryGetValue(control, out var state))
            {
                if (binding.Background.HasValue)
                {
                    state.RestoreBackground();
                }
                if (binding.Foreground.HasValue)
                {
                    state.RestoreForeground();
                }
                ApplyControl(state, refreshPainter: false);
            }
        }

        return new Registration(RemoveBinding);
    }

    public IDisposable AddAdapter(IThemeAdapter adapter)
    {
        VerifyThread();
        ArgumentNullException.ThrowIfNull(adapter);
        if (adapters.Contains(adapter))
        {
            throw new ArgumentException("This adapter is already registered.", nameof(adapter));
        }

        adapters.Add(adapter);
        try
        {
            Refresh();
        }
        catch (Exception error)
        {
            try
            {
                RemoveAdapter(adapter);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Adapter attachment and rollback failed.", error, cleanup);
            }
            throw;
        }
        return new Registration(() =>
        {
            if (disposed)
            {
                return;
            }

            VerifyThread();
            RemoveAdapter(adapter);
        });
    }

    private void RemoveAdapter(IThemeAdapter adapter)
    {
        if (!adapters.Remove(adapter))
        {
            return;
        }
        var errors = new List<Exception>();
        foreach (Control control in controls.Keys.ToArray())
        {
            TryCleanup(() =>
            {
                if (!control.IsDisposed && adapter.CanHandle(control))
                {
                    adapter.Detach(control);
                }
            }, errors);
        }
        TryCleanup(Refresh, errors);
        if (errors.Count > 0)
        {
            throw new AggregateException("Adapter cleanup failed.", errors);
        }
    }

    public IDisposable AttachToolTip(ToolTip toolTip)
    {
        VerifyThread();
        ArgumentNullException.ThrowIfNull(toolTip);
        if (toolTips.Any(t => ReferenceEquals(t.ToolTip, toolTip)))
        {
            throw new ArgumentException("This tooltip is already attached.", nameof(toolTip));
        }

        var registration = new ThemedToolTipRegistration(this, toolTip);
        toolTips.Add(registration);
        return new Registration(() =>
        {
            if (disposed)
            {
                return;
            }

            VerifyThread();
            toolTips.Remove(registration);
            registration.Dispose();
        });
    }

    public void SetMode(ThemeMode mode)
    {
        VerifyThread();
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (applying || reconciling)
        {
            pendingMode = mode;
            QueueRefresh();
            return;
        }
        if (RequestedMode == mode)
        {
            return;
        }

        RequestedMode = mode;
        ApplyTheme(force: false);
    }

    /// <summary>Re-resolves Windows settings and invalidates palette-dependent app styling.</summary>
    public void Refresh()
    {
        VerifyThread();
        if (applying || reconciling)
        {
            pendingForce = true;
            QueueRefresh();
            return;
        }
        ApplyTheme(force: true);
    }

    private void ApplyTheme(bool force)
    {
        applying = true;
        try
        {
            ThemeContext previous = Context;
            ThemePalette previousPalette = Palette;
            UpdateApplicationPalette();
            bool renderChanged = force || previous.IsDark != Context.IsDark ||
                previous.IsHighContrast != Context.IsHighContrast || previousPalette != Palette;
            if (!renderChanged && previous.RequestedMode == Context.RequestedMode)
            {
                return;
            }

            if (renderChanged)
            {
                Revision++;
                foreach (var state in controls.Values.ToArray())
                {
                    ApplyControl(state, refreshPainter: true);
                }

                foreach (var toolTip in toolTips.ToArray())
                {
                    toolTip.RefreshColors();
                }
            }
            ThemeChanged?.Invoke(this, new(Context, Palette, Revision));
        }
        finally
        {
            applying = false;
            ScheduleQueuedRefresh();
        }
    }

    private void UpdateApplicationPalette()
    {
        bool highContrast = SystemInformation.HighContrast;
        bool supportsDark = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        bool dark = !highContrast && supportsDark && (RequestedMode == ThemeMode.Dark ||
            RequestedMode == ThemeMode.System && ReadWindowsDarkMode());
        SystemColorMode desired = dark ? SystemColorMode.Dark : SystemColorMode.Classic;
        if (nativeMode != desired)
        {
            Application.SetColorMode(desired);
            nativeMode = desired;
        }
        Context = new(RequestedMode, Application.IsDarkModeEnabled && !highContrast, highContrast);
        // Windows contrast colors take precedence over application palette factories.
        if (highContrast)
        {
            Palette = ThemePalette.Create(Context);
        }
        else
        {
            Palette = options.PaletteFactory?.Invoke(Context) ?? ThemePalette.Create(Context);
        }
        progressBars?.RefreshPolicy();
    }

    private static bool ReadWindowsDarkMode()
    {
        try
        {
            const string personalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            object? preference = Registry.GetValue(personalizeKey, "AppsUseLightTheme", 1);
            return preference is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            System.Diagnostics.Debug.WriteLine($"Unable to read Windows app theme preference: {ex.ToString()}");
            return false;
        }
    }

    private void WindowsPreferencesChanged(object sender, UserPreferenceChangedEventArgs e) => QueueRefresh();

    private void QueueRefresh()
    {
        if (disposed)
        {
            return;
        }

        Interlocked.Exchange(ref refreshRequested, 1);
        ScheduleQueuedRefresh();
    }

    private void ScheduleQueuedRefresh()
    {
        // SetColorMode can pump posted callbacks before returning. Do not re-post
        // while busy: that can keep its nested message pump alive indefinitely.
        if (disposed || applying || reconciling || Volatile.Read(ref refreshRequested) == 0 ||
            Interlocked.Exchange(ref refreshQueued, 1) != 0)
        {
            return;
        }

        try
        {
            dispatcher.BeginInvoke((Action)(() =>
            {
                Interlocked.Exchange(ref refreshQueued, 0);
                if (disposed)
                {
                    return;
                }

                if (applying || reconciling)
                {
                    return;
                }

                Interlocked.Exchange(ref refreshRequested, 0);
                ThemeMode? mode = pendingMode;
                bool force = pendingForce;
                pendingMode = null;
                pendingForce = false;
                if (mode is ThemeMode value)
                {
                    RequestedMode = value;
                }

                Reconcile();
                ApplyTheme(force || mode is null);
            }));
        }
        catch (InvalidOperationException) when (disposed || dispatcher.IsDisposed)
        {
            Interlocked.Exchange(ref refreshQueued, 0);
        }
    }

    private void Reconcile(IReadOnlyDictionary<Control, ThemeControlDefaults>? defaults = null)
    {
        if (applying || reconciling)
        {
            QueueRefresh();
            return;
        }
        reconciling = true;
        try
        {
            var reachable = new HashSet<Control>();
            var menuItems = new HashSet<ToolStripDropDownItem>();
            foreach (Control root in roots.Keys.ToArray())
            {
                Discover(root, reachable, menuItems);
            }

            var added = new List<ControlThemeState>();
            // Capture all original properties before changing any parent color.
            foreach (Control control in reachable)
            {
                if (controls.ContainsKey(control))
                {
                    continue;
                }

                ThemeControlDefaults? captured = null;
                defaults?.TryGetValue(control, out captured);
                var state = new ControlThemeState(control, captured);
                controls.Add(control, state);
                added.Add(state);
                Subscribe(control);
            }
            foreach (Control control in controls.Keys.Where(c => !reachable.Contains(c)).ToArray())
            {
                RemoveControl(control, restore: true);
            }

            foreach (var item in menuOpenHandlers.Keys.Where(i => !menuItems.Contains(i)).ToArray())
            {
                item.DropDownOpening -= menuOpenHandlers[item];
                menuOpenHandlers.Remove(item);
            }
            foreach (var item in menuItems)
            {
                if (menuOpenHandlers.ContainsKey(item))
                {
                    continue;
                }

                EventHandler handler = (_, _) => Reconcile();
                item.DropDownOpening += handler;
                menuOpenHandlers.Add(item, handler);
            }
            // A newly attached control may already have a painter cached under
            // an earlier mode, even though its root was not registered then.
            foreach (var state in added)
            {
                ApplyControl(state, refreshPainter: true);
            }
        }
        finally
        {
            reconciling = false;
            ScheduleQueuedRefresh();
        }
    }

    private void Discover(Control control, HashSet<Control> reachable, HashSet<ToolStripDropDownItem> items)
    {
        if (control.IsDisposed || !reachable.Add(control))
        {
            return;
        }

        if (options.ExcludeControl?.Invoke(control) == true)
        {
            reachable.Remove(control);
            return;
        }
        foreach (Control child in control.Controls)
        {
            Discover(child, reachable, items);
        }

        if (control.ContextMenuStrip is { } menu)
        {
            Discover(menu, reachable, items);
        }

        if (control is ToolStrip strip)
        {
            foreach (ToolStripDropDownItem item in strip.Items.OfType<ToolStripDropDownItem>())
            {
                items.Add(item);
                if (item.HasDropDownItems)
                {
                    Discover(item.DropDown, reachable, items);
                }
            }
        }
    }

    private void Subscribe(Control control)
    {
        control.HandleCreated += HandleCreated;
        control.ControlAdded += ControlTreeChanged;
        control.ControlRemoved += ControlTreeChanged;
        control.ContextMenuStripChanged += ContextMenuChanged;
        control.Disposed += ControlDisposed;
        if (control is ProgressBar progress)
        {
            progressBars?.Attach(progress);
        }
        if (control.GetType() == typeof(Label))
        {
            control.Paint += DisabledLabelPaint;
        }

        if (control is ToolStrip strip)
        {
            strip.ItemAdded += ToolStripChanged;
            strip.ItemRemoved += ToolStripChanged;
        }
    }

    private void HandleCreated(object? sender, EventArgs e)
    {
        if (disposed || applying || reconciling || sender is not Control control || !controls.TryGetValue(control, out var state))
        {
            return;
        }
        // Progress registrations attach to each replacement HWND without changing the control.
        if (control is ProgressBar)
        {
            progressBars?.Refresh((ProgressBar)control);
            state.LastNativeDark = Context.IsDark;
            state.LastNativeHandle = control.Handle;
            return;
        }
        control.BeginInvoke((Action)(() =>
        {
            if (disposed || control.IsDisposed || !controls.TryGetValue(control, out var current) || !ReferenceEquals(current, state))
            {
                return;
            }

            if (applying || reconciling)
            {
                QueueRefresh();
                return;
            }
            applying = true;
            try
            {
                ApplyControl(state, refreshPainter: false);
            }
            finally
            {
                applying = false;
                ScheduleQueuedRefresh();
            }
        }));
    }

    private void ApplyControl(ControlThemeState state, bool refreshPainter)
    {
        Control control = state.Control;
        if (control.IsDisposed || control.Disposing)
        {
            return;
        }

        bindings.TryGetValue(control, out var binding);
        state.Apply(Context, Palette, options, binding?.Background, binding?.Foreground);
        if (control.IsHandleCreated)
        {
            bool progressTheme = state.LastNativeDark != Context.IsDark || state.LastNativeHandle != control.Handle;
            if (control is ProgressBar progress && progressBars is not null)
            {
                progressBars.Refresh(progress);
            }
            else
            {
                NativeControlRenderer.Apply(control, Context, ReportDiagnostic, refreshPainter, progressTheme);
            }
            state.LastNativeDark = Context.IsDark;
            state.LastNativeHandle = control.Handle;
        }
        else if (refreshPainter && control is ButtonBase button)
        {
            NativeControlRenderer.RefreshButtonPainter(button);
        }

        foreach (var adapter in adapters.ToArray())
        {
            if (adapter.CanHandle(control))
            {
                adapter.Apply(control, Context, Palette);
            }
        }

        control.Invalidate();
    }

    private void DisabledLabelPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not Label label || label.Enabled || !Context.IsDark || Context.IsHighContrast)
        {
            return;
        }

        TextFormatFlags flags = TextFormatFlags.WordBreak;
        if (!label.UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }

        if (label.AutoEllipsis)
        {
            flags |= TextFormatFlags.EndEllipsis;
        }

        if (label.RightToLeft == RightToLeft.Yes)
        {
            flags |= TextFormatFlags.RightToLeft;
        }

        flags |= label.TextAlign switch
        {
            ContentAlignment.TopCenter => TextFormatFlags.HorizontalCenter,
            ContentAlignment.TopRight => TextFormatFlags.Right,
            ContentAlignment.MiddleLeft => TextFormatFlags.VerticalCenter,
            ContentAlignment.MiddleCenter => TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter,
            ContentAlignment.MiddleRight => TextFormatFlags.VerticalCenter | TextFormatFlags.Right,
            ContentAlignment.BottomLeft => TextFormatFlags.Bottom,
            ContentAlignment.BottomCenter => TextFormatFlags.Bottom | TextFormatFlags.HorizontalCenter,
            ContentAlignment.BottomRight => TextFormatFlags.Bottom | TextFormatFlags.Right,
            _ => 0
        };
        Rectangle content = label.ClientRectangle;
        content.X += label.Padding.Left;
        content.Y += label.Padding.Top;
        content.Width = Math.Max(0, content.Width - label.Padding.Horizontal);
        content.Height = Math.Max(0, content.Height - label.Padding.Vertical);
        TextRenderer.DrawText(e.Graphics, label.Text, label.Font, content,
            Palette.DisabledText, label.BackColor, flags);
    }

    internal void ReportDiagnostic(string operation, int errorCode) => Diagnostic?.Invoke(this, new(operation, errorCode));
    private void ControlTreeChanged(object? sender, ControlEventArgs e) => Reconcile();
    private void ContextMenuChanged(object? sender, EventArgs e) => Reconcile();
    private void ToolStripChanged(object? sender, ToolStripItemEventArgs e) => Reconcile();
    private void ControlDisposed(object? sender, EventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        roots.Remove(control);
        RemoveControl(control, restore: false);
        if (!disposed)
        {
            Reconcile();
        }
    }

    private void Detach(Control root)
    {
        if (disposed)
        {
            return;
        }

        VerifyThread();
        if (!roots.TryGetValue(root, out int count))
        {
            return;
        }

        if (count > 1)
        {
            roots[root] = count - 1;
        }
        else
        {
            roots.Remove(root);
        }

        Reconcile();
    }

    private void RemoveControl(Control control, bool restore)
    {
        if (!controls.Remove(control, out var state))
        {
            return;
        }

        control.HandleCreated -= HandleCreated;
        control.ControlAdded -= ControlTreeChanged;
        control.ControlRemoved -= ControlTreeChanged;
        control.ContextMenuStripChanged -= ContextMenuChanged;
        control.Disposed -= ControlDisposed;
        control.Paint -= DisabledLabelPaint;
        if (control is ToolStrip strip)
        {
            strip.ItemAdded -= ToolStripChanged;
            strip.ItemRemoved -= ToolStripChanged;
        }
        bindings.Remove(control);
        var errors = new List<Exception>();
        if (control is ProgressBar progress)
        {
            TryCleanup(() => progressBars?.Detach(progress), errors);
        }
        foreach (var adapter in adapters.ToArray())
        {
            TryCleanup(() =>
            {
                if (!control.IsDisposed && adapter.CanHandle(control))
                {
                    adapter.Detach(control);
                }
            }, errors);
        }

        if (restore && !control.IsDisposed)
        {
            TryCleanup(state.Restore, errors);
            TryCleanup(() => NativeControlRenderer.Apply(control, Context, ReportDiagnostic, false, true), errors);
            TryCleanup(control.Invalidate, errors);
        }
        if (errors.Count > 0)
        {
            throw new AggregateException("Control theme cleanup failed.", errors);
        }
    }

    internal void VerifyRegistrationActivation()
    {
        VerifyThread();
        if (applying || reconciling)
        {
            throw new InvalidOperationException("Activate registrations outside theme callbacks; post to the UI thread when needed.");
        }
    }

    internal void VerifyThread()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        VerifyAccess();
    }

    internal void VerifyAccess()
    {
        if (Environment.CurrentManagedThreadId != uiThread)
        {
            throw new InvalidOperationException("Use ThemeManager on its owning UI thread.");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        VerifyThread();
        disposed = true;
        var errors = new List<Exception>();
        try
        {
            foreach (var registration in registrations.ToArray())
            {
                TryCleanup(registration.Dispose, errors);
            }
            registrations.Clear();
            SystemEvents.UserPreferenceChanged -= WindowsPreferencesChanged;
            foreach (var item in menuOpenHandlers)
            {
                item.Key.DropDownOpening -= item.Value;
            }

            menuOpenHandlers.Clear();
            foreach (var tooltip in toolTips.ToArray())
            {
                TryCleanup(tooltip.Dispose, errors);
            }

            toolTips.Clear();
            roots.Clear();
            foreach (Control control in controls.Keys.ToArray())
            {
                TryCleanup(() => RemoveControl(control, restore: true), errors);
            }

            bindings.Clear();
            adapters.Clear();
        }
        finally
        {
            TryCleanup(dispatcher.Dispose, errors);
            TryCleanup(() => progressBars?.Dispose(), errors);
            ThemeChanged = null;
            Diagnostic = null;
            Interlocked.CompareExchange(ref activeManager, null, this);
        }
        if (errors.Count > 0)
        {
            throw new AggregateException("Theme cleanup failed; all registrations were released.", errors);
        }
    }

    private static void TryCleanup(Action cleanup, List<Exception> errors)
    {
        try
        {
            cleanup();
        }
        catch (Exception error)
        {
            errors.Add(error);
        }
    }

    private sealed record ColorBinding(ThemeColorRole? Background, ThemeColorRole? Foreground);
    private sealed class Registration(Action detach) : IDisposable
    {
        private Action? action = detach;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
