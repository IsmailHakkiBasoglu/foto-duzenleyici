# Sürekli Galeri ve Oturum Kataloğu — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Zaman çizelgesine, bütün ayların yeniden eskiye tek ızgarada aktığı bir galeri, sağ kenarda sürüklenebilir zaman çubuğu ve oturum boyunca bellekte tutulan dosya kataloğu eklemek.

**Architecture:** Core'da salt okunur `CatalogReader` her dosyayı bir kez okuyup `MediaCatalog` üretir; tablo (`TimelineResult`) ve galeri bu tek kaynaktan türetilir. Katalog `ShellViewModel`'in tuttuğu `CatalogStore`'da oturum boyunca yaşar; Düzenle / Temizle / Geri Al başlayınca geçersiz sayılır. Galerinin satır düzeni ve zaman çubuğu hesabı WPF'siz `GalleryLayout`'ta (test edilebilir); App'te `GalleryView` + `TimeScrubber` çizer.

**Tech Stack:** .NET 8, WPF, WPF-UI, CommunityToolkit.Mvvm, xUnit (tr-TR kültürü, `Fakes` / `UiFakes`).

**Spec:** `docs/superpowers/specs/2026-11-20-surekli-galeri-design.md`

## Global Constraints

- Galeri ve katalog **salt okunur**: hiçbir dosyaya yazmaz, taşımaz, silmez.
- Özel klasörler dışlanır: `_Tekrarlar`, `_Benzerler`, `_Kusurlu`, `_Gunluk`, `_Ayiklananlar`, `Tarihsiz` (`Timeline.SpecialFolderNames`).
- Okuma 4 iş parçacığı (`MaxDegreeOfParallelism = 4`), ilerleme `(done, total)`, Durdur ile iptal.
- Tablo ve galeri aynı yöntem ve süzgeçte aynı ay için **aynı sayıyı** gösterir.
- Galeri sırası: aylar yeniden eskiye, ay içinde yeniden eskiye; klasör adları yönteminde tarihi başka aya düşenler ayın sonunda, kendi aralarında yeniden eskiye. Ay görünümü eskiden yeniye kalır.
- Katalog 100.000 dosyada 100 MB'ın altında (ölçülür, raporlanır).
- "Yeniden say" düğmesinin metni **"Yeniden tara"** olur.
- Gerçek arşive (`D:\1- HERKES\...`) hiçbir şey yazılmaz/çalıştırılmaz; denemeler geçici klasör, örnek klasör ve render düzeneğiyle.
- Commit iletileri Türkçe; `Co-Authored-By: Claude` ve "Generated with Claude Code" satırı **eklenmez**.
- Testler: `dotnet test D:\FotoDuzenleyici\FotoDuzenleyici.Tests --filter FullyQualifiedName~<Sınıf>`; görev sonunda tam takım `dotnet test D:\FotoDuzenleyici\FotoDuzenleyici.Tests`.

## Review Focus

1. Kök klasör sonradan silinmiş / sürücü çıkarılmış → katalog hata satırı verir, galeri boş durum gösterir, çökme yok. (Task 1 testi `Read_MissingRoot_ReportsError_NoItems`.)
2. Okuma sürerken kök değişir ya da Yeniden tara'ya basılır → eski okumanın sonucu depoya yazılmaz. (Task 3 testi `ChangeFolder_WhileReading_OldCatalogNotStored`.)
3. Ay görünümünde atılan dosyalar → galeriye dönünce görünmez, ay sayısı düşer. (Task 3 testi `Discard_InMonth_RemovedFromCatalogAndGallery`.)
4. Süzgeç (ör. Video) seçilince hiç dosya kalmaz → galeri "Bu süzgeçte dosya yok" der; zaman çubuğu gizlenir. (Task 4 testi `Build_FilterLeavesNothing_IsEmpty`.)
5. Pencere daralıp sütun sayısı değişir → zaman çubuğu konumları ve "hangi ay" hesabı yeni satırlara göre. (Task 4 testi `Layout_ColumnsChange_MonthStartsRecomputed`.)

---

### Task 1: Katalog okuyucu (Core)

