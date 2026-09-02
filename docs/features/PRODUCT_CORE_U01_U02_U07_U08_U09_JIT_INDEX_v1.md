# PRODUCT-CORE-JIT-001 — Ürün çekirdeği U-01/U-02/U-07/U-08/U-09 teslim paketi

> Durum: `BUSINESS_READY / IMPLEMENTATION_GATED`
> Sürüm: `v1`
> Tarih: `2026-09-02`
> Product kaynak snapshot: `main@fdcfa568488089e79b420594311adb8cde642b72`
> Branch-base refresh: `main@9a7f9b0e2bc2c7664094f6cf4d0000b913169145` — PR #183 C-01 disposable browser kapısı Product artifact'larına dokunmaz
> İş sahibi: `ChatGPT-BUSINESS`
> Uygulama sahipleri: `Codex — backend/platform`, `Claude — frontend`
> İlgili epic: `#100`
> Bağlayıcı kararlar: `#98 PRD-SCOPE-001=A`, `#99 PRD-PERM-001=A`

Bu indeks, henüz geliştirmesi başlamamış Ürün ekranları için uygulamaya verilecek iş paketinin tek giriş noktasıdır. Kaynaklarla uzlaştırılmış iş baseline'ı, kabul senaryoları ve kanıt gereksinimleri hazırdır; test kanıtı henüz üretilmemiştir. `BUSINESS_READY`, owner-complete veya implementation GO anlamına gelmez. Üretim kodu platform ve owner kapıları tamamlanmadan başlatılamaz. Bu paket yeni ekran kimliği, endpoint, permission string'i, rol veya muhasebe/stock davranışı icat etmez.

## 1. Teslim kapsamı

| Sıra | Kanonik ekran | Sonuç | Normatif belge | Ana kabul bağı |
|---:|---|---|---|---|
| 1 | `U-08` | Birimler ve dönüşüm kuralları | `PRODUCT_U08_UNITS_CONVERSIONS_JIT_SPEC_v1.md` | `AT-016` |
| 2 | `U-09` | Teknik şablon ve alan sözleşmesi | `PRODUCT_U09_TECHNICAL_TEMPLATES_JIT_SPEC_v1.md` | `AT-015`, `AT-016` |
| 3 | `U-01` | Ürün listesi ve Ürün 360 çekirdeği | `PRODUCT_U01_U02_IDENTITY_LIFECYCLE_JIT_SPEC_v1.md` | `AT-015` |
| 4 | `U-02` | Yeni/düzenle, yaşam döngüsü ve kimlik | `PRODUCT_U01_U02_IDENTITY_LIFECYCLE_JIT_SPEC_v1.md` | `AT-015` |
| 5 | `U-07` | Paket standardı sürümleme | `PRODUCT_U07_PACKAGE_STANDARDS_JIT_SPEC_v1.md` | `AT-017` |
| — | Ortak | Çalıştırılabilir senaryo kataloğu | `../acceptance/PRODUCT_CORE_U01_U02_U07_U08_U09_ACCEPTANCE_v1.md` | `AT-001/002/003/005/006/008/015/016/017/045/049`; eşleşmeyen vakalar local-only |
| — | Ortak | Formül golden tohumu ve geçiş kapısı | `../formulas/PRODUCT_UNIT_CONVERSION_GOLDEN_v1.md` + `../formulas/PRODUCT_UNIT_CONVERSION_GOLDEN_v1.csv` | `AT-016` |
| — | Ortak | Makine-okur golden snapshot zarfı | `../formulas/PRODUCT_UNIT_CONVERSION_GOLDEN_MANIFEST_v1.json` | `AT-016`, `AT-045` |
| — | Ortak | Fail-closed paket/aritmetik/AT/promotion doğrulayıcısı | `../acceptance/PRODUCT_CORE_JIT_VALIDATOR_v1.mjs` | Local integrity gate |
| — | Ortak | Bağımsız descriptor, 11-artifact SHA-256 envanteri ve exact case→AT pin'i | `../acceptance/PRODUCT_CORE_JIT_AUTHORITY_v1.json` | Local authority gate |
| — | Ortak | Negatif ve mutation testleri | `../acceptance/PRODUCT_CORE_JIT_VALIDATOR_MUTATION_TEST_v1.mjs` | Mutation gate |

