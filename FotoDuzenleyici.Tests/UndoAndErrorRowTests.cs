using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>Geri Al sonrası boş klasör kalmaması ve Okunamayanlar satırları.</summary>
public class UndoAndErrorRowTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_undo_" + Guid.NewGuid().ToString("N"));
    public UndoAndErrorRowTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string T(string rel) => Path.Combine(_tmp, rel);

    string Make(string rel, string content = "x")
    {
        var p = T(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    PlanItem Item(string source, string targetRel) => new()
    {
        File = new MediaFile { Path = source, Size = 1, Kind = FileKind.Photo },
        Action = PlanAction.Flawed,
        TargetDir = Path.GetDirectoryName(T(targetRel))!,
        TargetName = Path.GetFileName(targetRel),
    };

    string CleanRun(params PlanItem[] items)
    {
        var journal = T(@"kok\_Ayiklananlar\_Gunluk\temizlik-1.jsonl");
        Assert.Equal(items.Length, new Executor(journal).Run(items, move: true).Done);
        return journal;
    }

    [Fact]
    public void Undo_Clean_RemovesRejectsFolderCompletely()
    {
        var src = Make(@"kok\a\b.jpg");
        var journal = CleanRun(Item(src, @"kok\_Ayiklananlar\Kusurlu\a\b.jpg"));

        var r = Executor.Undo(journal);

        Assert.Equal(1, r.Done);
        Assert.True(File.Exists(src));
        Assert.False(Directory.Exists(T(@"kok\_Ayiklananlar")));
        Assert.True(Directory.Exists(T("kok")));
    }

    [Fact]
    public void Undo_Clean_KeepsRejectsFolderWhenItHoldsOtherFiles()
    {
        var src = Make(@"kok\a\b.jpg");
        var other = Make(@"kok\_Ayiklananlar\Kusurlu\a\onceki.jpg");
        var journal = CleanRun(Item(src, @"kok\_Ayiklananlar\Kusurlu\a\b.jpg"));

        Executor.Undo(journal);

        Assert.True(File.Exists(other));
        Assert.False(File.Exists(journal));
        Assert.False(Directory.Exists(T(@"kok\_Ayiklananlar\_Gunluk")));
    }

    [Fact]
    public void Undo_Clean_KeepsJournalWhenAFileCouldNotReturn()
    {
        var src = Make(@"kok\a\b.jpg");
        var journal = CleanRun(Item(src, @"kok\_Ayiklananlar\Kusurlu\a\b.jpg"));
        Make(@"kok\a\b.jpg", "sonradan gelen");     // eski yer dolu: geri dönemez

        var r = Executor.Undo(journal);

        Assert.Equal(1, r.Skipped);
        Assert.True(File.Exists(journal));
        Assert.True(File.Exists(T(@"kok\_Ayiklananlar\Kusurlu\a\b.jpg")));
    }

    [Fact]
    public void Undo_Clean_SecondRootRejectsAlsoRemoved()
    {
        var a = Make(@"kok\a.jpg");
        var b = Make(@"kok2\alt\b.jpg");
        var journal = CleanRun(Item(a, @"kok\_Ayiklananlar\Kusurlu\a.jpg"), Item(b, @"kok2\_Ayiklananlar\Kusurlu\alt\b.jpg"));

        Executor.Undo(journal);

        Assert.False(Directory.Exists(T(@"kok\_Ayiklananlar")));
        Assert.False(Directory.Exists(T(@"kok2\_Ayiklananlar")));
        Assert.True(Directory.Exists(T(@"kok2\alt")));
    }

    [Fact]
    public void Undo_RunAgainOnOldJournal_ClearsLeftoverFolders()
    {
        var src = Make(@"kok\a\b.jpg");
        var journal = CleanRun(Item(src, @"kok\_Ayiklananlar\Kusurlu\a\b.jpg"));
        // Önceki sürümün Geri Al'ı: dosya döndü, klasörler kaldı.
        File.Move(T(@"kok\_Ayiklananlar\Kusurlu\a\b.jpg"), src);

        var r = Executor.Undo(journal);

        Assert.Equal(1, r.Skipped);
        Assert.False(Directory.Exists(T(@"kok\_Ayiklananlar")));
    }

    [Fact]
    public void Undo_MissingJournal_DoesNothing()
    {
        var r = Executor.Undo(T(@"yok\islem.jsonl"));
        Assert.Equal(0, r.Done + r.Failed + r.Skipped);
    }

    [Fact]
    public void Undo_Organize_RemovesEmptyDateFolders_KeepsArchiveAndJournal()
    {
        var src = Make(@"kaynak\a.jpg", "icerik");
        var journal = T(@"arsiv\_Gunluk\islem-1.jsonl");
        var item = Item(src, @"arsiv\2026-01\2026-01-Foto\a.jpg");
        Assert.Equal(1, new Executor(journal, File.Delete).Run(new[] { item }).Done);

        var r = Executor.Undo(journal, recycle: File.Delete);

        Assert.Equal(1, r.Done);
        Assert.True(File.Exists(src));
        Assert.False(Directory.Exists(T(@"arsiv\2026-01")));
        Assert.True(File.Exists(journal));
    }

    [Fact]
    public void ErrorRow_FileInScan_HasFrameAndMessage()
    {
        var f = new MediaFile { Path = @"D:\k\a.JPG", Kind = FileKind.Photo };
        var row = ScanErrorRow.Create(@"D:\k\a.JPG: düz görüntü", new[] { f });

        Assert.Same(f, row.Frame!.File);
        Assert.Equal("a.JPG", row.Name);
        Assert.Equal("düz görüntü", row.Message);
        Assert.Equal(@"D:\k\a.JPG", row.Path);
    }

    [Theory]
    [InlineData(@"D:\k\klasor: Erişim engellendi")]
    [InlineData("Önbellek kaydedilemedi: disk dolu")]
    public void ErrorRow_NotAScannedFile_HasNoFrame(string error)
    {
        var row = ScanErrorRow.Create(error, Array.Empty<MediaFile>());
        Assert.Null(row.Frame);
        Assert.Equal(error, row.Text);
    }

    [Fact]
    public void Review_OpenError_OpensOnlyFilesFromScan()
    {
        var fakes = new Fakes();
        var session = new OrganizeSession(Array.Empty<string>(), new PlanOptions { DestinationRoot = _tmp });
        session.Files.Add(new MediaFile { Path = @"D:\k\a.jpg", Kind = FileKind.Photo });
        var vm = new ReviewStepViewModel(new OrganizeJob(session), new Plan(), fakes.Services,
            ReviewData.Empty(new ReviewStore(T("k.json")), new[] { @"D:\k\a.jpg: düz görüntü", "Önbellek kaydedilemedi: x" }));

        Assert.Equal(2, vm.ErrorRows.Count);
        vm.OpenErrorFileCommand.Execute(vm.ErrorRows[1]);
        Assert.Empty(fakes.Shell.Files);
        vm.OpenErrorFileCommand.Execute(vm.ErrorRows[0]);
        Assert.Equal(new[] { @"D:\k\a.jpg" }, fakes.Shell.Files);
    }
}
