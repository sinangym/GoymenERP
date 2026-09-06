/**
 * T-08 — UX-AC-001..003 in a REAL browser (G-SUPPORT-004 row 8): keyboard-only
 * completion with a visible focus, live-region announcements as DOM facts, and
 * reflow at 640 and 320 CSS px viewports (the 200 % / 400 % equivalents of a
 * 1280 px layout). Screen-reader speech is NOT measured here (EO stays pending).
 */
import { expect, test } from '@playwright/test'
import { A_ID, B_ID, LEGAL_NAME, PARTY, candidate, candidatePage, mutationView } from '../src/fixtures.js'
import { TLane, activeElement, json, shot, writeJsonEvidence } from '../src/tHarness.js'

test('T-08a the create flow completes with the keyboard only; every stop has a visible focus; the success is announced once', async ({ page }, testInfo) => {
  const slug = 't08a-keyboard-only-create'
  const lane = await TLane.install(page, {
    preferCompany: A_ID,
    capabilities: () => ['cari.parties.list', 'cari.parties.create', 'cari.accounts.link'],
    create: () => json(mutationView(), 201),
  })
  await page.goto('/cari/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni Cari' })).toBeVisible({ timeout: 60_000 })

  // Walk the Tab order from the top of the document until the kind select.
  const stops: Awaited<ReturnType<typeof activeElement>>[] = []
  await page.locator('body').click({ position: { x: 1, y: 1 } })
  for (let i = 0; i < 25; i += 1) {
    await page.keyboard.press('Tab')
    const stop = await activeElement(page)
    stops.push(stop)
    if (stop.id === 'cari-create-kind') break
  }
  expect(stops.at(-1)?.id, 'the kind select is reachable by Tab').toBe('cari-create-kind')
  expect(stops.every((s) => s.focusVisible), 'every keyboard stop shows a visible focus').toBe(true)

  await page.keyboard.press('ArrowDown') // native select: first option
  await expect(page.getByLabel('Taraf türü')).toHaveValue('1')
  await page.keyboard.press('Tab')
  expect((await activeElement(page)).id).toBe('cari-create-legal-name')
  await page.keyboard.type(LEGAL_NAME)
  await page.keyboard.press('Tab') // trade name
  await page.keyboard.press('Tab') // first role checkbox
  expect((await activeElement(page)).id).toBe('cari-create-role-customer')
  await page.keyboard.press('Space')
  await expect(page.getByRole('checkbox', { name: 'Müşteri' })).toBeChecked()
  // Tab to the submit button and press Enter.
  for (let i = 0; i < 6; i += 1) {
    await page.keyboard.press('Tab')
    if ((await activeElement(page)).text === 'Cari oluştur') break
  }
  expect((await activeElement(page)).text).toBe('Cari oluştur')
  await shot(page, 'T', slug, 'focus-on-submit')
  await page.keyboard.press('Enter')
  await expect(page.locator('[data-outcome="created"]')).toBeVisible()
  expect(lane.creates()).toHaveLength(1)
  // The announcement is in a status live region, exactly once.
  const status = page.locator('[role="status"]').filter({ hasText: 'Cari CAR-000001 oluşturuldu.' })
  await expect(status).toHaveCount(1)
  await expect(page.getByText('Cari CAR-000001 oluşturuldu.')).toHaveCount(1)
  await shot(page, 'T', slug, 'created-announced')
  writeJsonEvidence('T', 'measurements', `${slug}.json`, {
    tabStopsToKind: stops.map((s) => ({ id: s.id, tag: s.tag, text: s.text, focusVisible: s.focusVisible, outline: s.outlineStyle })),
    keyboardOnly: true,
    announcementsInStatusRegion: 1,
    screenReaderSpeechMeasured: false,
  })
  await lane.finish(testInfo, slug)
})

test('T-08b reflow: no page-level horizontal scroll at 640 and 320 CSS px; controls stay reachable', async ({ page }, testInfo) => {
  const slug = 't08b-reflow-640-320'
  const lane = await TLane.install(page, {
    preferCompany: B_ID,
    candidates: () => json(candidatePage([candidate(), candidate({ id: '22222222-2222-2222-2222-222222222222', code: 'CAR-000002' })])),
  })
  const measurements: Record<string, unknown>[] = []
  for (const width of [640, 320]) {
    await page.setViewportSize({ width, height: 900 })
    for (const route of ['/cari/yeni', '/cari/sirkete-bagla']) {
      await page.goto(route)
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 60_000 })
      if (route === '/cari/sirkete-bagla') await expect(page.getByRole('row', { name: /CAR-000001/ })).toBeVisible()
      const m = await page.evaluate(() => ({
        innerWidth: window.innerWidth,
        scrollWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
        bodyScrollWidth: document.body.scrollWidth,
      }))
      const submit = page.getByRole('button', { name: route === '/cari/yeni' ? 'Cari oluştur' : 'Şirkete bağla' })
      await submit.scrollIntoViewIfNeeded()
      const box = await submit.boundingBox()
      const withinViewport = box !== null && box.x >= 0 && box.x + box.width <= m.innerWidth + 1
      expect(m.scrollWidth, `${route} at ${width}px must not scroll horizontally`).toBeLessThanOrEqual(m.innerWidth)
      expect(withinViewport, `${route} submit control fits at ${width}px`).toBe(true)
      await shot(page, 'T', slug, `${width}px-${route.replace(/\W+/g, '-')}`)
      measurements.push({ width, route, ...m, submitBox: box, withinViewport })
    }
  }
  writeJsonEvidence('T', 'measurements', `${slug}.json`, {
    method: 'viewport widths 640/320 CSS px as 200%/400% equivalents of a 1280 px layout; browser zoom UI not driven',
    measurements,
    party: PARTY,
  })
  await lane.finish(testInfo, slug)
})
