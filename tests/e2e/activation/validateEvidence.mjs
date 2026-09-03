/**
 * The activation evidence gate (#199 P1-1, P1-2).
 *
 * The version this replaces counted `<testcase` occurrences and looked for the
 * absence of failure/error/skipped tags. Codex was right that this is not a gate:
 * it accepts a renamed case, an extra case, and a report produced by a run of
 * something else entirely. Worse, the workflow made the whole activation block
 * conditional on the source files existing, so DELETING the suite turned the job
 * green. An evidence gate with a skip path is not a gate.
 *
 * What is asserted here is the exact shape of one specific run:
 *
 *   - exactly one testcase, no more and no fewer;
 *   - its name carries the exact case id;
 *   - zero failures, errors and skips, counted from BOTH the suite attributes
 *     and the element bodies, because either alone can be made to lie;
 *   - the timing evidence #199 P1-2 requires, bound to an observed authorized
 *     response rather than to when a row happened to become visible.
 *
 * Playwright emits test annotations as <property> under <properties>; that was
 * verified against @playwright/test 1.62 rather than assumed.
 */

export const EXPECTED_CASE_ID = 'C01A-HISTORY-REVOKE-forward'
export const STALE_WINDOW_MS = 30_000

export const TIMING = {
  authorizedAt: 'activation.authorizedResponseAt',
  returnAt: 'activation.returnAt',
  elapsed: 'activation.elapsedMs',
  url: 'activation.authorizedResponseUrl',
  status: 'activation.authorizedResponseStatus',
}

class EvidenceError extends Error {}

function fail(code, detail) {
  throw new EvidenceError(`${code}: ${detail}`)
}

function attr(tag, name) {
  const match = new RegExp(`\\b${name}="([^"]*)"`).exec(tag)
  return match === null ? null : match[1]
}

/** Sum a numeric attribute across every element of a tag, absent counting as a lie. */
function sumAttribute(xml, tagName, attribute) {
  let total = 0
  for (const tag of xml.match(new RegExp(`<${tagName}\\b[^>]*>`, 'g')) ?? []) {
    const raw = attr(tag, attribute)
    if (raw === null || !/^\d+$/.test(raw)) {
      fail('ACTIVATION_REPORT_MALFORMED', `<${tagName}> has no numeric ${attribute}`)
    }
    total += Number(raw)
  }
  return total
}

export function validateActivationEvidence(xml) {
  if (typeof xml !== 'string' || xml.trim() === '') {
    fail('ACTIVATION_REPORT_EMPTY', 'the report is empty')
  }

  const cases = xml.match(/<testcase\b[^>]*>/g) ?? []
  if (cases.length !== 1) {
    fail('ACTIVATION_CASE_COUNT', `expected exactly 1 testcase, found ${cases.length}`)
  }

  const name = attr(cases[0], 'name')
  if (name === null || !name.includes(EXPECTED_CASE_ID)) {
    fail('ACTIVATION_CASE_IDENTITY', `expected ${EXPECTED_CASE_ID}, found ${name ?? '(unnamed)'}`)
  }

  // Both sources. A body-only check misses a suite that declares failures it did
  // not inline; an attribute-only check misses an inlined <failure>.
  for (const [tag, attribute] of [
    ['failure', 'failures'],
    ['error', 'errors'],
    ['skipped', 'skipped'],
  ]) {
    if (new RegExp(`<${tag}\\b`).test(xml)) {
      fail('ACTIVATION_NOT_CLEAN', `report contains <${tag}>`)
    }
    const declared = sumAttribute(xml, 'testsuite', attribute)
    if (declared !== 0) {
      fail('ACTIVATION_NOT_CLEAN', `testsuite declares ${attribute}=${declared}`)
    }
  }

  // Retries surface as additional testcases, so the count check above already
  // rejects them; this names the condition rather than leaving it implied.
  const suiteTests = sumAttribute(xml, 'testsuite', 'tests')
  if (suiteTests !== 1) {
    fail('ACTIVATION_RETRY_OR_EXTRA', `testsuite declares tests=${suiteTests}, expected 1`)
  }

  const properties = new Map()
  for (const tag of xml.match(/<property\b[^>]*>/g) ?? []) {
    const key = attr(tag, 'name')
    if (key !== null) properties.set(key, attr(tag, 'value'))
  }

  for (const key of Object.values(TIMING)) {
    if (!properties.has(key) || properties.get(key) === null || properties.get(key) === '') {
      fail('ACTIVATION_TIMING_MISSING', `the report carries no ${key}`)
    }
  }

  const elapsed = Number(properties.get(TIMING.elapsed))
  const authorizedAt = Number(properties.get(TIMING.authorizedAt))
  const returnAt = Number(properties.get(TIMING.returnAt))
  if (!Number.isFinite(elapsed) || !Number.isFinite(authorizedAt) || !Number.isFinite(returnAt)) {
    fail('ACTIVATION_TIMING_MALFORMED', 'a timing value is not a number')
  }
  if (elapsed !== returnAt - authorizedAt) {
    fail(
      'ACTIVATION_TIMING_INCONSISTENT',
      `elapsed ${elapsed} does not equal ${returnAt} - ${authorizedAt}`,
    )
  }
  if (elapsed < 0 || elapsed >= STALE_WINDOW_MS) {
    fail(
      'ACTIVATION_OUTSIDE_STALE_WINDOW',
      `the return landed ${elapsed}ms after the authorized response`,
    )
  }

  const status = properties.get(TIMING.status)
  if (status !== '200') {
    fail('ACTIVATION_RESPONSE_NOT_OK', `the bound authorized response was ${status}`)
  }
  if (!/\/api\/v1\/counterparties/.test(properties.get(TIMING.url) ?? '')) {
    fail('ACTIVATION_RESPONSE_UNBOUND', 'the timing is not bound to a counterparties read')
  }

  return { caseName: name, elapsed, authorizedAt, returnAt }
}

if (process.argv[1] && process.argv[1].endsWith('validateEvidence.mjs')) {
  const { readFileSync } = await import('node:fs')
  const target = process.argv[2]
  if (!target) {
    console.error('usage: node validateEvidence.mjs <activation-junit.xml>')
    process.exit(2)
  }
  let xml
  try {
    xml = readFileSync(target, 'utf8')
  } catch {
    console.error(`::error::ACTIVATION_JUNIT_MISSING: ${target}`)
    process.exit(1)
  }
  try {
    const result = validateActivationEvidence(xml)
    console.log(
      `C-01A activation evidence: ${result.caseName} — 1 case, skip=0, ` +
        `${result.elapsed}ms after the authorized response`,
    )
  } catch (error) {
    console.error(`::error::${error.message}`)
    process.exit(1)
  }
}
