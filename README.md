# LangLayoutBeacon

A tiny Windows utility: show a small translucent language banner near the **text caret** (not mouse cursor) when keyboard layout changes.

## Scope
- Only one function: layout-change visual hint.
- No autocorrect, no text transform, no dictionary logic.
- No network calls.

## Runtime behavior
- Polls active window keyboard layout every ~70 ms.
- Tracks caret anchor continuously and keeps a **persistent mini-banner** near caret (enabled by default).
- On layout switch, banner language updates and mini-banner smoothly scales up to switch-banner size, then back down.
- In legacy mode (`persistentBannerEnabled=false`), banner appears only on switch and auto-hides.
- Caret positioning strategy:
  1) native caret API (`GetGUIThreadInfo`),
  2) MSAA caret fallback (`OBJID_CARET`),
  3) UI Automation caret fallback (with range expansion),
  4) mouse-cursor fallback (offset configurable via settings).

## Tray icon
- App shows a tray icon while running.
- Right-click tray icon -> **Exit** to stop utility.

## Settings
File: `src/LangLayoutBeacon/appsettings.json` (copied next to exe on publish)

```json
{
  "bannerDurationMs": 520,
  "bannerOffsetPx": 10,
  "persistentBannerEnabled": true,
  "persistentBannerScale": 0.5,
  "switchBannerScale": 1.0,
  "baseFontSize": 10,
  "mouseFallbackOffsetX": 14,
  "mouseFallbackOffsetY": 16
}
```

- `bannerDurationMs`: pulse/show duration clamp 300..1200 ms
- `bannerOffsetPx`: offset near text caret (0..80 px)
- `persistentBannerEnabled`: always-on mini-banner mode (default: `true`)
- `persistentBannerScale`: mini-banner scale (default `0.5`, i.e. ~4x smaller area than switch banner)
- `switchBannerScale`: switch-banner scale (default `1.0`)
- `baseFontSize`: base font size before scale multipliers
- `mouseFallbackOffsetX`: mouse fallback X offset in px (clamp 0..120)
- `mouseFallbackOffsetY`: mouse fallback Y offset in px (clamp 0..120)

## Build (Windows 11 x64)
Prerequisites:
- .NET SDK 8+
- Inno Setup 6

### Mode 2: framework-dependent single-file (small EXE)
```powershell
./make.ps1 -All -Mode fd-single
```
Output:
- App: `build/publish-fd-single/LangLayoutBeacon.exe`
- Installer: `build/installer/LangLayoutBeacon_setup_fd-single.exe`

### Mode 3: framework-dependent multi-file (smallest app folder)
```powershell
./make.ps1 -All -Mode fd-multi
```
Output:
- App: `build/publish-fd-multi/LangLayoutBeacon.exe` (+ DLLs)
- Installer: `build/installer/LangLayoutBeacon_setup_fd-multi.exe`

### Optional legacy mode (large, self-contained)
```powershell
./make.ps1 -All -Mode self-contained
```

## CI build by tag mode
GitHub Actions determines mode from tag suffix:
- `...-fd-single` -> framework-dependent single-file
- `...-fd-multi` -> framework-dependent multi-file

Tag format:
- `vMAJOR.MINOR.PATCH-fd-single`
- `vMAJOR.MINOR.PATCH-fd-multi`

Examples:
- `v1.3.0-fd-multi`
- `v1.3.1-fd-multi`

Versioning rule:
- current baseline: `1.3.0`
- patch-only updates increment PATCH: `1.3.1`, `1.3.2`, ...
- feature upgrades increment MINOR: `1.4.0`, `1.5.0`, ...

## Security notes
- No HTTP/API calls in code.
- No clipboard file persistence.
- No keylogging storage.
- Uses Windows APIs only for layout and caret coordinates.
