# Zaman çizelgesi Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ana ekrandan açılan, seçilen klasördeki fotoğrafları yıl × ay tablosunda sayan salt okunur "Zaman çizelgesi" ekranı.

**Architecture:** Sayım `FotoDuzenleyici.Core/Timeline.cs`'te iki yöntemle (klasör adları / çekim tarihi) dosya başına
`TimelineEntry` listesi üretir. `FotoDuzenleyici.ViewModels` bu listeyi süzgece göre tabloya çevirir
(`TimelineTable`), `TimelineViewModel` klasör/yöntem seçimini, arka plan sayımını ve tıklamaları yönetir. WPF
sayfası yalnız bağlanır. Kabuk ve ana ekran mevcut olay desenini (`…Requested` → `ShellViewModel`) kullanır.

**Tech Stack:** .NET 8, WPF + WPF-UI, CommunityToolkit.Mvvm, xUnit, MetadataExtractor (mevcut `Scanner`).

**Spec:** `docs/superpowers/specs/2026-11-17-zaman-cizelgesi-design.md`

## Global Constraints

- Hiçbir dosyaya yazılmaz, taşınmaz, silinmez; yalnız `AppSettings.LastTimelineFolder` kaydedilir (`TrySave`).
- Sayılan türler yalnız `FileKind.Photo`, `Raw`, `Video`; Pdf/belge/diğer hiç sayılmaz.
- Özel klasör adları (her iki yöntemde, her derinlikte taranmaz): `_Tekrarlar`, `_Benzerler`, `_Kusurlu`,
  `_Gunluk`, `_Ayiklananlar`, `Tarihsiz`.
- Ay klasörü adı kalıbı: `^\d{4}-(0[1-9]|1[0-2])$`.
- Varsayılan süzgeç **Foto**. Süzgeç değişince yeniden sayılmaz.
- Sayım gezinmeyi kilitlemez, kapatırken sormaz; sayfadan çıkış / pencere kapanışı sayımı sessizce iptal eder.
- Art arda ≥2 tamamen boş yıl tek satıra toplanır: "2001 – 2015: boş".
- Arayüz metinleri Türkçe; sayılar `N0`, boyutlar `Sizes.Text`.
- Yeni NuGet bağımlılığı yok. Klasik görünüme dokunulmaz.
- Commit iletilerine Claude ortak yazar satırı eklenmez.
- Hiçbir test ya da komut `D:\1- HERKES\…` altında çalışmaz/yazmaz; testler `Path.GetTempPath()` altında.

## Review Focus

