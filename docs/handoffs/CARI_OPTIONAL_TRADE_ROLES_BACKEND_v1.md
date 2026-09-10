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
stored flags. Omit the HTTP `role` query parameter for an unfiltered request;
the query parser accepts named legacy roles, not numeric `role=0`. Missing/zero
normalization applies to command payloads, not the HTTP role filter.

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

## Required validation tooling correction

The existing RCSI evidence publisher must admit exactly one winner per output path.
On Unix, `File.Move(overwrite: false)` can perform a destination existence check
followed by replacing rename. The mandatory concurrent publisher test exposed two
successful writers. Publication now uses native `link` on the fully flushed,
same-directory temporary file, which cannot replace an existing destination;
cleanup unlinks only the caller's temporary name. Unsupported linking fails closed
with the existing evidence-write-failed outcome. Windows publication is unchanged.
The existing concurrent/stale-output tests remain strict; the concurrent target now
also exercises UTF-8 filename marshalling. The reviewed tool fingerprint is refreshed.
This enforces the existing evidence contract and changes no deployment or SQL authority.

## 11 September integration delta: enforced client contract

The earlier no-gate limitation is replaced in this candidate by the API-side
`X-ERP-Counterparty-Contract: optional-trade-roles-v1` protocol. It is a client
compatibility declaration, not authentication, a permission, a business role, or
a production activation switch. Claude's payload acknowledgment is not a receipt
for this new transport protocol; explicit recipient acknowledgment and browser
acceptance remain outstanding until recorded against the integrated source.

- Every Cari command, including active-state and replay, requires exactly one
  supported header before executing the handler. Missing header returns HTTP 409
  `client_contract_required`; empty, duplicate, comma-combined or unsupported
  declarations return HTTP 409 `client_contract_unsupported`. Neither rejection
  stores business, audit or idempotency results or consumes an attempt key.
- Legacy GET requests may receive existing 1..15-only authorized projections.
  If list, detail or link-candidates would expose any visible account with zero
  roles, the server returns `409 client_contract_required` instead of that
  success body. Only the authorized projection is inspected; other companies'
  hidden zero rows do not affect the response. GET never creates records.
- Unsupported GET declarations replace only successful authorized projections
  with `409 client_contract_unsupported`. Existing authorization, validation,
  not-found and availability failures retain their original status and hidden
  data behavior. In particular, an unauthorized list request remains HTTP 403,
  without a contract acknowledgment or any account identifiers.
- With the exact supported request declaration, the server owns and returns the
  same response header. It does not echo unknown values. Responses are private,
  no-store, and CORS exposes this header only within the existing origin policy.
- A new client must perform a successful read with the declaration and require
  the matching acknowledgment before enabling **any** Cari command. Pin that
  readiness to API origin, actor/session, selected company and local generation.
  An old server can ignore the request header, but supplies no acknowledgment;
  the new form must stay blocked, with no POST and no fabricated legacy role.
- Send the declaration and validate the acknowledgment on every supported
  request/response. Missing or mismatched acknowledgment after a command is an
  unknown outcome, not permission to resubmit with a fresh idempotency key.
  A 409 compatibility rejection after an earlier unknown outcome must preserve
  that unresolved attempt/key. A corrected client can retry the same normalized
  payload/key after context validation and fresh server acknowledgment.
- Logout, company/session/origin change and pending responses invalidate client
  readiness through the existing generation fences. Re-negotiate after a fence;
  no automatic retry or transfer of another context's attempt is introduced.
- Compatible requests still accept omitted roles and numeric 0..15, including
  15. Null, strings, negative, fractional and unknown-bit values are rejected.
  Responses require numeric roles; no missing/null-to-zero decoder fallback.
  Omit `role` for unfiltered reads. `expectedVersion` remains at least 1.
- List versions remain numeric int64; detail/mutation/candidate account versions
  remain positive decimal strings. No version or fingerprint encoding changed.

The backend supplies the enforcement and acknowledgment. New-form/old-server,
late response, logout/company switch and unknown-outcome browser conformance are
Claude-owned integration dependencies, not claimed passing by backend SQL tests.
There is no claim that an unsupported old binary has acquired this protocol.
Mixed backend nodes must be pinned/drained in the rollout checkpoint; missing
acknowledgment fails closed on the client. Keep the existing zero-row Down refusal
and compatible-schema forward fix. Do not infer a production rollout from merge.

The #227 main journey is retained alongside every prior required SQL test and
its omission guard. With two new protocol SQL facts, the strict inventory is
68 discoveries / 75 executions, all passed and zero skipped required. New
candidate CI and two independent exact-head reviews supersede the old head's
evidence only after they actually complete; this document does not claim results.

Sources: [.NET Unix move implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs),
[link atomic destination semantics](https://man7.org/linux/man-pages/man2/link.2.html).
