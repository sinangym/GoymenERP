# PRODUCT-CORE-ACCEPTANCE-001 — U-01/U-02/U-07/U-08/U-09 çalıştırılabilir kabul matrisi

> Durum: `TEST_DESIGN_READY / EXECUTION_GATED`
> Sürüm: `v1`
> Tarih: `2026-09-02`
> Kanonik bağlar: `AT-015`, `AT-016`, `AT-017`
> Spec indeksi: `../features/PRODUCT_CORE_U01_U02_U07_U08_U09_JIT_INDEX_v1.md`

Bu belge global acceptance ID üretmez. `CASE-PRD-*` kimlikleri mevcut `AT-*` kayıtlarının çalıştırılabilir alt senaryolarıdır. Spec-only PR bu testleri geçmiş veya kanonik AT durumunu değiştirmiş sayılmaz.

`PRODUCT_CORE_JIT_AUTHORITY_v1.json`, U-07/U-08/U-09 dahil 11 artifact'ın exact SHA-256 envanterini, manifestten bağımsız promotion descriptor setini ve 128 local case'in exact canonical `AT-*` setini pinler. Bu yerel authority + mutation kanıtı shared workflow yerine geçmez; `G-PRD-CI-01` görünür runner wiring'i tamamlanana kadar CI promotion kapısı açıktır.

## 1. Test veri sözlüğü

Aşağıdaki adlar fixture alias'ıdır; production UUID, kod, rol, UnitId, enum veya permission string değildir.

### 1.1 Şirket ve kullanıcılar

| Alias | Anlam |
|---|---|
| `CMP-A`, `CMP-B` | Birbirinden izole iki hukuki şirket |
| `PLANT-A1`, `PLANT-A2`, `PLANT-B1` | Kendi şirketine bağlı tesisler |
| `ACT-SA` | Exact `SYSTEM_ADMINISTRATOR`; kabul edilmiş group-shared Product scope'u, fakat company-child için yalnız `CMP-A`, `PLANT-A1`, `PLANT-A2` scope'u olan kullanıcı. Production fixture'ı ancak `#104` kapanınca etkin |
| `ACT-SA-B` | Exact `SYSTEM_ADMINISTRATOR`; aynı group-shared Product scope'u, fakat company-child için yalnız `CMP-B`, `PLANT-B1` scope'u olan ikinci fixture kullanıcı |
| `ACT-OTHER` | Authenticated fakat exact role sahip olmayan kullanıcı |
| `ACT-ANON` | Kimliği doğrulanmamış kullanıcı |

### 1.2 Product'lar

| Alias | Durum/özellik |
|---|---|
| `P-DRAFT` | Eksik teknik/birim alanlı Draft |
| `P-SHEET` | Active sac Product; birden çok bağımsız rol |
| `P-TUBE` | Active boru/profil; kg + adet takibi |
| `P-INACTIVE` | Kullanılmış ve global Inactive |
| `P-ALIAS` | Güncel kodu ve yeniden kullanılamayan eski code alias'ı var |

### 1.3 Registry ve sürümler

| Alias | Anlam |
|---|---|
| `UNIT-KG/COUNT/METER/PACK` | Test registry'sinde onaylı UnitId alias'ları |
| `TPL-SHEET-V1` | Approved template version |
| `TPL-SHEET-V2-DRAFT` | Breaking alan değişimi içeren Draft version |
| `TPL-TUBE-V1` | Approved boru/profil template version |
| `CONV-TUBE-V1/V2` | Aynı dönüşüm lineage'ının iki immutable version'ı |
| `PACK-A-V1/V2` | `P-TUBE + CMP-A + PLANT-A1` package versions |
| `PACK-B-V1` | `P-TUBE + CMP-B + PLANT-B1` package version |

## 2. Kanıt standardı

| Kod | Kanıt katmanı | Minimum kanıt |
|---|---|---|
| `UT` | Unit/domain | Deterministik kural ve negatif sınırlar |
| `CT` | Contract/API | OpenAPI/serializer/auth/error ve fixture parity |
| `SQL` | Gerçek veritabanı integration | FK/index/unique/race/transaction/rollback; InMemory kabul edilmez |
| `FE` | Frontend component | Durum, doğrulama, klavye ve capability davranışı |
| `E2E` | Browser üzerinden gerçek akış | UI → API → gerçek DB ve kalıcı kanıt |
| `SEC` | Güvenlik | Default-deny, direct-ID/count/non-disclosure, company isolation |
| `SRC` | Kaynak parity | Kanonik screen/route/source ve mock dışlama |

Her kritik write vakası success kadar şu kanıtları da içermelidir: yetkisiz çağrıda business etkisi sıfır, audit/idempotency arızasında atomik rollback, stale expected-version'da overwrite olmaması ve response sonrası DB doğrulaması. Platform contract ilgili command için event/outbox zorunlu kılarsa onun failure/rollback kanıtı da aynı sınıra eklenir.

