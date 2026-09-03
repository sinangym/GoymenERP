/**
 * Negative mutations for the activation evidence gate (#199 P1-1).
 *
 * Each case is a report that the PREVIOUS gate accepted. If any of these passes,
 * the gate is decorative.
 */
import { test } from 'node:test'
import assert from 'node:assert/strict'
import { validateActivationEvidence, EXPECTED_CASE_ID } from './validateEvidence.mjs'

const PROPS = `<properties>
<property name="activation.authorizedResponseAt" value="1000"></property>
<property name="activation.returnAt" value="6000"></property>
<property name="activation.elapsedMs" value="5000"></property>
<property name="activation.authorizedResponseUrl" value="http://127.0.0.1:15080/api/v1/counterparties?pageSize=50"></property>
<property name="activation.authorizedResponseStatus" value="200"></property>
<property name="activation.testedCommit" value="abc1234"></property>
<property name="activation.runId" value="42"></property>
<property name="activation.composeProject" value="goymen-c01-42-1"></property>
</properties>`

function report({ cases = 1, name = `${EXPECTED_CASE_ID} a revoked grant closes the route`,
  failures = 0, errors = 0, skipped = 0, body = '', props = PROPS } = {}) {
  const testcases = Array.from({ length: cases }, (_, index) =>
    `<testcase name="${index === 0 ? name : `EXTRA-${index}`}" classname="a.spec.ts" time="1">${body}${props}</testcase>`,
  ).join('\n')
  return `<testsuites tests="${cases}" failures="${failures}" errors="${errors}" skipped="${skipped}">
<testsuite name="a.spec.ts" tests="${cases}" failures="${failures}" errors="${errors}" skipped="${skipped}">
${testcases}
</testsuite>
</testsuites>`
}

test('the honest report passes', () => {
  const result = validateActivationEvidence(report())
  assert.equal(result.elapsed, 5000)
})

test('a renamed case is refused', () => {
  assert.throws(() => validateActivationEvidence(report({ name: 'SOMETHING-ELSE runs' })),
    /ACTIVATION_CASE_IDENTITY/)
})

test('an extra passing case is refused', () => {
  assert.throws(() => validateActivationEvidence(report({ cases: 2 })), /ACTIVATION_RETRY_OR_EXTRA/)
})

test('an extra case hidden behind honest-looking counters is refused', () => {
  // The sneakier shape: counters say one test, the tree holds two.
  const twoCases = report({ cases: 2 }).replace(/tests="2"/g, 'tests="1"')
  assert.throws(() => validateActivationEvidence(twoCases), /ACTIVATION_CASE_COUNT/)
})

test('a skipped case is refused', () => {
  assert.throws(() => validateActivationEvidence(report({ skipped: 1, body: '<skipped/>' })),
    /ACTIVATION_NOT_CLEAN/)
})

test('a suite that only DECLARES a failure is refused', () => {
  // No inlined <failure>; the body-only check the old gate used missed this.
  assert.throws(() => validateActivationEvidence(report({ failures: 1 })), /ACTIVATION_NOT_CLEAN/)
})

test('an empty report is refused', () => {
  assert.throws(() => validateActivationEvidence(''), /ACTIVATION_REPORT_EMPTY/)
})

test('a report with no timing evidence is refused', () => {
  assert.throws(() => validateActivationEvidence(report({ props: '' })), /ACTIVATION_TIMING_MISSING/)
})

test('a return outside the stale window is refused', () => {
  const props = PROPS.replace('value="6000"', 'value="41000"').replace('value="5000"', 'value="40000"')
  assert.throws(() => validateActivationEvidence(report({ props })), /ACTIVATION_OUTSIDE_STALE_WINDOW/)
})

test('a timing that does not add up is refused', () => {
  // The shape a hand-written or replayed elapsed would have.
  const props = PROPS.replace('name="activation.elapsedMs" value="5000"', 'name="activation.elapsedMs" value="10"')
  assert.throws(() => validateActivationEvidence(report({ props })), /ACTIVATION_TIMING_INCONSISTENT/)
})

test('timing bound to something other than a counterparties read is refused', () => {
  const props = PROPS.replace('/api/v1/counterparties?pageSize=50', '/api/v1/shell/capabilities')
  assert.throws(() => validateActivationEvidence(report({ props })), /ACTIVATION_RESPONSE_UNBOUND/)
})

