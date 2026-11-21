# Sürekli galeri ve oturum kataloğu — tasarım

Durum: onaylandı (2026-11-22) · Tarih: 2026-11-20 · Yol haritası: "Fotoğraflar uygulaması yerine", 1. sürüm

## Amaç ve neden

Kullanıcının uzun vadeli hedefi, uygulamayı fotoğraf ve videolar için **ana galeri** olarak kullanmak; Windows
Fotoğraflar'ı ve Gezgin'i açmadan bakmak, ayıklamak, düzenlemek. Bugün zaman çizelgesi bir yıl × ay tablosu; bir
aya tıklayınca yalnız o ay açılıyor. Kullanıcı Windows Fotoğraflar / immich'teki gibi **bütün yılların tek bir
uzun ızgarada aktığı** ve sağ kenardaki **sürüklenebilir zaman çubuğuyla** yıllar arasında atlanabilen bir galeri
istedi. Dosyaların hangi alt klasörde durduğu galeride önemli değil; seçilen en üst klasörün altındaki her şey
zamana göre görünür.

Kullanıcı kararları (bu konuşmada):
- Sürekli galeri (seçenek 2), tablo da kalır.
- Görüntüleyici yalnız uygulamanın içinden açılacak; Gezgin'de çift tık ile açılma yok. Sağ tık özellikleri sonra.
- Videolar galeride ve görüntüleyicide yer alacak.
- Okunan bilgiler **uygulama açık kaldıkça bellekte** tutulur; kapatıp açınca yeniden okunur. Diske katalog
  dosyası yazılmaz. Gerektiğinde elle **yeniden tara** düğmesi.
- İş iki sürüme bölünür (bu belge 1. sürüm). Kullanıcı itiraz etmedi.

## 1. sürüm kapsamı

1. **Oturum kataloğu:** seçilen kök için her medya dosyasının yolu, türü, boyutu, değişme zamanı, çekim zamanı ve
   tarih kaynağı bellekte; bir kez okunur, oturum boyunca bütün ekranlar bunu kullanır.
2. **Sürekli galeri:** bütün aylar yeniden eskiye tek ızgarada, ay başlıklarıyla.
3. **Zaman çubuğu:** sağ kenarda sürüklenebilir; yıl etiketleri, sürüklerken "Ekim 2026" balonu.

2. sürüm (ayrı tasarım konuşması): görüntüleyici — büyük resim, yakınlaştırma, bilgi paneli (EXIF, konum),
video oynatma, yığınlar (RAW+JPG, seri çekim), kayıpsız döndürme / aynalama.

## Hedef dışı (bu sürümde yok)

- Kendi görüntüleyicisi (2. sürüm). Galeride çift tık bugünkü gibi dosyayı Windows'un varsayılan uygulamasında açar.
- Diske yazılan katalog, veritabanı.
- Galeride çoklu seçim, taşıma, silme, yıldız, etiket. Ayıklama ay görünümünde kalır.
- Gezgin ile dosya ilişkilendirme, sağ tık menüsü.
- Arama, yüz, harita.

## İstenmeyenler (çalışsa bile başarısızlık sayılır)

- Galerinin dosyalara dokunması. Galeri ve katalog **salt okunur**.
- Kaydırırken takılma; büyük arşivde (≥ 50.000 dosya) bellek ya da arayüz donması.
- Tablo ile galerinin aynı ay için farklı sayı göstermesi.
- Uygulamanın kendi işlemlerinden (Ayıkla, Düzenle, Temizle) sonra galerinin var olmayan dosyaları göstermesi.

## Yaklaşımlar

1. **Aylar ekrana geldikçe okunur (kayıt yok):** tablo sayılarıyla boş kutular yerleşir, ay görünür olunca
   `MonthReader` ile okunur. Az iş; ama her açılışta ve her uzak atlamada bekleme, okuma tekrarı. Elendi.
2. **Kalıcı katalog dosyası:** her kök için diskte dizin, sonraki açılışlar anında. Kullanıcı istemedi: kapat-aç
   yapınca yeniden okunsun.
3. **Oturum kataloğu (seçilen):** okuma bir kez, bellekte; tablo, galeri ve ay görünümü aynı veriyi kullanır.
   Kapatınca gider. "Yeniden tara" ile tazelenir. İleride diske yazmak istenirse aynı yapı kaydedilir.

## Nasıl çalışır

### Katalog

