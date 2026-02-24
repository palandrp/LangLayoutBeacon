# LangLayoutBeacon

A tiny Windows utility: show a small translucent language banner near the **text caret** (not mouse cursor) when keyboard layout changes.

## Scope
- Only one function: layout-change visual hint.
- No autocorrect, no text transform, no dictionary logic.
- No network calls.

## Runtime behavior
- Polls active window keyboard layout every ~70 ms.
- On change, resolves current locale and shows 2-letter code (e.g. EN, RU).
- Banner auto-hides in configurable range 300-700 ms (default 520 ms).
- If caret position can't be read, falls back to centered popup.

## Settings
File: `src/LangLayoutBeacon/appsettings.json` (copied next to exe on publish)

```json
{
  "bannerDurationMs": 520,
  "bannerOffsetPx": 10
}
```

- `bannerDurationMs`: clamp 300..700 ms
- `bannerOffsetPx`: offset near text caret (0..40 px)

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

Examples:
- `v1.0.1-fd-single`
- `v1.0.1-fd-multi`

## Security notes
- No HTTP/API calls in code.
- No clipboard file persistence.
- No keylogging storage.
- Uses Windows APIs only for layout and caret coordinates.
