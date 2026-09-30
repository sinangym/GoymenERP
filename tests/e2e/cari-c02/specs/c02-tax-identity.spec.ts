import { expect, test, type Page } from '@playwright/test'
import { TAX_CHECKER_ACTOR, TAX_MAKER_ACTOR, readEnvironment } from '../src/env'

/**
 * AT-009 (user decision 2026-09-30) — VKN/TCKN on the Cari card, as a user walks it on the normal host against the
 * disposable stack built from the exact head:
 *
 *  T-01 the maker enters a number on the card: a wrong check digit is refused on the screen and nothing is sent; a
 *       valid number is saved, and after closing and reopening the card it is still there (read back from the server).
 *  T-02 the same number on a SECOND card of the company: the warning is shown before saving and the save goes through;
 *       both cards then carry the warning, naming each other.
 *  T-03 the maker asks to change the first card's number: it goes PENDING, the stored number stays, and the maker —
 *       who also holds approve — is offered no decision on their own request.
 *  T-04 a DIFFERENT person (the checker) approves it; reopening the card shows the new number and no pending change.
 *
 * The numbers are derived from the tested commit so a re-run in the same database never trips over an earlier run's
 * cards. THIS SUITE WRITES (see c02-create-and-reopen.spec.ts).
 */

const { testedCommit } = readEnvironment()

const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const CREATE_ROUTE = '/cari/yeni'
const KIND_LABEL = 'Taraf türü'
const LEGAL_NAME_LABEL = 'Hukuki ad'
const SUBMIT_LABEL = 'Cari oluştur'
const LEGAL_PERSON_KIND = 'Tüzel kişi'

/** Exact strings from src/cari/taxIdentity.ts (TAX_IDENTITY_TEXT). */
const NUMBER_LABEL = 'VKN (10 hane) veya TCKN (11 hane)'
const SAVE = 'Kaydet'
const SAVE_ANYWAY = 'Uyarıya rağmen kaydet'
const INVALID_VKN = 'VKN geçersiz: kontrol hanesi tutmuyor.'
const DUPLICATE_TITLE = 'Aynı numara bu şirketin başka cari kartında da kayıtlı'
const REQUEST_CHANGE = 'Numara değişikliği talep et'
const PROPOSED_LABEL = 'Yeni VKN / TCKN'
const REASON_LABEL = 'Değişiklik gerekçesi'
const SEND_REQUEST = 'Onaya gönder'
const PENDING_TITLE = 'Onay bekleyen değişiklik'
const OWN_REQUEST = /Bu talebi siz açtınız/
const APPROVE = 'Onayla'

/** Gelir İdaresi VKN check digit for nine leading digits (the server's rule). */
function vknFor(nine: string): string {
  let sum = 0
  for (let i = 1; i <= 9; i++) {
    const t = (Number(nine[i - 1]) + 10 - i) % 10
    sum += t === 9 ? 9 : (t * 2 ** (10 - i)) % 9
  }
  return nine + String((10 - (sum % 10)) % 10)
}

function digitsFrom(seed: string, count: number): string {
  return [...seed].map((character) => String(parseInt(character, 16) % 10)).join('').slice(0, count)
}

const FIRST_VKN = vknFor(digitsFrom(testedCommit, 9))
const SECOND_VKN = vknFor(digitsFrom(testedCommit.slice(9), 9))
const WRONG_VKN = FIRST_VKN.slice(0, 9) + String((Number(FIRST_VKN[9]) + 1) % 10)
const NAME_PREFIX = `SENTETIK C02 VKN ${testedCommit.slice(0, 12)}`

async function signInAs(page: Page, actor: string): Promise<void> {
  await page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

async function createCard(page: Page, legalName: string): Promise<string> {
  await page.goto(CREATE_ROUTE)
  await page.getByLabel(KIND_LABEL).selectOption({ label: LEGAL_PERSON_KIND })
  await page.getByLabel(new RegExp(`^${LEGAL_NAME_LABEL}`)).fill(legalName)
  const created = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' && new URL(response.url()).pathname === '/api/v1/counterparties',
  )
  await page.getByRole('button', { name: SUBMIT_LABEL }).click()
  const response = await created
  expect(response.status()).toBe(201)
  const body = (await response.json()) as { id?: unknown; code?: unknown }
  if (typeof body.id !== 'string' || typeof body.code !== 'string') throw new Error('create must return id and code')
  await expect(page).toHaveURL(new RegExp(`/cari/${body.id}$`))
  return body.id
}

function taxBlock(page: Page) {
  return page.getByTestId('cari-tax-identity')
}

function taxPut(page: Page, partyId: string, suffix = '') {
  return page.waitForResponse(
    (response) =>
      response.request().method() === 'PUT' &&
      new URL(response.url()).pathname === `/api/v1/counterparties/${partyId}/tax-identity${suffix}`,
  )
}

test.beforeAll(() => {
  expect(testedCommit).toMatch(/^[0-9a-f]{40}$/)
  expect(FIRST_VKN).not.toBe(SECOND_VKN)
})

test.describe.configure({ mode: 'serial' })

let firstId = ''
let secondId = ''