`PROPOSED_JIT` olarak işaretlenen exact lifecycle, filter/cursor, graph resolver, migration ve scope/precedence vakaları ancak ilgili owner/API/gate kabulünden sonra implementation acceptance olur. Kabul edilene kadar bunlar tasarım seçimlerini görünür kılan fail-closed test taslaklarıdır; mevcut repo kararını geçmiş sayılmaz.

`AT` sütunundaki `—`, vakanın local `CASE-PRD-*` kabulü olduğunu ve kanonik `ACCEPTANCE_70.md` içinde aynı anlamda bir global `AT-*` bulunmadığını gösterir. Global Product idempotency/replay vakaları `PROPOSED_JIT / G-PRD-PERSIST-01` altındadır: exact command scope ve cross-origin model kabul edilene kadar persistent command/replay fail-closed kalır; bu vakalar bir model seçmiş sayılmaz.

## 3. Ortak çapraz kesit vakaları

| ID | Önkoşul ve eylem | Beklenen sonuç | Katman | AT |
|---|---|---|---|---|
| `CASE-PRD-X-001` | `ACT-OTHER` beş ekranın global arama/list/detail/write capability'lerini çağırır | Bütün eylemler default-deny; yetkisiz kayıt sonuç, öneri, toplam veya sayı olarak sızdırılmaz ve write etkisi yoktur | CT,SEC,E2E | AT-002 |
| `CASE-PRD-X-002` | `ACT-ANON` doğrudan UUID/route kullanır | Kimlik doğrulama standardına uygun red; nesne varlığı açıklanmaz | CT,SEC | — |
| `CASE-PRD-X-003` | `ACT-SA`, payload'a yetkisiz `CMP-B`/PlantId/actor/status ekler | Server context/protected-field guard fail-closed; yabancı scope etkilenmez | CT,SQL,SEC | — |
| `CASE-PRD-X-004` | `PROPOSED_JIT`: kabul edilmiş global command scope altında aynı idempotency key ve normalized payload iki kez gönderilir | Gate açıkken command/replay persistence yoktur; model kabul edilince tek business outcome ve güncel auth sonrası güvenle açıklanabilen terminal response kanıtlanır | SQL,E2E | — |
| `CASE-PRD-X-005` | `PROPOSED_JIT`: kabul edilmiş global scope modelinde aynı key farklı actor/operation/payload veya scope ile tekrar kullanılır | Gate açıkken fail-closed; kabul edilen model conflict/non-disclosing red, ikinci business/audit etkisinin olmaması ve stored response sızmamasını kanıtlar | SQL,SEC | — |
| `CASE-PRD-X-006` | İki aktör aynı revision'ı günceller | Tek kazanan; diğeri stale conflict alır, otomatik overwrite olmaz | SQL,FE,E2E | AT-005 |
| `CASE-PRD-X-007` | Audit/idempotency persistence failure enjekte edilir | Business write dahil transaction tam rollback; zorunlu event/outbox varsa aynı test sınırına katılır | SQL | AT-003 |
| `CASE-PRD-X-008` | ERP-000J/AI capability kapalı | AI paneli, butonu, placeholder'ı ve network çağrısı mount edilmez | FE,E2E,SRC | — |
| `CASE-PRD-X-009` | Capability açık; AI bir change-set önerir | Yalnız açıklanabilir Draft/diff; write/approve/activate yok | CT,FE,E2E | — |
| `CASE-PRD-X-010` | Birim dönüşümü rounding/tolerance golden'ında server preview ile client-side hesap sonucu farklılaştırılır | UI server sonucunu gösterir; client değeri authoritative yapmaz | CT,FE | AT-016 |
| `CASE-PRD-X-011` | Save/preview/activation 5xx veya network hatası verir | Form state korunur; sahte başarı/toast ve route çıkışı yok; retry erişilebilir | FE,E2E | AT-006 |
| `CASE-PRD-X-012` | 320px ve geniş viewport; yalnız klavye ve screen-reader isimleriyle kullanım | Bilgi kaybı yok; görünür focus, label/error association, dialog focus trap ve reachable actions | FE,E2E | — |
| `CASE-PRD-X-013` | UI source/route/field fixture parity kontrolü | Yalnız kanonik U-01/U-02/U-07/U-08/U-09; yeni ID/route veya backend dışı alan yok | CT,SRC | — |
| `CASE-PRD-X-014` | Summary/aggregate capability response'ta yok | UI `0`, fiyat, stok veya ilişki sayısı uydurmaz; unsupported/hidden state | FE,SRC | — |
| `CASE-PRD-X-015` | Seçili şirket portable export/import edilir | Aynı Product UUID/kod; yalnız seçili şirket child'ları; mismatch sessiz remap/clone yapmaz | CT,SQL,SEC | — |
| `CASE-PRD-X-016` | Company/group context hızla değiştirilirken eski request geç döner | Request iptal edilir veya generation fence eski cevabı düşürür; eski child/count/badge yeni context'e render edilmez | FE,E2E,SEC | AT-008 |
| `CASE-PRD-X-017` | `PROPOSED_JIT`: timeout sonrası outcome bilinmez ve kabul edilmiş global modelin aynı key retry akışı denenir | Gate açıkken stored result okunmaz; model kabul edilince terminal sonuç güvenle reconcile edilir ve ikinci business etkisi oluşmaz | CT,SQL,E2E | — |
| `CASE-PRD-X-018` | `PROPOSED_JIT`: ilk başarıdan sonra permission/group scope kaldırılır ve kabul edilmiş modelde replay istenir | Gate açıkken stored result okunmaz; model kabul edilince güncel auth yeniden doğrulanır ve stored response hassas veri sızdırmaz | CT,SEC | — |

