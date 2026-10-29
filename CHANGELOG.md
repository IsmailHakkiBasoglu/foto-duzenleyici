# Değişiklik Günlüğü

Bu projedeki önemli değişiklikler bu dosyada tutulur.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/), sürüm numaraları
[Anlamsal Sürümleme](https://semver.org/lang/tr/) kurallarına uyar.

## [Yayınlanmamış]

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

[Yayınlanmamış]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.3...HEAD
[0.1.3]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.2...v0.1.3
[0.1.2]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/releases/tag/v0.1.0
