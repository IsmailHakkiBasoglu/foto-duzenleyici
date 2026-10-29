# Benzer fotoğraflar (algısal hash) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Küçültülmüş kopyaları ve seri çekimleri algısal hash (dHash) ile bulup mevcut "Muhtemelen aynı kare" gruplarıyla birlikte tek bir "Benzerler" sekmesinde, önizlemeli olarak göstermek.

**Architecture:** WIC (WPF imaging) ile küçük boyutta çözülen fotoğraftan 64 bit dHash çıkarılır (`ImageHasher`), izler yoldan bağımsız anahtarla JSON önbellekte tutulur (`HashCache` + `SimilarityScan`), `SimilarFinder` izleri ve aynı kare gruplarını birleşim-bul ile türlü gruplara çevirir. Arayüz ve Planner'ın mevcut "Kalsın → `_Benzerler`" akışı aynen kullanılır.

**Tech Stack:** .NET 8 (`net8.0-windows`, `UseWPF`), WPF `System.Windows.Media.Imaging`, MetadataExtractor 2.9.3, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-30-benzer-fotograflar-design.md`

## Global Constraints

- Yeni NuGet paketi eklenmez.
- Core, CLI ve Tests: `<TargetFramework>net8.0-windows</TargetFramework>` + `<UseWPF>true</UseWPF>`.
- Hiçbir dosya otomatik taşınmaz; ayıklanan dosya yalnızca kullanıcı "Kalsın" işaretini kaldırırsa `_Benzerler`'e gider.
- Önbellek dosyası: `%LOCALAPPDATA%\FotoDuzenleyici\benzerlik-onbellek.json`; yalnızca iz saklanır.
- Önbellek anahtarı: boyut + son yazma zamanı (UTC tick) + ilk 64 KB'nin SHA-256'sı. Yol kullanılmaz.
- İz için çözme genişliği en fazla 72 px; önizleme ~1000 px. Tam çözünürlükte çözme yok.
- Eşikler: kopya Hamming ≤ 4; seri çekim Hamming ≤ 12 + iki dosyada EXIF tarihi + aynı marka/model + aradaki süre < 10 sn.
- İz hesaplama 4 iş parçacığı.
- Kapsam: yalnızca `FileKind.Photo`; RAW, video, birebir kopyaların kopya tarafı ve `_Benzerler` içindekiler hariç.
- Arayüz ve kullanıcıya görünen metinler Türkçe; kod yorumları mevcut dosyalardaki gibi Türkçe.
- Her task sonunda `dotnet test FotoDuzenleyici.sln` tamamen yeşil olmalı.

## Review Focus

1. **Düz/tek renk görüntüler** (siyah kare, boş gökyüzü, beyaz ekran görüntüsü) dHash'te neredeyse sıfır üretir ve birbirleriyle "kopya" sanılır. Beklenen: düz görüntünün izi çıkarılmaz (`null`, sebep "düz görüntü"). Şartnameye eklenen bir koruma. → Task 1 testi.
2. **Alışılmadık görüntü biçimleri** (8×8 küçük PNG, saydam PNG, gri tonlu JPEG): istisna olmadan iz ya da `null`. → Task 1 testi.
3. **Kilitli / erişilemeyen dosya** iz hesaplanırken: hata listesine düşer, diğer dosyalar işlenir. → Task 2 testi.
4. **Paralel yazma**: 4 iş parçacığı aynı önbelleğe yazarken kayıp ya da istisna olmamalı. → Task 2 testi (50 dosya).
5. **Önizlemede dosya yok** (tarama sonrası silinmiş/taşınmış): çökme yok, "Önizleme yok" gösterilir. → Task 1 testi (`Decode` eksik dosyada `null`) + Task 5 elle kontrol.

---

### Task 1: Projeleri `net8.0-windows`'a geçir, yönü oku, `ImageHasher`

**Files:**
- Modify: `FotoDuzenleyici.Core/FotoDuzenleyici.Core.csproj`, `FotoDuzenleyici.Cli/FotoDuzenleyici.Cli.csproj`, `FotoDuzenleyici.Tests/FotoDuzenleyici.Tests.csproj` (TargetFramework + UseWPF)
- Modify: `FotoDuzenleyici.Core/Models.cs` (`MediaFile.Orientation`)
- Modify: `FotoDuzenleyici.Core/Scanner.cs` (`ReadMetadataInto`: IFD0 Orientation)
- Create: `FotoDuzenleyici.Core/ImageHasher.cs`
- Create: `FotoDuzenleyici.Tests/TestImages.cs` (WPF ile desenli görüntü üretir)
- Test: `FotoDuzenleyici.Tests/ImageHasherTests.cs`

**Interfaces:**
- Produces:
  - `MediaFile.Orientation : int { get; set; } = 1`
  - `public static class ImageHasher`
    - `public static BitmapSource? Decode(string path, int orientation, int decodePixelWidth)` — WIC ile `decodePixelWidth` genişlikte çözer, EXIF yönünü (1–8) uygular, dondurur. Dosya yok/çözülemezse `null`.
    - `public static ulong? DHash(string path, int orientation, out string? error)` — `Decode(path, orientation, 72)` → Gray8 → alan ortalamasıyla 9×8 → 64 bit dHash. Çözülemezse `null` + `error`; düz görüntüde (`max-min` gri değeri 9×8 ızgarada < 8) `null` + `error = "düz görüntü"`.
    - `public static int Distance(ulong a, ulong b)` — `BitOperations.PopCount(a ^ b)`.
  - Test yardımcısı `static class TestImages`:
    - `static byte[] PatternJpeg(int width, int height, int seed, int quality = 90, ushort? orientation = null, bool rotate90 = false)` — seed'e bağlı bloklar + gradyan; `rotate90` pikselleri saat yönünde 90° döndürür; `orientation` EXIF yön etiketi (`/app1/ifd/{ushort=274}`) yazar.
    - `static byte[] SolidJpeg(byte r, byte g, byte b)` ve `static byte[] TinyPng(bool transparent)`.

- [ ] **Step 1: Projeleri geçir ve mevcut testleri doğrula**

Üç csproj'da `net8.0` → `net8.0-windows`, `<UseWPF>true</UseWPF>` ekle.
Run: `dotnet test FotoDuzenleyici.sln`
Expected: 49 test geçer.

- [ ] **Step 2: Başarısız testleri yaz** (`ImageHasherTests.cs`; her test geçici klasöre yazar)

```csharp
[Fact] void ResizedRecompressedCopy_IsWithinFourBits()
    // PatternJpeg(1200, 800, seed: 1, quality: 95) vs PatternJpeg(400, 267, seed: 1, quality: 60)
    // Assert.InRange(ImageHasher.Distance(h1, h2), 0, 4)
