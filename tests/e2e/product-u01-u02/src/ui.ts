import { expect, type Page } from '@playwright/test'
import { GROUP_SAC } from './fixtures'

export async function openList(page: Page) {
  await page.goto('/urun')
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünler' })).toBeVisible()
}
export async function openCreate(page: Page) {
  await page.goto('/urun/yeni')
  await expect(page.getByRole('heading', { level: 1, name: 'Yeni ürün' })).toBeVisible()
}
export async function openEdit(page: Page, productId: string) {
  await page.goto(`/urun/${productId}/duzenle`)
  await expect(page.getByRole('heading', { level: 1, name: 'Ürünü düzenle' })).toBeVisible()
}
export const createHeading = (page: Page) => page.getByRole('heading', { level: 1, name: 'Yeni ürün' })
export const editHeading = (page: Page) => page.getByRole('heading', { level: 1, name: 'Ürünü düzenle' })

/** The create minimum: a name and a code group chosen from the server's list. */
export async function fillMinimum(page: Page, name = 'Siyah sac') {
  await page.getByLabel(/Ürün adı/).fill(name)
  await expect(page.getByRole('option', { name: /Sac \(SAC\)/ })).toBeAttached()
  await page.getByLabel(/Kod grubu/).selectOption(GROUP_SAC)
}
export const saveDraft = (page: Page) => page.getByRole('button', { name: 'Taslak olarak kaydet', exact: true })
export const saveEdit = (page: Page) => page.getByRole('button', { name: 'Değişiklikleri kaydet', exact: true })
export const retrySame = (page: Page) => page.getByRole('button', { name: 'Aynı işlemi tekrar dene', exact: true })
export const companyPicker = (page: Page) => page.getByRole('combobox', { name: 'Şirket', exact: true })
/** The shell's neutral block — the same one for "not granted", "closed" and "unknown route". */
export const neutralBlock = (page: Page) => page.locator('[data-state="not-found"]')

export async function changeNameAndReason(page: Page, name = 'Galvaniz sac', reason = 'Ad düzeltildi') {
  await page.getByLabel(/Ürün adı/).fill(name)
  await page.getByLabel(/Değişiklik gerekçesi/).fill(reason)
}

/** The contract's Idempotency-Key alphabet: 1..128 printable ASCII, no whitespace, no comma. */
export const KEY_ALPHABET = /^[\x21-\x2b\x2d-\x7e]{1,128}$/
