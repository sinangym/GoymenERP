# Cari Opsiyonel Ticari Rol Uzlaştırma Şartnamesi v1

- Tarih: 11 Eylül 2026
- Şerit: `CHATGPT-BUSINESS`
- Durum: **REVIEW EDİLEBİLİR İŞ ŞARTNAMESİ / ERP KABULÜ DEĞİL**
- Dosya: `docs/features/CARI_ROLE_OPTIONAL_RECONCILIATION_SPEC_v1.md`
- Hazırlık tabanı: `main@060da63f2644cb271d78d9eab8b7a6985d544ad1`
- İlgili kaynaklar: PR #227, #228, #229, #230, #231, #232, #233 ve Issue #66 koordinasyon kayıtları.
- Öncelikli transport kaynağı: PR #233 comment `5626451657` (`#230@080e7a1` exact-source handoff).

Bu belge uygulama kodu yazmaz, kanonik karar/task durumunu değiştirmez, `docs/planning/2026-09-10/**` veya paylaşılan ilerleme belgelerinin tek-yazar sahipliğini devralmaz. PR #229 içindeki tarihli karar paketi insan review kapısında kalır; burada kullanılması onun merge edildiği veya insan tarafından kabul edildiği anlamına gelmez. Otomatik şirket ilişkisi için aşağıdaki `B` seçeneği yalnız CHATGPT-BUSINESS önerisidir; kullanıcı kararı değildir.

## 1. Kaynak önceliği ve güncel depo gerçeği

Kaynak önceliği `AGENTS.md` ile aynıdır: en son tarihli kullanıcı kararı → `KARAR_SICILI` → Master Rapor → ilgili feature/ADR → eski referanslar. Açık yetki, güvenlik, migration veya production politikası tahminle kapatılmaz.

11 Eylül 2026 doğrulamasında:

- PR #227 merge edilmiştir; `main=060da63f2644cb271d78d9eab8b7a6985d544ad1`. #227 mevcut create/list/detail/company-link API akışının gerçek-SQL baseline kabul yolculuğudur; roleless frontend, mixed-version veya production kabulü değildir.
- PR #228 açıktır ve draft'tır; `head=d46f7f819456fc578a25cdcabf5c98ad6f634456`. C-02 create/link/router/attempt ve kontrollü Chromium donor kaynağı burada zaten vardır. Aynı implementation paralel yeniden yazılmaz.
- #228 recipient makbuzu `5626181170` **HANDOFF-ACCEPTED (recipient)** olarak yayımlanmıştır; gerçek donor `HANDOFF-RELEASED` hâlâ gereklidir. Recipient makbuzu donor release yerine geçmez.
- PR #229 açıktır ve draft'tır; `head=e1fd69da69df36a72e559ea07163fb3f359b3c61`. İnsan karar incelemesi gereken planlama paketidir.
- PR #230 açıktır ve draft'tır. `080e7a1d24f74975b45546c5277f0529c13f06d1` main `060da63` ile uzlaştırılmış transport-gate adayının exact kanıt başıdır: 7/7 CI, gerçek SQL `75/75`, exact RCSI `10/10`, skip=0. Ampere bu SHA için P0=0/P1=0/P2=0; James aynı SHA için error-ack dokümantasyonunda P2=1 bildirmiştir.
- #230 güncel head `880cae838e5d2f01a988f1e4a89afbe67858f48c`, James P2'sini `docs(openapi): clarify conditional counterparty error acknowledgment` ile düzeltmeyi hedefler. Bu yeni SHA'nın CI'si doğrulama anında çalışmaktadır; `080e7a1` başarısı `880cae8` için otomatik PASS sayılmaz.
- PR #233 açıktır ve draft'tır; `head=424a586fc07efa291db4b37866ba5cce54fb7443`, base `main@060da63`. Temiz main tabanlı 15 frontend yoludur; Node24 component suite `267/267 PASS`, C-01 gerçek browser `10/10 PASS`. Bu head yeni `X-ERP-Counterparty-Contract` protokolünü henüz uygulamamıştır.
- PR #231 `173b003` tarihsel kaynak olarak korunur; aktif temiz frontend adayı #233'tür.

