# Yeni arayüz 1. parça — uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WPF-UI kabuğu, Ana ekran ve dört adımlı Düzenle akışı (Klasörler → Tara ve incele → Onayla →
Uygulanıyor/Bitti). Ekran mantığı test edilebilen bir ViewModel katmanında durur; eski pencere
"Klasik görünüm" olarak kalır.

**Architecture:** Yeni `FotoDuzenleyici.ViewModels` projesi CommunityToolkit.Mvvm ile ViewModel'leri ve
hizmet arayüzlerini taşır, WPF tipi kullanmaz. Pencereye bağlı işler `Services` kaydının arkasındadır;
testler sahtelerini verir. `FotoDuzenleyici.App`, WPF-UI ile kabuğu ve sayfaları çizer ve hizmetlerin
gerçek uygulamalarını yazar. Core'a yalnız `PlanCsv` eklenir.

**Tech Stack:** C# .NET 8, WPF, WPF-UI 4.2.1, CommunityToolkit.Mvvm 8.4.2, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-07-yeni-arayuz-1-design.md`

## Global Constraints

- **ViewModels projesi**
  - Hedef çerçeve `net8.0-windows`, çünkü Core da öyle. `UseWPF` yok.
  - Kodda `System.Windows.*` kullanılmaz. Spec'teki "net8.0" ifadesi WPF bağımsızlığı anlamına gelir.
- **Dosyaya dokunma:** Arayüz dosyalara yalnızca Core üzerinden dokunur: `OrganizeSession`, `Executor`.
- **Yollar ve dosya adları:**
  - Ayarlar: `%LOCALAPPDATA%\FotoDuzenleyici\ayarlar.json`.
  - Günlük: `<arşiv>\_Gunluk\islem-yyyyMMdd-HHmmss.jsonl`.
- **Sayılar ve argümanlar:**
  - Son kullanılanlar en fazla 5 çift tutar.
  - Kalan süre ilk 20 işlemden sonra gösterilir.
  - `--klasik` argümanı eski pencereyi açar.
- **Testler:** Mevcut 281 test yeşil kalır.
- **Commit mesajları:** Co-Authored-By satırı ve "Generated with" yazısı yok.
- **Gerçek veri:** Elle denemede kullanıcının gerçek verisinde Uygula'ya basılmaz.

## Review Focus

1. **Uygulama sürerken pencere kapatma ya da gezinme.**
   - Beklenen: gezinme kilitli olur; kapatma "durdurulsun mu?" diye sorar.
   - Test: Görev 5 (`Apply_NavigationLockedWhileRunning`).
2. **Arşiv aynı zamanda kaynak.** Dosyalar çift sayılmamalı; `ScanRoots` kullanılır.
   - Test: Görev 3 (`Scan_ArchiveAlsoSource_NoDoubleCount`).
3. **Durdurma sonrası Onayla'ya dönüş.**
   - Beklenen: yarım işlem satırı görünür; Devam et kalan dosyaları işler.
   - Test: Görev 5'in uçtan uca testi.
4. **Bitti'den sonra eski planla yeniden Uygula.** Engellenir.
   - Test: Görev 5 (`AfterFinish_ConfirmUnavailable`).
5. **Var olmayan kaynak.** Taramaya katılmaz; tek kaynak oysa Tara kapalı olur.
   - Test: Görev 2.

---

### Task 1: ViewModels projesi, hizmet arayüzleri, ayarlar

**Files:**
- Create: `FotoDuzenleyici.ViewModels/FotoDuzenleyici.ViewModels.csproj`
  - Paketler: Core'a referans, `CommunityToolkit.Mvvm` 8.4.2.
  - Kullanımlar (`<Using>`): `System.IO`, `FotoDuzenleyici.Core`.
- Create: `FotoDuzenleyici.ViewModels/Services.cs`
- Create: `FotoDuzenleyici.ViewModels/Settings.cs`
- Modify:
  - `FotoDuzenleyici.sln`: projeyi ekle.
  - `FotoDuzenleyici.Tests/FotoDuzenleyici.Tests.csproj`: ViewModels'e referans.
