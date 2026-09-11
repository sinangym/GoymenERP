import { expect, test } from '../src/test'
import { A_ID, B_ID, PARTY_2, IDENTITY, candidate, candidatePage, linkedView, mutationView } from '../../cari-acceptance-009/src/fixtures'
import { Lane, held, json, problem, shell, refetch, insensitiveTransport, transportEvents, CoverageWitness, save } from '../src/harness'
import { openLink, choose, selection, submit, noSuccess } from '../src/ui'

const p2 = candidate({ id: PARTY_2, code: 'CAR-000002' })
const forA = candidate({ visibleSourceAccounts: linkedView().companyAccounts })
const callbackPoint = { file: 'useCommandAttempt.ts', needle: 'inFlightRef.current = false', label: 'production command promise continuation' }

test.afterEach(async ({ page }, info) => {
  if (info.status === 'passed') return
  const state = await shell(page).catch(() => null)
  save('context-failure-state', { title: info.title, status: info.status, state, at: new Date().toISOString() })
})

test('ME-002/003 dirty Link Kal preserves cursor/selection; Discard clears context/key; late 201 reaches production callback but cannot complete in A', async ({ page }, info) => {
  const late = held()
  await insensitiveTransport(page, { method: 'POST', fragment: '/company-account' })
  const lane = await Lane.install(page, {
    slug: 'dirty-link-late-production-callback', company: B_ID,
    candidates: ({ url, company }) => company === A_ID ? json(candidatePage([forA]))
      : url.searchParams.has('cursor') ? json(candidatePage([p2]))
      : json(candidatePage([candidate()], { hasMore: true, nextCursor: 'c1' })),
    link: async ({ company, roles }) => {
      if (company === B_ID) { await late.promise; return json(linkedView(roles ?? 2, { id: PARTY_2, code: 'CAR-000002' }), 201) }
      return json(mutationView({ companyAccounts: [{ ...mutationView().companyAccounts[0], roles: roles ?? 2 }] }), 201)
    },
  })
  try {
    await openLink(page)
    await page.getByRole('button', { name: 'Daha fazla aday yükle' }).click()
    await choose(page, 'CAR-000002')
    await page.getByRole('searchbox', { name: 'Aday ara' }).fill('saklanan taslak')
    const picker = page.getByRole('combobox', { name: 'Şirket', exact: true })
    const beforeKal = lane.of('candidates').length
    await picker.selectOption(A_ID)
    await expect(page.getByRole('dialog')).toBeVisible()
    await page.getByRole('dialog').getByRole('button', { name: 'Kal', exact: true }).click()
    await expect(picker).toHaveValue(B_ID)
    await expect(page.getByRole('searchbox')).toHaveValue('saklanan taslak')
    await expect(page.getByRole('radio', { name: /CAR-000002/ })).toBeChecked()
    await expect(page.getByRole('checkbox', { name: 'Tedarikçi' })).toBeChecked()
    await expect.poll(() => selection(page)).toBe('fresh')
    expect(lane.of('candidates')).toHaveLength(beforeKal)
    expect(lane.of('capabilities').filter((r) => r.company === A_ID)).toHaveLength(0)
    await submit(page).click()
    await expect.poll(() => lane.of('link').length).toBe(1)
    const old = lane.of('link')[0]
    await expect(page.getByRole('button', { name: 'Gönderiliyor…' })).toBeDisabled()
    // Kal also preserves an in-flight immutable attempt; it does not resend it.
    await picker.selectOption(A_ID)
    await page.getByRole('dialog').getByRole('button', { name: 'Kal', exact: true }).click()
    expect(lane.of('link')).toHaveLength(1)
    expect(lane.of('link')[0].key).toBe(old.key)
    await picker.selectOption(A_ID)
    await page.getByRole('dialog').getByRole('button', { name: 'Değişiklikleri bırak' }).click()
    await expect(picker).toHaveValue(A_ID)
    await expect(page.getByRole('radio', { name: /CAR-000001/ })).toBeVisible()
    await expect(page.getByRole('searchbox')).toHaveValue('')
    await expect(page.getByRole('checkbox', { name: 'Tedarikçi' })).not.toBeChecked()
    await expect.poll(() => selection(page)).toBe('none')
    const aReads = lane.of('candidates').filter((r) => r.company === A_ID)
    expect(aReads.length).toBeGreaterThan(0)
    expect(aReads.every((r) => r.cursor === null && r.q === null)).toBe(true)
    const witness = await CoverageWitness.start(page)
    late.release()
    await expect.poll(async () => (await transportEvents(page)).filter((e) => e.event === 'production-json-resolved').length).toBe(1)
    await expect.poll(() => old.browserFinished).toBe(true)
    await page.waitForTimeout(250)
    const coverage = await witness.finish([callbackPoint])
    expect(coverage[0].count, 'the real then(outcome) callback must execute after discard').toBeGreaterThan(0)
    const events = await transportEvents(page)
    expect(events.some((e) => e.event === 'production-abort-observed')).toBe(true)
    expect(events.some((e) => e.event === 'production-json-resolved' && e.aborted && e.status === 201)).toBe(true)
    expect(old.browserCancelled).toBe(false)
    await noSuccess(page)
    await expect.poll(() => selection(page)).toBe('none')
    expect(lane.of('link')).toHaveLength(1)
    await choose(page)
    await submit(page).click()
    await expect(page.locator('[data-outcome="linked"]')).toHaveCount(1)
    const newer = lane.of('link')[1]
    expect(newer.company).toBe(A_ID)
    expect(newer.key).not.toBe(old.key)
    expect(newer.roles).toBe(2)
    expect(lane.of('link')).toHaveLength(2)
    expect(lane.externalBlocked).toBe(0)
    expect(lane.of('unexpected')).toHaveLength(0)
    await lane.finish(info, { me: ['ME-002', 'ME-003'], kalCursorAndFormPreserved: true,
      kalPendingAttemptPreserved: true, discardStateCleared: true, oldCallbackSuppressed: true,
      newCompanyNewKey: true, transportEvents: events, productionCoverage: coverage })
  } finally { late.release() }
})

