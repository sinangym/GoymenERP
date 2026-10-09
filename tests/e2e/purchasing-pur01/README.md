# PUR01 - Purchasing create/list/detail/approval acceptance

This suite proves the normal Purchasing user path against the disposable real stack:

- the gated fixture publishes one real Cari Supplier account and one Product purchase candidate;
- the creator saves exactly one order through P-04, closes the form, reopens the same record and the approver uses it in the next operation;
- the list and fresh API reads distinguish the fixture Draft from the UI-created `+1` order;
- a typed, gated Purchasing fixture writes one separate synthetic Draft order as the approval/replay precondition;
- the approver opens that record from the real list and sends a real approval;
- Playwright withholds only the first HTTP response after the API commits;
- the user closes the detail, reopens the same order from the list, and resends the same stored command;
- the API returns the idempotent replay and the screen reports it;
- a reader can list/read but cannot create or approve, and the other-company context cannot create or expose records/screens.

The goods-receipt journey (`pur01-goods-receipt-journey.spec.ts`, #478 PUR-HOST) proves the receiver path on the
same stack:

- one approved order is arranged through the creator's real create and the approver's real approval commands;
- the disposable receiver (`purchasing.orders.read` + `purchasing.goods-receipts.record`, the only actor holding
  the receipt name) opens it from the list and records ONE partial receipt at a location the server offers;
- the answer carries the acceptance, lot, label and movement ids the browser generated, the remaining quantity and
  the Free initial block; the user closes and reopens the order and a fresh read shows the same single receipt;
- a separate stock reader (`inventory.labels.read` + `inventory.balances.read`, no Purchasing name) opens the SAME
  label in Inventory: company owner and custodian, Free, no required document;
- the identical request under the same key replays with the original correlation and read-only SQL counts prove
  one acceptance, lot, label, receipt movement, movement line and Inventory command;
- the approver and the reader open the same order without the receipt control, and their direct calls are 403.

The document-vault journey (`pur01-documents-journey.spec.ts`, P-03) uses the same seeded Purchase Order:

- the creator role must publish `documents.files.read` and `documents.files.upload` only in the disposable PUR01
  override; the ordinary order reader deliberately has neither Documents capability;
- an empty order vault uploads one real PNG, loses only the first HTTP answer after commit, retries the same
  key/file/company and receives the exact replay;
- the user closes the order, reopens the same record, previews and downloads the stored image;
- the ordinary reader gets no panel, while foreign-company and unknown owners remain non-disclosing;
- read-only SQL proves exactly one document, version, owner link, upload attempt and audit event.

The suite adds no production grant, role, policy, local database, mock response, or alternate API. Fixture
rows are preconditions, not user acceptance; only the browser's real create/close/reopen/approve flow,
fresh reads, replay, and negative calls are acceptance evidence.

Inputs are fail-closed: `BROWSER_E2E_API_BASE_URL`, `BROWSER_E2E_TESTED_COMMIT`, and
`BROWSER_E2E_PUR01_SEED` must be supplied by the central browser workflow; the goods-receipt journey also reads
the workflow's `BROWSER_E2E_SQL_CONNECTION` for read-only effect counts. Run selection and disposable
identity/company bindings are owned by the central browser harness.
