# Bulanık ve karanlık kareler — tasarım

Tarih: 2026-11-06 · Yol haritası: Aşama 2, "Bulanık veya çok karanlık kareleri işaretlemek (yerel hesaplama)"

## Amaç

İki iş:

1. **Seri çekimde seçim:** Benzerler'deki seri çekim gruplarında (gerçek arşivde 506 grup, bugün hiçbirinde
   öneri yok) karelerin netliği kıyaslanır; en net kare ve belirgin bulanık kareler işaretlenir.
2. **Arşiv geneli:** tek başına bakıldığında bulanık ya da çok karanlık olan fotoğraflar yeni bir
   **Kusurlu** sekmesinde listelenir; kullanıcı istediklerini ayıklar.

**Başarı ölçütü:** Seri çekim gruplarında ◎ işareti, karşılaştırma ekranında gözle seçilecek kareyle
büyük ölçüde örtüşür. Kusurlu listesi gerçek arşivde elle gözden geçirilebilecek kadar kısadır ve
arkası flu portreler, sade gökyüzü gibi bilerek yumuşak kareler listeyi doldurmaz.

**Kullanıcının seçimleri (2026-11-06):**
- İkisi birden: seri çekimde netlik sıralaması + arşiv genelinde Kusurlu sekmesi.
- Ölçüm yaklaşımı A: parça parça netlik + parlaklık histogramı (yeni paket yok, yapay zekâ modeli yok).

**Değişmeyen kurallar:** Hiçbir dosya otomatik taşınmaz. Ayıklanan dosya silinmez, bir klasöre
taşınır; Geri Al çalışır. Kararlar uygulama kapanınca hatırlanır. İşaret değişince mevcut plan
geçersizleşir.

**Kapsam dışı:** Pozlama düzeltme; yüz, göz kapalı, gülümseme tespiti; otomatik ayıklama; RAW
dosyalarını ölçmek; "aynı kare" ve "küçültülmüş kopya" gruplarında netliğe göre öneri.

## Bileşenler

### `QualityMeter` (Core, yeni)

`static QualityScore? Measure(string path, int orientation, out string? error)`

- `ImageHasher.Decode(path, orientation, 1024)` ile yaklaşık 1024 px genişliğe küçültülerek çözülür,
  `Gray8`'e çevrilir. Küçültme kamera gürültüsünü büyük ölçüde bastırır.
- Her iç piksel için 3×3 Laplace (`4·p − sol − sağ − üst − alt`). Görüntü 4×4 = 16 parçaya bölünür;
  her parçada Laplace değerlerinin varyansı hesaplanır.
- `Sharpness` = en yüksek 2 parçanın ortalaması. Tek parçaya bakmak tek bir parlak nokta ya da
  gürültüye karşı kırılgan; ortalamaya bakmak arkası flu portreyi bulanık sayar.
- `MeanLuma` = ortalama parlaklık (0–255). `DarkFraction` = parlaklığı `DarkPixel` (20) altında olan
  piksellerin oranı.
- Çözülemeyen dosya `null` döner, `error` doldurulur.

`readonly record struct QualityScore(double Sharpness, double MeanLuma, double DarkFraction)`

### `QualityCache` (Core, yeni)

`%LOCALAPPDATA%\FotoDuzenleyici\kalite-onbellek.json`. Anahtar `HashCache.Key` (yoldan bağımsız: boyut +
son yazma zamanı + ilk 64 KB SHA-256). `TryGet`, `Set`, `Save` (`.tmp` + `File.Move`); bozuk dosya boş
başlar. Benzerlik önbelleğiyle birleştirilmez: farklı çözüm boyutu kullanır, birleştirmek mevcut
izleri geçersiz kılar.

### `QualityScan` (Core, yeni)

`SimilarityScan.Compute` ile aynı biçim: `Compute(files, dupGroups, ignoreFolders, cache, errors,
progress, ct)` → `Dictionary<MediaFile, QualityScore>`.

- Yalnızca `FileKind.Photo`; birebir kopyalar ve `_Benzerler`, `_Kusurlu` klasörleri dışarıda.
- 4 iş parçacığı; durdurulunca o ana kadar ölçülenler önbelleğe kaydedilir.

### `QualityRules` (Core, yeni)

Eşikler ve kararlar tek yerde, sabit olarak. Sayılar gerçek arşiv ölçümünden sonra kullanıcıyla
seçilir (bkz. Kalibrasyon); testler seçilen değerleri korur.

- `IsBlurry(score)`: `Sharpness < BlurThreshold`.
- `IsDark(score)`: `MeanLuma < DarkMean && DarkFraction > DarkShare`.
- `RankBurst(IReadOnlyList<(MediaFile, QualityScore?)>)` → her kare için `Percent` (en net = 100),
  `Sharpest` (◎) ve `Soft` (BULANIK).
  - Ölçülemeyen kare yüzde almaz ve işaretlenmez.
  - Ölçülen kare sayısı 2'den azsa hiçbir işaret yok.
  - Bütün ölçülen kareler `TieShare`'in (başlangıç %85) üstündeyse ◎ ve BULANIK yok.
  - Aksi hâlde en net kare ◎; yüzdesi `SoftShare`'in (başlangıç %50) altındaki kareler BULANIK.

### Seri çekim (App + `SimilarRow`)

- `SimilarRow` yeni alanlar: `SharpPercent` (int?), `Sharpest`, `Soft`. Yalnızca `SimilarKind.Burst`
  gruplarında doldurulur.
- Şerit kartı: ◎ (sağ üst, ★ ile aynı yerde; seri çekimde ★ zaten yok) ve BULANIK rozeti (sol alt,
  RAW rozetinin yanında).
