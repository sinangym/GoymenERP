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

The suite adds no production grant, role, policy, local database, mock response, or alternate API. Fixture
rows are preconditions, not user acceptance; only the browser's real create/close/reopen/approve flow,
fresh reads, replay, and negative calls are acceptance evidence.

Inputs are fail-closed: `BROWSER_E2E_API_BASE_URL`, `BROWSER_E2E_TESTED_COMMIT`, and
`BROWSER_E2E_PUR01_SEED` must be supplied by the central browser workflow. Run selection and disposable
identity/company bindings are owned by the central browser harness.
