# Dilim release guide

Repository: https://github.com/h-skycloud/dilim . Current version: 1.0.0.

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
dotnet test tests/Dilim.Core.Tests
dotnet test tests/Dilim.Setup.Tests
dotnet test tests/Dilim.App.Tests --filter "FullyQualifiedName~CatalogTests|FullyQualifiedName~DilimBrandingTests|FullyQualifiedName~ConversionSettingsTests|FullyQualifiedName~LocalizationTests|FullyQualifiedName~SingleInstanceTests|FullyQualifiedName~UpdateServiceTests|FullyQualifiedName~DilimPieTests"
dotnet test tests/Dilim.Engines.Tests --filter "FullyQualifiedName~FileConverterParityTests|FullyQualifiedName~VideoTuningTests|FullyQualifiedName~PdfCompressionTests|FullyQualifiedName~DilimToolTests|FullyQualifiedName~EngineInstallerTests"
```

Full upstream engine validation has eight known OFD failures reproduced in the pristine base; do not claim it all passes. See DILIM-INITIAL-VALIDATION.md and DILIM-VALIDATION.md.

## Publish — every new version

1. Raise `<Version>` in Directory.Build.props (e.g. 0.3.0 → 0.3.1).
2. Add a `## 0.3.1` section at the top of CHANGELOG.md, starting with its `<!-- update -->` block:
   `level:` (`required` / `recommended` / `optional`) and one-line summaries per language (`tr:`, `en:`, …). The
   update card in installed copies shows exactly these lines; the release workflow refuses a version without them.
3. Commit and push to `main`.
4. GitHub → Actions → **Dilim release** → Run workflow. Tick **publish** to release at once, or leave it unticked
   for a draft, check the assets and publish it by hand.

Installed copies ask GitHub for `releases/latest` at start and every few hours. They see the new version only when
the release is **published** (a draft or a pre-release is invisible to them), its tag is `vVERSION`, and it carries
`Dilim-VERSION-win-Setup.exe` and `Dilim-VERSION-SHA256SUMS.txt`: "Update" downloads that Setup, checks it against
the checksum file and runs it. Portable copies get a link to the release page instead.

### Website download button

Every release also carries version-less copies, so a website link never has to change:

- Setup: `https://github.com/h-skycloud/dilim/releases/latest/download/Dilim-Setup.exe`
- Portable: `https://github.com/h-skycloud/dilim/releases/latest/download/Dilim-Portable.zip`
- Release page: `https://github.com/h-skycloud/dilim/releases/latest`

### Notes

Preserve the MIT copyright notice in LICENSE and THIRD-PARTY-NOTICES.md. Commit reviewed source without personal
settings, history, logs, caches or backups. Code signing is not configured; binaries are unsigned (Windows
SmartScreen warns on first download). Keep ProductIdentity.InstallId stable: it is how Setup finds the previous
installation. Windows x64 is the validated target.

## winget

Every release carries `Dilim-<version>-winget-manifests.zip`: the version, installer and locale manifests of
`Dilim.Dilim`, with the Setup's address and SHA-256 filled in. To publish a version on winget, unzip it and open a pull
request to `microsoft/winget-pkgs` with the `manifests/d/Dilim/Dilim/<version>` folder, or let wingetcreate do it:

```
wingetcreate submit --token <GitHub token> manifests/d/Dilim/Dilim/<version>
```

The first submission is reviewed by the winget team; later versions only need the new folder. Setup takes Inno's
silent switches, so `winget install Dilim.Dilim` and `winget upgrade` install it without the wizard.
