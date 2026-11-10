# Setup.exe — tasarım

Tarih: 2026-11-10 · Durum: kullanıcının sohbette verdiği kararlarla yazıldı (uzaktan; "planlı işleri yap").

## Amaç

Foto Düzenleyici'yi GitHub'dan indiren herkesin (yalnız geliştirici değil) zip açmadan, tek bir
`FotoDuzenleyici-Setup-x.y.z.exe` ile kurup kaldırabilmesi. Taşınabilir zip yayınlanmaya devam eder.

## Kullanıcı kararları

- Dağıtım: **herkese açık** (bilinmeyen kişiler de indirir).
- Güncelleme: **uygulama hiçbir şey yapmaz**, internete bağlanmaz. Yeni sürüm = yeni Setup.exe eskisinin üzerine.
- Ekstralar: masaüstü kısayolu, klasör sağ tık menüsü, komut satırı aracı, kaldırırken ayarları sor.
- Sağ tık: **ana ekran açılır, klasör hazır**; Düzenle'de kaynak, Temizle'de temizlenecek klasör olur.

## Araç

Inno Setup 6 (ücretsiz, Türkçe dil dosyası hazır, `PrivilegesRequiredOverridesAllowed=dialog`, Pascal betikle
PATH ve kaldırma sorusu). Alternatifler: WiX/MSI (Türkçe arayüz ve özel soru zahmetli), Velopack
(asıl değeri otomatik güncelleme; istenmedi).

## Kurulum davranışı

- Dil: Türkçe; sistem dili Türkçe değilse İngilizce.
- İlk soru: **Yalnız benim için** (varsayılan, yönetici izni yok, `%LOCALAPPDATA%\Programs\Foto Düzenleyici`)
  / **Tüm kullanıcılar için** (yönetici, `Program Files\Foto Düzenleyici`). Kayıt defteri yazımları `HKA` ile
  seçime uyar (HKCU / HKLM).
- Lisans sayfası (MIT), sonra görevler:
  - Masaüstü kısayolu — varsayılan kapalı
  - Klasör sağ tık menüsü "Foto Düzenleyici ile aç" — varsayılan açık
  - Komut satırı aracını PATH'e ekle (`fotoduzenle`) — varsayılan kapalı
- Her zaman: Başlat menüsü kısayolu, Program Ekle/Kaldır kaydı (simge, sürüm, yayıncı, GitHub adresi).
- Son sayfa: "Foto Düzenleyici'yi başlat" kutusu.
- x64 Windows 10/11 dışı reddedilir.

## Dosya düzeni

Kurulum klasöründe uygulama (`FotoDuzenleyici.exe`) ve komut satırı aracı (`fotoduzenle.exe`) **aynı klasöre,
tek dosya değil** yayınlanır; .NET çalışma zamanını paylaşırlar (iki tek-dosya exe kurulumu iki katına
çıkarırdı). README, LICENSE, CHANGELOG da kopyalanır. Zip bugünkü gibi tek dosyalı uygulamayı içerir.

CLI projesinin derleme adı `fotoduzenle` olur (`AssemblyName`).

## Uygulamadaki değişiklikler

1. **Açılış klasörü:** Komut satırında `--` ile başlamayan ve var olan bir klasör olan ilk argüman "seçili klasör"
   olur (yoksa / klasör değilse yok sayılır). Ana ekranda kartların üstünde ince bir şerit:
   "Seçili klasör: `D:\…`" ve **×** (temizle). Seçili klasör varken:
   - Düzenle kartı "Başla →": kaynak = o klasör, arşiv = son kullanılan arşiv (yoksa boş).
   - Temizle kartı "Başla →": temizlenecek klasör = o klasör.
   - "Son kullanılanlar" satırlarındaki **Aç** ve Yedekle değişmez.
   Bir akış başlatılınca ya da × ile seçim biter; ana ekrana dönülünce şerit görünmez.
2. **Çalışıyor işareti:** Uygulama ve CLI açılışta `FotoDuzenleyici.Calisiyor` adlı bir mutex açar (yerel ve
   `Global\`). Kurulum (`AppMutex`) bunu görünce "önce uygulamayı kapat" der; güncelleme yarıda kalmaz.

`--klasik` bayrağı aynen çalışır; klasör argümanı klasik pencerede yok sayılır.

## Güncelleme ve sürüm

- Aynı `AppId`; yeni sürüm aynı klasöre kurulur, ayarlar (`%LOCALAPPDATA%\FotoDuzenleyici`) dokunulmaz.
- Kurulu sürüm daha yeniyse uyarı: "Daha yeni bir sürüm (x) kurulu. Yine de eski sürüm kurulsun mu?" (varsayılan Hayır).
- Kurulu dosya düzeni değişirse eski dosyalar `[InstallDelete]` ile temizlenir (şimdilik gerek yok).

## Kaldırma

- Sağ tık kaydı, PATH girdisi, kısayollar ve kurulum klasörü silinir.
- Sessiz kaldırma değilse soru: "Ayarlar ve önbellekler (`%LOCALAPPDATA%\FotoDuzenleyici`) de silinsin mi?
  Fotoğraflarına ve arşivdeki günlüklere dokunulmaz." Varsayılan **Hayır**.
- Arşiv, `_Gunluk`, `_Tekrarlar`, yedek klasörleri hiçbir durumda silinmez.

## İmza

Kod imzası yok (sertifika ücretli). README'ye "Windows korudu" uyarısının nasıl geçileceği yazılır. İleride
SignPath (açık kaynak için ücretsiz) denenebilir — ayrı iş.

## Yayın

- `publish.ps1`: zip'e ek olarak `dist\setup\` klasörünü (iki proje, tek dosya değil) yayınlar ve Inno Setup
  bulunursa `dist\FotoDuzenleyici-Setup-<sürüm>.exe` derler; bulunamazsa uyarıp geçer. Sürüm csproj'dan okunur.
- GitHub Actions `release` işi: Inno Setup yoksa `choco install innosetup` ile kurar; zip ve Setup.exe birlikte
  sürüme eklenir.
- Ayrı bir `kurulum` işi (PR ve main): Setup.exe'yi derler, **sessiz kurar** (`/VERYSILENT /CURRENTUSER` ve tüm
  görevler), dosyaları, sağ tık kaydını ve PATH'i denetler, sessiz kaldırır ve kayıtların silindiğini denetler;
  Setup.exe'yi yapıt olarak ekler.

## Test

- Birim testleri (xUnit, TDD): argümandan klasör seçimi; ana ekran kartlarının seçili klasörle oluşturduğu
  çift; × ile temizleme; kabuğun ilk ana ekrandan sonra seçimi unutması.
- Kurulum: CI'deki sessiz kur/kaldır denemesi. Geliştirici bilgisayarında elle deneme kullanıcı dönünce
  (Inno Setup'ın yerel kurulumu kullanıcı onayı ister).

## Kapsam dışı

Otomatik güncelleme, kod imzası, Windows 11 ilk sağ tık menüsü (imzalı paket ister), dosya ilişkilendirme,
klasör arka planı sağ tık menüsü, ARM64.
