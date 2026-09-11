/**
 * The client-contract write gate, in a real browser against controlled responses.
 *
 * These are the variants the unit and transport suites cannot reach: they need a
 * rendered form, a real submit, and a count of what actually left the browser. The
 * distinction they exist to protect is that a write may only follow a fresh
 * successful GET that came back ACKNOWLEDGED — and that everything else, however
 * healthy it looks, leaves the gate shut.
 *
 * CONTROLLED, and never described otherwise: every response here is fulfilled by the
 * lane. None of this is an old backend binary, a real SQL replay, or a durable
 * server-side effect. What it proves is what this client does when a server behaves
 * in each of these ways.
 */
import { expect, test } from '../src/test'
import { B_ID, candidate, candidatePage, linkedView } from '../../cari-acceptance-009/src/fixtures'
import { CONTRACT_VERSION, Lane, json, problem, refetch } from '../src/harness'
import { openLink, choose, submit, noSuccess } from '../src/ui'

test('CG-001 old server: valid rows, no acknowledgment — the form is closed and nothing is POSTed', async ({ page }, info) => {
  // A node that predates the contract answers 200 with a perfectly good body and
  // simply never echoes the declaration. Writing to it is how a role-less record
  // reaches a server that cannot represent one.
  const lane = await Lane.install(page, { slug: 'contract-old-server', company: B_ID, silentServer: true })
  await openLink(page)
  await choose(page)

  await expect(submit(page)).toBeDisabled()
  await submit(page).click({ force: true })
  await page.waitForTimeout(500)

  expect(lane.of('link')).toHaveLength(0)
  // The rows are still usable — an old node still serves reads. Only the write closed.
  await expect(page.getByRole('radio', { name: /CAR-000001/ })).toBeChecked()
  await noSuccess(page)
  await lane.finish(info, { silentServer: true, links: lane.of('link').length, submitDisabled: true })
})

test('CG-002 an acknowledged ERROR is not readiness', async ({ page }, info) => {
  /**
   * The filter writes the acknowledgment before the handler runs, so a 503 can carry
   * it. Presence on an error proves the server understood the declaration and nothing
   * more; it must not open the write gate.
   */
  const lane = await Lane.install(page, {
    slug: 'contract-error-with-ack', company: B_ID,
    candidates: () => problem(503, 'service_unavailable'),
  })
  // Not `openLink`: a 503 renders no candidate rows to wait for.
  await page.goto('/cari/sirkete-bagla')
  await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible()
  await expect(page.locator('[data-state="service-unavailable"]')).toBeVisible()
  await expect(submit(page)).toBeDisabled()

  expect(lane.of('link')).toHaveLength(0)
  // The acknowledgment did reach the browser; it simply did not count.
  const acked = lane.of('candidates').filter((r) => r.contractAck === CONTRACT_VERSION)
  expect(acked.length).toBeGreaterThan(0)
  await lane.finish(info, { ackOnError: true, links: 0 })
})

test('CG-003 a read that fails after a good one closes the gate again', async ({ page }, info) => {
  // Readiness is not a latch. A later read in the same context that never reaches the
  // server withdraws it, and the button follows.
  /**
   * An explicit switch, not a read counter: under the Vite dev server StrictMode
   * duplicates a mount's first read and cancels one copy, so "the second read" is not
   * the one this case means.
   */
  let unreachable = false
  const lane = await Lane.install(page, {
    slug: 'contract-revoked-by-failure', company: B_ID,
    candidates: () => (unreachable ? { abort: true } : json(candidatePage([candidate()]))),
  })
  await openLink(page)
  await choose(page)
  await expect(submit(page)).toBeEnabled()
  // A later read in the same context, which never reaches the server.
  unreachable = true
  await refetch(page, 'candidates')
  await page.waitForTimeout(900)

  await expect(submit(page)).toBeDisabled()
  expect(lane.of('link')).toHaveLength(0)
  await lane.finish(info, { revokedByUnreachableRead: true, links: 0 })
})