Uygulama sırası bağımlılık sırasıdır. `U-01/U-02`, `U-08` ve `U-09` sözleşmeleri olmadan; `U-07` ise ürün kimliği ve birim rolleri olmadan üretim davranışı kazanamaz.

## 2. Kaynak önceliği ve yorum kuralı

Çelişkide aşağıdaki sıra uygulanır:

1. `docs/decisions/KARAR_SICILI.md` ve kapanmış owner kararları `#98`, `#99`.
2. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`.
3. `docs/features/PLATFORM_GROUP_SHARED_MASTER_CONTRACT_DRAFT_v1.md` ve issue `#104`.
4. Bu JIT paketindeki iş sözleşmeleri.
5. UI kartları ve HTML prototipi; yalnız görünüm/akış referansıdır.

Prototipteki örnek sayı, fiyat, indirim, kullanıcı, yetki, durum ve doğrudan “aktif ürün oluşturma” davranışı normatif değildir. Kaynak ekran ayrıştırma durumu da korunur:

| Kaynak | Kullanım biçimi |
|---|---|
| `PRODUCT-U-01` | `LOCKED_VISUAL`, `U-01` için `CANDIDATE_EXACT` referans |
| `PRODUCT-U-01A` | `LOCKED_VISUAL`, `U-01` içindeki `EMBEDDED_COMPONENT`; yalnız capability varsa |
| `PRODUCT-U-02` | `LOCKED_VISUAL`, `U-02` için `CANDIDATE_EXACT` referans |
| `PRODUCT-U-03` | `U-02` için `CANDIDATE_MERGE`; ayrı route olarak kilitlenmez |
| `PRODUCT-U-04` | Yalnız duplicate-preview davranış referansı; kanonik ekran sahipliği açık |
| `PRODUCT-U-05` | `LOCKED_VISUAL`, `U-07` için `CANDIDATE_EXACT_REFERENCE`; final mapping değildir |
| `PRODUCT-A-01` | `LOCKED_VISUAL`, `U-08/U-09/AV-02` için `CANDIDATE_SPLIT`; ayrıştırma henüz kabul edilmiş değildir |

`LOCKED_VISUAL` yalnız kaynak görsel bütünlüğünü korur; veri sahipliği, iş kuralı, permission, route veya final kanonik eşleme anlamına gelmez.

## 3. Değişmez iş kuralları