- Test: `FotoDuzenleyici.Tests/SettingsStoreTests.cs`

**Interfaces — Produces:**
```csharp
namespace FotoDuzenleyici.ViewModels;
public interface IFolderPicker { string? PickFolder(string title, string? initial = null); }
public interface IFilePicker { string? OpenJournal(string? initialDir); string? SaveCsv(string suggestedName); }
public interface IDialogs { bool Confirm(string title, string message); void Info(string title, string message); }
public interface IShell { void OpenFolder(string path); void OpenClassic(); }
public interface ISettingsStore { AppSettings Load(); void Save(AppSettings s); bool LoadFailed { get; } }
public sealed record Services(IFolderPicker Folders, IFilePicker Files, IDialogs Dialogs, IShell Shell,
    ISettingsStore Settings, Func<string, long> FreeSpace, Action<string>? Recycle = null);
public sealed record RecentPair(List<string> Sources, string Archive, DateTime Used);
public sealed class AppSettings {
    public List<RecentPair> Recent { get; set; } = new();
    public SourceFolderMode SourceFolder { get; set; } = SourceFolderMode.None;
    public bool RenameWithDate { get; set; } public bool AppendSourceToName { get; set; }
    public bool SeparateRawFolder { get; set; } = true;
    public string Theme { get; set; } = "Sistem";      // Sistem | Açık | Koyu
    public bool RecycleSource { get; set; } = true;
    public void AddRecent(IEnumerable<string> sources, string archive, DateTime now); }
public sealed class JsonSettingsStore : ISettingsStore { public JsonSettingsStore(string path); public static string DefaultPath { get; } }
```
- `FreeSpace(path)` → yolun sürücüsündeki boş bayt.
- `Recycle`:
  - `null` → Windows Geri Dönüşüm Kutusu; doğrudan `Executor`'a geçirilir.
  - Testler bir klasöre taşıyan bir eylem verir.

- [ ] **Step 1: Failing tests**
  - `Settings_RoundTrip`: kaydet → yükle; bütün alanlar eşit.
  - `Settings_Missing_ReturnsDefaults`: `LoadFailed == false`.
  - `Settings_Corrupt_ReturnsDefaults_AndFlags`: dosyaya `"{bozuk"` yazılır; `LoadFailed == true`.
  - `AddRecent_KeepsFiveNewestFirst_AndDedupes`:
    - 6 farklı çift → 5 kalır; en yeni başta.
    - Aynı kaynak kümesi + arşiv, sıra ve büyük/küçük harf farkıyla yeniden eklenince tek kayıt olur ve
      başa geçer.
- [ ] **Step 2:** `dotnet test --filter SettingsStoreTests` → derleme hatası ya da FAIL.
- [ ] **Step 3:** Uygula.
  - Kayıt atomik olmalı: önce `.tmp`'ye yaz, sonra `File.Move(overwrite:true)`.
  - Klasör yoksa oluşturulur.
- [ ] **Step 4:** Aynı filtre → PASS; ardından tam `dotnet test` → 285 geçer.
- [ ] **Step 5:** Commit: "ViewModels projesi: hizmet arayüzleri ve ayarlar".

### Task 2: Klasörler adımı

**Files:**
- Create: `FotoDuzenleyici.ViewModels/FoldersStepViewModel.cs`
- Test: `FotoDuzenleyici.Tests/FoldersStepTests.cs`

