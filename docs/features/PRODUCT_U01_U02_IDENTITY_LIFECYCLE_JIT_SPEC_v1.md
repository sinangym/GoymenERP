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

## 19. 12 Eylül 2026 — ilk U-01/U-02 çalışan alt ve yeni Ürün çifti

**TASARLANDI / ürün HTTP–REAL_SQL–browser kabulü NOT_RUN.** Son kullanıcı talimatı, Cari ekibini değiştirmeden `CODEX-BACKEND-URUN` + `CLAUDE-FRONTEND-URUN` çiftini açmıştır; Business ve tek coordinator mevcut sahiplerdir. Bu bölüm mevcut `W1-URUN / #100 / PRD-IDENTITY-001` işinin ilk uygulama girdisidir, ikinci spec/manifest/acceptance ailesi değildir. §1–18 tam hedef olarak korunur; buradaki ilk-alt ayrımı, tamamlanmamış hedefleri DONE yapmaz. Cari #232/cab393aa ve yalnız final gerçek L03 paketiyle güncellenecek kabul matrisi değişmez.

### 19.1 Kaynak ve sıralama

Doğrulanan main `3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f`; mevcut eşdeğer iş #181/`cc2ea1ee4b29b05f98d06158165c9bbf9f4b2220` ve bu dosyadır. Yeni iş çoğaltılmaz. #181'in eski tabanı/merge çakışması ve shared-file yazarlığı coordinator'ın teknik entegrasyon işidir; bu ek rebase, freeze kaldırma veya merge yetkisi vermez.

Son kullanıcı/dated SINAN > [DEC-PRD-001…011][first-decisions] > [Master §7.4/§27][first-master] > ilgili spec/ADR/#100 sırası; [AGENTS][first-agents] güvenlik ve tek-yazar sınırı korunur. [31 Ağustos kaydı][first-dated] ve [10 Eylül kaydı][first-latest] kaynak olarak tüketilir; #229'un insan kapısı ayrıca kalır. [v3 §8][first-v3] ve son talimat gereği ilk sonuç **liste + kart + temel oluşturmadır**. Eski indeksin U-08/U-09-önce sırası bütün ayar ekranlarını Draft oluşturmanın ön şartına dönüştürmez; kullanılan en küçük referans/veri sözleşmeleri aşağıda ayrılır, activation güvenliği kaldırılmaz.

[UI katalog][first-ui]: U-01 = Ürünler/listesi + 360 (`PRODUCT-U-01/U-01A`), U-02 = Yeni/Düzenle Ürün (`PRODUCT-U-02/U-03/U-04` aday birleşimi). Görseldeki değer, doğrudan Active kaydetme veya kaynak-U-04 adı iş kararı/kanonik yeni route değildir. U-03/U-04 Service kapsamı bu ilk Product işine karıştırılmaz.

### 19.2 Kullanıcı sonucu ve ilk-alt sınırı

**U-01 → bir kartı görüntüle → U-02 Yeni Ürün → temel bilgileri Draft kaydet → server UUID/kod/sürümüyle kart → listeye dön, kod/adla bul ve yeniden aç.** Aynı alt, oluşan Draft'ın temel alanlarını güncel sürümle düzeltip yeniden okuma yolunu da kapsar. Eksik teknik/birim bilgileri açık gösterilir; sahte tamlık veya otomatik activation yoktur.

İlk dilimde liste/detail, temel Draft create/edit, bağımsız rol gösterimi, global kimlik/kod ve alias çakışma koruması, arama/filtre, temel eksiklik gösterimi ve güvenli dönüş vardır. Active/Inactive tarihçe okuması desteklenir; **activate/deactivate/reactivate, manuel kod/alias yönetimi, U-07/U-08/U-09'un tam editörleri, Excel, Service ve fiyat** mevcut hedefte ayrı açık altlardır. Code/alias namespace bütünlüğü ilk kayıtta zorunludur; alias değiştirme ekranının sonraya kalması eski alias'ı yeniden kullanılabilir yapmaz. İlk-alt PASS, §18 veya W1-URUN'un tamamının kabulü değildir.

### 19.3 Alanlar ve DTO sınırı

Aşağıdaki adlar mantıksal DTO önerisidir; exact ad/route/enum/maxLength/nullability/error sözleşmesinin tek sahibi Backend'dir ve ilk implementation PR'ında OpenAPI + örnek ile pinler. `name` için önerilen teknik boy1..250, `q` için0..200'dür; bunlar işletme/formül politikası değildir. Yanlış değer sessiz kırpılmaz; kontrol karakteri reddedilir, Unicode ad/aksan ve kodun anlamı korunur.

