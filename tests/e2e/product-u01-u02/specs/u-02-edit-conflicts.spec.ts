import { expect, test } from '@playwright/test'
import { Lane, json } from '../src/harness'
import { PRODUCT_1, PROBLEM } from '../src/fixtures'
import { changeNameAndReason, openEdit, saveEdit } from '../src/ui'

test('U-02 stale version: both versions shown, input kept, only a reload offered — never a rebase', async ({ page }, info) => {
  const lane = await Lane.install(page, {
    slug: 'u02-stale-version',
    edit: () => json({ code: PROBLEM.versionConflict, expectedVersion: '7', currentVersion: '9' }, 409),
  })
  try {
    await openEdit(page, PRODUCT_1)
    await changeNameAndReason(page)
    await saveEdit(page).click()
    const stale = page.getByTestId('product-edit-stale')
    await expect(stale).toBeVisible()
    await expect(stale).toContainText('7')
    await expect(stale).toContainText('9')
    await expect(stale.getByRole('button', { name: 'Güncel hâli yükle' })).toBeVisible()
    await expect(page.getByRole('button', { name: /yine de/i })).toHaveCount(0)
    await expect(page.getByLabel(/Ürün adı/)).toHaveValue('Galvaniz sac')
    expect(lane.of('edit')).toHaveLength(1)
    await lane.finish(info, { scope: 'U-02', rebase: false })
  } finally { lane.dispose() }
})

test('U-02 lifecycle conflict: the screen says basic edit is Draft-only, not a generic failure', async ({ page }, info) => {
  const lane = await Lane.install(page, {
    slug: 'u02-lifecycle-conflict',
    edit: () => json({ code: PROBLEM.lifecycleConflict }, 409),
  })
  try {
    await openEdit(page, PRODUCT_1)
    await changeNameAndReason(page)
    await saveEdit(page).click()
    await expect(page.getByText('Bu kayıt artık taslak değil')).toBeVisible()
    expect(lane.of('edit')).toHaveLength(1)
    await lane.finish(info, { scope: 'U-02', lifecycleConflictNamed: true })
  } finally { lane.dispose() }
})
