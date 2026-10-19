namespace HenLib.WinFormsTheming;

/// <summary>
/// Owns a configured group of roots, tooltips, bindings, adapters, commands, and callbacks.
/// Prepare captures defaults without applying a theme; Activate completes setup once.
/// The owner root's disposal or manager disposal also releases this registration.
/// </summary>
public sealed class ThemeRegistration : IDisposable
{
    private readonly ThemeManager manager;
    private readonly Control owner;
    private readonly List<Control> roots = new();
    private readonly Dictionary<Control, ThemeControlDefaults> defaults = new();
    private readonly List<Func<IDisposable>> factories = new();
    private readonly List<IDisposable> resources = new();
    private readonly List<Action> updateCommands = new();
    private EventHandler<ThemeChangedEventArgs>? changed;
    private EventHandler<ThemeDiagnosticEventArgs>? diagnostic;
    private bool activating, disposed;

    internal ThemeRegistration(ThemeManager manager, Control owner)
    {
        this.manager = manager;
        this.owner = owner;
        Include(owner);
        owner.Disposed += OwnerDisposed;
    }

    public bool IsActive { get; private set; }

    /// <summary>Capture a detached root now and attach it when activated. Does not own the control.</summary>
    public ThemeRegistration Include(Control root)
    {
        VerifyConfigurable();
        ArgumentNullException.ThrowIfNull(root);
        ObjectDisposedException.ThrowIf(root.IsDisposed, root);
        if (!roots.Contains(root))
        {
            foreach (var pair in manager.CaptureDefaults(root))
            {
                defaults.TryAdd(pair.Key, pair.Value);
            }
            roots.Add(root);
        }
        return this;
    }

    public ThemeRegistration ToolTip(ToolTip toolTip)
    {
        VerifyConfigurable();
        ArgumentNullException.ThrowIfNull(toolTip);
        factories.Add(() => manager.AttachToolTip(toolTip));
        return this;
    }

    public ThemeRegistration BindColors(Control control, ThemeColorRole? background = null, ThemeColorRole? foreground = null)
    {
        VerifyConfigurable();
        ArgumentNullException.ThrowIfNull(control);
        factories.Add(() => manager.BindColors(control, background, foreground));
        return this;
    }

    /// <summary>Own the lifetime of an application-wide adapter registration.</summary>
    public ThemeRegistration AddAdapter(IThemeAdapter adapter)
    {
        VerifyConfigurable();
        ArgumentNullException.ThrowIfNull(adapter);
        factories.Add(() => manager.AddAdapter(adapter));
        return this;
    }

    /// <summary>Wire a menu command and maintain its checked state using the requested mode.</summary>
    public ThemeRegistration ModeCommand(ToolStripMenuItem item, ThemeMode mode)
    {
        VerifyConfigurable();
        ArgumentNullException.ThrowIfNull(item);
        ValidateMode(mode);
        factories.Add(() =>
        {
            ObjectDisposedException.ThrowIf(item.IsDisposed, item);
            bool original = item.Checked;
            EventHandler click = (_, _) => manager.SetMode(mode);
            item.Click += click;
            Action update = () =>
            {
                if (!item.IsDisposed)
                {
                    item.Checked = manager.RequestedMode == mode;
                }
            };
            updateCommands.Add(update);
            return new Cleanup(() =>
            {
                item.Click -= click;
                updateCommands.Remove(update);
                if (!item.IsDisposed)
                {
                    item.Checked = original;
                }
            });
        });
        return this;
    }

    /// <summary>Wire a button command without replacing its configured appearance.</summary>
    public ThemeRegistration ModeCommand(ButtonBase button, ThemeMode mode)
    {
        VerifyConfigurable();
        ArgumentNullException.ThrowIfNull(button);
        ValidateMode(mode);
        factories.Add(() =>
        {
            ObjectDisposedException.ThrowIf(button.IsDisposed, button);
            EventHandler click = (_, _) => manager.SetMode(mode);
            button.Click += click;
            return new Cleanup(() => button.Click -= click);
        });
        return this;
    }