[Fact] void ExifOrientation6_MatchesPhysicallyRotatedImage()
    // A = PatternJpeg(1200, 800, 2, orientation: 6); B = PatternJpeg(1200, 800, 2, rotate90: true)
    // DHash(A, 6) ile DHash(B, 1) arası ≤ 4; DHash(A, 1) ile DHash(B, 1) arası > 12
[Fact] void DifferentPattern_IsFarApart()            // seed 1 vs seed 3 → Distance > 12
[Fact] void CorruptFile_ReturnsNullWithError()       // rastgele 1 KB bayt ".jpg" → null, error != null
[Fact] void SolidImage_ReturnsNull_AsFlat()          // SolidJpeg(0,0,0) → null, error == "düz görüntü"
[Fact] void TinyAndTransparentPng_DoNotThrow()       // TinyPng(false), TinyPng(true) → istisna yok
[Fact] void Decode_MissingFile_ReturnsNull()         // Decode("…\\yok.jpg", 1, 1000) == null
[Fact] void Scanner_ReadsOrientation()               // PatternJpeg(..., orientation: 6) → ReadFile(p).Orientation == 6; etiket yoksa 1
```

- [ ] **Step 3: Testin başarısız olduğunu gör**

Run: `dotnet test FotoDuzenleyici.Tests --filter ImageHasherTests`
Expected: derleme hatası (`ImageHasher`, `Orientation` yok).

- [ ] **Step 4: Uygula**

- `Scanner.ReadMetadataInto`: `ExifIfd0Directory` içinden `ExifDirectoryBase.TagOrientation` (yoksa 1).
- `Decode`: `FileStream` (FileShare.ReadWrite) + `BitmapImage { StreamSource, DecodePixelWidth, CacheOption = OnLoad, CreateOptions = IgnoreColorProfile }`; yön için `TransformedBitmap` (2: yatay ayna, 3: 180°, 4: dikey ayna, 5: ayna+270°, 6: 90°, 7: ayna+90°, 8: 270°); `Freeze()`. Tüm istisnalar yakalanır.
- `DHash`: `FormatConvertedBitmap(..., PixelFormats.Gray8)` → `CopyPixels` → 9×8 alan ortalaması → satır içinde `g[y,x] < g[y,x+1]` ise bit 1.

- [ ] **Step 5: Testlerin geçtiğini gör**

Run: `dotnet test FotoDuzenleyici.sln`
Expected: tümü geçer (49 + 8).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "ImageHasher: WIC ile dHash, EXIF yönü; projeler net8.0-windows"
```

