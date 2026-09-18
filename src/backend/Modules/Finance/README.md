# Finance module

Plan #275 T04 ("Finance banka kaynağı ve kalıcı taslak"); #254 `FINANCE_MANUAL_SOURCE_JIT_SPEC_v1` §2, §4, §5 FS1;
contract `GoymenERP.Modules.Finance.Contracts` (#258, #261). The contract's own notes live in
`examples/README.md`.

## FIN-T04a: persistent manual bank receipt drafts (preparation only)

> **Runtime status: every save is refused and nothing is stored.** On main, the host's adapters are:
> - the permission port is `UnavailableFinancePermissionPort` → `Unavailable` (`finance_permission_mapping_unavailable`);
> - with a permission, the bank account reader is `UnavailableFinanceBankAccountReader` → `bank_account_unavailable`;
> - with a bank account, the Settings money profile provider is unavailable → `settings_provider_unavailable`.
>
> The positive SQL evidence uses **isolated synthetic owner ports only**. This slice is **not a finance
> delivery** and claims **no user outcome**. A draft has **zero money effect**: no source, no bank
> balance, no allocation, no journal.

**What is here** (`GoymenERP.Modules.Finance.Infrastructure`):

| Part | What |
|---|---|
| `FinanceDbContext` | Schema `finance`. `ReceiptDrafts` and `Commands` are tenant tables: filtered by the server-resolved company and write-guarded (no delete, no company change, allowed fields only). Audit's sidecar is written in the same save |
| `ReceiptDraftService` | `IManualReceiptService.DraftSaveAsync` and `DetailAuthorizedAsync`. `List`, `Record` and `Reverse` answer `Unavailable` (not this slice; Record/Reverse are T05) |
| Migration | `20260918200000_InitialFinanceDrafts` (hand-written; a unit test pins snapshot == model) |
| `FinanceModuleExtensions` | DbContext registration and the migrate initializer. The host does not call it yet |

**Save rules** (the #258 contract, unchanged):
- **Authority.**
  - The server company and actor must equal the request context.
  - The `SaveDraft` action is checked before anything is read and again after every owner await, replay included.
  - The amount is shown only with `ReadAmount`; otherwise it is `Redacted` or `Unavailable`, computed fresh, also on replay.
- **Owners.**
  - Bank account, party account and the Settings money profile go through the reviewed ports and `PreparationValidator`, including the profile's scale via `ExactPositiveDecimalV1`.
  - An unavailable owner fails closed with its own code; nothing is stored.
- **Idempotency.**
  - Key = (company, actor, key), binary collation. The key must have no surrounding whitespace.
  - The same key with the same canonical payload (SHA-256 of the contract JSON) answers the stored result as a replay.
  - Another payload gets `IdempotencyConflict`.
- **Concurrency.** `expectedVersion` against `Version` (a bigint concurrency token, lossless text). A stale version gets `VersionConflict` and nothing changes.
- **Transaction.** One save covers the draft, the command record and the audit event, all or nothing.
- **Amount.** Stored as the exact text that passed the profile's scale (`varchar(max)`). No database decimal, no local digit limit, no rounding.
- **Source number.** No series is decided, so a draft shows its id; no number is invented.
- **Allocation.** A draft has no allocation fact: unknown, never 0.

**Storage limits** (refused with `finance_value_does_not_fit`, never cut; not a business policy):
- bank reference: 128 characters;
- description: 1000 characters;
- idempotency key, actor and correlation id: 128 characters each.

**Needed for a real user outcome** (none of these is OPEN-009; OPEN-009 concerns Record/Reverse authority):
1. **FS0 treasury account owner (Finance).** Its currency needs Settings' currency source, which is Unavailable today.
2. **Settings' approved money profile data** for `finance.manual-bank-receipt.v1`. The operation key itself is on main; mapping money operations is the backend owners' technical work (Business #254 / 5654539215). What is missing is Settings' approved profile, which is Settings' work (T02-b, central writer). Until it lands, Finance's money answer stays Unavailable.
3. **Finance permission registration** (central writer) and **the grant** (human).
4. **Host wiring** (central writer):
   - DI of the service and the ports;
   - the audit envelope;
   - the reviewed-inventory registration of this DbContext;
   - HTTP.

## Validation

- `tests/GoymenERP.Finance.UnitTests`: contract and adapter tests, plus `FinanceDraftPersistenceModelTests`. The latter checks schema, filters, the idempotency key, snapshot == model, the write guard and the payload hash, all without a database.
- `tests/GoymenERP.Api.IntegrationTests/FinanceDraftSqlServerRelationalAcceptanceTests.cs` runs on real SQL Server with synthetic owner ports:
  - save, reopen, save with a fresh version;
  - stale version, replay and conflict;
  - company isolation;
  - the real Unavailable adapters refuse every save;
  - rollback.
- None of this is API or browser evidence.
