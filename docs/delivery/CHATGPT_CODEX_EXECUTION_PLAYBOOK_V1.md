# ChatGPT–Codex Göymen ERP Yürütme Playbook'u v1.3

## 1. Her döngünün zorunlu başlangıcı

ChatGPT ve Codex her çalışma döngüsünde sırasıyla şunları okur ve uygular:

1. `AGENTS.md`
2. `docs/governance/CODEX_OTONOM_PROJE_ORKESTRATORU_v1.md`
3. `docs/decisions/KARAR_SICILI.md`
4. `docs/master/GOYMEN_ERP_MASTER_RAPOR_V3.md`
5. `docs/backlog/task_manifest.json`
6. Göreve ilişkin ADR, feature spec, acceptance, formula ve UI kartları
7. `docs/delivery/GOYMEN_ERP_END_TO_END_EXECUTION_PLAN_V1.md`
8. `docs/backlog/erp_e2e_workstreams_v1.json`
9. Canlı main, açık PR'lar, worktree, exact SHA, diff ve CI

E2E workstream dosyası ikincil execution overlay'idir. `AGENTS.md`, orkestratör ve `task_manifest.json` yerine geçmez.

## 2. Kaynak önceliği

```text
En son ürün sahibi kararı
→ KARAR_SICILI.md
→ Master Rapor v3
→ eski prototip/StokERP yalnız donör veya referans
```

StokERP'den alınan kural kabul edilmiş contract ve golden test olmadan production davranışı olamaz.

## 3. Ana çalışma değişmezleri

- Exact SHA, diff ve CI ortak gerçektir.
- Aynı aggregate, migration, shared CI, ortak shell veya contract üzerinde tek writer vardır.
- Reviewer diğer ajanın branch'ine yazmaz.
- Head değişirse eski review ve CI geçersizdir.
- P0/P1 merge engeller.
- P2/P3 ayrı issue'ya taşınır.
- D2 kararı olmadan para, stok, muhasebe, rol, retention, cutover veya topology kodlanmaz.
- ERP-000F kabul edilmeden durable async event veya worker iddiası yapılmaz.
- UI, API, DB, security, real SQL ve E2E tamamlanmadan modül done değildir.
- Auto-merge yoktur.
- Bir iş bloke olursa ajan kendi sahiplik alanındaki sonraki bağımsız güvenli işe geçer.

## 4. Sahiplik

### 4.1 CHATGPT-BUSINESS

Production ve contract sahibi olduğu alanlar:

- Counterparty iş davranışı
- Product ve Service
- Sales ve Purchasing
- Business Purchases
- Inventory ve Allocation
- Production ve Traceability
- Logistics ve Weighbridge
- Finance, Treasury ve Accounting
- Rapor semantiği
- Modül UI'ları
- Formula, golden test, migration mapping ve UAT

### 4.2 CODEX-PLATFORM

Production ve platform contract sahibi olduğu alanlar:

- Identity ve authentication
- Generic authorization
- Common React shell ve design system
- Generic approval ve task engine
- Document Vault technical core ve PDF runtime
- Outbox, inbox ve workers
- Search infrastructure
- AI gateway ve policy enforcement
- Observability
- CI/CD, migration tooling ve deployment
- Backup ve restore
- Generic integration tooling

### 4.3 Identity özel ayrımı

Machine plan şu alanları ayrı taşır:

```text
business_policy_owner: CHATGPT-BUSINESS
platform_contract_owner: CODEX-PLATFORM
primary_writer: CODEX-PLATFORM
business_policy_reviewer: CHATGPT-BUSINESS
security_merge_gate: HUMAN
```

Örnek:

- `CARI-PERM-001` rol matrisi ChatGPT business policy alanıdır.
- Generic reconciliation, auth ve RBAC/ABAC contract'ı Codex platform alanıdır.
- Tek bir `contract_owner` alanı iki sahiplik alanını birleştiremez.

## 5. Normatif Dalga 0 ve canlı durum

```text
PR #64 MERGED
→ PR #68 MERGED
→ PR #31 HUMAN MERGE GATE
→ Issue #53
→ PR #36
→ W1
```

- #64, Option A Identity reconciliation çekirdeğidir ve main'dedir.
- #68, eski #60'ın current-main taşımasıdır ve `main@5a4f56bc1e05ca1ce7f030f4c9c253f46e104e9f` ile merge edilmiştir.
- #68 new-main CI ve UI golden yeşildir.
- #31 exact head `4c5854881d9d8ee8256e98e686065629925ec504`, current-main, Draft/CLEAN/MERGEABLE, full CI yeşil ve ChatGPT security review kabul edilmiştir; insan shared-CI/security merge kararı bekler.
- #53, #31 merge ve new-main CI tamamlanana kadar `start_blocked_by=[PR-31]` durumundadır.
- #36, #53 tamamlanana kadar başlamaz; sonrasında duplicate header/permission parçaları taşımadan current-main üzerinde temizlenir.
- W1 bütün Dalga 0 başlangıç engelleri ve tamamlanma kapıları kapanmadan başlamaz.

