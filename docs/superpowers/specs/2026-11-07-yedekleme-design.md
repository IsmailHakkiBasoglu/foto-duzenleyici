# Yedekleme ve hash ile doğrulama — tasarım

Tarih: 2026-11-07 · Yol haritası: Aşama 4, "Arşivi ikinci bir diske yedekleme ve yedeği hash ile doğrulama"

## Amaç

Düzenli arşivin ikinci bir diske artımlı olarak yedeklenmesi. Her kopya SHA-256 ile doğrulanır.
Yedek sonradan tam olarak yeniden okunup bozulma (bit çürümesi) ya da eksik dosya için denetlenebilir.
Bulunan sorunlar arşivden onarılabilir.

**Başarı ölçütü:** Gerçek arşivin ilk yedeği eksiksiz biter. İkinci çalıştırma, değişmemiş dosyaları
okumadan birkaç saniyede biter. Yedekte elle bozulan bir dosyayı tam doğrulama yakalar, Onar da
düzeltir. Yedek klasörü uygulama olmadan da Gezgin'de gezilebilir.

**Kullanıcının seçimleri (2026-11-07):**
- Arşivden silinen ya da taşınan dosyaların yedekteki kopyası **yedekte kalır**: yedek yalnız büyür.
- Doğrulama: **kopyalarken her dosya**, ayrıca **"Yedeği tam doğrula" düğmesi**.
- Arşivde içeriği değişen dosyanın eski sürümü **`_EskiSurumler` klasörüne taşınır**, yenisi kopyalanır.
- Tasarım (aşağıdaki bölümler) olduğu gibi onaylandı.

**Değişmeyen kurallar:**
- Uygulama hiçbir dosyayı silmez: ne arşivde ne yedekte. Arşive yalnızca okumak için dokunulur.
- Hash eşleşmeden hiçbir kopya "yedeklendi" sayılmaz.
- İptal ya da çökme yedeği tutarsız bırakmaz.

**Kapsam dışı:** Zamanlanmış ya da otomatik yedekleme; ağ ve bulut hedefleri; sıkıştırma, şifreleme;
birden çok yedek hedefinin aynı anda yönetilmesi; taşınan dosyaları yedekte hash ile bulup yerinde
taşıma (yeniden kopyalanır, eski yolu da kalır); `_EskiSurumler` / `_Bozuk` temizliği.

## Kavramlar

