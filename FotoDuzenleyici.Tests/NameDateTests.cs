using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Adında zaten tarih ya da model adı olan dosyalara ikinci kez eklenmez.</summary>
public class NameDateTests
{
    static string Target(string name, DateTime taken, string? source = null, bool date = true, bool appendSource = false)
    {
        var f = new MediaFile { Path = @"C:\gelen\" + name, Kind = FileKind.Photo, Taken = taken, Source = source };
        var plan = Planner.Build(new[] { f }, new List<DuplicateGroup>(),
            new PlanOptions { DestinationRoot = @"Z:\out_yok", RenameWithDate = date, AppendSourceToName = appendSource });
        return plan.Items[0].TargetName;
    }

    static readonly DateTime Day = new(2024, 3, 12, 10, 15, 0);

    [Theory]
    [InlineData("IMG-20240312-WA0007.jpg")]
    [InlineData("IMG_20240312_101500.jpg")]
    [InlineData("12.03.2024 doğum günü.jpg")]
    [InlineData("12-03-2024 deniz.jpg")]
    [InlineData("2024_03_12 piknik.jpg")]
    [InlineData("Screenshot 2024.03.12 at 10.15.jpg")]
    public void SameDateAlreadyInName_NoPrefix(string name) => Assert.Equal(name, Target(name, Day));

    [Theory]
    [InlineData("12.03.2023 eski.jpg", "2024-03-12 12.03.2023 eski.jpg")]
    [InlineData("IMG_0001.jpg", "2024-03-12 IMG_0001.jpg")]
    [InlineData("v20240312x.jpg", "v20240312x.jpg")]
    public void OtherOrNoDate_PrefixAdded(string name, string expected) => Assert.Equal(expected, Target(name, Day));

    [Theory]
    [InlineData("IMG_0001_Canon.JPG", "IMG_0001_Canon.JPG")]
    [InlineData("Canon IMG_0001.JPG", "Canon IMG_0001.JPG")]
    [InlineData("IMG_0001 canon.JPG", "IMG_0001 canon.JPG")]
    [InlineData("IMG_0001.JPG", "IMG_0001 Canon.JPG")]
    [InlineData("Canonical.JPG", "Canonical Canon.JPG")]
    public void SourceAlreadyInName_NotAppendedAgain(string name, string expected) =>
        Assert.Equal(expected, Target(name, Day, "Canon", date: false, appendSource: true));
}
