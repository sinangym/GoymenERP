import { expect, test, type Page } from '@playwright/test'
import {
  ACCESS_ADMIN_ACTOR,
  ACCESS_ADMIN_OTHER_COMPANY_ACTOR,
  CREATOR_ACTOR,
  CREATOR_USER_ID,
  OTHER_COMPANY_USER_ID,
  readEnvironment,
} from '../src/env'

/**
 * AT-004 read-only access view ("Kullanıcılar ve yetkiler") on the NORMAL host, in a real browser against the
 * disposable stack: real API, real SQL, the `normal` capability profile, the authorized web.
 *
 * WHY IN c02: the suite already seeds two actors who differ only by company, each holding a synthetic role with an
 * approver — exactly the grants an access view must show, and exactly the pair a company fence must separate. The
 * two administrators added for this file hold `identity.assignments.read` ONLY, company-wide, one per company.
 *
 * THE JOURNEY IS THE CRITERION: sidebar → pick a user → roles, permissions, scope, validity, approver → open the
 * role → its permissions and holders → back keeps the selection → reload shows the same. ID-03 measures the
 * company fence with a FULLY AUTHORIZED administrator of the other company, not an unauthorized one. ID-04 shows a
 * user without the read gets no entry and no read.
 *
 * IT WRITES NOTHING. Every request this file makes is a GET; ID-01 asserts that on the wire.
 */

const { testedCommit } = readEnvironment()

const ROUTE = '/ayarlar/kullanicilar-ve-yetkiler'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'

/** Exact strings from src/identity/identityText.ts and the c02 override's synthetic names. */
const NAV_LABEL = 'Kullanıcılar ve yetkiler'
const CREATOR_NAME = 'SENTETIK browser-suite:c02 cari oluşturucu'
const OTHER_COMPANY_NAME = 'SENTETIK browser-suite:c02 diğer şirketin yetkili kullanıcısı'
const CREATOR_ROLE_NAME = 'SENTETIK browser-suite:c02 cari oluşturma ve okuma rolü'
const CREATOR_ROLE_CODE = 'BROWSER_C02_CREATOR'
const APPROVER_NAME = 'Grup Geliştirme Yöneticisi'

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

test.beforeAll(() => {
  expect(testedCommit).toMatch(/^[0-9a-f]{40}$/)
})

test.describe.configure({ mode: 'serial' })

test('ID-01 an administrator reaches the view from the sidebar and reads one user’s grants', async ({ page }) => {
  const methods: string[] = []
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/identity/access/')) methods.push(request.method())
  })
  await signInAs(page, ACCESS_ADMIN_ACTOR)
  await page.goto('/')

  const nav = page.getByRole('navigation', { name: 'Modül navigasyonu' })
  await nav.getByRole('link', { name: NAV_LABEL }).click()
  await expect(page.getByRole('heading', { name: NAV_LABEL, level: 1 })).toBeVisible()

  const users = page.getByRole('list', { name: 'Kullanıcılar' })
  // Company A's holder is listed; company B's is not.
  await expect(users.getByRole('link', { name: new RegExp(CREATOR_NAME) })).toBeVisible()
  await expect(users.getByRole('link', { name: new RegExp(OTHER_COMPANY_NAME) })).toHaveCount(0)

  const detailResponse = page.waitForResponse(
    (response) => new URL(response.url()).pathname === `/api/v1/identity/access/users/${CREATOR_USER_ID}`,
  )
  await users.getByRole('link', { name: new RegExp(CREATOR_NAME) }).click()
  expect((await detailResponse).status()).toBe(200)
  await expect(page).toHaveURL(new RegExp(`\\?kullanici=${CREATOR_USER_ID}$`))

  await expect(page.getByRole('heading', { name: CREATOR_NAME, level: 2 })).toBeVisible()
  const row = page.getByRole('table', { name: 'Rol atamaları' }).getByRole('row', { name: new RegExp(CREATOR_ROLE_NAME) })
  await expect(row.getByText(CREATOR_ROLE_CODE, { exact: true })).toBeVisible()
  await expect(row.getByText('Geçerli', { exact: true })).toBeVisible()
  await expect(row.getByText('Süresiz', { exact: true })).toBeVisible()
  await expect(row.getByText('Şirket geneli', { exact: true })).toBeVisible()
  await expect(row.getByText(APPROVER_NAME, { exact: true })).toBeVisible()

  const effective = page.getByRole('list', { name: 'Şu an geçerli yetkiler' })
  await expect(effective.getByText('counterparty.parties.create', { exact: true })).toBeVisible()
  await expect(effective.getByText('counterparty.parties.read', { exact: true })).toBeVisible()

  // Read-only on the screen and on the wire.
  await expect(page.getByRole('main').getByRole('button')).toHaveCount(0)
  expect(methods.length).toBeGreaterThan(0)
  expect(methods.every((method) => method === 'GET')).toBe(true)
})

