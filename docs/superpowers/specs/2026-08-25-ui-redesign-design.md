# Xentra AV — UI Redesign (Frosted Glass Premium + Custom Chrome)

**Date:** 2026-08-25
**Status:** Approved

## Direction
Modern dark glassmorphism with a custom borderless window. Translucent panels, soft
shadows, hairline borders, restrained accent (cyan primary / violet secondary / green safe /
red threat), clean Segoe UI typography, subtle motion.

## Window shell
- Borderless (`WindowStyle=None`, `AllowsTransparency=True`), 14px rounded corners, soft drop
  shadow + faint accent glow.
- Custom title bar: drag region, "XENTRA" wordmark, minimal min/max/close icon buttons with
  hover glow. No default caption.
- Resize via `WindowChrome`.

## Frosted glass
Simulated (WPF has no native acrylic): translucent dark panels (~0.55 opacity) over a deep
near-black background with low-opacity radial cyan/violet glow, 1px light hairline border,
soft shadow. Reads as premium glass, not a true OS backdrop blur (known ceiling).

## Views
- **Dashboard:** hero "Protection" card with glowing animated shield emblem + real-time shield
  state; 3 glass stat cards (Threats blocked / Files scanned / Quarantined); Recent activity
  feed.
- **Scan:** animated circular spinner ring with % in center + current file; glass stat row
  (scanned / threats / progress); completion summary; live results feed; quick-scan buttons.
- **Quarantine:** items as glass **cards** (filename, threat, date, path) with Restore/Delete
  actions revealed on hover; refresh bar.

## Motion
- View switches: fade + slight slide (code-behind Storyboard).
- Hover glows on nav/buttons; smooth glowing progress; spinning scan ring; gentle shield pulse.

## Tech / constraints
- Pure WPF + XAML, no new NuGet packages. Icons as inline vector `Path` geometry.
- Keeps MVVM (`MainViewModel`); adds only props needed. Code-behind handles drag / title
  buttons / view transitions.

## Files
- `src/AV.App/Styles.xaml` (new) — brushes, styles, icons, animations.
- `src/AV.App/App.xaml` — merge Styles.
- `src/AV.App/MainWindow.xaml` — full restyle.
- `src/AV.App/MainWindow.xaml.cs` — drag, title buttons, nav, transitions.
- `src/AV.App/ViewModels/MainViewModel.cs` — rename `isQuarantineVisible` → `IsQuarantineVisible`.

## Verify
Builds clean + runs. Visual confirmation by user (WPF cannot be rendered headlessly here).
