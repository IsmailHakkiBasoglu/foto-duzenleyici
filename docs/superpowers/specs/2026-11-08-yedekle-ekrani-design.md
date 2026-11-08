# Yedekle ekranı — tasarım

Tarih: 2026-11-08 · Yol haritası: yeni arayüzün son parçası · Motor tasarımı: `2026-11-07-yedekleme-design.md`

## Amaç

Ana ekrandaki Yedekle kartından arşivi ikinci bir diske güvenle kopyalamak ve yedeğin bozulmadığını
denetleyip gerekirse onarmak. Motor (`ArchiveBackup`, `BackupManifest`) hazır ve testli; bu iş onun ekranıdır.

**Başarı ölçütü:** Kullanıcı klasörleri seçer, kopyalamadan önce ne olacağını görür, onaylar, sonucu okur.
Durdurulan yedek kaldığı yerden sürer. Yedekte elle bozulan dosyayı doğrulama bulur, Onar düzeltir.

**Kullanıcının seçimleri:**
- 2026-11-07: Hiçbir şey silinmez; arşivden silinen dosya yedekte kalır; değişen dosyanın eski hali
  `_EskiSurumler`'e taşınır; her kopya doğrulanır, ayrıca tam doğrulama düğmesi vardır.
- 2026-11-08: Kopyalama başlamadan önce **özet** gösterilir ve onay beklenir. Tasarım olduğu gibi onaylandı.

**Değişmeyen kurallar:** Arşive yalnız okumak için dokunulur. Özet adımı hiçbir yere yazmaz. Yer yetmiyorsa
ya da klasörler iç içeyse kopyalama başlatılamaz.

**Kapsam dışı:** Zamanlanmış yedek; ağ ve bulut hedefleri; sıkıştırma, şifreleme; birden çok yedek hedefini
yönetme (yalnız son çift hatırlanır); `_EskiSurumler` / `_Bozuk` temizliği; Klasik görünüme yedek eklemek;
kopyalama öncesi süre tahmini (disk hızı bilinmez).

## Motor değişiklikleri (`FotoDuzenleyici.Core/ArchiveBackup.cs`)

### Özet: `Preview`

```
public BackupPreview Preview(string archiveRoot, string backupRoot, BackupOptions opt, CancellationToken ct = default)

public sealed class BackupPreview
{
    public int New, Changed, Unchanged, MissingInArchive;   // özellik olarak
    public long BytesNeeded;                                 // yeni + değişmiş
    public long FreeBytes;
    public List<string> Errors;                              // okunamayan klasör / dosya
    public bool Cancelled;
    public string? Blocked;                                  // başlatılamama nedeni
}
```

- `Run`'ın kök denetimi, tarama ve yer hesabı özel bir ortak yönteme (`Survey`) alınır; `Preview` ve `Run`
  ikisi de onu çağırır. Özetin söylediğiyle yapılan aynı koddan çıkar. `Run`'ın dışarıdan görünen davranışı değişmez.
