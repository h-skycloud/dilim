# Contributing to Dilim

Dilim is an open-source application based on Filee. Website: https://github.com/h-skycloud/dilim.

Use the .NET SDK pinned in global.json and build Filee.slnx. Internal Filee.* project identifiers remain for compatibility. Start with README.md and docs/DILIM-RELEASE.md.

Keep changes focused, preserve original copyright and third-party notices, and describe what changed and how it was checked. Use generated samples in tests; never add personal documents, settings, history, keys or logs. Conversion changes should verify content/metadata and source preservation, not just exit codes. Avoid overwriting sources by default. Report known failures honestly.

Before proposing an engine, document its pinned version, integrity, license and corresponding-source requirements. FileConverter is a behavior reference; do not copy its GPL implementation into MIT application code.
