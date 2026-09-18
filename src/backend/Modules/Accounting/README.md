# Accounting module (ACC-1 + ACC-2 domain slice)

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
- Reusing a `correctionId` with different content is refused with
  `accounting_correction_content_conflict` (ACC-2, SİPARİŞ #283 P3). Content means:
  - the lines, compared as a set of (account, debit, credit), so line order does not matter;
  - the transaction, accounting and value dates.
- These are delivery details, not content, so a redelivery may change them:
  - the recording instant;
  - the posting rule version;
  - the journal id a redelivery proposes.
  They do not turn one correction decision into two.
- The key is scoped to the company ledger.

About the original key and content:
- An original is keyed by the business fact, not by its content. A later delivery of the same
  fact answers `AlreadyPosted` with the first journal even when its lines differ, for example
  under a newer posting rule version (R4: no second original).
- A different accounting effect for an already-posted fact is a correction or a reversal, each
  with its own identity.
- This is a deliberate asymmetry with corrections: a correction's content IS the decision, so a
  content mismatch under one `correctionId` is refused.

About identity texts:
- consumer, messageId and postingPurpose are compared ordinally and exactly.
- Leading or trailing whitespace is refused, not trimmed.

About failed postings:
- A failed posting records neither the effect nor the inbox marker. This covers an unbalanced
  journal, a closed period, a company mismatch, and a correction identity or content conflict.
  Neither queue changes either.

## Exception queue and pending valuation

- A missing account mapping goes to the exception queue, never to a guessed account.
- An unknown amount stays pending, never a zero.
- Each queue keeps one entry per (source, purpose). Each entry reflects the LATEST delivery of the
  fact:
  - an exception entry while some line has no mapping;
  - a pending entry while some amount is unknown.
- A delivery with both gaps appears in both queues. Its pending count and rule version are
  refreshed too (ACC-2, CARİ #283 INFO).
- The first message that raised an entry stays recorded.
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
- the posting purpose, correction and inbox keys must use a case-sensitive, accent-sensitive
  collation, or a binary comparison, to match the ordinal rule;
- the inbox key is persisted with the company: (CompanyId, consumer, messageId). In memory the
  ledger is already one company's (ACC-2, CARİ #283 INFO);
- this slice has no migration.

Also not covered here: chart of accounts (T22), posting rules, FX, OPEN-009 permissions.

## Rule codes and read-only views (ACC-2)

- Every rule refusal is an `AccountingDomainException` with an `AccountingErrorCodes` code, and
  the tests assert the code.
- A null argument or a null line is an `ArgumentException`: a caller error, not a rule.
- `accounting_journal_already_reversed` was never raised and is removed. A repeated reversal
  answers the `AlreadyReversed` outcome.
- These views are read-only wrappers, and a cast cannot reach the stored data:
  - `Journal.Lines`;
  - `PostingException.MissingMappingKeys`;
  - the ledger's `Journals`, `OpenExceptions` and `PendingValuations`.
  Tests pin this.

## Validation

`tests/GoymenERP.Accounting.UnitTests` holds synthetic unit tests only. They are not SQL, API or
browser evidence.
