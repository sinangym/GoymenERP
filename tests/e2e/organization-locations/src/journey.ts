/**
 * Selectors and small steps shared by the Organization journeys.
 *
 * They are written against the SHIPPED screens (`src/organization/**`), so a rename
 * there breaks these in one place instead of three. Where a stable hook exists it is
 * preferred over text: the outcome notices carry `data-outcome`, and the state badges
 * carry `data-state`, precisely so acceptance does not have to match Turkish prose that
 * product wording may legitimately change.
 */
import { expect, type Locator, type Page } from '@playwright/test'

export const ORG_LIST_PATH = '/ayarlar/organizasyon'

export function locationCardPath(locationId: string): string {
  return `/ayarlar/organizasyon/lokasyon/${locationId}`
}

/** The reasoned action opens from the card; the same label submits inside the dialog. */
export const ACTION_LABEL = /Pasife al|Aktifleştir/

export const outcome = {
  applied: '[data-outcome="applied"]',
  replayed: '[data-outcome="replayed"]',
  unknown: '[data-outcome="unknown"]',
  outstanding: '[data-outcome="outstanding"]',
  versionConflict: '[data-outcome="version-conflict"]',
  idempotencyConflict: '[data-outcome="idempotency-conflict"]',
  forbidden: '[data-outcome="forbidden"]',
} as const

export function listHeading(page: Page): Locator {
  return page.getByRole('heading', { name: 'Organizasyon', exact: true })
}

export function cardHeading(page: Page): Locator {
  return page.getByRole('heading', { name: 'Lokasyon', exact: true })
}

/**
 * The in-app back link. EXACT, not /Organizasyon/: the shell's sidebar also carries an
 * "Organizasyon" nav entry once the routes are mounted, and a loose pattern matches
 * both.
 */
export function backLink(page: Page): Locator {
  return page.getByRole('link', { name: '← Organizasyon' })
}

/** A location row's link, addressed by its business code. */
export function locationLink(page: Page, code: string): Locator {
  return page.getByRole('link', { name: code, exact: true })
}

/** The card's current state, read from the badge's stable hook. */
export async function cardState(page: Page): Promise<'active' | 'passive'> {
  const badge = page.locator('dd [data-state]').first()
  await expect(badge).toBeVisible()
  const value = await badge.getAttribute('data-state')
  if (value !== 'active' && value !== 'passive') {
    throw new Error(`unexpected card state attribute: ${String(value)}`)
  }
  return value
}

export function actionTrigger(page: Page): Locator {
  // The card's own trigger, i.e. the one OUTSIDE the dialog.
  return page.locator('section').getByRole('button', { name: ACTION_LABEL }).first()
}

export function dialog(page: Page): Locator {
  return page.getByRole('dialog')
}

/**
 * Opens the confirmation modal, types the justification and submits.
 *
 * The submit is taken from INSIDE the dialog: the card's trigger shares its label, and
 * clicking the wrong one would prove nothing about the command.
 */
export async function submitReasonedChange(page: Page, reason: string): Promise<void> {
  await actionTrigger(page).click()
  const modal = dialog(page)
  await expect(modal).toBeVisible()
  await modal.getByLabel('Gerekçe').fill(reason)
  await modal.getByRole('button', { name: ACTION_LABEL }).click()
}

/** Reads the version the card was loaded with, from the detail response the app made. */
export function versionOf(body: unknown): number {
  if (typeof body !== 'object' || body === null) throw new Error('detail body is not an object')
  const version = (body as { version?: unknown }).version
  if (typeof version !== 'number') throw new Error('detail body carries no numeric version')
  return version
}

export function idOf(body: unknown): string {
  if (typeof body !== 'object' || body === null) throw new Error('detail body is not an object')
  const id = (body as { id?: unknown }).id
  if (typeof id !== 'string') throw new Error('detail body carries no id')
  return id
}

/** Locations on the page the app currently shows, in server order. */
export function pageItems(body: unknown): Array<{ id: string; code: string; isActive: boolean; version: number }> {
  if (typeof body !== 'object' || body === null) throw new Error('page body is not an object')
  const items = (body as { items?: unknown }).items
  if (!Array.isArray(items)) throw new Error('page body carries no items array')
  return items as Array<{ id: string; code: string; isActive: boolean; version: number }>
}