**Interfaces:**
- **Consumes:** Görev 1'deki `Services` ve `AppSettings`.
- **Produces:**
```csharp
public sealed partial class SourceEntry : ObservableObject { public string Path { get; } public bool Exists { get; } }
public sealed partial class FoldersStepViewModel : ObservableObject {
    public FoldersStepViewModel(Services s, AppSettings settings);
    public ObservableCollection<SourceEntry> Sources { get; }
    [ObservableProperty] string? archive;
    public IRelayCommand AddSourceCommand { get; } public IRelayCommand<SourceEntry> RemoveSourceCommand { get; }
    public IRelayCommand PickArchiveCommand { get; }
    public bool CanScan { get; }                 // ≥1 var olan kaynak && Archive dolu
    public IReadOnlyList<string> ExistingSources { get; }
    public string OptionsSummary { get; }        // ör. "Ada tarih ekle · RAW ayrı klasörde"
    public PlanOptions BuildPlanOptions();       // DestinationRoot = tam yol(Archive), diğerleri settings'ten
    public void Load(RecentPair p);
    public void RememberCurrent(DateTime now);   // settings.AddRecent + Settings.Save
}
```
- Seçenek değiştirme iletişim kutusu App'te yazılır ve doğrudan `AppSettings` alanlarını değiştirir.
- `OptionsSummary` yeniden hesaplanır: `NotifyOptionsChanged()` yöntemi.

- [ ] **Step 1: Failing tests**
  - `AddSource_FromPicker_AddsOnce`: aynı yol iki kez seçilir, sonda ters bölü farkıyla → bir kayıt.
  - `MissingSource_NotExisting_ExcludedFromScan`.
  - `CanScan_RequiresExistingSourceAndArchive`: 3 durum.
  - `CanScan_PickerCancelled_NoChange`: seçici `null` döner.
  - `BuildPlanOptions_UsesSettingsAndArchive`.
  - `Load_RecentPair_FillsBoth`.
  - `RememberCurrent_SavesRecent`.
- [ ] **Step 2:** `dotnet test --filter FoldersStepTests` → FAIL.
- [ ] **Step 3:** Uygula. `CanScan` değişimini koleksiyon ve `Archive` değişiminde bildir.
- [ ] **Step 4:** PASS + tam suite.
- [ ] **Step 5:** Commit: "Yeni arayüz: Klasörler adımı".

### Task 3: Akış, tarama ve İnceleme adımı (+ `PlanCsv`, `PlanSummary`)

**Files:**
- Create:
  - `FotoDuzenleyici.Core/PlanCsv.cs`
  - `FotoDuzenleyici.ViewModels/PlanSummary.cs`
  - `FotoDuzenleyici.ViewModels/OrganizeFlowViewModel.cs`
  - `FotoDuzenleyici.ViewModels/ReviewStepViewModel.cs`
- Modify: `FotoDuzenleyici.App/MainWindow.xaml.cs`. `Csv_Click`, `PlanCsv.Write` çağırsın; biçim aynen kalır.
- Test:
  - `FotoDuzenleyici.Tests/PlanSummaryTests.cs`
  - `FotoDuzenleyici.Tests/OrganizeFlowScanTests.cs`

**Interfaces:**
- **Consumes:** Görev 2'deki `FoldersStepViewModel`.
- **Produces:**
```csharp
// Core
public static class PlanCsv { public static void Write(Plan plan, string path); } // MainWindow.Csv_Click'teki başlık ve satır biçimi, UTF-8 BOM
// ViewModels
public sealed record PlanSummary(int MoveCount, long MoveBytes, int InPlace, int DupCount, long DupBytes,
    int Undated, int Renamed, int ApplyCount) { public static PlanSummary From(Plan plan, PlanOptions opt); }
public enum FlowStep { Folders, Review, Confirm, Apply }
public enum PlanFilter { All, Move, Duplicate, InPlace, Renamed }
public sealed partial class OrganizeFlowViewModel : ObservableObject {
    public OrganizeFlowViewModel(Services s, AppSettings settings);
    public FoldersStepViewModel Folders { get; }
    [ObservableProperty] FlowStep step; [ObservableProperty] bool isBusy; [ObservableProperty] string? errorMessage;
    [ObservableProperty] string? progressText; [ObservableProperty] double progress; // 0..1
    public ReviewStepViewModel? Review { get; } public ConfirmStepViewModel? Confirm { get; } public ApplyStepViewModel? Apply { get; }
    public IAsyncRelayCommand ScanCommand { get; } public IRelayCommand CancelCommand { get; }
    public IRelayCommand GoToConfirmCommand { get; } public IRelayCommand BackCommand { get; } public IAsyncRelayCommand RescanCommand { get; }
    public bool NavigationLocked { get; }        // Apply çalışırken true
}
public sealed partial class ReviewStepViewModel : ObservableObject {
    public ReviewStepViewModel(OrganizeSession session, Plan plan, Services s);
    public PlanSummary Summary { get; } public int DuplicateGroups { get; } public IReadOnlyList<string> ScanErrors { get; }
    [ObservableProperty] PlanFilter filter; [ObservableProperty] string search = "";
    public IReadOnlyList<PlanItem> Rows { get; }  // Filter + Search uygulanmış, Kaynak yoluna göre sıralı
    public IRelayCommand SaveCsvCommand { get; }
}
```
- `Confirm`/`Apply` tipleri Görev 4–5'te yazılır. Bu görevde akış yalnız `Folders` ↔ `Review` arasında gezer.
- **Tarama:** `Folders.RememberCurrent`, sonra `new OrganizeSession(ExistingSources, BuildPlanOptions())`.
  - `Scan(scanProg, hashProg, ct)` ve `BuildPlan()` `Task.Run` içinde çalışır.
  - İptal → `Step = Folders`, `ErrorMessage = null`, `ProgressText = "Durduruldu"`.
  - İstisna → `ErrorMessage = ex.Message`; `Step` değişmez.
