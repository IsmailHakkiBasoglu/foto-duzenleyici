# Temizle modülü — tasarım

Tarih: 2026-11-07 · Yeni arayüz yol haritası: "Düzenle / Temizle / Yedekle" bağımsız modüller

## Amaç

Bir ya da birden çok klasördeki birebir kopyaları, benzer kareleri ve bulanık / karanlık fotoğrafları
bulmak ve ayıklamak. Dosyalar tarih klasörlerine taşınmaz, düzen olduğu gibi kalır. Düzenle'den bağımsız
kullanılır, ama aynı çekirdek parçalarını (tarayıcı, kopya bulucu, benzerlik ve netlik ölçümü,
önbellekler) paylaşır.

**Başarı ölçütü:**
- Bir klasör Temizle ile tarandığında fazla kopyalar ve kazanılacak alan raporlanır.
- Uygulandığında ayıklananlar anında `_Ayiklananlar` altına taşınır.
- Geri Al hepsini yerine döndürür.
- `_Ayiklananlar` ne Temizle'nin ne Düzenle'nin sonraki taramasında görünür.

**Kullanıcının seçimleri (2026-11-07):**
- Ayıklananlar, temizlenen klasörün içinde `_Ayiklananlar` klasörüne gider; Geri Al çalışır. Klasörü
  baktıktan sonra kullanıcı kendisi siler.
