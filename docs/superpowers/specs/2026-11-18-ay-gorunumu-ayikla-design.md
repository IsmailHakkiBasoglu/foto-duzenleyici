# Ay gÃ¶rÃ¼nÃ¼mÃ¼ ve hÄ±zlÄ± ayÄ±klama â€” tasarÄ±m

Tarih: 2026-11-18 Â· Durum: taslak (kullanÄ±cÄ± onayÄ± bekliyor)

## Niyet

KullanÄ±cÄ± Zaman Ã§izelgesinde dolu bir aya (Ã¶rn. "AÄŸustos 2026 Â· 140 foto") tÄ±klayÄ±nca o ayÄ±n iÃ§ine
girmek istiyor. Kendi sÃ¶zleriyle: **"o ay ne Ã§ektiÄŸimi hÄ±zlÄ±ca gÃ¶rÃ¼p hatÄ±rlamak istiyorum. belki hÄ±zlÄ±ca
silme yaparÄ±m ya da Ã§Ã¶p kutusuna atÄ±lacaklar listesi iÃ§in iÅŸaretleme yaparÄ±m. mobilde fotoÄŸraf kaydÄ±rarak
telefonda yer aÃ§an uygulamalar var ya onun gibi dÃ¼ÅŸÃ¼n."**

Daha bÃ¼yÃ¼k hedef: uygulamayÄ± Windows FotoÄŸraflar uygulamasÄ± ve Gezgin yerine kullanmak. Bu tasarÄ±m o hedefin
ilk parÃ§asÄ±. Hedefin tamamÄ± ayrÄ± bir yol haritasÄ± maddesidir ("FotoÄŸraflar uygulamasÄ± yerine").

## Hedefler

- Aya tÄ±klayÄ±nca, uygulamadan Ã§Ä±kmadan, o ayÄ±n fotoÄŸraflarÄ± kÃ¼Ã§Ã¼k resimlerle gÃ¶rÃ¼nÃ¼r.
- GÃ¶rÃ¼nÃ¼mde gezinmek hÄ±zlÄ± ve akÄ±cÄ± olur; 1.000 dosyalÄ± bir ayda da Ã¶yle kalÄ±r.
- KÃ¶tÃ¼ kareler hÄ±zlÄ±ca "atÄ±lsÄ±n" diye iÅŸaretlenir. Ä°ÅŸaretlenenler gÃ¶zden geÃ§irilir ve tek onayla Geri DÃ¶nÃ¼ÅŸÃ¼m
  Kutusu'na gÃ¶nderilir.
- Ä°ÅŸlem bitince, Zaman Ã§izelgesine dÃ¶nÃ¼ldÃ¼ÄŸÃ¼nde o ayÄ±n sayÄ±sÄ± gÃ¼ncel olur.

## Hedef dÄ±ÅŸÄ± (bu iÅŸ deÄŸil)

- Video oynatma, yakÄ±nlaÅŸtÄ±rma, dÃ¶ndÃ¼rme, dÃ¼zenleme, yeniden adlandÄ±rma, taÅŸÄ±ma.
- Birden Ã§ok ayÄ± aynÄ± anda ayÄ±klamak.
- AyÄ±klama kararlarÄ±nÄ± kalÄ±cÄ± saklamak. Liste yalnÄ±z o ay aÃ§Ä±kken yaÅŸar.
- "FotoÄŸraflar uygulamasÄ± yerine" hedefinin geri kalanÄ±.

## Anti-hedefler (Ã§alÄ±ÅŸsa bile baÅŸarÄ±sÄ±zlÄ±k sayÄ±lÄ±r)

- KullanÄ±cÄ±nÄ±n onayÄ± olmadan herhangi bir dosyanÄ±n gitmesi.
- Bir dosyanÄ±n sessizce, kalÄ±cÄ± olarak silinmesi. Bu, Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu olmayan sÃ¼rÃ¼cÃ¼leri de kapsar.
- Hedefte var olan bir dosyanÄ±n Ã¼zerine yazÄ±lmasÄ±.
- Ay gÃ¶rÃ¼nÃ¼mÃ¼nÃ¼n aÃ§Ä±lÄ±rken ya da kaydÄ±rÄ±lÄ±rken takÄ±lmasÄ±.
- AtÄ±lan dosyanÄ±n RAW eÅŸinin sÃ¼rpriz biÃ§imde gitmesi. KullanÄ±cÄ± bunu gÃ¶zden geÃ§irme ekranÄ±nda Ã¶nceden
  gÃ¶rmeli.

