# Cari Opsiyonel Ticari Rol Uzlaştırma Şartnamesi v1

- Tarih: 11 Eylül 2026
- Şerit: `CHATGPT-BUSINESS`
- Durum: **REVIEW EDİLEBİLİR İŞ ŞARTNAMESİ / ERP KABULÜ DEĞİL**
- Dosya: `docs/features/CARI_ROLE_OPTIONAL_RECONCILIATION_SPEC_v1.md`
- Hazırlık tabanı: `main@060da63f2644cb271d78d9eab8b7a6985d544ad1`
- İlgili kaynaklar: PR #227, #228, #229, #230, #231 ve Issue #66 koordinasyon kayıtları.

Bu belge uygulama kodu yazmaz, kanonik karar/task durumunu değiştirmez, `docs/planning/2026-09-10/**` veya paylaşılan ilerleme belgelerinin tek-yazar sahipliğini devralmaz. PR #229 içindeki tarihli karar paketi insan review kapısında kalır; burada kullanılması onun merge edildiği veya insan tarafından kabul edildiği anlamına gelmez.

## 1. Kaynak önceliği ve güncel depo gerçeği

Kaynak önceliği `AGENTS.md` ile aynıdır: en son tarihli kullanıcı kararı → `KARAR_SICILI` → Master Rapor → ilgili feature/ADR → eski referanslar. Açık yetki, güvenlik, migration veya production politikası tahminle kapatılmaz.

11 Eylül 2026 doğrulamasında:

- PR #227 merge edilmiştir; `main=060da63f2644cb271d78d9eab8b7a6985d544ad1`. Merge edilen #227 yalnız mevcut create/list/detail/company-link API akışının TestServer HTTP + gerçek SQL kabul yolculuğunu ekler; frontend, normal-provider, tamamlanmış Cari veya production kabulü vermez.
- PR #228 açıktır ve draft'tır; `head=d46f7f819456fc578a25cdcabf5c98ad6f634456`. C-02 create/link uygulaması ve kontrollü Chromium paketi burada zaten vardır. Aynı create/link/router uygulaması paralel yeniden yazılmaz.
- PR #229 açıktır ve draft'tır; `head=e1fd69da69df36a72e559ea07163fb3f359b3c61`. İnsan karar incelemesi gereken planlama paketidir.
- PR #230 açıktır ve draft'tır; `head=d97f8ee97c9cd8e22c2d3497094113a9f4decf43`. Opsiyonel ticari rol backend/OpenAPI/SQL adayıdır; #227 merge'inden sonra mevcut main ile yeniden uzlaştırılması gerekir.
- PR #231 açıktır ve draft'tır; `head=173b003b4ac09aa396eeb6bf8c78611de76a5dc7`. Liste/detay consumer tarafında `roles=0` uyumu vardır; mevcut base'i #229 dalıdır ve doğrudan main'e retarget edilmesi başka şeridin planlama dosyalarını diff'e taşır. Kaynak korunarak owner tarafından temiz entegrasyon adayı hazırlanmalıdır.

## 2. Kilitli kullanıcı sonucu

Aynı hukuki karşı taraf tek `Party` kimliğidir. Cari oluştururken veya mevcut Party'yi yetkili olunan bir Göymen şirketiyle ilişkilendirirken önceden sabit `Müşteri / Tedarikçi / Fason / Hizmet` ticari sınıflandırması seçmek zorunlu değildir.

Ticari yön ilgili gerçek iş işleminden gelir. Cari üzerindeki legacy ticari sınıflandırma:

- kullanıcı yetkisi değildir,
- şirket erişimi değildir,
- alış/satış yapabilme kapısı değildir,
- hesabın aktif/pasif durumunu belirlemez,
- ikinci bir Party/Cari kimliği oluşturma nedeni değildir.

Korunanlar: `Party.Kind`, değişmez Cari kodu, `CompanyPartyAccount`, `CompanyId` izolasyonu, Identity permission/rol sistemi, aktif/pasif yaşam döngüsü, audit, idempotency ve concurrency.

## 3. Gerçekleşmiş wire sözleşmesi

### 3.1 İstek

`POST /api/v1/counterparties` için `kind` ve `legalName` zorunludur. `tradeName` ve `roles` opsiyoneldir.

