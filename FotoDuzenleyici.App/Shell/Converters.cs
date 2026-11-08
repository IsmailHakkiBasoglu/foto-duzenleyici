using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FotoDuzenleyici.Core;

namespace FotoDuzenleyici.App.Shell;

/// <summary>Bayt → "214,6 GB".</summary>
sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => Format(System.Convert.ToInt64(value));
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;

    public static string Format(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):N1} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):N1} MB",
        >= 1L << 10 => $"{b / 1024.0:N0} KB",
        _ => $"{b} B",
    };
}

/// <summary>Uzun yolu baştan kısaltır: dosya adı ve hedef klasör hep görünür. Parametre: en fazla karakter.</summary>
sealed class PathTailConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var s = value as string ?? "";
        var max = p is string ps && int.TryParse(ps, out var m) ? m : 70;
        return s.Length <= max ? s : "…" + s[^(max - 1)..];
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Değerin metni parametreye eşitse Visible; "!" ile başlayan parametre tersini verir.</summary>
sealed class EqualsVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var param = p as string ?? "";
        bool not = param.StartsWith('!');
        if (not) param = param[1..];
        bool eq = string.Equals(value?.ToString(), param, StringComparison.Ordinal);
        return eq ^ not ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Enum ↔ RadioButton.IsChecked.</summary>
sealed class EnumBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => string.Equals(value?.ToString(), p as string, StringComparison.Ordinal);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is true && p is string s ? Enum.Parse(t, s) : Binding.DoNothing;
}

/// <summary>null / boş / 0 / false → Collapsed. "!" parametresi tersini verir.</summary>
sealed class HasValueVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool has = value switch
        {
            null => false,
            string s => s.Length > 0,
            bool b => b,
            int i => i != 0,
            System.Collections.ICollection col => col.Count > 0,
            _ => true,
        };
        if (p as string == "!") has = !has;
        return has ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is not true;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is not true;
}

/// <summary>Eylem → Türkçe ad.</summary>
sealed class ActionNameConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is PlanAction a ? PlanPreview.ActionName(a) : "";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>
/// Adım çubuğu: değer = etkin adım (FlowStep), parametre = bu öğenin sırası (0..2; Apply 2 sayılır).
/// Geçmiş → başarı rengi, etkin → vurgu rengi, gelecek → soluk metin.
/// </summary>
sealed class StepBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        int cur = Math.Min(value is ViewModels.FlowStep s ? (int)s : 0, 3);
        if (cur == 3) cur = 2;
        int me = int.Parse((string)p);
        var key = me < cur ? "SystemFillColorSuccessBrush" : me == cur ? "AccentTextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>0..1 kesir → "%62".</summary>
sealed class PercentConverter : IMultiValueConverter
{
    public object Convert(object[] v, Type t, object p, CultureInfo c)
    {
        var done = v.Length > 0 && v[0] is int d ? d : 0;
        var total = v.Length > 1 && v[1] is int tt ? tt : 0;
        return total == 0 ? "%0" : $"%{100 * done / total}";
    }
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Çalışırken başlık.</summary>
sealed class ApplyTitleConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        ViewModels.ApplyMode.Resume => "Devam ediliyor",
        ViewModels.ApplyMode.Undo => "Geri alınıyor",
        _ => "Uygulanıyor",
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>(Mode, Stopped) → bitti başlığı.</summary>
sealed class DoneTitleConverter : IMultiValueConverter
{
    public object Convert(object[] v, Type t, object p, CultureInfo c)
    {
        if (v.Length > 1 && v[1] is true) return "Durduruldu";
        if (v.Length > 0 && v[0] is ViewModels.ApplyMode.Undo) return "Geri alındı";
        return v.Length > 2 && v[2] is true ? "Temizlik bitti" : "Arşiv düzenlendi";
    }
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>TimeSpan? → "~9 dk kaldı".</summary>
sealed class EtaConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is TimeSpan ts
        ? ts.TotalMinutes >= 1 ? $"~{Math.Ceiling(ts.TotalMinutes):N0} dk kaldı" : $"~{Math.Ceiling(ts.TotalSeconds):N0} sn kaldı"
        : "";
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