- Kök başına bir katalog nesnesi; aynı oturumda tek kök tutulur (kök değişince eskisi bırakılır).
- Okuma bugünkü "çekim tarihine göre" sayımın taramasıyla aynı: özel klasörler (`_Tekrarlar`, `_Benzerler`,
  `_Kusurlu`, `_Gunluk`, `_Ayiklananlar`, `Tarihsiz`) dışlanır, Foto / Raw / Video sayılır, 4 iş parçacığı,
  ilerleme ve Durdur.
- Her girişte `Scanner.ReadFile`'ın döndürdüğü `MediaFile` olduğu gibi saklanır (yol, tür, boyut, değişme ve
  çekim zamanı, tarih kaynağı, yön, genişlik, makine bilgileri…) ve "ay" (aşağıdaki yönteme göre). Ay görünümü ve
  inceleme araçlarının ihtiyaç duyduğu alanlar böylece eksik kalmaz; yeniden okuma gerekmez.
- **Yöntem kullanıcının seçimi olarak kalır** (bugünkü gibi):
  - Çekim tarihine göre: dosyanın ayı çekim tarihi.
  - Klasör adlarına göre: dosyanın ayı içinde durduğu `yyyy-MM` klasörü (bugünkü ay görünümü davranışı);
    ay içindeki sıra çekim zamanına göre. Ay klasörü dışındaki dosyalar bugünkü gibi "dışarıda" sayılır,
    galeride görünmez.
  - Böylece tablo ve galeri aynı sayıları gösterir.
- Klasör adları yönteminde tablo bugünkü gibi hemen (EXIF okumadan) sayılır; katalog okuması galeri ilk açıldığında
  başlar. Çekim tarihi yönteminde sayım zaten her dosyayı okuduğundan katalog bu tek okumayla dolar; ikinci okuma olmaz.
- Ay görünümü bir aya girerken katalog doluysa o ayın dosyalarını katalogdan alır, yeniden okumaz.

### Tazelik

- Kapat-aç → yeniden okunur.
- **Yeniden tara** düğmesi (bugünkü "Yeniden say"ın yerini alır): kataloğu atar, baştan okur.
- Uygulamanın kendi işlemleri: Ayıkla'da atılan dosyalar katalogdan çıkarılır (bugün tabloyu yamalayan yer).
  Düzenle, Temizle ya da Geri Al bu kökte dosya taşıdıysa katalog "eskidi" sayılır; zaman çizelgesine dönünce
  yeniden okunur.
- Uygulama dışında (Gezgin'de) yapılan değişiklikler izlenmez; kullanıcı Yeniden tara'ya basar. Galeri açarken
  artık olmayan bir dosyanın küçük resmi gelmezse kutu "önizleme yok" gösterir, çökmez.

### Zaman çizelgesi sayfası

```
┌ Zaman çizelgesi ─ D:\Arşiv ─ [Galeri | Tablo] ─ Foto ▾ ─ ⟳ Yeniden tara ───────────┐
│ Kasım 2026 · 214                                                     [Ayıkla ▶] │ 2026
│ ▢ ▢ ▢ ▢ ▢ ▢ ▢ ▢ ▢ ▢                                                             │  ┃
│ ▢ ▢ ▢ ▢ ▢+RAW ▢ ▢ ▢▶ ▢                                                          │  ┃ ┌──────────┐
│ Ekim 2026 · 1.001                                                    [Ayıkla ▶] │  ●─┤ Ekim 2026│
│ ▢ ▢ ▢ ▢ ▢ ▢ ▢ ▢ ▢ ▢                                                             │  ┃ └──────────┘
│ ...                                                                             │ 2025
└─────────────────────────────────────────────────────────────────────────────────┘
```

- Üstte **Galeri | Tablo** seçimi; varsayılan Galeri, seçim ayarlarda saklanır. Tablo bugünkü haliyle kalır;
  tablodaki bir aya tıklamak bugünkü gibi ay görünümünü açar.
- Tür süzgeci (Foto / Raw / Video / Hepsi) galeride de geçerli; RAW eşi olan JPG'de "+RAW" rozeti (ay görünümündeki
  `MonthItems.Build` kuralı), videolarda ▶ işareti.
- **Sıra:** galeri baştan sona yeniden eskiye (aylar da, ay içindeki dosyalar da), Fotoğraflar uygulamasındaki gibi.
  Ay görünümü bir çalışma ekranı olduğu için kendi sırasını (eskiden yeniye) korur. Klasör adları yönteminde
  tarihi başka aya düşen dosyalar ayın sonunda, kendi tarihlerine göre sıralı durur (ayrı başlık yok).
