# C-01 real-browser acceptance suite

Runs `B-C01-001` … `B-C01-010` against the disposable compose stack that
`.github/workflows/c01-real-browser-e2e.yml` stands up, and emits the evidence
that `tools/validate_c01_e2e_contract.py` checks.

## Why this exists at all

The C-01 acceptance cases that matter most cannot be proved in jsdom. Measured,
not assumed: the app binds `fetch` at module load, so a page-level `fetch` patch
never reaches it, and every jsdom attempt at the stale-response cases ends in the
request being **aborted** rather than delivered. An abort is a safe outcome; it is
not evidence that a delivered stale response would be rejected, because nothing
was delivered. Only response-stage interception against the real server can show
a response arriving after the context has moved and being ignored.

## Design constraints that are not free choices

- **One Playwright project.** The evidence contract requires exactly ten JUnit
  `testcase` entries with unique `B-C01` ids. Running the viewport matrix as
  projects would emit five entries per case. The matrix is walked inside
  `B-C01-005` instead.
- **No retries.** An acceptance case that only passes on the second attempt has
  not shown the behaviour it claims. `retry-200` is a fault the suite injects on
  purpose, which is a different thing from a flaky test.
- **The fixture identity is read, never hardcoded.** `src/evidenceReporter.ts`
  composes the `fixture` block from `c01-e2e-fixture-evidence.json`, which the
  materializer wrote after querying the live database. A reporter that embedded
  the SHA-256 and row counts would emit evidence that agrees with the validator
  by construction and says nothing about what the browser actually read.
- **Redaction is structural.** A path reaches the network evidence only through
  `redactPath`, which returns `URL.pathname` and therefore cannot carry a query
  string or a cursor. The validator re-checks this; the guarantee is that no code
  path appends a query in the first place.

- **Artifact privacy.** Playwright traces, screenshots and video are OFF. A trace
  captures full request URLs and headers, and CI uploads artifacts — keeping them
  would re-publish, in a zip, exactly the cursor material `redactPath` exists to
  strip. A failing case is diagnosed by re-running it against the disposable
  stack, which is reproducible by construction.

## What each case actually exercises

Ten green JUnit names are not a claim of complete scenario parity with Issue #146.
This table is the honest scope, so the report cannot be read as more than it is.

| Case | Exercised | Not exercised here |
|---|---|---|
| `B-C01-001` | nav discoverability, 50 rows, first row identity, combined roles, GSP-only party absent, no raw UUID / integration code | column-by-column formatting |
| `B-C01-002` | unauthorized profile: no nav entry, deep link closes non-disclosingly, and **zero** counterparty requests attempted | per-capability matrix beyond `cari.parties.list` |
| `B-C01-003` | a real GDC response withheld, GSP committed and painted, then the stale response **delivered** and ignored | actor change (that is SAB, not C-01) |
| `B-C01-004` | `aria-sort` toggle, server-side re-sort proven by row identity, focus retained across re-render | full tab-order walk, focus-visible styling |
| `B-C01-005` | five viewports, no page-level horizontal scroll, `display: table` preserved, labelled scroll region | colour-contrast, zoom beyond 320px |
| `B-C01-006` | one `h1`, nav landmark, six `scope=col`, polite live region, `aria-busy=false`, no internal capability vocabulary | full axe ruleset |
| `B-C01-007` | page 2 appends `CAR-000051/052`, 52 rows, no duplicates, control removed when `hasMore` is false | concurrent double-click on the control |
| `B-C01-008` | 500, 503, abort and dropped-connection each preserve the verified rows; retry re-issues the same cursor; recovery appends exactly once | server-side retry semantics |
| `B-C01-009` | the application's own cursor corrupted in flight, real `400 invalid_cursor`, recovery to page 1 announced in the live region | cursor forgery across companies |
| `B-C01-010` | party state and account state as separate cells, globally inactive party hidden by the default filter | the full account-state filter matrix |

## Status

All ten pass against the disposable stack, and the emitted evidence passes
`tools/validate_c01_e2e_contract.py`: every required fault outcome observed,
`skipped=0`, and no query string or cursor in any recorded path.

`retry-200` is **observed**, not declared — the recorder writes it only when a
real 200 arrives for the retried read.
