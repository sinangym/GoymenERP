# SALES-ORDER-CORE-JIT-001 — S-01/S-02/S-03/S-04 satış çekirdeği

> Durum: `BUSINESS_READY / IMPLEMENTATION_GATED`
> Sürüm: `v1`
> Tarih: `2026-09-02`
> Kaynak snapshot: `main@80c567a63da8e059ce92aa1b23dad9f4568ad2ca`
> İş ve kabul sahibi: `ChatGPT-BUSINESS`
> Backend/API sahibi: `Codex`
> Frontend/browser sahibi: `Claude`
> Kanonik ekranlar: `S-01`, `S-02`, `S-03`, `S-04`
> Yakın fakat kapalı bağımlılıklar: `S-05`, `S-06`, `PRODUCT-U-06`
> Ana kararlar: `DEC-SAL-001..008`
> Ana kabul bağları: `AT-007`, `AT-021..025`
> Acceptance: `../acceptance/SALES_S01_S04_ORDER_CORE_ACCEPTANCE_v1.md`
> Machine scenarios: `../acceptance/SALES_S01_S04_ORDER_CORE_SCENARIOS_v1.json`
> Authority: `../acceptance/SALES_S01_S04_ORDER_CORE_AUTHORITY_v1.json`
> Validator: `../acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_v1.mjs`
> Mutation suite: `../acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_MUTATION_TEST_v1.mjs`

Bu belge, Satış ilk dalgasının iş sözleşmesini uygulama ekiplerine tam zamanında verir. Bir production schema, OpenAPI, permission manifesti, rol ataması, migration veya aktivasyon onayı değildir. Exact endpoint, payload alan adı, enum/string, permission/capability ve domain hata kodu Codex tarafından owner sözleşmesiyle yayınlanmadan burada uydurulmaz.

`S-05 Fiyat–İskonto–Marj–Risk Kontrolü`, `S-06 Satış Onayları`, Service, AI, belge/PDF, outbox ve tahsis owner'ları bu pakette yalnız açık bağımlılık sınırı olarak tanımlanır. Bu bağımlılıklar kapalıyken sipariş taslağına ait güvenli contract/fixture çalışması yapılabilir; production submit/approve/activate veya downstream etki yapılamaz.

## 1. Kaynak önceliği ve bağlayıcılık

Çelişkide aşağıdaki sıra kullanılır:

