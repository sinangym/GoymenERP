# Optional counterparty trade classification: backend v1 candidate

Decision source: the user's 10 September 2026 implementation instruction and
`docs/decisions/SINAN_KARARLARI_2026-09-10.md` in proposal PR #229, package head
`e1fd69da69df36a72e559ea07163fb3f359b3c61`. Technical acceptance source:
`docs/planning/2026-09-10/ILK_DILIM_CARI_ROL_UZLASTIRMA.md` at that same head.
These package documents are not assumed merged. This implementation is based on
accepted main `9f5b9bfdef249b9c8e89c6668f66b6aa55c34602`, independently of open
PRs #227 and #228. It does not change canonical decisions or task acceptance.

## API and domain

`POST /api/v1/counterparties` requires the existing legal kind and legal name.
`tradeName` and `roles` are optional. An omitted `roles` value and explicit numeric
`0` normalize to the existing `PartyRole.None`. Link accepts a JSON object `{}` at
`POST /api/v1/counterparties/{partyId}/company-account`. Missing or malformed HTTP
bodies, `null`, strings, negative values and unknown role bits remain invalid.

The numeric `roles` response field remains present: zero for no classification,
unchanged 1..15 for stored legacy flags. Neither value grants permissions or defines
whether a party can buy or sell. `Party.Kind`, account/party active states, immutable
party codes and company-account identity remain independent. Clearing classification
does not reactivate an account. Existing role query parameters still filter legacy
stored flags; query `role=0` means no role filter, not a roleless-only search.

Existing v1 command routes and stable errors remain. Create/link require the existing
company-wide permissions and resolved active company, with server-side authorization
and service reauthorization. Active-state continues to require expected version and
reason. New-record commands do not invent an expected version. No Identity roles,
grants, company context, external integration code projection or capability are changed.

## Persistence, transaction and retry

`CompanyPartyAccount.Roles` remains a NOT NULL integer. The additive migration
`20260910120000_AllowCounterpartyAccountNoRole` widens
`CK_CompanyPartyAccounts_Roles` from 1..15 to 0..15. Applied historical migrations
are unchanged. The company/party unique index, foreign keys, tenant write guards,
logical version and SQL rowversion remain. There is no data update or backfill.

Normalized missing/zero requests use the same current fingerprint. Legacy 1..15
fingerprint encoding and durable command schema version 1 are unchanged; old response,
audit and request hash bytes are not rewritten. Actor/company/payload/key binding,
fresh authorization on replay and unknown-schema rejection remain. Different-key
duplicate links retain the unique-constraint conflict behavior.

The existing transaction includes business records, audit and durable idempotency
result. The existing `counterparty.party.created.v1` and
`counterparty.company-account.created.v1` audit events carry actual classification.
Failed persistence rolls back the transaction; same-key retry reuses the original
logical attempt. No new external event, dispatcher, outbox/inbox, calculation, financial
or stock effect is introduced. GET/list/detail/candidates never create an account.
Implicit account creation at the first commercial command is a separate future slice.

## Upgrade, rollback and consumer handoff

EF applies the constraint replacement transactionally. Fresh installation and upgrade
must preserve old account identity/flags and durable audit/replay data. Before any
roleless write, downgrade restores the old constraint. If a zero-role account exists,
Down throws SQL error 51000 before dropping the widened constraint. Do not delete
accounts or assign fabricated Customer/all-role values to force rollback. Keep the
expanded schema and compatible readers, pause new writes and deliver a forward fix.

Response zero is incompatible with old readers requiring at least one flag. Claude
owns list/detail/mutation/candidate decoding, forms, stale context and browser tests.
Accept both 0 and 1..15 without weakening tenant/identifier/version validation. Remove
mandatory trade-role form selection; retain legal kind and active company. Update or
block stale browser clients before permitting new roleless writes in an integrated
release. This backend PR alone does not enable rollout or normal runtime capability.
Backend candidate tests do not establish mixed-version browser or production acceptance.

Risk is K3. Two independent reviews of the final commit plus fresh required CI and
relevant SQL evidence precede coordinator consideration. CODEX-BACKEND does not merge
its PR. The separate CODEX-ORCHESTRATOR receives the exact commit, test evidence and
open consumer gates through the PR/Issue #66; the existing progress owner records them.
Browser/mixed-version deployment and production activation remain NOT_RUN here.
