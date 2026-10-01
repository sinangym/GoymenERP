import { expect, test, type BrowserContext, type Page, type Request, type Route } from '@playwright/test'
import { readEnvironment } from '../src/env.js'

const env = readEnvironment()

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

test('FIN01 allocation reader is fail-closed for proposer and mounted for the authorized reader', async ({ browser }) => {
  const proposerContext = await browser.newContext()
  const proposerRewritten = await asActor(proposerContext, env.seed.proposerActor)
  const proposer = await proposerContext.newPage()
  const proposerAnswers = watchAnswers(proposer)
  await proposer.goto('/finans')
  const refused = await proposer.evaluate(async (companyId) => {
    const response = await fetch('/api/v1/finance/allocations', {
      headers: { 'X-Company-Id': companyId },
    })
    return { status: response.status, body: await response.text() }
  }, env.seed.companyId)
  expect(refused.status, refused.body).toBe(403)
  expectEveryAnswerRewritten(proposerAnswers, proposerRewritten)
  await proposerContext.close()

  const readerContext = await browser.newContext()
  const readerRewritten = await asActor(readerContext, env.seed.readerActor)
  const reader = await readerContext.newPage()
  const readerAnswers = watchAnswers(reader)
  const listAnswer = reader.waitForResponse((response) => response.request().method() === 'GET'
    && new URL(response.url()).pathname === '/api/v1/finance/allocations')
  await reader.goto('/finans/dagitimlar')
  const listResponse = await listAnswer
  const list = await listResponse.json() as { readonly companyId?: unknown; readonly items?: unknown }
  expect(listResponse.status(), JSON.stringify(list)).toBe(200)
  expect(list.companyId).toBe(env.seed.companyId)
  expect(Array.isArray(list.items)).toBe(true)
  await expect(reader.getByRole('heading', { level: 1, name: 'Finansal dağıtımlar', exact: true }))
    .toBeVisible({ timeout: 30_000 })
  await expect(reader.getByRole('form', { name: 'Finansal dağıtım oluştur' })).toHaveCount(0)
  expectEveryAnswerRewritten(readerAnswers, readerRewritten)
  await readerContext.close()
})
