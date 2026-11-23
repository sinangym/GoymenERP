<!-- Ajan araştırma çıktısı (22 Kasım 2026); karar değildir. Birleşik değerlendirme: docs/delivery/ERP_FIKIR_HAVUZU_v1.md -->

# GoymenERP capability inventory (read-only review, repo HEAD 3dae29911, 2026-11-22)

**How to read this.** "Exists" means I found code: an endpoint in `src/backend/GoymenERP.Api/Endpoints/*`, a domain or service class, or a React screen. Most module READMEs label their own runtime or browser acceptance as `NOT_RUN`, so existing code is not the same as production-proven. Two older documents (the delivery plan `docs/delivery/KALAN_MODULLER_HAZIRLIK_PLANI_v1.md` §2, last status 2026-11-07, and the master report) are partly out of date. Where they disagree with the code, I followed the code.

**Scale:** about 3,000 backend .cs files across 17 modules, 447 HTTP routes, 286 permission constants (`BuildingBlocks/Security/ErpPermissions.cs`) and about 350 React screens.

**Who can use it today (observation).** The seed `GoymenERP.Api/Seed/identity-seed.json` has only 5 system roles, all for organization or security work. The module READMEs say 49 times that a permission is "granted to no role" (a human gate). Business roles are only a proposal (`docs/delivery/SATIS_ROL_YETKI_ONERISI_v1.md`). So in practice no user holds sales, finance or other business permissions out of the box.

---

## 1. Per module

### Organization
- **Exists:** companies (GDC, GSP, GIN; open a new company), the active-company context, sites, locations/warehouses (including an operator and active/passive state), work centers with a calendar, and a company tax-number (VKN) change-request and correction flow (`OrganizationEndpoints*.cs`).
- **Screens:** `frontend/.../organization/*` (CompanySetupPage, WorkCenter*, LocationDetailPage, CompanyTaxIdPage).
- **Missing:** bin or shelf locations (none in `Organization.Domain/Locations`), shifts.
- **Open decisions:** OPEN-001 (GIN's real legal name and VKN), OPEN-030 (VKN change lifecycle).

### Identity, SecurityEvents and Audit
- **Identity:** local accounts with PBKDF2 passwords, lockout, sessions and a login rate limit (`appsettings.json`). Optional TOTP two-factor with recovery codes, user management, role and user reads, role assignments and revocations, access-grant requests with approve/reject/withdraw (maker-checker), and first-admin setup for a new company. An OIDC JWT mode exists.
- **SecurityEvents:** `GET /api/v1/security-events`, with 1–2 year retention.
- **Audit:** an audit event query and refused-attempt records. Screen: `audit/AuditLogPage`.
- **Missing:** approval delegation (vekâlet), escalation and SLAs; production SSO.
- **Open decisions:** OPEN-013 (production OIDC/MFA). Role grants are the owner's (Sinan's) decision.

### Settings
- Over 30 versioned, audited policy areas (`Settings*Endpoints.cs`): units and unit conversion, money profiles, the tax master (rate lists, withholding, buyer situations), exchange rates, document number series (with gap reports), discount and contract-discount limits, sales delivery tolerances and approval thresholds, quote policy, e-invoice scenario code lists, finance approval matrices, cash-count-difference policies, bank institutions, collateral types and rates, purchase-invoice variance policy, segregation of duties, manufacturing parameter/tolerance profiles, work-center capacity, warehouse transfer scale, reference lists, notification retention, login rate limit, and bank CSV mappings.
- **Exchange rates:** TCMB import exists but is off by default (`Settings:ExchangeRates:TcmbEnabled`). Manual rates and corrections are supported.
- **Screens:** 27 under `settings/*`.
- **Open decisions:** OPEN-004/005/007 (tolerance and formula values are temporary assumptions), OPEN-012 (rate cut-off time and holiday calendar).

### Product
- **Exists:** product card lifecycle (draft, activate, deactivate, reactivate), code groups, duplicate candidates, history, Excel import with preview, item type, dimensions, COGS classification, price lists (versioned, scoped, tiered), package standards (import and approval), paint membership, and a profile weight table.
- **Screens:** `product/*`, `/fiyat-listeleri`.
- **Missing:** a separate service catalog; supplier-specific purchase prices.
- **Open:** the rule for "same product" in duplicate detection (NEEDS-SINAN).

### Counterparty (Cari)
- **Exists:** party list and create, link to a company account, active state, contact information, tax identity with duplicate check, legal and trade name, identity history and succession, merge, group deactivation preview, customer groups, and e-document profile.
- **Cari 360:** Sales, Purchasing and Finance tabs (`cari/Cari360Tabs.tsx`). The account statement (ekstre) is read from Finance `party-ledger`.
- **Missing:** reconciliation (mutabakat), IBAN on the party card.

