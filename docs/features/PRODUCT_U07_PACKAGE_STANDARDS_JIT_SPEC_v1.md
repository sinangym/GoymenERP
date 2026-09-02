# PRODUCT-U07-JIT-001 — Paket standartları

> Kanonik ekran: `U-07`
> Durum: `BUSINESS_READY / SCOPE_PRECEDENCE_AND_PLATFORM_GATED`
> Ana kararlar: `DEC-PRD-008`, `DEC-PRDCT-008`, `PRD-SCOPE-001=A`, `PRD-PERM-001=A`
> Ana kabul: `AT-017`

## 1. Amaç

U-07, global Product için şirket/tesis kapsamlı paket standardı sürümlerini taslak, doğrulama, onay ve tarihçe akışıyla yönetir. Yeni sürüm gelecekteki uygun işlemlere uygulanır; sipariş, plan, etiket ve sevkiyat üzerinde saklanan eski snapshot'ı değiştirmez. Stock owner ileride paketi resmi tüketici ilan ederse aynı değişmezlik stok hareketi snapshot'ına da uygulanır.

## 2. Kapsam

- Product + Company kapsamındaki paket standardı ve Site boyutu. Site cardinality/zorunluluk/fallback davranışı `PRD-PACK-SITE-001` kararını bekler.
- Sürümlü paket miktarı/birimi, ürün boyu ve onaylı ağırlık/formül referanslarının yönetimi.
- Effective date, validation, etki önizlemesi, onay ve retirement.
- Paket standardı tarihçesi ve downstream snapshot contract'ı.
- U-01/U-02 Product bağlamından U-07'ye güvenli geçiş.

## 3. Kapsam dışı

- Müşteri istisnasını aynı Product standardı satırında tutmak; ayrı kayıt/owner contract gerekir.
- Fiziksel paketi, etiketi, lotu, paleti veya sevkiyatı master standardı gibi saklamak.
- Sipariş/üretim planı/sevkiyatı yeniden hesaplamak.
- Fiyat, indirim, vergi, paket depo stoğu veya ambalaj malzemesi tüketimi.
- Group-wide standard/ürün override precedence'ını açık owner kararı olmadan production kuralına dönüştürmek.
- Exact approval state adları, API route'u, permission string'i veya tolerance değeri.

## 4. Kapsam ve sahiplik

Product global group-shared kimliktir; Package Standard company-scoped child kayıttır ve Site boyutu `PRD-PACK-SITE-001` kararına tabidir:

```text
Global Product
  └─ Company scope
       └─ Site scope (cardinality/fallback TBD)
            └─ Package Standard lineage
                 └─ Immutable versions
```

Kurallar:

1. Package Standard'ın ProductId'si global Product'a referans verir; Product kopyalanmaz.
2. CompanyId server-resolved context'tir. Client başka şirketin CompanyId'sini yazarak scope değiştiremez.
3. Kabul edilen contract Site'ı kullanıyorsa verilen Site, aynı CompanyId altında ve kullanıcı scope'unda olduğu server tarafından doğrulanır.
4. Başka şirketin standardı listede, count'ta, direct ID'de veya resolver sonucunda sızmaz.
5. V1'de customer exception ayrı kayıttır. U-07 temel Product-package standardını düzenler; customer exception'ın create/edit/precedence davranışı bu paketin dışında kalır.

`PRODUCT-U-05` kaynağındaki “grup standardı veya ürüne özel override” önerisi ile üstün `DEC-PRD-008` şirket/tesis kapsamı arasında delta vardır. `PRD-PACK-SCOPE-001` çözülmeden group-default/override production precedence kurulmaz. Güvenli doküman modeli Product + selected Company + plant/site referansını taşır; site'ın zorunluluğu ve fallback owner kararını bekler.

## 5. Kavramsal model

| Nesne | Minimum bilgi | Değişmez |
|---|---|---|
| `PackageStandardLineage` | immutable id, ProductId, server CompanyId ve karar sonrası Site scope reference | Scope sonradan taşınmaz; Site cardinality/fallback TBD |
| `PackageStandardVersion` | version, status, effective range, reason, approval metadata | Approved içerik yerinde değişmez |
| `PackageComposition` | package quantity + UnitId ve ürün miktar ilişkileri | UnitId'siz sayı yok |
| `PackageWeightBinding` | verified value veya FormulaId/version + source | Teorik/fiilî ayrımı açık |
| `PackageResolution` | işlem tarihinde çözülen standard version ve scope | Server authoritative |
| `PackageSnapshot` | işlemde kullanılan değer, UnitId, version, formula/rounding metadata | Geçmişte immutable |