`POST /api/v1/counterparties/{partyId}/company-account` için rol seçimi zorunlu değildir; geçerli JSON object `{}` rol-yok ilişki isteğidir.

`roles` için geçerli command anlamları:

- alan eksik → `PartyRole.None`;
- sayısal tamsayı `0` → `PartyRole.None`;
- sayısal tamsayı `1..15` → mevcut legacy flag birleşimleri.

Geçersiz command değerleri: `null`, string, kesirli sayı, negatif değer, `16` veya daha üstte tanımlı olmayan bitler ve malformed JSON. Link route'unda HTTP body'nin tamamen yokluğu `{}` ile aynı sayılmaz; route'un mevcut body/mediatype doğrulaması korunur.

### 3.2 Yanıt

Account projection'larındaki `roles` alanı **zorunlu sayısal tamsayıdır ve 0..15 aralığındadır**.

- `0` geçerlidir: belirtilmiş legacy ticari sınıflandırma yoktur.
- `1..15` geçerlidir: eski flag değerleridir.
- **15 geçerlidir.** `15` reddedilecek değer değildir.
- `16`, negatif, kesirli, `null` veya eksik response alanı malformed contract'tır.

Bu ayrım list, detail, mutation ve link-candidate olmak üzere dört account reader yüzeyinde aynıdır.

### 3.3 GET değişmezi

Liste, detay, link-candidate araması, filtre, count ve pagination:

- Party oluşturmaz,
- `CompanyPartyAccount` oluşturmaz,
- hesabı aktifleştirmez,
- business audit/idempotency etkisi üretmez.

HTTP legacy role filtresi ayrı kavramdır. Filtresiz okuma `role`/gereksiz `roleMatch` göndermez. `role=0` yeni bir “rolsüzleri filtrele” contract'ı değildir.

## 4. SQL, migration, retry ve rollback sınırı

PR #230 adayı `CompanyPartyAccount.Roles` alanını NOT NULL integer olarak bırakır ve check constraint'i `1..15` → `0..15` genişletir. Eski migration dosyaları veya legacy `1..15` verileri rewrite/backfill edilmez.

Aynı normalize command + aynı actor + aynı company + aynı idempotency key tekrarında ikinci business etki oluşmaz. Actor/company/payload değişirse mevcut conflict sınırı korunur. Farklı key'lerle aynı `(CompanyId, PartyId)` link yarışında tek account kalmalıdır.

Audit veya durable idempotency persistence başarısızsa business write atomik rollback olmalıdır.

Downgrade veri-bağımlıdır:

- henüz `roles=0` satırı yoksa eski constraint'e dönüş mümkün olabilir;
- `roles=0` satırı varsa Down DDL öncesi fail-closed reddetmelidir (`SQL 51000` adayı);
- rollback için hesabı silmek, `Customer=1` veya `15` uydurmak yasaktır;
- expanded schema + compatible reader korunur, yeni write durdurulur ve forward-fix uygulanır.

## 5. Çok şirketli Cari görünümü — iş kuralı

Bu konu tek-şirket Cari akışının blokajı değildir.

Değişmez iş modeli:

1. Hukuki cari/Party **tek kalır**.
2. Her Göymen şirketi ilişkisi ayrı `CompanyPartyAccount` olarak kalır.
3. Grup görünümünde aynı Party tek bir hukuki kimlik olarak gruplanabilir; fakat altındaki şirket hesapları açıkça ayrılmalıdır.
4. Her şirket hesabının `CompanyId`, aktif/pasif durumu ve legacy sınıflandırması başka şirket hesabıyla sessiz birleştirilemez.
5. Gelecekte bakiye/risk/limit/tutar gösterildiğinde şirket ve para birimi boyutu korunur. Farklı şirket/döviz tutarları tek hesap değeri gibi mahsuplaştırılamaz veya birleştirilemez.
6. Görsel tercih (şirket başına alt satır, açılır alt kayıt veya şirket rozetli ayrı account bölümü) geri alınabilir UX kararıdır. İş kuralı bunların herhangi birinde yukarıdaki ayrımı zorunlu tutar.

Dolayısıyla grup görünümünün nihai piksel düzeni, C-01/C-02/C-03/link tek-şirket düzeltmelerini bekletmez.

## 6. Kabul kimliği uzlaştırması