- **Tarihsiz sayımı:** `Move` öğesinin `TargetDir`'inin arşive göre ilk klasör adı `opt.UndatedFolderName`
  ise sayılır.
- **Arama:** kaynak ya da hedef yolunda, büyük/küçük harf duyarsız.

- [ ] **Step 1: Failing tests**
  - `PlanSummary_CountsAndBytes`: elle kurulmuş `Plan`; her eylemden bir öğe, biri tarihsiz, biri adı
    değişmiş → bütün alanlar beklenen değerde, `ApplyCount` = `AlreadyInPlace` dışındakiler.
  - `PlanCsv_WritesHeaderAndRows`.
  - `Scan_BuildsPlan_GoesToReview`:
    - geçici kaynakta 2 aynı pdf + 1 farklı pdf; ayrı bir arşiv.
    - Beklenen: `Step == Review`, `Summary.DupCount == 1`, `DuplicateGroups == 1`.
  - `Scan_ArchiveAlsoSource_NoDoubleCount`.
  - `Scan_Cancelled_StaysOnFolders`: tarama başlamadan iptal edilmiş belirteç.
  - `Review_FilterAndSearch`: `Duplicate` filtresi ve arama metni.
  - `Back_FromReview_ToFolders`.
  - Test dosyaları düz metin içerikli `.pdf`'lerdir; varsayılan `ScanOptions.IncludedKinds` PDF'i kapsar.
- [ ] **Step 2:** `dotnet test --filter "PlanSummaryTests|OrganizeFlowScanTests"` → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** PASS + tam suite.
- [ ] **Step 5:** Commit: "Yeni arayüz: akış, tarama ve İnceleme adımı".

### Task 4: Onayla adımı

**Files:**
- Create: `FotoDuzenleyici.ViewModels/ConfirmStepViewModel.cs`
- Modify: `OrganizeFlowViewModel` (`GoToConfirmCommand`, Confirm → Review geri)
- Test: `FotoDuzenleyici.Tests/ConfirmStepTests.cs`

