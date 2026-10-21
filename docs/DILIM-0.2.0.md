# Dilim 0.2.0 — 21 October 2026

The maintainer selected **Dilim** as the final product name. The new monochrome four-slice mark was independently redrawn as SVG from the maintainer's supplied reference and rendered into PNG/Windows ICO assets. The main header contains only Dilim. Personal website navigation and studio branding were removed from compiled application resources.

Product, settings/cache paths, IPC, startup/context-menu identity, shortcut, package scripts, documentation and update/issue links use Dilim. Repository: https://github.com/h-skycloud/dilim. Upstream Filee namespace/assembly identifiers remain implementation details; original MIT attribution is preserved.

Validation on Windows x64:

- Core: 431 passed, 2 case-sensitive-filesystem skips.
- App: 128 passed, including 54 page renders across supported languages, localization, preset editing/library execution, shortcuts and update behavior.
- Setup: 126 passed.
- Selected conversion/tools/engine-installer/runtime scope: 95 passed.
- Actual Setup payload sandbox: install, update and uninstall passed; settings, foreign files and a separate upstream installation survived.
- Windows calculated the modern extension family as `Dilim.ExplorerMenu_8z99cddef50t0`.
- Turkish catalogue render was visually inspected: Dilim title/icon, no studio subtitle or personal-website button.

Earlier conversion-parity evidence is in DILIM-VALIDATION.md. Physical CD/DVD extraction remains unverified and outside the owner's usage. The whole upstream engine suite is not claimed green; known OFD failures are recorded in DILIM-INITIAL-VALIDATION.md.

The public installer/portable package contains the application and required native runtimes; optional media/archive engines are installed separately from checksum-pinned original archives. Local user settings, history, backups, generated media and downloaded engines are excluded from source distribution. Code signing is not configured.