test('ME-003 current candidate 403 revokes Link during the session; an already pending Link 201 cannot reopen it', async ({ page }, info) => {
  const late = held()
  let revoke = false
  await insensitiveTransport(page, { method: 'POST', fragment: '/company-account' })
  const lane = await Lane.install(page, {
    slug: 'in-session-link-revoke', company: B_ID,
    candidates: () => revoke ? problem(403, 'forbidden') : json(candidatePage([candidate()])),
    link: async () => { await late.promise; return json(linkedView(), 201) },
  })
  try {
    await openLink(page)
    await choose(page)
    const before = await shell(page)
    await submit(page).click()
    await expect.poll(() => lane.of('link').length).toBe(1)
    revoke = true
    await refetch(page, 'candidates')
    await expect(page.locator('[data-state="not-found"]')).toBeVisible()
    await expect(page.getByRole('radio')).toHaveCount(0)
    await expect(page.getByRole('link', { name: 'Cariyi Şirkete Bağla', exact: true })).toHaveCount(0)
    const after = await shell(page)
    expect(after.epoch).toBe(before.epoch)
    expect(after.actor).toBe(before.actor)
    expect(after.sessionInvalid).toBe(false)
    expect(after.hasLink).toBe(false)
    expect(after.lastSecurityEffect).toBe('capability-refence')
    const cache = await page.evaluate(() => window.c010.candidateCache())
    expect(cache.filter((q) => q.namespace === before.namespace)).toHaveLength(0)
    late.release()
    await expect.poll(async () => (await transportEvents(page)).some((e) => e.event === 'production-json-resolved' && e.status === 201)).toBe(true)
    await page.waitForTimeout(250)
    await expect(page.locator('[data-state="not-found"]')).toBeVisible()
    await noSuccess(page)
    expect(lane.of('link')).toHaveLength(1)
    expect(lane.externalBlocked).toBe(0)
    await lane.finish(info, { me: ['ME-003'], sameDocument: true, before, after, namespaceRemoved: true,
      old201DidNotReopen: true, transportEvents: await transportEvents(page) })
  } finally { late.release() }
})