test('a non-200 bound response is refused', () => {
  const props = PROPS.replace('name="activation.authorizedResponseStatus" value="200"',
    'name="activation.authorizedResponseStatus" value="403"')
  assert.throws(() => validateActivationEvidence(report({ props })), /ACTIVATION_RESPONSE_NOT_OK/)
})

test('a name that merely CONTAINS the case id is refused', () => {
  // The exact mutation Codex named: `includes` accepted this.
  assert.throws(
    () => validateActivationEvidence(report({ name: `RENAMED-${EXPECTED_CASE_ID}-LOOKALIKE runs` })),
    /ACTIVATION_CASE_IDENTITY/,
  )
})

test('a case id with a suffix glued on is refused', () => {
  assert.throws(
    () => validateActivationEvidence(report({ name: `${EXPECTED_CASE_ID}-LOOKALIKE runs` })),
    /ACTIVATION_CASE_IDENTITY/,
  )
})

test('a case id used as a later word is refused', () => {
  assert.throws(
    () => validateActivationEvidence(report({ name: `something ${EXPECTED_CASE_ID}` })),
    /ACTIVATION_CASE_IDENTITY/,
  )
})

const THIS_RUN = { testedCommit: 'abc1234', runId: '42', composeProject: 'goymen-c01-42-1' }

test('the honest report passes its own run identity', () => {
  assert.equal(validateActivationEvidence(report(), THIS_RUN).elapsed, 5000)
})

test('a report from another run is refused', () => {
  assert.throws(
    () => validateActivationEvidence(report(), { ...THIS_RUN, runId: '99' }),
    /ACTIVATION_IDENTITY_MISMATCH/,
  )
})

test('a report from another head is refused', () => {
  assert.throws(
    () => validateActivationEvidence(report(), { ...THIS_RUN, testedCommit: 'def5678' }),
    /ACTIVATION_IDENTITY_MISMATCH/,
  )
})

test('a report from another attempt is refused', () => {
  assert.throws(
    () => validateActivationEvidence(report(), { ...THIS_RUN, composeProject: 'goymen-c01-42-2' }),
    /ACTIVATION_IDENTITY_MISMATCH/,
  )
})

test('a report carrying no run identity at all is refused', () => {
  const identityProps = /<property name="activation\.(testedCommit|runId|composeProject)"[^>]*><\/property>/g
  const props = PROPS.replace(identityProps, '')
  assert.throws(
    () => validateActivationEvidence(report({ props }), THIS_RUN),
    /ACTIVATION_IDENTITY_MISSING/,
  )
})

test('truncated XML is refused rather than partially believed', () => {
  const truncated = report().slice(0, Math.floor(report().length * 0.6))
  assert.throws(() => validateActivationEvidence(truncated), /ACTIVATION_REPORT_MALFORMED/)
})

test('a testcase written inside a comment does not count', () => {
  const hidden = report().replace(
    '</testsuite>',
    '<!-- <testcase name="C01A-HISTORY-REVOKE-forward fake" /> --></testsuite>',
  )
  // Still exactly one real case, so this must PASS -- the comment is invisible.
  assert.equal(validateActivationEvidence(hidden, THIS_RUN).elapsed, 5000)
})

test('a whole report faked inside CDATA is refused', () => {
  const cdata =
    '<testsuites tests="1" failures="0" errors="0" skipped="0">' +
    '<testsuite name="a" tests="1" failures="0" errors="0" skipped="0">' +
    '<system-out><![CDATA[<testcase name="C01A-HISTORY-REVOKE-forward x"><properties>' +
    '<property name="activation.elapsedMs" value="1"></property></properties></testcase>]]>' +
    '</system-out></testsuite></testsuites>'
  assert.throws(() => validateActivationEvidence(cdata), /ACTIVATION_CASE_COUNT/)
})

test('a root that declares a failure its suite hides is refused', () => {
  const lying = report().replace('<testsuites tests="1" failures="0"', '<testsuites tests="1" failures="1"')
  assert.throws(() => validateActivationEvidence(lying), /ACTIVATION_NOT_CLEAN/)
})

test('an unclosed element is refused', () => {
  assert.throws(
    () => validateActivationEvidence(report().replace('</testsuites>', '')),
    /ACTIVATION_REPORT_MALFORMED/,
  )
})

test('a timing value that is not a plain integer is refused', () => {
  const props = PROPS.replace('name="activation.elapsedMs" value="5000"', 'name="activation.elapsedMs" value="5e3"')
  assert.throws(() => validateActivationEvidence(report({ props })), /ACTIVATION_TIMING_MALFORMED/)
})