## KullanÄ±cÄ±nÄ±n verdiÄŸi kararlar

1. **Kapsam ve sÄ±ra:** Bu iÅŸ ay gÃ¶rÃ¼nÃ¼mÃ¼ ile hÄ±zlÄ± ayÄ±klamadÄ±r. "FotoÄŸraflar uygulamasÄ± yerine" hedefi yol
   haritasÄ±na ayrÄ± bir baÅŸlÄ±k olarak eklenir.
2. **AtÄ±lanlar Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu'na gider.** Geri getirmek Windows'un "Geri yÃ¼kle" iÅŸleviyle yapÄ±lÄ±r.
   Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu olmayan sÃ¼rÃ¼cÃ¼de dosyalar `_Ayiklananlar` klasÃ¶rÃ¼ne taÅŸÄ±nÄ±r ve kullanÄ±cÄ±ya bu
   aÃ§Ä±kÃ§a yazÄ±lÄ±r.
3. **BirleÅŸik gÃ¶rÃ¼nÃ¼m:** Ãœst Ã§ubukta iki konumlu bir dÃ¼ÄŸme bulunur: **â–¦ Izgara / ğŸ“… Takvim**.
   - **Izgara:** gÃ¼nlere bÃ¶lÃ¼nmÃ¼ÅŸ kÃ¼Ã§Ã¼k resim Ä±zgarasÄ±. Her gÃ¼nÃ¼n bir baÅŸlÄ±ÄŸÄ± vardÄ±r, Ã¶rn.
     "15 AÄŸustos Cumartesi Â· 38".
   - **Takvim:** ay haftalara bÃ¶lÃ¼nÃ¼r ve her gÃ¼n bir kutu olur. Kutuda o gÃ¼nÃ¼n birkaÃ§ kÃ¼Ã§Ã¼k resmi ve
     "+N" gÃ¶sterilir. Bir gÃ¼ne tÄ±klamak Izgara'ya geÃ§irir ve o gÃ¼nÃ¼n baÅŸÄ±na kaydÄ±rÄ±r.
   - Son seÃ§ilen gÃ¶rÃ¼nÃ¼m hatÄ±rlanÄ±r. Ä°lk kullanÄ±mda Izgara aÃ§Ä±lÄ±r *(YapÄ±cÄ± kararÄ±, kullanÄ±cÄ± gÃ¶rdÃ¼)*.
4. **AyÄ±kla (tek tek bÃ¼yÃ¼k gÃ¶rÃ¼nÃ¼m), tuÅŸ dÃ¼zeni B:**

   | Hareket | Ne yapar |
   |---|---|
   | â† / â†’ | Ã–nceki / sonraki fotoÄŸraf; karar vermez |
   | Delete | "AtÄ±lsÄ±n" iÅŸaretini koyar ya da kaldÄ±rÄ±r, sonrakine geÃ§er |
   | FotoÄŸrafÄ± sola sÃ¼rÃ¼kle | AtÄ±lsÄ±n, sonrakine geÃ§ |
   | FotoÄŸrafÄ± saÄŸa sÃ¼rÃ¼kle | KalsÄ±n (iÅŸaret varsa kalkar), sonrakine geÃ§ |
   | Esc | Ay gÃ¶rÃ¼nÃ¼mÃ¼ne dÃ¶n |

   Ãœstte tarih, saat, dosya adÄ±, boyut ve "58 / 140" sÄ±rasÄ± gÃ¶rÃ¼nÃ¼r. Altta kararlarÄ± âœ• iÅŸaretiyle gÃ¶steren
   kÃ¼Ã§Ã¼k resim ÅŸeridi bulunur.