1. Son kullanıcı kararları ve `docs/decisions/KARAR_SICILI.md`.
2. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`.
3. `docs/domain/DURUM_MAKINELERI.md` ve `docs/domain/ORTAK_KAYIT_METADATA.md`.
4. Kabul edilmiş ADR/platform sözleşmeleri.
5. Bu JIT spec ve task-local acceptance/scenario paketi.
6. UI katalog/kart/görsel/prototip; yalnız yerleşim ve akış referansıdır.

`SALES_S01_S04_ORDER_CORE_AUTHORITY_v1.json`, her machine `sourceRef` değerini pinned local blob, pinned binary, package-local decision gate veya exact GitHub issue-decision descriptor'ına çözer. Resolver katalogunda bulunmayan ya da pinned path envanterine bağlanmayan kaynak fail-closed'dur. GitHub issue descriptor'ları Product implementation kapanışı değildir; implementation öncesi live state/content yeniden doğrulanır.

### 1.1 Kaynak matrisi

| Kaynak | Bu pakette bağladığı gerçek | Bağlamadığı şey |
|---|---|---|
| `DEC-SAL-001` | Sipariş yaşam döngüsü, iç onay, kaynak, gerçekleşme, faturalama, finansal kapanış ve müşteri onayı ayrı eksenlerdir | Tek birleşik order-status enum'u |
| `DEC-SAL-002` | Cari, ürün, miktar, fiyat, iskonto, para, vade veya risk istisnası değişikliği yeni revizyon ve yeniden onay ister | Exact command/error/approval-role adı |
| `DEC-SAL-003` | Termin üst bilgi, satır ve miktar dilimlerinde izlenir; termin değişikliği auditlidir, yeniden onay istemez | Exact tarih/saat/time-zone ve dilim lifecycle enum'u |
| `DEC-SAL-004` | Satır fiziksel stok, onaylı alış, üretim planı, dış tedarik veya karma kaynakla karşılanabilir; AI yalnız önerir | Tahsis motoru veya fiziksel hareket |
| `DEC-SAL-005` | İptalden önce tahsis, bağlı alış/üretim, irsaliyesiz yükleme, faturalanmamış irsaliye ve eşleşmemiş avans/dekont etkisi gösterilir | Bu owner kayıtlarını Sales içinde kopyalamak veya otomatik terslemek |
| `DEC-SAL-006` | Fatura gruplama aynı şirket, cari, para, vergi, kur, vade/ticari koşul ve e-belge uyumu ister; miktar net irsaliye miktarıdır | Invoice owner davranışını veya düzeltme/kantar farkı politikasını Sales içinde uygulamak |
| `DEC-SAL-007` | İade, red ve düzeltme ayrı ana menü değil; kaynak sipariş ve belge bağını koruyan yaşam-döngüsü görünümüdür | Kaynak geçmişi silmek veya S-01..S-04 içinde yeni return root aggregate uydurmak |
| `DEC-SAL-008` | Araç/sevkiyat, sipariş tarihli nokta vade, fatura vadesi, taksit ve karma vade modelleri desteklenir | Exact payload, taksit algoritması, tatil/time-zone veya muhasebe etkisi uydurmak |
| Master §7.2 | Sipariş header/line ana alanları, altı kilitli Sipariş 360 sekmesi, ayrı miktar eksenleri ve revizyon izi | Prototipteki değer/rol/formülün production truth olması |
| Master §8.1 | Talep/tekliften finansal kapanışa uçtan uca sıra ve kontrol noktaları | İlk dalgada bütün zincirin uygulanmış sayılması |
| Master §9.2 | Cari/Product/Stock/Allocation owner sınırı; sipariş snapshot/referans tüketicisidir | Sales'in foreign owner tablolarına doğrudan yazması |
| Master §11 + domain durum sözleşmesi | Ayrı order state axes ve server-side geçiş ilkesi | UI rozet renginden durum türetmek |
| Master §27.2 | Kanonik `S-01..S-06` ekran adları | Eski DOCX source ID'lerini kanonik ekran sanmak |
| Master §28.10/§28.13/§28.14 | `INV-005`, `SIM-001` ve risk formülleri ile snapshot ilkesi | Açık katsayı/eşik/marj maliyet tabanı |
| Master §29.1 — `AT-007` | İptal ve ters işlem geçmişi silmez | Hard delete veya önceki revision/snapshot/audit bağını görünmez yapmak |
| Master §29.3 — `AT-021` | Taban fiyat altı satış onaysız geçemez | Fiyat tabanı, marj maliyet tabanı, eşik veya onay rolü uydurmak |
| Master §29.3 — `AT-022` | Risk limiti aşımı sessiz serbest bırakılamaz | CARI read modelinde olmayan risk/limit değerini `0` veya passed saymak |
| Master §29.3 — `AT-023` | Kritik **satır** değişikliği mevcut onayı geçersiz kılar; yeni revision yeniden değerlendirilir | Header kararlarını veya eski approval sonucunu bu kanonik satır kabulüne zorla bağlamak |
| Master §29.3 — `AT-024` | Kısmi gerçekleşmede kalan miktar `INV-005` ile owner verilerinden hesaplanır | Eksik downstream projection'ı sahte sıfırla tamamlamak |
| Master §29.3 — `AT-025` | Satış ve satın alma belgeleri owner/discriminator ile ayrılır | Aynı görünür numara/tarih/cari nedeniyle iki belge ailesini birleştirmek |
| `ADR-009` | Append-only audit, correlation/causation ve atomik iş+audit sınırı | İstemcinin actor/audit metadata'sı üzerinde otorite olması |
| `ADR-011` | Kalıcı idempotency, replay authorization ve optimistic concurrency | Her endpoint için otomatik/implicit idempotency |
| UI katalog/kartları | Kanonik/source eşlemesi ve bilgi mimarisi | Permission, CompanyId, formül, state veya transaction kuralı |

## 2. Kanonik ekran ve UI kaynak uzlaştırması

| Kanonik ekran | İş sonucu | Kabul edilen kaynak topolojisi | Bu dalga sınırı |
|---|---|---|---|
| `S-01 Satış Talebi / Teklif Havuzu` | Aynı Sales kaydının Talep ve Teklif ticari aşamalarındaki havuzu; fırsat yalnız opsiyonel kaynak bağıdır | `MASTER3-S-01 → S-01 + S-02`, `CONFIRMED_SPLIT_MERGE_REFERENCE` | Aynı immutable identity S-02 Sipariş aşamasına ilerler; exact geçiş/create/send `SAL-REQ-001` kapanmadan production write yok |
| `S-02 Satış Siparişleri` | Server-side liste + hızlı detay | `MASTER3-S-01 → S-01 + S-02`, confirmed split | Ayrı state axes, company scope ve order discriminator; downstream sayılar owner API yoksa gösterilmez |
| `S-03 Yeni/Düzenle Satış Siparişi` | Rehberli draft form + satırlar | Kanonik kayıtta `MASTER3-S-03/S-04/S-05/PRODUCT-U-06`, `CONFIRMED_SPLIT_MERGE_REFERENCE`; katalogda `PRODUCT-U-06` yalnız candidate embedded selector | Product-only draft form contract; Service, AI, price/risk submit ve approval kapalı |
| `S-04 Sipariş 360` | Satırlar, tahsis, gerçekleşme, faturalar, belgeler, revizyon/geçmiş | `MASTER3-S-02/S-03/S-04/S-07/S-09/S-12`, `CONFIRMED_SPLIT_MERGE_REFERENCE`; `UI-MAP-002` ile source S-12 yalnız Revizyon & Geçmiş sekmesidir | İlk dalgada çekirdek özet, Satırlar ve Revizyon/Geçmiş; owner capability olmayan sekmeler fake zero/empty üretmez |

### 2.1 Kimlik çakışması yasağı

- DOCX source `S-05 — AI ile Satır Oluştur`, kanonik `S-03` içindeki capability-gated yardımcı akıştır; kanonik `S-05` değildir.
- DOCX source `S-06 — Tahsis / Kaynak Yönetimi`, kanonik `ST-01` ve `AL-01..06` ailesine ayrılır; kanonik `S-06 Satış Onayları` değildir.
- `PRODUCT-U-06 — Sipariş İçinde Ürün / Hizmet Seçici`, `S-03/P-04` için `CANDIDATE_SPLIT`, `LOCKED_VISUAL`, `prototype_coverage=NO` kaydıdır. Ayrı Product route'u veya kanonik `U-06` değildir.
- Repo kanonik `U-06`, Fiyat Simülasyonu / Toplu Güncelleme ekranıdır. İki `U-06` anlamı birbirine çevrilmez.
- `S-05` ve `S-06` kanonik kayıtları `DECISION_ONLY_REFERENCE_MISSING` durumundadır; yeni pixel-lock veya source ID üretilemez.

## 3. Dilim sonucu ve bilinçli kapsam dışı

### 3.1 Bu paket neyi hazırlar

- `S-01` için tek Sales record identity üzerinde Talep/Teklif ticari aşamalarının güvenli read-model sınırı ve açık geçiş kapısı.
- `S-02` için company-scoped sipariş listesi, filtre/sort/page projection'ı ve ayrı state axes.
- `S-03` için draft header/line/termin bilgi sözleşmesi, snapshot ve command metadata sınırı.
- `S-04` için 360 çekirdek özet, kilitli tab düzeni, revision/diff/audit davranışı.
- `PRODUCT-U-06` için S-03'e gömülü, server-authoritative Product seçim projection'ı.
- `S-05/S-06` için tüketilecek çıktı sözleşmesi ve fail-closed dependency gate'i.
- `AT-007` ile `AT-021..025` için çalıştırılabilir task-local `CASE-SAL-*` vakaları ve sentetik golden zarfı.

### 3.2 Bu paket neyi yapmaz

- Exact API route, DTO property, enum, domain error code veya permission string yayınlamaz.
- Talep/Teklif/Sipariş için kilitli tek-record identity gerçeğini korur; exact create/edit/send/transition guard'larını tahmin etmez.
- Service kimliği, scope'u, fiyatı veya stok üretmeme davranışını Product'tan türetmez.
- Fiyat, iskonto, marj, risk veya approval hesabını Sales UI içinde yeniden yazmaz.
- Tahsis/rezerv/stok hareketi, yükleme, irsaliye, fatura, açık kalem veya tahsilat üretmez.
- Belge/PDF/OCR/AI sonucu, sahte KPI, sahte count veya fake empty owner tab göstermez.
- Product/Cari master verisini Sales tablosuna ikinci master olarak kopyalamaz.

## 4. Domain sahipliği ve referans sınırı

| Veri/davranış | Owner | Sales'in hakkı |
|---|---|---|
| Party ve global aktiflik | Counterparty | Immutable Party reference + sipariş anı display snapshot'ı |
| CompanyPartyAccount, Customer rolü ve şirket aktifliği | Counterparty | Server doğrulamalı current-company reference + minimal snapshot |
| Teslim adresi/muhatap | Counterparty | Owner version/reference + sipariş anı snapshot; CARI alanını değiştiremez |
| Product kimliği/lifecycle/role/template/unit | Product | Eligible selector projection + immutable reference + order-line snapshot |
| Package standardı | Product/Package owner | Exact approved version/reference + transaction snapshot |
| Fiyat/iskonto politikası | Price owner | Server evaluation/snapshot; UI hesaplayıcı değildir |
| Risk/limit/istisna | Cari/Finance owner | Server evaluation reference/snapshot; Sales ikinci risk defteri kurmaz |
| Sipariş, satır, termin, revision | Sales | Ana owner |
| Approval task/matrix | Approval/Settings owner | Approval reference ve sonucu tüketir; rol/eşik kopyalamaz |
| Tahsis/kaynak | Allocation owner | S-04 read projection; Sales rezerv tablosu kurmaz |
| Gerçekleşme/irsaliye/fatura | Logistics/Document/Invoice owner | Owner projection + referans; ilk dalgada write yok |
| Audit/idempotency/correlation | Platform | Sales command'ıyla aynı atomic boundary; protected metadata server-owned |

## 5. Sipariş aggregate bilgi sözleşmesi

Bu bölümdeki adlar **kavramsal iş alanlarıdır**, OpenAPI/property/enum sabiti değildir. Exact isim, type/nullability/scale Codex contract PR'ında kilitlenir.

### 5.1 Header minimumu

| İş alanı | Zorunluluk | Kural |
|---|---|---|
| Order identity | Server-generated, immutable | UUID/ULID benzeri; client üretmez |
| Company context | Zorunlu, server-resolved | Header/policy üzerinden doğrulanır; body/query override authoritative değildir |
| Visible order number | Server-owned | Exact series/year/prefix `SAL-NUM-001` açık; client kesin numara ilan etmez |
| Revision identity/number | Zorunlu | Rev.0 korunur; approved revision yerinde değiştirilmez |
| Commercial-stage lineage | Zorunlu | Talep → Teklif → Sipariş aynı immutable Sales record identity'sinde ilerler; önceki aşama/revision geçmişi korunur. Exact transition command/guard `SAL-REQ-001` ile gated |
| Customer Party reference | Submit için zorunlu | Global aktif Party |
| Current-company account reference | Submit için zorunlu | Aynı Party, aktif account, Customer rolü, exact CompanyContext |
| Customer snapshot | Zorunlu snapshot | Kod/unvan ve owner version; tarihsel render owner değişiminden etkilenmez |
| Order business date | Zorunlu | Price/risk/effective resolver girdisi; timezone/policy owner contract'ından |
| Currency reference | Submit için zorunlu | Değişikliği kritik revision'dır |
| Price/discount condition refs | Gated | `G-SAL-PRICE-01`; evaluation snapshot olmadan submit yok |
| Due/payment terms | Gated | Master header alanı; exact model `SAL-DUE-001` altında açık |
| Delivery address/contact ref + snapshot | Gated | Counterparty owner version'ı; exact requiredness `SAL-CARI-SEL-001` |
| Sales owner reference | Gated | Exact role/assignment string'i uydurulmaz |
| Separate state axes | Zorunlu | §7 sözlüğü; tek stored summary status yok |
| Created/updated/version/correlation | Zorunlu | Ortak metadata ve optimistic concurrency |

### 5.2 Satır minimumu

| İş alanı | Zorunluluk | Kural |
|---|---|---|
| Line identity/order | Zorunlu | Stable line ID; UI array index'i kimlik değildir |
| Line kind | İlk dalgada Product | Service `G-SAL-SERVICE-01` kapanmadan selectable değildir |
| Product reference | Product line'da zorunlu | Active + sellable + selector-authorized Product |
| Product snapshot | Zorunlu | Code/name/family, Product revision, pinned template/version ve selection evidence |
| Entered quantity + UnitId | Zorunlu | Pozitif decimal; unit registry controlled |
| Primary quantity + UnitId | Submit için zorunlu | Ana stok semantiği kg; approved conversion/rule version ile server sonucu |
| Conversion trace | Submit için zorunlu | Rule/formula version, rounding/tolerance/source; UI hesaplamaz |
| Order unit / price unit | Ayrı | Biri diğerinin yerine kullanılamaz |
| Unit price/currency/price snapshot | Gated | `G-SAL-PRICE-01`; price version/business date/source pinned |
| Discount input/result snapshot | Gated | % veya amount exact vocabulary owner contract'ında; UI neti authoritative hesaplamaz |
| Tax/VAT context | Gated | Exact vergi sahibi/snapshot `SAL-PRICE-001`; fixture/prototip seed değildir |
| Package standard snapshot | Uygunsa | Exact approved version; yeni standard geçmiş satırı değiştirmez |
| Termin slices | Submit için policy'ye göre | Toplam dilim miktarı satır aktif miktarıyla aynı unit semantics içinde uzlaşır |
| Source strategy preference | Opsiyonel taslak | Fiziksel stok/onaylı alış/üretim/dış/karma; tahsis veya hareket yaratmaz |
| Net active/allocated/realized/invoiced/remaining | Server-derived | Ayrı değerler; owner projection yoksa UI `0` uydurmaz |
| Line version | Zorunlu | Aggregate/version contract'ıyla birlikte stale overwrite engeli |

### 5.3 Termin dilimi minimumu

- Stable slice identity ve bağlı line identity.
- Girilen miktar + UnitId; gerekiyorsa primary quantity ve pinned conversion trace.
- Taahhüt/plan business date alanı; exact date-time/time-zone semantiği owner contract'ıyla.
- Header defaultundan üretildi ise source/provenance.
- Değiştiren actor, reason, updated time ve revision/audit linki.
- Termin değişikliği approval'ı geçersiz kılmaz; fakat sessiz ve auditsiz değişiklik yasaktır.

### 5.4 Revision minimumu

- Immutable revision identity ve monoton business revision number.
- Base/superseded revision reference.
- Değişiklik reason'ı, actor/time, correlation ve source channel.
- Alan bazlı before/after diff ve `critical/non-critical` sınıflandırma sonucu.
- Önceki approval/evaluation referansları ile yeni revision'ın approval sonucu ayrı.
- Eski satır/termin/price/product/customer snapshot'ları tarihsel olarak çözülebilir.

### 5.5 Kilitli Sipariş Detay sözleşmesi

`sales-order-detail-locked-decision-v1.0.docx` aşağıdaki bilgi ve hesap ayrımlarını bağlar. Bunlar kavramsal iş alanlarıdır; exact API property, enum, precision, rounding, FormulaId veya permission ilanı değildir.

| Konu | Bağlayıcı davranış | Fail-closed sınır |
|---|---|---|
| Üst gösterim | Ticari aşama, approval ve operation olmak üzere üç üst display axis birlikte görünür. Sipariş Özeti varsayılan tek kompakt yatay satırdır; `Detayı Göster` ile açılır. Identity, revision, customer, Sipariş Tutarı, Operasyon Gerçekleşen, Fatura Gerçekleşen, Kalan, ana miktar, tahsis/operasyon/fatura oranı ve en yakın açık termini owner provenance ile toplar; aktif satırlar ve fiyat hareketleriyle birebir uzlaşır. Kalan = Sipariş Tutarı − Operasyon Gerçekleşen tutarıdır ve TL+USD izlenir. Operasyon yüzdesi mal ve hizmet için ortak gerçekleşme oranıdır; Fatura yüzdesi gerçekleşen/faturalanabilir kısım üzerinden hesaplanır | Üç axis tek stored status/renk altında ezilemez; owner sonucu olmayan KPI/oran/termin `0` veya success sayılmaz. Kalan fatura/tahsis verisinden türetilemez; operasyon ve fatura oranı kilitli basis dışında başka pay/paydaya bağlanamaz |
| Çift para gösterimi | Her önemli ticari tutar TL ve USD karşılıklarını birlikte gösterir; işlem ana parası ayrıca vurgulanabilir. Sipariş ve fatura kur snapshot'ları type/date/source/version bakımından ayrıdır | FX owner sonucu yoksa karşılık `0` veya son-cache ile uydurulmaz; alan unavailable/gated kalır. Exact kur/yuvarlama `G-SAL-FX-01` ile kapalıdır |
| Teslim/taşıma varsayılanı | Header termin, teslim ve nakliye bilgisi satır veya shipment için açık override/provenance taşıyabilir | Override owner contract'ı yoksa sessiz kalıtım veya finansal etki yoktur |
| Efektif kg fiyatı | Sipariş görünümü order amount / teorik kg `T`; fulfillment görünümü fulfillment amount / actual dispatch kg `G`; invoice görünümü invoice amount / invoice-linked actual kg `G` semantiğini kullanır | Payda `0`, eksik veya farklı revision/unit olduğunda sonuç üretilmez; UI hesap otoritesi değildir; scale/rounding açık kalır |
| Faturalanmamış gerçekleşen | `Detayı Göster` açıldığında `operasyon gerçekleşen ticari tutar − fatura gerçekleşen ticari tutar` olarak, aynı commercial revision/currency basis ve pinned FX snapshot'larıyla TL+USD gösterilir. Invoice-linked miktar ayrıca kendi UnitId/provenance alanıdır | Daraltılmış tek satırda zorunluymuş gibi gösterilemez; allocation/order miktarı ticari tutar yerine veya invoice-linked quantity faturalanmış tutar yerine kullanılamaz; farklı currency/FX basis sessiz çıkarılamaz |
| Miktar özeti | Mal KG ve adet eksenleri ayrı görünür; adetli satırın KG karşılığı aynı mal miktarına ikinci kez eklenmez. Service ticari miktarı fiziksel I/O'dan ayrıdır | Unit'siz toplama, adet+KG veya goods+Service-input double count yasaktır |
| Tahsis yüzdesi | Yalnız fiziksel tahsis isteyen satırlar paydaya girer; stockless Service satırı allocation yüzdesini bozmaz | Service'i `0 kg` fiziksel satır gibi paydaya eklemek veya eksik owner verisini tam tahsis saymak yasaktır |
| Service/fason satırı | Ticari Service quantity/fulfilled/remaining, fiziksel input/output miktarlarından ayrıdır; kısa input→output özeti ve çoklu I/O detayına geçiş korunur. Uygun stock varsa fason stok doğrudan sevk kaynağı olabilir | Service fiziksel stok üretmez; aynı fiziksel Product goods ve Service input olarak iki kez sayılmaz; `SRV-SCOPE-001` kapanmadan production satırı yoktur |
| Fiyat ayrıntısı ve hareketi | Baz fiyat, yüzde/tutar iskonto, vade farkı, artı/eksi fiyat farkı, masraf, KDV, tevkifat ve final fiyat ayrı provenance/result alanlarıdır. Invoice aşamasında fiyat hareketini yalnız owner-authorized apply capability kesinleştirir; bu capability'siz accounting actor yalnız taslak hazırlayıp onaya gönderebilir. Hareket geçmişi actor/time/stage/reason ile append-only auditlidir | Sıra, vergi dahil/haricilik, cost basis, precision, exact rol/permission ve onay rotası owner contract olmadan uydurulmaz; fiyat hareketi geçmişi silinmez veya sessiz yeniden yazılmaz |
| Owner eksenleri | Source preference taslak girdidir; yalnız Allocation owner kaynak/tahsis eksenini, yalnız Fulfillment owner gerçekleşen/kalanı, yalnız Invoice owner invoice-linked miktar ve kur snapshot'ını değiştirir | Sales draft save stok hareketi/tahsis/irsaliye/fatura üretmez; owner capability yoksa fake zero/empty yoktur |
| Invoice grouping | `DEC-SAL-006` uyum boyutları company, customer, currency, tax, exchange rate, terms/commercial conditions ve e-document'tır; net dispatch quantity owner girdisidir | Sales ilk dalga invoice gruplamaz; eksik uyumu varsayılan eşit saymaz |
| Vade modelleri | `DEC-SAL-008` gereği araç/sevkiyat, sipariş tarihli nokta, fatura, taksit ve karma modeller ayrı iş anlamlarıdır | Exact date rule, tatil/time-zone, finansal effect ve payload `SAL-DUE-001` kapanmadan yoktur |
| AI sınırı | AI order bağlamında yalnız yetkili ERP fonksiyonları üzerinden öneri/önizleme üretebilir; kritik işlemi kesinleştirmez ve nihai sipariş onayı vermez | AI Product, fiyat, permission, tahsis veya approval authority değildir; `G-SAL-AI-01` kapanmadan panel/ağ isteği mount edilmez |

Kaynak uzlaştırması: kilitli DOCX içindeki statik Şekil 1, `Faturalanmamış Gerçekleşen` değerini kompakt bölgede resmederken §4 KPI tablosu bu değeri açıkça “Detay açıldığında” şartına bağlar. Davranış sözleşmesinde açık tablo hükmü üstündür: daraltılmış tek satır bu KPI'yı göstermek zorunda değildir; değer `Detayı Göster` açıldığında görünür. Şekil yalnız yerleşim referansıdır ve bu görünürlük kuralını genişletmez.

Bu sözleşmenin sentetik efektif-kg ve faturalanmamış-gerçekleşen örnekleri yalnız exact arithmetic oracle'dır. Production promotion; immutable UnitId/price/formula/policy versions, decimal scale, rounding, FX snapshot, owner evidence, real SQL round-trip ve business-owner acceptance olmadan yasaktır.

## 6. Cari seçim sözleşmesi

S-03 customer seçimi, Counterparty owner projection'ını tüketir; CARI CRUD veya risk kartı değildir.

### 6.1 Eligibility

Yeni draftta seçilebilir customer:

1. Global Party aktiftir.
2. Aktif CompanyContext için tam bir CompanyPartyAccount vardır.
3. CompanyPartyAccount aktiftir.
4. Aynı account Customer rolünü taşır.
5. Caller, Sales draft eylemi ve dar Counterparty selection projection'ı için server-authorized'dır.

Default list/filter bu şartları uygular. Direct Party/account ID göndermek seçim proof'u değildir; save ve submit server tarafında yeniden doğrulanır. Pasif/foreign/hidden/Customer rolü olmayan kayıt aynı güvenli non-disclosure sınırında reddedilir.

### 6.2 Projection minimumu

- Party ID, cari code, legal/trade name, kind ve global active state.
- Yalnız aktif CompanyContext'e ait account ID, account active state, birleşik roles ve version.
- Yetkili address/contact seçeneklerinin dar reference + display alanları; owner capability yoksa gösterilmez.
- Bakiye, risk, limit, banka/vergi/sicil veya foreign company account özeti bu selector projection'ına eklenmez.

## 7. Ayrı durum eksenleri ve geçiş sözleşmesi

### 7.1 Bağlayıcı state vocabulary

| Eksen | İş durumları |
|---|---|
| Yaşam döngüsü | `Taslak / Aktif / Kapalı / İptal` |
| İç onay | `Hazırlanıyor / Onay bekliyor / Onaylı / Reddedildi` |
| Kaynak | `Kaynaksız / Kısmi / Tam kaynaklı` |
| Gerçekleşme | `Başlamadı / Kısmi / Tamamlandı` |
| Faturalama | `Yok / Kısmi / Tam` |
| Finansal kapanış | `Açık / Kısmi / Kapalı` |
| Müşteri onayı | `Bekleniyor / Alındı / Vazgeçildi` |

Bu Türkçe iş terimleri canlı domain sözleşmesinin semantiğidir; API enum literal'i ilan edilmez. Master §11'deki `Taslak → Kontrolde → Onaylı → Operasyonda → Kısmi → Tamamlandı/Kapatıldı → İptal` yalnız UI için türetilebilecek özet akış referansıdır; ayrı eksenleri tek stored status'a indirmez.

### 7.2 Geçiş matrisi

| İş eylemi | Bağlayıcı sonuç | Açık/gated ayrıntı |
|---|---|---|
| Yeni draft oluştur | Sipariş draft olarak doğar; stok/tahsis/risk/açık kalem etkisi yok | Diğer eksenlerin exact initial/default değeri `SAL-STATE-001` |
| Draft kaydet | Aynı revision üzerinde izinli draft alanları ve audit/idempotency atomik saklanır | Exact field editability/API contract |
| Kontrole/onaya gönder | S-05 evaluation ve S-06 workflow eksiksiz değilse fail-closed; auto-approval yok | `G-SAL-PRICE-01`, `G-SAL-RISK-01`, `G-SAL-APP-01` |
| Onayla/reddet | Yalnız current revision; eski/stale revision sonucu yeni revision'a taşınmaz | Exact approval type/order/SoD `SAL-APP-001` |
| Kritik alan değiştir | Yeni revision; önceki approval yeni revision için geçersiz; yeniden onay zorunlu | Yeni revision'ın exact initial approval state'i `SAL-STATE-001` |
| Termin değiştir | Tarihçe/audit; yeniden onay yok | Permission/owner ve exact reason policy `SAL-TERM-001` |
| Kaynak/tahsis güncelle | Yalnız Allocation owner sonucu Sales projection'ını değiştirir; stok hareketi değildir | `G-SAL-ALLOC-01` |
| Kısmi gerçekleşme/faturalama | İlgili ayrı eksen ve miktar projection'ı owner olayından güncellenir | İlk dalgada write yok; `AT-024` downstream gate'li |
| İptal önizle | `DEC-SAL-005` etki sınıfları eksiksiz ve revision-bound gösterilir | Owner API yoksa cancel command kapalı |
| İptal uygula | Preview current, yetki/onay uygun ve owner contract'ları başarılıysa lifecycle iptal olur; geçmiş silinmez | Cross-module compensation/partial failure `SAL-CANCEL-001` |
| Kapat | Operasyonel ve finansal kapanış birbirine eşitlenmez | Exact close precondition `SAL-CLOSE-001` |

### 7.3 Kritik değişiklik sınıfı

`DEC-SAL-002` gereği aşağıdakilerin herhangi biri yeni revision ve yeniden onay ister:

- customer Party veya current-company account;
- Product/Service identity;
- quantity;
- unit price;
- discount;
- currency;
- due/payment terms;
- risk exception.

Termin değişikliği `DEC-SAL-003` gereği ayrıca audit edilir fakat yeniden onay istemez. İç not/etiket açıklaması kaynakta “istemeyebilir” olarak geçer; exact allowlist kabul edilmeden implementation bu alanı otomatik non-critical saymaz.

## 8. S-01 — Talep/Teklif Havuzu

### 8.1 Güvenli read projection

S-01 read model en az immutable Sales record ID, ticari aşama (`Talep` veya `Teklif` iş anlamı; exact API literal'i değil), görünen reference number, customer display, owner, business/validity dates, ayrı approval/operation axes, currency/tutar varsa owner snapshot, source/channel, opsiyonel opportunity reference ve version taşır. Talep ve Teklif ayrı aggregate değildir: aynı kaydın aşamalarıdır. Sipariş aşamasına geçiş de identity değiştirmez; aşama/revision geçmişi okunabilir kalır. Fırsat kaydı varsa yalnız lineage/reference'tır ve Sales identity yerine geçmez.

### 8.2 Production write kapısı

Talep/Teklif/Siparişin aynı identity'de ilerlemesi kilitlidir; ancak create/edit/send/expire/win/loss/transition davranışı için exact state machine, numbering, validity, customer acceptance ve revision guard contract'ı yoktur. `SAL-REQ-001` kapanmadan:

- create/convert/send aksiyonu capability olarak yayımlanmaz;
- prototipteki durum/örnek tutar seed edilmez;
- “siparişe dönüştü” ilişkisi client-side state değişikliğiyle kurulmaz;
- S-01 production write veya AT tamamlanma iddiası yapılamaz.

## 9. S-02 — Satış Siparişleri

### 9.1 Liste projection'ı

Minimum görünür bilgi: order number, business date, customer snapshot, currency ve accepted commercial total snapshot, revision, ayrı state axes, next termin summary varsa owner projection, net active/allocated/realized/invoiced/remaining quantities varsa unit-aware owner projection ve updated/version.

Kurallar:

- Query server-side search/filter/sort/page uygular; browser tüm kayıtları indirip yeniden sıralamaz.
- Sales order discriminator zorunludur; purchase document/order satırı aynı listeye karışmaz (`AT-025`).
- Count, total ve KPI yalnız aynı authorized company/filter scope'undan gelir; yoksa uydurulmaz.
- Miktarlar unit'siz toplanmaz. Kg/adet/metre/paket tek sayı yapılmaz.
- `INV-005 = NetAktifSipariş − Gerçekleşen − İptal` owner calculator sonucu olarak tüketilir; UI aynı formülü yeniden yazmaz.

## 10. S-03 — Yeni/Düzenle Satış Siparişi

### 10.1 Form bölümleri

1. Şirket ve draft/revision bağlamı — read-only, server-authoritative.
2. Customer ve teslim bağlamı — Counterparty selector.
3. Ticari koşullar — business date, currency, due, price/discount evaluation references.
4. Satırlar — Product selector, quantity/unit, server conversion, package ve termin.
5. Kaynak stratejisi tercihi — yalnız taslak; allocation yaratmaz.
6. Server validation/etki özeti — S-05/S-06 capability yoksa unavailable/gated.
7. Save Draft; submit/approve ayrı ve gated aksiyonlardır.

Toplu satır gridinde boş placeholder satırlar calculation/payload'a katılmaz. Client satır index'i business identity değildir. Satır silme geçmiş revision'ı yok etmez.

### 10.2 Kavramsal draft mutation zarfı

Aşağıdaki örnek **OpenAPI değildir**; zorunlu bilgi içeriğini gösterir:

```json
{
  "expectedOrderVersion": "server-issued-token",
  "baseRevisionReference": "immutable-revision-reference",
  "orderBusinessDate": "business-date",
  "customerReference": {
    "partyId": "immutable-reference",
    "companyAccountId": "current-company-reference",
    "ownerVersion": "server-issued-token"
  },
  "currencyReference": "registry-reference",
  "dueTermsDraft": "owner-contract-shaped-value",
  "deliveryReference": "counterparty-owner-reference-or-null",
  "lines": [
    {
      "lineId": "stable-client-or-server-reference",
      "expectedLineVersion": "server-issued-token-or-null",
      "lineKind": "business-kind-not-api-enum",
      "productReference": "immutable-product-reference",
      "enteredQuantity": "decimal",
      "enteredUnitReference": "registry-reference",
      "priceDraft": "price-owner-shaped-value",
      "discountDraft": "price-owner-shaped-value",
      "packageVersionReference": "immutable-version-or-null",
      "terminSlices": [
        {
          "sliceId": "stable-reference",
          "quantity": "decimal",
          "unitReference": "registry-reference",
          "businessDate": "business-date"
        }
      ],
      "sourceStrategyPreference": "owner-contract-shaped-value-or-null"
    }
  ],
  "reason": "business-reason-when-required"
}
```

### 10.3 Protected/server fields

Client payload'ı şu değerlerde authority değildir: CompanyId, order/revision number, lifecycle/approval/source/fulfillment/invoice/financial/customer axes, totals, primary conversion result, formula/rule version, price/risk/approval result, actor/role/permission, audit timestamps, correlation/causation, idempotency hash, allocation/stock quantity ve owner snapshots. Server bu alanları ya yok saymaz ya da sessiz kabul etmez; contract'a göre fail-closed doğrular.

### 10.4 Command metadata

- CompanyContext server tarafından doğrulanır.
- Her kritik write tek logical attempt için `Idempotency-Key` taşır.
- Update/revision/termin/cancel command'i current expected version taşır; exact taşıma yeri OpenAPI'de belirlenir.
- Correlation/causation platform sözleşmesine uyar; hostile değer echo edilmez.
- Unknown outcome retry aynı actor, company, operation, normalized payload ve aynı key ile yapılır; yeni key ile kör tekrar yoktur.

## 11. PRODUCT-U-06 — gömülü Product seçim contract'ı

### 11.1 Statü

`PRODUCT-U-06` final kanonik ekran değildir. `S-03/P-04` owner formlarına gömülen `CANDIDATE_SPLIT` bileşendir; locked visual iş kuralı veya route/permission kaynağı değildir. HTML prototype coverage yoktur.

### 11.2 İlk dalga eligibility

Product seçilebilir olmak için:

- global lifecycle Active;
- sellable rolü owner contract'ında kesin/uygun;
- sales UnitId ve primary kg yoluna deterministik approved conversion hazır;
- required template/unit completeness geçmiş;
- caller dar operational-selection projection'ı için server-authorized;
- company-scoped price/package projection isteniyorsa yalnız aktif CompanyContext owner kayıtları görünür.

Draft/Inactive Product selector sonucuna girmez. Direct ID ile save/submit bunu atlayamaz. Product rolü `not-evaluated` ise sessiz `false` veya `true` yapılmaz; selector fail-closed olur.

### 11.3 Projection minimumu

- Product ID, code, display name, family/class ve lifecycle.
- Sellable role sonucu ve provenance/owner evidence reference.
- Sales UnitId, primary UnitId, conversion readiness ve pinned rule/formula version özeti.
- Teknik display özeti ve pinned template/version.
- Current-company package summary yalnız Package owner contract'ı hazırsa.
- Current-company/business-date/currency price candidate yalnız Price owner hazırsa.
- Product master version ve selection proof/revision.

Stok/ATP, diğer şirket fiyat/paket/stock, maliyet/marj, supplier/customer-specific code, lot/heat/coil/MTC veya fake AI score bu projection'ın parçası değildir.

### 11.4 Permission kararı

Owner kararı `PRD-PERM-001=A`, ilk admin pilotunda Product master read/write'ı exact `SYSTEM_ADMINISTRATOR` ile sınırlar. Bunun Sales operasyon selector'ına dar tüketici projection olarak nasıl uygulanacağı kaynakta kilitli değildir. `SAL-PRD-ACCESS-001` kapanmadan non-admin Sales kullanıcısına Product existence/read grant'i uydurulmaz; production selector NO-GO'dur.

### 11.5 Service sınırı

Kaynak görsel Product/Hizmet seçimi gösterse de Service için `SRV-SCOPE-001` kimlik, lifecycle, company scope, stok üretmeme, permission ve fiyat contract'ı yoktur. Service sekmesi/sonucu/line kind'i `G-SAL-SERVICE-01` kapanmadan mount edilmez.

## 12. S-04 — Sipariş 360 ve revision

### 12.1 Kilitli tab düzeni

1. Satırlar.
2. Tahsis/Kaynak.
3. Gerçekleşme/İrsaliye.
4. Faturalar.
5. Sözleşme/Belgeler.
6. Revizyon/Geçmiş.

İlk dalgada Satırlar ile Revizyon/Geçmiş gerçek order read modelinden çalışabilir. Diğer tab capability/owner API yoksa gizlenir veya açık `Kullanılamıyor` durumu gösterir; boş liste, `0`, “yok” veya sahte success ile owner gerçeği iddia edilmez.

### 12.2 Özet

- Order/customer/revision identity.
- Yedi ayrı state axis; her biri metin/ikon ve erişilebilir adla.
- Miktar summary'leri birim ve source'uyla.
- Price/risk/approval sonucu yalnız pinned owner snapshot hazırsa.
- Available actions yalnız server capability + current state preconditions ile.

### 12.3 Revision davranışı

- Approved revision immutable history'dir.
- Kritik değişiklik ayrı revision/diff oluşturur; önceki approval yeni revision'a aktarılmaz.
- Termin değişikliği aynı business order üzerinde auditli değişikliktir; yeniden approval yaratmaz.
- Eski revision açıldığında güncel master Product/Cari/fiyat/paket ile sessiz rerender/recalculate yapılmaz.
- UI before/after diff'i server contract'tan gösterir; client kendi criticality classifier'ını authority yapmaz.

### 12.4 İptal impact preview

Cancel action şu owner etkilerini revision-bound preview'da ayrı gösterir:

- aktif tahsis;
- bağlı satın alma;
- bağlı üretim;
- irsaliyesiz yükleme;
- faturalanmamış irsaliye;
- eşleşmemiş avans/dekont.

Preview eksik owner, unavailable veri, stale order version veya preview hash/revision mismatch durumunda cancel yapılamaz. Preview görmek owner kaydını değiştirmez. Uygulama için exact compensation, partial failure ve approval contract'ı `SAL-CANCEL-001` altında kalır.

## 13. S-05/S-06 yakın bağımlılık sözleşmesi

### 13.1 S-05 sonucu

S-05'in gelecekte üreteceği revision-bound evaluation en az:

- order/revision/company/customer/business-date/currency bağlamı;
- her satır için price version, price unit, quantity/conversion ve discount sonucu;
- list/gross/net, minimum price veya policy comparison;
- risk formül girdileri, before/after risk ve policy/limit reference;
- margin varsa owner-approved cost basis/formula reference;
- pass/warning/approval/block semantiği ve reasons;
- evaluation version/hash, expiry ve audit/correlation

taşır. Exact result enum/string ve endpoint burada ilan edilmez.

`SIM-001 = Liste × (100 − İskonto%) / 100` ve Master risk denklemleri yalnız versioned deterministic backend calculator'da çalışır. Price-floor, risk ve margin politikası/threshold'u UI'da hard-code edilmez. Price/risk owner unavailable ise sonuç `pass` varsayılamaz ve submit/approve kapalıdır.

### 13.2 S-06 sonucu

S-06 gelecekte current revision, immutable evaluation reference, approval reason/type, assigned authority scope, SoD, requester/approver, decision time, expected version ve audit/correlation taşıyan approval task/result üretir. Exact rol adı, approval sırası, eşik veya self-approval istisnası `SAL-APP-001` ve Settings/Approval owner contract'ı olmadan uydurulmaz.

## 14. Hata, empty, loading ve accessibility sözleşmesi

Her ekran/bileşen şu durumları birbirinden ayırır:

- initial loading/skeleton;
- gerçekten veri yok;
- filtre sonucu boş;
- unauthorized/forbidden/non-disclosing;
- validation summary + alan ilişkisi;
- stale optimistic conflict + compare/reload;
- save/retry/idempotent replay;
- offline/500/503 ve unknown write outcome;
- partial/unavailable owner projection;
- dirty form/unsaved navigation;
- capability/gate kapalı;
- başarı + safe correlation/audit reference.

Platform-owned stable sonuçlar mevcut contract'tan tüketilir: invalid correlation/company context metadata, authentication required, forbidden, internal error ve service unavailable. Sales-owned validation/not-found/conflict discriminator'ları ayrı owner manifesti kabul edilmeden literal olarak çoğaltılmaz; UI localized `title/detail` parse ederek iş akışı yönetmez.

Accessibility minimumu:

- klavye ile bütün görev akışı, görünür focus ve skip link;
- form label/description/error association ve validation summary focus'u;
- modal/drawer focus trap, restore ve Escape davranışı;
- tablo başlıkları, sort state, row action ve selection'ın ayırt edilebilir accessible name'i;
- state/critical diff için renk dışı metin+ikon;
- polite/assertive live region'ın doğru kullanımı;
- `1440+`, `1024–1439`, `768–1023`, `<768` reflow; %200 zoom ve %400 reflow;
- reduced motion ve dokunma hedefleri;
- Product selector dialog/listbox pattern'i seçilirse active-descendant/selection/focus contract testleri.

## 15. Audit, idempotency ve concurrency

### 15.1 Read

Read/list/preview, business write, CommandExecution veya stok/tahsis etkisi yaratmaz. Correlation/log/trace uygulanır. Hassas query/cursor/raw payload client log/localStorage/telemetry'ye yazılmaz.

### 15.2 Write

Her kritik command:

- current CompanyContext ve backend authorization;
- expected aggregate/revision version;
- tek logical attempt idempotency key;
- canonical normalized payload fingerprint;
- correlation/causation;
- business reason gereken geçişte reason;
- atomik business write + append-only audit + completed command result

taşır.

Same key/same actor/company/operation/payload tek stable business outcome üretir. Same key farklı actor/operation/payload conflict veya non-disclosing red verir. Farklı key ile aynı expected version yarışında tek winner olur; loser current-vs-expected conflict alır ve otomatik overwrite edilmez. Replay öncesi güncel auth/company/target visibility yeniden doğrulanır.

Sales lifecycle/revision/approval veya cancel olayı başka owner'a güvenilir yayımlanacaksa `ERP-000F` transactional outbox/inbox/dedup kapısı gerekir. Bu kapı yokken event publish edilmiş veya downstream uygulanmış sayılmaz.

## 16. Açık kararlar ve güvenli varsayılanlar

Bu `SAL-*` kimlikleri paket-içi izleme anahtarlarıdır; merkezi `DEC-*`, `OPEN-*`, permission veya API sabiti değildir.

| Kimlik | Açık konu | Güvenli varsayılan |
|---|---|---|
| `SAL-REQ-001` | Tek Sales record üzerinde Talep → Teklif → Sipariş geçiş guard'ları, numbering, validity, send/customer acceptance ve revision ayrıntıları | Aynı immutable identity korunur; S-01 read projection dışında create/send/transition production write yok |
| `SAL-STATE-001` | Order axes initial values, approval→lifecycle ilişkisi, customer approval N/A ve close transitions | Ayrı axes korunur; tek enum veya auto-transition yok |
| `SAL-NUM-001` | Order/quotation/request visible number series, year/prefix ve gap policy | Server-owned opaque/temporary draft reference; production number policy uydurulmaz |
| `SAL-CARI-SEL-001` | Customer account/address/contact snapshot requiredness ve CARI document gates | Yalnız active Customer account; unsupported address/contact capability submit'i açmaz |
| `SAL-PRD-ACCESS-001` | Admin-only Product master read kararı ile Sales operational selector projection yetkisi | Non-admin selector default-deny; exact permission/grant yok |
| `SAL-SERVICE-001` | Service identity/scope/lifecycle/permission/price ve Product selector birleşimi | Service result/line mount edilmez |
| `SAL-PRICE-001` | Price precedence, business date, currency/FX, VAT, discount amount/% ve snapshot contract | S-05 unavailable; UI price authoritative değildir |
| `SAL-MARGIN-001` | Margin cost basis, formula, rounding ve unavailable davranışı | Margin pass/amount üretilmez; approval bypass yok |
| `SAL-RISK-001` | Risk/limit owner projection, threshold, exception validity ve approval route | Risk pass varsayılmaz; submit/approve gated |
| `SAL-APP-001` | Order/price/risk approval types, order, role/scope, thresholds, delegation ve SoD | Hard-coded role/eşik yok; S-06 production NO-GO |
| `SAL-DUE-001` | Araç/sevkiyat, nokta vade ve header/line due payloadının ilk dalga exact modeli | Alan structural draft olabilir; financial effect yok |
| `SAL-TERM-001` | Header default, line/slice date semantics, permission ve exact reason policy | Termin auditli; approval inheritance değişmez; ambiguous submit fail |
| `SAL-CANCEL-001` | Owner impact completeness, preview expiry/hash, compensation ve partial failure | Preview eksikse cancel kapalı; hiçbir owner kaydı otomatik silinmez |
| `SAL-CLOSE-001` | Operasyonel/ticari/finansal close preconditions | Otomatik close yok |
| `SAL-ERR-001` | Sales module stable validation/not-found/conflict code manifesti | Semantic error classes; UI literal tahmin etmez |
| `SAL-PERM-001` | Sales screen/action permission, capability ve role grants | Default-deny; role adı hard-code edilmez |

## 17. Implementation gate matrisi

| Gate | Sahip/bağımlılık | `dependsOnDecisionGateIds` | Geçilmeden yapılamaz | Minimum kanıt |
|---|---|---|---|---|
| `G-SAL-CARI-01` | Counterparty/CARI | `SAL-CARI-SEL-001` | Customer selector, draft submit ve current/historical customer snapshot projection | Active Party+account+Customer role, selected-company non-disclosure, owner version CT+SQL+E2E |
| `G-SAL-PRD-01` | Product `#100/#104`, U-01/U-02/U-08/U-09 | — | PRODUCT-U-06 production selector, Product line submit ve Product/package/unit snapshot geçmişi | Global Product auth, Active+sellable, unit/template completeness, no Draft/Inactive direct-ID bypass |
| `G-SAL-PRD-ACCESS-01` | Product/Sales/Security owner | `SAL-PRD-ACCESS-001` | Non-admin operational Product selection | Owner decision + exact manifest/grant/default-deny/migration-reconciliation evidence |
| `G-SAL-PERSIST-01` | Codex/Sales + platform audit/idempotency | `SAL-REQ-001`, `SAL-STATE-001`, `SAL-NUM-001`, `SAL-DUE-001`, `SAL-TERM-001`, `SAL-CLOSE-001`, `SAL-ERR-001`, `SAL-PERM-001` | Sales schema/migration/draft write ve S-01/S-02/S-04 production read projection/route'ları | Module owner, real SQL, tenant/FK/unique/concurrency, atomic rollback |
| `G-SAL-PRICE-01` | Product Price/U-05 + S-05 | `SAL-PRICE-001`, `SAL-MARGIN-001` | Price/discount snapshot ve `AT-021` production behavior | Deterministic evaluation, version/business-date/currency/unit, golden + policy owner |
| `G-SAL-RISK-01` | C-08/F-13 + S-05 + Settings | `SAL-RISK-001` | Risk/limit evaluation ve `AT-022` | No-double-count formula, current projection, threshold/exception policy, real SQL |
| `G-SAL-APP-01` | S-06/D-05/SY-04 | `SAL-APP-001` | Submit/approve/reject, approval task/result projection ve approved lifecycle behavior | Approval matrix, SoD, exact revision binding, idempotency/audit/browser |
| `G-SAL-SERVICE-01` | `SRV-SCOPE-001` + Service owner | `SAL-SERVICE-001` | Service selector/line | Identity/lifecycle/company scope/unit/price/permission contract |
| `G-SAL-ALLOC-01` | ST/AL + `ERP-000F` if evented | — | Tahsis/Kaynak tab write veya source-axis update | Central allocation contract, atomik/idempotent race, no stock movement |
| `G-SAL-FULFILL-01` | Logistics/Fulfillment | `SAL-DUE-001`, `SAL-TERM-001` | Realization projection, remaining quantity, dispatch linkage ve `AT-024` | Owner event/projection, unit/version provenance, INV-005 calculator, real SQL/E2E |
| `G-SAL-INVOICE-01` | Finance/Invoicing | — | Invoice axis, quantities, tab projection, grouping ve invoice-stage price movement | Invoice owner discriminator, order/revision linkage, real SQL/E2E |
| `G-SAL-DOC-01` | `ERP-000G` belge kasası + `ERP-000H` PDF şablonu ve Document/PDF owner | — | Sözleşme/Belgeler write/export | Vault/reference/security/PDF/audit evidence |
| `G-SAL-AI-01` | `ERP-000J` | — | AI satır taslağı/paneli | Tool allowlist, capability-off/on, preview hash, user confirmation |
| `G-SAL-EVENT-01` | `ERP-000F` | — | Cross-module lifecycle event delivery | Transactional outbox/inbox/dedup/retry evidence |
| `G-SAL-PURCHASE-READ-01` | Purchasing owner | — | Bağlı satın alma source/cancel impact projection | Current-company owner reference, capability, version/completeness ve real SQL/E2E |
| `G-SAL-PRODUCTION-READ-01` | Production owner | — | Bağlı üretim source/cancel impact projection | Current-company owner reference, capability, version/completeness ve real SQL/E2E |
| `G-SAL-FINANCE-READ-01` | Finance/Open-item/Collection owner | — | Eşleşmemiş avans/dekont impact ve finansal kapanış projection'ı | No-double-count, owner reference, capability, version/completeness ve real SQL/E2E |
| `G-SAL-FX-01` | Finance/Settings/FX owner | `SAL-PRICE-001` | Ana para + TL/USD karşılığı ve order/invoice ayrı FX snapshot'ları | Exact source/type/date/version, scale/rounding, stale/unavailable fail-closed ve golden/SQL |