## 4. U-01 — Product listesi ve 360

| ID | Önkoşul ve eylem | Beklenen sonuç | Katman | AT |
|---|---|---|---|---|
| `CASE-PRD-U01-001` | `ACT-SA` global listeyi açar; `P-SHEET` iki şirkette kullanılmıştır | Tek Product satırı/UUID; şirket başına duplicate satır yok | CT,SQL,E2E | — |
| `CASE-PRD-U01-002` | Güncel code ile arama | Canonical Product tek sonuç; exact code match nedeni görünür | CT,FE,E2E | — |
| `CASE-PRD-U01-003` | Eski code alias ile arama | Aynı canonical Product tek sonuç; “eski kod/alias” etiketi; alias yeniden kullanılmaz | CT,SQL,E2E | — |
| `CASE-PRD-U01-004` | Role filter `any` ile iki rol seçilir | Roller class/group'tan değil Product role setinden değerlendirilir; herhangi biri eşleşenler gelir | CT,E2E | AT-015 |
| `CASE-PRD-U01-005` | Aynı Product class'ındaki kayıtlarda commercial Product role filter `all` ile iki rol seçilir | Ürün sınıfından bağımsız olarak her iki role aynı Product'ta sahip kayıtlar gelir; join satır çoğaltmaz | CT,SQL,E2E | AT-015 |
| `CASE-PRD-U01-006` | Aynı class'ta farklı role setleri olan iki Product | Class filtresi role sonucunu üretmez/değiştirmez; bağımsızlık görünür | CT,E2E | AT-015 |
| `CASE-PRD-U01-007` | Lifecycle filtresi Draft/Active/Inactive | Admin yönetim listesinde doğru statüler; nihai işlem selector semantiğiyle karıştırılmaz | CT,FE | — |
| `CASE-PRD-U01-008` | Filter/sort değişirken opaque cursor var | Cursor sıfırlanır; client cursor üretmez/parse etmez | CT,FE,E2E | — |
| `CASE-PRD-U01-009` | Geçerli sonraki cursor | Birleşim sıralı ve duplicatesiz; aynı filtre/auth scope korunur | CT,SQL,E2E | — |
| `CASE-PRD-U01-010` | Invalid/expired cursor | Güvenli recovery; veri sızıntısı veya bozuk liste yok | CT,FE | — |
| `CASE-PRD-U01-011` | İlk sayfa API hatası | Full error + retry; empty state veya sahte veri gösterilmez | FE,E2E | — |
| `CASE-PRD-U01-012` | Sonraki sayfa API hatası | Mevcut satırlar korunur; inline retry; duplicate fetch yok | FE,E2E | — |
| `CASE-PRD-U01-013` | Liste count destekleniyor | Count aynı global/auth/filter scope'undan; role/alias join'i sayıyı çoğaltmaz | CT,SQL | — |
| `CASE-PRD-U01-014` | `P-TUBE` 360 açılır, authorized `CMP-A` company scope seçilidir | Global kimlik ortak; yalnız yetkili şirket `CMP-A` package/child özeti görünür; `CMP-B` görünmez | CT,SEC,E2E | AT-001 |
| `CASE-PRD-U01-015` | Backend stok/fiyat aggregate capability sağlamaz | 360 ilgili değeri uydurmaz; unsupported/hidden state | FE,SRC | — |
| `CASE-PRD-U01-016` | `P-INACTIVE` kullanılmıştır | Tarihçe/alias görünür; delete aksiyonu yok; yeni kullanım kapalı etiketi | CT,FE,E2E | — |
| `CASE-PRD-U01-017` | 360'dan U-02/U-07/U-08/U-09'a geçilir | Canonical route ve aynı Product context; candidate source yeni ekran ID'sine dönüşmez | FE,E2E,SRC | — |

## 5. U-02 — Yeni/düzenle Product

