using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>Son gözden geçirmenin Önemli bulguları: her biri bir senaryo.</summary>
public class ReviewFixTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_revfix_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly string _bin;
    Action? _onRecycle;

    public ReviewFixTests()
    {
        _bin = Path.Combine(_tmp, "_cop");
        Directory.CreateDirectory(_bin);
        _f.Recycle = p => { File.Move(p, Path.Combine(_bin, Guid.NewGuid().ToString("N"))); _onRecycle?.Invoke(); };
    }

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Src => Path.Combine(_tmp, "kaynak");
    string Arc => Path.Combine(_tmp, "arsiv");

    void Make(string name, string content, int day)
    {
        Directory.CreateDirectory(Src);
        var p = Path.Combine(Src, name);
        File.WriteAllText(p, content);
        var t = new DateTime(2024, 3, day);
        File.SetCreationTime(p, t); File.SetLastWriteTime(p, t);
    }

    (ShellViewModel shell, OrganizeFlowViewModel flow) Start()
    {
        Directory.CreateDirectory(Arc);
        _f.Settings.Stored.AddRecent(new[] { Src }, Arc, DateTime.Now);
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        home.StartOrganizeCommand.Execute(home.LastOrganize);
        return (shell, (OrganizeFlowViewModel)shell.CurrentPage);
    }

    async Task<OrganizeFlowViewModel> ToConfirm(OrganizeFlowViewModel flow)
    {
        await flow.ScanCommand.ExecuteAsync(null);
        flow.GoToConfirmCommand.Execute(null);
        return flow;
    }

    // 1 — ikinci X, "o anki dosyayı bitir" beklemesini atlamamalı
    [Fact]
    public async Task Close_SecondRequestWhileWaiting_DoesNotCloseOrAskAgain()
    {
        Make("a.pdf", "A", 12); Make("b.pdf", "B", 13);
        var (shell, flow) = Start();
        await ToConfirm(flow);
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        _onRecycle = () => { entered.Set(); gate.Wait(TimeSpan.FromSeconds(10)); };
        flow.Confirm!.ApplyCommand.Execute(null);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        _f.Dialogs.Answer = true;
        Assert.Equal(CloseDecision.WaitThenClose, shell.RequestClose());
        Assert.Equal(CloseDecision.Stay, shell.RequestClose());
        Assert.Single(_f.Dialogs.Asked);

        _onRecycle = null; gate.Set();
        await flow.ApplyTask!;
        Assert.Equal(CloseDecision.Close, shell.RequestClose());
    }

    [Fact]
    public async Task Close_Declined_Stays()
    {
        Make("a.pdf", "A", 12);
        var (shell, flow) = Start();
        await ToConfirm(flow);
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        _onRecycle = () => { entered.Set(); gate.Wait(TimeSpan.FromSeconds(10)); };
        flow.Confirm!.ApplyCommand.Execute(null);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        _f.Dialogs.Answer = false;
        Assert.Equal(CloseDecision.Stay, shell.RequestClose());
        Assert.False(flow.Apply!.IsCancellationRequested);
        gate.Set();
        await flow.ApplyTask!;
    }

    // 2 — uygulama sürerken Klasik görünüm açılmamalı (aynı günlükte ikinci Executor)
    [Fact]
    public async Task OpenClassic_BlockedWhileApplying()
    {
        Make("a.pdf", "A", 12);
        var (shell, flow) = Start();
        await ToConfirm(flow);
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        _onRecycle = () => { entered.Set(); gate.Wait(TimeSpan.FromSeconds(10)); };
        flow.Confirm!.ApplyCommand.Execute(null);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        shell.OpenClassicCommand.Execute(null);
        Assert.Equal(0, _f.Shell.Classic);
        Assert.NotNull(shell.ErrorMessage);

        gate.Set();
        await flow.ApplyTask!;
    }

    // 3a — boş alan okunamıyor (UNC, takılı olmayan disk): çökmez, "bilinmiyor" der, Uygula'yı engellemez
    [Fact]
    public async Task FreeSpace_Throws_UnknownNotCrash()
    {
        Make("a.pdf", "A", 12);
        _f.FreeFn = _ => throw new ArgumentException("UNC yolu");
        var (_, flow) = Start();
        await ToConfirm(flow);
        Assert.NotNull(flow.Confirm);
        Assert.False(flow.Confirm!.SpaceKnown);
        Assert.True(flow.Confirm.CanApply);
    }

    // 3b — plan.csv Excel'de açık: çökmez, bildirilir
    [Fact]
    public async Task SaveCsv_FileLocked_ReportsInsteadOfThrowing()
    {
        Make("a.pdf", "A", 12);
        var (_, flow) = Start();
        await flow.ScanCommand.ExecuteAsync(null);
        var csv = Path.Combine(_tmp, "plan.csv");
        File.WriteAllText(csv, "eski");
        _f.Files.Csv = csv;
        using (new FileStream(csv, FileMode.Open, FileAccess.Read, FileShare.None))
            flow.Review!.SaveCsvCommand.Execute(null);
        Assert.Single(_f.Dialogs.Infos);
    }

    // 3c — ayar dosyası kilitli: tema / seçenek kaydı çökertmez
    [Fact]
    public void SettingsSaveFails_ThemeAndOptions_DoNotThrow()
    {
        var shell = new ShellViewModel(_f.Services);
        _f.Settings.ThrowOnSave = true;
        shell.Theme = "Koyu";
        Assert.NotNull(shell.ErrorMessage);
        var flow = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored);
        flow.Folders.NotifyOptionsChanged();
    }

    // 4 — son kullanılanlar kaydedilemese de tarama sürer
    [Fact]
    public async Task SettingsSaveFails_ScanStillRuns()
    {
        Make("a.pdf", "A", 12);
        var (_, flow) = Start();
        _f.Settings.ThrowOnSave = true;
        await flow.ScanCommand.ExecuteAsync(null);
        Assert.Null(flow.ErrorMessage);
        Assert.Equal(FlowStep.Review, flow.Step);
    }

    // 5 — durdurulan iş, yeniden taramadan sonuç ekranından devam ettirilebilir
    [Fact]
    public async Task Stopped_ResumeDirectly_WithoutRescan()
    {
        Make("a.pdf", "A", 12); Make("b.pdf", "B", 13); Make("c.pdf", "C", 14);
        var (_, flow) = Start();
        await ToConfirm(flow);
        _onRecycle = () => { flow.Apply?.CancelCommand.Execute(null); _onRecycle = null; };
        flow.Confirm!.ApplyCommand.Execute(null);
        await flow.ApplyTask!;
        Assert.True(flow.Apply!.Stopped);

        await flow.Apply.ResumeCommand.ExecuteAsync(null);

        Assert.Equal(ApplyMode.Resume, flow.Apply.Mode);
        Assert.False(flow.Apply.Stopped);
        Assert.Empty(Directory.GetFiles(Src));
        Assert.Empty(Executor.FindUnfinished(Arc));
    }

    // 6 — durdurulup Geri Al denirse kalanlar "devam et" diye sonra yeniden önerilmemeli
    [Fact]
    public async Task Stopped_ThenUndo_LeavesNoUnfinishedJournal()
    {
        Make("a.pdf", "A", 12); Make("b.pdf", "B", 13); Make("c.pdf", "C", 14);
        var (_, flow) = Start();
        await ToConfirm(flow);
        _onRecycle = () => { flow.Apply?.CancelCommand.Execute(null); _onRecycle = null; };
        flow.Confirm!.ApplyCommand.Execute(null);
        await flow.ApplyTask!;
        Assert.True(flow.Apply!.Stopped);

        _f.Dialogs.Answer = true;
        await flow.Apply.UndoCommand.ExecuteAsync(null);

        Assert.Equal(3, Directory.GetFiles(Src).Length);
        Assert.Empty(Executor.FindUnfinished(Arc));
    }
}
