using System.Diagnostics;
using System.Text.Json;
using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>
/// Temizle gözden geçirme bulguları: aynı fiziksel dosya iki kez sayılmamalı (junction, sistem klasörü),
/// sürücü kökü doğru taranmalı, yarım kalan temizlik bulunmalı, korunan kopya taşımadan önce denetlenmeli.
/// </summary>
public class CleanSafetyTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_cleansafe_" + Guid.NewGuid().ToString("N"));
    public CleanSafetyTests() => Directory.CreateDirectory(_tmp);
    public void Dispose()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(_tmp, "*", SearchOption.AllDirectories))
                File.SetAttributes(d, FileAttributes.Directory);
            Directory.Delete(_tmp, true);
        }
        catch { }
    }

    string Make(string rel, string content)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Scanner_DoesNotFollowJunctions_NorHiddenSystemFolders()
    {
        Make(@"kok\Gercek\a.pdf", "A");
        Make(@"kok\$RECYCLE.BIN\S-1\$Rabc.pdf", "A");
        File.SetAttributes(Path.Combine(_tmp, @"kok\$RECYCLE.BIN"), FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);
        var link = Path.Combine(_tmp, @"kok\Baglanti");
        var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{Path.Combine(_tmp, @"kok\Gercek")}\"")
            { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        Assert.True(Directory.Exists(Path.Combine(link)), "junction oluşturulamadı");

        var files = new Scanner(new ScanOptions { Roots = { Path.Combine(_tmp, "kok") }, IncludedKinds = { FileKind.Pdf } }).Scan();

        Assert.Equal(new[] { Path.Combine(_tmp, @"kok\Gercek\a.pdf") }, files.Select(f => f.Path));
    }

    [Fact]
    public void Scanner_KeepsDriveRootSeparator()
    {
        Assert.Equal(@"E:\", Scanner.Normalize(@"E:\"));
        Assert.Equal(@"E:\Foto", Scanner.Normalize(@"E:\Foto\"));
        Assert.Equal(@"E:\Foto", Scanner.Normalize("E:/Foto/"));
    }

    [Fact]
    public void CleanSession_FindsUnfinishedJournals_InEveryRoot()
    {
        var root2 = Path.Combine(_tmp, "r2");
        var j = Path.Combine(root2, @"_Ayiklananlar\_Gunluk\temizlik-1.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(j)!);
        File.WriteAllText(j, JsonSerializer.Serialize(new JournalEntry
            { Source = Path.Combine(root2, "a.pdf"), Target = Path.Combine(root2, @"_Ayiklananlar\Tekrarlar\a.pdf"), State = "Planned", Move = true }) + "\n");
        Directory.CreateDirectory(Path.Combine(_tmp, "r1"));

        var s = new CleanSession(new[] { Path.Combine(_tmp, "r1"), root2 });

        var (journal, left) = Assert.Single(s.FindUnfinished());
        Assert.Equal(j, journal);
        Assert.Equal(1, left);
    }

    [Fact]
    public void Move_KeeperGone_DuplicateNotMoved()
    {
        var src = Make(@"b\x.pdf", "AYNI");
        var keeper = new MediaFile { Path = Path.Combine(_tmp, @"a\x.pdf"), Kind = FileKind.Pdf }; // diskte yok
        var item = new PlanItem
        {
            File = new MediaFile { Path = src, Kind = FileKind.Pdf }, DuplicateOf = keeper, Action = PlanAction.Duplicate,
            TargetDir = Path.Combine(_tmp, @"_Ayiklananlar\Tekrarlar\b"), TargetName = "x.pdf",
        };
        var r = new Executor(Path.Combine(_tmp, "j.jsonl")).Run(new[] { item }, move: true);
        Assert.Equal(1, r.Failed);
        Assert.True(File.Exists(src));
        Assert.Contains("Korunan", r.Errors[0]);
    }

    [Fact]
    public void Undo_MoveDoneButNotLogged_IsStillMovedBack()
    {
        var src = Path.Combine(_tmp, @"a\x.pdf");
        var dst = Make(@"_Ayiklananlar\Tekrarlar\a\x.pdf", "ICERIK");
        var j = Path.Combine(_tmp, "j.jsonl");
        // Süreç File.Move ile "Moved" satırı arasında öldü: son satır Started.
        File.WriteAllText(j,
            JsonSerializer.Serialize(new JournalEntry { Source = src, Target = dst, State = "Planned", Move = true }) + "\n" +
            JsonSerializer.Serialize(new JournalEntry { Source = src, Target = dst, State = "Started", Move = true }) + "\n");

        var u = Executor.Undo(j);

        Assert.Equal(1, u.Done);
        Assert.Equal("ICERIK", File.ReadAllText(src));
    }
}