## 2. Kilitli kullanıcı sonucu

Aynı hukuki karşı taraf tek `Party` kimliğidir. Cari oluştururken veya mevcut Party'yi yetkili olunan bir Göymen şirketiyle ilişkilendirirken önceden sabit `Müşteri / Tedarikçi / Fason / Hizmet` ticari sınıflandırması seçmek zorunlu değildir.

Ticari yön ilgili gerçek iş işleminden gelir. Cari üzerindeki legacy ticari sınıflandırma:

- kullanıcı yetkisi değildir,
- şirket erişimi değildir,
- alış/satış yapabilme kapısı değildir,
- hesabın aktif/pasif durumunu belirlemez,
- ikinci bir Party/Cari kimliği oluşturma nedeni değildir.

Korunanlar: `Party.Kind`, değişmez Cari kodu, `CompanyPartyAccount`, `CompanyId` izolasyonu, Identity permission/rol sistemi, aktif/pasif yaşam döngüsü, audit, idempotency ve concurrency.

## 3. Payload wire sözleşmesi

### 3.1 İstek

`POST /api/v1/counterparties` için `kind` ve `legalName` zorunludur. `tradeName` ve `roles` opsiyoneldir.

`POST /api/v1/counterparties/{partyId}/company-account` için rol seçimi zorunlu değildir; geçerli JSON object `{}` rol-yok ilişki isteğidir.

`roles` command anlamları:

- alan eksik → `PartyRole.None`;
- sayısal tamsayı `0` → `PartyRole.None`;
- sayısal tamsayı `1..15` → mevcut legacy flag birleşimleri.

Geçersiz command değerleri: `null`, string, kesirli sayı, negatif değer, `16` veya tanımsız bitler ve malformed JSON. Link route'unda HTTP body'nin tamamen yokluğu `{}` ile aynı sayılmaz; mevcut body/mediatype doğrulaması korunur.

### 3.2 Yanıt

Account projection'larındaki `roles` alanı **zorunlu sayısal tamsayıdır ve 0..15 aralığındadır**.

- `0` geçerlidir: belirtilmiş legacy ticari sınıflandırma yoktur.
- `1..15` geçerlidir.
- **15 geçerlidir.**
- `16`, negatif, kesirli, `null` veya eksik response alanı malformed contract'tır.

Bu ayrım list, detail, mutation ve link-candidate dört account reader yüzeyinde aynıdır.

### 3.3 GET değişmezi

Liste, detay, link-candidate araması, filtre, count ve pagination:

- Party oluşturmaz,
- `CompanyPartyAccount` oluşturmaz,
- hesabı aktifleştirmez,
- business audit/idempotency etkisi üretmez.

Filtresiz okuma `role` göndermez; gereksiz `roleMatch` göndermez. `role=0` yeni bir “rolsüzleri filtrele” contract'ı değildir.

## 4. Client-server compatibility transport sözleşmesi

`roles=0` okuyabilmek **tek başına yeterli değildir**. Yeni frontend ile yeni backend'in birbirini exact protokol düzeyinde tanıması ve command öncesinde mevcut server/context uyumluluğunu taze olarak kanıtlaması gerekir.

### 4.1 Contract declaration

Desteklenen her Cari exchange'de istemci tam olarak bir kez şunu gönderir:

```text
X-ERP-Counterparty-Contract: optional-trade-roles-v1
```

Command'lerde bu declaration zorunludur. Missing command header → `409 client_contract_required`. Empty/duplicate/combined/unsupported declaration → `409 client_contract_unsupported`.

Bu pre-handler contract reddi:

- business handler çalıştırmaz,
- Party/account yazmaz,
- business audit yazmaz,
- durable idempotency result üretmez.

Header authentication, permission, capability veya feature flag değildir.

### 4.2 Command readiness yalnız taze başarılı GET ile oluşur

Form/yazma aksiyonu ancak aynı güncel bağlamda yapılan **taze, başarılı ve gövdesi başarıyla doğrulanmış bir GET** sonrasında açılabilir.

Hazırlık kanıtı birlikte şunları gerektirir:

```text
HTTP success
+ response DTO validation success
+ exact X-ERP-Counterparty-Contract: optional-trade-roles-v1 acknowledgment
+ aynı API origin
+ aynı actor/session
+ aynı selected CompanyId
+ aynı client generation
```

Readiness global bir boolean değildir. Logout, session değişimi, company değişimi, API origin değişimi veya generation/fence değişimi readiness'i geçersiz kılar. Geç kalan response eski context için readiness kuramaz.

Eski server request header'ı yok sayıp `200` dönebilir fakat acknowledgment vermeyebilir. Bu durumda yeni client formu kapalı tutar ve **POST sayısı 0** olmalıdır. Uyumluluğu aşmak için `Customer=1` veya `15` uydurulmaz.

### 4.3 GET compatibility davranışı

Legacy/headerless GET, görünür projection yalnız `roles=1..15` içeriyorsa mevcut authorized legacy başarıyı döndürebilir. Görünür authorized projection içinde `roles=0` varsa incompatible success body yerine compatibility `409` verilir.

Unsupported GET declaration mevcut authorization/validation/not-found/availability davranışını maskelenmemelidir. Örneğin unauthorized list `403` kalır; acknowledgment veya account kimliği sızdırmaz. Foreign-company zero row görünür hale gelmez.

### 4.4 Error response acknowledgment semantiği

**Hata response'undaki acknowledgment readiness değildir ve command success değildir.**

`080e7a1` runtime'da exact declaration client-contract filtresinden geçtikten sonra handler tarafından üretilen bazı error response'lar acknowledgment taşıyabilir. Buna karşılık authentication, company resolution veya body binding gibi filtre öncesi hatalar acknowledgment içermeyebilir.

Bu nedenle:

- error + matching ack → readiness kurulmaz;
- error + matching ack → business success değildir;
- error + ack yok → tek başına “eski server” kanıtı değildir;
- mevcut HTTP/problem-code/unknown-outcome davranışı korunur.

James P2, OpenAPI'nin error response'lardaki bu koşullu header gerçeğini eksik belgelemesiydi. `880cae8` bu açıklamayı generated OpenAPI'ye taşımayı hedefleyen P2 düzeltmesidir. `880cae8` final CI/review tamamlanmadan P2 “final PASS” olarak bu belgeye yazılmaz.

### 4.5 Command acknowledgment ve unknown outcome

Command exchange declaration gönderir ve response acknowledgment doğrular.

Başarılı command response için matching acknowledgment beklenir. Command response'ta acknowledgment eksik/uyumsuzsa istemci sonucu güvenli biçimde **unknown outcome** olarak ele alır; “başarısız oldu, yeni key ile yeniden dene” sonucuna atlamaz.

Unknown outcome sırasında aşağıdakiler korunur:

- aynı command attempt,
- aynı actor/session ve CompanyContext,
- aynı normalized payload,
- aynı Party/resource binding,
- **aynı Idempotency-Key**.

Daha sonra gelen compatibility `409` bu attempt'i silmez. İstemci otomatik yeni key üretmez ve otomatik blind retry yapmaz. Güncel context tekrar doğrulandıktan ve taze başarılı GET + exact acknowledgment yeniden alındıktan sonra yalnız kullanıcının/akışın açık same-attempt retry'ı aynı key ile yapılabilir.

Create ve link birbirinden ayrı logical attempt'tir; birbirlerinin key'ini paylaşmaz.

