import { expect, test, type Page, type Response } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()
const { preparerActor, readerActor } = env.seed
const DRAFTS_PATH = '/api/v1/sales/drafts'
const LIST_ROUTE = '/satis/siparisler'
const DEVELOPMENT_USER_HEADER = 'X-Development-User'
const COMPANY_A = '10000000-0000-0000-0000-000000000001'
const COMPANY_B = '10000000-0000-0000-0000-000000000002'
const LONG_LEGAL_NAME =
  'Teslimat müşterisi KARADENİZ ÇELİK DÖKÜM MAKİNA İMALAT SANAYİ VE DIŞ TİCARET ANONİM ŞİRKETİ GEBZE ÜRETİM VE LOJİSTİK TESİSLERİ'

interface PartyDisplay {
  readonly partyId: string
  readonly code: string
  readonly legalName: string
  readonly tradeName: string | null
}

interface DraftSummary {
  readonly id: string
  readonly partyDisplay: PartyDisplay
}

interface DraftPage {
  readonly items: readonly DraftSummary[]
  readonly nextCursor: string | null
}

function signInAs(page: Page, actor: string): Promise<void> {
  return page.setExtraHTTPHeaders({ [DEVELOPMENT_USER_HEADER]: actor })
}

function isPreparingList(response: Response, withCursor: boolean): boolean {
  if (response.request().method() !== 'GET') return false
  const url = new URL(response.url())
  return url.pathname === DRAFTS_PATH
    && url.searchParams.get('approvalState') === 'Preparing'
    && url.searchParams.has('cursor') === withCursor
}

function partyText(party: PartyDisplay): string {
  return `${party.code} — ${party.legalName}${party.tradeName === null ? '' : ` (${party.tradeName})`}`
}

test('SALES-DS-01 real list keeps exact Cari, cursor retry, context fence and 390px cards', async ({ page, context }) => {
  await signInAs(page, preparerActor)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(LIST_ROUTE)
  await expect(page.getByRole('heading', { level: 1, name: 'Satış siparişi taslakları' })).toBeVisible({ timeout: 30_000 })

  const firstPageAnswer = page.waitForResponse((response) => isPreparingList(response, false))
  await page.getByLabel('İç onay durumu').selectOption('Preparing')
  const firstPage = await firstPageAnswer
  expect(firstPage.status()).toBe(200)
  const first = await firstPage.json() as DraftPage
  expect(first.items).toHaveLength(50)
  expect(first.nextCursor).toBeTruthy()
  const row = first.items[0]
  expect(row.partyDisplay.legalName).toContain(LONG_LEGAL_NAME)
  const exactParty = partyText(row.partyDisplay)

  const party = page.getByTestId(`sales-row-party-${row.id}`)
  await expect(party).toHaveText(exactParty)
  await expect(party.getByRole('link', { name: exactParty })).toHaveAttribute('href', `/cari/${row.partyDisplay.partyId}`)
  await expect(page.getByRole('table')).toBeVisible()
  await expect(page.getByTestId('sales-draft-cards')).toHaveCount(0)

  const initialRows = page.locator('tbody tr')
  await expect(initialRows).toHaveCount(50)
  const cursorRequests: string[] = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (request.method() === 'GET' && url.pathname === DRAFTS_PATH && url.searchParams.has('cursor')) {
      cursorRequests.push(url.searchParams.get('cursor') ?? '')
    }
  })

  await context.setOffline(true)
  await page.getByRole('button', { name: 'Daha fazla göster' }).click()
  await expect(page.getByText('Sonraki sayfa alınamadı. Gösterilen satırlar geçerli.')).toBeVisible()
  await expect(initialRows).toHaveCount(50)
  await context.setOffline(false)

  const retryAnswer = page.waitForResponse((response) => isPreparingList(response, true))
  await page.getByRole('button', { name: 'Tekrar dene' }).click()
  const secondPageAnswer = await retryAnswer
  expect(secondPageAnswer.status()).toBe(200)
  const second = await secondPageAnswer.json() as DraftPage
  expect(second.items.length).toBeGreaterThan(0)
  await expect(page.locator('tbody tr')).toHaveCount(first.items.length + second.items.length)
  expect(cursorRequests).toEqual([first.nextCursor, first.nextCursor])
  await expect(page.getByText('Listenin sonu.')).toBeVisible()

  // The suite-only second assignment exposes the real shell company picker without adding a role or
  // permission. Company B has no seeded Sales drafts: its terminal first page must remove every row,
  // Cari link and continuation state that belonged to company A.
  const companyPicker = page.getByRole('combobox', { name: 'Şirket' })
  const companyBFirstPage = page.waitForResponse((response) => isPreparingList(response, false))
  await companyPicker.selectOption(COMPANY_B)
  await expect(companyPicker).toHaveValue(COMPANY_B)
  const companyBAnswer = await companyBFirstPage
  expect(companyBAnswer.status()).toBe(200)
  expect((await companyBAnswer.json() as DraftPage).items).toHaveLength(0)
  await expect(page.getByTestId(`sales-row-party-${row.id}`)).toHaveCount(0)
  await expect(page.locator('a[href^="/cari/"]')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Daha fazla göster' })).toHaveCount(0)
  await expect(page.getByText('Listenin sonu.')).toHaveCount(0)
  await expect(page.getByText(/henüz kayıt yok/i)).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)

  // Return to A before the existing actor-boundary assertions, proving the successor read is fresh
  // rather than a one-way navigation or a locally hidden table.
  const companyAFirstPage = page.waitForResponse((response) => isPreparingList(response, false))
  await companyPicker.selectOption(COMPANY_A)
  await expect(companyPicker).toHaveValue(COMPANY_A)
  expect((await companyAFirstPage).status()).toBe(200)
  await expect(page.getByTestId(`sales-row-party-${row.id}`)).toHaveText(exactParty)
  await expect(page.getByRole('table')).toBeVisible()

  // A fresh actor context must not retain the previous actor's published Cari link.
  await signInAs(page, readerActor)
  await page.reload()
  await expect(page.getByRole('heading', { level: 1, name: 'Satış siparişi taslakları' })).toBeVisible({ timeout: 30_000 })
  const readerParty = page.getByTestId(`sales-row-party-${row.id}`)
  await expect(readerParty).toHaveText(exactParty)
  await expect(readerParty.getByRole('link')).toHaveCount(0)

  // The same real API data reflows to cards at the phone width without page-level horizontal overflow.
  await page.setViewportSize({ width: 390, height: 844 })
  await page.reload()
  const cards = page.getByTestId('sales-draft-cards')
  await expect(cards).toBeVisible()
  await expect(page.getByRole('table')).toHaveCount(0)
  const mobileParty = page.getByTestId(`sales-row-party-${row.id}`)
  await expect(mobileParty).toHaveText(exactParty)
  await expect(mobileParty.getByRole('link')).toHaveCount(0)
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)

  // Every accepted read remains company-bound; a foreign company context cannot disclose the fixture rows.
  const foreign = await page.request.get(`${env.apiBaseUrl}${DRAFTS_PATH}?pageSize=50`, {
    headers: {
      [DEVELOPMENT_USER_HEADER]: readerActor,
      'X-Company-Id': '20000000-0000-0000-0000-000000000002',
      'X-ERP-Sales-Read-Contract': 'record-party-display-v1',
    },
  })
  expect(foreign.status()).toBe(403)
  expect(await foreign.text()).not.toContain(row.id)
})