1. Tek bir global `Product` UUID/kod/kartı bütün Göymen şirketlerinde ortaktır. Şirket başına Product kopyası kurulmaz.
2. Şirket ayrımı etiket, lot, paket, stok, sipariş, kabul, üretim, sevkiyat, fiyat, vergi/muhasebe ve işlem snapshot'larında başlar.
3. Product UUID değişmezdir. Görünür kod kontrollü grup prefix + sequence ile üretilir; eski kod yeniden kullanılamayan, aranabilir alias olur. Manuel kod istisnası yalnız `SYSTEM_ADMINISTRATOR` içindir.
4. Ürün sınıfı/grubu ile satın alınabilir, satılabilir, üretim girdisi ve üretilen çıktı rolleri bağımsızdır. Rol kaynağı/provenance korunur; “henüz değerlendirilemedi” sessizce `false` yapılmaz.
5. Fiziksel etiket/lot Product değildir. Product yalnız takip politikasını taşır; menşe çoğunlukla lot niteliğidir.
6. `Draft` Product nihai satış/satın alma/stok/üretim seçicilerinde görünmez. Kullanılmış Product fiziksel silinmez; global inactive yeni kullanımı iki şirkette de durdurur, tarihçeyi bozmaz.
7. Stok, satın alma, satış, fiyat, üretim ve paket birimleri ayrı rollerdir. Her hareket girilen miktarı/birimi ve çevrilmiş birincil miktarı birlikte saklar.
8. Birincil stok miktarı `kg`dır; boru/profilde `kg + adet` zorunludur, metre ve paket ikincil olabilir.
9. Dönüşüm yöntemi yalnız sabit katsayı, sürümlü formül veya işlem anındaki fiilî ölçüm olabilir. UI formülü yeniden hesaplamaz.
10. Teknik teorik ağırlıkta manuel doğrulanmış/standart master değer geometri tahmininden üstündür. Hiçbir teorik değer fiilî stok veya maliyeti ezemez.
11. Paket standardı Product + şirket/tesis kapsamlı, sürümlü, yürürlük tarihli ve onaylıdır. İşlem kendi snapshot'ını saklar; sonraki değişiklik geçmişi değiştirmez.
12. AI yalnız okur, açıklar, önerir, taslak/önizleme üretir. Onay, aktivasyon, permission veya doğrudan yazma yetkisi kazanmaz.
13. V1'de Product read dahil bütün Product yönetim eylemleri yalnız tam `SYSTEM_ADMINISTRATOR` rolüne açıktır; diğer roller default-deny'dır. Teknik permission string'leri platform manifestinden gelmeden uydurulmaz.

## 4. Domain sahipliği

| Veri/davranış | Sahip modül | Bu paketin sınırı |
|---|---|---|
| Product kimliği, yaşam döngüsü, roller, family/template bağı | Product | Normatif |
| Unit rolleri ve Product dönüşüm kuralı | Product + ortak formula registry | Normatif iş sözleşmesi; registry uygulaması gate'li |
| Teknik template ve sürümü | Product | Normatif |
| Paket standardı | Product | Normatif master; işlem snapshot'ı tüketici modülünde |
| Fiziksel lot/etiket/stok | Stock | Product'a taşınmaz |
| Reçete/rota/iş emri | Production | Product template'ına taşınmaz |
| Sipariş/kabul/sevkiyat snapshot'ı | Sales/Purchasing/Stock | Product geçmişini yeniden yazmaz |
| Fiyat/iskonto/vergi/muhasebe | Price/Accounting | Bu pakette veri modeli yok |
| Dosya/ek | Document | Bu pakette binary saklama yok |
| AI runtime, model ve tool allowlist | `ERP-000J` / platform | Capability yoksa görünmez |
| Şirket bağlama, audit, idempotency, auth | Platform/Codex | Server-authoritative; Product tarafından kopyalanmaz |

### 4.1 Durum sözlüğü

| Varlık türü | Durum semantiği | Not |
|---|---|---|
| Product | `Draft`, `Active`, `Inactive` durum kümesi | Create-as-Draft, explicit activation, deactivation ve `SYSTEM_ADMINISTRATOR` reactivation eylemlerinin varlığı kilitlidir; reactivation'ın exact precondition/impact/in-flight davranışı `PRD-LC-001` ve uygulama kapıları altında fail-closed kalır |
| Sürümlü ayar (unit/template/package) | `Draft → Approved → Retired` | Exact enum `PROPOSED_JIT`; published version yerinde değişmez |
| Sürümlü ayarın yürürlüğü | `Effective` | Ayrı zorunlu stored status değildir; Approved + scope + business date aralığından türetilir |

Bu ayrım nedeniyle “approve” bir command/karardır; “effective” ise resolver sonucudur. Slash-state (`Approved/Active`) üretim contract'ı olarak kullanılmaz.

## 5. Ortak ekran kabuğu

Bütün beş ekran aşağıdaki durumları ayrı ve erişilebilir biçimde sunmalıdır:

- ilk yükleme skeleton'ı;
- anlamlı empty state ve filtre sonucu empty state;
- izin yok/non-disclosure;
- ilk sayfa hatası ve yenileme;
- sonraki sayfa hatası ve mevcut sonucu koruyarak tekrar deneme;
- stale/concurrency çatışması ve yeniden yükleme;
- alan seviyesinde doğrulama özeti;
- kaydedilmemiş değişiklik koruması;
- başarılı işlem sonrası server response ile yenileme;
- capability kapalı AI alanının hiç mount edilmemesi.

Frontend, backend'de olmayan bir aksiyonu aktif gösteremez; backend de istemciden gelen `CompanyId`, rol, audit actor, kod sequence veya onay kimliğine güvenemez.

## 6. Uygulama kapıları

| Gate | Sahip | Geçilmeden yapılamaz | Kanıt |
|---|---|---|---|
| `G-PRD-PLATFORM-01` group-shared aggregate allowlist | Codex/platform, `#104` | Product production read/write API, schema ve migration | Architecture + SQL isolation testleri |
| `G-PRD-PERM-01` exact permission manifest/naming | Codex/security | Route/action permission binding | Default-deny ve seed/upgrade testleri |
| `G-PRD-PERSIST-01` DbContext/migration/idempotency/audit altyapısı | Codex | Kalıcı write/activation | Gerçek DB integration kanıtı |
| `G-PRD-UNIT-01` UnitId listesi, boyutlar, precision/rounding/tolerance onayı | Product owner + formula owner | Dönüşüm aktivasyonu | Onaylı registry + golden |
| `G-PRD-TEMPLATE-01` ilk template alan sözlüğü ve formula binding onayı | Product owner | Template aktivasyonu | Sürümlü fixture + validation |
| `G-PRD-PACK-01` package ölçü/approval vocabulary onayı | Product owner | Paket standardı aktivasyonu | Fixture + snapshot contract |
| `G-PRD-ROLE-01` Product rol/provenance/türetim sözleşmesi | Product + Production owner | Product aktivasyonu | `PRD-ROLE-001` owner kararı + negative acceptance |
| `G-PRD-TRACK-01` takip profili ve family minimumları | Product + Stock/Traceability owner | Tracking gerektiren template/Product aktivasyonu | `PRD-TRACK-001` owner kararı + boundary testleri |
| `G-PRD-UI-01` `PRODUCT-A-01` → `U-08/U-09` görsel ayrıştırma kabulü | Product + UX | Piksel kilidi | Ayrı responsive ekran kanıtı |
| `G-PRD-AI-01` ERP-000J capability/tool allowlist | Platform/AI | AI paneli ve aksiyonları | Capability-off/on contract testleri |
| `G-PRD-CI-01` Product JIT validator + mutation shared-workflow wiring | Codex/CI owner | Bu paketin CI tarafından merge kapısı sayılması | Exact komutların görünür step/job olarak nonzero propagation ile çalışması + validator/authority source guard |

`BUSINESS_READY`, bu kapıların geçtiği anlamına gelmez. Gate geçilmemiş/kapanmamışken yalnız schema-free UI shell, fixture tabanlı sözleşme testi ve dokümantasyon çalışması yapılabilir; production etkisi doğuran stub, sahte başarı veya geçici yetki eklenemez.

## 7. Açık politika kaydı

Bu bölümdeki `G-PRD-*` ve `PRD-*` kimlikleri paket-içi izleme anahtarlarıdır; merkezi karar/formül/acceptance siciline kendiliğinden yeni kayıt eklemez.

