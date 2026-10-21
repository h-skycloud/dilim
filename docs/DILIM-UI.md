# Filee · Dilim Windows delivery

The reference UI is implemented as a native Avalonia workspace on Filee's conversion foundation.
Upstream base: `85f7e56156b373e889a725e7d245d8024b3d5f70`. Branch: `codex/filee-dilim-ui`.

## Start

Double-click `../Dilim Başlat.vbs` from the outer workspace, or
`artifacts/Dilim-Windows/Dilim Başlat.vbs` from the portable package.
No .NET SDK/runtime or administrator privileges are needed for that published package.
Keep all package files together. Settings/history live in its `data` folder.
The portable build leaves Explorer/startup registrations alone and disables automatic update checks.

A source launch is still available: `pwsh -NoProfile -File build/run-dilim.ps1`.
It requires .NET 10 and uses a separate development data folder.

## Delivered behavior

- Three contextual pie modes: Convert, Tools and File actions, from the desktop or the main page.
- Default Explorer shortcuts: Ctrl+Alt+Space / Ctrl+Alt+T / Ctrl+Alt+A.
- Drag modifiers: Shift / Ctrl / Alt, respectively; Tab or wheel flips faces; Esc closes.
- Gesture settings include the target pie mode. Existing customized gestures are preserved.
- 26 tool entries use native image/PDF operations, Filee's job queue, or real FFmpeg operations.
- Six file actions: Windows share sheet, floating file shelf, file clipboard, move, path clipboard and terminal.
- Tools take explicit time/coordinate/page parameters and show progress, cancellation, output paths and open-folder.
- Photo editing supports rotation, mirroring, resize, brightness, contrast and saturation. Crop uses the visual crop overlay.
- PDF organization accepts ordered/repeated/reversed ranges, e.g. `3,1-2,5`.
- Merge/collage windows let users reorder input files. The shelf supports native file drag-out.
- FFmpeg and 7-Zip are bundled. Other optional engines remain available in Engines.
- EN/TR switches live and preserves the catalogue's selected files and mode.
- Native global gestures, tray behavior, existing conversion presets/profiles, history and advanced settings remain usable.
- Edits write new outputs under Dilim beside the source. Standard conversions retain their existing output rules.
- Move is explicitly labelled as relocating originals; preflight never overwrites existing names.

## Defined scope

Tools are compact operation editors, not a full layered photo/video editing suite. Video redaction uses a fixed
rectangle and an explicit time interval; there is no motion tracking. Video joining normalizes to 1280×720 and
requires all clips to agree about audio. Audio outputs are M4A. PDF metadata cleanup removes document information,
not visible page content. Share opens the system UI; the user selects the destination. Folders must be zipped first.
The shelf keeps references for the current session. Cancelled batches can retain already-completed outputs/moves.

## Rebuild

```powershell
pwsh -NoProfile -File build/build-dilim.ps1
```

This fetches checksum-pinned FFmpeg/7-Zip and publishes self-contained Windows x64 app/share-helper packages.
`-SkipEngines` reuses previously fetched copies. A rebuild preserves existing portable settings.
The Windows-only share helper is deliberately outside the cross-platform solution; the packaging script builds it.

See `DILIM-REHBER.html`, `DILIM-REHBER.md`, `DILIM-TOOL-MATRIX.md` and the validation reports.
