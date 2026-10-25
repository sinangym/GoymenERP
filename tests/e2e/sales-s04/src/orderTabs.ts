import { expect, type Page } from '@playwright/test'

/**
 * UI-10: S-04 Sipariş 360 shows its sections in the six locked tabs (Master §7.2). A spec that uses a section outside
 * the first tab ("Satırlar") opens that tab first, the way a user does; inactive panels stay mounted but hidden.
 */
export const ORDER_TAB_NAME = {
  lines: 'Satırlar',
  allocation: 'Tahsis / Kaynak',
  realization: 'Gerçekleşme / İrsaliye',
  invoices: 'Faturalar',
  documents: 'Sözleşme / Belgeler',
  history: 'Revizyon / Geçmiş',
} as const

export async function openOrderTab(page: Page, name: (typeof ORDER_TAB_NAME)[keyof typeof ORDER_TAB_NAME]): Promise<void> {
  const tab = page.getByRole('tab', { name, exact: true })
  await tab.click()
  await expect(tab).toHaveAttribute('aria-selected', 'true')
}