5. **RAW+JPG eÅŸleri:** Ay gÃ¶rÃ¼nÃ¼mÃ¼nde eÅŸin yalnÄ±z JPG'si gÃ¶sterilir. JPG "atÄ±lsÄ±n" iÅŸaretlenirse RAW eÅŸi
   de onunla gider. GÃ¶zden geÃ§irme ekranÄ±nda o resmin Ã¼stÃ¼nde "+RAW" etiketi bulunur ve toplam boyuta RAW da
   dahildir.
   - **Ä°stisna (kullanÄ±cÄ± isteÄŸi):** Bazen kullanÄ±cÄ± kÃ¶tÃ¼ JPG'yi atÄ±p RAW'Ä± saklamak isteyebilir. RAW eÅŸi
     olan bir fotoÄŸraf AyÄ±kla'da aÃ§Ä±kken bir **"RAW'Ä± tut"** dÃ¼ÄŸmesi gÃ¶rÃ¼nÃ¼r (anlamÄ±: "bu fotoÄŸrafÄ±n
     RAW'Ä±nÄ± dahil etme"). DÃ¼ÄŸmeye basÄ±lÄ±rsa o fotoÄŸraf atÄ±ldÄ±ÄŸÄ±nda RAW yerinde kalÄ±r.
   - SeÃ§im fotoÄŸraf baÅŸÄ±nadÄ±r ve yalnÄ±z o ay aÃ§Ä±kken saklanÄ±r, tÄ±pkÄ± iÅŸaretler gibi.
   - GÃ¶zden geÃ§ir ekranÄ±nda bu fotoÄŸrafÄ±n etiketi "+RAW" yerine "RAW kalÄ±r" olur ve toplam boyuta RAW
     katÄ±lmaz.
   - GÃ¶zden geÃ§ir'de etikete tÄ±klamak da seÃ§imi deÄŸiÅŸtirir *(YapÄ±cÄ± kararÄ±)*.
6. **YaklaÅŸÄ±m 1:** Aya girerken o ayÄ±n dosyalarÄ± yeniden okunur. AyrÄ±ntÄ±sÄ± aÅŸaÄŸÄ±daki YaklaÅŸÄ±mlar
   bÃ¶lÃ¼mÃ¼nde.

## YaklaÅŸÄ±mlar

| | YaklaÅŸÄ±m | ArtÄ± | Eksi |
|---|---|---|---|
| **1 (seÃ§ildi)** | Aya girerken o ayÄ±n klasÃ¶rleri okunur: gÃ¼n, saat, yÃ¶n, RAW+JPG eÅŸleri | Liste her zaman gÃ¼ncel olur; Gezgin'de yapÄ±lan deÄŸiÅŸiklikleri gÃ¶rÃ¼r. SayÄ±m deÄŸiÅŸmez. | Aya giriÅŸte kÄ±sa bir bekleme olur (140 dosyada yaklaÅŸÄ±k 1 sn). |
| 2 | Zaman Ã§izelgesi sayarken her dosyanÄ±n yolu ve gÃ¼nÃ¼ bellekte tutulur | Aya giriÅŸ anÄ±nda olur. | "KlasÃ¶r adlarÄ±ndan" yÃ¶ntemi EXIF okumadÄ±ÄŸÄ± iÃ§in gÃ¼n bilgisi zaten yoktur. Liste sayÄ±m anÄ±nda donar. |
| 3 | AyÄ±kla gÃ¶rÃ¼nÃ¼mÃ¼ ayrÄ±, tam ekran bir pencere olur | Ä°kinci ekrana taÅŸÄ±nabilir. | Bu iÅŸ iÃ§in fazla bÃ¼yÃ¼k. "FotoÄŸraflar yerine" aÅŸamasÄ±nda yeniden dÃ¼ÅŸÃ¼nÃ¼lecek. |

## Ekranlar ve akÄ±ÅŸ

