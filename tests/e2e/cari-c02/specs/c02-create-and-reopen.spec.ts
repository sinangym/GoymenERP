import { expect, test, type Page } from '@playwright/test'
import { CREATOR_ACTOR, OTHER_COMPANY_ACTOR, readEnvironment } from '../src/env'

/**
 * CARI-NORMAL (`browser:c02:ci`) — the create/reopen journey on the NORMAL host, in a real browser
 * against the disposable stack built from the exact head: real API, real SQL, the `normal` capability
 * profile, the authorized web.
 *
 * WHAT THIS SUITE IS FOR, and why nothing already in the repo covers it. Cari was the last module
 * still entirely staged on the normal host: C-01 is list-only inside a disposable envelope, and the
 * `c03` suite that would have exercised the staged profile HAS NO RUNNER and has never run. So no
 * layer showed a normal user creating a counterparty and reading it back. Unit and component tests
 * show the screens render; only this layer shows the journey completes through the real API into a
 * persisted row and back out of it.
 *
 * THE JOURNEY IS THE CRITERION, not a set of page checks: screen → real API → persisted row → close
 * and reopen → the same record. C02-01 and C02-02 are that journey split so a failure names which
 * half broke.
 *
 * IT WRITES, and both registries say so (absent from the validator's read-only set, on the
 * workflow's writing arm). The post-browser SQL verification asserts zero audit and command deltas,
 * which a create must violate — so it is correctly not run for this suite.
 *
 * THE EDGE IS MEASURED, NOT THE HAPPY PATH ALONE. C-03's context is
 * `SingleOrAuthorizedMultiCompanyRead`, wider than this journey needs, and publishing a wider context
 * obliges showing its boundary: C02-03 has a fully authorized actor OF ANOTHER COMPANY fail to reach
 * the record. Using an unauthorized actor instead would have proved the permission fence and said
 * nothing about the company fence — different claims.
 *
 * WHAT IT DOES NOT COVER, so its green is not read as more than it is: C-01 and the link workspace
 * stay staged, so there is no list here and no company-link journey; the contact editor (C-06) is
 * declared only by the C006 acceptance profile and is out of scope entirely.
 */

const { testedCommit } = readEnvironment()

const CREATE_ROUTE = '/cari/yeni'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'

/** Exact strings from src/cari/cariCommandText.ts — a spec that retypes UI text drifts from it silently. */
const CREATE_TITLE = 'Yeni Cari'
const KIND_LABEL = 'Taraf türü'
const LEGAL_NAME_LABEL = 'Hukuki ad'
const SUBMIT_LABEL = 'Cari oluştur'
const SUCCESS_TITLE = 'Cari oluşturuldu'
const SUCCESS_CODE_LABEL = 'Cari kodu'
const LEGAL_PERSON_KIND = 'Tüzel kişi'
const CARD_IDENTITY_HEADING = 'Kimlik'

/** Unique per run so a re-run never collides with a row an earlier run left in a disposable database. */
const LEGAL_NAME = `SENTETIK C02 ${testedCommit.slice(0, 12)}`

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

test.beforeAll(() => {
  // The run is bound to the head it was built from; a suite that cannot say which commit it tested is
  // not evidence about any commit.
  expect(testedCommit).toMatch(/^[0-9a-f]{40}$/)
})

test.describe.configure({ mode: 'serial' })

let createdPartyPath = ''

test('C02-01 a creator reaches C-02 THROUGH THE LIST and the record is persisted through the real API', async ({ page }) => {
  await signInAs(page, CREATOR_ACTOR)
  await page.goto('/')

  // The CANONICAL entry path, walked rather than asserted: sidebar -> C-01 -> the list's single
  // authorised "Yeni". K5 (Sinan, 20 Eylül 2026) puts creation inside the relevant list, so a
  // direct goto to /cari/yeni would pass while proving nothing about the path a user has.
  const nav = page.getByRole('navigation', { name: 'Modül navigasyonu' })
  const listEntry = nav.getByRole('link', { name: 'Cari Kartlar' })
  await expect(listEntry).toBeVisible()

  // And the OTHER half of K5, which only a browser can show: the sidebar must NOT also offer a
  // create entry. Asserting the list link exists says nothing about the duplicate being gone.
  await expect(nav.getByRole('link', { name: 'Yeni Cari' })).toHaveCount(0)

  await listEntry.click()
  await expect(page.getByRole('heading', { name: 'Cari Kartlar' })).toBeVisible()

  await page.getByRole('link', { name: 'Yeni Cari' }).click()
  await expect(page).toHaveURL(new RegExp(`${CREATE_ROUTE}$`))
  await expect(page.getByRole('heading', { name: CREATE_TITLE })).toBeVisible()

  await page.getByLabel(KIND_LABEL).selectOption({ label: LEGAL_PERSON_KIND })
  await page.getByLabel(new RegExp(`^${LEGAL_NAME_LABEL}`)).fill(LEGAL_NAME)
  await page.getByRole('button', { name: SUBMIT_LABEL }).click()

  // The success panel is the API's answer, not the form's optimism: it carries the server-assigned code.
  await expect(page.getByText(SUCCESS_TITLE)).toBeVisible()
  await expect(page.getByText(SUCCESS_CODE_LABEL)).toBeVisible()

  // The detail is published in this head, so the create navigates there rather than summarising.
  await expect(page).toHaveURL(/\/cari\/[0-9a-f-]{36}$/)
  createdPartyPath = new URL(page.url()).pathname
  expect(createdPartyPath).toMatch(/^\/cari\/[0-9a-f-]{36}$/)
})

test('C02-02 closing and reopening the card reads the SAME record back from the server', async ({ page }) => {
  expect(createdPartyPath, 'C02-01 must have created the record').not.toBe('')
  await signInAs(page, CREATOR_ACTOR)

  // Leave the app entirely, then come back by URL: this is "close and reopen", so nothing may be
  // served from the state the create left behind.
  await page.goto('/')
  await page.goto(createdPartyPath)

  await expect(page.getByRole('heading', { name: new RegExp(LEGAL_NAME) })).toBeVisible()
  await expect(page.getByRole('heading', { name: CARD_IDENTITY_HEADING })).toBeVisible()

  // The list IS published in this head, so the card's return is live and must work — the guards
  // added alongside suppress it only when the list is absent, and a guard that suppressed it here
  // would be a regression rather than a safety. Walking it also closes the loop the user walks.
  const back = page.getByRole('link', { name: /listesine dön/ })
  await expect(back).toBeVisible()
  await back.click()
  await expect(page.getByRole('heading', { name: 'Cari Kartlar' })).toBeVisible()

  // And the created row is IN the list: "persisted" means the record is where the user looks for
  // it, not merely that a detail URL resolves.
  await expect(page.getByRole('link', { name: new RegExp(LEGAL_NAME) })).toBeVisible()
})

test('C02-03 an actor authorized in ANOTHER company does not reach the record', async ({ page }) => {
  expect(createdPartyPath, 'C02-01 must have created the record').not.toBe('')
  await signInAs(page, OTHER_COMPANY_ACTOR)

  await page.goto(createdPartyPath)

  // Refused, and the name must not leak through the refusal — a 404 body that still renders the
  // heading would be a leak with a passing status code.
  await expect(page.getByRole('heading', { name: new RegExp(LEGAL_NAME) })).toHaveCount(0)
  expect(await page.textContent('body')).not.toContain(LEGAL_NAME)
})
