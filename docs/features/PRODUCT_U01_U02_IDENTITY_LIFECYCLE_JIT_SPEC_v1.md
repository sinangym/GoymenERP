# PRODUCT-U01-U02-JIT-001 — Ürün kimliği, liste/360 ve yaşam döngüsü

> Kanonik ekranlar: `U-01 Ürün Listesi + 360`, `U-02 Yeni/Düzenle Ürün`
> Durum: `BUSINESS_READY / PLATFORM_AND_API_GATED`
> Ana kararlar: `DEC-PRD-001..007`, `DEC-CAR-003`, `DEC-GOV-003`, `PRD-SCOPE-001=A`, `PRD-PERM-001=A`
> Ana kabul: `AT-015`

## 1. Amaç

U-01 ve U-02, bütün Göymen şirketlerinin paylaştığı tek Product kimliğini güvenli biçimde aratır, gösterir, Draft olarak oluşturur ve kontrollü yaşam döngüsü işlemlerine hazırlar. Bu ekranlar şirket başına Product kopyası, istemci kaynaklı CompanyId veya otomatik aktivasyon oluşturamaz.

## 2. Kapsam

### U-01

- Product listesi, server-side filtre/sıralama/sayfalama ve Product 360 çekirdek görünümü. Exact opaque-cursor modeli `PROPOSED_JIT`tir.
- Kimlik, kod/alias, sınıf/grup/family, bağımsız roller, lifecycle, template/unit/package completeness ve güvenli ilişki özetleri.
- Yetkili aksiyonlara U-02/U-07/U-08/U-09 geçişi.
- Duplicate warning/compare önizlemesine güvenli geçiş; otomatik merge yok.

### U-02

- Yeni Product'ı Draft oluşturma.
- Draft veya izin verilen Product alanlarını optimistic concurrency ile düzenleme.
- Template/version seçimi, teknik alanlar, unit-role completeness, Product rolleri ve tracking policy.
- Server kod üretimi, admin-only manuel kod istisnası, alias görünürlüğü.
- Aktivasyon öncesi validation/impact preview; kaydetme ile aktivasyonun ayrılması.

## 3. Kapsam dışı

- CompanyProduct/ProductAccount kopyası veya v1 company-specific active policy.
- Fiyat/iskonto/vergi, stok miktarı, lot/etiket, tedarikçi-müşteri ürün kodu, reçete/rota ve işlem yazımı.
- Product duplicate auto-merge, otomatik delete, otomatik activation.
- Excel import uygulaması; ayrı flow/contract gerekir.
- Service ekranı ve Service scope'u.
- Exact route, endpoint, permission string ve error code isimleri.

## 4. Kimlik ve şirket sınırı

1. Product tek immutable UUID, görünür kod ve karttır; iki hukuki şirket aynı kimliği kullanır.
2. Product aggregate üzerinde `OwnerCompanyId`, per-company Product clone veya v1 zorunlu `CompanyProductPolicy` yoktur.
3. İlk sürümde aktif Product her iki şirkette kullanılabilir. Global inactive yeni ticari/operasyonel seçimi iki şirkette de engeller; tarihçe görünür kalır.
4. Şirket/site kapsamı fiyat, package standard/override, tax/accounting ve fiziksel/transactional child kayıtlarında başlar.
5. Child kaydın CompanyId'si server-resolved CompanyContext'ten gelir; client override fail-closed'dur.
6. Şirket taşınabilir paketinde aynı Product UUID/kod referansı korunur; yalnız seçilen şirketin child linkleri çıkar. Aynı UUID + aynı canonical identity bağlanabilir; UUID/kod mismatch sessiz remap veya yeni UUID üretmez.

Platform group-shared allowlist, auth scope ve persistence guard'ları `#104` tamamlanmadan Product production read/write NO-GO'dur.

## 5. Product alan sözleşmesi

### 5.1 Kimlik — server authoritative

| Alan anlamı | Davranış |
|---|---|
| Product UUID | Immutable, server-generated; kullanıcı düzenleyemez |
| Görünür Product code | Grup prefix + sequence; server üretir |
| Code alias | Eski kod aranabilir ve yeniden kullanılamaz; tarihçede kalır |
| Manuel code exception | Yalnız exact `SYSTEM_ADMINISTRATOR`, reason ve audit ile |
| Name/display name | Global etkili alan; değişiklik iki şirketi etkiler |
| Revision/token | Optimistic concurrency; client üretmez |

### 5.2 Sınıflandırma ve roller