JSON tarafında 16 local owner kararının tamamı en az bir case/source veya implementation gate dependency'sine bağlıdır; bağı kopan karar validator tarafından fail-closed reddedilir.

### 17.1 Production NO-GO özeti

Canlı `main`de Sales veya Product production modülü yoktur. CARI tarafında okuma/link/create capability trenleri ve shell/auth/browser PR'ları hâlâ aktiftir. Bu nedenle:

- S-01/S-02 production route/list GO değildir.
- S-03 customer/Product selector ve draft write GO değildir.
- S-04 yalnız fixture/contract tasarımıdır; owner tabları fake data ile açılamaz.
- S-05/S-06, Service, AI, belge/PDF, event/outbox, tahsis, fulfillment ve invoicing production NO-GO'dur.

## 18. Handoff ve tek-writer sırası

### 18.1 ChatGPT-BUSINESS — şimdi

1. Bu spec, acceptance matrisi ve machine JSON parity'sini korur.
2. `SAL-*` açıklarını `NEEDS-SINAN` veya owner contract'a taşır; para/risk/permission politikasını varsaymaz.
3. Sentetik golden fixture'ı production seed/price/policy olmadığını açık tutar.
4. Backend/frontend PR'larında source-ID, single-status, fake owner total ve permission driftini blocker sayar.

