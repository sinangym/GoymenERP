# Sinan Kararları — 2026-11-10 (Satış: API-07 risk istisnası — sevk kimliği, limit içi ret, tek sevk tüketimi, talebin geri çekilmesi, izinlerin ataması)

Bu belge önceki karar dosyalarını silmez; SAT-D283…SAT-D287'yi ekler. SAT-D153…SAT-D162 (EK21), SAT-D209 (EK35),
SAT-D281 ve SAT-D282 (EK55), DEC-SAL-002 ve DEC-FIN-009 geçerlidir; bu belge onların karar metnini değiştirmez
(bkz. "Bilinçli değişiklikler"). `KARAR_SICILI` DEC-FIN-009 satırının yalnız `KabulKaniti` sütununa EK56 referansı
eklenir.

**Kaynak:**

- Sinan'ın 10 Kasım 2026'da Claude Code cloud oturumunda (`session_01SPADrzecggx3apH2gfNW7b`) Satış (Claude) şeridine
  önerili seçenekli bir soru turunda verdiği **dört** cevap (sorular 1–4); dördünde de **önerilen seçenek** seçildi.
  Soru 5 bu turda sorulmadı; aynı gün EK55 SAT-D282 (4) turunda verilen "Ayarlar ekranı" cevabından aynen alınır. Sorular, Satış
  tarafı taslak PR #1054'ün (API-07 tek sevk risk istisnası) açık bıraktığı noktalar içindir.
- Seçilmeyen seçenekler aşağıdaki tabloda soru turundaki açıklamalarıyla yazılmıştır.
- Cevaplar henüz Issue #66'ya yazılmadı; bu belge Issue #66'ya yorum yazmaz. Merge makbuzu merge eden tarafından
  yazılır.

## Soru ve cevaplar