---

### Task 2: `HashCache` ve `SimilarityScan`

**Files:**
- Create: `FotoDuzenleyici.Core/HashCache.cs`
- Create: `FotoDuzenleyici.Core/SimilarityScan.cs`
- Test: `FotoDuzenleyici.Tests/HashCacheTests.cs`

**Interfaces:**
- Consumes: `ImageHasher.DHash` (Task 1).
- Produces:
  - `public sealed class HashCache`
    - `public static string DefaultPath` → `%LOCALAPPDATA%\FotoDuzenleyici\benzerlik-onbellek.json`
    - `public HashCache(string path)` — dosyayı yükler; yoksa/bozuksa boş başlar, istisna fırlatmaz.
    - `public static string Key(string filePath)` → `$"{size}:{lastWriteUtcTicks}:{sha256(ilk 64 KB) hex}"`
    - `public bool TryGet(string key, out ulong hash)`, `public void Set(string key, ulong hash)` — iş parçacığı güvenli (`ConcurrentDictionary`).
    - `public int Count`
    - `public void Save()` — `path + ".tmp"`'ye yaz, sonra üzerine taşı (`File.Move(tmp, path, overwrite: true)`); klasörü oluşturur.
  - `public static class SimilarityScan`
    - `public static Dictionary<MediaFile, ulong> Compute(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dupGroups, string? ignoreFolder, HashCache cache, List<string> errors, IProgress<(int done, int total, int fromCache)>? progress = null, CancellationToken ct = default)`
      - Kapsam kuralı Global Constraints'teki gibi (`ignoreFolder` = `_Benzerler` yolu).
      - `Parallel.ForEach`, `MaxDegreeOfParallelism = 4`; hatalar `errors`'a `"{path}: {error}"` olarak (kilitli) eklenir.
      - İptalde `cache.Save()` çağrılır, sonra `OperationCanceledException` yeniden fırlatılır. Normal bitişte de `Save()`.
      - `Save()` başarısız olursa (IO/yetki) tarama sürer; `errors`'a `"Önbellek kaydedilemedi: {mesaj}"` eklenir.

- [ ] **Step 1: Başarısız testleri yaz** (`HashCacheTests.cs`; `TestImages.PatternJpeg` ile dosya üretir, önbellek yolu geçici klasörde)

```csharp
[Fact] void SecondRun_ComesFromCache()            // 3 dosya; 2. Compute'ta son progress.fromCache == 3, aynı izler
[Fact] void ChangedFile_IsRecomputed()            // dosyanın içeriği değişir + mtime ileri → fromCache == 2, yeni iz farklı
[Fact] void MovedAndRenamed_StillHitsCache()      // File.Move başka klasöre farklı adla (mtime korunur) → fromCache == 3
[Fact] void CorruptCacheFile_IsIgnored()          // önbellek dosyasına "{bozuk" yaz → new HashCache(path).Count == 0, istisna yok
[Fact] void Cancel_SavesPartialProgress()         // ilk progress'te iptal → ThrowsAny<OperationCanceledException>; new HashCache(path).Count >= 1
[Fact] void LockedFile_IsReportedAndOthersHashed() // bir dosya FileShare.None ile açık → errors tek kayıt, diğer 2 dosyanın izi var
[Fact] void ParallelWrites_LoseNothing()          // 50 farklı seed'li dosya → sonuç 50 iz, Save sonrası yeni HashCache.Count == 50
[Fact] void ExcludesRawVideoDupCopiesAndSimilarFolder() // MediaFile listesiyle: yalnızca uygun fotoğraf sonuçta
[Fact] void UnwritableCache_ScanStillReturnsHashes()   // önbellek yolu var olan bir klasörü gösterir → 3 iz döner, errors "Önbellek kaydedilemedi" içerir
```

