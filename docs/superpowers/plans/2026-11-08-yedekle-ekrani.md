# Yedekle Ekranı Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Yeni arayüze, kopyalamadan önce özet gösteren Yedekle ekranını (yedekle, doğrula, onar) eklemek.

**Architecture:** `ArchiveBackup`'a yalnız sayan `Preview` eklenir; `Run` ile aynı tarama kodunu paylaşır. Ekran mantığı tek sayfalık `BackupFlowViewModel` durum makinesidir. Kabuk, uzun süren sayfaları `ILongRunningPage` arayüzüyle tanır.

**Tech Stack:** C# / WPF .NET 8, WPF-UI 4.2.1, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-08-yedekle-ekrani-design.md`

## Global Constraints

- Arşive yalnız okumak için dokunulur; `Preview` hiçbir yere yazmaz.
- Yer yetmiyorsa ya da klasörler iç içeyse kopyalama başlatılamaz.
- Arayüz metinleri Türkçe; spec'te geçen metinler aynen kullanılır.
- Commit'lere `Co-Authored-By: Claude` eklenmez. `.superpowers/` izlenmez.
- Kullanıcının `D:\1- HERKES\001 - İSMAİL HAKKI BAŞOĞLU` arşivinde hiçbir şey çalıştırılmaz.
- Test komutu: `dotnet test --nologo -v q`. Görev testleri için `--filter FullyQualifiedName~<Sınıf>`.

## Review Focus

1. Yedek diski kopyalama sırasında çıkarılır: uygulama çökmez, Finished'da motorun hata satırları görünür (Görev 2, `Run_DiskGone_ShowsErrors`).
2. Özetten sonra Geri ile klasör değiştirilir: eski özet kullanılmaz (Görev 2, `FolderChange_DropsPreview`).
3. Başlat'a iki kez basılır: ikinci iş başlamaz (Görev 2, `Start_Twice_RunsOnce`).
4. Boş alan ölçülemeyen hedef (ağ yolu, `long.MaxValue`): "ölçülemedi" yazar, engellemez (Görev 2, `FreeText_Unmeasurable`).
5. Özet taraması sürerken ana ekrana dönülmek istenir: dönülmez; pencere kapatılırsa soru sorulmaz, tarama durdurulur (Görev 4, `Shell_ScanRunning_LocksButClosesWithoutAsking`).

---

### Task 1: Motor: `Preview` ve onarım iptali

