# Yeni arayüz, 1. parça (kabuk + Düzenle akışı) — tasarım

Tarih: 2026-11-07 · Yol haritası: taslaklar → teknoloji seçimi → ViewModel katmanı → yeni arayüz → Setup.exe
Görsel taslak: `docs/tasarim/arayuz-taslak.html` (ekran 0, 1, 2, 3, 3a, 3b)

## Amaç

Uygulamaya Windows 11 / Fluent görünümlü yeni bir kabuk eklemek ve Düzenle akışını bu kabukta,
dört adımlı bir sihirbaz olarak sunmak:

- Klasörler
- Tara ve incele
- Onayla
- Uygulanıyor / Bitti

Ekran mantığı, WPF'e bağımlı olmayan ve test edilebilen bir ViewModel katmanında durur. Eski pencere
"Klasik görünüm" olarak açılabilir kalır.

**Başarı ölçütü:**
- Kullanıcı yeni arayüzde birden çok kaynak ve bir arşiv seçer, tarar.
- Özeti ve planı görür, onaylar, uygular ve sonucu görür.
- Sonucu Geri Al ile geri alabilir.
- Bunların hepsi eski pencereye gerek kalmadan yapılır.
- Aynı akış ViewModel testlerinde gerçek dosyalarla uçtan uca geçer.

**Kullanıcının seçimleri (2026-11-07):**
- Eski pencere, yeni arayüz her işini yapana kadar yan yana kalır ("Klasik görünüm").
- İlk parçada inceleme bölümleri (Benzerler, Yinelenenlerde konum seçimi, Kusurlu) yok. Özet + Plan
  geliyor; incelemeler 2. parçada.
- Teknoloji: WPF-UI + CommunityToolkit.Mvvm; ViewModel'ler ayrı, WPF'siz projede (yaklaşım A).

**Değişmeyen kurallar:**
- Core'daki güvenlik davranışı aynen kullanılır: kopyala + doğrula + Geri Dönüşüm, günlük, Geri Al,
  yarım kalan işleme devam.
- Arayüz Core'u atlayıp dosyaya dokunmaz.
- Hiçbir şey kullanıcı onaylamadan uygulanmaz.

**Kapsam dışı (sonraki parçalar):**
- Temizle ve Yedekle ekranları.
- Benzerler, Kusurlu ve Yinelenenler inceleme ekranları; karşılaştırma penceresi.
- Klasör ağacından hariç tutma.
- Duraklat, "Yeniden dene", zaman çizelgesi.
- Setup.exe.

## Mimari

```
FotoDuzenleyici.Core         değişmez (gerekirse küçük, testli eklemeler)
FotoDuzenleyici.ViewModels   yeni; net8.0; Core + CommunityToolkit.Mvvm; WPF referansı yok
FotoDuzenleyici.App          WPF-UI kabuğu ve sayfalar + eski MainWindow / CompareWindow
FotoDuzenleyici.Tests        + ViewModel testleri (ViewModels projesine referans)
```

**Hizmet arayüzleri.** Pencereye bağlı her şey, ViewModels projesindeki arayüzlerin arkasında durur.
Bunların WPF uygulaması App'te, sahte sürümleri testlerde yazılır.

- `IFolderPicker`: klasör seçtirir; vazgeçilirse `null` döner.
- `IFilePicker`: günlük dosyası seçtirir (Geri Al için) ve CSV kaydetme yolu sorar.
- `IDialogs`: evet/hayır onayı ve bilgi mesajı.
- `IShell`: dosya gezgininde klasör açar ve Klasik görünüm penceresini açar.
- `ISettingsStore`: ayarları yükler ve kaydeder.

**ViewModel'ler:**
- `ShellViewModel`
  - Etkin sayfa: Ana ekran ya da Düzenle akışı.
  - Komutlar: Geri Al…, Klasik görünüm, tema.
  - Üstte görünen hata şeridi (`ErrorMessage`).
