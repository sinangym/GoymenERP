# Counterparty (Cari) module

Only the AT-009, SAT-D57 and SAT-D102 slices and the location-operator source are described here; the earlier Cari slices are
documented in `docs/features/CARI_*` and `docs/handoffs/CARI_*`.

## Location operator eligibility source v1

`ICounterpartyLocationOperatorEligibilitySourceV1` is the exact, company-scoped Cari
answer for an Organization location command that has already authorized its own operation.
It accepts the current server company/actor and one proposed `PartyId`; it never exposes a
Cari list, search or card endpoint and grants no authority. An eligible operator is an active
Party with an active, unique account in that company carrying the existing `Supplier` role.
`Tolling` remains independent and does not substitute for `Supplier`. Missing/foreign,
inactive and non-supplier records are closed outcomes with no display payload. The source
writes no assignment, audit, idempotency, outbox or domain record; Organization relation
storage and seed Party mapping are separate delivery slices.

## AT-009 — VKN/TCKN on the Cari card (user decision 2026-09-30)

| Rule | Behaviour |
|---|---|
| Validation | A number is stored only when it is 10 digits with a valid Gelir İdaresi VKN check digit, or 11 digits (first not 0) with valid NVİ TCKN check digits (`Domain/Parties/TaxIdentityNumber.cs`). Surrounding whitespace is trimmed; nothing else is cleaned. An invalid number is refused `400 tax_identity_invalid` and never stored. The database repeats the format (10/11 ASCII digits) as a check constraint. |
| Duplicate | The same number on another Cari card **of the active company** is a warning, never a refusal (branch / same firm). The read carries `duplicates`; `POST …/duplicates` with `{ "taxIdentityNumber": "..." }` in the JSON body gives the same warning before saving without placing the identifier in the URL. Cards other companies hold are not looked at (company fence). The lookup index is **not** unique. |
| First entry | `PUT /api/v1/counterparties/{partyId}/tax-identity` `{expectedVersion, taxIdentityNumber}` → 204. A party that already has a number: `409 tax_identity_change_requires_approval`. |
| Change | `PUT …/tax-identity/change-request` `{expectedVersion, proposedTaxIdentityNumber, reason}` → 204; the request is **pending** and the stored number does not move. One pending request per party per company (filtered unique index `UX_PartyTaxIdentityChangeRequests_Pending`, `[Status] = N'Pending'`). Refusals: `tax_identity_not_set`, `tax_identity_unchanged`, `tax_identity_change_pending`. |
| Maker-checker | `PUT …/tax-identity/change-request/decision` `{expectedVersion, changeRequestId, decision: approve\|reject, note?}` → 204. The proposer is refused `409 tax_identity_self_decision` even when they hold the approve permission; the database refuses a decided row whose decider is its requester. Approval re-checks that the party still carries the number the request was made for (`409 tax_identity_change_stale`). A decided request never moves again (`tax_identity_change_not_pending`). |
| Read | `GET …/tax-identity` → number, kind (`vkn`/`tckn`), duplicates, `pendingChange`, `lastDecidedChange`, and `viewer {canUpdate, canApprove, isRequesterOfPending}` measured for the reader. |

Common to every command: company-wide permission (`counterparty.parties.tax-identity.update` for entry and request,
`counterparty.parties.tax-identity.approve` for the decision, plus `counterparty.parties.read`), one `Idempotency-Key`
(204 / 404 / 409 answers are stored and replayed), the **Party** version as decimal text (`version_conflict` when
stale), the active company's account as the existence fence (404 otherwise), one audit event per real change
(`counterparty.party.tax-identity.entered|change-requested|change-approved|change-rejected.v1`; the approval carries
its `ApprovalContextJson`). The number is never written to logs.

The number belongs to the **Party** (one legal counterparty for every Göymen company, DEC-CAR-001); the change
request belongs to the company it was raised in. The Party write guard allows only this column (with its version
and timestamp) to change. Migration `20260930150000_AddPartyTaxIdentity`; rollback is refused with `51059` while
any number or request exists.

Screen: the Cari card's identity section (`src/frontend/goymen-erp-web/src/cari/CariTaxIdentityPanel.tsx`).
Browser journey: `tests/e2e/cari-c02/specs/c02-tax-identity.spec.ts` (actors `c02-tax-maker`, `c02-tax-checker`).

## SAT-D57 — customer (price) group on the Cari card (Sinan, 2026-10-27, `SINAN_KARARLARI_2026-10-27_SATIS_EK9.md`)

