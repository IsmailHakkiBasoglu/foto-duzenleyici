# Zaman çizelgesi — tasarım

Tarih: 2026-11-17 · Yol haritası: Aşama 3, "Ay ay zaman çizelgesi; ay başına dosya sayısı ve boyut grafiği"

## Amaç

Kullanıcı bir klasöre bakınca **hangi ayda ne kadar fotoğraf olduğunu** bir bakışta görmek istiyor. Bu bir
gezinme ya da düzenleme aracı değil; genel bakış ekranı.

Klasör düzenlenmiş bir arşiv olabilir (Düzenle'nin `Yıl-Ay` klasörleri) ya da hiç düzenlenmemiş bir klasör
olabilir. Kullanıcının sözü: "eğer hiç düzenlenmemiş bir klasöre uygulayacaksa senin sistemin çalışmayabilir"
— sayım yöntemi kullanıcıya bırakılır.

## Hedefler

- Seçilen klasördeki fotoğrafların aylara göre dağılımı tek ekranda: yıl × ay tablosu.
- İki sayım yöntemi, kullanıcı seçer: **Klasör adlarından** (hızlı) ve **Çekim tarihinden** (EXIF).
- Foto / Raw / Video / Hepsi süzgeci; varsayılan **Foto**.
- Ay kutusunda sayı; üstüne gelince sayı, boyut, tür dağılımı; tıklayınca ilgili klasör(ler) Gezgin'de.

## Hedef dışı

- Fotoğrafları uygulama içinde göstermek / küçük resimler.
- Kopya ayıklama ya da hash hesaplama (çekim tarihi yönteminde kopyalar iki kez sayılır, ekranda yazar).
- Sonuçları kaydetmek, dışa aktarmak, önbelleğe almak.
- Ay başına boyut grafiği ayrı bir görünüm olarak (boyut ipucunda ve toplamda gösterilir).

## Olmaması gerekenler (anti-hedefler)

- **Hiçbir dosyaya dokunmak.** Ekran tamamen salt okunurdur; yalnız son seçilen klasör ayarlara yazılır.
- Düzenlenmemiş klasörde sessizce boş tablo göstermek: yöntem neden seçildiyse bir satırla yazılır.
- Uzun EXIF taramasında arayüzü dondurmak: ilerleme ve İptal şart.

## Yaklaşımlar

**Görünüm** — tarayıcıda üç taslak gösterildi, kullanıcı **A**'yı seçti:

| | Görünüm | Artı | Eksi |
|---|---|---|---|
| **A (seçildi)** | Yıl × ay tablosu, kutu koyuluğu = sayı | Tüm arşiv tek ekranda, boş aylar göze batar, sayı okunur | Ay içi eğilim (artış/düşüş) çubuk kadar net değil |
| B | Ay ay çubuk grafik | Eğilim net | Yıllar çoğaldıkça yana kayar; sayı için üstüne gelmek gerekir |
| C | Yıl yıl liste + yatay çubuk | Sayılar yazılı | Uzun liste, genel tablo için kaydırma |

**Sayım yöntemi** — önce yalnız klasör adları önerildi; kullanıcı düzenlenmemiş klasörler için bunun
çalışmayacağını belirtti. Sonuç: iki yöntem, ikisi de kullanıcıya açık.

| Yöntem | Nasıl | Hız | Ne zaman |
|---|---|---|---|
| Klasör adlarından | Seçilen klasörün doğrudan altındaki `yyyy-MM` adlı klasörler; içleri (alt klasörler dahil) sayılır | Saniyeler (yalnız dosya listesi ve boyut) | Düzenle'den geçmiş arşiv |
| Çekim tarihinden | Her dosya `Scanner` ile okunur; ay = `Taken` | Düzenle'nin taramasıyla aynı (binlerce dosyada dakikalar) | Her klasör |

## Ekran

```
Zaman çizelgesi
Klasör: D:\…\100CANON   [Değiştir…]                     [Foto|Raw|Video|Hepsi]
( ) Klasör adlarından (hızlı)   (•) Çekim tarihinden (EXIF, daha yavaş)
Bu klasörde "Yıl-Ay" klasörü bulunamadı, bu yüzden Çekim tarihinden seçildi.
2.373 dosya · 26,7 GB · 2022 Ekim – 2023 Mart · 6 ay dolu, 0 ay boş
        Oca Şub Mar Nis May Haz Tem Ağu Eyl Eki  Kas  Ara
2022    ░░  ░░  ░░  ░░  ░░  ░░  ░░  ░░  ░░  240  980  700   1.920
2023     30  35  40                                         105
Tablo dışında: Tarihsiz 3 dosya. Bu yöntem kopyaları ayıklamaz; aynı fotoğrafın iki kopyası iki kez sayılır.
```

Taslaklar: `.superpowers/brainstorm/…/zaman-cizelgesi-ekran-v3.html` (git'e girmez).

### Açılış

- Ana ekranda dördüncü kart: **Zaman çizelgesi** ("Hangi ayda ne kadar fotoğraf var").
- Açılacak klasör sırası: sağ tıkla açılmış klasör (`HomeViewModel.SelectedFolder`) → son zaman çizelgesi
  klasörü → son Düzenle arşivi (`settings.Recent[0].Archive`) → yoksa boş ekran ve **Klasör seç…**.
- Klasör seçilince sayım hemen başlar. **Değiştir…** klasör seçicisi açar.
- Son seçilen klasör ayarlarda tutulur (`AppSettings` içine yeni alan; eski ayar dosyaları sorunsuz açılır).
  *(Yapıcı kararı)* Seçildiği anda kaydedilir. Sıradaki aday klasör artık yoksa atlanır, bir sonrakine geçilir.

### Yöntem seçimi

- Varsayılan: seçilen klasörün doğrudan altında en az bir `yyyy-MM` adlı klasör varsa **Klasör adlarından**,
  yoksa **Çekim tarihinden**. Seçimin nedeni bir satırla yazılır.
- Kullanıcı yöntemi değiştirince sayım yeniden yapılır. Yöntem ayarlarda tutulmaz (her klasör için yeniden
  belirlenir).

### Sayım kuralları

Ortak:
- Tür `FileClassifier.Classify` ile. Süzgeç Foto / Raw / Video / Hepsi; **Hepsi** = Foto + Raw + Video
  (Pdf, belge ve diğerleri hiç sayılmaz).
- `_Ayiklananlar` (Temizle) hiçbir yöntemde sayılmaz (`ScanOptions.ExcludedFolderNames` varsayılanı).
- Boyut: dosya boyutlarının toplamı; gösterim mevcut boyut biçimlendiricisiyle (`Sizes`).

Klasör adlarından:
- Yalnız seçilen klasörün **doğrudan** altındaki `^\d{4}-(0[1-9]|1[0-2])$` adlı klasörler; içlerindeki
  tüm dosyalar (alt klasörler dahil) o aya sayılır. EXIF okunmaz.
- Aynı düzeydeki `_Tekrarlar`, `_Benzerler`, `_Kusurlu`, `Tarihsiz` tabloya girmez; varsa altta dosya
  sayılarıyla ayrıca yazılır. Diğer klasörler ve kökteki gevşek dosyalar yok sayılır.

Çekim tarihinden:
- Dosyalar `Scanner.EnumerateFiles` ile bulunur, her biri `Scanner.ReadFile` ile okunur (paralel, ilerleme
  bildirimli, iptal edilebilir). Ay = `MediaFile.Taken`. Yalnız Foto, Raw, Video okunur (Pdf okunmaz).
  *(Yapıcı kararı)* `ReadFile` bir dosyada istisna atarsa o dosya "okunamadı" sayılır, sayım sürer;
  `Scanner`'ın kendisi değiştirilmez, sarmalanır.
- `ReadFile` her zaman bir tarih verir (EXIF → dosya adı → dosya zamanı), yani gerçek anlamda "Tarihsiz"
  dosya çıkmaz. *(Yapıcı kararı)* Tarihi EXIF / video metadatası dışından gelenler (`DateSource.FileName`,
  `FileTime`) tabloya sayılır; ipucunda "x tanesinin tarihi dosya adından / dosya zamanından" ve altta toplam
  olarak yazılır. Okunamayan dosyalar ve klasörler (`Scanner.Errors` + istisnalar) altta sayı olarak yazılır.
- *(Yapıcı kararı)* Düzenle'nin özel klasörleri (`_Tekrarlar`, `_Benzerler`, `_Kusurlu`, `_Gunluk`,
  `_Ayiklananlar`) bu yöntemde de taranmaz (`ExcludedFolderNames`'e eklenir), böylece düzenlenmiş bir arşiv
  iki yöntemde aynı sonucu verir. Kök düzeyinde varsa altta dosya sayılarıyla yazılır, klasör adlarından
  yöntemindeki gibi.
- Kopyalar ayıklanmaz; bu yöntemde altta "aynı fotoğrafın kopyaları ayrı sayılır" notu durur.

Klasör adlarından yönteminde *(yapıcı kararı)*: `yyyy-MM` klasörleri dışında Foto/Raw/Video dosyası varsa
(özel klasörler hariç) altta "N dosya Yıl-Ay klasörleri dışında" yazar ve **Çekim tarihinden say** bağlantısı
sunulur. Varsayılan yöntem kuralı değişmez.

Süzgeç (Foto/Raw/Video/Hepsi) yeniden saymaz: tarama bir kez yapılır, süzgeç bellekte uygulanır.

### Tablo

- Satır: ilk dolu yıldan son dolu yıla her yıl. *(Yapıcı kararı)* Art arda iki ya da daha çok tamamen boş
  yıl tek ince satıra toplanır ("2001 – 2015: boş"); tek boş yıl normal satırdır. Böylece saati sıfırlanmış
  bir makineden gelen tek bir 1990 tarihli dosya tabloyu 30 satıra uzatmaz. Boş ay sayısı bu toplanmış
  yılları içermez.
  Sütun: 12 ay. Son sütun yıl toplamı.
- Kutu: süzgeçteki sayı; 0 ise taralı ve boş. Renk koyuluğu sayının en büyük aya oranının kareköküyle
  (az sayılı aylar da görünür kalsın); açık ve koyu temada okunur.
- Özet satırı: toplam dosya, toplam boyut, ilk–son ay, dolu ay ve boş ay sayısı (ilk ile son dolu ay
  arasında). Hepsi süzgece göre.

### Ay kutusu etkileşimi

- Üstüne gelince: "Kasım 2022 — 980 foto · 7,9 GB", tür dağılımı (Foto · Raw · Video), çekim tarihi
  yönteminde dosyaların bulunduğu klasörler (en çok olan ilk üç, sayılarıyla).
- Tıklayınca, klasör adlarından yönteminde: o ayın `yyyy-MM` klasörü Gezgin'de açılır.
- Tıklayınca, çekim tarihi yönteminde: o ayın dosyalarının bulunduğu klasörler listelenir (sayılarıyla),
  her biri Gezgin'de açılabilir. Tek klasör varsa doğrudan açılır. *(Yapıcı kararı)* Klasör = dosyanın
  doğrudan bulunduğu klasör, seçilen köke göre kısa yol olarak gösterilir.
- Boş kutu tıklanmaz.

### Uzun süren sayım

- İlerleme ("1.240 / 2.373 dosya okundu") ve **İptal**. İptal edilince tablo boş kalır, "Sayım durduruldu"
  yazar, **Yeniden say** sunulur.
- Sayım sürerken klasör/yöntem değiştirilirse önceki sayım iptal edilir.
- Sayım hiçbir dosyayı değiştirmediği için gezinmeyi kilitlemez ve kapatırken sormaz: kullanıcı sayfadan
  çıkarsa ya da pencereyi kapatırsa sayım sessizce iptal edilir (gerekirse `ILongRunningPage` ile
  `IsLocked = false`, `MustConfirmClose = false`, `RequestStop` = iptal).

## Kısıtlar

- .NET 8, WPF + WPF-UI, CommunityToolkit.Mvvm; ekran mantığı `FotoDuzenleyici.ViewModels`'te (WPF'siz,
  testli), sayım `FotoDuzenleyici.Core`'da. Mevcut desenler: `HomeViewModel` olayları → `ShellViewModel`
  sayfa geçişi.
- Testler xUnit, TDD; geçici klasörde örnek dosya ağaçlarıyla.
- İnternet yok, yeni bağımlılık yok.
- Arayüz metinleri Türkçe.

## Yapıcıya kalanlar

- Sınıf, dosya ve olay adları; ay sonucunun veri yapısı.
- Klasör adlarından yönteminde dosya listesini paralel okuyup okumamak.
- Isı rengi tonları (mevcut tema renklerinden), kutu boyutları, ipucu düzeni.
- Klasör listesi açılır pencere mi, kutunun altında panel mi.
- Klasik görünüme bir şey eklenmez.
