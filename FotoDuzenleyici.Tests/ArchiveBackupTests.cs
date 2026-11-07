using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Arşivi ikinci diske yedekleme: artımlı kopya, hash doğrulaması, hiçbir şeyin silinmemesi.</summary>
public class ArchiveBackupTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_backup_" + Guid.NewGuid().ToString("N"));
    readonly string _arc, _bak;
    static readonly DateTime Day = new(2026, 11, 7);

    public ArchiveBackupTests()
    {
        _arc = Path.Combine(_tmp, "arsiv");
        _bak = Path.Combine(_tmp, "yedek");
        Directory.CreateDirectory(_arc);
        Directory.CreateDirectory(_bak);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_tmp, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_tmp, true);
        }
        catch { }
    }

    string Arc(string rel, string content)
    {
        var p = Path.Combine(_arc, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        if (File.Exists(p)) File.SetAttributes(p, FileAttributes.Normal);
        File.WriteAllText(p, content);
        return p;
    }

    string Bak(string rel) => Path.Combine(_bak, rel);

    static ArchiveBackup New() => new() { Today = () => Day };

    BackupResult Run(ArchiveBackup? b = null, BackupOptions? opt = null) =>
        (b ?? New()).Run(_arc, _bak, opt ?? new BackupOptions());

    sealed class SyncProgress(Action<BackupProgress> on) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) => on(value);
    }

    [Fact]
    public void FirstRun_CopiesAll_WithHashesAndTimes()
    {
        var a = Arc(@"2024-01\2024-01-Foto\a.jpg", "aaaa");
        var b = Arc(@"2024-02\b.mp4", "bbbbbbbb");
        File.SetLastWriteTimeUtc(a, new DateTime(2024, 1, 5, 10, 0, 0, DateTimeKind.Utc));

        var r = Run();

        Assert.Null(r.Blocked);
        Assert.Equal(2, r.New);
        Assert.Equal(12, r.BytesCopied);
        Assert.Empty(r.Errors);
        Assert.Equal("aaaa", File.ReadAllText(Bak(@"2024-01\2024-01-Foto\a.jpg")));
        Assert.Equal(File.GetLastWriteTimeUtc(a), File.GetLastWriteTimeUtc(Bak(@"2024-01\2024-01-Foto\a.jpg")));
        var m = BackupManifest.Load(_bak);
        Assert.True(m.TryGet(@"2024-02\b.mp4", out var e));
        Assert.Equal(DuplicateFinder.Hash(b).ToLowerInvariant(), e.Sha256);
        Assert.Equal(8, e.Size);
    }

    [Fact]
    public void SecondRun_SkipsUnchanged()
    {
        Arc("a.jpg", "aaaa");
        Arc(@"x\b.jpg", "bb");
        Run();
        var r = Run();
        Assert.Equal(0, r.New);
        Assert.Equal(2, r.Unchanged);
        Assert.Equal(0, r.BytesCopied);
    }

    [Fact]
    public void ChangedFile_OldVersionMovedToDatedFolder_SecondChangeSameDayGetsSuffix()
    {
        Arc(@"alt\a.jpg", "v1");
        Run();
        Arc(@"alt\a.jpg", "v2-uzun");
        var r = Run();
        Assert.Equal(1, r.Changed);
        Assert.Equal("v2-uzun", File.ReadAllText(Bak(@"alt\a.jpg")));
        Assert.Equal("v1", File.ReadAllText(Bak(@"_EskiSurumler\2026-11-07\alt\a.jpg")));

        Arc(@"alt\a.jpg", "v3-daha-uzun");
        Run();
        Assert.Equal("v3-daha-uzun", File.ReadAllText(Bak(@"alt\a.jpg")));
        Assert.Equal("v2-uzun", File.ReadAllText(Bak(@"_EskiSurumler\2026-11-07\alt\a (2).jpg")));
    }

    [Fact]
    public void DeletedFromArchive_StaysInBackup_CountedMissing()
    {
        var a = Arc("a.jpg", "aaaa");
        Arc("b.jpg", "bb");
        Run();
        File.Delete(a);
        var r = Run();
        Assert.Equal(1, r.MissingInArchive);
        Assert.True(File.Exists(Bak("a.jpg")));
        Assert.True(BackupManifest.Load(_bak).TryGet("a.jpg", out _));
    }

    [Fact]
    public void DuplicatesFolder_SkippedByDefault_IncludedWhenAsked()
    {
        Arc(@"_Tekrarlar\2024-01\d.jpg", "dd");
        Arc("a.jpg", "aa");
        Run();
        Assert.False(File.Exists(Bak(@"_Tekrarlar\2024-01\d.jpg")));

        var r = Run(opt: new BackupOptions { IncludeDuplicates = true });
        Assert.Equal(1, r.New);
        Assert.True(File.Exists(Bak(@"_Tekrarlar\2024-01\d.jpg")));
    }

    [Fact]
    public void ReservedFolderNamesInArchive_AreSkipped()
    {
        Arc(@"_EskiSurumler\z.jpg", "z");
        Arc(@"_Bozuk\y.jpg", "y");
        Arc("a.jpg", "a");
        var r = Run();
        Assert.Equal(1, r.New);
        Assert.False(File.Exists(Bak(@"_EskiSurumler\z.jpg")));
        Assert.False(File.Exists(Bak(@"_Bozuk\y.jpg")));
    }

    [Fact]
    public void HashMismatch_TwoTries_ThenError_NoPartLeft_NotInManifest()
    {
        Arc("a.jpg", "aaaa");
        Arc("b.jpg", "bb");
        int calls = 0;
        var b = New();
        b.AfterWrite = part =>
        {
            if (File.ReadAllText(part) != "aaaa") return;
            calls++;
            File.AppendAllText(part, "X");
        };

        var r = Run(b);

        Assert.Equal(2, calls);
        Assert.Single(r.Errors);
        Assert.Contains("a.jpg", r.Errors[0]);
        Assert.False(File.Exists(Bak("a.jpg")));
        Assert.Empty(Directory.GetFiles(Bak(ArchiveBackup.TempFolder)));
        var m = BackupManifest.Load(_bak);
        Assert.False(m.TryGet("a.jpg", out _));
        Assert.True(m.TryGet("b.jpg", out _));
    }

    [Fact]
    public void LeftoverTemp_IsDeletedOnNextRun_UserPartFileUntouched()
    {
        Arc("a.jpg", "a");
        Directory.CreateDirectory(Bak(ArchiveBackup.TempFolder));
        File.WriteAllText(Bak(@"_Gecici\0123.part"), "yarım");
        Directory.CreateDirectory(Bak("eski"));
        File.WriteAllText(Bak(@"eski\x.jpg.part"), "kullanicinin");
        Run();
        Assert.False(File.Exists(Bak(@"_Gecici\0123.part")));
        Assert.True(File.Exists(Bak(@"eski\x.jpg.part")));
    }

    [Fact]
    public void NotEnoughSpace_CopiesNothing_Blocked()
    {
        Arc("a.jpg", new string('a', 100));
        var b = New();
        b.FreeSpace = _ => 10;
        var r = Run(b);
        Assert.NotNull(r.Blocked);
        Assert.Contains("yer", r.Blocked);
        Assert.False(File.Exists(Bak("a.jpg")));
    }

    [Fact]
    public void NestedRoots_Rejected()
    {
        Arc("a.jpg", "a");
        var inside = Path.Combine(_arc, "Yedek");
        var r1 = New().Run(_arc, inside, new BackupOptions());
        Assert.NotNull(r1.Blocked);
        Assert.False(Directory.Exists(inside) && Directory.EnumerateFiles(inside).Any());

        var r2 = New().Run(_arc, _tmp, new BackupOptions());
        Assert.NotNull(r2.Blocked);
    }

    [Fact]
    public void Cancel_SavesProgress_NextRunCopiesRest()
    {
        for (int i = 0; i < 5; i++) Arc($"f{i}.jpg", "icerik" + i);
        using var cts = new CancellationTokenSource();
        var p = new SyncProgress(v => { if (v.Done == 2) cts.Cancel(); });

        var r = New().Run(_arc, _bak, new BackupOptions { SaveEvery = 1 }, p, cts.Token);

        Assert.True(r.Cancelled);
        Assert.Equal(2, BackupManifest.Load(_bak).Count);
        var r2 = Run();
        Assert.Equal(3, r2.New);
        Assert.Equal(5, BackupManifest.Load(_bak).Count);
    }

    [Fact]
    public void ReadOnlyBackupCopy_ChangedLater_OldVersionStillMoved()
    {
        Arc("a.jpg", "v1");
        Run();
        File.SetAttributes(Bak("a.jpg"), FileAttributes.ReadOnly);
        Arc("a.jpg", "v2-uzun");
        var r = Run();
        Assert.Empty(r.Errors);
        Assert.Equal("v2-uzun", File.ReadAllText(Bak("a.jpg")));
        Assert.Equal("v1", File.ReadAllText(Bak(@"_EskiSurumler\2026-11-07\a.jpg")));
    }

    [Fact]
    public void Verify_FindsCorruptAndMissing()
    {
        Arc("a.jpg", "aaaa");
        Arc(@"x\b.jpg", "bb");
        Arc("c.jpg", "cc");
        Arc("d.jpg", "v1");
        Run();
        Arc("d.jpg", "v2-uzun");
        Run(); // _EskiSurumler'e bir dosya düşer; doğrulamada sayılmamalı
        File.WriteAllText(Bak("a.jpg"), "aaXa");
        File.Delete(Bak(@"x\b.jpg"));
        File.WriteAllText(Bak("yabanci.txt"), "?");

        var v = New().Verify(_bak);

        Assert.Equal(2, v.Ok);
        Assert.Equal(1, v.NotInManifest);
        Assert.Contains(v.Problems, p => p.Entry.Path == "a.jpg" && p.Status == VerifyStatus.Corrupt);
        Assert.Contains(v.Problems, p => p.Entry.Path == @"x\b.jpg" && p.Status == VerifyStatus.Missing);
        Assert.Equal(2, v.Problems.Count);
    }

    [Fact]
    public void Repair_MovesCorruptToBozuk_RecopiesMissing_ThenVerifyClean()
    {
        Arc("a.jpg", "aaaa");
        Arc(@"x\b.jpg", "bb");
        Run();
        File.WriteAllText(Bak("a.jpg"), "aaXa");
        File.Delete(Bak(@"x\b.jpg"));
        var b = New();
        var v = b.Verify(_bak);

        var r = b.Repair(_arc, _bak, v);

        Assert.Equal(2, r.Repaired);
        Assert.Empty(r.NotRepaired);
        Assert.Equal("aaaa", File.ReadAllText(Bak("a.jpg")));
        Assert.Equal("bb", File.ReadAllText(Bak(@"x\b.jpg")));
        Assert.Equal("aaXa", File.ReadAllText(Bak(@"_Bozuk\2026-11-07\a.jpg")));
        var again = b.Verify(_bak);
        Assert.Empty(again.Problems);
        Assert.Equal(2, again.Ok);
    }

    [Fact]
    public void Repair_ArchiveChanged_NotRepaired_Untouched()
    {
        Arc("a.jpg", "aaaa");
        Run();
        File.WriteAllText(Bak("a.jpg"), "aaXa");
        Arc("a.jpg", "yeni-icerik");
        var b = New();

        var r = b.Repair(_arc, _bak, b.Verify(_bak));

        Assert.Equal(0, r.Repaired);
        Assert.Single(r.NotRepaired);
        Assert.Equal("aaXa", File.ReadAllText(Bak("a.jpg")));
        Assert.False(Directory.Exists(Bak("_Bozuk")));
    }

    // ---- Gözden geçirme bulguları ----

    [Fact]
    public void UserPartFiles_AreBackedUpAndKept()
    {
        Arc("video.mp4.part", "indirilen");
        Arc("x.jpg", "foto");
        Arc("x.jpg.part", "kullanicinin");
        Run();
        var r = Run();
        Assert.Empty(r.Errors);
        Assert.Equal(3, r.Unchanged);
        Assert.Equal("indirilen", File.ReadAllText(Bak("video.mp4.part")));
        Assert.Equal("foto", File.ReadAllText(Bak("x.jpg")));
        Assert.Equal("kullanicinin", File.ReadAllText(Bak("x.jpg.part")));
    }

    [Fact]
    public void DriveRoot_IsNormalized_AndNestingDetected()
    {
        Assert.Equal(@"E:\", ArchiveBackup.Norm(@"E:\"));
        Assert.Equal(@"D:\Arsiv", ArchiveBackup.Norm(@"D:\Arsiv\"));
        Assert.True(ArchiveBackup.Nested(ArchiveBackup.Norm(@"E:\"), ArchiveBackup.Norm(@"E:\Yedek")));
        Assert.False(ArchiveBackup.Nested(@"D:\Arsiv", @"D:\Arsiv2"));
    }

    [Fact]
    public void LockedSource_ChangedFile_ErrorButCurrentBackupStays()
    {
        Arc("a.jpg", "v1");
        Arc("b.jpg", "b1");
        Run();
        var a = Arc("a.jpg", "v2-uzun");
        Arc("b.jpg", "b2-uzun");
        BackupResult r;
        using (new FileStream(a, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            r = Run();
        Assert.Single(r.Errors);
        Assert.Equal("v1", File.ReadAllText(Bak("a.jpg")));
        Assert.False(File.Exists(Bak(@"_EskiSurumler\2026-11-07\a.jpg")));
        Assert.Equal("b2-uzun", File.ReadAllText(Bak("b.jpg")));

        var again = Run();
        Assert.Empty(again.Errors);
        Assert.Equal("v2-uzun", File.ReadAllText(Bak("a.jpg")));
    }

    [Fact]
    public void ReadOnlySource_IsBackedUp_AndChangesFollow()
    {
        var a = Arc("a.jpg", "v1");
        File.SetAttributes(a, FileAttributes.ReadOnly);
        Assert.Empty(Run().Errors);
        Arc("a.jpg", "v2-uzun");
        File.SetAttributes(a, FileAttributes.ReadOnly);
        var r = Run();
        Assert.Empty(r.Errors);
        Assert.Equal("v2-uzun", File.ReadAllText(Bak("a.jpg")));
    }

    [Fact]
    public void LongPaths_NoCrash()
    {
        var rel = Path.Combine(new string('k', 90), new string('l', 90), new string('m', 60) + ".jpg");
        Arc(rel, "v1");
        Assert.Empty(Run().Errors);
        Arc(rel, "v2-uzun");
        var r = Run();
        Assert.Empty(r.Errors);
        Assert.True(Path.Combine(_bak, @"_EskiSurumler\2026-11-07", rel).Length > 260);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(_bak, @"_EskiSurumler\2026-11-07", rel)));
    }

    [Fact]
    public void Cancel_DuringLargeFileCopy_LeavesNothingHalfDone()
    {
        Arc("buyuk.mp4", new string('v', 3 << 20));
        using var cts = new CancellationTokenSource();
        var b = New();
        b.OnChunk = _ => cts.Cancel();
        var r = b.Run(_arc, _bak, new BackupOptions(), null, cts.Token);
        Assert.True(r.Cancelled);
        Assert.False(File.Exists(Bak("buyuk.mp4")));
        Assert.Empty(Directory.GetFiles(_bak, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(BackupManifest.FileName)));
        Assert.False(BackupManifest.Load(_bak).TryGet("buyuk.mp4", out _));
    }

    [Fact]
    public void IdenticalFileAlreadyInBackup_IsAdopted_NotDuplicated()
    {
        Arc(@"x\a.jpg", "aynisi");
        Directory.CreateDirectory(Bak("x"));
        File.WriteAllText(Bak(@"x\a.jpg"), "aynisi");
        var r = Run();
        Assert.Equal(1, r.New);
        Assert.False(Directory.Exists(Bak("_EskiSurumler")));
        Assert.True(BackupManifest.Load(_bak).TryGet(@"x\a.jpg", out _));
    }

    [Fact]
    public void HiddenSystemFolder_IsSkipped()
    {
        Arc(@"System Volume Information\x.dat", "sistem");
        File.SetAttributes(Path.Combine(_arc, "System Volume Information"), FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);
        Arc("a.jpg", "a");
        var r = Run();
        Assert.Empty(r.Errors);
        Assert.Equal(1, r.New);
        Assert.False(File.Exists(Bak(@"System Volume Information\x.dat")));
    }

    [Fact]
    public void Repair_UpdatesSize_NoEndlessCorruptLoop()
    {
        Arc("a.jpg", "aaaa");
        Run();
        var m = BackupManifest.Load(_bak);
        m.TryGet("a.jpg", out var e);
        m.Set(e with { Size = e.Size + 1 });
        m.Save();
        var b = New();
        var v = b.Verify(_bak);
        Assert.Single(v.Problems);
        Assert.Equal(1, b.Repair(_arc, _bak, v).Repaired);
        Assert.Empty(b.Verify(_bak).Problems);
    }

    [Fact]
    public void ManifestLockedMidRun_RecordedAsError_NoCrash()
    {
        for (int i = 0; i < 3; i++) Arc($"f{i}.jpg", "x" + i);
        FileStream? hold = null;
        var p = new SyncProgress(v =>
        {
            if (v.Done == 1) hold = new FileStream(Bak(BackupManifest.FileName), FileMode.Open, FileAccess.Read, FileShare.None);
        });
        BackupResult r;
        try { r = New().Run(_arc, _bak, new BackupOptions { SaveEvery = 1 }, p); }
        finally { hold?.Dispose(); }
        Assert.Contains(r.Errors, e => e.Contains("kaydedilemedi"));
        Assert.Equal(3, Directory.GetFiles(_bak, "f*.jpg").Length);
    }

    [Fact]
    public void CorruptManifest_Blocked()
    {
        Arc("a.jpg", "a");
        File.WriteAllText(Bak(BackupManifest.FileName), "{bozuk");
        var r = Run();
        Assert.NotNull(r.Blocked);
        Assert.False(File.Exists(Bak("a.jpg")));
    }
}