## 6. ChatGPT aktif sıra

1. PR #65 canlı gerçek ve gate-semantik remediation
2. PR #31 exact-head shared-CI/security review — tamamlandı
3. PR #31 insan merge ve new-main CI sonrasında Issue #53
4. Issue #53 merge sonrasında PR #36
5. W1 başlangıç engelleri kapanırsa CARI-UI-01
6. PRD-01
7. SAL-01

## 7. Codex aktif sıra

1. PR #31 insan shared-CI/security merge kapısı
2. PR #31 merge sonrası new-main CI ve handoff
3. PR #65 yeni exact-head read-only re-review
4. Issue #53 PR'ını read-only review
5. PR #36 final head review
6. W0 ve ERP-000K başlangıç engelleri kapanınca PLT-01 common shell

## 8. DoR, execution ve gate semantiği

Her wave, workstream ve aktif kayıt aşağıdaki alanları ayrı taşır:

```text
dor_status
execution_status
start_blocked_by
completion_gates
```

- `dor_status`, analiz ve sözleşme hazırlığını gösterir; execution izni değildir.
- `start_blocked_by`, işe başlanmasını yasaklayan çözülmemiş önkoşullardır.
- `completion_gates`, çalışma sürerken açık kalabilir; fakat accepted/merged/released durumuna geçişi engeller.
- `start_blocked_by=[]` olan iş `in_progress` olabilir.
- Açık `completion_gates` bulunan `in_progress` iş henüz kabul veya merge edilmiş sayılmaz.
- Eski belirsiz `blocked_by` alanı yasaktır.
- Canonical `task_manifest.json` statüsünü machine overlay değiştiremez.
- Released ayrıca migration, activation, recovery ve operasyon kanıtı gerektirir.

## 9. Kanonik platform gate kaynakları

| Workstream | Canonical task | Readiness kaynağı | Başlangıç engelleri |
|---|---|---|---|
| OUT-01 | ERP-000F | PR #10 | ADR-006, OPEN-022…025 |
| DOC-01 | ERP-000G | PR #11 | ADR-005, OPEN-026…029 |
| PDF-01 | ERP-000H | PR #13 | ERP-000G, OPEN-019, decision pack |
| SRCH-01 | ERP-000I | PR #12 | ADR-007, OPEN-015, M, L, search contract |
| AI-01 | ERP-000J | PR #14 | ADR-008, OPEN-020, L, AI contract |
| PLT-01 | ERP-000K | PR #18 | W0, shell contract, PR #18 dependencies |
| OBS-01 | ERP-000L | PR #17 | M, N ve kayıtlı OPEN/decision pack |
| DB-01 | ERP-000M | main readiness | ADR-003/010, OPEN-014/015/017, SQL PoC contract |
| REC-01 | ERP-000N | PR #16 | M ve recovery başlangıç engelleri |
| DEL-01 | ERP-000O | PR #19 | M, ADR-010, OPEN-014, delivery contract |

Bu `start_blocked_by` kayıtları kapanmadan ilgili workstream başlamaz. CI, karşı review, UAT ve insan merge gibi doğrulamalar ayrıca `completion_gates` olarak tutulur.

## 10. Raporlama gate'i

`RPT-01`, PR #2'nin yedi başlangıç engeli kapanmadan başlayamaz:

1. Current-main content ve semantic conflicts
2. Mevcut acceptance kanıtlarının korunması
3. Canonical task manifest truth preservation
4. Karar Sicili/Master discovery bağı
5. ADR-008, OPEN-020, veri sınıflandırması, KVKK ve default-deny
6. ERP-RPT-001/RPT-003 acceptance döngüsünün çözülmesi
7. R-06/R-07 UI katalog veya açık backlog uzlaştırması

Current CI, UI validator ve insan review ise RPT-01 kabul/merge tamamlanma kapılarıdır.

## 11. Event delivery karar ağacı

```text
Etki aynı transaction içinde mi?
    Evet → senkron application contract kullanılabilir.
    Hayır → durable delivery gerekli mi?
        Hayır → transient veya yeniden hesaplanabilir olduğu açıkça belirtilir.
        Evet → ERP-000F minimumu kabul edildi mi?
            Hayır → execution başlamaz.
            Evet → outbox/inbox, retry, dedup, poison ve redrive.
```

Notification, OCR, AI worker, external delivery ve async search projection bu ağaca tabidir.

## 12. Dikey dilim contract'ı

Her production PR aşağıdaki kısa sözleşmeyi taşır:

```text
SLICE-CONTRACT
User outcome
Canonical decisions
Canonical task IDs
Readiness sources
Source-of-truth module
Business policy owner
Platform contract owner
Data model
State transitions
Money/quantity/unit/formula
Permission/CompanyContext
Approval/SoD
Audit/reversal
Idempotency/concurrency
Event delivery mode
UI states
SQL acceptance
Browser E2E
Out of scope
```

