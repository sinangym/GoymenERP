import { expect, test, type BrowserContext, type Page, type Request, type Response, type Route } from '@playwright/test'
import { loadFin01Env } from '../support/fin01-env'

const env = loadFin01Env()

async function asActor(context: BrowserContext, actor: string): Promise<Set<Request>> {
  const rewritten = new Set<Request>()
  await context.route('**/api/v1/**', async (route: Route) => {
    rewritten.add(route.request())
    await route.fallback({ headers: { ...route.request().headers(), 'x-development-user': actor } })
  })
  return rewritten
}

function watchAnswers(page: Page): Request[] {
  const answered: Request[] = []
  page.on('requestfinished', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/')) answered.push(request)
  })
  return answered
}

function expectEveryAnswerRewritten(answered: readonly Request[], rewritten: ReadonlySet<Request>) {
  const foreign = answered.filter((request) => !rewritten.has(request)).map((request) => request.url())
  expect(foreign, 'every API answer must belong to a request sent as the bound actor').toEqual([])
}

function isAllocationList(response: Response): boolean {
  return response.request().method() === 'GET'
    && new URL(response.url()).pathname === '/api/v1/finance/allocations'
}

test('FIN01 allocation reader closes and reopens the same persisted allocation', async ({ page, context }) => {
  const rewritten = await asActor(context, env.seed.proposerActor)
  const answered = watchAnswers(page)

  const firstList = page.waitForResponse(isAllocationList)
  await page.goto('/finans/dagitimlar')
  const listResponse = await firstList
  expect(listResponse.status()).toBe(200)
  const list = await listResponse.json() as {
    readonly companyId: string
    readonly items: readonly {
      readonly id: string
      readonly companyId: string
      readonly sourceId: string
      readonly openItemId: string
      readonly amount: string
      readonly status: string
    }[]
  }
  expect(list.companyId).toBe(env.seed.companyId)
  expect(list.items.length).toBeGreaterThan(0)
  const persisted = list.items[0]!
  expect(persisted.companyId).toBe(env.seed.companyId)

  const row = page.getByRole('row').filter({ hasText: persisted.sourceId }).filter({ hasText: persisted.openItemId })
  await expect(row).toContainText(persisted.amount)
  await expect(row).toContainText(persisted.status === 'Active' ? 'Aktif' : 'Bırakıldı')

  const detailPath = `/api/v1/finance/allocations/${persisted.id}`
  const firstDetail = page.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === detailPath)
  await row.getByRole('button', { name: 'Aç', exact: true }).click()
  const firstDetailResponse = await firstDetail
  expect(firstDetailResponse.status()).toBe(200)
  const firstDetailBody = await firstDetailResponse.json() as {
    readonly allocation: {
      readonly id: string
      readonly companyId: string
      readonly sourceId: string
      readonly openItemId: string
    }
  }
  expect(firstDetailBody.allocation).toMatchObject({
    id: persisted.id,
    companyId: env.seed.companyId,
    sourceId: persisted.sourceId,
    openItemId: persisted.openItemId,
  })
  await expect(page).toHaveURL(new RegExp(`dagitim=${persisted.id}`))
  await expect(page.getByRole('region', { name: 'Dağıtım detayı' })).toContainText(persisted.id)
  await expect(page.getByRole('button', { name: 'Dağıtımı bırak' })).toHaveCount(0)

  await page.getByRole('button', { name: 'Kapat', exact: true }).click()
  await expect(page.getByRole('region', { name: 'Dağıtım detayı' })).toHaveCount(0)
  await expect(page).not.toHaveURL(/dagitim=/)

  const reopenedDetail = page.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === detailPath)
  await row.getByRole('button', { name: 'Aç', exact: true }).click()
  const reopenedResponse = await reopenedDetail
  expect(reopenedResponse.status()).toBe(200)
  const reopenedBody = await reopenedResponse.json() as { readonly allocation: { readonly id: string } }
  expect(reopenedBody.allocation.id).toBe(persisted.id)
  await expect(page.getByRole('region', { name: 'Dağıtım detayı' })).toContainText(persisted.id)
  expectEveryAnswerRewritten(answered, rewritten)
})