- `HomeViewModel`
  - Modül kartları: Düzenle etkin; Temizle ve Yedekle "yakında".
  - Son kullanılanlar.
- `OrganizeFlowViewModel`
  - Etkin adım (`Folders`, `Review`, `Confirm`, `Apply`) ve adım çubuğunun durumu.
  - Taramadan sonra bir `OrganizeSession` ve oluşan `Plan`'ı tutar.
  - Adımlar: `FoldersStepViewModel`, `ReviewStepViewModel`, `ConfirmStepViewModel`, `ApplyStepViewModel`.

**Uzun işler:**
- Tarama, plan ve uygulama `Task.Run` üzerinde çalışır.
- İlerleme `IProgress<…>` ile, durdurma `CancellationToken` ile yapılır.
- ViewModel'ler UI iş parçacığını bilmez; ilerlemenin UI'a taşınmasını `Progress<T>` sağlar.

**Ayarlar:**
- Dosya: `%LOCALAPPDATA%\FotoDuzenleyici\ayarlar.json`.
- İçerik:
  - son kullanılan 5 "kaynaklar → arşiv" çifti ve tarihi;
  - son `PlanOptions` seçimleri (tarih ekle, kaynak adı ekle, kaynak klasörü modu, RAW ayrı);
  - tema (`Sistem` / `Açık` / `Koyu`);
  - "Geri Dönüşüm Kutusu'na al" seçimi.
- Bozuk ya da okunamayan dosyada boş başlanır ve şerit bir kez uyarır.

## Ekranlar

**Ana ekran (taslak 0)**
- Başlık "Ne yapmak istiyorsun?" ve üç kart.
- **Düzenle** etkin; varsa "Son: <ilk kaynak> (+n) → <arşiv> · <tarih>".
- **Temizle** ve **Yedekle** soluk, "yakında" etiketli.
- Son kullanılanlardan birine tıklanınca Düzenle akışı o klasörlerle dolu açılır.

**1 · Klasörler (taslak 1)**
- **Nereden?**
  - Kaynak listesi: ekle (`IFolderPicker`) ve ✕ ile çıkar.
  - Aynı klasör ikinci kez eklenmez.
  - Var olmayan klasör listede uyarı rengiyle görünür ve taramaya katılmaz.
- **Nereye?**
  - Tek arşiv klasörü.
  - Bilgi notu: "Arşiv de taranır; zaten olanın kopyası tanınır, tarih ve model adı iki kez yazılmaz."
- **Düzen çipleri:** seçili `PlanOptions`'ın özeti. "Değiştir" bir iletişim kutusu açar:
  - kaynak klasörü modu;
  - ada tarih ekle;
  - ada kaynak ekle;
  - RAW ayrı klasör.
- **"Tara ve incele"**
  - En az bir var olan kaynak ve bir arşiv seçilmeden kapalıdır.
  - Basılınca seçimler son kullanılanlara kaydedilir ve tarama başlar. Tarama, `OrganizeSession.Scan` ile
    hash ve kopya bulma da dahil yapılır.
  - İlerleme şeridi görünür: "Taranıyor 1.240 / 4.250" ve "Kopyalar aranıyor …"; yanında **Durdur**.
  - Durdurulursa ya da hata olursa 1. adımda kalınır.
  - Başarılı taramada `BuildPlan()` (benzer ve kusurlu olmadan) çalışır ve 2. adıma geçilir.

**2 · Tara ve incele (taslak 2, ilk parça sürümü)**
- **Yan menü**
  - Özet ve Plan.
  - "Karar bekleyenler" altında Yinelenenler (grup sayısı); Benzerler ve Kusurlu soluk, "Klasik
    görünümde" bağlantısıyla.
  - Okunamayanlar (`ScanErrors` sayısı).
