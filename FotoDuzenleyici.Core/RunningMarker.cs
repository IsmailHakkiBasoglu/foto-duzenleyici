namespace FotoDuzenleyici.Core;

/// <summary>Uygulama ya da komut satırı açıkken var olan adlı mutex; kurulum güncellemeden önce kapatılmasını ister.</summary>
public static class RunningMarker
{
    public const string Name = "FotoDuzenleyici.Calisiyor";

    // Süreç boyunca tutulur; kapanınca Windows bırakır. Sahiplik alınmaz, yalnız varlığı önemli.
    static Mutex? _local, _global;

    /// <summary>Yerel ve <c>Global\</c> adlarla açar (tüm kullanıcılar kurulumu başka oturumda da görsün). Hata yok sayılır.</summary>
    public static void Hold()
    {
        try { _local ??= new Mutex(false, Name); } catch { }
        try { _global ??= new Mutex(false, @"Global\" + Name); } catch { }
    }
}
