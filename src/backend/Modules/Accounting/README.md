# Accounting module (ACC-1 domain, ACC-3 persistence)

Plan #275 §7.6, R4, R6, §13; Ö2, Ö4, Ö5; ADR-002, ADR-009.
- The domain slice (ACC-1) is in memory.
- ACC-3 adds the persistent store in the `accounting` schema.
- There is still no HTTP, host wiring, permission or UI surface, so no user can post a real
  journal yet.

## Identities

The ledger keeps these keys apart. None of them stands in for another.

| Identity | Key | Repeat answer |
|---|---|---|
| Delivery (inbox) | (consumer, messageId), per company ledger | `DuplicateMessage` plus what the first delivery recorded: its journal, queue entry or pending entry |
| Original posting | (company, sourceId, postingPurpose) | `AlreadyPosted` plus the existing original, whatever the message id or rule version |
| Reversal | reversed journal id (at most one reversal per journal) | `AlreadyReversed` plus the existing reversal |
| Correction | **correctionId** (a Guid the producer gives the correction decision) | same correctionId and same corrected journal: `AlreadyPosted` plus the existing correction journal |

About the correction key:
- `correctionId` names one correction decision. It does not name the delivery, the corrected
  journal or the amount.
- A redelivery of the same decision under a new message id posts nothing more.
- A different, legitimate correction of the same journal carries its own `correctionId` and posts.
- Reusing a `correctionId` for a different journal is refused with
  `accounting_correction_identity_conflict`.
- The key is scoped to the company ledger.

About identity texts:
- consumer, messageId and postingPurpose are compared ordinally and exactly.
- Leading or trailing whitespace is refused, not trimmed.

About failed postings:
- A failed posting records neither the effect nor the inbox marker. This covers an unbalanced
  journal, a closed period, a company mismatch and a correction identity conflict.

## Exception queue and pending valuation

- A missing account mapping goes to the exception queue, never to a guessed account.
- An unknown amount stays pending, never a zero.
- Each queue keeps one entry per (source, purpose), updated by later deliveries of the same fact.
- A later delivery clears an entry it proves stale:
  - A fully mapped delivery clears the mapping exception.
  - A fully valued delivery clears the pending valuation.
  - A posting clears both.

About re-drive:
- The inbox dedups on the delivery. After the mapping or value is fixed, the fact is posted by a
  new delivery with a new message id. Replaying the same message only returns the recorded
  queue entry.
- The re-drive producer (who re-sends and when) is not decided in this slice. It belongs to the
  integration or outbox work.

## Dates and period

- Transaction, accounting and value dates are separate. The accounting date decides the period.
- The recording instant must be UTC. Unset dates are refused.
- `Close(closedByUserId, closedAtUtc)` records who closed the period and when.
- A second close is refused with `accounting_period_already_closed`.
- Checking the authority to close belongs to the caller. No reopen is modelled, because reopening
  is an approval workflow (Ö4).
- The caller resolves which period applies. Rejecting overlapping periods of one company belongs
  to the future period store, not this aggregate. (INFO)

## dependency_wait

**Amount scale.** Ö2 settles a 2-decimal final amount for TL/USD/EUR. The journal still compares
debit and credit exactly and applies no scale or rounding, because:
- a journal carries only an opaque `CurrencyId`;
- the effective per-currency money profile is Settings-owned approved data, which is not yet
  supplied (see `Modules/Settings/README.md`: "do not approve … currency scale 2");
- hardcoding 2 here would be a second source of the scale.

When the Settings money profile is available, posting validates each amount against that
profile's scale and refuses excess digits without rounding.

**Chart of accounts and mappings (T22).**
- An account is still an opaque (company, id) identity with no foreign key, because no chart
  exists yet.
- A missing mapping stays an exception-queue entry; nothing is posted to a guessed account.

**Not here.**
- Host DI and wiring, HTTP, permissions, UI.
- Posting rules, FX, OPEN-009 permissions.

## Persistence (ACC-3)

