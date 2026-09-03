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
  assert.throws(() => validateActivationEvidence(report({ cases: 2 })), /ACTIVATION_CASE_COUNT/)
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
