import { expect, test } from '../src/test'
import { B_ID, PARTY_2, PROBLEM, candidate, candidatePage, linkedView } from '../../cari-acceptance-009/src/fixtures'
import { Lane, held, json, problem } from '../src/harness'
import { openLink, choose, selection, submit } from '../src/ui'

for (const variant of ['invalid-cursor', 'malformed'] as const) {
  test('ME-003 recovery ' + variant + ': pending and failed first page grant nothing; only fresh success opens selection', async ({ page }, info) => {
    const failureGate = held()
    const freshGate = held()
    let broken = false
    let stage: 'failure' | 'fresh' = 'failure'
    const lane = await Lane.install(page, {
      slug: 'recovery-' + variant, company: B_ID,
      candidates: async ({ url }) => {
        if (url.searchParams.get('cursor') === 'broken') {
          broken = true
          return variant === 'invalid-cursor'
            ? problem(400, PROBLEM.invalidCursor)
            : json(candidatePage([candidate()], { countScope: 'not-in-contract' }))
        }
        if (!broken) return json(candidatePage([candidate()], { hasMore: true, nextCursor: 'broken' }))
        const requestStage = stage
        if (requestStage === 'failure') { await failureGate.promise; return problem(503, PROBLEM.serviceUnavailable) }
        await freshGate.promise
        return json(candidatePage([candidate({ id: PARTY_2, code: 'CAR-000002' })]))
      },
      link: () => json(linkedView(2, { id: PARTY_2, code: 'CAR-000002' }), 201),
    })
    try {
      await openLink(page)
      await choose(page)
      const baseline = lane.of('candidates').length
      await page.getByRole('button', { name: 'Daha fazla aday yükle' }).click()
      if (variant === 'malformed') {
        await expect(page.locator('[data-state="malformed-response"]')).toBeVisible()
        await expect.poll(() => selection(page)).toBe('suspended')
        await page.locator('[data-state="malformed-response"]').getByRole('button', { name: 'Tekrar dene' }).click()
      }
      await expect.poll(() => lane.of('candidates').slice(baseline).filter((r) => r.cursor === null).length).toBeGreaterThan(0)
      await expect(page.locator('[data-state="initial-loading"]')).toBeVisible()
      await expect(page.getByRole('radio')).toHaveCount(0)
      await expect.poll(() => selection(page)).not.toBe('fresh')
      await expect(page.getByRole('status').getByText(/Aday listesi yenilendi/)).toHaveCount(0)
      await submit(page).click()
      expect(lane.of('link')).toHaveLength(0)
      failureGate.release()
      await expect(page.locator('[data-state="service-unavailable"]')).toBeVisible()
      await expect(page.getByRole('radio')).toHaveCount(0)
      await expect.poll(() => selection(page)).not.toBe('fresh')
      await submit(page).click()
      expect(lane.of('link')).toHaveLength(0)
      await expect(page.getByRole('status').getByText(/Aday listesi yenilendi/)).toHaveCount(0)
      stage = 'fresh'
      const beforeRetry = lane.of('candidates').length
      await page.locator('[data-state="service-unavailable"]').getByRole('button', { name: 'Tekrar dene' }).click()
      await expect.poll(() => lane.of('candidates').length).toBeGreaterThan(beforeRetry)
      await expect.poll(() => selection(page)).not.toBe('fresh')
      expect(lane.of('link')).toHaveLength(0)
      freshGate.release()
      await expect(page.getByRole('radio', { name: /CAR-000002/ })).toBeVisible()
      await expect(page.getByRole('radio', { name: /CAR-000001/ })).toHaveCount(0)
      if (variant === 'invalid-cursor') await expect(page.getByRole('status').getByText('Aday listesi yenilendi; ilk sayfaya dönüldü.', { exact: true })).toBeVisible()
      await choose(page, 'CAR-000002')
      await expect.poll(() => selection(page)).toBe('fresh')
      await submit(page).click()
      await expect(page.locator('[data-outcome="linked"]')).toHaveCount(1)
      expect(lane.of('link')).toHaveLength(1)
      expect(lane.of('link')[0].company).toBe(B_ID)
      expect(lane.of('link')[0].path).toContain(PARTY_2)
      expect(lane.externalBlocked).toBe(0)
      expect(lane.of('unexpected')).toHaveLength(0)
      await lane.finish(info, { me: ['ME-003'], variant, pendingNoAuthority: true, failedNoAuthority: true,
        freshReadIssued: true, freshSelectionThenOnePost: true, baselineReads: baseline })
    } finally { failureGate.release(); freshGate.release() }
  })
}