Exact field/DB/API isimleri Codex contract'ında kilitlenir.

## 6. Minimum paket standardı içeriği

Onaylanacak exact field dictionary `G-PRD-PACK-01` kapısındadır. İş sözleşmesi en az şu anlamları gerektirir:

- ProductId, server CompanyId ve kabul edilen policy gerektiriyorsa Site scope reference;
- package type/reference ve package UnitId;
- package içi miktar + UnitId (ör. adet; exact unit registry'den);
- gerekiyorsa nominal boy/uzunluk + UnitId;
- teorik paket/ürün ağırlığı varsa değer + UnitId veya FormulaId/version;
- verified/standard/geometry-estimate source ve provenance;
- effective-from, optional effective-to, status, revision;
- rounding/tolerance policy reference;
- change reason, approval ve audit metadata.

Kaynakta paket içi adet, adet/boy birimi, standart boy, `kg/boy`, `kg/paket` ve “açık paket” alanları mevcuttur; ancak exact field contract'ları henüz kilitli değildir. “Açık paket”in parçalı miktar/downstream anlamı `PRD-PACK-OPEN-001` çözülmeden production davranışına dönüşmez. Prototype'teki bütün örnek değerler ve formula code'ları seed/golden değildir.

## 7. Doğrulama ve birim ilişkisi

1. Package quantity ve bütün fiziksel değerler pozitif ve UnitId ile verilmelidir; `null`, `0` ve boş aynı anlam değildir.
2. Package UnitId ve içerik birimi U-08 registry/role contract'ıyla compatible olmalıdır.
3. Boru/profil package standardı adet ilişkisini ve kg çözüm yolunu tamamlamalıdır; metre gerekiyorsa boy üzerinden sürümlü kural kullanır.
4. Formula-bound weight için FormulaId/version, parameter values/source, golden, rounding/tolerance zorunludur.
5. Manuel doğrulanmış/standart weight geometri estimate'inden üstündür. Fiilî işlem ölçümü yalnız o işlem snapshot'ında authoritative'dir.
6. NaN/Infinity, zero division, inactive UnitId, unresolved FormulaVersion ve incomplete parameter set approval'ı engeller.
7. Aynı Product + Company + kabul edilmiş Site scope + kullanım bağlamında aynı business date için birden çok uygun Approved version bulunamaz.
8. Effective range overlap approval'da fail-closed'dur; exact boundary inclusivity `PRD-PACK-EFFECTIVE-001` ile kilitlenmelidir.

## 8. Yaşam döngüsü ve sürümleme

> `PROPOSED_JIT`: Exact enum, cancellation, self-approval ve automatic supersede davranışı owner/API kabulünü bekler. Aşağıdaki durumlar minimum önerilen iş semantiğidir; bağlayıcı çekirdek published version immutability ve geçmiş snapshot'ın değişmemesidir.

Önerilen durumlar `Draft`, `Approved`, yürürlük aralığında türetilen `Effective` ve `Retired` semantiğidir. `Approved` ile `Effective` aynı kavram değildir:

- New/Create Draft: production resolver'a girmez.
- Validate/Preview: birim, formül, overlap, scope ve downstream etki kontrolü; yan etkisiz.
- Approve: yalnız yetkili explicit command; başlangıç tarihi, değişiklik nedeni, old/new diff ve güvenilir impact sonucu ister. Approved version yalnız business date effective range içindeyse resolver'da `Effective` olur.
- Change: approved satırı edit etmez, yeni Draft version üretir.
- Retire: yeni işlem çözümünden çıkarır; eski snapshot ve audit okunur.

V1'de tüm eylemler exact `SYSTEM_ADMINISTRATOR` rolündedir ve ayrı Product approver rolü üretilmez. Bu karar otomatik self-approval veya “aynı kişi mutlaka onaylar” teknik kuralı değildir; exact approval/SoD davranışı `PRD-PACK-APPROVAL-001` ile kilitlenir.

## 9. Effective resolution ve açık precedence

Kilitleyebildiğimiz minimum resolver:

1. Server Product, Company, kabul edilmiş scope sözleşmesine göre Site context, business date ve kullanım bağlamını alır.
2. Yalnız aynı Company scope'undaki Approved ve business date'te Effective version'ları değerlendirir.
3. Company/Site/group/customer adayları arasında herhangi bir fallback veya özgüllük sırası uygulanmadan önce `PRD-PACK-SCOPE-001` ve `PRD-PACK-SITE-001` owner onayı gerekir.
4. Müşteri istisnası ayrı owner contract ile resolver'a eklenir; U-07 bunu inline override olarak yaratmaz.
5. Hiç sonuç veya birden çok sonuç varsa resolver fail-closed sonuç döndürür; frontend kendi standardını seçmez.

Dolayısıyla bu JIT paketi scope veri modelini hazırlar; group/company/plant/customer tam precedence tablosunu owner kararı olmadan “kilitli” ilan etmez.

## 10. Snapshot sözleşmesi

Paket standardını kullandığı owner contract ile doğrulanan sipariş, plan, etiket ve sevkiyat kendi sahip modülünde en az şu bilgiyi snapshot'lar. Stok hareketi ancak Stock owner contract'ı bunu tüketici olarak ilan ederse aynı sözleşmeye katılır:

- PackageStandard lineage/version id;
- ProductId, server-resolved CompanyId ve contract Site scope'u kullanıyorsa server-resolved SiteId;
- package type/reference, miktarlar ve UnitId'ler;
- kullanılan FormulaId/version ve parameter/source;
- theoretical/verified/actual niteliği;
- raw/result quantities, rounding/tolerance policy version;
- effective/business date ve calculated/resolved UTC;
- source/reason/correlation.

Yeni U-07 version'ı geçmiş snapshot'a cascade update yapmaz. Eski işlem “latest package standard” ile yeniden render edilmez; kendi snapshot'ını gösterir.

## 11. U-07 ekran sözleşmesi

> `PROPOSED_JIT`: Aşağıdaki kolon, drawer ve aksiyon kompozisyonu `PRODUCT-U-05` candidate reference'ından türetilmiştir; final mapping/API contract değildir. Company isolation, version immutability, snapshot ayrımı ve fail-closed resolution bağlayıcı çekirdektir.

### 11.1 Bağlam başlığı

Global Product code/name/status, selected Company ve sözleşmenin gerektirdiği Site context açıkça gösterilir. Product kimliği ile company scope görsel olarak ayrılır. Company/Site selector yalnız kullanıcının server-authorized scope'larını seçer; payload authority değildir.

### 11.2 Liste

Minimum kolonlar: scope, version, package type, quantity/unit özeti, length/weight özeti, source (verified/formula/estimate), effective range, status, validation, last changed. “Current”, “future”, “retired”, “draft”, “conflict” yalnız renk değil metin/ikonla ayrılır.

### 11.3 Detail/version drawer

Composition, UnitId'ler, formula trace, source/provenance, effective range, approval, downstream impact aggregate'i, version diff ve audit timeline gösterilir.

### 11.4 Editor

- Registry picker'ları; serbest UnitId/formula yok.
- Product/Company scope read-only; yanlış scope için yeni lineage gerekir.
- Conditional package quantity/length/weight fields.
- Save Draft, Validate, Compare, Impact Preview ve Approve ayrı aksiyonlar.
- Preview sonucu stok/sipariş yazmaz.

### 11.5 Geçmiş ve etki önizleme

Yeni version'ın hangi future-dated plan/order akışlarında aday olacağı aggregate olarak gösterilebilir; sahip modül verisi yoksa `0` uydurulmaz. Geçmiş işlem listesi kendi snapshot version'ını gösterir.

## 12. Güvenli durumlar

- Company context yok/geçersiz: write yok; başka company fallback yok.
- Product inactive: tarihçe read-only; yeni Draft/approval/effective davranışı owner lifecycle contract'ına göre kapalı, güvenli varsayılan yok.
- Unit/formula registry unavailable veya golden eksik: Draft okunur/saklanabilir; approval kapalı.
- Overlap/ambiguous resolution: conflict detaylı gösterilir; auto-trim/auto-retire yok.
- Stale version: compare/reload; otomatik overwrite yok.
- Permission yok: route/action default-deny ve direct-ID non-disclosure.
- AI capability kapalı: öneri UI'sı mount edilmez.
- Downstream impact API yok: unsupported state; sahte count yok.

## 13. Yetki, audit ve idempotency

- V1 read/manage/approve/retire yalnız exact `SYSTEM_ADMINISTRATOR`.
- Server CompanyContext, actor, approval, status, version ve audit alanlarını client'tan authoritative almaz.
- Her write/approval/retirement; before/after, ProductId, Company scope, contract kullanıyorsa Site scope, version, actor, UTC, reason, request/correlation ve source ile audit edilir.
- Idempotency scope actor + operation + Company/Site origin scope + normalized payload hash'i içerir. Aynı key/kapsam/payload tek stable business outcome üretir; replay response'u güncel permission/scope ile yeniden authorize edilir. Farklı actor/scope/operation/payload conflict veya non-disclosing red verir.
- Audit/idempotency başarısızsa package write atomik rollback olur. Platform event/outbox'ı bu command için zorunlu kılarsa o kayıt da aynı kabul edilmiş atomic boundary'ye katılır.

## 14. Taşınabilir şirket paketi

Company export yalnız seçilen şirketin package standard child kayıtlarını içerir; global Product aynı UUID/kod referansıyla taşınır. Import'ta same UUID + same canonical identity bind edilebilir; UUID/code mismatch veya yabancı company scope sessiz remap/clone yaratmaz. Snapshot/version referanslarının collision politikası platform portable package contract'ında kanıtlanır.

## 15. Kabul bağı

- `AT-017`: yeni paket version'ı yeni işleme uygulanır; eski işlem snapshot'ı değişmez.
- `AT-001`: authorized company/site package görünürlüğü; `AT-002`: list/detail/search non-disclosure.
- `AT-003`: kritik package change audit'i; `AT-005`: concurrent approval conflict; `AT-006`: başarısız save sonrası form girdisinin korunması.
- `AT-016/045`: package→quantity→kg/metre dönüşümü approved unit/formula ve boru/profil golden'ına bağlanır.
- Resolver outage, idempotency ve genel route/UI vakaları local `CASE-PRD-*` kabulüdür; kanonik anlamı yoksa global `AT-*` ile zorla eşlenmez.
- Local case'ler: `CASE-PRD-U07-*`.

## 16. Açık kararlar

| Kimlik | Karar gereken konu | Güvenli varsayılan |
|---|---|---|
| `PRD-PACK-SCOPE-001` | Group default, Product override, company default, plant override ve customer exception tam precedence'ı | Product+Company açık; Site yalnız kabul edilmiş kararla; ambiguous fail |
| `PRD-PACK-SITE-001` | Site zorunlu mu, cardinality nedir, company fallback var mı ve scope nasıl seçilir | Site/fallback varsayılmaz; ambiguous fail |
| `PRD-PACK-EFFECTIVE-001` | Effective range sınır dahil/dahil değil ve future order business-date seçimi | Overlap approval'ı kapalı |
| `PRD-PACK-FIELDS-001` | Exact package type/quantity/length/weight alan sözlüğü | Onaysız alan production contract olmaz |
| `PRD-PACK-OPEN-001` | Açık paket, parçalı miktar ve downstream etkisi | Production etkisi yok |
| `PRD-PACK-APPROVAL-001` | Exact lifecycle, self-approval ve gelecekte maker-checker davranışı | Explicit permission + command; otomatik approval yok |
| `OPEN-007` (bkz. indeks §7) | Exact yoğunluk/kerf/coating, precision, rounding ve tolerance | Registry onayı olmadan production hesap yok |

## 17. Implementation kapıları

Production için `G-PRD-PLATFORM-01`, `G-PRD-PERM-01`, `G-PRD-PERSIST-01`, `G-PRD-UNIT-01`, `G-PRD-PACK-01`; ayrıca `PRD-PACK-SCOPE-001`, `PRD-PACK-SITE-001`, `PRD-PACK-EFFECTIVE-001`, `PRD-PACK-APPROVAL-001` ve migration/consumer contract kararları gerekir. Kaynak mapping final değilken U-07 görseli `CANDIDATE_EXACT_REFERENCE` olarak ele alınır; functional contract piksel kilidi değildir.

## 18. Definition of Done

- Company isolation, overlap, uniqueness, optimistic race, audit failure rollback ve idempotency gerçek SQL testleriyle geçer. Owner/API contract Site modelini kabul ederse Site isolation da aynı kanıt setine eklenir.
- Approved version immutable; update yeni version; retirement history-safe'dir.
- Eski order/plan/label/shipment snapshot'ı yeni version sonrası byte/semantic olarak değişmez. Stock owner paketi tüketici ilan ederse aynı assertion movement snapshot'ı için de zorunludur.
- Cross-company direct-ID/count/resolver sızıntısı yoktur.
- UI loading/empty/error/forbidden/stale/conflict/gate-disabled durumlarında responsive ve klavye erişilebiliridir.