PR #229 içindeki `ILK_DILIM_CARI_ROL_UZLASTIRMA.md` dosyasının CR kimlikleri değiştirilmez ve geriye dönük yeniden anlamlandırılmaz.

| Paket-local kimlik | Bu belgedeki kimlik | Anlam |
|---|---|---|
| CR-01 | CRO-AC-001 | Roleless create |
| CR-02 | CRO-AC-002 | Migration, legacy koruma, invalid SQL/wire sınırı |
| CR-03 | CRO-AC-003 | Roleless company link |
| CR-04 | CRO-AC-004 | Şirket/yetki/non-disclosure |
| CR-05 | CRO-AC-005 | Same-key retry ve conflict |
| CR-06 | CRO-AC-006 | Different-key race ve transactional rollback |
| CR-07 | CRO-AC-007 | Durable legacy/roleless replay |
| CR-08 | CRO-AC-008 | Roleless read/filter/candidate ve GET saflığı |
| CR-09 | CRO-AC-009 | Passive/version/permission ayrımı |
| CR-10 | CRO-AC-010 | Mixed old/new client ve rollout |
| — | CRO-AC-011 | Wire invalid-response/request matrisi |
| — | CRO-AC-012 | Başarı sonrası detail/list dönüşü ve state/focus |

Gelecekte aynı roleless Carinin gerçek alış ve satış siparişlerinde kullanılmasını kanıtlayacak senaryo bu Cari diliminde çalışmış test değildir. Yerel referans: `FUT-CARI-ORDER-001`; durum `FUTURE / NOT_IMPLEMENTED / NOT_RUN`.

## 7. Kanıt sınıfları

Kanıtlar birbirinin yerine kullanılmaz:

- **REAL_SQL**: gerçek SQL Server üzerinde backend/HTTP persistence kabulü.
- **COMPONENT**: React/Vitest decoder/form/state davranışı; gerçek tarayıcı veya gerçek server değildir.
- **CONTROLLED_BROWSER**: kontrollü/stub/seam tabanlı Chromium; kullanıcı akışını ölçer ama gerçek backend/persistence kanıtı değildir.
- **ISOLATED_C01_BROWSER**: izole compose yığınında gerçek API + frontend browser koşusu; yalnız aktive edilen capability ve test edilen contract kapsamını kanıtlar.
- **INTEGRATED_ROLELESS_BROWSER**: #227 baseline + güncel #230 transition/backend + dört reader/form uyumu ile roleless create/list/detail/link akışının gerçek browser/API/SQL birleşik kanıtı. Henüz yoktur.

“SUCCESS” yalnız ilgili sütunun kendi kapsamıdır; başka sütuna taşınmaz.

## 8. Kanıta bağlı kabul matrisi

