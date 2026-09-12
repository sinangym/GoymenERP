/**
 * L-02 — server-side authority refusal and non-disclosure, LIVE, against the bound
 * environment snapshot.
 *
 * Product authority is GLOBAL and current: company membership alone grants nothing
 * (README §authorization, PRD-SCOPE-001=A). The direct probe sends one request per
 * case as the bound unauthorized actor and records the real status and the real
 * ProblemDetails code; nothing is fulfilled. The browser half then shows what the
 * authorized actor sees for a record that does not exist: the same neutral block, with
 * nothing about the id confirmed.
 */
import { expect, test } from '@playwright/test'
import { ActorProbe, LiveRecorder, evidenceIdentity, lShot, liveEnvironment, writeLJson } from '../src/live'
import { neutralBlock } from '../src/ui'

const env = liveEnvironment()
const RUN_TOKEN = new Date().toISOString().replace(/\D/g, '').slice(8, 14)
const FORBIDDEN_NAME = `${env.browserPrefix} Yetkisiz ${RUN_TOKEN}`
const NOWHERE = '00000000-0000-4000-8000-000000000001'

test('L-02 an actor without global Product authority is refused on read and write, and nothing is created', async ({ page, request }, info) => {
  // The probe reuses the 009 actor probe; the counterparty contract header it adds is
  // outside this module's contract and is simply not read by the Product endpoints.
  const probe = new ActorProbe(request, 'optional-trade-roles-v1')
  const api = env.apiBaseUrl.replace(/\/$/, '')
  const actor = env.unauthorizedActor

  const list = await probe.send({ label: 'list', actor, method: 'GET', path: `${api}/api/v1/products`, companyId: env.companyId })
  const card = await probe.send({ label: 'card', actor, method: 'GET', path: `${api}/api/v1/products/${NOWHERE}`, companyId: env.companyId })
  const create = await probe.send({
    label: 'create', actor, method: 'POST', path: `${api}/api/v1/products`, companyId: env.companyId,
    idempotencyKey: `l02-${RUN_TOKEN}-${Math.random().toString(16).slice(2, 10)}`,
    data: { name: FORBIDDEN_NAME, groupId: NOWHERE },
    mustNotDisclose: [FORBIDDEN_NAME],
  })
  for (const r of [list, card, create]) {
    expect(r.status, r.label).toBe(403)
    expect(r.problemCode, r.label).toBe('forbidden')
  }
  expect(create.disclosedForbiddenText).toBe(false)

  // The authorized actor's list does not contain the refused name: nothing was created.
  const recorder = LiveRecorder.attach(page)
  await page.addInitScript((company) => localStorage.setItem('goymen-company-id', company), env.companyId)
  await page.goto('/urun')
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toBeVisible()
  await page.getByLabel('Kod, eski kod veya ad ara').fill(RUN_TOKEN)
  await expect.poll(async () => recorder.records.some((r) => r.method === 'GET' && r.path === '/api/v1/products' && r.queryKeys.includes('q') && r.status === 200)).toBe(true)
  await expect(page.getByText(FORBIDDEN_NAME)).toHaveCount(0)
  await lShot(page, 'l02', '01-nothing-created')

  // Non-disclosure in the browser: a record id that does not exist closes to the neutral block.
  await page.goto(`/urun/${NOWHERE}`)
  await expect(neutralBlock(page)).toBeVisible()
  await expect(page.getByText(NOWHERE)).toHaveCount(0)
  await lShot(page, 'l02', '02-neutral-block')

  writeLJson('authority', 'l02-probes.json', { ...evidenceIdentity(env), probes: probe.records })
  await recorder.finish(info, 'l02-authority', { ...evidenceIdentity(env), refusedAs: 'unauthorizedActor' })
})
