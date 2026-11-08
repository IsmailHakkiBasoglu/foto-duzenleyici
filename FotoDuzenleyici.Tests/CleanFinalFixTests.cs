using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>Temizle son gözden geçirme bulguları (her biri önce kırmızı).</summary>
public class CleanFinalFixTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_cleanfix_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    public CleanFinalFixTests() => Directory.CreateDirectory(_tmp);
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

    // Önemli 1: düzenli arşiv temizlenirken Düzenle'nin _Tekrarlar / _Benzerler / _Kusurlu klasörleri taranmaz;
    // tarih klasöründeki asıl dosya, _Tekrarlar'daki daha eski kopyası yüzünden taşınmaz.
    [Fact]
    public async Task Clean_Archive_SkipsOrganizeRejectFolders()
    {
        var organized = Put(@"arsiv\2024-05\2024-05-Pdf\x.pdf", "ayni", day: 9);
        Put(@"arsiv\_Tekrarlar\2024-05\x.pdf", "ayni", day: 1);
        Put(@"arsiv\_Benzerler\2024-05\y.pdf", "y", day: 1);
        Put(@"arsiv\_Kusurlu\2024-05\z.pdf", "z", day: 1);
        _f.Settings.Stored.FindSimilar = false; _f.Settings.Stored.MarkFlawed = false;
        var vm = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored, FlowKind.Clean);
        vm.Folders.Load(new RecentPair(new List<string> { D("arsiv") }, "", DateTime.Now));

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Empty(vm.Review!.Plan.Items);
        Assert.DoesNotContain(vm.Review.Job.Files, f => f.Path.Contains(@"\_Tekrarlar\") || f.Path.Contains(@"\_Benzerler\") || f.Path.Contains(@"\_Kusurlu\"));
        Assert.True(File.Exists(organized));
    }

    async Task<(ShellViewModel Shell, string Journal, string Copy)> StoppedClean()
    {
        Put(@"klasor\a.pdf", "bir", 1);
        var a2 = Put(@"klasor\x\a.pdf", "bir", 5);
        Put(@"klasor\b.pdf", "iki", 1);
        Put(@"klasor\x\b.pdf", "iki", 5);
        _f.Settings.Stored.FindSimilar = false; _f.Settings.Stored.MarkFlawed = false;
        _f.Settings.Stored.AddRecentClean(new[] { D("klasor") }, Array.Empty<string>(), DateTime.Now);
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        home.StartCleanCommand.Execute(home.LastClean);
        var flow = (OrganizeFlowViewModel)shell.CurrentPage;
        await flow.ScanCommand.ExecuteAsync(null);
        flow.GoToConfirmCommand.Execute(null);
        var journal = flow.Confirm!.JournalPath;
        flow.OnApplyProgressForTest = done => { if (done >= 1) flow.Apply?.CancelCommand.Execute(null); };
        flow.Confirm.ApplyCommand.Execute(null);
        await flow.ApplyTask!;
        Assert.True(flow.Apply!.Stopped);
        shell.GoHomeCommand.Execute(null);
        return (shell, journal, a2);
    }

    // Önemli 2: temizlik günlüğünde Geri Al metni "Geri Dönüşüm Kutusu" demez.
    [Fact]
    public async Task Shell_Undo_CleanJournal_TextSaysNothingDeleted()
    {
        var (shell, journal, _) = await StoppedClean();
        _f.Files.Journal = journal;
        _f.Dialogs.Asked.Clear();

        await shell.UndoCommand.ExecuteAsync(null);

        var asked = Assert.Single(_f.Dialogs.Asked);
        Assert.DoesNotContain("Geri Dönüşüm", asked);
        Assert.Contains("Ayıklananlar eski yerlerine taşınır; hiçbir şey silinmez.", asked);
    }

    [Fact]
    public async Task ApplyStep_Undo_CleanText()
    {
        var (shell, journal, _) = await StoppedClean();
        var a = new ApplyStepViewModel(_f.Services, Path.Combine(D("klasor"), "_Ayiklananlar"));
        // Bitti ekranındaki Geri Al: Journal yalnızca bir işten sonra dolu; doğrudan metin kuralını sına.
        Assert.Contains("hiçbir şey silinmez", ApplyStepViewModel.UndoText(journal));
        Assert.Contains("Geri Dönüşüm", ApplyStepViewModel.UndoText(Path.Combine(D("arsiv"), "_Gunluk", "islem-1.jsonl")));
        Assert.True(a.IsClean);
    }

    // Küçük 2 → önemli: durdurulmuş temizlik kabuktan geri alınınca kalanlar "yarım işlem" olarak önerilmez.
    [Fact]
    public async Task Shell_Undo_StoppedClean_LeavesNothingPending()
    {
        var (shell, journal, copy) = await StoppedClean();
        _f.Files.Journal = journal;

        await shell.UndoCommand.ExecuteAsync(null);

        Assert.Empty(Executor.Pending(journal));
        Assert.True(File.Exists(copy));
    }

    // Küçük 4 → önemli: ayar dosyasında bozuk Temizle listesi açılışı çökertmez.
    [Fact]
    public void Settings_CorruptRecentClean_Loads()
    {
        var path = D("ayarlar.json");
        File.WriteAllText(path, "{\"RecentClean\":[null,{\"Sources\":null,\"Archive\":\"\",\"Used\":\"2026-11-08T00:00:00\"}]}");
        var s = new JsonSettingsStore(path).Load();
        Assert.Empty(s.RecentClean);
        File.WriteAllText(path, "{\"RecentClean\":null}");
        Assert.NotNull(new JsonSettingsStore(path).Load().RecentClean);
        Assert.Null(new HomeViewModel(new JsonSettingsStore(path).Load()).LastClean);
    }
}
