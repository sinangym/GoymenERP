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
import { B_ID, IDENTITY, PARTY_2, candidate, candidatePage, linkedView } from '../../cari-acceptance-009/src/fixtures'
import { CONTRACT_HEADER, CONTRACT_VERSION, Lane, NO_ACK, held, insensitiveTransport, json, problem, refetch, shell } from '../src/harness'
import { openLink, choose, submit, noSuccess } from '../src/ui'

const p2 = candidate({ id: PARTY_2, code: 'CAR-000002' })

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

test('CG-007 a context change does not inherit the previous context readiness', async ({ page }, info) => {
  /**
   * Readiness belongs to one origin, actor session, company and generation. When the
   * actor changes, the grant the previous one earned is gone, and the new context has to
   * earn its own: here the server stops acknowledging at the moment of the switch, so
   * the rows render but the gate stays shut.
   *
   * Without this the sequence "sign in as A, read, switch to B, write" would carry A's
   * grant into B's session — a write to a node whose answer to B was never validated.
   */
  const actor2 = '88888888-8888-8888-8888-888888888888'
  let actor = IDENTITY.userId
  const lane = await Lane.install(page, {
    slug: 'contract-context-change', company: B_ID,
    identity: () => ({ ...IDENTITY, userId: actor, displayName: actor === actor2 ? 'Kullanıcı B' : 'Kullanıcı A' }),
    // Acknowledged for the first actor; silent for the second. `NO_ACK` is explicit:
    // a lane never loses its acknowledgment by accident.
    candidates: () => (actor === actor2
      ? json(candidatePage([p2]), 200, { [CONTRACT_HEADER]: NO_ACK })
      : json(candidatePage([candidate()]))),
  })
  await openLink(page)
  await choose(page)
  await expect(submit(page)).toBeEnabled()
  const before = await shell(page)

  actor = actor2
  await refetch(page, 'identity')
  await expect.poll(async () => (await shell(page)).actor).toBe(actor2)
  await expect(page.getByRole('radio', { name: /CAR-000002/ })).toBeVisible()
  const after = await shell(page)
  expect(after.epoch).toBeGreaterThan(before.epoch)

  // The new context can read; it may not write.
  await page.getByRole('radio', { name: /CAR-000002/ }).check()
  await expect(submit(page)).toBeDisabled()
  await submit(page).click({ force: true })
  await page.waitForTimeout(400)
  expect(lane.of('link')).toHaveLength(0)
  await noSuccess(page)
  const unacknowledged = lane.of('candidates').filter((r) => r.contractAck === null)
  expect(unacknowledged.length).toBeGreaterThan(0)
  await lane.finish(info, { before, after, newContextUnacknowledged: true, links: lane.of('link').length })
})

test('CG-008 a LATE acknowledged read from the previous context cannot open the gate for the new one', async ({ page }, info) => {
  /**
   * The same read, arriving late. The request was issued by the old actor and comes back
   * — 200, acknowledged, perfectly valid — after the context has already changed. It
   * describes a context that no longer exists, so it may not speak for the current one,
   * and the button must not blink open when it lands.
   */
  const late = held()
  const actor2 = '88888888-8888-8888-8888-888888888888'
  let actor = IDENTITY.userId
  await insensitiveTransport(page, { method: 'GET', fragment: 'cursor=old-actor' })
  const lane = await Lane.install(page, {
    slug: 'contract-late-old-context-ack', company: B_ID,
    identity: () => ({ ...IDENTITY, userId: actor, displayName: actor === actor2 ? 'Kullanıcı B' : 'Kullanıcı A' }),
    candidates: async ({ url }) => {
      // The old actor's paged read: held open, then answered 200 WITH the acknowledgment.
      if (url.searchParams.get('cursor') === 'old-actor') { await late.promise; return json(candidatePage([candidate()])) }
      return actor === actor2
        ? json(candidatePage([p2]), 200, { [CONTRACT_HEADER]: NO_ACK })
        : json(candidatePage([candidate()], { hasMore: true, nextCursor: 'old-actor' }))
    },
  })
  try {
    await openLink(page)
    await choose(page)
    await page.getByRole('button', { name: 'Daha fazla aday yükle' }).click()
    await expect.poll(() => lane.of('candidates').filter((r) => r.cursor === 'old-actor').length).toBe(1)

    actor = actor2
    await refetch(page, 'identity')
    await expect.poll(async () => (await shell(page)).actor).toBe(actor2)
    await page.getByRole('radio', { name: /CAR-000002/ }).check()
    await expect(submit(page)).toBeDisabled()

    // Now let the old context's acknowledged response arrive.
    late.release()
    await expect.poll(() => lane.of('candidates').filter((r) => r.cursor === 'old-actor' && r.contractAck === CONTRACT_VERSION).length).toBe(1)
    await page.waitForTimeout(600)

    await expect(submit(page)).toBeDisabled()
    await submit(page).click({ force: true })
    await page.waitForTimeout(300)
    expect(lane.of('link')).toHaveLength(0)
    await noSuccess(page)
    await lane.finish(info, { lateAckDelivered: true, stillClosed: true, links: lane.of('link').length })
  } finally { late.release() }
})
