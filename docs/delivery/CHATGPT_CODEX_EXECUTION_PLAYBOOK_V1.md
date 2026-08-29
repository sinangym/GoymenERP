# ChatGPT–Codex Göymen ERP Yürütme Playbook'u v1.1

**Amaç:** Göymen ERP'yi iki ajanla hızlı, çakışmasız ve doğrulanabilir biçimde tamamlamak.  
**Kanonik plan:** `docs/delivery/GOYMEN_ERP_END_TO_END_EXECUTION_PLAN_V1.md`  
**Machine overlay:** `docs/backlog/erp_e2e_workstreams_v1.json`  
**Kanonik görev grafiği:** `docs/backlog/task_manifest.json`  
**Karar sırası:** son ürün sahibi kararı → `KARAR_SICILI.md` → Master v3 → eski prototip/StokERP referansı  
**Son doğrulanan main:** `7e832d2b98a07428d46ead54a9d136b24c62203a`

---

## 1. Değişmez çalışma ilkeleri

1. GitHub exact SHA/diff/CI ortak gerçektir.
2. E2E plan task graph'ı supersede etmez; overlay'dir.
3. Aynı aggregate/shared dosyada tek writer.
4. Reviewer diğer ajanın branch'ine yazmaz.
5. Head değişirse review ve CI stale.
6. P0/P1 merge engeller; P2/P3 ayrı issue'ya taşınır.
7. Bilinen karar yeniden analiz edilmez.
8. D2 karar olmadan para/stok/muhasebe/rol/topoloji üretilmez.
9. Outbox kabul edilmeden async durability iddia edilmez.
10. UI + API + DB + security + real SQL + E2E olmadan modül done değildir.
11. Auto-merge yoktur.
12. Bir iş bloke olursa ajan kendi sahiplik alanındaki sonraki bağımsız işi seçer.

---

## 2. Her oturumun başlangıç algoritması

Her ajan:

```text
1. fetch/prune
2. current main SHA
3. kendi açık PR'ları
4. karşı ajanın review bekleyen PR'ları
5. Issue #66 ve machine overlay
6. kanonik task IDs ve blocker'lar
7. aynı dosya/aggregate üzerinde başka writer var mı?
8. exact güvenli sonraki iş
```

kaydı çıkarır.

Zorunlu durum özeti:

```text
DONE
IN REVIEW
BLOCKED
SAFE NEXT
CURRENT MAIN SHA
ACTIVE WRITE SCOPE
REVIEWED HEADS
CI EVIDENCE
```

---

## 3. Normatif Dalga 0

Tek sıra:

```text
PR #64 → PR #60 → PR #31 → Issue #53 → PR #36 → W1
```

Güncel:

- PR #64 merged as `main@7e832d2…`.
- PR #60 ChatGPT tarafından current-main tabanında yeniden taşınacak.
- PR #31 PR #60 sonrasında Codex tarafından sync edilecek.
- Issue #53 PR #31 sonrasında ChatGPT tarafından kapatılacak.
- PR #36 en son temizlenecek ve full acceptance çalışacak.
- W1 ancak bu zincir tamamlanınca açılacak.

### 3.1 Neden bu sıra?

- PR #60 canlı Counterparty HTTP sınırındaki P1'i küçük ve bağımsız kapatır.
- PR #31 sonraki production PR'ların geçeceği ortak CI güvenlik tabanıdır.
- Issue #53 yeni CI tabanında Counterparty audit izolasyonunu kapatır.
- PR #36 önceki düzeltmeleri duplicate etmeden en son current-main üzerinde tamamlanır.
- W1, eksik Cari Core veya açık tenant P1 üzerinde başlamaz.

---

## 4. ChatGPT çalışma planı

### 4.1 Yakın sıra

```text
A. PR #65 P1 düzeltmeleri
B. PR #60 current-main transport
C. PR #31 read-only iş etkisi incelemesi
D. Issue #53 production fix
E. PR #36 finalizasyon
F. CARI-UI-01
G. PRD-01
H. SAL-01
```

### 4.2 ChatGPT branch açmadan önce

Kontrol listesi:

- current main exact SHA;
- aynı işi yapan PR/branch;
- karar sicili ID'leri;
- Master bölümü;
- source-of-truth modülü;
- canonical task IDs;
- D2 karar;
- event delivery mode;
- migration sahibi;
- UI referansı;
- gerçek SQL kabulü;
- Codex read-only sınırı.

### 4.3 ChatGPT'nin yazacağı alanlar

