<a id="top"></a>

# WinFormsTheming

## TLDR

Uses .NET 10 WinForms' built-in Light/Dark theming and fills its gaps with reliable
runtime switching, automatic System Mode updates, animated progress bars with borders
and shimmer, and restoration of Designer colors. Native Dark requires Windows 11.
Applications own their saved preferences and business-state colors.

[↑ Top](#top)

## Table of contents

- [TLDR](#tldr)
- [Use](#use)
  - [Startup order](#startup-order)
  - [Application wiring](#application-wiring)
- [Customization](#customization)
- [Limits](#limits)
- [Exception handling](#exception-handling)
- [Verification](#verification)
- [Origin and AI assistance](#origin-and-ai-assistance)
- [Contributing](#contributing)
- [License](#license)

[↑ Top](#top)

## Use

Reference `src/HenLib.WinFormsTheming/HenLib.WinFormsTheming.csproj`, or use the
`HenLib.WinFormsTheming` package. There are no application or other HenLib dependencies.

[↑ Top](#top)

### Startup order

Create one manager on the STA UI thread before forms, including field-initialized
windows. `Register` captures defaults, attaches resources, and applies initial styling:

```csharp
using HenLib.WinFormsTheming;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        // The host can load its saved preference here.
        using var themes = new ThemeManager(ThemeMode.System);
        using var main = new MainForm(); // Constructor calls InitializeComponent().
        using var registration = themes.Register(main);
        Application.Run(main);
    }
}
```

To start in Dark Mode, replace `ThemeMode.System` above with `ThemeMode.Dark`:

```csharp
using var themes = new ThemeManager(ThemeMode.Dark);
```

Create it before any forms; this initial mode also applies to `Prepare`/`Activate`.

For capture before further application initialization, use `Prepare` in the form
constructor immediately after `InitializeComponent()`, then `Activate` when ready.
This replaces hand-written default capture and registration bookkeeping:

```csharp
InitializeComponent();
theme = themes.Prepare(this, setup => setup
    .Include(secondaryWindow)
    .ToolTip(helpToolTip)
    .ModeCommand(lightItem, ThemeMode.Light)
    .ModeCommand(darkItem, ThemeMode.Dark)
    .ModeCommand(systemItem, ThemeMode.System)
    .BindColors(noticeLabel, foreground: ThemeColorRole.WarningText)
    .OnThemeChanged((_, change) => ApplyStateColors(change.Context, change.Palette))
    .OnDiagnostic((_, error) => LogThemeFailure(error.Operation, error.ErrorCode)));
originals = theme.DefaultsFor(accentPanel);
InitializeApplication();
theme.Activate();
```

Here `theme`/`originals` are form fields; controls and callbacks belong to the host.
Capture after Designer properties and any intended baseline configuration. Later
initialization does not replace that baseline. `DefaultsFor` exposes captured colors,
explicit/inherited flags, and restoration helpers; it accepts controls present at
capture. New descendants are captured automatically when discovered by the manager.
Tooltip/command originals are captured at activation.

Choose one root registration owner: the entry point or the form. Preparation applies
no theming; activation is idempotent. Configure before activation. Setup calls run
on the owning UI thread outside theme/adapter callbacks; use `BeginInvoke` to defer
registration from such a callback. Existing low-level `Attach` APIs remain available.

[↑ Top](#top)

### Application wiring

Configure the features present in the host:

| Registration method | Behavior |
| --- | --- |
| `Include(root)` | Capture/register detached windows or popups. Ordinary descendants/context menus/dynamic controls are discovered automatically. |
| `ToolTip(component)` | Register a tooltip component separately from the control tree. Controls still need their normal tooltip settings enabled. |
| `ModeCommand(menuItemOrButton, mode)` | Wire the click; menu checkmarks track the requested preference, including System Mode. Captions remain host-owned. |
| `BindColors(control, ...)` | Apply palette roles after roots are attached. |
| `AddAdapter(adapter)` | Own an application-wide adapter's lifetime; its `CanHandle` selects controls and `Detach` restores changes. |
| `OnThemeChanged(handler)` | Run once after all initial setup, then on theme changes. Use it for application-owned item/state colors and persistence of `RequestedMode`. |
| `OnDiagnostic(handler)` | Observe native failures, including initial attachment. |

Registration owns subscriptions and resource tokens. Explicit disposal, owner-root
disposal, or manager disposal releases them; activation failures roll back acquired
resources. Included windows/tooltips themselves remain host-owned. Store snapshots
needed by adapter cleanup before activation. Adapters also cover control refreshes
such as handle recreation, which need not raise `ThemeChanged`.

The manager follows Windows notifications automatically. The host supplies saved
preferences and business-state styling; use `RequestedMode` for persistence/menu
selection and `Context.IsDark` for resolved appearance.

[↑ Top](#top)

## Customization

- `ThemeChanged`, `Context`, and `Palette`: update application-owned drawing.
- `BindColors`: palette-role overrides; retain/dispose its registration.
- `PaletteFactory`, `ExcludeControl`, and `AddAdapter`: customize control handling.
- `AttachToolTip` and `ThemeMessageBox.Show`: themed tooltip/dialog alternatives.
- Buttons preserve configured styles. `NativeTextButtons` is an optional rendering
  policy that can change appearance/layout. Call `Refresh()` after runtime image changes.
- Progress bars preserve their native controls, values, accessibility, and explicit
  colors. Light uses green; Dark uses the accent. Incomplete progress shimmers and
  marquee moves. Hidden/minimized/disabled/completed/error/paused bars stop motion.
  Windows animation preferences and high contrast also suppress it.
- `AnimateProgressBars = false` retains static managed drawing;
  `ProgressBarRendering = Native` restores framework rendering and its animation gaps.

[↑ Top](#top)

## Limits

WinForms requires Windows. Any CPU allows different processor architectures within
Windows; it does not change which Windows features are available.

Dark mode uses WinForms' `Application.SetColorMode`, whose native Dark path is
[supported on Windows 11 and later](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.application.setcolormode?view=windowsdesktop-10.0).
Installing .NET 10 on older Windows does not provide that supported native Dark path.
This library checks for Windows 11 (build 22000+) and resolves to Light on older
supported Windows, retaining the requested mode. Supporting Dark there would require
a separate custom rendering/fallback implementation; this library does not provide one.
High contrast also disables native Dark and takes precedence over custom palettes.

Theme changes visit registered controls and can recreate managed button handles.
Do not mix direct `Application.SetColorMode` calls with the manager. Explicit colors
are preserved outside high contrast; system-color backgrounds refresh without losing
their symbolic values or inheritance. Mixed RichTextBox content stays application-owned.
In Dark mode, disabled standard TextBox/MaskedTextBox text uses a readable disabled shade
(at least 4.5:1 against its background), preserving native layout, masking, scrolling,
and enabled state. Stock NumericUpDown/DomainUpDown refresh their editor and arrow
themes too, including readable disabled text. Read-only spinners retain working arrows.
Light/high contrast retain native text rendering. RichTextBox formatting
and derived/custom controls remain application-owned.
Managed message-box captions are English; native file/folder dialogs use Windows drawing.

| Needs dedicated handling | Gap |
| --- | --- |
| DateTimePicker / MonthCalendar | Native calendar surfaces can remain Light. |
| DataGridView | Header and cell-style palette handling. |
| LinkLabel | Link/active/visited/disabled colors. |
| PropertyGrid | Some internal/construction-time colors across transitions. |

Custom/owner-drawn controls, actual Windows contrast themes, and monitor/DPI changes
need consumer acceptance. Observe native API failures through `Diagnostic`.

[↑ Top](#top)

## Exception handling

Unexpected managed exceptions propagate to the caller, or to the host's UI-thread
exception handling for queued work. Invalid arguments, wrong-thread use, disposed
objects, and failures from palette factories, adapters, or event handlers are not
converted into success. A failed theme operation can leave partially updated UI;
`SetMode` and `Refresh` are not transactional.

Failed setup attempts cleanup before rethrowing. When cleanup also fails, both the
original and cleanup errors are retained in `AggregateException`; disposal attempts
remaining releases and reports collected failures.

Two narrow recovery cases are intentional:

- The private `IsWindowsAppThemeDark` probe only reads Windows' **app** preference
  (`AppsUseLightTheme`). Missing/unrecognized values or expected security, access,
  and I/O failures resolve to Light in System Mode. The requested System preference
  is retained. The probe logs caught exceptions with `ToString()` and does not
  change Windows settings, controls, or WinForms color mode; unexpected exceptions
  still propagate.
- Deferred refresh/animation checks can become unnecessary during disposal or
  handle teardown. Only the filtered exceptions for those lifecycle races are ignored.

Native failures reported through `Diagnostic` are return-code failures, not swallowed
managed exceptions. Rendering continues as a best-effort operation. Subscribe through
`OnDiagnostic`/`Diagnostic` to record them; exceptions thrown by subscribers propagate.

[↑ Top](#top)

## Verification

See [verification commands and fixtures](verification/README.md). Automated checks,
interactive fixtures, and the independent/package consumer are separate. Build with
`dotnet build HenLib.WinFormsTheming.sln`; package only the library with `dotnet pack
src/HenLib.WinFormsTheming/HenLib.WinFormsTheming.csproj -c Release`.

[↑ Top](#top)

## Origin and AI assistance

The original implementation was developed by a human prompting **OpenAI GPT-6.1 Sol**.

All contributions must disclose generative AI assistance. Provider, tool/product,
and model/version are sufficient; see [CONTRIBUTING.md](CONTRIBUTING.md).

[↑ Top](#top)

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) for the mandatory AI assistance disclosure
and submission requirements, and [AGENTS.md](AGENTS.md) for implementation and
verification guidance. AI-assisted contributions are welcome with full disclosure.

[↑ Top](#top)

## License

MIT; see [LICENSE](LICENSE). The AI disclosure policy governs contributions;
the license remains the standard MIT license.

[↑ Top](#top)