```
Zaman Ã§izelgesi â”€â”€(dolu aya tÄ±kla)â”€â”€â–º Ay gÃ¶rÃ¼nÃ¼mÃ¼ (Izgara | Takvim)
                                        â”‚  Ã§ift tÄ±k / "AyÄ±kla â–¶"
                                        â–¼
                                      AyÄ±kla (bÃ¼yÃ¼k, tek tek) â”€â”€Escâ”€â”€â–º Ay gÃ¶rÃ¼nÃ¼mÃ¼
                                        â”‚
            alt ÅŸerit "GÃ¶zden geÃ§ir" â”€â”€â–º  GÃ¶zden geÃ§ir â”€â”€"N dosyayÄ± Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu'na at"â”€â”€â–º Ay gÃ¶rÃ¼nÃ¼mÃ¼ (atÄ±lanlar yok)
Ay gÃ¶rÃ¼nÃ¼mÃ¼ â”€â”€"â† Zaman Ã§izelgesi"â”€â”€â–º Zaman Ã§izelgesi (tablo aynÄ± yerde, sayÄ±lar gÃ¼ncel)
```

EkranlarÄ±n hepsi aynÄ± pencerede, bugÃ¼nkÃ¼ sayfalar gibi birbirinin yerine geÃ§er.

### Ay gÃ¶rÃ¼nÃ¼mÃ¼

- **Ãœst Ã§ubuk:** "â† Zaman Ã§izelgesi", ay adÄ±, dosya sayÄ±sÄ± ve toplam boyut, â–¦/ğŸ“… dÃ¼ÄŸmesi, "AyÄ±kla â–¶" ve
  bir â‹¯ menÃ¼sÃ¼. MenÃ¼de "Gezgin'de aÃ§" bulunur. AyÄ±n dosyalarÄ± birden Ã§ok klasÃ¶rdeyse bu seÃ§enek bir alt
  menÃ¼de o klasÃ¶rleri listeler.
- **Alt ÅŸerit:** yalnÄ±z iÅŸaretli dosya varken gÃ¶rÃ¼nÃ¼r. Ä°Ã§inde "ğŸ—‘ AtÄ±lacaklar: N Â· boyut",
  "GÃ¶zden geÃ§ir" ve "Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu'na at" bulunur. "Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu'na at" dÃ¼ÄŸmesi Ã¶nce GÃ¶zden
  geÃ§ir ekranÄ±nÄ± aÃ§ar; dosyalar doÄŸrudan gitmez.
- **IÅŸaretleme:** Bir kÃ¼Ã§Ã¼k resim seÃ§iliyken Delete tuÅŸu ya da saÄŸ tÄ±k menÃ¼sÃ¼ndeki "AtÄ±lsÄ±n" kullanÄ±lÄ±r.
  Ä°ÅŸaretli resim soluk gÃ¶rÃ¼nÃ¼r ve kÃ¶ÅŸesinde kÄ±rmÄ±zÄ± âœ• bulunur. AynÄ± iÅŸlem iÅŸareti geri kaldÄ±rÄ±r.
- **YÃ¼kleme:** Okuma sÃ¼rerken bir ilerleme gÃ¶stergesi gÃ¶rÃ¼nÃ¼r. Okuma bir Durdur dÃ¼ÄŸmesiyle ya da geri
  dÃ¶nÃ¼lerek iptal edilebilir.
- **SÃ¼zgeÃ§:** Hangi tÃ¼rlerin gÃ¶sterileceÄŸini Zaman Ã§izelgesinin sÃ¼zgeci belirler (Foto / Raw / Video / Hepsi).
  - JPG'si gÃ¶rÃ¼nen bir RAW ayrÄ±ca gÃ¶sterilmez.
  - "Raw" sÃ¼zgecinde yalnÄ±z RAW dosyalar gÃ¶rÃ¼nÃ¼r. Bunlar iÅŸaretlenirse JPG eÅŸi gitmez, Ã§Ã¼nkÃ¼ 5. karar
    yalnÄ±z JPG'den RAW'a doÄŸru iÅŸler *(YapÄ±cÄ± kararÄ±)*.

### GÃ¶zden geÃ§ir

