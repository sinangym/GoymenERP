/**
 * L-01 — the LIVE U-01/U-02 journey: real browser, real Product API, real store behind it.
 *
 * create → list (found by search) → card → basic edit with a reason → fresh version →
 * back to the card → reopen from the list. BOTH commands go through a real lost reply
 * and a real same-key retry: the first create POST and the first edit PATCH are
 * re-issued to the real API by the test, and only once the real success and the real
 * product identity have been SEEN is the browser's delivery of that reply aborted. The
 * user then retries the SAME key, which travels as an ordinary browser request and
 * comes back as a real replay (`isReplay: true`) of the same record.
 *
 * NOTHING here is mocked: no `route.fulfill`, no synthesised body, no invented code or
 * version. Every code, id and version asserted below was read from a real response.
 * The withheld replies are recorded as `upstream_success_then_delivery_abort`.
 */
import { expect, test } from '@playwright/test'
import { LiveRecorder, ProductDeliveryWithholder, lShot, readProductManifest, writeLJson } from '../src/live'
import { retrySame, saveDraft, saveEdit } from '../src/ui'

const manifest = readProductManifest()
const RUN_TOKEN = new Date().toISOString().replace(/\D/g, '').slice(8, 14)
const NAME = `${manifest.browserPrefix} Kabul sac ${RUN_TOKEN}`
const EDITED_NAME = `${NAME} galvaniz`
const REASON = `${manifest.browserPrefix} kabul koşumu ${RUN_TOKEN}: ad düzeltildi`
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