- **Kutucuklar**
  - Taşınacak: `Move` sayısı ve GB.
  - Zaten yerinde: `AlreadyInPlace`.
  - Birebir kopya: `Duplicate` sayısı ve GB.
  - Tarihsiz: hedefi `UndatedFolderName` altında olan `Move`'lar.
- **Dikkat listesi**
  - Yinelenen grup sayısı: "öntanımlı korunan kopya geçerli; değiştirmek için Klasik görünüm".
  - Adı değişen dosya sayısı (`Renamed`).
  - Okunamayan dosya sayısı.
- **Plan tablosu**
  - Sütunlar: Eylem, Kaynak, Hedef, Tarih.
  - Filtre: Tümü / Taşı / Kopya / Yerinde / Adı değişen.
  - Arama: kaynak ya da hedef yolunda, büyük/küçük harf duyarsız.
  - Satırlar sanallaştırılmış.
  - Yollar baştan kısaltılır, dosya adı görünür kalır.
- **Alt çubuk:** özet cümle, "Planı CSV kaydet" ve **"Onay ekranına →"**.
- **Üstte:** "⟳ Yeniden tara" (1. adımdaki seçimlerle yeniden tarar) ve "‹ Klasörler".

**3 · Onayla (taslak 3)**
- **Özet satırları** (sayı + GB + nereye):
  - Taşınacak → `<arşiv>\yyyy-MM`
  - Birebir kopya → `_Tekrarlar`
  - Tarihsiz → `Tarihsiz`
  - Adı değişecek (sayı)
  - Zaten yerinde: dokunulmayacak
- **Denetimler:**
  - **Yer.** Arşivin sürücüsündeki boş alan ile gereken alan karşılaştırılır. Gereken alan, bütün `Move` ve
    `Duplicate` öğelerinin toplam boyutudur. Aynı sürücüdeki dosyalar da önce kopyalanıp sonra Geri
    Dönüşüm'e gittiği için hepsini saymak güvenli taraftır.
    - Yetmezse kırmızı satır görünür ve Uygula kapanır.
  - **Başka disk.** Arşivden farklı sürücüdeki kaynaklar listelenir: "kopyalanıp doğrulanır, asılları
    Geri Dönüşüm'e gider".
  - **Yarım kalmış işlem.** `Executor.FindUnfinished(arşiv)` sonuç verirse sarı satır ve iki düğme
    görünür.
    - **Devam et:** 3a'da `Resume` çalışır. Bitince planın eskidiği söylenir ve "Yeniden tara" önerilir.
    - **Vazgeç:** onay istenir, sonra `Abandon` çalışır.
    - Bu satır çözülmeden Uygula kapalıdır.
  - **Günlük.** Yazılacak günlük yolu gösterilir: `<arşiv>\_Gunluk\islem-yyyyMMdd-HHmmss.jsonl`.
- **Onay kutusu:** "Kaynaktaki eski dosyalar Geri Dönüşüm Kutusu'na alınsın". Öntanımlı açıktır; değeri
  ayarlardan gelir.
- **Düğmeler:** "← İncelemeye dön" ve **"Uygula · N işlem"**. N, `AlreadyInPlace` dışındaki öğe sayısıdır.
  - Ayrıca bir onay kutusu açılmaz; bu ekranın kendisi onaydır.

**3a · Uygulanıyor**
- Gösterilenler:
  - yüzde, "yapılan / toplam", o anki dosya;
  - tahmini kalan süre: geçen süre ve yapılan sayıdan hesaplanır, ilk 20 işlemden sonra görünür.
- **Durdur:** iptal belirtecini tetikler. Executor o anki dosyayı bitirir, kalanlar günlükte bekler. Sonuç
  ekranı "durduruldu, kalan N dosya Onayla ekranında devam ettirilebilir" der.
- Adım çubuğu ve gezinme kilitlidir. Pencere kapatılmak istenirse "işlem sürüyor, durdurulsun mu?" diye
  sorulur.

