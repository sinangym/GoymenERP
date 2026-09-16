/**
 * The lane's screens, mounted through the ROUTER THE APP ACTUALLY BUILDS.
 *
 * Every other test in this folder mounts `organizationRoutes` directly — the array
 * `shell/router.tsx` spreads — so they prove the routes work, but not that the shell
 * still spreads them. Deleting `...organizationRoutes` from `shell/router.tsx` left all
 * 1382 frontend tests green (measured by PR #268 review #2), and the symptom in the app
 * would be the catch-all answering `/ayarlar/organizasyon*`: a screen the server grants
 * and the client silently does not mount.
 *
 * So this file imports `shellRoutes` itself. It asserts reachability and the closed case
 * and nothing about Cari or Ürün: their routes are in the table but never entered.
 *
 * Still a component/DOM test over fixtures — not real API, real SQL or browser acceptance.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import { shellRoutes } from '../shell/router'
import {
  LOC_H0,
  LOC_H1,
  WC_PAINT,
  WC_TUBE,
  WORK_CENTERS_ONLY_CAPABILITIES,
  installFetch,
  json,
  renderAt,
  requestsFor,
  workCenter,
} from './testHarness'
import { workCenterListPath } from './capabilities'

const ROWS = [
  workCenter(WC_PAINT, LOC_H0, 'WC-PAINT', 'Boyama Hattı'),
  workCenter(WC_TUBE, LOC_H1, 'WC-TUBE', 'Boru / Profil Hattı', { kind: 'TubeProfile' }),
]

function stub(capabilities: readonly string[]) {
  return installFetch({
    capabilities,
    respond: (url) => (url.includes('/organization/work-centers') ? json(ROWS) : undefined),
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ORG-03 through the shell’s own route table', () => {
  it('is reachable at its published path when the server grants it', async () => {
    const recorded = stub(WORK_CENTERS_ONLY_CAPABILITIES)
    renderAt(workCenterListPath(), { routeTable: shellRoutes })

    expect(await screen.findByRole('heading', { name: 'İş Merkezleri' })).toBeTruthy()
    expect(await screen.findByText('WC-PAINT')).toBeTruthy()
    // Reachability means the screen's own read ran, not merely that a heading rendered.
    expect(requestsFor(recorded, '/organization/work-centers').length).toBeGreaterThan(0)
  })

  it('closes onto the neutral not-found block, and calls nothing, without the grant', async () => {
    // Same path, same router; only the server's snapshot differs. An ungranted capability
    // and an unknown path are deliberately indistinguishable (ERP-000K §10.2).
    const recorded = stub([])
    renderAt(workCenterListPath(), { routeTable: shellRoutes })

    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'İş Merkezleri' })).toBeNull()
    expect(requestsFor(recorded, '/organization/work-centers')).toHaveLength(0)
  })
})