    /// <summary>Run once after activation, and after each manager ThemeChanged event.</summary>
    public ThemeRegistration OnThemeChanged(EventHandler<ThemeChangedEventArgs> handler)
    {
        VerifyConfigurable();
        ArgumentNullException.ThrowIfNull(handler);
        changed += handler;
        return this;
    }

    /// <summary>Observe native failures, including those during initial attachment.</summary>
    public ThemeRegistration OnDiagnostic(EventHandler<ThemeDiagnosticEventArgs> handler)
    {
        VerifyConfigurable();
        ArgumentNullException.ThrowIfNull(handler);
        diagnostic += handler;
        return this;
    }

    public ThemeControlDefaults DefaultsFor(Control control)
    {
        VerifyAlive();
        ArgumentNullException.ThrowIfNull(control);
        if (defaults.TryGetValue(control, out var original))
        {
            return original;
        }
        throw new ArgumentException("The control was not present when this registration captured its roots.", nameof(control));
    }

    /// <summary>Attach all resources and apply the initial callback. Repeated activation is a no-op.</summary>
    public void Activate()
    {
        VerifyAlive();
        if (IsActive)
        {
            return;
        }
        if (activating)
        {
            throw new InvalidOperationException("Registration activation is already in progress.");
        }
        manager.VerifyRegistrationActivation();
        activating = true;
        try
        {
            manager.Diagnostic += Diagnostic;
            manager.ThemeChanged += ThemeChanged;
            foreach (Control root in roots)
            {
                Own(() => manager.Attach(root, defaults));
            }
            foreach (var factory in factories)
            {
                Own(factory);
            }
            IsActive = true;
            activating = false;
            ThemeChanged(manager, new(manager.Context, manager.Palette, manager.Revision));
        }
        catch (Exception error)
        {
            try
            {
                Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Theme registration activation and cleanup failed.", error, cleanup);
            }
            throw;
        }
        finally
        {
            activating = false;
        }
    }

    private void Own(Func<IDisposable> factory)
    {
        var resource = factory();
        if (disposed)
        {
            resource.Dispose();
            throw new ObjectDisposedException(nameof(ThemeRegistration));
        }
        resources.Add(resource);
    }

    private void ThemeChanged(object? sender, ThemeChangedEventArgs e)
    {
        if (!IsActive || disposed)
        {
            return;
        }
        foreach (Action update in updateCommands.ToArray())
        {
            if (disposed)
            {
                return;
            }
            update();
        }
        changed?.Invoke(manager, e);
    }

    private void Diagnostic(object? sender, ThemeDiagnosticEventArgs e) => diagnostic?.Invoke(manager, e);

    private static void ValidateMode(ThemeMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private void VerifyAlive()
    {
        manager.VerifyThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void VerifyConfigurable()
    {
        VerifyAlive();
        if (IsActive || activating)
        {
            throw new InvalidOperationException("Configure a registration before activation.");
        }
    }

    private void OwnerDisposed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        manager.VerifyAccess();
        disposed = true;
        IsActive = false;
        owner.Disposed -= OwnerDisposed;
        manager.ThemeChanged -= ThemeChanged;
        manager.Diagnostic -= Diagnostic;
        changed = null;
        diagnostic = null;
        var errors = new List<Exception>();
        for (int index = resources.Count - 1; index >= 0; index--)
        {
            try
            {
                resources[index].Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        resources.Clear();
        factories.Clear();
        updateCommands.Clear();
        roots.Clear();
        defaults.Clear();
        manager.ForgetRegistration(this);
        if (errors.Count > 0)
        {
            throw new AggregateException("Theme registration cleanup failed; all resources were released.", errors);
        }
    }

    private sealed class Cleanup(Action action) : IDisposable
    {
        private Action? cleanup = action;
        public void Dispose() => Interlocked.Exchange(ref cleanup, null)?.Invoke();
    }
}