| Alan / anlam | İlk kayıtta zorunluluk, boş değer ve kural |
|---|---|
| `id`, `code`, `status`, `version`, audit metadata | Server-authoritative. Product UUID server üretimidir ve değişmez; Cari child-UUID kuralı buraya taşınmaz. Kod yapılandırılmış grup öneki+sıra, ilk durum Draft. Create bunları mass-assign edemez. |
| `name` | Trim sonrası boş olmayan ad gerekir; null/boş ret. Aynı ad tek başına unique veya otomatik merge sebebi değildir. |
| `groupId` / kod grubu | Otomatik kod için mevcut, yetkili okunabilen ve prefix kuralı belirli grup referansı gerekir. Sihirli/fake grup/UUID/default şirket yok. Henüz prefix verisi yoksa yalnız gerçek create bunun hazırlanmasını bekler; form/DTO hazırlanabilir. |
| Sınıf ve `familyId` | Grup, aile ve ticari roller ayrı kavramlardır. Draft'ta tamamlanmamış referans boş kalabilir; gönderilmiş bilinmeyen/uyumsuz referans reddedilir. Aileyi ürün adından veya görselden tahmin etme. |
| Satın alınabilir/satılabilir/girdi/çıktı | Aynı üründe çoklu ve sınıftan bağımsız. Kullanıcının açık seçimi ile derived/not-evaluated ayrılır; gönderilmeyen/değerlendirilemeyen rol false değildir. Derived rolün elle değiştirilmesi yok. Exact provenance/rol contract'ı olmadan activation yok; eksik türetim Draft'ın kimliğini bekletmez. |
| `templateId` + `templateVersion`, nitelikler | İkisi birlikte geçerli sürüme bağlı veya ikisi boş. Verilen her nitelik onaylı FieldId/tür/birim profiline uyar; yanlış tür/unknown FieldId ret. Draft'ta eksik gerekli activation alanı saklanabilir ve eksik görünür. Serbest kolon veya ölçüleri adsız sayıya dönüştürme yok. |
| Stok/alış/satış/fiyat/üretim/paket birim referansları | Roller ayrı; verilirse geçerli UnitId. Birincil stok semantiği kg; boru/profil kesin kullanımında kg+adet gerekir. Draft'ta çözülmemiş referans boş/eksik kalabilir; hayalî UnitId veya varsayılan1 dönüşüm yok. Fiyat birim referansı fiyat/tutar görme yetkisi değildir. |
| Takip ve ağırlık bilgisi | Ürün takip politikası fiziksel lot/heat/coil/menşe değildir. Doğrulanmış/standart ağırlık ile geometrik tahmin ayrı kaynaklıdır; bu ilk alt yeni ağırlık hesabı veya stok miktarı üretmez. Desteklenmeyen bilgi0/false yapılmaz. |
| `expectedVersion`, değişiklik gerekçesi | Edit'te okunan Product sürümü zorunlu; örnekte kayıpsız decimal string, client Number dönüşümü yok. Yeni create'e olmayan sürüm uydurulmaz. Global temel edit §10.3 uyarınca etki uyarısı+gerekçe taşır. |

Eksik update alanı değişmez; açık null yalnız nullable alanı temizler. Koleksiyon eksikliği silme değildir. Seçili template/version değişirse mevcut nitelikler sessiz düşürülmez veya yeniden yorumlanmaz; uyumsuz veri açık farkla düzeltilir. Kısmi geçersiz payload'ın yalnız geçerli parçalarını yazma yoktur. `completeness` sunucu sonucudur; tüm modüller hazırmış gibi yüzde/yeşil durum üretilmez.

### 19.4 Komut/okuma sözleşmesi ve gerçek API bağımlılığı

Okunan main modül envanterinde Product yoktur; list/detail/create/edit veya Product izinleri yayımlanmış mevcut API gibi kullanılamaz. Backend önce aynı işte şu tek contract'ı verir: **global list/search, UUID/detail, Draft-create, Draft-basic-edit**, gereken grup/alan/birim referans okumaları; source SHA, request/response/başarı/error örneği, version, yetki ve capability/route eşlemesi. Frontend bitişi veya canlı ortam bunun ön şartı değildir.