- Ä°ÅŸaretli dosyalar kÃ¼Ã§Ã¼k resim olarak listelenir. Bir resme tÄ±klamak onu listeden Ã§Ä±karÄ±r.
- Toplam dosya sayÄ±sÄ± ve boyut, RAW eÅŸleri dahil gÃ¶sterilir.
- AÃ§Ä±klama metni: "Dosyalar Windows'un Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu'na gider; oradan 'Geri yÃ¼kle' ile
  dÃ¶nebilirsin. Disk yeri, Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu'nu boÅŸaltÄ±nca aÃ§Ä±lÄ±r."
- SÃ¼rÃ¼cÃ¼de Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu yoksa sarÄ± bir uyarÄ± Ã§Ä±kar ve dÃ¼ÄŸmenin metni de deÄŸiÅŸir:
  "N dosyayÄ± _Ayiklananlar'a taÅŸÄ±".
- "VazgeÃ§" dÃ¼ÄŸmesi listeye dokunmadan geri dÃ¶ner.

## Kurallar

- **Okunacak dosyalar:**
  - *KlasÃ¶r adlarÄ±ndan* yÃ¶nteminde, hÃ¼crenin klasÃ¶rlerinin iÃ§indekilerin hepsi okunur.
  - *Ã‡ekim tarihinden* yÃ¶nteminde hÃ¼crenin klasÃ¶rleri okunur, ama yalnÄ±z Ã§ekim tarihi o yÄ±l-aya dÃ¼ÅŸen
    dosyalar alÄ±nÄ±r.
  - Tarih okuma kurallarÄ± uygulamanÄ±n her yerinde kullanÄ±lanlarla aynÄ±dÄ±r: EXIF, yoksa dosya adÄ±, yoksa
    dosya zamanÄ±.
  - Zaman Ã§izelgesinin dÄ±ÅŸarÄ±da bÄ±raktÄ±ÄŸÄ± Ã¶zel klasÃ¶rler burada da atlanÄ±r.
- **SÄ±ralama ve gruplama:** Dosyalar Ã§ekim zamanÄ±na gÃ¶re sÄ±ralanÄ±r, gÃ¼ne gÃ¶re gruplanÄ±r.
- **KlasÃ¶r adÄ± ile tarih uyuÅŸmazlÄ±ÄŸÄ±:** *KlasÃ¶r adlarÄ±ndan* yÃ¶nteminde bir dosyanÄ±n Ã§ekim tarihi baÅŸka bir
  aya dÃ¼ÅŸebilir (Ã¶rn. elle konmuÅŸ bir dosya). BÃ¶yle bir dosya yine gÃ¶sterilir, gerÃ§ek gÃ¼nÃ¼nÃ¼n altÄ±nda.
  - O gÃ¼n o ayda deÄŸilse ayÄ±n sonunda "BaÅŸka aydan" baÅŸlÄ±klÄ± bir grupta toplanÄ±r *(YapÄ±cÄ± kararÄ±)*.
- **Atma iÅŸlemi:**
  - Her dosya iÃ§in Ã¶nce Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu'nun bulunduÄŸu doÄŸrulanÄ±r, sonra dosya gÃ¶nderilir.
  - Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu olmayan sÃ¼rÃ¼cÃ¼lerde dosya `_Ayiklananlar` klasÃ¶rÃ¼ne taÅŸÄ±nÄ±r. Kapsanan sÃ¼rÃ¼cÃ¼ler:
    Ã§Ä±karÄ±labilir sÃ¼rÃ¼cÃ¼ler (USB bellek, SD kart) ve aÄŸ sÃ¼rÃ¼cÃ¼leri.
    - TaÅŸÄ±ma Temizle'nin taÅŸÄ±ma koduyla aynÄ± gÃ¼venceleri taÅŸÄ±r: Ã¼zerine yazmaz ve gÃ¼nlÃ¼k tutar.
    - BÃ¶ylece uygulamanÄ±n Geri Al'Ä± ile geri alÄ±nabilir.
  - KalÄ±cÄ± silme hiÃ§bir koÅŸulda yoktur.
