import { expect, test } from '../src/test'
import { B_ID, PARTY_2, candidate, candidatePage, linkedView } from '../../cari-acceptance-009/src/fixtures'
import { Lane, json } from '../src/harness'
import { openLink, choose, submit, noSuccess } from '../src/ui'

test('ME-004 Link mutation decoder missing branches: wrong Party, missing/duplicate account, role bits and numeric versions never complete', async ({ page }, info) => {
  const account = linkedView().companyAccounts[0]
  const cases = [
    { name: 'wrong-party-binding', body: linkedView(2, { id: PARTY_2 }) },
    { name: 'missing-account', body: linkedView(2, { companyAccounts: [] }) },
    { name: 'duplicate-account', body: linkedView(2, { companyAccounts: [account, account] }) },
    { name: 'unknown-role-bit', body: linkedView(16) },
    { name: 'numeric-party-version', body: linkedView(2, { version: 1 }) },
    { name: 'numeric-account-version', body: linkedView(2, { companyAccounts: [{ ...account, version: 1 }] }) },
  ]
  let current = cases[0]
  const lane = await Lane.install(page, { slug: 'link-decoder-branches', company: B_ID, link: () => json(current.body, 201, undefined, current.name) })
  const measured: { name: string; unknown: boolean; noSuccess: boolean; posts: number }[] = []
  for (const item of cases) {
    current = item
    await openLink(page)
    await choose(page)
    const baseline = lane.of('link').length
    await submit(page).click()
    await expect(page.locator('[data-attempt="unknown"]')).toContainText('Sunucu yanıtı beklenen biçimde değil.')
    await noSuccess(page)
    expect(lane.of('link').length - baseline).toBe(1)
    expect(lane.of('link').at(-1)?.fixture).toBe(item.name)
    await expect.poll(() => lane.of('link').at(-1)?.browserFinished).toBe(true)
    await expect(page.getByRole('checkbox', { name: 'Tedarikçi' })).toBeDisabled()
    measured.push({ name: item.name, unknown: true, noSuccess: true, posts: 1 })
  }
  expect(lane.externalBlocked).toBe(0)
  expect(lane.of('unexpected')).toHaveLength(0)
  await lane.finish(info, { me: ['ME-004'], branches: measured, candidateOrDetailDecoderClaims: false })
})

test('ME-004 candidate decoder missing source/version/continuation branches reject the entire page without creating selection authority', async ({ page }, info) => {
  const source = candidate().visibleSourceAccounts[0]
  const cases = [
    { name: 'missing-visible-source', body: candidatePage([candidate({ visibleSourceAccounts: [] })]) },
    { name: 'duplicate-source-account', body: candidatePage([candidate({ visibleSourceAccounts: [source, source] })]) },
    { name: 'target-as-source', body: candidatePage([candidate({ visibleSourceAccounts: [{ ...source, companyId: B_ID }] })]) },
    { name: 'numeric-source-version', body: candidatePage([candidate({ visibleSourceAccounts: [{ ...source, version: 1 }] })]) },
    { name: 'unknown-source-role', body: candidatePage([candidate({ visibleSourceAccounts: [{ ...source, roles: 16 }] })]) },
    { name: 'missing-continuation-cursor', body: candidatePage([candidate()], { hasMore: true }) },
    { name: 'duplicate-party', body: candidatePage([candidate(), candidate()]) },
  ]
  let current = cases[0]
  const lane = await Lane.install(page, {
    slug: 'candidate-decoder-branches', company: B_ID, candidates: () => json(current.body, 200, undefined, current.name),
  })
  const measured: { name: string; pageRejected: boolean; selectionCount: number; posts: number }[] = []
  for (const item of cases) {
    current = item
    await page.goto('/cari/sirkete-bagla')
    await expect(page.locator('[data-state="malformed-response"]')).toBeVisible()
    expect(lane.of('candidates').at(-1)?.fixture).toBe(item.name)
    await expect.poll(() => lane.of('candidates').at(-1)?.browserFinished).toBe(true)
    await expect(page.getByRole('radio')).toHaveCount(0)
    await expect(page.getByRole('row', { name: /CAR-000001/ })).toHaveCount(0)
    await page.getByRole('checkbox', { name: 'Tedarikçi' }).check()
    /**
     * A rejected page leaves no fresh successful read standing, so the write gate is
     * closed and the submit is DISABLED — the selection error can no longer be
     * provoked by clicking, because the click never reaches a handler. The fact this
     * case is about is unchanged and still asserted: nothing was POSTed.
     */
    await expect(submit(page)).toBeDisabled()
    expect(lane.of('link')).toHaveLength(0)
    measured.push({ name: item.name, pageRejected: true, selectionCount: 0, posts: 0 })
  }
  expect(lane.externalBlocked).toBe(0)
  await lane.finish(info, { me: ['ME-004'], branches: measured, allDecoderBranchesClaimed: false })
})
