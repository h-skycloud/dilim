# Dilim ve FileConverter — 21 Ekim 2026

Karşılaştırma fotoğraf/PDF dönüştürme ve sıkıştırma ile yoğun video kullanımına göre yapıldı. Kurulu FileConverter 2.1.0 ayarlarındaki **75 ön ayar uyarısız aktarıldı**. **67 disk dışı ön ayar gerçek örnek dosyalarla çıktı üretti**; özgünlerin SHA-256 değerleri değişmedi. Kullanıcının kullanmadığı 8 CD/DVD ön ayarı fiziksel sürücüyle denenmedi.

| Alan | Dilim karşılığı |
|---|---|
| Fotoğraf | Kalite, yüzde/kenar boyutu, döndürme, maksimum kenar, 2'nin kuvvetine kare, renk/DPI/meta veri; ayrıca kırpma/not/karartma |
| PDF | Görselden PDF, PDF'den sayfa görselleri, DPI/kalite ile metni koruyan sıkıştırma, birleştirme/bölme/sıralama |
| Video | MP4/MKV/AVI/WEBM/OGV/GIF; CRF, dokuz kodlama hızı, FPS, ölçek/döndürme, kesin boyut, ses ayarları, özel FFmpeg çıktı seçenekleri |
| Ses | AAC/FLAC/MP3/OGG/WAV; CBR/VBR, bit derinliği/kanal/örnekleme, birbirinden bağımsız tempo ve perde |
| Ön ayarlar | XML içe aktarma, klasörler, tüm uyumlu ayarları çalıştırma, pie profiline ekleme |
| Özgün dosya | Koru / başarılı dönüşümden sonra arşivle / sil; içe aktarımda güvenli varsayılan Koru |
| Windows akışı | Gezgin klasik sağ tık, üç pie menüsü, kısayol/sürükleme, kuyruk ve geçmiş |

FileConverter'ın 14 varsayılan özel FFmpeg komutu aktarılarak üretilmiş dosyalar üzerinde çalıştırıldı. Komut alanı yalnız çıktı seçenekleri içindir; giriş/çıkış yollarını uygulama yönetir. 720p/1080p komutları gerektiğinde oranı esnetir; normal maksimum yükseklik seçimi oranı korur. Farklı kodeklerde aynı CRF aynı kalite anlamına gelmez; çıktı her zaman küçülmez.

Sağ tık girişinin görünümü FileConverter'ın iç içe menüsüyle aynı değildir: Dilim kendi pie menüsünü açar. İçe aktarılan bütün ayarlar ana ekrandaki **Tüm uyumlu ön ayarlar** listesinden çalışır; sık kullanılanlar pie profiline eklenebilir.

## Geçiş

Bu bilgisayardaki taşınabilir kopya bağımsız FFmpeg ve 7-Zip içerir; FileConverter'ın motor klasörünü kullanmaz. Genel dağıtım paketi motorları ilk açılışta asıl yayıncılarından doğrulayarak indirir; ardından çevrimdışı çalışır. PDF sıkıştırma için Ghostscript zorunlu değildir.

Fotoğraf/PDF/video kullanımı için teknik geçiş kontrolleri tamamlandı. Ayrıntılı kanıt: [DILIM-VALIDATION.md](DILIM-VALIDATION.md). FileConverter kaldırılmadı; birlikte kaldırma ayrı adım. Ayar yedeği çalışma alanının dışındaki references/fileconverter-settings-backup klasöründe tutuluyor ve açık kaynak paketine alınmıyor.

## Kaynaklar ve köken

- [FileConverter](https://github.com/Tichau/FileConverter)
- [İncelenen ön ayar modeli](https://github.com/Tichau/FileConverter/blob/5dce6c0980635e3a6ae8d8acff235bcc9b1441a4/Application/FileConverter/ConversionPreset/ConversionPreset.cs)
- [Ayar denetimleri](https://github.com/Tichau/FileConverter/blob/5dce6c0980635e3a6ae8d8acff235bcc9b1441a4/Application/FileConverter/Views/Resources/ConversionPresetTemplates.xaml)
- [Video/ses davranışı](https://github.com/Tichau/FileConverter/blob/5dce6c0980635e3a6ae8d8acff235bcc9b1441a4/Application/FileConverter/ConversionJobs/ConversionJob_FFMPEG.Converters.cs)

Dilim Dilim contributors tarafından Filee'nin MIT temeli üzerinde geliştirildi. FileConverter kaynak kodu kopyalanmadı; uyumluluk davranışı bağımsız uygulandı. Özgün Filee ve bağımlılıkların lisans/atıfları korunur. [Dilim contributors](https://github.com/h-skycloud/dilim).