- **Video küçük resimleri:** bugünkü çözücü (WIC) videoyu açamıyor; ay görünümünde videolar resimsiz, yalnız ▶.
  Galeride videolar için Windows'un kendi küçük resim hizmeti (Gezgin'in kullandığı kabuk küçük resmi) kullanılır;
  alınamazsa bugünkü gibi ▶ yer tutucusu. Aynı yol ay görünümüne de uygulanır.
- Ay başlığı: "Ekim 2026 · 1.001" ve **Ayıkla ▶** (o ayın ay görünümünü açar; dönünce galeri aynı yerde kalır).
- Izgara satırları ay görünümündeki gibi sanal (yalnız görünen satırlar ve küçük resimleri yüklenir); küçük resim
  boyutu ay görünümüyle aynı.
- Galeride çift tık / Enter: dosya Windows'un varsayılan uygulamasında açılır (2. sürümde kendi görüntüleyicimiz).
- Katalog okunurken: bugünkü ilerleme ekranı (sayaç, Durdur); okuma bitince galeri açılır.

### Zaman çubuğu

- Sağ kenarda dikey şerit. Konum, galerideki satır konumuyla orantılı (her ayın payı o ayın satır sayısına göre).
- Yıl etiketleri şeritte; aylar küçük çentik.
- Fareyle tutup sürükleyince galeri anında o konuma gider ve yanında "Ekim 2026" balonu görünür. Şeride
  tıklamak o noktaya atlar. Fare tekerleği normal kaydırma.
- Sürüklerken küçük resim yüklemesi bekletilir, bırakınca görünenler yüklenir (takılmayı önler).
- Klavye: Page Up / Page Down sayfa, Home / End en yeni / en eski.

## Kısıtlar

- .NET 8, WPF, WPF-UI, CommunityToolkit.Mvvm; mevcut desen: ViewModels projesinde VM, App'te görünüm, Core'da
  salt okunur okuma kodu, xUnit testleri sahte servislerle.
- Okuma kodu gerçek dosyalara dokunmaz; testler geçici klasörde çalışır.
- Bellek: katalog 100.000 dosyada 100 MB'ın altında kalmalı (giriş başına ~1 KB üst sınır). Bu kesin sınır değil,
  ölçülüp raporlanır; küçük resimler ayrıca, yalnız görünenler için tutulur.
- Katalog oturum düzeyinde tutulur (zaman çizelgesi sayfası her girişte yeniden kurulduğu için sayfa nesnesinde değil).
- Kullanıcının arşivinde (`D:\1- HERKES\...`) hiçbir şey yazılmaz ya da çalıştırılmaz; deneme, örnek klasör ve
  render düzeneğiyle.

## Yapıcıya bırakılanlar

- Katalog sınıfının adı, iç yapısı (ay başına liste, sıralı dizi vb.).
- Galerinin satır düzleştirme ayrıntıları (ay görünümündeki `MonthHeaderRow` / `MonthTileRow` deseninin
  genişletilmesi önerilir).
- Zaman çubuğunun çizimi (çentik sıklığı, yıl etiketi çakışınca seyreltme), balon tasarımı.
- Klasör adları yönteminde tablo sayımı ile katalog okumasını tek taramada birleştirmek isteğe bağlı.

## Benim kararlarım (kullanıcı kontrol etsin)

- Tablo kaldırılmıyor; Galeri | Tablo seçimi, varsayılan Galeri.
- Yöntem seçimi (klasör adları / çekim tarihi) galeride de geçerli; tablo ve galeri hep aynı sayıyı gösterir.
- Galeride ayıklama yok; ay başlığındaki "Ayıkla ▶" ay görünümüne götürür.
- "Yeniden say" düğmesinin adı "Yeniden tara" olur ve kataloğu tazeler.
- Uygulamanın kendi taşıma işlerinden sonra katalog otomatik tazelenir; dış değişiklikler için elle.
- Galeride sıra baştan sona yeniden eskiye; ay görünümü eskiden yeniye kalır.
- Videolara gerçek küçük resim (Windows kabuk küçük resmi), galeride ve ay görünümünde.
- Katalog okunan bilgiyi eksiksiz tutar; bellek hedefi 100.000 dosyada < 100 MB.
- Video oynatma 2. sürümde; ilk tercih Windows'un kendi oynatıcısı (MediaElement), codec sorunu olursa VLC motoru.
