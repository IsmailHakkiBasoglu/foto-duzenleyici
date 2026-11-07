# Yeni arayüz: Temizle ekranı — tasarım

Tarih: 2026-11-08 · Çekirdek: `2026-11-07-temizle-design.md` (CleanSession, taşıma kipi, CLI)
Önceki parçalar: `2026-11-07-yeni-arayuz-1-design.md`, `2026-11-08-yeni-arayuz-2-design.md`

## Amaç

Ana ekrandaki **Temizle** kartını çalışır yapmak. Kullanıcı bir ya da birden çok klasörü (düzenli arşivin
kendisi ya da düzenlenmemiş bir klasör) seçer, tarar, Düzenle'deki inceleme ekranlarıyla karar verir,
onaylar. Ayıklananlar her klasörün kendi `_Ayiklananlar\Tekrarlar|Benzerler|Kusurlu` klasörüne, göreli
yoluyla **taşınır** (kopyalanmaz). Geri Al hepsini yerine döndürür.

**Başarı ölçütü:**
- Temizle; klasörler, tarama, inceleme (Yinelenenler, Benzerler + toplu düğmeler, Kusurlu, Okunamayanlar),
  onay, uygulama ve Bitti ekranlarıyla uçtan uca çalışır.
- Kopyalarda kalan: kullanıcının seçtiği → öncelikli klasördeki → en eski.
- Uygulama yarıda durdurulursa kaldığı yerden sürer; Geri Al ile tamamı geri alınır.
- Düzenle'nin davranışı ve bütün mevcut testleri değişmez.

**Kullanıcının seçimleri (2026-11-08):**
- Kullanım: hem düzenli arşivin kendisi hem düzenlenmemiş klasörler.
- Yaklaşım A: Düzenle sihirbazı ortaklaştırılır; farklar bir "iş türü" katmanında (`IScanJob`).
- Tasarımın iki bölümü (akış, yapı) sohbette onaylandı.

**Değişmeyen kurallar:** hiçbir dosya silinmez ve üzerine yazılmaz; her işlem günlüğe yazılır ve geri
alınabilir; hiçbir şey onaysız taşınmaz.

**Kapsam dışı:** `_Ayiklananlar`'ı boşaltma düğmesi; klasik pencereye Temizle; farklı diske ayıklama;
Yedekle ekranı.

## Akış

**Ana ekran:** Temizle kartının "Başla" düğmesi etkin. Son temizlenen klasörler Düzenle'ninkilerden ayrı
tutulur (`AppSettings.RecentClean`, en çok 5) ve kartta "Son: …" ile sunulur.

