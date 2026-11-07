using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>Ana ekrandan Temizle; kabuktaki Klasik görünüm ve Geri Al.</summary>
public class CleanShellTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_cleanshell_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    public CleanShellTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string D(string rel) => Path.Combine(_tmp, rel);

    string Put(string rel, string content, int day)
    {
        var p = D(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "%PDF-1.4 " + content);
        var t = new DateTime(2024, 3, day, 9, 0, 0);
        File.SetCreationTime(p, t); File.SetLastWriteTime(p, t);
        return p;
    }

    [Fact]
    public void Home_StartClean_OpensCleanFlow()
    {
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        Assert.Null(home.LastClean);

        home.StartCleanCommand.Execute(null);

        var flow = Assert.IsType<OrganizeFlowViewModel>(shell.CurrentPage);
        Assert.Equal(FlowKind.Clean, flow.Kind);
        Assert.Empty(flow.Folders.Sources);
    }

    [Fact]
    public void Home_LastClean_Prefills()
    {
        Directory.CreateDirectory(D("klasor"));
        _f.Settings.Stored.AddRecentClean(new[] { D("klasor") }, new[] { D(@"klasor\onemli") }, DateTime.Now);
        _f.Settings.Stored.AddRecent(new[] { D("baska") }, D("arsiv"), DateTime.Now);
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        Assert.NotNull(home.LastClean);
        Assert.Equal(D("baska"), home.LastOrganize!.Sources[0]);

        home.StartCleanCommand.Execute(home.LastClean);

        var flow = (OrganizeFlowViewModel)shell.CurrentPage;
        Assert.Equal(new[] { D("klasor") }, flow.Folders.ExistingSources);
        Assert.Equal(D(@"klasor\onemli"), Assert.Single(flow.Folders.Priority).Path);
    }

    [Fact]
    public void OpenClassic_FromClean_Empty()
    {
        Directory.CreateDirectory(D("klasor"));
        _f.Settings.Stored.AddRecentClean(new[] { D("klasor") }, Array.Empty<string>(), DateTime.Now);
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        home.StartCleanCommand.Execute(home.LastClean);

        shell.OpenClassicCommand.Execute(null);

        Assert.Null(Assert.Single(_f.Shell.Starts));
    }

    [Fact]
    public async Task Shell_Undo_CleanJournal()
    {
        Put(@"klasor\a.pdf", "ayni", 1);
        var copy = Put(@"klasor\alt\a.pdf", "ayni", 5);
        _f.Settings.Stored.FindSimilar = false; _f.Settings.Stored.MarkFlawed = false;
        _f.Settings.Stored.AddRecentClean(new[] { D("klasor") }, Array.Empty<string>(), DateTime.Now);
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        home.StartCleanCommand.Execute(home.LastClean);
        var flow = (OrganizeFlowViewModel)shell.CurrentPage;
        await flow.ScanCommand.ExecuteAsync(null);
        flow.GoToConfirmCommand.Execute(null);
        var journal = flow.Confirm!.JournalPath;
        flow.Confirm.ApplyCommand.Execute(null);
        await flow.ApplyTask!;
        Assert.False(File.Exists(copy));
        shell.GoHomeCommand.Execute(null);

        _f.Files.Journal = journal;
        await shell.UndoCommand.ExecuteAsync(null);

        Assert.True(File.Exists(copy));
        var undo = (OrganizeFlowViewModel)shell.CurrentPage;
        Assert.Equal(Path.Combine(D("klasor"), "_Ayiklananlar"), undo.Apply!.Archive);
    }
}