### 4.6 Mixed-node sınırı

Bu protokol eski backend node'larını retrofit etmez. Aynı deployment'ta yeni/eski node karışımı varsa coordinator düzeyinde affinity/drain/routing checkpoint gerekir. Bir istekte acknowledgment alınmış olması sonraki request'in aynı uyumlu node'a gideceğini kendiliğinden kanıtlamaz.

Mixed-node routing/drain production politikası bu spec tarafından seçilmez ve production aktivasyonu vermez.

## 5. SQL, migration, retry ve rollback sınırı

PR #230 `CompanyPartyAccount.Roles` alanını NOT NULL integer olarak bırakır ve check constraint'i `1..15` → `0..15` genişletir. Eski migration dosyaları veya legacy `1..15` verileri rewrite/backfill edilmez.

Aynı normalized command + aynı actor + aynı company + aynı idempotency key tekrarında ikinci business etki oluşmaz. Actor/company/payload değişirse mevcut conflict sınırı korunur. Farklı key'lerle aynı `(CompanyId, PartyId)` link yarışında tek account kalmalıdır.

Audit veya durable idempotency persistence başarısızsa business write atomik rollback olmalıdır.

Downgrade veri-bağımlıdır:

- henüz `roles=0` satırı yoksa eski constraint'e dönüş mümkün olabilir;
- `roles=0` satırı varsa Down DDL öncesi fail-closed reddetmelidir (`SQL 51000`);
- rollback için hesabı silmek, `Customer=1` veya `15` uydurmak yasaktır;
- expanded schema + compatible reader korunur, yeni write durdurulur ve forward-fix uygulanır.

## 6. Çok şirketli Cari görünümü — iş kuralı

Bu konu tek-şirket Cari akışının blokajı değildir.

Değişmez iş modeli:

1. Hukuki cari/Party **tek kalır**.
2. Her Göymen şirketi ilişkisi ayrı `CompanyPartyAccount` olarak kalır.
3. Grup görünümünde aynı Party tek hukuki kimlik olarak gruplanabilir; altındaki şirket hesapları açıkça ayrılmalıdır.
4. Her şirket hesabının `CompanyId`, aktif/pasif durumu ve legacy sınıflandırması başka şirket hesabıyla sessiz birleştirilemez.
5. Gelecekte bakiye/risk/limit/tutar gösterildiğinde şirket ve para birimi boyutu korunur. Farklı şirket/döviz tutarları tek hesap değeri gibi mahsuplaştırılamaz/birleştirilemez.
6. Şirket başına alt satır, açılır account alanı veya rozetli görünüm gibi sunum tercihi geri alınabilir UX kararıdır; iş kuralı yukarıdaki ayrımı korur.

Grup görünümünün nihai piksel düzeni C-01/C-02/C-03/link tek-şirket düzeltmelerini bekletmez.

## 7. Kabul kimliği uzlaştırması

PR #229 `ILK_DILIM_CARI_ROL_UZLASTIRMA.md` içindeki CR kimlikleri değiştirilmez ve yeniden anlamlandırılmaz.

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

Yeni transport senaryoları ayrı bir kimlik ailesi oluşturmaz; CRO-AC-001/003/005/008/010/011/012 içine aşağıda bağlanır.

Gelecekte aynı roleless Carinin gerçek alış ve satış siparişlerinde kullanılmasını kanıtlayacak senaryo bu Cari diliminde çalışmış test değildir. Yerel referans: `FUT-CARI-ORDER-001`; durum `FUTURE / NOT_IMPLEMENTED / NOT_RUN`.

## 8. Kanıt sınıfları

Kanıtlar birbirinin yerine kullanılmaz:

- **REAL_SQL**: gerçek SQL Server üzerinde backend/HTTP persistence ve transport kabulü.
- **COMPONENT**: React/Vitest decoder/form/context/readiness davranışı; gerçek tarayıcı veya gerçek server değildir.
- **CONTROLLED_BROWSER**: kontrollü/stub/seam tabanlı Chromium; gerçek backend/persistence değildir.
- **ISOLATED_C01_BROWSER**: izole compose yığınında gerçek API + frontend; yalnız aktive edilen capability ve exact test kapsamını kanıtlar.
- **INTEGRATED_ROLELESS_BROWSER**: güncel backend transport/schema + güncel frontend protocol/forms + roleless create/list/detail/link + SQL persistence birleşik kanıtı.

Exact-head kanıtı yalnız kendi SHA'sına aittir. `080e7a1` başarıları `880cae8` veya sonraki SHA'ya otomatik taşınmaz.

## 9. Kanıta bağlı kabul matrisi

| Kabul | REAL_SQL / backend | COMPONENT | CONTROLLED / C-01 browser | INTEGRATED_ROLELESS_BROWSER | Güncel hüküm |
|---|---|---|---|---|---|
| CRO-AC-001 roleless create | **PASS @080e7a1**: main reconcile + optional-role SQL/protocol inventory korunmuş; broad `75/75`, RCSI `10/10`, skip0. Command header gate eksik/unsupported write'ı handler öncesi durdurur. | #228 donor formunda role zorunluluğu tarihsel olarak hâlâ var; #233 transport/form birleşimi henüz yok. | #228 eski roleful controlled create kanıtı; #233 C-01 ilgisiz. | **NOT_RUN** | Backend exact 080 PASS; kullanıcı create + fresh-GET readiness + new-client/old-server POST0 browser açık. |
| CRO-AC-002 migration/legacy | **PASS @080e7a1**: migration/Down/legacy replay korunumu, #227 inventory ile main uzlaşması tamamlanmış. | N/A | N/A | N/A | 080 exact-head kanıtlı; 880 P2 docs SHA'sına CI otomatik taşınmaz. |
| CRO-AC-003 roleless link | **PASS @080e7a1**: missing/0 link, race, transaction ve protocol gate. Target company için taze readiness ayrıca frontend sorumluluğu. | #228 link form donor kaynağı; donor release bekleniyor. | Eski roleful controlled link tarihsel kanıt. | **NOT_RUN** | Target-company fresh GET + ack → same Party/roles0 B link integrated browser açık. |
| CRO-AC-004 company/yetki | **PASS @080e7a1**: authorization/company isolation korunur; unsupported GET declaration handler 403/404 gibi error'ları maskelenmez; zero detection authorized projection sınırındadır. | #233 mevcut company/generation reader guard'ları kendi kapsamlarında testli. | #233 C-01 authorized list regresyonu. | **NOT_RUN roleless** | Header permission değildir; birleşik negatif browser açık. |
| CRO-AC-005 same-key retry | **PASS @080e7a1**: durable same-key replay/protocol facts. Command compatibility rejection handler/idempotency etkisi üretmez. | Donor `commandAttempt/useCommandAttempt` same-attempt semantics korunacak; yeni protocol unknown-outcome testleri henüz yok. | #228 controlled unknown-outcome `persistentServerReplayClaimed=false`; gerçek durable browser replay değildir. | **NOT_RUN** | Missing/mismatched command ack → unknown; same key/context/payload korunmalı; auto new-key yasak. |
| CRO-AC-006 race/rollback | **PASS @080e7a1**: prior race/rollback + reconciled mandatory inventory. | N/A | N/A | N/A | Backend exact-head PASS. |
| CRO-AC-007 durable replay | **PASS @080e7a1**: legacy/roleless replay ve command transport declaration davranışı backend scope'ta korunur. | N/A | Controlled replay durable server kanıtı değildir. | **NOT_RUN mixed-version** | Backend PASS; client/mixed-node kanıtı ayrı. |
| CRO-AC-008 roleless read/GET saflığı | **PASS @080e7a1**: roleless list/detail/candidate GET no-write + compatibility 409/non-disclosure protocol facts. | **PASS kısmi @#233/424a586**: list/detail roles `0..15`, null/missing/fraction/negative/16 reject; Node24 `267/267 PASS`. Mutation/candidate donor tarafı henüz alınmadı. | #233 gerçek C-01 `10/10 PASS`; yalnız list/regression, yeni transport header/readiness uygulanmış değil. | **NOT_RUN** | Dört reader + transport readiness tamamlanmadan genel PASS yok. |
| CRO-AC-009 passive/version/permission | 080 backend kapsamı active-state command'i de contract header'a bağlar; önceki lifecycle/version kuralları korunur. Exact yeni integrated browser yok. | N/A | N/A | **NOT_RUN** | Backend contract mevcut; end-to-end active-state optional-role kanıtı genel Cari kapanışı değildir. |
| CRO-AC-010 mixed client/rollout | **PASS backend protocol scope @080e7a1**, fakat mixed-node/browser değildir. Eski client headerless GET zero projection görürse incompatible success withheld/409; command header gate vardır. | #233 `424a586` yeni transport protokolünü henüz uygulamıyor. | #233 C-01 yeni frontend + eski backend yalnız payload/list regresyonudur; new protocol readiness değildir. | **NOT_RUN**: old-client/new-server, new-client/old-server, mixed-node affinity/drain, pending/company/logout/unknown | **PARTIAL / CR-10 NOT_RUN**. |
| CRO-AC-011 invalid wire/transport | **PASS @080e7a1**: invalid roles + missing/unsupported/duplicate contract declaration protocol facts. James P2 yalnız error-header dokümantasyon tamamlığıydı. | #233 list/detail payload guard PASS; transport error-ack semantics henüz component'e alınmadı. | Mevcut C-01 header protokolünü test etmiyor. | **NOT_RUN** | 880cae8 error-header docs düzeltmesi mevcut fakat final CI/review sonucu bekleniyor. |
| CRO-AC-012 başarı/detail/list dönüşü | #227 baseline create→list→detail→link roleful gerçek SQL; 080 transport backend scope ekler, frontend journey değildir. | **PASS kısmi @424a586**: sonraki sayfa dönüşü artık taze cursor ile en çok 4 sayfa ileri walk; remembered cursor replay edilmez. | #233 C-01 list browser PASS; detail capability/return walk browserda çalıştırılmış değildir. | **NOT_RUN** | Component dönüş düzeltmesi tam roleless create/detail/link/browser kabulü değildir. |

