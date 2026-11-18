# Ay görünümü ve hızlı ayıklama — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clicking a filled month in Zaman çizelgesi opens an in-app month view (Izgara / Takvim) with thumbnails, a big one-by-one Ayıkla view for marking photos, and a Gözden geçir screen that sends marked files (and their RAW pairs) to the Recycle Bin — or to `_Ayiklananlar` when the bin can't take them.

**Architecture:** Core gets a month reader (re-reads the cell's folders on entry), a discarder (plans recycle-vs-move per file, sends, journals) behind an `IRecycleBin` seam, and a generalized `ThumbnailLoader`. ViewModels get `MonthViewModel` (load, grid/calendar, marks, triage, review, discard) hosted *inside* `TimelineViewModel` as a `Month` property, so the timeline table survives the round trip and is patched in place after a discard. App gets `MonthView.xaml` (three states: browse, Ayıkla, Gözden geçir) shown over the timeline page when `Month` is set.

**Tech Stack:** .NET 8, WPF, WPF-UI, CommunityToolkit.Mvvm, xUnit. No new packages.

**Spec:** `docs/superpowers/specs/2026-11-18-ay-gorunumu-ayikla-design.md`

## Global Constraints

- Never delete permanently; never overwrite an existing file; nothing leaves without the Gözden geçir button.
- No network access; no new NuGet packages.
- Screen logic lives in `FotoDuzenleyici.ViewModels` with tests; App holds only XAML and thin glue.
- Thumbnails: max 4 decodes at once, max 600 cached, invisible requests cancelled; grid realizes only visible rows.
- Special folders (`Timeline.SpecialFolderNames`) are skipped exactly as the timeline skips them.
- UI copy (verbatim from spec): "← Zaman çizelgesi", "▦ Izgara", "📅 Takvim", "Ayıkla ▶", "Gezgin'de aç", "Gözden geçir", "🗑 Atılacaklar: N · boyut", "RAW'ı tut", "+RAW", "RAW kalır", "Başka aydan", "Vazgeç", "N dosya atıldı, M dosya atılamadı", "N dosya atılmak üzere işaretli. Bırakıp çıkılsın mı?", review note "Dosyalar Windows'un Geri Dönüşüm Kutusu'na gider; oradan 'Geri yükle' ile dönebilirsin. Disk yeri, Geri Dönüşüm Kutusu'nu boşaltınca açılır."
- Month/day names use `CultureInfo.CurrentCulture` (tests set `tr-TR`), as `TimelineTable` does.
- Commits: no "Co-Authored-By: Claude" line, no "Generated with Claude Code" (project memory overrides harness attribution).
- Work on branch `ay-gorunumu`; merge to main and push when the final review is clean.

## Review Focus

1. Recycle Bin refuses silently (bin disabled, file bigger than bin, removable / network / UNC path) → file must go to `_Ayiklananlar` or stay marked as "atılamadı", never vanish permanently. Pinned by Task 3 tests `Plan_NoBin_RoutesToRejects`, `Run_SendReturnsFalse_CountsFailed_FileStays`.
2. A JPG with "RAW'ı tut" is discarded → its RAW stays on disk and is not in the size total. Pinned by Task 5 `KeepRaw_RawNotDiscarded_NotInSize`.
3. Leaving the month (Geri, Ana ekran, Geri Al, window close) with marks → one question, "Hayır" keeps the user in place. Pinned by Task 6 `GoHome_WithMarks_AsksAndStays`, `RequestClose_WithMarks_AsksAndStays`.
4. Name collision in `_Ayiklananlar\Ayikla\…` (same relative path discarded twice across runs) → second file gets a free name, nothing overwritten. Pinned by Task 3 `Plan_TargetExists_PicksFreeName`.
5. Timeline numbers after discard: cell count/size drop by exactly the discarded files (RAW pair included under "Hepsi"/"Raw" filters), `_Ayiklananlar` footer rises by moved files. Pinned by Task 6 `Discard_UpdatesCellAndFooter_WithoutRecount`.

---

### Task 1: Month reader (Core)

**Files:**
- Create: `FotoDuzenleyici.Core/MonthReader.cs`
- Test: `FotoDuzenleyici.Tests/MonthReaderTests.cs`

**Interfaces:**
- Consumes: `Timeline.SpecialFolderNames`, `Timeline.Walk` (internal), `Scanner.ReadFile`, `TimelineMethod`.
- Produces:
  ```csharp
  public sealed class MonthResult { public List<MediaFile> Files { get; } = new(); public List<string> Errors { get; } = new(); public bool Cancelled { get; set; } }
  public static class MonthReader {
      public static MonthResult Read(TimelineMethod method, int year, int month, IReadOnlyList<string> folders,
          IProgress<(int done, int total)>? progress = null, CancellationToken ct = default, Func<string, MediaFile>? read = null);
  }
  ```

Rules: `FolderNames` → every counted file under each folder, recursively, via `Timeline.Walk` (keeps files whose date is another month — the VM groups them as "Başka aydan"). `CaptureDate` → only the **direct** files of each folder (the timeline's `Folder` is the file's own directory) whose `Taken` falls in year-month. Reads in `Parallel.For`, MaxDOP 4, per-file try/catch into `Errors`; cancel → `Files` cleared, `Cancelled = true`. Duplicate folder paths are read once.

- [ ] **Step 1: Write the failing tests** (use an injected `read` that maps file name → date, like `TimelineCaptureDateTests`)
  - `FolderNames_ReadsRecursively_KeepsOtherMonthFiles`: `2026-08\2026-08-Foto\a.jpg` (Aug 3), `2026-08\2026-08-Foto\x\b.jpg` (Jul 30) → both returned.
  - `FolderNames_SkipsSpecialSubfolders`: `2026-08\_Tekrarlar\c.jpg` not returned.
  - `CaptureDate_DirectFilesOnly_FiltersMonth`: folder has `a.jpg` (Aug), `b.jpg` (Sep), `sub\c.jpg` (Aug) → only `a.jpg`.
  - `ReadError_GoesToErrors_OthersContinue`.
  - `Cancel_ClearsFiles_SetsCancelled`.
- [ ] **Step 2: Run** `dotnet test --filter MonthReaderTests` — Expected: FAIL (type missing).
- [ ] **Step 3: Implement `MonthReader.Read`** in `FotoDuzenleyici.Core/MonthReader.cs`. `Walk` takes a `TimelineResult` for errors: pass a scratch `TimelineResult` and copy its `Errors`.
- [ ] **Step 4: Run** the same filter — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Ay okuyucu: hücrenin klasörlerini aya girerken okur"`

### Task 2: Generalize the thumbnail loader (Core + App)

**Files:**
- Modify: `FotoDuzenleyici.Core/ThumbnailLoader.cs`, `FotoDuzenleyici.Core/SimilarGroupState.cs` (SimilarRow), `FotoDuzenleyici.App/Shell/ThumbTracker.cs`
- Test: `FotoDuzenleyici.Tests/ThumbnailLoaderTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IThumbTarget { MediaFile File { get; } ImageSource? Thumb { get; set; } bool ThumbFailed { get; set; } }
  // ThumbnailLoader.Request(IThumbTarget), Cancel(IThumbTarget); SimilarRow : IThumbTarget
  // ThumbTracker.Attach(FrameworkElement) works for any DataContext that is IThumbTarget
  ```

- [ ] **Step 1: Write the failing test** `Request_AcceptsAnyThumbTarget`: a test-only `class Target : IThumbTarget` gets its `Thumb` set by the loader with a fake decode.
- [ ] **Step 2: Run** `dotnet test --filter ThumbnailLoaderTests` — Expected: FAIL (compile).
- [ ] **Step 3: Implement**: replace `SimilarRow` with `IThumbTarget` in loader fields/methods; `SimilarRow` implements it; `ThumbTracker` casts to `IThumbTarget`.
- [ ] **Step 4: Run** `dotnet test` (whole suite) — Expected: PASS, existing loader and similar tests unchanged.
- [ ] **Step 5: Commit** `git commit -m "Küçük resim yükleyici her satır türüyle çalışır"`

### Task 3: Discarder and recycle-bin seam (Core)

**Files:**
- Create: `FotoDuzenleyici.Core/Discarder.cs`, `FotoDuzenleyici.Core/WindowsRecycleBin.cs`
- Modify: `FotoDuzenleyici.Core/CleanSession.cs` (make `FreeName` `internal static`)
- Test: `FotoDuzenleyici.Tests/DiscarderTests.cs`

**Interfaces:**
- Consumes: `Executor` (move mode + journal), `CleanSession.RejectsFolder`, `CleanSession.FreeName`, `JournalEntry`.
- Produces:
  ```csharp
  public interface IRecycleBin {
      /// false → this file would not be recycled (no bin on drive, bin off, too big, unknown).
      bool CanRecycle(string path, long size);
      /// Sends to the bin; true only if the file is gone afterwards. Must never delete permanently without Windows asking.
      bool Send(string path);
  }
  public enum DiscardRoute { RecycleBin, MoveToRejects }
  public sealed record DiscardStep(MediaFile File, DiscardRoute Route, string? Target);
  public sealed class DiscardResult { public List<DiscardStep> Done { get; } = new(); public List<(DiscardStep Step, string Error)> Failed { get; } = new(); public string? MoveJournal, RecycleJournal; }
  public static class Discarder {
      public const string SubFolder = "Ayikla";
      public static List<DiscardStep> Plan(IEnumerable<MediaFile> files, string root, IRecycleBin bin);
      public static DiscardResult Run(IReadOnlyList<DiscardStep> steps, string root, IRecycleBin bin, string logDir,
          IProgress<(int done, int total, string current)>? progress = null, CancellationToken ct = default);
  }
  public sealed class WindowsRecycleBin : IRecycleBin { public static WindowsRecycleBin Instance { get; } }
  ```

Rules:
- `Plan`: route = `bin.CanRecycle(path, size) ? RecycleBin : MoveToRejects`. Move target = `<root>\_Ayiklananlar\Ayikla\<path relative to root>`, name made free with `FreeName` (against disk and other planned targets). Files outside `root` → `MoveToRejects` is impossible: such files get `RecycleBin` only if `CanRecycle`, else are left out of the plan (and the VM reports them as atılamadı).
- *Ruling:* mounted volumes inside root never reach here (`Timeline.Walk`/`Scanner` skip reparse points), so `_Ayiklananlar` is always under root; the spec's "other volume" clause needs no code.
- `Run`: moves → one `new Executor(<root>\_Ayiklananlar\_Gunluk\ayikla-yyyyMMdd-HHmmss.jsonl).Run(items, move: true)` (Geri Al and unfinished-run detection work as for Temizle). Recycles → for each: `bin.Send(path)`; journal lines (`JournalEntry` with `Action = "Discard"`, `State = "Recycled" | "Failed"`) appended to `<logDir>\ayikla-yyyyMMdd-HHmmss.jsonl`. *Ruling:* a mixed run writes two journals (moves in the archive, recycles in app data) — spec's "single journal" wording vs. not creating `_Ayiklananlar` for recycle-only runs; cost: two files to look at for one run.
- `WindowsRecycleBin.CanRecycle`: false for UNC paths and `DriveType` Removable / Network / CDRom / NoRootDirectory; false if `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\{volume GUID}` has `NukeOnDelete = 1`, or `MaxCapacity` (MB) minus current bin size (`SHQueryRecycleBin`) is below `size`; true otherwise (lookup failure → true, `Send` is still safe).
- `WindowsRecycleBin.Send`: `SHFileOperation` `FO_DELETE` with `FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_SILENT`; returns `!File.Exists(path)`. `FOF_WANTNUKEWARNING` makes Windows ask before any permanent delete; a "Hayır" leaves the file and counts as failed.

- [ ] **Step 1: Write the failing tests** (fake `IRecycleBin` in the test file: `Func<string,long,bool> Can`, `Func<string,bool> Send`, records calls)
  - `Plan_BinAccepts_RoutesToRecycle`
  - `Plan_NoBin_RoutesToRejects`: target = `root\_Ayiklananlar\Ayikla\2026-08\2026-08-Foto\a.jpg`.
  - `Plan_TargetExists_PicksFreeName`: target file pre-created → planned name differs, existing file untouched.
  - `Plan_TwoSameNames_GetDistinctTargets`.
  - `Run_Recycle_CallsSend_WritesRecycleJournal_NoRejectsFolder`: `_Ayiklananlar` does not exist afterwards; log in `logDir`.
  - `Run_Move_MovesFile_JournalInGunluk_UndoRestores`: `Executor.Undo(result.MoveJournal)` puts the file back.
  - `Run_SendReturnsFalse_CountsFailed_FileStays`.
  - `Run_Mixed_DoneHasBoth`.
- [ ] **Step 2: Run** `dotnet test --filter DiscarderTests` — Expected: FAIL (types missing).
- [ ] **Step 3: Implement** `Discarder` and `WindowsRecycleBin` (P/Invoke `SHFileOperationW` with `SHFILEOPSTRUCT`, `pFrom` double-null-terminated; `SHQueryRecycleBinW`; `GetVolumeNameForVolumeMountPointW` for the GUID).
- [ ] **Step 4: Run** the filter, then `dotnet test` — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Ayıklananları atma: Geri Dönüşüm Kutusu ya da _Ayiklananlar, günlükle"`

### Task 4: MonthViewModel — load, Izgara, Takvim (ViewModels)

**Files:**
- Create: `FotoDuzenleyici.ViewModels/MonthPhoto.cs`, `FotoDuzenleyici.ViewModels/MonthViewModel.cs`
- Modify: `FotoDuzenleyici.ViewModels/Settings.cs` (`public string MonthView { get; set; } = "Grid";`), `FotoDuzenleyici.ViewModels/Services.cs` (add `IRecycleBin? Bin = null` as last record parameter; `public IRecycleBin RecycleBin => Bin ?? WindowsRecycleBin.Instance;`), `FotoDuzenleyici.Tests/UiFakes.cs` (`FakeBin`, `Fakes.Bin`)
- Test: `FotoDuzenleyici.Tests/MonthViewModelTests.cs`

**Interfaces:**
- Consumes: `MonthReader.Read`, `RawJpgPairs.Index`, `KindFilter`, `IThumbTarget`, `Sizes.Text`.
- Produces:
  ```csharp
  public sealed partial class MonthPhoto : ObservableObject, IThumbTarget {
      public MediaFile File { get; } public IReadOnlyList<MediaFile> Raws { get; }   // RAW pair(s) travelling with this photo
      public DateTime Taken { get; } public bool OtherMonth { get; } public bool HasRaw => Raws.Count > 0;
      [ObservableProperty] bool marked; [ObservableProperty] bool keepRaw;
      public long DiscardSize { get; }   // File.Size + (KeepRaw ? 0 : Raws sizes)
      public string RawLabel { get; }    // "+RAW" | "RAW kalır"
  }
  public static class MonthItems { public static List<MonthPhoto> Build(IReadOnlyList<MediaFile> files, KindFilter filter, int year, int month); }
  public sealed record DayGroup(DateOnly? Day, string Header, IReadOnlyList<MonthPhoto> Photos);  // Day null → "Başka aydan"
  public sealed record CalendarDay(int Day, bool InMonth, int Count, IReadOnlyList<MonthPhoto> Preview, int More); // Preview ≤ 4
  public enum MonthViewMode { Grid, Calendar }
  public sealed partial class MonthViewModel : ObservableObject {
      public MonthViewModel(Services s, AppSettings settings, string root, TimelineMethod method, int year, int month,
          IReadOnlyList<string> folders, KindFilter filter, Func<string, MediaFile>? read = null);
      public Task LoadAsync();  [ObservableProperty] bool isLoading;  public string ProgressText { get; }
      public string Title { get; }  // "Ağustos 2026"
      public string Summary { get; } // "{n} dosya · {Sizes.Text}"
      public IReadOnlyList<MonthPhoto> Photos { get; } public IReadOnlyList<DayGroup> Days { get; }
      public IReadOnlyList<IReadOnlyList<CalendarDay>> Weeks { get; }  // Monday-first, 7 per row
      [ObservableProperty] MonthViewMode mode;  [ObservableProperty] DateOnly? scrollTarget;
      public IReadOnlyList<string> Folders { get; }  public IReadOnlyList<string> Errors { get; }
      [RelayCommand] void OpenDay(CalendarDay d);  [RelayCommand] void OpenFolder(string path);
      [RelayCommand] void Back();  public event Action? BackRequested;  [RelayCommand] void Stop();
  }
  ```

Rules for `MonthItems.Build`: Photo → photos, each with its pair RAWs attached; Raw → every RAW, nothing attached; Video → videos; All → photos (RAWs attached) + RAWs without a photo pair + videos. Sorted by `Taken`, then path. `OtherMonth` = `Taken` not in year-month (or null). Days: group by `Taken.Date`, header `"{d} {MonthName} {DayName} · {count}"` (e.g. "15 Ağustos Cumartesi · 38"); `OtherMonth` photos form the last group, header `"Başka aydan · {count}"`. `Mode` initial from `settings.MonthView`; on change save with `TrySave`. `OpenDay` (only `Count > 0`) → `Mode = Grid`, `ScrollTarget = that date`.

- [ ] **Step 1: Write the failing tests** (tr-TR culture; injected `read`)
  - `Build_Photo_AttachesRawPair_HidesRaw`, `Build_Raw_ShowsAllRaws_NoAttach`, `Build_All_LoneRawShown_PairedRawHidden`.
  - `Days_GroupedAndHeaded`: header `"15 Ağustos Cumartesi · 2"` for 2026-08-15.
  - `OtherMonth_LastGroup_BaskaAydan`.
  - `Weeks_MondayFirst_August2026`: first row has `InMonth=false` Mon–Fri, day 1 on Saturday; day 15 `Count` correct; `Preview.Count ≤ 4`, `More = Count − 4`.
  - `OpenDay_SwitchesToGrid_SetsScrollTarget`.
  - `Mode_Remembered_InSettings`.
  - `Load_ShowsTitleAndSummary` (`"Ağustos 2026"`, `"3 dosya · …"`).
  - `Stop_DuringLoad_Cancels`.
- [ ] **Step 2: Run** `dotnet test --filter MonthViewModelTests` — Expected: FAIL.
- [ ] **Step 3: Implement** the types above; load on a background task with the timeline's generation-counter pattern; progress monotonic.
- [ ] **Step 4: Run** filter + full suite — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Ay görünümü: yükleme, Izgara ve Takvim"`

### Task 5: Marking, Ayıkla and Gözden geçir (ViewModels)

**Files:**
- Modify: `FotoDuzenleyici.ViewModels/MonthViewModel.cs`
- Test: `FotoDuzenleyici.Tests/MonthTriageTests.cs`

**Interfaces:**
- Consumes: `Discarder.Plan/Run`, `Services.RecycleBin`, `DiscardStep`, `DiscardRoute`.
- Produces (on `MonthViewModel`):
  ```csharp
  [RelayCommand] void ToggleMark(MonthPhoto p);
  public int MarkedCount { get; } public long MarkedSize { get; } public bool HasMarked { get; }
  public string MarkedText { get; }               // "🗑 Atılacaklar: 5 · 42 MB" (photos marked; size incl. RAWs not kept)
  // Ayıkla
  [ObservableProperty] bool isTriage; [ObservableProperty] MonthPhoto? current; public string Position { get; } // "58 / 140"
  [RelayCommand] void StartTriage(MonthPhoto? from);  [RelayCommand] void Next(); [RelayCommand] void Previous();
  [RelayCommand] void MarkAndNext();   // Delete: toggle current, then Next
  [RelayCommand] void DiscardAndNext();// drag left: Marked = true, then Next
  [RelayCommand] void KeepAndNext();   // drag right: Marked = false, then Next
  [RelayCommand] void ToggleKeepRaw(MonthPhoto? p); [RelayCommand] void CloseTriage();
  // Gözden geçir
  public sealed record ReviewLine(MonthPhoto Photo, DiscardRoute Route, bool RawToRejects);
  [ObservableProperty] bool isReview; public IReadOnlyList<ReviewLine> ReviewLines { get; }
  public string ReviewTotal { get; }      // "{files} dosya · {size}" — files = photos + RAWs not kept
  public string DiscardButtonText { get; } public bool HasRejectRoute { get; }
  [RelayCommand] void OpenReview(); [RelayCommand] void Unmark(MonthPhoto p); [RelayCommand] void CancelReview();
  [RelayCommand] Task Discard();  [ObservableProperty] bool isDiscarding; public Task? DiscardTask { get; }
  [ObservableProperty] string? resultMessage;
  public IReadOnlyList<(MediaFile File, bool Moved)> Discarded { get; }   // accumulated across runs, for the timeline patch
  public string? LeaveQuestion { get; }   // null when nothing marked
  ```

Rules:
- `Next`/`Previous` clamp at the ends (no wrap). `StartTriage(null)` starts at the first photo. Double-click in grid → `StartTriage(photo)`.
- `OpenReview` plans every marked photo's file plus its RAWs unless `KeepRaw`, via `Discarder.Plan`. `DiscardButtonText`: all recycle → `"{n} dosyayı Geri Dönüşüm Kutusu'na at"`; all move → `"{n} dosyayı _Ayiklananlar'a taşı"`; mixed → `"{a} dosyayı Geri Dönüşüm Kutusu'na at, {b} dosyayı _Ayiklananlar'a taşı"` (counts are files). `HasRejectRoute` drives the yellow warning; a line whose photo or RAW moves shows a "_Ayiklananlar" tag.
- In review, clicking a line's RAW label toggles `KeepRaw` and re-plans (*Yapıcı kararı* in spec).
- `Discard`: runs `Discarder.Run` on a background task (log dir = `Path.Combine(Path.GetDirectoryName(ReviewStore.DefaultPath)!, "Ayikla")`, injectable via ctor for tests); afterwards removes done photos from `Photos`/`Days`/`Weeks`, clears their marks, keeps failed ones marked, sets `ResultMessage = "{done} dosya atıldı, {failed} dosya atılamadı"` (drop the second clause when 0), leaves review.
- `LeaveQuestion = $"{MarkedCount} dosya atılmak üzere işaretli. Bırakıp çıkılsın mı?"` when `MarkedCount > 0`. `Back()` asks it via `Dialogs.Confirm` first; "no" → stays. `Back()` is ignored while `IsDiscarding`.

- [ ] **Step 1: Write the failing tests** (`FakeBin` from Task 4; real temp files)
  - `ToggleMark_UpdatesCountAndText`.
  - `MarkAndNext_TogglesAndAdvances`, `Next_AtEnd_Stays`, `DiscardAndNext_Marks`, `KeepAndNext_Unmarks`.
  - `KeepRaw_RawNotDiscarded_NotInSize`: JPG+CR3 marked, KeepRaw → after `Discard`, CR3 exists, JPG gone, `MarkedSize` before was JPG only.
  - `MarkedJpg_TakesRawAlong`.
  - `Review_ButtonText_AllRecycle`, `Review_ButtonText_Mixed`, `Review_NoBin_HasRejectRoute`.
  - `Unmark_InReview_RemovesLine`.
  - `Discard_RemovesDonePhotos_KeepsFailedMarked_Message`: `"1 dosya atıldı, 1 dosya atılamadı"`.
  - `Back_WithMarks_AsksAndStays`, `Back_NoMarks_RaisesBackRequested`.
- [ ] **Step 2: Run** `dotnet test --filter MonthTriageTests` — Expected: FAIL.
- [ ] **Step 3: Implement** on `MonthViewModel`.
- [ ] **Step 4: Run** filter + full suite — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Ayıkla ve Gözden geçir: işaretleme, RAW'ı tut, atma"`

### Task 6: Timeline and shell integration (ViewModels)

**Files:**
- Create: `FotoDuzenleyici.ViewModels/ILeaveGuard.cs`
- Modify: `FotoDuzenleyici.ViewModels/TimelineViewModel.cs`, `FotoDuzenleyici.ViewModels/ShellViewModel.cs`
- Test: `FotoDuzenleyici.Tests/MonthTimelineTests.cs`; update `TimelineViewModelTests` cases that assert the old `OpenCell` → Gezgin behaviour.

**Interfaces:**
- Produces:
  ```csharp
  public interface ILeaveGuard { string? LeaveQuestion { get; } }
  // TimelineViewModel : ILeaveGuard;  [ObservableProperty] MonthViewModel? month;
  ```

Rules:
- `OpenCell(cell)` (non-empty) → `Month = new MonthViewModel(_s, _settings, Result.Root, Result.Method, cell.Year, cell.Month, cell.Folders.Select(f => f.Folder).ToList(), Filter)`, then `_ = Month.LoadAsync()`. The old folder-list panel (`SelectedCell`, `SelectedFolders`, `CloseFolders`) is removed; "Gezgin'de aç" lives in the month view's ⋯ menu (`Month.Folders`, `OpenFolderCommand`).
- On `Month.BackRequested`: patch, then `Month = null`. Patch = for each `Discarded` file remove one `Result.Entries` item with the cell's Year/Month, same `Kind` and `Size`, whose `Folder` is the file's directory (CaptureDate) or an ancestor of it (FolderNames); moved files add to `Result.Special["_Ayiklananlar"]`; rebuild `Table` and `FooterLines`. No recount.
- `TimelineViewModel` as `ILongRunningPage`: `IsLocked`/`MustConfirmClose` = `Month?.IsDiscarding == true`; `RunningTask` = `Month?.DiscardTask` while discarding; `RequestStop` also stops the month load.
- `ILeaveGuard.LeaveQuestion` = `Month?.LeaveQuestion`.
- `ShellViewModel`: `GoHome`, `Undo`, `StartBackup` → if `CurrentPage is ILeaveGuard { LeaveQuestion: { } q }` and `!Dialogs.Confirm("Ay görünümü", q)` → return. `RequestClose` → same check before its existing logic; "no" → `CloseDecision.Stay`.

- [ ] **Step 1: Write the failing tests**
  - `OpenCell_OpensMonth_WithCellFoldersAndFilter`.
  - `Back_ClosesMonth_TableKept`.
  - `Discard_UpdatesCellAndFooter_WithoutRecount`: FolderNames archive, 3 photos in 2026-08; discard 1 recycled + 1 moved → cell count 1, footer has `"_Ayiklananlar: 1 dosya"`, `BeforeCountForTest` not called again.
  - `GoHome_WithMarks_AsksAndStays`, `GoHome_WithMarks_Yes_Leaves`.
  - `RequestClose_WithMarks_AsksAndStays`.
  - `WhileDiscarding_IsLocked`.
- [ ] **Step 2: Run** `dotnet test --filter "MonthTimelineTests|TimelineViewModelTests|TimelineShellTests"` — Expected: FAIL on new tests.
- [ ] **Step 3: Implement**; update old `OpenCell` tests to the new behaviour (ledger the change).
- [ ] **Step 4: Run** full suite — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Zaman çizelgesinden aya giriş; dönüşte sayılar yerinde güncellenir"`

### Task 7: Month view UI (App)

**Files:**
- Create: `FotoDuzenleyici.App/Shell/MonthView.xaml` (+ `.cs`), `FotoDuzenleyici.App/Shell/LargeImage.cs` (decode helper with next-image preload)
- Modify: `FotoDuzenleyici.App/Shell/TimelinePage.xaml` (host `MonthView` over the table when `Month != null`; remove folder-list panel), `FotoDuzenleyici.App/Shell/ShellResources.xaml` if converters are needed
- Test: manual render with the scratch harness (`render\Program.cs` + `Timeline.cs`), light and dark.

Layout (matches the approved mockups `.superpowers/brainstorm/1266-1794978897/content/ay-gorunumu-v2.html`, `ayikla.html`, `gozden-gecir.html`):
- **Top bar:** "← Zaman çizelgesi" (`BackCommand`), Title, Summary, segmented "▦ Izgara" / "📅 Takvim" bound to `Mode`, "Ayıkla ▶" (`StartTriageCommand`), ⋯ menu with "Gezgin'de aç" (one item, or a submenu of `Folders`).
- **Izgara:** day headers + thumbnail tiles; one `ListBox`/`ItemsControl` over a flattened row list or grouping with `VirtualizingPanel.IsVirtualizingWhenGrouping` + `ui:VirtualizingWrapPanel` (as `FlawedSection.xaml`); tiles use `ThumbTracker.Attach`; marked tile 45% opacity with red ✕; Delete / right-click "Atılsın" → `ToggleMarkCommand`; double-click → `StartTriageCommand`; `ScrollTarget` scrolls its day header into view.
- **Takvim:** 7-column grid, Pzt…Paz header, out-of-month and empty days faded, up to 4 mini thumbs + "+N"; click → `OpenDayCommand`.
- **Ayıkla:** big image (decoded to the window width, next one preloaded), header "dd MMMM yyyy · HH:mm", file name, size, `Position`; buttons "🗑 Atılsın (Delete)", "✓ Kalsın", "RAW'ı tut" (visible when `Current.HasRaw`, toggled state); bottom strip of thumbnails with ✕ marks; keys ← → Delete Esc; mouse drag: horizontal drag beyond 25% of image width → left `DiscardAndNext`, right `KeepAndNext`, image follows the pointer and snaps back below threshold. Videos: thumbnail or video icon + "Varsayılan uygulamada aç" (`Shell.OpenFile`).
- **Alt şerit** (when `HasMarked`): `MarkedText`, "Gözden geçir", red "Geri Dönüşüm Kutusu'na at" → both `OpenReviewCommand`.
- **Gözden geçir:** thumbnails with day label and `RawLabel` tag (clickable → `ToggleKeepRawCommand`), click thumbnail → `UnmarkCommand`; `ReviewTotal`; the spec's note text; yellow warning when `HasRejectRoute`; "Vazgeç" and red `DiscardButtonText` button; progress while `IsDiscarding`; `ResultMessage` shown in the browse view afterwards.

- [ ] **Step 1: Build** `dotnet build -c Release` — Expected: 0 errors, 0 warnings.
- [ ] **Step 2: Render check** with the harness on a sample month folder (≥ 60 generated JPEGs over several days incl. one JPG+RAW pair): grid, calendar, Ayıkla, Gözden geçir, light and dark. Expected: matches mockups, no clipped text.
- [ ] **Step 3: Scroll check**: 1,000-file sample month scrolls without stalls; thumbnails fill in.
- [ ] **Step 4: Run** `dotnet test` — Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Ay görünümü ekranları: Izgara, Takvim, Ayıkla, Gözden geçir"`

### Task 8: Docs

**Files:** `README.md` (Zaman çizelgesi paragraph: aya girme, Ayıkla, Geri Dönüşüm Kutusu), `CHANGELOG.md` ([Yayınlanmamış] → Eklendi), `YOL_HARITASI.md` (item ✅ Yayınlanmamış).

- [ ] **Step 1:** Write the three edits.
- [ ] **Step 2: Run** `dotnet test` — Expected: PASS.
- [ ] **Step 3: Commit** `git commit -m "Belgeler: ay görünümü ve hızlı ayıklama"`