- [ ] **Step 2: Başarısız olduğunu gör**

Run: `dotnet test FotoDuzenleyici.Tests --filter HashCacheTests`
Expected: derleme hatası.

- [ ] **Step 3: `HashCache` ve `SimilarityScan.Compute`'u uygula** (JSON: `System.Text.Json`, `Dictionary<string, ulong>`).

- [ ] **Step 4: Testlerin geçtiğini gör**

Run: `dotnet test FotoDuzenleyici.sln`
Expected: tümü geçer.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Benzerlik izi önbelleği: yoldan bağımsız anahtar, iptalde kayıt"
```

---

### Task 3: `SimilarFinder`

**Files:**
- Create: `FotoDuzenleyici.Core/SimilarFinder.cs`
- Test: `FotoDuzenleyici.Tests/SimilarFinderTests.cs`

**Interfaces:**
- Consumes: `SameFrameGroup` (mevcut), `ImageHasher.Distance` (Task 1).
- Produces:
  - `public enum SimilarKind { SameFrame, ResizedCopy, Burst }`
  - `public sealed class SimilarGroup { required List<MediaFile> Members; required SimilarKind Kind; MediaFile? Suggested }`
  - `public sealed class SimilarOptions { int CopyMaxDistance = 4; int BurstMaxDistance = 12; TimeSpan BurstWindow = TimeSpan.FromSeconds(10); }`
  - `public static List<SimilarGroup> SimilarFinder.Find(IReadOnlyList<MediaFile> files, IReadOnlyDictionary<MediaFile, ulong> hashes, IReadOnlyList<SameFrameGroup> sameFrame, SimilarOptions? options = null)` — `hashes` boşsa sonuç yalnızca aynı kare grupları (Kind = SameFrame).

Algoritma kararları (testlerin belirlemediği):
- Birleşim-bul; bağlar: aynı kare grup üyeleri, kopya bağları, seri bağları. Her bağın türü kaydedilir; grup türü öncelik sırası SameFrame > ResizedCopy > Burst.
- Kopya adayları: 64 bit → 5 parça (bit 0–12, 13–25, 26–38, 39–51, 52–63); her parça değeri için `Dictionary<(int part, ulong value), List<MediaFile>>`; yalnızca ortak kovadaki çiftler karşılaştırılır.
- Seri adayları: EXIF tarihli dosyalar `Taken`'a göre sıralanır, kayan pencere `BurstWindow` içinde karşılaştırılır; marka+model büyük/küçük harf duyarsız eşit olmalı.
- Öneri: SameFrame → çözünürlük ↓, `Modified` ↑, `Size` ↓; ResizedCopy → çözünürlük ↓, `DateSource == Exif` önce, `Modified` ↑; Burst → `null`.
- Gruplar ilk üyenin `Taken`'ına göre sıralı döner.

- [ ] **Step 1: Başarısız testleri yaz** (izler elle verilir: `ulong` sabitleri; `MediaFile` nesneleri bellekte)

```csharp
[Fact] void FourBitsApart_IsResizedCopy_EvenWithoutExif()      // biri FileTime tarihli → Kind == ResizedCopy
[Fact] void FiveBitsApart_WithoutExif_IsNotGrouped()
[Fact] void TwelveBitsApart_SameCamera_WithinTenSeconds_IsBurst() // Suggested == null
[Fact] void TwelveBitsApart_ElevenSecondsApart_IsNotGrouped()
[Fact] void TwelveBitsApart_DifferentCamera_IsNotGrouped()
[Fact] void ChainedPairs_FormOneGroup()                        // A~B (3 bit), B~C (3 bit), A-C 6 bit → tek grup, 3 üye
[Fact] void SameFrameGroup_MergesWithCopyOfIt()                // SameFrameGroup{A,B} + C, A'ya 2 bit → tek grup, Kind == SameFrame
[Fact] void NoHashes_ReturnsSameFrameGroupsOnly()
[Fact] void ResizedCopy_SuggestsHighestResolutionThenExif()    // 6000×4000 EXIF'siz vs 1600×1067 EXIF'li → 6000×4000; eşit çözünürlükte EXIF'li
[Fact] void TwentyThousandHashes_UnderThreeSeconds()           // rastgele 20.000 iz + 100 seri; Stopwatch < 3 sn
```

- [ ] **Step 2: Başarısız olduğunu gör**

Run: `dotnet test FotoDuzenleyici.Tests --filter SimilarFinderTests`
Expected: derleme hatası.

- [ ] **Step 3: `SimilarFinder.Find`'ı uygula**

- [ ] **Step 4: Testlerin geçtiğini gör**

Run: `dotnet test FotoDuzenleyici.sln`
Expected: tümü geçer.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "SimilarFinder: küçültülmüş kopya ve seri çekim grupları"
```

