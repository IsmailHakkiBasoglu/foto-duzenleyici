# Benzer fotoğraflar (algısal hash) — tasarım

Tarih: 2026-10-30 · Yol haritası: Aşama 2, "Benzer fotoğraflar (algısal hash)"

## Amaç

Baytları ve EXIF'i farklı ama görüntüsü aynı ya da çok benzer fotoğrafları bulup kullanıcıya
göstermek. İki hedef birlikte:

1. **Küçültülmüş kopyalar** — WhatsApp, Instagram, mesajla gelen kopyalar: EXIF'i silinmiş,
   boyutu küçülmüş, yeniden sıkıştırılmış ama aynı kare. Amaç fazlalığı ayıklamak.
2. **Seri çekimler** — saniyeler içinde çekilmiş çok benzer kareler. Amaç kullanıcının en iyisini
   seçmesi.

Hiçbir dosya otomatik taşınmaz. Mevcut "Muhtemelen aynı kare" (EXIF parmak izi) davranışı aynen
sürer: her dosyanın "Kalsın" işareti vardır, işareti kaldırılan dosya Uygula'da `_Benzerler`
klasörüne gider, Geri Al çalışır.

**Başarı ölçütü:** Gerçek arşivin bir bölümünde WhatsApp tipi kopyalar ve seri çekimler doğru
gruplanır, alakasız fotoğraflar gruplanmaz; ikinci taramada izler önbellekten gelir ve tarama
belirgin biçimde hızlanır.