**Interfaces — Produces:**
```csharp
public sealed partial class ConfirmStepViewModel : ObservableObject {
    public ConfirmStepViewModel(Plan plan, PlanOptions opt, Services s, AppSettings settings, DateTime now);
    public PlanSummary Summary { get; }
    public long NeedBytes { get; } public long FreeBytes { get; } public bool SpaceOk { get; }
    public IReadOnlyList<string> OtherDriveSources { get; }   // arşivle aynı sürücü kökünde olmayan kaynak kökleri
    [ObservableProperty] (string Journal, int Remaining)? unfinished;  // Executor.FindUnfinished(arşiv).FirstOrDefault
    public IRelayCommand AbandonCommand { get; }   // Dialogs.Confirm → Executor.Abandon → Unfinished = sonraki ya da null
    public IRelayCommand ResumeCommand { get; }    // akışa ResumeRequested(journal) bildirir (Görev 5 bağlar)
    [ObservableProperty] bool recycleSource;       // settings.RecycleSource; değişince kaydedilir
    public string JournalPath { get; }             // <arşiv>\_Gunluk\islem-{now:yyyyMMdd-HHmmss}.jsonl
    public bool CanApply { get; }                  // SpaceOk && Unfinished == null && Summary.ApplyCount > 0
    public event Action<string>? ResumeRequested; public event Action? ApplyRequested;
    public IRelayCommand ApplyCommand { get; }     // CanApply ise ApplyRequested
}
```
- `NeedBytes` = `MoveBytes + DupBytes`.
- `FreeBytes` = `s.FreeSpace(arşiv)`. Arşiv henüz yoksa onu içeren ilk var olan üst klasör kullanılır.

- [ ] **Step 1: Failing tests**
  - `Space_Insufficient_BlocksApply`: `FreeSpace` = 1 bayt.
  - `Space_Enough_AllowsApply`.
  - `Unfinished_Shown_BlocksApply`: arşivin `_Gunluk`'una `Planned` satırlı bir günlük yazılır.
  - `Abandon_AfterConfirm_ClearsAndAllowsApply`: sahte `Dialogs.Confirm = true`.
  - `Abandon_Declined_KeepsBlocked`.
  - `OtherDrive_ListsSourcesOnDifferentRoot`: yollar `E:\x`, `D:\a` ile kurulur; yalnız hesap yapılır, diske
    dokunulmaz.
  - `JournalPath_UsesArchiveAndTime`.
  - `RecycleSource_PersistsToSettings`.
- [ ] **Step 2:** `dotnet test --filter ConfirmStepTests` → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** PASS + tam suite.
- [ ] **Step 5:** Commit: "Yeni arayüz: Onayla adımı".

### Task 5: Uygulama adımı, devam, Geri Al ve uçtan uca

**Files:**
- Create: `FotoDuzenleyici.ViewModels/ApplyStepViewModel.cs`
- Modify: `OrganizeFlowViewModel`. `ApplyRequested` → `Apply` adımı + `Run`; `ResumeRequested` → `Resume`.
- Test:
  - `FotoDuzenleyici.Tests/ApplyStepTests.cs`
  - `FotoDuzenleyici.Tests/OrganizeFlowEndToEndTests.cs`

**Interfaces — Produces:**
```csharp
public enum ApplyMode { Run, Resume, Undo }
public sealed partial class ApplyStepViewModel : ObservableObject {
    public ApplyStepViewModel(Services s, string archive);
    public Task RunAsync(IReadOnlyList<PlanItem> items, string journal, bool recycle);
    public Task ResumeAsync(string journal);
    public Task UndoAsync(string journal);
    [ObservableProperty] ApplyMode mode; [ObservableProperty] bool isRunning; [ObservableProperty] bool stopped;
    [ObservableProperty] int done; [ObservableProperty] int total; [ObservableProperty] string? current;
    [ObservableProperty] TimeSpan? remaining; [ObservableProperty] ExecuteResult? result;
    public string Journal { get; }
    public IRelayCommand CancelCommand { get; } public IAsyncRelayCommand UndoCommand { get; } // Dialogs.Confirm → UndoAsync(Journal)
    public IRelayCommand OpenArchiveCommand { get; }
    public static TimeSpan? Eta(int done, int total, TimeSpan elapsed);  // done < 20 → null; aksi: elapsed / done * (total - done)
}
```
- `Run`/`Resume`, `new Executor(journal, s.Recycle)` ile çalışır.
- Durdurma:
  - `Executor` iptalde `OperationCanceledException` fırlatabilir ya da kısmi sonuç dönebilir.
  - İkisinde de `Stopped = true` olur ve `Result` günlükteki son durumlardan hesaplanır: `Executor.FinalStates`.
