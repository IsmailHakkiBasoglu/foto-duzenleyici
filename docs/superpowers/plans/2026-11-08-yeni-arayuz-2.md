# Yeni arayüz 2. parça (inceleme ekranları) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Klasikteki Yinelenenler, Benzerler ve Kusurlu incelemelerini aynı davranışla yeni kabuğun "Tara ve incele" adımına taşımak.

**Architecture:** Benzer/kusurlu grup kurma mantığı klasik pencereden Core'a (`ReviewBuilder`) taşınır, iki pencere onu kullanır. Yeni pencerede tarama dört aşamaya çıkar; her inceleme bölümünün WPF'siz, test edilebilir bir ViewModel'i olur; `ReviewStepViewModel` bölümü, "plan eskidi" durumunu ve Onayla'ya geçerken planın yeniden kurulmasını yönetir. Görünüm (App) yalnız bağlama, küçük resim yükleme ve servis uygulamalarını yapar.

**Tech Stack:** .NET 8 (net8.0-windows), WPF + WPF-UI 4.2.1, CommunityToolkit.Mvvm 8.4.2, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-08-yeni-arayuz-2-design.md`

## Global Constraints

- Commit mesajlarına "Co-Authored-By: Claude" ya da "Generated with Claude Code" satırı EKLENMEZ.
- Dal: `yeni-arayuz-2`. main'e birleştirme ve push yalnız kullanıcı isteyince.
- Kullanıcının arşivine (`D:\1- HERKES\001 - İSMAİL HAKKI BAŞOĞLU`) yazılmaz; gerçek veride Uygula yapılmaz.
- ViewModels projesi WPF tipi kullanmaz (Core'daki `SimilarRow.Thumb` gibi WPF alanlarına dokunmaz); WPF işi App'te.
- Arayüz metinleri Türkçe ve spec'teki metinlerle birebir.
- Ayıklananlar silinmez: `_Benzerler`, `_Kusurlu`, `_Tekrarlar` klasörlerine ayrılır (Planner'ın mevcut davranışı).
- Kararlar klasikle ortak dosyada: `ReviewStore` (`benzer-kararlar.json`).
- Test komutu: `dotnet test` (kök dizinde). Her görev sonunda tüm testler yeşil.

## Review Focus

- Yeniden tarama: önceki taramanın grupları ve olay abonelikleri yeni incelemeyi etkilememeli (Task 7 testi `Rescan_OldGroupsDoNotTouchNewReview`).
- Karşılaştırma penceresinde verilen karar plana "eskidi" demeli (Task 5 testi `Compare_ChangedCallback_MarksStale`).
- Karar dosyası yazılamazsa inceleme sürmeli, uyarı hata şeridine gitmeli (Task 5 testi `Saver_SaveFails_WarnsAndContinues`).
- Benzerlik seçeneği kapatılıp yeniden taranınca Benzerler boş durumda, plan `Similar` içermemeli; kayıtlı kararlar silinmemeli (Task 3 testi `Scan_OptionsOff_SkipsPhases3And4`).
- Onayla → İncelemeye dön → değişiklik yok → Onayla: RAW sorusu tekrar sorulmamalı (Task 7 testi `Confirm_NotStale_DoesNotAskAgain`).

---

### Task 1: Core `ReviewBuilder` ve klasik pencerenin ona geçmesi

**Files:**
- Create: `FotoDuzenleyici.Core/ReviewBuilder.cs`
- Modify: `FotoDuzenleyici.App/MainWindow.xaml.cs` (`ShowSimilar`, `ShowFlawed`, `BuildSimilarMap`, `Plan_Click` içindeki RAW birleştirme ve `ignoreDirs`)
- Test: `FotoDuzenleyici.Tests/ReviewBuilderTests.cs`

**Interfaces:**
- Produces (hepsi `public static class ReviewBuilder`):
  - `IReadOnlyList<string> IgnoreDirs(PlanOptions opt)` → `[<arşiv>\_Benzerler, <arşiv>\_Kusurlu]` (`SimilarFolderName`, `FlawedFolderName`).
  - `List<SimilarGroupState> BuildSimilar(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dups, IReadOnlyDictionary<MediaFile, ImagePrint> prints, IReadOnlyList<string> ignoreDirs, IReadOnlyDictionary<MediaFile, QualityScore> scores, ReviewStore store)` — `SimilarFinder.Find(files, prints, SameFrameFinder.Find(files, dups, ignoreDirs))`, `FromGroups`, `HasRaw` (Photo + `RawJpgPairs.Index`), `StoreKey` (`HashCache.Key`, hata → null), `ApplyBurstRanks(scores)`, `Restore(store)`. Olay bağlamaz.
  - `List<SimilarGroupState> BuildFlawed(IReadOnlyList<MediaFile> files, IReadOnlyDictionary<MediaFile, QualityScore> scores, IReadOnlyList<SimilarGroupState> similar, ReviewStore store)` — `FlawedList.Build(scores, similar, similar.Count + 1)`, `HasRaw`, `StoreKey`, `Restore`.
  - `Dictionary<MediaFile, MediaFile> SimilarMap(IEnumerable<SimilarGroupState> similar)` — ayıklanan → grupta kalan (önerilen işaretliyse o, yoksa ilk kalan).
  - `HashSet<MediaFile> FlawedSet(IEnumerable<SimilarGroupState> flawed)` — `Keep == false` olanlar.
  - `List<(MediaFile Raw, MediaFile Photo)> PendingRaws(IReadOnlyList<MediaFile> files, IReadOnlyDictionary<MediaFile, MediaFile> similarOf, IReadOnlySet<MediaFile> flawed)` — `RawJpgPairs.UnreleasedRaws(files, similarOf.Keys ∪ flawed)`.
  - `void IncludeRaws(IEnumerable<(MediaFile Raw, MediaFile Photo)> raws, Dictionary<MediaFile, MediaFile> similarOf, HashSet<MediaFile> flawed)` — eşi benzerde ayıklandıysa `similarOf[raw] = similarOf[photo]`, değilse `flawed.Add(raw)`.

- [ ] **Step 1: Testleri yaz** (`TestImages.PatternJpeg` ile gerçek dosyalar, geçici `ReviewStore`):
  - `BuildSimilar_ResizedCopy_OneGroupWithKeys`: `a.jpg` = PatternJpeg(1200,800,seed 1), `b.jpg` = PatternJpeg(600,400,seed 1) → 1 grup, `Kind == ResizedCopy`, her satırda `StoreKey != null`.
  - `BuildSimilar_RestoresStoredKeep`: store'a `b`'nin anahtarıyla `SetKeep(k,false)` → grupta `b.Keep == false`.
  - `BuildSimilar_IgnoresParkedFolders`: `_Benzerler` altındaki kopya gruba girmez.
  - `BuildSimilar_HasRaw`: `a.jpg` yanında `a.CR3` (Raw) → `a` satırında `HasRaw`.
  - `BuildFlawed_ExcludesSimilarMembers`: benzer gruptaki bulanık kare Kusurlu'ya girmez; tek başına `BlurredPatternJpeg` girer, `Flaw` "BULANIK" içerir.
  - `SimilarMap_PrefersSuggestedKeeper`, `FlawedSet_OnlyReleased`.
  - `IncludeRaws_SimilarRawFollowsKeeper_FlawedRawToFlawed`.
- [ ] **Step 2:** `dotnet test --filter ReviewBuilderTests` → derleme hatası (ReviewBuilder yok).
- [ ] **Step 3:** `ReviewBuilder`'ı yaz; klasikteki gövdeleri buraya taşı.
- [ ] **Step 4:** `dotnet test --filter ReviewBuilderTests` → PASS.
- [ ] **Step 5:** `MainWindow`'u `ReviewBuilder`'a geçir: `ShowSimilar` → `await Task.Run(() => ReviewBuilder.BuildSimilar(...))` sonra olayları bağla (mevcut `CanRelease`/`Changed`/`PropertyChanged` bağlamaları aynen); `ShowFlawed` aynı; `BuildSimilarMap` → `ReviewBuilder.SimilarMap(_groups)`; `Plan_Click` RAW birleştirme → `PendingRaws` + `IncludeRaws`; `ignoreDirs` → `IgnoreDirs(BuildPlanOptions())`. Metinler ve MessageBox aynen.
- [ ] **Step 6:** `dotnet build` ve `dotnet test` → tümü yeşil.
- [ ] **Step 7:** Commit: `refactor: benzer/kusurlu grup kurma ReviewBuilder'a tasindi`