- Class/group/family/template birbirinden açıkça ayrılır. `Subgroup` yalnız candidate UI/data-dictionary alanıdır; registry owner kararı olmadan bağlayıcı Product modeli sayılmaz.
- Satın alınabilir, satılabilir, üretim girdisi ve üretilen çıktı rolleri sınıf/gruptan bağımsızdır.
- Bir Product birden fazla role sahip olabilir.
- Rol kaynağı/provenance (`manual`, kabul edilmiş domain ilişkisiyle `derived`, `not-evaluated` semantiği) korunur. Henüz değerlendirilemeyen rol sessizce `false` diye kalıcılaştırılmaz.
- Üretim ilişkilerinden türetilen rol, çelişkili serbest checkbox ile ezilemez. Exact türetim algoritması ve storage vocabulary `PRD-ROLE-001` altında owner kararını bekler.
- Role değişimi downstream impact preview ister; tarihsel işlemleri değiştirmez.
- Supplier/customer-specific Product code v1 dışıdır.

### 5.3 Teknik ve birim bağları

- TemplateId + pinned TemplateVersion ve field values U-09 contract'ına uyar.
- Stock/purchase/sales/price/production/package UnitId rolleri U-08 contract'ına uyar.
- Boru/profil family'sinde kg + adet completeness zorunluluğu aktivasyonda doğrulanır.
- Verified/standard unit weight ile geometry estimate ayrı source/provenance taşır.
- Tracking policy Product'ta olabilir; physical lot/label/origin değeri Product'a konmaz.

### 5.4 Lifecycle ve audit görünürlüğü

Status, created/updated metadata, deactivation reason, last activation/deactivation ve code alias history read-only server alanlarıdır. Exact API field isimleri contract ile kilitlenir.

## 6. Yaşam döngüsü

Minimum bağlayıcı durumlar:

- `Draft`: oluşturma/düzenleme alanı; nihai işlem seçicilerinde görünmez.
- `Active`: validation tamam; yeni izinli işlemlerde seçilebilir.
- `Inactive`: yeni kullanım global olarak kapalı; tarihsel kayıt ve referanslar görünür.

Kurallar:

1. Yeni kayıt her zaman Draft doğar. Prototipteki doğrudan aktif oluşturma davranışı kullanılmaz.
2. Save Draft ile Activate ayrı command ve ayrı audit kaydıdır.
3. Activation; kimlik, benzersiz kod/alias, required template fields, unit-role conversion path, Product roles ve registry/golden kapılarını server'da doğrular.
4. Kullanılmış Product fiziksel silinmez. UI delete sunmaz; deactivation kullanır.
5. Inactive Product geçmiş dokümanlarda çözülür; yeni selector/query'de seçilebilir sonuç olarak dönmez.
6. `SYSTEM_ADMINISTRATOR` için explicit reactivation eyleminin varlığı `PRD-PERM-001=A` ile kilitlidir. Exact validation, impact preview, in-flight işlem davranışı ve gelecekte maker-checker ayrımı `PRD-LC-001` altında kabul edilene; `G-PRD-PLATFORM-01`, `G-PRD-PERM-01` ve `G-PRD-PERSIST-01` geçilene kadar command fail-closed, UI disabled/gated kalır. Bu kapılar eylemin varlığını yeniden açmaz.
7. Status, actor, approval ve reason istemcinin serbest mass-assignment alanı değildir.

## 7. Kod ve alias sözleşmesi

- Normal create'te görünür kod server'ın grup prefix + sequence mekanizmasından gelir.
- Kod üretim yarışı DB uniqueness ile çözülür; “önce kontrol sonra insert” tek başına yeterli değildir.
- Kod değişikliği izinli olduğunda eski kod non-reusable alias olur; alias araması aynı Product'a götürür ve “eski kod” olarak etiketlenir.
- Product code ve alias aynı global namespace'te çakışamaz.
- Manuel kod istisnası permission + explicit reason + duplicate/collision validation gerektirir.
- UI oluşturulacağını düşündüğü kodu placeholder olarak gösterebilir ancak kesin kodu server response gelmeden ilan edemez.

## 8. U-01 liste sözleşmesi

> `PROPOSED_JIT`: Arama alanları, `any/all` filter vocabulary, kolon seti ve opaque cursor bu JIT'in önerdiği API/UI davranışıdır; exact query contract ve canonical mapping kabul edilmeden locked runtime sözleşmesi değildir. Default-deny, global tek Product satırı ve server-side scope/filter değişmezdir.

### 8.1 Filtreler

Minimum semantik filtreler: serbest arama (kod, alias, ad), lifecycle status, class/group/family, Product roles, template, unit/package completeness ve uyarı durumu. Exact query adları API contract'ında kilitlenir.

Arama davranışı:

- Trim/normalization server standardına uyar.
- Alias eşleşmesi canonical Product'ı tek satır döndürür ve eşleşme nedenini belirtir.
- Role filter'ında `any/all` semantiği UI'da açık olur.
- Filter değişince cursor sıfırlanır; stale cursor güvenli recovery sunar.