- Bitince akış:
  - `Review` ve `Confirm` `null` olur, plan geçersizleşir.
  - `NavigationLocked = IsRunning`.
  - `BackCommand` yalnız `Folders`'a döner ("yeni işlem").

- [ ] **Step 1: Failing tests**
  - `Eta_NullUntil20_ThenLinear`.
  - `Run_MovesFiles_ReportsResult`: geçici kaynak/arşiv, `Recycle` = bir klasöre taşı.
  - `Cancel_BeforeStart_Stopped_JournalHasPending`.
  - `Apply_NavigationLockedWhileRunning`: yavaş bir `Recycle` kancasıyla, çalışırken
    `NavigationLocked == true`.
  - `AfterFinish_ConfirmUnavailable`: `Confirm == null`, `Step == Apply`, `BackCommand` → `Folders`.
  - `EndToEnd_PickScanConfirmApplyUndo`:
    - Yalnız ViewModel komutlarıyla: kaynak + arşiv seç → `ScanCommand` → `GoToConfirmCommand` → `ApplyCommand`.
    - Dosyalar arşivin `yyyy-MM` klasörlerinde; `UndoCommand` (Confirm = true) sonrası kaynakta.
  - `EndToEnd_StopThenResume`:
    - Run 1 dosyadan sonra durdurulur (iptali `Recycle` kancasında tetikle).
    - Yeni akış aynı arşivle tarar → Onayla'da `Unfinished` dolu → `ResumeCommand` → kalan dosyalar işlenir.
- [ ] **Step 2:** `dotnet test --filter "ApplyStepTests|OrganizeFlowEndToEndTests"` → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** PASS + tam suite.
- [ ] **Step 5:** Commit: "Yeni arayüz: uygulama, devam ve Geri Al".

### Task 6: Kabuk ve Ana ekran ViewModel'leri

**Files:**
- Create:
  - `FotoDuzenleyici.ViewModels/ShellViewModel.cs`
  - `FotoDuzenleyici.ViewModels/HomeViewModel.cs`
- Test: `FotoDuzenleyici.Tests/ShellTests.cs`

**Interfaces — Produces:**
```csharp
public sealed partial class HomeViewModel : ObservableObject {
    public HomeViewModel(AppSettings settings); public RecentPair? LastOrganize { get; } public IReadOnlyList<RecentPair> Recent { get; }
    public event Action<RecentPair?>? OrganizeRequested; public IRelayCommand<RecentPair?> StartOrganizeCommand { get; } }
public sealed partial class ShellViewModel : ObservableObject {
    public ShellViewModel(Services s);
    [ObservableProperty] object currentPage;   // HomeViewModel ya da OrganizeFlowViewModel
    [ObservableProperty] string? errorMessage; // ayar yükleme uyarısı + global Geri Al hataları
    public IRelayCommand GoHomeCommand { get; }          // akış kilitliyse çalışmaz
    public IAsyncRelayCommand UndoCommand { get; }       // Files.OpenJournal(son arşiv\_Gunluk) → Confirm → yeni akış Apply adımında UndoAsync
    public IRelayCommand OpenClassicCommand { get; }
    [ObservableProperty] string theme;                    // değişince ayarlara kaydedilir
    public bool CanClose();                               // akış çalışıyorsa Dialogs.Confirm("işlem sürüyor, durdurulsun mu?") → Cancel
}
```

- [ ] **Step 1: Failing tests**
  - `Start_ShowsHome`.
  - `StartOrganize_WithRecent_PrefillsFolders`.
  - `GoHome_BlockedWhileApplying`.
  - `CorruptSettings_ShowsWarningOnce`.
  - `Undo_PickerCancelled_NoChange`.
  - `CanClose_WhileRunning_AsksAndCancels`.
  - `Theme_Persists`.
- [ ] **Step 2:** `dotnet test --filter ShellTests` → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** PASS + tam suite.
- [ ] **Step 5:** Commit: "Yeni arayüz: kabuk ve Ana ekran".

