# Dilim · Kısa rehber

## 1. Temiz kurulum

`Dilim-0.2.0-win-Setup.exe` dosyasını çalıştır. Kullanıcı hesabına kurulur; ayrı .NET kurulumu gerekmez. Başlat menüsündeki **Dilim** ile aç.

Video/ses için **FFmpeg**, arşiv için **7-Zip** motorunu kur. İndirme bir kez yapılır; sonra çevrimdışı kullanabilirsin.

## 2. İlk pie menüsünü aç

1. Dosya Gezgini'nde bir fotoğraf seç.
2. **Ctrl + Alt + Boşluk** tuşlarına bas.
3. **WEBP** dilimine tıkla.
4. Sonuçtaki onayı bekle; Geçmiş'ten klasörü aç.

Bu kısayol Gezgin/masaüstü dosya seçimiyle çalışır.

## 3. Üç menü

| Menü | Kısayol | Sürüklerken tut |
|---|---|---|
| Dönüştür | Ctrl + Alt + Boşluk | Shift |
| Araçlar | Ctrl + Alt + T | Ctrl |
| Dosya işlemleri | Ctrl + Alt + A | Alt |

**Tab / tekerlek:** diğer yüz. **Esc / orta alan:** kapat. Bir grubun üzerinde bekle: alt seçenekler.

Sürüklemek için dosyayı sol tuşla tut → hareket ettir → ilgili tuşu basılı tut → istediğin dilime bırak.

## 4. Kısayolsuz kullan

Ana ekranda **Dosya seç** → **Tüm uyumlu ön ayarlar** listesinden seçim yap → **Dönüştür**. Ekrandaki dilimlere de tıklayabilirsin. **Pie menüyü aç** seçimi çarka taşır.

Kurulu sürümde Gezgin sağ tık → Windows 11'de **Diğer seçenekleri göster → Dilim ile dönüştür**.

## 5. Sıkıştır ve düzenle

**Ctrl + Alt + T → Sıkıştır**:

- Fotoğraf: WEBP/JPG/AVIF, kalite ve boyut yüzdesi.
- PDF: görsel DPI/JPEG kalitesi. Metin/çizimler korunur.
- Video: kodek, CRF, yükseklik, FPS, ses bit hızı. 0 yükseklik/FPS kaynağı korur.
- Ses: hedef biçim ve bit hızı.

**Çalıştır → Klasörü aç**. Düşük CRF daha fazla ayrıntı verir; çıktı her dosyada küçülmeyebilir. Araç çıktıları kaynağın yanındaki **Dilim** klasöründedir.

Kes/kırp/birleştir/hız/kare al; PDF böl/sırala/birleştir; fotoğraf not/kırp/karart; dosya kopyala/taşı/raf/paylaş araçları türüne göre görünür. Zamanlar saniye, boyut/konum piksel cinsindendir.

## 6. Ön ayarını kaydet

**Önayarlar** bölümünden bir ayar seç/oluştur. Görsel/PDF/video/ses seçeneklerini düzenle → **Kaydet**. Gelişmiş bölümde kesin boyut, 2'nin kuvvetine kare, tempo/perde, VBR ve özel FFmpeg çıktı seçenekleri bulunur.

**Çark düzenleyici** bölümünden sık kullandığın ayarı ilgili profilin ön/arka yüzüne ekle. Bütün uyumlu ayarlar ana ekrandaki listeden çalışır.

FileConverter yedeğini taşımak için **Önayarlar → FileConverter XML içe aktar** ile yedek `Settings.user.xml` dosyasını seç. İçe aktarılan silme/arşivleme seçenekleri önce **Özgünü koru** olarak gelir.

## 7. Menü açılmıyorsa

Dilim'in çalıştığını ve duraklatılmadığını kontrol et. Gezgin'de dosya seç; Kısayollar bölümünde hareketin etkin olduğuna bak. Uygulamayı yönetici olarak çalıştırma. Ana ekrandaki **Pie menüyü aç** ile dene. Gri dilimler eksik motorun nedenini gösterir.

Pencereyi kapatmak tepsiye alabilir; tamamen çıkmak için tepsi → Çıkış.

## 8. Ayarlar ve sınırlar

Kurulu ayarlar: `%APPDATA%/Dilim`. Motorlar: `%LOCALAPPDATA%/Dilim/engines`. Taşınabilir sürüm: kendi `data` klasörü.

Varsayılan özgünleri korumaktır. Önayarda açıkça seçilen silme/arşivleme yalnız tamamen başarılı yerel işten sonra uygulanır. Fotoğraf araçları katmanlı editör değildir; video karartma hareket takip etmez. PDF meta veri temizliği sayfadaki görünen bilgiyi silmez. Birleştirilen videolar 1280×720'ye sığdırılır; tümü sesli veya tümü sessiz olmalıdır.

[Kaynak kodu ve sürümler](https://github.com/h-skycloud/dilim).
