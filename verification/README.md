<a id="top"></a>

# Verification

## TLDR

Run `Verify.ps1` for automated checks. Run `Open-Fixtures.cmd` for interactive
Light/Dark progress fixtures, or `Open-Fixtures.cmd --gallery` for common controls.
Both need Windows 11, .NET 10 SDK, and an unlocked interactive desktop.

[↑ Top](#top)

## Table of contents

- [TLDR](#tldr)
- [Table of contents](#table-of-contents)
- [Automated](#automated)
- [Human verification](#human-verification)

[↑ Top](#top)

## Automated

From this directory, run `powershell -File .\Verify.ps1` (`-SkipPackage` skips packaging).
Exit code 0 means success; failures print to the console. These are STA executable
checks with real HWNDs, so use this runner rather than `dotnet test`.
Start in normal contrast; actual Windows contrast-theme acceptance uses the human fixtures.

| Source | Covers |
| --- | --- |
| Automated/ProgressChecks | Shimmer/marquee frames, native messages, ranges, RTL, explicit colors, accessibility, lifecycle, static/native policies. |
| Automated/VisibilityChecks | Controls/form hide/show, theme changes while hidden, restore popup, preserved values, resumed animation. |
| Automated/StartupChecks | Untouched startup and passive hide/show/minimize/restore, without clicks or forced repaint. |
| Automated/LifecycleChecks | Bindings, designer colors, tooltip title/icons, cached button painters, simulated contrast policy. |
| Automated/CommonControlChecks | TrackBar and scrollbar/list-box drawing through repeated transitions. |
| ConsumerSmoke / PackageConsumer | Independent consumer with 20 switches, focus/selection/scroll preservation, menus, adapters, cleanup, and message-box results; project and package references. |

The package check uses an isolated temporary feed/cache and removes it afterwards.
Checks use only this library and framework controls. No reports or captures are persisted.
Windows motion preferences are respected; disabled motion does not prove animation.

[↑ Top](#top)

## Human verification

The fixtures stay open until closed. Progress: change theme/value/state/speed, hide
controls, hide the form, switch theme in its restore popup, and show it again.
Gallery: inspect 28 common controls; it deliberately exposes known coverage gaps.
Also review flicker, hover/pressed/disabled states, native borders, menu navigation,
Windows theme notifications, contrast themes, and monitor/DPI changes manually.

`Shared/` contains fixture forms reused by the interactive and automated projects.
Keep source and scripts; do not commit generated binaries, packages, screenshots,
reports, or evaluation evidence. Build output directories are ignored.

[↑ Top](#top)
