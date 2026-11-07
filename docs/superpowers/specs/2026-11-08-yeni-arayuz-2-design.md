# Yeni arayüz, 2. parça (inceleme ekranları) — tasarım

Tarih: 2026-11-08 · Önceki parça: `2026-11-07-yeni-arayuz-1-design.md` (0.2.0)
Görsel taslak: `docs/tasarim/arayuz-taslak.html` (ekran 2, 2b Yinelenenler, 2c Benzerler, 2d Kusurlu)

## Amaç

Klasik penceredeki üç incelemeyi, davranışını değiştirmeden, yeni kabuğun "Tara ve incele" adımına
taşımak:

- **Yinelenenler:** her kopya grubunda korunacak konumu seçmek.
- **Benzerler:** aynı kare, küçültülmüş kopya ve seri çekim gruplarında kareleri ayıklamak.
- **Kusurlu:** bulanık ve karanlık kareleri ayıklamak.

**Başarı ölçütü:**
- Kullanıcı yeni arayüzde tarar, üç incelemede de karar verir, Onayla'ya geçer.
- Planda kararları görür (_Tekrarlar, _Benzerler, _Kusurlu), uygular, Geri Al ile geri alabilir.
- Bunların hepsi Klasik görünüme gerek kalmadan yapılır.
- Kararlar klasik pencereyle ortak dosyada saklanır; biri ötekinin kararını görür.

**Kullanıcının seçimleri (2026-11-08):**
- Hedef: klasikteki özellikler aynen, yeni görünümle. İnceleme akışı yeniden tasarlanmaz.
- Yaklaşım A: her inceleme için ayrı, test edilebilir ViewModel; Core'daki durum nesneleri yeniden
  kullanılır; klasik pencereye neredeyse dokunulmaz.
- Tasarımın üç bölümü (tarama ve plan, ekranlar, yapı ve testler) sohbette onaylandı.

**Değişmeyen kurallar** (1. parçadan): Core'un güvenlik davranışı aynen; arayüz dosyaya Core'u atlayarak
dokunmaz; hiçbir şey onaysız uygulanmaz; ayıklananlar silinmez, klasöre ayrılır.

**Kapsam dışı:** Temizle ve Yedekle ekranları; Setup.exe; klasik pencerenin kaldırılması; zaman
çizelgesi; karşılaştırma penceresinin yeniden tasarımı.

## Tarama

`OrganizeFlowViewModel.Scan` dört aşamada yürür (klasikteki sırayla):

1. Metadata okuma (Scanner).
2. Birebir kopyalar (DuplicateFinder, SHA-256).
3. Benzerlik izi (`SimilarityScan.Compute`, `HashCache` önbelleği) — "Benzer fotoğrafları ara" açıksa.
4. Netlik ölçümü (`QualityScan.Compute`, `QualityCache` önbelleği) — "Bulanık/karanlık kareleri
   işaretle" açıksa.

- Benzerlik ve netlik aşamalarında `_Benzerler` ve `_Kusurlu` (arşivin altında) hariç tutulur.
- **İki yeni ayar** (`AppSettings`): `FindSimilar = true`, `MarkFlawed = true`. Düzen seçenekleri
  penceresinde iki onay kutusu olarak görünür ve kalıcıdır.
- **İlerleme metni** aşamayı söyler: "Metadata okunuyor… d/t", "Hash hesaplanıyor… d/t",
  "Benzerlik izi… d/t (c'i önbellekten)", "Netlik ölçülüyor… d/t (c'i önbellekten)".
- **Durdurma:**
  - Aşama 1–2'de durdurulursa bugünkü gibi Klasörler'e dönülür.
  - Aşama 3'te durdurulursa benzer gruplar boş kalır, aşama 4 atlanır. Aşama 4'te durdurulursa Kusurlu
    boş kalır; benzerler yine gösterilir (seri çekim sıralaması netlik puanı olmadan).
  - Her iki durumda İnceleme açılır ve hata şeridinde klasikteki not çıkar: "Benzerlik araması
    durduruldu; hesaplanan izler kaydedildi, bir sonraki taramada kaldığı yerden sürer." (netlik için
    karşılığı).
- Benzer ve kusurlu grupların kurulması klasikteki `ShowSimilar` / `ShowFlawed` mantığıyla aynıdır:
  `SimilarFinder.Find` + `SameFrameFinder`, `SimilarGroupState.FromGroups`, RAW eşi rozeti
  (`RawJpgPairs.Index`), `StoreKey` (`HashCache.Key`), `ApplyBurstRanks`, `Restore(ReviewStore)`;
  kusurlu için `FlawedList.Build`. Bu mantık App'ten Core'a, iki pencerenin ortak kullandığı tek bir
  yere (`ReviewBuilder`) taşınır; klasik pencere de onu çağırır.
