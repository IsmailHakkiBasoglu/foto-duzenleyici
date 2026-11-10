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

## Kurulum

[Releases](../../releases) sayfasından iki biçimden birini indir. .NET veya başka bir şey kurmana gerek yok.

- **`FotoDuzenleyici-Setup-x.y.z.exe`** (önerilen): çift tıkla. Başta *yalnız benim için* (yönetici izni
  istemez) ya da *tüm kullanıcılar için* seçilir. İsteğe bağlı: masaüstü kısayolu, klasör ve sürücü
  sağ tık menüsüne **Foto Düzenleyici ile aç** (Windows 11'de *Daha fazla seçenek göster* altında; açılan
  klasör ana ekranda seçili gelir, Düzenle'de kaynak, Temizle'de temizlenecek klasör olur), komut satırı
  aracını (`fotoduzenle`) PATH'e ekleme. Yeni sürüm için yeni Setup.exe'yi eskisinin üzerine kur; ayarlar kalır.
  Kaldırırken ayarların da silinip silinmeyeceği sorulur (varsayılan: kalır). Fotoğraflarına ve arşivdeki
  günlüklere kurulum ya da kaldırma hiçbir durumda dokunmaz.
- **`FotoDuzenleyici-win-x64.zip`** (taşınabilir): bir klasöre çıkar, `FotoDuzenleyici.exe`'ye çift tıkla.

Uygulama güncelleme denetimi de dahil hiçbir şey için internete bağlanmaz.

> Windows *"Windows bilgisayarınızı korudu"* ya da "bilinmeyen yayıncı" uyarısı verirse: *Ek bilgi → Yine de
> çalıştır*. (Uygulama henüz dijital olarak imzalı değil.)

Açılışta **Ana ekran** gelir; **Düzenle** dört adımda ilerler: *Klasörler → Tara ve incele → Onayla →
Uygulanıyor / Bitti*. *Tara ve incele* adımında sol menüden **Yinelenenler** (korunacak kopyayı seç),
**Benzerler** (aynı kare, küçültülmüş kopya, seri çekim) ve **Kusurlu** (bulanık / karanlık) bölümlerini
gözden geçirebilirsin; kararların Onayla'ya geçerken plana yansır.

**Temizle** aynı adımlarla çalışır ama dosyaları tarih klasörlerine taşımaz: seçtiğin klasörlerdeki
kopyaları, benzer ve kusurlu kareleri klasörün içindeki `_Ayiklananlar` klasörüne taşır (kopyalamaz,
silmez). İstersen "kopyalarda öncelikli klasörler" seçebilirsin; Geri Al hepsini yerine döndürür.
`_Ayiklananlar`'a baktıktan sonra klasörü kendin silebilirsin.

Eski pencere **Klasik görünüm** olarak
sağ üstteki ⚙ menüsünde. Eski pencereyi doğrudan açmak için:
`FotoDuzenleyici.exe --klasik`.

## Güvenlik

- Dosyaların içeriği **hiç değiştirilmez**: EXIF/GPS/video metadatası olduğu gibi kalır.
- Her dosya önce kopyalanır, SHA-256 ile doğrulanır, oluşturma/değiştirme tarihleri korunur; ancak sonra eskisi
  **Geri Dönüşüm Kutusu'na** alınır (kalıcı silme yok).
- Hedefte var olan hiçbir dosyanın üzerine yazılmaz. İsim çakışmasında tarih eklenir.
- Her işlem günlüğe yazılır; **Geri Al** ile eski yerine dönülür.
- Uygulamadan önce mutlaka **Onay** ekranı gösterilir: ne yapılacağı, yer ve yarım kalmış işlem denetimi.

## Kaynaktan derleme

.NET 8 SDK gerekir.

```powershell
dotnet test
./publish.ps1        # dist\FotoDuzenleyici-win-x64.zip ve (Inno Setup 6 kuruluysa) dist\FotoDuzenleyici-Setup-x.y.z.exe
```

Kurulum betiği `installer\FotoDuzenleyici.iss`; `installer\test-install.ps1` Setup.exe'yi sessizce kurup
denetler ve kaldırır (GitHub Actions her gönderimde çalıştırır; kendi bilgisayarında çalıştırırsan kurulu
Foto Düzenleyici'yi kaldırır).

## Proje yapısı

- `FotoDuzenleyici.Core` – tarama, tarih okuma, yinelenen bulma, planlama, güvenli uygulama
- `FotoDuzenleyici.ViewModels` – ekran mantığı (WPF'siz, testli): Ana ekran ve Düzenle akışı
- `FotoDuzenleyici.App` – WPF arayüzü: yeni kabuk (WPF-UI) ve Klasik görünüm
- `FotoDuzenleyici.Cli` – komut satırı aracı (`fotoduzenle.exe`); `--apply` verilmedikçe hiçbir dosyaya dokunmaz, `--undo <günlük>` ile geri alır
- `FotoDuzenleyici.Tests` – birim testleri

## Plan ve değişiklikler

- [YOL_HARITASI.md](YOL_HARITASI.md) – yapılanlar ve sıradaki aşamalar
- [CHANGELOG.md](CHANGELOG.md) – sürüm sürüm değişiklikler

Metadata okuma: [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) (Apache-2.0).

Bu proje Claude (Anthropic) yardımıyla geliştirildi.