| Kabul | REAL_SQL | COMPONENT | CONTROLLED_BROWSER | ISOLATED_C01_BROWSER | INTEGRATED_ROLELESS_BROWSER | Güncel hüküm |
|---|---|---|---|---|---|---|
| CRO-AC-001 roleless create | **PASS candidate** — #230 `CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.MissingAndZeroRoles_CreateAndLinkShareReplayAndRejectInvalidJson`; SQL broad 72/72 skip0 @ `d97f8ee` | #228 create form mevcut ama rolü hâlâ zorunlu tutuyor | #228 17/17 eski roleful kontrollü akış; roleless kanıt değildir | C-01 liste testi; ilgisiz | **NOT_RUN** | Backend davranışı kanıtlı; kullanıcı create kabulü açık |
| CRO-AC-002 migration/legacy | **PASS candidate** — #230 previous-schema upgrade, safe Down ve roleless-Down refusal testleri | N/A | N/A | N/A | N/A | #230 head'inde kanıtlı; main `060da63` ile conflict/revalidation bekliyor |
| CRO-AC-003 roleless link | **PASS candidate** — #230 missing/0 link + race testleri | #228 link formu mevcut ama rol zorunluluğu eski | #228 kontrollü link kanıtı roleful; roleless değildir | C-01 ilgisiz | **NOT_RUN** | Backend kanıtlı; roleless UI/link kabulü açık |
| CRO-AC-004 company/yetki | **PASS candidate** — #230 roleless read/write authorization ve company isolation | #228/#231 mevcut capability/company guard testleri yalnız kendi yüzeyinde | Kısmi kontrollü sınır kanıtı | #231 C-01 yetkili liste regresyonu var | **NOT_RUN roleless** | Backend PASS candidate; birleşik negatif browser açık |
| CRO-AC-005 same-key retry | **PASS candidate** — #230 durable replay ve same-key retry | #228 `commandAttempt/useCommandAttempt` mevcut; korunacak | 17/17 koşum unknown-outcome davranışını kontrollü response ile ölçer; `persistentServerReplayClaimed=false` | C-01 ilgisiz | **NOT_RUN** | Gerçek server + browser retry birleşimi açık |
| CRO-AC-006 race/rollback | **PASS candidate** — #230 different-key link race ve audit/idempotency failure rollback | N/A | N/A | N/A | N/A | Backend candidate PASS; restack sonrası yeniden kanıt gerekir |
| CRO-AC-007 durable replay | **PASS candidate** — #230 legacy fingerprint/durable replay korunumu | N/A | kontrollü replay gerçek durable server replay değildir | N/A | **NOT_RUN mixed-version** | Backend candidate PASS; rollout kanıtı ayrı |
| CRO-AC-008 roleless read/GET saflığı | **PASS candidate** — #230 roleless list/detail/candidate + GET no-write | **PASS kısmi** — #231 list/detail `roles=0`, null/missing/unknown-bit guard testleri; Node24 suite 266/266 | #228 mutation/candidate guard `>=1`, roleless için henüz uyumsuz | **PASS yalnız liste/regresyon** — #231 B-C01-001..010 10/10 ve ayrı 16/16 liste davranışı, fakat API #230 öncesi olduğundan `roles=0` cevabı kanıtlamaz | **NOT_RUN** | Dört reader tamamlanmadan genel PASS yok |
| CRO-AC-009 passive/version/permission | #230 domain inactive+roles0 ayrımını ve genel concurrency sınırını koruyor; exact roleless stale-version bağı coordinator kanıtında ayrı PASS olarak pinli değil | N/A | N/A | N/A | **NOT_RUN** | **PARTIAL**; mevcut backend reconciliation işinde exact bağ doğrulanacak/eksikse eklenecek |
| CRO-AC-010 mixed client | Backend #230 rollout gereksinimi tanımlı | #231 yeni reader/eski contract uyumluluğu component düzeyinde | N/A | **PASS tek yön:** yeni #231 frontend + #230 öncesi backend, C-01 10/10 | **NOT_RUN:** yeni backend+eski reader, roleless yeni form+eski backend, old-tab reload/block | **PARTIAL / CR-10 NOT_RUN** |
| CRO-AC-011 invalid wire | **PASS candidate request tarafı** — #230 null/string/negative/16/malformed etkisiz reddi | **PASS kısmi response tarafı** — #231 list/detail `0` kabul; null/missing/negative/fraction/16 reject; #228 mutation/candidate 0 uyumu henüz eksik | Eski controlled form suite yeni contract'ı kanıtlamaz | Liste C-01 yeni roleless wire üretmiyor | **NOT_RUN** | Dört reader/form uzlaşması bekliyor |
| CRO-AC-012 başarı/list dönüşü | **Baseline PASS** — merge edilmiş #227 gerçek SQL create→list→detail→link yolculuğu; roleless değildir | #231 ilk sayfadaki kayıt için state/focus dönüşü testli; ikinci sayfa cursor zinciri replay edilmez | #228 create/link kontrollü browser kullanışlı tarihsel kanıt | #231 detail capability bu koşuda aktif değildi; detail→list browser koşulmadı | **NOT_RUN** | **PARTIAL**. “Kayıt ilk sayfada değil; liste ilk sayfadan yeniden okundu” uyarısı tam dönüş kabulü değildir |

### 8.1 #227'nin doğru kullanımı

Merge edilmiş #227, mevcut API'nin gerçek SQL üzerinde aynı Party'yi A şirketinde oluşturup B'ye bağlayabildiğinin ve okuma/replay'in ek business etki üretmediğinin baseline kanıtıdır. `roles` opsiyonelliği #227'de uygulanmadığı için CRO-AC-001/003'ün roleless UI kabulü olarak kullanılamaz.

### 8.2 #228 kontrollü Chromium'un doğru kullanımı