test('CG-004 unknown → re-check → an acknowledged 500 leaves it unknown under the SAME key', async ({ page }, info) => {
  /**
   * The sequence the retry rule exists for. The first POST is processed and its
   * answer is lost; the re-send comes back 500 WITH the acknowledgment. That 500
   * describes the second request. It says nothing about whether the first one landed,
   * so the attempt stays unresolved — same key, same payload — and no third request
   * is minted under a new one.
   */
  let posts = 0
  const keys: (string | null)[] = []
  const lane = await Lane.install(page, {
    slug: 'contract-unknown-then-ack-500', company: B_ID,
    link: ({ key }) => {
      posts += 1
      keys.push(key)
      if (posts === 1) return { abort: true }
      return problem(500, 'internal_error')
    },
  })
  await openLink(page)
  await choose(page)
  await submit(page).click()

  const unknown = page.locator('[data-attempt="unknown"]')
  await expect(unknown).toBeVisible()
  await unknown.getByRole('button', { name: 'Aynı işlemi yeniden dene' }).click()
  await page.waitForTimeout(900)

  // Still unresolved, and still the same attempt.
  await expect(page.locator('[data-attempt="unknown"]')).toBeVisible()
  expect(posts).toBe(2)
  expect(keys[1]).toBe(keys[0])
  await noSuccess(page)
  await lane.finish(info, { posts, sameKey: keys[0] === keys[1], stillUnknown: true })
})

test('CG-005 unknown → re-check → a compatibility 409 also leaves it unknown', async ({ page }, info) => {
  // A 409 on a re-issue is consistent both with "the first attempt landed" and with
  // "it never did". It is not an answer either way, so it may not close the attempt
  // or release its key.
  let posts = 0
  const keys: (string | null)[] = []
  const lane = await Lane.install(page, {
    slug: 'contract-unknown-then-409', company: B_ID,
    link: ({ key }) => {
      posts += 1
      keys.push(key)
      if (posts === 1) return { abort: true }
      return problem(409, 'client_contract_unsupported')
    },
  })
  await openLink(page)
  await choose(page)
  await submit(page).click()

  const unknown = page.locator('[data-attempt="unknown"]')
  await expect(unknown).toBeVisible()
  await unknown.getByRole('button', { name: 'Aynı işlemi yeniden dene' }).click()
  await page.waitForTimeout(900)

  await expect(page.locator('[data-attempt="unknown"]')).toBeVisible()
  expect(keys[1]).toBe(keys[0])
  // No "Anladım" that would clear it to idle and let a new key be minted.
  await expect(page.getByRole('button', { name: 'Anladım' })).toHaveCount(0)
  await noSuccess(page)
  await lane.finish(info, { posts, sameKey: keys[0] === keys[1], acknowledgedClose: false })
})

test('CG-006 unknown → explicit retry after a fresh acknowledged read → one controlled success', async ({ page }, info) => {
  /**
   * The path that IS allowed to resolve it: re-validate in this context, then re-send
   * the same attempt explicitly. The retry re-reads first — a command failure does not
   * revoke readiness, so without that read the second POST would ride the grant the
   * first one earned.
   */
  let posts = 0
  let reads = 0
  const keys: (string | null)[] = []
  const lane = await Lane.install(page, {
    slug: 'contract-unknown-then-success', company: B_ID,
    candidates: () => {
      reads += 1
      return json(candidatePage([candidate()]))
    },
    link: ({ key, roles }) => {
      posts += 1
      keys.push(key)
      return posts === 1 ? { abort: true } : json(linkedView(roles ?? 0), 201)
    },
  })
  await openLink(page)
  await choose(page)
  await submit(page).click()
  const unknown = page.locator('[data-attempt="unknown"]')
  await expect(unknown).toBeVisible()

  const readsBeforeRetry = reads
  await unknown.getByRole('button', { name: 'Aynı işlemi yeniden dene' }).click()
  await page.waitForTimeout(1200)

  expect(reads).toBeGreaterThan(readsBeforeRetry)
  expect(posts).toBe(2)
  expect(keys[1]).toBe(keys[0])
  await lane.finish(info, {
    reValidatedBeforeResend: reads > readsBeforeRetry,
    posts,
    sameKey: keys[0] === keys[1],
    note: 'Controlled 201. Not a durable server commit or replay claim.',
  })
})
