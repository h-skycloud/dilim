# Open source and attribution

Dilim is an open-source distribution developed from [Filee](https://github.com/KnifeLemon/Filee), by Myeonggu Kim (KnifeLemon) and contributors. The original MIT copyright and license are preserved in LICENSE. Dilim contributions are also MIT-licensed. Internal `Filee.*` project and assembly names remain as technical identifiers.

The product website is https://github.com/h-skycloud/dilim. The public name is Dilim; the repository is h-skycloud/dilim. No download, popularity or signing claims refer to the upstream product.

FileConverter's behavior and the user's XML settings are used as compatibility references. Its GPL source code has not been copied into the application. Tests use independently authored fixtures.

Third-party libraries and conversion engines retain their own licenses; see THIRD-PARTY-NOTICES.md and the license files inside each engine directory. They are not relicensed as MIT.

## Optional engines and local offline builds

- FFmpeg 9.0.2 full shared Windows build: https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2 . The release identifies FFmpeg source commit `946fcce07b`; source: https://github.com/FFmpeg/FFmpeg/tree/946fcce07b . Build configuration and dependency information are shipped with the original engine archive. Source/build project: https://github.com/GyanD/codexffmpeg . GPL-3.0-or-later build, executed as a separate process.
- 7-Zip: https://www.7-zip.org/ . LGPL and unRAR restrictions apply to the distributed components; source: https://www.7-zip.org/download.html . Original license files are retained.
- rhwp: https://github.com/edwardkim/rhwp . MIT. Optional formats beyond FileConverter's scope use separately downloaded engines with their own notices.
- Microsoft C++ runtime: distributed app-locally from the version/checksum pinned in ENGINE-VERSIONS.json; its license notices are retained by the engine fetch process.

The default public package does not bundle FFmpeg, 7-Zip or rhwp executables: the application downloads the pinned original engine archives when selected. For the optional offline `-BundleEngines` variant, before public distribution verify the exact engine build's complete corresponding-source and redistribution requirements, including enabled codec libraries, and make the required source available alongside the binary release. The source ZIP contains this application's source, not every engine's source. This provenance record is not a substitute for engine obligations.

## Release contents

The release builder starts from a clean staging directory. It excludes developer caches, downloaded source references, `.git`, user settings, history, backups and generated test outputs. App source and licenses are packaged separately from binaries. No Windows registry changes are made merely by building a package.
