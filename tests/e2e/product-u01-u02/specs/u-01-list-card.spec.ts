import { expect, test } from '@playwright/test'
import { Lane, json } from '../src/harness'
import { PRODUCT_1, productCore, productPage } from '../src/fixtures'
import { openList } from '../src/ui'

test('U-01 list → card: rows from the server, no company header on any Product request, labels and completeness from the card', async ({ page }, info) => {
  const lane = await Lane.install(page, { slug: 'u01-list-card' })
  try {
    await openList(page)
    // The row is the server's code, as a link to the 360 only because detail is granted.
    const row = page.getByRole('link', { name: 'SAC-42' })
    await expect(row).toBeVisible()
    await expect(page.getByTestId(`product-completeness-${PRODUCT_1}`)).toContainText('Eksik')

    await row.click()
    await expect(page.getByRole('heading', { level: 1, name: 'Siyah sac' })).toBeVisible()
    await expect(page.getByTestId('product-group')).toHaveText(/Sac/)
    // A null label with a null id is "not assigned" — never a blank, never an invented name.
    await expect(page.getByTestId('product-class')).toContainText('atanmadı')
    await expect(page.getByTestId('product-completeness')).toContainText('Eksik')
    await expect(page.getByTestId('product-missing-fields')).toContainText('references.units.stock')
    await expect(page.getByRole('link', { name: 'Düzenle' })).toBeVisible()

    // Global master: nothing Product-shaped carried X-Company-Id; the shell's own calls did.
    expect(lane.of('list').length).toBeGreaterThanOrEqual(1)
    expect(lane.of('card').length).toBeGreaterThanOrEqual(1)
    for (const r of lane.product()) expect(r.company, r.kind + ' carried a company header').toBeNull()
    expect(lane.of('capabilities').some((r) => r.company !== null)).toBe(true)
    // The card read is the exact route of the contract, for the exact id.
    expect(lane.of('card')[0].path).toBe(`/api/v1/products/${PRODUCT_1}`)
    expect(lane.externalBlocked).toBe(0)
    expect(lane.of('unexpected')).toHaveLength(0)
    await lane.finish(info, { scope: 'U-01', productRequestsWithoutCompany: lane.product().length })
  } finally { lane.dispose() }
})

test('U-01 list: a label the server could not read is said so, not shown as unassigned', async ({ page }, info) => {
  const lane = await Lane.install(page, {
    slug: 'u01-label-unavailable',
    list: () => json(productPage({ items: [productCore({ classId: 'cccccccc-0000-0000-0000-000000000001', labels: { group: 'Sac', class: null, family: null, primaryStockUnit: 'kg' } })] })),
  })
  try {
    await openList(page)
    await expect(page.getByTestId(`product-class-${PRODUCT_1}`)).toContainText('okunamadı')
    await lane.finish(info, { scope: 'U-01', rule: 'null label + present id ≠ null id' })
  } finally { lane.dispose() }
})