test('ME-002/003 identity refetch changes actor/epoch; old candidate 401 is consumed and reported by production but cannot end the new session', async ({ page }, info) => {
  const late = held()
  let actor = IDENTITY.userId
  const actor2 = '88888888-8888-8888-8888-888888888888'
  await insensitiveTransport(page, { method: 'GET', fragment: 'cursor=old-actor' })
  const lane = await Lane.install(page, {
    slug: 'actor-epoch-late-candidate-401', company: B_ID,
    identity: () => ({ ...IDENTITY, userId: actor, displayName: actor === actor2 ? 'Kullanıcı B' : 'Kullanıcı A' }),
    candidates: async ({ url }) => {
      if (url.searchParams.get('cursor') === 'old-actor') { await late.promise; return problem(401, 'unauthenticated') }
      return actor === actor2 ? json(candidatePage([p2]))
        : json(candidatePage([candidate()], { hasMore: true, nextCursor: 'old-actor' }))
    },
  })
  try {
    await openLink(page)
    await choose(page)
    const before = await shell(page)
    await page.getByRole('button', { name: 'Daha fazla aday yükle' }).click()
    await expect.poll(() => lane.of('candidates').filter((r) => r.cursor === 'old-actor').length).toBe(1)
    const old = lane.of('candidates').find((r) => r.cursor === 'old-actor')!
    actor = actor2
    await refetch(page, 'identity')
    await expect.poll(async () => (await shell(page)).actor).toBe(actor2)
    await expect(page.getByRole('radio', { name: /CAR-000002/ })).toBeVisible()
    const newer = await shell(page)
    expect(newer.epoch).toBeGreaterThan(before.epoch)
    expect(newer.namespace).not.toBe(before.namespace)
    await expect.poll(() => selection(page)).toBe('none')
    await expect(page.getByRole('checkbox', { name: 'Tedarikçi' })).not.toBeChecked()
    const witness = await CoverageWitness.start(page)
    late.release()
    await expect.poll(async () => (await transportEvents(page)).some((e) => e.event === 'production-json-resolved' && e.status === 401)).toBe(true)
    await expect.poll(() => old.browserFinished).toBe(true)
    await page.waitForTimeout(250)
    const coverage = await witness.finish([
      { file: 'useAuthBoundary.ts', needle: 'readAuthenticatedActor(queryClient)', label: 'production late security report' },
      { file: 'shellState.ts', needle: 'if (!current)', label: 'production snapshot currentness decision' },
    ])
    expect(coverage.every((c) => c.count > 0)).toBe(true)
    const final = await shell(page)
    expect(final).toEqual(newer)
    expect(final.sessionInvalid).toBe(false)
    expect(final.hasLink).toBe(true)
    expect(old.browserCancelled).toBe(false)
    const cache = await page.evaluate(() => window.c010.candidateCache())
    expect(cache.some((q) => q.namespace === before.namespace)).toBe(false)
    await expect(page.getByRole('radio', { name: /CAR-000002/ })).toBeVisible()
    await expect(page.getByRole('radio', { name: /CAR-000001/ })).toHaveCount(0)
    await noSuccess(page)
    expect(lane.of('link')).toHaveLength(0)
    expect(lane.externalBlocked).toBe(0)
    await lane.finish(info, { me: ['ME-002', 'ME-003'], before, newer, final,
      productionCoverage: coverage, transportEvents: await transportEvents(page),
      previousNamespaceAbsent: true, actualLoginFlowClaimed: false })
  } finally { late.release() }
})
