# Bulanık ve Karanlık Kareler — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fotoğrafların netliğini ve karanlığını yerelde ölçüp seri çekim gruplarında en net kareyi, arşiv genelinde bulanık/karanlık kareleri işaretlemek; kullanıcının ayıkladıklarını `_Kusurlu`'ya taşımak.

**Architecture:** Core'da ölçüm (`QualityMeter`), önbellek (`QualityCache`), tarama (`QualityScan`) ve eşik/sıralama kuralları (`QualityRules`). Kusurlu liste, mevcut `SimilarGroupState`/`SimilarRow` modeli üzerine tek kareli gruplar olarak kurulur; böylece şerit kartı, `ThumbnailLoader`, `ReviewStore` ve `CompareWindow` olduğu gibi kullanılır. Planner'a `Flawed` eylemi eklenir.

**Tech Stack:** C# / .NET 8 `net8.0-windows`, WPF, WIC (`System.Windows.Media.Imaging`), xUnit. Yeni paket yok.

**Spec:** `docs/superpowers/specs/2026-11-06-bulanik-karanlik-design.md`

## Global Constraints

- Yeni NuGet paketi eklenmez; görüntü çözme WIC ile (`ImageHasher`).
- Hiçbir dosya otomatik taşınmaz; ayıklama yalnızca kullanıcının işaretiyle, Uygula'da.
- Ayıklanan kusurlu kare `_Kusurlu\yyyy-MM\` altına taşınır (`PlanOptions.FlawedFolderName = "_Kusurlu"`); Geri Al çalışır.
- Önbellek: `%LOCALAPPDATA%\FotoDuzenleyici\kalite-onbellek.json`, anahtar `HashCache.Key`.
- Ölçüm çözüm genişliği en fazla 1024 px; 4×4 parça; netlik = en yüksek 2 parçanın Laplace varyansı ortalaması; karanlık piksel eşiği 20.
- Öncelik: birebir kopya > Benzerler ayıklaması > Kusurlu ayıklaması > normal taşıma.
- Arayüz metinleri Türkçe; rozet metinleri tam olarak `BULANIK`, `KARANLIK`, `RAW`; en net işareti `◎`.
- Dosyaları PowerShell `Get-Content`/`Set-Content` ile yeniden yazma (Türkçe karakterler bozulur); Edit aracı kullan.
- Commit mesajları `git commit -F` ile, sonunda `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- **Küçük görüntüler (WhatsApp, 640–1600 px):** 1024'ten dar görüntü büyütülerek çözülmemeli (büyütme yapay bulanıklık üretir); 320 px'ten dar görüntü ölçülmez ("çok küçük"). → Task 1 testi.
- **Düz sahne (bulutsuz gökyüzü, beyaz duvar):** ayrıntı yoksa bulanık sayılmamalı; yalnızca karanlıksa KARANLIK alabilir. → Task 1 (`Flat` alanı) ve Task 4 testi.
- **Ölçümü yarıda durdurma:** o ana kadarki ölçümler önbelleğe yazılır, sonraki tarama kaldığı yerden sürer; yarım sonuçla Kusurlu listesi gösterilmez. → Task 2 testi, Task 7.
- **Seri grupta ölçülemeyen kare:** yüzde almaz, ◎/BULANIK almaz, diğer karelerin sıralamasını bozmaz. → Task 4 testi.
- **Kusurlu kartta karar verip yeniden tarama:** karar `ReviewStore`'dan geri gelir; tek kareli grupta "en az bir kare kalsın" kuralı devreye girmez (ayıklanmış kare ayıklanmış kalır). → Task 5 testi.

---

### Task 1: QualityMeter — netlik ve karanlık ölçümü

**Files:**
- Create: `FotoDuzenleyici.Core/QualityMeter.cs`
- Modify: `FotoDuzenleyici.Core/ImageHasher.cs` (`TryDecode` → `internal`)
- Modify: `FotoDuzenleyici.Tests/TestImages.cs` (yeni üreticiler)
- Test: `FotoDuzenleyici.Tests/QualityMeterTests.cs`

