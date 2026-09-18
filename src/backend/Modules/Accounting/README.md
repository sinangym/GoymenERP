# Accounting module (ACC-1 domain slice)

Plan #275 §7.6, R4, R6, §13; Ö2, Ö4, Ö5. This is an in-memory domain slice with no
persistence, HTTP, permission or UI surface. It cannot post a real journal.

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

**Persistence and concurrency.** The unique indexes behind the four keys, the real inbox table,
the transaction boundary and the collation are central storage work:
- the posting purpose and correction keys must use a case-sensitive, accent-sensitive collation,
  or a binary comparison, to match the ordinal rule;
- this slice has no migration.

Also not covered here: chart of accounts (T22), posting rules, FX, OPEN-009 permissions.

## Validation

`tests/GoymenERP.Accounting.UnitTests` holds synthetic unit tests only. They are not SQL, API or
browser evidence.
