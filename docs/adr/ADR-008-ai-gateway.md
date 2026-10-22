# ADR-008 — AI Gateway sözleşmesi ve önizleme kimliği

- Durum: **Önerildi; W05.1 kısmi kaynak tasarımı. Kabul edilmiş ADR veya runtime aktivasyonu değildir.**
- Tarih: 2026-10-22
- Kaynak tabanı: `5b3e45dc738f80f64818fa763e35903544e65cdb`
- Sınıf: K3, modüller arası sözleşme ve güvenlik bağlamı

## Karar dayanağı ve kapsam

Bu tasarım `DEC-AI-001`…`DEC-AI-009`,
[`ERP-000J_AI_GATEWAY_CONTRACT_v1.md`](../features/ERP-000J_AI_GATEWAY_CONTRACT_v1.md),
[`AI_YETKI_SOZLESMESI.md`](../security/AI_YETKI_SOZLESMESI.md) ve
[`AI_VERI_GUVENLIK.md`](../security/AI_VERI_GUVENLIK.md) sınırlarını uygular.
[`SINAN_KARARLARI_2026-10-21.md`](../decisions/SINAN_KARARLARI_2026-10-21.md)
No 156, önizleme geçerliliği için **15 dakika** değerini verir: A fazı araçları salt okunur;
yazma için B fazında ikinci onay ve adım yükseltme gerekir. Dolayısıyla TTL bütünüyle
kararsız bir konu değildir. Bu kaynak paketi süreyi çalıştıran politika/registry/seed
uygulaması içermez; verilen UTC creation/expiry değerlerini ve aralarındaki sırayı temsil eder.

[`SINAN_KARARLARI_2026-10-22.md`](../decisions/SINAN_KARARLARI_2026-10-22.md)
W05.1 → W05.2 → W06 geliştirmesini açar. Önceki A-contract-only geliştirme engeli güncel
değildir. Bu açılış yazan araç, sağlayıcı, egress, grant veya production aktivasyonu sağlamaz.
No 156'nın runtime güvenlik sınırları korunur. Bu ADR yeni iş kararı, izin, saklama süresi
veya kapanmış `OPEN-*` kaydı oluşturmaz.

## Önerilen kaynak yapısı

BuildingBlocks içindeki `Ai` ad alanı yalnız türler ve saf fonksiyonlar taşır.
READ, DRAFT ve PREVIEW portları ayrıdır. Gelecekteki WRITE portu değişmez bir sahip-command
isteği ve aynı istekle sonuç sorgusu tanımlar. Handler, DI kaydı, endpoint, tool registry,
provider/SDK/model seçimi, DbContext/repository/SQL erişimi veya domain etkisi bulunmaz.
Bir portun veya `Write` enum değerinin varlığı çalıştırma izni değildir.

`AiToolDescriptorV1`; tool/sürüm/sahip, risk, veri sınıfı, permission ve field allowlist'i
ile input/output schema, field/limit, catalogue, authorization, source ve redaction politika
sürüm kimliklerini taşır. Bunlar onaylanmış sözleşmelere referanstır; gerçek schema,
byte/cardinality bütçesi, timeout, retry, egress matrisi veya kayıtlı katalog yerine geçmez.
Koleksiyonlar kopyalanıp salt okunur tutulur. Tool-id biçim kontrolü registry admission
değildir; örneğin sözdizimsel bir `sql.raw.v1` hiçbir SQL yetkisi oluşturmaz.

`AiServerContextV1` yalnız sunucudan çözümlenmiş şirket, aktör, oturum, scope/authority
sürümü ve correlation/causation ile varsa deployment/prompt-policy referansları içindir.
Modelden gelen `AiToolInputV1.ArgumentsJson` bu kimlikleri belirleyemez. DTO oluşturmak
kimliği doğrulamaz. Sonraki gateway; gerçek scope değerlerini, yetki/capability/SoD ve kaynak
sürümlerini mevcut sunucu otoritelerinden çözmek, komut anında yeniden ölçmek zorundadır.
Dar scope temsil edilemiyorsa company-wide genişletme yapılamaz.

## Kanonik JSON ve değişmez önizleme

`AiCanonicalJson` nested object alanlarını ordinal sıraya alır; önemsiz whitespace'i kaldırır,
array sırasını korur. Bozuk JSON, yorum, trailing comma, çoklu root ve her derinlikte
yinelenen decoded property adı reddedilir. Escape ile aynı adı oluşturan property'ler de
yinelenmedir. Sayılar parse edilip yuvarlanmaz: `1`, `1.0`, `1e0` ayrı token kalır. Teknik
parser derinlik sınırı 32'dir; bu değer iş veya tool bütçesi değildir. Bu temsil RFC 8785
uygunluk iddiası taşımaz.

Tam preview kimliği, sürümlü JSON zarfı içinde context + tool metadata + preview değerlerini
kanonik olarak bağlar. Hedef türü/ID/expected version, command, kullanıcıya gösterilen fark,
kaynak ID/sürümleri, oluşturma/expiry ve bütün kimlik/politika referansları kapsamdadır.
Command ve fark önce kanonikleştirilir, sonra zarfın JSON string alanları olarak açıkça
çerçevelenir. Serbest metni newline gibi ayraçlarla birleştirmek sınır çakışması yaratabilir;
JSON çerçevesi bunu önler. Modelin açıklaması bu kimliğin girdisi değildir.

SHA-256 digest indeks ve tutarlılık kontrolüdür. Sahip-command isteği, saklanan onayın
**tam değer snapshot'ı** ile exact eşleşme ister; yalnız hash sunmak yeterli değildir.
Her alan değişikliği eski snapshot'ı geçersiz kılar. Salt okunur preview kaynak listesi ve
metadata, çağıranın elindeki eski listelerin sonradan değiştirilmesinden etkilenmez.

