/**
 * ME-005 (zoom part): the link workspace at the LAYOUT EQUIVALENT of 200 % and 400 %
 * browser zoom on a 1280 px window — 640 and 320 CSS px viewports at device scale 2 and 4,
 * set through the Chromium DevTools Emulation domain in this real browser. The browser's
 * own zoom control (Ctrl +) is a browser-UI accelerator that CDP/Playwright cannot press
 * in a headless session; that control is therefore NOT driven and is reported as open.
 * Measured: no page-level horizontal scroll, the observed devicePixelRatio, and that the
 * controls stay reachable and complete a link.
 */
import path from 'node:path'
import { mkdirSync } from 'node:fs'
import { expect, test } from '../src/test'
import { B_ID, PARTY_2, candidate, candidatePage, linkedView } from '../../cari-acceptance-009/src/fixtures'
import { Lane, json } from '../src/harness'
import { openLink, choose, submit } from '../src/ui'
import { runDir } from '../paths'

const P2 = candidate({ id: PARTY_2, code: 'CAR-000002' })
const LEVELS = [
  { label: '200%', width: 640, height: 400, dpr: 2 },
  { label: '400%', width: 320, height: 200, dpr: 4 },
] as const

for (const level of LEVELS) {
  test(`ME-005 zoom-equivalent ${level.label}: ${level.width} CSS px at device scale ${level.dpr} — no horizontal scroll, DPR observed, controls reachable and the link completes`, async ({ page }, info) => {
    const lane = await Lane.install(page, {
      slug: `zoom-${level.width}`, company: B_ID,
      candidates: () => json(candidatePage([candidate(), P2])),
      link: ({ roles }) => json(linkedView(roles ?? 2), 201),
    })
    const cdp = await page.context().newCDPSession(page)
    await cdp.send('Emulation.setDeviceMetricsOverride', { width: level.width, height: level.height, deviceScaleFactor: level.dpr, mobile: false })
    await openLink(page)
    const metrics = await page.evaluate(() => ({
      innerWidth: window.innerWidth, innerHeight: window.innerHeight,
      scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth,
      bodyScrollWidth: document.body.scrollWidth, devicePixelRatio: window.devicePixelRatio,
      visualViewportScale: window.visualViewport?.scale ?? null,
    }))
    expect(metrics.devicePixelRatio).toBe(level.dpr)
    expect(metrics.innerWidth).toBe(level.width)
    expect(metrics.scrollWidth, `no page-level horizontal scroll at ${level.width} CSS px`).toBeLessThanOrEqual(metrics.innerWidth)
    await choose(page)
    const button = submit(page)
    await button.scrollIntoViewIfNeeded()
    const box = await button.boundingBox()
    const withinViewport = box !== null && box.x >= 0 && box.x + box.width <= metrics.innerWidth + 1
    expect(withinViewport, 'the submit control fits the viewport width').toBe(true)
    const screens = path.join(runDir, 'screens')
    mkdirSync(screens, { recursive: true })
    await page.screenshot({ path: path.join(screens, `zoom-${level.width}-before-submit.png`), fullPage: false })
    await button.click()
    await expect(page.locator('[data-outcome="linked"]')).toHaveCount(1)
    await page.screenshot({ path: path.join(screens, `zoom-${level.width}-linked.png`), fullPage: false })
    expect(lane.of('link')).toHaveLength(1)
    expect(lane.externalBlocked).toBe(0)
    await lane.finish(info, {
      me: ['ME-005'], level: level.label, method: 'CDP Emulation.setDeviceMetricsOverride: layout equivalent of browser zoom (CSS px viewport at device scale); browser UI zoom control not driven',
      metrics, submitBox: box, withinViewport, browserUiZoomDriven: false,
    })
  })
}