| ID | Önkoşul ve eylem | Beklenen sonuç | Katman | AT |
|---|---|---|---|---|
| `CASE-PRD-U02-001` | Geçerli minimum form kaydedilir | Kayıt her zaman Draft doğar; doğrudan Active yok | CT,SQL,E2E | — |
| `CASE-PRD-U02-002` | Normal create'te kullanıcı code göndermez | UUID ve prefix+sequence code server'dan gelir; UI önceden kesin code ilan etmez | CT,SQL,E2E | — |
| `CASE-PRD-U02-003` | `ACT-OTHER` manuel code yollar | Default-deny; Product/code/audit etkisi sıfır | CT,SQL,SEC | — |
| `CASE-PRD-U02-004` | `ACT-SA` manual code + reason kullanır | Manifest izni, global code/alias uniqueness ve reason ile atomik write/audit | CT,SQL,E2E | AT-003 |
| `CASE-PRD-U02-005` | İki paralel create aynı code/sequence için yarışır | DB global uniqueness ile tek kazanan; duplicate Product yok | SQL | AT-005 |
| `CASE-PRD-U02-006` | Yeni code mevcut alias ile çakışır | Hard block; alias namespace yeniden kullanılamaz | CT,SQL | — |
| `CASE-PRD-U02-007` | Bir Product'a satın alınabilir + satılabilir + üretim girdisi + üretilen çıktı rolleri atanır | Tek Product, dört bağımsız role; class/group otomatik değişmez | CT,SQL,E2E | AT-015 |
| `CASE-PRD-U02-008` | Class/group değişir | Role seti sessizce eklenmez/silinmez; impact diff açık | CT,FE | AT-015 |
| `CASE-PRD-U02-009` | Approved template/version seçilir | Server-driven alan contract'ı doğru version'a bağlı kaydedilir | CT,SQL,E2E | — |
| `CASE-PRD-U02-010` | Draft/unresolved template veya eksik required field ile Activate | Activation reddedilir; Draft korunur; alan hataları ilişkili | CT,SQL,FE | — |
| `CASE-PRD-U02-011` | Boru/profilde kg veya adet conversion path eksik | Draft save mümkün; activation fail-closed | CT,SQL,E2E | AT-016,AT-045 |
| `CASE-PRD-U02-012` | Bütün gate/validation geçer ve Activate çağrılır | Explicit command sonrası Active; ayrı audit; nihai selector'a bundan sonra girer | CT,SQL,E2E | AT-003 |
| `CASE-PRD-U02-013` | Draft Product satış/stok/üretim selector'ında aranır | Sonuç dönmez; direct payload ile kullanma da backend'de reddedilir | CT,SQL,SEC | — |
| `CASE-PRD-U02-014` | Name/teknik benzerliği bulunur, code collision yok | Warning/compare; kullanıcı Draft kaydedebilir; auto-merge/auto-block yok | CT,FE,E2E | — |
| `CASE-PRD-U02-015` | AI duplicate candidate önerir | Candidate + gerekçe/diff; UUID merge/code/activation write'ı yok | CT,FE | — |
| `CASE-PRD-U02-016` | Global name/family edit'i hazırlanır | İki şirkete global etki açık uyarı + reason; child company verisi değişmez | CT,SQL,FE | — |
| `CASE-PRD-U02-017` | Kullanılmış Active Product deactivate edilir | Global yeni kullanım iki şirkette kapanır; geçmiş işlem çözülür; fiziksel delete yok; kritik değişiklik audit edilir | CT,SQL,E2E | AT-003 |
| `CASE-PRD-U02-018` | Payload UUID/status/revision/actor/approval/CompanyId mass-assign eder | Protected alanlar kabul edilmez; server authoritative değerler korunur | CT,SQL,SEC | — |
| `CASE-PRD-U02-019` | Dirty form ile route/browser çıkışı | Continue/Save Draft/Discard semantiği; save failure çıkışı engeller, girdi korunur | FE,E2E | AT-006 |
| `CASE-PRD-U02-020` | Server validation başarısız save için çoklu alan hatası döndürür | Üst özet + input ilişkisi + ilk hataya klavye geçişi; form girdileri korunur | FE,E2E | AT-006 |
| `CASE-PRD-U02-021` | Derived/not-evaluated role durumu formda açılır ve kaydedilir | Provenance görünür; not-evaluated sessiz `false` olmaz; derived rol çelişkili manuel değerle ezilmez | CT,SQL,FE | — |
| `CASE-PRD-U02-022` | `PRD-ROLE-001` owner-approved storage/türetim contract'ı yokken Product Activate edilir | Aktivasyon fail-closed; Draft ve kullanıcı girdisi korunur | CT,SQL,FE | — |
| `CASE-PRD-U02-023` | `PROPOSED_JIT`: `ACT-SA`, kullanılmış `P-INACTIVE` için explicit Reactivate çağırır; paralel çağrılardan biri stale expected-version taşır | Eylemin varlığı kilitlidir; exact precondition/impact/in-flight policy veya üç platform gate açıkken command fail-closed ve UI disabled/gated kalır. Hepsi kabul edilince current revision, reason ve global impact preview ile tek Inactive→Active geçişi atomik audit edilir; stale çağrı overwrite etmeden conflict alır ve tarihsel referanslar değişmez | CT,SQL,FE,E2E | AT-003,AT-005 |

## 6. U-07 — Paket standartları

