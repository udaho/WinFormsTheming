<a id="top"></a>

# HenLib.WinFormsTheming

## TLDR

Reusable .NET 10 WinForms theming with Light, Dark, Follow Windows, native refresh,
designer-color preservation, and animated progress bars. Dark requires Windows 11.
Applications own their saved preferences and business-state colors.

[↑ Top](#top)

## Table of contents

- [TLDR](#tldr)
- [Use](#use)
- [Customization](#customization)
- [Limits](#limits)
- [Verification](#verification)

[↑ Top](#top)

## Use

Reference `src/HenLib.WinFormsTheming/HenLib.WinFormsTheming.csproj`, or use the
`HenLib.WinFormsTheming` package. There are no application or other HenLib dependencies.

```csharp
using HenLib.WinFormsTheming;

ApplicationConfiguration.Initialize();
using var themes = new ThemeManager(ThemeMode.System);
using var main = new MainForm();
using var registration = themes.Attach(main);
Application.Run(main);
```

Create one manager on the STA UI thread before forms. Use `SetMode(Light/Dark/System)`
from that thread. Attach detached windows separately; descendants, context menus,
dynamic controls, and recreated handles are discovered automatically. Dispose
registrations/manager to restore properties and release subscriptions.

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

Theme changes visit registered controls and can recreate managed button handles.
Do not mix direct `Application.SetColorMode` calls with the manager. Explicit colors
are preserved outside high contrast; mixed RichTextBox content stays application-owned.
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

## Verification

See [verification commands and fixtures](verification/README.md). Automated checks,
interactive fixtures, and the independent/package consumer are separate. Build with
`dotnet build HenLib.WinFormsTheming.sln`; package only the library with `dotnet pack
src/HenLib.WinFormsTheming/HenLib.WinFormsTheming.csproj -c Release`.

[↑ Top](#top)