### 9.1 `080e7a1` kanıt sınırı

`080e7a1` için doğrulanmış: main `060da63` uzlaşması, 7/7 final-head CI, SQL `75/75`, RCSI `10/10`, skip0. Ampere exact SHA için P0/P1/P2=0 raporlamıştır. James aynı exact SHA'da yalnız conditional error-response acknowledgment dokümantasyonunda P2=1 bulmuştur. Bu P2 runtime authorization açığı değil contract completeness açığıdır.

`880cae8` P2 düzeltme commit'idir. Şu an `080e7a1` test başarısı `880cae8` için PASS olarak kopyalanmaz; `880cae8` kendi final CI/review evidence'ını tamamlamalıdır.

### 9.2 #233 kanıt sınırı

#233 `424a586`, main tabanlı temiz 15 frontend yoldur. Node24 suite `267/267 PASS`, exact-head C-01 `10/10 PASS`. Sonraki sayfa dönüşü component düzeyinde taze cursor walk ile geliştirilmiştir.

Bu head yeni `X-ERP-Counterparty-Contract` declaration/ack/readiness protokolünü henüz uygulamaz. C-01 yalnız listeyi kanıtlar; create/detail/link, CR-10 veya new protocol integrated acceptance değildir.

### 9.3 #228 donor/recipient sınırı

