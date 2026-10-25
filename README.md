# Foto Düzenleyici

Dağınık klasörlerdeki fotoğraf ve videoları **çekim tarihine** (EXIF / video metadata) göre
`Yıl-Ay` klasörlerine toplayan, aynı içerikli kopyaları bulan Windows masaüstü uygulaması.

```
2026-01\
  2026-01-Foto\   (isteğe bağlı: 2026-01-Foto\2026-01-Fuji, 2026-01-Canon …)
  2026-01-Raw\
  2026-01-Video\
  2026-01-Pdf\
_Tekrarlar\       (birebir aynı içerikli kopyalar)
```

## Kullanım (kurulum yok)

1. [Releases](../../releases) sayfasından `FotoDuzenleyici-win-x64.zip` dosyasını indir.
2. Zip'i bir klasöre çıkar.
3. `FotoDuzenleyici.exe` dosyasına çift tıkla. .NET veya başka bir şey kurmana gerek yok.

> Windows "bilinmeyen yayıncı" uyarısı verirse: *Ek bilgi → Yine de çalıştır*.
> (Uygulama henüz dijital olarak imzalı değil.)

## Güvenlik

- Dosyaların içeriği **hiç değiştirilmez**: EXIF/GPS/video metadatası olduğu gibi kalır.
- Her dosya önce kopyalanır, SHA-256 ile doğrulanır, oluşturma/değiştirme tarihleri korunur; ancak sonra eskisi
  **Geri Dönüşüm Kutusu'na** alınır (kalıcı silme yok).
- Hedefte var olan hiçbir dosyanın üzerine yazılmaz. İsim çakışmasında tarih eklenir.
- Her işlem günlüğe yazılır; **Geri Al** ile eski yerine dönülür.
- Uygulamadan önce mutlaka **Önizleme** gösterilir.

## Kaynaktan derleme

.NET 8 SDK gerekir.

```powershell
dotnet test
./publish.ps1        # dist\FotoDuzenleyici-win-x64.zip üretir
```

## Proje yapısı

- `FotoDuzenleyici.Core` – tarama, tarih okuma, yinelenen bulma, planlama, güvenli uygulama
- `FotoDuzenleyici.App` – WPF arayüzü
- `FotoDuzenleyici.Cli` – salt okunur rapor aracı
- `FotoDuzenleyici.Tests` – birim testleri

Metadata okuma: [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) (Apache-2.0).