**Files:**
- Modify: `FotoDuzenleyici.Core/ArchiveBackup.cs`
- Test: `FotoDuzenleyici.Tests/BackupPreviewTests.cs` (yeni; kurulum için `ArchiveBackupTests.cs`'in yardımcılarını örnek al)

**Interfaces:**
- Produces: `BackupPreview ArchiveBackup.Preview(string archiveRoot, string backupRoot, BackupOptions opt, CancellationToken ct = default)`; `BackupPreview { int New, Changed, Unchanged, MissingInArchive; long BytesNeeded, FreeBytes; List<string> Errors; bool Cancelled; string? Blocked }` (özellikler); `public Func<string,long> FreeSpace`; `RepairResult.Cancelled`.

- [ ] **Step 1: Yazılacak testler** (`BackupPreviewTests`)
  - `Preview_MatchesRun`: yeni + değişmiş + aynı + arşivde yok içeren kurulumda `Preview`'ın dört sayısı sonraki `Run`'la aynı; `BytesNeeded == Run.BytesCopied`.
  - `Preview_WritesNothing`: yedek kökü yokken çağrılır, `Directory.Exists(bak)` false; kök varken `_Gecici\x.part` yerinde kalır, liste dosyası değişmez.
  - `Preview_Blocked`: `[Theory]` arşiv yok / iç içe / bozuk JSON / `FreeSpace = _ => 1`; `Blocked` dolu.
  - `Preview_SkipsDuplicatesByDefault`: `_Tekrarlar` sayılmaz; `IncludeDuplicates` ile sayılır.
  - `Preview_Cancelled`: iptal edilmiş belirteçle `Cancelled == true`.
  - `Run_CancelledBeforeScan_CopiesNothing`: `Cancelled == true`, `BytesCopied == 0`.
  - `Repair_Cancelled_Reports`: iptal edilmiş belirteçle `RepairResult.Cancelled == true`.
- [ ] **Step 2:** Çalıştır, FAIL (derleme hatası) gör.
- [ ] **Step 3:** `Run`'ın 96-145. satırlarını özel `Survey` yöntemine al: kök denetimleri, liste yükleme, tarama (`foreach` içinde `ct` denetimi), yer hesabı. `Directory.CreateDirectory` ve `CleanLeftovers` Survey'de **değil**, `Run`'da ve engel denetiminden sonra kalır; yedek klasörü yokken liste boş sayılır. Yedek kökü erişilemezse mevcut `Blocked` metni kullanılır. `Run`'daki `Blocked` metinleri değişmez. `Repair` iptalde `Cancelled = true` yapar.
- [ ] **Step 4:** `dotnet test --nologo -v q` → hepsi PASS (`ArchiveBackupTests` değişmeden geçer).
- [ ] **Step 5:** Commit: `feat(yedek): kopyalamadan once ozet (Preview)`.

### Task 2: Ayarlar ve `BackupFlowViewModel`: seçim, özet, yedekleme

**Files:**
- Modify: `FotoDuzenleyici.ViewModels/Settings.cs`
- Create: `FotoDuzenleyici.ViewModels/BackupFlowViewModel.cs`
- Test: `FotoDuzenleyici.Tests/BackupFlowTests.cs`

**Interfaces:**
- Consumes: Görev 1.
- Produces:
  - `record BackupPair(string Archive, string Backup, bool IncludeDuplicates, DateTime Used)`, `AppSettings.LastBackup`.
  - `enum BackupStep { Select, Preview, Running, Finished }`, `enum BackupActivity { Backup, Verify, Repair }`.
  - `BackupFlowViewModel(Services s, AppSettings settings, string? archive = null)`.
  - Gözlenen özellikler: `Step, Activity, Archive, Backup, IncludeDuplicates, IsScanning, Preview (BackupPreview?), Result (BackupResult?), Stopped, Done, Total, BytesDone, BytesTotal, Current, Remaining (TimeSpan?), ErrorMessage`.
  - Hesaplananlar: `CanScan, CanStart, CanVerify, NothingToCopy, FreeText, NeededText, FinishedSummary`.
  - Komutlar: `PickArchive, PickBackup, Scan, Start, Back, Stop, Resume, OpenBackup, Reset`.
  - `Task? RunningTask`, `static TimeSpan? Eta(long done, long total, TimeSpan elapsed)`, `internal Action<int>? OnProgressForTest`.

Ön doldurma önceliği: `archive` parametresi > `LastBackup.Archive` > `Recent[0].Archive`. `Backup` ve `IncludeDuplicates` yalnız `LastBackup`'tan. `Eta`: `done < 16 MB` ya da `total <= done` ise null, değilse geçen süre / done × kalan. `Scan` ve `Start`, `Task.Run` içinde motoru çağırır (`ApplyStepViewModel.Execute` kalıbı); `FreeSpace = _s.FreeSpace`. `FreeText`: `long.MaxValue` ise "ölçülemedi".

- [ ] **Step 1: Yazılacak testler** (`BackupFlowTests`, `Fakes` ile, gerçek geçici klasörler)
  - `Prefill_Order`: üç önceliğin üçü de sınanır.
  - `Scan_ShowsPreview_NoCopy`: `Step == Preview`, `Preview.New == n`, yedekte dosya yok.
  - `Start_Copies_SavesLastBackup`: `Step == Finished`, dosyalar yedekte, `Settings.Stored.LastBackup` dolu.
  - `Blocked_DisablesStart`: `Fakes.Free = 1`; `CanStart == false`, `Preview.Blocked` dolu.
  - `NothingToCopy_DisablesStart`: ikinci taramada `NothingToCopy == true`, `CanStart == false`.
  - `Stop_ThenResume_GoesToPreview`: `OnProgressForTest` ile durdur; `Stopped == true`; `Resume` sonrası `Step == Preview`, başlatınca kalanlar kopyalanır.
  - `ScanStop_ReturnsToSelect`.
  - `FolderChange_DropsPreview`: Geri, `Backup` değişir; `Preview == null`.
  - `Start_Twice_RunsOnce`: ilk iş sürerken ikinci `Start` yok sayılır.
  - `Run_DiskGone_ShowsErrors`: `Run` hata satırı döndüğünde `Step == Finished`, `Result.Errors` dolu (yedek yolunu erişilemez kılmak yerine kilitli kaynak dosyayla üret).
  - `FreeText_Unmeasurable`: `Fakes.Free = long.MaxValue`; `FreeText == "ölçülemedi"`, engel yok.
  - `Eta_Rules`: 15 MB → null; bitmiş → null; 100/200 MB ve 10 sn → 10 sn.
  - `Settings_CorruptLastBackup_Ignored`: `{"LastBackup":{"Archive":null,"Backup":null}}` yüklenince `LastBackup == null`.
- [ ] **Step 2:** Çalıştır, FAIL gör.
- [ ] **Step 3:** Yukarıdaki arayüzü yaz; `JsonSettingsStore.Load`'a `LastBackup` temizliğini ekle. `Archive` / `Backup` değişiminde `Preview = null` ve Görev 3'ün temizliği için `partial void` kancaları.
- [ ] **Step 4:** Tüm suite PASS.
- [ ] **Step 5:** Commit: `feat(yedek): yedekleme akisi (ozet, kopyalama, durdur)`.

### Task 3: Doğrulama ve onarım

**Files:**
- Modify: `FotoDuzenleyici.ViewModels/BackupFlowViewModel.cs`
- Test: `FotoDuzenleyici.Tests/BackupVerifyFlowTests.cs`

**Interfaces:**
- Consumes: Görev 2.
- Produces: `Verification (VerifyResult?), VerifyPartial, Repairs (RepairResult?), CanRepair, VerifySummary, RepairSummary, ProblemRows (IReadOnlyList<BackupProblemRow>)`; `record BackupProblemRow(string Path, string Reason)`; komutlar `Verify, Repair`.

`VerifySummary` biçimi: `"{ok:N0} sağlam · {eksik:N0} eksik · {bozuk:N0} bozuk"`; Reason: `"eksik"` / `"bozuk"`. `CanRepair`: `Verification` sorunlu ve `Archive` var. `CanVerify`: `Backup` klasörü var.

- [ ] **Step 1: Yazılacak testler**
  - `Verify_Clean`: `VerifySummary` "0 eksik · 0 bozuk" içerir, `CanRepair == false`.
  - `Verify_Corrupt_Repair_ThenClean`: elle bozulan dosya; `ProblemRows` "bozuk"; `Repair` sonrası `Repairs.Repaired == 1`, `Verification == null`; yeniden `Verify` temiz.
  - `Repair_NotRepairable_ShowsReason`: arşivdeki silinmiş; `RepairSummary` "arşivde artık yok" içerir.
  - `FolderChange_DropsVerification`: `CanRepair == false`.
  - `Verify_NoManifest_Explains`: `VerifySummary == "Bu klasörde bu uygulamayla alınmış bir yedek bulunamadı."`.
  - `Verify_Stopped_KeepsFound_Partial`: `VerifyPartial == true`, bulunan sorun onarılabilir.
  - `Verify_NotInManifest_NotAProblem`: fazladan dosya `CanRepair`'i açmaz.
- [ ] **Step 2:** Çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula. Listenin yokluğu `BackupManifest.Load(bak).Count == 0` ile anlaşılır.
- [ ] **Step 4:** Tüm suite PASS.
- [ ] **Step 5:** Commit: `feat(yedek): dogrula ve onar akisi`.

### Task 4: Kabuk ve ana ekran mantığı

**Files:**
- Create: `FotoDuzenleyici.ViewModels/ILongRunningPage.cs`
- Modify: `ShellViewModel.cs`, `HomeViewModel.cs`, `OrganizeFlowViewModel.cs`, `BackupFlowViewModel.cs` (ViewModels)
- Test: `FotoDuzenleyici.Tests/BackupShellTests.cs`

**Interfaces:**
- Consumes: Görev 2.
- Produces: `ILongRunningPage { bool IsLocked; bool MustConfirmClose; Task? RunningTask; string CloseQuestion; void RequestStop(); }`; `HomeViewModel.LastBackup`, `event Action? BackupRequested`, `StartBackupCommand`; `ShellViewModel.StartBackup(string? archive)` ve `OpenBackupFromFlow` için `OrganizeFlowViewModel.BackupRequested` (`Action<string>`), `BackupFromDoneCommand`.

`OrganizeFlowViewModel`: `IsLocked = NavigationLocked || IsBusy`, `MustConfirmClose = NavigationLocked`, `RunningTask = ApplyTask` (yalnız kilitliyken), `RequestStop = Apply?.CancelCommand`, `CloseQuestion` mevcut metin. Yedek: `IsLocked = Step == Running || IsScanning`, `MustConfirmClose = Step == Running`, `CloseQuestion` spec'teki metin. `RequestClose`'da kilitli ama `MustConfirmClose == false` ise `RequestStop()` çağrılıp `Close`.

- [ ] **Step 1: Yazılacak testler**
  - `Home_StartBackup_OpensPage`: `CurrentPage is BackupFlowViewModel`.
  - `Home_LastBackup_Exposed`.
  - `Shell_BackupRunning_Locks`: `GoHome`, `Undo`, `OpenClassic` çalışmaz; Klasik için `ErrorMessage` dolu.
  - `Shell_BackupRunning_Close_AsksAndWaits`: `Dialogs.Asked`'ta spec metni, evet → `WaitThenClose`, `RunningTask` dolu; hayır → `Stay`.
  - `Shell_ScanRunning_LocksButClosesWithoutAsking`.
  - `Flow_Done_BackupButton_PassesArchive`: `Archive` Düzenle arşivi.
- [ ] **Step 2:** Çalıştır, FAIL gör.
- [ ] **Step 3:** Uygula; `Flow?.FlushReviews()` çağrıları korunur.
- [ ] **Step 4:** Tüm suite PASS (`ShellTests` ve `CleanFinalFixTests` değişmeden).
- [ ] **Step 5:** Commit: `feat(yedek): kabuk kilidi ve ana ekran girisi`.

### Task 5: Sayfalar, belgeler, görsel denetim

**Files:**
- Create: `FotoDuzenleyici.App/Shell/BackupPage.xaml(.cs)`
- Modify: `ShellWindow.xaml` (`DataTemplate`), `HomePage.xaml:49-58`, `ApplyPage.xaml:118-123`, `CHANGELOG.md`, `YOL_HARITASI.md:39`, `README.md` (Yedekle varsa)
- Test: `scratchpad\render` düzeneği

**Interfaces:**
- Consumes: Görev 2-4.

- [ ] **Step 1:** `BackupPage.xaml`: spec'teki dört durum, `Step`'e göre görünürlük; mevcut `Panel`, `H2`, `Small`, `Mono` stilleri; yol etiketleri `TextWrapping="Wrap"`. Sonuç listeleri `ListBox` (sanallaştırılmış, yatay kaydırma kapalı).
- [ ] **Step 2:** Yedekle kartı Temizle kartı kalıbıyla etkin; `LastBackup` varsa "Son yedek:" ve `Mono` yol. `ApplyPage` kartı **Yedekle…** düğmesi; görünürlük `Apply.IsClean` tersi.
- [ ] **Step 3:** Geçici örnek klasörlerle render: Select, Preview, Finished, doğrulama sorunu durumları. Beklenen: bağlama hatası 0, metin taşması yok; PNG'ler okunur.
- [ ] **Step 4:** `CHANGELOG [Yayınlanmamış]` ve yol haritası güncellenir; "Yakında · komut satırında" metni kalkar. `dotnet test --nologo -v q` → hepsi PASS. `dotnet build -c Release` uyarısız.
- [ ] **Step 5:** Commit: `feat(yedek): Yedekle ekrani`.
