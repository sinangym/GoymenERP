# ChatGPT–Codex Göymen ERP Yürütme Playbook'u v1.2

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

## 5. Normatif Dalga 0

```text
PR #64 MERGED
→ PR #68
→ PR #31
→ Issue #53
→ PR #36
→ W1
```

- #68, eski ve kapatılmış #60'ın güncel-main taşımasıdır.
- #31, #68 merge olduktan sonra current main ile senkronize edilir.
- #53, #31 ile sertleştirilmiş CI üzerinde geliştirilir.
- #36, #53 sonrasında duplicate header/permission kodu taşımadan temizlenir.
- W1 bütün Dalga 0 çıkışları kapanmadan başlamaz.

## 6. ChatGPT aktif sıra

1. PR #65 plan ve truth remediation
2. PR #68 current-main Idempotency-Key hotfix
3. PR #31 salt-okunur iş etkisi review
4. Issue #53 Counterparty AuditEvent tenant/write guard
5. PR #36 Cari Core 1C finalizasyonu
6. W1 gate açılırsa CARI-UI-01
7. PRD-01
8. SAL-01

## 7. Codex aktif sıra

1. PR #65 yeni exact-head re-review
2. PR #68 exact-head read-only review
3. PR #31 current-main sync ve full CI
4. Issue #53 PR'ını read-only review
5. PR #36 final head review
6. W0 kapanınca ERP-000K execution gate kontrolü
7. Gate açılmışsa PLT-01 common shell

## 8. Readiness ve execution ayrımı

Her workstream iki ayrı statü taşır:

```text
dor_status
execution_status
```

- DoR hazır olabilir ama dependency/blocker açıksa execution blocked kalır.
- Canonical `task_manifest.json` statüsünü machine overlay değiştiremez.
- Accepted veya merged ancak exact-head CI ve karşı review ile kaydedilir.
- Released ayrıca migration, activation, recovery ve operasyon kanıtı gerektirir.

## 9. Kanonik platform gate kaynakları

| Workstream | Canonical task | Readiness kaynağı | Ana blocker |
|---|---|---|---|
| OUT-01 | ERP-000F | PR #10 | ADR-006, OPEN-022…025 |
| DOC-01 | ERP-000G | PR #11 | ADR-005, OPEN-026…029 |
| PDF-01 | ERP-000H | PR #13 | ERP-000G, OPEN-019, decision pack |
| SRCH-01 | ERP-000I | PR #12 | ADR-007, OPEN-015, M, L, search contract |
| AI-01 | ERP-000J | PR #14 | ADR-008, OPEN-020, L, AI contract |
| PLT-01 | ERP-000K | PR #18 | W0, shell contract, PR #18 dependencies |
| OBS-01 | ERP-000L | PR #17 | M, N ve kayıtlı OPEN/decision pack |
| DB-01 | ERP-000M | main readiness | ADR-003/010, OPEN-014/015/017, SQL PoC contract |
| REC-01 | ERP-000N | PR #16 | M ve recovery blocker'ları |
| DEL-01 | ERP-000O | PR #19 | M, ADR-010, OPEN-014, delivery contract |

Bu gate'ler kapanmadan ilgili workstream executable değildir.

## 10. Raporlama gate'i

`RPT-01`, PR #2'nin yedi blocker'ı kapanmadan başlayamaz:

1. Current-main content ve semantic conflicts
2. Mevcut acceptance kanıtlarının korunması
3. Canonical task manifest truth preservation
4. Karar Sicili/Master discovery bağı
5. ADR-008, OPEN-020, veri sınıflandırması, KVKK ve default-deny
6. ERP-RPT-001/RPT-003 acceptance döngüsünün çözülmesi
7. R-06/R-07 UI katalog veya açık backlog uzlaştırması

Ek olarak current CI ve UI validator yeşil olmalıdır.

## 11. Event delivery karar ağacı

```text
Etki aynı transaction içinde mi?
    Evet → senkron application contract kullanılabilir.
    Hayır → durable delivery gerekli mi?
        Hayır → transient veya yeniden hesaplanabilir olduğu açıkça belirtilir.
        Evet → ERP-000F minimumu kabul edildi mi?
            Hayır → execution blocked.
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
BLOCKED BY
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

1. Bütün dosyalarda `#64 → #68 → #31 → #53 → #36 → W1` sırası
2. Overlay'in canonical task graph'ı supersede etmemesi
3. `canonical_task_ids`, `blocked_by`, `dor_status`, `execution_status`, `event_delivery_mode`
4. Identity iş politikası ve platform contract sahiplik ayrımı
5. PR #10/#11/#13/#18/#19 readiness gate eşlemesi
6. PR #2 raporlama blocker'ları
7. `AGENTS.md`, orkestratör ve `task_manifest.json` zorunlu başlangıç kaynakları
8. Karar Sicili önceliği ve StokERP donor sınırı
9. JSON parse, benzersiz ID, resolved dependency ve acyclic graph
10. `git diff --check` temizliği
11. Full CI ve UI golden

Yeni commit eski review'u geçersiz yapar.

## 16. Bloke olunca çalışma

Ajan:

1. Blocker'ı exact kaynakla GitHub'a yazar.
2. Sahiplik dışına çıkmaz.
3. Aynı dosyada ikinci writer açmaz.
4. Kendi alanındaki sonraki bağımsız göreve geçer.
5. D2 gerekiyorsa tek karar paketi hazırlar.
6. Çalışmayı yalnız “bekliyorum” diyerek durdurmaz.

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

Merge sonrası:

- Yeni main SHA
- New-main CI
- Bağımlı branch'lerin stale işaretlenmesi
- Issue #66 durum güncellemesi

## 18. Otonom yürütme talimatı

```text
Açık insan kararı gerektirmeyen bütün güvenli teknik adımları art arda tamamla.
Yalnız D2 ürün kararları, kritik merge kapıları ve production activation için dur.
Bir iş bloke olursa sahiplik sınırına uyan sonraki bağımsız işi başlat.
Her bulguyu GitHub'a exact SHA ve kanıtla yaz.
Sohbet özetini source-of-truth sayma.
Auto-merge yapma.
```

Bu playbook `AGENTS.md`, orkestratör veya kanonik görev grafiğini değiştirmez; onların altında çalışan hızlı yürütme protokolüdür.
