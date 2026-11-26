# Araç arayüzleri — uygulama planı

21 aracın hepsinin dönüştürme kodu (`src/Dilim.Engines/Tools/ToolEngine.cs`) var; arayüzleri ortak bir metin kutusu
formu (`ToolWindow`). Her araç, aşağıdaki sırayla kendi görsel arayüzüne kavuşur; gerektiğinde arka ucu da genişler.
Sıra: en kolaydan en zora. Ortak bileşen, onu ilk kullanan aracın adımında yapılır ve sonrakiler onu kullanır.

## Ortak bileşenler

| Bileşen | İlk kullanan | Sonra kullananlar |
| --- | --- | --- |
| **Araç penceresi iskeleti** (yuvarlak kapat, ortalı başlık, içerik, alt şerit: ikincil + ana düğme, ilerleme, "Klasörü aç") | 1 | Hepsi |
| **Sıralama listesi** (numara, küçük görsel, ad, ↑/↓) | 1 | 2, 18 |
| **Küçük görsel üretimi** (PDF sayfası, video karesi) | 1–2 | 3, 5–9, 12–13 |
| **Görsel tuval + taşınabilir kutu** (köşe tutamaçları, oran kilidi) | 4 | 7, 15, 17, 18 |
| **Video önizleyici** (kare göster, kare ileri/geri, zaman, kaydırıcı) | 5 | 6–9, 17 |
| **Kare şeridi** (film şeridi, tutamaçlar, noktalar) | 8 | 9, 17 |
| **Dalga formu** (FFmpeg ile tepe değerleri, aralık seçimi) | 10 | 11–14 |
| **Ses önizleme** (çal / duraklat) | 10 | 11–14 |

## Sıra

| # | Araç | İstek no | Arayüzde yeni | Arka uçta yeni |
| --- | --- | --- | --- | --- |
| 1 ✅ | PDF birleştir | 20 | İskelet, sıralama listesi, sayfa küçük görseli | — |
| 2 ✅ | Videoları birleştir | 11 | Video küçük görseli | İlk klip çıktı tuvali (boyut, kare hızı); sesli/sessiz klip karışımı |
| 3 | PDF sayfalarını düzenle | 21 | Sayfa ızgarası; döndür, çoğalt, sil; sırayı sıfırla | Sayfa döndürme, aynı genişlik |
| 4 | Görsel kırp | 5 | Tuval + kutu, oran düğmeleri, px girişi | — |
| 5 | Video hızı | 10 | Video önizleyici, durak noktalı hız kaydırıcısı | Sessiz video desteği (var), perde koruma (var) |
| 6 | Video karelerini kaydet | 12 | Kare seçimi, seçilen kareler şeridi | Birden çok kare, tam çözünürlük PNG |
| 7 | Video kırp | 9 | Önizleyici + kutu | Ses kopyalanır (yeniden kodlanmaz) |
| 8 | Video kırp (süre) | 8 | Kare şeridi + tutamaçlar, zaman kodu girişi | — |
| 9 | Videoyu böl | 13 | Bölme noktaları, eşit parça | Özel noktalarda bölme, tek klasör |
| 10 | Ses kırp | 17 | Dalga formu, ses önizleme, sessiz uçları kırp | Baş/son sessizlik algılama |
| 11 | Ses kanalları | 18 | Sol/sağ ses düzeyi, kanal önizleme | `pan` ile kanal düzeyleri |
| 12 | Sesi normalleştir | 15 | Hedef LUFS, aralık, tepe kaydırıcıları; önce/sonra ölçüm | Ayarlanabilir loudnorm, ölçüm |
| 13 | Ses görselleştirici | 16 | Stil (dalga formu / sabit görsel), en-boy | Dikey/kare, sabit görsel |
| 14 | Bip | 19 | Bip katmanı, birden çok aralık, karşılaştırma | Birden çok aralık |
| 15 | Fotoğrafı gizle | 6 | Birden çok taşınabilir kutu, düz renk / bulanık, renk | Çoklu kutu, bulanıklık |
| 16 | Sıkıştır (PDF ✅, diğerleri) | 1 | Dengeli/Güçlü, hedef boyut veya yüzde, boyutlandırma, orijinal boyut | Hedef boyuta yaklaşan yineleme |
| 17 | Videoyu gizle | 14 | Kutu başına zaman aralığı, zaman çizelgesi | Çoklu kutu + aralık, bulanıklık |
| 18 | Kolaj | 7 | Izgara/satır/sütun/öne çıkan, tuval, aralık, köşe, arka plan, sıra | Düzen motoru |
| 19 | Metadata | 2 | Gruplu alanlar, arama, düzenle/sil, tümünü kaldır | Alan okuma/yazma (resim EXIF/IPTC/XMP, ses/video etiketleri, PDF bilgisi) |
| 20 | Fotoğrafa not ekle | 4 | Ok, kalem, metin, dikdörtgen, elips, vurgulayıcı, numaralı etiket, büyüteç, resim ekle; geri al | Katman çizimi |
| 21 | Fotoğraf düzenle | 3 | Canlı önizleme; ışık, renk, detay kaydırıcıları | Pus giderme, netlik, gren, gürültü azaltma |
| 22 | Yüz / metin algılama | 6 | "Yüzler", "Metin" düğmeleri | Yerel algılama modeli — en ağır iş, en sona |

## PDF boyut düşürücü (öne alındı) ✅

`PdfShrinker` + `PdfShrinkTool`. Akıllı (yalnız görseller; yazı/çizgi vektör kalır) ve Düzleştir (her sayfa tek görsel);
Hafif 300 dpi/%85 · Baskı 220/%80 · Dengeli 150/%72 · Ekran 110/%60 · En küçük 72/%45. Boyut tahmin edilmez, her ayar
gerçekten uygulanıp ölçülür; kalite, sayfaların ortasından 150 dpi kesitlerin yapısal benzerliğiyle (SSIM) ölçülür.
Hedef boyutta seviyeler denenir, sonra iki seviye arası ikiye bölmeyle hedefin hemen altına inilir. "Kaliteyi
karşılaştır" ilk sayfanın ortasını önce/sonra gösterir.

## İlkeler

- Orijinal dosya hiç değişmez; çıktı yanındaki `Dilim` klasörüne gider.
- Her pencere açık/koyu ve tr/en ekran görüntüsü testiyle gelir; yalnız tasarım token'ları kullanılır.
- Her adım ayrı commit; birkaç araç birikince sürüm.
