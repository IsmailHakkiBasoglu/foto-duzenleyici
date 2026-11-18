# Ay görünümü ve hızlı ayıklama — tasarım

Tarih: 2026-11-18 · Durum: onaylandı (2026-11-18)

## Niyet

Kullanıcı Zaman çizelgesinde dolu bir aya (örn. "Ağustos 2026 · 140 foto") tıklayınca o ayın içine
girmek istiyor. Kendi sözleriyle: **"o ay ne çektiğimi hızlıca görüp hatırlamak istiyorum. belki hızlıca
silme yaparım ya da çöp kutusuna atılacaklar listesi için işaretleme yaparım. mobilde fotoğraf kaydırarak
telefonda yer açan uygulamalar var ya onun gibi düşün."**

Daha büyük hedef: uygulamayı Windows Fotoğraflar uygulaması ve Gezgin yerine kullanmak. Bu tasarım o hedefin
ilk parçası. Hedefin tamamı ayrı bir yol haritası maddesidir ("Fotoğraflar uygulaması yerine").

## Hedefler

- Aya tıklayınca, uygulamadan çıkmadan, o ayın fotoğrafları küçük resimlerle görünür.
- Görünümde gezinmek hızlı ve akıcı olur; 1.000 dosyalı bir ayda da öyle kalır.
- Kötü kareler hızlıca "atılsın" diye işaretlenir. İşaretlenenler gözden geçirilir ve tek onayla Geri Dönüşüm
  Kutusu'na gönderilir.
- İşlem bitince, Zaman çizelgesine dönüldüğünde o ayın sayısı güncel olur.

## Hedef dışı (bu iş değil)

- Video oynatma, yakınlaştırma, döndürme, düzenleme, yeniden adlandırma, taşıma.
- Birden çok ayı aynı anda ayıklamak.
- Ayıklama kararlarını kalıcı saklamak. Liste yalnız o ay açıkken yaşar.
- "Fotoğraflar uygulaması yerine" hedefinin geri kalanı.

## Anti-hedefler (çalışsa bile başarısızlık sayılır)

- Kullanıcının onayı olmadan herhangi bir dosyanın gitmesi.
- Bir dosyanın sessizce, kalıcı olarak silinmesi. Bu, Geri Dönüşüm Kutusu olmayan sürücüleri de kapsar.
- Hedefte var olan bir dosyanın üzerine yazılması.
- Ay görünümünün açılırken ya da kaydırılırken takılması.
- Atılan dosyanın RAW eşinin sürpriz biçimde gitmesi. Kullanıcı bunu gözden geçirme ekranında önceden
  görmeli.

## Kullanıcının verdiği kararlar

1. **Kapsam ve sıra:** Bu iş ay görünümü ile hızlı ayıklamadır. "Fotoğraflar uygulaması yerine" hedefi yol
   haritasına ayrı bir başlık olarak eklenir.
2. **Atılanlar Geri Dönüşüm Kutusu'na gider.** Geri getirmek Windows'un "Geri yükle" işleviyle yapılır.
   Geri Dönüşüm Kutusu olmayan sürücüde dosyalar `_Ayiklananlar` klasörüne taşınır ve kullanıcıya bu
   açıkça yazılır.
3. **Birleşik görünüm:** Üst çubukta iki konumlu bir düğme bulunur: **▦ Izgara / 📅 Takvim**.
   - **Izgara:** günlere bölünmüş küçük resim ızgarası. Her günün bir başlığı vardır, örn.
     "15 Ağustos Cumartesi · 38".
   - **Takvim:** ay haftalara bölünür ve her gün bir kutu olur. Kutuda o günün birkaç küçük resmi ve
     "+N" gösterilir. Bir güne tıklamak Izgara'ya geçirir ve o günün başına kaydırır.
   - Son seçilen görünüm hatırlanır. İlk kullanımda Izgara açılır *(Yapıcı kararı, kullanıcı gördü)*.
