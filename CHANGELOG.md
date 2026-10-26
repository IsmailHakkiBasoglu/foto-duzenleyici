# Değişiklik Günlüğü

Bu projedeki önemli değişiklikler bu dosyada tutulur.
Biçim [Keep a Changelog](https://keepachangelog.com/tr-TR/1.1.0/), sürüm numaraları
[Anlamsal Sürümleme](https://semver.org/lang/tr/) kurallarına uyar.

## [Yayınlanmamış]

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

[Yayınlanmamış]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.2...HEAD
[0.1.2]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.1...v0.1.2
[0.1.1]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/IsmailHakkiBasoglu/foto-duzenleyici/releases/tag/v0.1.0