### Task 7: WPF-UI görünümleri ve hizmet uygulamaları

**Files:**
- Modify:
  - `FotoDuzenleyici.App/FotoDuzenleyici.App.csproj`: `WPF-UI` 4.2.1 ve ViewModels referansı.
  - `App.xaml`: WPF-UI `ThemesDictionary` + `ControlsDictionary`.
  - `App.xaml.cs`: `--klasik` argümanı → `MainWindow`; yoksa `ShellWindow`.
- Create, `FotoDuzenleyici.App/Shell/` altında:
  - `ShellWindow.xaml(.cs)`: `ui:FluentWindow`, Mica, `ui:TitleBar`, sağ üst menü, `ui:InfoBar`, sayfa
    `ContentControl` + `DataTemplate`'ler.
  - `HomePage.xaml`
  - `OrganizeFlowView.xaml`: adım çubuğu, ilerleme şeridi, adım `DataTemplate`'leri.
  - `FoldersPage.xaml`
  - `ReviewPage.xaml`: yan menü, kutucuklar, sanallaştırılmış `DataGrid`, alt çubuk.
  - `ConfirmPage.xaml`
  - `ApplyPage.xaml`: çalışıyor ve bitti durumları.
  - `OptionsDialog.xaml`
  - `WpfServices.cs`:
    - `IFolderPicker` → `Microsoft.Win32.OpenFolderDialog`;
    - `IFilePicker`, `IDialogs` → `ui:MessageBox`;
    - `IShell` → `explorer.exe` ve `new MainWindow().Show()`;
    - `FreeSpace` → `DriveInfo`.
  - `Converters.cs`: yolu baştan kısaltan dönüştürücü, bayt → "214,6 GB" ve eylem → renkli etiket.

Görsel ölçü kaynağı `docs/tasarim/arayuz-taslak.html`. Renkler WPF-UI tema kaynaklarından gelir. Tema
değişimi `ApplicationThemeManager.Apply` ile yapılır; `Sistem` → `SystemThemeWatcher`.

- [ ] **Step 1:** `dotnet build FotoDuzenleyici.sln -c Release` → 0 hata, 0 yeni uyarı.
- [ ] **Step 2: Elle duman testi.** Uygulamayı geçici klasörlerle aç; açık ve koyu temada ekran görüntüsü al:
  - Ana ekran;
  - Klasörler;
  - tarama ilerlemesi;
  - İnceleme;
  - Onayla: yarım işlem satırıyla ve onsuz;
  - Uygulanıyor;
  - Bitti;
  - Geri Al;
  - Klasik görünüm.
  - Uygulanıyor sırasında pencereyi kapatmayı dene → soru gelmeli.
  - Gerçek arşivde Uygula yok.
- [ ] **Step 3:** `dotnet test` → hepsi geçer.
- [ ] **Step 4:** Commit: "Yeni arayüz: WPF-UI kabuğu ve sayfalar".

### Task 8: Sürüm, belgeler ve yayın denetimi

**Files:**
- `FotoDuzenleyici.App/FotoDuzenleyici.App.csproj`: `<Version>0.2.0</Version>`.
- `CHANGELOG.md`: [0.2.0] başlığı. Yeni arayüz, Klasik görünüm, `--klasik`; [Yayınlanmamış] maddeleri
  buraya taşınır.
- `YOL_HARITASI.md`: Aşama 3 "Koyu tema, modern görünüm" → 🔄 (1. parça: kabuk + Düzenle).
- `README.md`: ekran adları ve `--klasik`.

- [ ] **Step 1:** `./publish.ps1` (ya da depodaki yayın komutu).
  - Tek dosya `.exe` oluşur.
  - Açılınca kabuk gelir: WPF-UI kaynakları tek dosyada yüklenebiliyor.
- [ ] **Step 2:** `dotnet test` → hepsi geçer.
- [ ] **Step 3:** Commit: "0.2.0: yeni arayüz (1. parça)". Etiket ve gönderim kullanıcı onayıyla yapılır.