**Interfaces:**
- Produces: `public readonly record struct QualityScore(double Sharpness, double MeanLuma, double DarkFraction, bool Flat);`
- Produces: `public static class QualityMeter { public const int MeasureWidth = 1024; public const int MinWidth = 320; public const byte DarkPixel = 20; public static QualityScore? Measure(string path, int orientation, out string? error); }`
- Produces (TestImages): `BlurredPatternJpeg(int w, int h, int seed, int radius)`, `HalfBlurredPatternJpeg(int w, int h, int seed, int radius)` (sol yarı keskin, sağ yarı bulanık), `DarkPatternJpeg(int w, int h, int seed, double scale)` (desen parlaklığı `scale` ile çarpılmış). Bulanıklık: piksel dizisine `radius` yarıçaplı kutu bulanıklığı, iki geçiş.

- [ ] **Step 1: Yeni test görüntüsü üreticilerini ve başarısız testleri yaz** (`QualityMeterTests`, dosyalar geçici klasöre yazılır)

```csharp
[Fact] public void Sharp_ScoresHigherThan_SameImageBlurred()
    => Assert.True(M(Pattern(1600,1200,1)).Sharpness > 4 * M(Blurred(1600,1200,1,radius:6)).Sharpness);
[Fact] public void HalfBlurred_ScoresLikeSharp()            // arkası flu portre
    => Assert.True(M(HalfBlurred(1600,1200,1,6)).Sharpness > 0.8 * M(Pattern(1600,1200,1)).Sharpness);
[Fact] public void Dark_HasLowMeanAndHighDarkFraction()
{ var s = M(Dark(1600,1200,1,0.08)); Assert.True(s.MeanLuma < 20); Assert.True(s.DarkFraction > 0.9); }
[Fact] public void SolidColor_IsFlat()                       => Assert.True(M(TestImages.SolidJpeg(120,160,200)).Flat);
[Fact] public void Pattern_IsNotFlat()                       => Assert.False(M(Pattern(1600,1200,1)).Flat);
[Fact] public void SmallImage_NotUpscaled_ScoresLikeLargeSharp()   // 800 px keskin, 4000 px keskin ile aynı mertebede
    => Assert.True(M(Pattern(800,600,1)).Sharpness > 0.5 * M(Pattern(4000,3000,1)).Sharpness);
[Fact] public void TinyImage_ReturnsNull_WithReason()
{ Assert.Null(QualityMeter.Measure(Write(Pattern(200,150,1)), 1, out var e)); Assert.Equal("çok küçük", e); }
[Fact] public void CorruptFile_ReturnsNull()                 => Assert.Null(QualityMeter.Measure(Write(new byte[]{1,2,3}), 1, out _));
[Fact] public void Orientation_DoesNotChangeScore()
    => Assert.InRange(M(TestImages.PatternJpeg(1600,1200,1,orientation:6)).Sharpness /
                      M(TestImages.PatternJpeg(1600,1200,1)).Sharpness, 0.9, 1.1);
```

`M(bytes)` = dosyaya yaz + `QualityMeter.Measure(path, 1, out _)!.Value`. `Pattern/Blurred/HalfBlurred/Dark` TestImages kısaltmaları.

- [ ] **Step 2: Çalıştır, derleme hatasıyla başarısız olduğunu gör**

Run: `dotnet test FotoDuzenleyici.Tests --filter QualityMeterTests`
Expected: FAIL (`QualityMeter` tanımlı değil)

- [ ] **Step 3: `QualityMeter.Measure` yaz**

Önce `BitmapDecoder.Create(..., BitmapCacheOption.None)` ile ilk karenin `PixelWidth`'i okunur (yalnız başlık). `< MinWidth` → `null`, `error = "çok küçük"`. Çözüm genişliği `Math.Min(MeasureWidth, gerçekGenişlik)`; `ImageHasher.TryDecode(path, orientation, w, out error)` → `Gray8`. Algoritma:

```text
lap(x,y) = 4p(x,y) − p(x−1,y) − p(x+1,y) − p(x,y−1) − p(x,y+1)   (kenar pikseller hariç)
16 parça (4×4); her parçada var(lap) → sırala → Sharpness = en yüksek 2'nin ortalaması
MeanLuma = ortalama p;  DarkFraction = (p < DarkPixel) oranı
Flat = 16 parçanın ortalama parlaklıklarının (max − min) < 6  VE  en yüksek parça var(lap) < 4
```