### Sales (the deepest module, 73 screens)
- **Order chain:** request pool, quotes (revision, loss reasons, expiry approval, conversion to order), and order drafts. Orders support commercial, quantity, line and header revisions, delivery schedules, remainder changes, line pricing and kg prices, below-floor and discount exception approvals, risk check, warnings and exceptions, over-delivery approval, line closure, force-close, cancellation with an impact preview, a source plan, subcontract (fason) tax and lifecycle, user closure, and a status view.
- **Downstream:** contracts with instalment plans and discount approval, dispatches, invoice drafts, matching and finalization, invoice XLSX export (`SalesEndpoints.Invoices.cs` `/export`), official cancellation with finance retry, customer returns, and subcontract service invoices.
- **Order 360:** six tabs (`sales/SalesOrderTabs.tsx`).
- **Control center and reports:** `GET /api/v1/sales/control-center`; reports for open orders, lost opportunities and price variance (`SalesReportContractsV1.cs`).
- **Open:** 15 files carry NEEDS-SINAN, for example SAT-D219 revisions, SAT-D258 margin on items that are both bought and produced, unit-conversion cost, and invoice match differences (NEEDS-SINAN #3).

### Purchasing
- **Requests:** submit, approve, reject, withdraw.
- **Orders:** approval, exchange rate, cancellation with an impact preview, future allocations, line schedules and closure/reopen.
- **Receiving:** goods receipts and batch receipts with approval, single and batch receipt reversals.
- **Other:** supplier returns with a cost-exception approval, supplier revisions, service orders (tax, approval, service acceptance, price revision), additional (landed) costs.
- **Invoices:** matching, variance approval, finalization and hand-off to Finance. Invoice import reads XLSX/table files and UBL-TR 2.1 XML (`Purchasing.Infrastructure/Imports/*`).
- **Missing:** RFQ or quote comparison, supplier performance (both in the backlog slice W2-SATINALMA).

### Inventory
- **Stock and labels:** stock positions, movement timeline, label detail, trace and genealogy, and label PDF with a Code39 barcode.
- **Label operations:** classification, transformation (split/merge), parent rolls, stock counts and scraps (each with an approval decision), opening stock and opening costs.
- **Allocation and warnings:** physical allocations with suggestions, future-supply allocations, shortage warnings, a negative-stock attempt log, and release of unused subcontract allocations.
- **Transfers (two legs):** dispatch, receive, accept, loss request and decision, over-acceptance, reverse and cancel, plus receipt documents.
- **Costing:** average cost and exact cost pools.
- **Missing:** quality block or quarantine (W3-KALITE), quantity-based reservation (K-5 open), minimum stock (K-7 open), bins, handheld scanning ("LoadScan dependency_wait").
- **Open:** OPEN-008 (valuation method).

### Logistics
- **Loading:** loading instructions (issue, loads, cancel), vehicle and unload weighings, dispatch notes (approve, cancel, record the e-İrsaliye number, PDF), shipments and deliveries.
- **Exceptions and returns:** plan-excess requests and approvals, weighing differences with approval, and customer returns.
- **Other:** subcontract deliveries, transfer departures and outgoing documents, and sales/purchase operations work lists.
- **Missing:** a vehicle, driver and gate master (L-04); no plate or driver fields in the domain.

### Manufacturing
- **Engines:** six deterministic calculators: Slitting, CutToLength, Winding, TubeProfile, ColdRolling and Painting, plus a mass-balance engine (`Manufacturing.Domain` `*EngineV1`).
- **Plans:** preview, approval, reapproval, rejection, revision, sales-order link, due date with a suggestion, priority, work-center queue reordering, input availability.
- **Orders:** input issues and returns, outputs, fact corrections, close, cancel, reopen, mass balance, lineage, cost trace, non-kg inputs, production cost (TRY and USD), cost recognition, and a monthly scrap price table. One report: output-above-input closures.
- **Missing:** MRP/APS, finite capacity scheduling, QC.

### Finance (Finance and Treasury)
- **Masters:** bank accounts and cash accounts.
- **Money movements:** manual receipts (drafts, POS with commission per DEC-FIN-014), manual bank payments, expense payments, cash transfers and FX exchanges with book values, and reversal proposals (maker-checker).
- **Open items:** open items and allocations, payment allocations.
- **Party side:** party ledger and balance, party balance transfers, opening balances, and a period carry-forward.
- **Customer cheques and promissory notes:** portfolio with a risk view (DEC-FIN-016).
- **Credit and risk:** credit limits, collaterals, credit accounts (loans with a repayment plan), credit movements, and a party credit panel.
- **Official invoices:** official sales invoice records (manual entry with PDF, maker-checker) and official invoice CSV import batches.
- **Screens:** 45 under `finance/*`.
- **Missing:** bank statement import (only the Settings CSV mapping exists; the Finance consumer FIN-S21 is not wired), 13-week cash plan, budget, intercompany netting (W4-NAKIT, W4-GRUPFINANS).
- **Open decisions:** OPEN-009/010/011/012.

### Accounting
- **Exists:** periods (propose, approve, close, approved reopening), journals, trial balance, a read-only chart of accounts (a mirror of the existing Mikro accounting software's chart), versioned posting-account mappings, normal journal drafts with approval, journal reversal, and source vouchers from the sales/purchase invoice inbox with approval and cancel-reversal.
- **Cost proposals:** inventory cost, invoice-line COGS correction, purchase and manufacturing cost differences.
- **Queues:** posting queues (exception and pending valuation).
- **Pilot switch:** automatic intake of invoice events stays **off** in the pilot (SINAN 2026-11-21, question 4).
- **Missing:** VAT returns, cost centers or dimensions (`CostCenterId`/`ProjectId` return 0 hits), FX revaluation (only a pending-valuation queue).
- **Open:** sales-side withholding account (NEEDS-SINAN #66 6707860683), OPEN-008, OPEN-016.

### Documents
- **Vault:** append-only, SHA-256 content-addressed storage on the company's own server or NAS, Windows Defender scanning (fail-closed), a 20 MB limit, 10-year retention with no delete path, versions, and multiple links per document (`Modules/Documents/README.md`).
- **Owner kinds:** party, sales order, purchase order, stock label, manufacturing plan/order, invoice import.
- **Generated PDFs** are stored in the vault for sales orders and labels.
- **Branding:** company logo and letterhead with template publication (marked PARTIAL).
- **Missing:** central archive and Document 360 screens, OCR, sharing, metadata classification.
- **Open:** OPEN-018/019/021.

### Work, Idempotency and AI
- **Work:**
  - Approval inbox (`/api/v1/work/approvals`, `/work/queue`) with only 4 approval kinds: finance invoice import, sales order draft, stock count, stock scrap.
  - Personal in-app notifications (`NotificationsPage`, 90-day retention) with a single consumer (purchase supply revived).
  - Outbox dispatcher is wired in `Program.cs:499–503`; turning it on in production is a human gate.
  - No common approval engine.
- **Idempotency:** persistent command replay; a 90-day retention purge exists but is off (`appsettings.json`). Open: OPEN-021.
- **AI:**
  - Present: the gateway skeleton (`GoymenERP.Api/Ai/*`), `/api/v1/ai/ask` and `/capabilities`, and a right-hand panel (`frontend/src/ai/AiPanel.tsx`).
  - Off: `Ai:Enabled=false`, the only provider is a deterministic fake that never answers in natural language, and 0 tools are registered.
  - Planned: slices AI-11…AI-23 in `docs/delivery/AI_TREN_PLANI_v2.md` (knowledge base, read tools, scheduled tasks, e-mail, learning loop).
  - Open: OPEN-014 and OPEN-020 (local model, GPU, cloud provider and KVKK data-protection basis).

### Search and reports
- **Global search:** `/api/v1/search` across 4 kinds (party, product, sales order, material label), permission-filtered; top-bar box plus `/arama`.
- **Reports:** only the Sales reports and the manufacturing closure report. The Reports menu group is deliberately not drawn (`shell/navigation.ts`). W5-RAPOR (report center, scheduled exports, data quality) is not built.

---

## 2. Planned but not built (backlog `docs/backlog/DELIVERY_BACKLOG_v1.json`)
- **W2:** document inbox with OCR and e-mail drafts (W2-BELGE); RFQ, three-way match and supplier performance (W2-SATINALMA).
- **W3:** quality, quarantine, non-conformance and MTC certificates (W3-KALITE); vehicle/driver/gate (W3-LOJISTIK).
- **W4:** cash plan, budget and scenarios (W4-NAKIT); intercompany netting and elimination (W4-GRUPFINANS); covenants (W4-RISK).
- **W5:** report center (W5-RAPOR); StokERP migration tooling (W5-MIGRASYON — only docs exist in `docs/migration/`); AI gateway activation.
- **Deferred entirely** (`docs/decisions/ERTELENEN_KAPSAM.md`; master report §20 and §27.13): HR and payroll, maintenance/CMMS, advanced quality and CAPA, fixed assets, CRM, projects and detailed costing, legal/compliance, occupational health and safety (İSG).

## 3. Open decisions (`docs/decisions/ACIK_KARARLAR.md`)
- Partly open: OPEN-001, 004, 005, 007–021, 030.
- Closed: OPEN-002, 003, 006.
- Most relevant to this comparison: OPEN-016 (integration provider, credentials and method), OPEN-013 (SSO/MFA), OPEN-020 (AI provider), OPEN-018/021 (retention and multi-year periods), OPEN-011 (cash plan).
- About 50 source files carry NEEDS-SINAN notes, concentrated in Sales (15), Inventory (6) and Purchasing (5).

## 4. Usability features
- **Present:**
  - Capability-based menu: a group is drawn only if the user holds one of its entries; no empty menus.
  - Company switcher with a confirmation dialog.
  - Global search.
  - Read-only approval inbox and in-app notifications.
  - Order 360 and Cari 360 tabs.
  - Sales control center, accounting posting queues, logistics work lists, work-center queue.
  - PDFs: sales order, dispatch note, barcode label.
  - Bulk operations: batch goods receipts and reversals, imports for product, package standard, paint membership, official invoices, purchase invoices and exchange rates.
  - XLSX invoice export.
  - Idempotent retry and conflict-diff UI.
  - Phone layout below 768px with a drawer menu (`AppShell.module.css`), reduced-motion support, ARIA tabs.
- **Absent:**
  - Home dashboard or KPIs: `ShellHome.tsx` intentionally shows none ("Sahte gösterge yok", i.e. "no fake indicators").
  - PWA or offline mode.
  - Invoice, purchase order and quote PDFs.
  - Generic list export.
  - Saved views.

## 5. Integration status (DEC-INT-001: no accounting, e-document or bank integration in the first release)
- **e-Fatura / e-Arşiv:** issued **outside** the ERP. Finance records the official invoice by hand with its PDF, or imports a CSV. The ERP holds e-invoice scenario code lists and the customer's e-document profile. There is no connection to GİB or a private integrator.
- **e-İrsaliye:** the number from the externally issued document is recorded on the dispatch note (`LoadingInstruction.cs:599–675`).
- **Incoming invoices:** UBL-TR XML file upload is parsed; signatures are not verified.
- **Banks:** CSV column mapping exists in Settings; the vault accepts MT940 and XLSX; the Finance statement importer is not wired; no bank API.
- **Payments:** POS and bank transfers are manual records only.
- **None at all:** EDI; e-mail (no SMTP, AI-D17 is only planned); WhatsApp (OS share only); weighbridge or scales (weights typed in manually per OPEN-003, no device code).
- **Labels:** PDF with Code39, no ZPL or direct printer.
- **TCMB rates:** adapter exists, off by default.
- **External AI:** refused until configured.
- **Observability:** OTLP export off.

## 6. Typical ERP areas: present, partial or absent
| Area | Status | Evidence |
|---|---|---|
| CRM / opportunities | Partial | Sales requests, quotes with loss reasons, lost-opportunity report; no activities or campaigns |
| Quality / QMS | Absent | W3-KALITE; no quarantine code |
| Maintenance / CMMS | Absent | ERTELENEN_KAPSAM |
| HR / payroll | Absent | 0 hits |
| Fixed assets | Absent | 0 hits |
| Projects / cost centers | Absent | 0 hits |
| Budgeting | Absent | W4-NAKIT |
| BI / analytics | Partial | 3 sales reports only |
| Customer / supplier portal | Absent | |
| WMS bins / handhelds | Absent | locations only; no scanning |
| MRP / APS | Absent | due-date suggestion and manual queue only |
| Demand forecasting | Absent | |
| Cost accounting | Partial | actual production cost, average cost, COGS and cost-difference proposals; no ABC or standard costing |
| Treasury / cash forecast | Partial | cash, bank, loans, collateral, cheques; no forecast (OPEN-011) |
| Tax / VAT returns | Absent | tax master only |
| Consolidation / intercompany | Absent | W4-GRUPFINANS |
| Multi-currency revaluation | Partial | rates, FX book value, USD cost; revaluation only a pending queue |
| Multi-company | Present | GDC/GSP/GIN with tenant filters on every table |
| Subcontracting (fason) | Present | sales, purchasing, logistics and inventory |
| Lot / label traceability | Present | genealogy and trace |
