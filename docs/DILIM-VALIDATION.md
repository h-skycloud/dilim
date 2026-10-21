# Dilim contributors validation — 21 October 2026

Scope: replace the owner's FileConverter 2.1.0 usage for photos, PDF and video, package an independently branded Windows distribution, preserve open-source attribution. Working name: Dilim. Website: https://github.com/h-skycloud/dilim.

## Conversion parity

The installed FileConverter configuration contains 75 presets, matching its installed defaults; 14 contain custom FFmpeg output options. The XML was backed up outside this repository before work. No FileConverter GPL implementation was copied into the application.

The production importer read all **75 presets with zero warnings**. All **67 non-disc presets produced real output** from generated PNG, two-page PDF, H.264 video or WAV input; source SHA-256 hashes stayed unchanged. This includes all 16 FileConverter target types, 25%/75% scale, left/right rotation, exact 720p/1080p custom commands and the four audio tempo/pitch presets. The eight CDA/DVD extraction presets were excluded because the owner explicitly does not use them; physical drive behavior remains unverified.

Evidence: ignored `artifacts/fileconverter-acceptance-final/report.json`. Reproduce from the repository:
`dotnet run --no-cache build/tools/check-fileconverter-presets.cs -- SETTINGS.XML artifacts/parity`.
The optional third argument imports verified presets into a specified local data directory; user data never enters a release.

New behavior includes maximum-side and power-of-two square image sizing, independent audio tempo/pitch, explicit CBR/VBR and quality, legacy video quantizers, exact video dimensions, validated FFmpeg output arguments, folders, all-compatible-preset execution, and explicit keep/archive/delete after wholly successful local jobs. Imported destructive source actions default to Keep and produce a warning. Custom argument parsing excludes additional inputs/output paths and uses ArgumentList rather than a shell.

Separate integration tests verify tempo duration and measured pitch, video size/FPS/channels, source preservation, PDF text preservation and custom compression DPI/quality. See earlier detailed evidence in DILIM-CONVERTER-VALIDATION.md.

## Automated evidence

| Scope | Passed | Skipped | Evidence |
|---|---:|---:|---|
| Complete Core | 431 | 2 Windows case-sensitive-file skips | dilim-core-final.trx |
| Complete Setup | 126 | 0 | dilim-setup-final.trx |
| Selected app workflows | 69 | 0 | dilim-app-final.trx |
| App engine/catalog/localization follow-up | 36 (overlaps app scope) | 0 | dilim-app-engines.trx |
| Conversion/tool/PDF/video engine scope | 58 | 0 | dilim-engines-final.trx |
| Engine installer/runtime scope | 37 | 0 | dilim-engine-install.trx |
| Complete platform suite | 48 | 3 non-Windows skips | dilim-platform.trx |

TRX files remain in ignored TestResults directories. Full upstream engine validation previously had eight OFD failures, reproduced unchanged in the pristine upstream base; the entire engine suite is not claimed green. Historical UI timing/watchdog limitations are recorded in DILIM-VALIDATION.md. Existing Avalonia obsolete API and nullable test warnings remain.

The production EngineInstaller successfully verified, extracted, marked and ran the exact pinned 7-Zip MSI and FFmpeg ZIP served from the build cache. Run `dotnet run --no-cache build/tools/check-engine-downloads.cs` after fetching those archives. The public package offers these engines for download; no dependency on FileConverter's engine directory is introduced.

## Product and packaging

- New Dilim contributors icon, attribution, website, title and version 0.1.0.
- Independent per-user installation, settings, cache, IPC, Explorer registry key, startup value and installation GUID. Original Filee remains separately identifiable.
- Modern Explorer package family was checked using Windows' family-name calculation: Dilim.Dilim.ExplorerMenu_qp53ahev3283g. Classic Explorer integration is the default; optional modern registration was not applied to the user's Windows installation.
- The actual Setup payload passed sandbox install, update and uninstall. Foreign files, settings and a stand-in upstream Filee installation survived. Evidence: ignored artifacts/dilim-installer-validation.json.
- Public installer/portable omit optional engine executables; first run offers pinned checksum-verified upstream downloads. Local offline builds carry separate engine redistribution/source obligations described in OPEN-SOURCE.md.
- Release source enumeration excludes ignored data, history, references, caches and generated outputs. Public artifacts omit development PDBs.
- No FileConverter uninstall, GitHub repository creation, public upload or real Explorer/startup registration was performed.

Native pie, Explorer shortcut, Escape and real video compression were previously observed and are detailed in DILIM-FINAL-VALIDATION.md and DILIM-CONVERTER-VALIDATION.md. The final native Windows application also displayed Dilim/Dilim contributors, version 0.1.0, the Dilim website link, the all-compatible-presets selector, import button, folder filter and expanded preset editor. Headless render checks cover normal/minimum widths; an integration test executes an imported preset absent from the pie through the full library selector.

## Acceptance meaning

The owner's stated photo/PDF/video workflows can migrate after these checks. This does not claim byte-identical encodes, universally smaller files, or superior visual quality for every input. Disc hardware extraction and every rare/corrupt input are not certified. Windows x64 is the validated release target. Code signing and the final public name/repository remain maintainer choices.

FileConverter remains installed and its settings backup is retained for the separately requested joint removal step.
