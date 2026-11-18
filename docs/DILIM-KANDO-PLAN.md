# Kando'dan alınabilecek özellikler — plan

Kaynak: [kando-menu/kando](https://github.com/kando-menu/kando) (MIT, TypeScript/Electron). Kod kopyalanmaz; fikirler
Dilim'in Avalonia/C# yapısına ve mevcut tasarım diline (yumuşak yüzeyler, tek turuncu vurgu, `Soft.axaml` /
`Dilim.axaml` token'ları, `Motion` ile azaltılmış hareket desteği) uygun olarak yeniden yazılır. Bir algoritma birebir
taşınırsa THIRD-PARTY-NOTICES.md'ye Kando'nun MIT bildirimi eklenir.

## Dilim'de zaten olanlar

| Kando | Dilim karşılığı |
| --- | --- |
| Pie menü, kısayolla açılma | Ctrl+Alt+Boşluk / T / A, sürükleme jesti (`TriggerService`, `GestureDetector`) |
| Klavye ile gezinme | Ok tuşları, Enter, Esc, Tab ile çevirme (`DonutMenu.HandleKey`) |
| Alt menüler | Gruplar: dilimden açılan ikinci halka (`DonutMenu.Groups`) |
| Menü temaları | `theme.json`, yumuşaklık, vurgu rengi (`ThemeService`, docs/CUSTOM-THEMES.md) |
| Uygulamaya göre kapatma | Hariç tutulan uygulamalar (`ExcludedAppsViewModel`) |

## Önerilen özellikler (öncelik sırasıyla)

### Faz 1 — Hız (küçük iş, büyük kazanç)

1. **Hızlı seçim tuşları.** Her dilime 1–9 (veya etiketin altı çizili harfi) atanır; tuşa basınca dilim seçilir.
   Etiket altında küçük, `TextSecondaryBrush` renginde rakam rozeti. Ayar: Genel → "Hızlı seçim tuşlarını göster".
   Dokunulacak yerler: `DonutMenu.HandleKey`, `DonutMenu` etiket çizimi, `DonutSettings`.
2. **Turbo mod.** Ctrl+Alt basılıyken açılan pie'da imleci dilime doğru götürüp tuşları bırakmak o dilimi seçer;
   tıklama gerekmez. Bırakıldığında imleç merkezdeyse menü kapanır. Ayar ile kapatılabilir.
   Dokunulacak yerler: `RadialController` (tuş bırakma olayı), `TriggerService` klavye kancası.
3. **Yön kaması (selection wedge).** İmleç halkanın dışına çıktığında da en yakın dilim vurgulanır; merkezden imlece
   doğru soluk turuncu (`AccentFaintBrush`) bir kama çizilir. Hedefleme hatasını azaltır, tasarımı bozmaz.

### Faz 2 — İşaretleme (marking) modu

4. **Basılı tut–çiz–bırak seçimi.** Tıklama modunda fare basılıyken bir yöne çizgi çekip bırakmak o yöndeki dilimi
   seçer; çizgi gruptaki ikinci halkaya da devam eder (iç içe seçim tek hareketle). Kando'nun yaklaşımı: çizgi belirli
   uzunluğu geçince ve yön keskin değiştiğinde ya da imleç kısa süre durduğunda seçim yapılır. Dilim'de eşikler
   `DragThreshold` ile aynı mantıkta tutulur.
5. **Sağ tık / merkez = geri.** Açık bir grup halkasındayken sağ tık veya merkeze dönmek bir üst seviyeye döner.

### Faz 3 — Esneklik

6. **Özel eylem dilimleri.** Dönüştürme ve araçların yanında: "URL aç", "Komut çalıştır", "Dosyayı uygulamayla aç",
   "Metin yapıştır", "Kısayol gönder". Önce Dosya işlemleri pie'ına eklenir; ön ayar düzenleyicide yeni "Eylem" türü.
7. **Koşullu pie'lar.** Profil seçimine ön plandaki uygulama koşulu eklenir (ör. Photoshop'tayken farklı dilimler).
   Altyapı hazır: `IPlatformServices.ProcessNameAt`.
8. **Pie dışa/içe aktarma.** Dilim profillerini tek `.dilim-pie.json` dosyası olarak paylaşma (FileConverter XML içe
   aktarmanın yanına).

### Faz 4 — İnce ayar (isteğe bağlı)

9. **Sabit konum / ekran ortası.** Pie'ı imleç yerine ekran ortasında açma seçeneği (dokunmatik ve sunum için).
10. **Ses teması.** Açılış/seçim için çok kısa, kapatılabilir sesler (varsayılan kapalı).
11. **Raf + pie birleşimi.** Raftaki seçili dosyalar için pie'ı Raf penceresinden açma (0.3.0'da eklendi:
    "Dönüştür" düğmesi) — sonraki adım: raf satırını doğrudan pie dilimine sürükleme.

## Alınmayacaklar

- Başarımlar (achievements), oyun kolu girişi, Electron'a özgü pencere yönetimi: dosya dönüştürücünün amacına katkısı
  düşük.
- Kando'nun CSS tabanlı menü temaları: Dilim'in `theme.json` ve Avalonia stil sistemi aynı işi görüyor.

## Tasarım kuralları (tüm fazlar)

- Yeni renk eklenmez; yalnız `AccentBrush`, `AccentSoftBrush`, `AccentFaintBrush`, `SurfaceBrush`, `TextSecondaryBrush`.
- Her animasyon `Motion.Enabled` / `reduced-motion` sınıfına uyar.
- Her yeni görünüm için headless ekran görüntüsü testi (açık/koyu, tr/en) eklenir.