- Counterparty;
- Product/Service;
- Sales/Purchasing;
- Inventory/Allocation;
- Production/Traceability;
- Logistics/Weighbridge;
- Finance/Treasury/Accounting;
- business reports;
- module UI;
- formulas/golden tests;
- migration mapping/UAT.

### 4.4 ChatGPT'nin yazmayacağı alanlar

Codex aktif writer iken:

- Identity core;
- `.github/**`;
- ortak shell;
- generic approval/task engine;
- outbox/inbox framework;
- search infra;
- AI gateway;
- observability/deploy/recovery tooling.

---

## 5. Codex çalışma planı

### 5.1 Yakın sıra

```text
A. PR #65 yeni head read-only re-review
B. PR #60 yeni head read-only review
C. PR #31 current-main sync/full CI
D. ChatGPT Issue #53 PR'ını review
E. ChatGPT PR #36 final head review
F. W0 kapanınca PLT-01 gate kontrolü
G. Gate açılmışsa common shell
```

### 5.2 Codex'in yazacağı alanlar

- Identity/auth;
- common shell/design system;
- generic approval/task engine;
- Document Vault technical core/PDF runtime;
- outbox/inbox/workers;
- search;
- AI gateway;
- observability;
- CI/CD/migration artifacts;
- deployment;
- backup/restore;
- generic tooling.

### 5.3 Codex'in yazmayacağı alanlar

ChatGPT aktif writer iken:

- Counterparty business code;
- Product/Sales/Purchasing business semantics;
- Inventory/Production/Finance formulas;
- module-specific UI business behavior;
- ChatGPT branches.

---

## 6. PR #65 re-review talimatı

Codex yeni exact head için şunları doğrular:

### P1-1

Bütün repository truth dosyalarında:

```text
#64 → #60 → #31 → #53 → #36 → W1
```

### P1-2

Machine plan:

- `canonical_task_ids`;
- `blocked_by`;
- `dor_status`;
- `execution_status`;
- `overlay_rule`;
- `event_delivery_mode`

alanlarını taşır.

Dependency veya blocker açık iş `execution_status=ready` olamaz.

ERP-000F öncesi yalnız senkron transaction/application-contract yolu kullanılabilir. İlk gerçek async event/projection/worker öncesi F minimumu zorunludur.

### P1-3

Karar sırası:

```text
latest product-owner decision
→ docs/decisions/KARAR_SICILI.md
→ Master v3
→ old prototype/StokERP donor/reference
```

StokERP kabul edilmiş contract ve golden test olmadan production davranışı yetkilendiremez.

### Review sonucu

```text
HANDOFF-ACCEPTED
```

veya somut source/diff/CI kanıtıyla:

```text
REQUEST_CHANGES
```

P2/P3 ayrı issue olur.

---

## 7. Dikey dilim sözleşmesi

Her production PR'da:

```text
SLICE-CONTRACT
User outcome:
Canonical decisions:
Canonical task IDs:
Source-of-truth module:
Data model:
State transitions:
Money/quantity/unit/formula:
Permission/CompanyContext:
Approval/SoD:
Audit/reversal:
Idempotency/concurrency:
Event delivery mode:
UI states:
SQL acceptance:
E2E:
Out of scope:
```

---

## 8. Handoff

```text
HANDOFF-V1

OWNER:
WORKSTREAM:
SLICE:
BASE:
HEAD:
USER OUTCOME:
CANONICAL TASK IDS:
BLOCKED BY:
DOR STATUS:
EXECUTION STATUS:
EVENT DELIVERY MODE:
WRITE SCOPE:
READ-ONLY BOUNDARIES:
BUSINESS INVARIANTS:
TEST EVIDENCE:
OPEN P0/P1:
OPEN D2:
NEXT SAFE ACTION:
DO NOT:
```

Karşı review:

```text
HANDOFF-ACCEPTED
Reviewed-Head:
Reviewer:
Result:
Evidence:
P0:
P1:
P2 follow-ups:
```

---

## 9. Test kapıları

### Her business write

- authorized success;
- unauthorized 403;
- cross-company non-disclosure;
- same-key replay;
- payload mismatch;
- different-key race;
- stale version;
- audit failure rollback;
- idempotency failure rollback;
- real SQL.

### UI

- loading;
- empty;
- error;
- forbidden;
- not-found;
- conflict;
- responsive;
- keyboard;
- visual regression.

### Formül

- version;
- input snapshot;
- unit/rounding;
- golden data;
- tolerance boundary;
- invalid inputs;
- no stale preview.

### Finans/stok

- double-counting yok;
- negative/over-allocation yok;
- reversal;
- period lock;
- SoD;
- migration reconciliation.

