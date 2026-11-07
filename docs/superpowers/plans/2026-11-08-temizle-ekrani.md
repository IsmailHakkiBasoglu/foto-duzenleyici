# Temizle ekranı Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ana ekrandaki Temizle kartını, Düzenle sihirbazının ortaklaştırılmış adımlarıyla çalışır yapmak.

**Architecture:** İki işin farkları `IScanJob` arayüzünde toplanır (`OrganizeJob` bugünkü davranış, `CleanJob` CleanSession). `OrganizeFlowViewModel` bir `FlowKind` alır; Klasörler, İnceleme, Onayla, Uygula adımları `IScanJob` üzerinden çalışır. Önce Düzenle saf yeniden düzenlemeyle `IScanJob`'a geçirilir (davranış ve testler aynı), sonra Temizle eklenir.

**Tech Stack:** .NET 8 (net8.0-windows), WPF + WPF-UI 4.2.1, CommunityToolkit.Mvvm 8.4.2, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-08-temizle-ekrani-design.md`

## Global Constraints

- Commit mesajlarına "Co-Authored-By: Claude" ya da "Generated with Claude Code" satırı EKLENMEZ.
- Dal: `temizle-ekrani`. Aşama bitince main'e birleştirilir ve GitHub'a gönderilir (kullanıcı tercihi); sürüm yayını sorulur.
- Kullanıcının arşivine (`D:\1- HERKES\001 - İSMAİL HAKKI BAŞOĞLU`) yazılmaz; gerçek veride Uygula yapılmaz.
- ViewModels projesi WPF tipi kullanmaz.
- Temizle'de hiçbir dosya silinmez ve kopyalanmaz: `Executor.Run(..., recycleSource: false, ..., move: true)`.
- Arayüz metinleri Türkçe ve spec'teki metinlerle birebir.
- Test komutu: `dotnet test` (kök). Her görev sonunda tüm testler yeşil; Düzenle'nin mevcut testleri değiştirilmeden geçer (yalnız yapıcı imzası değişen yerler uyarlanır).

## Review Focus

- İç içe klasörler (`D:\A` ve `D:\A\B` birlikte seçili): tek kök, ayıklanan `D:\A\_Ayiklananlar` altına gider (Task 4 testi `Clean_NestedFolders_SingleRoot`).
- Temizle'den sonra Düzenle taraması `_Ayiklananlar`'ı görmez (Task 4 testi `Organize_AfterClean_IgnoresRejects`).
- Temizlik günlüğü sağ üstteki Geri Al ile seçilince her şey yerine döner ve "aç" `_Ayiklananlar`'ı açar (Task 5 testi `Shell_Undo_CleanJournal`).
- Taşıma sırasında Durdur → Kaldığı yerden devam et: kalanlar taşınır, yarım dosya yok (Task 4 testi `Clean_StopThenResume`).
- Özet sayıları ve kazanılacak alan planla tutarlı (Task 4 testi `Clean_SummaryMatchesPlan`).

---

### Task 1: Core — öncelik fonksiyonlu yinelenen durumları

**Files:**
- Modify: `FotoDuzenleyici.Core/DuplicateGroupState.cs`
- Test: `FotoDuzenleyici.Tests/DuplicateGroupStateTests.cs` (varsa ekle, yoksa yeni)

**Interfaces:**
- Produces:
  - `static List<DuplicateGroupState> Build(IEnumerable<DuplicateGroup> groups, Func<MediaFile, bool> preferred)`; mevcut `Build(groups, PlanOptions opt)` bunu `m => DuplicateKeeper.IsOrganized(m, opt)` ile çağırır.
  - Durum, kurulduğu öncelik fonksiyonunu saklar: `void Refresh()` ve `void Reset()` (parametresiz) onu kullanır. Mevcut `Refresh(PlanOptions)` / `Reset(PlanOptions)` aynen kalır (klasik pencere).
  - `DuplicateRow.Organized` adı korunur; anlamı "öncelikli konumda" (yorum güncellenir).

- [ ] **Step 1: Testler:** `Build_WithPreferred_KeepsPreferred` (iki konumdan öncelikli olan `IsKeeper`, `Organized` true), `Reset_Parameterless_UsesPreferred` (elle seçim → Reset → öncelikliye döner), `Build_PlanOptions_Unchanged` (düzenli arşivdeki seçilir).
- [ ] **Step 2:** `dotnet test --filter DuplicateGroupStateTests` → FAIL (aşırı yükleme yok).
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** `dotnet test` → yeşil.
- [ ] **Step 5:** Commit: `feat(core): yinelenen durumlarina oncelik fonksiyonu`

### Task 2: `IScanJob`, `OrganizeJob`, `CleanJob`

**Files:**
- Create: `FotoDuzenleyici.ViewModels/ScanJobs.cs`
- Test: `FotoDuzenleyici.Tests/ScanJobTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum FlowKind { Organize, Clean }
public interface IScanJob
{
    FlowKind Kind { get; }
    IReadOnlyList<string> Sources { get; }
    List<MediaFile> Files { get; }
    List<DuplicateGroup> Duplicates { get; }
    IReadOnlyList<string> ScanErrors { get; }
    /// <summary>Düzenle: hedef ayarları; Temizle: null (Tarihsiz sayımı yok).</summary>
    PlanOptions? PlanOptions { get; }
    IReadOnlyList<string> IgnoreDirs { get; }
    bool MoveOnly { get; }
    /// <summary>Düzenle: arşiv; Temizle: ilk kökün _Ayiklananlar klasörü.</summary>
    string RejectsFolder { get; }
    void Scan(IProgress<(int, int)>? scan, IProgress<(int, int)>? hash, CancellationToken ct);
    bool IsPreferred(MediaFile f);
    Plan BuildPlan(IReadOnlyDictionary<MediaFile, MediaFile>? similarOf, IReadOnlySet<MediaFile>? flawed, IReadOnlySet<MediaFile>? keepDuplicates);
    string NewJournalPath(DateTime now);
    List<(string Journal, int Remaining)> FindUnfinished();
}
public sealed class OrganizeJob(OrganizeSession session) : IScanJob   // Session özelliği de açık
public sealed class CleanJob(CleanSession session) : IScanJob
```
- `OrganizeJob`: `IgnoreDirs = ReviewBuilder.IgnoreDirs(opt)`, `MoveOnly = false`, `RejectsFolder = opt.DestinationRoot`, `IsPreferred = DuplicateKeeper.IsOrganized(f, opt)`, `NewJournalPath = <arşiv>\_Gunluk\islem-yyyyMMdd-HHmmss.jsonl`, `FindUnfinished = Executor.FindUnfinishedIn(<arşiv>\_Gunluk)`, `BuildPlan` → `session.BuildPlan(similarOf, flawed, keep)`.
- `CleanJob`: `PlanOptions = null`, `IgnoreDirs = []`, `MoveOnly = true`, `RejectsFolder = Path.Combine(Roots[0], "_Ayiklananlar")`, `IsPreferred = DuplicateKeeper.InAny(f, PriorityFolders)`, `NewJournalPath = <Roots[0]>\_Ayiklananlar\_Gunluk\temizlik-yyyyMMdd-HHmmss.jsonl` (tarih parametreden), `FindUnfinished = session.FindUnfinished()`, `BuildPlan` → `session.BuildPlan(keep, similarOf, flawed)`.

- [ ] **Step 1: Testler** (gerçek dosyalar): `CleanJob_ScanAndPlan_OnlyRejects` (iki konumlu birebir kopya → plan tek öğe, `Duplicate`, hedef `<kök>\_Ayiklananlar\Tekrarlar\…`), `CleanJob_PreferredFolderKept`, `CleanJob_JournalAndRejectsFolder`, `OrganizeJob_MatchesSession` (aynı girdide `BuildPlan` öğeleri `session.BuildPlan` ile aynı; `NewJournalPath` 1. parçadaki biçim).
- [ ] **Step 2:** Çalıştır → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** `dotnet test` → yeşil.
- [ ] **Step 5:** Commit: `feat(arayuz): IScanJob, OrganizeJob, CleanJob`

### Task 3: Düzenle akışını `IScanJob`'a geçir (saf yeniden düzenleme)

**Files:**
- Modify: `OrganizeFlowViewModel.cs`, `ReviewStepViewModel.cs`, `DuplicatesReviewViewModel.cs`, `ConfirmStepViewModel.cs`, `ApplyStepViewModel.cs`, `PlanSummary.cs`
- Modify (yalnız imza uyarlaması): ilgili testler

**Interfaces:**
- Consumes: Task 1 `Build(groups, preferred)`, `Refresh()`, `Reset()`; Task 2 `IScanJob`, `OrganizeJob`.
- Produces:
  - `ReviewStepViewModel(IScanJob job, Plan plan, Services s, ReviewData data, Action<string>? warn = null)`; `Job` özelliği (`Session` kalkar).
  - `DuplicatesReviewViewModel(IReadOnlyList<DuplicateGroupState> groups, Services s, Action changed)` — `Reset`/`Refresh` parametresiz durum yöntemlerini kullanır.
  - `PlanSummary.From(Plan plan, PlanOptions? opt)` — opt null ise `Undated = 0`.
  - `ConfirmStepViewModel(Plan plan, IScanJob job, Services s, AppSettings settings, DateTime now)`; `Archive` = `job.RejectsFolder`; `JournalPath = job.NewJournalPath(now)`; yarım işlem `job.FindUnfinished()`; yeni `bool MoveOnly` ve `bool ShowSpace` (= !MoveOnly). `MoveOnly` iken `SpaceKnown = false` değil, `SpaceOk = true` (denetim yok), `OtherDriveSources` boş.
  - `ApplyStepViewModel.RunAsync(items, journal, recycle, bool move = false)` → `Executor.Run(..., move: move)`.
  - `OrganizeFlowViewModel`: tarama `IScanJob CreateJob()` ile (bu görevde yalnız `OrganizeJob`); `ScanReview(IScanJob job, …)`; `StartApply` `c.MoveOnly` ile `move` geçirir ve `recycle = !c.MoveOnly && c.RecycleSource`.

- [ ] **Step 1:** Uygula (yeni davranış yok; testler zaten kapsar).
- [ ] **Step 2:** `dotnet test` → bütün mevcut testler yeşil (yalnız `new ReviewStepViewModel(session, …)` ve `new ConfirmStepViewModel(plan, opt, sources, …)` çağrıları `new OrganizeJob(session)` ile uyarlanır).
- [ ] **Step 3:** Commit: `refactor(arayuz): Duzenle akisi IScanJob uzerinden`

### Task 4: Temizle akışı (ViewModels)

**Files:**
- Modify: `Settings.cs` (`RecentClean`), `FoldersStepViewModel.cs`, `OrganizeFlowViewModel.cs`, `ReviewStepViewModel.cs` (metinler)
- Test: `FotoDuzenleyici.Tests/CleanFlowTests.cs`

**Interfaces:**
- Consumes: Task 2 `CleanJob`, Task 3 akış.
- Produces:
  - `AppSettings.RecentClean: List<RecentPair>` (Archive alanı `""`), `AddRecentClean(IEnumerable<string> sources, IEnumerable<string> priority, DateTime now)` en çok 5, yeni başta, aynı küme tekilleştirilir. `RecentPair`'a `List<string>? Priority = null` eklenir (sona, isteğe bağlı).
  - `OrganizeFlowViewModel(Services s, AppSettings settings, FlowKind kind = FlowKind.Organize)`; `Kind`.
  - `FoldersStepViewModel(Services s, AppSettings settings, FlowKind kind = FlowKind.Organize)`: `Kind`; `ObservableCollection<SourceEntry> Priority`; `AddPriorityCommand`, `RemovePriorityCommand(SourceEntry)`; Clean'de `CanScan` = en az bir var olan kaynak; `RememberCurrent` Clean'de `AddRecentClean`; `Load(RecentPair)` Clean'de Priority'yi de doldurur; `Title` = "Fotoğraf arşivini düzenleyelim" / "Neyi temizleyelim?".
  - Akış Clean'de `new CleanJob(new CleanSession(Folders.ExistingSources, Folders.Priority…, scanOptions))`.
  - `ReviewStepViewModel.BarTitle` Clean'de: eski değilse `"Plan hazır: {n:N0} dosya _Ayiklananlar'a taşınacak ({bytes})."`; eskiyse 2. parçadaki metin. `Kind` özelliği; `RejectBytes` (plan öğelerinin toplam boyutu).

- [ ] **Step 1: Testler** (gerçek dosyalar, sahte servisler):
  - `Clean_ScanReviewApplyUndo`: kopya çifti + küçültülmüş kopya + karanlık kare; Benzerler'de küçük kare ve Kusurlu'da karanlık kare ayıklanır; Onayla (`MoveOnly`, `ShowSpace == false`); Uygula; kaynaklar yok, `_Ayiklananlar\Tekrarlar|Benzerler|Kusurlu\<göreli>` var, içerik aynı; Geri Al → hepsi yerinde, `_Ayiklananlar` altındaki dosyalar yok.
  - `Clean_PreferredFolderKeeper`: öncelikli klasördeki kopya kalır.
  - `Clean_NestedFolders_SingleRoot`.
  - `Clean_StopThenResume`: taşımada Geri Dönüşüm kancası çağrılmadığı için yeni test kancası `ApplyStepViewModel.internal Action<int>? OnProgressForTest` (her ilerleme bildiriminde, arka plan iş parçacığında, yapılan sayıyla çağrılır); ilk dosyada `CancelCommand` → `Stopped`, `PendingAfterStop > 0`; Kaldığı yerden devam et → hepsi taşınmış.
  - `Clean_SummaryMatchesPlan`: kutucuk sayıları ve `RejectBytes` plandakiyle aynı; `Summary.Undated == 0`.
  - `Organize_AfterClean_IgnoresRejects`: Temizle uygulandıktan sonra aynı klasör Düzenle ile taranınca `_Ayiklananlar` altındaki dosya planda yok.
  - `Clean_RecentRemembered_Separately`: tarama sonrası `RecentClean` 1 kayıt (öncelikli klasörle), `Recent` değişmez.
- [ ] **Step 2:** Çalıştır → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** `dotnet test` → yeşil.
- [ ] **Step 5:** Commit: `feat(arayuz): Temizle akisi`

### Task 5: Ana ekran ve kabuk

**Files:**
- Modify: `HomeViewModel.cs`, `ShellViewModel.cs`
- Test: `FotoDuzenleyici.Tests/CleanShellTests.cs`

**Interfaces:**
- Produces: `HomeViewModel.LastClean: RecentPair?`, `event Action<RecentPair?>? CleanRequested`, `StartCleanCommand(RecentPair?)`. `ShellViewModel` CleanRequested'de `new OrganizeFlowViewModel(_s, _settings, FlowKind.Clean)` açar (doluysa `Folders.Load`). `OpenClassic` Clean akışında `start = null`. Sağ üstteki Geri Al değişmez (günlüğün `_Gunluk` üstü = `_Ayiklananlar`).

- [ ] **Step 1: Testler:** `Home_StartClean_OpensCleanFlow`, `Home_LastClean_Prefills`, `OpenClassic_FromClean_Empty`, `Shell_Undo_CleanJournal` (Temizle uygulanır; kabuktan o günlükle Geri Al → dosyalar yerinde; `Apply.Archive` = `<kök>\_Ayiklananlar`).
- [ ] **Step 2:** Çalıştır → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** `dotnet test` → yeşil.
- [ ] **Step 5:** Commit: `feat(arayuz): ana ekrandan Temizle`

### Task 6: Görünüm

**Files:**
- Modify: `HomePage.xaml` (Temizle kartı: Opacity kalkar, "Başla" düğmesi `StartCleanCommand`, "Son: …" `LastClean.Sources[0]`; "Yakında" yazısı kalkar), `FoldersPage.xaml(.cs)` (Clean'de Nereye bölümü yerine "Kopyalarda öncelikli klasörler (isteğe bağlı)" listesi + "Klasör ekle"; başlık `Title`; Düzen satırı Clean'de yalnız tarama seçenekleri), `OptionsDialog.xaml(.cs)` (Clean'de tarih düzeni kutuları gizli; yapıcıya `FlowKind`), `ReviewPage.xaml` (Clean'de kutucuklar: Ayıklanacak kopya / Benzer / Kusurlu / Kazanılacak alan; Düzenle'ninkiler gizli), `DuplicatesSection.xaml` (etiket "düzenli" / "öncelikli" → `Converter` ya da VM'de `PreferredLabel`), `ConfirmPage.xaml` (Clean'de: üç satır `_Ayiklananlar\…` hedefiyle, "Dosyalar kopyalanmaz; aynı disk içinde yeri değişir, hiçbir şey silinmez.", yer ve başka disk satırları ile Geri Dönüşüm kutusu gizli), `ApplyPage.xaml` (Clean'de "Ayıklananları aç").

- [ ] **Step 1:** Uygula; görünürlükler `Kind`'a bağlı (`EqVis` ile `Clean` / `Organize`).
- [ ] **Step 2:** `dotnet build` hatasız; harness'a Temizle turu ekle (örnek klasör, tarama, bölümler, ayıklama, Onayla, Uygula, Bitti), açık ve koyu temada metin dökümü, bağlama hatası 0.
- [ ] **Step 3:** `dotnet test` yeşil. Commit: `feat(arayuz): Temizle ekranlari`

### Task 7: Belgeler ve paket

**Files:**
- Modify: `CHANGELOG.md` ([Yayınlanmamış] → Eklendi: Temizle ekranı), `README.md` (Temizle kullanımı), `YOL_HARITASI.md` (Temizle maddesi ✅ yeni arayüz)

- [ ] **Step 1:** Belgeleri güncelle.
- [ ] **Step 2:** `./publish.ps1` (uygulama açıksa atla ve raporda söyle); exe açılıyor.
- [ ] **Step 3:** `dotnet test` yeşil. Commit: `docs: Temizle ekrani`