- **Arşiv kökü:** düzenli arşiv klasörü (Plan'daki hedef klasör, `PlanOptions.DestinationRoot`).
- **Yedek kökü:** kullanıcının seçtiği klasör, ör. `F:\Yedek\Arşiv`. Arşiv kökünün içinde ya da
  arşiv kökü yedek kökünün içinde olamaz (iki yönde de reddedilir).
- **Göreli yol:** dosyanın arşiv köküne göre yolu. Yedekte aynı göreli yolda durur.
- **Ayrılmış klasörler** (yedek kökünde): `_EskiSurumler`, `_Bozuk`. Arşivde aynı adlı klasör varsa
  yedeklenirken atlanır; yedek listesi bu klasörlerdeki dosyaları içermez.

## Bileşenler

### `BackupManifest` (Core, yeni)

Yedek kökündeki `.fotoduzenleyici-yedek.json` dosyası. Kayıt başına:

| Alan | Anlamı |
| --- | --- |
| `Path` | göreli yol (anahtar, büyük/küçük harf duyarsız) |
| `Size` | bayt |
| `Modified` | arşivdeki dosyanın son yazılma zamanı (UTC) |
| `Sha256` | onaltılık küçük harf |
| `BackedUp` | yedeklendiği an (UTC) |

- `static BackupManifest Load(string backupRoot)`: dosya yoksa boş liste döner. Bozuk JSON okunamazsa
  bir istisna fırlatır; yedek o durumda başlamaz, çünkü listesiz yedek her şeyi yeniden kopyalardı.
- `void Save()`: önce `.tmp` dosyasına yazar, sonra yerine taşır (atomik değiştirme).
- `TryGet(path, out entry)`, `Set(entry)`, `Remove(path)`, `Entries`.

### `ArchiveBackup` (Core, yeni)

```
BackupResult Run(string archiveRoot, string backupRoot, BackupOptions opt,
                 IProgress<BackupProgress>? progress, CancellationToken ct)
VerifyResult Verify(string backupRoot, IProgress<BackupProgress>? progress, CancellationToken ct)
RepairResult Repair(string archiveRoot, string backupRoot, VerifyResult problems,
                    IProgress<BackupProgress>? progress, CancellationToken ct)
```

`BackupOptions`: `IncludeDuplicates` (varsayılan `false`), `DuplicatesFolderName` (`_Tekrarlar`),
`SaveEvery` (varsayılan 200 dosya).

#### Yedekle (`Run`)

1. **Kontroller:** kökler var mı, iç içe değil mi; liste yüklenebiliyor mu. Yedek kökünde artık kalan
   `*.part` dosyaları silinir. Bunlar yalnızca uygulamanın kendi yarım kopyalarıdır; liste dışıdırlar.
2. **Tarama:** arşiv kökündeki tüm dosyalar listelenir. `IncludeDuplicates` kapalıysa
   `_Tekrarlar\` altı atlanır. Ayrılmış klasör adları da atlanır. Her dosya şunlardan biri olur:
   - **yeni:** listede yok;
   - **değişmiş:** listede var ama `Size` ya da `Modified` farklı;
   - **aynı:** ikisi de eşit. Bu dosya okunmaz.
   Listede olup arşivde artık olmayan dosyalar **arşivde yok** olarak yalnızca sayılır.
   Bilinen sınır: boyutu ve zamanı aynı kalıp içeriği değişen bir dosya (çok nadir) "aynı" sayılır.
   Bu durum ancak tam doğrulamada değil, arşiv tarafında yakalanabilir, o da kapsam dışı.
3. **Boş alan:** yeni ve değişmiş dosyaların toplam boyutu + %1 pay, yedek diskindeki boş alandan
   büyükse hiçbir şey yapılmaz. Sonuç "yer yetmiyor: X gerekli, Y boş" der.
4. **Kopyalama** (dosya başına, sırayla):
   - Değişmişse yedekteki mevcut dosya önce
     `_EskiSurumler\yyyy-MM-dd\<göreli yol>` yoluna taşınır. Aynı yol varsa sona ` (2)`, ` (3)`… eklenir.
     Yedekte dosya hiç yoksa taşıma adımı atlanır.
   - Kaynak okunurken SHA-256 hesaplanır, `<hedef>.part` dosyasına yazılır.
   - `.part` baştan okunur ve hash'i hesaplanır. Eşleşirse:
     - son yazılma zamanı kaynağınkine ayarlanır;
     - dosya gerçek adına çevrilir;
     - listeye yazılır.
   - Eşleşmezse `.part` silinir ve bir kez daha denenir. İkinci denemede de eşleşmezse dosya hata
     olarak raporlanır ve listeye girmez.
   - Okuma/yazma hatası (dosya kilitli, disk çıkarıldı…) dosyaya özgü hata olur ve sıradakine geçilir.
     Hedef disk yoksa ya da dolduysa yedekleme durur.
   - Her `SaveEvery` dosyada bir ve sonda liste kaydedilir. İptalde mevcut dosya yarıda bırakılır
     (`.part` kalır, sonraki çalıştırma temizler), liste kaydedilir.
5. **Sonuç (`BackupResult`):**
   - yeni / değişmiş / aynı / arşivde yok sayıları;
   - kopyalanan bayt;
   - hata listesi (göreli yol + neden);
   - iptal edildi mi.

#### Tam doğrula (`Verify`)

Listedeki her kayıt için yedekteki dosya okunur ve SHA-256 hesaplanır. Sonuç üç gruptan biridir:

- **tamam;**
- **eksik:** dosya yok;
- **bozuk:** hash ya da boyut farklı.

Arşive hiç dokunulmaz. Yedek kökünde olup listede olmayan dosyalar (ayrılmış klasörler hariç)
**listede yok** olarak yalnızca sayılır. İptal edilebilir; o ana kadarki sonuç döner.

#### Onar (`Repair`)

Yalnız `Verify` sonucundaki eksik ve bozuk kayıtlar için çalışır. Kaydın arşivdeki dosyası hâlâ duruyor ve
SHA-256'sı listedeki ile aynıysa:

- bozuk kopya önce `_Bozuk\yyyy-MM-dd\<göreli yol>` altına taşınır (eksikse bu adım yok);
- dosya "Kopyalama" adımındaki yolla, hash doğrulamalı olarak yeniden kopyalanır.

Arşivdeki dosya yoksa ya da içeriği değişmişse kayıt **onarılamadı** olarak raporlanır ve dokunulmaz.

### Arayüz: `BackupWindow` (App, yeni)

Ana penceredeki **"Yedekle…"** düğmesi bu pencereyi açar. Pencere modal değildir ama aynı anda yalnız
bir tane açılır.

- **Arşiv klasörü:** ana penceredeki hedef klasörden dolar, değiştirilebilir.
- **Yedek klasörü:** Gözat ile seçilir ve uygulama ayarlarında hatırlanır.
- **"Tekrarları da yedekle (_Tekrarlar)"** kutusu, varsayılan kapalı.
- **Düğmeler:** Yedekle, Tam doğrula ve Onar. Onar yalnız son doğrulamada sorun bulunduysa etkindir.
  İş sürerken bu üç düğme kapanır, İptal açılır.
- **İlerleme:** çubuk, şu anki dosya, bayt bazında yüzde ve "1.240 / 9.800 dosya".
- **Özet metni**, örnek: "1.240 yeni · 3 değişmiş · 9.512 aynı · 2 arşivde yok · 0 hata · 4,2 GB
  kopyalandı". Doğrulamada: "9.800 tamam · 0 eksik · 1 bozuk". Sorunlu dosyalar altta bir listede
  gösterilir (göreli yol + neden).
- Liste bozuksa ya da yer yetmiyorsa açık bir uyarı gösterilir ve iş başlamaz.
- İş sürerken pencere kapatılırsa önce iptal sorulur.

### Komut satırı (Cli)

- `--backup <yedek kökü>`: arşiv kökü olarak mevcut `<kök>` argümanını kullanır ve `Run` çalıştırır.
  `--include-duplicates` seçeneği alır.
- `--verify-backup <yedek kökü>`: `Verify` çalıştırır, sorunlu dosyaları yazar. Sorun varsa çıkış
  kodu 1 olur.
- `--csv` verilmişse sonuçlar `…-yedek.csv` ya da `…-dogrulama.csv` dosyasına da yazılır.

## Testler (xUnit, geçici klasörlerle)

1. İlk yedek tüm dosyaları kopyalar. Liste hash'leri dosyaların SHA-256'sına eşittir ve yedekteki
   dosyaların zamanı kaynakla aynıdır.
2. İkinci çalıştırmada değişmemiş dosyalar "aynı" sayılır ve kopyalanan bayt 0'dır.
3. Arşivde içeriği değişen dosyanın eski hali `_EskiSurumler\<bugün>\<göreli yol>` altında durur,
   yenisi yerinde olur. Aynı gün ikinci değişiklikte ` (2)` eklenir.
4. Arşivden silinen dosya yedekte ve listede kalır, "arşivde yok" olarak sayılır.
5. `_Tekrarlar` varsayılan olarak atlanır, `IncludeDuplicates` açıkken kopyalanır.
6. Hash uyuşmazlığında (test kancasıyla yazım bozulur) dosya iki denemeden sonra hata olur, listeye
   girmez ve `.part` kalmaz.
7. Yedek kökünde kalmış `.part` dosyası bir sonraki çalıştırmada silinir.
8. Yer yetmiyorsa (test kancasıyla boş alan küçük verilir) hiçbir dosya kopyalanmaz.
9. İç içe kökler reddedilir.
10. İptal: belirli sayıda dosyadan sonra iptal edildiğinde liste o ana kadarki dosyaları içerir.
    Sonraki çalıştırma kalanları kopyalar.
11. `Verify` elle değiştirilmiş bir dosyayı bozuk, silinmiş bir dosyayı eksik olarak bulur.
12. `Repair` bozuk dosyayı `_Bozuk`'a taşır ve yeniden kopyalar; sonraki `Verify` temiz çıkar.
    Arşivdeki dosyası değişmiş kayıt onarılamadı olarak raporlanır.
13. Bozuk JSON listesi yedeği başlatmaz.

Boş alan ve yazım bozulması için `ArchiveBackup`'a iç (internal) test kancaları eklenir:
`Func<string, long> FreeSpace` ve kopyadan sonra çağrılan `Action<string>? AfterWrite`.