- Okunamayan dosyalar (tarama + benzerlik + netlik hataları) Okunamayanlar bölümünde listelenir.

## Kararların saklanması

- Kararlar klasikle aynı dosyada: `ReviewStore` (`ReviewStore.DefaultPath`). Klasikte verilen karar
  yenide görünür, tersi de.
- Dosya okunamazsa boş başlanır, hata şeridinde "Önceki benzer kararları okunamadı; boş başlandı." çıkar.
- Her değişiklikte kayıt planlanır; art arda tıklamalar tek yazmaya iner (1 sn, klasikteki gibi).
  Pencere kapanırken, Onayla'ya geçerken ve yeniden taramada bekleyen kayıt hemen yazılır.
- Kayıt başarısızsa hata şeridinde "Benzer kararları kaydedilemedi; işaretler bu oturumda geçerli." çıkar.
- İki pencere aynı anda açıksa son yazan kazanır (klasik pencere tek başına da böyle çalışıyordu).

## Ekranlar

"Tara ve incele" adımının sol menüsü bölüm değiştirir; sağ taraf seçili bölümü gösterir. Alt çubuk
("Onay ekranına →") her bölümde aynıdır.

| Menü | Sayaç | İçerik |
|---|---|---|
| Özet ve Plan | – | 1. parçadaki ekran |
| Yinelenenler | grup sayısı | Kopya grupları kart halinde, en çok yer kazandıran başta. Kartta küçük resim, ad, boyut, "N konum · X MB boşa", her konum için seçim düğmesi; arşivdeki kopyada "düzenli", elle seçilen grupta "elle seçildi" etiketi. Üstte "N grup · M kopya · X GB kazanılacak · k grupta elle seçim" ve **Öneriye dön**. Çift tık dosyayı Windows'ta açar. |
| Benzerler | incelenen / toplam | Her grup bir satır: tür, kare sayısı, tarih aralığı, yatay küçük resim şeridi. Kartta ★ önerilen, ◎ en net, RAW, BULANIK/KARANLIK rozetleri, seri çekimde netlik yüzdesi. Tek tık kalsın ↔ ayıkla; grubun son karesi ayıklanamaz (hata şeridinde "Her grupta en az bir dosya kalmalı."). Çift tık ya da Enter karşılaştırmayı açar. "Yalnız incelenmemişler" süzgeci. |
| Kusurlu | ayıklanacak / toplam | Tek kare kartları ızgarada. "Bulanıklar" ve "Karanlıklar" süzgeçleri; "Bulanık: a · Karanlık: b · Ayıklanacak: c / n". Tek tık kalsın ↔ ayıkla; çift tık tam ekran. |
| Okunamayanlar | sayı | Yol ve neden listesi. |

- Ölçüm yapılmadıysa (seçenek kapalı ya da durduruldu) ilgili bölümde boş durum yazısı: "Benzerlik
  araması yapılmadı." / "Netlik ölçülmedi." ve Düzen seçeneklerine giden bir bağlantı.
- Özet'teki dikkat listesi bölümlere bağlanır ("İncele" düğmesi ilgili bölümü açar).
- Plan tablosunun süzgecine **Benzer** ve **Kusurlu** eklenir.
- **Karşılaştırma penceresi:** klasikteki `CompareWindow` olduğu gibi açılır (görünen gruplar
  arasında gezinir, kararlar aynı nesnelere yazılır). Kapanınca listede son bakılan grup seçili olur;
  süzgeç açıksa incelenenler düşer.
- **Küçük resimler:** `ThumbnailLoader` ile, yalnız ekranda görünen kartlar için; bu iş görünüm
  katmanında (App) yapılır, ViewModel'de değil. Listeler sanallaştırılır.
- Onay ekranının özetine iki satır eklenir: "_Benzerler'e ayrılacak" ve "_Kusurlu'ya ayrılacak".

## Plan ve kararlar

