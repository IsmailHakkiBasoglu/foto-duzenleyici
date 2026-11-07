namespace FotoDuzenleyici.ViewModels;

/// <summary>Klasör seçtirir; vazgeçilirse null.</summary>
public interface IFolderPicker { string? PickFolder(string title, string? initial = null); }

/// <summary>Geri Al için günlük seçtirir ve CSV'nin kaydedileceği yolu sorar; vazgeçilirse null.</summary>
public interface IFilePicker
{
    string? OpenJournal(string? initialDir);
    string? SaveCsv(string suggestedName);
}

/// <summary>Evet/hayır sorusu ve bilgi iletisi.</summary>
public interface IDialogs
{
    bool Confirm(string title, string message);
    void Info(string title, string message);
    /// <summary>Evet / Hayır / İptal sorusu.</summary>
    Answer Ask(string title, string message);
}

/// <summary>Dosya Gezgini'nde klasör açar, eski pencereyi (Klasik görünüm) açar.</summary>
public interface IShell
{
    void OpenFolder(string path);
    /// <summary>Dosyayı Windows'un varsayılan uygulamasında açar; dosyaya dokunmaz.</summary>
    void OpenFile(string path);
    /// <summary><paramref name="start"/>: yeni akışta seçili klasörler ve seçenekler; null → boş açılır.</summary>
    void OpenClassic(ClassicStart? start);
}

/// <summary>Tam ekran karşılaştırma penceresi (klasikteki CompareWindow). Kararlar doğrudan grupların satırlarına yazılır.</summary>
public enum Answer { Yes, No, Cancel }

public interface ICompare
{
    /// <param name="changed">Pencerede bir karar verildikçe çağrılır.</param>
    /// <returns>Kapanırken gösterilen grup; son grupta Enter ile bitirildiyse FinishedAll.</returns>
    (SimilarGroupState Current, bool FinishedAll) Open(IReadOnlyList<SimilarGroupState> groups, int start, ReviewStore store, Action changed);
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings s);
    /// <summary>Dosya vardı ama okunamadı ya da bozuktu; varsayılanlarla başlandı.</summary>
    bool LoadFailed { get; }
}

/// <summary>
/// Pencereye bağlı her şey. <paramref name="FreeSpace"/>: yolun sürücüsündeki boş bayt.
/// <paramref name="Recycle"/>: null → Windows Geri Dönüşüm Kutusu (Executor'a aynen geçer); testler başka eylem verir.
/// </summary>
public sealed record Services(IFolderPicker Folders, IFilePicker Files, IDialogs Dialogs, IShell Shell,
    ISettingsStore Settings, Func<string, long> FreeSpace, Action<string>? Recycle = null,
    CachePaths? Caches = null, Func<TimeSpan, Action, IDisposable>? Schedule = null, ICompare? Compare = null)
{
    public CachePaths CacheFiles => Caches ?? CachePaths.Default;

    /// <summary>İşi geciktirerek çalıştırır (Schedule yoksa hemen). Dönen nesne bekleyeni iptal eder.</summary>
    public IDisposable Later(TimeSpan delay, Action work)
    {
        if (Schedule is not null) return Schedule(delay, work);
        work();
        return Nothing.Instance;
    }

    sealed class Nothing : IDisposable { public static readonly Nothing Instance = new(); public void Dispose() { } }
}

public static class SettingsStoreExtensions
{
    /// <summary>Kaydetmeyi dener; ayar dosyası kilitli ya da salt okunursa false döner. Ayar kaydı asıl işi durdurmamalı.</summary>
    public static bool TrySave(this ISettingsStore store, AppSettings s)
    {
        try { store.Save(s); return true; }
        catch (Exception) { return false; }
    }
}

/// <summary>Klasik pencerenin, yeni akışta seçilenlerle dolu açılması için.</summary>
public sealed record ClassicStart(IReadOnlyList<string> Sources, string Archive, SourceFolderMode SourceFolder,
    bool RenameWithDate, bool AppendSourceToName, bool SeparateRawFolder, bool RecycleSource);

/// <summary>Kararlar ve önbellekler; öntanımlı olarak klasik pencereyle ortak dosyalar.</summary>
public sealed record CachePaths(string Reviews, string Hashes, string Quality)
{
    public static CachePaths Default { get; } = new(ReviewStore.DefaultPath, HashCache.DefaultPath, QualityCache.DefaultPath);
}
