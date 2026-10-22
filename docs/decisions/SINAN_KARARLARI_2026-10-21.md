# Sinan Kararları — 2026-10-21 (delege edilmiş karar turu, r2)

Kaynak: Sinan Göymen'in 21 Ekim 2026 tarihli talimatı (Claude cloud oturumu). Doğrudan
kullanıcı ifadesi: *"5 benim onayımı bekleyendede sisteme uyumlu göymenerp projesine uyumlu ve
şirketlerin iş akış sürecine uyumlu olacak şekilde mantıklı cevapları sen uygula"* ve *"github
… çok masraflı olduğu için devre dışı bırakmıştım bana maliyetsiz başka bir en mantıklı çözüm
önerisini bul ve uygula"* ve *"6 maddelik yavaşlatmanın tamamını ortadan kaldıralım"*.

Önceki makbuzlar: #66/6313909467 (Actions bütçesi açılmıyor; şeritler aynı), #66/6314251238
(CI-LOCAL-FIRST-ADMISSION-01), #66/6314255220 (ADR-SET-002 START-GO).

r2: ilk taslak 146 ajanlık bağımsız doğrulamadan geçirildi (6 boyut, bulgu başına iki karşı
kontrol); 55 onaylı bulgu bu sürüme işlendi.

> **22 Ekim sonraki karar notu:** Bu belge 21 Ekim tarihsel kararlarını korur. Daha yeni
> [bütün dalgalar kararı](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6342711040)
> Finance STOP'u ve AI `A-contract-only` sınırını development için kaldırdı; Finance Dalga D
> ile W05.2/W06 geliştirmesi açıldı. Production, provider, grant/rol/`Critical`, secret,
> deployment, workflow aktivasyonu ve `OPEN-*` kapatma insanda kalır. Saat kuralı zaten
> [gece slotunu kaldıran kararla](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6342843156)
> §2'ye işlendi; burada tekrar tanımlanmaz.

## 0. Bu belgenin yetkisi ve sınırı

- **Yetki katmanı (2026-08-31 belgesiyle aynı):** yalnız yukarıdaki doğrudan Sinan ifadesi
  "son kullanıcı kararı" seviyesindedir. §1–§3 Claude sentezidir; **Sinan bu PR'ı merge edene
  kadar öneridir.** Merge sonrası §3 satırları `DELEGE-20261021-<no>` kimliğiyle kullanıcı
  kararı seviyesine çıkar ve ayrı dar PR ile sicile işlenir (Sinan merge eder).
- **Makbuz:** Sinan'ın bu PR'ı merge etmesi veya Issue #66'ya yazdığı tek satırlık onay.
  Ajan yazımı `DELEGE-*` yorumu makbuz sayılmaz; makbuz yoksa PR draft kalır
  (`KAYNAK_MAKBUZU_GEREKLI`).
- **Bu belge hiçbir `OPEN-*` satırını KAPANDI yapmaz** ve KILITLI/K0 sicil kararlarını
  (ör. DEC-DOC-008, DEC-STK-005/008, DEC-SAL-005/007, DEC-PRD-007/008) daraltmaz; bir satır bir
  DEC-* kararına dokunuyorsa o kararı açıkça uygular. İlke metni sicil ile çelişirse sicil
  geçerlidir ve ilke düzeltilir.
- Her değer **Settings/Politika Registry'de başlangıç sürümü (v1)** olarak girilir; koda
  gömülmez (`DEC-SET-001`). Sinan herhangi bir satırı tek satırlık Issue #66 yorumuyla geri
  alabilir; geri alma, o satırla üretilmiş para/stok/üretim kaydını geriye dönük değiştirmez.
- **21 Ekim tarihsel sınırı (22 Ekim notuyla development için superseded):** Finance STOP
  (Dalga D yalnız yetkili source-first; §3.4 tasarım kararıdır, aktivasyon değildir). AI
  yalnız `A-contract-only`. Production, deploy, secret, e-posta
  sağlayıcı ve workflow aktivasyonu insanda. `Critical` kümesine (grant-time çift kontrol) yeni
  ad eklenmez; katalog kaydı ≠ Critical sınıflaması ≠ grant. Fiziksel silme yalnız en yüksek
  yetkili (OPEN-021, DEC-DATA-001).
- Para/stok/üretim olayı bu belgeyle geriye dönük değişmez; kararlar yalnız yeni kayıtlara
  uygulanır.

## 1. Delegasyon ilkeleri

