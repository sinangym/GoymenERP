# Counterparty (Cari) module

Only the AT-009 slice is described here; the earlier Cari slices are documented in `docs/features/CARI_*` and
`docs/handoffs/CARI_*`.

## AT-009 — VKN/TCKN on the Cari card (user decision 2026-09-30)

| Rule | Behaviour |
|---|---|
| Validation | A number is stored only when it is 10 digits with a valid Gelir İdaresi VKN check digit, or 11 digits (first not 0) with valid NVİ TCKN check digits (`Domain/Parties/TaxIdentityNumber.cs`). Surrounding whitespace is trimmed; nothing else is cleaned. An invalid number is refused `400 tax_identity_invalid` and never stored. The database repeats the format (10/11 ASCII digits) as a check constraint. |
| Duplicate | The same number on another Cari card **of the active company** is a warning, never a refusal (branch / same firm). The read carries `duplicates`; `GET …/duplicates?number=` gives the same warning before saving. Cards other companies hold are not looked at (company fence). The lookup index is **not** unique. |
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