**Owner.** Counterparty owns both the company's group list (`counterparty.CustomerGroups`) and the assignment
(`CompanyPartyAccount.CustomerGroupId`). The decision puts the group on the Cari card, company-scoped; keeping the
list in the same module lets the database itself refuse another company's group (composite foreign key
`(CompanyId, CustomerGroupId) → CustomerGroups(CompanyId, Id)`), which a Settings-owned list could not do without a
cross-module foreign key. Managing the list "from Ayarlar" is only the UX reading of the option text (EK9 says so); the
screen is `/cari/musteri-gruplari` (see below), the data stays here.

| Rule | Behaviour |
|---|---|
| List | `GET /api/v1/counterparties/customer-groups` → `{companyId, items[{id,name,isActive,version}], viewer{canManage}}`, the active company's groups only (passive ones included). **Nothing is seeded** — not in code, not in the migration: Sinan's examples (toptancı, perakendeci, imalatçı) are entered by the company's administrator. |
| Create | `POST …/customer-groups` `{id, name}` → 204. The caller chooses the UUID, so a lost answer is retried with the same key and id. Name: trimmed, NFC, 1..100, no control characters (`400 validation_failed`); unique per company among active AND passive groups (`409 customer_group_name_taken`; index `UX_CustomerGroups_CompanyId_Name`). An id that already exists is `409 customer_group_exists`. |
| Rename / passive | `PUT …/customer-groups/{id}/name` `{expectedVersion, name}`, `PUT …/customer-groups/{id}/active-state` `{expectedVersion, isActive}` → 204; the GROUP version as decimal text (`version_conflict`), `404 not_found` for an unknown or another company's group. A group is never deleted (write guard); deactivation is reversible. A same-value command is a fenced no-op without a version move or audit. |
| Account read | `GET /api/v1/counterparties/{partyId}/company-account/customer-group` → `{partyId, accountId, companyId, version, accountIsActive, customerGroup|null, viewer{canAssign}}`; `customerGroup: null` = no group (the price chain's group tier is skipped for this account). |
| Assign | `PUT …/{partyId}/company-account/customer-group` `{expectedVersion, customerGroupId}` → 204; the ACCOUNT version (shared with the other account commands); `customerGroupId` must be present — a group id or an explicit `null` to clear. A group that is not the active company's is `422 customer_group_not_found` (never named); a passive group is `409 customer_group_inactive` unless it already is the account's group (then a no-op). A passive group keeps its existing assignments **and their price** (SAT-D77, Sinan 2026-10-28, EK12 #853): passive only closes the group to new assignments. |

Common to every command: company-wide permission (`counterparty.customer-groups.manage` for the list,
`counterparty.accounts.customer-group.assign` for the account, plus `counterparty.parties.read`), measured first and
re-measured before the write; one `Idempotency-Key` (stored 204/404/409/422 answers replay); correlation/causation on
the stored execution and the audit; one audit event per real change (`counterparty.customer-group.created|renamed|
active-state.changed.v1`, `counterparty.company-account.customer-group.changed.v1`). Both permission names are
registered and Critical-free, **assigned to no role** (human gate).

**Read port.** `ICounterpartyAccountCustomerGroupSourceV1.GetAuthorizedAsync(companyId, companyAccountId)` answers
`Found` (with `CustomerGroupId` or null, the group's active flag and the account version), `NotFound`, `Denied` (context
is not the requested company, or no company-wide `counterparty.parties.read`) or `Unavailable` (any owner failure).
Consumers treat Denied/Unavailable as "could not be read" (SAT-D58 fail-closed), never as "no group". A passive group's
existing assignment is a usable `Found` group (SAT-D77): its price still applies; `CustomerGroupIsActive = false` is
information only. Counterparty does
not reference Product or Sales; the price list's CustomerGroup scope consumes this port in its own slice.

Migration `20261027202534_AddCounterpartyCustomerGroups` (generated with `dotnet ef`): rollback is refused with
`51091` while any group or assignment exists; with an empty list it drops the table and the all-null column.

Screens: the Cari card's account section (`src/frontend/goymen-erp-web/src/cari/CariCustomerGroupPanel.tsx`) and the
company list `/cari/musteri-gruplari` (`CariCustomerGroupsPage.tsx`), linked from the Cari list and card and mounted
under the Cari list capability (its read needs exactly `counterparty.parties.read`). Drawing it under the Ayarlar
sidebar group needs its own server shell capability (a Cari owner-contract change) and is left to that step.

## SAT-D102 — e-belge status and e-Fatura scenario on the Cari card (Sinan, 2026-10-28, `SINAN_KARARLARI_2026-10-28_SATIS_EK13.md`)

**Owner.** Counterparty owns the customer's e-belge profile, held on the company's account (`CompanyPartyAccount`), so
it carries `CompanyId` and each company records its own check when it opens the card. The allowed scenario codes and
the default scenario are **Settings** data (`ISettingsEInvoiceScenarioPolicyProviderV1`, effective-dated, reviewed
catalog `ApprovedEInvoiceScenarioCatalog`); Cari keeps no list of its own and reads Settings only through that port.
Both Settings keys start **empty** (SAT-D102: "Boş kalsın, ben gireyim"; no code list is decided either), so today no
override can be chosen and every customer's scenario is `Unresolved`.

| Rule | Behaviour |
|---|---|
| Status | `EDocumentStatus` = `Unknown` (0, every existing account; never inferred) / `EInvoiceTaxpayer` (1, wire `EInvoice`) / `EArchive` (2). A known status carries the server time of the check and the checker's actor id; Unknown carries neither (check constraint `CK_CompanyPartyAccounts_EDocumentStatus`). |
| Read | `GET /api/v1/counterparties/{partyId}/company-account/e-document` → status, `statusCheckedAtUtc`, `statusCheckedByUserId`, `scenarioOverrideCode`, `evaluatedOn` (Europe/Istanbul business day), `scenarioCodes {state, codes}`, `settingsDefault {state, scenarioCode}`, `effectiveScenario {outcome, source, scenarioCode, unresolvedReason}`, `viewer {canUpdate}`. Settings states: `Set` / `NotSet` ("kaynak yok") / `Unavailable`. |
| Record check | `PUT …/e-document/status` `{expectedVersion, status}` → 204. A known status always records a new check (time + checker move, version moves, audited) even when unchanged; `Unknown` resets; Unknown → Unknown is a fenced no-op. |
| Scenario override | `PUT …/e-document/scenario` `{expectedVersion, scenarioCode}` (must be present; `null` clears). A NEW code must be in the Settings list in force today: no list → `422 einvoice_scenario_code_list_missing`; not listed → `422 einvoice_scenario_not_listed`; Settings unreadable → platform `503 service_unavailable`, nothing stored (retry with the same key). Re-sending the stored code is a no-op. Lexical shape `^[A-Z][A-Z0-9_]{0,31}$` (check constraint). |
| Effective scenario | One rule (`CounterpartyEInvoiceScenarioResolverV1`): override if present AND listed on the date → `Resolved/CustomerOverride`; an override the list does not carry → `Unresolved/CustomerOverrideNotListed` (no fallback to the default); no override → Settings default (`Resolved/SettingsDefault`) or `Unresolved/SettingsDefaultNotSet`; Settings unreadable → `Unavailable`. |

Common to both commands: company-wide `counterparty.accounts.e-document.update` (new; registered, not Critical,
**assigned to no role** — human gate; deliberately not `counterparty.parties.update.basic`, so existing basic-edit
grants are not widened) plus `counterparty.parties.read`, measured first and re-measured before the write; one
`Idempotency-Key` (stored 204/404/409/422 replay); the ACCOUNT version as decimal text; correlation/causation; one audit
event per real change (`counterparty.company-account.e-document-status.recorded.v1`,
`counterparty.company-account.e-invoice-scenario.changed.v1`). No GİB or other external call is made; the GİB mükellef
query is a follow-up.

**Read port.** `ICounterpartyAccountEDocumentProfileSourceV1.GetAuthorizedAsync(companyId, companyAccountId,
effectiveOn)` answers `Found` (status, check, override and the effective scenario for `effectiveOn`), `NotFound`,
`Denied` or `Unavailable` — fail-closed as the SAT-D57 port. Intended consumer: the Sales invoice draft (API-32, S5
follow-up), which must refuse an e-Fatura draft while the status is Unknown or the scenario is not `Resolved`. It is
not wired into Sales invoice grouping in this slice.

Migration `20261028231320_AddCounterpartyAccountEDocumentProfile` (generated with `dotnet ef`): four additive columns
and two check constraints. Rollback is refused with `51121` while any account has a known status or an override; with
every account Unknown and no override it drops them. Forward-fix plan otherwise: keep the additive columns and correct
with a new migration.

Screen: the Cari card's account section (`src/frontend/goymen-erp-web/src/cari/CariEDocumentPanel.tsx`).
