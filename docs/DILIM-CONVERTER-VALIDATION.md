# Converter settings validation · 2026-10-21

Scope: FileConverter comparison, persistent video tuning, configurable quick compression for images/PDF/video/audio, custom PDF image compression settings.

## Automated checks

- Engines: **168 passed, 0 failed, 0 skipped** (`dilim-converter-engines-final.trx`). Includes existing MediaTests, PdfCompressionTests, DilimToolTests and new VideoTuningTests.
- Settings library: **70 passed** (`dilim-converter-core.trx`). Includes saving/reloading new options, cloning and legacy defaults.
- App: **60 passed** (`dilim-converter-app.trx`) plus **one additional UI binding/render test** from `dilim-converter-controls.trx` (5 passed, 4 overlap): **61 distinct tests**. Includes localization, catalogue, preset editor and compression request mapping.
- Final video test rerun after fixing audio channel application: **11 passed** (`dilim-converter-video-final.trx`, overlap with the engine suite).
- Total across those three final scopes: **299 distinct passing tests**. This is a targeted validation, not a claim that every unrelated upstream test passes.
- Solution build and self-contained win-x64 publish succeeded. Existing obsolete Avalonia control/bitmap and nullable test warnings remain.
- Formatting corrected and whitespace verification passed for changed C# files; `git diff --check` passed.
- Final localization-only check: **24 passed**, overlapping the app scope above (`dilim-converter-localization-final.trx`).

The first engine run exposed a real issue: video presets rejected audio stream-copy for a requested channel change but did not send that channel choice to the encoder. The video path now applies ChannelAndRateArguments after the codec arguments. The final 168-test run is green.
An initial Debug build with `--no-restore` encountered the repository's conditional diagnostics dependency after Release publishing; a normal restore/build succeeded without changing Program.cs.

## Actual file behavior

- Generated H.264, HEVC and VP9 conversions: rotation, percentage scale, maximum height, exact 12 FPS, mono audio verified by ffprobe; source hashes unchanged.
- Generated GIF: 8 frames, 120×160 output after rotation/scale/FPS.
- Generated PDF: custom 100 DPI overrides Print/300 DPI even with the optional Ghostscript flag present. Image width becomes about 300 px at its 3-inch page size; extracted text matches the input and the file is smaller. Custom settings use the built-in compressor.
- Turkish preset editor rendered with 23.976 FPS; changing NumericUpDown to 29.97 updates the VM to the correct decimal value.
- Native portable app opened the actual video compression tool. Selected height **180**, FPS **12**, CRF **30**, H.264. The screen reported **Tamamlandı**; bundled ffprobe verified **320×180, 12/1 FPS, H.264 + AAC, 2 seconds** in `artifacts/samples/Dilim/video-ayar-ornek.mp4`.
- Native inspection found an outdated fixed-value compression description. It was replaced with a description of the new selectable settings before final publication.

## Delivery and user state

- Portable app: `artifacts/Dilim-Windows`, root launcher `../Dilim Başlat.vbs`.
- Both HTML guides and the Markdown guide updated with video/compression instructions.
- FileConverter 2.1.0 installation and settings retained. Its 75 preset configurations matched the installed defaults; 14 default presets contain custom FFmpeg commands. Those commands were inspected as text, not executed.
- FileConverter settings backup: `../references/fileconverter-settings-backup/Settings.user.xml`.
- Dilim pre-update `settings.json` backed up to `../references/Dilim-before-converter-update` before publishing. No persisted presets/profiles files existed in this portable profile; its built-in library remains available. Existing portable data retained.
- No recipient messages, file uploads, external writes, uninstall, Windows startup or Explorer registration changes.

Limits and comparison sources are in `DILIM-FILECONVERTER-KARSILASTIRMA.md`; usage is in `DILIM-REHBER.md`.
