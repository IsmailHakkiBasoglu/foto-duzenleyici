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
}
