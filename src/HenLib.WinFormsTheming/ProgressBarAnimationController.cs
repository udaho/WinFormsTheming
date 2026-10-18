using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HenLib.WinFormsTheming;

/// <summary>One UI timer shared by every attached progress bar.</summary>
internal sealed class ProgressBarAnimationController : IDisposable
{
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private readonly ThemeManager manager;
    private readonly bool allowAnimation;
    private readonly Dictionary<ProgressBar, ThemedProgressBarWindow> bars = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private bool disposed;

    internal ProgressBarAnimationController(ThemeManager manager, bool allowAnimation)
    {
        this.manager = manager;
        this.allowAnimation = allowAnimation;
        timer.Tick += Tick;
        RefreshPolicy();
    }

    internal ThemeContext Context => manager.Context;
    internal ThemePalette Palette => manager.Palette;
    internal double ElapsedMilliseconds => clock.Elapsed.TotalMilliseconds;
    internal bool MotionEnabled
    {
        get; private set;
    }
    internal void ReportDiagnostic(string operation, int errorCode) => manager.ReportDiagnostic(operation, errorCode);

    internal void RefreshPolicy()
    {
        MotionEnabled = allowAnimation && !Context.IsHighContrast &&
            SystemParametersInfo(SpiGetClientAreaAnimation, 0, out int animations, 0) && animations != 0;
        foreach (var bar in bars.Values)
        {
            bar.Invalidate();
        }
        StateChanged();
    }

    internal void Attach(ProgressBar control)
    {
        if (bars.ContainsKey(control))
        {
            return;
        }

        bars.Add(control, new ThemedProgressBarWindow(this, control));
        StateChanged();
    }

    internal void Refresh(ProgressBar control)
    {
        if (bars.TryGetValue(control, out var window))
        {
            window.Refresh();
        }
    }

    internal void Detach(ProgressBar control)
    {
        if (bars.Remove(control, out var window))
        {
            window.Dispose();
        }
        StateChanged();
    }

    internal void StateChanged()
    {
        if (!disposed)
        {
            timer.Enabled = bars.Values.Any(bar => bar.NeedsAnimation);
        }
    }

    private void Tick(object? sender, EventArgs e)
    {
        foreach (var bar in bars.Values.ToArray())
        {
            if (bar.NeedsAnimation)
            {
                bar.Invalidate();
            }
        }
        StateChanged();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= Tick;
        timer.Dispose();
        foreach (var window in bars.Values)
        {
            window.Dispose();
        }
        bars.Clear();
        clock.Stop();
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
}
