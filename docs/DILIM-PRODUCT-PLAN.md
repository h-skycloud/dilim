# Dilim contributors converter product plan

Product: Dilim. The owner selected the final name and a monochrome four-slice icon on 21 October 2026. Repository: https://github.com/h-skycloud/dilim. Personal website navigation and studio branding were removed from the app.

Visual thesis: a calm local desktop workspace with a single orange accent and a recognizable radial action menu.
Content plan: conversion workspace first, editable presets next, clear results/history, product and source attribution in About and the repository.
Interaction thesis: keep the existing pie entrance, front/back flip and progress/completion transitions; respect reduced motion.

## Acceptance gates

- [x] Inventory every FileConverter output, input family and setting against the installed 2.1.0 defaults and inspected upstream source.
- [x] Advanced FFmpeg options, audio tempo/pitch and VBR/legacy quality controls.
- [x] Image power-of-two square / independent maximum-side constraints; exact-size video presets.
- [x] Explicit post-conversion keep/archive/delete policies, safe defaults and success-only processing.
- [x] FileConverter preset import with an honest report of unsupported settings and disabled destructive imported actions.
- [x] Missing formats and CD audio support, with hardware limitations stated in validation.
- [x] Preset organization and Explorer context-menu workflow.
- [x] Dilim product identity, isolated installation/settings/IPC/registry identity; preserve upstream MIT attribution.
- [x] Installer and portable release packages, optional pinned engine downloads, licenses/source information, reproducible build and checksums.
- [x] GitHub-ready README, contribution/release documentation and CI. Do not publish under a guessed owner/name.
- [x] Functional, integration, UI and installer sandbox checks; real-file parity evidence before advising removal.

FileConverter remains installed. Its removal is a separate joint step requested by the owner, after the replacement has passed the acceptance gates.


Scope note: the owner does not use CDA/DVD extraction. Those eight presets remain available but physical disc hardware has not been validated; it does not block their photo/PDF/video migration. See DILIM-VALIDATION.md for exact evidence and limitations.
