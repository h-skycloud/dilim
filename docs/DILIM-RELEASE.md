# Dilim release guide

Repository: https://github.com/h-skycloud/dilim . Current version: 0.2.0.

## Build

Use Windows x64, the SDK in global.json, Git and PowerShell 7.

```powershell
./build/fetch-fonts.ps1
./build/build-dilim-release.ps1 -RepositoryUrl https://github.com/h-skycloud/dilim
./build/check-dilim-installer.ps1
```

Outputs: Setup, portable ZIP, application source ZIP and SHA-256 checksums in Releases/. Self-contained; no separate .NET installation. Default installation is per-user. Classic Explorer menu, optional startup/PATH/desktop integration, optional modern extension (`-IncludeModernMenu`).

The public package includes app-local C++ runtimes/native libraries and their notices. FFmpeg, 7-Zip and other optional engines are fetched separately from checksum-pinned original archives. The production download installer verifies content before extraction. After installing the required engines, conversion works offline. FileConverter's installed engines are never used.

Offline `-BundleEngines` is for local testing until the exact engine corresponding-source/redistribution obligations in OPEN-SOURCE.md have been fulfilled.

## Validation

Run projects sequentially because generated files are shared.

```powershell
dotnet test tests/Filee.Core.Tests
dotnet test tests/Filee.Setup.Tests
dotnet test tests/Filee.App.Tests --filter "FullyQualifiedName~CatalogTests|FullyQualifiedName~DilimBrandingTests|FullyQualifiedName~ConversionSettingsTests|FullyQualifiedName~LocalizationTests|FullyQualifiedName~SingleInstanceTests|FullyQualifiedName~UpdateServiceTests|FullyQualifiedName~DilimPieTests"
dotnet test tests/Filee.Engines.Tests --filter "FullyQualifiedName~FileConverterParityTests|FullyQualifiedName~VideoTuningTests|FullyQualifiedName~PdfCompressionTests|FullyQualifiedName~DilimToolTests|FullyQualifiedName~EngineInstallerTests"
```

Full upstream engine validation has eight known OFD failures reproduced in the pristine base; do not claim it all passes. See DILIM-INITIAL-VALIDATION.md and DILIM-VALIDATION.md.

## Publish

Preserve the original MIT attribution and THIRD-PARTY-NOTICES.md. Commit reviewed source without personal settings, history, logs, caches or backups. The manual Release workflow builds a draft; review assets and checksums before publishing it. Application releases use `vVERSION` tags and `Dilim-VERSION-win-Setup.exe` naming for the updater.

Code signing is not configured; binaries are unsigned. Keep ProductIdentity.InstallId stable for future upgrades. Windows x64 is the validated target; inherited macOS/Linux implementations are not certified Dilim releases.
