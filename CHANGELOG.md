# Changelog

What changed in each version of Dilim, for the people using it. The release workflow puts the section of the version it
publishes in the GitHub release notes.

Start a section with an `<!-- update -->` block (hidden on GitHub). The app's update notice shows its level and its
one-line summaries in the user's language (a line per change; a language without lines shows the English ones):

- `required`: wrong or lost results, security, Dilim not starting. The card shows every time Dilim starts.
- `recommended`: bug fixes. The card shows once per version.
- `optional`: new features only. No card, the sidebar button only.

## 0.5.0

<!-- update
level: recommended
tr: PDF sayfalarını düzenle: sayfaları sürükleyerek sırala, döndür, çoğalt ya da kaldır
tr: Birden çok PDF seçince sayfaları tek dosyada birleşir; istersen hepsi aynı genişlikte
tr: Güncellemeden sonra yeni sürüm, arkada açık kalan eski Dilim'in yerini alır
en: Organize PDF pages: drag to reorder, rotate, duplicate or remove pages
en: Pages of several PDFs are combined into one file; optionally all the same width
en: After an update the new version replaces an older Dilim still running in the background
-->

- **Organize PDF pages:** every page as a card to drag into a new order, rotate, duplicate or remove; "Reset Order"
  brings the original back. Keyboard: ←/→ select, Alt+←/→ move, R rotate, Ctrl+D duplicate, Delete remove. With
  several PDFs selected, their pages are combined into one file. "Make all pages the same width" scales pages to the
  narrowest one.
- **Updates take effect at once:** when an older Dilim is still running (started with Windows, or installed in another
  folder), opening the new version asks it to quit and takes its place.

## 0.4.0

<!-- update
level: optional
tr: PDF boyutunu düşür: Akıllı ya da Düzleştir, beş seviye, ölçülen boyut ve kalite, hedef boyut
tr: Kaliteyi karşılaştır: aynı sayfanın önce ve sonra hali yan yana
tr: PDF birleştir ve Videoları birleştir yeni pencerelerinde; sırayı küçük görsellerle düzenle
en: Reduce PDF size: Smart or Flatten, five levels, measured size and quality, a target size
en: Compare quality: the same part of a page before and after, side by side
en: Merge PDFs and Join Videos in new windows; arrange the order with thumbnails
-->

- **Reduce PDF size:** pick Smart (only pictures are downsized; text and lines stay sharp and selectable) or Flatten
  (every page becomes one picture, for drawing-heavy sheets), then one of five levels: Light, Print, Balanced, Screen,
  Smallest. The size and quality shown are measured on your file, not estimated. Type a target size, or pick 5, 10, 20
  or 25 MB, and the best quality that stays under it is chosen, between levels too. "Compare quality" shows the same
  part of a page before and after.
- **Merge PDFs and Join Videos** open in a new window: the files with their first page or a frame, in the order you
  selected them, to rearrange before merging. Joined videos take their size and frame rate from the first clip; clips
  of other shapes are fitted inside it, and clips with and without sound can be joined together.

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