#228 current source tree için kontrollü kabul artifact'i `TRX 80/80`, helper `54/54`, browser `17/17`, retry/skipped/global error `0` olarak raporlanmıştır. Artifact kendi özetinde `local_checks_passed_not_ERP_acceptance`, `publish_ready:false`, `erp_accepted:false` der. Persistent server replay, gerçek roleless backend, gerçek browser zoom veya screen-reader konuşmasını kanıtlamaz.

### 8.3 #231 browser kanıtının doğru kullanımı

#231'in izole C-01 koşusu artık **RUN** durumundadır. B-C01-001..010 `10/10 PASS`; ayrıca liste davranışı için tek-seferlik `16/16` koşu kaydı vardır. Bu yığın yalnız `cari.parties.list` capability'sini açmıştır. Detail, create ve link için bu koşu kanıt değildir.

Koşudaki API #230 öncesi contract'tır. Bu nedenle ölçülen kombinasyon **yeni reader + eski backend** yönüdür; CR-10'un ters yönünü veya roleless write'ı kanıtlamaz.

## 9. Frontend sözleşme düzeltmesi — dört reader + formlar

Tek frontend writer mevcut #228 kaynak devrini kullanmalıdır; yeni paralel create/link/router implementation açılmaz.

Dört response reader için ortak guard:

```text
required roles field
integer
0 <= roles <= 15
known mask only
```

Geçerli: `0`, `1`, ..., `15`.

Geçersiz: missing response field, `null`, string, fraction, negative, `16+`/unknown bit.

#231 list/detail yüzeylerinde bu guard uygulanmıştır. #228'de mutation/link-candidate tarafındaki `wireGuards.isKnownRoleFlags` hâlâ `>=1` kabulü yaptığı için `0` cevabı malformed sayabilir; bu düzeltilmelidir.

Create/link formlarında sabit ticari rol seçimi submit şartı olmaktan çıkarılmalıdır. Request `roles` alanını hiç göndermeyebilir veya `0` gönderebilir; kullanıcıya yeni zorunlu sınıflandırma checkbox'ı uydurulmaz. Legacy sınıflandırma gösterilecekse opsiyonel/geçmiş bilgi olduğu açık olmalıdır.

Korunacak davranışlar:

- legal kind/name,
- aktif CompanyContext,
- source/target Party/company binding,
- non-disclosure,
- dirty-form koruması,
- aynı logical attempt için aynı idempotency key,
- unknown-outcome güvenli retry,
- company switch/logout generation fence,
- malformed success response'un “başarılıymış gibi” gösterilmemesi,
- yetkisiz action'ın render edilmemesi.

## 10. Mixed-version geçiş kapısı

Roleless write'ın var olması, onu hemen normal runtime'da açma yetkisi değildir.

Güvenli iş sırası:

1. `roles=0..15` okuyabilen consumer'lar hazırlanır.
2. Uyumsuz açık tab/client'lara roleless response ulaşmasını engelleyen somut reload/block/min-client benzeri fail-closed mekanizma kanıtlanır.
3. #230 SQL/backend contract main ile uzlaştırılır; #227'nin mandatory SQL journey envanteri kaybolmaz.
4. Yeni roleless formun eski backend'e submit etmesi fail-closed engellenir/kanıtlanır.
5. Roleless write capability ancak bu uyumluluk kapıları sağlandıktan sonra normal runtime adayı olur.
6. Gerçek integrated browser/API/SQL acceptance ve K3 exact-head review tamamlanır.
7. Production aktivasyonu ayrıca Sinan kapısındadır.

Bu belge somut teknik mekanizmayı (`minClientVersion`, reload token, feature capability vb.) seçmez; CODEX-BACKEND ve CLAUDE-FRONTEND mevcut mimari içinde en dar fail-closed çözümü üretir ve coordinator exact-head kanıtlar.

## 11. Kalan görevler — mevcut sahipleri koruyarak

### CODEX-BACKEND — mevcut PR #230 sahibi

**CB-1 — main/#227 uzlaştırması.** Kaynak: `main@060da63`, coordinator #230 kaydı. Beklenen: #230 owner kendi branch'inde #227 ile çakışan üç dosyayı uzlaştırır; #227 create/list/detail/link mandatory SQL journey, omission testleri, skip-zero ve fingerprint guard'ları kaybolmaz. Bitiş: yeni exact head, yeniden hesaplanmış mandatory SQL inventory, skip=0 ve diff'te journey korunumu.

