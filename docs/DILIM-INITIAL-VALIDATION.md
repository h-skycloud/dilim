# Dilim UI validation — first delivery, 20 October 2026

Historical report. See DILIM-FINAL-VALIDATION.md for the completed Windows delivery.

Base: `85f7e56156b373e889a725e7d245d8024b3d5f70`, Windows x64, .NET SDK 10.0.302.

## Final-source checks

- Application build: passed (`dotnet build src/Filee.App --no-restore`).
- Formatting: passed (`dotnet format Filee.slnx --verify-no-changes --no-restore`).
- Whitespace: passed (`git diff --check`).
- Final focused tests: **33 passed, 0 failed, 0 skipped**. These run from a separate output
  directory (`--artifacts-path artifacts/ui-validation`) so the full-suite runner can finish
  with its own loaded assemblies.
- All nine translation files: **884 identical keys**, matching numbered placeholders.
- English/Turkish catalogue screenshots: 1080 px; Turkish minimum-width screenshot: 820 px.
- Real catalogue conversion test: PNG → WEBP twice, original SHA-256 unchanged,
  distinct output names, correct format and image dimensions.
- Missing/deleted file and unavailable-engine tests: no conversion starts; reason is displayed.
- Quick language switch: persisted to settings; selected files and catalogue page retained.
- Custom theme test: passed with the updated light surface colour.

Focused command:

```powershell
dotnet test tests/Filee.App.Tests --artifacts-path artifacts/ui-validation `
  --filter 'FullyQualifiedName~CatalogTests|FullyQualifiedName~LocalizationTests|FullyQualifiedName~LocalizationResourceTests|FullyQualifiedName~Custom_theme_colours_replace_the_built_in_ones_and_go_away_with_the_file'
```

## Wider checks

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Core | 425 | 0 | 2 |
| Platform | 48 | 0 | 3 |
| CLI | 26 | 0 | 1 |
| Setup | 126 | 0 | 0 |
| Engines | 1,369 | 8 | 54 |

The eight engine failures are **the same eight OFD tests** reproduced in the unmodified
base checkout at the exact same commit. No engine implementation was changed here.
The comparison of failed test-name sets was exact.

Two wider UI failures were also independently reproduced in that unmodified checkout:

- `UiWatchdogTests.A_stall_is_reported_once_and_its_end_with_the_length`: the host culture
  expects `12,5` while the diagnostic message contains `12.5`.
- `RenderTests.Engines_page_shows_versions_descriptions_and_download_speed`: the locally
  discovered Ghostscript and Calibre copies do not report the version strings expected by the test.

The theme-colour expectation was intentionally updated from `#F6F5F1` to `#F4F4F4`
and then passed in the final focused run. These checks do not establish that the entire upstream
suite is green. TRX outputs are in each test project's ignored `TestResults` folder.

The full UI run was stopped after approximately 19 minutes of the broad multilingual rendering
suite. It did not produce a completed suite summary and must be treated as incomplete. The other
five suites above completed. The final-source focused UI run completed separately with 33 passes.

## Native Windows check

The actual development executable was opened using `build/run-dilim.ps1`:

1. A generated `dilim örnek.png` was opened with the native Windows file picker.
2. EN/TR was switched while the file remained selected.
3. Clicking WEBP on the wheel produced a real WEBP, recorded a completed history entry,
   and displayed the completed result with an Open folder button.
4. Repeating the conversion created numbered copies instead of replacing the previous output.
5. Output format/dimensions were independently checked: WEBP, 640 × 420.

The app remains open with its isolated development profile. Windows startup/Explorer
registrations are not changed (`FILEE_DATA_DIR` isolation, also confirmed in its startup log).

Proof image: `artifacts/dilim-ui-windows.jpg`.
Headless final screenshots: `artifacts/ui-validation/bin/Filee.App.Tests/debug/screenshots`.