4. **Ayıkla (tek tek büyük görünüm), tuş düzeni B:**

   | Hareket | Ne yapar |
   |---|---|
   | ← / → | Önceki / sonraki fotoğraf; karar vermez |
   | Delete | "Atılsın" işaretini koyar ya da kaldırır, sonrakine geçer |
   | Fotoğrafı sola sürükle | Atılsın, sonrakine geç |
   | Fotoğrafı sağa sürükle | Kalsın (işaret varsa kalkar), sonrakine geç |
   | Esc | Ay görünümüne dön |

   Üstte tarih, saat, dosya adı, boyut ve "58 / 140" sırası görünür. Altta kararları ✕ işaretiyle gösteren
   küçük resim şeridi bulunur.
5. **RAW+JPG eşleri:** Ay görünümünde eşin yalnız JPG'si gösterilir. JPG "atılsın" işaretlenirse RAW eşi
   de onunla gider. Gözden geçirme ekranında o resmin üstünde "+RAW" etiketi bulunur ve toplam boyuta RAW da
   dahildir.
   - **İstisna (kullanıcı isteği):** Bazen kullanıcı kötü JPG'yi atıp RAW'ı saklamak isteyebilir. RAW eşi
     olan bir fotoğraf Ayıkla'da açıkken bir **"RAW'ı tut"** düğmesi görünür (anlamı: "bu fotoğrafın
     RAW'ını dahil etme"). Düğmeye basılırsa o fotoğraf atıldığında RAW yerinde kalır.
   - Seçim fotoğraf başınadır ve yalnız o ay açıkken saklanır, tıpkı işaretler gibi.
   - Gözden geçir ekranında bu fotoğrafın etiketi "+RAW" yerine "RAW kalır" olur ve toplam boyuta RAW
     katılmaz.
   - Gözden geçir'de etikete tıklamak da seçimi değiştirir *(Yapıcı kararı)*.
6. **Yaklaşım 1:** Aya girerken o ayın dosyaları yeniden okunur. Ayrıntısı aşağıdaki Yaklaşımlar
   bölümünde.

## Yaklaşımlar

| | Yaklaşım | Artı | Eksi |
|---|---|---|---|
| **1 (seçildi)** | Aya girerken o ayın klasörleri okunur: gün, saat, yön, RAW+JPG eşleri | Liste her zaman güncel olur; Gezgin'de yapılan değişiklikleri görür. Sayım değişmez. | Aya girişte kısa bir bekleme olur (140 dosyada yaklaşık 1 sn). |
| 2 | Zaman çizelgesi sayarken her dosyanın yolu ve günü bellekte tutulur | Aya giriş anında olur. | "Klasör adlarından" yöntemi EXIF okumadığı için gün bilgisi zaten yoktur. Liste sayım anında donar. |
| 3 | Ayıkla görünümü ayrı, tam ekran bir pencere olur | İkinci ekrana taşınabilir. | Bu iş için fazla büyük. "Fotoğraflar yerine" aşamasında yeniden düşünülecek. |

## Ekranlar ve akış

```
Zaman çizelgesi ──(dolu aya tıkla)──► Ay görünümü (Izgara | Takvim)
                                        │  çift tık / "Ayıkla ▶"
                                        ▼
                                      Ayıkla (büyük, tek tek) ──Esc──► Ay görünümü
                                        │
            alt şerit "Gözden geçir" ──►  Gözden geçir ──"N dosyayı Geri Dönüşüm Kutusu'na at"──► Ay görünümü (atılanlar yok)
Ay görünümü ──"← Zaman çizelgesi"──► Zaman çizelgesi (tablo aynı yerde, sayılar güncel)
```

Ekranların hepsi aynı pencerede, bugünkü sayfalar gibi birbirinin yerine geçer.

### Ay görünümü

- **Üst çubuk:** "← Zaman çizelgesi", ay adı, dosya sayısı ve toplam boyut, ▦/📅 düğmesi, "Ayıkla ▶" ve
  bir ⋯ menüsü. Menüde "Gezgin'de aç" bulunur. Ayın dosyaları birden çok klasördeyse bu seçenek bir alt
  menüde o klasörleri listeler.
- **Alt şerit:** yalnız işaretli dosya varken görünür. İçinde "🗑 Atılacaklar: N · boyut",
  "Gözden geçir" ve "Geri Dönüşüm Kutusu'na at" bulunur. "Geri Dönüşüm Kutusu'na at" düğmesi önce Gözden
  geçir ekranını açar; dosyalar doğrudan gitmez.