**Files:**
- Create: `FotoDuzenleyici.Core/MediaCatalog.cs`
- Test: `FotoDuzenleyici.Tests/CatalogReaderTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record CatalogItem(MediaFile File, int Year, int Month, string Folder, bool OtherMonth)` — `Folder`: klasör adlarında ay klasörünün tam yolu, çekim tarihinde dosyanın doğrudan klasörü (bugünkü `TimelineEntry.Folder` anlamı). `OtherMonth`: klasör adlarında çekim tarihi yok ya da başka aya düşüyor.
  - `public sealed class MediaCatalog { string Root; TimelineMethod Method; IReadOnlyList<CatalogItem> Items; List<string> Errors; SortedDictionary<string,int> Special; int Outside; bool Cancelled; TimelineResult ToResult(); IReadOnlyList<MediaFile> FilesOf(int year, int month); int Remove(IEnumerable<string> paths); }`
  - `public static class CatalogReader { public static MediaCatalog Read(string root, TimelineMethod method, IProgress<(int done, int total)>? progress = null, CancellationToken ct = default, Func<string, MediaFile>? read = null); }`

- [ ] **Step 1: Write the failing tests** (geçici kökte `Put(rel, size)` yardımcısı, `MonthTimelineTests`'teki gibi)

```csharp
[Fact] public void Read_FolderNames_MatchesTimelineByFolders()
{   // 2026-08 altında 3, 2026-09 altında 1 foto; kökte 1 dışarıda dosya; _Tekrarlar\x.jpg
    var c = CatalogReader.Read(_root, TimelineMethod.FolderNames);
    var t = Timeline.ByFolders(_root);
    Assert.Equal(Counts(t.Entries), Counts(c.ToResult().Entries)); // (yıl, ay, tür) → adet
    Assert.Equal(t.Outside, c.Outside);
    Assert.Equal(t.Special, c.Special);
}
[Fact] public void Read_CaptureDate_MatchesTimelineByCaptureDate() { /* aynı karşılaştırma, CaptureDate */ }
[Fact] public void Read_FolderNames_UnreadableFile_StillCounted_ErrorReported()
{   // read: belirli yolda IOException atar
    Assert.Equal(4, c.Items.Count); Assert.Single(c.Errors);
    Assert.True(c.Items.Single(i => i.File.Path == bad).OtherMonth);
}
[Fact] public void Read_FolderNames_DateInOtherMonth_FlaggedOtherMonth() { /* 2026-08 klasöründe IMG_20260915_x.jpg */ }
[Fact] public void Read_KeepsFullMediaFile() { /* read sahte Orientation=6, Width=4000 döner → Items[0].File.Orientation == 6 */ }
[Fact] public void Read_MissingRoot_ReportsError_NoItems() { Assert.Empty(c.Items); Assert.Single(c.Errors); }
[Fact] public void Read_Cancelled_EmptyAndFlagged() { /* ilerleme ilk bildirimde iptal */ Assert.True(c.Cancelled); Assert.Empty(c.Items); }
[Fact] public void FilesOf_ReturnsOnlyThatMonth() { Assert.Equal(3, c.FilesOf(2026, 8).Count); }
[Fact] public void Remove_DropsItems_ResultCountsFollow() { Assert.Equal(1, c.Remove(new[] { a })); Assert.Equal(2, c.FilesOf(2026, 8).Count); }
[Fact] public void Memory_100kItems_Under100MB()
{   // 100.000 CatalogItem, ~110 karakterlik yollar, dolu MediaFile; GC.GetTotalMemory(true) farkı
    Assert.True(bytes < 100L * 1024 * 1024, $"{bytes / 1024 / 1024} MB");
}
```

- [ ] **Step 2: Run** `dotnet test D:\FotoDuzenleyici\FotoDuzenleyici.Tests --filter FullyQualifiedName~CatalogReaderTests` → Expected: derleme hatası (`CatalogReader` yok).

- [ ] **Step 3: Implement `CatalogReader.Read` in `FotoDuzenleyici.Core/MediaCatalog.cs`**
  - Yol toplama: klasör adlarında `Timeline.ByFolders` ile aynı gezinti (kök dosyaları ve ay dışı klasörler `Outside`, özel klasörler `Special`, ay klasörleri `Timeline.Walk`); çekim tarihinde `Timeline.ByCaptureDate`'in `Scanner` seçenekleriyle aynı. Kod tekrarını önlemek için iki yol toplama işini `Timeline` içinde `internal` yardımcılara çıkar ve `ByFolders` / `ByCaptureDate` da onları kullansın.
  - Okuma `Parallel.For` (4), dizinle sıralı dizi, `progress` her dosyada.
  - Klasör adlarında okuma atarsa: `new MediaFile { Path, Size = FileInfo.Length (okunamazsa 0), Kind = FileClassifier.Classify }`, `OtherMonth = true`, hata satırı. Çekim tarihinde atan ya da `Taken` boş dosya bugünkü gibi sayılmaz.
  - `ToResult()`: her öğe için `TimelineEntry(Year, Month, File.Kind, File.Size, Folder, File.DateSource)`; `Special`, `Outside`, `Errors`, `Cancelled` aynen.
  - İptalde `Items` boş, `Cancelled = true`.

- [ ] **Step 4: Run** aynı komut → Expected: PASS (10 test); `Memory_...` iletisindeki MB değerini ledger'a yaz.

- [ ] **Step 5: Run full suite** → Expected: önceki 629 + 10 geçer (Timeline yeniden düzenlemesi `TimelineFolderTests` / `TimelineCaptureDateTests`'i bozmamalı).

- [ ] **Step 6: Commit** `git commit -m "Katalog okuyucu: her dosya bir kez okunur, tablo bundan türetilir"`

---

### Task 2: Oturum deposu ve geçersiz kılma (ViewModels)

**Files:**
- Create: `FotoDuzenleyici.ViewModels/CatalogStore.cs`
- Modify: `FotoDuzenleyici.ViewModels/ShellViewModel.cs` (alan, `StartTimeline`, `StartOrganize`, `StartClean`, `Undo`)
- Test: `FotoDuzenleyici.Tests/CatalogStoreTests.cs`

**Interfaces:**
- Consumes: `MediaCatalog` (Task 1).
- Produces:
  - `public sealed class CatalogStore { MediaCatalog? Get(string root, TimelineMethod method); void Set(MediaCatalog c); void Invalidate(); }` — `Get` kök (büyük/küçük harf duyarsız, sondaki ayıraç yok sayılır) ve yöntem eşleşmezse ya da geçersizse null. Tek katalog tutulur; `Set` öncekini bırakır. İptal edilmiş katalog saklanmaz.
  - `ShellViewModel.Catalogs` (`CatalogStore`, oturum boyu tek nesne), `TimelineViewModel` kurulurken verilir.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Get_SameRootAndMethod_ReturnsStored()
[Fact] public void Get_OtherRootOrMethod_Null()
[Fact] public void Get_RootWithTrailingSlashOrOtherCase_Matches()
[Fact] public void Invalidate_ThenGet_Null()
[Fact] public void Set_Cancelled_NotStored()
// ShellTests'e:
[Fact] public void StartOrganize_InvalidatesCatalog()   // depoya katalog koy, akışı başlat → Get null
[Fact] public void StartClean_InvalidatesCatalog()
[Fact] public async Task Undo_InvalidatesCatalog()      // FakeFiles günlük döner, Confirm evet
[Fact] public void StartBackup_KeepsCatalog()
[Fact] public void Timeline_ReenteredSameSession_GetsSameStore()
```

- [ ] **Step 2: Run** `--filter FullyQualifiedName~CatalogStoreTests` → Expected: derleme hatası.
- [ ] **Step 3: Implement** `CatalogStore` ve `ShellViewModel` değişiklikleri (Undo'da geçersiz kılma, günlük seçilip onaylandıktan sonra).
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~CatalogStoreTests|FullyQualifiedName~ShellTests"` → Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Katalog oturum boyunca saklanır; Düzenle, Temizle, Geri Al tazeler"`

---

### Task 3: Zaman çizelgesi katalogla çalışır

**Files:**
- Modify: `FotoDuzenleyici.ViewModels/TimelineViewModel.cs`, `FotoDuzenleyici.ViewModels/MonthViewModel.cs` (yapıcı + `Load`), `FotoDuzenleyici.ViewModels/Settings.cs` (`TimelineView = "Gallery"`), `FotoDuzenleyici.App/Shell/TimelinePage.xaml` ("Yeniden say" → "Yeniden tara")
- Test: `FotoDuzenleyici.Tests/TimelineCatalogTests.cs`; mevcut `TimelineViewModelTests`, `MonthTimelineTests` gerekiyorsa uyarlanır

**Interfaces:**
- Consumes: `CatalogReader.Read`, `MediaCatalog.ToResult/FilesOf/Remove`, `CatalogStore`.
- Produces:
  - `public enum TimelineViewKind { Gallery, Table }`; `TimelineViewModel.View` (`[ObservableProperty]`, ayara yazılır), `TimelineViewModel.Catalog` (`MediaCatalog?`), `event Action? CatalogChanged` (galeri yeniden kurulsun).
  - `TimelineViewModel(Services s, AppSettings settings, string? startFolder = null, CatalogStore? catalogs = null)` — null → kendi `new CatalogStore()`.
  - `public Task EnsureCatalogAsync()` — katalog yoksa okur (aynı `IsCounting/Done/Total`, nesil sayacı).
  - `MonthViewModel(..., IReadOnlyList<MediaFile>? preloaded = null)` — doluysa `MonthReader` çağrılmaz.
  - `public void OpenMonth(int year, int month)` — `OpenCell` ile aynı işi ay numarasıyla yapar (galeri başlığı kullanır).

Davranış:
- `CountAsync`: depoda (kök, yöntem) için katalog varsa okumadan `Result = Catalog.ToResult()`. Yoksa: yöntem çekim tarihi **ya da** görünüm Galeri ise `CatalogReader.Read`, sonuç depoya; yöntem klasör adları ve görünüm Tablo ise bugünkü `Timeline.ByFolders` (katalog yok).
- `View` Galeri'ye geçince katalog yoksa `EnsureCatalogAsync()`; bitince `Result` katalogdan yeniden kurulur.
- `Recount` ("Yeniden tara"): `catalogs.Invalidate()` sonra `CountAsync()`.
- `OpenCell` / `OpenMonth`: katalog varsa `preloaded = Catalog.FilesOf(y, m)`.
- `Patch`: bugünkü tablo yamasına ek olarak `Catalog.Remove(atılan yollar)`, `Result = Catalog.ToResult()`, `CatalogChanged`.
- Nesil sayacı eşleşmeyen sonuç depoya yazılmaz.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task CaptureDate_ReadsOnce_SecondCountUsesStore()   // read sayacı: ikinci CountAsync'te artmaz
[Fact] public async Task FolderNames_TableView_NoCatalogRead()          // read sayacı 0, Catalog null
[Fact] public async Task GalleryView_ReadsCatalog_TableMatches()        // Table toplamı == Catalog.Items.Count (süzgeç All)
[Fact] public async Task Recount_InvalidatesAndRereads()
[Fact] public async Task OpenCell_WithCatalog_MonthDoesNotReread()      // MonthViewModel read sayacı 0, Photos.Count 3
[Fact] public async Task Discard_InMonth_RemovedFromCatalogAndGallery() // Ayıkla (FakeBin) → Back → Catalog.FilesOf(2026,8).Count 2, CatalogChanged tetiklendi
[Fact] public async Task ChangeFolder_WhileReading_OldCatalogNotStored()// BeforeCountForTest içinde Folder değişir → store.Get(eski kök) null
[Fact] public void View_Persisted()                                     // View = Table → settings.TimelineView "Table" kaydedildi
```

- [ ] **Step 2: Run** `--filter FullyQualifiedName~TimelineCatalogTests` → Expected: derleme hatası.
- [ ] **Step 3: Implement** yukarıdaki davranış.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~Timeline|FullyQualifiedName~Month"` → Expected: PASS. Varsayılan görünüm Galeri olduğu için mevcut testlerde okuma sayısı değişirse beklentiyi spec'e göre güncelle ve ledger'a `Ruling:` yaz.
- [ ] **Step 5: Run full suite** → PASS.
- [ ] **Step 6: Commit** `git commit -m "Zaman çizelgesi oturum kataloğunu kullanır; Yeniden tara"`

---

### Task 4: Galeri modeli ve satır düzeni (ViewModels)

**Files:**
- Create: `FotoDuzenleyici.ViewModels/GalleryViewModel.cs`, `FotoDuzenleyici.ViewModels/GalleryLayout.cs`
- Modify: `FotoDuzenleyici.ViewModels/TimelineViewModel.cs` (`Gallery` özelliği: katalog / süzgeç değişince yeniden kurulur; `MonthRequested` → `OpenMonth`)
- Test: `FotoDuzenleyici.Tests/GalleryTests.cs`

**Interfaces:**
- Consumes: `MediaCatalog`, `MonthItems.Build`, `MonthPhoto`, `KindFilter`.
- Produces:
  - `public sealed record GalleryMonth(int Year, int Month, IReadOnlyList<MonthPhoto> Photos) { string Title /* "Ekim 2026 · 1.001" */; string ShortTitle /* "Ekim 2026" */; }`
  - `public sealed class GalleryViewModel : ObservableObject` — `static GalleryViewModel Build(MediaCatalog c, KindFilter f, Services s)`; `IReadOnlyList<GalleryMonth> Months`; `bool IsEmpty`; `[RelayCommand] OpenMonth(GalleryMonth)` → `event Action<int,int>? MonthRequested`; `[RelayCommand] OpenFile(MonthPhoto)` → `Services.Shell.OpenFile`.
  - `public sealed record GalleryHeaderRow(GalleryMonth Month)`; `public sealed record GalleryTileRow(GalleryMonth Month, IReadOnlyList<MonthPhoto> Photos)`.
  - `public sealed class GalleryLayout` — `static GalleryLayout Build(IReadOnlyList<GalleryMonth> months, int columns, double headerHeight, double rowHeight)`; `IReadOnlyList<object> Rows`; `double TotalHeight`; `double OffsetOfRow(int row)`; `int RowAtOffset(double y)`; `GalleryMonth? MonthAtOffset(double y)`; `IReadOnlyList<(int Year, double Offset)> YearMarks` (her yılın ilk ayının üst kenarı).

Sıra kuralı: ay listesi yeniden eskiye; her ayda `MonthItems.Build` sonucu `OtherMonth=false` olanlar `Taken` azalan (eşitlikte yol artan), ardından `OtherMonth=true` olanlar aynı kuralla. Boş aylar listelenmez.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Build_MonthsNewestFirst()
[Fact] public void Build_PhotosInMonthNewestFirst_OtherMonthAtEnd()
[Fact] public void Build_RawPairShownOnceWithBadge()          // a.jpg + a.cr2, süzgeç Photo → 1 karo, HasRaw
[Fact] public void Build_CountsEqualTable_ForEachFilter()     // her KindFilter için ay adetleri == TimelineTable.Build(c.ToResult().Entries, f)
[Fact] public void Build_FilterLeavesNothing_IsEmpty()
[Fact] public void Title_TurkishMonthAndCount()               // "Ekim 2026 · 1.001"
[Fact] public void OpenMonth_RaisesMonthRequested()
[Fact] public void OpenFile_CallsShell()                      // _f.Shell.Opened içerir
[Fact] public void Layout_RowsAndOffsets()                    // 2 ay (5 ve 3 foto), 4 sütun → başlık,2 satır,başlık,1 satır; offsetler 40/104 ile
[Fact] public void Layout_RowAtOffset_AndMonthAtOffset()
[Fact] public void Layout_YearMarks_FirstMonthOfEachYear()
[Fact] public void Layout_ColumnsChange_MonthStartsRecomputed()
```

- [ ] **Step 2: Run** `--filter FullyQualifiedName~GalleryTests` → Expected: derleme hatası.
- [ ] **Step 3: Implement**; `RowAtOffset` ikili arama.
- [ ] **Step 4: Run** aynı komut → PASS; ardından tam takım → PASS.
- [ ] **Step 5: Commit** `git commit -m "Galeri modeli: yeniden eskiye aylar, satır düzeni, yıl işaretleri"`

---

### Task 5: Video küçük resimleri (Core)

**Files:**
- Create: `FotoDuzenleyici.Core/ShellThumbnail.cs`
- Modify: `FotoDuzenleyici.Core/ThumbnailLoader.cs` (`DefaultDecode`)
- Test: `FotoDuzenleyici.Tests/ShellThumbnailTests.cs`

**Interfaces:**
- Produces: `public static class ShellThumbnail { public static BitmapSource? Get(string path, int size); }` — `IShellItemImageFactory.GetImage` (`SIIGBF_BIGGERSIZEOK`), HBITMAP → donmuş `BitmapSource`, `DeleteObject`; hata ya da dosya yoksa null, asla atmaz. Kendi STA iş parçacığında çalışır (`WindowsRecycleBin.Send` deseni).
- `ThumbnailLoader.DefaultDecode(f)`: `f.Kind == FileKind.Video` → `ShellThumbnail.Get(f.Path, 256)`; öbürleri bugünkü gibi.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Get_MissingFile_Null()
[Fact] public void Get_Jpeg_ReturnsFrozenBitmap()            // TestImages ile 64×48 JPG; sonuç Frozen, en/boy > 0
[Fact] public void DefaultDecode_Video_UsesShell_NoThrowWhenNoCodec() // geçersiz baytlı .mp4 → null ya da resim, istisna yok
```

- [ ] **Step 2: Run** `--filter FullyQualifiedName~ShellThumbnailTests` → derleme hatası.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS; tam takım → PASS.
- [ ] **Step 5: Commit** `git commit -m "Videolara Windows küçük resmi"`

---

### Task 6: Galeri ekranı ve zaman çubuğu (App)

**Files:**
- Create: `FotoDuzenleyici.App/Shell/GalleryView.xaml(.cs)`, `FotoDuzenleyici.App/Shell/TimeScrubber.cs`
- Modify: `FotoDuzenleyici.App/Shell/TimelinePage.xaml` (Galeri | Tablo seçimi; Galeri seçiliyken tablo paneli yerine `GalleryView`; ay görünümü bugünkü gibi en üstte), `FotoDuzenleyici.Core/ThumbnailLoader.cs` (`bool Paused`), `FotoDuzenleyici.App/Shell/ThumbTracker.cs` (`Pause(bool)`)
- Test: `FotoDuzenleyici.Tests/ThumbnailLoaderTests.cs` (+1)

**Interfaces:**
- Consumes: `GalleryViewModel`, `GalleryLayout` (Task 4), `ThumbTracker`, ay görünümündeki karo şablonu (`MonthView` ızgara karosu: aynı boyut 132×100, "+RAW" rozeti, ▶ işareti — ortak `DataTemplate` olarak `ShellResources.xaml`'a taşı).
- Produces: `ThumbnailLoader.Paused` — true iken istekler kuyruğa girer, çözme başlamaz; false olunca kuyruk işlenir.

Görünüm kuralları (spec'ten):
- Üst çubuk: klasör, **Galeri | Tablo** segmenti, süzgeç, **⟳ Yeniden tara**. Okuma sürerken bugünkü ilerleme ekranı ve Durdur.
- `ListBox` + `VirtualizingStackPanel` (`ScrollUnit="Pixel"`, geri dönüşüm), satırlar `GalleryLayout.Rows`; sütun sayısı genişlikten (`MonthView.Columns()` formülü), genişlik değişince düzen yeniden kurulur ve görünen ay korunur.
- Başlık satırı: `Title` + sağda **Ayıkla ▶** (`OpenMonthCommand`). Ay görünümünden dönünce galeri aynı kaydırma konumunda.
- Çift tık / Enter: `OpenFileCommand`. Home/End en üst/en alt; Page Up/Down liste varsayılanı.
- `TimeScrubber`: sağda 24 px şerit; `YearMarks` yıl etiketleri (üst üste binenler seyreltilir), sürükleyince `ScrollToVerticalOffset(fraction × TotalHeight)`, yanında `MonthAtOffset` → `ShortTitle` balonu (Popup); tıklayınca atlar; sürükleme boyunca `ThumbTracker.Pause(true)`, bırakınca false. Boş galeride gizli.
- Boş durum: "Bu süzgeçte dosya yok".

- [ ] **Step 1: Write the failing test** `ThumbnailLoaderTests.Paused_QueuesUntilResumed` (sahte decode sayacı: Paused iken 0, false sonrası 1).
- [ ] **Step 2: Run** → FAIL (`Paused` yok).
- [ ] **Step 3: Implement** `Paused`, sonra görünümler.
- [ ] **Step 4: Run** tam takım → PASS; `dotnet build D:\FotoDuzenleyici\FotoDuzenleyici.App` uyarısız.
- [ ] **Step 5: Görsel doğrulama:** scratchpad render düzeneğine `gallery` kipi ekle (örnek klasörde 3 yıl × birkaç ay, ~1.000 dosya); ekran görüntüsünde ay başlıkları yeniden eskiye, zaman çubuğu yıl etiketleri, balon görünür. Uygulamayı örnek klasörle aç: sürükleme, Ayıkla ▶ → ay görünümü → geri aynı konum, Yeniden tara, süzgeç. Kaydırma adım süresini ölç (hedef: ay görünümündeki 14–31 ms düzeyi) ve ledger'a yaz.
- [ ] **Step 6: Commit** `git commit -m "Sürekli galeri ekranı ve zaman çubuğu"`

---

### Task 7: Belgeler

**Files:**
- Modify: `README.md` (zaman çizelgesi paragrafına galeri ve zaman çubuğu), `CHANGELOG.md` (`[Yayınlanmamış]` → Zaman çizelgesi: sürekli galeri, zaman çubuğu, oturum kataloğu, Yeniden tara, video küçük resimleri), `YOL_HARITASI.md` ("Fotoğraflar uygulaması yerine" maddesine "🔄 1. parça: sürekli galeri" notu)

- [ ] **Step 1:** Belgeleri güncelle.
- [ ] **Step 2: Run** tam takım → PASS.
- [ ] **Step 3: Commit** `git commit -m "Belgeler: sürekli galeri"`
