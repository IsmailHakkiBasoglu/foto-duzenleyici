using System.Text;

namespace FotoDuzenleyici.Core;

/// <summary>Planı Excel'in açabileceği CSV olarak yazar (noktalı virgül, UTF-8 BOM).</summary>
public static class PlanCsv
{
    public static void Write(Plan plan, string path)
    {
        var sb = new StringBuilder("Eylem;Kaynak;Hedef;Tarih;TarihKaynagi;Kamera;Not\n");
        foreach (var i in plan.Items)
            sb.AppendLine($"{i.Action};{i.File.Path};{i.TargetPath};{i.File.Taken:yyyy-MM-dd HH:mm:ss};{i.File.DateSource};{i.File.Source};{i.Note}");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }
}
