# Selectable browser acceptance (BROWSER-CI)

Workflow: `.github/workflows/browser-acceptance.yml`. Source: Business/Sinan, #66 5731422079.

It runs **one** browser suite against the **exact head** of **one** same-repo pull request, on the C-01 disposable stack: SQL Server + API + the authorized (`group-bootstrap-admin`) and unauthorized (`company-a-user`) web profiles built from that head, with the canonical `C01-E2E-GOLDEN-V1` fixture. Chromium is real, the API is real, and nothing is mocked. No secret, deployment, production system or host business database is used.

## How it is triggered (never on push, never on a PR update)

- **Dispatch** (after this workflow is on main): *Actions → Browser acceptance (selectable) → Run workflow*, choose `suite` and enter the PR number. The workflow resolves the PR's exact head sha from the API and tests that code under the workflow as it stands on main. Only same-repo branches are accepted; fork PRs are refused.
- **Label**: add `browser:<suite>` to a PR whose branch already carries this workflow. It runs once for the head the label was added on. To run again, remove the label and add it back.

The head under test must contain its base (main), the same rule as C-01. Otherwise the run fails with `BROWSER_BASE_NOT_MERGED`.

## Suite contract (for suite owners)

A suite is **one script** in `tests/e2e/package.json`: `browser:<suite>:ci`. It usually points at its own Playwright config. If the script is missing at the tested head, the run fails with `SUITE_NOT_READY` rather than skipping.

The config reads these variables and must have **no defaults**:

| Variable | Meaning |
|---|---|
| `BROWSER_E2E_AUTHORIZED_BASE_URL` | authorized web profile |
| `BROWSER_E2E_UNAUTHORIZED_BASE_URL` | unauthorized web profile |
| `BROWSER_E2E_API_BASE_URL` | disposable API |
| `BROWSER_E2E_ARTIFACTS_DIR` | output directory |
| `BROWSER_E2E_JUNIT_FILE` | the **one** JUnit file the workflow reads (`failure`/`error`/`skipped` = 0, at least one case) |
| `BROWSER_E2E_SQL_CONNECTION`, `BROWSER_E2E_DATABASE` | the disposable database (for suites that must observe or reset state) |
| `BROWSER_E2E_TESTED_COMMIT` | the exact head |

Retries must be 0, and traces, screenshots and video must be off (the C-01 privacy decision). Only the JUnit file, the fixture evidence, the post-browser SQL evidence and `compose-ps.txt` are uploaded, and only after `tools/validate_browser_acceptance.py privacy` passes. Retention is 7 days.

`smoke` and `c03` are declared **read-only**: after the suite, the fixture tool verifies that SQL still matches the fixture. Writing suites (`org03`, `u02`) check their own post-conditions.

| Suite | Owner | Status |
|---|---|---|
| `smoke` | CARİ | plumbing only: the stack, the fixture, a real list read and a neutral unauthorized block. **Not feature acceptance.** |
| `c03` | CARİ | spec lands with #267 |
| `org03` | ORGANİZASYON | owner adapts `tests/e2e/organization-locations` |
| `u02` | ÜRÜN | owner adapts `tests/e2e/product-u01-u02` |

Limits: `timeout-minutes: 45` for the browser job (5 for resolve), concurrency per suite and PR with `cancel-in-progress`. A budget or access problem shows up as a failed run; no plan or limit is changed.