- Karşılaştırma penceresi: başlık "3 · Netlik %42" biçiminde.
- Yeni düğme **"Bulanıkları ayıkla"**: seri çekim gruplarında `Soft` olan kareleri Ayıkla yapar;
  `Sharpest` olana dokunmaz; grubun son karesi kuralı geçerli. Kararlar `ReviewStore`'a yazılır.
- "Önerileri uygula" seri çekimlere eskisi gibi dokunmaz.

### Kusurlu sekmesi (App + Core `FlawedState`)

- Liste: `QualityRules.IsBlurry || IsDark` olan ve hiçbir Benzerler grubunda bulunmayan fotoğraflar,
  çekim tarihine göre.
- `FlawedRow` (Core): dosya, puan, `Reason` (BULANIK / KARANLIK / ikisi), `Keep` (varsayılan true),
  `Thumb`, `StoreKey`. `ReviewStore.GetKeep/SetKeep` ile kalıcı.
- Görünüm: sanallaştırılmış ızgara (`VirtualizingWrapPanel` yok; satır başına N kart olacak biçimde
  satırlara bölünmüş `ListBox`, şeritteki kart şablonu yeniden kullanılır). Üstte "Bulanıklar",
  "Karanlıklar" onay kutuları ve "Ayıklanacak: n" sayacı. Küçük resimler `ThumbnailLoader` ile.
- Tek tık Kalsın ↔ Ayıkla (son kare kuralı yok). Çift tık: `CompareWindow` tek kareli modda
  (yakınlaştırma ve kaydırma aynı; "en az bir kare kalmalı" kuralı kapalı; ← → sonraki/önceki
  kusurlu kare; Enter yok).

### Plan ve Uygula

- Yeni `PlanAction.Flawed`, yeni `PlanOptions.FlawedFolderName = "_Kusurlu"`. `Planner.Build`'e
  `flawedOf` (ayıklanan kusurlu dosyalar kümesi) parametresi. Hedef `_Kusurlu\yyyy-MM\`.
- `_Kusurlu` altındaki dosyalar `_Benzerler` gibi yerinde kalır (AlreadyInPlace) ve ölçülmez.
- Öncelik: birebir kopya > Benzerler ayıklaması > Kusurlu ayıklaması > normal taşıma.
- RAW+JPG eşleri: Planner'ın eş adlandırması `Flawed` için de geçerli. Plan'da tek soru: Benzerler
  ve Kusurlu'da ayıklanan karelerin RAW eşleri birlikte sayılır; Evet derse RAW kendi JPG'sinin
  gittiği klasöre (`_Benzerler` ya da `_Kusurlu`) gider. `RawJpgPairs.UnreleasedRaws` buna göre
  genelleştirilir.
- `Executor` değişmez (yalnızca hedef yolları uygular).

### Tarama ve seçenekler

- Yeni onay kutusu "Bulanık / karanlık kareleri işaretle" (varsayılan açık). Benzerlik adımından
  sonra çalışır; ilerleme aynı durum çubuğunda; Durdur geçerli.
- Özet sekmesi: "Bulanık: n, Karanlık: n (Benzerler dışında)", "Seri çekimde en net önerisi: n grup",
  "Ölçülemeyen: n".

### Komut satırı

`--quality`: ölçer; bulanık / karanlık sayıları, en düşük puanlı 10 dosya. `--csv` ile birlikte
`…-kusurlu.csv` (Dosya;Neden;Netlik;Parlaklik;KaranlikOrani;Cekim) ve tüm ölçümlerin dağılımı için
`…-kalite.csv` (kalibrasyon için).

## Kalibrasyon

Uygulamanın ilk adımı: `QualityMeter` ve `--quality` hazır olunca gerçek arşivde (`D:\1- HERKES\001 -
İSMAİL HAKKI BAŞOĞLU`) ölçüm alınır. Netlik ve parlaklık dağılımı ile en düşük puanlı kareler
(küçük resimli bir sayfa) kullanıcıya gösterilir. `BlurThreshold`, `DarkMean`, `DarkShare`,
`SoftShare`, `TieShare` birlikte seçilir; ardından arayüz işleri başlar. İlk ölçüm süresi bu adımda
ölçülür ve kullanıcıya bildirilir.

## Hata durumları

- Çözülemeyen dosya: ölçülmez, hata listesine yazılır, hiçbir listede görünmez.
- Önbellek bozuk: boş başlar; kaydedilemezse hata listesine yazılır, tarama sürer.
- Karar dosyası bozuk: bugünkü `ReviewStore` davranışı.

## Testler

- `QualityMeter`: test sırasında üretilen görüntülerle — keskin desen > aynı desenin bulanığı;
  yarısı keskin yarısı bulanık (arkası flu portre) görüntü bulanık sayılmaz; düz siyah/çok karanlık
  görüntü `IsDark`; bozuk dosya `null`; yön bilgisi puanı değiştirmez.
- `QualityCache`: kaydet-yükle, bozuk dosya, yoldan bağımsız anahtar.
- `QualityRules.RankBurst`: yüzde, ◎, BULANIK, beraberlik eşiği, ölçülemeyen kare, tek kare.
- `FlawedState`: Benzerler'deki dosyalar listeye girmez; kalıcı karar geri yüklenir.
- Planner: `Flawed` hedefi, `_Kusurlu`'daki dosya yerinde kalır, öncelik sırası, eşin RAW'ı.
- `RawJpgPairs.UnreleasedRaws`: Benzerler ve Kusurlu birlikte.
- Gerçek arşivde: kalibrasyon ve yalnızca Plan (Uygula yok); arayüz elle denenir.
