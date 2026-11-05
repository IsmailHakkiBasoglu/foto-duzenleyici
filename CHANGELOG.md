# Değişiklik Günlüğü

Bu projedeki önemli değişiklikler bu dosyada tutulur.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/), sürüm numaraları
[Anlamsal Sürümleme](https://semver.org/lang/tr/) kurallarına uyar.

## [Yayınlanmamış]

### Eklendi
- **RAW + JPG eşleri birlikte:** aynı klasörde aynı adı taşıyan RAW ve JPG (ya da HEIC) aynı kare
  sayılıyor. Düzenli arşivde `2026-01-Raw` klasörü kardeş `2026-01-Foto` klasörüyle eşleniyor; tekrar
  çalıştırmada da eşler tanınıyor.
- Benzerler'de ayıklanan karenin RAW eşi varsa Plan'a basınca soruluyor: RAW'lar da `_Benzerler`'e
  gitsin mi (Evet), normal klasörüne mi (Hayır), yoksa plan hiç oluşturulmasın mı (İptal).
- Benzerler şeridinde RAW eşi olan karelerin köşesinde **RAW** rozeti.
- Özette ve komut satırında "RAW+JPG eşi" sayısı; komut satırı adı eşinden ayrılan eş varsa sayıyor.
- **Bulanık ve karanlık kareler:** tarama her fotoğrafın netliğini (en net bölgelerine bakarak; arkası flu
  portre bulanık sayılmaz) ve karanlığını yerelde ölçüyor. Yeni seçenek "Bulanık / karanlık kareleri
  işaretle" (varsayılan açık). Ölçümler `%LOCALAPPDATA%\FotoDuzenleyici\kalite-onbellek.json`
  dosyasında saklanıyor; dosya taşınıp yeniden adlandırılsa da bulunuyor. Gerçek arşivde ilk ölçüm
  ~2 dakika, sonrakiler anında.
- Seri çekim gruplarında kareler netliğe göre kıyaslanıyor: en net kare **◎**, en netin yarısının
  altında kalan kareler **BULANIK** rozeti alıyor; kareler birbirine çok yakınsa işaret konmuyor.
  Karşılaştırma penceresinde her karenin başlığında netlik yüzdesi ("3 · Netlik %42").
- **Bulanıkları ayıkla** düğmesi: seri çekimlerde yalnız BULANIK rozetli kareleri ayıklar; en net
  kareye, incelenmiş gruplara ve en net karesini elle ayıkladığın gruplara dokunmaz. "Önerileri uygula"
  seri çekimlere eskisi gibi dokunmuyor.
- **Kusurlu** sekmesi: Benzerler dışında kalan, tek başına bulanık ya da çok karanlık kareler
  ("Bulanıklar" / "Karanlıklar" süzgeci). Varsayılan olarak hepsi kalır; ayıklananlar Uygula'da
  `_Kusurlu\yyyy-MM` klasörüne gider (silinmez, Geri Al çalışır) ve sonraki taramalarda yerinde kalır.
  Çift tıklama kareyi tam ekran açar (yakınlaştırma, ← → sonraki). Eşikler gerçek arşivde
  örneklere bakılarak seçildi: netlik 25'in altı, parlaklık 30'un altında ve piksellerin %60'tan
  fazlası karanlık.
- Plan'daki RAW sorusu artık Benzerler'de ve Kusurlu'da ayıklanan kareleri birlikte soruyor; RAW,
  JPG'sinin gittiği klasöre gidiyor.
- Komut satırına `--quality`: ölçüm, en düşük netlikli kareler, bulanık / karanlık sayıları;
  `--csv` ile `…-kalite.csv` (tüm ölçümler) ve `…-kusurlu.csv`.

### Düzeltildi
- Eşin bir dosyası isim çakışması yüzünden tarih ya da `(2)` alınca diğeri eski adında kalıyordu;
  artık ikisi aynı adı alıyor ("RAW ayrı klasör" açıkken de). Saniye farkıyla ay sınırına düşen
  eşler de ayrı aylara bölünmüyor.

### Değişti
- Birebir kopyalar eşten bağımsız: `_Tekrarlar`'a giden bir JPG'nin RAW'ı kendi yerine gidiyor
  (RAW'ın kendisi kopya değilse).

## [0.1.5] - 2026-11-04

### Eklendi
- Benzerler sekmesi artık **küçük resimli şerit**: her grup bir sıra kart. Karta tıklamak
  Kalsın ↔ Ayıkla arasında geçiş yapar; kalan kare yeşil çerçeveli, ayıklanan soluk ve ✗ işaretli.
  Grubun son karesi ayıklanamaz.
- **Karşılaştırma penceresi:** bir karta çift tıklayınca (ya da grup seçiliyken Enter) grup
  tam ekran açılır, kareler yan yana büyük gösterilir. Tuşlar: 1–9 ya da tıklama Kalsın ↔ Ayıkla,
  Boşluk yalnız bu kalsın, Enter grubu incelendi yapıp sonrakine geçer, ← → gruplar arasında,
  Esc kapatır. Fare tekerleği ya da Z ile bütün kareler aynı noktaya birlikte yakınlaşır
  (netlik kıyası), 0 sıfırlar; yakınlaşınca tam çözünürlük yüklenir, sürükleyerek birlikte
  kaydırılır.
- Kararlar (kalsın / ayıkla, incelendi) uygulama kapanınca da hatırlanıyor:
  `%LOCALAPPDATA%\FotoDuzenleyici\benzer-kararlar.json`. Dosya kimliği yoldan bağımsız; dosya
  taşınıp yeniden adlandırılsa da karar bulunur. Gruba yeni kare eklenirse grup yeniden
  "incelenmedi" görünür, eski işaretler korunur.
- "Yalnız incelenmemişler" süzgeci ve "İncelenen: n / toplam" sayacı.

### Değişti
- Benzerler sekmesindeki satır listesi ve sağdaki tek önizleme paneli kaldırıldı; yerini şerit
  ve karşılaştırma penceresi aldı.

## [0.1.4] - 2026-11-04

### Eklendi
- "Muhtemelen aynı kare" sekmesi: adı ya da baytları farklı ama kamerası, modeli ve çekim anı
  aynı olan fotoğraflar (yeniden kaydedilmiş, dışa aktarılmış, başka yerden tekrar gelmiş)
  gruplanıyor. Saniye kesri ve gövde seri numarası farklıysa (seri çekim, iki gövde) gruplanmıyor;
  RAW + JPG eşleri ve videolar hiç gruplanmıyor.
- Gruplar otomatik taşınmıyor: her dosyanın "Kalsın" işareti var, işareti kaldırılan dosya
  Uygula'da `_Benzerler` klasörüne gidiyor (silinmiyor, Geri Al çalışıyor). ★ ile önerilen
  dosya en yüksek çözünürlüklü, eşitlikte orijinal (ilk yazılan) olanı; "Önerileri uygula"
  düğmesi tek tıkla yalnızca önerileri bırakıyor.
- Tarama artık fotoğrafların piksel boyutunu, saniye kesrini ve gövde seri numarasını da okuyor.
- Komut satırı aracı bu grupları raporluyor.
- **Benzer fotoğraflar:** görüntünün kendisine bakılarak (algısal iz) iki yeni grup türü:
  - *Küçültülmüş kopya* — WhatsApp, mesaj ya da yedekten gelen, EXIF'i silinmiş, küçültülmüş
    kopyalar. Önce 64 bitlik iz, sonra 16×16 küçük resim karşılaştırılır; alakasız ama sade
    görüntüler (gradyan, gökyüzü) kopya sanılmıyor.
  - *Seri çekim* — aynı kameranın 10 saniye içinde çektiği çok benzer kareler. Öneri yok;
    hangisinin kalacağını önizlemeye bakıp sen seçiyorsun, "Önerileri uygula" bunlara dokunmuyor.
- Sekmenin adı artık "Benzerler"; yeni Tür sütunu ve sağda seçili fotoğrafın önizlemesi
  (ok tuşlarıyla gezilir). EXIF'teki yön bilgisi uygulanıyor.
- Benzerlik izleri `%LOCALAPPDATA%\FotoDuzenleyici\benzerlik-onbellek.json` dosyasında
  saklanıyor (fotoğrafın kendisi değil); sonraki taramalar çok daha hızlı. Dosyalar taşınıp
  yeniden adlandırılsa da önbellek geçerli kalıyor.
- Tarama için **Durdur** düğmesi. Benzerlik adımında durdurulursa hesaplanan izler saklanıyor.
- "Benzer fotoğrafları da ara" seçeneği (varsayılan açık); komut satırında `--similar`.
- Tek renk ya da ayrıntısız görüntülerin (siyah kare, yumuşak gradyanlı gökyüzü) izi
  çıkarılmıyor; özette sayılıyor.
- Seri çekim kareleri hiçbir durumda ★ almıyor: aynı saniyede çekilmiş kareler (saniye kesri
  farklı) de, bir karenin kopyası üzerinden gruba bağlanan kareler de.

- Komut satırında `--similar` ile `--csv` birlikte verilince tüm benzer grupları ayrı bir
  `…-benzerler.csv` dosyasına yazılıyor (tür, grup, öneri, çözünürlük, çekim anı).

### Düzeltildi
- Saniye kesri yazmayan kameralarda (Fuji X-H2) aynı saniyede çekilmiş ardışık kareler
  (`DSCF8746` / `DSCF8747`) "aynı kare" ya da "küçültülmüş kopya" sanılıp ★ alıyordu;
  "Önerileri uygula" ayrı bir fotoğrafı `_Benzerler`'e taşıyacaktı. Kamera dosya numarası
  (aynı ön ek, farklı numara) artık ayrı kare sayılıyor; bu kareler ★'sız seri çekim olarak
  gösteriliyor. Gerçek arşivde 10 grup etkileniyordu.

### Değişti
- Core, komut satırı ve test projeleri `net8.0-windows` oldu (görüntü çözme için Windows'un
  kendi bileşeni WIC kullanılıyor; yeni paket yok).

## [0.1.3] - 2026-10-30

### Düzeltildi
- Video tarihleri artık yerel saate çevriliyor. Kameralar (Canon ve Fuji'de doğrulandı)
  video tarihini UTC yazıyor; bu yüzden akşam 21:00'den sonra çekilen videolar bir önceki güne,
  ay sonundaysa önceki aya düşüyordu.
- iPhone gibi cihazların saat dilimiyle birlikte yazdığı video tarihi varsa o kullanılıyor.
- Tarihi sonradan düzelen bir dosyanın adındaki eski tarih öneki yenisiyle değiştiriliyor;
  önceden `2023-03-20 2023-03-19 MVI_3028.MP4` gibi çift tarihli ad oluşuyordu.
- Planlayıcıdaki bozuk Türkçe karakterler düzeltildi; arayüzdeki "İsim çakışması: tarih
  eklendi" notu artık düzgün görünüyor.
- Uygula yarıda kesilince (elektrik, çökme) kalan izler artık sorun çıkarmıyor: hedefte kalan
  yarım `.kopya_tmp` dosyası temizleniyor, hedefe yerleşmiş ama günlüğe yazılamamış dosya
  yeniden kopyalanmadan tamamlanıyor ve Geri Al'a dahil oluyor.
- Günlüğün son satırı yarım kalmışsa Geri Al artık günlüğü okuyabiliyor.

### Eklendi
- Kesintiye dayanıklılık: Uygula başlarken planın tamamı günlüğe yazılıyor. İşlem yarıda
  kalırsa hedef klasör seçildiğinde veya Plan'a basıldığında "Yarım kalan işlem var, devam
  edilsin mi?" diye soruluyor; yeniden tarama ve hash olmadan kalan dosyalarla sürüyor.
  "Hayır" denirse kalan dosyalara dokunulmuyor ve bir daha sorulmuyor.
- Komut satırı aracına `--resume <günlük>` seçeneği; `--apply` yarım kalan işlem varsa uyarıyor.
- Uçtan uca entegrasyon testi: EXIF'li fotoğraflar, UTC tarihli video, birebir kopya ve aynı
  adlı farklı dosyalarla Tara → Plan → Uygula (kaynaklar silinerek) → tekrar çalıştır → Geri Al.
  Örnek dosyalar test sırasında üretiliyor; Geri Dönüşüm Kutusu yerine geçici klasör kullanılıyor.
- GitHub Actions: her gönderimde derleme ve test; `v*` etiketi gönderilince zip derlenip
  sürüm notuyla birlikte GitHub sürümüne ekleniyor.

## [0.1.2] - 2026-10-27

### Düzeltildi
- Düzenlenmiş bir klasör tekrar çalıştırıldığında artık hiçbir şey değişmiyor. Klasör yolu
  `C:/...` biçiminde verildiğinde uygulama kendi dosyalarını tanımıyor ve hepsine "(2)"
  eklemeye kalkıyordu.
- "Kaynak adını ekle" açıkken her çalıştırmada ad uzuyordu (`... Canon Canon`). Önceki turda
  eklenmiş kaynak adı artık tanınıyor.
- Birebir kopyalarda düzenli klasördeki dosya her zaman kalıyor; dışarıdan gelen eşi
  `_Tekrarlar`'a gidiyor. Önceden seçim dosya oluşturma tarihine göre yapılıyordu.

### Eklendi
- Düzenli klasöre elle eklenen yeni dosyalar, tekrar çalıştırıldığında diğerleriyle aynı
  düzende adlandırılıyor.
- Komut satırı aracına `--apply`, `--keep-source` ve `--undo <günlük>` seçenekleri.
- Tekrar çalıştırma, elle eklenen dosya ve kopya önceliği için testler.

## [0.1.1] - 2026-10-25

### Eklendi
- Uygulama simgesi (exe, pencere ve görev çubuğu).

## [0.1.0] - 2026-10-25

### Eklendi
- İlk sürüm: EXIF / video metadatasına göre tarama, `Yıl-Ay` klasörlerine planlama,
  SHA-256 ile birebir kopya tespiti, kopyala → doğrula → Geri Dönüşüm Kutusu ile güvenli
  taşıma, işlem günlüğü ve Geri Al, WPF arayüzü.

[Yayınlanmamış]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.5...HEAD
[0.1.5]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.4...v0.1.5
[0.1.4]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.3...v0.1.4
[0.1.3]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/releases/tag/v0.1.0