### 18.2 Codex — gate sonrası

1. Önce cross-module selection contracts: CARI customer ve Product operational selector; exact permission/error/OpenAPI.
2. Sales aggregate/state/revision ve SQL persistence owner'ı; tenant/FK/audit/idempotency/concurrency.
3. S-02/S-04 read projection'ları ve S-03 Draft create/update command'i.
4. Deterministic snapshot/resolver bağları; S-05/S-06 yokken submit/approve endpoint'i yayımlamaz.
5. Price/risk/approval owner contract'ları kabul edilince S-05/S-06 orchestration.
6. Tahsis/irsaliye/event işleri ayrı owner treninde.

### 18.3 Claude — accepted contract sonrası

1. Contract/fixture revision'ına bağlı ortak Sales shell ve safe states.
2. `S-01` read-only havuz, gated aksiyonlar.
3. `S-02` liste/hızlı detay.
4. `S-03` draft form + embedded PRODUCT-U-06; dirty form ve keyboard/a11y.
5. `S-04` çekirdek summary/Satırlar/Revizyon; owner tabları capability-gated.
6. S-05/S-06 UI ancak owner contract + capability hazırsa.

Backend hazır değilken frontend fake price/risk/approval/stock/count üretmez; component contract fixture'ı açıkça non-production etiketlenir.

