using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>ViewModel testleri için sahte hizmetler: seçiciler sıradaki yanıtı döner, onay sabit yanıtlıdır.</summary>
sealed class FakeFolders : IFolderPicker
{
    public Queue<string?> Next { get; } = new();
    public string? PickFolder(string title, string? initial = null) => Next.Count > 0 ? Next.Dequeue() : null;
}

sealed class FakeFiles : IFilePicker
{
    public string? Journal; public string? Csv;
    public string? OpenJournal(string? initialDir) => Journal;
    public string? SaveCsv(string suggestedName) => Csv;
}

sealed class FakeDialogs : IDialogs
{
    public bool Answer = true;
    public List<string> Asked { get; } = new();
    public List<string> Infos { get; } = new();
    public bool Confirm(string title, string message) { Asked.Add(title + ": " + message); return Answer; }
    public void Info(string title, string message) => Infos.Add(title + ": " + message);
}

sealed class FakeShell : IShell
{
    public List<string> Opened { get; } = new();
    public int Classic;
    public void OpenFolder(string path) => Opened.Add(path);
    public void OpenClassic() => Classic++;
}

sealed class MemorySettings : ISettingsStore
{
    public AppSettings Stored = new();
    public int Saves;
    public bool LoadFailed { get; set; }
    public AppSettings Load() => Stored;
    public bool ThrowOnSave;
    public void Save(AppSettings s) { if (ThrowOnSave) throw new IOException("ayarlar.json kilitli"); Stored = s; Saves++; }
}

sealed class Fakes
{
    public FakeFolders Folders = new();
    public FakeFiles Files = new();
    public FakeDialogs Dialogs = new();
    public FakeShell Shell = new();
    public MemorySettings Settings = new();
    public long Free = long.MaxValue;
    public Action<string>? Recycle;

    public Func<string, long>? FreeFn;
    public Services Services => new(Folders, Files, Dialogs, Shell, Settings, FreeFn ?? (_ => Free), Recycle);
}
