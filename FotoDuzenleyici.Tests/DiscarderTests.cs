using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Ay görünümünden atma: Geri Dönüşüm Kutusu ya da _Ayiklananlar, günlükle.</summary>
public class DiscarderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_dis_" + Guid.NewGuid().ToString("N"));
    readonly string _logs;

    public DiscarderTests()
    {
        Directory.CreateDirectory(_root);
        _logs = _root + "_logs";
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { Directory.Delete(_logs, true); } catch { }
    }

    /// <summary>Sahte kutu: Send dosyayı "kutu" klasörüne taşır (gerçek kutuya dokunulmaz).</summary>
    sealed class Bin : IRecycleBin
    {
        public Func<string, long, bool> Can = (_, _) => true;
        public Func<string, bool>? SendFunc;
        public List<string> Sent { get; } = new();
        public bool CanRecycle(string path, long size) => Can(path, size);
        public bool Send(string path)
        {
            Sent.Add(path);
            if (SendFunc is not null) return SendFunc(path);
            File.Delete(path);
            return true;
        }
    }

    MediaFile Put(string rel, int size = 3)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
        return new MediaFile { Path = p, Size = size, Kind = FileClassifier.Classify(p) };
    }

    string Rejects(string rel) => Path.Combine(_root, CleanSession.RejectsFolder, Discarder.SubFolder, rel);

    [Fact]
    public void Plan_BinAccepts_RoutesToRecycle()
    {
        var f = Put(@"2026-08\2026-08-Foto\a.jpg");

        var step = Assert.Single(Discarder.Plan(new[] { f }, _root, new Bin()));

        Assert.Equal(DiscardRoute.RecycleBin, step.Route);
        Assert.Null(step.Target);
    }

    [Fact]
    public void Plan_NoBin_RoutesToRejects()
    {
        var f = Put(@"2026-08\2026-08-Foto\a.jpg");

        var step = Assert.Single(Discarder.Plan(new[] { f }, _root, new Bin { Can = (_, _) => false }));

        Assert.Equal(DiscardRoute.MoveToRejects, step.Route);
        Assert.Equal(Rejects(@"2026-08\2026-08-Foto\a.jpg"), step.Target);
    }

    [Fact]
    public void Plan_PassesSizeToBin()
    {
        var f = Put("a.jpg", size: 7);
        long seen = -1;

        Discarder.Plan(new[] { f }, _root, new Bin { Can = (_, s) => { seen = s; return true; } });

        Assert.Equal(7, seen);
    }

    [Fact]
    public void Plan_TargetExists_PicksFreeName()
    {
        var f = Put(@"2026-08\a.jpg");
        var existing = Rejects(@"2026-08\a.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "eski");

        var step = Assert.Single(Discarder.Plan(new[] { f }, _root, new Bin { Can = (_, _) => false }));

        Assert.Equal(Rejects(@"2026-08\a (2).jpg"), step.Target);
        Assert.Equal("eski", File.ReadAllText(existing));
    }

    [Fact]
    public void Plan_TwoSameNames_GetDistinctTargets()
    {
        var a = new MediaFile { Path = Path.Combine(_root, "2026-08", "a.jpg"), Size = 1 };
        var b = new MediaFile { Path = Path.Combine(_root, "2026-08", "A.JPG"), Size = 1 };

        var steps = Discarder.Plan(new[] { a, b }, _root, new Bin { Can = (_, _) => false });

        Assert.Equal(2, steps.Select(s => s.Target!.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void Plan_OutsideRoot_NoBin_LeftOut()
    {
        var outside = new MediaFile { Path = Path.Combine(Path.GetTempPath(), "baska", "x.jpg"), Size = 1 };

        Assert.Empty(Discarder.Plan(new[] { outside }, _root, new Bin { Can = (_, _) => false }));
    }

    [Fact]
    public void Run_Recycle_CallsSend_WritesRecycleJournal_NoRejectsFolder()
    {
        var f = Put(@"2026-08\a.jpg");
        var bin = new Bin();
        var steps = Discarder.Plan(new[] { f }, _root, bin);

        var r = Discarder.Run(steps, _root, bin, _logs);

        Assert.Equal(f.Path, Assert.Single(bin.Sent));
        Assert.Single(r.Done);
        Assert.Empty(r.Failed);
        Assert.False(Directory.Exists(Path.Combine(_root, CleanSession.RejectsFolder)));
        Assert.NotNull(r.RecycleJournal);
        Assert.StartsWith(Path.GetFullPath(_logs), r.RecycleJournal);
        Assert.Contains("Recycled", File.ReadAllText(r.RecycleJournal!));
        Assert.Null(r.MoveJournal);
    }

    [Fact]
    public void Run_Move_MovesFile_JournalInGunluk_UndoRestores()
    {
        var f = Put(@"2026-08\a.jpg");
        var bin = new Bin { Can = (_, _) => false };
        var steps = Discarder.Plan(new[] { f }, _root, bin);

        var r = Discarder.Run(steps, _root, bin, _logs);

        Assert.Single(r.Done);
        Assert.False(File.Exists(f.Path));
        Assert.True(File.Exists(Rejects(@"2026-08\a.jpg")));
        Assert.Empty(bin.Sent);
        Assert.Equal(Path.Combine(_root, CleanSession.RejectsFolder, "_Gunluk"), Path.GetDirectoryName(r.MoveJournal));

        Executor.Undo(r.MoveJournal!);
        Assert.True(File.Exists(f.Path));
    }

    [Fact]
    public void Run_SendReturnsFalse_CountsFailed_FileStays()
    {
        var f = Put(@"2026-08\a.jpg");
        var bin = new Bin { SendFunc = _ => false };
        var steps = Discarder.Plan(new[] { f }, _root, bin);

        var r = Discarder.Run(steps, _root, bin, _logs);

        Assert.Empty(r.Done);
        Assert.Equal(f.Path, Assert.Single(r.Failed).Step.File.Path);
        Assert.True(File.Exists(f.Path));
        Assert.Contains("Failed", File.ReadAllText(r.RecycleJournal!));
    }

    [Fact]
    public void Run_SendThrows_CountsFailed_OthersContinue()
    {
        var a = Put(@"2026-08\a.jpg");
        var b = Put(@"2026-08\b.jpg");
        var bin = new Bin();
        var steps = Discarder.Plan(new[] { a, b }, _root, bin);
        bin.SendFunc = p => { if (p == a.Path) throw new IOException("kilitli"); File.Delete(p); return true; };

        var r = Discarder.Run(steps, _root, bin, _logs);

        Assert.Equal(b.Path, Assert.Single(r.Done).File.Path);
        Assert.Contains("kilitli", Assert.Single(r.Failed).Error);
    }

    [Fact]
    public void Run_MoveTargetAppearedMeanwhile_Failed_NothingOverwritten()
    {
        var f = Put(@"2026-08\a.jpg");
        var bin = new Bin { Can = (_, _) => false };
        var steps = Discarder.Plan(new[] { f }, _root, bin);
        Directory.CreateDirectory(Path.GetDirectoryName(steps[0].Target)!);
        File.WriteAllText(steps[0].Target!, "sonradan");

        var r = Discarder.Run(steps, _root, bin, _logs);

        Assert.Single(r.Failed);
        Assert.True(File.Exists(f.Path));
        Assert.Equal("sonradan", File.ReadAllText(steps[0].Target!));
    }

    [Fact]
    public void Run_Mixed_DoneHasBoth()
    {
        var a = Put(@"2026-08\a.jpg");
        var big = Put(@"2026-08\b.mp4", size: 50);
        var bin = new Bin { Can = (_, s) => s < 10 };
        var steps = Discarder.Plan(new[] { a, big }, _root, bin);

        var r = Discarder.Run(steps, _root, bin, _logs);

        Assert.Equal(2, r.Done.Count);
        Assert.Contains(r.Done, s => s.Route == DiscardRoute.RecycleBin && s.File.Path == a.Path);
        Assert.Contains(r.Done, s => s.Route == DiscardRoute.MoveToRejects && s.File.Path == big.Path);
        Assert.NotNull(r.MoveJournal);
        Assert.NotNull(r.RecycleJournal);
    }
}
