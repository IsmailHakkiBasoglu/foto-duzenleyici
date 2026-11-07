# Yedekleme çekirdeği — uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Arşivi ikinci bir diske artımlı, SHA-256 doğrulamalı yedekleyen; yedeği tam doğrulayıp
arşivden onaran Core sınıfları ve komut satırı seçenekleri.

**Architecture:** `BackupManifest` yedek kökündeki JSON listeyi yönetir. `ArchiveBackup` (instance; test
kancaları için) `Run` / `Verify` / `Repair` sunar ve dosyaları tek tek, `.part` → hash → yeniden adlandırma
sırasıyla kopyalar. Cli, `--backup` ve `--verify-backup` ile bunları çağırır. Arayüz bu turda yok.

**Tech Stack:** C# .NET 8 (net8.0-windows), System.Text.Json, System.Security.Cryptography, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-07-yedekleme-design.md`

## Global Constraints

- Hiçbir dosya silinmez: arşive yalnız okumak için dokunulur. Yedekte yalnız uygulamanın kendi `.part`
  ve `.tmp` dosyaları silinir; eski sürüm `_EskiSurumler\yyyy-MM-dd\…`, bozuk kopya `_Bozuk\yyyy-MM-dd\…`
  altına taşınır. Aynı hedef varsa ` (2)`, ` (3)`… eklenir (uzantıdan önce).
- Liste dosyası: yedek kökünde `.fotoduzenleyici-yedek.json`; göreli yol anahtarı büyük/küçük harf duyarsız.
- Hash: SHA-256, onaltılık **küçük harf**.
- `Modified` UTC tutulur; kıyas tam eşitlik (`DateTime` ticks).
- Kullanıcıya dönük metinler Türkçe.
- Commit mesajlarında Co-Authored-By satırı yok.

## Review Focus

1. Yedek kökü arşiv kökünün alt klasörü (ör. `D:\Arsiv\Yedek`) → reddedilmeli; yoksa yedek kendini kopyalar.
2. Arşivde `_EskiSurumler` / `_Bozuk` adlı klasör → yedeklenirken atlanmalı.
3. Arşivde salt okunur dosya → yedekteki kopya da salt okunur olur; sonraki eski sürüm taşıması ve
   `.part` yeniden adlandırması çalışmalı (taşıma öncesi salt okunur bayrağı kaldırılır).