test('L-01 live journey: create (lost reply → same-key replay) → list → card → edit (lost reply → same-key replay) → card → reopen', async ({ page }, info) => {
  const recorder = LiveRecorder.attach(page)
  const withholder = await ProductDeliveryWithholder.install(page, [
    { label: 'create', method: 'POST', expectedStatus: 201, match: (p) => p === '/api/v1/products' },
    { label: 'edit', method: 'PATCH', expectedStatus: 200, match: (p) => /^\/api\/v1\/products\/[^/]+\/draft$/.test(p) },
  ])
  await page.addInitScript((company) => localStorage.setItem('goymen-company-id', company), manifest.companyId)

  // ── create ────────────────────────────────────────────────────────────────
  await page.goto('/urun/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible()
  await page.getByLabel(/Ürün adı/).fill(NAME)
  // A REAL code group from the live catalogue — the first one the server offered.
  const group = page.getByLabel(/Kod grubu/)
  await expect.poll(async () => (await group.locator('option').count()) > 1).toBe(true)
  const groupValue = await group.locator('option').nth(1).getAttribute('value')
  expect(groupValue).toMatch(UUID_RE)
  await group.selectOption(groupValue as string)
  await lShot(page, 'l01', '01-create-filled')

  await saveDraft(page).click()
  // The browser never received the 201: the outcome is unknown, the key is held.
  await expect(page.getByTestId('product-create-unknown')).toBeVisible()
  await lShot(page, 'l01', '02-create-unknown')
  expect(withholder.withheld.filter((w) => w.label === 'create')).toHaveLength(1)
  const createWithheld = withholder.withheld[0]
  expect(createWithheld.marker, createWithheld.upstreamError ?? '').toBe('upstream_success_then_delivery_abort')
  expect(createWithheld.companyId).toBeNull()
  expect(createWithheld.identity.lifecycle).toBe('Draft')
  const productId = createWithheld.identity.id as string
  const createdCode = createWithheld.identity.code as string
  expect(productId).toMatch(UUID_RE)

  await retrySame(page).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()
  // The replay of the SAME record: the server says so, and the code is the same one.
  await expect(page.getByText('Bu istek daha önce işlenmişti; ikinci bir kayıt oluşturulmadı.')).toBeVisible()
  await expect(page.getByText(`${createdCode} — ${NAME}`)).toBeVisible()
  const createPosts = recorder.records.filter((r) => r.method === 'POST' && r.path === '/api/v1/products')
  expect(createPosts).toHaveLength(2)
  expect(createPosts[0].idempotencyKey).toBe(createWithheld.idempotencyKey)
  expect(createPosts[1].idempotencyKey).toBe(createWithheld.idempotencyKey)
  expect(createPosts[1].status).toBe(201)
  await lShot(page, 'l01', '03-create-replayed')

  // ── list → card ───────────────────────────────────────────────────────────
  await page.getByRole('link', { name: 'Listeye dön' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toBeVisible()
  await page.getByLabel('Kod, eski kod veya ad ara').fill(RUN_TOKEN)
  const row = page.getByRole('link', { name: createdCode, exact: true })
  await expect(row).toBeVisible()
  await lShot(page, 'l01', '04-list-found')
  await row.click()
  await expect(page.getByRole('heading', { level: 1, name: NAME })).toBeVisible()
  const versionBefore = (await page.getByText('Sürüm').locator('xpath=following-sibling::dd').first().textContent())?.trim() ?? ''
  expect(versionBefore).toBe(createWithheld.identity.version)
  await lShot(page, 'l01', '05-card')

  // ── edit ──────────────────────────────────────────────────────────────────
  await page.getByRole('link', { name: 'Düzenle' }).click()
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünü düzenle' })).toBeVisible()
  await page.getByLabel(/Ürün adı/).fill(EDITED_NAME)
  await page.getByLabel(/Değişiklik gerekçesi/).fill(REASON)
  await saveEdit(page).click()
  await expect(page.getByTestId('product-edit-unknown')).toBeVisible()
  await lShot(page, 'l01', '06-edit-unknown')
  const editWithheld = withholder.withheld.find((w) => w.label === 'edit')
  expect(editWithheld?.marker, editWithheld?.upstreamError ?? '').toBe('upstream_success_then_delivery_abort')
  expect(editWithheld?.companyId).toBeNull()
  expect(editWithheld?.identity.id).toBe(productId)
  expect(editWithheld?.identity.version).not.toBe(versionBefore)

  await retrySame(page).click()
  await expect(page.getByText('Değişiklikler kaydedildi')).toBeVisible()
  await expect(page.getByText('Bu istek daha önce işlenmişti; ikinci bir değişiklik uygulanmadı.')).toBeVisible()
  const patches = recorder.records.filter((r) => r.method === 'PATCH' && r.path === `/api/v1/products/${productId}/draft`)
  expect(patches).toHaveLength(2)
  expect(patches[0].idempotencyKey).toBe(patches[1].idempotencyKey)
  expect(patches[1].status).toBe(200)
  const sentBody = patches[0].requestBody as { expectedVersion?: unknown; changes?: Record<string, unknown>; reason?: unknown }
  expect(sentBody.expectedVersion).toBe(versionBefore)
  expect(Object.keys(sentBody.changes ?? {})).toEqual(['name'])
  await lShot(page, 'l01', '07-edit-replayed')

  // ── back to the card, then reopen from the list ───────────────────────────
  await page.getByRole('link', { name: 'Ürün kartına dön' }).click()
  await expect(page.getByRole('heading', { level: 1, name: EDITED_NAME })).toBeVisible()
  const versionAfter = (await page.getByText('Sürüm').locator('xpath=following-sibling::dd').first().textContent())?.trim() ?? ''
  expect(versionAfter).toBe(editWithheld?.identity.version)
  await page.goto('/urun')
  await page.getByLabel('Kod, eski kod veya ad ara').fill(RUN_TOKEN)
  await page.getByRole('link', { name: createdCode, exact: true }).click()
  await expect(page.getByRole('heading', { level: 1, name: EDITED_NAME })).toBeVisible()
  await lShot(page, 'l01', '08-reopened')

  // Global master: no Product request carried a company header.
  for (const r of recorder.records.filter((x) => x.path.startsWith('/api/v1/products'))) expect(r.companyId, `${r.method} ${r.path}`).toBeNull()

  writeLJson('withheld', 'l01-withheld.json', { runId: manifest.runId, instanceId: manifest.instanceId, withheld: withholder.withheld, passedThrough: withholder.passedThrough })
  await recorder.finish(info, 'l01-journey', {
    runId: manifest.runId, instanceId: manifest.instanceId, productId, createdCode, versionBefore, versionAfter,
    note: 'Live Product API through the Vite proxy; two withheld deliveries, two same-key replays. Not ERP acceptance by itself: erpAccepted is the environment publisher’s field.',
    erpAccepted: manifest.erpAccepted,
  })
})
