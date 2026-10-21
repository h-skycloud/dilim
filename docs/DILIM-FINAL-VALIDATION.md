# Dilim Windows delivery validation — 21 October 2026

## Build and package

- `dotnet build Filee.slnx --no-restore`: succeeded, 0 errors. Existing upstream nullable warnings remain.
- `build/build-dilim.ps1 -SkipEngines`: self-contained Windows x64 app and Windows share helper published successfully.
- Portable app DLL SHA-256 matched the Release build output after publishing.
- Checksum-pinned FFmpeg 9.0.2 and 7-Zip 26.03 fetched by the upstream build script and bundled with their licenses.
- `dotnet format Filee.slnx --verify-no-changes --no-restore`: passed after the last source changes.
- `git diff --check`: passed.
- Nine locales have 1,036 identical keys and matching numbered-placeholder sets. Repeated placeholders may differ by language.

## Automated checks

| Check | Passed | Failed | Skipped |
|---|---:|---:|---:|
| UI, catalogue, localization, gesture, radial, group/flip/lifecycle delivery run | 118 | 0 | 0 |
| Final keyboard/focus change regression run (overlaps the previous run) | 55 | 0 | 0 |
| Final Escape/lifecycle/controller regression (overlaps above) | 11 | 0 | 0 |
| Native image/PDF/FFmpeg tool tests | 25 | 0 | 0 |
| Windows/Explorer platform selection tests | 7 | 0 | 0 |

The final keyboard run also adds Escape forwarding coverage. Counts are separate runs and must not be added as
unique tests. The existing first-delivery report documents upstream full-suite failures; this delivery does not
claim that every upstream external-engine or multi-language rendering test passes.

Coverage includes all 15 new media operations against generated media, readable output probes, channel/video
properties, real photo redaction/background/annotation/colour/resize operations, PDF reorder and metadata removal,
input hashes, cancellation/validation, persisted menu modes/custom shortcuts, move conflict preflight and real
queued compression, image metadata removal, PDF create/split/merge/compression and ZIP extraction.

## Native Windows checks

- Root VBS launcher opens the portable app using its own saved Turkish profile. The global hook starts successfully.
- A real selected PNG was converted via the live pie's WEBP slice. A new numbered WEBP appeared and the pie showed success.
- Explorer selection shortcut opens the live Convert pie, Ctrl+Alt+T opens the contextual six-petal Tools face,
  and Ctrl+Alt+A opens all six File actions.
- Cursor position is queried before key chords even if the mouse has not moved since startup. Explorer's transient
  empty selection is retried without blocking the UI or reading another foreground window.
- Final portable build: Ctrl+Alt+A opened the six-action pie and a subsequent Escape visually closed it.
- Click pies request activation. If Windows leaves them inactive, the hook can forward Tab/wheel/Escape while visible.
- Windows native share sheet was visually observed with the generated PNG and the Nearby sharing entry. It was closed
  without selecting a recipient. No file transmission was tested or performed.
- No Windows startup, Explorer integration, security or privacy settings were changed.

The Windows share source follows Microsoft's desktop DataTransferManager interop pattern:
https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/integrate-sharesheet-send

## Artifacts

- Portable directory: `artifacts/Dilim-Windows`
- Root launcher: `../Dilim Başlat.vbs`
- Short offline HTML guide: `../Dilim Rehber.html`
- Source guides: `docs/DILIM-REHBER.html`, `docs/DILIM-REHBER.md`
- Detailed behavior and limits: `docs/DILIM-TOOL-MATRIX.md`
- Test evidence: each test project's ignored `TestResults/dilim-*.trx`

Tools use explicit operation fields rather than a layered/timeline editor. Video redaction does not track motion;
PDF metadata removal does not remove visible content. Sharing requires the user's recipient choice and Windows
sharing configuration. These limits are also stated in the user guide.