Recipient `HANDOFF-ACCEPTED` `5626181170` ile mevcuttur. Donor `HANDOFF-RELEASED` henüz eksiktir. Donor release olmadan #228 shared/create/link yüzeyleri #233 owner tarafından devralınmış sayılmaz. Bu spec donor gibi makbuz vermez.

## 10. Frontend implementation sözleşmesi

Tek frontend writer mevcut #233 sahibidir; #228 donor release sonrasında mevcut create/link/router kaynaklarını devralır. Yeni paralel implementation açılmaz.

### 10.1 Dört reader

List/detail/mutation/link-candidate response için:

```text
roles required
integer
0 <= roles <= 15
known mask only
```

Geçerli: `0..15` — **15 dahil**. Geçersiz: missing/null/string/fraction/negative/16+ unknown bit.

#233 list/detail uyumludur. Mutation/candidate donor code'daki `>=1` guard donor release sonrası aynı contract'a çekilir.

### 10.2 Formlar

Create/link mandatory role validation/text kaldırılır. Request roles omitted veya `0` olabilir. Source account rolü target account'a kör kopyalanmaz. Dirty/pending, idempotency attempt, Party/company binding, action gating ve a11y korunur.

### 10.3 Transport readiness

Frontend her desteklenen exchange'de exact declaration gönderir. Command UI readiness yalnız taze başarılı validated GET + matching response acknowledgment ile kurulur ve şu tuple'a bağlıdır:

```text
(apiOrigin, actor/session, selectedCompanyId, generation)
```

Tuple değişiminde readiness düşer. Old server 200 + ack yok → form blocked, POST0. Error response readiness kurmaz. Pre-filter error'da ack yokluğu old-server teşhisi değildir.

### 10.4 Unknown outcome

Command response acknowledgment eksik/uyumsuzsa unknown outcome. Same attempt/key/context/payload korunur. Otomatik yeni key yok; otomatik blind retry yok. Fresh successful ack + current context sonrasında yalnız same-key explicit retry.

## 11. Kalan görevler — mevcut sahipleri koruyarak

### CODEX-BACKEND — PR #230 mevcut sahibi

**CB-1 — P2 finalizasyonu.** `880cae8` conditional error-response acknowledgment açıklamasını OpenAPI/handoff/test contract'ında tamamlar. Bitiş: bu SHA veya daha yeni exact head için fresh required CI ve P2 closure review; `080e7a1` sonuçları otomatik taşınmaz.

**CB-2 — protocol invariants koruma.** Missing/unsupported command declaration handler öncesi 409; authorized read compatibility behavior; error header koşullu semantiği; CORS allowlist; GET no-write; same-key durable retry; SQL75/RCSI envanteri sonraki head'de korunur.

**CB-3 — izole integrated acceptance altyapı devri.** Koordinatör sırasına göre mevcut backend/test altyapı sahibi create/detail/link capability desteğini run/commit/company/permission-bound biçimde tamamlar. Bu yeni backend implementation görevi değildir; mevcut test altyapısının integrated roleless browser için açılmasıdır.

### CLAUDE-FRONTEND — PR #233 mevcut sahibi

**CF-1 — yeni protocol exact-source receipt.** `080e7a1`/sonraki kabul edilen backend protocolünü exact source olarak al; declaration + stable error code + readiness contract'ı #233'e uygula. Eski payload-only handoff yeterli değildir.

**CF-2 — donor release sonrası dört reader/form entegrasyonu.** Recipient receipt zaten `5626181170`; donor HANDOFF-RELEASED gelmeden shared #228 yüzeylerine yazma. Release sonrası mutation/candidate roles0 guard, create/link mandatory-role kaldırma ve mevcut command ProblemCode'ları + yeni `client_contract_required/unsupported` kodları birlikte korunur.