---

### Task 4: Uçtan uca test ve CLI `--similar`

**Files:**
- Modify: `FotoDuzenleyici.Cli/Program.cs`
- Test: `FotoDuzenleyici.Tests/IntegrationTests.cs` (yeni test)

**Interfaces:**
- Consumes: `SimilarityScan.Compute`, `HashCache`, `SimilarFinder.Find`, `SameFrameFinder.Find`, `Planner.Build(..., similarOf)`, `Executor(journal, recycle)`, `Executor.Undo(..., recycle:)`.

- [ ] **Step 1: Başarısız testi yaz**

```csharp
[Fact] void ResizedCopy_EndToEnd_ToSimilarFolder_AndUndo()
    // kaynak: "IMG_0001.JPG" = PatternJpeg(1200, 800, 5, 95, EXIF'li değil) ve
    //         "WhatsApp Image 2024-03-10.jpeg" = PatternJpeg(400, 267, 5, 60)
    // Compute (önbellek geçici klasörde) → Find → tek grup, Kind == ResizedCopy, Suggested = büyük olan
    // similarOf = { küçük → büyük } → Planner → Executor(FakeRecycle).Run
    // _Benzerler altında tam 1 dosya; düzenli klasör tekrar taranınca grup yok, tüm planda AlreadyInPlace
    // Undo(recycle: FakeRecycle).Done == 2, iki kaynak yerinde
```

- [ ] **Step 2: Testi çalıştır**

Run: `dotnet test FotoDuzenleyici.Tests --filter ResizedCopy_EndToEnd`
Expected: PASS. Bu test Task 1–3'ün parçalarını birlikte doğrular; yeni üretim kodu gerektirmez. Kırılabildiğini görmek için testte `SimilarFinder.Find`'a `new SimilarOptions { CopyMaxDistance = -1 }` verip FAIL gör, sonra geri al.

- [ ] **Step 3: CLI `--similar`**

`--similar` verilirse: `SameFrameFinder.Find` sonrası `SimilarityScan.Compute(..., new HashCache(HashCache.DefaultPath), errors, ilerleme stderr'e %10'luk)` → `SimilarFinder.Find`; mevcut "Muhtemelen aynı kare" raporu yerine:
```
== Benzerler ==
Aynı kare: N grup  Küçültülmüş kopya: N grup  Seri çekim: N grup
İz çıkarılamayan: N
```
ve tür başına ilk 5 grup (`★` öneri). `--similar` yoksa mevcut rapor aynen kalır. Kullanım yorumuna `--similar` eklenir.

