/**
 * The activation evidence gate (#199 P1-1/P1-2, #203 review P1-2/P1-3).
 *
 * HOW THIS GOT HERE, because each step was a real defect:
 *
 *  1. The first version counted `<testcase` occurrences and looked for the
 *     absence of failure/error/skipped tags, and the workflow made the whole
 *     activation block conditional on the sources existing -- so DELETING the
 *     suite turned the job green.
 *  2. Then it matched the case id with `includes`, which accepted
 *     `RENAMED-<id>-LOOKALIKE`.
 *  3. Then it was still regex over the raw text, so a `<testcase` written inside
 *     a comment or a `system-out` CDATA counted, truncated XML parsed fine, and
 *     a root element declaring `failures="1"` passed while an inner suite said
 *     zero.
 *  4. And the report named no run, so an artifact from one run validated
 *     unchanged in another.
 *
 * So the text is now actually PARSED, the structure is checked rather than
 * pattern-matched, and the evidence carries the identity of the run that
 * produced it. A gate that reads its input differently from the thing that
 * produced it is not a gate; that lesson cost four rounds.
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

/**
 * Run identity baked into the evidence itself (#203 review P1-3).
 *
 * The report must name the run that produced it, and that name must be the run
 * doing the checking.
 */
export const IDENTITY = {
  testedCommit: 'activation.testedCommit',
  runId: 'activation.runId',
  composeProject: 'activation.composeProject',
}

/**
 * Server-assigned request identities (#184 section 2.1/7).
 *
 * The exactly-once match against the API's structured record is Codex's
 * surface; what is enforced here is that this side produced something worth
 * matching.
 */
export const CORRELATIONS = 'activation.requestCorrelations'

class EvidenceError extends Error {}

function fail(code, detail) {
  throw new EvidenceError(`${code}: ${detail}`)
}

function decode(value) {
  return value
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&amp;/g, '&')
}

/**
 * A deliberately small, deliberately strict XML reader.
 *
 * It understands exactly what a JUnit report needs and REFUSES everything else,
 * which is the point: comments and CDATA are consumed and discarded rather than
 * scanned, so text shaped like a testcase inside them cannot be mistaken for
 * one. Anything unbalanced, truncated, or trailing the root element throws.
 *
 * No dependency, on purpose. Adding an XML parser to this package would change
 * the pinned lockfile that Codex's package contract validates, which is a
 * supply-chain conversation this fix does not need to have.
 */