| # | İlke |
|---|---|
| P1 | Değer koda gömülmez; registry v1 değeri olarak girilir, Ayarlar'dan değişir, kullanılan sürüm + değer işlem snapshot'ına yazılır (DEC-SET-001, DEC-PRDCT-008). |
| P2 | Para/stok/finans olayı bir kez kaydedilir ve geriye dönük değiştirilmez. **İade** = GİB/e-Fatura pratiğine uygun ayrı belge (iade faturası) ve ayrı olay; kaynak belge bağı korunur (DEC-SAL-007, DEC-FIN-003). **Düzeltme/iptal** = DEC-DOC-008 gereği iptal veya ters kayıt + gerekçe (+ gerekirse yeni kayıt); orijinal silinmez, kaynak bağı korunur (DEC-FIN-004). Ters kayıt kopya hareket değildir. Hiçbir yol aynı olayı ikinci kez üretmez. |
| P3 | Fail-closed: politika yoksa `Unavailable`; onay yoksa işlem durur; yetki default-deny. |
| P4 | Türk mevzuatı ve GİB/e-Fatura/e-İrsaliye pratiği esas alınır (iade faturası, satır bazlı KDV ve tevkifat, TTK/VUK saklama). |
| P5 | Yetki: izin adı kataloğa yazılır (kayıt ≠ grant). Bu belgedeki iş unvanları (Depo yöneticisi, Kalite sorumlusu, Satın alma sorumlusu, Üretim müdürü, Satış onaycısı vb.) `PERSONA_GRANT_MATRISI_KARTI_v0`'daki dört personadan **değildir**; OPEN-013 grant matrisi için **aday persona** etiketidir. Aday persona rol kodu, system-role seed'i veya persona kartı satırı üretmez; karta giriş yalnız Sinan'ın cümlesiyle olur. Onay adımı kodda yalnız sahip modülün kaydettiği **izin adıyla** ölçülür; rol/persona adı koda veya REG değerine yazılmaz. Satırda izin adı yoksa adı sahip modül koyar. Grant seed'i yoktur; kullanıcılar canlıya yaklaşırken atanır (OPEN-013). |
| P6 | Onay: maker ≠ approver; tek onaycı başlangıç; çok kademeli zincir yalnız kantar **blok** bandındaki istisnada (No 62/63) ve finans onay eşiklerinde (DEC-FIN-012). |
| P7 | Tarih: iş günü = `Europe/Istanbul` takvim günü; mali yıl = takvim yılı. |
| P8 | **Sonraki sorular.** Koordinatör, cevabı bu belgedeki bir karardan veya P1–P7'den **doğrudan türeyen** yeni `NEEDS-SINAN`'ı `DELEGE-<tarih>-<no>` etiketiyle Issue #66'ya tek satır yazar ve devam eder. `DELEGE` cevabı `GECICI_VARSAYIM` seviyesindedir: registry v1 değeri olarak uygulanabilir, Sinan tek satırla geri alabilir; sicile `KILITLI` olarak **yalnız Sinan'ın açık onayıyla** girer (sessizlik onay değildir; haftalık özette listelenir). İlkelerden türetilemeyen yeni para/stok/üretim/formül/eşik/yetki (rol/grant/Critical)/güvenlik/migration/saklama politikası, `OPEN-*` kapatma ve production/deploy/secret Sinan'a kalır (AGENTS.md); haftalık listede toplanır. |

## 2. Süreç kararları (yavaşlatan 6 neden)

| Neden | Karar |
|---|---|
| 1 Merge yolu kapalı | **Sıfır maliyetli CI modeli** (`docs/development/CI_SIFIR_MALIYET_MODELI_20261021.md`): GitHub Actions self-hosted runner'da ücretsizdir; hosted dakika ve artifact/cache depolaması kullanılmaz; bütçe `$0` kalır. #765 bu modele göre tamamlanır. Workflow'ları yeniden etkinleştirmek Sinan'ın tıkıdır (ücretsiz). `$0` limiti hosted fallback'e karşı sigorta değildir; tek güvence `runs-on` etiketleridir. |
| 2 Bilgisayar kaynağı | [22 Ekim kullanıcı kararı](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6342843156): Ağır testler (SQL/browser/full/container) saat bağımsız her zaman, mevcut tek ağır-iş kilidi altında sırayla koşar. Anlık 2 GiB boş RAM kapısı korunur; eşik tutmazsa koşu reddedilir ve ret gerekçesi makbuza yazılır, koşu ilk boş fırsatta aynı kapılarla otomatik yeniden denenir. Scheduler/servis/config veya yeni watcher eklenmez. |
| 3 Head ilerleyince kanıt düşüyor | **Review dondurma + delta review (K1/K2):** yazar `REVIEW-READY <sha>` yazar, review gelene kadar push yapmaz. Bulgular yeni commit'le kapanır; reviewer `<önceki-sha>..<yeni-sha>` deltasını okur ve `DELTA-OK <yeni-sha>` yazar. Tam review + zincirleme DELTA-OK, adlandırılan sha için **bağımsız exact-head review sayılır**; koordinatör yalnız o sha'yı expected-head ile merge eder. DELTA-OK'tan sonraki her push yeni `REVIEW-READY` ister. Delta `origin/main` merge'i içeriyorsa veya REVIEW-READY diff'i dışındaki dosyaya dokunuyorsa delta modu düşer. **K3'te delta review yoktur;** her yeni head için iki tam exact-head review yeniden alınır. |
| 4 Tören | `GOYMEN_ERP_HIZLI_TESLIM_v4.md`: dalga START-GO'su (v4 kabulünden sonra koordinatör verir), READ-ACK PR açıklamasına girer, checkpoint yorumu yalnız merge/bloke/haftalık özet, K1/K2 tek review. K3 tanımı ve iki review korunur. v4, AGENTS.md'ye v4 işaretçisi Sinan tarafından merge edilene kadar yürürlüğe girmez. |
| 5 Karar bekleyen 96 satır | §3'te cevaplandı. Yeni sorular P8 ile akar. |
| 6 İzin onayları | `.claude/settings.json` izin listesi (v4 Ek A; koordinatör ayrı dar PR ile ekler, Sinan merge eder). Cloud oturumlarında **Auto** modu. |

## 3. 96 sorunun kararları

Sütunlar: No (SORU_SINIFLANDIRMA_175_v2), Madde, Karar, Nereye yazılır.
**REG** = Settings registry v1 değeri; **KAT** = izin kataloğu kaydı + aday persona etiketi
(persona kartı satırı değil); **SPEC** = modül spec/acceptance; **SRC** = source-first tasarım
(Finance STOP içinde).

### 3.1 Birim, yuvarlama, ölçek (OPEN-007)

