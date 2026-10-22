<a id="top"></a>

# Agent Guide

## TLDR

Keep theming reusable and preserve configured colors/control behavior. Disclose all
generative AI assistance. Control changes need automated regressions and generic human fixtures for startup,
switching, and lifecycle. Keep verification source; discard generated evidence.

[↑ Top](#top)

## Table of contents

- [TLDR](#tldr)
- [Project contract](#project-contract)
- [Implementation map](#implementation-map)
- [Lessons and gotchas](#lessons-and-gotchas)
- [Exception handling](#exception-handling)
- [Verification policy](#verification-policy)
- [Working conventions](#working-conventions)
- [AI disclosure policy](#ai-disclosure-policy)

[↑ Top](#top)

## Project contract

- .NET 10 WinForms requires Windows; Any CPU concerns processor architecture.
  Native Dark requires Windows 11. Older supported Windows resolves to Light,
  retaining the requested mode. High contrast overrides custom colors.
- Keep namespace/package `HenLib.WinFormsTheming`. No application, settings-file,
  or other HenLib dependencies. Hosts supply modes/options, persistence, palettes,
  adapters, and business-state colors.
- Create one `ThemeManager` on the STA UI thread after application initialization,
  before forms. Use `Register`, or `Prepare` after `InitializeComponent` and `Activate`
  after host initialization. Registrations own setup/cleanup; the host owns components.
- Display **System Mode**; retain API/preference value `ThemeMode.System`.
- Read [integration and limits](README.md). Calendars, DataGridView, LinkLabel,
  and PropertyGrid have gaps. Native file/folder dialogs retain Windows rendering;
  managed confirmation captions are English. Custom controls need dedicated review.

[↑ Top](#top)

## Implementation map

Under `src/HenLib.WinFormsTheming/`:

| Files | Responsibility |
| --- | --- |
| `ThemeManager.cs` | Resolution, Windows notifications, discovery, bindings/adapters, lifecycle. |
| `ThemeRegistration.cs`, `ThemeControlDefaults.cs` | Setup scope, early snapshots, commands/callbacks, rollback and cleanup. |
| `ControlThemeState.cs` | Original properties, color ownership/restoration, button/tab policy. |
| `NativeControlRenderer.cs` | HWND theming, child windows, cached button painters, redraw. |
| `ProgressBarAnimationController.cs`, `ThemedProgressBarWindow.cs` | Shared UI timer; painting over existing progress HWNDs. |
| `ThemedDisabledTextBoxWindow.cs` | Contrast-aware disabled native edit text; no enabled/content/layout changes. |
| `ThemedToolTipRegistration.cs`, `ThemeMessageBox.cs` | Tooltip lifecycle and confirmations. |

Use public palette/binding/adapter extension points. Keep application-specific
styling in the consuming application.

[↑ Top](#top)

## Lessons and gotchas

- `Application.SetColorMode` and form hide/show left controls stale. Preserve targeted
  native/painter refresh; avoid blanket form hiding or recreating every HWND.
- Designer assignments execute at runtime. Snapshot originals before changing parents.
  Use `ShouldSerializeValue`: restore explicit colors, reset inherited/default colors.
  Preserve intentional colors in both modes outside high contrast, unless explicitly bound.
- `SystemColors` identity can stay equal while ARGB changes. Preserve pinned-ARGB
  refresh of inherited brushes. Set TabPage `UseVisualStyleBackColor` after BackColor,
  whose setter can clear it.
- Explicit system backgrounds also cache native brushes. Refresh through RGB and
  restore the current symbolic color; preserve inheritance and application callbacks.
  Keep this before the final TabPage background policy. Verify native pixels, not
  just Color equality; system-color tokens can compare equal across modes.
- Button painters cache theme state, including before late attachment. Refresh may
  recreate HWNDs; preserve styles, images, sizing, and focus. `NativeTextButtons`
  stays opt-in because appearance/layout differ.
- Tree traversal during discovery/theme refresh is intentional; keep it out of paint
  and animation ticks. Coalesce refreshes: `SetColorMode` can pump messages, and
  reposting while busy can sustain a nested message pump.
- Configure before activation. Setup calls require the owning UI thread outside
  theme/adapter callbacks; defer through `BeginInvoke` when necessary. Capture snapshots
  used by adapter cleanup before activation. Never replace host settings storage.
- Subscribe once; release handlers, subclasses, timers, bindings, and adapters on
  detach/disposal. Preserve hidden/new control discovery, recreated-handle refresh,
  menu/child-window handling, and restoration of adapter-owned changes.
- Progress rendering preserves native ranges, values, states, marquee speed, RTL,
  and accessibility. Managed drawing adds borders/shimmer in both modes; Native
  retains framework animation gaps. Respect disabled motion and high contrast.
- Visibility events can precede HWND visibility. Keep deferred animation checks after
  show/restore. Require motion without clicks or forced repaint; snapshots and
  `DrawToBitmap` can conceal missing repaint triggers.
- Disabled EDIT ignores ForeColor. Its native GrayText comes from GetSysColor(17),
  not .NET Dark SystemColors.GrayText. Preserve native shaping/password masking by
  recoloring a native client-render mask; never toggle Enabled or replace the text.
  The temporary parent paint subclass/brush must be removed after each capture.
  Light/high contrast bypass this rendering. Restrict it to stock TextBox/MaskedTextBox;
  derived/custom controls and RichTextBox remain application-owned.
- Mixed RichTextBox content stays application-owned; binding foreground recolors text.
  Preserve tooltip titles/icons, custom owner drawing, and original properties.

[↑ Top](#top)

## Exception handling

- Propagate unexpected managed exceptions, including host callbacks/adapters. Use
  `throw;` to retain the stack. Queued failures reach the host's UI exception handling.
- Roll back setup resources before rethrowing. Attempt remaining cleanup; if cleanup
  also fails, retain the original and cleanup failures in `AggregateException`.
  Theme changes are not transactional; do not promise complete UI rollback.
- Catch-and-fallback requires an explicit, narrow contract. The read-only
  `IsWindowsAppThemeDark` probe resolves missing/unrecognized preferences or expected
  security/access/I/O failures to Light, logs full exceptions, and preserves System
  as the requested mode. Keep theme-changing operations outside that catch.
- Keep filtered disposal/handle-teardown races narrowly guarded. Native return-code
  failures use the documented `Diagnostic` policy; subscriber exceptions propagate.

[↑ Top](#top)

## Verification policy

For new control support or changes to drawing, refresh, or lifecycle:

- Extend a generic sample in `verification/Shared/` and regressions in
  `verification/Automated/`; a bug regression should expose the original failure.
  Add new scenarios to `Verify.ps1`.
- Fixtures/checks use neutral framework controls/data. No consuming-application
  dependencies, settings/workers, or copied/inspired UI, workflows, names, or conventions.
- Cover applicable cases below; explain omissions/unverified cases. Update README
  limits as coverage changes, keeping partial support explicit.

| Area | Coverage |
| --- | --- |
| Startup/switching | Fresh Light/Dark processes; repeated round trips from both; same-mode requests; switched appearance matches destination startup. |
| System Mode | When affected: preference resolution/notifications in both directions; explicit Light/Dark requests stay selected. |
| Colors | Inherited/default and Designer-assigned colors; bindings/custom palettes; Light restoration; contrast override/restoration. |
| Lifecycle | Hidden tabs/controls/forms; switch while hidden, reshow; dynamic/late attachment, reparenting, handle recreation, detach/reattach/disposal. |
| Behavior | Relevant values, selection, scrolling, focus, layout, enabled/read-only states, accessibility, RTL, menus/child windows; stable handles across switches except documented button refresh. |
| Resources | No handler/timer growth or retained registrations; restored originals; inspect `Diagnostic` failures and actual drawing. |
| Animation | Passive startup, hide/show/minimize/restore without input/forced painting; pause/resume/suppressed motion. Static frames do not establish animation. |

- Run automated checks through `verification/Verify.ps1`, which defaults to an
  isolated, non-visible desktop. Never use `-Interactive`, launch visible fixtures,
  or steal desktop focus without the user explicitly requesting visible testing.
- The gallery-only SplitContainer paints its splitter through a display DC and
  can fail on inactive desktops; native-scroll regressions omit that untested sample.
- Manually inspect native painting: contrast, borders, hover/pressed/disabled states,
  clipping, flicker, motion. Control focus during comparisons; user activity changes
  selection/focus appearance. `DrawToBitmap` is supplementary.
- Follow [commands and automated/human fixture roles](verification/README.md).
  Shared-manager changes require the full runner; API/distribution changes require
  independent project/package consumers. Human fixtures stay open until closed.
- Automated checks require Windows 11, .NET 10 SDK, normal contrast, and an unlocked
  desktop. Simulated contrast does not verify actual Windows contrast themes.
  Actual contrast-theme and monitor/DPI acceptance remain outstanding. Disabled
  Windows motion cannot establish animation success.
- Keep source/scripts in Git; ignore build outputs. Remove temporary feeds/caches
  and captures after use. Do not commit/archive reports, screenshots, packages,
  binaries, or evaluation evidence. Summarize validation and remaining gaps in the
  change/commit message. Documentation-only edits need navigation/format review.

[↑ Top](#top)

## Working conventions

- Preserve CRLF and existing encoding/BOMs. Use Allman braces on their own lines
  for all code blocks, including single-statement/empty methods, accessors, lambdas,
  loops, `using`, `lock`, `try`, `catch`, and `finally` blocks.
- Replace nested ternaries and long/complex expressions or returns with explicit
  branches, guard clauses, named values, or small helpers. Preserve evaluation order
  and short-circuit behavior; keep simple expressions concise.
- Log/print exceptions with `ex.ToString()` and relevant operation/control/state
  context. Never reduce diagnostic output to `Message` or another subset. Concise
  user-facing messages require a separate full diagnostic log; formatters must not
  silently discard exceptions.
- Keep docs concise: TLDR and a linked TOC at the top. List sections/subsections,
  excluding the TOC itself. End each section/subsection with `↑ Top`.
  Maintain anchors; links to other Markdown files omit fragments.
- Detailed Git commit messages must reflect the actual diff/validation. Preserve
  unrelated working changes. Push only when requested.

[↑ Top](#top)

## AI disclosure policy

- Follow [CONTRIBUTING.md](CONTRIBUTING.md) for all generative AI assistance,
  including docs, tests, reviews, and agents/subagents.
- For each provider/model, the required disclosure is just `Provider`, `Tool/product`,
  and `Model/version` (for example, `GPT-6.1 Sol`). These three fields are sufficient.
- Give the actual model/version; use `Unknown / not exposed` if unavailable.
  State `AI assistance: None` only when none was used.
- Commit-message disclosure is sufficient; source/test banners are not required.
  PRs may reference disclosed commits. Preserve disclosures when squashing; issues
  and reviews without commits carry their own disclosure.
- Preserve the original implementation credit to human prompting of OpenAI
  GPT-6.1 Sol. MIT licensing remains unchanged.

[↑ Top](#top)