1. Klasör sayım sırasında silinir ya da bir dosya kilitlenir → sayım çökmeden biter, sorun "okunamadı"
   sayısına girer (`ByCaptureDate` dosya başı try/catch; `ByFolders` klasör erişim hatası `Errors`'a).
2. Sayım sürerken kullanıcı klasörü/yöntemi değiştirir ya da Ana ekrana döner → eski sayımın sonucu yeni
   ekrana yazılmaz (sonuç, başladığı klasör+yöntem hâlâ geçerliyse uygulanır).
3. Sürücü kökü (`E:\`) seçilir → `$RECYCLE.BIN`/`System Volume Information` sayılmaz (`Scanner` zaten atlar;
   `ByFolders` de gizli+sistem klasörleri atlar).
4. Hiç fotoğraf yok ya da süzgeçte hiç yok (yalnız video) → tablo boş, "Bu klasörde fotoğraf bulunamadı"
   gibi bir satır; çökme yok, `max = 0` bölmesi yok.
5. Ayar dosyasında olmayan klasör (`LastTimelineFolder` silinmiş) → atlanır, sıradaki aday kullanılır.

Her biri aşağıda sahibi olan görevde bir testle sabitlenmiştir.

---

### Task 1: Core — klasör adlarından sayım

**Files:**
- Create: `FotoDuzenleyici.Core/Timeline.cs`
- Test: `FotoDuzenleyici.Tests/TimelineFolderTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum TimelineMethod { FolderNames, CaptureDate }
  /// Folder: FolderNames → yyyy-MM klasörünün tam yolu; CaptureDate → dosyanın doğrudan bulunduğu klasör.
  public sealed record TimelineEntry(int Year, int Month, FileKind Kind, long Size, string Folder, DateSource Source);
  public sealed class TimelineResult
  {
      public required string Root { get; init; }
      public TimelineMethod Method { get; init; }
      public List<TimelineEntry> Entries { get; } = new();
      /// Kök düzeyindeki özel klasörler → içlerindeki Foto/Raw/Video sayısı (yalnız > 0 olanlar).
      public SortedDictionary<string, int> Special { get; } = new(StringComparer.OrdinalIgnoreCase);
      /// FolderNames: yyyy-MM ve özel klasörler dışındaki Foto/Raw/Video sayısı. CaptureDate'te 0.
      public int Outside { get; set; }
      /// "yol: ileti" — okunamayan klasör ve dosyalar.
      public List<string> Errors { get; } = new();
      public bool Cancelled { get; set; }
  }
  public static class Timeline
  {
      public static IReadOnlySet<string> SpecialFolderNames { get; }
      public static bool IsMonthFolderName(string name);
      /// Kökün doğrudan altında en az bir ay klasörü varsa FolderNames, yoksa CaptureDate.
      public static TimelineMethod DefaultMethod(string root);
      public static TimelineResult ByFolders(string root, CancellationToken ct = default);
  }
  ```

- [ ] **Step 1: Write the failing tests** (`TimelineFolderTests`, geçici ağaç; dosya içerikleri önemsiz, boyutlar `new string('x', n)` ile)

```csharp
[Fact] public void IsMonthFolderName_AcceptsOnlyYearDashMonth()
{ Assert.True(Timeline.IsMonthFolderName("2022-11")); Assert.False(Timeline.IsMonthFolderName("2022-13"));
  Assert.False(Timeline.IsMonthFolderName("2022-11-Foto")); Assert.False(Timeline.IsMonthFolderName("Kasım")); }

[Fact] public void DefaultMethod_FolderNamesWhenMonthFolderExists_ElseCaptureDate()
// kök\2022-11\ var → FolderNames; yalnız kök\Tatil\ → CaptureDate

[Fact] public void ByFolders_CountsFilesUnderMonthFolders_WithKindAndSize()
// kök\2022-11\2022-11-Foto\a.jpg (3 B), b.jpg (5 B); kök\2022-11\2022-11-Raw\a.cr3 (7 B); kök\2022-11\2022-11-Video\v.mp4
// kök\2022-11\2022-11-Pdf\x.pdf → Entries: 2 Photo, 1 Raw, 1 Video (pdf yok); hepsi Year 2022 Month 11,
// Folder == kök\2022-11; Photo boyutları toplamı 8

[Fact] public void ByFolders_SpecialFoldersReportedNotCounted()
// kök\_Tekrarlar\2022-11\c.jpg, kök\Tarihsiz\d.jpg, kök\2022-11\_Tekrarlar\e.jpg (iç içe özel: ne sayılır ne raporlanır)
// → Entries'de c/d/e yok; Special["_Tekrarlar"] == 1, Special["Tarihsiz"] == 1

[Fact] public void ByFolders_FilesOutsideMonthFolders_CountedAsOutside()
// kök\f.jpg, kök\Tatil\g.jpg, kök\Tatil\notlar.txt → Outside == 2

[Fact] public void ByFolders_SkipsHiddenSystemAndReparseFolders()
// kök\2022-11\gizli (Hidden|System) içinde h.jpg → sayılmaz

[Fact] public void ByFolders_RootMissing_ReturnsErrorNotThrow()
// olmayan kök → Entries boş, Errors.Count == 1
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TimelineFolderTests`
Expected: derleme hatası — `Timeline` tanımlı değil.

- [ ] **Step 3: Implement `Timeline` (enum, record, result, `SpecialFolderNames`, `IsMonthFolderName`, `DefaultMethod`, `ByFolders`) in `FotoDuzenleyici.Core/Timeline.cs`**

`ByFolders`: kökün doğrudan alt klasörlerini gez; ay klasörü → içini özyinelemeli say (özel adlı, gizli+sistem
ve reparse klasörlere girme, `Scanner.Walk`'taki kuralla aynı); özel adlı → `Special`'a say; diğer klasörler ve
kökteki dosyalar → `Outside`'a say. Her klasör döngüsünde `ct.ThrowIfCancellationRequested()`; iptalde
`Cancelled = true` ve `Entries` temizlenmiş sonuç döner (istisna dışarı çıkmaz). Erişim hataları `Errors`'a
"yol: ileti". `Source` = `DateSource.None`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~TimelineFolderTests`
Expected: 7 geçti.

- [ ] **Step 5: Commit**

```bash
git add FotoDuzenleyici.Core/Timeline.cs FotoDuzenleyici.Tests/TimelineFolderTests.cs
git commit -m "Zaman çizelgesi: klasör adlarından sayım"
```

---

### Task 2: Core — çekim tarihinden sayım

**Files:**
- Modify: `FotoDuzenleyici.Core/Timeline.cs`
- Test: `FotoDuzenleyici.Tests/TimelineCaptureDateTests.cs`

**Interfaces:**
- Consumes: Task 1 `TimelineResult`, `TimelineEntry`, `SpecialFolderNames`; mevcut `Scanner`, `ScanOptions`, `Scanner.ReadFile`.
- Produces:
  ```csharp
  public static TimelineResult ByCaptureDate(string root, IProgress<(int done, int total)>? progress = null,
      CancellationToken ct = default, Func<string, MediaFile>? read = null);   // read null → Scanner.ReadFile
  ```

- [ ] **Step 1: Write the failing tests** (`TestMedia.Jpeg(make, taken)` ile EXIF'li JPEG)

```csharp
[Fact] public void ByCaptureDate_UsesExifDate_AndContainingFolder()
// kök\Tatil\a.jpg EXIF 2021-07-04 → Entry (2021, 7, Photo, Folder == kök\Tatil, Source == Exif)

[Fact] public void ByCaptureDate_FileNameAndFileTimeSources_AreKept()
// kök\IMG_20200105_x.jpg (geçersiz içerik) → (2020,1, Source FileName);
// kök\y.jpg geçersiz içerik, LastWriteTime/CreationTime 2019-03-10 → (2019,3, Source FileTime)

[Fact] public void ByCaptureDate_SkipsSpecialFolders_ReportsRootLevelOnes()
// kök\_Tekrarlar\b.jpg, kök\_Ayiklananlar\c.jpg → Entries'de yok; Special["_Tekrarlar"] == 1, Special["_Ayiklananlar"] == 1

[Fact] public void ByCaptureDate_IgnoresPdfAndOther()
// kök\x.pdf, kök\n.txt → Entries boş, read hiç çağrılmaz (read sahtesi çağrıları sayar)

[Fact] public void ByCaptureDate_ReadThrows_CountsAsError_AndContinues()
// read: "bozuk.jpg" için IOException, diğerleri Scanner.ReadFile → Entries 1, Errors.Count == 1

[Fact] public void ByCaptureDate_ReportsProgress_ToTotal()
// 3 dosya → son bildirim (3, 3)

[Fact] public void ByCaptureDate_Cancelled_ReturnsCancelledWithoutEntries()
// iptal edilmiş belirteç → Cancelled == true, Entries boş, istisna yok
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TimelineCaptureDateTests`
Expected: derleme hatası — `ByCaptureDate` yok.

- [ ] **Step 3: Implement `ByCaptureDate`**

`new Scanner(new ScanOptions { Roots = { root }, IncludedKinds = { Photo, Raw, Video }, ExcludedFolderNames = SpecialFolderNames kopyası })`
ile `EnumerateFiles()`; `Scanner.Scan`'ı değil kendi `Parallel.For`'unu kullan (MaxDegreeOfParallelism 4) ve her
dosyada `read` çağrısını try/catch'le sar (hata → `Errors` "yol: ileti", lock'lu). `scanner.Errors` sonuca eklenir.
`Special`: kökün doğrudan altındaki özel adlı klasörlerde `FileClassifier` ile Photo/Raw/Video say (EXIF okumadan).
Iptal: `OperationCanceledException` yakalanır → `Cancelled = true`, `Entries` boş.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~Timeline`
Expected: 14 geçti.

- [ ] **Step 5: Commit**

```bash
git add FotoDuzenleyici.Core/Timeline.cs FotoDuzenleyici.Tests/TimelineCaptureDateTests.cs
git commit -m "Zaman çizelgesi: çekim tarihinden sayım"
```

---

### Task 3: ViewModels — tablo oluşturma

**Files:**
- Create: `FotoDuzenleyici.ViewModels/TimelineTable.cs`
- Test: `FotoDuzenleyici.Tests/TimelineTableTests.cs`

**Interfaces:**
- Consumes: `TimelineEntry` (Task 1).
- Produces:
  ```csharp
  public enum KindFilter { Photo, Raw, Video, All }
  public sealed record FolderCount(string Folder, int Count);
  public sealed record TimelineCell(int Year, int Month, int Count, long Size, int Photo, int Raw, int Video,
      int NotFromMetadata, IReadOnlyList<FolderCount> Folders, double Intensity)
  {
      public bool IsEmpty => Count == 0;
      public string Title { get; }      // "Kasım 2022" (tr-TR ay adı)
      public string Tooltip { get; }    // bkz. Step 1
  }
  public sealed record TimelineRow(int Year, int? EndYear, IReadOnlyList<TimelineCell> Months, int Total)
  {
      public bool IsCollapsed => EndYear is not null;   // Months boş
      public string Label { get; }      // "2022" ya da "2001 – 2015: boş"
  }
  public sealed record TimelineTable(IReadOnlyList<TimelineRow> Rows, int Total, long Size,
      DateOnly? First, DateOnly? Last, int FilledMonths, int EmptyMonths)
  {
      public string Summary { get; }    // bkz. Step 1
      public static TimelineTable Build(IEnumerable<TimelineEntry> entries, KindFilter filter);
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Build_PhotoFilter_CountsOnlyPhotos_ButCellKeepsKindBreakdown()
// 2022-11: 2 Photo (3+5 B), 1 Raw, 1 Video → cell.Count == 2, Size == 8, Photo 2, Raw 1, Video 1

[Fact] public void Build_AllFilter_CountsPhotoRawVideo()   // aynı veri, All → Count == 4

[Fact] public void Build_RowsSpanFirstToLastYear_TwelveCellsEach_WithYearTotal()
// 2021-07 ve 2022-11 → Rows: 2021, 2022; her birinde 12 hücre; Rows[1].Total == 2022 sayısı

[Fact] public void Build_SingleEmptyYear_IsNormalRow()          // 2019 ve 2021 dolu → 2020 normal satır, 12 boş hücre

[Fact] public void Build_TwoOrMoreEmptyYears_CollapseToOneRow()
// 1990-01 ve 2022-11 dolu → Rows: 1990, (1991–2021 toplanmış), 2022; Rows[1].Label == "1991 – 2021: boş"

[Fact] public void Build_EmptyMonths_CountedBetweenFirstAndLast_ExcludingCollapsedYears()
// 2022-10, 2022-12 dolu → FilledMonths 2, EmptyMonths 1

[Fact] public void Build_Intensity_IsSqrtOfShareOfMax_ZeroWhenEmpty()
// en büyük ay 100, diğeri 25 → 1.0 ve 0.5; boş hücre 0

[Fact] public void Build_Folders_GroupedByFolder_MostFirst()
// aynı ayda kök\A 3 dosya, kök\B 1 → Folders == [(A,3),(B,1)]

[Fact] public void Build_NotFromMetadata_CountsFileNameAndFileTimeSources()

[Fact] public void Build_NoEntriesForFilter_EmptyTable_NoDivideByZero()
// yalnız Video, filtre Photo → Rows boş, Total 0, First null, Summary == "Bu klasörde bu türde dosya bulunamadı."

[Fact] public void Summary_And_Tooltip_Text()
// Summary: "2.373 dosya · 26,7 GB · 2022 Ekim – 2023 Mart · 6 ay dolu, 0 ay boş" biçimi (tr-TR kültürüyle)
// Tooltip: "Kasım 2022 — 2 dosya · 8 B\nFoto 2 · Raw 1 · Video 1" ; Folders > 1 ise
// "\nKlasörler: A (3), B (1)" (en çok üç); NotFromMetadata > 0 ise "\n1 tanesinin tarihi dosya adından / dosya zamanından"
```

Testler `CultureInfo.CurrentCulture = new("tr-TR")` ile çalışır (ay adları ve ayraçlar için), sonunda geri alınır.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TimelineTableTests`
Expected: derleme hatası — `TimelineTable` yok.

- [ ] **Step 3: Implement `TimelineTable.cs`**

Süzgeç önce uygulanır (yıl aralığı, max, özet süzgece göre); hücredeki Photo/Raw/Video dağılımı süzgeçsiz tüm
kayıtlardan. Klasörlerde gösterim için tam yol tutulur (kısaltma sayfanın işi, Task 4 `ShortPath`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~TimelineTableTests`
Expected: 11 geçti.

- [ ] **Step 5: Commit**

```bash
git add FotoDuzenleyici.ViewModels/TimelineTable.cs FotoDuzenleyici.Tests/TimelineTableTests.cs
git commit -m "Zaman çizelgesi: süzgeçli yıl × ay tablosu"
```

---

### Task 4: ViewModels — sayfa, ayar, ana ekran kartı, kabuk

**Files:**
- Create: `FotoDuzenleyici.ViewModels/TimelineViewModel.cs`
- Modify: `FotoDuzenleyici.ViewModels/Settings.cs` (yeni alan), `HomeViewModel.cs`, `ShellViewModel.cs`
- Test: `FotoDuzenleyici.Tests/TimelineViewModelTests.cs`, `FotoDuzenleyici.Tests/TimelineShellTests.cs`

**Interfaces:**
- Consumes: Task 1–3; mevcut `Services`, `Fakes`/`FakeFolders`/`FakeShell`/`MemorySettings`, `ILongRunningPage`.
- Produces:
  ```csharp
  // AppSettings
  public string? LastTimelineFolder { get; set; }

  // HomeViewModel
  public event Action? TimelineRequested;
  [RelayCommand] void StartTimeline();          // → StartTimelineCommand

  public sealed partial class TimelineViewModel : ObservableObject, ILongRunningPage
  {
      public TimelineViewModel(Services s, AppSettings settings, string? startFolder = null);
      [ObservableProperty] string? folder;                 // ctor: aday sırası, var olmayanlar atlanır
      [ObservableProperty] TimelineMethod method;          // folder seçilince DefaultMethod
      [ObservableProperty] KindFilter filter;              // varsayılan Photo; değişince Table yeniden kurulur
      [ObservableProperty] bool isCounting;
      [ObservableProperty] int done; [ObservableProperty] int total;
      [ObservableProperty] TimelineResult? result;
      [ObservableProperty] TimelineTable? table;
      [ObservableProperty] bool stopped;
      [ObservableProperty] TimelineCell? selectedCell;     // çekim tarihinde çok klasörlü hücre tıklanınca
      public string MethodReason { get; }   // "Bu klasörde \"Yıl-Ay\" klasörü bulundu, bu yüzden Klasör adlarından seçildi." / "… bulunamadı, … Çekim tarihinden …"
      public IReadOnlyList<string> FooterLines { get; }
      public string ProgressText { get; }   // "1.240 / 2.373 dosya okundu"
      public string ShortPath(string folder);   // köke göre göreli; kökün kendisi "."
      public Task CountAsync();              // önceki sayımı iptal eder; Folder null ise hiçbir şey yapmaz
      [RelayCommand] void ChangeFolder();    // IFolderPicker → Folder, LastTimelineFolder kaydı (TrySave), Method = Default, sayım
      [RelayCommand] void Stop();            // iptal → Stopped = true, Table null
      [RelayCommand] void OpenCell(TimelineCell cell);
      [RelayCommand] void OpenFolder(string folder);   // IShell.OpenFolder
      [RelayCommand] void UseCaptureDate();            // Method = CaptureDate
      // ILongRunningPage: IsLocked false, MustConfirmClose false, RunningTask = son sayım, CloseQuestion "", RequestStop = Stop
  }

  // ShellViewModel: home.TimelineRequested → StartTimeline(): if (Locked) return; sayfa = new TimelineViewModel(_s, _settings, _startFolder); _ = vm.CountAsync();
  // _startFolder unutulmaz (salt okunur ekran). GoHome: CurrentPage TimelineViewModel ise önce RequestStop().
  ```

- [ ] **Step 1: Write the failing tests**

`TimelineViewModelTests` (geçici ağaçlar, `Fakes`):
```csharp
[Fact] public void Ctor_FolderOrder_StartFolder_ThenLastTimeline_ThenLastArchive()
[Fact] public void Ctor_SkipsMissingFolders()                 // LastTimelineFolder silinmiş → Recent[0].Archive
[Fact] public void Ctor_NoCandidate_FolderNull_CountDoesNothing()
[Fact] public async Task Count_FolderNames_BuildsPhotoTable()  // ay klasörlü ağaç → Method FolderNames, Table.Total == foto sayısı
[Fact] public async Task Count_UnorganizedFolder_DefaultsToCaptureDate_WithReason()
[Fact] public async Task FilterChange_RebuildsTable_WithoutRecount()   // Result aynı nesne kalır
[Fact] public async Task MethodChange_Recounts()
[Fact] public void ChangeFolder_SavesLastTimelineFolder_AndCounts()      // FakeFolders.Next; MemorySettings.Saves >= 1
[Fact] public void ChangeFolder_Cancelled_KeepsFolder()
[Fact] public async Task Stop_CancelsCount_SetsStopped_TableNull()
[Fact] public async Task FolderChangeDuringCount_OldResultDiscarded()    // Review Focus 2: ilk sayım kapıda beklerken klasör değişir; sonuç yeni klasöre ait
[Fact] public async Task OpenCell_FolderNames_OpensMonthFolder()        // FakeShell.Opened == [kök\2022-11]
[Fact] public async Task OpenCell_CaptureDate_SingleFolder_OpensIt_MultiFolder_SelectsCell()
[Fact] public async Task FooterLines_SpecialOutsideErrorsAndCopyNote()
// FolderNames + Outside 2 → "2 dosya Yıl-Ay klasörleri dışında"; Special → "_Tekrarlar: 1 dosya";
// CaptureDate → "Kopyalar ayıklanmaz; aynı fotoğrafın kopyaları ayrı sayılır." ; Errors 1 → "1 dosya ya da klasör okunamadı"
[Fact] public void LongRunningPage_NeverLocks_NeverAsks()
[Fact] public async Task VanishedFolder_DuringCount_NoCrash()           // Review Focus 1
```
Sayımı kapıda bekletmek için `TimelineViewModel` içinde `internal Action? BeforeCountForTest` (arka plan iş
parçacığında, sayım başlamadan) — `BackupFlowViewModel.BeforeScanForTest` deseniyle.

`TimelineShellTests`:
```csharp
[Fact] public void Home_StartTimeline_OpensTimelinePage_WithStartFolder()
[Fact] public void Home_StartTimeline_KeepsStartFolderForLaterHome()
[Fact] public async Task GoHome_DuringCount_StopsIt_AndNavigates()
[Fact] public void RequestClose_DuringCount_ClosesWithoutAsking()      // FakeDialogs.Asked boş, CloseDecision.Close
[Fact] public void SettingsJson_WithoutLastTimelineFolder_LoadsNull()   // JsonSettingsStore eski dosya
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~TimelineViewModelTests|FullyQualifiedName~TimelineShellTests"`
Expected: derleme hatası — `TimelineViewModel`, `StartTimelineCommand`, `LastTimelineFolder` yok.

- [ ] **Step 3: Implement**

Sayım `Task.Run` içinde (`ByFolders` / `ByCaptureDate`), ilerleme `Progress<(int,int)>` ile; başlarken
`(folder, method)` yakalanır, bitince hâlâ `Folder`/`Method` ile aynıysa ve iptal edilmediyse `Result` ve `Table`
atanır. `OnFolderChanged` yöntem varsayılanını ve `MethodReason`'ı günceller; `OnMethodChanged` ve
`ChangeFolder` sayımı başlatır (`_ = CountAsync()`); ctor sayım başlatmaz (kabuk başlatır).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: tüm takım geçer (503 + yeni testler).

- [ ] **Step 5: Commit**

```bash
git add FotoDuzenleyici.ViewModels FotoDuzenleyici.Tests
git commit -m "Zaman çizelgesi: sayfa mantığı, ana ekran kartı, kabuk geçişi"
```

---

### Task 5: WPF sayfası, ana ekran kartı, belgeler

**Files:**
- Create: `FotoDuzenleyici.App/Shell/TimelinePage.xaml`, `TimelinePage.xaml.cs`
- Modify: `FotoDuzenleyici.App/Shell/ShellWindow.xaml` (DataTemplate), `HomePage.xaml` (dördüncü kart),
  `FotoDuzenleyici.App/Shell/Converters.cs` (yoğunluk → fırça), `README.md`, `CHANGELOG.md` ([Yayınlanmamış] → Eklendi),
  `YOL_HARITASI.md` (Aşama 3 "Ay ay zaman çizelgesi" → ✅ (Yayınlanmamış))

**Interfaces:**
- Consumes: Task 4 `TimelineViewModel` ve `TimelineTable` özellikleri, `StartTimelineCommand`.

- [ ] **Step 1: Sayfa**

Üst satır: "Klasör:" + `Folder` (Mono, kısaltmalı) + **Değiştir…** (`ChangeFolderCommand`); sağda Foto/Raw/Video/Hepsi
dört seçmeli düğme (`Filter`). Altında iki RadioButton (`Method`) ve `MethodReason` (Small). İlerleme: `IsCounting`
iken ProgressBar (`Done`/`Total`), `ProgressText`, **Durdur** (`StopCommand`). `Stopped` iken "Sayım durduruldu" +
**Yeniden say**. `Table.Summary`; tablo: ay başlıkları satırı + `Table.Rows` için ItemsControl; normal satırda yıl,
12 hücreli `UniformGrid`, yıl toplamı; toplanmış satırda tek ince `Label`. Hücre: `Border` (köşe 4), arka plan =
vurgu rengi × (0.15 + 0.85 × `Intensity`) opaklık, boşsa taralı/soluk fırça; `Count` (boşsa metin yok); `ToolTip`
= `Tooltip`; tıklama → `OpenCellCommand` (boş hücrede devre dışı). `SelectedCell` doluyken tablonun altında klasör
listesi paneli: her satır `ShortPath(Folder)` + sayı + **Aç** (`OpenFolderCommand`). En altta `FooterLines` ve,
`Result.Outside > 0` ise **Çekim tarihinden say** (`UseCaptureDateCommand`). Açık ve koyu temada okunur.

- [ ] **Step 2: Kabuk ve ana ekran**

`ShellWindow.xaml`'a `<DataTemplate DataType="{x:Type vm:TimelineViewModel}"><local:TimelinePage /></DataTemplate>`.
`HomePage.xaml`: kartlar 2 × 2 (`UniformGrid Columns="2"`, kart kenar boşlukları buna göre); dördüncü kart
"Zaman çizelgesi", simge `CalendarLtr24`, metin "Hangi ayda ne kadar fotoğraf var; klasör düzenli olmasa da.",
**Aç →** (`StartTimelineCommand`).

- [ ] **Step 3: Derle ve görsel denetim**

Run: `dotnet build -c Release`
Expected: 0 hata.
Scratchpad'deki görüntü alma aracına `timeline` kipi ekle (geçici örnek ağaçta `TimelineViewModel` +
`CountAsync` beklenir, sayfa PNG'ye çizilir) ve `home` kipini yeniden çalıştır; açık ve koyu temada
ana ekran ile zaman çizelgesi PNG'lerini incele: dört kart sığıyor, hücre sayıları okunuyor, boş aylar ayırt
ediliyor, alt satırlar görünüyor. Örnek ağaç `D:\1- HERKES\…` altında değil, geçici klasörde.

- [ ] **Step 4: Belgeler ve tüm takım**

README'ye "Zaman çizelgesi" paragrafı (iki yöntem, salt okunur), CHANGELOG `[Yayınlanmamış] / Eklendi` maddesi,
YOL_HARITASI satırı.
Run: `dotnet test`
Expected: tüm takım geçer.

- [ ] **Step 5: Commit**

```bash
git add FotoDuzenleyici.App README.md CHANGELOG.md YOL_HARITASI.md
git commit -m "Zaman çizelgesi: ekran, ana ekran kartı, belgeler"
```
