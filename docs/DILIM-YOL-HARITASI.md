# Dilim yol haritası

Durum: 1.1.0 (Kasım 2026). ✅ işaretliler yayında. Öncelik sırası: önce kullanıcının her gün hissettiği hız ve güven, sonra yeni yetenekler.
Ayrıntılı Kando karşılaştırması: [DILIM-KANDO-PLAN.md](DILIM-KANDO-PLAN.md).

## 0.4 — Hız (pie menüsü)

| İş | Neden | Büyüklük |
| --- | --- | --- |
| ✅ Hızlı seçim tuşları (1–9) | Dilimi fareye dokunmadan seçmek | Küçük |
| ✅ Turbo mod: Ctrl+Alt bırakınca seçim | Tek hareketle dönüştürme | Küçük–orta |
| ✅ Yön kaması vurgusu | Halkanın dışında da doğru dilimi göstermek, hedefleme hatasını azaltmak | Küçük |
| ✅ Sağ tık / merkez = geri | Grup halkasından çıkmak | Küçük |

## 0.5 — Raf ve iş akışı

| İş | Neden | Büyüklük |
| --- | --- | --- |
| ✅ Raf satırını doğrudan bir pie dilimine sürükleme | Raftan tek hareketle dönüştürme | Orta |
| ✅ Raf grupları / sabitlenen öğeler | Proje başına dosya setleri (ör. "Proje A sunum") | Orta |
| ✅ Raf küçük önizlemeleri (resim, PDF ilk sayfa) | Dosyayı adından değil görüntüsünden tanımak | Orta |
| ✅ Dönüşüm sonucu otomatik rafa ekleme (isteğe bağlı) | Çıktıyı hemen paylaşmak / sürüklemek | Küçük |

## 0.6 — Esneklik

| İş | Neden | Büyüklük |
| --- | --- | --- |
| ✅ Özel eylem dilimleri (URL aç, komut çalıştır, uygulamayla aç) | Pie'ı dönüştürmenin ötesinde bir başlatıcıya çevirmek | Orta |
| ✅ Uygulamaya göre pie (Photoshop'ta başka, Gezgin'de başka) | Bağlama uygun dilimler | Orta |
| ✅ Pie dışa/içe aktarma (`.dilim-pie.json`) | Ayarları ekip içinde paylaşmak | Küçük |

## Dağıtım ve güven

| İş | Neden | Büyüklük |
| --- | --- | --- |
| Kod imzalama (ör. SignPath Foundation veya Azure Trusted Signing) | SmartScreen uyarısını kaldırmak; en çok kullanıcı kaybettiren adım | Orta (başvuru süreci) |
| Web sitesinde sürüm numarasını GitHub'dan otomatik gösterme | Buton ve sürüm bilgisi kendiliğinden güncel kalır | Küçük |
| ✅ Güncelleme kartında "bu sürümde neler var" bağlantısı | Kullanıcı notların tamamına ulaşsın | Küçük |
| ✅ winget paketi | `winget install Dilim` ile kurulum, otomatik güncelleme | Küçük–orta |

## Kalite

| İş | Neden | Büyüklük |
| --- | --- | --- |
| ✅ Linux'ta kırmızı kalan Windows'a özgü testleri işaretlemek | Linux geliştirme ortamında sinyal/gürültü | Küçük |
| Eski 4 App ve 35 Setup test hatasının kök nedenleri | CI'da tam tablo | Orta |
| Engine testlerini CI'da gece çalıştırmak | Dönüştürme kalitesindeki gerilemeleri yakalamak | Küçük |

## Kullanıcıya kalan

- **Kod imzalama:** SignPath Foundation (ücretsiz, açık kaynak) veya Azure Trusted Signing başvurusu senin hesabınla
  yapılır; sertifika gelince `release.yml`'a imzalama adımı eklenir.
- **winget:** Her sürümde `Dilim-<sürüm>-winget-manifests.zip` üretilir; ilk gönderim `microsoft/winget-pkgs`'e senin
  hesabınla açılacak bir PR ister (docs/DILIM-RELEASE.md).
- **Web sitesi sürüm numarası:** Site kodu senin bilgisayarında; `https://api.github.com/repos/h-skycloud/dilim/releases/latest`
  adresinden `tag_name` okuyan küçük bir betikle gösterilebilir. İndirme bağlantısı zaten hep son sürümü verir.

## İlke

- Sürüm notları yalnız kullanıcının gördüğü değişikliği anlatır; iç geçmişi ("artık çalışıyor", "yeniden yapıldı")
  anlatmaz. Her yenilik bir kazanım olarak yazılır.
- Yeni her görünüm açık/koyu ve tr/en ekran görüntüsü testiyle gelir; renkler yalnız tasarım token'larından.