## 13. Handoff formatı

```text
HANDOFF-V1
OWNER
WORKSTREAM
SLICE
BASE
HEAD
USER OUTCOME
CANONICAL TASK IDS
READINESS SOURCES
START BLOCKED BY
COMPLETION GATES
DOR STATUS
EXECUTION STATUS
EVENT DELIVERY MODE
BUSINESS POLICY OWNER
PLATFORM CONTRACT OWNER
WRITE SCOPE
READ-ONLY BOUNDARIES
TEST EVIDENCE
OPEN P0/P1
OPEN D2
NEXT SAFE ACTION
DO NOT
```

Karşı review:

```text
HANDOFF-ACCEPTED veya REQUEST_CHANGES
Reviewed-Head
Reviewer
Result
Evidence
P0
P1
P2 follow-ups
```

## 14. Zorunlu testler

Her business write için uygulanabilir olanlar:

- Authorized success
- Unauthorized 403
- Cross-company non-disclosure
- Same-key replay
- Same-key payload mismatch
- Different-key race
- Stale version
- Audit failure rollback
- Idempotency failure rollback
- Gerçek SQL

UI:

- Loading
- Empty
- Error
- Forbidden
- Not found
- Conflict
- Responsive
- Keyboard
- Visual regression

Formül:

- Formula version
- Input snapshot
- Unit ve rounding
- Golden data
- Tolerance boundaries
- Invalid input
- Stale preview rejection

## 15. PR #65 re-review kontrolü

Codex yeni exact head üzerinde şunları doğrular:

1. Bütün dosyalarda `#64 merged → #68 merged → #31 human merge gate → #53 → #36 → W1` sırası
2. Overlay'in canonical task graph'ı supersede etmemesi
3. `canonical_task_ids`, `start_blocked_by`, `completion_gates`, `dor_status`, `execution_status`, `event_delivery_mode`
4. `blocked_by` alanının bulunmaması
5. Identity iş politikası ve platform contract sahiplik ayrımı
6. D2 registry kimliklerinin Karar Sicili kaynaklarıyla çözülmesi
7. PR #10/#11/#13/#18/#19 readiness gate eşlemesi
8. PR #2 raporlama başlangıç engelleri
9. `AGENTS.md`, orkestratör ve `task_manifest.json` zorunlu başlangıç kaynakları
10. Karar Sicili önceliği ve StokERP donor sınırı
11. JSON parse, benzersiz ID, çözülmüş gate referansı ve acyclic graph
12. `git diff --check` temizliği
13. Full CI ve UI golden

Yeni commit eski review'u geçersiz yapar.

## 16. Başlangıç engeli veya tamamlanma kapısı açıldığında

Ajan:

1. Gate'i exact kaynakla GitHub'a yazar.
2. Bunun `start_blocked_by` mı yoksa `completion_gate` mi olduğunu açıkça sınıflandırır.
3. Sahiplik dışına çıkmaz.
4. Aynı dosyada ikinci writer açmaz.
5. Başlangıç engeli varsa kendi alanındaki sonraki bağımsız göreve geçer.
6. Yalnız tamamlanma kapısı açıksa güvenli geliştirmeyi sürdürebilir; kabul/merge iddiasında bulunmaz.
7. D2 gerekiyorsa tek karar paketi hazırlar.
8. Çalışmayı yalnız “bekliyorum” diyerek durdurmaz.

## 17. Merge

İnsan onayı zorunlu:

- Identity ve permission
- Shared CI
- Destructive schema ve migration
- Stok, para ve muhasebe politikası
- Production activation
- Backup ve recovery
- Cutover

Merge öncesi:

- Exact head
- Expected-head koruması
- Full CI
- Gerçek SQL
- Karşı review
- P0/P1 sıfır
- Bütün completion gate'lerin kapalı olması

Merge sonrası:

- Yeni main SHA
- New-main CI
- Bağımlı branch'lerin stale işaretlenmesi
- Issue #66 durum güncellemesi

## 18. Otonom yürütme talimatı

```text
Açık insan kararı gerektirmeyen bütün güvenli teknik adımları art arda tamamla.
Yalnız D2 ürün kararları, kritik merge kapıları ve production activation için dur.
Bir iş start_blocked_by nedeniyle başlayamıyorsa sahiplik sınırına uyan sonraki bağımsız işi başlat.
Yalnız completion_gates açıksa güvenli geliştirmeyi sürdür fakat işi accepted/merged sayma.
Her bulguyu GitHub'a exact SHA ve kanıtla yaz.
Sohbet özetini source-of-truth sayma.
Auto-merge yapma.
```

Bu playbook `AGENTS.md`, orkestratör veya kanonik görev grafiğini değiştirmez; onların altında çalışan hızlı yürütme protokolüdür.