- **Işaretleme:** Bir küçük resim seçiliyken Delete tuşu ya da sağ tık menüsündeki "Atılsın" kullanılır.
  İşaretli resim soluk görünür ve köşesinde kırmızı ✕ bulunur. Aynı işlem işareti geri kaldırır.
- **Yükleme:** Okuma sürerken bir ilerleme göstergesi görünür. Okuma bir Durdur düğmesiyle ya da geri
  dönülerek iptal edilebilir.
- **Süzgeç:** Hangi türlerin gösterileceğini Zaman çizelgesinin süzgeci belirler (Foto / Raw / Video / Hepsi).
  - JPG'si görünen bir RAW ayrıca gösterilmez.
  - "Raw" süzgecinde yalnız RAW dosyalar görünür. Bunlar işaretlenirse JPG eşi gitmez, çünkü 5. karar
    yalnız JPG'den RAW'a doğru işler *(Yapıcı kararı)*.

### Gözden geçir

- İşaretli dosyalar küçük resim olarak listelenir. Bir resme tıklamak onu listeden çıkarır.
- Toplam dosya sayısı ve boyut, RAW eşleri dahil gösterilir.
- Açıklama metni: "Dosyalar Windows'un Geri Dönüşüm Kutusu'na gider; oradan 'Geri yükle' ile
  dönebilirsin. Disk yeri, Geri Dönüşüm Kutusu'nu boşaltınca açılır."
- Sürücüde Geri Dönüşüm Kutusu yoksa sarı bir uyarı çıkar ve düğmenin metni de değişir:
  "N dosyayı _Ayiklananlar'a taşı".
- "Vazgeç" düğmesi listeye dokunmadan geri döner.

## Kurallar

- **Okunacak dosyalar:**
  - *Klasör adlarından* yönteminde, hücrenin klasörlerinin içindekilerin hepsi okunur.
  - *Çekim tarihinden* yönteminde hücrenin klasörleri okunur, ama yalnız çekim tarihi o yıl-aya düşen
    dosyalar alınır.
  - Tarih okuma kuralları uygulamanın her yerinde kullanılanlarla aynıdır: EXIF, yoksa dosya adı, yoksa
    dosya zamanı.
  - Zaman çizelgesinin dışarıda bıraktığı özel klasörler burada da atlanır.
- **Sıralama ve gruplama:** Dosyalar çekim zamanına göre sıralanır, güne göre gruplanır.
- **Klasör adı ile tarih uyuşmazlığı:** *Klasör adlarından* yönteminde bir dosyanın çekim tarihi başka bir
  aya düşebilir (örn. elle konmuş bir dosya). Böyle bir dosya yine gösterilir, gerçek gününün altında.
  - O gün o ayda değilse ayın sonunda "Başka aydan" başlıklı bir grupta toplanır *(Yapıcı kararı)*.
- **Atma işlemi:**
  - Her dosya için önce Geri Dönüşüm Kutusu'nun bulunduğu doğrulanır, sonra dosya gönderilir.
  - Geri Dönüşüm Kutusu olmayan sürücülerde dosya `_Ayiklananlar` klasörüne taşınır. Kapsanan sürücüler:
    çıkarılabilir sürücüler (USB bellek, SD kart) ve ağ sürücüleri.
    - Taşıma Temizle'nin taşıma koduyla aynı güvenceleri taşır: üzerine yazmaz ve günlük tutar.
    - Böylece uygulamanın Geri Al'ı ile geri alınabilir.
  - Kalıcı silme hiçbir koşulda yoktur.
- **Geri Dönüşüm Kutusu var ama dosyayı almıyor** *(Yapıcı kararı)*:
  - Kapsanan durumlar: kutu kapatılmış ya da 0 MB'a ayarlanmış, ya da dosya kutuda kalan yerden büyük
    (çoğu zaman büyük bir video).
  - Bugünkü `Executor.RecycleBin` çağrısı Windows'un "kalıcı silinsin mi?" sorusunu bastırıyor. Bu yüzden
    bu yol kullanılmaz.
  - Gönderimden önce her dosya için kutunun dosyayı alacağı doğrulanır. Alamayacaksa dosya, kutusu olmayan
    sürücüdeki gibi `_Ayiklananlar`'a taşınır.
  - Gözden geçir ekranı bunu önceden hesaplar. Karışık bir toplu işte düğme şöyle yazar: "4 dosyayı Geri
    Dönüşüm Kutusu'na at, 2 dosyayı _Ayiklananlar'a taşı". Taşınacak dosyaların üstünde ayrıca bir etiket
    bulunur.
  - Gönderim sırasında yine de beklenmedik bir kalıcı silme sinyali gelirse o dosya atlanır ve "atılamadı"
    sayılır.