- Tarama sırasında `ct` denetlenir (bugün `Run`'ın tarama bölümü iptal edilemiyor; ortak yöntemle o da edilebilir olur).
- `Preview` yedek klasörünü **oluşturmaz**, `_Gecici`'yi temizlemez, listeyi kaydetmez. Yedek klasörü henüz yoksa
  liste boş sayılır ve boş alan klasörün sürücüsünden okunur.
- Engeller `Run` ile aynıdır: arşiv yok, iç içe klasörler, okunamayan liste, yer yetmiyor (gereken + %1).
- `FreeSpace` kancası `public` olur; ekran `Services.FreeSpace`'i verir.

### Onarım

`RepairResult`'a `bool Cancelled` eklenir; `Repair` iptalde bunu doldurur.

## Ekran

### `BackupFlowViewModel` (ViewModels, yeni)

Tek sayfa, durum makinesi. `Düzenle`/`Temizle` sihirbazı kullanılmaz: o inceleme ve plan için kuruludur.

`BackupStep`: `Select` → `Preview` → `Running` → `Finished`. Doğrulama ve onarım da `Running`'i kullanır;
`BackupActivity` (`Backup`, `Verify`, `Repair`) hangi işin sürdüğünü söyler.

| Durum | İçerik | Düğmeler |
| --- | --- | --- |
| **Select** | Arşiv klasörü, yedek klasörü (Gözat), "Tekrarları da yedekle (_Tekrarlar)" (varsayılan kapalı) | Tara (iki klasör seçili ve arşiv varken), Yedeği doğrula (yedek klasörü varken) |
| **Preview** | Yeni / değişmiş / aynı / arşivde yok sayıları, gereken alan, boş alan; okunamayanlar; engel varsa nedeni | Yedeklemeyi başlat (engel yoksa ve kopyalanacak dosya varsa), Geri |
| **Running** | İlerleme çubuğu (bayta göre), o anki dosya, "1.240 / 9.800 dosya", kalan süre | Durdur |
| **Finished** | Sonuç sayıları, kopyalanan boyut, yapılamayan dosyalar | Yedeği doğrula, Onar (sorun bulunduysa), Kaldığı yerden devam et (durdurulduysa), Yedek klasörünü aç, Yeni işlem, Ana ekrana dön |

Tarama (`Preview` çağrısı) sürerken sayfada meşgul göstergesi ve Durdur vardır. Tarama durdurulursa Select'e dönülür.

**Ön doldurma:** `AppSettings.LastBackup` varsa ondan; yoksa arşiv `Recent` listesindeki son arşivden.
Düzenle'nin Bitti ekranından gelindiyse arşiv o işin arşividir.

**Kopyalanacak dosya yoksa** özet "Yedek güncel, kopyalanacak dosya yok." der; başlat düğmesi kapalıdır.

**Doğrulama sonucu:**
- "9.800 sağlam · 0 eksik · 1 bozuk" ve altında sorunlu dosyalar (göreli yol + "eksik" / "bozuk").
- Yedek listesi yoksa ya da boşsa: "Bu klasörde bu uygulamayla alınmış bir yedek bulunamadı."
- Listede olmayan dosyalar yalnız sayı olarak yazılır, sorun sayılmaz.
- Onar, arşiv klasörü seçili ve varken açılır; yalnız son doğrulamanın sonucuyla çalışır. Klasör seçimi
  değişirse doğrulama sonucu silinir (başka arşivle onarım yapılamaz).
- Onarım sonucu: onarılan sayısı, onarılamayanlar nedenleriyle. Onarım bitince doğrulama sonucu silinir;
  kullanıcı isterse yeniden doğrular.
- Doğrulama durdurulursa o ana kadarki sonuç "yarım" diye belirtilerek gösterilir; bulunan sorunlar onarılabilir.

**Hatalar:** `Blocked` metni olduğu gibi gösterilir. Disk kopan / dolan durumda motorun hata satırları Finished
listesinde çıkar. Beklenmeyen istisnada sayfa Select'e döner ve kabuk hata şeridi gösterilir.

**Devam etme:** Ayrı kayıt gerekmez; motor listeye göre kalanları bulur. "Kaldığı yerden devam et", özeti
atlamaz: yeniden tarar ve Preview'a döner.

### Ayarlar (`Settings.cs`)

`public BackupPair? LastBackup { get; set; }`
`public sealed record BackupPair(string Archive, string Backup, bool IncludeDuplicates, DateTime Used);`

Yedekleme başlatıldığında `TrySave` ile yazılır. Bozuk kayıt (`Archive` ya da `Backup` null) yüklenirken yok sayılır.

### Kabuk (`ShellViewModel`)

Bugün kilit yalnız `OrganizeFlowViewModel`'i tanıyor. Ortak arayüz eklenir:

```
public interface ILongRunningPage
{
    bool IsLocked { get; }        // gezinme kapalı mı (tarama dahil)
    bool MustConfirmClose { get; }// pencere kapatılırken sorulsun mu
    Task? RunningTask { get; }
    void RequestStop();
}
```

`OrganizeFlowViewModel` ve `BackupFlowViewModel` uygular. `Locked`, `RequestClose`, `RunningTask` buna geçer;
Düzenle/Temizle'nin davranışı ve metni değişmez. Yedek için kapatma sorusu: "Yedekleme sürüyor. Durdurulsun mu?
O anki dosya yarıda kesilir, yedek tutarlı kalır; kalanlar sonra devam ettirilebilir."

Yedek sürerken: ana ekrana dönüş, Geri Al, Klasik görünüm kapalıdır (Klasik için mevcut hata iletisi yedek
için de geçerli hale getirilir).

### Sayfalar (App)

- `Shell/BackupPage.xaml(.cs)`: yukarıdaki dört durum; mevcut `Panel`, `H2`, `Small` stilleri ve `DataTemplate` eşlemesi.
- `HomePage.xaml`: Yedekle kartı Temizle kartı gibi tıklanır olur (`Opacity` ve "Yakında" kalkar); `LastBackup` varsa
  "Son: arşiv → yedek" satırı.
- `ApplyPage.xaml`: Bitti ekranındaki "Yakında" kartı **Yedekle…** düğmesi olur; yalnız Düzenle'de görünür
  (Temizle'nin tek bir arşivi yoktur), arşivi dolu açar. Temizle Bitti ekranında kart gizlenir.
- `HomeViewModel.BackupRequested` olayı, `ShellViewModel.StartBackup(string? archive)`.

## Testler (xUnit, geçici klasörler)

**Motor**
1. `Preview` sayıları, ardından çalışan `Run`'ın sayılarıyla aynıdır (yeni, değişmiş, aynı, arşivde yok, bayt).
2. `Preview` yedek kökünü oluşturmaz, `_Gecici`'ye ve listeye dokunmaz.
3. `Preview` engelleri: arşiv yok, iç içe, bozuk liste, yer yetmiyor.
4. `Preview` iptali `Cancelled` döner; `Run`'ın tarama aşamasında iptal hiçbir şey kopyalamaz.
5. `_Tekrarlar` kuralı `Preview`'da da geçerlidir.
6. `Repair` iptalde `Cancelled` döner.

**Ekran mantığı**
7. Select → Tara → Preview → başlat → Finished; `LastBackup` yazılır.
8. Engelli ve "kopyalanacak dosya yok" özette başlat kapalıdır.
9. Durdur → Finished'da devam düğmesi; devam Preview'a döner, kalanı kopyalar.
10. Doğrula bozuk dosya bulur → Onar → yeniden doğrulama temiz; onarılamayan neden gösterir.
11. Klasör değişince doğrulama sonucu silinir, Onar kapanır.
12. Yedek listesi olmayan klasörde doğrulama açıklayıcı metin verir.
13. Ön doldurma: `LastBackup`, yoksa son arşiv; Bitti ekranından gelen arşiv önceliklidir.
14. Kabuk: yedek sürerken GoHome / Undo / Klasik çalışmaz; kapatma sorar, evet → durdurup bekler.
15. Düzenle akışının kilit davranışı değişmez (mevcut testler).
16. Bozuk `LastBackup` ayarı açılışı bozmaz.

Ekran, `scratchpad\render` düzeneğiyle örnek klasörlerde çalıştırılıp bağlama hataları ve görüntü denetlenir.
Kullanıcının gerçek arşivinde hiçbir şey çalıştırılmaz.