Run: `dotnet run --project FotoDuzenleyici.Cli -- --root <TestImages ile üretilmiş klasör> --similar`
Expected: "Küçültülmüş kopya: 1 grup".

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "Benzerler: uçtan uca test ve CLI --similar"
```

---

### Task 5: Arayüz — Benzerler sekmesi, Durdur, önizleme

**Files:**
- Modify: `FotoDuzenleyici.App/MainWindow.xaml`
- Modify: `FotoDuzenleyici.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: Task 1–3 API'leri; mevcut `SimilarRow`, `BuildSimilarMap`, `Planner.Build(..., similarOf)`.
- Değişiklikler:
  - Sekme başlığı "Benzerler"; `SimilarRow`'a `Kind` (gösterim: "Aynı kare" / "Küçültülmüş kopya" / "Seri çekim") ve **Tür** sütunu; `Suggested` grubun `Suggested`'ına göre (seri çekimde hiçbiri).
  - `SuggestSimilar_Click`: önerisi olmayan gruplara dokunmaz.
  - Seçenek `OptSimilar`: "Benzer fotoğrafları da ara", `IsChecked="True"`.
  - `StopBtn` "Durdur": taramada etkin, `_cts.Cancel()`. `Scanner.Scan` ve `DuplicateFinder.Find` aynı token'ı alır; bu adımlarda iptal → "Tarama durduruldu." ve sonuç yok. `SimilarityScan.Compute` iptalinde → aynı kare grupları gösterilir, durum: "Benzerlik araması durduruldu; hesaplanan izler kaydedildi."
  - İlerleme metni: `$"Benzerlik izi… {done}/{total} ({fromCache}'i önbellekten)"`.
  - Önizleme: tablonun sağında `Image` (yükseklik serbest, `Stretch=Uniform`) + bilgi `TextBlock` (çözünürlük, MB, tarih, kamera). `SelectionChanged` → artan sürüm sayacı; `Task.Run(() => ImageHasher.Decode(path, orientation, 1000))`; dönüşte sürüm eşleşmiyorsa atılır; `null` ise "Önizleme yok".
  - Özet: `Aynı kare: N, Küçültülmüş kopya: N, Seri çekim: N grup` ve `Benzerlik izi çıkarılamayan: N` + ilk 3 hata.

- [ ] **Step 1: Uygula ve derle**

Run: `dotnet build FotoDuzenleyici.sln` → 0 hata; `dotnet test FotoDuzenleyici.sln` → tümü geçer.

- [ ] **Step 2: Elle doğrula** (örnek klasör: `TestImages` ile üretilmiş orijinal + küçültülmüş kopya + 3 karelik seri çekim + EXIF'li aynı kare çifti; scratchpad'deki geçici konsol projesiyle `D:\FD_Benzer2`'ye yazılır)

- Tara → Benzerler sekmesinde 3 grup, Tür sütunu doğru; seri çekimde ★ yok.
- Satır seçince önizleme görünür; ok tuşlarıyla gezilir; dosya silinince "Önizleme yok".
- "Önerileri uygula" seri çekim grubunu değiştirmez.
- Tara sırasında Durdur: benzerlik adımında → aynı kare grupları duruyor, durum metni doğru; ikinci Tara'da "…önbellekten" sayısı > 0.
- Plan + Uygula (Geri Dönüşüm Kutusu kapalı) → `_Benzerler` doğru.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "Arayüz: Benzerler sekmesi, Durdur, önizleme"
```

---

### Task 6: Belgeler, gönderim ve gerçek arşivde hız ölçümü

**Files:**
- Modify: `CHANGELOG.md` (`[Yayınlanmamış]` → Eklendi), `YOL_HARITASI.md` (Benzer fotoğraflar ✅)

- [ ] **Step 1: CHANGELOG ve yol haritası**; `dotnet test FotoDuzenleyici.sln` yeşil; commit.
- [ ] **Step 2: Kullanıcıya hangi klasörün ölçüleceğini sor**, onayla salt okunur ölçüm yap (`--apply` kullanılmaz; `--no-hash` SHA'yı atlar, benzerlik için gerekmez):
  `dotnet run --project FotoDuzenleyici.Cli -c Release -- --root "<seçilen klasör>" --similar --no-hash`
  İki kez çalıştır; ilk ve ikinci süreyi (önbellek etkisi), tür başına grup sayılarını ve örnek grupları raporla.
- [ ] **Step 3: Kullanıcı onayıyla `main`'e gönder**, Actions sonucunu kontrol et.