Create niyeti (çalıştırılabilir gerçek fixture değil; referans yer tutucu):

```json
{
  "name": "40x40 profil taslağı",
  "groupId": "<sunucunun-sunduğu-kod-grubu-kimliği>"
}
```

Beklenen **önerilen**201 sonucu: tek yeni server UUID/kod, Draft, kayıpsız sürüm ve eksik alan listesi; kesin body Backend OpenAPI'sinden gelir. Kalınlık/kalite/boy/birim dönüşümü örnek addan türetilmez. İki şirketteki yetkili global okumada aynı UUID/kod vardır; ProductAccount, stok, etiket, sipariş, para veya fiyat kaydı oluşmaz.

Edit niyeti: aynı Product'a `{"expectedVersion":"1","changes":{"name":"40x40 profil — gözden geçirilen taslak"},"reason":"Ürün adını düzeltme"}`. Salt okuma/önizleme kayıt yaratmaz. Başarıda canonical değer/sürüm yalnız doğrulanmış response veya Backend'in belirlediği taze GET'ten gelir. Gönderilen taslak server sonucu gibi boyanmaz; 204 seçilirse yeni değer/sürüm için taze GET zorunludur.

Command, audit ve durable idempotent sonuç atomik; same-key replay ikinci ürün/kod/audit etkisi yaratmaz. Unknown sonucu yeni key ile aşma yok: aynı girişim/path/normalize payload/actor ve kabul edilmiş origin kapsamı korunur, güncel yetki ve contract kontrolünden sonra açık retry. Origin değişirse eski girişim otomatik taşınmaz. Global key/audit subject teknik bağını `G-PRD-PERSIST-01` altında Backend pinler; mevcut CompanyId-keyed store veya Cari `optional-trade-roles-v1` başlığı Product'a otomatik kopyalanmaz. `CompanyId=null/Guid.Empty`, keyfî owner veya query-filter bypass çözüm değildir.

