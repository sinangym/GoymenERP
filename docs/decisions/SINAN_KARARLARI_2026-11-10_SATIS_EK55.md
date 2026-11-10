# Sinan Kararları — 2026-11-10 (Satış: API-34 Excel'de resmî KDV/tevkifat ayrı sütun, UBL-TR profilinin kesinleşmede sabitlenmesi, risk istisnasının Satış tarafı, üç satış izninin rol ataması)

Bu belge önceki karar dosyalarını silmez; SAT-D279…SAT-D282'yi ekler. SAT-D102 (EK15), SAT-D153…SAT-D162 (EK21),
SAT-D179 (EK25), SAT-D188 (EK29), SAT-D202 (EK33), SAT-D209 (EK35), SAT-D236 (EK47), SAT-D241 ve SAT-D244 (EK48),
SAT-D248 ve SAT-D250 (EK49), SAT-D276…SAT-D278 (EK54), DEC-SAL-002, DEC-SAL-006 ve DEC-INT-001 geçerlidir; bu belge
onların metnini değiştirmez (bkz. "Bilinçli değişiklikler").

**Kaynak:**

- Sinan'ın 10 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine
  önerili seçenekli sorularla verdiği dört cevap; dördünde de önerilen seçenek seçildi. Cevaplar aynı gün Issue #66
  [yorum 6771235622](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6771235622)'ye yazıldı. Aşağıdaki
  "Cevap" sütunu o yorumun metnidir (aynen). Soruların seçilmeyen seçenekleri ve seçenek açıklamaları bu belgeyi yazan
  ajana iletilmediği için yazılmadı (uydurulmadı); "Kayda geçen anlam" sütunu koordinatörün ilettiği anlamdır.
  Soru 4'ün cevabındaki "rol tanımı ayrı PR'la yapılır" cümlesi, koordinatörün bildirdiğine göre Sinan'ın seçtiği
  seçeneğin açıklamasındaki "Rol tanımı PR'ı açılır" ifadesinden gelir.

Cevaplar #66 yorumu 6771235622'de zaten kayıtlıdır; bu belge Issue #66'ya ayrıca yalnız merge makbuzunu yazar, yeni
karar yazmaz.

## Soru ve cevaplar