- **`_Ayiklananlar`'ın yeri** *(Yapıcı kararı)*:
  - Temizle'deki gibi Zaman çizelgesinin kökünün altında, `_Ayiklananlar\Ayikla\<köke göre göreli yol>`.
  - Dosya kökle aynı birimde değilse (örn. bağlanmış bir sürücü), `_Ayiklananlar` o dosyanın birimindeki en
    üst kök altı klasöre konur.
  - Taşınan dosyalar Zaman çizelgesi alt satırındaki `_Ayiklananlar: N dosya` sayısına eklenir.
- **Günlük** *(Yapıcı kararı)*:
  - Yalnız Geri Dönüşüm Kutusu'na gönderilen bir işlem için `_Ayiklananlar` klasörü oluşturulmaz. Günlük
    uygulamanın kendi veri klasörüne, yalnız kayıt amacıyla yazılır. Bu işlemi yarıda kalınan yerden
    sürdürme ve uygulamanın Geri Al'ı yoktur; geri getirme Windows'un "Geri yükle"siyle yapılır.
  - Taşıma içeren işlemlerde günlük, Temizle'deki gibi `_Ayiklananlar\_Gunluk` içine yazılır. Böylece Geri
    Al ve yarım işlem denetimi bugünkü gibi çalışır.
  - Geri Al, Geri Dönüşüm Kutusu'na gidenleri atlar, çünkü onların hedefi yoktur.
- **Hatalar:** Bir dosya kilitliyse ya da yerinde yoksa diğerleri atılmaya devam eder. Sonunda
  "N dosya atıldı, M dosya atılamadı" denir ve atılamayanlar işaretli kalır.
- **Kayıt:** İşlemin bir günlük kaydı, mevcut günlük biçimiyle yazılır. Böylece neyin ne zaman atıldığı
  bilinir.
- **İşaretliyken çıkış:** İşaretli dosya varken ay görünümünden çıkmak ya da uygulamayı kapatmak şunu
  sorar: "5 dosya atılmak üzere işaretli. Bırakıp çıkılsın mı?" İşaretler kaydedilmez.
- **Sayıların güncellenmesi:** Zaman çizelgesine dönüldüğünde atılan dosyalar o ayın sayısından ve
  boyutundan düşülür, tablo ile özet yeniden kurulur. Tam yeniden sayım yapılmaz.

## Kısıtlar

- Teknoloji bugünküyle aynı kalır: .NET 8, WPF, WPF-UI, CommunityToolkit.Mvvm. Ekran mantığı testli
  `FotoDuzenleyici.ViewModels` projesinde durur.
- Küçük resimler mevcut `ThumbnailLoader` ile yüklenir; bunun için loader genelleştirilir.
  - Aynı anda en fazla 4 dosya çözülür, bellekte en çok 600 sonuç tutulur.
  - Görünmeyen öğenin isteği iptal edilir.
  - Izgara sanal kaydırma kullanır: yalnız görünen satırlar oluşturulur.
- Ayıkla'daki büyük resim ekran boyutunda çözülür. Bir sonraki resim önceden yüklenir.
- Uygulama internete bağlanmaz. Yeni bir paket eklenmez.

## Yapıcıya bırakılanlar

- Videolar:
  - Küçük resim çıkarılabiliyorsa gösterilir, yoksa bir video simgesi görünür.
  - Ayıkla'da videoyu oynatmak yerine "Varsayılan uygulamada aç" düğmesi bulunur.
- Tarihi dosya adından ya da dosya zamanından tahmin edilen fotoğrafların üstünde küçük bir işaret olup
  olmayacağı.
- Takvim kutusunda kaç küçük resim gösterileceği.
- Yükleme sırasındaki ilerleme göstergesinin biçimi.
- Sürükleme eşiği ve animasyonu.
