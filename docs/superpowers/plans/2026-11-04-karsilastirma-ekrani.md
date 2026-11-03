# Benzerler için karşılaştırma ekranı — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Benzerler sekmesini grupların küçük resimli şerit görünümüne çevirmek, çift tıklayınca açılan tam ekran karşılaştırma penceresi eklemek ve kararları (kalsın/ayıkla, incelendi) uygulama kapanınca da hatırlamak.

**Architecture:** Kural ve durum kodu Core'da: `ReviewStore` (kalıcı kararlar), `SimilarRow` + `SimilarGroupState` (grup durumu, "en az bir kare kalsın", geri yükleme), `ThumbnailLoader` (arka planda çözme, LRU). App yalnızca görünüm: Benzerler sekmesinde `ListBox` şeridi ve yeni `CompareWindow`. Plan/Uygula akışı değişmez; `BuildSimilarMap()` satırların `Keep` değerini okumaya devam eder.

**Tech Stack:** .NET 8 (`net8.0-windows`, `UseWPF`), WPF, xUnit. Yeni paket yok.

**Spec:** `docs/superpowers/specs/2026-11-04-karsilastirma-ekrani-design.md`

## Global Constraints

- Yeni NuGet paketi yok; MVVM kütüphanesi yok.
- Karar dosyası: `%LOCALAPPDATA%\FotoDuzenleyici\benzer-kararlar.json`; fotoğraf ya da küçük resim saklanmaz.
- Dosya kimliği: `HashCache.Key(path)`; grup imzası: üye anahtarlarının sıralanıp birleştirilmiş hâlinin SHA-256'sı.
- Kayıt: değişiklikten ~1 sn sonra tek yazma + pencere kapanırken; geçici dosyaya yaz, sonra değiştir.
- Küçük resim: `ImageHasher.Decode(path, orientation, 256)`, aynı anda en fazla 4, bellekte son 600.
- Şerit kartı 140 px; karşılaştırmada en fazla 4 sütun, ilk çözme 1200 px, yakınlaşınca tam çözünürlük (`decodePixelWidth = 0`).
- Her grupta en az bir kare kalır; hiçbir dosya otomatik taşınmaz; işaret değişince plan geçersizleşir.
- "Önerileri uygula" / "Hepsini koru" işaretleri kaydeder ama grubu incelendi yapmaz.
- Kullanıcıya görünen metinler ve kod yorumları Türkçe, mevcut dosyalardaki gibi.
- Her task sonunda `dotnet test FotoDuzenleyici.sln` tamamen yeşil.

## Review Focus

