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
}

/// <summary>Dosya Gezgini'nde klasör açar, eski pencereyi (Klasik görünüm) açar.</summary>
public interface IShell
{
    void OpenFolder(string path);
    /// <summary><paramref name="start"/>: yeni akışta seçili klasörler ve seçenekler; null → boş açılır.</summary>
    void OpenClassic(ClassicStart? start);
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
    ISettingsStore Settings, Func<string, long> FreeSpace, Action<string>? Recycle = null);

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