## 19. Aktif CARI/Product PR çakışma sınırı

`2026-09-02` current-main denetiminde açık veya doğrudan bağımlı paylaşılan yüzeyler:

| PR | Dokunduğu paylaşılan yüzey | Sales işi için sınır |
|---|---|---|
| `#161` | karar/backlog/decision-pack/validators | Bu Sales paketi central decision/backlog dosyalarını değiştirmez; `#161` canonical sayılmaz |
| `#181` | Product JIT docs/formulas | Sales yalnız dependency/reference tüketir; Product dosyalarını değiştirmez |
| `#184` | CARI owner docs/spec/acceptance | Sales yeni `SALES_*` dosya adlarını kullanır; CARI owner yüzeyini değiştirmez |
| `#188` | SAB/C-01 K3 workflow ve kanıt sözleşmesi | Sales paketi `.github/**` veya SAB kaynaklarını değiştirmez; genel C-01 yeşili `G-SAL-CI-01` kapanışı değildir |
| `main@d4af2229` (`#169` merge) | shell merkezi auth boundary ve gerçek-browser kanıtı | Sales route/capability adı yine mevcut değildir; frontend/backend route wiring bu docs PR'ın kapsamı değildir |

Bu paket yeni ve paket-özel `SALES_*` dosyalarıyla mevcut PR path'lerine doğrudan çakışmaz. Uygulama branch'i açılırken live main, PR base/head, mergeability ve filename listesi yeniden okunur.