**3b · Bitti**
- Başlık: "Arşiv düzenlendi" ya da "Durduruldu". Altında süre.
- **Kutucuklar:** Başarılı (`Done`), Atlandı (`Skipped`), Yapılamadı (`Failed`).
- **Yapılamayanlar listesi:** `Errors`; liste yoksa gizlenir.
- **Düğmeler:**
  - Arşivi aç;
  - bu işlemi Geri Al: onay istenir, sonra `Executor.Undo` çalışır ve ilerleme 3a görünümünde gösterilir;
  - Ana ekrana dön.
- **Yedekle kartı:** "yakında".
- Plan artık geçersizdir. Akış yeniden başlatılmadan Onayla'ya dönülemez.

**Kabuk**
- WPF-UI `FluentWindow`, Mica arka planı, özel başlık çubuğu.
- Sağ üstte:
  - **Geri Al…:** `IFilePicker` ile günlük seçtirir; varsayılan klasör `<son arşiv>\_Gunluk`. Onay istenir,
    sonra `Undo` çalışır ve ilerleme şeritte gösterilir.
  - **⚙ menüsü:** Klasik görünüm, Tema (Sistem/Açık/Koyu), Hakkında (sürüm).
- **Hata şeridi** (WPF-UI `InfoBar`): beklenmeyen istisnanın iletisi gösterilir ve kapatılabilir. Akış
  bulunduğu adımda kalır.
- Uygulama açılışta yeni kabuğu gösterir. `--klasik` argümanı ile doğrudan eski pencere açılır.

## Hatalar

- ViewModel'deki her uzun işlem `try/catch` ile sarılır:
  - `OperationCanceledException` → "Durduruldu" durumu;
  - diğer istisnalar → `ErrorMessage`.
- Program kapanmaz; dosya durumu günlükte olduğu için devam ya da Geri Al mümkün kalır.
- Tarama sırasında arşiv ya da kaynak çıkarılırsa Core'un `ScanErrors` listesi kullanılır. Kaynağın tümü
  okunamıyorsa şerit uyarır.
- Plan oluşturulduktan sonra dosyalar dışarıdan değişirse Executor'ın mevcut denetimleri geçerlidir; hatalı
  dosyalar 3b'de listelenir.

## Test

**ViewModel birim testleri** (sahte `IFolderPicker`, `IDialogs`, `ISettingsStore` ile):
- Klasörler adımı:
  - "Tara" düğmesinin açık/kapalı kuralları;
  - aynı klasörün ikinci kez eklenmemesi;
  - var olmayan klasörün atlanması;
  - son kullanılanlara kayıt.
- Özet ve Onayla hesapları: küçük bir `Plan` için sayılar, GB, tarihsiz, adı değişen ve N.
- Plan filtresi ve arama.
- Yer denetimi: boş alan bir arayüzle verilir (`Func<string, long>`).
- Yarım kalan işlem:
  - satır görünür;
  - Uygula kapalıdır;
  - Vazgeç → `Abandon` çalışır.
- Durdurma: iptal → 3b "Durduruldu".
- Ayar dosyası bozuk → boş başlanır, uyarı bir kez verilir.

**Uçtan uca** (geçici klasörler, gerçek Core):
- Akış: kaynak + arşiv seç → tara → plan → onay → uygula → dosyalar arşivde → Geri Al → dosyalar yerinde.
- Bütün adımlar ViewModel komutlarıyla sürülür.

**Elle:**
- Uygulama açılır; dört ekran açık ve koyu temada ekran görüntüsüyle denetlenir.
- Klasik görünüm açılır.
- Gerçek veride Uygula, kullanıcı istemeden yapılmaz.

## Sürüm

Bu parça bitince **0.2.0** çıkar. CHANGELOG ve YOL_HARITASI güncellenir: Aşama 3 "Koyu tema, modern
görünüm" → 🔄 (1. parça).
