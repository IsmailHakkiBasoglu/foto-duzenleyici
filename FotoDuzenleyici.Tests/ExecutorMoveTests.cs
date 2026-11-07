using System.Text.Json;
using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>İşlem motorunun taşıma modu (Temizle): yeniden adlandırma, günlük, sürdürme ve Geri Al.</summary>
public class ExecutorMoveTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_move_" + Guid.NewGuid().ToString("N"));
    readonly string _journal;
    public ExecutorMoveTests()
    {
        Directory.CreateDirectory(_tmp);
        _journal = Path.Combine(_tmp, "_Ayiklananlar", "_Gunluk", "temizlik.jsonl");
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static readonly DateTime When = new(2023, 4, 5, 6, 7, 8);

    string Make(string rel, string content)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        File.SetCreationTime(p, When);
        File.SetLastWriteTime(p, When);
        return p;
    }

    PlanItem Item(string src, string targetRel) => new()
    {
        File = new MediaFile { Path = src, Kind = FileKind.Pdf },
        TargetDir = Path.GetDirectoryName(Path.Combine(_tmp, targetRel))!,
        TargetName = Path.GetFileName(targetRel),
        Action = PlanAction.Duplicate,
    };

    [Fact]
    public void Move_RenamesFile_KeepsContentAndTimes_StateMoved()
    {
        var src = Make(@"a\x.pdf", "ICERIK");
        var r = new Executor(_journal).Run(new[] { Item(src, @"_Ayiklananlar\Tekrarlar\a\x.pdf") }, move: true);
        var dst = Path.Combine(_tmp, @"_Ayiklananlar\Tekrarlar\a\x.pdf");
        Assert.Equal(1, r.Done);
        Assert.False(File.Exists(src));
        Assert.Equal("ICERIK", File.ReadAllText(dst));
        Assert.Equal(When, File.GetLastWriteTime(dst));
        Assert.Equal("Moved", Executor.FinalStates(_journal).Single().State);
    }

    [Fact]
    public void Move_TargetExists_NotOverwritten_Failed()
    {
        var src = Make(@"a\x.pdf", "YENI");
        var dst = Make(@"_Ayiklananlar\Tekrarlar\a\x.pdf", "ESKI");
        var r = new Executor(_journal).Run(new[] { Item(src, @"_Ayiklananlar\Tekrarlar\a\x.pdf") }, move: true);
        Assert.Equal(1, r.Failed);
        Assert.Equal("YENI", File.ReadAllText(src));
        Assert.Equal("ESKI", File.ReadAllText(dst));
    }

    [Fact]
    public void Move_Resume_SourceGoneTargetThere_CountsMoved()
    {
        var src = Path.Combine(_tmp, @"a\x.pdf");
        var dst = Make(@"_Ayiklananlar\Tekrarlar\a\x.pdf", "TASINMIS");
        Directory.CreateDirectory(Path.GetDirectoryName(_journal)!);
        File.WriteAllText(_journal, JsonSerializer.Serialize(new JournalEntry
            { Source = src, Target = dst, Action = "Duplicate", State = "Planned", Move = true }) + "\n");

        var r = new Executor(_journal).Resume();

        Assert.Equal(1, r.Done);
        Assert.Equal("Moved", Executor.FinalStates(_journal).Single().State);
    }

    [Fact]
    public void Undo_Moved_MovesBack()
    {
        var src = Make(@"a\x.pdf", "ICERIK");
        new Executor(_journal).Run(new[] { Item(src, @"_Ayiklananlar\Tekrarlar\a\x.pdf") }, move: true);
        var u = Executor.Undo(_journal, recycle: _ => throw new InvalidOperationException("taşımada Geri Dönüşüm Kutusu kullanılmaz"));
        Assert.Equal(1, u.Done);
        Assert.Equal("ICERIK", File.ReadAllText(src));
        Assert.False(File.Exists(Path.Combine(_tmp, @"_Ayiklananlar\Tekrarlar\a\x.pdf")));
    }

    [Fact]
    public void Undo_Moved_SourcePathOccupied_Skipped()
    {
        var src = Make(@"a\x.pdf", "ICERIK");
        new Executor(_journal).Run(new[] { Item(src, @"_Ayiklananlar\Tekrarlar\a\x.pdf") }, move: true);
        File.WriteAllText(src, "SONRADAN GELEN");
        var u = Executor.Undo(_journal);
        Assert.Equal(1, u.Skipped);
        Assert.Equal("SONRADAN GELEN", File.ReadAllText(src));
        Assert.True(File.Exists(Path.Combine(_tmp, @"_Ayiklananlar\Tekrarlar\a\x.pdf")));
    }

    [Fact]
    public void Move_LockedSource_FailsThatFile_OthersContinue()
    {
        var a = Make(@"a.pdf", "A");
        var b = Make(@"b.pdf", "B");
        ExecuteResult r;
        using (new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None))
            r = new Executor(_journal).Run(new[]
            {
                Item(a, @"_Ayiklananlar\Kusurlu\a.pdf"), Item(b, @"_Ayiklananlar\Kusurlu\b.pdf"),
            }, move: true);
        Assert.Equal(1, r.Failed);
        Assert.Equal(1, r.Done);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(Path.Combine(_tmp, @"_Ayiklananlar\Kusurlu\b.pdf")));
    }
}