Bilinen conflict yüzeyi:

- `tools/verify_rcsi_relational_acceptance.py`
- `tests/rcsi_relational_acceptance_verifier/test_verify_rcsi_relational_acceptance.py`
- `tests/GoymenERP.ArchitectureTests/PlatformTenantQueryFilterBypassGuardTests.cs`

**CB-2 — fail-closed transition.** Kaynak: package CR-10 / CRO-AC-010 ve coordinator rollout handoff. Beklenen: consumer compatibility sağlanmadan missing/0 role write persist edemez; yeni form eski backend'e sessiz roleless request gönderemez; eski reader incompatible success/read ile devam edemez. Bitiş: version/transition negatifleri + real SQL/API kanıtı; GET no-write korunur.

**CB-3 — mevcut kanıtı kapatma.** Kaynak: CRO-AC-009. Beklenen: roleless inactive account üzerinde stale expected-version/permission davranışının mevcut testle birebir kanıtı varsa matrise pinlenir; yoksa aynı backend reconciliation işinde dar test eklenir. Yeni paralel görev açılmaz.

**CB-4 — final K3 kapısı.** Kaynak: AGENTS K3. Bitiş: fresh required CI + real SQL skip0 + yeni exact head için iki bağımsız review attestation; eski d97 review'ları yeni delta'yı tek başına kapsıyor sayılmaz.

### CLAUDE-FRONTEND — #231 sahibi / #228 donor-recipient hattı

**CF-1 — kaynak devrini tamamla, duplicate uygulama açma.** Kaynak: #228 `d46f7f8` ve coordinator handoff. Beklenen: #228'deki mevcut C-02/link/router/attempt/form kaynakları explicit HANDOFF-RELEASED/HANDOFF-ACCEPTED ile tek frontend writer'a geçer. Bitiş: exact source inventory ve tek aktif writer.

**CF-2 — dört reader'ı birleştir.** Kaynak: #230 wire contract + #231 `48373f9/3da9192`. Beklenen: list/detail/mutation/link-candidate response `roles=0..15`, **15 dahil**, kabul eder; null/missing/fraction/negative/16 reddeder; CompanyId/id/version guard'ları korunur. Bitiş: dört projection için focused tests green.

**CF-3 — create/link form uyumu.** Kaynak: kullanıcı kararı + #230 request contract. Beklenen: create/link rol seçmeden gönderilebilir; mandatory role validation/text kaldırılır; source role hedefe kör kopyalanmaz; dirty-form/idempotency/company fences korunur. Bitiş: component + controlled browser source tests; ikinci bir create/link implementation yok.

**CF-4 — integrated browser/a11y handoff.** Kaynak: CRO-AC-001/003/005/010/012. Beklenen: corrected frontend + transitioned backend ile roleless create→detail/list→link, retry/unknown-outcome, company/logout stale response, old/new version gate, error/focus/a11y. Bitiş: gerçek integrated browser/API/SQL koşusu skip/retry beklentileriyle raporlanır. C-01 liste koşusu bunun yerine kullanılamaz; actual browser zoom/screen-reader yapılmadıysa PASS yazılmaz.

### CODEX-ORCHESTRATOR

**CO-1.** Yeni paralel create/link veya backend implementation görevi açma; CB ve CF mevcut owner teslimlerini bekle.

**CO-2.** Entegrasyon ağacında #227 merged baseline'ın kaybolmadığını, #229 insan karar paketinin implementation diff'ine yanlışlıkla taşınmadığını ve #231 kaynaklarının main-based temiz adayda yalnız owner tarafından taşındığını doğrula.

**CO-3.** K3 merge için exact head/base, fresh CI, gerçek SQL, ilgili gerçek browser, iki bağımsız review ve P0/P1=0 kapılarını doğrula. Kontrollü Chromium veya C-01 liste kanıtını roleless integrated acceptance yerine koyma.

**CO-4.** #229'u insan review olmadan merge/accepted sayma; production/capability aktivasyonunu ayrıca Sinan kapısında bırak.

### CHATGPT-BUSINESS

**GB-1.** Bu spec ve CRO matrisi güncel exact-head kanıtlarla truth-sync tutulur; implementation statüsü ile kullanıcı kabulü ayrılır.