| # | Soru | Cevap | Seçilmeyen seçenek(ler) | Kayda geçen anlam |
|---|---|---|---|---|
| 1 | Risk istisnası hangi sevk kimliğine bağlanır? | Önerilen seçenek: Yükleme talimatı | **İrsaliye** (irsaliye kesilirken bağlanır; daha geç, yeniden tasarım gerekir) · **Kesin sevkiyat** (kesinleşmiş sevkiyata bağlanır; en geç an) | İstisna, Lojistik'in **yükleme talimatı** kimliğine bağlanır. PR #1054'ün bugünkü tasarımını (`loadingInstructionId`) teyit eder. |
| 2 | Siparişin riski limit içindeyse istisna talebi reddedilsin mi? | Önerilen seçenek: "Evet, reddet" | **Hayır, izin ver** (önceden onay almak için limit içindeyken de talep açılabilir) | **Evet, reddedilir.** Siparişin riski limit içindeyse talep **reddedilir**; yalnız limiti aşan sipariş istisna isteyebilir. |
| 3 | Onaylanmış istisna nasıl tüketilir; bağlı sevk iptal edilirse ne olur? | Önerilen seçenek: Tek sevk, iptalde düşer | **Gün sonuna kadar geçerli** (aynı iş günü içinde o sevkiyat için tekrar kullanılabilir; iptal etkilemez) | Onaylanmış istisnayı **tek bir sevk** tüketir. O sevk iptal edilirse istisna **düşer**; yeni sevk için yeni talep gerekir. |
| 4 | Talep eden, bekleyen talebini geri çekebilir mi? | Önerilen seçenek: Evet, geri çeksin | **Hayır** (yalnız onaycı reddederek kapatır) | **Evet.** Talep eden **kendi bekleyen** talebini geri çekebilir; yeni bir **Geri çekildi** (`Withdrawn`) durumu eklenir. |
| 5 | `sales.orders.risk-exception.request` / `.approve` izinleri rollere nasıl verilir? | Bu turda sorulmadı; EK55 SAT-D282 (4) cevabı "Ayarlar ekranı" aynen uygulanır | — (EK55 SAT-D282 (4) turunda: kod PR'ında rol tanımı seçeneği reddedildi) | Kodda grant **yoktur**; izinler kurulumda **Ayarlar rol ekranından** atanır (EK55 SAT-D282 (4) yolu). Grant insan kapısı değişmez. |

## Kararlar

| No | Konu | Karar | Etkilenen yer |
|---|---|---|---|
| SAT-D283 | İstisnanın bağlandığı sevk kimliği | (1) Tek sevk risk istisnası (SAT-D154) Lojistik'in **yükleme talimatı** kimliğine bağlanır. (2) Bu, EK55 "Uygulama ayrıntısı"nda K3 uygulama PR'ına bırakılan "hangi sevk referansı" seçimini kapatır ve PR #1054'ün tasarımını teyit eder. (3) Kimlik Satış kaydında referans olarak tutulur; Lojistik tablosuna yazılmaz, Lojistik bağlantısı açılmaz (SAT-D209, SAT-D281 (3) aynen) | API-07 (PR #1054), Satış okuma portu `ISalesShipmentRiskExceptionReadPortV1`; DEC-FIN-009 |
| SAT-D284 | Limit içindeki siparişte talep | (1) Siparişin riski **limit içindeyse** istisna talebi **reddedilir**. (2) Yalnız riski **limiti aşan** sipariş için istisna istenebilir. (3) Bu karar yeni eşik veya tolerans getirmez; "limit içi / limit aşımı" mevcut risk kontrolünün (DEC-FIN-009) sonucundan okunur. Ret cevabı tutar, limit veya bakiye taşımaz (SAT-D162 aynen) | API-07 talep komutu (PR #1054'te yok; ayrı Satış PR'ı) |
| SAT-D285 | İstisnanın tüketimi ve sevk iptali | (1) Onaylanmış istisnayı **tek bir sevk** tüketir (SAT-D154 aynen). (2) İstisnanın bağlı olduğu sevk **iptal edilirse istisna düşer**; aynı sipariş için yeniden sevk istenirse **yeni talep ve yeni onay** gerekir. (3) SAT-D160'taki iş tarihi sonu geçerliliği aynen geçerlidir. (4) Tüketimi ve iptal sonucu düşmeyi Lojistik sevk kapısı uygular; Satış şeridinde bu bağlantı yapılmaz. Bu kural, Lojistik tüketicisi için okuma portunun **sözleşmesi** olarak kayda geçer (SAT-D281 (2), SAT-D209) | Satış okuma portu sözleşmesi; Lojistik sevk kapısı (Lojistik başlayınca) |
| SAT-D286 | Talebin geri çekilmesi | (1) Talep eden, **kendi bekleyen** (henüz onaylanmamış/reddedilmemiş) talebini **geri çekebilir**. (2) Yeni bir **Geri çekildi** (`Withdrawn`) durumu eklenir; geri çekilmiş talep onaylanamaz veya reddedilemez. (3) Başkasının talebini geri çekme bu kararla açılmaz. (4) Geri çekme, diğer komutlar gibi expected version, idempotency, deneme ve audit kaydıyla yapılır (AGENTS.md kritik komut kuralları) | API-07 (PR #1054'te yok; ayrı Satış PR'ı, durum kümesi değiştiği için migration/constraint K3) |
| SAT-D287 | İki risk istisnası izninin ataması | (1) `sales.orders.risk-exception.request` ve `sales.orders.risk-exception.approve` için **kodda grant, rol tanımı veya seed yoktur**. (2) İzinler kurulumda **Ayarlar rol ekranından** atanır (EK55 SAT-D282 (4), EK54 SAT-D276 (3) yolu); rol setleri SAT-D153, SAT-D159 ve SAT-D188'dedir. (3) Grant insan kapısıdır ve değişmez; Ayarlar rol ekranı gelene kadar E4 / `OPEN-013` gereği bekler. Bu belge grant açmaz | Ayarlar → "Kullanıcılar ve yetkiler" (kurulum), `docs/security/PERSONA_GRANT_MATRISI_KARTI_v0.md` |

Uygulama ayrıntısı (geri alınabilir):

- **SAT-D284:** Ret, mevcut problem kodu ailesinde ayrı bir kodla döner (ör. `risk_exception_within_limit`);
  durum değişebileceği için (risk sonradan limiti aşabilir) kayıtlı terminal ret değildir, yeniden denenebilir.
  Kodun adı uygulama PR'ında seçilir. Yalnız risk sonucu açıkça **limit içi** (`Within`) olduğunda reddedilir; risk
  sonucu okunamıyorsa (`Unavailable`) "limit içi" sayılmaz ve talep bu kararla reddedilmez (fail-closed: istisna
  yine onaycı onayı ister).
- **SAT-D285:** Satış okuma portu tüketimi ve sevk iptalini bilmez (bunlar Lojistik durumudur); port yalnız Satış
  kaydının durumunu ve SAT-D160 iş tarihi geçerliliğini döner. Tüketim ve iptal kuralını Lojistik sevk kapısı port
  cevabının üstüne uygular. Satış'ın tüketim/iptal bilgisini nasıl öğreneceği (olay/komut) Lojistik başlayınca karara
  bağlanır (`NEEDS-SINAN`, SAT-D209).
- **SAT-D286:** Geri çekme yalnız `Requested` durumundan yapılır; geri çekilen talep, aynı yükleme talimatı için yeni
  talep açılmasını engellemez (bekleyen talep tekilliği yalnız `Requested` durumunu kapsar).

## Kapatılan açık sorular

- EK55 "Uygulama ayrıntısı" — istisnanın bağlanacağı sevk referansı — SAT-D283 (yükleme talimatı).
- Limit içindeki siparişte talep — SAT-D284 (reddedilir).
- İstisnanın tüketimi ve sevk iptali — SAT-D285 (tek sevk tüketir; iptalde düşer, yeni talep gerekir). SAT-D160'taki
  "bağlı sevk iptal edilirse istisna da düşer" uygulama ayrıntısı bu kararla somut kullanıcı kararı olur.
- Bekleyen talebin geri çekilmesi — SAT-D286 (talep eden kendi talebini geri çekebilir).
- İki risk istisnası izninin atama yolu — SAT-D287 (Ayarlar rol ekranı; kodda grant yok).
- **Bu belge hiçbir `OPEN-*` kaydını kapatmaz.** Yukarıdaki soruların `OPEN-NNN` kimliği yoktur; rol atama ekranının
  yokluğu (`OPEN-013`, kurulum rehberi E4) açık kalır.

## Bilinçli değişiklikler

- **SAT-D154, SAT-D160:** metni değişmez; SAT-D285 tek sevk tüketimini ve sevk iptalinde düşmeyi teyit eder.
- **SAT-D159, SAT-D162:** metni değişmez; Sevkiyatçı'nın talep yüzeyi açık kalır (bkz. "Sınırlar").
- **SAT-D281:** metni değişmez; EK55 "Uygulama ayrıntısı"ndaki sevk referansı seçimi SAT-D283 ile yapılır.
- **SAT-D282, SAT-D276:** metni değişmez; SAT-D287 aynı atama yolunu iki risk istisnası iznine uygular.
- Başka hiçbir kararın anlamı değişmez.

## Uygulama listesi

| Karar | İş | Şerit / Kapı |
|---|---|---|
| SAT-D283 | PR #1054'te davranış değişikliği gerekmez; PR açıklaması ve sözleşme kod yorumlarındaki "EK55 pending" / "NEEDS-SINAN" referansları SAT-D283/SAT-D285'e çekilir. SAT-D284 takip PR'ı gelene kadar #1054'ün limit içindeki siparişte de talep kabul ettiği PR açıklamasına bilinen açık olarak yazılır. | Claude (Satış); K3 (PR #1054) |
| SAT-D284 | Talep komutunda limit içi siparişin reddi (risk kontrolü sonucundan), problem kodu, OpenAPI ve testler; ön yüzde ret mesajı. | Satış (ayrı PR, #1054 sonrası); K3 değerlendirmesi o PR'da |
| SAT-D285 | Satış tarafında kod yok; okuma portu sözleşmesine tüketim ve iptal kuralının yazılması. Tüketim ve iptal bağlantısı Lojistik sevk kapısında. | Lojistik (Lojistik başlayınca); cross-module contract K3 |
| SAT-D286 | `Withdrawn` durumu, geri çekme komutu ve endpoint'i (expected version, idempotency, audit), constraint/trigger güncellemesi (forward-fix migration), okuma portu durum eşlemesi ve `SalesShipmentRiskExceptionStatusV1` sözleşmesi (geri çekilen talep `Rejected` dönmemeli), OpenAPI ve testler; ön yüzde "geri çek" düğmesi. | Satış (ayrı PR); migration ve cross-module contract K3 |
| SAT-D287 | Bu PR'da kod işi yoktur; persona kartı ve kurulum kontrol listesine iki izin satırı ayrı doküman adımında. | Doküman; grant insan kapısı |

## Sınırlar

- **Kod ve veri değişmez.** Bu belge kod, manifest, katalog, migration, OpenAPI, rol seed'i veya Ayarlar verisi
  değiştirmez; grant açmaz, rol atamaz.
- **Açık kalan (`NEEDS-SINAN`, karar verilmedi):** Sevkiyatçı'nın giriş noktası — Lojistik tarafında talebin açılacağı
  ekran (SAT-D159, SAT-D162'deki sevk/yükleme ekranının biçimi ve Satış talep komutuyla bağlantısı). Bu Lojistik
  şeridinin konusudur ve bu belgede seçilmez.
- SAT-D285 Lojistik modülünde bağlantı, tablo veya ekran açmaz; Lojistik sevk kapısının portu ne zaman ve nasıl
  tüketeceği Lojistik başlayınca incelenir (SAT-D209).
- Bu belge hiçbir `OPEN-*` kaydını kapatmaz; production aktivasyonu yapmaz; yeni izin adı açmaz.
- İnsan kapıları (deployment, environment, secret, Settings aktivasyonu, grant/rol/Critical, production, `OPEN-*`
  kapatma, `.claude/settings.json`) değişmez.
