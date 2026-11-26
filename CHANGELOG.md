# Changelog

What changed in each version of Dilim, for the people using it. The release workflow puts the section of the version it
publishes in the GitHub release notes.

Start a section with an `<!-- update -->` block (hidden on GitHub). The app's update notice shows its level and its
one-line summaries in the user's language (a line per change; a language without lines shows the English ones):

- `required`: wrong or lost results, security, Dilim not starting. The card shows every time Dilim starts.
- `recommended`: bug fixes. The card shows once per version.
- `optional`: new features only. No card, the sidebar button only.

## 1.1.0

<!-- update
level: recommended
tr: Tüm pencereler yeni kart tasarımında: düzenleyiciler, adlandırma, motor kurulumu, raf ve bildirimler
tr: Pie'da hızlı seçim: 1–9 tuşları, yön ile seçim, turbo mod, sağ tık ile geri, ekran ortasında açma
tr: Raf: küçük önizlemeler, proje başına raflar, Shift ile sürükleyip pie dilimine bırakma, sonuçları rafa ekleme
tr: Uygulamaya özel pie, pie dışa/içe aktarma ve kendi dilimlerin (web adresi, komut, programla aç)
en: Every window in the new card design: editors, renaming, engine setup, shelf and notices
en: Quicker pie: keys 1–9, choosing by direction, turbo mode, right-click to go back, opening in the middle
en: Shelf: thumbnails, a shelf per project, Shift+drag onto a pie slice, results added to the shelf
en: Pies for specific apps, pie export/import and slices of your own (web address, command, open with)
-->

- **One design everywhere.** The preset and group editors, file renaming, LibreOffice options, the close question,
  engine setup, messages and the shelf open in the same soft card as the tools, with a round close button and the
  buttons at the bottom. The corner notices share its corners, and the update card links to the full notes.
- **Choosing faster in the pie** (Settings → Shortcuts → Quick choice): keys 1–9 and 0 choose a slice; past the ring,
  the slice the pointer points at is chosen; opened with a shortcut, move to a slice while holding the keys and let go
  (turbo); right-click or the centre goes back from a group's ring; the pie can open in the middle of the screen.
- **Shelf:** pictures of images, PDFs and videos; shelves per project (click the title); Shift+drag a row to open the
  pie and drop it on a slice; optionally, what conversions and tools save goes on the shelf (Settings → General).
- **Pies for an app:** a donut can be limited to apps (Settings → Toolbar → Apps), and opens ahead of the general one
  over them. A single pie exports to a `.dilim-pie.json` file and imports back.
- **Your own slices** on the file actions pie: open a web address (`{name}` and the like are filled in), run a command
  or open the files with a program.
- Each release also carries winget manifests.

## 1.0.1

<!-- update
level: recommended
tr: PDF sıkıştırma artık PDF menüsünün ilk dilimi ve boyut düşürücüyü doğrudan açar
tr: Yolları kopyala, Taşı, Paylaş, Terminal ve diğer hızlı araçlar yeni pencere tasarımında
tr: Yolları kopyala: tam yol veya yalnız ad, satır ya da boşluk ayırıcı, tırnak ve önizleme
en: Compress PDF is now the first slice of the PDF menu and opens the size reducer directly
en: Copy paths, Move, Share, Terminal and the other quick tools use the new window design
en: Copy paths: full path or name only, line or space separator, quotes and a preview
-->

- **Compress PDF** is the first slice of the PDF menu and opens the PDF size reducer with its levels and live preview.
- The quick tools and file actions (Copy paths, Copy, Move, Share, Open Terminal, Add background, Remove audio, Split
  PDF, Create PDF, Extract archive) open in the same card as every other tool, with the selected files and pictures of
  them on top and only the settings each one has.
- **Copy paths** can copy full paths or names only, one per line or separated by spaces, optionally in quotes, with a
  preview of the text.
- **Move** shows the chosen folder before moving; **Add background** offers its colours as swatches.

## 1.0.0

<!-- update
level: recommended
tr: Her araç artık kendi penceresinde: önizleme, sürüklenebilir kutular, film şeridi ve dalga formu
tr: Video: hız, kare kaydetme, kırpma, kesme, bölme ve bölgesel gizleme
tr: Ses: kesme, kanallar, normalleştirme, görselleştirici ve bip
tr: Fotoğraf: kırpma, düzenleme, not ekleme, gizleme (yüz ve metin algılama), kolaj, meta veri
tr: Sıkıştırma: resim, video ve ses için Dengeli/Güçlü ve hedef dosya boyutu
en: Every tool has its own window: previews, draggable boxes, film strip and waveform
en: Video: speed, frames, crop, trim, split and redaction by time range
en: Audio: trim, channels, normalize, visualizer and bleep
en: Photos: crop, edit, annotate, redact (face and text detection), collage, metadata
en: Compress pictures, video and sound: Balanced/Strong and a target file size
-->

Every tool now opens in a window of its own, with what it works on in front of you.

- **Photos.** *Crop* with a box, preset ratios and exact pixels. *Edit photo* with a live preview: exposure,
  brightness, contrast, highlights, shadows, whites, blacks, gamma, temperature, tint, saturation, vibrance, hue,
  clarity, dehaze, grain and noise reduction. *Annotate* with arrows, pen, text, rectangles, ellipses, highlighter,
  numbered labels, a magnifier and inserted pictures. *Redact* several areas with a solid colour or pixelation;
  *Faces* and *Text* find them for you on this computer (Windows' own detection). *Collage* in a grid, row, column or
  featured layout with canvas, width, spacing, corners, background and order.
- **Video.** *Change speed* from 0.25× to 4× (sound keeps its pitch), *save frames* as full-size PNGs, *crop* with the
  original sound kept as it is, *trim* with handles or exact times, *split* at points or into equal parts, *redact*
  boxes that each have their own time range, *join* clips of different shapes.
- **Audio.** *Trim* on the waveform (silent ends removed if you like), *convert channels* with left and right volume
  and a preview of each, *normalize* to a target loudness with input and output levels, *visualize* as a moving
  waveform or a still picture in landscape, portrait or square, *bleep* several sections and compare.
- **Compress** pictures, video and sound: Balanced or Strong, a smaller size, and a target file size or share.
- **Metadata:** every field of a picture, recording, video or PDF, grouped and searchable; change, remove, or remove
  everything.
- **PDF:** reduce size, organize pages and merge, as in 0.4 and 0.5.

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
