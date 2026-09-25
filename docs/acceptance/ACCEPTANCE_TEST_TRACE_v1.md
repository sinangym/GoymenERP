# Acceptance → Test Trace v1 (R03)

> **Traceability aid, not an acceptance verdict.** This file maps each canonical acceptance case to
> the test runners that exist on `main`. It does not run tests, does not read CI results and does not
> change any canonical record: `ACCEPTANCE_MATRIX.csv` (Modul/Durum/Kanıt), `ACCEPTANCE_70.md`,
> `DELIVERY_BACKLOG_v1.json`, `task_manifest.json`, decisions and task statuses are untouched.
> **NOT_RUN or unknown is never PASS.** A test that exists but was skipped (for example a
> `[SqlServerFact]` without a SQL Server connection) is NOT_RUN, not PASS.

| Field | Value |
|---|---|
| Contract / row | GOYMEN-CLAUDE-FULL-DELIVERY-20260926-V2, R03 (Issue #66 comment 5783807859) |
| Author lane | Cari/Platform (Claude) |
| `main` SHA traced | `ddbad2ec9a3d915536d37e10f088bcf6f3af3d9f` |
| Date | 2026-09-26 |
| Scope | All 70 rows of `docs/acceptance/ACCEPTANCE_MATRIX.csv` (same list as `ACCEPTANCE_70.md`) |

## 1. Method

1. **Rows.** Took the 70 `AT-xxx` rows and their `Aciklama` text verbatim from `ACCEPTANCE_MATRIX.csv`.
   68 rows have `Modul=TBD`. AT-003 has `Platform.Audit` and AT-005 has `Platform.Idempotency`.
2. **Owner lane.** For each AT, found the slice that lists it in `DELIVERY_BACKLOG_v1.json`
   (`waves[].slices[].acceptance` or `global_acceptance`). Then mapped the slice to one of the five lanes,
   using the owner candidates in the R01–R72 table (Issue #66 comment 5783807859) where a slice is split:
   - W0-\*, W0-SHELL, W1-CARI, W1-YETKI, W2-BELGE, W5-AIGATEWAY → **Cari/Platform**
     (R12 role/permission, R57 document vault, R58 PDF/share, R59 search, R63/R64 AI/OCR and R67 restore are Cari rows).
   - W1-URUN, W3-STOK, W3-TAHSIS, W3-ETIKET, W3-KALITE → **Product/Inventory** (R05–R08, R14–R23).
   - W2-SATIS, W2-SATINALMA, W2-IRSALIYE, W3-LOJISTIK → **Sales/Purchasing/Logistics** (R25–R31, R41, R43, R62).
   - W4-MUHASEBE → **Finance/Accounting**.
   - W3-URETIM → **Organization/Manufacturing** (R32–R40).

   Every lane where the matrix says `TBD` is marked **PROPOSED**. The mapping only proposes a lane. It does not assign an owner.
3. **Evidence search on the traced SHA.** Every test name was listed from four sources:
   - (a) the SQL relational inventories `tools/relational_inventory/*.txt`;
   - (b) `[Fact]` / `[Theory]` / `[SqlServerFact]` methods under `tests/GoymenERP.*`;
   - (c) vitest titles under `src/frontend/goymen-erp-web/src/**/*.test.ts(x)`;
   - (d) Playwright titles under `tests/e2e/**`.

   Each AT was then matched in two ways: by explicit `AT-xxx` references in code and acceptance docs
   (`CARI_C01_C03_ACCEPTANCE_v1.md` §5 and `CARI_C02_LINK_ACCEPTANCE_v1.md` §E), and by reading the test
   name, plus its assertions where the name was not conclusive. A test is listed as proof only if it
   directly exercises a clause of the AT. A related test that does not prove the AT is listed under
   **Adjacent (does not prove this AT)**. No test was invented. Where nothing qualifies, the entry says `NONE`.
4. **Runner mapping.** Each reference was mapped to the workflow that executes it, from `.github/workflows/*.yml` (read only).
5. **Status.** Each row was classified by the rule in §2, against the matrix's own `TestKatmani` column.

## 2. Evidence status definitions

| Status | Meaning |
|---|---|
| `AUTOMATED_ON_MAIN` | Every clause of the AT is exercised by at least one automated test on `main` that a runner in §3 executes. The layers named in the matrix `TestKatmani` column are all present (`E2E+Contract` needs a browser suite plus a backend/SQL contract test; AT-003 needs `Unit+HTTP+SQL Server`). Label-gated browser suites count as a runner, but each one is tagged. This is **not** a PASS and does not replace UAT. |
| `PARTIAL` | Some clauses, some modules or some required layers have automated tests. Typical cases: domain/SQL proof without a browser, a browser suite that is local only, or one module out of several. |
| `NONE` | No automated test on `main` directly exercises the AT. Adjacent tests may still be listed. |

## 3. Runners and trigger tags

| Tag / runner | What runs it | Trigger |
|---|---|---|
| **CI-backend** | `ci.yml` job `backend`: `dotnet test GoymenERP.slnx` (all .NET unit and non-SQL HTTP tests) | pull_request + push `main` |
| **CI-relational** | `ci.yml` job `relational`: `tools/verify_rcsi_relational_acceptance.py --broad` against `tools/relational_inventory/*.txt` on a SQL Server 2022 service | pull_request + push `main` |
| **CI-frontend** | `ci.yml` job `frontend`: `npm test` (`vitest run`) in `src/frontend/goymen-erp-web` | pull_request + push `main` |
| `[PR+main]` | `c01-real-browser-e2e.yml`: `npm run c01:ci` | pull_request + push `main` + dispatch |
| `[PR+main*]` | Same workflow: `sab:ci` (runs only when `validate_sab_e2e_contract.py preflight` enables it) and `activation:ci` (runs only when its files exist) | as above, conditional |
| `[label]` | `browser-acceptance.yml`: `npm run browser:<suite>:ci` for `smoke, c03, org03, u02, s03, mfg, fin01, s04, acc01, l04, pur01, r14, c02` | **only** workflow_dispatch or a `browser:<suite>` PR label; not on push to `main` |
| `[local]` | Playwright suites with their own config and no npm script/workflow: `tests/e2e/cari-acceptance-009`, `tests/e2e/cari-acceptance-010` (local `tools/ci/cari-c02-link.ps1 -Mode Verify`; CI runs `BackendVerify` only), `tests/e2e/organization-locations`, `tests/e2e/product-u01-u02` | manual/local only |

`cari-c02-link-acceptance.yml` runs the Cari C006 backend envelope (`BackendVerify`) and a frontend evidence scanner. It runs no browser acceptance.

## 4. Reference conventions

- **SQL**: `Class.Method` = `GoymenERP.Api.IntegrationTests.Class.Method`, verbatim from the inventory file named in parentheses (`tools/relational_inventory/<file>.txt`). Runner: CI-relational.
- **.NET unit/HTTP**: `Project/Class.Method` with the `GoymenERP.` prefix dropped (e.g. `Inventory.UnitTests/StockLedgerTests.X` = `tests/GoymenERP.Inventory.UnitTests/StockLedgerTests.cs`, method `X`). Runner: CI-backend.
- **Frontend unit**: path under `src/frontend/goymen-erp-web/src/` › test title. Runner: CI-frontend.
- **Browser**: npm script › spec path under `tests/e2e/` › test id or title, followed by a trigger tag from §3.

## 5. Counts

| Lane | Rows | AUTOMATED_ON_MAIN | PARTIAL | NONE |
|---|---:|---:|---:|---:|
| Cari/Platform | 23 | 1 | 9 | 13 |
| Product/Inventory | 22 | 2 | 11 | 9 |
| Sales/Purchasing/Logistics | 12 | 0 | 5 | 7 |
| Finance/Accounting | 1 | 0 | 1 | 0 |
| Organization/Manufacturing | 12 | 1 | 10 | 1 |
| **Total** | **70** | **4** | **36** | **30** |

Lane is PROPOSED for 68 rows (matrix `TBD`). It is derived from the matrix `Modul` for AT-003 and AT-005.

## 6. Summary table

| AT | Aciklama (matrix) | Lane | Backlog slice | Status |
|---|---|---|---|---|
| AT-001 | Kullanıcı yalnız yetkili şirket, şube ve depo kayıtlarını görür. | Cari/Platform · PROPOSED | W0-SHELL | PARTIAL |
| AT-002 | Global arama yetkisiz kaydı sonuç, öneri, toplam veya sayı olarak sızdırmaz. | Cari/Platform · PROPOSED | W0-SEARCH | NONE |
| AT-003 | Kritik değişiklik önce/sonra, neden, kullanıcı, rol ve korelasyonla audit üretir. | Cari/Platform (Modul=Platform.Audit) | global | AUTOMATED_ON_MAIN |
| AT-004 | Oluşturan kişi yüksek riskli kendi işlemini görev ayrılığına aykırı onaylayamaz. | Cari/Platform · PROPOSED | W1-YETKI | PARTIAL |
| AT-005 | Eşzamanlı değişiklikte ikinci kullanıcıya çatışma ve fark gösterilir. | Cari/Platform (Modul=Platform.Idempotency) | global | PARTIAL |
| AT-006 | Başarısız kaydetme kullanıcının form verisini kaybettirmez. | Cari/Platform · PROPOSED | W0-SHELL | PARTIAL |
| AT-007 | İptal ve ters işlem geçmişi silmez. | Sales/Purchasing/Logistics · PROPOSED | W2-SATIS | PARTIAL |
| AT-008 | Şirket kapsamı değişikliği açık formu sessizce başka şirkete taşımaz. | Cari/Platform · PROPOSED | W0-SHELL | PARTIAL |
| AT-009 | Aynı VKN/kimlik için ikinci cari kontrollü mükerrer uyarısı üretir. | Cari/Platform · PROPOSED | W1-CARI | NONE |
| AT-010 | Tek cari kimliği müşteri ve tedarikçi rollerini ayrı hesaplarla taşır. | Cari/Platform · PROPOSED | W1-CARI | PARTIAL |
| AT-011 | TRY, USD ve EUR bakiyeleri açıklamasız tek sayıya netleştirilmez. | Cari/Platform · PROPOSED | W1-CARI | PARTIAL |
| AT-012 | Cari birleştirme ilişkili belge ve audit bağlarını kaybetmez. | Cari/Platform · PROPOSED | W1-CARI | NONE |
| AT-013 | Pasif cari yeni işleme seçilemez; geçmiş kayıtta görünür kalır. | Cari/Platform · PROPOSED | W1-CARI | PARTIAL |
| AT-014 | Eksik zorunlu vergi/banka belgesi doğru akışı doğru noktada durdurur. | Cari/Platform · PROPOSED | W1-CARI | NONE |
| AT-015 | Ürün sınıfı ve ticari rol bağımsızdır; çelişki kontrol edilir. | Product/Inventory · PROPOSED | W1-URUN | PARTIAL |
| AT-016 | Birim dönüşümü yuvarlama/tolerans golden örneğini geçer. | Product/Inventory · PROPOSED | W1-URUN | NONE |
| AT-017 | Paket standardının yeni sürümü geçmiş siparişi değiştirmez. | Product/Inventory · PROPOSED | W1-URUN | NONE |
| AT-018 | Fiyat sürümü para, birim, KDV, kanal ve geçerlilik bağlamını korur. | Product/Inventory · PROPOSED | W1-URUN | NONE |
| AT-019 | Excel içe aktarma eşleme/önizleme/onay olmadan kayıt yazmaz. | Product/Inventory · PROPOSED | W1-URUN | NONE |
| AT-020 | Aynı Excel dosyasının tekrarı mükerrer kalıcı kayıt üretmez. | Product/Inventory · PROPOSED | W1-URUN | NONE |
| AT-021 | Satış siparişi fiyat alt sınırını aşarsa doğru onaya gider. | Sales/Purchasing/Logistics · PROPOSED | W2-SATIS | NONE |
| AT-022 | Risk limiti aşımı siparişi sessizce serbest bırakmaz. | Sales/Purchasing/Logistics · PROPOSED | W2-SATIS | NONE |
| AT-023 | Onaylı siparişte kritik satır değişikliği eski onayı geçersizleştirir. | Sales/Purchasing/Logistics · PROPOSED | W2-SATIS | PARTIAL |
| AT-024 | Kısmi teslimat sipariş kalanını doğru tutar. | Sales/Purchasing/Logistics · PROPOSED | W2-SATIS | PARTIAL |
| AT-025 | Satış ve satın alma belgeleri operasyon listelerinde karışmaz. | Sales/Purchasing/Logistics · PROPOSED | W2-SATIS | NONE |
| AT-026 | Satın alma siparişi fazla/eksik teslimat toleransını uygular. | Sales/Purchasing/Logistics · PROPOSED | W2-SATINALMA | PARTIAL |
| AT-027 | Tedarikçi fiyat/miktar farkı kullanıcı onayı olmadan faturaya geçmez. | Sales/Purchasing/Logistics · PROPOSED | W2-SATINALMA | NONE |
| AT-028 | İade kaynak sipariş, teslimat ve fatura bağını korur. | Sales/Purchasing/Logistics · PROPOSED | W2-SATINALMA | NONE |
| AT-029 | Fiziksel mevcut ile mülkiyet sahibi ayrı doğru gösterilir. | Product/Inventory · PROPOSED | W3-STOK | PARTIAL |
| AT-030 | Müşteri malzemesi şirket serbest stoku gibi satılamaz. | Product/Inventory · PROPOSED | W3-STOK | PARTIAL |
| AT-031 | Fasoncudaki şirket malzemesi doğru emanet/lokasyon durumunda görünür. | Product/Inventory · PROPOSED | W3-STOK | PARTIAL |
| AT-032 | Aktif tahsis serbest miktarı düşürür ama stok hareketi oluşturmaz. | Product/Inventory · PROPOSED | W3-TAHSIS | AUTOMATED_ON_MAIN |
| AT-033 | Aynı etiket izin verilenden fazla aktif tahsis edilemez. | Product/Inventory · PROPOSED | W3-TAHSIS | PARTIAL |
| AT-034 | Tahsis serbest bırakılınca kalan doğru geri döner. | Product/Inventory · PROPOSED | W3-TAHSIS | AUTOMATED_ON_MAIN |
| AT-035 | Kalite bloğu serbest miktardan doğru düşer. | Product/Inventory · PROPOSED | W3-TAHSIS | PARTIAL |
| AT-036 | Yüklemeye tutulan miktar başka siparişe kaynak olamaz. | Product/Inventory · PROPOSED | W3-TAHSIS | NONE |
| AT-037 | Sayım farkı neden ve onay olmadan kesinleşmez. | Product/Inventory · PROPOSED | W3-STOK | NONE |
| AT-038 | Etiket bölme/birleştirme soy ağacını korur. | Product/Inventory · PROPOSED | W3-ETIKET | PARTIAL |
| AT-039 | Lot/rulo/ısı/MTC bağı girişten çıkışa izlenir. | Product/Inventory · PROPOSED | W3-ETIKET | PARTIAL |
| AT-040 | Serbest fiziksel miktarın her bileşeni kaynak kayda açılır. | Product/Inventory · PROPOSED | W3-STOK | PARTIAL |
| AT-041 | Kaynağı hazır olmayan plan hazır durumuna geçemez. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-042 | Fiilî üretim tüketimi tahsis ve fiziksel mevcudu aşamaz. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-043 | Boy kesme çıktıları + kalan + fire girdiyi tolerans içinde dengeler. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-044 | Dilme kombinasyonu genişlik ve kenar firesini dengeler. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-045 | Boru/profil adet–boy–kg dönüşümü golden örneğini geçer. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-046 | Boyama girdisi iyi/ikinci kalite/yeniden işleme/kayıp ile dengelenir. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-047 | Soğuk hadde giriş/çıkış/proses kaybı ortak dengeyi sağlar. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-048 | Tolerans dışı kütle farkı onaysız kapanamaz. | Organization/Manufacturing · PROPOSED | W3-URETIM | AUTOMATED_ON_MAIN |
| AT-049 | Yeni reçete/formül/tolerans sürümü geçmiş gerçekleşmeyi değiştirmez. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-050 | Çıktı etiketi bütün kaynak girdilere geri izlenir. | Product/Inventory · PROPOSED | W3-ETIKET | PARTIAL |
| AT-051 | Kalite karantinasındaki çıktı sevkiyata açılamaz. | Product/Inventory · PROPOSED | W3-KALITE | PARTIAL |
| AT-052 | Düzeltme önceki gerçekleşmeyi silmez; ters/düzeltme zinciri kurar. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-053 | Kantar neti iki tartım ve brüt/dara yönüyle tutarlıdır. | Sales/Purchasing/Logistics · PROPOSED | W3-LOJISTIK | NONE |
| AT-054 | Fiilî yüklenen etiketler tahsis ve sevkiyat planıyla uzlaştırılır. | Sales/Purchasing/Logistics · PROPOSED | W3-LOJISTIK | PARTIAL |
| AT-055 | Aynı sevkiyat gerçekleşmesi iki kez irsaliye/faturaya bağlanamaz. | Sales/Purchasing/Logistics · PROPOSED | W2-IRSALIYE, W3-LOJISTIK | NONE |
| AT-056 | OCR/AI çıkarımı ham belge ve kullanıcı düzeltmesiyle izlenir. | Cari/Platform · PROPOSED | W2-BELGE | NONE |
| AT-057 | Aynı belge hash'i ve benzer anahtarlar mükerrer uyarısı üretir. | Cari/Platform · PROPOSED | W0-DOCVAULT, W2-BELGE | NONE |
| AT-058 | Ödeme/tahsilat açık kalem ve banka hareketine çift yönlü bağlanır. | Finance/Accounting · PROPOSED | W4-MUHASEBE | PARTIAL |
| AT-059 | AI kalıcı değişikliği fark önizlemesi ve yetkili onay olmadan uygulamaz. | Cari/Platform · PROPOSED | W5-AIGATEWAY | NONE |
| AT-060 | AI yetkisiz şirket verisini cevap, özet, araç çağrısı veya kaynakta sızdırmaz. | Cari/Platform · PROPOSED | W5-AIGATEWAY | NONE |
| AT-061 | Üretim planı girdisi değişince `Hesapla` düğmesi olmadan doğru önizleme güncellenir; kayıt oluşmaz. | Organization/Manufacturing · PROPOSED | W3-URETIM | NONE |
| AT-062 | Eksik/geçersiz üretim girdisinde eski sonuç güncel gibi kalmaz; neden ve eksik alan görünür. | Organization/Manufacturing · PROPOSED | W3-URETIM | PARTIAL |
| AT-063 | Stok satırından Üretim Planla, Tahsis Revize ve Yükleme Talimatı işlemleri doğru satır bağlamıyla açılır. | Product/Inventory · PROPOSED | W3-STOK | NONE |
| AT-064 | Alış siparişi kalanı `Kaynak/Durum = Alış Siparişi` görünür fakat mal kabulden önce fiziksel/serbest stoğu artırmaz. | Product/Inventory · PROPOSED | W3-STOK | NONE |
| AT-065 | Tek belge bir kez saklanır; stok/cari/muhasebe/finans ekranları aynı olay ve dosya referansını gösterir. | Cari/Platform · PROPOSED | W2-BELGE | NONE |
| AT-066 | PDF adı Türkçe karakterleri korur, yasak dosya karakterlerini güvenli dönüştürür ve gömülü fontla doğru render olur. | Cari/Platform · PROPOSED | W0-PDF | PARTIAL |
| AT-067 | WhatsApp Business olmadan Paylaş işlemi desteklenen cihazda sistem paylaşım penceresini açar; destek yoksa indir + metin kopyala çalışır. | Cari/Platform · PROPOSED | W2-BELGE | NONE |
| AT-068 | AI kişi bazlı yetki taslağı ve risk/fark üretir fakat yetkili yönetici onayı olmadan izin değişmez. | Cari/Platform · PROPOSED | W5-AIGATEWAY | NONE |
| AT-069 | Yetkili global arama temsilî üç yıllık hacmin üç katında belirlenen p95 hedefini geçer; derin sayfalama veri atlamaz/tekrarlamaz. | Cari/Platform · PROPOSED | W0-SEARCH | NONE |
| AT-070 | Yedekten nokta-zaman geri yükleme ve nesne dosyası bütünlüğü gerçek provada mutabık sonuç verir. | Cari/Platform · PROPOSED | W0-RESTORE | NONE |

## 7. Per-AT trace

Every row lists four kinds of proof: Browser, SQL, .NET unit/HTTP and Frontend unit. `NONE` in a field means no qualifying test was found for that layer.

### 7.1 Cari/Platform (23)

#### AT-001 — Kullanıcı yalnız yetkili şirket, şube ve depo kayıtlarını görür.
- **Lane:** Cari/Platform · PROPOSED (W0-SHELL / ERP-000K)
- **Browser:**
  - `c01:ci` › `specs/c01.spec.ts` › B-C01-001, B-C01-002, B-C01-003 `[PR+main]`
  - `activation:ci` › `activation/historyRevoke.spec.ts` › C01A-HISTORY-REVOKE-forward `[PR+main*]`
  - `browser:c03:ci` › `cari-c03/specs/c03-company-relationships.spec.ts` › C03 `[label]`
  - `browser:c02:ci` › `cari-c02/specs/c02-create-and-reopen.spec.ts` › C02-03 `[label]`
  - `browser:org03:ci` › `organization-org03/specs/org03-work-centre-list.spec.ts` › ORG03-01…04 `[label]`
  - `browser:acc01:ci` › `accounting-acc01/specs/acc01-periods-and-reads.spec.ts` › ACC01-08 `[label]`
  - `organization-locations/specs/o-02-scope-and-permissions.spec.ts` (site-scoped viewer, other company) `[local]`
- **SQL:**
  - `CompanyDirectorySqlServerRelationalAcceptanceTests.Directory_EnforcesAssignmentIsolationActiveFilteringAndReadOnlyBehavior` (platform)
  - `ShellCapabilitySqlServerRelationalAcceptanceTests.ExactCompanySnapshot_TracksPersistedAuthorizationAndHasNoWriteSideEffects` (platform)
  - `OrganizationLocationLifecycleSqlServerRelationalAcceptanceTests.ScopedPagingAndRejectedCommands_PreserveLocationAndExposeNoForeignState` (organization)
  - `CounterpartyReadPageSqlServerRelationalAcceptanceTests.C01ReadPage_EnforcesAuthorizationFiltersTurkishSearchKeysetAndRedaction` (counterparty)
  - `InventorySqlServerRelationalAcceptanceTests.Each_company_sees_only_its_own_labels_and_a_code_is_unique_per_company_only` (inventory). Each other module inventory has comparable company-isolation rows.
- **.NET:**
  - `Api.IntegrationTests/CompanyIsolationTests`:
    - `.SiteScopedViewer_SeesOnlyAuthorizedHierarchy`
    - `.CompanySelector_ReturnsOnlyAuthorizedCompanies`
    - `.DirectUrlWithOtherCompanyRecord_ReturnsGenericNotFound`
    - `.QueryParameterTampering_DoesNotChangeCompanyScopeOrCount`
  - `Api.IntegrationTests/CompanyDirectoryAcceptanceTests` (class)
  - `ArchitectureTests/PlatformTenantQueryFilterBypassGuardTests` (class)
- **Frontend:** `cari/CariListPage.test.tsx` › "a company switch never leaves company A rows on screen under company B"
- **Status:** PARTIAL
- **Gap:**
  - Company-level isolation is proven at every layer.
  - Site (şube) scope is proven at HTTP level only; its browser proof is `[local]`.
  - Warehouse/location (depo) scoped visibility of stock records has no test. Inventory reads require company-wide grants (`Inventory.UnitTests/InventoryStockPositionTests.Both_company_wide_read_permissions_are_required_before_SQL`).
- **Human-only:** UAT of the real role/company/site assignment matrix (R70) and owner sign-off.

#### AT-002 — Global arama yetkisiz kaydı sonuç, öneri, toplam veya sayı olarak sızdırmaz.
- **Lane:** Cari/Platform · PROPOSED (W0-SEARCH / ERP-000I, manifest `ready`)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** module list/search reads keep other companies out of rows and counts:
  - `CompanyIsolationTests.QueryParameterTampering_DoesNotChangeCompanyScopeOrCount`
  - `InventorySqlServerRelationalAcceptanceTests.Stock_position_search_is_bounded_and_never_leaks_another_companys_rows` (inventory)
- **Status:** NONE
- **Gap:** No global search endpoint or UI exists on `main`. Suggestions, totals and counts across modules are untested.
- **Human-only:** leak review with real multi-company data.

#### AT-003 — Kritik değişiklik önce/sonra, neden, kullanıcı, rol ve korelasyonla audit üretir.
- **Lane:** Cari/Platform (matrix Modul=Platform.Audit; backlog `global_acceptance`)
- **Browser:** not required by the matrix layer (`Unit+HTTP+SQL Server`)
- **SQL:**
  - `SqlServerRelationalAcceptanceTests.MigrationSeedWriteGuardCompositeForeignKeyAndHttpIsolation_AreEnforced` (counterparty; audit append-only, atomicity, reject path)
  - `OrganizationAuditEventTenantGuardSqlServerRelationalAcceptanceTests.AuditSidecar_IsTenantFilteredAndAllWritePathsFailClosed` (organization)
  - `CounterpartyAuditEventTenantGuardSqlServerRelationalAcceptanceTests.AuditSidecar_IsTenantFilteredAndAllWritePathsFailClosed` (counterparty)
- **.NET:**
  - `Api.IntegrationTests/AuditAcceptanceTests.CriticalCommand_ProducesCompleteCompanyScopedAuditEvent` (asserts version before/after, state before/after, reason, user, role codes, correlation and causation)
  - `Api.IntegrationTests/AuditAcceptanceTests.MissingReasonAndInvalidCorrelation_AreRejectedBeforeMutation`
  - `Api.IntegrationTests/AuditAcceptanceTests.AuditRead_RequiresPermissionAndKeepsSelectedCompanyBoundary`
  - `Audit.UnitTests/AuditFoundationTests` (class)
  - `Audit.UnitTests/AuditSaveChangesInterceptorRecoveryTests.TrackedModifiedAndDeletedAuditEvents_AreRejected`
  - `ArchitectureTests/PlatformTenantQueryFilterBypassGuardAuditTests` (class)
- **Frontend:** NONE (no audit screen)
- **Status:** AUTOMATED_ON_MAIN (consistent with the matrix's own `Durum=Otomatik` and its `Kanıt` column)
- **Gap:**
  - The full-field assertion covers one command (Organization location active state).
  - Other modules assert audit cardinality in their SQL rows (for example `SalesDraftSqlServerRelationalAcceptanceTests.A_draft_with_lines_and_slices_is_stored_replayed_and_audited_in_one_save`). There is no single cross-module "every critical command audits" inventory.
- **Human-only:** business confirmation of which commands are critical; audit review in UAT.

#### AT-004 — Oluşturan kişi yüksek riskli kendi işlemini görev ayrılığına aykırı onaylayamaz.
- **Lane:** Cari/Platform · PROPOSED (W1-YETKI; R12)
- **Browser:**
  - `browser:fin01:ci` › `finance-fin01/specs/fin01-bank-account-and-draft.spec.ts` › FIN01-01, FIN01-02B, FIN01-03B `[label]`
  - `browser:acc01:ci` › ACC01-01 `[label]`
  - `browser:s04:ci` › `sales-s04/specs/s04-d2-persona-withdrawal.spec.ts` › S04-D2-01 `[label]`
- **SQL:**
  - `FinanceBankAccountSqlServerRelationalAcceptanceTests.Two_racing_approvals_store_one_and_the_database_refuses_a_self_approval` (finance)
  - `FinanceBankAccountSqlServerRelationalAcceptanceTests.A_pending_IBAN_never_replaces_the_approved_value_and_only_another_actor_can_approve_it` (finance)
  - `FinanceRecordSqlServerRelationalAcceptanceTests.Reversal_requires_another_checker_and_keeps_the_original_with_two_way_history` (finance)
  - `AccountingApiSqlServerRelationalAcceptanceTests.A_period_is_proposed_approved_by_another_user_and_closed_and_every_answer_of_a_key_holds` (accounting)
  - `SalesDraftApprovalSqlServerRelationalAcceptanceTests.The_submitter_cannot_decide_their_own_order_a_different_approver_can_and_the_creator_withdraws_without_money_or_stock_on_real_sql` (sales)
- **.NET:**
  - `Accounting.UnitTests/PeriodApprovalTests.The_preparer_cannot_approve_their_own_period`
  - `Finance.UnitTests/ReversalProposalPolicyTests.Maker_cannot_approve_their_own_proposal`
  - `Finance.UnitTests/ManualBankPaymentServiceTests.Maker_cannot_approve_and_unknown_bank_result_writes_nothing`
  - `Identity.UnitTests/AccessGrantRequestTests.CriticalGrant_RequesterCannotApproveOwnRequest`
- **Frontend:** NONE found by title
- **Status:** PARTIAL
- **Gap:**
  - Maker-checker is implemented module by module. There is no central approval matrix or SoD policy (W1-YETKI).
  - Tests pin some self-approval as allowed: `browser:s04:ci` S04-D2-02 (a non-SalesSupport creator may approve own records) and `Accounting.UnitTests/NormalJournalDraftV1Tests.Actual_narrow_approve_authority_allows_own_or_another_normal_draft`.
  - Purchasing approval and Product activation have no self-approval test.
  - The matrix does not define which operations are "high risk". This trace does not choose them.
- **Human-only:** business definition of high-risk operations and thresholds; persona UAT.

#### AT-005 — Eşzamanlı değişiklikte ikinci kullanıcıya çatışma ve fark gösterilir.
- **Lane:** Cari/Platform (matrix Modul=Platform.Idempotency; backlog `global_acceptance`)
- **Browser:**
  - `browser:s04:ci` › `sales-s04/specs/s04-approval-journey.spec.ts` › S04-03 `[label]`
  - `browser:acc01:ci` › ACC01-04 `[label]`
  - `browser:l04:ci` › `logistics-l04/specs/l04-loading-journey.spec.ts` › L04-03 `[label]`
  - `browser:mfg:ci` › `manufacturing-mfg/specs/mfg-order-journey.spec.ts` › "a write that another writer overtook is a conflict, re-read, and a new command at the new version" `[label]`
  - `product-u01-u02/specs/u-02-edit-conflicts.spec.ts` › "U-02 stale version: both versions shown, input kept, only a reload offered — never a rebase" `[local]`
  - `organization-locations/specs/o-03-conflict-unknown-replay.spec.ts` `[local]`
- **SQL:**
  - `SqlServerRelationalAcceptanceTests.MigrationSeedWriteGuardCompositeForeignKeyAndHttpIsolation_AreEnforced` (counterparty; idempotency/concurrency/atomicity path)
  - `InventorySqlServerRelationalAcceptanceTests.Two_writers_that_read_the_same_piece_cannot_both_commit` (inventory)
  - `ManufacturingSqlServerRelationalAcceptanceTests.Two_writers_that_read_the_same_order_cannot_both_commit` (manufacturing)
  - `ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.ProductGlobalRuntime_TwoEditsStagedAtVersionOne_ProduceOneLateEfConflict` (product)
  - `CounterpartyWriteSafetySqlServerRelationalAcceptanceTests.ConcurrentCreatesAndDifferentKeyStatusWrites_AreSerialized` (counterparty)
- **.NET:**
  - `Api.IntegrationTests/IdempotencyAcceptanceTests.VersionConflict_IsPersistedAndSafelyReplayed`
  - `Api.IntegrationTests/IdempotencyAcceptanceTests.StaleExpectedVersionWithNewKey_DoesNotCreateSecondBusinessEvent`
  - `Manufacturing.UnitTests/VersionFenceTests.Two_callers_that_read_the_same_order_cannot_both_write`
- **Frontend:**
  - `product/ProductEditPage.test.tsx` › "stops, shows both versions, keeps the input and offers only a reload"
  - `organization/LocationDetailPage.test.tsx` › "shows a version conflict with both numbers"
  - `cari/CariAccountStatus.test.tsx` › "version_conflict: no overwrite, no new key; …"
- **Status:** PARTIAL
- **Gap:**
  - Conflict detection and re-read are proven broadly.
  - "Fark gösterilir" (showing the difference) is proven only as both versions or both numbers, in Product edit and Organization location (component tests and `[local]` browser suites). Sales, Finance, Accounting, Logistics and Manufacturing screens have no field-level difference test.
  - Every CI-wired browser proof is `[label]`.
  - The matrix row still says UI/E2E evidence is pending. This file does not change that.
- **Human-only:** conflict wording/usability check with real users.

#### AT-006 — Başarısız kaydetme kullanıcının form verisini kaybettirmez.
- **Lane:** Cari/Platform · PROPOSED (W0-SHELL)
- **Browser:**
  - `cari-acceptance-009/specs/t-06-payload-metadata-decoder.spec.ts` › T-06a ("…a 400 command_metadata_invalid is shown by code and the form is preserved") `[local]`
  - `organization-locations/specs/o-03-conflict-unknown-replay.spec.ts` › "a stale version is refused 409 and the justification draft survives the recovery" `[local]`
  - `product-u01-u02/specs/u-02-edit-conflicts.spec.ts` `[local]`
- **SQL / .NET:** NONE (client-side state)
- **Frontend:**
  - `cari/CariContactEditPage.test.tsx` › "keeps the draft after a server 400 and lets the user correct it under a new key"
  - `sales/SalesDraftCreatePage.test.tsx` › "a 422 places the owner’s product error on the line it names, and keeps what was typed"
  - `organization/LocationDetailPage.test.tsx` › "keeps the typed reason after a version conflict reload"
  - `manufacturing/OrderDetailPage.test.tsx` › "refreshes both owner reads after a parent-label version conflict and keeps the typed return"
  - `product/ProductEditPage.test.tsx` › "stops, shows both versions, keeps the input and offers only a reload"
- **Status:** PARTIAL
- **Gap:**
  - Browser proof is `[local]` only. No CI-wired suite asserts that form data survives a failed save.
  - Finance, Accounting, Purchasing and Logistics forms have no such component test (by title search).
- **Human-only:** —

#### AT-008 — Şirket kapsamı değişikliği açık formu sessizce başka şirkete taşımaz.
- **Lane:** Cari/Platform · PROPOSED (W0-SHELL)
- **Browser:**
  - `c01:ci` › B-C01-003 `[PR+main]` (read, not a form)
  - `sab:ci` › `sab/sab.spec.ts` › SAB-009 `[PR+main*]` (read)
  - `cari-acceptance-009/specs/t-04-context-switch-discard-fence.spec.ts` › T-04 `[local]`
  - `cari-acceptance-009/specs/t-09-account-status-switch-layering.spec.ts` › T-09 `[local]`
  - `product-u01-u02/specs/u-02-context-change.spec.ts` `[local]`
  - `cari-acceptance-010/specs/t-context.spec.ts` `[local]`
- **SQL:** `CounterpartyCompanyLinkReviewGapSqlServerRelationalAcceptanceTests.CompanyInputsCannotOverrideActiveContext_AndWrongScopeIsForbidden` (counterparty)
- **.NET:**
  - `Api.IntegrationTests/CompanyIsolationTests.SelectedCompanyHeader_CannotEscalateAuthorization`
  - `Api.IntegrationTests/CompanyIsolationTests.CommandWithOtherCompanyRecord_IsRejectedAndDoesNotMutate`
- **Frontend:**
  - `shell/SwitchConfirmDialog.test.tsx`
  - `shell/shellState.test.ts` › "a dirty scope forces confirmation without changing the active context or fence"
  - `shell/contextFence.test.ts`
  - `cari/CariCreatePage.test.tsx` › "a dirty form makes the switch ASK: …"
  - `product/ProductCreatePage.test.tsx` › "makes a company switch ASK while the form is dirty, and keeps the form"
  - `finance/ManualReceiptDraftForm.test.tsx` › "an edited form makes a company switch ask; a clean form does not register"
  - `sales/orderAttempt.test.ts` › "writes nothing when the context changed while the send was in flight"
- **Status:** PARTIAL
- **Gap:**
  - Browser proof for dirty forms is `[local]` only. The CI-wired B-C01-003 and SAB-009 cover reads.
  - No dirty-form company-switch test was found by title for Logistics, Purchasing, Manufacturing or Accounting forms.
- **Human-only:** —

#### AT-009 — Aynı VKN/kimlik için ikinci cari kontrollü mükerrer uyarısı üretir.
- **Lane:** Cari/Platform · PROPOSED (W1-CARI)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `cari/CariCreatePage.test.tsx` asserts that VKN/TCKN are **absent** from the create form. `CARI_C02_LINK_ACCEPTANCE_v1.md` §E marks AT-009 "tamamlanmadı".
- **Status:** NONE
- **Gap:** no VKN/TCKN field, no duplicate engine and no warning UI.
- **Human-only:** duplicate policy decision and real VKN/TCKN sample data.

#### AT-010 — Tek cari kimliği müşteri ve tedarikçi rollerini ayrı hesaplarla taşır.
- **Lane:** Cari/Platform · PROPOSED (W1-CARI)
- **Browser:**
  - `browser:c03:ci` › C03 (one Party, per-company accounts with different roles) `[label]`
  - `browser:c02:ci` › C02-01 `[label]`
- **SQL:**
  - `CounterpartyJourneySqlServerRelationalAcceptanceTests.CreateFindDetailAndLinkSecondCompany_PreserveIdentityVisibilityAndExactlyOnceEffects` (counterparty)
  - `CounterpartyCompanyLinkSqlServerRelationalAcceptanceTests.ExistingParty_CanBeLinkedToSecondAuthorizedCompany_WithoutDuplicatingParty` (counterparty)
- **.NET:**
  - `Counterparty.UnitTests/CompanyPartyAccountTests.One_company_account_can_hold_multiple_commercial_roles`
  - `Counterparty.UnitTests/PartyTests.Supported_party_kinds_share_one_party_model`
- **Frontend:** `cari/CariListPage.test.tsx` › "AT-010: a single account carries combined role flags (one row, both roles)"
- **Status:** PARTIAL
- **Gap:**
  - One identity with combined roles on one company account is proven.
  - "Ayrı hesaplarla" (separate customer receivable vs supplier payable accounts) is not modelled.
  - Whether the current DEC-CAR-001 model satisfies this wording is a business reading, not decided here.
- **Human-only:** business confirmation of the account model.

#### AT-011 — TRY, USD ve EUR bakiyeleri açıklamasız tek sayıya netleştirilmez.
- **Lane:** Cari/Platform · PROPOSED (W1-CARI). Balance data is Finance/Accounting-owned per `CARI_CORE_READINESS_v1.md`.
- **Browser:** NONE (the trial balance screen is staged: `Accounting.UnitTests/AccountingShellCapabilityProviderTests.Only_accepted_screens_are_published_and_the_journal_detail_and_trial_balance_stay_staged`)
- **SQL:** `AccountingTrialBalanceSqlServerRelationalAcceptanceTests.Posted_journals_are_grouped_by_company_date_account_and_currency_without_writes` (accounting)
- **.NET:**
  - `Accounting.UnitTests/TrialBalanceV1Tests.Posted_effects_are_grouped_per_account_and_currency_without_cross_currency_netting`
  - `Counterparty.UnitTests/PartyCoreViewComposerTests.Composer_never_invents_balance_or_financial_fields`
- **Frontend:**
  - `accounting/trialBalance.test.ts` › "rejects inconsistent line equations and currency aggregates"
  - `cari/CariListPage.test.tsx` › "shows no balance / risk / limit column (DEC-CAR-002, AT-011, mockTruthForbidden)"
- **Status:** PARTIAL
- **Gap:**
  - No-netting is proven for the Accounting trial-balance read only.
  - There is no Cari balance or statement surface (C-04/C-05, W2-ACIKKALEM). Leaving balances out of the Cari screens is not completion (`CARI_C01_C03_ACCEPTANCE_v1.md` §5).
- **Human-only:** statement presentation review with real multi-currency data.

#### AT-012 — Cari birleştirme ilişkili belge ve audit bağlarını kaybetmez.
- **Lane:** Cari/Platform · PROPOSED (W1-CARI)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** Party merge (C-11) is not implemented.
- **Human-only:** merge approval by the data owner on real duplicates.

#### AT-013 — Pasif cari yeni işleme seçilemez; geçmiş kayıtta görünür kalır.
- **Lane:** Cari/Platform · PROPOSED (W1-CARI)
- **Browser:**
  - `c01:ci` › B-C01-010 (party state and account state shown separately; a globally inactive party is hidden by the default filter) `[PR+main]`
  - `cari-acceptance-009/specs/l-04-account-status-journey.spec.ts` › L-04 `[local]`
- **SQL:**
  - `CounterpartyCompanyLinkInactivePartySqlServerRelationalAcceptanceTests.GloballyInactiveParty_IsNonDisclosingAndDurablyReplayedWithoutEffects` (counterparty)
  - `CounterpartyOptionalRolesSqlServerRelationalAcceptanceTests.RolelessInactiveAccount_StaleVersionAndPermissionGatesRemainEnforced` (counterparty)
- **.NET:**
  - `Sales.UnitTests/SalesDraftServiceTests.A_reference_the_Cari_owner_does_not_list_or_lists_inactive_is_refused_and_nothing_is_recorded`
  - `Counterparty.UnitTests/CompanyPartyAccountTests.Clearing_trade_classification_preserves_inactive_state_and_advances_version`
- **Frontend:** `cari/CariAccountStatus.test.tsx`; `cari/CariListPage.test.tsx` › "a role-less account does not become passive or unauthorized"
- **Status:** PARTIAL
- **Gap:**
  - Refusing an inactive account is proven for Sales drafts and Cari links only. Purchasing, Logistics and Finance have no such test.
  - "Visible on historical records" is proven only in the Cari list and detail reads. Consumer documents that reference a now-passive cari are not tested.
- **Human-only:** —

#### AT-014 — Eksik zorunlu vergi/banka belgesi doğru akışı doğru noktada durdurur.
- **Lane:** Cari/Platform · PROPOSED (W1-CARI)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `FinanceBankAccountSqlServerRelationalAcceptanceTests.A_proposal_is_not_usable_until_another_user_approves_it_once_and_moves_no_money` (finance). This is a Finance bank-account approval gate, not a Cari tax/bank document gate.
- **Status:** NONE
- **Gap:** no tax/bank document requirement on Cari (C-07) and no stop points in consuming flows.
- **Human-only:** decision on which documents are mandatory, and at which step.

#### AT-056 — OCR/AI çıkarımı ham belge ve kullanıcı düzeltmesiyle izlenir.
- **Lane:** Cari/Platform · PROPOSED (W2-BELGE; R64)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** no document inbox or OCR/AI extraction. ERP-000J is `blocked`.
- **Human-only:** extraction quality review on real documents.

#### AT-057 — Aynı belge hash'i ve benzer anahtarlar mükerrer uyarısı üretir.
- **Lane:** Cari/Platform · PROPOSED (W0-DOCVAULT / ERP-000G `ready`; W2-BELGE)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `Accounting.UnitTests/AccountingPersistenceModelTests.Every_delivery_keeps_a_required_64_hex_content_hash` hashes message content, not documents.
- **Status:** NONE
- **Gap:** the document vault and hash deduplication are not implemented.
- **Human-only:** similarity-key policy decision.

#### AT-059 — AI kalıcı değişikliği fark önizlemesi ve yetkili onay olmadan uygulamaz.
- **Lane:** Cari/Platform · PROPOSED (W5-AIGATEWAY / ERP-000J `blocked`)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** no AI gateway on `main`.
- **Human-only:** approval policy; red-team review.

#### AT-060 — AI yetkisiz şirket verisini cevap, özet, araç çağrısı veya kaynakta sızdırmaz.
- **Lane:** Cari/Platform · PROPOSED (W5-AIGATEWAY / ERP-000J `blocked`)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** no AI gateway or tool registry.
- **Human-only:** red-team/prompt-injection review with real multi-company data.

#### AT-065 — Tek belge bir kez saklanır; stok/cari/muhasebe/finans ekranları aynı olay ve dosya referansını gösterir.
- **Lane:** Cari/Platform · PROPOSED (W2-BELGE; R57)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `Inventory.UnitTests/MaterialLabelTests.Attaching_a_document_is_recorded_with_who_and_when` covers a document reference on a label only.
- **Status:** NONE
- **Gap:** no shared document store or cross-screen file reference.
- **Human-only:** —

#### AT-066 — PDF adı Türkçe karakterleri korur, yasak dosya karakterlerini güvenli dönüştürür ve gömülü fontla doğru render olur.
- **Lane:** Cari/Platform · PROPOSED (W0-PDF / ERP-000H; R58)
- **Browser / SQL:** NONE
- **.NET:**
  - `DocumentGeneration.Pdf.Tests/TechnicalPdfGeneratorTests.Visible_file_name_builder_preserves_nfc_turkish_and_exact_master_pattern`
  - `DocumentGeneration.Pdf.Tests/TechnicalPdfGeneratorTests.Visible_file_name_sanitization_is_idempotent_and_path_safe`
  - `DocumentGeneration.Pdf.Tests/TechnicalPdfGeneratorTests.Visible_file_name_rejects_reserved_device_stems`
  - `DocumentGeneration.Pdf.Tests/TechnicalPdfGeneratorTests.Generated_pdf_is_one_page_semantic_golden_with_embedded_noto_sans`
  - `DocumentGeneration.Pdf.Tests/TechnicalPdfGeneratorTests.Vendored_noto_sans_regular_hash_is_locked`
  - `ArchitectureTests/PlatformTenantQueryFilterBypassGuardTests.Pdfsharp_dependency_is_limited_to_the_isolated_pdf_component`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:**
  - The PDF component is not wired to any API endpoint or UI download, so there is no end-to-end file-name or render proof.
  - The branding font path is gated (`pdf.branding.enabled` default `false`, OPEN-019).
- **Human-only:** visual render check in real PDF viewers and printers.

#### AT-067 — WhatsApp Business olmadan Paylaş işlemi desteklenen cihazda sistem paylaşım penceresini açar; destek yoksa indir + metin kopyala çalışır.
- **Lane:** Cari/Platform · PROPOSED (W2-BELGE; R58)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** no share action.
- **Human-only:** real-device check (mobile with and without Web Share support).

#### AT-068 — AI kişi bazlı yetki taslağı ve risk/fark üretir fakat yetkili yönetici onayı olmadan izin değişmez.
- **Lane:** Cari/Platform · PROPOSED (W5-AIGATEWAY)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `Identity.UnitTests/AccessGrantRequestTests.CriticalGrant_RequesterCannotApproveOwnRequest` and `.CriticalGrant_TargetUserCannotSelfApprove` cover a non-AI grant request.
- **Status:** NONE
- **Gap:** no AI permission draft and no permission-management screens (R12).
- **Human-only:** manager approval UAT.

#### AT-069 — Yetkili global arama temsilî üç yıllık hacmin üç katında belirlenen p95 hedefini geçer; derin sayfalama veri atlamaz/tekrarlamaz.
- **Lane:** Cari/Platform · PROPOSED (W0-SEARCH / ERP-000I; production gate `search.productionSloAccepted=false`; OPEN-015)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** keyset paging without skips or repeats in module lists:
  - `c01:ci` › B-C01-007 `[PR+main]`
  - `FinanceDraftSqlServerRelationalAcceptanceTests.Drafts_are_listed_newest_first_across_pages_with_a_company_bound_cursor` (finance)
  - `SalesDraftSqlServerRelationalAcceptanceTests.A_stored_draft_is_rebuilt_through_the_domain_and_listed_newest_first_with_a_company_bound_cursor` (sales)
- **Status:** NONE
- **Gap:** no global search. No volume or p95 test exists anywhere.
- **Human-only:** representative 3-year × 3 dataset and the p95 target (OPEN-015).

#### AT-070 — Yedekten nokta-zaman geri yükleme ve nesne dosyası bütünlüğü gerçek provada mutabık sonuç verir.
- **Lane:** Cari/Platform · PROPOSED (W0-RESTORE / ERP-000N `blocked`; R67)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** migration rollback rows such as `InventorySqlServerRelationalAcceptanceTests.A_rollback_refuses_while_commands_or_opening_counts_exist` (inventory) test schema rollback, not backup restore.
- **Status:** NONE
- **Gap:** no backup/restore drill tooling or tests.
- **Human-only:** a real restore drill (R67/R71) is inherently human-run.

### 7.2 Product/Inventory (22)

#### AT-015 — Ürün sınıfı ve ticari rol bağımsızdır; çelişki kontrol edilir.
- **Lane:** Product/Inventory · PROPOSED (W1-URUN)
- **Browser:** NONE for class/role. `browser:u02:ci` › `product-u02-suite/u02.spec.ts` › U02-FIX-01 `[label]` covers technical fields and history only.
- **SQL:**
  - `ProductGlobalRuntimeSqlServerRelationalAcceptanceTests.ProductGlobalRuntime_ReadCreateEditReplayAndCompanyIsolation_AreDurable` (product)
  - `ProductFamilyRegistrySqlServerRelationalAcceptanceTests.A_draft_binds_a_released_family_and_template_and_the_policy_judges_the_stored_card` (product)
- **.NET:**
  - `Product.UnitTests/ProductBasicEditTests.Classification_and_multiple_manual_roles_are_independent`
  - `Product.UnitTests/ProductBasicEditTests.Derived_role_cannot_be_overridden_and_failure_is_atomic`
  - `Product.UnitTests/ProductBasicEditTests.Invalid_or_forged_role_provenance_is_rejected`
- **Frontend:** `product/ProductCreatePage.test.tsx` › "offers class and family as REFERENCES scoped to the chosen group"
- **Status:** PARTIAL
- **Gap:** class/role independence and contradiction control are proven in the domain and persistence layers. No browser test asserts them.
- **Human-only:** —

#### AT-016 — Birim dönüşümü yuvarlama/tolerans golden örneğini geçer.
- **Lane:** Product/Inventory · PROPOSED (W1-URUN; R06)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):**
  - `Product.UnitTests/ProductFamilyRequirementPolicyV2Tests.The_first_path_is_kilogram_to_kilogram_without_conversion`
  - `Inventory.UnitTests/KilogramsTests.A_fourth_decimal_is_refused_not_rounded_or_truncated`
  - `Settings.UnitTests/ApprovedUnitProfileProviderTests` (class)
- **Status:** NONE
- **Gap:** only the kg→kg path exists. There is no conversion formula and no conversion golden.
- **Human-only:** approved conversion factors, rounding and tolerance.

#### AT-017 — Paket standardının yeni sürümü geçmiş siparişi değiştirmez.
- **Lane:** Product/Inventory · PROPOSED (W1-URUN; R06)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `Sales.UnitTests/SalesOrderDraftTests.A_line_keeps_the_owners_snapshots_the_exact_quantity_and_the_clients_text`
- **Status:** NONE
- **Gap:** no package standard entity and no versioning.
- **Human-only:** —

#### AT-018 — Fiyat sürümü para, birim, KDV, kanal ve geçerlilik bağlamını korur.
- **Lane:** Product/Inventory · PROPOSED (W1-URUN; price rows R27 are listed under Sales)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):**
  - `Sales.UnitTests/SalesOrderDraftTests.Commercial_fields_are_kept_as_text_and_the_price_unit_pairing_is_enforced`
  - `SalesDraftApprovalSqlServerRelationalAcceptanceTests.An_approved_drafts_price_change_archives_the_approved_revision_and_advances_it_in_one_save_on_real_sql` (sales)
- **Status:** NONE
- **Gap:** no price list or price-version entity. VAT and channel are absent.
- **Human-only:** —

#### AT-019 — Excel içe aktarma eşleme/önizleme/onay olmadan kayıt yazmaz.
- **Lane:** Product/Inventory · PROPOSED (W1-URUN; R08)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** no import feature.
- **Human-only:** real legacy files for mapping UAT.

#### AT-020 — Aynı Excel dosyasının tekrarı mükerrer kalıcı kayıt üretmez.
- **Lane:** Product/Inventory · PROPOSED (W1-URUN; R08)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `FinanceRecordSqlServerRelationalAcceptanceTests.Same_admission_key_with_another_batch_is_an_idempotency_conflict_without_second_effect` (finance). This is an approved-file source batch in another module.
- **Status:** NONE
- **Gap:** no import feature.
- **Human-only:** —

#### AT-029 — Fiziksel mevcut ile mülkiyet sahibi ayrı doğru gösterilir.
- **Lane:** Product/Inventory · PROPOSED (W3-STOK)
- **Browser:** NONE
- **SQL:**
  - `InventorySqlServerRelationalAcceptanceTests.Stock_position_read_reports_physical_allocation_and_available_components_from_real_SQL` (inventory)
  - `InventoryReadApiSqlServerRelationalAcceptanceTests.Parent_roll_reference_keeps_real_physical_block_document_ownership_facts_without_availability` (inventory)
- **.NET:**
  - `Inventory.UnitTests/StockLedgerTests.Company_owner_custodian_and_location_are_separate_facts`
  - `Inventory.UnitTests/InventoryStockPositionTests.Customer_owned_stock_keeps_owner_separate_from_company_and_custodian`
- **Frontend:**
  - `inventory/InventoryLabelDetailPage.test.tsx` › "reopens one actual label identity together with its ownership, location and quantities"
  - `inventory/InventoryStockPositionPage.test.tsx` › "reopens real position components …"
- **Status:** PARTIAL
- **Gap:** no browser suite covers the stock position or label detail screens. Customer-owned intake flows are not built (R45).
- **Human-only:** —

#### AT-030 — Müşteri malzemesi şirket serbest stoku gibi satılamaz.
- **Lane:** Product/Inventory · PROPOSED (W3-STOK)
- **Browser / SQL:** NONE
- **.NET:**
  - `Inventory.UnitTests/StockLedgerTests.Customer_owned_goods_held_by_our_company_never_count_as_our_stock`
  - `Inventory.UnitTests/InventoryParentRollTests.Customer_owned_facts_remain_visible_but_cannot_be_selected_without_context`
  - `Api.IntegrationTests/ManufacturingInventoryOutputAdmissionTests.Counterparty_identity_is_fail_closed_before_owner_reads_until_its_reference_port_exists`
- **Frontend:** `manufacturing/OrderDetailPage.test.tsx` › "keeps customer/fason ownership fail-closed and sends no input command"
- **Status:** PARTIAL
- **Gap:** no SQL or browser test shows that a sales allocation or shipment refuses customer-owned stock.
- **Human-only:** —

#### AT-031 — Fasoncudaki şirket malzemesi doğru emanet/lokasyon durumunda görünür.
- **Lane:** Product/Inventory · PROPOSED (W3-STOK)
- **Browser / SQL:** NONE
- **.NET:** `Inventory.UnitTests/StockLedgerTests.Company_owner_custodian_and_location_are_separate_facts` (custodian = fason case)
- **Frontend:** `manufacturing/OrderDetailPage.test.tsx` › "keeps customer/fason ownership fail-closed and sends no input command"
- **Status:** PARTIAL
- **Gap:** no subcontract dispatch/return flow (R45) and no SQL or browser proof.
- **Human-only:** reconciliation with real subcontractor counts.

#### AT-032 — Aktif tahsis serbest miktarı düşürür ama stok hareketi oluşturmaz.
- **Lane:** Product/Inventory · PROPOSED (W3-TAHSIS)
- **Browser:** `browser:s04:ci` › `sales-s04/specs/s04-approval-journey.spec.ts` › S04-06 (asserts in SQL that the movement count is unchanged across allocate and release) `[label]`
- **SQL:** `InventorySqlServerRelationalAcceptanceTests.Physical_sales_allocation_reduces_free_capacity_without_a_movement_and_release_restores_it` (inventory)
- **.NET:**
  - `Inventory.UnitTests/StockAllocationTests.Create_starts_active_at_version_one_without_a_stock_movement`
  - `Inventory.UnitTests/InventoryStockPositionTests.Physical_components_and_active_allocations_are_reported_without_double_counting`
- **Frontend:** `sales/SalesDraftAllocationPanel.test.tsx` › "saves, closes, reopens, releases and reopens the same server record"
- **Status:** AUTOMATED_ON_MAIN
- **Gap:**
  - The only allocation consumer is the approved sales-order line. Production allocation does not exist.
  - The browser proof is `[label]`. The free-quantity decrease is asserted in SQL, not on screen.
- **Human-only:** UAT sign-off.

#### AT-033 — Aynı etiket izin verilenden fazla aktif tahsis edilemez.
- **Lane:** Product/Inventory · PROPOSED (W3-TAHSIS)
- **Browser:** NONE for the refusal
- **SQL:**
  - `InventorySqlServerRelationalAcceptanceTests.Concurrent_physical_allocations_cannot_both_consume_the_same_free_capacity` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Physical_sales_allocation_refuses_over_allocation_stale_and_foreign_sources_without_effect` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Stock_position_read_fails_closed_when_active_allocations_exceed_the_stored_free_bucket` (inventory)
- **.NET:** `Inventory.UnitTests/StockAllocationTests.Above_remaining_has_no_effect`
- **Frontend:** NONE for the refusal
- **Status:** PARTIAL
- **Gap:** the over-allocation refusal is not proven in the UI or a browser suite.
- **Human-only:** —

#### AT-034 — Tahsis serbest bırakılınca kalan doğru geri döner.
- **Lane:** Product/Inventory · PROPOSED (W3-TAHSIS)
- **Browser:** `browser:s04:ci` › S04-06 (release, `remainingKg: '0'`, reopen from server truth) `[label]`
- **SQL:**
  - `InventorySqlServerRelationalAcceptanceTests.Physical_sales_allocation_reduces_free_capacity_without_a_movement_and_release_restores_it` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Physical_allocation_HTTP_create_replay_and_release_use_one_company_scoped_SQL_lifecycle` (inventory)
- **.NET:**
  - `Inventory.UnitTests/StockAllocationTests.Release_returns_every_remaining_kilogram_and_closes_without_consuming_stock`
  - `Inventory.UnitTests/StockAllocationTests.Partial_consumption_advances_version_and_stays_active`
- **Frontend:** `sales/SalesDraftAllocationPanel.test.tsx` › "saves, closes, reopens, releases and reopens the same server record"
- **Status:** AUTOMATED_ON_MAIN
- **Gap:** same scope limits as AT-032: sales consumer only, and the browser proof is `[label]`.
- **Human-only:** UAT sign-off.

#### AT-035 — Kalite bloğu serbest miktardan doğru düşer.
- **Lane:** Product/Inventory · PROPOSED (W3-TAHSIS; quality block R23)
- **Browser:** NONE
- **SQL:** `InventorySqlServerRelationalAcceptanceTests.Receipt_issue_block_and_release_are_durable_replayable_audited_and_reconcile` (inventory)
- **.NET:**
  - `Inventory.UnitTests/StockLedgerTests.Blocking_moves_stock_out_of_free_without_changing_the_physical_total`
  - `Inventory.UnitTests/InventoryReadModelsTests.Balances_are_exact_text_totals_by_block_and_a_deterministic_order`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:** no quality workflow or UI for placing and releasing a block (R23), and no browser proof.
- **Human-only:** —

#### AT-036 — Yüklemeye tutulan miktar başka siparişe kaynak olamaz.
- **Lane:** Product/Inventory · PROPOSED (W3-TAHSIS)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** the allocation exclusivity rows under AT-033.
- **Status:** NONE
- **Gap:** loading-instruction lines are not bound to labels or allocations (R41). There is no loading hold in Inventory.
- **Human-only:** —

#### AT-037 — Sayım farkı neden ve onay olmadan kesinleşmez.
- **Lane:** Product/Inventory · PROPOSED (W3-STOK; R21)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):**
  - `Inventory.UnitTests/OpeningCountTests.Every_counted_piece_becomes_one_free_receipt_sourced_by_its_count_line_and_the_book_equals_the_count`
  - `browser:r14:ci` › R14 `[label]`

  Both cover opening count only. There is no count difference.
- **Status:** NONE
- **Gap:** no periodic count or count-difference adjustment.
- **Human-only:** a physical count.

#### AT-038 — Etiket bölme/birleştirme soy ağacını korur.
- **Lane:** Product/Inventory · PROPOSED (W3-ETIKET)
- **Browser:** NONE
- **SQL:**
  - `InventorySqlServerRelationalAcceptanceTests.Split_then_merge_preserves_mass_replays_and_reads_the_many_to_many_genealogy` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Stale_foreign_and_allocated_sources_fail_closed_without_a_genealogy_effect` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Genealogy_migration_rolls_back_when_empty_and_blocks_data_loss_after_a_transformation` (inventory)
- **.NET:**
  - `Inventory.UnitTests/InventoryLabelGenealogyTests.Split_requires_one_whole_source_and_conserves_mass_into_new_labels`
  - `Inventory.UnitTests/InventoryLabelGenealogyTests.Merge_requires_multiple_sources_and_one_new_label`
  - `Inventory.UnitTests/InventoryLabelDetailTests.Valid_split_and_merge_transformation_groups_are_returned`
- **Frontend:** `inventory/InventoryLabelDetailPage.test.tsx` › "keeps genealogy labels inert without the inventory label read capability"
- **Status:** PARTIAL
- **Gap:** no split/merge user screen (R19) and no browser proof.
- **Human-only:** —

#### AT-039 — Lot/rulo/ısı/MTC bağı girişten çıkışa izlenir.
- **Lane:** Product/Inventory · PROPOSED (W3-ETIKET)
- **Browser:** NONE
- **SQL:**
  - `InventorySqlServerRelationalAcceptanceTests.A_label_without_its_lot_is_refused_by_the_store_and_by_the_schema` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Unknown_heat_and_coil_stay_null_and_a_text_that_does_not_fit_is_refused_not_cut` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Manufacturing_issue_return_and_output_commit_once_replay_and_keep_exact_physical_trace` (inventory)
- **.NET:**
  - `Inventory.UnitTests/MaterialLabelTests.Lot_and_label_trace_is_mandatory`
  - `Inventory.UnitTests/MaterialLabelTests.Heat_and_coil_are_never_generated`
  - `Inventory.UnitTests/MaterialLabelTests.Only_the_required_documents_decide_the_status`
  - `Inventory.UnitTests/MaterialLotTests.A_lot_is_one_company_and_one_product_with_heat_and_coil_only_when_known`
- **Frontend:** NONE by title
- **Status:** PARTIAL
- **Gap:** the trace is proven at receipt and production. The exit end (shipment) and a full chain test are missing. MTC upload (R23) is not built. No browser proof.
- **Human-only:** real MTC documents.

#### AT-040 — Serbest fiziksel miktarın her bileşeni kaynak kayda açılır.
- **Lane:** Product/Inventory · PROPOSED (W3-STOK)
- **Browser:** NONE
- **SQL:**
  - `InventorySqlServerRelationalAcceptanceTests.Stock_position_read_reports_physical_allocation_and_available_components_from_real_SQL` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Label_detail_reads_one_versioned_identity_and_its_positions_without_an_effect` (inventory)
- **.NET:**
  - `Inventory.UnitTests/InventoryStockPositionTests.Physical_components_and_active_allocations_are_reported_without_double_counting`
  - `Inventory.UnitTests/InventoryLabelDetailTests.Detail_returns_the_bounded_newest_first_movement_timeline`
  - `Inventory.UnitTests/StockLedgerTests.Balances_reconcile_with_the_movements_that_made_them`
- **Frontend:** `inventory/InventoryStockPositionPage.test.tsx` › "reopens real position components and links to canonical product and location records without raw ids"
- **Status:** PARTIAL
- **Gap:** no test drills from each free component to its source document (receipt, allocation or block record). No browser proof.
- **Human-only:** —

#### AT-050 — Çıktı etiketi bütün kaynak girdilere geri izlenir.
- **Lane:** Product/Inventory · PROPOSED (W3-ETIKET; R35)
- **Browser:** `browser:mfg:ci` › `manufacturing-mfg/specs/mfg-order-journey.spec.ts` › "a recorder selects owner-backed references and stores one company-owned output and Inventory label" `[label]`
- **SQL:**
  - `InventorySqlServerRelationalAcceptanceTests.Manufacturing_issue_return_and_output_commit_once_replay_and_keep_exact_physical_trace` (inventory)
  - `ManufacturingSqlServerRelationalAcceptanceTests.Manufacturing_output_refuses_an_inventory_orphan_without_backfilling_the_fact` (manufacturing)
- **.NET:**
  - `Manufacturing.UnitTests/SlittingOrderTests.Every_output_is_bound_to_an_issued_parent_roll_of_this_order`
  - `Inventory.UnitTests/InventoryLabelGenealogyTests.Output_cannot_copy_print_or_document_history`
- **Frontend:** NONE by title
- **Status:** PARTIAL
- **Gap:** each output is bound to one issued parent roll. Tracing back to *all* inputs across several rolls or steps is not proven, and there is no production trace view.
- **Human-only:** —

#### AT-051 — Kalite karantinasındaki çıktı sevkiyata açılamaz.
- **Lane:** Product/Inventory · PROPOSED (W3-KALITE; R23)
- **Browser:** NONE
- **SQL:** `InventorySqlServerRelationalAcceptanceTests.Receipt_issue_block_and_release_are_durable_replayable_audited_and_reconcile` (inventory)
- **.NET:**
  - `Inventory.UnitTests/StockLedgerTests.Blocked_stock_cannot_leave_until_it_is_released`
  - `Inventory.UnitTests/InventoryManufacturingRealizationContractTests.Output_requires_explicit_identity_position_block_documents_and_product_version`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:** no quality quarantine workflow, and no shipment-level refusal test for a quality-blocked label.
- **Human-only:** quality release decision.

#### AT-063 — Stok satırından Üretim Planla, Tahsis Revize ve Yükleme Talimatı işlemleri doğru satır bağlamıyla açılır.
- **Lane:** Product/Inventory · PROPOSED (W3-STOK)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** stock rows have none of the three actions. The MFG plan picks parent rolls inside its own form, and the Logistics instruction starts from approved sales lines.
- **Human-only:** —

#### AT-064 — Alış siparişi kalanı `Kaynak/Durum = Alış Siparişi` görünür fakat mal kabulden önce fiziksel/serbest stoğu artırmaz.
- **Lane:** Product/Inventory · PROPOSED (W3-STOK). The goods receipt screen, R26, is a Sales lane row.
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):**
  - `InventorySqlServerRelationalAcceptanceTests.Goods_receipt_commits_purchasing_and_inventory_once_and_both_participants_replay` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Goods_receipt_host_rollback_removes_purchasing_and_inventory_effects` (inventory)

  Both show that stock arrives with the receipt command.
- **Status:** NONE
- **Gap:** no test asserts that an approved purchase order leaves physical and free stock unchanged. No stock view shows the purchase-order remainder row.
- **Human-only:** —

### 7.3 Sales/Purchasing/Logistics (12)

#### AT-007 — İptal ve ters işlem geçmişi silmez.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-SATIS). Current evidence is mostly Finance/Accounting.
- **Browser:**
  - `browser:fin01:ci` › FIN01-03C, FIN01-03D `[label]`
  - `browser:s04:ci` › `sales-s04/specs/s04-d2-persona-withdrawal.spec.ts` › S04-D2-03 `[label]`
- **SQL:**
  - `FinanceRecordSqlServerRelationalAcceptanceTests.Reversal_requires_another_checker_and_keeps_the_original_with_two_way_history` (finance)
  - `FinanceCommandApiSqlServerRelationalAcceptanceTests.A_reversal_proposal_requires_another_HTTP_user_and_preserves_both_history_links` (finance)
  - `AccountingSqlServerRelationalAcceptanceTests.Reversal_and_correction_identities_hold_in_the_store_and_in_the_database` (accounting)
  - `SalesDraftApprovalSqlServerRelationalAcceptanceTests.The_withdrawal_migration_refuses_to_roll_back_while_any_draft_is_withdrawn` (sales)
- **.NET:**
  - `Sales.UnitTests/SalesOrderDraftApprovalTests.Withdraw_takes_back_an_unapproved_draft_into_its_own_terminal_state_and_deletes_nothing`
  - `Sales.UnitTests/SalesPersistenceModelTests.Drafts_lines_slices_and_attempts_never_change_and_nothing_is_deleted`
  - `Accounting.UnitTests/SourcePostingTests.Nothing_can_delete_or_edit_a_posted_journal`
  - `Finance.UnitTests/ReversalProposalPolicyTests.Approval_creates_a_distinct_recorded_source_and_reversal_event_without_editing_original`
- **Frontend:** `finance/FinanceSourceDetail.test.tsx` › "Record re-reads once, while a reversal proposal leaves the original source untouched"
- **Status:** PARTIAL
- **Gap:** cancelling a confirmed sales order (iptal, R30) and stock return/reversal (R22/R44) are not implemented. Sales covers only withdrawal of an unapproved draft.
- **Human-only:** —

#### AT-021 — Satış siparişi fiyat alt sınırını aşarsa doğru onaya gider.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-SATIS)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `Sales.UnitTests/SalesOrderDraftTests.A_discount_is_a_percentage_within_0_and_100_kept_as_text_with_no_threshold_invented` explicitly invents no threshold.
- **Status:** NONE
- **Gap:** no price floor rule and no approval routing by price.
- **Human-only:** floor and threshold policy decision.

#### AT-022 — Risk limiti aşımı siparişi sessizce serbest bırakmaz.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-SATIS; risk data W4-RISK)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** no risk limit model.
- **Human-only:** limit policy decision.

#### AT-023 — Onaylı siparişte kritik satır değişikliği eski onayı geçersizleştirir.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-SATIS)
- **Browser:** `browser:s04:ci` › `sales-s04/specs/s04-commercial-revision.spec.ts` › S04-08 (Approved → Rev.1 Preparing) `[label]`
- **SQL:** `SalesDraftApprovalSqlServerRelationalAcceptanceTests.An_approved_drafts_price_change_archives_the_approved_revision_and_advances_it_in_one_save_on_real_sql` (sales)
- **.NET:**
  - `Sales.UnitTests/SalesOrderDraftTests.A_revision_of_an_approved_draft_archives_the_approved_snapshot_first_then_advances_and_reenters_preparation`
  - `Sales.UnitTests/SalesDraftServiceTests.An_approved_price_change_archives_the_approved_revision_advances_the_draft_and_is_read_back_and_a_resent_key_replays`
- **Frontend:** `sales/SalesDraftRevisionHistory.test.tsx`; `sales/commercialRevision.test.ts`
- **Status:** PARTIAL
- **Gap:**
  - Only commercial revision (price, price unit, discount) of an approved draft is modelled, and it voids the approval.
  - Quantity, delivery date and product changes on an approved order, and the "critical change" list (R30), are not implemented.
- **Human-only:** definition of "critical" fields.

#### AT-024 — Kısmi teslimat sipariş kalanını doğru tutar.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-SATIS)
- **Browser:** `browser:l04:ci` › L04-03 `[label]`
- **SQL:**
  - `LogisticsInstructionSqlServerRelationalAcceptanceTests.An_instruction_with_lines_is_stored_issued_loaded_within_the_cap_replayed_and_rebuilt_in_one_save_each` (logistics)
  - `PurchasingSqlServerRelationalAcceptanceTests.Acceptance_participant_replays_exact_content_and_refuses_stale_over_remaining_or_cross_company_without_partial_write` (purchasing)
- **.NET:**
  - `Logistics.UnitTests/LoadingInstructionTests.Loading_four_of_six_tons_leaves_two_to_load_and_never_more_than_instructed`
  - `Logistics.UnitTests/LoadingInstructionTests.Shipping_comes_out_of_the_loaded_share_only_so_the_loaded_sub_share_is_never_subtracted_twice`
  - `Purchasing.UnitTests/PurchaseOrderAcceptanceTests.Forty_of_a_hundred_tons_accepted_leaves_sixty_expected_and_never_a_hundred_and_forty`
- **Frontend:** NONE by title
- **Status:** PARTIAL
- **Gap:** the remainder is kept on the Logistics instruction line and on the purchase order. The sales-order line's own delivered/remaining quantity after shipment is not modelled (R42/R43).
- **Human-only:** —

#### AT-025 — Satış ve satın alma belgeleri operasyon listelerinde karışmaz.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-SATIS)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `Logistics.UnitTests/LogisticsInstructionServiceTests.A_line_that_is_not_an_approved_line_of_this_customer_is_refused_fail_closed_and_nothing_is_stored` shows the Logistics instruction sources only approved sales lines.
- **Status:** NONE
- **Gap:** no shared operations list mixes inbound and outbound, so there is nothing to test yet.
- **Human-only:** —

#### AT-026 — Satın alma siparişi fazla/eksik teslimat toleransını uygular.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-SATINALMA)
- **Browser:** NONE (`browser:pur01:ci` covers request/order/approval, not receipt)
- **SQL:** `PurchasingSqlServerRelationalAcceptanceTests.Acceptance_participant_replays_exact_content_and_refuses_stale_over_remaining_or_cross_company_without_partial_write` (purchasing)
- **.NET:** `Purchasing.UnitTests/PurchaseOrderAcceptanceTests.Acceptance_can_reach_the_commitment_exactly_but_not_beyond_it`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:** acceptance is capped at the ordered quantity, which is zero over-delivery. There is no configurable over/under tolerance, no short-close and no goods receipt screen (R26).
- **Human-only:** tolerance values (policy).

#### AT-027 — Tedarikçi fiyat/miktar farkı kullanıcı onayı olmadan faturaya geçmez.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-SATINALMA)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** no purchase invoice or three-way match (R62).
- **Human-only:** —

#### AT-028 — İade kaynak sipariş, teslimat ve fatura bağını korur.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-SATINALMA)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** no return flow (R22/R44) and no invoice.
- **Human-only:** —

#### AT-053 — Kantar neti iki tartım ve brüt/dara yönüyle tutarlıdır.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W3-LOJISTIK)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):** `Logistics.UnitTests/LoadingInstructionTests.Every_load_states_how_it_was_measured`
- **Status:** NONE
- **Gap:** no weighbridge session and no gross/tare net calculation.
- **Human-only:** calibration against a real weighbridge.

#### AT-054 — Fiilî yüklenen etiketler tahsis ve sevkiyat planıyla uzlaştırılır.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W3-LOJISTIK)
- **Browser:** `browser:l04:ci` › L04-03, L04-04 `[label]`. These cover quantity only; no labels are involved.
- **SQL:**
  - `InventorySqlServerRelationalAcceptanceTests.Definite_shipment_flushes_one_atomic_effect_replays_and_host_rollback_removes_every_effect` (inventory)
  - `InventorySqlServerRelationalAcceptanceTests.Definite_shipment_hides_foreign_references_and_same_key_payload_conflicts_without_effect` (inventory)
- **.NET:** `Inventory.UnitTests/InventoryDefiniteShipmentContractTests.Closed_error_contract_keeps_stale_allocation_stale_label_and_unavailable_distinct`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:** the Inventory definite-shipment participant checks allocation and label versions. Loading-instruction lines are not bound to labels or allocations (R41), and definite shipment is not reachable by users (R42/R43).
- **Human-only:** —

#### AT-055 — Aynı sevkiyat gerçekleşmesi iki kez irsaliye/faturaya bağlanamaz.
- **Lane:** Sales/Purchasing/Logistics · PROPOSED (W2-IRSALIYE, W3-LOJISTIK)
- **Browser / SQL / .NET / Frontend:** NONE
- **Adjacent (does not prove this AT):**
  - `Logistics.UnitTests/LoadingInstructionTests.Deliveries_are_bound_to_their_source_shipment_and_capped_per_shipment`
  - `AccountingSqlServerRelationalAcceptanceTests.Two_racing_deliveries_of_one_fact_store_one_original_and_the_loser_retries_to_already_posted` (accounting)
- **Status:** NONE
- **Gap:** no dispatch note (irsaliye) or invoice (R62).
- **Human-only:** —

### 7.4 Finance/Accounting (1)

#### AT-058 — Ödeme/tahsilat açık kalem ve banka hareketine çift yönlü bağlanır.
- **Lane:** Finance/Accounting · PROPOSED (W4-MUHASEBE)
- **Browser:** `browser:fin01:ci` › FIN01-02D, FIN01-02E, FIN01-03 `[label]`
- **SQL:**
  - `FinanceRecordSqlServerRelationalAcceptanceTests.A_record_makes_one_money_event_and_every_answer_of_its_key_holds` (finance)
  - `FinanceRecordSqlServerRelationalAcceptanceTests.Concurrent_source_admission_commits_one_open_item_and_returns_one_durable_replay` (finance)
  - `FinanceRecordSqlServerRelationalAcceptanceTests.Concurrent_allocation_create_and_reversal_approval_commit_exactly_one_complete_outcome` (finance)
  - `FinanceCommandApiSqlServerRelationalAcceptanceTests.Manual_bank_payment_is_company_scoped_replayable_and_fails_closed_for_an_unowned_document` (finance)
- **.NET:**
  - `Finance.UnitTests/FinanceAllocationPolicyTests.Release_restores_both_balances_without_creating_money`
  - `Finance.UnitTests/FinanceAllocationPolicyTests.Create_refuses_another_account_currency_or_stale_version`
- **Frontend:** `finance/allocationCommand.test.ts` › "settles create only for its own active allocation and exact source/open item"
- **Status:** PARTIAL
- **Gap:**
  - Money source ↔ open-item allocation is proven in the backend and SQL.
  - There is no bank statement or bank movement to link to, and no reconciliation (R50).
  - Open-item sources are synthetic or approved-file adapters, not native invoices (R62).
  - Allocation is not exercised in a browser.
- **Human-only:** reconciliation against a real bank statement.

### 7.5 Organization/Manufacturing (12)

#### AT-041 — Kaynağı hazır olmayan plan hazır durumuna geçemez.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** `browser:mfg:ci` › `manufacturing-mfg/specs/mfg-plan-journey.spec.ts` › "permission and stale-label refusals write no plan, and a refusal replay is stable" `[label]`
- **SQL:**
  - `ManufacturingSqlServerRelationalAcceptanceTests.A_slitting_spec_is_plan_content_and_its_approval_is_refused_naming_every_missing_input` (manufacturing)
  - `ManufacturingApiSqlServerRelationalAcceptanceTests.Stale_versions_reused_ids_masses_outside_the_profile_and_foreign_work_centers_are_refused` (manufacturing)
- **.NET:** `Manufacturing.UnitTests/Engines/SlittingPlanApprovalTests.Nothing_known_refuses_the_approval_and_names_every_missing_input_nothing_changes`
- **Frontend:** NONE by title
- **Status:** PARTIAL
- **Gap:** approval is refused when spec inputs are missing or labels are stale. Resource readiness (material allocation, capacity, work-centre availability, R40) is not a gate.
- **Human-only:** —

#### AT-042 — Fiilî üretim tüketimi tahsis ve fiziksel mevcudu aşamaz.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** `browser:mfg:ci` › mfg-order-journey › "a recorder issues input, reopens it, and returns unused stock with the fresh Inventory label version" `[label]`
- **SQL:**
  - `InventorySqlServerRelationalAcceptanceTests.Manufacturing_participant_refuses_stale_foreign_over_return_payload_and_profile_failures_without_partial_effect` (inventory)
  - `ManufacturingSqlServerRelationalAcceptanceTests.Manufacturing_input_issue_refuses_stale_and_foreign_parent_references_without_partial_fact` (manufacturing)
- **.NET:**
  - `Inventory.UnitTests/StockLedgerTests.Physical_stock_never_goes_below_zero`
  - `Manufacturing.UnitTests/SlittingOrderTests.Input_can_be_issued_only_from_a_roll_the_plan_names`
  - `Manufacturing.UnitTests/SlittingOrderTests.Unused_input_is_returned_and_can_never_exceed_what_was_issued`
- **Frontend:** `manufacturing/OrderDetailPage.test.tsx` › "keeps customer/fason ownership fail-closed and sends no input command"
- **Status:** PARTIAL
- **Gap:**
  - The physical ceiling is proven in the ledger unit test. There is no SQL row for issuing more than the physical quantity.
  - The allocation ceiling cannot be tested because production allocation does not exist.
- **Human-only:** —

#### AT-043 — Boy kesme çıktıları + kalan + fire girdiyi tolerans içinde dengeler.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** `browser:mfg:ci` › mfg-order-journey › "each canonical process reaches an Inventory-backed realization and authorized close" (includes CutToLength; generic realization only) `[label]`
- **SQL:** `ManufacturingSqlServerRelationalAcceptanceTests.A_close_from_stored_facts_reconciles_each_roll_on_its_own` (manufacturing; generic balance)
- **.NET:**
  - `Manufacturing.UnitTests/Engines/CutToLengthEngineTests.Master_golden_28_750_kg_3x1500_as_3000x50_6000x55_4500x10`
  - `Manufacturing.UnitTests/Engines/CutToLengthEngineTests.An_exact_fit_leaves_exactly_zero_and_one_kilogram_more_is_refused_never_a_negative_remainder`
  - `Manufacturing.UnitTests/Engines/MassBalanceEngineTests.Bal003_reconciles_every_category_and_keeps_the_differences_sign`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:**
  - The engine golden is unit-level only. The browser test does not assert the cut-to-length split.
  - The tolerance is never assumed (`EngineBehaviourTests.A_tolerance_is_never_assumed_zero_when_missing_but_a_stated_zero_is_a_value`), and its values are not decided.
  - No cut-to-length user chain (R39).
- **Human-only:** tolerance values and a real-run comparison.

#### AT-044 — Dilme kombinasyonu genişlik ve kenar firesini dengeler.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** `browser:mfg:ci` › mfg-plan-journey › "each canonical process follows the same authorized save and fresh-reopen path" `[label]`. This test does not assert width.
- **SQL:** `ManufacturingSqlServerRelationalAcceptanceTests.A_slitting_spec_is_plan_content_and_its_approval_is_refused_naming_every_missing_input` (manufacturing)
- **.NET:**
  - `Manufacturing.UnitTests/Engines/SlittingWindingGoldenTests.Slitting_master_golden_28_750_kg_on_1500_mm_as_150x4_and_240x3`
  - `Manufacturing.UnitTests/Engines/EngineBehaviourTests.Theoretical_band_count_and_width_scrap_SLT_008_009`
  - `Manufacturing.UnitTests/Engines/EngineBehaviourTests.Used_width_beyond_the_net_width_is_refused_not_clamped`
  - `Manufacturing.UnitTests/SlittingOrderTests.Section13_slitting_example_balances_with_every_class_kept_apart`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:** the width and edge-trim balance is proven in the engine only. No browser or SQL assertion checks it.
- **Human-only:** real coil comparison.

#### AT-045 — Boru/profil adet–boy–kg dönüşümü golden örneğini geçer.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** `browser:mfg:ci` › mfg-order-journey (TubeProfile process, generic realization only) `[label]`
- **SQL:** NONE specific
- **.NET:**
  - `Manufacturing.UnitTests/Engines/TubeProfileAndPaintingEngineTests.Tube_master_golden_20_packages_of_50_at_6_m_and_30_m_per_minute`
  - `Manufacturing.UnitTests/Engines/TubeProfileAndPaintingEngineTests.Tube_mass_needs_the_unit_kg_from_the_master_and_never_estimates_it`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:** the engine needs the unit kg from the master and does not derive it. There is no count + kg + package chain (R37).
- **Human-only:** —

#### AT-046 — Boyama girdisi iyi/ikinci kalite/yeniden işleme/kayıp ile dengelenir.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** `browser:mfg:ci` › mfg-order-journey (Paint process, generic realization only) `[label]`
- **SQL:** `ManufacturingSqlServerRelationalAcceptanceTests.A_close_from_stored_facts_reconciles_each_roll_on_its_own` (manufacturing; generic)
- **.NET:**
  - `Manufacturing.UnitTests/Engines/MassBalanceEngineTests.Every_output_class_is_a_bal003_category_so_the_roll_balance_drops_nothing`
  - `Manufacturing.UnitTests/Engines/TubeProfileAndPaintingEngineTests.Painting_master_golden_with_the_simultaneous_count_derived_from_its_50_minutes` (time only)
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:** the output classes are GoodProduct, SecondQuality, UsableRemnant, Scrap and ProcessLoss. There is no **rework (yeniden işleme)** class. The painting golden covers time, not mass. No painting chain (R38).
- **Human-only:** —

#### AT-047 — Soğuk hadde giriş/çıkış/proses kaybı ortak dengeyi sağlar.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** `browser:mfg:ci` › mfg-order-journey (ColdRolling process, generic realization only) `[label]`
- **SQL:** `ManufacturingSqlServerRelationalAcceptanceTests.A_close_from_stored_facts_reconciles_each_roll_on_its_own` (manufacturing; generic)
- **.NET:**
  - `Manufacturing.UnitTests/Engines/ColdRollingEngineTests.Master_golden_28_750_kg_1500_mm_from_2_00_to_1_50_mm`
  - `Manufacturing.UnitTests/Engines/ColdRollingEngineTests.Scrap_leaves_the_output_mass_exactly_and_the_output_metres_follow_it`
  - `Manufacturing.UnitTests/Engines/MassBalanceEngineTests.Bal003_reconciles_every_category_and_keeps_the_differences_sign`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:** the browser test records GoodProduct output only. No end-to-end cold-rolling process-loss balance is asserted, and there is no cold-rolling user chain (R36).
- **Human-only:** —

#### AT-048 — Tolerans dışı kütle farkı onaysız kapanamaz.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** `browser:mfg:ci` › mfg-order-journey › "a closer is refused while a roll is unexplained, and closes it once the mass is accounted" `[label]`
- **SQL:** `ManufacturingApiSqlServerRelationalAcceptanceTests.The_close_writes_once_replays_its_key_and_refuses_an_unexplained_difference` (manufacturing)
- **.NET:**
  - `Manufacturing.UnitTests/OrderTargetAndCloseTests.An_order_with_an_unexplained_difference_does_not_close_and_keeps_it_visible`
  - `Manufacturing.UnitTests/OrderTargetAndCloseTests.One_unbalanced_roll_keeps_the_whole_order_open`
  - `Manufacturing.UnitTests/WipCloseTests.Rolls_never_offset_each_other_a_surplus_on_one_roll_does_not_cover_a_gap_on_another`
- **Frontend:** `manufacturing/OrderDetailPage.test.tsx` › "shows the decided refusal reason and does not close the order"
- **Status:** AUTOMATED_ON_MAIN
- **Gap:**
  - Implemented more strictly than the AT: any unexplained difference refuses the close, and there is no tolerance band.
  - There is no approval path to close with an out-of-tolerance difference.
  - The browser proof is `[label]`.
- **Human-only:** tolerance and approval policy; UAT sign-off.

#### AT-049 — Yeni reçete/formül/tolerans sürümü geçmiş gerçekleşmeyi değiştirmez.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** NONE
- **SQL:**
  - `ManufacturingApiSqlServerRelationalAcceptanceTests.Each_locked_organization_process_persists_reopens_and_snapshots_into_its_order` (manufacturing)
  - `ManufacturingSqlServerRelationalAcceptanceTests.Accepted_inventory_label_versions_are_durable_in_the_plan_and_order_snapshot` (manufacturing)
- **.NET:**
  - `Manufacturing.UnitTests/OrderTargetAndCloseTests.The_order_keeps_the_target_it_was_created_with`
  - `Manufacturing.UnitTests/SlittingOrderTests.The_order_keeps_its_own_snapshot_of_the_planned_inputs`
  - `Manufacturing.UnitTests/Engines/SlittingPlanApprovalTests.A_stored_approval_is_restored_without_re_running_the_gate`
- **Frontend:** NONE
- **Status:** PARTIAL
- **Gap:** the order and plan keep snapshots. There is no recipe/formula/tolerance registry with versions, so "a new version" cannot be exercised yet.
- **Human-only:** —

#### AT-052 — Düzeltme önceki gerçekleşmeyi silmez; ters/düzeltme zinciri kurar.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** NONE
- **SQL:**
  - `ManufacturingSqlServerRelationalAcceptanceTests.A_fact_id_is_one_fact_in_the_store_and_in_the_database` (manufacturing)
  - `ManufacturingSqlServerRelationalAcceptanceTests.Manufacturing_return_and_output_commit_once_and_replay_after_the_order_advances` (manufacturing)
- **.NET:**
  - `Manufacturing.UnitTests/ManufacturingPersistenceModelTests.Stored_manufacturing_is_never_deleted_and_only_its_allowed_fields_change`
  - `Manufacturing.UnitTests/RealizationFactTests.The_latest_wip_reading_counts_and_an_older_one_never_replaces_it`
- **Frontend:** `manufacturing/OrderDetailPage.test.tsx` › "reports a replayed close as history, not as a second close"
- **Status:** PARTIAL
- **Gap:** facts are append-only and never deleted. There is no reverse/correction command or chain for realization facts. The only correction input is an approved measurement correction inside the balance engine.
- **Human-only:** —

#### AT-061 — Üretim planı girdisi değişince `Hesapla` düğmesi olmadan doğru önizleme güncellenir; kayıt oluşmaz.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser / SQL / .NET / Frontend:** NONE
- **Status:** NONE
- **Gap:** no live preview endpoint or UI (no preview in the Manufacturing frontend or backend).
- **Human-only:** —

#### AT-062 — Eksik/geçersiz üretim girdisinde eski sonuç güncel gibi kalmaz; neden ve eksik alan görünür.
- **Lane:** Organization/Manufacturing · PROPOSED (W3-URETIM)
- **Browser:** NONE
- **SQL:** `ManufacturingSqlServerRelationalAcceptanceTests.A_slitting_spec_is_plan_content_and_its_approval_is_refused_naming_every_missing_input` (manufacturing)
- **.NET:**
  - `Manufacturing.UnitTests/Engines/EngineBehaviourTests.A_missing_parameter_is_unavailable_and_named_never_a_default`
  - `Manufacturing.UnitTests/Engines/MassBalanceEngineTests.Bal003_names_every_missing_category_and_refuses_a_fourth_decimal_or_a_negative_mass`
  - `Manufacturing.UnitTests/Engines/CutToLengthEngineTests.The_plan_names_missing_inputs_and_refuses_wrong_ones`
- **Frontend:**
  - `manufacturing/OrderDetailPage.test.tsx` › "shows BAL-003 as the server computed it and BAL-004 / BAL-006 as the engine's Unavailable, never a number"
  - `manufacturing/OrderDetailPage.test.tsx` › "reads the balance again when the order is re-read at a new version after an accepted fact"
- **Status:** PARTIAL
- **Gap:** naming the missing inputs is proven. Because there is no live preview (AT-061), "a stale result is not shown as current" is untested in the plan-input context. No browser proof.
- **Human-only:** —

## 8. Top gaps (cross-cutting)

1. **The platform capabilities behind 10 Cari/Platform rows do not exist on `main`:** global search (AT-002, AT-069), document vault/OCR/share (AT-056, AT-057, AT-065, AT-067), AI gateway (AT-059, AT-060, AT-068) and restore drill (AT-070). They depend on ERP-000G/H/I (`ready`) and ERP-000J/N (`blocked`).
2. **Cari master-data rules are missing:** VKN duplicate (AT-009), merge (AT-012), tax/bank document gate (AT-014) and a Cari multi-currency balance surface (AT-011).
3. **Commercial controls are not built:** price floor (AT-021), risk limit (AT-022), supplier variance (AT-027), returns (AT-028), weighbridge (AT-053), double invoice (AT-055) and invoices generally (R62).
4. **The Product master is NONE across AT-016…AT-020:** unit conversion, package, price version and Excel import.
5. **Most browser proof is not on the mandatory path.** Only `c01`, `sab` and `activation` run on every PR and push to `main`. Thirteen suites are `[label]`, and four suites are `[local]` only (`cari-acceptance-009/010`, `organization-locations`, `product-u01-u02`). Their assertions are not CI evidence until wired.
6. **Manufacturing formulas (AT-043…AT-047) are engine-level golden unit tests only.** The generic browser realization does not assert process-specific balances. AT-046 lacks a rework class, and AT-061 (live preview) is absent.
7. **SoD (AT-004) is per module.** Some self-approval is explicitly allowed and pinned by tests (Sales non-SalesSupport, Accounting normal journal draft). A central approval matrix is needed before this row can close.

## 9. How to update this trace

- Re-run the method in §1 against a newer `main` SHA and publish `ACCEPTANCE_TEST_TRACE_v2.md`. Do not rewrite v1 in place.
- Moving a matrix row's `Durum`/`Kanıt` or filling `Modul` is a canonical change that belongs to its owner. This trace only informs that change.
- A row may move to `AUTOMATED_ON_MAIN` only when §2 is met on an exact SHA. A green run of the named runners on that SHA is still required before anyone calls it PASS.