**1 · Klasörler** — başlık "Neyi temizleyelim?"
- "Klasörler": birden çok; var olmayan uyarıyla gösterilir, taranmaz (Düzenle'deki gibi).
- "Kopyalarda öncelikli klasörler (isteğe bağlı)": birden çok; kopya grubunda bu klasörlerdeki dosya kalır.
  Boşsa en eski kalır.
- Arşiv seçimi ve tarih düzeni seçenekleri görünmez. Düzen seçenekleri penceresinde yalnız "Tarama"
  bölümü (benzer ara, bulanık işaretle) görünür; ayarlar Düzenle ile ortak.
- "Tara ve incele" en az bir var olan klasör olunca etkin.

**2 · Tara ve incele** — Düzenle'deki ekran, şu farklarla:
- Özet kutucukları: **Ayıklanacak kopya**, **Benzer**, **Kusurlu**, **Kazanılacak alan** (ayıklananların
  toplam boyutu).
- Plan tablosu yalnız ayıklanacakları gösterir (CleanSession böyle kurar).
- Yinelenenlerde arşiv etiketi "düzenli" yerine "öncelikli"; öneri öncelikli klasöre göredir.
- Dikkat listesi ve alt çubuk metinleri iş türüne göre ("… _Ayiklananlar'a taşınacak").

**3 · Onayla**
- Özet satırları: "N kopya → `_Ayiklananlar\Tekrarlar`", "N benzer → `_Ayiklananlar\Benzerler`",
  "N kusurlu → `_Ayiklananlar\Kusurlu`" (0 olanlar gizli) ve "Kazanılacak alan".
- Açıklama: "Dosyalar kopyalanmaz; aynı disk içinde yeri değişir, hiçbir şey silinmez."
- Denetimler: yarım kalmış temizlik (`CleanSession.FindUnfinished`) → Devam et / Vazgeç; günlük yolu
  (`CleanSession.NewJournalPath`).
- Gizlenenler: boş alan denetimi (taşımada gerekmez), başka disk uyarısı, Geri Dönüşüm kutusu.
- Uygula: `Executor.Run(items, recycleSource: false, …, move: true)`.

**3a / 3b · Uygulanıyor / Bitti** — aynı; Bitti'de "Arşivi aç" yerine **"Ayıklananları aç"** (ilk
klasörün `_Ayiklananlar`'ı). Durdur, Kaldığı yerden devam et, Geri Al aynen.

**Sağ üstteki Geri Al:** temizlik günlüklerini de alır. Günlük `<kök>\_Ayiklananlar\_Gunluk\…`
olduğundan "aç" düğmesi `_Ayiklananlar` klasörünü açar.

**Klasik görünüm:** Temizle akışından açılırsa boş açılır (klasikte Temizle yok).

## Yapı

```
FotoDuzenleyici.Core        DuplicateGroupState.Build(groups, Func<MediaFile,bool> preferred) aşırı yüklemesi
FotoDuzenleyici.ViewModels  IScanJob, OrganizeJob, CleanJob; FlowKind { Organize, Clean }
                            OrganizeFlowViewModel(kind), FoldersStep / ReviewStep / ConfirmStep / ApplyStep iş türüne göre
                            HomeViewModel: CleanRequested, LastClean; AppSettings.RecentClean
FotoDuzenleyici.App         Home kartı, FoldersPage, ReviewPage, ConfirmPage, ApplyPage metin ve görünürlükleri
```

**`IScanJob`** (ViewModels):
- `IReadOnlyList<string> Sources`; `List<MediaFile> Files`; `List<DuplicateGroup> Duplicates`;
  `IReadOnlyList<string> ScanErrors`.
- `void Scan(IProgress<(int,int)> scan, IProgress<(int,int)> hash, CancellationToken ct)`.
- `IReadOnlyList<string> IgnoreDirs` — benzerlik / netlik ölçümünde atlanacaklar.
- `bool IsPreferred(MediaFile f)` — Yinelenenlerdeki öneri ve etiket.
- `Plan BuildPlan(similarOf, flawed, keepDuplicates)`.
- `string NewJournalPath(DateTime now)`; `List<(string Journal, int Remaining)> FindUnfinished()`.
- `bool MoveOnly` — taşıma kipi; `string RejectsFolder` — Bitti'de açılacak klasör (Düzenle: arşiv).

**`OrganizeJob`**: `OrganizeSession` + `PlanOptions`; bugünkü davranış (IsPreferred = düzenli arşivde,
günlük `<arşiv>\_Gunluk\islem-…`, FindUnfinished = arşivin `_Gunluk`'u, MoveOnly = false).
**`CleanJob`**: `CleanSession`; IsPreferred = öncelikli klasörde, IgnoreDirs = boş (`_Ayiklananlar` zaten
taranmaz), MoveOnly = true.

## Hatalar

- Farklı diske taşıma Executor'da reddedilir ("farklı disk; taşıma yapılmaz"); satır Yapılamayanlar'a
  düşer, dosyaya dokunulmaz. Her kök kendi `_Ayiklananlar`'ına taşındığı için normalde oluşmaz.
- Hedefte aynı ad: CleanSession " (2)" ekler.
- Klasörlerden biri tarama sırasında yok olursa Scanner hatası Okunamayanlar'a gider.

## Testler

Sahte servisler, gerçek dosyalar:
- Temizle taraması: kopya + benzer + kusurlu bulunur; plan yalnız ayıklananları içerir.
- Öncelikli klasördeki kopya kalır; elle seçim önceliği geçer.
- Benzer / kusurlu kararları `_Ayiklananlar\Benzerler|Kusurlu` altına göreli yolla gider.
- Uygula taşır (kaynak yok, hedef var, içerik aynı); Geri Al hepsini yerine döndürür.
- Durdur → Kaldığı yerden devam et; yarım kalmış temizlik Onayla'da görünür.
- Ana ekrandan Temizle akışı açılır; son temizlenenler ayrı listede.
- Düzenle'nin bütün mevcut testleri geçer.

## Gözden geçirmede bakılacaklar

- İç içe seçilmiş klasörler (`D:\A` ve `D:\A\B`) tek kök sayılıyor mu; ayıklanan doğru `_Ayiklananlar`'a mı gidiyor.
- Temizle'den sonra Düzenle taraması `_Ayiklananlar`'ı görmüyor mu.
- Temizlik günlüğü sağ üstteki Geri Al ile seçilince doğru geri alınıyor mu.
- Taşıma sırasında Durdur: yarım dosya kalmıyor mu.
- Kazanılacak alan ve özet sayıları plandakiyle tutarlı mı.
