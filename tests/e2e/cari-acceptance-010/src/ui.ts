import { expect, type Page } from '@playwright/test'
import { Diagnostics } from './diagnostics'
export async function openLink(page: Page, code = 'CAR-000001') {
  const diagnostics = Diagnostics.for(page)
  const t0 = Date.now()
  diagnostics?.mark('open-link:goto-start')
  await page.goto('/cari/sirkete-bagla')
  diagnostics?.mark('open-link:goto-done', `${Date.now() - t0}ms`)
  await expect(page.getByRole('heading', { level: 1, name: 'Cariyi Şirkete Bağla' })).toBeVisible()
  diagnostics?.mark('open-link:heading-visible', `${Date.now() - t0}ms`)
  await expect(page.getByRole('radio', { name: new RegExp(code) })).toBeVisible()
  diagnostics?.mark('open-link:candidates-visible', `${Date.now() - t0}ms`)
}
export async function choose(page: Page, code = 'CAR-000001') {
  await page.getByRole('radio', { name: new RegExp(code) }).check()
  await page.getByRole('checkbox', { name: 'Tedarikçi' }).check()
}
export const selection = (page: Page) => page.locator('[data-selection]').getAttribute('data-selection')
export const submit = (page: Page) => page.getByRole('button', { name: 'Şirkete bağla', exact: true })
export async function noSuccess(page: Page) {
  await expect(page.locator('[data-outcome]')).toHaveCount(0)
  await expect(page).toHaveURL(/\/cari\/sirkete-bagla$/)
}
export interface FocusStop { tag: string; id: string; inputType: string | null; role: string | null; name: string | null; text: string; focusVisible: boolean }
/** The focused element as the browser sees it, including `:focus-visible`; label text stands in for a missing aria-label. */
export function focused(page: Page): Promise<FocusStop> {
  return page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null
    if (!el || el === document.body) return { tag: el?.tagName.toLowerCase() ?? '', id: '', inputType: null, role: null, name: null, text: '', focusVisible: false }
    const labels = 'labels' in el ? (el as HTMLInputElement).labels : null
    const labelText = labels && labels.length > 0 ? Array.from(labels).map((label) => (label.textContent ?? '').trim()).join(' ') : null
    return {
      tag: el.tagName.toLowerCase(), id: el.id,
      inputType: el instanceof HTMLInputElement ? el.type : null,
      role: el.getAttribute('role'),
      name: el.getAttribute('aria-label') ?? labelText ?? el.getAttribute('name'),
      text: (el.textContent ?? '').trim().slice(0, 80),
      focusVisible: el.matches(':focus-visible'),
    }
  })
}