**CF-3 — context-bound readiness component testleri.** Fresh GET+ack; old server 200/no-ack → POST0; error ack readiness kurmaz; pre-filter error no-ack old-server demek değildir; origin/actor/session/company/generation fence; pending/late response; logout/company change.

**CF-4 — unknown attempt.** Missing/mismatched command ack → unknown; exact attempt/payload/context/key tutulur; later 409 attempt'i silmez; auto new key/retry yok; fresh ack sonrası same-key explicit retry.

**CF-5 — integrated browser.** Koordinatörün pinned backend/frontend/schema/fixture head'lerinde roleless create→list→detail→B link; authorization/company negatives; old-client/new-server; new-client/old-server; company/logout/pending/unknown/same-key. C-01 `10/10` bunun yerine kullanılamaz. Mixed-node routing/drain coordinator koşuludur.

### CODEX-ORCHESTRATOR

**CO-1.** Yeni paralel spec/create-link/backend writer açma; mevcut #230/#233 ve donor ownership sırasını koru.

**CO-2.** #228 recipient receipt'i tekrar isteme; yalnız gerçek donor release'i takip et. Bu business spec donor makbuzu vermez.

**CO-3.** Backend exact SHA evidence'ını taşıma: `080e7a1` PASS ≠ `880cae8` PASS. Her yeni head için fresh CI/review sonucu ayrıca kaydedilir.

**CO-4.** Integrated browser için capability fixture/test altyapısını mevcut sahibine tamamlat; “yalnız liste açık” durumunu kalıcı ürün engeli diye yorumlama.

**CO-5.** Final K3 kapısında exact backend/frontend/schema/fixture pinleri, fresh CI, gerçek SQL, integrated browser ve iki bağımsız exact-head review doğrula. Production activation ayrı Sinan kapısıdır.

### CHATGPT-BUSINESS

**GB-1.** Yalnız bu #232 dosyasını değişen exact-head evidence ile truth-sync tut; yeni paralel spec oluşturma.

**GB-2.** CRO-AC kimlikleri ve CR eşlemesi sabit tutulur; transport acceptance yeni ID ailesine bölünmez.

**GB-3.** Sonraki yalnız bir dilim otomatik şirket ilişkisi pre-spec'idir; coordinator sırası roleless/mixed-version Cari kapanışından sonra implementation'a geçer.

## 12. Sonraki tek dilim — otomatik şirket ilişkisi pre-spec

Yerel çalışma adı: `CARI_AUTO_ACCOUNT_ON_FIRST_AUTHORIZED_BUSINESS_COMMAND_JIT_v1`.

Durum: **PRE-SPEC / IMPLEMENTATION-READY DEĞİL / NOT_RUN**.

Bu dilim mevcut opsiyonel-rol düzeltmelerini bloke etmez. Mevcut roleless/mixed-version entegrasyon kapısı kapanmadan implementation başlamaz.

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

Mevcut role-optional/transport Cari düzeltmesi için yeni kullanıcı iş kararı gerekmemektedir. Backend P2, contract dokümantasyon/test düzeltmesi olarak mevcut teknik yetki kapsamında kapatılır.

### NEEDS-SINAN-CARI-AUTO-001 — otomatik account ensure permission kaynağı

Karar gelecekte implementation başlamadan önce: **A / B / C**.

Öneri: **B**, çünkü günlük zorunlu ayrı link adımını standalone Cari-link yetkisi dağıtmadan karşılar. Ancak bu öneri mevcut role-optional Cari düzeltmesinin şartı değildir ve henüz onaylanmış sayılmaz.

Çok şirketli Cari görünümünün tam görsel kompozisyonu bu aşamada kullanıcı karar kapısı değildir; veri ayrımı iş şartıdır, sunum geri alınabilir UX kararı olarak ilerleyebilir.

PR #229 ve bu PR #232 insan review/acceptance kapıları ayrıca korunur; bu belge bu review'ları kendiliğinden tamamlamaz.