export function parseXml(text) {
  let index = 0
  const stack = []
  let root = null

  const skipWhitespace = () => {
    while (index < text.length && /\s/.test(text[index])) index += 1
  }
  const consume = (token) => {
    if (!text.startsWith(token, index)) return false
    index += token.length
    return true
  }
  const consumeUntil = (token, what) => {
    const end = text.indexOf(token, index)
    if (end === -1) fail('ACTIVATION_REPORT_MALFORMED', `unterminated ${what}`)
    const body = text.slice(index, end)
    index = end + token.length
    return body
  }

  skipWhitespace()
  while (index < text.length) {
    if (text[index] !== '<') {
      // Character data between elements. Never inspected for structure.
      const next = text.indexOf('<', index)
      index = next === -1 ? text.length : next
      continue
    }
    if (consume('<?')) {
      consumeUntil('?>', 'processing instruction')
      continue
    }
    if (consume('<!--')) {
      consumeUntil('-->', 'comment')
      continue
    }
    if (consume('<![CDATA[')) {
      consumeUntil(']]>', 'CDATA section')
      continue
    }
    if (consume('<!')) {
      consumeUntil('>', 'declaration')
      continue
    }
    if (consume('</')) {
      const name = consumeUntil('>', 'closing tag').trim()
      const open = stack.pop()
      if (open === undefined || open.name !== name) {
        fail(
          'ACTIVATION_REPORT_MALFORMED',
          `closing tag ${name} does not match ${open === undefined ? '(nothing)' : open.name}`,
        )
      }
      if (stack.length === 0) {
        // The root closed; nothing but whitespace may follow.
        skipWhitespace()
        if (index < text.length) {
          fail('ACTIVATION_REPORT_MALFORMED', 'content follows the root element')
        }
      }
      continue
    }
    if (!consume('<')) fail('ACTIVATION_REPORT_MALFORMED', 'unexpected input')

    const raw = consumeUntil('>', 'element')
    const selfClosing = raw.endsWith('/')
    const body = selfClosing ? raw.slice(0, -1) : raw
    const match = /^([A-Za-z_][\w.:-]*)([\s\S]*)$/.exec(body.trim())
    if (match === null) fail('ACTIVATION_REPORT_MALFORMED', `unparsable element <${body.trim()}>`)
    const node = { name: match[1], attrs: {}, children: [] }

    const attributes = /([A-Za-z_][\w.:-]*)\s*=\s*"([^"]*)"/g
    let attribute
    while ((attribute = attributes.exec(match[2])) !== null) {
      if (Object.hasOwn(node.attrs, attribute[1])) {
        fail('ACTIVATION_REPORT_MALFORMED', `<${node.name}> repeats attribute ${attribute[1]}`)
      }
      node.attrs[attribute[1]] = decode(attribute[2])
    }

    if (stack.length === 0) {
      if (root !== null) fail('ACTIVATION_REPORT_MALFORMED', 'more than one root element')
      root = node
    } else {
      stack[stack.length - 1].children.push(node)
    }
    if (!selfClosing) stack.push(node)
    else if (stack.length === 0) {
      skipWhitespace()
      if (index < text.length) {
        fail('ACTIVATION_REPORT_MALFORMED', 'content follows the root element')
      }
    }
  }

  if (stack.length > 0) {
    fail('ACTIVATION_REPORT_MALFORMED', `unclosed <${stack[stack.length - 1].name}>`)
  }
  if (root === null) fail('ACTIVATION_REPORT_MALFORMED', 'the report has no root element')
  return root
}

function descendants(node, name, found = []) {
  for (const child of node.children) {
    if (child.name === name) found.push(child)
    descendants(child, name, found)
  }
  return found
}

/** Absent counts as a lie, not as zero. */
function counter(node, attribute) {
  const raw = node.attrs[attribute]
  if (raw === undefined || !/^\d+$/.test(raw)) {
    fail('ACTIVATION_REPORT_MALFORMED', `<${node.name}> has no numeric ${attribute}`)
  }
  return Number(raw)
}

/**
 * @param xml the report
 * @param expected the identity THIS run declares; the report must match it
 */