Her hata (`try/catch`) → `null` + `error = ex.Message`.

- [ ] **Step 4: Testleri çalıştır**

Run: `dotnet test FotoDuzenleyici.Tests --filter QualityMeterTests`
Expected: PASS (9 test). `Flat` eşikleri testlerle uyuşmazsa yalnız bu iki sabiti ayarla ve gerekçeyi yoruma yaz.

- [ ] **Step 5: Commit** — `Kalite: netlik ve karanlık ölçümü (QualityMeter)`

---

### Task 2: QualityCache ve QualityScan

**Files:**
- Create: `FotoDuzenleyici.Core/QualityCache.cs`, `FotoDuzenleyici.Core/QualityScan.cs`
- Test: `FotoDuzenleyici.Tests/QualityScanTests.cs`

**Interfaces:**
- Consumes: `QualityMeter.Measure`, `QualityScore`, `HashCache.Key`
- Produces: `public sealed class QualityCache { public static string DefaultPath { get; } /* ...\kalite-onbellek.json */ public QualityCache(string path); public int Count { get; } public bool TryGet(string key, out QualityScore s); public void Set(string key, QualityScore s); public void Save(); }`
- Produces: `public static class QualityScan { public static Dictionary<MediaFile, QualityScore> Compute(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dupGroups, IReadOnlyList<string> ignoreFolders, QualityCache cache, List<string> errors, IProgress<(int done, int total, int fromCache)>? progress = null, CancellationToken ct = default); }`

`HashCache` ile aynı desen (ConcurrentDictionary, `.tmp` + `File.Move`, bozuk dosya → boş). `QualityScan` `SimilarityScan.Compute` ile aynı: yalnız `FileKind.Photo`, kopyalar ve `ignoreFolders` altındakiler hariç, `MaxDegreeOfParallelism = 4`, `finally` içinde `cache.Save()`.

- [ ] **Step 1: Başarısız testleri yaz**

```csharp
[Fact] public void Cache_SaveLoad_RoundTrips()            // Set → Save → yeni QualityCache aynı yolu okur → TryGet eşit
[Fact] public void Cache_CorruptFile_StartsEmpty()        // "{bozuk" yaz → Count == 0
[Fact] public void Scan_SkipsRawCopiesAndIgnoredFolders() // RAW, kopya, _Kusurlu\x.jpg sonuçta yok; normal JPG var
[Fact] public void Scan_SecondRun_ComesFromCache()        // ikinci Compute'ta progress.fromCache == total
[Fact] public void Scan_FileMovedAndRenamed_StillCached() // dosyayı taşı (zaman damgası korunur) → fromCache == 1
[Fact] public void Scan_Cancelled_SavesWhatWasMeasured()
{
    // 8 dosya; progress ilk raporda iptal eder → OperationCanceledException
    // yeni QualityCache(aynı yol).Count >= 1
}
[Fact] public void Scan_UnreadableFile_GoesToErrors()     // bozuk .jpg → errors tek kayıt, "yol: sebep"
```

- [ ] **Step 2: Çalıştır, başarısız** — `dotnet test FotoDuzenleyici.Tests --filter QualityScanTests` → FAIL (tanımsız tipler)
- [ ] **Step 3: `QualityCache` ve `QualityScan`'i yaz** (JSON kaydı `record Entry(double S, double L, double D, bool F)`)
- [ ] **Step 4: Çalıştır** — PASS (7 test)
- [ ] **Step 5: Commit** — `Kalite: önbellek ve tarama`

---

### Task 3: Komut satırı `--quality` ve kalibrasyon (KULLANICI KONTROL NOKTASI)

**Files:**
- Modify: `FotoDuzenleyici.Cli/Program.cs`

**Interfaces:**
- Consumes: `QualityScan.Compute`, `QualityCache`
- Produces: `--quality` seçeneği; `--csv X` ile `X-kalite.csv` (`Dosya;Netlik;Parlaklik;KaranlikOrani;Duz;Cekim;Kamera;Genislik`) — tüm ölçümler.