| No | Madde | Karar | Yer |
|---|---|---|---|
| 4 | P05.2 | Sarf birimleri: `L` boyut `volume` ölçek 3; `adet` boyut `count` ölçek 0. Yürürlük = yayın günü; geriye dönük yok. | REG |
| 7 | EK-21 | Hizmet birimleri: `saat` (`time`, 2), `adet` (`count`, 0), `sefer` (`count`, 0), `ton·km` (`transport`, 3), `ton` (`mass`, 3), `kg` (`mass`, 3), `m` (`length`, 3). Var olan birim yeniden tanımlanmaz. | REG |
| 8 | EK-22 | Yüzdeyle toplu güncelleme sonucu listenin fiyat ölçeğine getirilir (TL/kg 4 ondalık; TL/ton ve TL/adet 2 ondalık); yuvarlama en yakın, yarı-yukarı (`AwayFromZero`). Çok listeli toplu yayın tek onayla; her liste ayrı sürüm ve audit kaydı alır. | REG + SPEC |
| 12 | EK-23 | Konuya özel yuvarlama/dönüşüm kuralı yoksa şirket geneli kurala düşülür (evet). | REG |
| 14 | EK-23 | Taban (floor) fiyat toplu yüzde değişiminde **değişmez**; ayrı güncellenir. | SPEC |
| 15 | S1-ROUNDING | Fatura: satır tutarı 2 ondalık yarı-yukarı; KDV ve, uygulanıyorsa, KDV tevkifatı satır bazında hesaplanır, 2 ondalık; belge toplamları satır toplamlarından türer. Açık kalem (DEC-FIN-005) = Ödenecek Tutar = (Matrah + Hesaplanan KDV) − Tevkif Edilen KDV (UBL-TR `PayableAmount`). Tevkifat uygulanabilirliği, oranı ve yürürlük tarihi sürümlü KDV masterında veri olarak tutulur (demir-çelik ürün teslimleri dahil); kodda sabit oran yoktur. Bankacı yuvarlama kullanılmaz. | REG (calculator) |
| 83 | M01.4 | Sonlanmayan sonuç reddedilmez; iç hesap exact rational (D-SLITTING-DECIMAL-RESIDUAL-01=B ile tutarlı); kayıt/gösterim ölçeği süre için dakika 2 ondalık yarı-yukarı. | REG (calculator) |
| 85 | M01.7 | Bölüm sınırı kg hassasiyetine **aşağı** yuvarlanır (3 ondalık, floor); kapasite aşılmaz. | REG (calculator) |
| 9 | EK-23 | Yoğunluk ve kg/m **ürün kartında** (aile tanımı varsayılan verir; onay ürün kartı onaycısı; DEC-PRD-009: doğrulanmış/standart master üstün, geometrik hesap yalnız tahmin). **Paket içi adet ürün kartı niteliği değildir:** DEC-PRD-008 / D-PACKAGE=A paket standardındadır (ürün + şirket kapsamlı, tesis override, müşteri istisnası, sürümlü, işlem snapshot alır). Yuvarlama politikası (kip+ölçek) Settings'te şirket geneli, ürün kartında override. | SPEC + REG |
| 86 | M02.5 | `D-M025-KERF=A` onaylandı: profil başına PerPiece/BetweenPieces, kerf, baş/son trim; global varsayım yok. | REG |
| 168 | R06.3 | Taşımada kg 3 ondalık yarı-yukarı dönüşüm kuralı uygulanır; düzeltme listesi yalnız kuralın uygulanamadığı satırlar için. | SPEC |

### 3.2 Kantar, mal kabul, sayım, stok (OPEN-004, EK-08/14, I-dilimleri)