| # | Soru | Cevap (aynen, #66 yorumu 6771235622) | Kayda geçen anlam |
|---|---|---|---|
| 1 | API-34 Excel dışa aktarımında resmî belgenin KDV/tevkifat tutarı nasıl gösterilsin? | "Fatura Excel dışa aktarımı resmî KDV/tevkifat: **ayrı sütun** — hesaplanan tutar kalır, eşleşmiş resmî belgenin KDV/tevkifat tutarları yanına ayrı sütunlarda yazılır." | ERP'nin hesapladığı KDV ve tevkifat sütunları kalır; eşleşmiş resmî belgenin KDV ve tevkifat tutarları yanlarına **ayrı sütunlar** olarak eklenir; fark görünür. |
| 2 | API-34 UBL-TR profili (SAT-D278) hangi anın değeriyle yazılsın? | "UBL-TR profili: **fatura kesinleşirken sabitlenir**; sonraki her dışa aktarım aynı profili taşır." | Profil fatura kesinleşirken belirlenir ve faturada saklanır; sonraki her dışa aktarım aynı profili taşır. EK54'ün açık bıraktığı "fatura anı / dışa aktarma anı" sorusunu kapatır. |
| 3 | Risk istisnası (API-07, SAT-D153…D162) ne zaman yapılsın? | "Risk istisnası (API-07, SAT-D153…D162): **Satış tarafı şimdi yapılır** (talep/onay/geçerlilik kaydı + okuma portu); Lojistik sevk kapısı başlayınca bu kaydı okur." | Satış tarafı (talep, onay, geçerlilik kaydı ve sürümlü okuma portu) şimdi yapılır; Lojistik sevk kapısı Lojistik başlayınca bu kaydı okur. Bu adımda yeni Lojistik bağlantısı açılmaz (SAT-D209 aynen). |
| 4 | Üç satış izni hangi rollere verilsin? | "İzinler: `sales.invoices.export` (Muhasebe, Muhasebe Müdürü, GM, Admin), `sales.orders.margin.read` ve `sales.orders.source-plan.write` (SAT-D179: Satış Destek, Satış Uzmanı, Müdür, Admin) **karar dosyalarındaki rollere verilsin**. Bu cevap grant insan kapısı için somut kullanıcı kararıdır; rol tanımı ayrı PR'la yapılır." | Üç izin karar dosyalarındaki rollere verilir (SAT-D276, SAT-D250, SAT-D179/SAT-D188). Bu cevap grant insan kapısı için somut kullanıcı kararıdır. Ayrı rol tanımı adımının biçimi 2026-11-10'da ayrıca cevaplandı: Ayarlar ekranı (bkz. SAT-D282 (4); #66 6772460073). `margin.read` dar okumayla SAT-D250 kümesini izler (bkz. SAT-D282 (5)). |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D279 | API-34 Excel'de resmî KDV/tevkifat | (1) API-34 Excel dışa aktarmasında ERP'nin **hesapladığı KDV ve tevkifat** sütunları **kalır**. (2) Faturanın eşleştiği resmî belgenin (API-35, SAT-D241) **KDV ve tevkifat tutarları** bu sütunların **yanına ayrı sütunlar** olarak yazılır; hesaplanan tutar resmî tutarla değiştirilmez, ikisi yan yana görünür ve fark okunur. (3) SAT-D244 (tarih aralığı, yalnız kesinleşmiş, toplu), SAT-D248 (geniş sütun seti, sütun ad/sırasının yapılandırılabilir kalması) ve SAT-D236 (3) "yalnız dosya üretir" aynen geçerlidir. Bu karar yeni hesap veya tolerans getirmez; değerler kayıtlı tutarlardan okunur | API-34 (Excel üreticisi, sözleşme ve testler); DEC-SAL-006 |
| SAT-D280 | API-34 UBL-TR profilinin anı | (1) Bir faturanın UBL-TR profili (`TEMELFATURA` / `TICARIFATURA`, SAT-D278) **fatura kesinleşirken sabitlenir ve faturada saklanır**. (2) Sonraki **her** dışa aktarım aynı saklı profili taşır; müşterinin e-Fatura senaryosu sonradan değişse de kesinleşmiş faturanın profili değişmez. (3) EK54 "Sınırlar"daki "senaryonun hangi andaki değerinin (fatura anı / dışa aktarma anı) kullanılacağı" sorusu bu kararla kapanır. (4) Profil değerinin **kaynağı** (müşterinin Cari e-belge ayarı / SAT-D102 senaryosu, Cari okuma portu) EK54'teki gibi kalır; o kısmın açık noktaları EK54'te yazıldığı gibi açıktır. Serbest profil parametresi açılmaz. (5) Fatura tipi, VKN-adres kaynağı ve zip paketleme `NEEDS-SINAN` olarak açık kalır; UBL-TR üretimi bunlar karara bağlanmadan etkinleştirilmez | API-34 UBL-TR dilimi, fatura kesinleştirme (API-36 kesinleşme kaydı); DEC-INT-001, DEC-SAL-006 |
| SAT-D281 | Risk istisnasının (API-07) Satış tarafı | (1) API-07 risk istisnasının **Satış tarafı şimdi yapılır**: talep, onay ve geçerlilik kaydı (SAT-D153…SAT-D162 kuralları aynen: tek sevk, onaylandığı iş tarihinin sonuna kadar geçerlilik, isteyen kendi talebini onaylayamaz, sipariş revizyonu açmaz) ve bu kaydı dışarıya veren **sürümlü bir okuma portu**. (2) Lojistik sevk kapısı, **Lojistik geliştirmesi başlayınca** bu okuma portunu tüketir. (3) Bu adımda **yeni Lojistik bağlantısı, tablosu veya ekranı açılmaz**; SAT-D209 aynen geçerlidir ve sevk kapısının tüketimi "ertelendi: Lojistik başlayınca" olarak işaretlenir. (4) İzin adları API-07 planındaki `sales.orders.risk-exception.request` / `.approve`'dur; uygulama PR'ında manifest'e eklenir ve hiçbir role atanmaz (rol setleri SAT-D153, SAT-D159, SAT-D188'dedir) | API-07, MOD-01, Satış okuma portu (cross-module contract: K3), persona kartı; DEC-SAL-002 |
| SAT-D282 | Üç satış izninin rol ataması | (1) `sales.invoices.export` **Muhasebe, Muhasebe Müdürü, Genel Müdür ve Admin** rollerine verilir (SAT-D276 (1) kümesi). (2) `sales.orders.margin.read` **Genel Müdür ve Admin** rollerine verilir (SAT-D276 (2), SAT-D250 kümesi). (3) `sales.orders.source-plan.write` **Satış Destek, Satış Uzmanı, Müdür ve Admin** rollerine verilir (SAT-D179); SAT-D188 gereği "Müdür" Satış Müdürü ve Genel Müdür'dür. (4) Bu, Sinan'ın grant insan kapısı için **somut kullanıcı kararıdır**. Bu PR grant veya seed açmaz. Cevaptaki "rol tanımı ayrı PR'la yapılır" ifadesi Sinan'ın seçtiği seçeneğin açıklamasından ("Rol tanımı PR'ı açılır") gelir; ayrı rol tanımı adımının **biçimi** (kurulumda Ayarlar rol ekranından atama mı, kodda bir rol tanımı PR'ı mı; #66 6771667746) için Sinan 2026-11-10'da "Hayır, Ayarlar ekranı" cevabını verdi (#66 6772460073; 6771735647'nin yerine geçer): kodda rol tanımı veya grant yoktur; EK54 SAT-D276 (3) yolu geçerlidir — atama kurulumda Ayarlar rol ekranından yapılır; iş rolleri ve satış izinleri için kodda seed/grant yoktur (sistem rolleri ayrıca seed'lidir). Grant insan kapısıdır; Ayarlar rol ekranı gelene kadar E4 / `OPEN-013` gereği bekler. Bu belge manifest değişikliği de yapmaz. (5) `sales.orders.margin.read`'in rol seti için bir okuma belirsizliği vardır: #66 yorumundaki parantez ("SAT-D179: Satış Destek, Satış Uzmanı, Müdür, Admin") `margin.read`'i de SAT-D179 rollerine bağlıyor gibi okunabilir; seçilen seçenek "karar dosyasındaki rollere" dediği için `margin.read` SAT-D250/SAT-D276 (2) kümesini (Genel Müdür + Admin) izler — dar okuma budur | Ayarlar → "Kullanıcılar ve yetkiler" (kurulum), `docs/operations/CANLI_ORTAM_KURULUM_REHBERI_v1.md` §4.6, `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md`; DEC-SAL-006 |

