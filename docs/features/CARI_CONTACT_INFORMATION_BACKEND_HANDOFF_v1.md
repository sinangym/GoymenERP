# Cari company-account contact information: backend handoff v1

Date: 2026-09-12. Scope: W1-CARI / CARI-CORE-1, first basic card-edit slice.
This is an implementation contract, not production activation or full Cari acceptance.

## Sources and ownership

- Base: main `3e7dd98a5f8b7cb9e6850e05148eb4e7b26f3e3f`.
- Business: #232/`c762029c57099cfd17ebf4d6bf74fcf1820bb7fc`,
  `CARI_UI_VERTICAL_SLICE_v1.md` section 14 and Issue #66 comment 5642102538.
- Closed #230/#233/#234 and C006 evidence remains unchanged.
- Backend owns DTO, module persistence, API, OpenAPI and SQL tests.
  Claude owns frontend and browser acceptance. Coordinator owns merge and final reviews.
- This file's containing commit pins the DTO and checked-in OpenAPI.
  Frontend/composition pins are external acceptance inputs, not guessed here.

## Working boundary

Read/update only the selected CompanyPartyAccount's operational profile.
Party legal identity is read-only. No official address, identifier, bank, global
status or other-company profile changes. No create/link/merge/delete side effect.
GET never creates a Party, account, profile or child. Existing roles=0..15,
create/link omitted-or-zero roles, and unfiltered-list omission of role remain unchanged.

`GET /api/v1/counterparties/{partyId}/contact-information`
requires company-wide `counterparty.parties.read`.

`PUT /api/v1/counterparties/{partyId}/contact-information`
requires BOTH that read permission and dedicated company-wide
`counterparty.parties.update.basic`. Read/create/status do not imply basic edit.
No normal role or seed receives the new permission automatically.
A company-scoped API grant does not enable the normal staged UI provider.

Both routes require server-resolved `X-Company-Id`; query parameters are rejected.
Payload cannot select another company or modify global fields; unknown JSON members
reject the entire command. Authorized company without the Party/account relation:
non-disclosing 404, no account creation. Unauthorized company/narrow scope: 403.
Existing protected audit read API remains the history source and requires its
own `identity.audit.read` authorization; basic edit does not grant audit access.

## Wire examples

Use the existing authenticated transport and correlation/idempotency headers.
A real write additionally requires `X-ERP-Counterparty-Contract: optional-trade-roles-v1`.
Readiness is a fresh successful decoder-valid GET with that exact response ACK
in the same API/actor/session/company context. Error ACK is conditional and means
neither success nor readiness. Absence on an error is not proof of an old server.

GET response (an existing account without contact data):

```json
{
  "partyId": "11111111-1111-4111-8111-111111111111",
  "accountId": "22222222-2222-4222-8222-222222222222",
  "companyId": "33333333-3333-4333-8333-333333333333",
  "version": "1",
  "isActive": true,
  "information": {
    "phone": null, "phoneExtension": null, "email": null, "website": null,
    "addresses": [], "contacts": []
  }
}
```

PUT body (UUIDs are illustrative, not environment fixture identities):

```json
{
  "expectedVersion": "1",
  "changes": {
    "phone": "+90 0328 000 00 00",
    "addresses": [
      {
        "id": "44444444-4444-4444-8444-444444444444",
        "operation": "upsert", "purpose": "delivery",
        "countryCode": "TR", "line1": "Delivery address", "city": "Osmaniye"
      },
      {
        "id": "55555555-5555-4555-8555-555555555555",
        "operation": "upsert", "purpose": "operational",
        "countryCode": "DE", "line1": "Operational address"
      }
    ],
    "contacts": [
      {
        "id": "66666666-6666-4666-8666-666666666666",
        "operation": "upsert", "displayName": "Operations contact", "purpose": "Operations",
        "channels": [
          {
            "id": "77777777-7777-4777-8777-777777777777",
            "operation": "upsert", "kind": "phone", "value": "00123", "extension": "001"
          },
          {
            "id": "88888888-8888-4888-8888-888888888888",
            "operation": "upsert", "kind": "email", "value": "contact@example.test"
          }
        ]
      }
    ]
  }
}
```

Success: HTTP 204, empty body. Follow with fresh GET; account version becomes
`"2"` for an actual change, new children have `version:"1", isActive:true`.
The read returns all fields, including nullable optional text and retained inactive
children. Party identity/roles are read from the existing detail endpoint, not
duplicated in this new profile response.

Clear/update example:

```json
{
  "expectedVersion": "2",
  "changes": {
    "phone": null,
    "addresses": [
      {"id":"44444444-4444-4444-8444-444444444444","operation":"deactivate"}
    ]
  }
}
```

The omitted second address and contacts remain. To clear a phone that has an
extension, clear `phoneExtension` explicitly too. Changing only phone never
silently destroys a retained extension.

## Version, child identity and patch semantics

- `version` and `expectedVersion` are canonical positive decimal STRINGS:
  1..9223372036854775807, no leading zero, sign, fraction, exponent or whitespace.
  JSON numbers are rejected for this request. SQL rowversion bytes are never wire versions.
