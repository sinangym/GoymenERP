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
| `FinanceBankAccountService` | FS0: the bank account master (`IFinanceBankAccountMaster`) and its authorized reader (`IFinanceBankAccountReader`); see below |
| Migration | `20260918230000_AddBankAccounts` (FS0; the shared `Commands` record names exactly one target) |

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
1. **FS0 bank account source (Finance).** Built (see below), not yet host-registered. Its currency needs Settings' currency candidates, which are Unavailable today, so every proposal is refused.
2. **Settings' approved money profile data** for `finance.manual-bank-receipt.v1`. The operation key itself is on main; mapping money operations is the backend owners' technical work (Business #254 / 5654539215). What is missing is Settings' approved profile, which is Settings' work (T02-b, central writer). Until it lands, Finance's money answer stays Unavailable.
3. **Finance permission registration** (central writer) and **the grant** (human).
4. **Host wiring** (central writer):
   - DI of the service and the ports;
   - the audit envelope;
   - the reviewed-inventory registration of this DbContext;
   - HTTP.

## FS0: the Finance bank account master (card #66 5731563923)

> **Runtime status: not registered by the host; every proposal would be refused** because no Settings
> currency source exists (T02-b). The positive SQL evidence uses isolated synthetic permission and
> currency ports. An account holds **no balance** and has **zero money effect**.

- **Fields.** Exactly `BankAccountView`'s: id, company, currency, display name, active, usable for
  receipt, version. Bank name, branch, IBAN, account number, account type, a currency change and
  deactivation or closing are **not decided in any source** and are not built.
- **Lifecycle (Ö4, Sinan #66 5722637822).** `Propose` stores a *Proposed* account: neither active nor
  usable, so a draft against it is refused with `bank_account_ineligible`. One `Approve` by an
  authorized user **other than the proposer** makes it *Approved* (active and usable). There is no
  second approval. The proposer's own approval is refused with `finance_approver_is_proposer`, and
  the database refuses it too (`CK_BankAccounts_Approval`). A routine recorded receipt is outside
  this rule.
- **Currency.** Required, never defaulted. It must be one of Settings' money currency candidates of
  the server-resolved company (`ISettingsMoneyCandidateReader`), checked on propose and again on
  approve. Being listed proves existence only; money validity stays the profile's, at draft save.
  An unavailable Settings source refuses (`settings_provider_unavailable`), and nothing is stored.
- **Authority.** Actions `ProposeBankAccount`, `ApproveBankAccount` and `ReadBankAccounts` go
  through `IFinancePermissionPort`, fresh before anything is read and after every owner await,
  replay included. The technical permission names proposed for central registration (CARİ) are
  `finance.bank-account.read`, `finance.bank-account.propose` and `finance.bank-account.approve`.
  The real grant, and which person holds which, is a human gate.
- **Idempotency.** The shared `Commands` record, keyed by (company, actor, key). The payload hash
  covers the command name and the request JSON. The same key and payload replays; another payload
  **or another command** (including a draft save) gets `IdempotencyConflict`.
- **Concurrency.** Approve names the expected version. Two racing approvals store one; the other
  gets `VersionConflict`.
- **Transaction.** One save covers the account, the command record and the audit event. The
  approval's audit event carries the approval context (proposer and approver).
- **Storage limits** (refused with `finance_value_does_not_fit`, never cut; not a policy): display
  name 200 characters.

## Validation

- `tests/GoymenERP.Finance.UnitTests`: contract and adapter tests, plus `FinanceDraftPersistenceModelTests`. The latter checks schema, filters, the idempotency key, snapshot == model, the write guard and the payload hash, all without a database.
- `tests/GoymenERP.Api.IntegrationTests/FinanceDraftSqlServerRelationalAcceptanceTests.cs` runs on real SQL Server with synthetic owner ports:
  - save, reopen, save with a fresh version;
  - stale version, replay and conflict;
  - company isolation;
  - the real Unavailable adapters refuse every save;
  - rollback.
- `tests/GoymenERP.Finance.UnitTests/FinanceBankAccountTests.cs`: the FS0 model, write guard, payload hash and every refusal before any read.
- `tests/GoymenERP.Api.IntegrationTests/FinanceBankAccountSqlServerRelationalAcceptanceTests.cs` runs FS0 on real SQL Server (synthetic permission and currency ports):
  - propose, refusal of self-approval, one approval by another user, then the draft saves against it through the real reader;
  - company isolation;
  - replay and conflict, across commands too;
  - currency outside the candidates, and the real unavailable Settings adapter;
  - racing approvals, and the database's own Ö4 check;
  - rollback.
- None of this is API or browser evidence.