**Kapsam dışı:** Küçük resimli yan yana karşılaştırma ekranı (Aşama 3), netlik/karanlık ölçümü
(Aşama 2'nin ayrı maddesi), RAW ve video için benzerlik.

## Kararlar

- **Görüntü çözme: Windows WIC** (WPF'in `System.Windows.Media.Imaging` sınıfları). Yeni paket yok;
  JPEG'i küçük boyutta doğrudan çözer (DCT ölçekleme), Windows uzantıları kuruluysa HEIC okur.
  Core, CLI ve Tests projeleri `net8.0-windows` + `UseWPF` olur. ImageSharp (HEIC yok, ek lisans)
  ve System.Drawing (yavaş, önerilmiyor) elendi.
- Arşiv boyutu bilinmediği için **önbellek zorunlu**.

## Uygulama sırasında verilen kararlar (2026-10-30)

- **Kopya doğrulaması:** iz (Hamming ≤ 4) tek başına yetmiyor; 2000 alakasız, sade desende
  35 sahte kopya grubu çıktı (çakışma + zincirleme). Kopya bağı ayrıca 16×16 gri küçük
  resimlerin ortalama mutlak farkı ≤ 8 ister. Önbellek iz + küçük resmi saklar (`ImagePrint`).
- **Seri kareler kopya sayılmaz:** iki dosyada da aynı kameranın EXIF'i varsa ve çekim anları
  farklıysa, görüntü ne kadar benzese de kopya bağı kurulmaz (sabit sahnede seri çekim ★ alıp
  ayıklanmasın); yalnızca seri çekim kuralıyla gruplanabilir.
- **Düz görüntüler:** 9×8 ızgarada en açık–en koyu farkı < 8 olan görüntünün izi çıkarılmaz.

## Bileşenler (Core)

### `ImageHasher`

- `static ulong? DHash(string path, int orientation)` — 64 bitlik fark hash'i (dHash).
- Adımlar: WIC ile en fazla ~72 px genişlikte çöz (`BitmapImage.DecodePixelWidth`,
  `CacheOption.OnLoad`, `Freeze`) → EXIF yönünü (`MediaFile.Orientation`, 1–8) uygula →
  `Gray8`'e çevir → kendi kodumuzla alan ortalamasıyla 9×8'e küçült → her satırda soldan sağa
  komşu farkının işaretinden 64 bit.
- Çözülemeyen dosyada istisna fırlatmaz, `null` döner (hata metni çağırana bildirilir).
- İş parçacığı güvenli: her çağrı kendi WIC nesnelerini oluşturur ve dondurur.

### `HashCache`

- Dosya: `%LOCALAPPDATA%\FotoDuzenleyici\benzerlik-onbellek.json`. Yalnızca izler saklanır,
  görüntü saklanmaz.
- **Anahtar:** boyut + son yazma zamanı (UTC tick) + dosyanın ilk 64 KB'sinin SHA-256'sı.
  Yol kullanılmaz: Uygula dosyaları taşır/yeniden adlandırır ama zaman damgalarını korur, bu
  anahtar bundan etkilenmez.
- Bozuk dosya sessizce yok sayılır. Yazma atomik: geçici dosyaya yaz, sonra değiştir.
- Durdurulan taramada o ana kadarki izler kaydedilir.

### `SimilarFinder`

Girdi: izi olan fotoğraflar + mevcut aynı kare grupları. Çıktı: türlü gruplar.

| Bağ türü | Koşul |
|---|---|
| Küçültülmüş kopya | Hamming uzaklığı ≤ 4; tarih/EXIF şartı yok |
| Seri çekim | Hamming ≤ 12 **ve** iki dosyada da EXIF tarihi **ve** aynı kamera (marka+model) **ve** çekim anları arası < 10 sn |

- Gruplar birleşim-bul (union-find) ile kurulur: A~B, B~C ise {A,B,C}.
- Grup türü: içinde aynı kare bağı varsa *Aynı kare*; yoksa en az bir kopya bağı varsa
  *Küçültülmüş kopya*; değilse *Seri çekim*.
- Aynı kare grupları (`SameFrameFinder`) aynı birleşim yapısına bağ olarak girer; aynı dosyalar
  iki ayrı grupta görünmez.
- **Hız:**
  - Kopya adayları: 64 bit 5 parçaya (13+13+13+13+12) bölünür; Hamming ≤ 4 ise en az bir parça
    birebir eşittir (güvercin yuvası). Yalnızca ortak parçası olan çiftler karşılaştırılır.
  - Seri çekim adayları: çekim anına göre sıralanır, yalnızca 10 sn pencere içindekiler
    karşılaştırılır.
- Eşikler `SimilarOptions` içinde sabit varsayılanlarla tutulur (ileride ayarlanabilir).

### Öneri (★)

| Tür | Önerilen |
|---|---|
| Aynı kare | Mevcut kural: en yüksek çözünürlük, eşitlikte ilk yazılan |
| Küçültülmüş kopya | En yüksek çözünürlük → EXIF'i olan → ilk yazılan |
| Seri çekim | **Öneri yok.** "Önerileri uygula" bu gruplara dokunmaz |

## Tarama akışı

1. Metadata (mevcut) — `MediaFile`'a `Orientation` eklenir (EXIF IFD0 Orientation, yoksa 1).
2. SHA-256 birebir kopyalar (mevcut).
3. **Benzerlik izi** — seçenek "Benzer fotoğrafları da ara" (varsayılan açık):
   - Kapsam: yalnızca `FileKind.Photo`; RAW, video, birebir kopyaların kopya tarafı ve
     `_Benzerler` içindekiler hariç.
   - 4 iş parçacığı; ilerleme: "Benzerlik izi… 1.234 / 20.000 (18.000'i önbellekten)".
   - **Durdur** düğmesi (tüm tarama için): 1. ya da 2. adımda basılırsa tarama tümden iptal olur
     (bugünkü davranış gibi hiçbir sonuç kalmaz). 3. adımda basılırsa o ana kadarki izler
     kaydedilir, benzerlik grupları gösterilmez, birebir kopya ve aynı kare sonuçları ile plan
     normal kullanılır.
4. Gruplama (`SimilarFinder`) → Benzerler sekmesi.

## Arayüz

- "Muhtemelen aynı kare" sekmesi **"Benzerler"** olur; yeni **Tür** sütunu.
- Kalsın işaretleri, "grubun son işaretlisi bırakılamaz" kuralı, "Önerileri uygula",
  "Hepsini koru", seçim değişince planın geçersizleşmesi aynen kalır.
- **Önizleme:** tablonun sağında; seçili satırın fotoğrafı arka planda ~1000 px çözülüp gösterilir,
  altında çözünürlük, MB, tarih, kamera. Ok tuşlarıyla gezilir. Yalnızca son seçim gösterilir
  (hızlı gezinmede eski çözümler iptal).
- Özet: tür başına grup sayısı ve "Benzerlik izi çıkarılamayan: N" (+ ilk birkaç sebep).

## CLI

- `--similar`: benzerlik izini hesaplar (varsayılan kapalı) ve grupları türüyle raporlar.
  Dosyalara farklı işlem yapmaz.

## Hata durumları

| Durum | Davranış |
|---|---|
| Çözülemeyen dosya | İz yok, tarama sürer, özette sayılır |
| Önbellek bozuk / okunamıyor | Yok sayılır, yeniden oluşturulur |
| Önbellek yazılamıyor | Tarama sürer, uyarı durum çubuğunda |
| Tarama durduruldu | İzler kaydedilir, benzerlik grupları gösterilmez |

## Testler

- **ImageHasher:** testte WPF ile desenli JPEG üretilir. Küçültülmüş + farklı kalitede yeniden
  kaydedilmiş kopya ≤ 4 bit; EXIF yönü 6 (90°) olan kopya döndürülmüş orijinalle eşleşir; farklı
  desen > 12 bit; bozuk dosya `null`.
- **SimilarFinder:** elle verilen izlerle kopya eşiği, seri çekim zaman/kamera şartı, zincirleme
  gruplama, aynı kare ile birleşme, türe göre öneri, seri çekimde önerisizlik; 20.000 sahte izle
  süre < 3 sn.
- **HashCache:** isabet; dosya değişince yeniden hesap; taşıma + yeniden adlandırmadan sonra isabet;
  bozuk dosya.
- **Uçtan uca:** Tara → benzerlik grubu → `_Benzerler` → Geri Al.
- **Elle (arayüz):** önizleme, Durdur, "Önerileri uygula"nın seri çekime dokunmaması.
- **Gerçek arşivde hız ölçümü:** kullanıcı onayıyla, arşivin bir bölümünde salt okunur tarama.
