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

Commands:
```powershell
./make.ps1 -Publish
./make.ps1 -Installer
# or
./make.ps1 -All
```

Output:
- App: `build/publish/LangLayoutBeacon.exe`
- Installer: `build/installer/LangLayoutBeacon_setup.exe`

## Security notes
- No HTTP/API calls in code.
- No clipboard file persistence.
- No keylogging storage.
- Uses Windows APIs only for layout and caret coordinates.