- **Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu var ama dosyayÄ± almÄ±yor** *(YapÄ±cÄ± kararÄ±)*:
  - Kapsanan durumlar: kutu kapatÄ±lmÄ±ÅŸ ya da 0 MB'a ayarlanmÄ±ÅŸ, ya da dosya kutuda kalan yerden bÃ¼yÃ¼k
    (Ã§oÄŸu zaman bÃ¼yÃ¼k bir video).
  - BugÃ¼nkÃ¼ `Executor.RecycleBin` Ã§aÄŸrÄ±sÄ± Windows'un "kalÄ±cÄ± silinsin mi?" sorusunu bastÄ±rÄ±yor. Bu yÃ¼zden
    bu yol kullanÄ±lmaz.
  - GÃ¶nderimden Ã¶nce her dosya iÃ§in kutunun dosyayÄ± alacaÄŸÄ± doÄŸrulanÄ±r. Alamayacaksa dosya, kutusu olmayan
    sÃ¼rÃ¼cÃ¼deki gibi `_Ayiklananlar`'a taÅŸÄ±nÄ±r.
  - GÃ¶zden geÃ§ir ekranÄ± bunu Ã¶nceden hesaplar. KarÄ±ÅŸÄ±k bir toplu iÅŸte dÃ¼ÄŸme ÅŸÃ¶yle yazar: "4 dosyayÄ± Geri
    DÃ¶nÃ¼ÅŸÃ¼m Kutusu'na at, 2 dosyayÄ± _Ayiklananlar'a taÅŸÄ±". TaÅŸÄ±nacak dosyalarÄ±n Ã¼stÃ¼nde ayrÄ±ca bir etiket
    bulunur.
  - GÃ¶nderim sÄ±rasÄ±nda yine de beklenmedik bir kalÄ±cÄ± silme sinyali gelirse o dosya atlanÄ±r ve "atÄ±lamadÄ±"
    sayÄ±lÄ±r.
- **`_Ayiklananlar`'Ä±n yeri** *(YapÄ±cÄ± kararÄ±)*:
  - Temizle'deki gibi Zaman Ã§izelgesinin kÃ¶kÃ¼nÃ¼n altÄ±nda, `_Ayiklananlar\Ayikla\<kÃ¶ke gÃ¶re gÃ¶reli yol>`.
  - Dosya kÃ¶kle aynÄ± birimde deÄŸilse (Ã¶rn. baÄŸlanmÄ±ÅŸ bir sÃ¼rÃ¼cÃ¼), `_Ayiklananlar` o dosyanÄ±n birimindeki en
    Ã¼st kÃ¶k altÄ± klasÃ¶re konur.
  - TaÅŸÄ±nan dosyalar Zaman Ã§izelgesi alt satÄ±rÄ±ndaki `_Ayiklananlar: N dosya` sayÄ±sÄ±na eklenir.
- **GÃ¼nlÃ¼k** *(YapÄ±cÄ± kararÄ±)*:
  - YalnÄ±z Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu'na gÃ¶nderilen bir iÅŸlem iÃ§in `_Ayiklananlar` klasÃ¶rÃ¼ oluÅŸturulmaz. GÃ¼nlÃ¼k
    uygulamanÄ±n kendi veri klasÃ¶rÃ¼ne, yalnÄ±z kayÄ±t amacÄ±yla yazÄ±lÄ±r. Bu iÅŸlemi yarÄ±da kalÄ±nan yerden
    sÃ¼rdÃ¼rme ve uygulamanÄ±n Geri Al'Ä± yoktur; geri getirme Windows'un "Geri yÃ¼kle"siyle yapÄ±lÄ±r.
  - TaÅŸÄ±ma iÃ§eren iÅŸlemlerde gÃ¼nlÃ¼k, Temizle'deki gibi `_Ayiklananlar\_Gunluk` iÃ§ine yazÄ±lÄ±r. BÃ¶ylece Geri
    Al ve yarÄ±m iÅŸlem denetimi bugÃ¼nkÃ¼ gibi Ã§alÄ±ÅŸÄ±r.
  - Geri Al, Geri DÃ¶nÃ¼ÅŸÃ¼m Kutusu'na gidenleri atlar, Ã§Ã¼nkÃ¼ onlarÄ±n hedefi yoktur.
