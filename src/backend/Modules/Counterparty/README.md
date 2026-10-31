# Counterparty (Cari) module

Only the AT-009, SAT-D57 and SAT-D102/SAT-D119 slices and the location-operator source are described here; the earlier Cari slices are
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

## SAT-D102 / SAT-D119 / SAT-D120 — customer e-belge status and e-Fatura scenario (Sinan, 2026-10-28 and 2026-10-31; `SINAN_KARARLARI_2026-10-28_SATIS_EK13.md`, `…_2026-10-31_SATIS_EK17.md`)

**Owner and the CompanyId exception.** SAT-D119 makes the customer's e-belge status a fact of the customer's VKN/TCKN,
shared by every Göymen company. Counterparty owns it as ONE group-level record per tax identity number,
`counterparty.CustomerEDocumentProfiles` (`CustomerEDocumentProfile`): status, when/who/in which company it was last
checked, and the e-Fatura scenario override. This table deliberately has **no `CompanyId` tenant key and no query
filter** — the same narrow, documented group-wide exception as `Parties` (DEC-CAR-001). The exception is held narrow by
these rules:

- **Reach only through the caller's own account.** Every read and command first measures the company-wide permission,
  then requires the ACTIVE company's `CompanyPartyAccount` for the requested party (`CompanyId == server company`); only
  then is the party's VKN read and the shared row resolved. No account → 404 / port `NotFound`; the VKN is not even read.
  No list, search or "by VKN" route exists, so a company can reach a profile only for a customer it already holds.
- **Company-private rows never leak.** The shared row carries only status, check time, checker, checking company and
  override. Accounts, groups, contact data and every other company-scoped row stay behind their CompanyId filter.
- **The write guard** forbids deleting a profile and changing its id or VKN, requires a resolved company context, and
  refuses a newly written checking company other than the active one (a check is always recorded in the writing company).
- **The checking company is not a tenant key.** `EDocumentStatusCheckedInCompanyId` (FK to the company) only decides who
  sees the checker (below).

| Rule | Behaviour |
|---|---|
| Status | `Unknown` (0, never inferred) / `EInvoiceTaxpayer` (1, wire `EInvoice`) / `EArchive` (2). A known status carries the server time, the checker's actor id and the checking company; Unknown carries none of them (check constraint `CK_CustomerEDocumentProfiles_Status`). |
| Read | `GET /api/v1/counterparties/{partyId}/company-account/e-document` → `taxIdentityNumberPresent`, `status`, `statusCheckedAtUtc`, `statusCheckedInActiveCompany`, `statusCheckedByDisplayName`, `scenarioOverrideCode`, `evaluatedOn` (Europe/Istanbul business day), the active company's Settings `scenarioCodes {state, codes}` and `settingsDefault {state, scenarioCode}`, `effectiveScenario`, the fence `version` + `profileToken`, `viewer {canUpdate}`. |
| Fence (review P2-1) | The PAIR the read returned: `version` (the shared PROFILE version as decimal text; `"0"` = no profile yet, the first write creates it) and `profileToken` (opaque: SHA-256 over the party's VKN and the profile's id, or "none"). Commands send both (`expectedVersion`, `expectedProfileToken`). Another profile than the one read — e.g. after the party's VKN changed — is 409 `version_conflict` even when the numbers agree, so a stale client can never overwrite another taxpayer's record (ABA). The token goes only to a reader who already sees the card's VKN, so today it reveals nothing beyond the reader's own card; it is an UNKEYED hash, though, and with no profile it is a function of the VKN alone (a 10/11-digit space is searchable), so it must not be logged, snapshotted or shown elsewhere — a server-keyed HMAC is a follow-up to land before a consumer (API-32) stores it. |
| Record check | `PUT …/e-document/status` `{expectedVersion, expectedProfileToken, status}` → 204. A known status always records a new check (time + checker + company move, version moves, audited) even when unchanged; `Unknown` resets; Unknown → Unknown is a fenced no-op. |
| Scenario override | `PUT …/e-document/scenario` `{expectedVersion, expectedProfileToken, scenarioCode}` (must be present; `null` clears). A NEW code must be in the ACTING company's Settings list in force today: no list → 422 `einvoice_scenario_code_list_missing`; not listed → 422 `einvoice_scenario_not_listed`; Settings unreadable → platform 503 `service_unavailable`, nothing stored (retry with the same key). Re-sending the stored code is a no-op. Lexical shape `^[A-Z][A-Z0-9_]{0,31}$` (check constraint). |
| Settings (SAT-D120) | The code list starts with the standard GİB/UBL-TR set `TEMELFATURA, TICARIFATURA, EARSIVFATURA, IHRACAT` (reviewed, effective-dated catalog for GDC/GSP/GIN, Settings README). The **default stays empty** (SAT-D102), so a customer without an override is `Unresolved`. |
| Effective scenario | One rule (`CounterpartyEInvoiceScenarioResolverV1`), evaluated per reading company: override if present AND listed in that company's list → `Resolved/CustomerOverride`; an override the list does not carry → `Unresolved/CustomerOverrideNotListed` (never falls back to the default); no override → the Settings default or `Unresolved/SettingsDefaultNotSet`; Settings unreadable → `Unavailable`. Whether status and scenario fit (e.g. `EARSIVFATURA` on an `EInvoice` customer) is the invoice consumer's rule. |
| Passive account | ASSUMPTION-CARI-EDOC-4 (below): the read works (`accountIsActive: false`, the screen is read-only); both commands are 422 `e_document_account_inactive` and nothing is stored. |
| No VKN | A party with no VKN/TCKN cannot have a profile: the read shows `taxIdentityNumberPresent: false`, Unknown, Unresolved, `profileToken: null`; both commands are 422 `e_document_tax_identity_required` and nothing is stored. |

