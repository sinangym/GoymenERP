# Accounting module (ACC-1 + ACC-2 domain, ACC-3 persistence, ACC-V0 periods and reads)

Plan #275 §7.6, R4, R6, §13; Ö2, Ö4, Ö5; ADR-002, ADR-009, ADR-011.
- The domain slice (ACC-1) is in memory.
- ACC-3 adds the persistent store in the `accounting` schema.
- ACC-V0 adds the period lifecycle with Ö4 maker≠checker, the authorized HTTP surface for periods and
  for reading journals and queues, and the Ö2 scale check at posting. It is still not possible to
  create, approve or reverse a voucher (ACC-V1), so no user posts a real journal yet.

## Identities

The ledger keeps these keys apart. None of them stands in for another.

| Identity | Key | Repeat answer |
|---|---|---|
| Delivery (inbox) | (consumer, messageId), per company ledger | same content: `DuplicateMessage` plus what the first delivery recorded (its journal, queue entry or pending entry); other content: `accounting_delivery_content_conflict` (ACC-4) |
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
  - the lines, compared as a multiset of (account, debit, credit): line order does not matter,
    but a repeated line does;
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
- A message is immutable (ACC-4, ORG #298 P3-1). The store keeps a SHA-256 hash of each
  delivery's canonical content (`Deliveries.RequestHash`). The same (consumer, message id) with
  other content is refused with `accounting_delivery_content_conflict` and stores nothing; it is
  never silently given the first answer. Content means:
  - receive: source id, purpose, rule version, currency, the business dates, and the proposed
    lines (mapping key, account, side, amount) as a multiset;
  - reverse: the reversed journal and the business dates;
  - correct: the correction id, the corrected journal, rule version, business dates and lines
    as a multiset.
  - Not content: the journal id a delivery proposes and the recording instant, which a
    redelivery may change. Amounts compare by value, so 100 and 100.00 are the same.
  - The hash is required (`NOT NULL`, exactly 64 characters): there is no delivery without one,
    so no replay escapes the content check.
- The re-drive producer (who re-sends and when) is not decided in this slice. It belongs to the
  integration or outbox work.

## Dates and period

- Transaction, accounting and value dates are separate. The accounting date decides the period.
- The recording instant must be UTC. Unset dates are refused.
- **Opening is maker≠checker (Ö4, #66 5722637822: "dönem açma … hazırlayandan farklı tek yetkili
  onayında").** A period is born `Proposed` by its preparer (`AccountingPeriod.Propose`); it covers its
  dates but takes no posting (`accounting_period_not_open`). `Approve(approver, at)` by ONE other
  authorised person opens it; the preparer's own approval is refused (`accounting_period_self_approval`),
  and so is a second approval (`accounting_period_not_proposed`). There is no other way to an open period.
- `Close(closedByUserId, closedAtUtc)` closes an OPEN period and records who and when (Ö5: one authorised
  act; Ö4 does not list closing, so no second person). A proposal cannot be closed; a second close is
  refused with `accounting_period_already_closed`.
- Checking the authority to propose, approve or close belongs to the caller (the HTTP surface below).
- **Not modelled, by decision:** reopening (an approval workflow of its own), and withdrawing or
  rejecting an unapproved proposal. Ö4 names only the approval; the withdraw/reject question is
  NEEDS-BUSINESS #66 5742097485. Until it is answered, a wrong proposal keeps its dates.
- The period store refuses a period that overlaps another period of the company, whatever its status.
- **Answered for Finance (Sinan's K1, #66 5748258299, closing 5745134466):** a definitive manual bank receipt's
  period follows its transaction date `occurredOn`. Accounting does NOT hold that mapping: `IAccountingPeriodStatusPortV1`
  answers only "the period covering this date is open / not open / absent", and never learns which of a caller's dates
  was handed to it. The mapping is one host class, so no other accounting source inherits it. Accounting's own
  journals keep carrying their explicit accounting date; nothing here picks a date for another module.
- **Ö2 rule for the first HTTP amount input (FIN-T05 / ACC-V1):** the amount TEXT is validated lexically
  (Settings' `ExactPositiveDecimalV1`) before it is converted, because a `decimal` parse rounds past about
  28 significant digits. Today no Accounting route takes an amount; journal amounts arrive in-process as
  `decimal`, so the producing module owns that check.

## dependency_wait

**Amount scale (Ö2) — done in ACC-V0.** The journal still compares debit and credit exactly; the scale
is never hard-coded. Posting (receive and correct) checks every known amount against the company's
approved Settings money profile for the journal currency on its accounting date, under Accounting's own
operation key `accounting.journal-amount.v1` (`AccountingAmountScaleVerifier`):
- excess digits are refused (`accounting_amount_scale_exceeded`), never rounded; trailing zeros are not
  precision; nothing of the delivery is stored;
- a Settings refusal keeps Settings' own code (fail-closed): without the operation key on the approved
  profile, or for a company without an approved source, every posting is refused;
- a replayed delivery answers before the check, and an unknown amount (pending) is not checked.
The operation key on the GDC/TRY profile is a central Settings change (CARİ), with Ö2 as its source.

**Chart of accounts and mappings (T22).**
- An account is still an opaque (company, id) identity with no foreign key, because no chart
  exists yet.
- A missing mapping stays an exception-queue entry; nothing is posted to a guessed account.

**Not here.**
- Voucher create / approve / reverse (ACC-V1): waits on NEEDS-BUSINESS 5740501573 (chart of accounts),
  5740501655 (voucher approval) and OPEN-009.
- Posting rules, FX, OPEN-009 permissions.

## ACC-V0: periods over HTTP, journal and queue reads

`AccountingApiService` (Application) and `AccountingEndpoints` (API; the host mapping is central):

| Operation | Permission (narrow, none granted) | Notes |
|---|---|---|
| `GET /api/v1/accounting/periods`, `GET …/periods/{id}` | `accounting.periods.read` | newest first; `isOwnProposal` per caller |
| `POST /api/v1/accounting/periods` | `accounting.periods.propose` | `{periodId, startsOn, endsOn}` → 201 Proposed |
| `POST …/periods/{id}/approval` | `accounting.periods.approve` | `{expectedVersion}`; the preparer gets 403 whatever they hold (Ö4) |
| `POST …/periods/{id}/close` | `accounting.periods.close` | `{expectedVersion}`; one authorised act (Ö5) |
| `GET /api/v1/accounting/journals`, `GET …/journals/{id}` | `accounting.journals.read` | M-04 read-only: source link, lines, balanced totals |
| `GET /api/v1/accounting/posting-queues` | `accounting.posting-queues.read` | M-01 read-only: missing mappings and pending amounts |

- Each read is its own name (Sinan's persona rule, #66 5738113623 §4): a period reader reads no journal,
  a journal reader no queue.
- **ADR-011:** each command is one attempt per (company, command, SHA-256 of the key), in
  `accounting.Commands`. The same key, request and actor answers the stored result with
  `Idempotency-Replayed` and `Idempotency-Original-Correlation-Id`; another request or actor is 409
  `idempotency_conflict`. A success is recorded in the same save as the period change and its audit
  event; an unknown period (404) and a stale version (409 `accounting_version_conflict`) are recorded as
  the key's terminal answers; two attempts racing on one key leave one record and the loser answers from it
  (real SQL: the same approval sent twice at once is one approval and one replay).
- **§7, decided for Accounting (SİPARİŞ #354 P3-2): the 422 state refusals are NOT terminal.** A proposal
  that is no longer proposed, a period that is not open or already closed, an invalid period and an overlap
  (409) answer this attempt only; nothing is recorded under the key, so the same key retried after the state
  is corrected is measured again. Only a success, an unknown period (404) and a stale version (409
  `accounting_version_conflict`) are the key's answer for good: those are facts of the request itself, the
  others facts of a state that can change. ADR-011 §7 leaves this choice to each module; this is Accounting's.
- **#276:** the permission is measured fresh before any stored answer is read, and AGAIN after that lookup,
  right before the one write (the store reads the period and saves in that call; Manufacturing's `FenceAsync`
  shape, SİPARİŞ #354 P3-1). A grant lost in between stores nothing (`AccountingApiFenceTests`). Only a
  company-wide grant writes, and its role codes go into the audit envelope. The approval's audit event carries
  the O4 context (who prepared, who approved).
- **403 shape:** a denial by this service (a missing or site-scoped grant, the Ö4 preparer) is 403 with
  `code: "forbidden"` (the Finance bank-account precedent); the platform's own policy 403 before the handler
  stays code-less. Both are the same refusal to the client, and neither discloses anything.
- Company: only the server-resolved context; another company's period or journal is a code-less 404.
- Migration `AddPeriodApproval`: the preparer and approval facts (`CK_Periods_Status` holds approver ≠
  preparer in the schema too) and `Commands`. Up fails visibly on a table that already holds a period
  (no environment does: Accounting was never host-wired). Down refuses with `THROW 51048` (Accounting's
  module number) while any period or command record exists: a forward fix.

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
| `Deliveries` | **(CompanyId, Consumer, MessageId)** | R4 inbox; keeps the first answer and the content hash for replay |
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
- Two overlapping period openings racing each other, with or without a calendar row yet: one
  commits, the other gets `accounting_concurrent_change`, and its retry gets
  `accounting_period_overlap` (ACC-4 SQL evidence).

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

## B-04a: posting account mapping catalog (backend only)

Sinan #66 6707619685: invoice finalization makes a DRAFT voucher whose accounts are mapped in Settings
("Ayarlar") per product type, never hard-coded. This slice stores that mapping in Accounting; the Ayarlar
screen and the voucher draft (B-04b) come later. #66 6707864938 says how a company fills it (150/153 by the
purchase line purpose, service purchase → `expense`, scrap sale → `revenue` with `ItemType=Scrap`, 601 by
`IsExport`, 391/191 optionally per `VatRateCode`). Those are data rows of a version, not code.

- **Version**: (CompanyId, MappingVersion), `EffectiveFrom` (business date), Prepared (1) → Active (2) →
  Superseded (3). Rows are immutable. Each row has a purpose (`sales-invoice` | `purchase-invoice`) and a key
  (`receivable`, `revenue`, `vat-output`, `withholding-receivable` — its account is open, NEEDS-SINAN #66 6707860683 | `payable`, `inventory`, `vat-input`, `withholding-payable`,
  `expense`). It may also name optional selectors (`ItemType`, `LinePurpose`, `IsExport`, `VatRateCode`, the
  last only on a VAT key) and an `AccountId` that must be in the ACTIVE approved account plan (its code is
  frozen on the row).
- **Resolution** (`IAccountingPostingAccountMappingResolverV1`, active version only): the matching row with
  the most named selectors wins. Equally specific matches are `Ambiguous` and no match is `Unmapped`; neither
  falls back to an account. A date before `EffectiveFrom`, a missing active version or plan, or an account
  no longer in the active plan is `Unavailable`.
- **Commands**: `POST /api/v1/accounting/posting-account-mappings` (prepare, `expectedLatestMappingVersion`),
  `POST …/{mappingVersion}/activation` (`expectedVersion`, `expectedActiveMappingVersion`; the preparer is
  refused, 403 `accounting_posting_mapping_self_activation`, recorded), `GET …/active`,
  `GET …/{mappingVersion}`. Permissions `accounting.posting-mappings.read|prepare|activate` are registered,
  not granted.
- **Database**: `PostingAccountMappingVersions` (filtered unique `UX_PostingAccountMappingVersions_Active`
  = one active version per company; `CK_…_MakerChecker`) and `PostingAccountMappingRows` (unfiltered unique
  selector index: NULL selectors compare equal; key-per-purpose and VAT-rate checks).
- **Migration** `20261107140038_AddPostingAccountMappings`: Down refuses with **THROW 51212** while any mapping
  version exists (forward fix); an empty store rolls back freely.

### Not decided yet (B-04a, `OPEN-*` — behaviour is NOT chosen here)

- **`EffectiveFrom` semantics (OPEN-*)**: a future-dated `EffectiveFrom` makes every date `Unavailable` as soon as it
  supersedes the previous version (there is no per-date version history); a past-dated `EffectiveFrom` silently
  re-resolves past invoices against the new version. No fallback to the superseded version and no date-range
  history exists; the business rule is open and nothing here picks it.
- **Resolver granularity (B-04b)**: the resolver port answers one question per call and loads the account plan on
  every call. Lines of the same voucher draft can resolve from different versions if a version is activated in the
  middle. Batch / version-pinned resolution is B-04b's job, not part of this slice.
- **Known, not a defect here**: an export scrap sale resolves
  `Ambiguous` until a business rule names the winner; the resolver never guesses.

## B-04b: source document acceptance port (backend only, no HTTP)

Sinan #66 6707619685 and the 7 Kasım kararı (KLN-D6…D19, SAT-D208/D223/D226): a FINALISED sales invoice becomes ONE
DRAFT voucher in Accounting's own tables. The port is `IAccountingSourceDocumentAdmissionPortV1`
(`Application/Contracts`); Accounting writes only `SourceDocumentAdmissions`, `SourceDocumentVoucherDrafts` and
`SourceDocumentVoucherDraftLines`, never the producer's tables. No permission is registered and no grant is made.

- **Call (Sales host adapter, AFTER the finalisation commit)**:

  ```csharp
  var answer = await port.AdmitAsync(new AccountingSourceDocumentAdmissionRequestV1(
      CompanyId: companyId, ActorId: actorId, CorrelationId: correlationId, CausationId: null,
      IdempotencyKey: AccountingSourceDocumentKindsV1.SalesInvoiceKey(invoiceId, finalizedVersion),
      DocumentKind: AccountingSourceDocumentKindsV1.SalesInvoice, SourceDocumentId: sourceDocumentId,
      InvoiceId: invoiceId, FinalizedVersion: finalizedVersion, SourceActorId: finalizedByUserId,
      PartyId: partyId, CurrencyId: currencyId, ExchangeRate: null /* TL; else the single fixed rate text */,
      DocumentDate: invoiceDate, DueDate: dueDate,
      DocumentTotals: new("1200", "240", "0", "1440"), FunctionalTotals: new("1200", "240", "0", "1440"),
      Lines: lines /* functional-currency net/VAT/withholding per line, VatRateCode per group */), ct);
  ```

  Key: `sales-invoice-accounting:{invoiceId:D}:{finalizedVersion}`. The same key with other content is refused
  (`idempotency_conflict`), never replayed.
- **Answers**: `Admitted` (voucher draft id; a replay says `IsReplay`) · `Refused` + reason ≤ 64 chars +
  `Retryable` · `Unavailable` (no authority measurement, failed read, lost race: nothing is known; re-drive the same
  key). A missing producer port or `Unavailable` leaves the invoice "waiting for Accounting", re-drivable.
- **Fail-closed mapping ("eşleme bekliyor")**: a missing, inactive, not-yet-effective or ambiguous account mapping
  is a retryable `Refused` (`mapping_pending` | `mapping_ambiguous`), recorded as a `Waiting` row (the attempt is
  counted); the same key is admitted once Accounting has configured the mapping. No account is guessed.
- **Content refusals** (`totals_mismatch`, `withholding_exceeds_vat`, `amount_invalid`, `exchange_rate_invalid`,
  `line_invalid`, `document_invalid`, `voucher_unbalanced`, `mapping_query_invalid`) are recorded terminal refusals
  and replay for the key. Malformed identity (`request_invalid`, `document_kind_unsupported`,
  `idempotency_key_invalid`) is refused unrecorded.
- **Voucher** (no recomputation, no threshold; Accounting uses the producer's amounts): debit `receivable` =
  net + VAT − withholding; credit `revenue` per invoice line = net; credit `vat-output` per `VatRateCode` group =
  Σ(VAT − withholding). Zero lines are skipped and the voucher must balance. The 12.000 TL tevkifat threshold is
  the producer's configuration. `PeriodId` is stamped only if an Open period covers the date, else null
  ("dönem bekliyor"); the draft is never refused for a closed or missing period (KLN-D15).
- **Authority** is host-injected (`IAccountingSourceDocumentAdmissionAuthorityV1`, the Finance pattern); the default is
  `UnavailableAccountingSourceDocumentAdmissionAuthorityV1`, so an unwired host answers `Unavailable`.
- **Database**: the admission row is the attempt record and the terminal refusal record
  (`UX_SourceDocumentAdmissions_Key` / `_Document` unique per company; draft and lines immutable).
- **Migration** `20261108222146_AddSourceDocumentAdmissions`: Down refuses with **THROW 51240** while any admission
  or draft exists (forward fix). 51234 is taken by an open PR.

### Not decided yet (B-04b)

- NEEDS-SINAN: the sales-side withholding counter-account (#66 6707860683): no withholding line is written.
- NEEDS-SINAN: the reversing voucher's period and the FX-difference accounting; cancel/return
  (`sales-invoice-cancel:…`, KLN-D16) and the purchase kind are not in V1 (`document_kind_unsupported`).
- NEEDS-SINAN: which permission measures this port, and the host adapter (Sales/host lane).
- NEEDS-SINAN: "dönem bekliyor" is a null `PeriodId` in V1; whether it should be a stored queue flag is open.

## Rule codes and read-only views (ACC-2)

- Every rule refusal is an `AccountingDomainException` with an `AccountingErrorCodes` code, and
  the tests assert the code.
- A null argument or a null line is an `ArgumentException`: a caller error, not a rule. A posting
  request with a null source, dates or lines is refused this way before the company check, in
  the domain and in the store (ACC-4).
- `accounting_journal_already_reversed` was never raised and is removed. A repeated reversal
  answers the `AlreadyReversed` outcome.
- These views are read-only wrappers, and a cast cannot reach the stored data:
  - `Journal.Lines`;
  - `PostingException.MissingMappingKeys`;
  - the ledger's `Journals`, `OpenExceptions` and `PendingValuations`.
  Tests pin this.

## Validation

- `tests/GoymenERP.Accounting.UnitTests` holds synthetic unit and persistence-model tests: the
  schema, filters and write guard without a database, and the snapshot matching the model.
- `tests/GoymenERP.Api.IntegrationTests/AccountingSqlServerRelationalAcceptanceTests.cs` holds the
  real SQL Server evidence: durability, replay, company isolation, the unique indexes without the
  store, a one-original race, a close-versus-posting race, racing period proposals, a same-id
  different-content refusal for every operation (ACC-4) and rollback. Its ledger cases use a SYNTHETIC
  scale-2 money profile (they are about the ledger, not the Settings source).
- `tests/GoymenERP.Api.IntegrationTests/AccountingApiSqlServerRelationalAcceptanceTests.cs` (ACC-V0) is the
  real host over HTTP: Ö4/Ö5, every ADR-011 answer, isolation, the narrow reads, and the Ö2 check against
  Settings' REAL approved GDC/TRY profile. It is API and SQL evidence, not browser evidence.