**GB-2.** Sonraki yalnız bir dilim, aşağıdaki otomatik şirket ilişkisi pre-spec'idir. Bunun ötesinde sipariş/genel ERP analizi açılmaz.

## 12. Sonraki tek dilim — otomatik şirket ilişkisi pre-spec

Yerel çalışma adı: `CARI_AUTO_ACCOUNT_ON_FIRST_AUTHORIZED_BUSINESS_COMMAND_JIT_v1`.

Durum: **PRE-SPEC / IMPLEMENTATION-READY DEĞİL / NOT_RUN**.

Bu dilim mevcut opsiyonel-rol düzeltmelerini bloke etmez. Ancak mevcut roleless/mixed-version entegrasyon kapısı kapanmadan implementation başlamaz.

### 12.1 Tetikleyici

GET/list/detail/search/candidate tetikleyici değildir.

Yalnız gerçekten var olan, authenticated ve yetkilendirilmiş bir **business write command** şirket hesabına ihtiyaç duyduğu anda account ensure değerlendirebilir. Henüz uygulanmamış satış/satın alma endpoint'i sırf bu kabul için uydurulmaz.

Tetikleyici bağlamı en az:

- authenticated actor,
- resolved tek `CompanyContext`,
- `PartyId`,
- ilgili business command'in kendi permission'ı,
- Party görünürlük/uygunluk kontrolü,
- command'in kendi validation'ı,
- idempotency/correlation bilgisi.

Hedef `CompanyId` request body'den serbestçe seçilmez; doğrulanmış CompanyContext'ten gelir.

### 12.2 Account davranışı

- Hedef şirkette uygun aktif `CompanyPartyAccount` varsa tekrar oluşturulmaz.
- Hesap yoksa, yalnız izin politikası sağlanıyorsa `roles=0` account ensure edilebilir.
- Başka şirketin role/aktiflik/bakiye bilgisinden hedef hesaba kör kopyalama yapılmaz.
- Mevcut hesap pasifse sessiz reaktivasyon yapılmaz; active-state/version/permission sözleşmesi ayrı kalır.

### 12.3 Atomiklik ve rollback

Account ensure ile onu gerektiren business write aynı logical başarı zincirine bağlıdır. Business persistence/audit/idempotency başarısızsa yeni account kalmamalıdır. Modül sınırı korunur; bir business modülü Counterparty tablosuna doğrudan yazmaz.

### 12.4 Idempotency ve race

Aynı business command replay'i ikinci account yaratmaz. İki eşzamanlı command aynı `(CompanyId, PartyId)` account'a ihtiyaç duyarsa account cardinality `1` kalır; her business command kendi authorization/idempotency sonucuna göre tamamlanır.

### 12.5 Açık yetki politikası

Aşağıdaki seçim **henüz kullanıcı tarafından onaylanmış değildir**:

- **A:** business command permission + mevcut `counterparty.accounts.create` birlikte gerekir.
- **B — CHATGPT-BUSINESS önerisi:** ilgili business command permission'ı yalnız kendi transaction'ı içinde eksik account ensure etkisini yetkilendirir; kullanıcıya standalone link yetkisi vermez.
- **C:** ayrı `counterparty.accounts.ensure` permission tanımlanır.

`B` yalnız öneridir. Kullanıcı onayı gelmeden `KILITLI`, `ACCEPTED`, `DONE` veya production politikası olarak kaydedilemez.

## 13. Gerçek kullanıcı kararı gereken maddeler

### NEEDS-SINAN-CARI-AUTO-001 — otomatik account ensure permission kaynağı

Karar: **A / B / C**.

Öneri: **B**, çünkü günlük zorunlu ayrı link adımını kaldırma hedefini standalone Cari-link yetkisi dağıtmadan karşılar. Ancak bu öneri mevcut role-optional Cari düzeltmesinin şartı değildir.

Çok şirketli Cari görünümünün tam görsel kompozisyonu bu aşamada kullanıcı karar kapısı değildir; yukarıdaki veri ayrımı iş şartıdır, piksel/alt-satır tercihi geri alınabilir UX kararı olarak ilerleyebilir.

PR #229 için insan review/acceptance kapısı ayrıca korunur; bu belge o review'u kendiliğinden tamamlamaz.
