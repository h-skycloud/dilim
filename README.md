# Dilim

<img src="src/Dilim.App/Assets/Icons/dilim.png" width="112" alt="Dilim four-slice icon">

A local, open-source Windows file converter with a radial menu.

[Downloads](https://github.com/h-skycloud/dilim/releases) · [Türkçe rehber](docs/DILIM-REHBER.md) · [License](LICENSE) · [Attribution](OPEN-SOURCE.md)

## Use

Install `Dilim-1.0.0-win-Setup.exe`, or extract the portable ZIP and run `Dilim Başlat.vbs`. No separate .NET installation is required. Select a file in Explorer and press **Ctrl + Alt + Space**. **Ctrl + Alt + T** opens tools; **Ctrl + Alt + A** opens file actions. Tab flips the pie; Escape closes it.

Choose FFmpeg and 7-Zip on first run for video/audio and archive operations. Engines are downloaded from pinned, checksum-verified original releases. After installation conversions work offline. Files are processed locally; media is not uploaded. The app has no personal-website shortcut.

## Features

- Every tool in its own window with a live preview: photos (crop, edit, annotate, redact with face/text detection, collage, metadata), video (speed, frames, crop, trim, split, redact, join), audio (trim, channels, normalize, visualizer, bleep) and compression with a target size.
- PDF size reducer (Smart or Flatten, five levels, measured size and quality, target size), page organizer, merge and split.
- Video H.264/HEVC/AV1/VP9, CRF, encoder speed, FPS, exact/scaled dimensions, rotation, hardware encoding and custom FFmpeg output options.
- Audio conversion, CBR/VBR, sample format/rate, independent tempo and pitch.
- FileConverter XML preset import, folders, a complete compatible-preset list and editable pie profiles.
- Batch queue, history, cancellation, Explorer integration, file actions and a floating shelf.
- Keep originals by default; explicitly selected archive/delete actions run only after wholly successful local conversions.

Installed settings: `%APPDATA%/Dilim`. Downloaded engines: `%LOCALAPPDATA%/Dilim/engines`. Portable settings stay in its local `data` directory. Windows x64 is the validated distribution; macOS/Linux inheritance is not a validated Dilim release. Installer binaries are currently unsigned.

## Develop and package

Use the SDK pinned in `global.json`, Git and PowerShell 7:

```powershell
./build/fetch-fonts.ps1
dotnet build Dilim.slnx
./build/fetch-engines.ps1 -Only ffmpeg,7zip
./build/build-dilim-release.ps1 -RepositoryUrl https://github.com/h-skycloud/dilim
./build/check-dilim-installer.ps1
```

`Releases/` receives Setup, Portable, application source ZIP and SHA-256 checksums. User settings, logs, caches and generated test media are excluded. The default public package downloads optional engines separately. Before publicly distributing an offline `-BundleEngines` variant, fulfill exact engine license/corresponding-source obligations in OPEN-SOURCE.md.

See [release/build guide](docs/DILIM-RELEASE.md), [conversion validation](docs/DILIM-VALIDATION.md) and [contribution guide](CONTRIBUTING.md).

## License

Dilim is open source under the MIT license; see [LICENSE](LICENSE) and [OPEN-SOURCE.md](OPEN-SOURCE.md). Dependencies and optional engines keep their own licenses; see THIRD-PARTY-NOTICES.md. FileConverter's configuration/behavior is a compatibility reference; its GPL source is not incorporated into Dilim.