| ID | Önkoşul ve eylem | Beklenen sonuç | Katman | AT |
|---|---|---|---|---|
| `CASE-PRD-U07-001` | `ACT-SA` yalnız `CMP-A`, `ACT-SA-B` yalnız `CMP-B` standardını oluşturur | Tek global Product; iki ayrı authorized company-scoped lineage; Product clone yok | CT,SQL,SEC | — |
| `CASE-PRD-U07-002` | `CMP-A` context'i liste/detail/count/resolver çalıştırır | `PACK-B-V1` hiçbir kanaldan sızmaz | CT,SQL,SEC,E2E | AT-001,AT-002 |
| `CASE-PRD-U07-003` | `CMP-A` payload'ına `PLANT-B1` eklenir | Company/plant FK/context guard reddeder; business etkisi yok | CT,SQL,SEC | — |
| `CASE-PRD-U07-004` | New package version kaydedilir | Draft; resolver/yeni işlemde kullanılmaz | CT,SQL,E2E | — |
| `CASE-PRD-U07-005` | Approved UnitId/formula/golden, başlangıç tarihi, reason, old/new diff ve güvenilir impact sonrası approve | Immutable approved version ve ayrı audit; resolver'a yalnız yürürlükteyken girer | CT,SQL,E2E | AT-003 |
| `CASE-PRD-U07-006` | Aynı scope ve tarihte iki version overlap eder | Approval fail-closed; auto-trim/auto-retire yok | UT,SQL,FE | — |
| `CASE-PRD-U07-007` | Approved `PACK-A-V1` alanı doğrudan edit edilir | Reddedilir; değişiklik `PACK-A-V2` Draft oluşturmalıdır | CT,SQL | — |
| `CASE-PRD-U07-008` | Eski order/plan/label/shipment package standard V1 snapshot'ı taşır; yeni package V2 approve edilir | Geçmiş siparişin package snapshot'ı ve render edilen anlamı değişmez | SQL,E2E | AT-017 |
| `CASE-PRD-U07-009` | V2 effective tarihinden sonra yeni işlem çözülür | Uygun tek V2 snapshot'lanır; latest string lookup'a bağımlı kalmaz | CT,SQL,E2E | — |
| `CASE-PRD-U07-010` | Customer-specific ihtiyaç girilmeye çalışılır | Inline Product standard override oluşmaz; ayrı owner contract/capability'ye yönlenir | CT,FE,SRC | — |
| `CASE-PRD-U07-011` | FormulaVersion/golden/rounding/tolerance unresolved | Draft saklanabilir; approve ve production resolve kapalı | CT,SQL,FE | AT-016 |
| `CASE-PRD-U07-012` | Boru/profil adet–boy–kg birim dönüşümünde verified weight ve geometry estimate farklıdır | İki değer/source açık; verified gelecekteki teorik çözümde üstün, geçmiş fiilî değer değişmez | UT,CT,FE | AT-016,AT-045 |
| `CASE-PRD-U07-013` | Resolver no-match veya ambiguous sonuç alır | Fail-closed; frontend kendi fallback standardını üretmez | CT,SQL,FE | — |
| `CASE-PRD-U07-014` | Aynı expected-version ile iki approval yarışır | Tek approved winner; conflict loser, overlap/duplicate yok | SQL | AT-005 |
| `CASE-PRD-U07-015` | Audit insert failure approval sırasında | Version/status/idempotency tam rollback; platform zorunlu event/outbox tanımlarsa o da rollback | SQL | AT-003 |
| `CASE-PRD-U07-016` | `CMP-A` portable export alınır | Global Product reference + yalnız `CMP-A` package versions; `CMP-B` yok | CT,SQL,SEC | — |
| `CASE-PRD-U07-017` | Registry/impact API unavailable | History read-only olabilir; approve kapalı; fake impact count yok | FE,E2E | — |
| `CASE-PRD-U07-018` | `PRD-PACK-SITE-001` veya scope/precedence kararı açık | Resolver keyfi company/site fallback seçmez; approve/effective production akışı kapalı | CT,SQL,FE | — |
| `CASE-PRD-U07-019` | `PRD-PACK-APPROVAL-001` exact lifecycle/SoD contract'ı açık | Draft/validation korunur; automatic/self approval varsayımıyla status değişmez | CT,SQL,FE | — |

## 7. U-08 — Birimler ve dönüşümler