test('ID-02 opening the role, going back and reloading keep the same user selected', async ({ page }) => {
  await signInAs(page, ACCESS_ADMIN_ACTOR)
  await page.goto(`${ROUTE}?kullanici=${CREATOR_USER_ID}`)
  await expect(page.getByRole('heading', { name: CREATOR_NAME, level: 2 })).toBeVisible()

  await page
    .getByRole('table', { name: 'Rol atamaları' })
    .getByRole('link', { name: CREATOR_ROLE_NAME })
    .click()
  await expect(page.getByRole('heading', { name: CREATOR_ROLE_NAME, level: 1 })).toBeVisible()
  const permissions = page.getByRole('list', { name: 'Rolün yetkileri' })
  await expect(permissions.getByText('counterparty.parties.create', { exact: true })).toBeVisible()
  await expect(permissions.getByText('counterparty.parties.read', { exact: true })).toBeVisible()
  const holders = page.getByRole('table', { name: 'Bu şirketteki sahipleri' })
  await expect(holders.getByRole('link', { name: CREATOR_NAME })).toBeVisible()
  await expect(holders.getByText(OTHER_COMPANY_NAME)).toHaveCount(0)

  await page.getByRole('navigation', { name: 'Geri' }).getByRole('link').click()
  await expect(page).toHaveURL(new RegExp(`${ROUTE}\\?kullanici=${CREATOR_USER_ID}$`))
  const selected = page.getByRole('list', { name: 'Kullanıcılar' }).getByRole('link', { name: new RegExp(CREATOR_NAME) })
  await expect(selected).toHaveAttribute('aria-current', 'true')

  // Reopen: a fresh load of the same address reads the same user back from the server.
  await page.reload()
  await expect(page.getByRole('heading', { name: CREATOR_NAME, level: 2 })).toBeVisible()
  await expect(
    page.getByRole('table', { name: 'Rol atamaları' }).getByRole('row', { name: new RegExp(CREATOR_ROLE_NAME) }),
  ).toBeVisible()
})

test('ID-03 the other company’s fully authorized administrator cannot see company A’s grants', async ({ page }) => {
  await signInAs(page, ACCESS_ADMIN_OTHER_COMPANY_ACTOR)
  await page.goto(ROUTE)
  const users = page.getByRole('list', { name: 'Kullanıcılar' })
  await expect(users.getByRole('link', { name: new RegExp(OTHER_COMPANY_NAME) })).toBeVisible()
  await expect(users.getByRole('link', { name: new RegExp(CREATOR_NAME) })).toHaveCount(0)

  const denied = page.waitForResponse(
    (response) => new URL(response.url()).pathname === `/api/v1/identity/access/users/${CREATOR_USER_ID}`,
  )
  await page.goto(`${ROUTE}?kullanici=${CREATOR_USER_ID}`)
  const response = await denied
  expect(response.status()).toBe(404)
  const body = await response.text()
  expect(body).not.toContain(CREATOR_USER_ID)
  expect(body).not.toContain(CREATOR_NAME)
  await expect(page.getByText(/Bulunamadı — kullanıcı yetkileri/)).toBeVisible()
  await expect(page.getByText(CREATOR_NAME)).toHaveCount(0)

  // Its own company's user opens normally.
  await page.goto(`${ROUTE}?kullanici=${OTHER_COMPANY_USER_ID}`)
  await expect(page.getByRole('heading', { name: OTHER_COMPANY_NAME, level: 2 })).toBeVisible()
})

test('ID-04 a user without the read gets no entry and no read', async ({ page }) => {
  const reads: string[] = []
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/identity/access/')) reads.push(request.url())
  })
  await signInAs(page, CREATOR_ACTOR)
  await page.goto('/')
  const nav = page.getByRole('navigation', { name: 'Modül navigasyonu' })
  await expect(nav.getByRole('link', { name: 'Cari Kartlar' })).toBeVisible()
  await expect(nav.getByRole('link', { name: NAV_LABEL })).toHaveCount(0)

  await page.goto(`${ROUTE}?kullanici=${CREATOR_USER_ID}`)
  await expect(page.getByText(/Bulunamadı/).first()).toBeVisible()
  await expect(page.getByRole('heading', { name: NAV_LABEL })).toHaveCount(0)
  expect(reads).toEqual([])
})