- [ ] **Step 1: `--quality` ekle** — ölçüm süresi, ölçülen / önbellekten / ölçülemeyen sayısı, netliğe göre en düşük 10 dosya yazdırılır; `--csv` ile `-kalite.csv`. Kullanım yorumuna satır eklenir. `ignoreFolders` = `dest\_Benzerler`, `dest\_Kusurlu`.
- [ ] **Step 2: Derle ve tüm testleri çalıştır** — `dotnet test FotoDuzenleyici.Tests` → PASS
- [ ] **Step 3: Gerçek arşivde ölç** (yalnız rapor, `--apply` yok)

Run: `dotnet run --project FotoDuzenleyici.Cli -- --root "D:\1- HERKES\001 - İSMAİL HAKKI BAŞOĞLU" --dest "D:\1- HERKES\001 - İSMAİL HAKKI BAŞOĞLU" --quality --csv <scratchpad>\arsiv.csv`
Expected: ölçüm süresi ve sayılar; `arsiv-kalite.csv` oluşur.

- [ ] **Step 4: Kalibrasyon sayfası hazırla** — scratchpad'de tek bir HTML: netlik yüzdelik dilimleri (1, 5, 10, 25, 50), en düşük netlikli 40 kare ve en karanlık 20 kare (küçük resimler `ImageHasher.Decode(..., 320)` ile PNG'ye çevrilip gömülür; her birinin altında netlik/parlaklık). Ayrıca 10 seri çekim grubunda kareler yan yana, netlik yüzdeleriyle.
- [ ] **Step 5: DUR — kullanıcıyla eşikleri seç.** Sayfayı gönder; `BlurThreshold`, `DarkMean`, `DarkShare`, `SoftShare` (başlangıç 0.50), `TieShare` (başlangıç 0.85) değerlerini birlikte belirle. Seçilen değerler Task 4'e girer.
- [ ] **Step 6: Commit** — `CLI: --quality ve kalite CSV'si`

---

### Task 4: QualityRules — eşikler ve seri çekim sıralaması

**Files:**
- Create: `FotoDuzenleyici.Core/QualityRules.cs`
- Test: `FotoDuzenleyici.Tests/QualityRulesTests.cs`

**Interfaces:**
- Consumes: `QualityScore`
- Produces:
  ```csharp
  public static class QualityRules {
      public const double BlurThreshold = /* Task 3 */, DarkMean = /* Task 3 */, DarkShare = /* Task 3 */,
                          SoftShare = /* Task 3, başl. 0.50 */, TieShare = /* Task 3, başl. 0.85 */;
      public static bool IsBlurry(QualityScore s);   // !s.Flat && s.Sharpness < BlurThreshold
      public static bool IsDark(QualityScore s);     // s.MeanLuma < DarkMean && s.DarkFraction > DarkShare
      public static string? FlawText(QualityScore s); // "BULANIK" | "KARANLIK" | "BULANIK · KARANLIK" | null
      public static List<BurstRank> RankBurst(IReadOnlyList<QualityScore?> scores);
  }
  public readonly record struct BurstRank(int? Percent, bool Sharpest, bool Soft);
  ```

- [ ] **Step 1: Başarısız testleri yaz**

```csharp
static QualityScore S(double sharp, double mean = 120, double dark = 0, bool flat = false) => new(sharp, mean, dark, flat);

[Fact] public void Blurry_BelowThreshold()   { Assert.True(IsBlurry(S(BlurThreshold - 1))); Assert.False(IsBlurry(S(BlurThreshold + 1))); }
[Fact] public void Flat_IsNeverBlurry()      => Assert.False(IsBlurry(S(0, flat: true)));
[Fact] public void FlatDark_IsDarkOnly()     => Assert.Equal("KARANLIK", FlawText(S(0, mean: DarkMean - 1, dark: DarkShare + 0.01, flat: true)));
[Fact] public void Dark_NeedsBothConditions(){ Assert.False(IsDark(S(500, DarkMean - 1, DarkShare - 0.01))); Assert.True(IsDark(S(500, DarkMean - 1, DarkShare + 0.01))); }
[Fact] public void Rank_PercentOfSharpest()
{
    var r = RankBurst(new QualityScore?[] { S(100), S(40), S(90) });
    Assert.Equal(new int?[] { 100, 40, 90 }, r.Select(x => x.Percent));
    Assert.Equal(new[] { true, false, false }, r.Select(x => x.Sharpest));
    Assert.Equal(new[] { false, true, false }, r.Select(x => x.Soft));
}
[Fact] public void Rank_AllClose_NoMarks()   => Assert.All(RankBurst(new QualityScore?[] { S(100), S(TieShare * 100 + 1) }), x => Assert.False(x.Sharpest || x.Soft));
[Fact] public void Rank_Unmeasured_IgnoredAndUnmarked()
{
    var r = RankBurst(new QualityScore?[] { null, S(100), S(30) });
    Assert.Equal(new BurstRank(null, false, false), r[0]);
    Assert.True(r[1].Sharpest); Assert.True(r[2].Soft);
}
[Fact] public void Rank_SingleMeasured_NoMarks() => Assert.All(RankBurst(new QualityScore?[] { null, S(100) }), x => Assert.False(x.Sharpest || x.Soft));
```