| No | Madde | Karar | Yer |
|---|---|---|---|
| 51 | EK-08 (K1) | Tolerans hat–ürün–işlem bazlı (hat opsiyonel). **Ürün satırı varsa ürün, yoksa işlem satırı geçerli**; ikisi de yoksa `Unavailable`. Geri düşüm tüketici (EK-08) çözümündedir, adaptörde değil. | REG + SPEC (EK-08 aile şeması; ADR-SET-002 değişmez) |
| 62 | EK-14 | Uyarı → devam + kayıt. Onay bandı (onay ≤ fark < blok) → tek onaycı istisnası beklenir (No 63). **Blok (fark ≥ blok eşiği) → varsayılan ret** (kabul yapılmaz, tedarikçiye bildirim); yalnız No 63'teki iki kademeli istisna onayıyla gerçek kg üzerinden kabul edilebilir; override değeri + gerekçe işlem snapshot'ına ve audit'e yazılır. Hüküm verilemedi → fail-closed, kabul yapılmaz. Kg bazlı kabulde kantar fiş numarası zorunlu. Politika ürün/işlem bazlı; tedarikçi bazlı değil. E.1 aile eşlemesi: Warning=Proceed, ApprovalRequired=RequireException, Blocked=Refuse (istisna hariç), Unavailable=Refuse. | REG + SPEC |
| 63 | EK-14 | Onay bandı istisnası tek onaycı: aday persona Depo/Lojistik yöneticisi (maker ≠ approver; izin adını Logistics sahibi belirler). Blok bandı istisnası iki kademe: Depo/Lojistik yöneticisi → Satın alma sorumlusu; iki onay tamamlanmadan kabul yapılmaz; EK-28 onay kuyruğunda, gerekçe + audit. | KAT + REG |
| 65 | EK-14 (EK-12 bağlantılı) | **Müşteri kapısında reddedilen (teslim edilmemiş) pay**, teslimat farkı kaydına (EK-12) bağlı tek bir Inventory "ret geri girişi" hareketiyle stoğa döner; varsayılan lokasyon sevkin çıktığı yaprak lokasyon; hasar şüphesinde mal **bloklu** stok olarak alınır (`BlockState=Blocked`, DEC-STK-006; sabit adlı karantina lokasyonu yoktur — DEC-ORG-002; şirket isterse Ayarlar'da "bloklu mal lokasyonu" tanımlar, REG, varsayılan boş). Logistics yalnız fark olayını yayımlar, hareketi Inventory üretir; kapalı `logistics.delivery-differences.rejection-reentry` yeteneği açılır. Onay: aday persona Depo/Lojistik yöneticisi. Bu akış müşteri iadesi değildir; müşteri iadesi yalnız teslim edilmiş paydan çalışır (DEC-SAL-007). Tedarikçi kabulünde blok sonrası boşaltılmış mal için aynı bloklu-stok modeli No 62 kapsamındadır. | SPEC + KAT + REG |
| 64 | EK-14 | Ana stok birimi kg olan her üründe (DEC-PRD-007; boru/profil adet/metre ile girilse de) kabulün **kg bacağı her zaman** kantar politikasından geçer (OPEN-003; No 62/63); irsaliye adet/metre taşıyorsa kg ürün kartı dönüşümüyle türetilir (No 9) ve tartılan kg bununla karşılaştırılır. Adet/metre sayım bacağı için **ayrı miktar politikası**: başlangıç tam eşleşme, fark = onay. Kantar politikası yalnız kg bacağı olmayan kalemlerde uygulanmaz: yoğunluğu tanımsız sarf (D-V21-SARF=A: kg null) ve StockItem'ı olmayan hizmet. | REG + SPEC |
| 70 | EK-14 Q-05 | Tek onaycı (aday persona Kalite sorumlusu). Uygulama sahip modülde: hurda → Inventory, tedarikçi iadesi → Purchasing, yeniden işleme → Manufacturing, sapma ile serbest → Quality. Quality kararı yayımlar; sahip modül kendi olayını üretir. | SPEC |
| 59 | EK-14 | Yıllık tam sayım (Aralık) tüm iç lokasyonlar; aylık döngüsel sayım hol bazlı; harici depo mutabakatı üç ayda bir. Değerler REG. | REG |
| 69 | EK-14 | "Zamanı geldi" yalnız bilgi/uyarı; işlem durdurmaz. Tek blok: yıl sonu sayımı bitmeden dönem kapanışı yapılmaz. | SPEC |
| 60 | EK-14 | Oturumu açan kapatabilir; sayım farkı onayı başka kişi (maker ≠ approver). | SPEC |
| 61 | EK-14 | Sayım oturumu açıkken lokasyon hareketleri dondurulur; acil hareket için oturum yetkiliyle kapatılıp yeniden açılır. | SPEC |
| 67 | EK-14 | Stok yalnız yaprak (en alt) lokasyonda tutulur; üst lokasyon toplamdır. | SPEC |
| 43 | I02.1 | Tartı fazlası ayrı "transfer farkı" olayıdır; kabul reddi değil. Hedef gerçek kg ile girer; fark kaynakta sayım farkı olarak araştırılır. | SPEC |
| 44 | I02.2 | Hasarlı gelen malzeme hedefte **bloklu** stok olarak alınır + fark kaydı. | SPEC |
| 46 | I04.1 | Hold, çocuk etiketlere taşınarak bölme yapılabilir; hold kapatılmaz. | SPEC |
| 47 | I05.1 | Şartlı kabul ayrı yetki ister: `quality.inspections.release-with-deviation` (aday persona Kalite sorumlusu). | KAT |
| 50 | I05.2 | Aynı etiket belgesi öncelikli; mal kabul/tedarikçi sertifikası da kabul edilir, etiket–belge bağı kayda yazılır. | SPEC |
| 48 | I05.1 | `quality.inspections.read` → aday persona: operasyon personaları; `.record` → Kalite kontrol görevlisi; `.decide` → Kalite sorumlusu. | KAT |
| 45 | I03.1 | `inventory.stock-transfer.write` → aday persona: Depo operatörü, Depo yöneticisi; kart girişi Sinan'da. | KAT |
| 71 | I08.1 | ST-07 fason talebi **P-xx fason siparişi** bağlamından başlar; ST-01 etiketleri sipariş satırına bağlanır. | SPEC |
| 73 | I08.2 (**K3**) | **Yeni karar (kapsayan karar yok; H sınıfı):** DEC-STK-008'in iki bacaklı akışına üçüncü sonuç eklenir — dönüş bacağı yerine fasoncu lokasyonundan müşteriye **merkeze girişsiz satış sevki**, yalnız şirket malı için. `D-I08-SUBCONTRACT-01=A` sahiplik dağılımı korunur (hizmet taahhüdü Purchasing, custody/konum Inventory, sevk Logistics). Yetkili onayı zorunlu. İrsaliye: satış sevk irsaliyesini satıcı sıfatıyla **Göymen** düzenler, fiziksel çıkış noktası fasoncu lokasyonu; fasoncunun fason dönüş irsaliyesi aynı sevke bağlanır. Merkez depoya yapay giriş/çıkış kaydı yapılmaz (tek fiziksel olay); fason dönüş kalanı sevk edilen kg kadar kapanır; kayıp/hasar ayrı olay. İki bağımsız exact-head review (cross-module). DEC-STK-008 Not sütununa "üçüncü sonuç — DELEGE-20261021-73" eklenir. | SPEC + KARAR_SICILI notu |
| 36 | EK-07 | Kalite reddiyle bloklu mal doğrudan tedarikçiye iade edilebilir; blok kaldırılmaz, iade olayı blok stoğundan çıkar. | SPEC |
| 37 | EK-07 | İade sonrası yenisi gelecekse sipariş satırı kabul kalanı iade miktarı kadar yeniden açılır (gerekçe + yetki). | SPEC |
| 175 | EK-45 | Cihaz tartım anı `DeviceCapturedAt` ayrı alan olarak yazılır (sunucu damgası yanında). Çevrimdışı taslak en çok 240 dk sonra kabul edilir; sonrası yetkili onayı ister. | REG + SPEC |
| 173 | EK-45 | Gönderilmemiş sayım taslağı cihazda şifreli kalır, yalnız aynı kullanıcıya, en çok 24 saat; sonra silinir. | REG + SPEC |

### 3.3 Satış, satın alma, teslim farkı, fatura

| No | Madde | Karar | Yer |
|---|---|---|---|
| 20 | F03.1 | Tedarikçiye iade = Göymen'in kestiği **iade faturası** (ayrı belge; orijinal alış faturası + kaynak sipariş/kabul bağı zorunlu — DEC-SAL-007/DEC-FIN-003). "Ters kayıt + yeni fatura" yolu kullanılmaz; alış faturasının kendi iptali/düzeltmesi DEC-DOC-008 kapsamında kalır. | SPEC |
| 21 | F03.1 | Müşteri iadesi = müşterinin kestiği iade faturası, orijinal satış faturası + sevk bağıyla ayrı belge olarak kaydedilir (DEC-FIN-003). Teslim edilmiş mal için satış faturası "ters kayıt + yeni fatura" ile düzeltilmez; e-Fatura RED/iptal yolu bu kararla kapatılmaz (Master fatura yaşam döngüsü, DEC-DOC-008). | SPEC |
| 25 | F04.1 | Fatura sipariş fiyatından düşükse istisna istenmez; bilgi uyarısı + sapma kaydı (rapor). Blok yalnız aleyhte fark. | REG + SPEC |
| 26 | F05.1 | Fatura sipariş para biriminde kesilir; TL karşılığı fatura günü politika kuruyla (DEC-FIN-010). Farklı para birimi için sipariş revizyonu gerekir. | SPEC |
| 17 | F06.1 | Düzeltmede para birimi değişirse taşınan pay **orijinal belgenin kuruyla** çevrilir (TL karşılığı korunur); kur farkı üretilmez; gerekçe + yetki + audit. | SPEC |
| 35 | F06.2 | Taksit eşlemesi sıra numarasıyla; taksit sayısı değişirse ödenmiş taksitler korunur, kalan tutar yeni taksitlere sırayla dağıtılır. | SPEC |
| 38 | F05.2 | Müşteri iadesi sevk payı defterinde "iade edilen" olarak düşülür (ayrı olay, orijinal pay değişmez). | SPEC |
| 57 | EK-12 | Fazla teslimat için ek faturalama incelemesi yapılır: "fazla teslim" olayı → satış sorumlusu inceleme kuyruğu; otomatik fatura yok. | SPEC |
| 40 | F05.2 (EK-01) | **Alış siparişinde** (AT-026; `PurchaseDelivery` işlem kimliğiyle EK-08 politikası okunur) toleranslı fazla teslim: sapma onay eşiğinin altında (K4 `AtOrAbove`: eşik dahil değil) otomatik kabul; onay bandı → No 41 tek onay; blok bandı → varsayılan ret, yalnız No 63'teki iki kademeli istisna ile kabul. Sipariş kalanı 0; fazla kısım siparişe "fazla teslim" olarak bağlanır; kabul payı defteri ve stok gerçek kg. Satış tarafı fazla teslim incelemesi No 57'de ayrıdır. | REG + SPEC |
| 41 | F05.2 (EK-01) | `ApprovalRequired` düzeyindeki **alış** teslim farkını aday persona Satın alma sorumlusu onaylar (`purchasing.delivery-variance.approve`), EK-28 onay kuyruğu, gerekçe zorunlu; maker (kabulü kaydeden) ≠ approver. | KAT + SPEC |
| 55 | EK-12 | Onaylanan farkın mali etkisini belge sahibi modül (Sales/Purchasing) hazırlar. Logistics olay yayımlar (ERP-000F öncesi senkron sözleşme + okuma portu); Logistics para olayı üretmez. | SPEC |
| 30 | EK-02 | Zorla kapatma var: `sales.orders.force-close` (`sales.orders.*` ailesi, `sales.orders.cancel`'dan ayrı ad), gerekçe zorunlu, audit. **DEC-SAL-005 aynen uygulanır:** kapatma öncesi etki önizlemesi (aktif tahsis, bağlı satın alma/üretim, irsaliyesiz yükleme, faturalanmamış irsaliye, eşleşmemiş avans/dekont) zorunlu; herhangi bir kategori açık/okunamaz ise komut reddedilir; Satış başka modülün kaydını serbest bırakmaz, açık tahsis önce kendi sahibinde kapatılır (DEC-STK-005: sessiz serbest bırakma yok). Önizleme temizse kalan "iptal edilen kalan" olarak kaydedilir (DEC-SAL-001 ayrı eksen); stok/para olayı yok. | KAT + SPEC |
| 39 | F05.2 | Sipariş kabulü ayrı yetki değildir; sipariş onaycısı yeterli. | KAT |
| 10 | EK-23 | Hizmet satırı fiyatı ürün satırıyla aynı kademe zincirinden çözülür: anlaşma > cari özel > cari grubu > şirket (D-F02-PRICE-PRIORITY-TIER-SET-01=A; DEC-PRD-011); kaynak kademe ve miktar kümesi satıra snapshot'lanır. Hiçbir kademede kayıt yoksa elle giriş yalnız yetkili + gerekçe + audit ile. KDV oranı ile tevkifat uygulanabilirliği/oranı ürün-hizmet türü ve alıcı statüsüne göre KDV masterından; iskonto satır bazında yüzde. | SPEC |
| 160 | EK-06 | Onaylanmamış talepten sipariş açılamaz (default-deny). Acil durum: talep onayı hızlı yol (tek onaycı). | SPEC |
| 161 | EK-06 | Onaylı siparişi olan talep reddedilemez; önce sipariş iptal edilir. | SPEC |
| 162 | EK-05 | "Siparişe dönüştür" komutu: teklif satırları sipariş satırı olur (fiyat/miktar snapshot + teklif referansı); teklif başına tek sipariş. | SPEC |
| 163 | EK-04 | Bir satış teklifi aynı müşterinin birden fazla talebini karşılayabilir (talep → teklif N:1). | SPEC |
| 76 | EK-18 | Mükerrer cari birleştirme grup yöneticisi (aday persona) onayıyla, Party global seviyede; her şirkete bildirim. İzin adı (`counterparty.parties.merge.approve`) katalog kaydı, Critical bayrağı yok. | SPEC + KAT |
| 77 | EK-18 | Unmerge komutu yok. Hatalı birleştirme: yeni cari açılır; taşınan bağlantılar DEC-DOC-008 iptal/ters kayıt + sebep zinciriyle, kaynak kimlikleri ve audit korunarak düzeltilir (DEC-CAR-003). Yerinde değiştirme ve silme yok. | SPEC |

### 3.4 Risk, sevk kapısı, finans (Finance STOP içinde — tasarım, aktivasyon değil)

| No | Madde | Karar | Yer |
|---|---|---|---|
| 31 | EK-03 | Sevk kapısı risk okumasını **sistem bağlamıyla** yapar (fail-closed); kullanıcı yalnız istisna onayında yetki taşır. | SPEC |
| 32 | EK-03 | Risk istisnası iki tür: tek sevk (belge bazlı) ve tarih aralıklı (müşteri bazlı, üst sınır REG, başlangıç 30 gün). İkisi de onaylı ve auditli. | REG + SPEC |
| K6 | EK-03/RISK | Azaltım oranı kapsamları: tür+banka+vade > tür+banka > tür > şirket geneli (en özel kazanır); aynı özgüllükte iki yayımlı satır olamaz. Öncelik tüketici (RISK aile şeması) çözümündedir. | REG + SPEC/SRC (ADR-SET-002 değişmez) |
| K7 | RISK | Risk yüzde alanları 0–100. | REG |
| K8 | RISK | Maruziyet bazı `WithVat`: alacak = faturanın Ödenecek Tutarı (KDV dahil, tevkif edilen KDV düşülmüş; No 15). | REG |
| 34 | F06.2 | Çapraz para dağıtımında kur farkı, para olayından türeyen ayrı "kur farkı" satırıdır (muhasebe kaynağı); yön ödeme/tahsilat ile aynı. | SRC |
| 108 | F08.1 | `occurredOn` kuralı hazine para olaylarına da uygulanır (tek tarih semantiği). | SRC |
| 109 | F08.2 | Ciro edilmiş çek riskte "ciro edilmiş – rücu riski" sınıfında ayrı görünür; teminattaki çek "teminatta" sınıfında. Karşılıksız dönerse yeni olay "çek karşılıksız döndü": müşteri alacağı yeniden açılır, ciro edilen tarafa borç olayı. | SRC |
| 112 | F09.4 | BankCharge satırları muhasebe kaynağı olarak **para olayından** (Treasury) kaydedilir; Purchasing'de değil. | SRC |
| 113 | F09.5 | Açık kalemler nakit planına vade tarihiyle girer (vade yoksa belge tarihi + vade profili); alacak = giriş, borç = çıkış; vadesi geçmiş "bugün" kovası. | SRC |
| 114 | EK-40 | Grup içi netleştirme ve konsolidasyon ERP dışında (muhasebe); ERP yalnız grup içi bacakları işaretler ve raporlar (OPEN-016). | SRC |
| 115 | EK-41 | Banka kısmi/kesintili ödeme: tahsilat brüt tutar + ayrı banka masrafı para olayı; netleştirme yok. | SRC |
| 22 | F03.2 | İçe aktarılmış yönsüz açık kalem işaretlenmez; "yön belirsiz" karantina sınıfı; Finans sorumlusu düzeltme listesiyle yön atar. | SPEC |
| 79 | EK-19 | Onaylandı: devir runtime'ı gelene kadar mutabakat işlem başlangıcından kümülatif. | SPEC |
| 136 | W04.3 | Evet, dönem başı devir bakiyesi istenir; EK-43 devir runtime'ı gelene kadar No 79'daki kural geçerli. | SPEC |
| K14 | F06/KUR | Manuel kur istisnası şirket düzeyi aç/kapa anahtarıyla; başlangıç **açık** (yetki + gerekçe + audit, DEC-FIN-010). | REG |

### 3.5 Üretim

| No | Madde | Karar | Yer |
|---|---|---|---|
| 90 | M02.6 | `CardRevisionPrepare` → aday persona Teknik ofis/Üretim planlama; `CardRevisionApprove` → Üretim müdürü; maker ≠ approver. | KAT |
| 103 | M03.1 | Onaylandı: taslağı revize eden de, ilk hazırlayan da maker sayılır. | SPEC |
| 104 | M05.1 | Aynı anda kaydedilmiş çıktı varsa ölçüm güncel sayılmaz; ölçüm çıktıdan sonra olmalı (eşitlikte eski). | SPEC |
| 105 | M06.2 | `manufacturing.queue.apply` → aday persona Üretim planlama; `manufacturing.time-records.record` → Hat operatörü/Vardiya amiri. Adlar kabul. | KAT |
| 106 | EK-16 | Gerçek M04.1 paketi repoya commit edilmez; CI'ya repo dışı `E:\GoymenERP-CI\data\` üzerinden (sha256 pin); repoda sentetik örnek + manifest. | SPEC |

### 3.6 Ayarlar/politika registry (RV12 v2 K1–K20)

Bu bölümdeki öncelik, geri düşüm, sınır ve tarih kuralları **ADR-SET-002'ye yazılmaz**; ADR
yalnız saklama ve mekanizmayı sabitler (#66/6314255220 "Korunacak sınırlar"). Adaptör tam
kapsam okur (T4); her kural ilgili ailenin tüketici dilimi şemasıyla ve o dilimin K3
review'uyla gelir (T5).

| K | Karar |
|---|---|
| K1 | = No 51. |
| K2 | Kantar yüzde alanları 0–100 (üst sınır 100). |
| K3 | Warn ≤ Approval ≤ Block sırası kuraldır; bozan sürüm reddedilir. |
| K4 | Eşik karşılaştırması `AtOrAbove`: tam sınırdaki sapma uyarı verir. |
| K5 | Politika tarihi = `OccurredAt`'in `Europe/Istanbul` takvim günü (P7). |
| K9 | Saklama: değerlendirme günündeki yayımlı sürüm uygulanır; yasal taban (K12) ve legal hold her zaman üstündür. |
| K10 | AI geri bildirim/soru-cevap saklaması ayrı aile (Cilt 11 tüketicisi); süre 90 gün (No 159). Süre kayıt anında `RetainUntil` olarak sabitlenir; K9'un değerlendirme-günü kuralı bu aileye uygulanmaz. Yasal taban ve legal hold üstündür; imha EK-43'te kalır. |
| K11 | Saklama başlangıcı `year-end` = 31 Aralık (mali yıl = takvim yılı). |
| K12 | = No 172. |
| K13 | Tahsis "yaklaşıyor" uyarısı **1 gün** (`warningDays`, alt sınır 1; REG v1). Varsayılan `ReservedUntil` süresi DEC-STK-005 gereği 3 gün olduğundan uyarı penceresi bundan kısa tutulur; DEC-STK-005 değişmez. |
| K15 | #747 yaşam döngüsü (Draft → Approved → Published; onaylayan ≠ hazırlayan, yayımlayan ≠ hazırlayan) **bütün ailelere** uygulanır. |
| K16 | = No 133. |
| K17 | Her ailede `EffectiveFrom` varsayılanı yayın günüdür; ileri tarih izinli, geriye dönük yok; tüketici `businessDate` = işlemin `Europe/Istanbul` günü. |
| K18 | E.1/E.4/E7/F04.1 anlamları bu belgedeki ilgili satırlarla (62, 59, 175, 25) karara bağlandı. |
| K19, K20 | RV12 v2'de FOUND (DEC-SET-001: politika yoksa fail-closed, kodda varsayılan yok; snapshot = kullanılan sürüm + değer); yeni karar gerekmez. |
| 133 | EK-38: genel üç izin `settings.policies.prepare/approve/publish`; kritik aileler (kantar blok, finans onay eşiği, saklama) için ek `settings.policies.publish-critical`. Katalog kaydı; grant yok. |

### 3.7 Platform, kimlik, saklama, AI, belge

| No | Madde | Karar | Yer |
|---|---|---|---|
| 125 | EK-28 | Vekâlet: kullanıcı → kullanıcı, süre zorunlu (üst sınır REG, başlangıç 30 gün), işlem türü seçimli. "Bilgi iste" askıya alır, "iade" hazırlayana döndürür. Eskalasyon süresi REG (başlangıç 2 iş günü) → eskalasyon hedefi izin adıyla REG'de tanımlanır. Risk eşiği başlangıçta yalnız RISK-01. | REG + SPEC |
| 126 | EK-32 | `Exports:SynchronousRowLimit` = 10.000; üstü arka plan kuyruğu (ERP-000F gelene kadar açık hata ile reddedilir). Dosya 30 gün erişilebilir, sonra erişim kapanır; fiziksel imha OPEN-021'e tabi. İndirme: üreten + aynı yetkiye sahip kullanıcılar (teslim anında ölçülür). | REG + SPEC |
| 128 | EK-32/R-05 | Zamanlanmış rapor: başlangıç D-01/D-03/D-04 ekran KPI anlık görüntü dışa aktarımı (operasyon); M-01/F-01 Finance STOP kapsamında, v1'de zamanlanmaz. Teslim uygulama içi bildirim (e-posta EK-31 aktivasyonuna bağlı); alıcı yetkisi hazırlama ve teslim anında yeniden ölçülür, hazırlama kapsamı No 143; başarısız teslimde 3 deneme/15 dk, sonra hata kaydı. | REG + SPEC |
| 143 | EB-5 | Zamanlanmış rapor oturumsuz çalışır; sistem kimliği yalnız tetikleyicidir ve veri kapsamı taşımaz. Rapor her alıcı için ayrı, o alıcının server-çözümlü CompanyContext'i ve okuma yetkisi/alan maskeleme (No 135) kapsamında hazırlanır (ERP-000K §9: `all` CompanyId yok). Yetki hazırlama ve teslim anında ölçülür; herhangi birinde yoksa üretilmez/teslim edilmez. Kullanılan şirket seti, izin kapsamı ve tetikleyen kimlik rapor snapshot'ına ve audit'e yazılır. | SPEC |
| 149 | EB-7 | Onaylı irsaliye iptali politika ile kapatılabilir; başlangıç açık. Yetki: **mevcut** `logistics.dispatch-notes.cancel` (ErpPermissions.cs; yeni ad yok). Politika anahtarı (REG) yalnız Approved durumundaki iptal yolunu kapılar; Draft iptali politikadan bağımsız. Araç çıkmış irsaliye iptal edilemez → iade/düzeltme. | REG + KAT |
| 154 | EK-33 | Belge içeriğini ana kaydın okuyucusu **veya** referans kaydın okuyucusu okur; "hassas" sınıfındaki belgede yalnız ana kayıt okuyucusu. | SPEC |
| 124 | EK-26 | KPI v1 (ekran → KPI): D-01 Rol Bazlı Ana Sayfa → günlük sevk tonajı; D-03 Yönetim Özeti → açık sipariş tonajı; D-04 Operasyon Nabzı → kantar istisna oranı; PR-01 Üretim Ana Bakış → hat verimliliği (gerçek/plan kg) + kütle dengesi sapması; F-01 Finans Genel Bakış → vadesi geçmiş alacak (SRC); M-01 Muhasebe Kontrol Merkezi → bekleyen/hatalı kayıt sayısı (SRC); aktivasyon yok. Her KPI `FormulaId` ile calculator'da, sahip modülün okuma portundan. | SPEC |
| 130 | EK-34 | SQL Server Full-Text Search (ek bileşen yok). AT-069 p95 hedefi başlangıç 2 s (OPEN-015 sentetik hacim). | REG |
| 134 | EK-44 | OpenTelemetry → yerel OTLP collector + Prometheus/Grafana (on-prem, açık kaynak). Metrik 90 gün, trace 14 gün, log 180 gün. Güvenlik olayı deposu: giriş, yetki reddi, izin değişikliği, politika yayını, dışa aktarım, imha talebi; 2 yıl. | REG |
| 135 | EK-44/SY-08 | Maskeleme: VKN/TCKN (son 4 hariç), IBAN, telefon, e-posta, adres. Dışa aktarım ayrı izin `platform.export.sensitive`. Gizlilik ekranı yalnız kendi şirketi + yetkili. | KAT + SPEC |
| 165 | R01.2 | Mutlak oturum ömrü ERP'de uygulanır (başlangıç 12 saat); IdP gelince IdP ile hizalanır. | REG |
| 118 | W02.2 | Evet: D-IDP gelene kadar taze yetki ölçümü + iki kişi kuralı (request/approve, komut zamanı maker ≠ approver) yeterli (dev/staging). Production'da adım-yükseltme zorunlu. İzin adları `platform.events.redrive.request/approve` katalog kaydı, Critical bayrağı yok. | SPEC + KAT |
| 172 | EK-43 | Yasal alt sınır (TTK 10 yıl / VUK 5 yıl — büyüğü) **sistemce zorlanır** ve politika onayında iki kişi (prepare ≠ approve) de gerekir. | REG + SPEC |
| 156 | W05.1 | AI önizleme geçerliliği 15 dk. A fazında tüm araçlar salt-okunur, ikinci onay yok. Yazma araçları B fazında (kapalı) ikinci onay + adım-yükseltme. | REG |
| 157 | W05.2 | AI araç çağrıları ve reddedilen istekler güvenlik olayı deposuna; 1 yıl. (W05.2 beklemede; değer hazır.) | REG |
| 159 | W06.1 | AI soru/cevapları kalite değerlendirmesi için maskeli saklanır; 90 gün; kullanıcı kimliği ayrıştırılmış. | REG |
| 1 | R02.2 | İzole/sentetik ortamda gerçek VKN kullanılmaz; sentetik seed varyantı (geçerli biçimli sahte VKN). DataCustodian = Sinan. | SPEC |
| 164 | R01.1 | DATA-IDP aktivasyon sahibi ve güvenlik sahibi: Sinan (tek yetkili; ileride atanabilir). | SPEC |
| 167 | R06.2 | UAT kayıtları repo dışı `E:\GoymenERP-UAT\` + şirket yedeği; canlıya geçişten sonra 2 yıl. | SPEC |
| 169 | R08.1 | GO dosyası repo dışı `E:\GoymenERP-CI\evidence\GO\` + Issue #66 makbuzu; repoda yalnız sha256. | SPEC |
| 171 | EK-37 | GIN unvan/VKN ticaret sicil gazetesi veya vergi levhasıyla girilir; hazırlayan muhasebe/Sinan, onaylayan Sinan (`organization.companies.change.prepare/approve` katalog kaydı, Critical bayrağı yok). **Değerin kendisi Sinan'dan gelecek** (OPEN-001-GIN açık). | SPEC + KAT |
| 155 | EK-31 | **Sinan'da kalır.** Production e-posta sağlayıcısını Sinan production ortamında etkinleştirir. Dev/staging yerel sahte SMTP. | — |
| 2 | R04.1 | Final delta'da eski kaynakta bulunmayan kayıt **karantinaya** (veri sınıfı) alınır; otomatik pasif yok; insan incelemesi. | SPEC |

## 4. OPEN-* durumu — bu belge hiçbir OPEN-*'ı KAPANDI yapmaz

- §3 satırları yalnız registry v1 değeri/anlamı verir. İlgili `OPEN-*` satırlarının
  (OPEN-004/005/007/009/010/012/015/018/019/020/021/030) `ACIK_KARARLAR.md`'deki "gerçekten
  açık kalan bölümü" (saha golden / nihai işletme onayı, gerçek hacim ve production p95,
  logo/antet dosyaları, GPU/OCR PoC, legal-hold ve otomatik purge davranışı, VKN değişiklik
  yaşam döngüsü) aynen AÇIK kalır; durum KISMEN sürer. Sicil güncellemesinde yalnız "kapanan
  bölüm" sütununa `DELEGE-20261021-<no>` alıntısı eklenir.
- `OPEN-018/021`: bu belge yalnız K9, K10, K11, K12/172 ve 126/134/157/159 tekil süre
  değerlerini kapatır. Tür bazlı saklama süre matrisi, idempotency replay saklama,
  partition/archival, otomatik purge/imha davranışı ve ikinci onay akışı, legal-hold
  koyma/kaldırma, dönemler arası temsil ve çok-yıllı okuma mimarisi **açık** kalır.
- Hiç dokunulmayanlar: `OPEN-001-GIN` (değer), `OPEN-008` (maliyet yöntemi), `OPEN-011`
  (nakit tamponu), `OPEN-013` production IdP, `OPEN-014/017` donanım/RPO-RTO, `OPEN-016`
  entegrasyonlar.
- 14 adın `Critical` kümesine eklenmesi: reddedilmedi, **ertelendi** (Sinan). §3'teki KAT
  satırlarının gerektirdiği adlar (ör. `counterparty.parties.merge.approve`,
  `platform.events.redrive.request/approve`, `organization.companies.change.prepare/approve`)
  Critical bayrağı **olmadan**, yalnız katalog kaydı olarak girilir (grant yok, K3 iki
  bağımsız review); ilgili komutlar grant'e kadar `Unavailable`/default-deny çalışır.
- **21 Ekim durum kaydı:** Finance STOP bu belge tarihinde kalkmamıştı. Daha yeni
  [22 Ekim kararı](https://github.com/sinangym/GoymenERP/issues/66#issuecomment-6342711040)
  development için bu kapıyı kaldırdı; açık işletme kararlarını veya production kapılarını
  kapatmadı.

## 5. Uygulama

0. Bu governance PR'ı (insan review, Sinan merge eder) `AGENTS.md` "Çalışma orkestrasyonu"
   bölümüne v4 işaretçisini de ekler (v4 §5 metni). AGENTS değişikliği otomatik merge edilemez.
1. Merge → `KARAR_SICILI` / `ACIK_KARARLAR` dar güncelleme PR'ı (koordinatör hazırlar, Sinan
   merge eder): satırlar `GECICI_VARSAYIM`/KISMEN; KAPANDI yapılmaz; DEC-STK-008 Not sütununa
   No 73 eklenir.
2. Her dilim kendi REG değerlerini Settings seed'ine (v1 sürüm, `DELEGE-20261021-<no>`
   etiketi) ve KAT satırını izin kataloğuna (izin adı + aday persona etiketi) yazar; persona
   kartına, rol kodlarına ve identity-seed role'lerine dokunmaz; grant yok.
3. `SORU_SINIFLANDIRMA_175_v2.json` satırları `insana_gider=false`, `karar=DELEGE-20261021`
   olarak güncellenir (uygulama paketi).
