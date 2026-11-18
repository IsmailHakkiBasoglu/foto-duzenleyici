using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class MonthReaderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_mr_" + Guid.NewGuid().ToString("N"));
    readonly Dictionary<string, DateTime> _dates = new(StringComparer.OrdinalIgnoreCase);

    public MonthReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    string Put(string rel, DateTime taken)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[] { 1, 2, 3 });
        _dates[Path.GetFileName(p)] = taken;
        return p;
    }

    MediaFile Read(string p) => new()
    {
        Path = p, Size = 3, Kind = FileClassifier.Classify(p),
        Taken = _dates[Path.GetFileName(p)], DateSource = DateSource.Exif,
    };

    string Dir(string rel) => Path.Combine(_root, rel);

    [Fact]
    public void FolderNames_ReadsRecursively_KeepsOtherMonthFiles()
    {
        Put(@"2026-08\2026-08-Foto\a.jpg", new DateTime(2026, 8, 3));
        Put(@"2026-08\2026-08-Foto\x\b.jpg", new DateTime(2026, 7, 30));

        var r = MonthReader.Read(TimelineMethod.FolderNames, 2026, 8, new[] { Dir("2026-08") }, read: Read);

        Assert.Equal(new[] { "a.jpg", "b.jpg" }, r.Files.Select(f => Path.GetFileName(f.Path)).OrderBy(n => n));
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void FolderNames_SkipsSpecialSubfolders()
    {
        Put(@"2026-08\a.jpg", new DateTime(2026, 8, 3));
        Put(@"2026-08\_Tekrarlar\c.jpg", new DateTime(2026, 8, 3));

        var r = MonthReader.Read(TimelineMethod.FolderNames, 2026, 8, new[] { Dir("2026-08") }, read: Read);

        Assert.Equal("a.jpg", Path.GetFileName(Assert.Single(r.Files).Path));
    }

    [Fact]
    public void CaptureDate_DirectFilesOnly_FiltersMonth()
    {
        Put(@"Tatil\a.jpg", new DateTime(2026, 8, 3));
        Put(@"Tatil\b.jpg", new DateTime(2026, 9, 1));
        Put(@"Tatil\sub\c.jpg", new DateTime(2026, 8, 4));

        var r = MonthReader.Read(TimelineMethod.CaptureDate, 2026, 8, new[] { Dir("Tatil") }, read: Read);

        Assert.Equal("a.jpg", Path.GetFileName(Assert.Single(r.Files).Path));
    }

    [Fact]
    public void DuplicateFolders_ReadOnce()
    {
        Put(@"Tatil\a.jpg", new DateTime(2026, 8, 3));

        var r = MonthReader.Read(TimelineMethod.CaptureDate, 2026, 8, new[] { Dir("Tatil"), Dir("Tatil") + "\\" }, read: Read);

        Assert.Single(r.Files);
    }

    [Fact]
    public void ReadError_GoesToErrors_OthersContinue()
    {
        Put(@"2026-08\bozuk.jpg", new DateTime(2026, 8, 3));
        Put(@"2026-08\iyi.jpg", new DateTime(2026, 8, 3));

        var r = MonthReader.Read(TimelineMethod.FolderNames, 2026, 8, new[] { Dir("2026-08") },
            read: p => p.EndsWith("bozuk.jpg") ? throw new IOException("kilitli") : Read(p));

        Assert.Equal("iyi.jpg", Path.GetFileName(Assert.Single(r.Files).Path));
        Assert.Contains("bozuk.jpg", Assert.Single(r.Errors));
    }

    [Fact]
    public void Cancel_ClearsFiles_SetsCancelled()
    {
        Put(@"2026-08\a.jpg", new DateTime(2026, 8, 3));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var r = MonthReader.Read(TimelineMethod.FolderNames, 2026, 8, new[] { Dir("2026-08") }, ct: cts.Token, read: Read);

        Assert.True(r.Cancelled);
        Assert.Empty(r.Files);
    }

    [Fact]
    public void Progress_ReachesTotal()
    {
        Put(@"2026-08\a.jpg", new DateTime(2026, 8, 3));
        Put(@"2026-08\b.jpg", new DateTime(2026, 8, 4));
        var seen = new List<(int, int)>();

        MonthReader.Read(TimelineMethod.FolderNames, 2026, 8, new[] { Dir("2026-08") },
            progress: new SyncProgress<(int, int)>(seen.Add), read: Read);

        Assert.Contains((2, 2), seen);
    }

    sealed class SyncProgress<T>(Action<T> a) : IProgress<T> { public void Report(T v) { lock (this) a(v); } }
}
