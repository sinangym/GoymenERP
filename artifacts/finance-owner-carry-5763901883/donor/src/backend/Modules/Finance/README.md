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
| `ReceiptDraftService` | `IManualReceiptService.DraftSaveAsync`, `ListAuthorizedAsync` (FIN-T04b) and `DetailAuthorizedAsync`. `RecordAsync` since FIN-T05a (below; the period follows `occurredOn`); `Reverse` answers `Unavailable` (FIN-T06) |
| Migration | `20260918200000_InitialFinanceDrafts` (hand-written; a unit test pins snapshot == model) |
| `FinanceModuleExtensions` | DbContext registration and the migrate initializer. The host does not call it yet |
| `FinanceBankAccountService` | FS0: the bank account master (`IFinanceBankAccountMaster`) and its authorized reader (`IFinanceBankAccountReader`); see below |
| Migration | `20260918230000_AddBankAccounts` (FS0; the shared `Commands` record names exactly one target) |
| Migration | `20260919090000_AlignCommandsWithAdr011` (FIN-T04b; the command record keyed as ADR-011, and the list index) |

**Save rules** (the #258 contract, unchanged):
- **Authority.**
  - The server company and actor must equal the request context.
  - The `SaveDraft` action is checked before anything is read and again after every owner await, replay included.
  - The amount is shown only with `ReadAmount`; otherwise it is `Redacted` or `Unavailable`, computed fresh, also on replay.
- **Owners.**
  - Bank account, party account and the Settings money profile go through the reviewed ports and `PreparationValidator`, including the profile's scale via `ExactPositiveDecimalV1`.
  - An unavailable owner fails closed with its own code; nothing is stored.
- **Idempotency (ADR-011, since FIN-T04b).**
  - Key = (company, command name, key hash). The raw key is never stored: the record keeps the trimmed key's upper-case SHA-256 (§2). The key must have no surrounding whitespace.
  - The same key, the same canonical payload (SHA-256 of the contract JSON) and the same actor answer the stored result as a replay, with the first correlation (§4).
  - Another payload or **another actor** gets `IdempotencyConflict`, and nothing of the earlier result is disclosed (§3).
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
1. **FS0 bank account source (Finance).** Built (see below). With FIN-T04b it is the host's reader once the central writer wires `AddFinanceServices`. Its currency needs Settings' currency candidates (T02-b, #317).
2. **Settings' approved money profile data** for `finance.manual-bank-receipt.v1`. The operation key itself is on main; mapping money operations is the backend owners' technical work (Business #254 / 5654539215). What is missing is Settings' approved profile, which is Settings' work (T02-b, central writer). Until it lands, Finance's money answer stays Unavailable.
3. **Finance permission registration** (central writer) and **the grant** (human).
4. **Host wiring** (central writer): `AddFinancePersistence` (with Audit's interceptor), `AddFinanceServices` and `InitializeFinancePersistenceAsync` in `Program.cs`, the reviewed-inventory registration, and the OpenAPI document. The endpoint code itself is FIN-T04b's (below).
5. **An approved bank account** needs the bank-account master's HTTP and screen: the next card.

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
- **Idempotency.** The shared `Commands` record, keyed as ADR-011 by (company, command name, key
  hash) since FIN-T04b. The payload hash covers the command name and the request JSON. The same
  key, payload and actor replays; another payload or another actor gets `IdempotencyConflict`. The
  same key on another command is that command's own attempt.
- **Concurrency.** Approve names the expected version. Two racing approvals store one; the other
  gets `VersionConflict`.
- **Transaction.** One save covers the account, the command record and the audit event. The
  approval's audit event carries the approval context (proposer and approver).
- **Storage limits** (refused with `finance_value_does_not_fit`, never cut; not a policy): display
  name 200 characters.

## FIN-T04b: the draft over HTTP (card approved on #66)

| Route | Service | Answer |
|---|---|---|
| `POST /api/v1/finance/manual-receipts/drafts` | `DraftSaveAsync` (#258 `DraftSaveRequest`: create with null id and version, update with both, full replacement) | the #258 `CommandResult` for 200 Succeeded, 422 ValidationFailed (its issues), 409 VersionConflict / IdempotencyConflict; Denied and Unavailable are the platform problems 403 `forbidden` and 503 `service_unavailable` |
| `GET /api/v1/finance/manual-receipts?cursor=` | `ListAuthorizedAsync` | `SourcePage`; a malformed or foreign cursor is 400 |
| `GET /api/v1/finance/manual-receipts/{sourceId}` | `DetailAuthorizedAsync` | `SourceDetail`; another company's draft is 404 |
| `GET /api/v1/finance/bank-accounts` | FS0 `ListAuthorizedAsync` | `BankAccountPage` |

- **Company, actor, correlation** come only from the server context (`X-Company-Id` plus access, the authenticated actor, `X-Correlation-Id`).
- **Idempotency-Key** is required on the save: exactly one canonical value of at most 128 characters, otherwise 400. A replay carries `Idempotency-Replayed: true` and `Idempotency-Original-Correlation-Id`. What replays is the stored success and, since FIN-UI-1, the recorded terminal refusals (see below).
- **Enums** travel as their names (`CommandOutcome`, `SourceStage`, `AmountDisclosure`, `AllocationKnowledge` carry a string-enum converter, so the generated OpenAPI says so too).
- **Permission** is not a route policy. The services ask `FinancePermissionPort` afresh at every fence, through `IErpAuthorityRevalidationService` (#276), and only a granted company-wide scope is `Allowed`. A name not yet registered centrally is `Unavailable` (503), not a refusal.

  | Action | Name |
  |---|---|
  | `SaveDraft` | `finance.manual-receipt.draft.save` |
  | `ReadSources` | `finance.manual-receipt.draft.read` |
  | `ReadAmount` | `finance.manual-receipt.amount.read` |
  | `ReadBankAccounts` | `finance.bank-account.read` (FS0) |

  Record's name is `finance.manual-receipt.record` since FIN-T05a. FIN-T06 uses the separate
  `finance.manual-receipt.reverse.propose` and `finance.manual-receipt.reverse.approve` names; legacy direct Reverse
  remains unavailable. Names are declarations, not grants.

  Registering it was **not behaviour-neutral**, and the change is visible to the caller: Record moved from the port's
  "name unknown → `Unavailable`" branch, which asks the authority nothing and answers 503, to "name known → measured",
  which asks and — for an actor who does not hold the grant — answers `Denied`, so 403. Both refuse and neither stores
  anything; the second is the honest one once the capability exists, because the person simply does not have it. A name
  is still not a grant: nobody holds it until a human assigns it.
- **Settings' `settings_money_profile_not_found`** (the chosen currency has no approved profile) is a 422 validation result of the save (`money.currencyId`), not an outage. The draft-context read keeps it as 503 with its code: there the currency is the bank account's, not the request's.
- **List:** newest created first (`CreatedAtUtc desc, Id desc`, new index), so an edit never moves a draft between pages. The page size is a fixed technical 50, not a policy, and each amount is disclosed only with a fresh `ReadAmount`.
- **Honest runtime:** until T02-b is on main and a real bank account is approved, a save answers 503 or 422 with its owner's code. It is never painted green.

## FIN-UI-1a: the bank-account master over HTTP, terminal replay and the race (card approved on #66)

| Route | Service | Answer |
|---|---|---|
| `POST /api/v1/finance/bank-accounts` | `ProposeAsync` (`BankAccountProposeRequest`) | `BankAccountCommandResult` with the save's mapping (200 / 422 / 409; 403 and 503 as platform problems) |
| `POST /api/v1/finance/bank-accounts/{accountId}/approval` | `ApproveAsync` (body `BankAccountApprovalBody`: `expectedVersion`) | the same; the proposer's own approval is 403 (Ö4) |
| `GET /api/v1/finance/bank-accounts/master` | `ListMasterAsync` (read authority) | `BankAccountMasterPage`: each account with `isOwnProposal`, so F-04 offers the approval only to someone else |
| `GET /api/v1/finance/bank-accounts/currency-candidates` | `CurrencyCandidatesAsync` (propose authority) | Settings' candidates as given; empty means none, an unusable source is 503, never a default |

- **Visibility (least privilege, coordinator's technical default):** a pending proposal is shown only to a caller who may propose or approve (a fresh grant). A read-only caller sees approved accounts only, in `GET /bank-accounts`, in the master list and by id; the draft save treats a hidden proposal as `bank_account_ineligible`. Business may decide otherwise in a later slice.
- **Audit (ADR-009):** a proposal is audited with the roles that granted `finance.bank-account.propose`, an approval with those of `finance.bank-account.approve` (`FinanceBankAccountEnvelopes`).
- **ADR-011 §7:** an authorized terminal refusal is the key's result for good: for the save, `draft_not_found` (422) and a stale `expectedVersion` (409); for the approval, `bank_account_not_found` (422) and a stale version (409). It is recorded alone (no business change, no audit event) and replayed with its first correlation even after the target moved on. A refusal whose target does not exist has none: `CK_Commands_Target` admits a target-less record only when its stored outcome is `ValidationFailed` (migration `RecordTerminalCommandResults`, whose `Down` restores the stricter rule and fails visibly if such a row exists). Refusals on the merits (an owner's answer, a currency outside the candidates) are not terminal and are not recorded.
- **ADR-011 §8:** a save that loses a race (a unique key or the row version) keeps nothing tracked and reads the key afresh: if another caller of the same key won, its record is this answer too, as a replay; only a race that left the key empty is `concurrent_change` (409).
- **Shell (provider 1.1, central authority pending):** `Shell/FinanceShellCapabilityProvider` declares nine published capabilities: six routes plus the F-04 approval, F-02 Record and F-02 reversal-proposal controls. Reversal approval has its own checker route. A declaration grants nothing; each action measures its narrow Finance permission, and Record additionally requires Accounting period read. The shared-shell owner binds the reviewed authority revision and its hashes centrally before the normal host can serve this provider set.

## FIN-T05: Record of a realized manual bank receipt (card #66 5743678389)

> **Which date decides the period: `occurredOn`** — Sinan's K1 (#66 `5748258299`, 20 September 2026, closing
> `5745134466`). `valueOn` is the value date and does not move the period; `recordedAtUtc` is only the server's audit
> moment, so a back-dated event recorded today stays in its own period. A closed period goes to controlled correction:
> no silent date change, no automatic reopening.
>
> The mapping lives in ONE host class (`OccurredOnFinanceRecordPeriodRule`), not in this module and not in Finance's
> shared contract — deliberately, so the decision stays this source's date mapping instead of becoming a rule every
> accounting source inherits. The host also composes `AccountingPeriodStatusPort`, which reads the period covering a
> date under the CALLER's own `accounting.periods.read`. A persona that records definitive entries therefore needs
> that name; who holds it is a human grant decision (#66 `5748296481`).
>
> **Two refusals that must not be collapsed:** a period that is closed, proposed or absent answers 422
> `finance_period_not_open` ("decided, and the answer is no"); a rule that could not decide — none registered, the
> grant missing, or the context changed mid-read — answers 422 `finance_period_rule_unavailable` ("could not decide").
> Both are definite refusals, not an outage 503 that a screen would treat as an unknown outcome to resend, and neither
> is the idempotency key's answer.
>
> **Integration boundary:** provider contract 1.1 declares the Record and FIN-T06 controls, but the authority revision
> is central. Until the shared-shell owner binds the reviewed 1.1 revision, startup remains fail-closed. FIN-T05's
> number format is still unanswered, so the source keeps showing its own labelled reference.

- **Command:** `POST /api/v1/finance/manual-receipts/{sourceId}/record`, body the #258 `RecordRequest` (`draftId` must be
  the route's `sourceId`, else 400), `Idempotency-Key`; answered like the save (200 / 422 / 409; 403 / 503 code-less).
- **Authority (Ö4):** the narrow `Record` name, measured fresh before anything is read, after every owner read, after the
  period rule and right before the one save; no second approval, no amount threshold. The grant is human.
- **At record time** the owners are read again: the bank account (approved, usable, same currency), the party account
  (active) and the Settings money profile (in effect, the same profile, the stored amount within its scale — Ö2, never
  rounded). Any refusal stores nothing.
- **One money event, exactly once:** in ONE save the source turns `Recorded` (its version moves once; who and when are
  kept), ONE `finance.MoneyEvents` row, the ADR-011 command record and the audit event
  (`finance.manual-receipt.recorded.v1`, with the Record name's roles). The event is unique per (company, source, kind),
  append-only, bound to the company's source; its facts keep Ö5's dates apart (`OccurredOn`, `ValueOn`, `RecordedAtUtc`).
  The bank inflow and the customer credit are READS of this one row, never two rows; a distribution is not a money event.
- **ADR-011:** its own command name (`finance.manual-receipt.record`). Terminal (§7): a missing source (422
  `draft_not_found`) and a stale version (409). NOT terminal: a recorded source (`source_not_draft`), owner refusals and
  the period rule, so a corrected retry is measured again. §8: a lost race (version or unique index) reads the key.
- **Unallocated:** a recorded source has no allocation relation yet (FS3), so it is `Known` = the source amount − 0.
- **A recorded source never changes again:** the save refuses it (`source_not_draft`) and the write guard refuses any edit;
  its correction is the reversal (FIN-T06, maker≠checker, Ö4).
- **Migration** `20260920090000_AddMoneyEvents`: the stage and record trace on `ReceiptDrafts` (`CK_ReceiptDrafts_Stage`)
  and `MoneyEvents`. Down refuses with `THROW 51051` (Finance's module number, coordinator-assigned) while any money event
  or recorded source exists: a forward fix.
- **Screen:** the F-02 detail offers Record only when the shell returns `finance.receipts.record`; provider 1.1 declares
  it, while the shared authority binding and grants remain separate gates.
- **Not here:** no allocation, no journal (ACC-V1 takes the recorded source later), no official document (Ö8), no balance
  or statement read (FIN-T05b), no legacy direct reversal, no number series (Q2: the source shows its id). FIN-T06's
  governed proposal/checker flow is separate from direct reversal.

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
- `tests/GoymenERP.Api.IntegrationTests/FinanceRecordSqlServerRelationalAcceptanceTests.cs` (FIN-T05a) runs the Record on real
  SQL Server: the product's undecided rule refuses and stores nothing; with a synthetic open-period TEST INPUT (not a
  policy) one money event, every answer of its key, company isolation, a race of two records (one event) and the
  51051 Down refusal.
- None of this is API or browser evidence.
