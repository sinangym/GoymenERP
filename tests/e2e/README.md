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

## Status

The runner infrastructure is complete and verified end to end: Playwright's own
JUnit and HTML reporters plus `src/evidenceReporter.ts` produce artifacts that
pass the real validator, and the validator was confirmed to REJECT a query string
in a path, a missing fault outcome, a one-character fixture SHA change, a failed
case and a `retry-200` with `retryOrdinal: 0`.

**The ten case specs are not written yet.** `specs/` is empty on purpose. A suite
that emitted ten `passed` cases without exercising the application would turn the
merge gate green on nothing, which is worse than a gate that is honestly red.