### 8.2 Liste kolonları

Minimum görünür bilgi: Product code, name, class/family, role özeti, primary stock unit, lifecycle, template/unit/package completeness, last changed. Şirket stok/fiyat sayıları Product master alanı gibi gösterilmez; varsa seçili company child summary olarak açık etiketlenir.

### 8.3 Sayfalama ve count

- Cursor server tarafından opaque üretilir; frontend parse/üretmez.
- İlk sayfa hatası tam hata durumu; sonraki sayfa hatası mevcut listeyi korur.
- Total/count varsa aynı auth/filter/global scope'u kullanır; tahmini ise açıkça etiketlenir.
- Bir Product çok role/alias sahip diye satır çoğaltılmaz.

## 9. U-01 Product 360 sözleşmesi

Minimum bölümler:

1. Kimlik ve lifecycle header.
2. Class/group/family ve bağımsız Product roles.
3. Template/version ve teknik alan özeti.
4. Unit roles/conversion completeness; U-08'e geçiş.
5. Company/site-scoped package standards özeti; U-07'ye geçiş.
6. Alias ve version/audit timeline.
7. Server'ın sahip olduğu güvenli ilişki aggregate'leri; unsupported veriye `0` uydurulmaz.

360, fiziksel stok/lot, fiyat veya muhasebe detayını Product aggregate içine çekmez. İlgili modül route'u yoksa bölüm “capability unavailable” olarak gizlenir/işaretlenir; sahte metrik gösterilmez.

## 10. U-02 create/edit sözleşmesi

### 10.1 Adımlar

1. Kimlik: global name, sınıf/group/family; code mode read-only auto veya admin exception.
2. Roller ve takip politikası.
3. Technical template/version ve server-driven fields.
4. Unit roles ve conversion completeness özeti.
5. Review: duplicate warning, validation, cross-company/global impact ve Draft save.

Adım düzeni UX tarafından uyarlanabilir; veri ve doğrulama sınırları değişmez.

### 10.2 Duplicate kontrolü

- Code/alias exact collision hard block'tur.
- Name/technical similarity yalnız warning/compare üretir; kendi başına kaydı birleştirmez veya engellemez.
- AI duplicate önerisi açıklanabilir candidate listesi döndürür; auto-merge/auto-activate yapamaz.
- `PRODUCT-U-04` ayrı kanonik ekran olarak varsayılmaz; sahiplik kararı gelene kadar dialog/drawer/route seçimi frontend contract'ında geçici kalır.

### 10.3 Validation

- Alan-level hata ilgili input ile programatik ilişkilidir; üstte özet ve ilk hataya geçiş bulunur.
- Server validation tek gerçektir. Client aynı temel kontrolleri erken geri bildirim için yapabilir fakat server sonucunu ezmez.
- Draft eksik kaydedilebilir; activation completeness fail-closed'dur.
- Global etkili editlerde etki uyarısı ve reason zorunludur.

### 10.4 Kaydedilmemiş değişiklik

Route/tab/browser çıkışında unsaved guard; seçenekler “devam et”, “Draft kaydet” ve “değişiklikleri bırak” semantiğidir. Draft save başarısızsa sayfadan çıkılmaz ve kullanıcı girdisi korunur.

## 11. Yetki ve non-disclosure

`PRD-PERM-001=A` uyarınca ilk pilotta U-01/U-02 read dahil tüm Product eylemleri yalnız exact `SYSTEM_ADMINISTRATOR` rolüne açıktır. Diğer bütün roller default-deny'dır; yeni rol, otomatik assignment veya mevcut Organization/Security rolüne örtük grant yoktur.

- Route görünürlüğü security boundary değildir; backend aynı kuralı uygular.
- Direct UUID/code/alias sorgusu permission yokken Product varlığını açığa çıkarmaz.
- Manuel kod, activate/deactivate, unit/package/template yönetimi aynı role bağlı semantik aksiyonlardır; exact permission string platform manifestinden gelir.
- AI kullanıcı yetkisini genişletemez.

## 12. AI sözleşmesi

Capability açık ve kullanıcı yetkiliyse AI şunları yapabilir:

- form alanı taslağı ve açıklama;
- benzer/duplicate candidate önerisi;
- unit/package/template eksikliği uyarısı;
- edit etki simülasyonu.

AI doğrudan Product yazamaz, kod/permission üretemez, aktivasyon/deaktivasyon/onay yapamaz. Öneri ayrı Draft patch + kaynak/açıklama olarak kullanıcıya sunulur; kullanıcı uygulamadan önce diff görür. Capability kapalıysa U-01A paneli hiç mount edilmez.