`GoymenERP.Modules.Accounting.Infrastructure`: `AccountingDbContext`, the `InitialAccounting`
migration and `AccountingStore`.

`GoymenERP.Modules.Accounting.Application`: the store contracts (`IAccountingPeriodStore`,
`IAccountingPostingStore`) and the store codes.

**Tables** (all tenant rows, filtered by the server-resolved company and write-guarded):

| Table | Key | Notes |
|---|---|---|
| `PeriodCalendars` | CompanyId | Version bumped by every period opening, so overlapping openings cannot both commit |
| `Periods` | Id, AK (CompanyId, Id) | Only the close trace and the version change; posting and close both bump the version |
| `Journals` | Id, AK (CompanyId, Id) | Append-only; FK to the period and to the linked journal by (company, id) |
| `JournalLines` | (CompanyId, JournalId, LineNumber) | Append-only; one positive side per line (check constraint) |
| `Deliveries` | **(CompanyId, Consumer, MessageId)** | R4 inbox; keeps the first answer for replay |
| `PostingExceptions` | (CompanyId, SourceId, PostingPurpose) | Open or resolved; never deleted |
| `PendingValuations` | (CompanyId, SourceId, PostingPurpose) | Open or resolved; never deleted |

**Persistent identities** (unique, company-scoped, held under a race):
- `UX_Journals_Original`: (company, source, purpose) where Kind = original.
- `UX_Journals_Reversal`: (company, linked journal) where Kind = reversal.
- `UX_Journals_Correction`: (company, correction id).
- `PK_Deliveries`: (company, consumer, message id).

**How a write works:**
- One call is one save. The journal and its lines, the queue entries, the delivery, the period
  version and the audit events commit together or not at all.
- The store rebuilds only the stored facts the request needs (`SourcePostingLedger.FromStored`,
  `Journal.Restore`, `AccountingPeriod.Restore`) and lets the domain decide. The outcomes are
  the domain's.

**Races:**
- A writer that loses a unique identity or a version race gets
  `accounting_concurrent_change`, and nothing of it is stored.
- Retrying the same request answers from what the winner stored, for example `AlreadyPosted`.

**Period guard (Ö5):**
- Posting only into an open period that covers the accounting date.
- Every posting bumps its period's version, and so does the close. A close racing a posting
  cannot both commit.
- Trade-off: postings into the same period also serialise on this version. The loser gets
  `accounting_concurrent_change` and retries.

**Identity texts:**
- posting purpose, consumer and message id use `Latin1_General_100_BIN2`.
- SQL Server's `=` ignores trailing spaces even under a binary collation. The store therefore
  refuses non-canonical text (surrounding whitespace) BEFORE any lookup, so "m-1 " can never be
  answered as the replay of "m-1".

**Amounts:**
- Stored as `decimal(38,18)`. That is a storage limit, not a money policy.
- A value that does not fit is refused with `accounting_value_does_not_fit`, never rounded.

**Audit (ADR-009):**
- One audit event per stored business effect: period opened or closed, journal posted,
  exception queued, updated or resolved, pending valuation queued, updated or resolved.
- A replayed message and a delivery that only answers `AlreadyPosted` or `AlreadyReversed`
  store their delivery record and no audit event.

**Central, not in this module (CARİ):**
- the reviewed-inventory registration of this DbContext and its migrate / ensure-created branch;
- the module dependency allowlist entry Accounting.Infrastructure → Audit.Infrastructure
  (ADR-009);
- the integration-test project reference;
- the SQL verifier's broad counts.

## Validation

- `tests/GoymenERP.Accounting.UnitTests` holds synthetic unit and persistence-model tests: the
  schema, filters and write guard without a database, and the snapshot matching the model.
- `tests/GoymenERP.Api.IntegrationTests/AccountingSqlServerRelationalAcceptanceTests.cs` holds the
  real SQL Server evidence: durability, replay, company isolation, the unique indexes without the
  store, a one-original race, a close-versus-posting race and rollback.
- None of this is API or browser evidence.
