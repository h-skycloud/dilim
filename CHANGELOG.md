# Changelog

What changed in each version of Dilim, for the people using it. The release workflow puts the section of the version it
publishes in the GitHub release notes.

Start a section with an `<!-- update -->` block (hidden on GitHub). The app's update notice shows its level and its
one-line summaries in the user's language (a line per change; a language without lines shows the English ones):

- `required`: wrong or lost results, security, Dilim not starting. The card shows every time Dilim starts.
- `recommended`: bug fixes. The card shows once per version.
- `optional`: new features only. No card, the sidebar button only.

## 0.3.0

<!-- update
level: recommended
tr: Raf yeniden yapıldı: dosya bırak, birden fazlasını başka uygulamaya sürükle, yeniden başlatınca hatırlar
tr: Raf tepsi menüsünden de açılır; klasörde göster, yolu kopyala ve pie menüsüne gönder eklendi
tr: Dilim artık tamamen kendi adıyla: proje, dosya ve kurulum adları Dilim oldu
en: The shelf was rebuilt: drop files on it, drag several out to any app, and it remembers them after a restart
en: Open the shelf from the tray; show in folder, copy path and send to the pie were added
en: Dilim now stands on its own: project, file and installer names are Dilim
-->

- **Shelf (Raf):** rows can now really be dragged out (one or a selection) into Explorer, mail, chat or any app. Files
  and folders can be dropped on the shelf or picked with +. Ctrl/Shift multi-select, Delete, Ctrl+A, Ctrl+C (paths),
  Enter (open), Alt+↑/↓ (reorder), right-click menu (open, show in folder, copy path, convert, remove). References are
  kept in `shelf.txt` next to the settings; a file moved away by a drag leaves the shelf. Tray menu → Shelf.
- **Independent identity:** every project, namespace, assembly, executable (`Dilim.exe`, `dilim-cli`), environment
  variable (`DILIM_DATA_DIR`), installer payload and package identifier is Dilim's own. Engines installed by 0.2 are
  still recognised and not downloaded again.
- Filee's sponsor/supporter screens, its 2.0 "What's new" slides, its logo and website links were removed. The setup
  window draws Dilim's four-slice mark; the "What's new" slides of 0.3.0 show Dilim.

## 0.2.0

First Dilim release: product name and mark, radial tool and file-action pies, FileConverter preset import, catalogue,
standalone Windows Setup and portable packages. See docs/DILIM-0.2.0.md.