test('T-01 a refused number sends nothing; a valid one is saved and reopens from the server', async ({ page }) => {
  await signInAs(page, TAX_MAKER_ACTOR)
  firstId = await createCard(page, `${NAME_PREFIX} bir`)
  const block = taxBlock(page)
  await expect(block).toHaveAttribute('data-state', 'ready')
  await expect(block.getByText('Girilmemiş')).toBeVisible()

  let putCount = 0
  page.on('request', (request) => {
    if (request.method() === 'PUT' && request.url().includes('/tax-identity')) putCount += 1
  })
  await block.getByLabel(NUMBER_LABEL).fill(WRONG_VKN)
  await block.getByRole('button', { name: SAVE }).click()
  await expect(block.getByText(INVALID_VKN)).toBeVisible()
  expect(putCount).toBe(0)

  await block.getByLabel(NUMBER_LABEL).fill(FIRST_VKN)
  const saved = taxPut(page, firstId)
  await block.getByRole('button', { name: SAVE }).click()
  expect((await saved).status()).toBe(204)
  await expect(block.locator(`[data-tax-identity="${FIRST_VKN}"]`)).toBeVisible()

  // Close and reopen.
  await page.goto('/')
  await page.goto(`/cari/${firstId}`)
  await expect(taxBlock(page).locator(`[data-tax-identity="${FIRST_VKN}"]`)).toBeVisible()
  await expect(taxBlock(page).getByText('(VKN)')).toBeVisible()
})

test('T-02 the same number on another card warns before saving and does not block the save', async ({ page }) => {
  expect(firstId).not.toBe('')
  await signInAs(page, TAX_MAKER_ACTOR)
  secondId = await createCard(page, `${NAME_PREFIX} iki şube`)
  const block = taxBlock(page)
  await block.getByLabel(NUMBER_LABEL).fill(FIRST_VKN)
  await block.getByRole('button', { name: SAVE }).click()
  const warning = block.locator('[data-state="tax-identity-duplicate-before-save"]')
  await expect(warning).toContainText(DUPLICATE_TITLE)
  await expect(warning).toContainText(`${NAME_PREFIX} bir`)

  const saved = taxPut(page, secondId)
  await block.getByRole('button', { name: SAVE_ANYWAY }).click()
  expect((await saved).status()).toBe(204)
  await expect(block.locator(`[data-tax-identity="${FIRST_VKN}"]`)).toBeVisible()
  await expect(block.locator('[data-state="tax-identity-duplicate"]')).toContainText(`${NAME_PREFIX} bir`)

  await page.goto(`/cari/${firstId}`)
  await expect(taxBlock(page).locator('[data-state="tax-identity-duplicate"]')).toContainText(`${NAME_PREFIX} iki şube`)
})

test('T-03 a change of an existing number goes pending and its maker cannot decide it', async ({ page }) => {
  expect(firstId).not.toBe('')
  await signInAs(page, TAX_MAKER_ACTOR)
  await page.goto(`/cari/${firstId}`)
  const block = taxBlock(page)
  await expect(block.getByLabel(NUMBER_LABEL)).toHaveCount(0)
  await block.getByRole('button', { name: REQUEST_CHANGE }).click()
  await block.getByLabel(PROPOSED_LABEL).fill(SECOND_VKN)
  await block.getByLabel(REASON_LABEL).fill('SENTETIK vergi levhası yenilendi')
  const requested = taxPut(page, firstId, '/change-request')
  await block.getByRole('button', { name: SEND_REQUEST }).click()
  expect((await requested).status()).toBe(204)

  await page.goto('/')
  await page.goto(`/cari/${firstId}`)
  const reopened = taxBlock(page)
  await expect(reopened.locator(`[data-tax-identity="${FIRST_VKN}"]`)).toBeVisible()
  const pending = reopened.locator('[data-state="tax-identity-pending"]')
  await expect(pending).toContainText(PENDING_TITLE)
  await expect(pending.locator(`[data-proposed-tax-identity="${SECOND_VKN}"]`)).toBeVisible()
  await expect(pending.getByText(OWN_REQUEST)).toBeVisible()
  await expect(pending.getByRole('button', { name: APPROVE })).toHaveCount(0)
})

test('T-04 a different person approves it and the reopened card shows the new number', async ({ page }) => {
  expect(firstId).not.toBe('')
  await signInAs(page, TAX_CHECKER_ACTOR)
  await page.goto(`/cari/${firstId}`)
  const pending = taxBlock(page).locator('[data-state="tax-identity-pending"]')
  await expect(pending).toBeVisible()
  const approved = taxPut(page, firstId, '/change-request/decision')
  await pending.getByRole('button', { name: APPROVE }).click()
  expect((await approved).status()).toBe(204)

  await page.goto('/')
  await page.goto(`/cari/${firstId}`)
  const block = taxBlock(page)
  await expect(block.locator(`[data-tax-identity="${SECOND_VKN}"]`)).toBeVisible()
  await expect(block.locator('[data-state="tax-identity-pending"]')).toHaveCount(0)
  await expect(block.locator('[data-state="tax-identity-last-approved"]')).toBeVisible()
})