---

## 10. Event delivery karar ağacı

```text
Etki aynı transaction içinde mi?
    Evet → senkron application contract olabilir.
    Hayır → durable delivery gerekiyor mu?
        Hayır → açıkça transient/yeniden hesaplanabilir.
        Evet → ERP-000F minimumu kabul edildi mi?
            Hayır → execution blocked.
            Evet → outbox/inbox + retry + dedup + poison/redrive.
```

Notification, OCR, AI worker, external delivery ve async search projection bu ağaca tabidir.

---

## 11. Platform task mapping

| Workstream | Canonical task |
|---|---|
| Outbox/Inbox | ERP-000F |
| Document Vault | ERP-000G |
| Turkish PDF | ERP-000H |
| Search | ERP-000I |
| AI Gateway | ERP-000J |
| Common Shell | ERP-000K |
| Observability | ERP-000L |
| SQL Server PoC | ERP-000M |
| Backup/Restore | ERP-000N |
| CI/CD/Delivery | ERP-000O |

Machine overlay açık blocker'ları ayrıca taşır.

---

## 12. Dalga bazlı paralellik

### W1

- Codex: PLT-01, yalnız execution gate açıldıysa.
- ChatGPT: CARI-UI-01, ardından PRD-01.
- Codex: DOC-01 yalnız G/H blocker'ları kapandıysa.
- Karşılıklı review.

### W2

- ChatGPT: SAL-01 ve PUR-01 aynı dosya yüzeyine girmeden kontrollü sıra.
- Codex: APP-01 generic approval/task.
- Outbox minimumu ayrı; ilk dilimler senkron.
- OPR-01 PUR-01 sonrası.

### W3

- ChatGPT: INV-01 → ALC-01.
- Codex: barcode/offline/generic import tooling gerektiği ölçüde.
- Gerçek SQL allocation race zorunlu.

### W4

- ChatGPT: üretim motorları ve formula.
- Codex: generic calculator/scheduler/realtime/print tooling.
- Bir motor bitmeden sonraki aynı aggregate'a geçilmez.

### W5

- ChatGPT: loading/weighbridge/shipment/doc readiness.
- Codex: device/integration generic adapters.
- Dış etki async ise ERP-000F.

### W6

- ChatGPT: finance/treasury/accounting.
- Codex: generic bank import/payment file tooling.
- Para etkisi D2 ve insan merge kapısı.

### W7

- Codex: F/I/J/L/M/N/O.
- ChatGPT: report semantics, AI business tools, templates.
- Her platform görevi kendi canonical blocker'larını kapatır.

### W8

- ChatGPT: mapping/reconciliation/UAT.
- Codex: migration runner/release/backup/rollback.
- En az iki rehearsal.

---

## 13. Bloke olunca çalışma

Ajan:

1. blocker'ı GitHub'a exact kaynakla yazar;
2. sahiplik dışına çıkmaz;
3. aynı dosyada başka iş açmaz;
4. kendi alanındaki bağımsız sıradaki göreve geçer;
5. D2 gerekiyorsa tek karar paketi oluşturur;
6. “bekliyorum” diyerek çalışma döngüsünü durdurmaz.

---

## 14. Merge

İnsan onayı zorunlu:

- Identity/permission;
- shared CI;
- destructive schema;
- stock/money/accounting policy;
- production activation;
- backup/recovery;
- cutover.

Merge öncesi:

- exact head;
- expected-head protection;
- full CI;
- real SQL;
- karşı review;
- P0/P1=0.

Merge sonrası:

- current main SHA;
- new-main CI;
- dependent branch stale işareti;
- Issue #66 güncellemesi.

---

## 15. Otonom yürütme talimatı

```text
Açık insan kararı gerektirmeyen bütün güvenli teknik adımları art arda tamamla.
Yalnız D2 ürün kararları, kritik merge kapıları ve production activation için dur.
Bir iş bloke olursa sahiplik sınırına uyan sonraki bağımsız işi başlat.
Her bulguyu GitHub'a exact SHA ve kanıtla yaz.
Sohbet özetini source-of-truth sayma.
Auto-merge yapma.
```

---

## 16. İlk çıkış hedefi

W0 tamamlandığında:

- Cari Core 1C güvenli;
- stable CI;
- açık Counterparty P0/P1 yok;
- W1 task gate'leri net;
- ortak shell/Cari UI/Product çalışmalarına çakışmasız başlanabilir.

Bu playbook planın overlay konumunu, kanonik task graph'ı ve ürün karar sırasını değiştirmez.