export function validateActivationEvidence(xml, expected = null) {
  if (typeof xml !== 'string' || xml.trim() === '') {
    fail('ACTIVATION_REPORT_EMPTY', 'the report is empty')
  }
  const root = parseXml(xml)
  if (root.name !== 'testsuites') {
    fail('ACTIVATION_REPORT_STRUCTURE', `the root element is <${root.name}>, expected <testsuites>`)
  }

  const suites = root.children.filter((child) => child.name === 'testsuite')
  if (suites.length === 0) {
    fail('ACTIVATION_REPORT_STRUCTURE', 'the root carries no <testsuite>')
  }
  for (const child of root.children) {
    if (child.name !== 'testsuite') {
      fail('ACTIVATION_REPORT_STRUCTURE', `unexpected <${child.name}> under the root`)
    }
  }

  // Counters on the ROOT as well as each suite. Checking only one lets the other
  // declare a failure the gate never sees.
  for (const node of [root, ...suites]) {
    for (const attribute of ['failures', 'errors', 'skipped']) {
      const value = counter(node, attribute)
      if (value !== 0) {
        fail('ACTIVATION_NOT_CLEAN', `<${node.name}> declares ${attribute}=${value}`)
      }
    }
  }
  const rootTests = counter(root, 'tests')
  if (rootTests !== 1) {
    fail('ACTIVATION_RETRY_OR_EXTRA', `the root declares tests=${rootTests}, expected 1`)
  }
  const suiteTests = suites.reduce((total, suite) => total + counter(suite, 'tests'), 0)
  if (suiteTests !== 1) {
    fail('ACTIVATION_RETRY_OR_EXTRA', `the suites declare tests=${suiteTests}, expected 1`)
  }

  // Structural, not textual: a <failure> anywhere in the tree is real, and one
  // written inside a comment or CDATA never became a node at all.
  for (const tag of ['failure', 'error', 'skipped']) {
    if (descendants(root, tag).length > 0) {
      fail('ACTIVATION_NOT_CLEAN', `the report contains a <${tag}> element`)
    }
  }

  const cases = descendants(root, 'testcase')
  if (cases.length !== 1) {
    fail('ACTIVATION_CASE_COUNT', `expected exactly 1 testcase, found ${cases.length}`)
  }
  const testcase = cases[0]
  if (!suites.some((suite) => suite.children.includes(testcase))) {
    fail('ACTIVATION_REPORT_STRUCTURE', 'the testcase is not a direct child of a testsuite')
  }

  // EXACT leading token, not a substring. `includes` accepted
  // `RENAMED-C01A-HISTORY-REVOKE-forward-LOOKALIKE`.
  const name = testcase.attrs.name
  const leading = typeof name === 'string' ? name.trim().split(/\s+/)[0] : null
  if (leading !== EXPECTED_CASE_ID) {
    fail(
      'ACTIVATION_CASE_IDENTITY',
      `expected the name to begin with exactly ${EXPECTED_CASE_ID}, found ${leading ?? '(unnamed)'}`,
    )
  }

  // Properties must be owned by THAT testcase, not merely present somewhere.
  const properties = new Map()
  for (const container of testcase.children.filter((child) => child.name === 'properties')) {
    for (const property of container.children) {
      if (property.name !== 'property') {
        fail('ACTIVATION_REPORT_STRUCTURE', `unexpected <${property.name}> under <properties>`)
      }
      const key = property.attrs.name
      if (typeof key !== 'string' || key === '') {
        fail('ACTIVATION_REPORT_MALFORMED', 'a <property> has no name')
      }
      if (properties.has(key)) {
        fail('ACTIVATION_REPORT_MALFORMED', `<properties> repeats ${key}`)
      }
      properties.set(key, property.attrs.value ?? null)
    }
  }

  for (const key of Object.values(TIMING)) {
    const value = properties.get(key)
    if (value === undefined || value === null || value === '') {
      fail('ACTIVATION_TIMING_MISSING', `the report carries no ${key}`)
    }
  }

  // Strict formats. A timing value that is merely "a number" invites a float, an
  // exponent or a sign that reads differently downstream.
  const integer = (key) => {
    const raw = properties.get(key)
    if (!/^\d+$/.test(raw)) {
      fail('ACTIVATION_TIMING_MALFORMED', `${key} is ${raw}, expected a non-negative integer`)
    }
    return Number(raw)
  }
  const authorizedAt = integer(TIMING.authorizedAt)
  const returnAt = integer(TIMING.returnAt)
  const elapsed = integer(TIMING.elapsed)
  if (elapsed !== returnAt - authorizedAt) {
    fail(
      'ACTIVATION_TIMING_INCONSISTENT',
      `elapsed ${elapsed} does not equal ${returnAt} - ${authorizedAt}`,
    )
  }
  if (elapsed >= STALE_WINDOW_MS) {
    fail(
      'ACTIVATION_OUTSIDE_STALE_WINDOW',
      `the return landed ${elapsed}ms after the authorized response`,
    )
  }

  for (const key of Object.values(IDENTITY)) {
    const value = properties.get(key)
    if (value === undefined || value === null || value === '') {
      fail('ACTIVATION_IDENTITY_MISSING', `the report carries no ${key}`)
    }
  }
  if (expected !== null) {
    for (const [field, key] of Object.entries(IDENTITY)) {
      if (properties.get(key) !== expected[field]) {
        fail(
          'ACTIVATION_IDENTITY_MISMATCH',
          `${key} is ${properties.get(key)} but this run declares ${expected[field]}`,
        )
      }
    }
  }

  // The parsed reader hands these back already entity-decoded, so the JSON
  // arrives intact rather than as `{&quot;kind&quot;:...}`.
  let correlations
  try {
    correlations = JSON.parse(properties.get(CORRELATIONS) ?? 'null')
  } catch {
    fail('ACTIVATION_CORRELATIONS_MALFORMED', 'the correlation record is not JSON')
  }
  if (!Array.isArray(correlations) || correlations.length === 0) {
    fail('ACTIVATION_CORRELATIONS_MISSING', 'the report records no request correlations')
  }
  const seen = new Set()
  let capabilityCount = 0
  let businessCount = 0
  for (const entry of correlations) {
    if (entry === null || typeof entry !== 'object') {
      fail('ACTIVATION_CORRELATIONS_MALFORMED', 'a correlation entry is not an object')
    }
    if (entry.kind !== 'capability' && entry.kind !== 'business') {
      fail('ACTIVATION_CORRELATIONS_MALFORMED', `unknown correlation kind ${entry.kind}`)
    }
    if (typeof entry.correlationId !== 'string' || entry.correlationId === '') {
      fail(
        'ACTIVATION_CORRELATION_UNIDENTIFIED',
        `a ${entry.kind} response carried no server correlation id`,
      )
    }
    if (seen.has(entry.correlationId)) {
      fail(
        'ACTIVATION_CORRELATION_DUPLICATE',
        `correlation ${entry.correlationId} appears more than once`,
      )
    }
    seen.add(entry.correlationId)
    if (entry.kind === 'capability') capabilityCount += 1
    else businessCount += 1
  }
  if (capabilityCount === 0) {
    fail('ACTIVATION_CORRELATIONS_INCOMPLETE', 'no capability response was correlated')
  }
  if (businessCount === 0) {
    fail('ACTIVATION_CORRELATIONS_INCOMPLETE', 'no business response was correlated')
  }

  if (properties.get(TIMING.status) !== '200') {
    fail('ACTIVATION_RESPONSE_NOT_OK', `the bound authorized response was ${properties.get(TIMING.status)}`)
  }
  if (!/\/api\/v1\/counterparties/.test(properties.get(TIMING.url) ?? '')) {
    fail('ACTIVATION_RESPONSE_UNBOUND', 'the timing is not bound to a counterparties read')
  }

  return {
    caseName: name,
    elapsed,
    authorizedAt,
    returnAt,
    properties,
    correlations: correlations.length,
    capabilityCount,
    businessCount,
  }
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
  const expect = (name) => {
    const value = process.env[name]
    if (!value || !value.trim()) {
      console.error(`::error::ACTIVATION_EXPECTED_IDENTITY_MISSING: ${name}`)
      process.exit(1)
    }
    return value.trim()
  }
  try {
    const result = validateActivationEvidence(xml, {
      testedCommit: expect('GOYMEN_ERP_C01_E2E_TESTED_COMMIT'),
      runId: expect('GOYMEN_ERP_C01_E2E_RUN_ID'),
      composeProject: expect('GOYMEN_ERP_C01_E2E_COMPOSE_PROJECT'),
    })
    console.log(
      `C-01A activation evidence: ${result.caseName} — 1 case, skip=0, ` +
        `${result.elapsed}ms after the authorized response, ` +
        `${result.correlations} server-correlated requests ` +
        `(${result.capabilityCount} capability, ${result.businessCount} business)`,
    )
  } catch (error) {
    console.error(`::error::${error.message}`)
    process.exit(1)
  }
}