## Sonuç, belirsizlik ve audit

Başarısız read/draft/preview sonuçları payload veya kaynak taşıyamaz. Alan hata adları yalnız
`Invalid` durumunda mümkündür; denied/not-found/unavailable gibi durumlarda alan adıyla
kayıt varlığı açığa çıkarılamaz. Başarı içeriği eksik olamaz. Bunlar yapısal sınırlar olup
gerçek tenant/permission filtresi ve redaction kabulünün yerine geçmez.

Execution sonucu `Unknown`, `Applied` veya `Refused` olabilir. `NotRecorded` bir execution
sonucu değildir. Sonuç sorgusunda kanıt yoksa `Unknown` kalır; kesin yokluk yalnız aynı tam
preview kimliği ve aynı idempotency anahtarına bağlı, onaydan önceye ait olmayan açık sahip
kanıtıyla temsil edilir.
Bu kanıtı DTO'ya koymak gerçek sahip sorgusunun yapıldığını ispatlamaz. Sonraki owner adapter
kanıtı gerçek durable execution kaydından üretmeli; ağ hatasını yokluk saymamalıdır.

Execute ve FindResult aynı değişmez `AiOwnerCommandRequestV1` zarfını kullanır. Teknik aday
anahtar `ai:<preview UUID>` olarak preview'dan türetilir; sorgu için yeni key/body/context
üretilmez. Sahip domain'in şirket/command-type/payload binding'i, expected version, tek
transaction, outbox ve audit garantileri ayrıca korunmalıdır. Burada resend/retry, execution
store veya otomatik yeniden yürütme yoktur. Bu anahtar biçimi sahip-domain entegrasyonu
incelemesinde doğrulanacak teknik öneridir.

`AiAuditContextV1` schema version 1 ile kimlik, scope/authority ve sözleşme sürümleri,
preview hash/ID, gerçek onay kaydı referansı, onaylayan/zaman ve varsa model/prompt-policy
referanslarını serileştirir. Ham prompt, command, fark, kaynak başlığı/içeriği ve provider
cevabı için alan yoktur. Deployment kullanılmıyorsa null kalır. Onay zamanı UTC olmalı ve
preview aralığında bulunmalıdır; bu saf kontrol güncel saat/expiry kabulü, ikinci kişi,
reauth veya authorization kontrolü değildir. DTO bir onay veya durable audit olayı yaratmaz;
gelecekteki gateway güvenilen onay kaydını yüklemelidir.

## Açık ve ayrı kalan güvenlik işleri

Registry default-deny, strict owner schema, gerçek field/limit değerleri, veri sınıfı mirası,
provider capability, redaction/egress ve kaynak yeniden yetkilendirmesi sonraki uygulamanın
zorunlu girişleridir. Çok gizli veri yerelde kalır; hassas egress varsayılan olarak kapalıdır.
Second approval, adım yükseltme ve kalıcı audit/security-event yönlendirmesi bu pakette yoktur.
`OPEN-020`, `OPEN-018` ve `OPEN-021` durumları korunur. Yeni grant, rol, secret, saklama/imha
davranışı, ücretli fallback veya production etkisi eklenmez.

## Entegrasyon ve kabul durumu

Bu teslim tam olarak üç BuildingBlocks kaynağı, bu ADR taslağı ve yeni unit-test projesinin
iki dosyasından oluşur. Test kodu kanonikleştirme, alan/sürüm kayması, ayraç çakışması,
değişmez koleksiyonlar, veri sızdırmayan sonuçlar, approval snapshot ve Unknown/NotRecorded
ayrımını kapsar; fake provider çağrısını başarı kanıtı olarak kullanmaz.

**Kısmi kaynak; REVIEW-READY/CI-ready değildir.** Merkezi dosyaların mevcut sahipleri,
bu kaynaklar entegre edilirken aşağıdakileri gerçek güncel main ve üretici çıktısıyla
uzlaştırmalıdır:

- `GoymenERP.slnx`: test projesinin gerçek solution üyeliği.
- `tools/verify_backend_test_groups.py` ve ilgili testleri: yeni projenin doğru grupta
  eksiksiz kapsanması; sayı veya baseline elle tahmin edilmez.
- Architecture testleri ve BuildingBlocks kaynak fingerprint'i: üç yeni kaynağın açık kaydı.
- Recovery inventory generator/schema/test/release metadata: yeni proje ve gerçek test
  keşif envanterinin aynı exact source'a bağlanması; eski sayılar kopyalanmaz.

Bu altı-dosya sahipliği merkezi dosyalara yazma yetkisi vermez. Merkezi entegrasyondan sonra
yetkili yürütme aşamasında restore/build, saf unit testleri, test-group/Architecture/recovery
kontrolleri ve fresh exact-head zorunlu CI gerekir. Bu kaynak tesliminde hiçbiri
**çalıştırılmadı (NOT_RUN)**; geçme, skip=0 veya keşfedilmiş test sayısı beyanı yoktur.
İki bağımsız K3 exact-head review ve koordinatör merge kapısı ayrıdır. Kod içeren bu paket
belge PR'larına ait CI istisnasından yararlanamaz.

Gerçek sağlayıcı, SQL, API, browser, UAT ve production kabulü üretilmedi. W05.2 için gereken
üretici/güvenlik sözleşmesi kendi START-GO ve entegrasyonuyla ilerler. Kaynak teslimi işlevin
kullanılabilir olduğu veya teslim sayacının yükseldiği anlamına gelmez; 18/128 değişmez.