- İncelemede her değişiklik planı **eskidi** diye işaretler; alt çubuk "Plan güncellenecek: …" der.
- "Onay ekranına →" basıldığında plan eskiyse yeniden kurulur:
  1. Yinelenen durumları güncel arşive göre tazelenir (`DuplicateGroupState.Refresh`).
  2. Benzer haritası (ayıklanan → grupta kalan; önerilen işaretliyse o) ve kusurlu kümesi toplanır.
  3. Ayıklanan karelerin RAW eşi varsa tek soru sorulur (klasikteki metin):
     - **Evet:** RAW'lar da JPG'leriyle ayrılır.
     - **Hayır:** RAW'lar normal klasörlerine gider.
     - **İptal:** İnceleme'de kalınır, plan kurulmaz.
  4. `OrganizeSession.BuildPlan(similarOf, flawed, keepDuplicates)`; özet kutucukları ve plan tablosu
     yenilenir; sonra Onayla açılır.
- Plan eskimediyse doğrudan Onayla açılır (RAW sorusu tekrar sorulmaz).
- Onayla'dan "İncelemeye dön" ile gelinip karar değiştirilirse plan yine eskir.

## Mimari

```
FotoDuzenleyici.Core        + ReviewBuilder (benzer/kusurlu grup kurma, klasik ve yeni ortak)
FotoDuzenleyici.ViewModels  + DuplicatesReviewViewModel, SimilarReviewViewModel, FlawedReviewViewModel
                            ReviewStepViewModel: bölüm, PlanStale, plan yeniden kurma
                            OrganizeFlowViewModel: dört aşamalı tarama
FotoDuzenleyici.App         + DuplicatesSection, SimilarSection, FlawedSection, ErrorsSection (UserControl)
                            WpfCompare (ICompare), IDialogs.Ask'ın MessageBox uygulaması
```

**Yeni ve değişen servisler** (`Services`):
- `ICompare.Open(IReadOnlyList<SimilarGroupState> groups, int start, ReviewStore store, Action changed)`
  → `(SimilarGroupState Current, bool FinishedAll)`.
- `IDialogs.Ask(string title, string message)` → `Answer { Yes, No, Cancel }`.
- `CachePaths(string Reviews, string Hashes, string Quality)`: öntanımlı olarak Core'daki
  `DefaultPath`'ler; testler geçici klasör verir.
- Kayıt gecikmesi `Services.Schedule: Func<TimeSpan, Action, IDisposable>` ile verilir (App'te
  DispatcherTimer, testlerde hemen çalışan sahte). Yeni bir planlama öncekini iptal eder.

## Hatalar

- Okunamayan dosyalar → Okunamayanlar.
- Karar dosyası okunamazsa / yazılamazsa → hata şeridi; çalışma sürer.
- Benzerlik / netlik aşamasında beklenmeyen bir hata (iptal dışında) taramayı düşürmez: o aşamanın
  sonucu boş kalır, hata şeridinde mesaj çıkar, inceleme yine açılır.
- Uygulama sürerken İnceleme'ye dönülemez (1. parçadaki kilit aynen).

## Testler

Sahte servislerle, gerçek dosyalarla (örnek JPG'ler test içinde üretilir):

- Dört aşamalı tarama; seçenekler kapalıyken aşama 3–4 atlanır.
- Aşama 3'te ve 4'te durdurma: İnceleme açılır, not hata şeridinde, ilgili bölüm boş.
- Benzerlerde ayıklanan kare plana `Similar` olarak, Kusurlu'da ayıklanan `Flawed` olarak girer.
- Grubun son karesi ayıklanamaz.
- RAW sorusu: Evet / Hayır / İptal'in plana etkisi; plan eskimediyse soru tekrar sorulmaz.
- Yinelenenlerde elle seçim plana geçer; "Öneriye dön" elle seçimi kaldırır.
- Kararlar `ReviewStore`'a yazılır ve yeni taramada geri gelir; bozuk dosya uyarıyla boş başlar;
  kayıt hatası taramayı bozmaz.
- Karar değişince `PlanStale`; Onayla'ya geçişte plan yeniden kurulur ve özet güncellenir.
- Klasik pencere `ReviewBuilder`'a geçtikten sonra mevcut testleri geçer.

Görsel doğrulama 1. parçadaki gibi test programıyla (ekrandaki yazılar + bağlama hataları, açık ve koyu
tema); son bakışı kullanıcı yapar.

## Gözden geçirmede bakılacaklar

- Binlerce grupta kaydırma akıcı mı (sanallaştırma + küçük resim iptali).
- Karşılaştırma penceresinde verilen karar şeritte ve sayaçta hemen görünüyor mu, plan eskiyor mu.
- RAW eşi olan kare hem Benzerler'de hem Kusurlu'da ayıklanırsa RAW bir kez ve doğru klasöre gider mi.
- Tarama yeniden yapılınca eski grupların olay abonelikleri bırakılıyor mu.
- Klasik pencere ile yeni pencere aynı anda açıkken karar dosyası bozulmuyor mu.
