# Dilim yol haritası

Durum: 0.3.1 (Kasım 2026). Öncelik sırası: önce kullanıcının her gün hissettiği hız ve güven, sonra yeni yetenekler.
Ayrıntılı Kando karşılaştırması: [DILIM-KANDO-PLAN.md](DILIM-KANDO-PLAN.md).

## 0.4 — Hız (pie menüsü)

| İş | Neden | Büyüklük |
| --- | --- | --- |
| Hızlı seçim tuşları (1–9) | Dilimi fareye dokunmadan seçmek | Küçük |
| Turbo mod: Ctrl+Alt bırakınca seçim | Tek hareketle dönüştürme | Küçük–orta |
| Yön kaması vurgusu | Halkanın dışında da doğru dilimi göstermek, hedefleme hatasını azaltmak | Küçük |
| Sağ tık / merkez = geri | Grup halkasından çıkmak | Küçük |

## 0.5 — Raf ve iş akışı

| İş | Neden | Büyüklük |
| --- | --- | --- |
| Raf satırını doğrudan bir pie dilimine sürükleme | Raftan tek hareketle dönüştürme | Orta |
| Raf grupları / sabitlenen öğeler | Proje başına dosya setleri (ör. "Proje A sunum") | Orta |
| Raf küçük önizlemeleri (resim, PDF ilk sayfa) | Dosyayı adından değil görüntüsünden tanımak | Orta |
| Dönüşüm sonucu otomatik rafa ekleme (isteğe bağlı) | Çıktıyı hemen paylaşmak / sürüklemek | Küçük |

## 0.6 — Esneklik

| İş | Neden | Büyüklük |
| --- | --- | --- |
| Özel eylem dilimleri (URL aç, komut, uygulamayla aç, kısayol gönder) | Pie'ı dönüştürmenin ötesinde bir başlatıcıya çevirmek | Orta |
| Uygulamaya göre pie (Photoshop'ta başka, Gezgin'de başka) | Bağlama uygun dilimler | Orta |
| Pie dışa/içe aktarma (`.dilim-pie.json`) | Ayarları ekip içinde paylaşmak | Küçük |

## Dağıtım ve güven

| İş | Neden | Büyüklük |
| --- | --- | --- |
| Kod imzalama (ör. SignPath Foundation veya Azure Trusted Signing) | SmartScreen uyarısını kaldırmak; en çok kullanıcı kaybettiren adım | Orta (başvuru süreci) |
| Web sitesinde sürüm numarasını GitHub'dan otomatik gösterme | Buton ve sürüm bilgisi kendiliğinden güncel kalır | Küçük |
| Güncelleme kartında "bu sürümde neler var" bağlantısı | Kullanıcı notların tamamına ulaşsın | Küçük |
| winget paketi | `winget install Dilim` ile kurulum, otomatik güncelleme | Küçük–orta |

## Kalite

| İş | Neden | Büyüklük |
| --- | --- | --- |
| Linux'ta kırmızı kalan Windows'a özgü testleri işaretlemek | Linux geliştirme ortamında sinyal/gürültü | Küçük |
| Eski 4 App ve 35 Setup test hatasının kök nedenleri | CI'da tam tablo | Orta |
| Engine testlerini CI'da gece çalıştırmak | Dönüştürme kalitesindeki gerilemeleri yakalamak | Küçük |

## İlke

- Sürüm notları yalnız kullanıcının gördüğü değişikliği anlatır; iç geçmişi ("artık çalışıyor", "yeniden yapıldı")
  anlatmaz. Her yenilik bir kazanım olarak yazılır.
- Yeni her görünüm açık/koyu ve tr/en ekran görüntüsü testiyle gelir; renkler yalnız tasarım token'larından.