- Kopyada kalan konum: kullanıcının seçtiği → öncelikli klasördeki → en eski.
- Tasarım (taşıma yöntemi ve `_Ayiklananlar`'ın her iki modülde taranmaması dahil) onaylandı.

**Değişmeyen kurallar:**
- Hiçbir dosya silinmez.
- Hiçbir dosyanın üzerine yazılmaz.
- Her işlem günlüğe yazılır ve geri alınabilir.
- Hiçbir şey kullanıcı onaylamadan taşınmaz.

**Kapsam dışı:**
- Temizle arayüzü: yeni arayüzle gelecek, mevcut pencereye dokunulmaz.
- `_Ayiklananlar`'ı boşaltma ya da Geri Dönüşüm Kutusu'na gönderme düğmesi.
- Farklı diskteki bir hedefe ayıklama.
- Benzer / kusurlu kararlarının komut satırından verilmesi.

## Bileşenler

### `DuplicateKeeper` (Core, genelleştirme)

Yeni aşırı yükleme:

```
MediaFile Choose(DuplicateGroup g, Func<MediaFile, bool> preferred, IReadOnlySet<MediaFile>? chosen = null)
```

Sıra:
1. `chosen` içindeki üye;
2. `preferred` olan ilk üye (üye sırası: `Keeper`, sonra `Copies`);
3. `g.Keeper` (en eski).

Mevcut `Choose(g, PlanOptions, chosen)`, `m => IsOrganized(m, opt)` ile bu yeni aşırı yüklemeyi çağırır;
davranışı değişmez.

`static bool InAny(MediaFile f, IEnumerable<string> folders)`: dosya verilen klasörlerden birinin içinde mi
(büyük/küçük harf duyarsız, `D:\A` ile `D:\A2` karışmaz).

### Tarama: `_Ayiklananlar` hiçbir yerde taranmaz

`ScanOptions.ExcludedFolderNames` alanı eklenir. Varsayılanı `{ "_Ayiklananlar" }`. `Scanner` bu
adlardan birini taşıyan klasöre girmez; klasör hangi derinlikte olursa olsun. Böylece Düzenle (uygulama,
komut satırı, `OrganizeSession`) ve Temizle aynı kuralı kendiliğinden uygular.

### `CleanSession` (Core, yeni)

```
CleanSession(IEnumerable<string> roots, IEnumerable<string>? priorityFolders = null, ScanOptions? scan = null)
IReadOnlyList<string> Roots            // iç içe kökler tekilleştirilmiş (OrganizeSession.ScanRoots ile aynı kural, arşivsiz)
List<MediaFile> Files; List<DuplicateGroup> Duplicates; List<string> ScanErrors
void Scan(IProgress<(int,int)>? scan = null, IProgress<(int,int)>? hash = null, CancellationToken ct = default)
MediaFile Keeper(DuplicateGroup g, IReadOnlySet<MediaFile>? chosen = null)   // DuplicateKeeper + öncelikli klasörler
Plan BuildPlan(IReadOnlySet<MediaFile>? keepDuplicates = null,
               IReadOnlyDictionary<MediaFile, MediaFile>? similarOf = null,
               IReadOnlySet<MediaFile>? flawed = null)
string NewJournalPath()                 // <ilk kök>\_Ayiklananlar\_Gunluk\temizlik-yyyyMMdd-HHmmss.jsonl
const string RejectsFolder = "_Ayiklananlar"
```

Benzerlik ve netlik ölçümü `CleanSession` içinde sarmalanmaz. Arayüz bugünkü gibi
`SimilarityScan` / `QualityScan`'i `session.Files` ve `session.Duplicates` ile çağırır, kararları
`similarOf` / `flawed` olarak geri verir.

**Plan kuralları:**
- **Yalnız ayıklananlar plana girer.** Kalan dosyaların plan satırı yoktur; Temizle'de "yerinde kalır"
  bilgisi gerekmez.
- **Eylem önceliği:** fazla kopya → `Duplicate` (`DuplicateOf` = kalan). Kopya değilse ve benzerlerde
  ayıklandıysa → `Similar` (`Note`: "Muhtemelen aynı kare: <kalanın adı>"). İkisi de değilse ve kusurluysa
  → `Flawed`.
- **Hedef:** `<dosyanın kökü>\_Ayiklananlar\<Tekrarlar|Benzerler|Kusurlu>\<köke göre göreli yol>`.
  Dosyanın kökü, `Roots` içinde onu kapsayan köktür.
- **Çakışma:** hedefte dosya varsa ya da plandaki başka bir hedefle aynıysa ada ` (2)`, ` (3)`… eklenir
  (uzantıdan önce).

### `Executor`: taşıma modu

- `Run(items, recycleSource, progress, ct, move: false)`: yeni `move` parametresi. `true` ise günlük
  satırına `Move = true` yazılır.
- **Taşıma adımları** (`Process`):
  - hedefte dosya varsa üzerine yazılmaz, hata olur;
  - kaynak ile hedef farklı sürücüdeyse hata olur ("farklı disk; taşıma yapılmaz");
  - değilse `File.Move(kaynak, hedef)` çalışır. Bu tek bir yeniden adlandırmadır; kopya ya da Geri
    Dönüşüm Kutusu adımı yoktur.
  - Durum `Moved` olur.
- **Hash:** taşımada başlamadan hash hesaplanmaz; yeniden adlandırma içeriğe dokunmaz. Kopyalarda hash
  zaten bilindiği için günlüğe yine yazılır.
- **Sürdürme:** kaynak yok ama hedef varsa taşıma tamamlanmış sayılır (`Moved`).
- **Geri Al:** `Moved` satırlarında hedef duruyor ve kaynak yolu boşsa `File.Move(hedef, kaynak)` yapılır.
  Kaynak yolunda başka bir dosya varsa dokunulmaz (Skipped). `Copied` / `Recycled` için davranış aynı kalır.

### Komut satırı

- `--clean <klasör>`: birden çok verilebilir. `--priority <klasör>` ile öncelikli klasör (birden çok)
  belirtilir.
- **Rapor:**
  - taranan dosya sayısı;
  - kopya grubu sayısı, fazla kopya sayısı, kazanılacak alan;
  - ilk 10 grup, kalan ★ ile işaretli.
- `--csv` verilmişse `…-temizlik.csv` yazılır: `Eylem;Kaynak;Hedef;Korunan`.
- `--apply` yalnız birebir kopyaları `_Ayiklananlar\Tekrarlar`'a taşır. Ardından günlük yolunu yazar;
  `--undo <günlük>` ile geri alınır.

## Testler (xUnit, geçici klasörlerle)

1. `DuplicateKeeper`:
   - öncelikli klasördeki kopya en eskiye üstün gelir;
   - öncelikli klasörde yoksa en eski kalır;
   - elle seçim her şeyi geçer;
   - `InAny` `D:\A2`'yi `D:\A` içinde saymaz.
2. `Scanner`, her derinlikteki `_Ayiklananlar` klasörünü atlar (alt klasörde de).
3. `CleanSession.BuildPlan`:
   - fazla kopya `<kök>\_Ayiklananlar\Tekrarlar\<göreli yol>` hedefine gider;
   - kalan dosyanın plan satırı yoktur;
   - iki kök verildiğinde her dosya kendi kökünün `_Ayiklananlar`'ına gider;
   - kopyanın kalanı başka kökte olabilir.
4. Benzer ayıklanan → `Benzerler`, kusurlu → `Kusurlu`. Hem kopya hem benzer olan dosya `Tekrarlar`'a
   gider.
5. Hedefte aynı ad varsa ` (2)` eklenir. Aynı plan içinde iki dosya aynı hedefe düşerse ikincisine ` (2)`
   eklenir.
6. `Executor` taşıma modu:
   - dosya taşınır, kaynak yolu boşalır;
   - içerik ve zaman korunur;
   - Geri Al dosyayı yerine geri taşır;
   - hedefte dosya varsa üzerine yazılmaz, hata olur;
   - yarım kalan (kaynak yok, hedef var) satır sürdürmede `Moved` olur.
7. Uçtan uca: tara → planla → taşı → yeniden tara. `_Ayiklananlar` görünmez, kopya grubu kalmaz. Geri Al
   ile ilk duruma dönülür.
