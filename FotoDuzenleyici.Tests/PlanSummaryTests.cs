using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

public class PlanSummaryTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_plansum_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static PlanItem Item(PlanAction a, long size, string targetDir, bool renamed = false) => new()
    {
        File = new MediaFile { Path = @"C:\k\" + Guid.NewGuid().ToString("N") + ".jpg", Size = size, Kind = FileKind.Photo },
        Action = a, TargetDir = targetDir, TargetName = "x.jpg", Renamed = renamed,
    };

    [Fact]
    public void PlanSummary_CountsAndBytes()
    {
        var opt = new PlanOptions { DestinationRoot = @"D:\Arsiv" };
        var plan = new Plan();
        plan.Items.Add(Item(PlanAction.Move, 100, @"D:\Arsiv\2024-03\2024-03-Foto"));
        plan.Items.Add(Item(PlanAction.Move, 50, @"D:\Arsiv\Tarihsiz", renamed: true));
        plan.Items.Add(Item(PlanAction.Duplicate, 30, @"D:\Arsiv\_Tekrarlar\2024-03"));
        plan.Items.Add(Item(PlanAction.AlreadyInPlace, 999, @"D:\Arsiv\2024-01\2024-01-Foto"));

        var s = PlanSummary.From(plan, opt);

        Assert.Equal(2, s.MoveCount);
        Assert.Equal(150, s.MoveBytes);
        Assert.Equal(1, s.InPlace);
        Assert.Equal(1, s.DupCount);
        Assert.Equal(30, s.DupBytes);
        Assert.Equal(1, s.Undated);
        Assert.Equal(1, s.Renamed);
        Assert.Equal(3, s.ApplyCount);
    }

    [Fact]
    public void PlanCsv_WritesHeaderAndRows()
    {
        Directory.CreateDirectory(_tmp);
        var plan = new Plan();
        plan.Items.Add(Item(PlanAction.Move, 1, @"D:\Arsiv\2024-03"));
        var path = Path.Combine(_tmp, "plan.csv");
        PlanCsv.Write(plan, path);
        var lines = File.ReadAllLines(path);
        Assert.Equal("Eylem;Kaynak;Hedef;Tarih;TarihKaynagi;Kamera;Not", lines[0]);
        Assert.StartsWith("Move;C:\\k\\", lines[1]);
        Assert.Contains(@"D:\Arsiv\2024-03\x.jpg", lines[1]);
        Assert.Equal(2, lines.Length);
    }
}