- The account logical Version locks the whole graph. Child read versions are
  strings for display/history, not separate expected-version fields.
- Client-generated nonempty UUIDs identify explicit child operations. Keep them
  unchanged for same-key retry. Scope is account + child collection; a channel
  additionally belongs to its contact. A foreign UUID never addresses another
  account's row. New UUID upsert creates only a child under the selected account.
- `operation` is exactly `upsert|deactivate|reactivate`.
  Status operations require an existing child and no value fields or nested updates.
  Upsert of an inactive child does not reactivate it. No delete operation.
- Omitted text retains the value. Explicit null/trim-empty clears optional text.
  Required-on-create fields cannot be null or empty when provided.
- Omitted arrays and omitted children retain existing rows; arrays are operation
  lists, never replacements. Null arrays, null elements, empty UUID and duplicate
  UUID operations in one collection are rejected. Maximum 100 operations per
  collection per request; this is not a maximum persisted-card row count.
- Canonically unchanged input preserves child/account logical versions and emits
  no business audit. The durable no-op result is recorded. A guarded SQL update
  uses the existing Version + rowversion concurrency predicate, so no-op still
  participates in conflict detection; only the internal SQL rowversion may advance.

## Exact field matrix

All values are text, trimmed without accent folding or silent truncation.
Control characters are rejected. Optional fields accept null. The following are
the backend's technical bounds, replacing the earlier Business draft suggestions.

| Field | Required | Limit / validation |
|---|---|---|
| information phone / channel phone value | No / new phone channel | 32; preserve plus, leading zeros and punctuation |
| phoneExtension / phone channel extension | No | 10; requires phone, not allowed on email |
| information email / channel email value | No / new email channel | 254; one syntactically valid mailbox, no display-name/list; no delivery verification |
| website | No | 2048; absolute HTTP/HTTPS, host present, no embedded credentials; no fetch |
| address purpose | New address | closed string billing, delivery, operational |
| address countryCode | New address | two ASCII letters, uppercase storage; syntactic code, NOT authoritative ISO registry verification |
| address line1 / line2 | New address / No | 250 / 250 |
| address city / district / postalCode | No | 100 / 100 / 20; no TR default or foreign-city requirement |
| contact displayName / jobTitle | New contact / No | 200 / 120 |
| contact purpose | New contact | free explicit text, 1..120, not an enum |
| channel kind | New channel | closed string phone or email; immutable for existing UUID |
| channel value | New channel | phone32 or email254 as above |

Business draft reconciliation: client UUID replaces its server-generated-ID
suggestion to preserve deterministic child identity across unknown-result retries.
Phone32/address250/postal20/jobTitle120 replace proposed64/500/32/100.
Country validation intentionally claims only syntax, not a missing country-reference
service. There is no standalone mobile/fax scalar in this slice: multiple phone
channels are supported; separate classified mobile/fax fields remain later work.
No-op follows Business section 14.4, not legacy status-command increment behavior.

## Errors and retry

| HTTP / code | Meaning |
|---|---|
| 400 validation_failed | Handler validation rejected entire command, no partial state |
| 400 command_metadata_invalid | Invalid command metadata |
| 400 empty body | JSON binding failure before handler; do not require a problem body |
| 401 authentication_required / 403 forbidden | Existing platform boundary |
| 404 not_found | No visible selected-company account relation |
| 409 version_conflict | Stale account/rowversion; preserve draft and GET exact current version |
| 409 idempotency_conflict | Same key, different actor/payload |
| 409 client_contract_required / client_contract_unsupported | Existing protocol gate |
| 415 empty body | Unsupported request media type |
| 500 / 503 | Existing platform failure contract, not successful save |

Handler validation uses the existing ProblemDetails `code/title/detail`.
There is currently NO structured field/index/UUID error map. Do not parse localized
`detail` for UI branching or synthesize server field pointers. Display an error
summary, preserve draft, and use the pinned field constraints for local hints.
A version-conflict response omits the optional numeric `conflict` object;
fresh GET is the exact string-version source.

Same logical attempt retains path/company/payload/key after timeout or unknown
response. No automatic new-key overwrite. Durable 204/404/409 replays advertise
`Idempotency-Replayed` and `Idempotency-Original-Correlation-Id`.
Reauthorization occurs before replay. Existing history does not authorize new writes.

## Storage, atomicity and rollback

Nullable `counterparty.CompanyPartyAccounts.ContactInformationJson` stores the
typed schemaVersion1 account-owned document. SQL enforces NULL or ISJSON=1;
application validation enforces typed children. Existing account FK, unique
CompanyId+PartyId, roles0..15, Version>=1 and rowversion remain.
No second global identity, finance or stock ledger.

Command name: `counterparty.company-account.contact-information.update.v1`.
Audit event: `counterparty.company-account.contact-information.changed.v1`,
entity `Counterparty.CompanyPartyAccount`, operation `UpdateContactInformation`.
Before/after JSON and logical versions are protected audit data, not telemetry.
Account update, audit insertion and durable execution commit in the same existing
SaveChanges transaction. No external effect/event bus delivery; outbox/inbox not
introduced for an in-process audit event.