## 13. Güvenli UI durumları

### U-01

- initial loading, empty corpus, no-filter-result, forbidden/non-disclosure;
- first-page error, next-page error with retained rows, invalid cursor recovery;
- stale selected detail, missing/retired related capability;
- alias match label ve global inactive banner.

### U-02

- create schema/registry loading ve unavailable;
- field validation, server validation, code collision, duplicate warning;
- stale revision/compare, save pending/idempotent retry, save failed with retained input;
- unsaved navigation, Draft saved, activation blocked/ready/success/failure;
- capability-off AI ve permission-denied action.

Responsive düzende bilgi veya aksiyon yalnız hover'a bağlı kalmaz. Dialog focus trap, Escape davranışı, görünür focus ve hata duyurusu erişilebilir olmalıdır.

## 14. Audit, concurrency ve idempotency

- Create/edit/activate/deactivate/code exception/alias işlemleri actor, UTC, reason, before/after, request/correlation id ve global etki bağlamıyla audit edilir.
- ETag/revision zorunludur. Stale edit otomatik overwrite edilmez.
- `PROPOSED_JIT / G-PRD-PERSIST-01`: Global Product command identity'sinin exact storage key'i, actor/payload binding'i, authorization/origin kapsamı ve cross-origin replay modeli henüz production contract'ı değildir. Kabul edilene kadar global Product command/replay persistence çalışmaz; mevcut CompanyId-scoped key aynen taşınmaz, sahte owner CompanyId veya `Guid.Empty/all` scope üretilmez ve stored result okunmaz.
- Kabul edilecek model her logical command için tek stable business outcome'u, replay öncesi güncel permission/group scope reauthorization'ını ve response non-disclosure'ını kanıtlamalıdır. Aynı key'in farklı actor/operation/payload veya kabul edilmiş scope ile davranışı platform contract'ında açıkça kilitlenir; bu belge iki cross-origin modelinden birini seçmez.
- Audit actor, server time, status, sequence ve CompanyContext istemciden authoritative alınmaz.

## 15. Kavramsal backend contract'ları

Exact route adları kilitlenmeden aşağıdaki capability'ler gerekir:

- global Product search/list + server-side deterministik sayfalama; API kabul ederse opaque cursor;
- UUID/code/alias ile Product 360 read;
- Product Draft create/edit;
- activation validation preview ve activate/deactivate command;
- code exception/alias validation;
- duplicate candidate read-only preview;
- template, unit ve package summary navigation contract'ları.

Her response source scope, revision ve capability/permission sonucu açısından server authoritative'dir. Frontend Product'a CompanyId eklemez.

## 16. Kabul bağı

- `AT-015`: Product class/group/family/template ile commercial/operational role bağımsızlığı.
- `AT-001`: authorized company-child görünürlüğü; `AT-002`: global search/list/detail non-disclosure.
- `AT-003`: kritik lifecycle/kimlik değişikliği audit'i; `AT-005`: concurrent edit/command conflict.
- `AT-006`: başarısız save sonrası form girdisinin korunması; `AT-008`: CompanyContext değişiminde açık formun sessizce başka scope'a taşınmaması.
- Kanonik anlamı karşılamayan lifecycle, route, idempotency ve UI vakaları yalnız `CASE-PRD-*` local acceptance olarak kalır; global `AT-*` anlamı üretilmez.
- Local case'ler: `CASE-PRD-U01-*`, `CASE-PRD-U02-*`, `CASE-PRD-X-*`.

## 17. Implementation kapıları

Production schema/API için `G-PRD-PLATFORM-01`, `G-PRD-PERM-01`, `G-PRD-PERSIST-01`; Product activation için ayrıca `G-PRD-UNIT-01`, `G-PRD-TEMPLATE-01`, `G-PRD-ROLE-01` ve tracking gerektiren Product'ta `G-PRD-TRACK-01` zorunludur. Group-shared contract repo'da Draft/NO-GO iken production read/write endpoint veya migration oluşturulmaz.

## 18. Definition of Done

- Global uniqueness, alias race, group-shared allowlist ve company-child isolation gerçek SQL testleriyle kanıtlanır.
- Exact role default-deny, direct-ID non-disclosure ve seed/upgrade davranış testleri geçer.
- Draft/Active/Inactive, used Product no-delete, snapshot immutability ve stale/idempotency testleri geçer.
- U-01 list/detail count/filter ve deterministik sayfalama; U-02 create/edit/validation/unsaved/activation states E2E kanıtlanır. API opaque cursor modelini kabul ederse cursor continuation/recovery testleri de zorunludur.
- API/fixture/frontend field parity ve kanonik route/source parity kontrolleri geçer.