| ID | Önkoşul ve eylem | Beklenen sonuç | Katman | AT |
|---|---|---|---|---|
| `CASE-PRD-U08-001` | `P-SHEET` stock/purchase/sales/price/production/package UnitId'leri farklı atanır | Roller ayrı korunur; biri diğerinden türetilmez | CT,SQL,E2E | AT-016 |
| `CASE-PRD-U08-002` | Active Product primary stock unit doğrulanır | `kg` semantiği tek primary; UnitId registry'den | CT,SQL | AT-016 |
| `CASE-PRD-U08-003` | `P-TUBE` boru/profil adet–boy–kg birim dönüşümü ve Product activation completeness | kg + adet birlikte; metre/paket ikincil; eksikte Product activation yok | CT,SQL,E2E | AT-016,AT-045 |
| `CASE-PRD-U08-004` | Bir transactional unit'ten primary'ye tek Effective path | Aynı input/context/effective date deterministik aynı sonuç/version trace'i | UT,CT,SQL | AT-016 |
| `CASE-PRD-U08-005` | Effective path yok | Preview/production conversion fail-closed; stok/ticari etki yok | UT,CT,SQL | AT-016 |
| `CASE-PRD-U08-006` | Aynı effective context'te iki path farklı sonuç verir | Ambiguous validation; rule approval/effective resolution ve production conversion reddedilir | UT,SQL | AT-016 |
| `CASE-PRD-U08-007` | Unit conversion graph cycle içerir | Rule approval reddedilir; UI cycle'ı sessiz düzeltmez | UT,SQL,FE | AT-016 |
| `CASE-PRD-U08-008` | Aynı-boyut birim dönüşümü sabit rule pozitif factor ile çalışır | Approved registry precision içinde sonuç; version/audit saklanır | UT,CT,SQL | AT-016 |
| `CASE-PRD-U08-009` | Birim dönüşümü factor değeri `0`, negatif, NaN/Infinity veya incompatible dimension'dır | Rule approve edilmez; DB'de invalid Effective edge yok | UT,CT,SQL | AT-016 |
| `CASE-PRD-U08-010` | Cross-dimension conversion formula bridge olmadan | Fail-closed; UI ters/katsayı uydurmaz | UT,CT,FE | AT-016 |
| `CASE-PRD-U08-011` | Formula-bound unit conversion rule oluşturulur | Exact FormulaId + pinned version + UnitId parameter mapping zorunlu | CT,SQL | AT-016 |
| `CASE-PRD-U08-012` | Golden `Taslak`, FormulaVersion/rounding/tolerance `TBD` | Draft save + structural validation mümkün; numeric preview, approval/effective durum ve production etkisi yok | CT,SQL,E2E | AT-016 |
| `CASE-PRD-U08-013` | İşlemde fiilî kg/adet birim dönüşümü teorikten farklıdır | Her iki anlam ayrı; fiilî snapshot yalnız işlemde authoritative; master factor auto-update yok | CT,SQL,E2E | AT-016,AT-045 |
| `CASE-PRD-U08-014` | Movement farklı entered unit kullanır | Entered quantity/unit ve primary kg/result rule/formula version birlikte snapshot'lanır | CT,SQL | AT-016 |
| `CASE-PRD-U08-015` | Yeni formula/tolerance-backed birim dönüşümü `CONV-TUBE-V2` approve edilir ve business date'te Effective olur | Geçmiş V1 snapshot'lı gerçekleşmiş hareketin raw/result/trace değeri değişmez | SQL,E2E | AT-016,AT-049 |
| `CASE-PRD-U08-016` | Rounding policy onaylı fixture ile tie/boundary hesaplanır | Ara sonuç erken yuvarlanmaz; raw/result/policy version/tolerance izlenebilir | UT,SQL | AT-016 |
| `CASE-PRD-U08-017` | kg, adet, metre, paket liste aggregate'i istenir | Birimler tek karma toplama birleştirilmez; her biri ayrı UnitId ile | CT,FE | AT-016 |
| `CASE-PRD-U08-018` | Formula paydası `0` veya required input null | Sonuç yok; NaN/Infinity serialize/persist edilmez; write etkisi yok | UT,CT,SQL | AT-016 |
| `CASE-PRD-U08-019` | Unit/formula registry unavailable | History read-only olabilir; edit/approve kapalı; retry durumu | FE,E2E | — |
| `CASE-PRD-U08-020` | Conversion preview/simulator çalışır | Server trace gösterilir; movement/stock/audit-business write yok | CT,SQL,FE | AT-016 |
| `CASE-PRD-U08-021` | Birim dönüşümü kuralı reversible işaretli değilken client inverse ister | Sessiz reciprocal üretimi yok; explicit validation sonucu | UT,CT | AT-016 |
| `CASE-PRD-U08-022` | Inactive UnitId/eski rule yeni işlemde kullanılır | Yeni kullanım reddedilir; tarihsel snapshot çözülebilir | CT,SQL | AT-016 |
| `CASE-PRD-U08-023` | Açıkça pinned non-production/sandbox FormulaVersion ile preview çalışır | Non-authoritative etiketi + exact version trace; business/stock persistence yok | CT,FE,E2E | AT-016 |

## 8. U-09 — Teknik şablonlar

