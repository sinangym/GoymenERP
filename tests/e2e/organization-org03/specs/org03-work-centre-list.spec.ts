import { expect, test, type Page } from '@playwright/test'
import { READER_ACTOR, WORK_CENTERS_ONLY_ACTOR, readEnvironment } from '../src/env'

/**
 * ORG-03 (`browser:org03:ci`) — the work-centre list, in a real browser against the disposable stack
 * built from the exact head: real API, real SQL, the `normal` capability profile, the authorized web.
 *
 * WHAT THIS SUITE IS FOR. The slice's claim is a BROWSER fact and cannot be shown anywhere else: a user
 * holding `organization.work-centers.read` and NOT `organization.locations.read` sees the İş Merkezleri
 * entry in the sidebar and opens ORG-03 from it. Unit and component layers can show the screen renders;
 * only this layer shows the ENTRY EXISTS for that user — and that ORG-01, the screen a link would have
 * lived inside, does not open for them at all. That pair is why ORG-03 is its own sidebar entry (K5).
 *
 * READ-ONLY. Nothing here writes: no command, no form, no state change. Everything it reads is in the
 * built-in organization seed (five work centres across four locations of the GDC company), so the suite
 * needs no fixture and no seed envelope.
 *
 * WHAT IT DOES NOT COVER, so nobody reads its green as more than it is: the MIXED case — some rows
 * openable, some not — is absent, because the browser-suite contract refuses a narrowly-scoped actor
 * (see infra/browser-suites/org03.override.yml). That is the case `canOpenLocationDetail` exists to
 * discriminate, so browser evidence for the field itself remains owed. Covered here are the two ends:
 * all rows closed, and a row that opens.
 */

const { testedCommit } = readEnvironment()

const WORK_CENTRE_LIST_ROUTE = '/ayarlar/organizasyon/is-merkezleri'
const LOCATION_LIST_ROUTE = '/ayarlar/organizasyon'
const NAV_LABEL = 'İş Merkezleri'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'

/** The five seeded work centres of GDC, by code — the whole authorized set for a company-wide reader. */
const SEEDED_WORK_CENTRE_CODES = ['WC-PAINT', 'WC-TUBE', 'WC-SLIT', 'WC-CR', 'WC-CUT'] as const

/** The sentence the screen shows in place of the link when the server says the card is out of reach. */
const CLOSED_LOCATION_TEXT = 'Lokasyon kartı bu yetkiyle açılamıyor'

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

/** The sidebar entry, scoped to the nav landmark so it can never be confused with the screen's own heading. */
function navEntry(page: Page) {
  return page.getByRole('navigation', { name: 'Modül navigasyonu' }).getByRole('link', { name: NAV_LABEL })
}

test.beforeAll(() => {
  // The run is bound to the head it was built from; a suite that cannot say which commit it tested is
  // not evidence about any commit.
  expect(testedCommit).toMatch(/^[0-9a-f]{40}$/)
})

test('ORG03-01 a work-centre-only reader reaches the screen FROM THE SIDEBAR, and every row says the location card is closed', async ({ page }) => {
  await signInAs(page, WORK_CENTERS_ONLY_ACTOR)
  await page.goto('/')

  // The entry point itself is the claim. Navigating straight to the route would prove the route opens
  // and say nothing about whether this user could ever have found it.
  const entry = navEntry(page)
  await expect(entry).toBeVisible({ timeout: 30_000 })
  await expect(entry).toHaveAttribute('href', WORK_CENTRE_LIST_ROUTE)
  await entry.click()

  await expect(page.getByRole('heading', { level: 1, name: NAV_LABEL })).toBeVisible({ timeout: 30_000 })
  const table = page.getByRole('table')
  await expect(table).toBeVisible()
  for (const code of SEEDED_WORK_CENTRE_CODES) {
    await expect(table.getByText(code, { exact: true }), `seeded work centre ${code} is listed`).toBeVisible()
  }

  // EVERY row is closed for this actor, and it is asserted as a COUNT, not as "at least one": a single
  // closed row would also be true if the others wrongly linked.
  await expect(table.getByText(CLOSED_LOCATION_TEXT)).toHaveCount(SEEDED_WORK_CENTRE_CODES.length)
  // And the screen draws no location link at all — the inverse of the same fact, so a renamed sentence
  // cannot make the first assertion pass vacuously.
  await expect(table.getByRole('link')).toHaveCount(0)
})

test('ORG03-02 the same reader has NO location list to reach it from: ORG-01 is neither in the sidebar nor openable', async ({ page }) => {
  await signInAs(page, WORK_CENTERS_ONLY_ACTOR)
  await page.goto('/')
  await expect(navEntry(page)).toBeVisible({ timeout: 30_000 })

  // This is the other half of K5 and the reason ORG-03 is not a link inside ORG-01: for this user that
  // screen does not exist. An entry point inside a screen they cannot open is no entry point.
  await expect(
    page.getByRole('navigation', { name: 'Modül navigasyonu' }).getByRole('link', { name: 'Organizasyon' }),
  ).toHaveCount(0)

  await page.goto(LOCATION_LIST_ROUTE)
  // Neutral not-found, exactly as an unknown path gets: an ungranted route must not be distinguishable
  // from one that does not exist.
  await expect(page.getByRole('heading', { level: 1, name: 'Organizasyon' })).toHaveCount(0)
  await expect(page.getByRole('alert')).toBeVisible({ timeout: 30_000 })
})

test('ORG03-03 a reader holding the location name as well gets the link, and it opens the location card', async ({ page }) => {
  await signInAs(page, READER_ACTOR)
  await page.goto('/')

  const entry = navEntry(page)
  await expect(entry).toBeVisible({ timeout: 30_000 })
  await entry.click()
  const table = page.getByRole('table')
  await expect(table).toBeVisible({ timeout: 30_000 })

  // The other end of the pair: with the location name, every row links and none carries the closed
  // sentence. Both directions again, so neither can pass by the screen simply rendering nothing.
  await expect(table.getByText(CLOSED_LOCATION_TEXT)).toHaveCount(0)
  await expect(table.getByRole('link')).toHaveCount(SEEDED_WORK_CENTRE_CODES.length)

  await table.getByRole('link').first().click()
  // ORG-02 opened for real — the link is not merely present, it resolves through the real API.
  await expect(page).toHaveURL(/\/ayarlar\/organizasyon\/lokasyon\/[0-9a-f-]{36}$/, { timeout: 30_000 })
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible({ timeout: 30_000 })
})