- **Hatalar:** Bir dosya kilitliyse ya da yerinde yoksa diÄŸerleri atÄ±lmaya devam eder. Sonunda
  "N dosya atÄ±ldÄ±, M dosya atÄ±lamadÄ±" denir ve atÄ±lamayanlar iÅŸaretli kalÄ±r.
- **KayÄ±t:** Ä°ÅŸlemin bir gÃ¼nlÃ¼k kaydÄ±, mevcut gÃ¼nlÃ¼k biÃ§imiyle yazÄ±lÄ±r. BÃ¶ylece neyin ne zaman atÄ±ldÄ±ÄŸÄ±
  bilinir.
- **Ä°ÅŸaretliyken Ã§Ä±kÄ±ÅŸ:** Ä°ÅŸaretli dosya varken ay gÃ¶rÃ¼nÃ¼mÃ¼nden Ã§Ä±kmak ya da uygulamayÄ± kapatmak ÅŸunu
  sorar: "5 dosya atÄ±lmak Ã¼zere iÅŸaretli. BÄ±rakÄ±p Ã§Ä±kÄ±lsÄ±n mÄ±?" Ä°ÅŸaretler kaydedilmez.
- **SayÄ±larÄ±n gÃ¼ncellenmesi:** Zaman Ã§izelgesine dÃ¶nÃ¼ldÃ¼ÄŸÃ¼nde atÄ±lan dosyalar o ayÄ±n sayÄ±sÄ±ndan ve
  boyutundan dÃ¼ÅŸÃ¼lÃ¼r, tablo ile Ã¶zet yeniden kurulur. Tam yeniden sayÄ±m yapÄ±lmaz.

## KÄ±sÄ±tlar

- Teknoloji bugÃ¼nkÃ¼yle aynÄ± kalÄ±r: .NET 8, WPF, WPF-UI, CommunityToolkit.Mvvm. Ekran mantÄ±ÄŸÄ± testli
  `FotoDuzenleyici.ViewModels` projesinde durur.
- KÃ¼Ã§Ã¼k resimler mevcut `ThumbnailLoader` ile yÃ¼klenir; bunun iÃ§in loader genelleÅŸtirilir.
  - AynÄ± anda en fazla 4 dosya Ã§Ã¶zÃ¼lÃ¼r, bellekte en Ã§ok 600 sonuÃ§ tutulur.
  - GÃ¶rÃ¼nmeyen Ã¶ÄŸenin isteÄŸi iptal edilir.
  - Izgara sanal kaydÄ±rma kullanÄ±r: yalnÄ±z gÃ¶rÃ¼nen satÄ±rlar oluÅŸturulur.
- AyÄ±kla'daki bÃ¼yÃ¼k resim ekran boyutunda Ã§Ã¶zÃ¼lÃ¼r. Bir sonraki resim Ã¶nceden yÃ¼klenir.
- Uygulama internete baÄŸlanmaz. Yeni bir paket eklenmez.

## YapÄ±cÄ±ya bÄ±rakÄ±lanlar

- Videolar:
  - KÃ¼Ã§Ã¼k resim Ã§Ä±karÄ±labiliyorsa gÃ¶sterilir, yoksa bir video simgesi gÃ¶rÃ¼nÃ¼r.
  - AyÄ±kla'da videoyu oynatmak yerine "VarsayÄ±lan uygulamada aÃ§" dÃ¼ÄŸmesi bulunur.
- Tarihi dosya adÄ±ndan ya da dosya zamanÄ±ndan tahmin edilen fotoÄŸraflarÄ±n Ã¼stÃ¼nde kÃ¼Ã§Ã¼k bir iÅŸaret olup
  olmayacaÄŸÄ±.
- Takvim kutusunda kaÃ§ kÃ¼Ã§Ã¼k resim gÃ¶sterileceÄŸi.
- YÃ¼kleme sÄ±rasÄ±ndaki ilerleme gÃ¶stergesinin biÃ§imi.
- SÃ¼rÃ¼kleme eÅŸiÄŸi ve animasyonu.