| Kimlik | Açık konu | Güvenli varsayılan |
|---|---|---|
| `OPEN-007` | Exact yoğunluk/kerf/coating, precision, rounding ve tolerance | Registry onayı olmadan production hesap yok |
| `PRD-API-001` | Endpoint/payload/error code isimleri | Codex platform standardı yayınlanana kadar kavramsal sözleşme |
| `PRD-LC-001` | Inactive → Active için exact validation, impact preview, in-flight işlem davranışı ve gelecekte maker-checker ayrımı | Eylemin varlığını yeniden açmaz; koşullar kabul edilene ve `G-PRD-PLATFORM-01`/`G-PRD-PERM-01`/`G-PRD-PERSIST-01` geçilene kadar command fail-closed, UI disabled/gated kalır |
| `PRD-SRV-001` | Service kartının Product template ailesiyle ilişkisi | Service bu paketin dışında, ayrı scope |
| `PRD-DUP-001` | Duplicate compare ekranının kanonik sahipliği ve merge politikası | Yalnız uyarı/önizleme; auto-merge yok |
| `PRD-UI-SPLIT-001` | `PRODUCT-A-01` görselinin U-08/U-09 ayrımı | İşlevsel ayrım normatif, görsel kilit bekler |
| `PRD-ROLE-001` | Derived/manual/not-evaluated Product rol storage ve türetim sözleşmesi | Provenance korunur; unknown `false` olmaz; çelişkili override yok |
| `PRD-TEMPLATE-FIELD-001` | Exact family/field/requiredness ve structured profil kesit modeli | Prototip alanları seed olmaz; unknown field fail-closed |
| `PRD-TRACK-001` | Family bazlı takip profilleri ve lot/heat/coil/MTC zorunlulukları | Gerçek değerler Product'a taşınmaz; onaysız profil ile Product/template approve/activate edilmez |

## 8. Kaynak izlenebilirliği

| İş kuralı | Ana kaynak |
|---|---|
| Tek group-shared Product ve şirket child sınırı | Owner kararı `#98`; `docs/features/PLATFORM_GROUP_SHARED_MASTER_CONTRACT_DRAFT_v1.md`; issue `#104` |
| Pilot permission/default-deny | Owner kararı `#99` |
| Product kimliği, kod/alias, roller ve lifecycle | `docs/decisions/KARAR_SICILI.md` `DEC-PRD-001/003/004/010`, `DEC-CAR-003` |
| Birim rolleri ve primary quantity | `docs/decisions/KARAR_SICILI.md` `DEC-PRD-006/007`; `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` §7.4, §28 |
| Ağırlık otoritesi | `docs/decisions/KARAR_SICILI.md` `DEC-PRD-009`; `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` §28.2 |
| Paket sürümü ve snapshot | `DEC-PRD-008`; `AT-017` |
| Formula version/golden/snapshot | `docs/decisions/KARAR_SICILI.md` `DEC-PRDCT-004/008`; `docs/formulas/FORMUL_KATALOGU.csv`; `OPEN-007` |
| AI yetki sınırı | `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md` §6; ERP-000J capability bağı |
| Kanonik ekran kimlikleri/eşleme | `docs/ui/CANONICAL_SCREEN_STATUS.csv`, `docs/ui/SCREEN_CATALOG.csv`, ilgili `docs/ui/screens/catalog/PRODUCT-*.md` kartları |
| Ortak auth/audit/idempotency/company kuralları | `AGENTS.md`, `docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md`, kabul matrisi ve platform ADR/contract'ları |

Issue `#100` gövdesindeki bazı durum satırları canlı kapanış/merge durumunun gerisindedir; bu paket owner kararlarını ve canlı `main` içeriğini esas alır. Bununla birlikte `#99` kapanmış ve PR `#103` merge edilmiş olsa da group-shared platform sözleşmesinin kendi durumu hâlâ implementation-blocked/production NO-GO'dur.

Merge öncesinde `main` head, açık PR dosya çakışmaları, owner issue durumları ve source mapping tekrar okunmalıdır. Kaynak snapshot değişirse locked/proposed ayrımı yeniden değerlendirilmeden paket statüsü yükseltilmez.

## 9. Uygulama devri

### Codex — backend/platform