| ID | Önkoşul ve eylem | Beklenen sonuç | Katman | AT |
|---|---|---|---|---|
| `CASE-PRD-U09-001` | Template label/çeviri değişir | Stable field key ve saklanan anlam değişmez | UT,CT,SQL | — |
| `CASE-PRD-U09-002` | Aynı unit-bearing field key farklı dimension/type ile yeniden kullanılır | Approval reddedilir veya breaking new key/migration gerekir | UT,CT,SQL | AT-016 |
| `CASE-PRD-U09-003` | Unit-bearing decimal UnitId'siz gönderilir | Validation fail; Effective template veya Product write etkisi yok | CT,SQL | AT-016 |
| `CASE-PRD-U09-004` | `null`, `0` ve boş string ayrı fixture'larla gönderilir | Contract belirtilen ayrı anlamları korur; sessiz coercion yok | UT,CT | — |
| `CASE-PRD-U09-005` | Registry'de olmayan enum/reference veya UnitId | Draft validation hatası; template approval/effective kullanım yok | CT,SQL | AT-016 |
| `CASE-PRD-U09-006` | Field metadata'ya script/SQL/HTML expression enjekte edilir | Çalıştırılmaz/saklanmaz veya güvenli text policy; code execution yok | CT,SEC,FE | — |
| `CASE-PRD-U09-007` | Approved template field'i doğrudan edit edilir | Reddedilir; yeni Draft version gerekir | CT,SQL | — |
| `CASE-PRD-U09-008` | `P-SHEET` V1'e bağlıyken V2 approve edilir | Product V1'de kalır; silent latest migration yok | CT,SQL,E2E | — |
| `CASE-PRD-U09-009` | V2 required field/type/unit breaking değişiklik içerir | Breaking diff ve migration gereği açık; auto apply yok | UT,CT,FE | AT-016 |
| `CASE-PRD-U09-010` | Migration Dry Run yapılır | Mapping/failure/impact üretir; Product veya işlem yazmaz | CT,SQL,FE | — |
| `CASE-PRD-U09-011` | Yetkili explicit migration apply + idempotency | Yalnız validated Product seti contract'taki transaction politikasıyla; audit/version pin | CT,SQL,E2E | AT-003 |
| `CASE-PRD-U09-012` | Required template alanı eksik Product Activate edilir | Draft korunur; activation fail-closed ve alan hatası görünür | CT,SQL,FE | — |
| `CASE-PRD-U09-013` | Formula parameter field UnitId/dimension uyuşmaz | Template approval reddedilir; formula unit conversion çalıştırılmaz | UT,CT,SQL | AT-016 |
| `CASE-PRD-U09-014` | Formula-bound template'ta FormulaVersion/golden/rounding/tolerance unresolved | Template Draft kalır; approval/effective kullanım yok | CT,SQL | AT-016 |
| `CASE-PRD-U09-015` | Boru/profil adet–boy–kg formula/unit conversion binding'inde verified standard weight ve geometry estimate farklı | İki source ayrı; verified/master teorik kullanımda üstün; fiilî hareket etkilenmez | UT,CT,FE | AT-016,AT-045 |
| `CASE-PRD-U09-016` | Fiilî movement sonucu template field'e auto-learn yazılmak istenir | Reddedilir; hareket snapshot'ında kalır, yalnız ayrı onaylı öneri akışı olabilir | CT,SQL | — |
| `CASE-PRD-U09-017` | Service family/template U-09 Product flow'unda seçilir | `PRD-SRV-001` çözülene kadar unsupported; Product family diye sessiz eklenmez | CT,FE,SRC | — |
| `CASE-PRD-U09-018` | Frontend bilmediği field type alır | Tahmin/render/write yok; güvenli unsupported contract state | FE,E2E | — |
| `CASE-PRD-U09-019` | Unit/formula registry unavailable | Read-only history olabilir; edit/approve/migrate kapalı | FE,E2E | — |
| `CASE-PRD-U09-020` | Stale Draft iki sekmede düzenlenir | Tek winner; loser compare/reload, automatic overwrite yok | SQL,FE,E2E | AT-005 |
| `CASE-PRD-U09-021` | Template form preview açılır | U-02 server-driven görünümü; Product write/activation yok | CT,FE | — |
| `CASE-PRD-U09-022` | Section/field sıralaması yalnız klavye ile değiştirilir | Drag-and-drop'a bağlı olmadan aynı Draft sonucu ve focus yönetimi | FE,E2E | — |
| `CASE-PRD-U09-023` | Product/template payload'ına gerçek menşe/lot/heat/coil değeri attribute olarak yazılır | Owner sınırı nedeniyle reddedilir; yalnız approved tracking-policy reference kabul edilir | CT,SQL,SRC | — |
| `CASE-PRD-U09-024` | Family hiyerarşisi contract'ta kullanılıyorsa cycle oluşturulur veya aynı version'da duplicate field key/FieldId eklenir | Approval fail-closed; effective schema değişmez | UT,CT,SQL | — |
| `CASE-PRD-U09-025` | Aynı family/lineage ve çakışan effective interval için iki approval aynı expected-version ile yarışır | Tek winner; loser conflict; aynı intervalde iki effective candidate oluşmaz | SQL | AT-005 |
| `CASE-PRD-U09-026` | `PRD-TRACK-001` owner-approved profil yokken tracking gerektiren template approve veya Product activate edilir | Fail-closed; mevcut effective schema ve Product değişmez | CT,SQL | — |
| `CASE-PRD-U09-027` | Migration atomicity/partial-success policy henüz kabul edilmemişken Apply çağrılır | Yalnız Dry Run mümkündür; Product/template binding write'ı yok | CT,SQL,FE | — |
| `CASE-PRD-U09-028` | Ölçülü Product değeri farklı giriş birimiyle normalize edilir | Girilen value+UnitId, canonical value+UnitId ve ConversionVersion birlikte snapshot'lanır | CT,SQL | AT-016 |