Uygulama ayrıntısı (geri alınabilir):

- **SAT-D279:** Faturanın kayıtlı bir resmî eşleşme tutarı yoksa (resmî belge eşleşmemiş veya eşleşmede KDV/tevkifat
  tutarı kaydedilmemişse) resmî KDV ve resmî tevkifat sütunları **boş** bırakılır; sıfır yazılmaz, hesaplanan
  tutarla doldurulmaz. Sütun adları ve konumu SAT-D248'in yapılandırılabilir sütun setine girer; varsayılan konum
  hesaplanan sütunların hemen yanıdır.
- **SAT-D280:** Profil kesinleşme kaydıyla aynı işlemde saklanır; saklı profil yoksa (ör. bu karardan önce
  kesinleşmiş test faturası) UBL-TR üretimi o fatura için reddedilir, dışa aktarma anındaki senaryodan tahmin
  edilmez (SAT-D277 gerekçesiyle aynı: üretim öncesi, canlı veri yok).
- **SAT-D281:** İstisna kaydının hangi sevk referansına (ör. sevkiyat/yükleme talimatı kimliği) bağlanacağı K3 uygulama
  PR'ında seçilir; SAT-D154 tek sevki serbest bıraktığı için kayıt o sevkin referansını taşımak zorundadır. Lojistik
  tablosuna yazılmaz; referans Satış kaydında tutulur.
- **SAT-D282:** Rol adları `docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md` ve persona kartındaki adlardır; izin adlarının
  dizgesi main'deki `ErpPermissions` / `SalesPermissionManifestV1` kaydından gelir (`SalesInvoicesExport`,
  `SalesOrdersMarginRead`, `SalesOrdersSourcePlanWrite`). Ad değişirse karar yeni ada aynen geçer.

## Kapatılan açık sorular

- EK54 "Sınırlar" — UBL-TR profilinde senaryonun fatura anı / dışa aktarma anı değeri — SAT-D280 (kesinleşmede
  sabitlenir).
- API-34 Excel'de resmî KDV/tevkifat gösterimi — SAT-D279 (ayrı sütun).
- API-07 risk istisnasının zamanlaması — SAT-D281 (Satış tarafı şimdi; Lojistik tüketimi sonra).
- `sales.invoices.export`, `sales.orders.margin.read`, `sales.orders.source-plan.write` grant insan kapısı için somut
  kullanıcı kararı — SAT-D282.
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.** Yukarıdaki soruların `OPEN-NNN` kimliği yoktur; rol atama ekranının
  yokluğu (`OPEN-013`, kurulum rehberi E4) açık kalır.

## Bilinçli değişiklikler