4. Kopyalama sırasında kaynak dosya kilitli → dosyaya özgü hata, yedekleme sürer.
5. Çok uzun yollar (`_EskiSurumler\2026-11-07\…` önekiyle 260'ı aşan) → .NET 8 uzun yol destekler;
   hata olursa dosyaya özgü hata olarak raporlanmalı, çökmemeli.

Görev 2'deki testler 1–3'ü sabitler; 4–5 hata yakalamanın genel yolu ile karşılanır.

---

### Task 1: `BackupManifest`

**Files:**
- Create: `FotoDuzenleyici.Core/BackupManifest.cs`
- Test: `FotoDuzenleyici.Tests/BackupManifestTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record BackupEntry(string Path, long Size, DateTime Modified, string Sha256, DateTime BackedUp)`
  - `public sealed class BackupManifest`:
    - `public const string FileName = ".fotoduzenleyici-yedek.json"`
    - `static BackupManifest Load(string backupRoot)`: dosya yoksa boş. Bozuk JSON'da
      `InvalidDataException("Yedek listesi okunamadı: …")` fırlatır.
    - `void Save()`: `.tmp`'ye yazar, `File.Move(tmp, path, overwrite: true)`.
    - `bool TryGet(string relPath, out BackupEntry e)`, `void Set(BackupEntry e)`,
      `IReadOnlyCollection<BackupEntry> Entries`, `int Count`.

- [ ] **Step 1: Write failing tests.** Testler: `Load_Missing_IsEmpty`; `SaveLoad_RoundTrips_CaseInsensitive`
  (`Set` ile `a\B.jpg` yazılır, kaydedilip yeniden yüklenir; `TryGet("A\\b.JPG")` bulur, alanlar eşittir);
  `Load_CorruptJson_Throws` (dosyaya `{bozuk` yazılır → `InvalidDataException`); `Save_LeavesNoTmp`.
- [ ] **Step 2:** Run: `dotnet test --filter BackupManifestTests`. Expected: derleme hatası (tür yok).
- [ ] **Step 3: Implement** `BackupManifest`. JSON: `{ "Version": 1, "Entries": [ … ] }`. İçeride `Dictionary<string, BackupEntry>` (OrdinalIgnoreCase).
- [ ] **Step 4:** Run: aynı filtre. Expected: 4/4 geçer.
- [ ] **Step 5: Commit** `Yedek listesi (BackupManifest)`.

### Task 2: `ArchiveBackup.Run`

**Files:**
- Create: `FotoDuzenleyici.Core/ArchiveBackup.cs`
- Test: `FotoDuzenleyici.Tests/ArchiveBackupTests.cs` (geçici `arsiv` ve `yedek` klasörü, `IDisposable`)

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `public sealed class BackupOptions { bool IncludeDuplicates = false; string DuplicatesFolderName = "_Tekrarlar"; int SaveEvery = 200; }`
  - `public readonly record struct BackupProgress(int Done, int Total, long BytesDone, long BytesTotal, string Current)`
  - `public sealed class BackupResult { int New, Changed, Unchanged, MissingInArchive; long BytesCopied; List<string> Errors; bool Cancelled; string? Blocked; }`
    (`Blocked`: iş hiç başlamadıysa nedeni — iç içe kök, yer yok, bozuk liste, kök yok.)
  - `public sealed class ArchiveBackup`:
    - `public const string OldVersionsFolder = "_EskiSurumler", CorruptFolder = "_Bozuk"`
    - `internal Func<string, long> FreeSpace` (varsayılan `new DriveInfo(root).AvailableFreeSpace`)
    - `internal Action<string>? AfterWrite` (`.part` yazıldıktan sonra, doğrulamadan önce çağrılır)
    - `internal Func<DateTime> Today` (varsayılan `DateTime.Today`)
    - `BackupResult Run(string archiveRoot, string backupRoot, BackupOptions opt, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)`
    - `internal bool CopyVerified(string src, string dst, out string sha, out string? error)` (Task 3 kullanır)
  - Tests projesine `InternalsVisibleTo` (yoksa Core csproj'a eklenir).

- [ ] **Step 1: Write failing tests** (spec test 1–10 ve Review Focus 1–3):
  - `FirstRun_CopiesAll_WithHashesAndTimes`: liste hash'i `DuplicateFinder.Hash` küçük harfe eşit; yedek `LastWriteTimeUtc` = kaynak.
  - `SecondRun_SkipsUnchanged` → `Unchanged == n`, `BytesCopied == 0`.
  - `ChangedFile_OldVersionMovedToDatedFolder_SecondChangeSameDayGetsSuffix` (`Today` sabit 2026-11-07; `_EskiSurumler\2026-11-07\alt\a.jpg` ve `…\alt\a (2).jpg`).
  - `DeletedFromArchive_StaysInBackup_CountedMissing`.
  - `DuplicatesFolder_SkippedByDefault_IncludedWhenAsked`.
  - `ReservedFolderNamesInArchive_AreSkipped`.
  - `HashMismatch_TwoTries_ThenError_NoPartLeft_NotInManifest` (`AfterWrite` dosyaya bir bayt ekler).
  - `LeftoverPart_IsDeletedOnNextRun`.
  - `NotEnoughSpace_CopiesNothing_Blocked` (`FreeSpace = _ => 10`).
  - `NestedRoots_Rejected` (iki yön).
  - `Cancel_SavesProgress_NextRunCopiesRest` (`progress` içinde 2. dosyadan sonra `cts.Cancel()`; `SaveEvery = 1`).
  - `ReadOnlySource_ChangedLater_OldVersionStillMoved`.
  - `CorruptManifest_Blocked`.
- [ ] **Step 2:** Run: `dotnet test --filter ArchiveBackupTests`. Expected: derleme hatası.
- [ ] **Step 3: Implement.** Tarama `Directory.EnumerateFiles(archiveRoot, "*", AllDirectories)`; göreli yolun ilk parçası ayrılmış ad ya da (kapalıysa) tekrar klasörü ise atlanır. Kopya: 1 MB tamponla oku → `IncrementalHash` + `.part`'a yaz; `AfterWrite`; `.part`'ı yeniden hash'le; eşitse `File.SetLastWriteTimeUtc`, `File.Move(part, dst, overwrite:false)`. Hedefte dosya varsa (değişmiş) önce salt okunur bayrağını kaldırıp eski sürüm klasörüne taşı. `IOException` ya da `UnauthorizedAccessException` dosyaya özgü hatadır; hedef sürücü yoksa (`DirectoryNotFoundException` kökte) ya da diskte yer kalmadıysa (HResult 0x70 / 0x27) iş durur. İptalde liste kaydedilir, `Cancelled = true`.
- [ ] **Step 4:** Run: aynı filtre. Expected: hepsi geçer. Sonra tam paket: `dotnet test` → hepsi geçer.
- [ ] **Step 5: Commit** `Yedekleme: artımlı, hash doğrulamalı kopya (ArchiveBackup.Run)`.

### Task 3: `Verify` ve `Repair`

**Files:**
- Modify: `FotoDuzenleyici.Core/ArchiveBackup.cs`
- Test: `FotoDuzenleyici.Tests/ArchiveBackupTests.cs`

**Interfaces:**
- Consumes: Task 2 (`CopyVerified`, `Today`, klasör sabitleri).
- Produces:
  - `public enum VerifyStatus { Ok, Missing, Corrupt }`
  - `public sealed class VerifyResult { int Ok; List<(BackupEntry Entry, VerifyStatus Status)> Problems; int NotInManifest; bool Cancelled; string? Blocked; }`
  - `public sealed class RepairResult { int Repaired; List<string> NotRepaired; List<string> Errors; }`
  - `VerifyResult Verify(string backupRoot, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)`
  - `RepairResult Repair(string archiveRoot, string backupRoot, VerifyResult problems, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)`

- [ ] **Step 1: Write failing tests:**
  - `Verify_FindsCorruptAndMissing` (yedekte bir dosyaya bayt yazılır, biri silinir; listede olmayan bir dosya eklenir → `NotInManifest == 1`; `_EskiSurumler` içindekiler sayılmaz).
  - `Repair_MovesCorruptToBozuk_RecopiesMissing_ThenVerifyClean`.
  - `Repair_ArchiveChanged_NotRepaired_Untouched` (arşiv dosyası değiştirilir; yedekteki bozuk kopya yerinde kalır).
- [ ] **Step 2:** Run: `dotnet test --filter ArchiveBackupTests`. Expected: derleme hatası.
- [ ] **Step 3: Implement.** Repair'da arşiv dosyasının hash'i listedekiyle eşitse: bozuksa `_Bozuk\yyyy-MM-dd\<göreli>`'ye taşı, sonra `CopyVerified`. Liste kaydını günceller (`BackedUp`).
- [ ] **Step 4:** Run: filtre, sonra `dotnet test`. Expected: hepsi geçer.
- [ ] **Step 5: Commit** `Yedek doğrulama ve onarma`.

### Task 4: Komut satırı ve belgeler

**Files:**
- Modify: `FotoDuzenleyici.Cli/Program.cs` (argüman ayrıştırma ve `--undo` / `--resume`'dan sonra, `--root` kontrolünden önce erken dönüşler; kullanım yorumu)
- Modify: `CHANGELOG.md` ([Yayınlanmamış] → Eklendi), `YOL_HARITASI.md` (Aşama 4 yedek maddesi 🔄 "çekirdek + komut satırı; ekran yeni arayüzde")

**Interfaces:**
- Consumes: Task 2–3.

- [ ] **Step 1:** `--backup <yedek>` (`--root` gerekir, yalnız ilk kök; `--include-duplicates`), `--verify-backup <yedek>`. Çıktı: spec'teki özet satırı, sorunlu dosyalar; `--csv` verilmişse `…-yedek.csv` (`Durum;Dosya;Neden`) ya da `…-dogrulama.csv` (`Durum;Dosya`). Çıkış kodu: `--backup` hata/Blocked → 2; `--verify-backup` sorun → 1.
- [ ] **Step 2: Elle doğrulama** (scratchpad'de iki geçici klasörle): `dotnet run --project FotoDuzenleyici.Cli -- --root <a> --backup <y>` iki kez (ikincisi "0 yeni"), bir yedek dosyasını boz, `--verify-backup <y>` → çıkış kodu 1 ve dosya adı. Expected: tarif edildiği gibi.
- [ ] **Step 3:** `dotnet test` → hepsi geçer. **Commit** `Komut satırına --backup ve --verify-backup`.