- Kapıların durumunu issue/PR kanıtıyla yayınlar.
- Exact permission/action adlarını platform manifestinde tanımlar; bu belgede yazılan semantik eylemlerle eşler.
- CompanyContext, group-shared allowlist, audit, optimistic concurrency ve idempotency'yi server tarafında uygular.
- Formula/template/package versiyonlarını ve işlem snapshot sınırlarını gerçek DB testleriyle kanıtlar.
- Claude'a OpenAPI/fixture/error vocabulary sağlar; frontend'in tahmin yapmasına gerek bırakmaz.

### Claude — frontend

- Yalnız kanonik ekran kimliklerini ve yukarıdaki kaynak eşlemesini kullanır.
- API'den gelmeyen hesap, permission, status, CompanyId veya AI sonucu üretmez.
- Loading/empty/error/forbidden/stale/validation/unsaved/capability-off durumlarını ayrı uygular.
- `U-08/U-09` görsel ayrıştırması kabul edilene kadar referansı yeni bir kanonik ekran gibi kilitlemez.

### ChatGPT-BUSINESS — acceptance/arbitration

- Açık politikaları owner kararına taşır, çelişkiyi kayıt altına alır.
- `AT-015/016/017` alt senaryolarını ve golden fixture'ı günceller.
- PR'larda yeni iş kuralı, sahte permission veya prototip mock'unun production gerçeğine dönüştürülmesini engeller.

## 10. Çalıştırılabilir paket kapısı

Repo kökünden aşağıdaki iki komut birlikte geçmeden bu paketin exact head'i merge-ready değildir:

```bash
node docs/acceptance/PRODUCT_CORE_JIT_VALIDATOR_v1.mjs
node --test docs/acceptance/PRODUCT_CORE_JIT_VALIDATOR_MUTATION_TEST_v1.mjs
```

İlk komut kanonik `ACCEPTANCE_70.md` anlamlarını, 128 gapless local case'i, U-07/U-08/U-09 içerik ve cross-ref semantiğini, 11 artifact SHA-256 envanterini, exact case→AT haritasını, dokuz CSV/manifest/arithmetic parity vakasını, bağımsız pinned promotion descriptor'ını, mapping digest'ini ve reactivation/idempotency kapılarını fail-closed doğrular. İkinci komut bunların zayıflatılmış varyantlarının gerçekten kırmızıya döndüğünü kanıtlar. `--standalone` yalnız bu paketin dışarı alınmış yerel kopyasını denetlemek içindir; repo merge kapısında kullanılmaz.

Shared CI/workflow dosyası bu lane'in ownership'i değildir. Bu komutların runner'a bağlanması `G-PRD-CI-01` altında Codex/CI owner handoff'udur. Manuel exact-head koşum artifact bütünlüğünü kanıtlar ama shared-CI kapısını kapatmaz; wiring görünür runner step/job ve nonzero failure propagation ile doğrulanana kadar Product JIT promotion/merge **NO-GO** kalır.

## 11. Definition of Ready

Bir ekran geliştirmeye “başlanabilir” sayılabilmesi için:

- ilgili JIT spec içindeki `MUST` kuralları kabul edilmiş;
- bağımlı gate'ler issue/PR kanıtıyla kapalı;
- backend contract veya açıkça işaretlenmiş fixture aynı revizyona bağlı;
- local acceptance case ID'leri test planına taşınmış;
- belirsiz konu `OPEN-*`/`PRD-*` kimliğiyle fail-closed bırakılmış;
- diğer ajanların aktif dosyalarıyla ownership çakışması bulunmamış olmalıdır.

## 12. Definition of Done

Bu doküman paketi tek başına ekranları “Done” yapmaz. Uygulama DoD'u; unit/contract/integration/E2E, yetki ve şirket izolasyonu, gerçek DB kanıtı, responsive + klavye erişilebilirliği, audit/idempotency, golden doğrulama ve source-parity kontrolünün tamamını gerektirir. Kanonik `AT-015/016/017` statüsü yalnız bu kanıtlar birleştikten sonra değiştirilebilir.
