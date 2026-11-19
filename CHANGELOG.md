# Changelog

What changed in each version of Dilim, for the people using it. The release workflow puts the section of the version it
publishes in the GitHub release notes.

Start a section with an `<!-- update -->` block (hidden on GitHub). The app's update notice shows its level and its
one-line summaries in the user's language (a line per change; a language without lines shows the English ones):

- `required`: wrong or lost results, security, Dilim not starting. The card shows every time Dilim starts.
- `recommended`: bug fixes. The card shows once per version.
- `optional`: new features only. No card, the sidebar button only.

## 0.3.1

<!-- update
level: recommended
tr: Dilim Windows açılırken tepside başlar; kısayollar her an hazırdır
tr: Pencereyi kapatınca Dilim tepside çalışmaya devam eder (Genel ayarlarından değiştirilebilir)
tr: Tema sayfasındaki önizleme sadeleşti: örnek kontroller vurgu rengini gösterir, ayarlar anında kaydedilir
en: Dilim starts in the tray when Windows starts, so its shortcuts are always ready
en: Closing the window keeps Dilim running in the tray (change it under General)
en: The Theme preview is clearer: sample controls show the accent, settings save as you change them
-->

- **Ready in the background:** Dilim starts in the tray when you sign in to Windows and keeps running there when its
  window is closed, so the pie shortcuts and the drag gesture always work. Both can be changed under General; Setup
  ticks "Start with Windows" by default.
- **Theme preview:** the sample button, switch and progress bar under the pie show the accent colour and can no longer
  be mistaken for settings. Theme changes are saved as you make them.

## 0.3.0

<!-- update
level: recommended
tr: Raf: dosyaları rafa bırak, bir ya da birkaçını istediğin uygulamaya sürükle; raf yeniden başlatınca da hatırlar
tr: Raf tepsi menüsünden açılır; klasörde göster, yolu kopyala ve dönüştürmeye gönder
tr: Yeni kurulum ekranı ve "Yenilikler" slaytları
en: The shelf: drop files on it and drag one or several into any app; it remembers them after a restart
en: Open the shelf from the tray; show in folder, copy path and send to conversion
en: A new setup window and "What's new" slides
-->

- **Shelf (Raf):** drag one file or a selection out into Explorer, mail, chat or any app. Drop files and folders on
  the shelf or pick them with +. Ctrl/Shift multi-select, Delete, Ctrl+A, Ctrl+C (paths), Enter (open), Alt+↑/↓
  (reorder), right-click menu (open, show in folder, copy path, convert, remove). The shelf keeps references only and
  remembers them after a restart. Tray menu → Shelf.
- **Setup and slides:** the setup window draws Dilim's four-slice mark; "What's new" shows the shelf, the pie, the
  catalogue, themes and languages.

## 0.2.0

First Dilim release: product name and mark, radial tool and file-action pies, FileConverter preset import, catalogue,
standalone Windows Setup and portable packages. See docs/DILIM-0.2.0.md.
