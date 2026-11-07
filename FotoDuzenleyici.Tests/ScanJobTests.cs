using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>İş türü katmanı: Düzenle (OrganizeJob) ve Temizle (CleanJob).</summary>
public class ScanJobTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_job_" + Guid.NewGuid().ToString("N"));
    public ScanJobTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Put(string rel, string content, int day)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        var t = new DateTime(2024, 3, day, 10, 0, 0);
        File.SetCreationTime(p, t); File.SetLastWriteTime(p, t);
        return p;
    }

    string D(string rel) => Path.Combine(_tmp, rel);

    [Fact]
    public void CleanJob_ScanAndPlan_OnlyRejects()
    {
        Put(@"klasor\a.pdf", "AYNI", 1);
        var copy = Put(@"klasor\alt\a.pdf", "AYNI", 5);
        Put(@"klasor\b.pdf", "FARKLI", 2);
        var job = new CleanJob(new CleanSession(new[] { D("klasor") }));
        Assert.Equal(FlowKind.Clean, job.Kind);

        job.Scan(null, null, default);
        var plan = job.BuildPlan(null, null, null);

        var item = Assert.Single(plan.Items);
        Assert.Equal(PlanAction.Duplicate, item.Action);
        Assert.Equal(copy, item.File.Path);
        Assert.Equal(Path.Combine(D("klasor"), "_Ayiklananlar", "Tekrarlar", "alt", "a.pdf"), item.TargetPath);
        Assert.True(job.MoveOnly);
        Assert.Null(job.PlanOptions);
        Assert.Empty(job.IgnoreDirs);
    }

    [Fact]
    public void CleanJob_PreferredFolderKept()
    {
        var old = Put(@"klasor\a.pdf", "AYNI", 1);
        var pref = Put(@"klasor\onemli\a.pdf", "AYNI", 5);
        var job = new CleanJob(new CleanSession(new[] { D("klasor") }, new[] { D(@"klasor\onemli") }));
        job.Scan(null, null, default);

        Assert.True(job.IsPreferred(job.Files.Single(f => f.Path == pref)));
        Assert.False(job.IsPreferred(job.Files.Single(f => f.Path == old)));
        Assert.Equal(old, Assert.Single(job.BuildPlan(null, null, null).Items).File.Path);
    }

    [Fact]
    public void CleanJob_JournalAndRejectsFolder()
    {
        var job = new CleanJob(new CleanSession(new[] { D("klasor"), D("ikinci") }));
        Assert.Equal(Path.Combine(D("klasor"), "_Ayiklananlar"), job.RejectsFolder);
        Assert.Equal(Path.Combine(D("klasor"), "_Ayiklananlar", "_Gunluk", "temizlik-20261108-093005.jsonl"),
            job.NewJournalPath(new DateTime(2026, 11, 8, 9, 30, 5)));
        Assert.Empty(job.FindUnfinished());
    }

    [Fact]
    public void OrganizeJob_MatchesSession()
    {
        Put(@"kaynak\a.pdf", "AYNI", 1);
        Put(@"kaynak\alt\a.pdf", "AYNI", 5);
        Directory.CreateDirectory(D("arsiv"));
        var opt = new PlanOptions { DestinationRoot = D("arsiv") };
        var session = new OrganizeSession(new[] { D("kaynak") }, opt);
        var job = new OrganizeJob(session);
        Assert.Equal(FlowKind.Organize, job.Kind);
        job.Scan(null, null, default);

        var a = job.BuildPlan(null, null, null).Items.Select(i => (i.File.Path, i.Action, i.TargetPath));
        var b = session.BuildPlan().Items.Select(i => (i.File.Path, i.Action, i.TargetPath));
        Assert.Equal(b, a);
        Assert.False(job.MoveOnly);
        Assert.Same(opt, job.PlanOptions);
        Assert.Equal(D("arsiv"), job.RejectsFolder);
        Assert.Equal(ReviewBuilder.IgnoreDirs(opt), job.IgnoreDirs);
        Assert.Equal(Path.Combine(D("arsiv"), "_Gunluk", "islem-20261108-093005.jsonl"),
            job.NewJournalPath(new DateTime(2026, 11, 8, 9, 30, 5)));
        Assert.Equal(new[] { D("kaynak") }, job.Sources);
    }
}
