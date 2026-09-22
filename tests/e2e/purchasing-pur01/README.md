# PUR01 - Purchasing list/detail/approval acceptance

This suite proves the normal Purchasing user path against the disposable real stack:

- a typed, gated Purchasing fixture writes one synthetic Draft order as the precondition;
- the approver opens that record from the real list and sends a real approval;
- Playwright withholds only the first HTTP response after the API commits;
- the user closes the detail, reopens the same order from the list, and resends the same stored command;
- the API returns the idempotent replay and the screen reports it;
- a reader can list/read but cannot approve, and the other-company context cannot expose the record or screens.

The suite creates no purchase order through UI because Purchasing create is still staged as P04. It adds no
production grant, role, policy, local database, mock response, or alternate API. The fixture is not user
acceptance; only the browser's real approval/reopen/replay and negative calls are acceptance evidence.

Inputs are fail-closed: `BROWSER_E2E_API_BASE_URL`, `BROWSER_E2E_TESTED_COMMIT`, and
`BROWSER_E2E_PUR01_SEED` must be supplied by the central browser workflow. Run selection and disposable
identity/company bindings are owned by the central browser harness.