## 20. Definition of Ready ve Done

### 20.1 Spec/fixture Ready

- `CASE-SAL-*` Markdown ↔ JSON parity tamdır.
- Her case source ref, screen, AT, gate ve evidence layer taşır.
- Sentetik golden veriler production seed/policy değildir.
- Açık kararlar fail-closed ve owner'lıdır.
- Paket yalnız bu spec ile beş `docs/acceptance/**` artifact'ından oluşur; production kaynak veya ortak CI dosyasına dokunmaz.
- Standalone artifact doğrulaması: `node docs/acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_v1.mjs --standalone`.
- Mutasyon koruması: `node --test docs/acceptance/SALES_S01_S04_ORDER_CORE_VALIDATOR_MUTATION_TEST_v1.mjs`.
- Tam repo doğrulaması `--standalone` olmadan canonical `ACCEPTANCE_70`, formül kataloğu ve karar sicilini de yeniden okur.
- `G-SAL-CI-01` açıktır: bu iki komut shared workflow'da görünür runner step/job ve nonzero failure propagation ile çalışmadan genel CI yeşili paket promotion kanıtı değildir. Bu docs paketi `.github/**` yazmaz.

### 20.2 Implementation Ready

- İlgili `G-SAL-*` gate'leri issue/PR kanıtıyla kapanmıştır.
- Exact OpenAPI, permission/capability, domain error manifesti ve fixture aynı revision'a bağlıdır.
- Tek backend/frontend writer atanmıştır; aktif CARI/Product PR'leriyle file overlap yoktur.
- Migration/rollback, real SQL ve browser planı vardır.

### 20.3 Done

Spec-only merge hiçbir ekranı Done veya `AT-007`/`AT-021..025` passed yapmaz. Done için unit/contract/real-SQL/browser/a11y, Company isolation, auth, audit/idempotency/concurrency, revision/snapshot, failure/rollback, responsive ve exact source-parity kanıtlarının birleşmesi; P0/P1=0 ve explicit UAT gerekir.