**Yetki hazır kısmı:** [#98][first-scope] tek global Product; [#99][first-perm] ilk pilotta read dahil yalnız exact mevcut SYSTEM_ADMINISTRATOR grantee, diğerleri default-deny; ikinci genel approver MVP şartı değildir. Yetki server-owned açık group scope üzerinden doğrulanır, yalnız şirket ataması/JWT/UI/rol adı kontrolüyle global grant türetilmez. Exact izin adları/registry teknik kaynağını Backend mevcut #104/#103 güvenlik owner'ıyla koordine eder. Yeni rol veya otomatik assignment yok; bu belge fiilî kullanıcı ataması yapmaz; mevcut kurulum uzlaştırması yalnız onaylı bundle için add-only/idempotent/çakışmada fail-closed; bu belge production seed çağırmaz. Fiyat, şirket-child veya Cari-master onayı bu yetkiden çıkmaz.

**12 Eylül 2026 — açık kullanıcı kararı:** Sinan, CODEX-ORCHESTRATOR görevinde “İlk pilotta ortak Ürün yetkilerinin açık kullanıcı atamalarını ve geri alınmasını yalnız ben belirleyeceğim. Şirket adminliği otomatik ortak Ürün yetkisi vermesin.” demiştir; soru tarihçesi ve HUMAN-DECISION [aynı #66 kaydındadır][first-grant-issuer]. İlk pilot atama/geri alma karar sahibi yalnız Sinan’dır; #99A exact `SYSTEM_ADMINISTRATOR` alıcı rolü değişmez. Herhangi bir veya bütün şirketlerde admin olmak global eligibility üretmez; otomatik assignment/backfill yoktur. Bu karar belirli kişiye fiilî grant veya Sinan’ın yerel Identity ID eşlemesi değildir. Sunucuda doğrulanmış açık atama/geri alma kaydı ve güncel yetki bağı olmadan erişim default-deny kalır; teknik kimlik/issuer/provider bağını aynı Backend/Identity sahibi pinler. Kararın yanıtlanması insan review, tam #104 schema/runtime GO, deployment veya production onayı değildir.

### 19.5 UI, hata ve geri dönüş

| Durum / kullanıcı adımı | Ölçülecek davranış |
|---|---|
| U-01 ilk yükleme, gerçek boş, filtre boş | Ayrı durum ve anlaşılır aksiyon; hata/izin yokluğu boş sonuç değildir. Kod, ad, global durum, grup/aile ve desteklenen rol/birim/eksiklik özeti gösterilir. Stok/fiyat yoksa0 veya sahte özet yok. |
| Kod/alias/ad ara; durum/grup/aile/rol filtresi | Sunucu araması, tek Product satırı; alias aynı karta ve eski-kod etiketine gider. Unknown filtre ret; any/all seçilirse anlamı açık. Backend arama/sort/page/cursor limitlerini pinler; frontend tüm veri veya company fan-out yapmaz. |
| Sonraki sayfa, filtre değişimi, hata | Stabil server sıralaması+UUID tie-breaker; filtre/sıralama değişiminde eski devam token'ı bırakılır. İlk hata tam durum; sonraki hata önceki satırları korur. Yenileme eski önbelleği yeni sonuçmuş gibi sunmaz. |
| U-02 Draft kaydet/düzenle | Gerekli alanlar, readonly server kimliği, ortak-grup etki açıklaması, tek belirgin kaydet. Desteklenmeyen activation/diğer ekran aksiyonu çalışıyor görünmez. |
| Validation / kod-alias collision / stale version | Tüm yazma reddedilir; taslak ve erişilebilir hata özeti korunur. Alan pointer'ı varsa exact contract'la input'a bağlanır; localized detail parse edilmez. Stale'de güncel sürüm/farkı kullanıcı incelemeden rebase/overwrite yok. |
| 401/403/non-disclosing404, capability/contract bilinmiyor | Backend güncel yetki denetler; ürün/alias/count sızmaz. UI kullanılabilir yetkiyi tahmin etmez; hazır olmayan action yazma isteği göndermez. |
| Başarısızlık/unknown → devam | Yanlış başarı veya çıkış yok; aynı-key retry ve hatayı düzeltip işi bitirme. Post-success GET hatası önceki yazmayı geri almış sayılmaz; yalnız read recovery, kör ikinci write yok. |
| Dirty form/şirket/oturum/API bağlamı değişimi/geç yanıt | Devam et, Draft kaydet, değişiklikleri bırak seçenekleri; unknown için rollback iddiası yok. Grup ürünü aynı olsa da eski yetki/child/cevap yeni bağlama taşınmaz. Fiyat/şirket bilgileri sızmaz. |
| Kart → liste → yeniden aç, klavye | Aynı UUID ve canonical kod; güvenli iç route, aynı yetki bağında arama/filtre/sayfa ve odak korunur. Tab/Enter, görünür focus, label/hata ilişkisi ve dar ekran görevi ölçülür; yalnız screenshot kabul değildir. |

### 19.6 W1-URUN'un gerçek alt bağımlılıkları

[Mevcut backlog][first-backlog] `W0-SHELL/W1-SETTINGS` kenarları silinmez, DONE yapılmaz; aşağıdaki eylem kapsamıyla tüketilir. Ortak dosya/ortam atamasını coordinator yapar, Sinan'a işletme sorusu değildir.

| Mevcut iş/kapı | Şimdi gereken en küçük teslim / sahibi | Bütün ilk Draft işini bekletmeyen kalan |
|---|---|---|
| W0-SHELL / G-PRD-UI-01 | Mevcut auth/company kabuğu, U-01/U-02 route+server capability, hata/cache/context sözleşmesi. URUN frontend kendi alanında, ortak mount mevcut tek-yazarda. | Cari L03 veya genel shell'i yeniden yazmak yok. Ürün route'u, Cari capability açık diye açık sayılmaz. |
| W1-SETTINGS + PRD-IDENTITY-001 | Kod üretiminde kullanılan gerçek grup/prefix kaydı ve gereken referans okumaları. Ortak UnitId kaynağı kullanılır; Product'ın aile/template sahipliği Product'ta kalır. | Finans/kur/tolerans/maliyet ayarları ve bütün Settings yönetim ekranları yokken eksik Draft saklanabilir. Prefixi olmayan create ise açık teknik bağımlılıktır. |
| G-PRD-PLATFORM-01/PERM-01/PERSIST-01; #104 | Mevcut kilitli semantiğe uygun exact shared-master allowlist, group auth, izin registry, global audit/idempotency, schema/unique/race bağı. §19.4’te ilk pilot grantissuer Sinan olarak belirlenmiştir; fiilî kullanıcı ataması/Identity ID eşlemesi veya runtime kabulü yapılmamıştır. Aynı Backend/Identity ve Backend-URUN, açık kaydı ve güncel geri-alma denetimini mevcut owner sınırında pinler. | Grantissuer sorusu yeniden sorulmaz; teknik güvenlik kapısı atlanmaz. Typed read validation, model/DTO/fixture, terminal store ve transaction-consumer hazırlıkları bağımsız ilerler; ilgili review ve yetki bağı olmadan schema/public persist veya izin veren global runtime açılmış sayılmaz. |
| PRD-TEMPLATE-001 / U-09 | Seçilen şablon varsa FieldId/tür/null/ölçü birimi+immutable sürüm read'i; Product owner. | Tam şablon yönetimi, bütün aileler ve breaking migration ekranları ilk Draft şartı değil. Eksik şablon activation'ı bekletir. |
| PRD-UNIT-001 / U-08; OPEN-007 | Verilen unit referansının gerçek registry kimliği; fiziksel Product için kg, boru/profilde adet gereksinimi açıklaması. | Formül/yoğunluk/kerf/yuvarlama/tolerans seçilmez. Dönüşüm yolu ve doğrulanmış golden yalnız ilgili kesin kullanım/activation hesabının kapısıdır. |
| U-07/paket, Price, Service, Excel, AI, Sales/Stock | İleride versioned owner read/write; bugün gereksiz çağrı/placeholder yok. DEC-PRD-005/008/009/011 ve AT-016…020 mevcut kapsamda kalır. | Tam paket/fiyat/import, Service scope, finans veya dış entegrasyon ilk Draft'ı bekletmez. Sahip kayıt yokken bölüm tamamlanmış sayılmaz. |

### 19.7 İlk altın gerçek kabulü — mevcut CASE/AT kimlikleri

Hepsi **hazırlanan beklenti / NOT_RUN**; eski #181 validator/mutation sonucu ürün uygulama kanıtı değildir. Tam [mevcut kabul kataloğu][first-cases] ve 128 exact CASE→AT bağı değişmez. Burada bir vakaya yalnız yapılan altı kadar kanıt bağlanır; ilk dilim bütün U01/U02/U07/U08/U09 matrisini kapatmaz.

| Mevcut kabul bağı | Gerçek adım / beklenen kalıcı etki | Kanıt ve ilk-alt sınırı |
|---|---|---|
| CASE-PRD-U02-001/002/018, U01-001/002; X-003 | Yetkili admin Draft oluşturur; server UUID/kod ile karta, listeye ve yeniden karta gider. Tek Product,0 ProductAccount/işlem hareketi; korunan alan enjeksiyonu reddedilir. | HTTP+REAL_SQL ve gerçek browser aynı kaynak/fixture bağıyla; eksik alanlı Draft Active sayılmaz. |
| CASE-PRD-U01-003; U02-005/006 | Aliaslı kayıt kod/alias ile aynı tek sonuç; yeni kod mevcut kod/alias ile yarışır/çakışır. | DB global uniqueness+rollback; alias yönetim ekranı yapılmamışsa onun kabulü değil. |
| CASE-PRD-U01-004…007; U02-007/008/021 | Aynı sınıftaki ürünlere farklı manuel ticari roller, aynı ürüne çoklu rol; arama/filtreden sonra sınıf ve provenans değişmez. | AT-015'in karşılanan altı; derived/not-evaluated yanlış false yapılmaz. Derived motor henüz yoksa o alt NOT_IMPLEMENTED kalır. |
| CASE-PRD-U02-009…011/022 | Geçerli verilen referans kaydolur; eksik teknik/birim alanlı Draft kalır. Geçersiz FieldId/tür/ref reddedilir, bilinmeyen aktivasyon0. | HTTP/SQL+form; dönüşüm golden/AT-016/045 tamamlandı sayılmaz. |
| CASE-PRD-U01-008…013; X-012/013/014 | Arama, filtre, sayfa, ilk/sonraki hata, boş sonuç, klavye ve karta dönüş. | Component ayrı, gerçek browser ayrı; sayfa/count/dönüş veriyle doğrulanır. Desteklenmeyen özet0 değil. |
| CASE-PRD-X-001/002/016/018 | Diğer rol/anon/direct-ID/alias/count ve scope/oturum değişimi; eski response veya replay güncel yetkiyi aşamaz. | SEC+HTTP+browser; test grant'i production yetkisi değil, gerçek IdP ölçülmediyse controlled etiketi korunur. |
| CASE-PRD-X-004/005/006/007/017; U02-019/020 | Kayıt cevabını kontrollü kaybettir, aynı-key tekrarla; iki editörün stale sürümü, farklı payload ve audit/durable hata. | Tek kalıcı sonuç, atomik rollback; UI taslak/farkı korur ve hatadan sonra işi bitirir. SQL, helper ve browser birbirini ikame etmez. |
| CASE-PRD-U02-013/017/023; U01-016 | Draft'ın kesin kullanımına izin verilmez; kullanılan Inactive tarihçesi okunur, delete yok. | İlk Product sözleşmesinin guard'ı ölçülür. Olmayan Sales/Stock selector veya tam lifecycle runtime testi yapılmış sayılmaz; ilgili alt açık kalır. |

### 19.8 İki yazara somut başlangıç ve bir sonraki alt

**CODEX-BACKEND-URUN:** önce minimal grup/kod, global Product/versiyon ve read/create/edit DTO'sunu yukarıdaki nullable/scope/rol semantiğiyle tek API kaynağına bağla; #104 güvenlik owner'ıyla teknik group/audit/idempotency yüzeyini ve exact izinleri pinle. Ardından aynı scope'un endpoint/migration/gerçek SQL işleri. Erken field/response/ProblemCode/permission/capability örneğini frontend bitişini beklemeden ver. Formül, rol grantee veya fake ownerCompany seçme; Cari source/SQL dosyasına paralel yazma yok.

**CLAUDE-FRONTEND-URUN:** mevcut shell'i tüketen Ürün-owned U-01/U-02 ve liste→kart→Draft-create/edit→geri dönüşü hazırla. Backend DTO gelene kadar açık fixture ile yalnız component/UX; fixture'ı ürün API'si gibi sunma. DTO pininden sonra gerçek transport, readonly server alanları, errors/dirty/unknown/context ve aynı gerçek kabul yolunu bağla. Ortak router/capability/global CSS değişikliği coordinator'ın atadığı tek yazıcıdan geçer; Cari frontend oturumunun dosyalarını sahiplenme.

**Sonraki tek alt:** bu Draft'ın seçilmiş bir ürün ailesinde teknik alan/birim completeness'ini gerçek referanslarla tamamlama ve explicit activation için mevcut PRD-TEMPLATE-001/PRD-UNIT-001 bağını hazırlama. Şimdiden U-07/fiyat/Excel veya yeni genel ERP paketi açılmaz. Mevcut #98/#99 kararları hazırdır; prefix/registry/global-scope/API eksikleri teknik owner teslimidir, yeni Sinan sorusu değildir. İlgili hesapta gerçekten eksik sayısal politika çıkarsa yalnız o alt açık bırakılır.

[Erken devir][first-handoff] aynı #100 kanalındadır; yayımlanması iki oturumdan alındı makbuzu değildir. Her anlamlı implementation/gerçek koşumda kaynak SHA, gerçek checkout, API/SQL, unit/component ve browser kanıtı ayrı güncellenir. Kanonik backlog/AT durumları, #229/#232 insan kapıları, Cari ortak-master sorusu, onaysız otomatik-account B ve production sınırı değişmedi. Self-merge yoktur.

[first-agents]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/AGENTS.md
[first-v3]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/governance/GOYMEN_ERP_HIZLI_TESLIM_v3.md
[first-dated]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/decisions/SINAN_KARARLARI_2026-08-31.md
[first-latest]: https://github.com/sinangym/GoymenERP/blob/e1fd69da69df36a72e559ea07163fb3f359b3c61/docs/decisions/SINAN_KARARLARI_2026-09-10.md
[first-decisions]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/decisions/KARAR_SICILI.md
[first-master]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md
[first-backlog]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/backlog/DELIVERY_BACKLOG_v1.json
[first-ui]: https://github.com/sinangym/GoymenERP/blob/3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f/docs/ui/CANONICAL_SCREEN_STATUS.csv
[first-scope]: https://github.com/sinangym/GoymenERP/issues/98#issuecomment-5469061415
[first-perm]: https://github.com/sinangym/GoymenERP/issues/99#issuecomment-5469937658
[first-cases]: ../acceptance/PRODUCT_CORE_U01_U02_U07_U08_U09_ACCEPTANCE_v1.md
[first-handoff]: https://github.com/sinangym/GoymenERP/issues/100#issuecomment-5645884223
[first-grant-issuer]: https://github.com/sinangym/GoymenERP/issues/66#issuecomment-5646946499
