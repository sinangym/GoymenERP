import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PRODUCT_CAPABILITY } from './capabilities'
import { HISTORY_PAGE_SIZE } from './contract'
import { resetPendingAttempts } from './pendingAttempt'
import {
  COMPANY_A,
  COMPANY_B,
  PRODUCT_1,
  historyEntry,
  installFetch,
  json,
  productHistory,
  renderProductApp,
} from './testHarness'

/* U-01 Product 360, §9 item 6: the change history section on the card. */

const DETAIL_ONLY = [PRODUCT_CAPABILITY.productsDetail]
const PATH = `/urun/${PRODUCT_1}`
const OTHER_PRODUCT = '99999999-9999-9999-9999-999999999999'

afterEach(() => {
  cleanup()
  resetPendingAttempts()
  // See the create tests: the shell's stored company preference must not leak.
  try {
    window.localStorage.clear()
  } catch {
    // Storage unavailable in this environment: nothing was remembered.
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const switchedTo = (companyId: string) =>
  waitFor(() => {
    const picker = screen.getByLabelText('Şirket') as HTMLSelectElement
    expect(picker.disabled).toBe(false)
    expect(picker.value).toBe(companyId)
  })

const historyList = () => waitFor(() => screen.getByTestId('product-history'))
const cardHeading = () => screen.queryByRole('heading', { name: 'Siyah sac' })

const editEntry = (over: Record<string, unknown> = {}) =>
  historyEntry({
    eventId: 'eeeeeeee-0000-0000-0000-000000000002',
    operation: 'DraftBasicEdit',
    occurredAtUtc: '2026-09-11T09:30:00Z',
    versionBefore: '1',
    versionAfter: '2',
    reason: 'Aile bağı düzeltildi',
    ...over,
  })

/** A full page of edits, newest first, chaining down from version 51: what a CUT list looks like. */
const cutPage = () =>
  Array.from({ length: HISTORY_PAGE_SIZE }, (_, index) =>
    editEntry({
      eventId: `eeeeeeee-0000-0000-0000-${String(index + 10).padStart(12, '0')}`,
      occurredAtUtc: new Date(Date.UTC(2026, 8, 11, 9, 0, 0) - index * 60_000).toISOString(),
      versionBefore: String(HISTORY_PAGE_SIZE - index),
      versionAfter: String(HISTORY_PAGE_SIZE + 1 - index),
    }),
  )

describe('change history', () => {
  it('reads the history of the same product under the same origin, after the card', async () => {
    const { calls } = installFetch({ capabilities: DETAIL_ONLY })
    renderProductApp(PATH)

    await historyList()
    const historyCalls = calls.filter((call) => call.url.endsWith(`/products/${PRODUCT_1}/history`))
    expect(historyCalls).toHaveLength(1)
    expect(historyCalls[0].companyId).toBe(COMPANY_A)
    expect(historyCalls[0].method).toBe('GET')
    const cardIndex = calls.findIndex((call) => call.url.endsWith(`/products/${PRODUCT_1}`))
    expect(cardIndex).toBeGreaterThanOrEqual(0)
    expect(calls.indexOf(historyCalls[0])).toBeGreaterThan(cardIndex)
  })

  it('lists newest first with operation, version step, reason and the changed fields in words', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onHistory: () =>
        json(
          productHistory({
            entries: [
              editEntry({ changedFields: ['name', 'familyId', 'roles.sellable', 'references.units.sales'] }),
              historyEntry(),
            ],
          }),
        ),
    })
    renderProductApp(PATH)

    const items = within(await historyList()).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    expect(items[0].textContent).toContain('Temel bilgiler düzenlendi')
    expect(items[0].textContent).toContain('Sürüm 1 → 2')
    expect(items[0].textContent).toContain('Gerekçe: Aile bağı düzeltildi')
    expect(items[0].textContent).toContain('Değişen alanlar: Ürün adı, Aile, Rol: Satılabilir, Satış birimi')
    expect(items[1].textContent).toContain('Taslak oluşturuldu')
    expect(items[1].textContent).toContain('Sürüm 1')
    // A creation has nothing to diff against, so it claims no changed fields.
    expect(items[1].textContent).not.toContain('Değişen alanlar')
    expect(screen.getByText('Değişikliği yapan kişi bu görünümde gösterilmez.')).toBeTruthy()
  })

  it('says an edit touched no tracked field instead of listing nothing', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onHistory: () => json(productHistory({ entries: [editEntry(), historyEntry()] })),
    })
    renderProductApp(PATH)

    const items = within(await historyList()).getAllByRole('listitem')
    expect(items[0].textContent).toContain('İzlenen alanlarda değişiklik görünmüyor.')
  })

  it('shows activation as the lifecycle change recorded by Product', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onHistory: () =>
        json(productHistory({
          entries: [editEntry({ operation: 'Activate', changedFields: ['lifecycle'], reason: 'Ürün kartı onayı' }), historyEntry()],
        })),
    })
    renderProductApp(PATH)

    const items = within(await historyList()).getAllByRole('listitem')
    expect(items[0].textContent).toContain('Ürün aktifleştirildi')
    expect(items[0].textContent).toContain('Değişen alanlar: Durum')
  })

  it('names a field key or operation it does not know instead of dropping it', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onHistory: () =>
        json(
          productHistory({
            entries: [
              editEntry({ operation: 'Retire', changedFields: ['tracking.mode'] }),
              historyEntry(),
            ],
          }),
        ),
    })
    renderProductApp(PATH)

    const items = within(await historyList()).getAllByRole('listitem')
    expect(items[0].textContent).toContain('Tanınmayan değişiklik türü (Retire)')
    expect(items[0].textContent).toContain('Tanınmayan alan (tracking.mode)')
  })

  it('marks a cut list as the latest 50 only', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onHistory: () => json(productHistory({ entries: cutPage(), truncated: true })),
    })
    renderProductApp(PATH)

    const items = within(await historyList()).getAllByRole('listitem')
    expect(items).toHaveLength(HISTORY_PAGE_SIZE)
    expect(screen.getByText('Yalnız son 50 değişiklik gösteriliyor.')).toBeTruthy()
  })

  it('a 404 after the card loaded reads as "not available here", never as "product not found"', async () => {
    installFetch({ capabilities: DETAIL_ONLY, onHistory: () => json({ code: 'not_found' }, 404) })
    renderProductApp(PATH)

    await waitFor(() =>
      expect(screen.getByText('Değişiklik geçmişi bu ortamda henüz okunamıyor.')).toBeTruthy(),
    )
    expect(cardHeading()).toBeTruthy()
    expect(screen.queryByText('Bulunamadı — ürün')).toBeNull()
  })

  it('a service failure keeps the card and offers a retry that reads again', async () => {
    let attempts = 0
    const { calls } = installFetch({
      capabilities: DETAIL_ONLY,
      onHistory: () => {
        attempts += 1
        return attempts === 1 ? json({ code: 'service_unavailable' }, 503) : json(productHistory())
      },
    })
    renderProductApp(PATH)

    const alert = await waitFor(() => screen.getByRole('alert'))
    expect(alert.textContent).toContain('Değişiklik geçmişi okunamadı.')
    expect(cardHeading()).toBeTruthy()
    fireEvent.click(within(alert).getByRole('button', { name: 'Tekrar dene' }))
    await historyList()
    expect(calls.filter((call) => call.url.endsWith('/history'))).toHaveLength(2)
  })

  it('fails closed on a history that belongs to another product', async () => {
    installFetch({
      capabilities: DETAIL_ONLY,
      onHistory: () => json(productHistory({ productId: OTHER_PRODUCT })),
    })
    renderProductApp(PATH)

    await waitFor(() =>
      expect(
        screen.getByText('Sunucudan gelen değişiklik geçmişi beklenen biçimde değil; gösterilmedi.'),
      ).toBeTruthy(),
    )
    expect(screen.queryByTestId('product-history')).toBeNull()
  })

  it('a 403 on the history closes the capability rather than showing a local error', async () => {
    const { calls } = installFetch({ capabilities: DETAIL_ONLY, onHistory: () => json({ code: 'forbidden' }, 403) })
    renderProductApp(PATH)

    // The history was actually asked for; only then can its 403 be what closed the card.
    await waitFor(() => expect(calls.some((call) => call.url.includes('/history'))).toBe(true))
    await waitFor(() => expect(cardHeading()).toBeNull())
    expect(screen.queryByText('Değişiklik geçmişi okunamadı.')).toBeNull()
  })

  it('shows nothing of company A\'s history once the context is company B', async () => {
    // A's history is complete; B's read is still open. B's card must not carry A's rows,
    // not even for a moment, because the section is keyed by the company namespace.
    let reads = 0
    const { calls } = installFetch({
      capabilities: DETAIL_ONLY,
      onHistory: () => {
        reads += 1
        return reads === 1 ? json(productHistory({ entries: [editEntry(), historyEntry()] })) : new Promise(() => {})
      },
    })
    renderProductApp(PATH)

    await historyList()
    expect(screen.getByText('Gerekçe: Aile bağı düzeltildi')).toBeTruthy()

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    await switchedTo(COMPANY_B)

    await waitFor(() => expect(calls.filter((call) => call.url.endsWith('/history'))).toHaveLength(2))
    expect(calls.filter((call) => call.url.endsWith('/history'))[1].companyId).toBe(COMPANY_B)
    await waitFor(() => expect(cardHeading()).toBeTruthy())
    expect(screen.queryByTestId('product-history')).toBeNull()
    expect(screen.queryByText('Gerekçe: Aile bağı düzeltildi')).toBeNull()
    expect(screen.getByText('Değişiklik geçmişi yükleniyor…')).toBeTruthy()
  })

  it('never reads the history without the card', async () => {
    const { calls } = installFetch({
      capabilities: DETAIL_ONLY,
      onCard: () => json({ code: 'not_found' }, 404),
    })
    renderProductApp(PATH)

    await waitFor(() => expect(screen.getByText('Bulunamadı — ürün')).toBeTruthy())
    expect(calls.some((call) => call.url.includes('/history'))).toBe(false)
  })
})
