using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Düzenleme oturumu: birden çok kaynak + arşiv; arşiv her zaman taranır.</summary>
public class OrganizeSessionTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_session_" + Guid.NewGuid().ToString("N"));
    public OrganizeSessionTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Make(string rel, string content, DateTime time)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        File.SetCreationTime(p, time);
        File.SetLastWriteTime(p, time);
        return p;
    }

    static readonly HashSet<FileKind> Pdf = new() { FileKind.Pdf };

    OrganizeSession Session(string[] sources, string archive, bool renameWithDate = false) =>
        new(sources.Select(s => Path.Combine(_tmp, s)),
            new PlanOptions { DestinationRoot = Path.Combine(_tmp, archive), RenameWithDate = renameWithDate },
            new ScanOptions { IncludedKinds = Pdf });

    [Fact]
    public void ScanRoots_AddsArchive_AndDropsNestedOnes()
    {
        Assert.Equal(new[] { @"E:\DCIM", @"D:\Arsiv" }, OrganizeSession.ScanRoots(new[] { @"E:\DCIM" }, @"D:\Arsiv"));
        Assert.Equal(new[] { @"D:\A", @"E:\B" },
            OrganizeSession.ScanRoots(new[] { @"D:\A", @"D:\A\alt\", @"E:\B" }, @"D:\A\_DUZENLI"));
        Assert.Equal(new[] { @"D:\Arsiv" }, OrganizeSession.ScanRoots(new[] { @"D:\Arsiv\gelen" }, @"D:\Arsiv"));
        Assert.Equal(new[] { @"D:\Arsiv2", @"D:\Arsiv" }, OrganizeSession.ScanRoots(new[] { @"D:\Arsiv2" }, @"D:\Arsiv"));
    }

    [Fact]
    public void CopyOfArchivedFile_FromAnotherSession_IsDuplicate_ArchiveKeeps()
    {
        var t = new DateTime(2024, 5, 6, 7, 8, 9);
        // Önceki oturumda arşive girmiş dosya (yerinde, düzenli adıyla).
        var placed = Make(@"arsiv\2024-05\2024-05-Pdf\rapor.pdf", "AYNI", t);
        Make(@"gelen\telefon\rapor-kopya.pdf", "AYNI", t);

        var s = Session(new[] { "gelen" }, "arsiv");
        s.Scan();
        var plan = s.BuildPlan();

        Assert.Equal(2, s.Files.Count);
        Assert.Single(s.Duplicates);
        Assert.Equal(PlanAction.AlreadyInPlace, plan.Items.Single(i => i.File.Path == placed).Action);
        var dup = plan.Items.Single(i => i.File.Path.Contains("gelen"));
        Assert.Equal(PlanAction.Duplicate, dup.Action);
        Assert.Equal(placed, dup.DuplicateOf!.Path);
    }

    [Fact]
    public void ArchiveNotCreatedYet_ScanWorks_WithoutErrors()
    {
        Make(@"gelen\a.pdf", "A", new DateTime(2024, 1, 2));
        var s = Session(new[] { "gelen" }, "yok_arsiv");
        s.Scan();
        Assert.Single(s.Files);
        Assert.Empty(s.ScanErrors);
    }

    [Fact]
    public void ArchiveInsideSource_FilesNotScannedTwice()
    {
        var t = new DateTime(2024, 1, 2);
        Make(@"kok\a.pdf", "A", t);
        Make(@"kok\_DUZENLI\2024-01\2024-01-Pdf\b.pdf", "B", t);
        var s = Session(new[] { "kok" }, @"kok\_DUZENLI");
        s.Scan();
        Assert.Equal(2, s.Files.Count);
        Assert.Empty(s.Duplicates);
    }

    [Fact]
    public void FilesDroppedIntoArchiveByHand_GetSameNaming_WithoutDoubleDate()
    {
        var t = new DateTime(2024, 5, 6, 7, 8, 9);
        Make(@"arsiv\2024-05-06 notlar.pdf", "N", t);
        Make(@"arsiv\belge.pdf", "B", t);
        Make(@"gelen\x.pdf", "X", t);

        var s = Session(new[] { "gelen" }, "arsiv", renameWithDate: true);
        s.Scan();
        var names = s.BuildPlan().Items.Select(i => i.TargetName).OrderBy(n => n).ToList();

        Assert.Equal(new[] { "2024-05-06 belge.pdf", "2024-05-06 notlar.pdf", "2024-05-06 x.pdf" }, names);
    }
}
