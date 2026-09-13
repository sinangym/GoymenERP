import { expect, test } from '@playwright/test'
import { Lane, go, json, pending, shell } from '../src/harness'
import { A_ID, B_ID } from '../src/fixtures'
import { companyPicker, fillMinimum, openCreate, saveDraft } from '../src/ui'

test('U-02 dirty create + company switch: "Kal" keeps the input and the company; nothing is sent', async ({ page }, info) => {
  const lane = await Lane.install(page, { slug: 'u02-switch-kal' })
  try {
    await openCreate(page)
    await page.getByLabel(/Ürün adı/).fill('Siyah sac')
    await companyPicker(page).selectOption(B_ID)
    await expect(page.getByRole('dialog')).toBeVisible()
    await page.getByRole('dialog').getByRole('button', { name: 'Kal', exact: true }).click()
    await expect(companyPicker(page)).toHaveValue(A_ID)
    await expect(page.getByLabel(/Ürün adı/)).toHaveValue('Siyah sac')
    expect(lane.of('create')).toHaveLength(0)
    expect(lane.of('capabilities').filter((r) => r.company === B_ID)).toHaveLength(0)
    await lane.finish(info, { scope: 'U-02', kalKeeps: true })
  } finally { lane.dispose() }
})

test('U-02 unresolved attempt + confirmed switch: the attempt is dropped, the other company opens EMPTY, no resend', async ({ page }, info) => {
  const lane = await Lane.install(page, { slug: 'u02-switch-discard', create: () => json({}, 503) })
  try {
    await openCreate(page)
    await fillMinimum(page)
    await saveDraft(page).click()
    await expect(page.getByTestId('product-create-unknown')).toBeVisible()
    const key = lane.of('create')[0].key

    await companyPicker(page).selectOption(B_ID)
    await expect(page.getByRole('dialog')).toBeVisible()
    await page.getByRole('dialog').getByRole('button', { name: 'Değişiklikleri bırak' }).click()
    await expect(companyPicker(page)).toHaveValue(B_ID)
    await expect.poll(async () => (await shell(page)).company).toBe(B_ID)

    await go(page, '/urun')
    await go(page, '/urun/yeni')
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible()
    await expect(page.getByTestId('product-create-unknown')).toHaveCount(0)
    await expect(page.getByLabel(/Ürün adı/)).toHaveValue('')
    expect(await pending(page, 'create')).toBeNull()

    // And back: the person was asked and answered; the attempt does not come back.
    await companyPicker(page).selectOption(A_ID)
    await expect(companyPicker(page)).toHaveValue(A_ID)
    await go(page, '/urun')
    await go(page, '/urun/yeni')
    await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible()
    await expect(page.getByTestId('product-create-unknown')).toHaveCount(0)
    expect(lane.of('create')).toHaveLength(1)
    expect(lane.of('create')[0].key).toBe(key)
    await lane.finish(info, { scope: 'U-02', discardDrops: true, resends: 0 })
  } finally { lane.dispose() }
})