- [ ] **Step 2: Çalıştır, başarısız** — `--filter QualityRulesTests` → FAIL
- [ ] **Step 3: `QualityRules` yaz** — `Percent = (int)Math.Round(100 * s / max)`; düz kareler sıralamaya normal katılır.
- [ ] **Step 4: Çalıştır** — PASS (8 test)
- [ ] **Step 5: Commit** — `Kalite: eşikler ve seri çekim sıralaması`

---

### Task 5: Satır modeli — netlik alanları, Kusurlu gruplar, "Bulanıkları ayıkla"

**Files:**
- Modify: `FotoDuzenleyici.Core/SimilarFinder.cs` (`SimilarKind.Flawed` eklenir)
- Modify: `FotoDuzenleyici.Core/SimilarGroupState.cs`
- Create: `FotoDuzenleyici.Core/FlawedList.cs`
- Modify: `FotoDuzenleyici.Cli/Program.cs` (`Name(SimilarKind)` switch'ine `Flawed` → "Kusurlu")
- Test: `FotoDuzenleyici.Tests/FlawedListTests.cs`, `FotoDuzenleyici.Tests/SimilarGroupStateTests.cs`

**Interfaces:**
- Consumes: `QualityRules`, `QualityScore`, `BurstRank`
- Produces (`SimilarRow`): `int? SharpPercent { get; set; }`, `bool Sharpest { get; set; }`, `string? Flaw { get; set; }` (kart rozeti), `Star => Suggested ? "★" : Sharpest ? "◎" : ""`.
- Produces (`SimilarGroupState`): kurucuya `bool allowEmpty = false` (true ise `CanRelease` her zaman true ve `Restore` boş grubu doldurmaz); `KindName(SimilarKind.Flawed) == "Kusurlu"`; `public int ReleaseSoft()` — `Kind == Burst` iken `Flaw == "BULANIK" && !Sharpest && Keep` olan satırları `Keep = false` yapar (Changed tetiklenir), son-kare kuralına uyar, ayıklanan sayıyı döner; `public void ApplyBurstRanks(IReadOnlyDictionary<MediaFile, QualityScore> scores)` — yalnız Burst gruplarında `RankBurst` sonucunu `SharpPercent/Sharpest/Flaw("BULANIK" if Soft)` alanlarına yazar.
- Produces: `public static class FlawedList { public static List<SimilarGroupState> Build(IReadOnlyDictionary<MediaFile, QualityScore> scores, IEnumerable<SimilarGroupState> similarGroups, int firstNumber); }` — her kusurlu kare tek satırlı, `allowEmpty: true`, `Kind = Flawed`, `Flaw = FlawText`, çekim tarihine göre sıralı; `similarGroups` içindeki dosyalar hariç.

- [ ] **Step 1: Başarısız testleri yaz**

```csharp
// FlawedListTests
[Fact] public void Build_ListsBlurryAndDark_SortedByDate_ExcludingSimilarMembers()
[Fact] public void Build_GroupsAllowRelease_AndStayReleasedAfterRestore()
{
    // tek kare: CanRelease true; Keep=false → store.SetKeep; yeni Build + Restore(store) → Keep false kalır
}
[Fact] public void Build_FlawText_OnRow() // "BULANIK", "KARANLIK"
// SimilarGroupStateTests
[Fact] public void ApplyBurstRanks_SetsPercentStarAndSoft_OnlyForBurst()   // SameFrame grubunda alanlar boş kalır
[Fact] public void ReleaseSoft_ReleasesOnlySoftFrames_NeverSharpest()      // 3 kare: 100/40/90 → yalnız 40 ayıklanır, dönüş 1
[Fact] public void Star_ShowsSharpestMark()                                 // Sharpest=true, Suggested=false → "◎"
```

- [ ] **Step 2: Çalıştır, başarısız** — `--filter "FlawedListTests|SimilarGroupStateTests"` → FAIL
- [ ] **Step 3: Alanları, `allowEmpty`, `ApplyBurstRanks`, `ReleaseSoft`, `FlawedList.Build` yaz.** `Star`, `SharpPercent`, `Flaw` değişince `PropertyChanged` tetiklenir.
- [ ] **Step 4: Tüm testleri çalıştır** — `dotnet test FotoDuzenleyici.Tests` → PASS
- [ ] **Step 5: Commit** — `Kalite: satır modeli, Kusurlu liste, bulanıkları ayıkla`

---

### Task 6: Planner — `_Kusurlu` ve RAW eşleri

**Files:**
- Modify: `FotoDuzenleyici.Core/Models.cs` (`PlanAction.Flawed`, `PlanOptions.FlawedFolderName = "_Kusurlu"`)
- Modify: `FotoDuzenleyici.Core/Planner.cs`
- Modify: `FotoDuzenleyici.Core/RawJpgPairs.cs`
- Modify: `FotoDuzenleyici.Tests/RawJpgPairTests.cs`
- Test: `FotoDuzenleyici.Tests/RawJpgPairTests.cs`, `FotoDuzenleyici.Tests/EngineTests.cs`

**Interfaces:**
- Produces: `Planner.Build(files, dupGroups, opt, similarOf = null, IReadOnlySet<MediaFile>? flawed = null)`. `Route`: Duplicate > Similar > Flawed > `_Benzerler`/`_Kusurlu` altında ise AlreadyInPlace > Move. `Flawed` hedefi `dest\_Kusurlu\yyyy-MM`, not "Kusurlu kare". Eş adlandırması `Flawed` için de geçerli (aynı rota).
- Produces: `RawJpgPairs.UnreleasedRaws(IEnumerable<MediaFile> files, IReadOnlySet<MediaFile> released)` → `List<(MediaFile Raw, MediaFile Photo)>` (eski `similarOf` imzası kaldırılır; çağıran RAW'ı fotoğrafının gittiği yere ekler).

- [ ] **Step 1: Başarısız testleri yaz / güncelle**

```csharp
[Fact] public void Flawed_GoesToKusurluMonthFolder()           // Action Flawed, TargetDir Z:\out\_Kusurlu\2026-01
[Fact] public void FileAlreadyInKusurlu_StaysInPlace()
[Fact] public void Priority_DuplicateBeatsSimilarBeatsFlawed()  // aynı dosya üç kümede → Duplicate; similar+flawed → Similar
[Fact] public void FlawedPair_RawAndJpgGoTogether_SameName()   // flawed = {jpg, raf} → ikisi _Kusurlu, aynı gövde
[Fact] public void UnreleasedRaws_ReturnsRawWithItsPhoto()     // mevcut testi yeni imzaya çevir: (raf, jpg)
```

- [ ] **Step 2: Çalıştır, başarısız** — `--filter RawJpgPairTests` → FAIL
- [ ] **Step 3: `Models`, `Planner`, `RawJpgPairs`'i değiştir.** Mevcut çağıranlar (App `Plan_Click`) derlenmeye devam etsin diye `UnreleasedRaws` çağrısını Task 8'e kadar `similarOf.Keys.ToHashSet()` ile uyarla; RAW'ın hedefi `similarOf[photo]`.
- [ ] **Step 4: Tüm testleri çalıştır** — PASS
- [ ] **Step 5: Commit** — `Plan: _Kusurlu klasörü ve RAW eşleri`

---

### Task 7: Arayüz — tarama adımı ve seri çekim işaretleri

**Files:**
- Modify: `FotoDuzenleyici.App/MainWindow.xaml`, `FotoDuzenleyici.App/MainWindow.xaml.cs`
- Modify: `FotoDuzenleyici.Core/CompareDecode.cs` (`CompareTile.Caption`)

**Interfaces:**
- Consumes: `QualityScan.Compute`, `QualityCache`, `SimilarGroupState.ApplyBurstRanks`, `ReleaseSoft`, `SimilarRow.Flaw/SharpPercent/Star`
- Produces: `MainWindow._scores` (`Dictionary<MediaFile, QualityScore>`), `_qualityDone` (bool: ölçüm tamamlandı mı).

- [ ] **Step 1: Seçenek ve tarama adımı** — `OptSimilar`'ın altına `<CheckBox x:Name="OptQuality" Content="Bulanık / karanlık kareleri işaretle" IsChecked="True"/>`. `Scan_Click`'te benzerlikten sonra: `QualityScan.Compute(_files, _dups, [dest\_Benzerler, dest\_Kusurlu], new QualityCache(QualityCache.DefaultPath), _simErrors, qprog, ct)`; durum metni `"Netlik ölçülüyor… d/t (c'i önbellekten)"`. İptalde `_scores = new()`, `_qualityDone = false`, not: `"Netlik ölçümü durduruldu; ölçülenler kaydedildi, bir sonraki taramada kaldığı yerden sürer."`. `ShowSimilar` içinde `Restore`'dan önce `g.ApplyBurstRanks(_scores)`.
- [ ] **Step 2: Kart rozetleri** — `RawBadge`'in yanına (sol alt, yatay `StackPanel`) `Flaw` rozeti: arka plan `#C62828`, beyaz, kalın, 10 pt; boşsa gizli. ◎ `Star` bağlamasıyla zaten görünür.
- [ ] **Step 3: "Bulanıkları ayıkla" düğmesi** — "Önerileri uygula"nın yanına; tıklayınca `_groups.Sum(g => g.ReleaseSoft())`; durum metni `"{n} bulanık kare ayıklandı."`; `SimilarChanged()`.
- [ ] **Step 4: Karşılaştırma başlığı** — `CompareTile.Caption` başına `Row.SharpPercent` varsa `"{Number} · Netlik %{p}"`, yoksa bugünkü gibi. `CompareDecodeTests`'e bir test: `SharpPercent = 42` → Caption `"3 · Netlik %42"` ile başlar.
- [ ] **Step 5: Derle, tüm testler** — `dotnet build FotoDuzenleyici.sln` 0 hata; `dotnet test FotoDuzenleyici.Tests` PASS
- [ ] **Step 6: Elle dene** — uygulamayı aç, arşivi tara (UIA `InvokePattern` ile Tara), Benzerler'de ◎/BULANIK rozetli bir seri grubunu ekran görüntüsüyle doğrula, çift tıkla → başlıkta netlik yüzdesi.
- [ ] **Step 7: Commit** — `Arayüz: netlik ölçümü ve seri çekim işaretleri`

---

### Task 8: Arayüz — Kusurlu sekmesi ve Plan'daki tek RAW sorusu

**Files:**
- Modify: `FotoDuzenleyici.App/MainWindow.xaml`, `FotoDuzenleyici.App/MainWindow.xaml.cs`
- Modify: `FotoDuzenleyici.App/CompareWindow.xaml.cs` (yalnız uyarı metni: tek kareli grupta "en az bir kare" uyarısı zaten `CanRelease` true olduğu için çıkmaz; Enter "incelendi + sonraki" aynen kalır)

**Interfaces:**
- Consumes: `FlawedList.Build`, `RawJpgPairs.UnreleasedRaws(files, released)`, `Planner.Build(..., flawed)`, `CompareWindow(groups, startIndex, store, onDecisionsChanged)`
- Produces: `MainWindow._flawed` (`List<SimilarGroupState>`)

- [ ] **Step 1: Sekme** — Benzerler'den sonra `<TabItem Header="Kusurlu">`: üstte `OnlyBlurry` ("Bulanıklar", işaretli), `OnlyDark` ("Karanlıklar", işaretli) onay kutuları ve `FlawedCounter` ("Ayıklanacak: n / toplam"); altta sanallaştırılmış `ListBox x:Name="FlawedList"` — her öğe bir satır, satır `ItemsControl` ile yatay en çok 6 kart (Benzerler kart şablonu `DataTemplate` kaynağına taşınıp iki yerde kullanılır). Kart olayları (`Card_Loaded`, `Card_MouseUp`, çift tık) aynı işleyicilerle; çift tık `OpenCompare` yerine süzülmüş `_flawed` listesiyle `new CompareWindow(visible, index, _reviews, ...)`.
- [ ] **Step 2: Doldurma** — tarama sonunda `_qualityDone` ise `_flawed = FlawedList.Build(_scores, _groups, _groups.Count + 1)`; satırlara `StoreKey` (Task.Run) + `Restore(_reviews)` + `Changed` (kararı yaz, `SimilarChanged()`), `ShowSimilar` ile aynı desen. Süzgeç değişince satırlar yeniden kurulur.
- [ ] **Step 3: Plan** — `Plan_Click`: `flawedSet = _flawed.SelectMany(g => g.Rows).Where(r => !r.Keep).Select(r => r.File).ToHashSet()`; `released = similarOf.Keys ∪ flawedSet`; `UnreleasedRaws(_files, released)` boş değilse tek soru (metin: `"Benzerler'de ve Kusurlu'da ayıkladığın {n} karenin RAW eşi var.\n\nEvet: RAW'lar da JPG'leriyle birlikte taşınsın (_Benzerler / _Kusurlu).\nHayır: RAW'lar normal klasörlerine gitsin.\nİptal: plan oluşturulmasın."`); Evet'te fotoğraf `similarOf`'taysa `similarOf[raw] = similarOf[photo]`, değilse `flawedSet.Add(raw)`. `Planner.Build(..., similarOf, flawedSet)`.
- [ ] **Step 4: Özet** — `BuildSummary`: `"Kusurlu (Benzerler dışında): Bulanık n, Karanlık n; _Kusurlu'ya ayrılacak: n"`, `"Seri çekimde en net önerisi: n grup"`, ölçülemeyenler hata listesinde zaten var.
- [ ] **Step 5: Derle, tüm testler** — 0 hata, PASS
- [ ] **Step 6: Elle dene** — Kusurlu sekmesi dolu; bir kartı ayıkla → sayaç artar; çift tık → tek kare tam ekran, yakınlaştırma çalışır, → sonraki; Plan → (RAW eşi varsa soru) → Plan tablosunda `Flawed` satırı `_Kusurlu\yyyy-MM`. **Uygula'ya basma.** Denemeden sonra deneme kararlarını kullanıcıya sorup karar dosyasından temizle.
- [ ] **Step 7: Commit** — `Arayüz: Kusurlu sekmesi ve ortak RAW sorusu`

---

### Task 9: Belgeler, CLI raporu ve son doğrulama

**Files:**
- Modify: `FotoDuzenleyici.Cli/Program.cs` (`--quality --csv` ile `-kusurlu.csv`: `Dosya;Neden;Netlik;Parlaklik;KaranlikOrani;Cekim`; sayılar: bulanık, karanlık, en net önerisi alan seri grup)
- Modify: `CHANGELOG.md` (`[Yayınlanmamış]` → Eklendi), `YOL_HARITASI.md` (Aşama 2 son madde ✅)

- [ ] **Step 1: CLI kusurlu raporu** — `QualityRules` ve `FlawedList` mantığıyla (Benzerler grupları `--similar` verildiyse hariç).
- [ ] **Step 2: Gerçek arşivde rapor** — `--similar --quality --csv` ile; sayılar kalibrasyondaki beklentiyle tutarlı mı kontrol et.
- [ ] **Step 3: CHANGELOG ve yol haritası** — kullanıcı diliyle: ne ölçülüyor, ◎/BULANIK/KARANLIK, Bulanıkları ayıkla, Kusurlu sekmesi, `_Kusurlu`, önbellek dosyası, `--quality`.
- [ ] **Step 4: Tüm testler + derleme** — PASS, 0 uyarı
- [ ] **Step 5: Commit** — `Belgeler: bulanık ve karanlık kareler`
- [ ] **Step 6: Bütün dal için tek, taze gözden geçirme** (en yetenekli model) — bulguları düzelt, testleri yeniden çalıştır.
