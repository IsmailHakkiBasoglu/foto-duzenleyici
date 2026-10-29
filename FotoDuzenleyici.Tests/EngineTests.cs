using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class EngineTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_test_" + Guid.NewGuid().ToString("N"));
    public EngineTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Make(string rel, string content, DateTime? time = null)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        if (time is { } t) { File.SetCreationTime(p, t); File.SetLastWriteTime(p, t); }
        return p;
    }

    static (List<MediaFile>, List<DuplicateGroup>) ScanAll(string root)
    {
        var s = new Scanner(new ScanOptions { Roots = { root }, IncludedKinds = { FileKind.Document, FileKind.Other, FileKind.Pdf } });
        var files = s.Scan();
        return (files, DuplicateFinder.Find(files));
    }

    [Fact]
    public void SameNameDifferentContent_BothKept_WithDatePrefix()
    {
        var t = new DateTime(2026, 1, 15, 10, 0, 0);
        Make(@"src\a\IMG_0001.pdf", "AAA", t);
        Make(@"src\b\IMG_0001.pdf", "BBBB", t);
        var (files, dups) = ScanAll(Path.Combine(_tmp, "src"));
        var plan = Planner.Build(files, dups, new PlanOptions { DestinationRoot = Path.Combine(_tmp, "out") });
        Assert.Empty(dups);
        Assert.Equal(2, plan.Items.Select(i => i.TargetPath.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(1, plan.Collisions); // ilk dosya ham adı alır, ikinci çakışır
        Assert.Contains(plan.Items, i => i.TargetName.StartsWith("2026-01-15 "));
    }

    [Fact]
    public void IdenticalContent_GoesToDuplicates()
    {
        var t = new DateTime(2026, 1, 15);
        Make(@"src\a\x.pdf", "SAME", t);
        Make(@"src\b\y.pdf", "SAME", t.AddDays(1));
        var (files, dups) = ScanAll(Path.Combine(_tmp, "src"));
        var plan = Planner.Build(files, dups, new PlanOptions { DestinationRoot = Path.Combine(_tmp, "out") });
        Assert.Single(dups);
        Assert.Single(plan.Items, i => i.Action == PlanAction.Duplicate);
        Assert.Contains("_Tekrarlar", plan.Items.First(i => i.Action == PlanAction.Duplicate).TargetPath);
    }

    [Fact]
    public void Executor_CopiesVerifies_PreservesTimestamps_AndUndoRestores()
    {
        var t = new DateTime(2024, 5, 6, 7, 8, 9);
        var src = Make(@"src\doc.pdf", "HELLO WORLD", t);
        var (files, dups) = ScanAll(Path.Combine(_tmp, "src"));
        var plan = Planner.Build(files, dups, new PlanOptions { DestinationRoot = Path.Combine(_tmp, "out") });
        var journal = Path.Combine(_tmp, "j.jsonl");

        var r = new Executor(journal).Run(plan.Items, recycleSource: false);
        Assert.Equal(1, r.Done); Assert.Equal(0, r.Failed);
        var target = plan.Items[0].TargetPath;
        Assert.Equal("HELLO WORLD", File.ReadAllText(target));
        Assert.Equal(t, File.GetLastWriteTime(target));
        Assert.Equal(t, File.GetCreationTime(target));
        Assert.True(File.Exists(src)); // recycleSource:false → kaynak durur
    }

    [Fact]
    public void Executor_NeverOverwritesExistingTarget()
    {
        Make(@"src\doc.pdf", "NEW", new DateTime(2024, 5, 6));
        var (files, dups) = ScanAll(Path.Combine(_tmp, "src"));
        var plan = Planner.Build(files, dups, new PlanOptions { DestinationRoot = Path.Combine(_tmp, "out") });
        Directory.CreateDirectory(plan.Items[0].TargetDir);
        File.WriteAllText(plan.Items[0].TargetPath, "OLD");
        var r = new Executor(Path.Combine(_tmp, "j.jsonl")).Run(plan.Items, recycleSource: false);
        Assert.Equal(1, r.Failed);
        Assert.Equal("OLD", File.ReadAllText(plan.Items[0].TargetPath));
    }

    [Fact]
    public void FileNameDate_Parsed()
    {
        Assert.Equal(new DateTime(2024, 5, 1), Scanner.DateFromFileName("WhatsApp Görsel 2024-05-01 saat 10.00.00_abc.jpg"));
        Assert.Null(Scanner.DateFromFileName("IMG_4564.JPG"));
    }

    // --- Tekrar çalıştırma: düzenlenmiş klasör ikinci kez işlenince hiçbir şey değişmemeli ---

    List<PlanItem> OrganizeThenRescan(PlanOptions opt, Func<string, string> pathStyle)
    {
        Make(@"src\a\IMG_0001.pdf", "AAA", new DateTime(2026, 1, 15, 10, 0, 0));
        Make(@"src\b\IMG_0001.pdf", "BBBB", new DateTime(2026, 1, 15, 11, 0, 0));
        Make(@"src\c\kopya.pdf", "AAA", new DateTime(2026, 1, 16));
        var (files, dups) = ScanAll(Path.Combine(_tmp, "src"));
        var plan = Planner.Build(files, dups, opt);
        var r = new Executor(Path.Combine(_tmp, "j.jsonl")).Run(plan.Items, recycleSource: false);
        Assert.Equal(0, r.Failed);

        var (again, dups2) = ScanAll(pathStyle(opt.DestinationRoot));
        var opt2 = new PlanOptions
        {
            DestinationRoot = pathStyle(opt.DestinationRoot), RenameWithDate = opt.RenameWithDate,
            AppendSourceToName = opt.AppendSourceToName, SourceFolder = opt.SourceFolder,
        };
        return Planner.Build(again, dups2, opt2).Items;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rerun_OnOrganizedFolder_ChangesNothing(bool forwardSlashes)
    {
        var items = OrganizeThenRescan(new PlanOptions { DestinationRoot = Path.Combine(_tmp, "out"), RenameWithDate = true },
            p => forwardSlashes ? p.Replace('\\', '/') : p);
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal(PlanAction.AlreadyInPlace, i.Action));
    }

    [Theory]
    [InlineData("2026-01-03 DSCF0001 Fuji.JPG")]
    [InlineData("2026-01-03 101500 DSCF0001 Fuji.JPG")]
    [InlineData("2026-01-03 DSCF0001 Fuji (2).JPG")]
    public void Rerun_WithAppendSource_DoesNotRepeatSourceOrDate(string name)
    {
        var f = new MediaFile
        {
            Path = $@"Z:\out\2026-01\2026-01-Foto\{name}", Kind = FileKind.Photo,
            Taken = new DateTime(2026, 1, 3, 10, 15, 0), Source = "Fuji",
        };
        var plan = Planner.Build(new[] { f }, new List<DuplicateGroup>(),
            new PlanOptions { DestinationRoot = @"Z:\out", RenameWithDate = true, AppendSourceToName = true });
        Assert.Equal(PlanAction.AlreadyInPlace, plan.Items[0].Action);
        Assert.Equal(name, plan.Items[0].TargetName);
    }

    [Fact]
    public void NewFile_AddedByHand_GetsSameNamingAsOthers()
    {
        var f = new MediaFile
        {
            Path = @"Z:\out\2026-01\2026-01-Foto\DSCF0002.JPG", Kind = FileKind.Photo,
            Taken = new DateTime(2026, 1, 3, 10, 15, 0), Source = "Fuji",
        };
        var plan = Planner.Build(new[] { f }, new List<DuplicateGroup>(),
            new PlanOptions { DestinationRoot = @"Z:\out", RenameWithDate = true, AppendSourceToName = true });
        Assert.Equal("2026-01-03 DSCF0002 Fuji.JPG", plan.Items[0].TargetName);
    }

    [Fact]
    public void Duplicate_KeeperIsTheOrganizedCopy_EvenIfNewer()
    {
        var old = new DateTime(2020, 1, 1);
        Make(@"a_yeni\x.pdf", "SAME", old);
        var (files, dups) = ScanAll(Path.Combine(_tmp, "a_yeni"));
        var dest = Path.Combine(_tmp, "z_out");
        var placed = Planner.Build(files, dups, new PlanOptions { DestinationRoot = dest }).Items[0];
        // Elle kopyalanmış gibi: yazma zamanı aynı (aynı ay), oluşturma zamanı daha yeni.
        var organized = Make(Path.GetRelativePath(_tmp, placed.TargetPath), "SAME", old);
        File.SetCreationTime(organized, old.AddYears(5));

        var s = new Scanner(new ScanOptions { Roots = { Path.Combine(_tmp, "a_yeni"), dest }, IncludedKinds = { FileKind.Pdf } });
        var all = s.Scan();
        var plan = Planner.Build(all, DuplicateFinder.Find(all), new PlanOptions { DestinationRoot = dest });
        Assert.Equal(PlanAction.AlreadyInPlace, plan.Items.Single(i => i.File.Path.Contains("z_out")).Action);
        Assert.Equal(PlanAction.Duplicate, plan.Items.Single(i => i.File.Path.Contains("a_yeni")).Action);
    }

    [Fact]
    public void SourceSubfolder_Option_AddsLevel()
    {
        var f = new MediaFile { Path = @"C:\x\DSCF0001.JPG", Kind = FileKind.Photo, Taken = new DateTime(2026, 1, 3), Source = "Fuji" };
        var on = Planner.Build(new[] { f }, new List<DuplicateGroup>(),
            new PlanOptions { DestinationRoot = @"Z:\out", SourceFolder = SourceFolderMode.SubFolder });
        var off = Planner.Build(new[] { f }, new List<DuplicateGroup>(), new PlanOptions { DestinationRoot = @"Z:\out" });
        Assert.Equal(@"Z:\out\2026-01\2026-01-Foto\2026-01-Fuji", on.Items[0].TargetDir);
        Assert.Equal(@"Z:\out\2026-01\2026-01-Foto", off.Items[0].TargetDir);
    }

    static readonly TimeZoneInfo Turkey = TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "TR", "TR");

    [Fact]
    public void VideoUtcDate_IsConvertedToLocal_EvenAcrossMonthEnd()
    {
        // Gerçek örnek: MVI_3028 dosyada 19 Mart 21:45 (UTC), çekim 20 Mart 00:45.
        Assert.Equal(new DateTime(2023, 3, 20, 0, 45, 0), Scanner.UtcToLocal(new DateTime(2023, 3, 19, 21, 45, 0), Turkey));
        Assert.Equal(new DateTime(2024, 2, 1, 1, 30, 0), Scanner.UtcToLocal(new DateTime(2024, 1, 31, 22, 30, 0), Turkey));
        Assert.Equal(new DateTime(2022, 10, 29, 13, 54, 44), Scanner.UtcToLocal(new DateTime(2022, 10, 29, 10, 54, 44), Turkey));
    }

    [Theory]
    [InlineData("2023-05-01T23:34:56+0300", 2023, 5, 1, 23)]
    [InlineData("2023-05-01T23:34:56+03:00", 2023, 5, 1, 23)]
    [InlineData("2023-12-31T22:10:00-0500", 2023, 12, 31, 22)]
    public void AppleCreationDate_KeepsWallClockOfShootingPlace(string s, int y, int mo, int d, int h)
    {
        var dt = Scanner.ParseOffsetDate(s);
        Assert.NotNull(dt);
        Assert.Equal(new DateTime(y, mo, d, h, dt!.Value.Minute, dt.Value.Second), dt);
    }

    [Theory]
    [InlineData("2023-03-19 MVI_3028.MP4", "2023-03-20 MVI_3028.MP4")]
    [InlineData("2023-03-19 214500 MVI_3028.MP4", "2023-03-20 MVI_3028.MP4")]
    [InlineData("2023-03-20 MVI_3028.MP4", "2023-03-20 MVI_3028.MP4")]
    [InlineData("MVI_3028.MP4", "2023-03-20 MVI_3028.MP4")]
    public void CorrectedDate_ReplacesOldPrefix_InsteadOfStacking(string name, string expected)
    {
        var f = new MediaFile
        {
            Path = @"Z:\out\2023-03\2023-03-Video\" + name, Kind = FileKind.Video,
            Taken = new DateTime(2023, 3, 20, 0, 45, 0), DateSource = DateSource.VideoMetadata,
        };
        var plan = Planner.Build(new[] { f }, new List<DuplicateGroup>(),
            new PlanOptions { DestinationRoot = @"Z:\out", RenameWithDate = true });
        Assert.Equal(expected, plan.Items[0].TargetName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tarih değil")]
    public void AppleCreationDate_InvalidReturnsNull(string? s) => Assert.Null(Scanner.ParseOffsetDate(s));

    // --- Kesintiye dayanıklılık: yarıda kalan işlem günlükten kaldığı yerden sürer ---

    /// <summary>İlerleme bildirimi geldiğinde, belirli sayıda dosya bittiyse işlemi keser (çökmeyi taklit eder).</summary>
    sealed class CancelAfter(int files, CancellationTokenSource cts) : IProgress<(int done, int total, string current)>
    {
        public void Report((int done, int total, string current) p) { if (p.done >= files) cts.Cancel(); }
    }

    (List<PlanItem> items, string journal) PlanThree()
    {
        var t = new DateTime(2024, 5, 6, 7, 8, 9);
        Make(@"src\a.pdf", "AAA", t);
        Make(@"src\b.pdf", "BBBB", t);
        Make(@"src\c.pdf", "CCCCC", t);
        var (files, dups) = ScanAll(Path.Combine(_tmp, "src"));
        var dest = Path.Combine(_tmp, "out");
        var plan = Planner.Build(files, dups, new PlanOptions { DestinationRoot = dest });
        return (plan.Items, Path.Combine(dest, "_Gunluk", "islem-1.jsonl"));
    }

    [Fact]
    public void Interrupted_Run_ResumesRemainingFiles_WithoutRescan()
    {
        var (items, journal) = PlanThree();
        var cts = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new Executor(journal).Run(items, recycleSource: false, new CancelAfter(1, cts), cts.Token));
        Assert.Equal(2, Executor.Pending(journal).Count);
        Assert.Single(Executor.FindUnfinished(Path.Combine(_tmp, "out")));

        var r = new Executor(journal).Resume();
        Assert.Equal(2, r.Done); Assert.Equal(0, r.Failed);
        Assert.All(items, i => Assert.True(File.Exists(i.TargetPath)));
        Assert.Empty(Executor.Pending(journal));
        Assert.Empty(Executor.FindUnfinished(Path.Combine(_tmp, "out")));
        Assert.All(Executor.FinalStates(journal), e => Assert.Equal("Copied", e.State));
        Assert.All(Executor.FinalStates(journal), e => Assert.NotEmpty(e.Sha256)); // Geri Al için gerekli
    }

    [Fact]
    public void Resume_RemovesLeftoverTempCopy_AndFinishesFile()
    {
        var (items, journal) = PlanThree();
        var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new Executor(journal).Run(items, false, null, cts.Token));
        var first = items[0];
        Directory.CreateDirectory(first.TargetDir);
        File.WriteAllText(first.TargetPath + ".kopya_tmp", "YARI"); // kopyalama ortasında kesilmiş

        var r = new Executor(journal).Resume();
        Assert.Equal(3, r.Done); Assert.Equal(0, r.Failed);
        Assert.False(File.Exists(first.TargetPath + ".kopya_tmp"));
        Assert.Equal(File.ReadAllText(first.File.Path), File.ReadAllText(first.TargetPath));
    }

    [Fact]
    public void Resume_TargetAlreadyCopied_IsNotCopiedAgain_AndRecorded()
    {
        var (items, journal) = PlanThree();
        var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new Executor(journal).Run(items, false, null, cts.Token));
        // Hedefe yerleşmiş ama günlüğe yazılamamış: hedef zaten var, kaynak da duruyor.
        var first = items[0];
        Directory.CreateDirectory(first.TargetDir);
        File.Copy(first.File.Path, first.TargetPath);
        var stamp = new DateTime(2001, 1, 1);
        File.SetLastWriteTime(first.TargetPath, stamp);

        var r = new Executor(journal).Resume();
        Assert.Equal(3, r.Done); Assert.Equal(0, r.Failed);
        Assert.Equal(stamp, File.GetLastWriteTime(first.TargetPath)); // yeniden kopyalanmadı
        Assert.Equal("Copied", Executor.FinalStates(journal).First(e => e.Target == first.TargetPath).State);
    }

    [Fact]
    public void Resume_SourceAlreadyRecycled_CountsAsRecycled()
    {
        var (items, journal) = PlanThree();
        // Gerçekte kaynak ancak Started satırı (hash'iyle) yazıldıktan sonra gidebilir.
        foreach (var i in items) i.File.Sha256 = DuplicateFinder.Hash(i.File.Path);
        var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new Executor(journal).Run(items, true, null, cts.Token));
        // Kaynak Geri Dönüşüm Kutusu'na gitmiş, günlük satırı yazılamamış.
        var first = items[0];
        Directory.CreateDirectory(first.TargetDir);
        File.Move(first.File.Path, first.TargetPath);
        // Kalan dosyalar Geri Dönüşüm Kutusu'na gitmesin diye onları önceden yerleştir.
        foreach (var i in items.Skip(1)) { Directory.CreateDirectory(i.TargetDir); File.Move(i.File.Path, i.TargetPath); }

        var r = new Executor(journal).Resume();
        Assert.Equal(3, r.Done); Assert.Equal(0, r.Failed);
        Assert.All(Executor.FinalStates(journal), e => Assert.Equal("Recycled", e.State));
    }

    [Fact]
    public void Resume_TargetWithDifferentContent_IsNeverOverwritten()
    {
        var (items, journal) = PlanThree();
        var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new Executor(journal).Run(items, false, null, cts.Token));
        Directory.CreateDirectory(items[0].TargetDir);
        File.WriteAllText(items[0].TargetPath, "BAŞKA");

        var r = new Executor(journal).Resume();
        Assert.Equal(2, r.Done); Assert.Equal(1, r.Failed);
        Assert.Equal("BAŞKA", File.ReadAllText(items[0].TargetPath));
    }

    [Fact]
    public void Journal_WithTruncatedLastLine_StillReadable_AndUndoWorks()
    {
        var (items, journal) = PlanThree();
        Assert.Equal(3, new Executor(journal).Run(items, recycleSource: false).Done);
        File.AppendAllText(journal, "{\"Source\":\"C:\\\\yar"); // elektrik kesildi

        Assert.Equal(3, Executor.FinalStates(journal).Count);
        var u = Executor.Undo(journal); // kaynaklar durduğu için hepsi atlanır, ama günlük okunur
        Assert.Equal(0, u.Failed); Assert.Equal(3, u.Skipped);
    }

    [Fact]
    public void Journal_WithTruncatedLastLine_NextEntryStartsOnNewLine()
    {
        var (items, journal) = PlanThree();
        var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new Executor(journal).Run(items, false, null, cts.Token));
        File.AppendAllText(journal, "{\"Source\":\"C:\\\\yar"); // elektrik kesildi

        Executor.Abandon(journal); // ilk Skipped satırı yarım satıra yapışmamalı
        Assert.Empty(Executor.Pending(journal));
    }

    [Fact]
    public void OldFormatJournal_OneLinePerFile_StillWorks()
    {
        var j = Path.Combine(_tmp, "eski.jsonl");
        File.WriteAllText(j,
            "{\"Source\":\"C:\\\\a.jpg\",\"Target\":\"D:\\\\x\\\\a.jpg\",\"Action\":\"Move\",\"Sha256\":\"AB\",\"State\":\"Recycled\",\"Error\":null}\n" +
            "{\"Source\":\"C:\\\\b.jpg\",\"Target\":\"D:\\\\x\\\\b.jpg\",\"Action\":\"Move\",\"Sha256\":\"\",\"State\":\"Failed\",\"Error\":\"Kaynak yok\"}\n");
        Assert.Equal(new[] { "Recycled", "Failed" }, Executor.FinalStates(j).Select(e => e.State));
        Assert.Empty(Executor.Pending(j));
    }

    [Fact]
    public void Abandon_MarksRemainingAsSkipped_AndStopsAsking()
    {
        var (items, journal) = PlanThree();
        var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new Executor(journal).Run(items, false, null, cts.Token));

        Executor.Abandon(journal);
        Assert.Empty(Executor.Pending(journal));
        Assert.Empty(Executor.FindUnfinished(Path.Combine(_tmp, "out")));
        Assert.All(Executor.FinalStates(journal), e => Assert.Equal("Skipped", e.State));
        Assert.All(items, i => Assert.False(File.Exists(i.TargetPath)));
    }
}