1. **Geri yüklenen işaretlerin hepsi "ayıkla"** (önceki turda bir kare ayıklanıp `_Benzerler`'e gitti, kalanı da elle ayıklanmıştı): grup boş kalmamalı, ★ ya da ilk kare kalmalı. → Task 2 testi `Restore_AllSavedFalse_KeepsSuggestedOrFirst`.
2. **Anahtarı hesaplanamayan üye** (dosya silinmiş / kilitli): grup yine gösterilir, imza `null`, incelendi işaretlenemez ama çökme yok. → Task 2 testi `Restore_MissingKey_NoSignature_NoCrash`.
3. **Bozuk ya da yarım karar dosyası** (elektrik kesintisi): boş başlar, `LoadFailed == true`, uygulama çalışır. → Task 1 testi `Load_CorruptFile_StartsEmpty`.
4. **Hızlı kaydırma** (506 grup, ~1300 kare): ekrandan çıkan satırların bekleyen çözmeleri iptal edilmeli; aksi hâlde kuyruk birikir. → Task 3 testi `Cancel_QueuedRequest_IsNeverDecoded`.
5. **Karşılaştırmada son kalan kareyi ayıklamak** (tıklama, 1–9, Boşluk): işlem yapılmaz, uyarı görünür. → Task 2 testi `Release_LastKept_IsRefused`; Task 5 elle deneme adımı.

---

### Task 1: `ReviewStore` — kalıcı kararlar

**Files:**
- Create: `FotoDuzenleyici.Core/ReviewStore.cs`
- Test: `FotoDuzenleyici.Tests/ReviewStoreTests.cs`

**Interfaces:**
- Consumes: `HashCache.Key(string path)` (mevcut).
- Produces:
  - `public sealed class ReviewStore`
  - `public static string DefaultPath` → `%LOCALAPPDATA%\FotoDuzenleyici\benzer-kararlar.json`
  - `public ReviewStore(string path)`; `public bool LoadFailed { get; }`
  - `public bool? GetKeep(string key)`; `public void SetKeep(string key, bool keep)`
  - `public static string Signature(IEnumerable<string> memberKeys)`
  - `public bool IsReviewed(string signature)`; `public void SetReviewed(string signature, bool reviewed)`
  - `public void Save()` — hata olursa `IOException`/`UnauthorizedAccessException` fırlatır (çağıran yakalar).

- [ ] **Step 1: Testleri yaz** (`ReviewStoreTests`, geçici klasör `IDisposable` ile, `EngineTests` gibi)
  - `SaveThenLoad_RoundTrips`: `SetKeep("a", false)`, `SetKeep("b", true)`, `SetReviewed(sig, true)`, `Save()`; yeni `ReviewStore(path)` → `GetKeep("a") == false`, `GetKeep("b") == true`, `GetKeep("c") == null`, `IsReviewed(sig)`, `LoadFailed == false`.
  - `Load_MissingFile_StartsEmpty`: `LoadFailed == false`, `GetKeep("a") == null`.
  - `Load_CorruptFile_StartsEmpty`: dosyaya `"{ yarım"` yaz → `LoadFailed == true`, `GetKeep("a") == null`; ardından `SetKeep` + `Save()` çalışır.
  - `Signature_IndependentOfOrder_ChangesWithMembers`: `Signature(["a","b"]) == Signature(["b","a"])`, `!= Signature(["a","b","c"])`.
  - `Key_SurvivesMoveAndRename`: dosya yaz, zamanını sabitle, `HashCache.Key` → `SetKeep`; dosyayı `File.Move` ile başka klasöre başka adla taşı; `GetKeep(HashCache.Key(yeniYol)) == false`.
  - `SetReviewedFalse_Removes`: `SetReviewed(s, true)` sonra `false` → `!IsReviewed(s)`.

- [ ] **Step 2: Çalıştır, başarısız olduğunu gör**
  Run: `dotnet test FotoDuzenleyici.Tests --filter "FullyQualifiedName~ReviewStoreTests"` — Expected: derleme hatası (`ReviewStore` yok).

- [ ] **Step 3: `ReviewStore`'u yaz**
  JSON biçimi: `{ "Keep": { "<anahtar>": true|false }, "Reviewed": ["<imza>", ...] }` (`System.Text.Json`, `HashCache` ile aynı yükleme/kaydetme kalıbı: yüklemede hata → boş + `LoadFailed`; kayıtta `.tmp` + `File.Move(overwrite: true)`). İmza: anahtarlar ordinal sıralanır, `"\n"` ile birleştirilir, UTF-8 SHA-256 hex. İş parçacığı güvenliği gerekmez (yalnız UI iş parçacığı kullanır).

- [ ] **Step 4: Testleri çalıştır** — Expected: PASS; ardından `dotnet test FotoDuzenleyici.sln` yeşil.

- [ ] **Step 5: Commit** — `git commit -m "ReviewStore: benzer gruplarında kararların kalıcı saklanması"` (+ Co-Authored-By satırı).

---

### Task 2: `SimilarRow` Core'a taşınır, `SimilarGroupState` eklenir

**Files:**
- Create: `FotoDuzenleyici.Core/SimilarGroupState.cs` (`SimilarRow` ve `SimilarGroupState` birlikte)
- Modify: `FotoDuzenleyici.App/MainWindow.xaml.cs` — `SimilarRow` sınıfı (satır ~43–80) silinir; `ShowSimilar` / `CanRelease` kurulumu `SimilarGroupState` kullanır.
- Test: `FotoDuzenleyici.Tests/SimilarGroupStateTests.cs`

**Interfaces:**
- Consumes: Task 1 `ReviewStore`.
- Produces:
  - `SimilarRow` (namespace `FotoDuzenleyici.Core`): mevcut üyelerin hepsi aynı adla (`Group`, `File`, `Kind`, `KindText`, `Suggested`, `Star`, `Path`, `Name`, `Resolution`, `SizeMb`, `Taken`, `CanRelease`, `Changed`, `Keep`) + `public ImageSource? Thumb { get; set; }` (bildirimli) + `public bool ThumbFailed { get; set; }` (bildirimli; çözülemeyen dosya) + `public string? StoreKey { get; set; }` + `internal void LoadKeep(bool keep)` (bildirim yollar, `Changed`'i çağırmaz).
  - `public sealed class SimilarGroupState : INotifyPropertyChanged`
    - `public SimilarGroupState(int number, SimilarKind kind, IReadOnlyList<SimilarRow> rows)` — her satırın `CanRelease`'ini kendine bağlar.
    - `int Number`, `SimilarKind Kind`, `string KindText`, `IReadOnlyList<SimilarRow> Rows`, `string? Signature`, `bool Reviewed` (bildirimli), `int KeptCount`.
    - `public bool CanRelease(SimilarRow row)`
    - `public void KeepOnly(SimilarRow row)` — `row.Keep = true`, diğerleri `false` (normal `Keep` setter'ı, yani `Changed` çalışır).
    - `public void Restore(ReviewStore store)` — `StoreKey`'ler önceden atanmış olmalı.
    - `public void SetReviewed(ReviewStore store, bool reviewed)` — `Signature` null ise hiçbir şey yapmaz.
    - `public static List<SimilarGroupState> FromGroups(IReadOnlyList<SimilarGroup> groups)` — `MainWindow.ShowSimilar`'ın bugün yaptığı satır kurulumu (grup no 1'den, `Suggested = m == g.Suggested`).

- [ ] **Step 1: Testleri yaz** (`SimilarFinderTests.Photo` benzeri yardımcıyla `MediaFile`; satırlar `FromGroups` ile kurulur; `ReviewStore` geçici dosyada)
  - `Release_LastKept_IsRefused`: 2 satırlı grupta ilkini `Keep=false` → olur; ikincisini `Keep=false` → `Keep` hâlâ `true`.
  - `KeepOnly_ReleasesOthers`: 3 satır; `KeepOnly(rows[1])` → yalnız `rows[1].Keep`.
  - `Restore_AppliesSavedKeeps_AndReviewed`: store'da `rows[0]` anahtarı `false`, imza incelendi → `Restore` sonrası `rows[0].Keep == false`, `rows[1].Keep == true`, `Reviewed == true`; `Changed` hiç çağrılmadı (sayaçla).
  - `Restore_AllSavedFalse_KeepsSuggestedOrFirst`: iki satır da `false` kayıtlı; ★'lı grupta ★ kalır, ★'sız grupta ilk satır kalır.
  - `Restore_MembersChanged_NotReviewed_ButKeepsMarks`: store'da {a,b} imzası incelendi, `a=false`; grup {a,b,c} → `Reviewed == false`, `a` satırı `Keep == false`.
  - `Restore_MissingKey_NoSignature_NoCrash`: bir satırın `StoreKey = null` → `Signature == null`, `Reviewed == false`, `SetReviewed(store, true)` hiçbir şey yapmaz.

- [ ] **Step 2: Çalıştır** — Expected: derleme hatası (`SimilarGroupState` yok).

- [ ] **Step 3: `SimilarRow`'u Core'a taşı, `SimilarGroupState`'i yaz.**
  `Restore` sırası: her satıra `store.GetKeep(StoreKey)` varsa `LoadKeep`; sonra `KeptCount == 0` ise ★ yoksa `Rows[0]` → `LoadKeep(true)`; bütün `StoreKey`'ler doluysa `Signature = ReviewStore.Signature(...)`, `Reviewed = store.IsReviewed(Signature)`.

- [ ] **Step 4: `MainWindow`'u yeni tiplere bağla** — `_similar` yerine `List<SimilarGroupState> _groups`; `BuildSimilarMap`, `SuggestSimilar_Click`, `KeepAllSimilar_Click` `_groups.SelectMany(g => g.Rows)` üzerinden aynı mantıkla çalışır. Bu task'ta görünüm hâlâ DataGrid (`ItemsSource = _groups.SelectMany(g => g.Rows).ToList()`).

- [ ] **Step 5: Testler** — `dotnet test FotoDuzenleyici.sln` yeşil; uygulama derlenir (`dotnet build FotoDuzenleyici.App`).

- [ ] **Step 6: Commit** — `"SimilarGroupState: grup durumu ve kararların geri yüklenmesi"`.

---

### Task 3: `ThumbnailLoader`

**Files:**
- Create: `FotoDuzenleyici.Core/ThumbnailLoader.cs`
- Test: `FotoDuzenleyici.Tests/ThumbnailLoaderTests.cs`

**Interfaces:**
- Consumes: Task 2 `SimilarRow.Thumb`; `ImageHasher.Decode`.
- Produces:
  - `public sealed class ThumbnailLoader`
  - `public ThumbnailLoader(Func<MediaFile, ImageSource?> decode, int capacity = 600, int parallelism = 4)` — oluşturulduğu iş parçacığının `SynchronizationContext`'ini yakalar; sonuçları oraya `Post` eder (yoksa doğrudan atar).
  - `public static ImageSource? DefaultDecode(MediaFile f)` → `ImageHasher.Decode(f.Path, f.Orientation, 256)`.
  - `public void Request(SimilarRow row)` — önbellekte varsa `row.Thumb`'ı hemen atar; yoksa sıraya ekler (aynı dosya iki kez sıraya girmez).
  - `public void Cancel(SimilarRow row)` — henüz başlamamış isteği düşürür.
  - `public int CachedCount { get; }`

- [ ] **Step 1: Testleri yaz** (sahte `decode`: sayaç + `SemaphoreSlim` ile bekletme; `SynchronizationContext` yokken sonuç arka planda atanır, testler `SpinWait.SpinUntil(..., 5 sn)` ile bekler)
  - `Request_SetsThumb_AndCaches`: `Request` → `Thumb != null`; ikinci `Request` sahte `decode`'u yeniden çağırmaz.
  - `NeverMoreThanParallelismAtOnce`: 10 istek, `parallelism: 4`, `decode` içinde eşzamanlı sayının en büyüğü ≤ 4.
  - `Lru_EvictsOldest`: `capacity: 2`; a, b, c → `a` yeniden istenince `decode` tekrar çağrılır, `c` için çağrılmaz.
  - `Cancel_QueuedRequest_IsNeverDecoded`: `parallelism: 1`, ilk `decode` semaforda bekletilir; ikinci istek `Cancel` edilir; semafor bırakılınca ikinci dosya için `decode` çağrılmamış.
  - `DecodeReturnsNull_SetsThumbFailed_NoThrow`: `decode` null döner → `Thumb == null`, `ThumbFailed == true`.

- [ ] **Step 2: Çalıştır** — Expected: derleme hatası.

- [ ] **Step 3: Yaz.** Kuyruk: `LinkedList` + `lock`; `parallelism` kadar `Task.Run` işçisi kuyruk boşalınca biter, yeni istekte yeniden başlar. Önbellek anahtarı `MediaFile` (referans), LRU `LinkedList` + `Dictionary`. `ImageSource` çözüldükten sonra `Freeze()` edilmiş olmalı (`ImageHasher.Decode` zaten donmuş döner).

- [ ] **Step 4: Testler yeşil** — `dotnet test FotoDuzenleyici.sln`.

- [ ] **Step 5: Commit** — `"ThumbnailLoader: arka planda küçük resim, LRU ve iptal"`.

---

### Task 4: Şerit görünümü ve kararların kalıcılığı (App)

**Files:**
- Modify: `FotoDuzenleyici.App/MainWindow.xaml` — `SimilarTab` içindeki `DataGrid` ve sağdaki önizleme `Border`'ı kaldırılır, yerine `ListBox x:Name="SimilarList"`.
- Modify: `FotoDuzenleyici.App/MainWindow.xaml.cs` — `SimilarGrid_SelectionChanged` ve `_previewVersion` silinir; yükleme, kayıt, süzgeç, sayaç eklenir.

**Interfaces:**
- Consumes: Task 1 `ReviewStore`, Task 2 `SimilarGroupState`/`SimilarRow`, Task 3 `ThumbnailLoader`.
- Produces: `MainWindow` içinde `List<SimilarGroupState> _groups`, `ReviewStore _reviews`, `ThumbnailLoader _thumbs`, `void OpenCompare(SimilarGroupState g)` (Task 5 doldurur; bu task'ta boş gövde ya da yalnız durum çubuğu mesajı).

- [ ] **Step 1: XAML** — `ListBox`: `VirtualizingPanel.IsVirtualizing="True"`, `VirtualizingPanel.VirtualizationMode="Recycling"`, `ScrollViewer.CanContentScroll="True"`, `HorizontalContentAlignment="Stretch"`. Öğe şablonu: solda 150 px `StackPanel` (`Grup {Number}`, `KindText`, `"{Rows.Count} kare"`, `Reviewed` ise `"✓ incelendi"`); sağda `ItemsControl ItemsSource="{Binding Rows}"` + `WrapPanel`; kart 140 px genişlik: `Image Source="{Binding Thumb}" Height="100" Stretch="Uniform"` (`ThumbFailed` ise yerinde gri "önizleme yok" yazısı), altında `Name` (kırpılmış), sağ üst köşede `Star`; `Keep == true` → yeşil 3 px çerçeve (`#2E7D32`), `false` → `Opacity=0.4` + kırmızı `✗`; `ToolTip`: `Path`, `Resolution`, `SizeMb` MB, `Taken`.
  Üst çubuk: mevcut iki düğme + `CheckBox x:Name="OnlyUnreviewed" Content="Yalnız incelenmemişler"` + `TextBlock x:Name="ReviewCounter"`. Açıklama metni: "Karta tıkla: Kalsın ↔ Ayıkla. Çift tıkla ya da Enter: karşılaştırma. Ayıklanan dosya Uygula'da _Benzerler klasörüne gider (silinmez)."

- [ ] **Step 2: Kart tıklamaları** — kartta `MouseLeftButtonUp` (tek tık, `ClickCount == 1`) → `row.Keep = !row.Keep`; `MouseLeftButtonDown` `ClickCount == 2` → `OpenCompare(grup)`; `SimilarList` `KeyDown` Enter → seçili grup için `OpenCompare`.

- [ ] **Step 3: Küçük resimler** — kart `Loaded` → `_thumbs.Request(row)`, `Unloaded` → `_thumbs.Cancel(row)` (sanallaştırma sayesinde yalnız görünenler yüklenir).

- [ ] **Step 4: Kararların yüklenmesi ve kaydı**
  - `_reviews = new ReviewStore(ReviewStore.DefaultPath)` pencere açılışında; `LoadFailed` ise durum çubuğu: "Önceki benzer kararları okunamadı; boş başlandı."
  - `ShowSimilar`: `_groups = SimilarGroupState.FromGroups(groups)`; durum "Önceki kararlar yükleniyor…"; `await Task.Run` ile her satıra `StoreKey = HashCache.Key(path)` (istisna → `null`); UI iş parçacığında her gruba `Restore(_reviews)`; sonra `SimilarList.ItemsSource` ve sayaç.
  - Her `row.Changed`: `SetKeep(row.StoreKey, row.Keep)` (anahtar varsa) + `SimilarChanged()` + gecikmeli kayıt.
  - Gecikmeli kayıt: 1 sn `DispatcherTimer`, her değişiklikte yeniden başlar, `Tick`'te `Save()`; `Closing`'de bekleyen kayıt hemen yapılır. `Save` hatası → durum çubuğu: "Benzer kararları kaydedilemedi; işaretler bu oturumda geçerli."
  - Sayaç: `"İncelenen: {n} / {toplam}"`; `Reviewed` değişince güncellenir. Süzgeç: `CollectionViewSource.GetDefaultView(_groups).Filter = g => !OnlyUnreviewed || !g.Reviewed`.

- [ ] **Step 5: Elle doğrula** — `dotnet build FotoDuzenleyici.App` hatasız; uygulamayı aç, gerçek medya klasörünü hedefi `_DENEME_HEDEF` (yok) olarak tara: şerit akıcı kayıyor, küçük resimler geliyor, tıklama Kalsın/Ayıkla değiştiriyor, son kare ayıklanamıyor, uygulamayı kapat-aç-tara → işaretler geri geliyor. `dotnet test FotoDuzenleyici.sln` yeşil.

- [ ] **Step 6: Commit** — `"Benzerler: küçük resimli şerit görünümü, kararlar kalıcı"`.

---

### Task 5: Karşılaştırma penceresi

**Files:**
- Create: `FotoDuzenleyici.App/CompareWindow.xaml`, `FotoDuzenleyici.App/CompareWindow.xaml.cs`
- Modify: `FotoDuzenleyici.App/MainWindow.xaml.cs` — `OpenCompare` doldurulur.

**Interfaces:**
- Consumes: Task 2 `SimilarGroupState` (`KeepOnly`, `CanRelease`, `SetReviewed`), Task 1 `ReviewStore`.
- Produces: `public CompareWindow(IReadOnlyList<SimilarGroupState> groups, int startIndex, ReviewStore store, Action onDecisionsChanged)` — `groups` süzgeçten geçmiş (görünen) liste; `onDecisionsChanged` ana pencerenin gecikmeli kaydını ve sayacını tetikler.

- [ ] **Step 1: Düzen** — `WindowState="Maximized"`, `Owner` ana pencere, koyu arka plan (`#202020`). Üstte başlık: `"Grup {Number} / {toplam} · {KindText} · {n} kare"` + incelendiyse `" · İncelendi"`; uyarılar için aynı satırda `TextBlock`. Ortada `ItemsControl` + `UniformGrid Columns = Math.Min(4, n)`; her kutu: `Border ClipToBounds="True"` içinde `Image`, altında `"{i}  {Name}  ·  {Resolution}  ·  {SizeMb} MB  ·  {Taken}"`. Çerçeve: kalan yeşil (`#2E7D32`, 4 px), ayıklanan `Opacity=0.35`. Altta yardım satırı: "1–9 / tıklama: Kalsın ↔ Ayıkla · Boşluk: yalnız bu kalsın · Enter: incelendi, sonraki · ← →: grup · Tekerlek / Z: yakınlaştır · 0: sıfırla · Esc: kapat".

- [ ] **Step 2: Görüntüler** — açılışta her kare `Task.Run(() => ImageHasher.Decode(path, orientation, 1200))`; grup değişince eski sonuçlar sürüm sayacıyla yok sayılır (`MainWindow._previewVersion` kalıbı). İlk yakınlaştırmada her kare için `decodePixelWidth: 0` ile tam çözünürlük arka planda; gelince `Image.Source` değiştirilir.

- [ ] **Step 3: Eşzamanlı yakınlaştırma** — tek bir paylaşılan `ScaleTransform` + `TranslateTransform` (`TransformGroup`) bütün `Image`'ların `RenderTransform`'u; tekerlekte ölçek ×1.25 / ÷1.25, sınır 1–8; `RenderTransformOrigin` farenin üzerinde bulunduğu kutudaki göreli konum (bütün kutulara aynı değer). `Z` = 2× ile o an farenin altındaki nokta; `0` = ölçek 1, kaydırma 0. Sol tuşla sürükleme (ölçek > 1 iken) `TranslateTransform`'u değiştirir; ölçek 1'deyken sol tık işaret değiştirir.

- [ ] **Step 4: Tuşlar** — `PreviewKeyDown`: `D1`–`D9` / `NumPad1`–`NumPad9` → o karenin `Keep`'ini çevir; `Space` → son tıklanan ya da fare altındaki kareyle `KeepOnly`; `Enter` → `SetReviewed(store, true)`, `onDecisionsChanged()`, sonraki grup (son gruptaysa pencere kapanır ve durum "Bütün gruplar gözden geçirildi"); `Left`/`Right` → önceki/sonraki grup (yakınlaştırma sıfırlanır); `Escape` → kapat. Bırakılamayan kare (`CanRelease == false`) → başlıkta "Her grupta en az bir kare kalmalı." (3 sn sonra silinir).

- [ ] **Step 5: `OpenCompare`** — `var visible = CollectionViewSource.GetDefaultView(_groups).Cast<SimilarGroupState>().ToList()`; `new CompareWindow(visible, visible.IndexOf(g), _reviews, OnDecisionsChanged).ShowDialog()`; kapanınca `SimilarList` seçimi son gösterilen gruba kaydırılır.

- [ ] **Step 6: Elle doğrula** (gerçek arşiv, salt okunur hedef): 14 karelik grupta 4×4 düzen; 1–9, Boşluk, Enter, ← → çalışıyor; son kare ayıklanamıyor ve uyarı çıkıyor; tekerlekle bütün kareler aynı noktaya yakınlaşıyor, tam çözünürlük birkaç saniyede geliyor; Enter ile incelenen gruplar şeritte "✓ incelendi" ve sayaç artıyor; "Yalnız incelenmemişler" açıkken → yalnız onlar arasında geziyor; kapat-aç-tara sonrası incelendi bilgisi duruyor. `dotnet test FotoDuzenleyici.sln` yeşil.

- [ ] **Step 7: Commit** — `"Benzerler: tam ekran karşılaştırma penceresi"`.

---

### Task 6: Belgeler

**Files:**
- Modify: `CHANGELOG.md` (`## [Yayınlanmamış]` altına `### Eklendi`), `YOL_HARITASI.md` (Aşama 2 🔄 maddesi ✅; Aşama 3 "Kopya ve benzer grupları yan yana karşılaştırma ekranı" ✅), `README.md` gerekiyorsa Benzerler cümlesi.

- [ ] **Step 1:** CHANGELOG: şerit görünümü, karşılaştırma penceresi (tuşlar), kararların `%LOCALAPPDATA%\FotoDuzenleyici\benzer-kararlar.json` içinde saklanması ve "incelendi" bilgisi, "Yalnız incelenmemişler" süzgeci; sağdaki tek önizleme panelinin kaldırıldığı (`### Değişti`).
- [ ] **Step 2:** Yol haritası durumlarını güncelle (sürüm numarası yayın anında eklenir).
- [ ] **Step 3: Commit** — `"Belgeler: karşılaştırma ekranı"`.