**Who checked (review P3-1, the more private reading of EK17).** EK17 calls the sharing of the checker a reversible
interpretation. This slice shares the CHECK (its existence and date are visible group-wide — the SAT-D119 trade-off: a
company that opens a card with a VKN learns that the VKN was checked somewhere in the group and when) but NOT the
checker: only a reader of the company the check was recorded in gets `statusCheckedInActiveCompany: true` and the
checker's display name (Identity directory, MOD-08; null when it cannot be resolved — the raw id is never shown). Other
companies get `false` and the card says "başka bir grup şirketinde kontrol edildi". The same holds for the audit trail
(review B-P2-1): an audit image written under a company other than the checking one carries the check time and
`EDocumentStatusCheckedInActingCompany: false`, with the checker's user id and the checking company written as `null`;
only the checking company's own audit rows carry them. Whether the checker should be shown to the other companies is
NEEDS-SINAN-5.

**Reversible assumptions (EK17 "Açık kalanlar" left these to this K3 PR).** Each is fail-closed and can be changed by a
later decision without data loss:

- **ASSUMPTION-CARI-EDOC-1 — sharing key.** The key is the normalized VKN/TCKN string of the party (the AT-009 value
  object's canonical 10/11 ASCII digits). Different Party/branch cards that carry the same number therefore share one
  profile. Reason: the e-belge status is the GİB taxpayer's, and GİB keys it by the number; AT-009 already allows the same
  number on several cards as a warning. NEEDS-SINAN-4 asks whether this is intended or the key should be a single Party.
- **ASSUMPTION-CARI-EDOC-2 — TCKN and no number.** A TCKN is a key exactly like a VKN (a natural-person taxpayer). A
  customer with NO number (final consumer, e-Arşiv) gets no profile: the commands are 422 and nothing is stored, so no
  status is ever attached to an unidentifiable customer. Reason: without a number there is nothing to share and nothing GİB
  could confirm.
- **ASSUMPTION-CARI-EDOC-3 — VKN change (OPEN-030).** The status follows the number: after a party's VKN changes from X
  to Y (AT-009 maker-checker), its card reads Y's profile (Unknown if Y was never checked); X's profile stays with X as it
  is — not moved, not merged, not deleted (another card may still carry X). A client that read X and posts after the
  change gets 409 through the profile-bound fence above. Reason: SAT-D119 "VKN'ye bağlı tek olgu".
- **ASSUMPTION-CARI-EDOC-4 — passive account.** A passive account of the acting company reads the shared profile but
  does not change it (422 `e_document_account_inactive`, stored and replayed like every other refusal; the screen offers
  no form). Reason: the profile is shared by the whole group, and a company that closed its relationship with the
  customer should not move another company's record; reactivating the account reopens the commands.

Common to both commands: company-wide `counterparty.accounts.e-document.update` (registered, not Critical, **assigned to
no role** — human gate; deliberately not `counterparty.parties.update.basic`) plus `counterparty.parties.read`, measured
first and re-measured before the write; one `Idempotency-Key` per company (stored 204/404/409/422 replay; the same key in
another company is a separate command); correlation/causation; one audit event per real change,
`counterparty.customer-e-document.status.recorded.v1` and `counterparty.customer-e-document.e-invoice-scenario.changed.v1`,
entity `Counterparty.CustomerEDocumentProfile` / the profile id; the VKN is never written to the audit payload or logs.
The first create of a VKN's profile runs in one transaction behind an `UPDLOCK, HOLDLOCK` key-range lock on the VKN
(review B-P2-2): a concurrent creator — possibly another company — waits, then sees the winner's row and gets 409
`version_conflict`, so the unique index `UX_CustomerEDocumentProfiles_TaxIdentityNumber` is not hit and SQL Server's
duplicate-key message (which names the number) is never raised or logged.

**Audit trail (review P3-2).** The audit event is written in the ACTING company (the platform rule: every audit row has
its company). A change made by company B to a profile company A also reads therefore appears in B's trail, not in A's.
A cross-company trail of one profile, readable by its profile id, is a follow-up.

**Read port.** `ICounterpartyAccountEDocumentProfileSourceV1.GetAuthorizedAsync(companyId, companyAccountId,
effectiveOn)` resolves the account → its party's VKN → the shared profile and answers `Found` (status, check time,
override, effective scenario, `ProfileVersion` — the shared PROFILE version, never the account's — and `ProfileToken`),
`NotFound`, `Denied` or `Unavailable` (fail-closed). Intended consumer: the Sales invoice draft (API-32, S5 follow-up),
which must refuse an e-Fatura draft while the status is Unknown or the scenario is not `Resolved`, and judge the
status/scenario fit itself. It is not wired into Sales in this slice.

Migration `20261031143822_AddCustomerEDocumentProfiles` (generated with `dotnet ef`): the group-level table, unique
VKN/TCKN index, the checking-company foreign key and four check constraints (VKN digits, version, status/check
consistency, override shape); nothing seeded. Rollback is refused with `51122` while any profile exists; with an empty
table it drops the table. Forward-fix plan otherwise: keep the additive table and correct with a new migration.

Screen: the Cari card's account section (`src/frontend/goymen-erp-web/src/cari/CariEDocumentPanel.tsx`).