Migration `20260912003000_AddCompanyAccountContactInformation` is nullable/additive.
Down refuses any non-null stored document BEFORE removing schema (SQL51000).
Empty-column down is supported; populated installs require a compatible forward fix.
Never discard production contact data to force a rollback.

## Evidence and next gate

Focused test source:
`CounterpartyContactInformationSqlServerRelationalAcceptanceTests` covers real
SQL round-trip/history/A-B isolation, default permission denial, malformed payload,
exact Int64, race/replay, no-op, rollback injection and migration.
It uses the existing isolated SQL factory and test-only explicit permission grant;
no production bootstrap policy is changed.

Existing acceptance links: CARI-AC-009/010/011/020/021/022/026/030/031 and
AT-003/005/006/008. Old C006 is not evidence of this edit.

Initial editor20 and permission2 passed; the no-op additions have a separate target
run. Exact runtime/OpenAPI endpoint and deterministic build-time contract tests
passed after adding the two new operation expectations. Detailed current TRX/CI
results and source commit are recorded in the PR, not inferred from these counts.
Final CI/reviews and real frontend browser composition are NOT_RUN at this early
handoff. This is not a backend-ready or end-to-end-complete declaration.

Next: finish targeted SQL atomicity evidence, pin draft source for Claude, then use
the existing isolated acceptance harness for a real edit/reopen/list flow. Final
K3 merge requires fresh CI and two independent exact-head reviews via coordinator.
Global legal/tax/bank approval and the remaining owner-module views remain on the
same Business delivery list, not silently removed from the full user goal.

## Contact-edit composition preparation

The existing Testing-only provider now additionally declares:
`cari.accounts.contact.update` / `cari.contact-edit` /
`/cari/:partyId/bilgiler` / `C-06`, requiring read + basic company-wide.
Its offline registry revision is
`sha256:VgK2rm5DemWYcc50HvrUcElPoDcymsCMdawVI137j4A`.
The test profile explicitly grants basic edit only to its synthetic administrator.
Normal provider and normal roles remain unchanged.

The existing DatabaseEvidence snapshot adds contact audit/execution counters,
account contact JSON/active state and a hash of complete Party rows.
VerifyContactEdit compares one exact expected canonical profile and one logical
version increment, one audit/command, all other account rows and global Party hash.
It requires zero new Party/account. It is distinct from the unchanged strict
create/link journey ExpectedNewParties=1; helper tests are not browser acceptance.

Coordinator assigned the single external input writer to CODEX-BACKEND at:
`C:\src\GoymenERP-cari-party-card-backend-20260912\artifacts\cari-acceptance-006\contact-edit-20260912\contract.json`.
Do not start the old consumed fc resource identity. New run/resource/source pins
and command wiring must be finalized against the published frontend form candidate
before bind/setup/start/publish. This preparation does not emit ENV_READY.
The historical source-provenance manifest is not rewritten to describe adapted files.

CI at 1a330b4: backend/build/migration, OpenAPI, frontend, C006-envelope, C01-browser
and catalog passed; SQL83 had 79PASS/4FAIL/0skip; dependent Container build skipped.
The four failures were historical migration fixture/model mismatches:
two exact migration totals now require4; roles-only downgrade assertions must begin
at the roles migration boundary; current EF runtime cannot operate on a schema
without its new column. Fixed fixtures retain historical raw row/rowversion,
constraint, migration journal and durable-byte assertions; HTTP replays run only
after restoring the current schema. The four focused real-SQL reruns passed.
This is NOT a claim that the new binary supports an unmigrated old schema:
deploy the additive migration before the new binary; never roll the schema back
while that binary serves requests.

### L-03 two-edit SQL evidence contract

The four HTTP PUTs in the contact-edit journey mean two successful logical edits,
one same-key replay, and one stale-version refusal. The refusal changes no business
data but DOES persist its `VersionConflict`/409 idempotency result. Expected delta:
zero new Party/accounts, two contact audits, THREE contact executions (two
`Succeeded`/204 and one `VersionConflict`/409). Replay does not add a fourth receipt.

`DatabaseEvidence.VerifyContactJourney` binds the browser's actual three distinct
keys and typed request payload hashes to SQL company/actor/outcome/status/schema.
It checks the same-key retry payload, the initial and subsequent version fences,
the exact L-03 address/contact/channel values, final phone/extension clear, account
version +2, unchanged other-company accounts and complete global Party hash.
It requires a fresh contact baseline; historical evidence or an additional retry
journey cannot be passed as this run. Snapshot projection is read-only.

The single-edit helper and strict create/link `ExpectedNewParties=1` remain separate.
The L-03 helper's controlled tests are NOT live SQL or browser acceptance. Source
pins, measurement CLI wiring, frontend final review, exact local BackendVerify and
fresh runtime publication remain separate gates. No new ENV_READY is asserted.
