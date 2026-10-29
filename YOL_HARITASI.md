# Yol Haritası

Hedef: Foto Düzenleyici'yi tek seferlik bir taşıma aracından, tamamen bilgisayarda çalışan
kişisel bir **fotoğraf arşivi yöneticisine** dönüştürmek. Hiçbir fotoğraf dışarı gönderilmez.

Temel ilke: arayüzü güzelleştirmeden önce dosyaların asla kaybolmayacağından emin olmak.

Durum işaretleri: ✅ bitti · 🔄 sürüyor · ⬜ planlandı

## Aşama 1 — Güvenilirlik

- ✅ Uygula ve Geri Al'ı gerçek veride uçtan uca denemek (0.1.2)
- ✅ Tekrar çalıştırmaya uygun yapı: düzenli klasör tekrar işlenince hiçbir şey değişmez,
  elle eklenen dosyalar aynı düzende adlandırılır (0.1.2)
- ✅ Videolarda saat dilimi düzeltmesi (QuickTime tarihleri UTC tutar) (0.1.3)
- ✅ GitHub Actions: her gönderimde derleme ve test, sürüm etiketinde zip'i otomatik yayınlama (0.1.3)
- ✅ Kesintiye dayanıklılık: işlem yarıda kalırsa günlükten kaldığı yerden devam (0.1.3)
- ✅ Uygula / Geri Al için örnek dosyalarla otomatik entegrasyon testi (0.1.3)

## Aşama 2 — Akıllı ayıklama

- ✅ **Aynı EXIF parmak izi:** adı farklı ama kamera ve çekim anı aynı olan dosyaları
  "büyük olasılıkla aynı kare" olarak gruplamak (çözünürlük farklı olabilir: dışa aktarılmış kopyalar)
- ✅ **Benzer fotoğraflar (algısal hash):** WhatsApp'ın küçülttüğü kopyalar ve seri çekimler
- 🔄 Bu gruplar otomatik taşınmaz; yan yana gösterilir, hangisinin kalacağını kullanıcı seçer
  (seçim ve `_Benzerler` klasörü hazır; küçük resimli karşılaştırma Aşama 3'te)
- ⬜ RAW + JPG eşlerini her zaman birlikte taşımak
- ⬜ Bulanık veya çok karanlık kareleri işaretlemek (yerel hesaplama)

## Aşama 3 — Görsel arayüz

- ⬜ Plan tablosunun yanında küçük resim önizleme
- ⬜ Kopya ve benzer grupları yan yana karşılaştırma ekranı
- ⬜ Ay ay zaman çizelgesi; ay başına dosya sayısı ve boyut grafiği
- ⬜ Koyu tema, modern görünüm (WPF-UI / Fluent)

## Aşama 4 — Otomatik içe aktarma ve yedek

- ⬜ SD kart veya telefon takılınca "içe aktar" penceresi
- ⬜ Gelen kutusu klasörünü izleyip yeni dosyaları otomatik yerleştirme
- ⬜ Arşivi ikinci bir diske yedekleme ve yedeği hash ile doğrulama

## Aşama 5 — Arşiv kütüphanesi (uzun vade)

- ⬜ İsteğe bağlı katalog dosyası (hash → ad): kullanıcı dosyayı elle yeniden adlandırsa
  bile tanınır
- ⬜ Küçük bir SQLite veritabanıyla tarih, kamera, konum ve etikete göre arama
- ⬜ GPS bilgisinden harita görünümü
- ⬜ Yerel yapay zekâ ile yüz gruplama ve içerik etiketleri ("deniz", "doğum günü")

## Bilinen veri notları

- `18.10.2026 CANON 250D` klasörü, düzenlenmiş `100CANON_DUZENLI` ile birebir aynı
  içerikte (~28,7 GB). Bir sonraki çalıştırmada `_Tekrarlar`'a ayrılması beklenir.
- 258 videonun yalnızca biri (`MVI_3028`, iki kopyası var) UTC düzeltmesinden etkileniyor:
  19 Mart olarak adlandırılmış, gerçek çekim 20 Mart 00:45. Ay değiştiren video yok.
