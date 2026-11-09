/**
 * UI-50 / S-05 "Fiyat–İskonto–Marj–Risk Kontrolü" in the real shell: the read-only comparison shows the server's reference,
 * floor, unit price, adjustments and net per line, the server's floor-exception and discount-limit states, counts of the
 * pending ones, the margin as a labelled unavailable state, and the S-04 totals and risk cards. Loading, 403, 404, 503 with
 * retry, malformed, foreign, withheld, stale and unbound pricing never paint a figure. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_CAPABILITY } from './capabilities'
import { PRICE_CONTROL_TEXT as T } from './priceControlText'
import { ALL_SALES, COMPANY_A, COMPANY_B, KG, deferred, installFetch, json, renderSalesApp } from './testHarness'

type Body = Record<string, unknown> & { draft: Record<string, unknown>; lines: Record<string, unknown>[] }

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const LIST = 'abababab-abab-4bab-8bab-abababababab'
const PATH = `/satis/siparisler/${DRAFT}/kontrol`

function detail(over: { version?: string; companyId?: string; disclosure?: 'Visible' | 'Withheld' } = {}): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, version: over.version ?? '3', companyId: over.companyId ?? COMPANY_A }
  body.lines = [{ ...body.lines[0], unitPrice: over.disclosure === 'Withheld' ? null : '39000', priceUnitId: over.disclosure === 'Withheld' ? null : KG,
    priceDisclosure: over.disclosure ?? 'Visible' }]
  return body
}

const reference = {
  priceListId: LIST, priceListName: 'Yurt içi satış', versionNo: 2, validFrom: '2026-09-01', validThrough: null, currencyCode: 'TRY',
  priceUnitId: KG, unitPrice: '42000.50', floorUnitPrice: '40000', vatBasis: 'ExcludingVat',
  lowerQuantity: '10', upperQuantity: '20', tier: 'Company',
}

function pricing(version = '3', over: Record<string, unknown> = {}) {
  return {
    version,
    lines: [{
      lineId: LINE, lineNo: 1, unitPrice: '39000', priceUnitId: KG, priceSource: 'Manual', priceReference: reference,
      discountPercent: '12', discountAmount: null, surchargePercent: null, surchargeAmount: null, netUnitPrice: '34320',
      floorException: { requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: null, approvedAtUtc: null, reason: null },
      discountCheck: { checkedBy: 'maker', checkedAtUtc: '2026-10-01T08:00:00Z', listUnitPrice: '42000.50', limitPercent: '10',
        requiresApproval: true, approvedBy: null, approvedAtUtc: null, reason: null },
      ...over,
    }],
  }
}

/** A shared state block of this kind is on screen (inside `root` when given). */
async function findState(kind: string, root: ParentNode = document) {
  await waitFor(() => expect(root.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('UI-50 S-05 price, discount, margin and risk control', () => {
  it('shows the server figures and pending states per line, counts pending exceptions and sends no command', async () => {
    const harness = installFetch({ onDetail: () => json(detail()), onLinePricing: () => json(pricing()) })
    renderSalesApp(PATH)

    const row = await screen.findByTestId('sales-price-control-line-1')
    expect(within(row).getByTestId('sales-price-control-line-1-reference').textContent).toBe('42000.50 TRY')
    expect(within(row).getByTestId('sales-price-control-line-1-floor').textContent).toBe('40000 TRY')
    expect(within(row).getByTestId('sales-price-control-line-1-unit-price').textContent).toBe('39000 TRY')
    expect(within(row).getByTestId('sales-price-control-line-1-discount').textContent).toBe('%12')
    expect(within(row).getByTestId('sales-price-control-line-1-net').textContent).toBe('34320 TRY')
    expect(within(row).getByTestId('sales-price-control-line-1-floor-status').textContent).toBe(T.status.floorPending)
    expect(within(row).getByTestId('sales-price-control-line-1-discount-status').textContent).toBe(T.status.discountOverPending('10'))
    expect(screen.getByTestId('sales-price-control-floor-pending-count').textContent).toBe('1')
    expect(screen.getByTestId('sales-price-control-discount-pending-count').textContent).toBe('1')
    expect(screen.getByText(T.note)).toBeTruthy()
    expect(within(screen.getByTestId('sales-price-control-margin')).getByText(T.margin.unavailable)).toBeTruthy()
    expect(screen.getByTestId('sales-price-control-back').getAttribute('href')).toBe(`/satis/siparisler/${DRAFT}`)
    // Read-only: no POST of any kind left the screen.
    expect(harness.calls.filter((call) => call.method !== 'GET')).toEqual([])
    expect(harness.calls.some((call) => call.url.endsWith(`/sales/drafts/${DRAFT}/line-pricing`))).toBe(true)
  })

  it('shows approved and within-limit states as the server sends them, with zero pending', async () => {
    installFetch({
      onDetail: () => json(detail()),
      onLinePricing: () => json(pricing('3', {
        floorException: { requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: 'checker',
          approvedAtUtc: '2026-10-01T08:05:00Z', reason: 'SENTETIK sözleşme' },
        discountCheck: { checkedBy: 'maker', checkedAtUtc: '2026-10-01T08:00:00Z', listUnitPrice: '42000.50', limitPercent: '15',
          requiresApproval: false, approvedBy: null, approvedAtUtc: null, reason: null },
      })),
    })
    renderSalesApp(PATH)

    expect((await screen.findByTestId('sales-price-control-line-1-floor-status')).textContent).toBe(T.status.floorApproved)
    expect(screen.getByTestId('sales-price-control-line-1-discount-status').textContent).toBe(T.status.discountWithin('15'))
    expect(screen.getByTestId('sales-price-control-floor-pending-count').textContent).toBe('0')
    expect(screen.getByTestId('sales-price-control-discount-pending-count').textContent).toBe('0')
  })

  it('asks for no price when the detail withholds prices and says so', async () => {
    const harness = installFetch({ onDetail: () => json(detail({ disclosure: 'Withheld' })) })
    renderSalesApp(PATH)

    expect((await screen.findByTestId('sales-price-control-withheld')).textContent).toBe(T.lines.withheld)
    expect(harness.calls.some((call) => call.url.includes('/line-pricing'))).toBe(false)
    expect(screen.queryByTestId('sales-price-control-table')).toBeNull()
  })

  it('shows loading while the detail is pending', async () => {
    const pending = deferred<Response>()
    installFetch({ onDetail: () => pending.promise })
    renderSalesApp(PATH)

    await findState('initial-loading', await screen.findByRole('main'))
    pending.resolve(json(detail()))
  })

  it.each<[string, number]>([
    // A current 403 goes to the shared auth boundary (capability re-fence): the page closes as forbidden or not-found.
    ['forbidden', 403],
    ['not found', 404],
  ])('shows the neutral %s block and no figure', async (_name, status) => {
    installFetch({ onDetail: () => json({ status }, status) })
    renderSalesApp(PATH)

    await waitFor(() => expect(document.querySelector('[data-state="forbidden"], [data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByTestId('sales-price-control-table')).toBeNull()
    expect(screen.queryByTestId('sales-price-control-line-1')).toBeNull()
  })

  it('retries a 503 detail read and then shows the comparison', async () => {
    let attempts = 0
    installFetch({
      onDetail: () => (++attempts === 1 ? json({ status: 503 }, 503) : json(detail())),
      onLinePricing: () => json(pricing()),
    })
    renderSalesApp(PATH)

    await findState('service-unavailable')
    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByTestId('sales-price-control-line-1')).toBeTruthy()
  })

  it.each<[string, () => Body | Record<string, unknown>]>([
    ['a malformed detail', () => ({ draft: { id: DRAFT } })],
    ['a detail of another company', () => detail({ companyId: COMPANY_B })],
  ])('fails closed on %s', async (_name, body) => {
    installFetch({ onDetail: () => json(body()) })
    renderSalesApp(PATH)

    await findState('malformed-response')
    expect(screen.queryByTestId('sales-price-control-table')).toBeNull()
  })

  it('shows a stale notice, not figures, when the pricing belongs to another version, and re-reads on request', async () => {
    let version = '4'
    const harness = installFetch({ onDetail: () => json(detail()), onLinePricing: () => json(pricing(version)) })
    renderSalesApp(PATH)

    const stale = await screen.findByTestId('sales-price-control-stale')
    expect(screen.queryByTestId('sales-price-control-table')).toBeNull()
    version = '3'
    const before = harness.calls.filter((call) => call.url.includes('/line-pricing')).length
    await userEvent.click(within(stale).getByRole('button', { name: T.lines.refresh }))
    expect(await screen.findByTestId('sales-price-control-table')).toBeTruthy()
    expect(harness.calls.filter((call) => call.url.includes('/line-pricing')).length).toBeGreaterThan(before)
  })

  it('refuses pricing that does not describe exactly this draft’s lines', async () => {
    installFetch({ onDetail: () => json(detail()), onLinePricing: () => json(pricing('3', { lineId: 'ffffffff-ffff-4fff-8fff-ffffffffffff' })) })
    renderSalesApp(PATH)

    const block = await screen.findByTestId('sales-price-control-unreadable')
    await findState('malformed-response', block)
    expect(screen.queryByTestId('sales-price-control-table')).toBeNull()
  })

  it('shows an unreadable pricing read as its own state and keeps the margin unavailable', async () => {
    installFetch({ onDetail: () => json(detail()), onLinePricing: () => json({ status: 503 }, 503) })
    renderSalesApp(PATH)

    const block = await screen.findByTestId('sales-price-control-unreadable')
    expect(within(block).getByText(T.lines.unreadable)).toBeTruthy()
    await findState('service-unavailable', block)
    expect(within(screen.getByTestId('sales-price-control-margin')).getByText(T.margin.unavailable)).toBeTruthy()
  })

  it('routes a 403 detail read through the real auth boundary: the page closes and no figure or pricing read is left', async () => {
    const harness = installFetch({ onDetail: () => json({ status: 403 }, 403), onLinePricing: () => json(pricing()) })
    renderSalesApp(PATH)

    // The house rule: a current 403 is the shell's (capability re-fence), so the protected route closes as the neutral block.
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByTestId('sales-price-control-lines')).toBeNull()
    expect(screen.queryByTestId('sales-price-control-table')).toBeNull()
    expect(harness.calls.some((call) => call.url.includes('/line-pricing'))).toBe(false)
  })

  it('routes a 403 pricing read through the real auth boundary: the comparison closes and no figure is left', async () => {
    installFetch({ onDetail: () => json(detail()), onLinePricing: () => json({ status: 403 }, 403) })
    renderSalesApp(PATH)

    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByTestId('sales-price-control-lines')).toBeNull()
    expect(screen.queryByTestId('sales-price-control-summary')).toBeNull()
    expect(screen.queryByText('42000.50 TRY')).toBeNull()
  })

  it.each<[string, () => unknown]>([
    ['a pricing body without lines', () => ({ version: '3' })],
    ['a pricing line with an unknown member', () => ({ ...pricing(), lines: [{ ...pricing().lines[0], margin: '12' }] })],
    ['a pricing line with a numeric net', () => pricing('3', { netUnitPrice: 34320 })],
    ['a non-object pricing body', () => ['SENTETIK']],
  ])('fails closed on %s and paints no figure', async (_name, body) => {
    installFetch({ onDetail: () => json(detail()), onLinePricing: () => json(body()) })
    renderSalesApp(PATH)

    const block = await screen.findByTestId('sales-price-control-unreadable')
    await findState('malformed-response', block)
    expect(screen.queryByTestId('sales-price-control-table')).toBeNull()
    expect(screen.queryByTestId('sales-price-control-summary')).toBeNull()
  })

  it('opens under the detail capability only: without it nothing is read', async () => {
    const harness = installFetch({ capabilities: [SALES_CAPABILITY.draftsList], onDetail: () => json(detail()) })
    renderSalesApp(PATH)

    await findState('not-found')
    expect(harness.salesCalls()).toEqual([])
  })

  it('links S-04 to this comparison', async () => {
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()), onLinePricing: () => json(pricing()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)

    const link = await screen.findByTestId('sales-open-price-control')
    expect(link.getAttribute('href')).toBe(PATH)
    await userEvent.click(link)
    await waitFor(() => expect(screen.getByTestId('sales-price-control-lines')).toBeTruthy())
  })
})
