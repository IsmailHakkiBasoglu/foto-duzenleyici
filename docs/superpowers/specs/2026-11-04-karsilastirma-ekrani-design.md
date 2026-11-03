# Benzerler için karşılaştırma ekranı — tasarım

Tarih: 2026-11-04 · Yol haritası: Aşama 3, "Kopya ve benzer grupları yan yana karşılaştırma ekranı"
(Aşama 2'deki "yan yana gösterilir, kullanıcı seçer" maddesini de tamamlar)

## Amaç

Benzerler sekmesindeki grupların karelerini yan yana görüp hangisinin kalacağına hızlıca karar
vermek. Gerçek arşivde 506 seri çekim grubu var (çoğu 2–3 kare, en büyüğü 14 kare). Bugün her
dosya bir liste satırı ve sağda tek önizleme var; karar vermek için satır satır tıklamak gerekiyor.

**Başarı ölçütü:** 506 grup fareye az gidip gelerek, klavyeyle gezilebilir; bir grupta karar
birkaç saniye sürer. Kullanıcı ara verip ertesi gün yeniden taradığında incelediği gruplar
"incelendi" görünür ve işaretleri korunur.

**Kullanıcının seçimleri (2026-11-04):**
- İki görünüm birden: Benzerler sekmesinde grupların şerit görünümü (göz gezdirme) ve bir gruba
  çift tıklayınca açılan tam ekran karşılaştırma.
- Kararlar uygulama kapanınca da hatırlanır.

**Kapsam dışı:** RAW + JPG eşlerini birlikte taşıma, bulanık/karanlık kare işaretleme (Aşama 2'nin
ayrı maddeleri, sonraki alt projeler); diske küçük resim önbelleği; Yinelenenler ve Plan
sekmelerinde küçük resim.

**Değişmeyen kurallar:** Hiçbir dosya otomatik taşınmaz. Her grupta en az bir kare kalır.
Kalsın işareti kaldırılan dosya Uygula'da `_Benzerler` klasörüne gider, Geri Al çalışır.
İşaret değişince mevcut plan geçersizleşir (bugünkü `SimilarChanged` davranışı).

## Yaklaşım

Ayrı, küçük parçalar. `MainWindow.xaml.cs` (424 satır) yalnızca bağlantı kodu alır; yeni
mantık kendi dosyalarında. MVVM kütüphanesi eklenmez (yeni bağımlılık, tüm pencereyi yeniden
düzenleme gerektirir); mevcut `INotifyPropertyChanged` satır modeli kullanılır.

## Bileşenler

### `ReviewStore` (Core, yeni)

Kararları `%LOCALAPPDATA%\FotoDuzenleyici\benzer-kararlar.json` dosyasında saklar. Fotoğraf ya da
küçük resim saklamaz.

- **Dosya kimliği:** `HashCache.Key(path)` (boyut + son yazma zamanı + ilk 64 KB'nin SHA-256'sı).
  Yoldan bağımsızdır; dosya taşınıp yeniden adlandırılsa da tanınır.
- **Saklanan:**
  - `Keep`: dosya kimliği → `true` / `false` (kullanıcının son işareti).
  - `Reviewed`: incelenmiş grupların imzaları. İmza = üye kimliklerinin sıralanıp birleştirilmiş
    hâlinin SHA-256'sı.
- **API:**
  - `ReviewStore(string path)` — yükler; dosya yoksa ya da bozuksa boş başlar (`HashCache` gibi).
  - `bool? GetKeep(string key)`, `void SetKeep(string key, bool keep)`
  - `static string Signature(IEnumerable<string> memberKeys)`
  - `bool IsReviewed(string signature)`, `void SetReviewed(string signature, bool reviewed)`
  - `void Save()` — geçici dosyaya yazar, sonra değiştirir (kesinti dosyayı bozmaz).
- **Geri yükleme kuralı** (çağıran tarafta, `SimilarGroupState` içinde):
  - Kaydı olan her üyeye kaydedilmiş işaret uygulanır; kaydı olmayan üye `true` başlar.
  - Bu uygulamadan sonra gruptaki bütün üyeler `false` olursa, öneri (★) varsa o, yoksa ilk üye
    `true` yapılır (en az bir kare kalır kuralı).
  - Grup, imzası `Reviewed` içindeyse incelendi sayılır. Üyeler değiştiyse (yeni kare eklendi,
    biri ayıklanıp `_Benzerler`'e gitti) imza değişir: grup incelenmedi görünür ama eski üyelerin
    işaretleri korunur.
- Kayıt, değişiklikten ~1 saniye sonra bir kez yapılır (art arda tıklamalar tek yazma) ve pencere
  kapanırken yapılır.

### `SimilarGroupState` ve `SimilarRow` (Core)

Core zaten `net8.0-windows` + `UseWPF` (görüntü çözme için), bu yüzden durum sınıfları Core'da
durur ve birim testiyle sınanır.

- `SimilarRow` bugün `MainWindow.xaml.cs` içinde; Core'a taşınır. İki ekleme: `Thumb`
  (`ImageSource?`, değişince bildirim) ve `StoreKey` (`string?`).
- `SimilarGroupState` (yeni): grup no, tür, öneri, üye satırları, `Reviewed`, imza. "En az bir kare
  kalsın" kuralı, "yalnız bu kalsın" işlemi ve geri yükleme kuralı burada; bugün `MainWindow`'daki
  `CanRelease` mantığı buraya taşınır.
- Şerit görünümü de karşılaştırma penceresi de aynı nesneleri kullanır; biri değişince diğeri
  bağlama (binding) ile güncellenir.

### `ThumbnailLoader` (App, yeni)

- Arka planda `ImageHasher.Decode(path, orientation, 256)` ile çözer; aynı anda en fazla 4 dosya.
- Bellekte son 600 küçük resmi tutar (en eski kullanılan atılır; ~60 MB).
- `Request(SimilarRow)` sıraya ekler; sonuç geldiğinde `row.Thumb` atanır. Ekrandan çıkan
  satırların bekleyen istekleri iptal edilir (hızlı kaydırmada boşa çözme olmaz).
- Çözülemeyen dosyada `Thumb` boş kalır, kartta "önizleme yok" yazar.

### Şerit görünümü (Benzerler sekmesi, `SimilarGrid` DataGrid'inin yerine)

- `ListBox` + `VirtualizingStackPanel` (sanallaştırma açık, `VirtualizationMode=Recycling`);
  her öğe bir grup.
- Satır: solda grup no, tür, kare sayısı ve "✓ incelendi"; sağda kareler 140 px kart olarak yan
  yana (yatay sığmazsa alt satıra kayar).
- Kart: küçük resim, altında dosya adı; kalan kare yeşil çerçeve, ayıklanacak kare soluk ve ✗;
  ★ önerisi köşede. Fareyle üzerine gelince tam yol, çözünürlük, boyut, çekim anı.
- Tek tıklama: Kalsın ↔ Ayıkla. Çift tıklama (ya da satır seçiliyken Enter): karşılaştırma
  penceresini o grupla açar.
- Üst çubuk: mevcut "Önerileri uygula", "Hepsini koru"; yeni "Yalnız incelenmemişler" süzgeci ve
  "İncelenen: 37 / 506" sayacı.
- Sağdaki tek önizleme paneli kalkar (karşılaştırma penceresi onun yerini alır).

### Karşılaştırma penceresi (`CompareWindow`, yeni)

- Ana pencerenin sahibi olduğu, ekranı kaplayan pencere; bir grubu gösterir.
- Kareler yan yana büyük; en fazla 4 sütun, fazlası alt satırlarda (14 karelik grup 4 satır).
- Her karenin altında: numara (1–9), dosya adı, çözünürlük, MB, çekim anı (saniye kesriyle);
  Kalsın/Ayıkla durumu çerçeve rengiyle.
- Başlık: "Grup 12 / 506 · Seri çekim · 3 kare · İncelendi".
- Açılınca kareler ekran boyutunda (~1200 px) çözülür; yakınlaşınca görünen karelerin tam
  çözünürlüklüsü arka planda çözülür, o zamana kadar ekran boyutlusu büyütülerek gösterilir.

| Tuş / fare | İşlev |
|---|---|
| 1–9 ya da karta tıklama | O kare Kalsın ↔ Ayıkla |
| Boşluk | Seçili (üzerine gelinen ya da son tıklanan) kare tek başına kalsın |
| Enter | Grubu incelendi yap, sonraki gruba geç |
| ← / → | Önceki / sonraki grup (süzgeç açıksa yalnız incelenmemişler arasında) |
| Fare tekerleği / Z | Bütün kareler fare imlecinin bulunduğu göreli noktaya birlikte yakınlaşır |
| 0 | Yakınlaştırmayı sıfırla |
| Esc | Kapat |

- Yakınlaştırma eşzamanlıdır: her karede aynı göreli nokta (ör. %40, %55) aynı oranda büyür;
  netlik kıyası için. Sürükleyerek kaydırma da hepsine birlikte uygulanır.
- Son kalan kare ayıklanmak istenirse işlem yapılmaz, başlıkta "Her grupta en az bir kare kalmalı"
  yazar.

## Veri akışı

1. Tara bitince `ShowSimilar` grupları kurar (bugünkü gibi), ardından arka planda üyelerin
   `HashCache.Key`'lerini hesaplar ve `ReviewStore`'dan işaretleri ve incelendi bilgisini
   geri yükler. Bitene kadar durum çubuğunda "Önceki kararlar yükleniyor…".
2. Her işaret değişikliği: `SimilarRow.Keep` → `ReviewStore.SetKeep` → gecikmeli kayıt →
   `SimilarChanged` (plan geçersiz).
3. Enter ile incelendi: `ReviewStore.SetReviewed(signature, true)`.
4. "Önerileri uygula" ve "Hepsini koru" işaretleri değiştirir ve kaydeder ama grupları incelendi
   yapmaz.
5. Plan / Uygula akışı değişmez: `BuildSimilarMap()` satırların `Keep` değerini okur.

## Hata durumları

- Karar dosyası yok / bozuk / okunamıyor: boş başlanır, durum çubuğunda bir kez bilgi verilir;
  uygulama çalışmaya devam eder.
- Karar dosyası yazılamıyor: durum çubuğunda uyarı; işaretler oturum boyunca geçerli kalır.
- Dosya bulunamıyor ya da çözülemiyor (küçük resim veya tam çözünürlük): kartta "önizleme yok";
  işaretleme yine yapılabilir.
- Anahtar hesaplanamayan dosya (okuma hatası): o dosyanın kararı saklanmaz, grup imzası
  hesaplanmaz (incelendi bilgisi o grup için tutulmaz).

## Testler

- `ReviewStore` birim testleri:
  - Kaydet → yeni nesneyle yükle: işaretler ve incelendi aynı.
  - Bozuk JSON ve olmayan dosya boş başlar.
  - İmza üye sırasından bağımsız; bir üye eklenince imza değişir.
  - Dosya taşınıp yeniden adlandırılınca (zaman damgası korunarak) anahtar aynı, karar bulunur.
- `SimilarGroupState` birim testleri:
  - Kaydedilmiş işaretlerin hepsi `false` ise ★ ya da ilk üye kalır.
  - Son kalan kare bırakılamaz; "yalnız bu kalsın" diğerlerini bırakır.
  - Üyeler değişince incelendi düşer, eski işaretler kalır.
- Elle deneme (gerçek arşiv, salt okunur hedef): 506 grupta şerit kaydırma akıcı; karşılaştırma
  penceresinde ← → Enter ile gezinme; yakınlaştırma eşzamanlı; uygulamayı kapatıp açınca
  incelendi ve işaretler geri geliyor.
