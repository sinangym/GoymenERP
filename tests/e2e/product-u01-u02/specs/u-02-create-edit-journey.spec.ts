import { expect, test } from '@playwright/test'
import { Lane, json } from '../src/harness'
import { A_ID, PRODUCT_1, mutation, productCard, productCore } from '../src/fixtures'
import { KEY_ALPHABET, changeNameAndReason, fillMinimum, openList, saveDraft, saveEdit } from '../src/ui'

test('U-02 journey: list → new → Draft (server code) → card → basic edit with reason → fresh version → back to card', async ({ page }, info) => {
  let edited = false
  const lane = await Lane.install(page, {
    slug: 'u02-journey',
    card: () => json(productCard({ product: productCore(edited ? { name: 'Galvaniz sac', version: '8' } : {}) })),
    edit: ({ body }) => {
      edited = true
      const changes = (body as { changes?: { name?: string } }).changes ?? {}
      return json(mutation({ card: productCard({ product: productCore({ name: changes.name ?? 'Siyah sac', version: '8' }) }) }))
    },
  })
  try {
    await openList(page)
    await page.getByRole('link', { name: 'Yeni ürün' }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible()
    // No code field: the server mints the code, and it is shown only from its answer.
    await expect(page.getByLabel(/^Ürün kodu/)).toHaveCount(0)

    await fillMinimum(page)
    await saveDraft(page).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Taslak kaydedildi' })).toBeVisible()
    await expect(page.getByText('SAC-42 — Siyah sac')).toBeVisible()

    const post = lane.of('create')
    expect(post).toHaveLength(1)
    expect(post[0].company).toBe(A_ID)
    expect(post[0].payloadKeys).not.toContain('companyId')
    expect(post[0].key).toMatch(KEY_ALPHABET)
    // The create minimum and nothing server-owned; unanswered roles are omitted, not sent as false.
    expect(post[0].payloadKeys).toEqual(['groupId', 'name'])

    await page.getByRole('link', { name: 'Ürün kartını aç' }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Siyah sac' })).toBeVisible()
    await page.getByRole('link', { name: 'Düzenle' }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Ürünü düzenle' })).toBeVisible()
    // Group and code are frozen by the contract: no control for either.
    await expect(page.getByLabel(/Kod grubu/)).toHaveCount(0)

    await changeNameAndReason(page)
    await saveEdit(page).click()
    await expect(page.getByText('Değişiklikler kaydedildi')).toBeVisible()

    const patch = lane.of('edit')
    expect(patch).toHaveLength(1)
    expect(patch[0].path).toBe(`/api/v1/products/${PRODUCT_1}/draft`)
    expect(patch[0].company).toBe(A_ID)
    expect(patch[0].key).toMatch(KEY_ALPHABET)
    expect(patch[0].key).not.toBe(post[0].key)
    expect(patch[0].payloadKeys).toEqual(['changes', 'expectedVersion', 'reason'])

    await page.getByRole('link', { name: 'Ürün kartına dön' }).click()
    await expect(page.getByRole('heading', { level: 1, name: 'Galvaniz sac' })).toBeVisible()

    for (const r of lane.product()) expect(r.company, r.kind + ' origin header').toBe(A_ID)
    expect(lane.externalBlocked).toBe(0)
    expect(lane.of('unexpected')).toHaveLength(0)
    await lane.finish(info, { scope: 'U-01+U-02', oneCreate: true, oneEdit: true, distinctKeys: true })
  } finally { lane.dispose() }
})

test('U-02 edit sends the DIFF with the version the person read, and requires a reason for a real change', async ({ page }, info) => {
  const lane = await Lane.install(page, {
    slug: 'u02-edit-diff',
    edit: ({ body }) => {
      const b = body as { expectedVersion?: unknown; changes?: Record<string, unknown>; reason?: unknown }
      expect(b.expectedVersion).toBe('7')
      expect(Object.keys(b.changes ?? {})).toEqual(['name'])
      expect(typeof b.reason).toBe('string')
      return json(mutation({ card: productCard({ product: productCore({ name: 'Galvaniz sac', version: '8' }) }) }))
    },
  })
  try {
    await page.goto(`/urun/${PRODUCT_1}/duzenle`)
    await expect(page.getByRole('heading', { level: 1, name: 'Ürünü düzenle' })).toBeVisible()
    await page.getByLabel(/Ürün adı/).fill('Galvaniz sac')
    await saveEdit(page).click()
    // Reason missing: refused client-side, nothing sent.
    await expect(page.getByRole('alert')).toBeVisible()
    expect(lane.of('edit')).toHaveLength(0)

    await page.getByLabel(/Değişiklik gerekçesi/).fill('Ad düzeltildi')
    await saveEdit(page).click()
    await expect(page.getByText('Değişiklikler kaydedildi')).toBeVisible()
    expect(lane.of('edit')).toHaveLength(1)
    await lane.finish(info, { scope: 'U-02', diffOnly: true, expectedVersionFromCard: '7' })
  } finally { lane.dispose() }
})