## 9. Golden ve formül kabul kapısı

`../formulas/PRODUCT_UNIT_CONVERSION_GOLDEN_v1.csv` ile `../formulas/PRODUCT_UNIT_CONVERSION_GOLDEN_MANIFEST_v1.json` birlikte business arithmetic oracle taslağıdır. Aşağıdakiler tamamlanmadan executable production golden ve `AT-016/AT-045 DONE` değildir:

- FormulaVersion'ların registry'de immutable olarak yayınlanması;
- exact UnitId/precision/scale;
- rounding method, stage ve decimal count;
- tolerance type/value/boundary;
- density/verified profil-weight source ve onay sahibi;
- fixture'ların Sinan/iş sahibi onayı;
- decimal SQL round-trip ve gerçek fixture kanıtı.
- CSV ↔ manifest için dokuz TestId, FormulaId, raw output ve unit parity'si; CSV `Nitelik` ↔ manifest `measurementKind` parity'sinin `measurementKindMapping@1.0.0` exactly-one mapping ve SHA-256 digest'iyle doğrulanması;
- manifest `promotionSchema@1.0.0` alanlarının declared type/non-null/cardinality/conditional kurallarını geçmesi ve `SYNTHETIC_*` provenance'ın approved registry/master/owner-evidence kimlikleriyle değiştirilmesi;
- her promoted case `traceHash` değerinin `traceHashPolicy@1.0.0` formatını geçmesi ve `traceHash` çıkarılmış case'in RFC 8785 JCS gösteriminden yeniden hesaplanan SHA-256 ile eşleşmesi;
- applicable package satırlarında pinned PackageStandardVersion ve approved dataset + manifest content hash'inin rule/evidence'a bağlanması.

## 10. Kanonik AT kapanma matrisi

| AT | Bu paketteki kapsam | Kapanış için zorunlu kanıt | Spec-only sonucu |
|---|---|---|---|
| `AT-001` | Authorized company/site child visibility | A/B company/site CT + SQL + SEC + E2E | `TASLAK` kalır |
| `AT-002` | Global Product ve company-child list/detail/search non-disclosure | Direct-ID/count/search negatif SEC + E2E | `TASLAK` kalır |
| `AT-003` | Kritik Product/package/template/rule değişiklik audit'i | Atomik audit + rollback gerçek SQL | `TASLAK` kalır |
| `AT-005` | Concurrent create/edit/approve/reactivate conflict | Expected-version/race gerçek SQL | `TASLAK` kalır |
| `AT-006` | Başarısız save sonrası form girdisinin korunması | FE + browser failure/retry kanıtı | `TASLAK` kalır |
| `AT-008` | CompanyContext değişiminde açık form/response'un sessizce yeni scope'a taşınmaması | Generation fence + unsaved-state E2E | `TASLAK` kalır |
| `AT-015` | Class/group/family/template ile bağımsız Product roles; U-01/U-02/U-09 parity | CT + SQL + E2E, canonical source/route | `TASLAK` kalır |
| `AT-016` | Unit roles, deterministik/no-ambiguity conversion resolution, formula version, rounding/tolerance ve snapshot | Approved golden + UT + CT + SQL + E2E; graph modeli kabul edilirse graph-specific tests | `TASLAK` kalır |
| `AT-017` | Package version/effective resolution ve eski işlem snapshot immutability | CT + gerçek SQL + downstream E2E | `TASLAK` kalır |
| `AT-045` | Boru/profil adet–boy–kg doğrulanmış golden | Gerçek doğrulanmış profil master fixture | `TASLAK` kalır |
| `AT-049` | Yeni recipe/formula/tolerance version'ının geçmiş fiilî movement/snapshot'ı değiştirmemesi | Pinned old/new version + gerçek SQL + downstream E2E | `TASLAK` kalır |

## 11. Merge kabul özeti

Bir uygulama PR'ı aşağıdaki şartlarla merge-ready olabilir:

- kendi ekranının bütün P0/negative/security case'lerini test ID'siyle eşler;
- açık gate/owner kararını production stub veya permissive fallback ile aşmaz;
- gerçek DB gereken vakaları InMemory ile kapatmaz;
- kanonik route/source ID ve backend fixture parity kanıtı verir;
- capability-off, permission-denied, stale, failure ve unsaved state ekran kanıtlarını ekler;
- existing transaction snapshot'ını yeni master version ile yeniden yorumlamadığını gösterir;
- global AT durumunu yalnız tüm evidence birleştikten sonra ayrı truth-sync PR'ında günceller.