### Task 2: Ayarlar, özet ve plan süzgeci

**Files:**
- Modify: `FotoDuzenleyici.ViewModels/Settings.cs`, `PlanSummary.cs`, `ReviewStepViewModel.cs` (yalnız `PlanFilter` ve `Match`)
- Modify: `FotoDuzenleyici.App/Shell/OptionsDialog.xaml(.cs)`, `ConfirmPage.xaml`, `ReviewPage.xaml` (süzgeç düğmeleri)
- Test: `FotoDuzenleyici.Tests/PlanSummaryTests.cs`, `SettingsStoreTests.cs`, `ReviewFilterTests.cs` (yeni)

**Interfaces:**
- Produces: `AppSettings.FindSimilar = true`, `AppSettings.MarkFlawed = true`; `PlanSummary` sona eklenen `int SimilarCount, int FlawedCount` (Action `Similar` / `Flawed` sayısı); `PlanFilter { All, Move, Duplicate, InPlace, Renamed, Similar, Flawed }`.

- [ ] **Step 1: Testler:** `Settings_NewFlags_DefaultTrue_AndRoundTrip`; `Summary_CountsSimilarAndFlawed` (Planner'a `similarOf` ve `flawed` verilen planla); `Filter_SimilarAndFlawed`.
- [ ] **Step 2:** Çalıştır → FAIL (alanlar yok).
- [ ] **Step 3:** Uygula. `OptionsDialog`'a iki kutu: "Benzer fotoğrafları ara (küçültülmüş kopya, seri çekim)" ve "Bulanık / karanlık kareleri işaretle" (klasikteki metinler), `FindSimilar`/`MarkFlawed`'a yazılır. `ConfirmPage` özetine iki satır: "_Benzerler'e ayrılacak" (`SimilarCount`, → `_Benzerler`) ve "_Kusurlu'ya ayrılacak" (`FlawedCount`, → `_Kusurlu`), sayı 0 ise gizli. `ReviewPage` süzgecine "Benzer" ve "Kusurlu".
- [ ] **Step 4:** `dotnet test` → yeşil.
- [ ] **Step 5:** Commit: `feat(arayuz): benzer ve netlik secenekleri, ozet ve suzgec`

### Task 3: Dört aşamalı tarama ve `ReviewData`

**Files:**
- Modify: `FotoDuzenleyici.ViewModels/Services.cs`, `OrganizeFlowViewModel.cs`, `ReviewStepViewModel.cs` (yapıcı)
- Modify: `FotoDuzenleyici.App/Shell/WpfServices.cs`, `FotoDuzenleyici.Tests/UiFakes.cs`
- Test: `FotoDuzenleyici.Tests/OrganizeFlowReviewScanTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record CachePaths(string Reviews, string Hashes, string Quality) { public static CachePaths Default { get; } }` (Core `DefaultPath`'leri).
  - `Services`'e sona eklenen isteğe bağlı parametreler: `CachePaths? Caches = null` (null → `Default`), `Func<TimeSpan, Action, IDisposable>? Schedule = null` (null → hemen çalıştırır, `IDisposable` boş). `Fakes` geçici klasördeki `CachePaths` verir.
  - `public sealed record ReviewData(ReviewStore Store, List<SimilarGroupState> Similar, List<SimilarGroupState> Flawed, IReadOnlyDictionary<MediaFile, QualityScore> Scores, bool SimilarDone, bool QualityDone, IReadOnlyList<string> Errors)`.
  - `ReviewStepViewModel(OrganizeSession session, Plan plan, Services s, ReviewData data)`; `ReviewStepViewModel.Data`.
  - `OrganizeFlowViewModel.internal Action<string>? OnPhaseForTest` — her aşama başında `"metadata"`, `"similar"`, `"quality"` ile çağrılır (iş parçacığı fark etmez).
- Akış: `Scan` içinde `session.Scan` → `FindSimilar` ise `SimilarityScan.Compute(..., IgnoreDirs, new HashCache(Caches.Hashes), errors, prog, ct)` → `MarkFlawed` ve iptal yoksa `QualityScan.Compute(...)` → `ReviewBuilder.BuildSimilar/BuildFlawed` (store = `new ReviewStore(Caches.Reviews)`) → `session.BuildPlan()` → Review. Aşama 3/4 iptali `OperationCanceledException`'ı yakalar, ilgili sonuç boş, not `ErrorMessage`'a (spec'teki metinler). Aşama 3/4'te iptal dışı hata: sonuç boş, `ErrorMessage = "Benzerlik araması yapılamadı: " + ex.Message` (netlik: "Netlik ölçülemedi: "). Store `LoadFailed` → `ErrorMessage = "Önceki benzer kararları okunamadı; boş başlandı."`. `Errors` = tarama + benzerlik + netlik hataları.
- İlerleme metinleri: "Metadata okunuyor d/t", "Hash hesaplanıyor d/t", "Benzerlik izi… d/t (c'i önbellekten)", "Netlik ölçülüyor… d/t (c'i önbellekten)" (sayılar `N0`).

- [ ] **Step 1: Testler** (gerçek JPG'ler: bir küçültülmüş kopya çifti + bir bulanık kare):
  - `Scan_FindsSimilarAndFlawed`: Review açık, `Data.Similar.Count == 1`, `Data.Flawed.Count == 1`, `SimilarDone && QualityDone`.
  - `Scan_OptionsOff_SkipsPhases3And4`: iki bayrak false → listeler boş, `SimilarDone == false`, `OnPhaseForTest` "similar"/"quality" çağrılmaz, planda `Similar` yok; önceden store'a yazılmış karar dosyada durur.
  - `Scan_CancelInSimilar_OpensReviewWithNote`: `OnPhaseForTest = p => { if (p == "similar") vm.CancelCommand.Execute(null); }` → `Step == Review`, `Data.Similar` boş, `QualityDone == false`, `ErrorMessage` "Benzerlik araması durduruldu" ile başlar.
  - `Scan_CancelInQuality_KeepsSimilar`: "quality"'de iptal → `Similar.Count == 1`, `Flawed` boş, not "Netlik ölçümü durduruldu" ile başlar.
  - `Scan_CorruptReviewFile_WarnsAndContinues`.
- [ ] **Step 2:** Çalıştır → FAIL.
- [ ] **Step 3:** Uygula (mevcut iptal davranışı aşama 1–2 için aynen).
- [ ] **Step 4:** `dotnet test` → yeşil (mevcut akış testleri yeni yapıcıyla güncellenir).
- [ ] **Step 5:** Commit: `feat(arayuz): taramaya benzerlik ve netlik asamalari`

### Task 4: `ReviewSaver` ve Yinelenenler bölümü

**Files:**
- Create: `FotoDuzenleyici.ViewModels/ReviewSaver.cs`, `DuplicatesReviewViewModel.cs`
- Modify: `Services.cs` (`IShell.OpenFile(string path)`), `WpfServices.cs`, `UiFakes.cs`
- Test: `FotoDuzenleyici.Tests/DuplicatesReviewTests.cs`, `ReviewSaverTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class ReviewSaver(ReviewStore store, Func<TimeSpan, Action, IDisposable> schedule, Action<string> warn)`: `void Changed()` (1 sn sonra tek `Save`; yeni çağrı öncekini iptal eder), `void Flush()` (bekleyen varsa hemen kaydet), `ReviewStore Store`. Kayıt hatası → `warn("Benzer kararları kaydedilemedi; işaretler bu oturumda geçerli.")`, fırlatmaz.
  - `public sealed partial class DuplicatesReviewViewModel(IReadOnlyList<DuplicateGroupState> groups, PlanOptions opt, Services s, Action changed)`: `Groups`, `string CounterText`, `ResetCommand` ("Öneriye dön": her grupta `Reset(opt)`), `OpenFileCommand(DuplicateRow)`. Bir satır seçilince (`DuplicateGroupState.PropertyChanged`) `CounterText` güncellenir ve `changed()` çağrılır.
  - `CounterText`: grup yoksa "Birebir kopya yok."; yoksa `"{n} grup · {kopya} kopya · {GB:N2} GB kazanılacak"` + elle seçim varsa `" · {k} grupta elle seçim"` (klasikteki metin).

- [ ] **Step 1: Testler:** `Saver_Debounces_OneSave`, `Saver_Flush_SavesPending`, `Saver_SaveFails_WarnsAndContinues` (store yolu bir klasör → yazma hatası); `Duplicates_ChooseRow_CounterAndChanged`, `Duplicates_Reset_ClearsManual`, `Duplicates_OpenFile_UsesShell`.
- [ ] **Step 2:** Çalıştır → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** `dotnet test` → yeşil.
- [ ] **Step 5:** Commit: `feat(arayuz): yinelenenler bolumu ve karar kaydi`

### Task 5: Benzerler bölümü ve karşılaştırma servisi

**Files:**
- Create: `FotoDuzenleyici.ViewModels/SimilarReviewViewModel.cs`
- Modify: `Services.cs` (`ICompare`), `WpfServices.cs` (`WpfCompare`), `UiFakes.cs` (`FakeCompare`)
- Test: `FotoDuzenleyici.Tests/SimilarReviewTests.cs`

**Interfaces:**
- Produces:
  - `public interface ICompare { (SimilarGroupState Current, bool FinishedAll) Open(IReadOnlyList<SimilarGroupState> groups, int start, ReviewStore store, Action changed); }`; `Services`'e sona `ICompare? Compare = null`. `WpfCompare`: `new CompareWindow(groups, start, store, changed) { Owner = Application.Current.MainWindow }.ShowDialog()`.
  - `public sealed partial class SimilarReviewViewModel(List<SimilarGroupState> groups, bool done, ReviewSaver saver, Services s, Action changed, Action<string> warn)`:
    - `bool Done` (false → boş durum "Benzerlik araması yapılmadı.").
    - `[ObservableProperty] bool onlyUnreviewed`; `IReadOnlyList<SimilarGroupState> Visible`; `int ReviewedCount`, `int Total`, `string CounterText` = `"İncelenen: {r} / {t}"`; `int ReleasedCount` (ayıklanacak kare).
    - `[ObservableProperty] SimilarGroupState? selected`.
    - `ToggleCommand(SimilarRow row)`: `row.Keep = !row.Keep`.
    - `OpenCompareCommand(SimilarGroupState g)`: görünen liste ve dizinle `ICompare.Open`; dönüşte `Selected = Current`, `Visible` yenilenir, `FinishedAll` → `warn("Bütün gruplar gözden geçirildi.")`.
  - Yapıcı her satıra bağlar: `CanRelease` → reddedilirse `warn("Her grupta en az bir dosya kalmalı.")`; `Changed` → `store.SetKeep(StoreKey)`, `saver.Changed()`, sayaçlar, `changed()`. Grup `Reviewed` değişince `saver.Changed()` ve sayaç. Karşılaştırmaya verilen `changed` geri çağrısı da `saver.Changed()` + `changed()` yapar.
  - `void Detach()`: bütün satır/grup olaylarını bırakır (yeniden taramada çağrılır).

- [ ] **Step 1: Testler** (ReviewBuilder ile kurulan gerçek gruplar ya da elle kurulan `SimilarGroupState`):
  - `Toggle_ReleasesAndStoresKeep`: satır ayıklanır, store'da `GetKeep == false`, `changed` çağrıldı, `ReleasedCount == 1`.
  - `Toggle_LastKept_BlockedWithWarning`.
  - `OnlyUnreviewed_FiltersReviewed`.
  - `Compare_ChangedCallback_MarksStale`: `FakeCompare` geri çağrıyı çağırır → `changed` çağrıldı, `saver` kayıt planladı.
  - `Compare_SetsSelectedToCurrent`.
  - `Detach_StopsCallbacks`.
- [ ] **Step 2:** Çalıştır → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** `dotnet test` → yeşil.
- [ ] **Step 5:** Commit: `feat(arayuz): benzerler bolumu`

### Task 6: Kusurlu bölümü

**Files:**
- Create: `FotoDuzenleyici.ViewModels/FlawedReviewViewModel.cs`
- Test: `FotoDuzenleyici.Tests/FlawedReviewTests.cs`

**Interfaces:**
- Produces: `public sealed partial class FlawedReviewViewModel(List<SimilarGroupState> flawed, bool done, ReviewSaver saver, Services s, Action changed, Action<string> warn)`: `Done` (false → "Netlik ölçülmedi."), `[ObservableProperty] bool showBlurry = true, showDark = true`, `Visible` (Flaw'ı "BULANIK" içeren ve showBlurry ya da "KARANLIK" içeren ve showDark), `CounterText` = klasikteki metin (`"Kusurlu kare yok."` ya da `"Bulanık: a  ·  Karanlık: b  ·  Ayıklanacak: c / n"`), `int ReleasedCount`, `ToggleCommand(SimilarRow)`, `OpenCompareCommand(SimilarGroupState)` (bitince `warn("Bütün kusurlu kareler gözden geçirildi.")`), `Detach()`. Satır değişince Task 5'teki gibi store + saver + `changed()`.

- [ ] **Step 1: Testler:** `Toggle_ReleasesSingleFrame` (son kare kuralı yok), `Filters_BlurryDark`, `Counter_Text`, `Compare_ChangedCallback_MarksStale`.
- [ ] **Step 2:** Çalıştır → FAIL.
- [ ] **Step 3:** Uygula (ortak satır bağlama kodu Task 5 ile paylaşılır: `internal static class ReviewRows { Attach(...); Detach(...) }`).
- [ ] **Step 4:** `dotnet test` → yeşil.
- [ ] **Step 5:** Commit: `feat(arayuz): kusurlu bolumu`

### Task 7: İnceleme adımı: bölümler, eskiyen plan, RAW sorusu

**Files:**
- Modify: `FotoDuzenleyici.ViewModels/ReviewStepViewModel.cs`, `OrganizeFlowViewModel.cs`, `ShellViewModel.cs`, `Services.cs` (`IDialogs.Ask`, `Answer`), `WpfServices.cs`, `UiFakes.cs`
- Test: `FotoDuzenleyici.Tests/ReviewStepPlanTests.cs`

**Interfaces:**
- Consumes: Task 3 `ReviewData`, Task 4 `ReviewSaver`/`DuplicatesReviewViewModel`, Task 5–6 bölüm VM'leri, Task 1 `ReviewBuilder.SimilarMap/FlawedSet/PendingRaws/IncludeRaws`.
- Produces:
  - `public enum Answer { Yes, No, Cancel }`; `IDialogs.Answer Ask(string title, string message)`. `WpfDialogs`: `MessageBoxButton.YesNoCancel`. `FakeDialogs.NextAsk` (öntanımlı `Yes`) ve `AskedCount`.
  - `public enum ReviewSection { Summary, Duplicates, Similar, Flawed, Errors }`.
  - `ReviewStepViewModel`: `[ObservableProperty] ReviewSection section`; `Duplicates`, `Similar`, `Flawed` (bölüm VM'leri); `IReadOnlyList<string> Errors` (= `Data.Errors`); `[ObservableProperty] bool planStale`; `Plan`, `Summary`, `Rows` artık değişebilir (değişince bildirim); `string BarTitle` (eski değilse 1. parçadaki metin; eskiyse "Plan güncellenecek: onay ekranına geçerken seçimlerinle yeniden kurulur.").
  - `bool PrepareForConfirm()`: `PlanStale` değilse `true`. Eskiyse: `saver.Flush()`; yinelenen durumlar `Refresh(opt)`; `similarOf = SimilarMap`, `flawed = FlawedSet`; `raws = PendingRaws(...)`; varsa `Ask("RAW eşleri", <klasikteki metin, {raws.Count} ile>)`: Cancel → `false`, Yes → `IncludeRaws`; `Plan = Session.BuildPlan(similarOf, flawed, DuplicateGroupState.UserKeepers(...))`; `Summary`, `Rows` yenilenir; `PlanStale = false`; `true`.
  - `void Detach()` (bölümleri bırakır), `void FlushReviews()`.
  - `OrganizeFlowViewModel.GoToConfirm`: `Review.PrepareForConfirm()` false ise İnceleme'de kalır. `Rescan` ve yeni tarama başında eski `Review?.Detach()` + `FlushReviews()`. `public void FlushReviews()`.
  - `ShellViewModel.RequestClose` `Close` döndürmeden önce `Flow?.FlushReviews()`.
  - Özet'teki dikkat listesi "İncele" düğmeleri `ShowSectionCommand(ReviewSection)`.

- [ ] **Step 1: Testler** (gerçek dosyalar; küçültülmüş kopya + RAW eşli bulanık kare + iki konumlu birebir kopya):
  - `ReleaseSimilar_MarksStale_AndPlanHasSimilar`: bir kare ayıklanır → `PlanStale`; `PrepareForConfirm()` true; o dosyanın plan öğesi `PlanAction.Similar`; `Summary.SimilarCount == 1`; `PlanStale == false`.
  - `ReleaseFlawed_PlanHasFlawed`.
  - `RawAnswer_Yes_RawFollows` / `RawAnswer_No_RawMovesNormally` / `RawAnswer_Cancel_StaysInReview` (flow üzerinden: `Step == Review`, Confirm null).
  - `Confirm_NotStale_DoesNotAskAgain`: Onayla → Back → Onayla; `AskedCount` 1.
  - `DuplicateManualChoice_GoesToPlan`: elle seçilen konum planda `Duplicate` değil, öteki `Duplicate`.
  - `Rescan_OldGroupsDoNotTouchNewReview`: eski Review'ın satırı değiştirilir → yeni Review `PlanStale == false`.
  - `Close_FlushesPendingDecisions`: `Schedule` hiç çalıştırmayan sahte ile karar → `RequestClose()` → store dosyası kararı içerir.
- [ ] **Step 2:** Çalıştır → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** `dotnet test` → yeşil.
- [ ] **Step 5:** Commit: `feat(arayuz): inceleme bolumleri plana yansir, RAW sorusu`

### Task 8: Görünüm: menü, Yinelenenler, Okunamayanlar, servisler

**Files:**
- Modify: `FotoDuzenleyici.App/Shell/ReviewPage.xaml(.cs)` (sol menü `Section`'a bağlı; sağda `Section`'a göre içerik; alt çubuk `BarTitle`; "klasikte" yazıları kalkar)
- Create: `FotoDuzenleyici.App/Shell/DuplicatesSection.xaml(.cs)`, `ErrorsSection.xaml(.cs)`
- Modify: `WpfServices.cs` (`Schedule` = DispatcherTimer tek atış, `OpenFile` = `Process.Start(UseShellExecute)` + hata `Dialogs.Info`), `Converters.cs` (gerekirse)

- [ ] **Step 1:** Menü: Özet ve Plan / KARAR BEKLEYENLER: Yinelenenler (grup sayısı), Benzerler (`ReviewedCount / Total`), Kusurlu (`ReleasedCount / Total`) / DİĞER: Okunamayanlar (sayı). Seçili olan vurgulu. Özet'teki dikkat listesine Benzerler (incelenmemiş grup) ve Kusurlu (kare sayısı) satırları eklenir; her satırın "İncele" düğmesi `ShowSectionCommand` ile ilgili bölümü açar.
- [ ] **Step 2:** `DuplicatesSection`: başlık + açıklama (taslak 2b metinleri), `CounterText`, "Öneriye dön"; sanallaştırılmış liste; kartta ad, `SizeText`, `CountText`, `WastedText`, "elle seçildi" (`UserChosen`), her `DuplicateRow` için `RadioButton` (`IsKeeper` TwoWay, GroupName = grup numarası) + yol + "düzenli" (`Organized`); çift tık `OpenFileCommand`. Küçük resim `Preview` satırı için `ThumbnailLoader` (Task 9'daki ortak yardımcı; bu görevde yer tutucu kutu yeterli).
- [ ] **Step 3:** `ErrorsSection`: `Errors` listesi, Mono yazı.
- [ ] **Step 4:** `dotnet build` hatasız; scratch harness'ı (`scratchpad\render`) yeni yapıya göre güncelle, açık ve koyu temada Yinelenenler ve Okunamayanlar metinlerini dök, bağlama hatası 0.
- [ ] **Step 5:** `dotnet test` yeşil. Commit: `feat(arayuz): inceleme menusu, yinelenenler ve okunamayanlar ekranlari`

### Task 9: Görünüm: Benzerler ve Kusurlu, küçük resimler

**Files:**
- Create: `FotoDuzenleyici.App/Shell/SimilarSection.xaml(.cs)`, `FlawedSection.xaml(.cs)`, `ThumbTracker.cs`
- Modify: `DuplicatesSection.xaml(.cs)` (küçük resim)

**Interfaces:**
- Produces: `sealed class ThumbTracker(ThumbnailLoader loader)` — klasikteki `Card_Loaded/Unloaded/DataContextChanged/Track` mantığı: `void Attach(FrameworkElement card)`; kart görünür olunca `Request(row)`, kaldırılınca `Cancel(row)`. Bölümler tek bir `ThumbnailLoader(ThumbnailLoader.DefaultDecode)` paylaşır (ReviewPage'de); yeniden taramada `Clear()`.

- [ ] **Step 1:** `SimilarSection`: başlık + açıklama (taslak 2c), "Yalnız incelenmemişler", `CounterText`; boş durum ("Benzerlik araması yapılmadı." + Düzen seçeneklerine giden bağlantı: `Folders` adımına dön ve seçenekleri aç); sanallaştırılmış grup listesi; satırda tür rozeti, kare sayısı, tarih; yatay kart şeridi: küçük resim, `Star`, `RawBadge`, `Flaw`, `SharpPercent`, ad, çözünürlük; kalsın/ayıkla görünümü (yeşil / kırmızı çerçeve + "Ayıkla"). Tek tık `ToggleCommand`; çift tık ve Enter `OpenCompareCommand` (çift tıkta ilk tıklamanın değiştirdiği işaret geri alınır, klasikteki `_lastToggle` mantığı). `Selected` değişince listede `ScrollIntoView`.
- [ ] **Step 2:** `FlawedSection`: başlık + açıklama (taslak 2d), iki süzgeç, `CounterText`, boş durum ("Netlik ölçülmedi."), sanallaştırılmış ızgara (`VirtualizingWrapPanel` yoksa satır başına sabit sayıda kart), aynı tıklama kuralları.
- [ ] **Step 3:** `dotnet build`; harness'ta örnek JPG'lerle (küçültülmüş kopya + bulanık + karanlık) Benzerler ve Kusurlu metinlerini aç/koyu temada dök, bağlama hatası 0; bir karede tıklama komutunu çalıştırıp `PlanStale` olduğunu ve alt çubuk metnini doğrula.
- [ ] **Step 4:** `dotnet test` yeşil. Commit: `feat(arayuz): benzerler ve kusurlu ekranlari`

### Task 10: Belgeler ve yayın paketi

**Files:**
- Modify: `CHANGELOG.md` ([Yayınlanmamış] altına), `README.md` (yeni arayüz kullanımı: incelemeler), `YOL_HARITASI.md` (Aşama 3 maddesi: inceleme ekranları ✅ 2. parça)

- [ ] **Step 1:** Belgeleri güncelle (sürüm numarası değişmez; yayın kullanıcı isteyince).
- [ ] **Step 2:** `./publish.ps1` (uygulama açıksa önce kullanıcıya sor) → `dist\FotoDuzenleyici-win-x64.zip`; exe normal ve `--klasik` açılıyor.
- [ ] **Step 3:** `dotnet test` yeşil. Commit: `docs: yeni arayuz 2. parca`