- **SAT-D248:** metni değişmez; geniş sütun setine SAT-D279'un iki resmî tutar sütunu eklenir.
- **SAT-D278 ve EK54 "Uygulama ayrıntısı":** metni değişmez; profilin okunduğu an SAT-D280 ile kesinleşme anına
  sabitlenir (dışa aktarma anında yeniden okunmaz).
- **SAT-D162:** metni değişmez; Sevkiyatçı'nın sevk/yükleme ekranındaki talep yüzeyi bir Lojistik ekranıdır ve SAT-D209
  gereği Lojistik başlayınca yapılır (SAT-D281 (3)).
- **SAT-D179, SAT-D250, SAT-D276:** metni değişmez; SAT-D282 bunların rol setlerini grant insan kapısı için somut
  karar olarak teyit eder.
- Başka hiçbir kararın anlamı değişmez.

## Uygulama listesi

| Karar | İş | Şerit / Kapı |
|---|---|---|
| SAT-D279 | API-34 Excel üreticisine resmî KDV ve resmî tevkifat sütunları (kayıtlı eşleşme tutarından; yoksa boş), sözleşme ve üretim testleri. Para alanı olduğu için K3 değerlendirmesi o PR'da yapılır. | Codex (backend) |
| SAT-D280 | Kesinleştirmede profilin saklanması (migration gerekiyorsa forward-fix kuralıyla), UBL-TR diliminde saklı profilin okunması. UBL-TR üretimi açık noktalar kapanınca. | Codex (backend); migration K3 |
| SAT-D281 | API-07 talep/onay/geçerlilik kaydı, `sales.orders.risk-exception.request` / `.approve` izin adları (atamasız), sürümlü okuma portu ve sözleşme testi; ön yüz Satış ekranı. Lojistik tüketimi yok. | Codex (backend, K3); Claude (frontend) |
| SAT-D282 | Bu PR'da kod işi yoktur. Persona kartına iki satır, kurulum rehberine "Satış izinleri kurulum kontrol listesi". Ayrı rol tanımı adımının biçimi 2026-11-10'da Sinan tarafından cevaplandı: **Ayarlar ekranı** — kodda rol tanımı veya grant yoktur; izinler kurulumda Ayarlar rol ekranından verilir (EK54 SAT-D276 (3) yolu; #66 6772460073, 6771735647'nin yerine geçer). Grant insan kapısıdır; Ayarlar rol ekranı gelene kadar E4 / `OPEN-013` gereği bekler. | Doküman; grant insan kapısı |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, katalog, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; grant açmaz.
- **API-34 UBL-TR'de açık kalanlar (`NEEDS-SINAN`, EK54):** fatura tipi, satıcı/alıcı VKN-TCKN ve adres kaynağı, toplu
  paketleme (zip), resmî fatura tarihi, UN/ECE birim kodları ve ürün adlarının kaynağı. Profil değerinin kaynağındaki
  (Cari e-belge ayarı) açık noktalar EK54'te yazıldığı gibidir. Bunlar tahminle etkinleştirilmez.
- SAT-D281 Lojistik modülünde bağlantı, tablo veya ekran açmaz; Lojistik sevk kapısının bu portu ne zaman ve nasıl
  tüketeceği Lojistik başlayınca incelenir (SAT-D209).
- **SAT-D282 ayrı rol tanımı adımının biçimi (cevaplandı, 2026-11-10):** #66 6771667746'daki soruya (kurulumda Ayarlar
  rol ekranından atama mı, kodda bir rol tanımı PR'ı mı?) Sinan 2026-11-10'da Claude şeridi oturumunda doğrudan
  "Hayır, Ayarlar ekranı" diye cevap verdi: kodda rol tanımı veya grant yoktur; izinler kurulumda Ayarlar rol
  ekranından verilir (EK54 SAT-D276 (3) yolu). Düzeltme #66 6772460073'te kayıtlıdır ve "kodda ayrı rol-tanımı PR'ı"
  diyen 6771735647'nin yerine geçer. Grant insan kapısı olarak kalır; Ayarlar rol ekranı gelene kadar E4 / `OPEN-013`
  gereği bekler.
- Rol atama ekranı bugün yoktur (kurulum rehberi E4, `OPEN-013`). Bu belge ilk rol dağılımı yöntemini seçmez.
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; production değişikliği yapmaz; yeni izin adı açmaz.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez; SAT-D282 grant kapısı için somut karardır; uygulama biçimi yukarıdaki
  2026-11-10 cevabıdır (Ayarlar rol ekranı).
