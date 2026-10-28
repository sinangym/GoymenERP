/**
 * UI-60 S-07 "Sevkiyat / İrsaliyeler" and UI-61 S-08 "Faturaya Hazır" in the real shell (`AppShell`, `CapabilityRoute`) against
 * wire bodies pinned to API-30 / API-31's schemas (./examples). Component evidence, NOT_ENV: each route opens only on the id the
 * backend publishes (Sales shell 1.9); every read is company-bound; loading, empty, forbidden, unavailable and malformed answers
 * each have their state; no amount and no invoice command is ever shown.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dispatchesResponse from './examples/dispatches-response.json'
import candidatesResponse from './examples/invoice-candidates-response.json'
import { SALES_CAPABILITY } from './capabilities'
import { DISPATCH_TEXT } from './dispatchText'
import { COMPANY_A, installPoolFetch, json, type PoolCall } from './poolFixtures'
import { renderPool } from './poolTestHarness'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const T = DISPATCH_TEXT
const BOTH = [SALES_CAPABILITY.dispatchesList, SALES_CAPABILITY.invoiceCandidatesList, SALES_CAPABILITY.draftsDetail]
const notFound = (call: PoolCall): Response => {
  throw new Error(`unexpected ${call.method} ${call.url}`)
}
const problem = (status: number) => json({ status }, status, { 'Content-Type': 'application/problem+json' })
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T

describe('route gates (Sales shell 1.9)', () => {
  it('closes both routes onto the neutral block and sends no Sales read when the shell grants neither id', async () => {
    const fetch = installPoolFetch(notFound, [])
    for (const path of ['/satis/sevkiyatlar', '/satis/faturaya-hazir']) {
      renderPool(path)
      expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
      cleanup()
    }
    expect(fetch.calls).toHaveLength(0)
  })

  it('opens the dispatch list on its own id without the invoice id, and shows no tab to the screen it does not grant', async () => {
    installPoolFetch((call) => (call.url.includes('/sales/dispatches?') ? json(dispatchesResponse) : notFound(call)), [
      SALES_CAPABILITY.dispatchesList,
    ])
    renderPool('/satis/sevkiyatlar')
    expect(await screen.findByRole('table', { name: T.dispatches.caption })).toBeTruthy()
    expect(screen.queryByRole('navigation', { name: T.common.nav })).toBeNull()
    cleanup()
    renderPool('/satis/faturaya-hazir')
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
  })
})

describe('S-07 Sevkiyat / İrsaliyeler', () => {
  it('lists the notes with state, number, customer and each shipped line, through one company-bound read', async () => {
    const fetch = installPoolFetch((call) => (call.url.includes('/sales/dispatches?') ? json(dispatchesResponse) : notFound(call)), BOTH)
    renderPool('/satis/sevkiyatlar')
    const table = await screen.findByRole('table', { name: T.dispatches.caption })
    expect(screen.getByRole('heading', { level: 1, name: T.dispatches.heading })).toBeTruthy()
    expect(within(table).getByText('IRS-000002')).toBeTruthy()
    expect(within(table).getByText(T.dispatches.draftNumber)).toBeTruthy()
    expect(within(table).getByText('Onaylı')).toBeTruthy()
    expect(within(table).getByText('Taslak')).toBeTruthy()
    expect(within(table).getAllByText('CAR-000101 — SENTETIK Sevk Müşterisi A.Ş.')).toHaveLength(2)
    expect(within(table).getByText('1250.5 kg')).toBeTruthy()
    expect(within(table).getByText('40 adet')).toBeTruthy()
    // The order line opens Sipariş 360 only with that screen's grant.
    expect(within(table).getByRole('link', { name: 'Sipariş SO-2026-000007 / 1' }).getAttribute('href')).toBe(
      '/satis/siparisler/c3c3c3c3-0000-4000-8000-000000000001',
    )
    // The invoicing side is "not tracked yet", never a guessed state; no amount, no invoice command.
    expect(screen.getByText(T.common.invoicingNoSource)).toBeTruthy()
    expect(screen.queryByText(/TL|₺|Fatura Kes/)).toBeNull()
    expect(screen.queryByRole('button', { name: /fatura/i })).toBeNull()
    // Both tabs, the current one marked.
    const tabs = screen.getByRole('navigation', { name: T.common.nav })
    expect(within(tabs).getByRole('link', { name: T.common.dispatchesTab }).getAttribute('aria-current')).toBe('page')
    expect(fetch.calls).toHaveLength(1)
    expect(fetch.calls[0].companyId).toBe(COMPANY_A)
    expect(fetch.calls[0].url).toContain('/sales/dispatches?pageSize=50')
  })

  it('narrows on the server by the exact state and pages by the server cursor', async () => {
    const user = userEvent.setup()
    const next = clone(dispatchesResponse)
    next.items = [next.items[1]]
    next.items[0].id = 'd7d7d7d7-0000-4000-8000-000000000009'
    next.nextCursor = null as unknown as string
    const fetch = installPoolFetch((call) => {
      if (call.url.includes('cursor=')) return json(next)
      if (call.url.includes('state=Draft')) return json({ items: [dispatchesResponse.items[1]], nextCursor: null })
      if (call.url.includes('/sales/dispatches?')) return json(dispatchesResponse)
      return notFound(call)
    }, BOTH)
    renderPool('/satis/sevkiyatlar')
    await screen.findByRole('table', { name: T.dispatches.caption })
    await user.click(screen.getByRole('button', { name: /Daha fazla/ }))
    await waitFor(() => expect(document.querySelectorAll('[data-dispatch-id]')).toHaveLength(3))
    expect(fetch.calls[1].url).toContain('cursor=c2FsZXMtZGlzcGF0Y2gtY3Vyc29y')

    await user.selectOptions(screen.getByLabelText(T.dispatches.filter), 'Draft')
    await waitFor(() => expect(document.querySelectorAll('[data-dispatch-id]')).toHaveLength(1))
    expect(fetch.calls.at(-1)?.url).toContain('state=Draft')
  })

  it.each([
    // A current 403 goes to the shared auth boundary (capability re-fence): the screen closes, it never shows rows or "empty".
    [403, /Yetkiniz yok|Bulunamadı — sayfa/],
    [503, /Servis geçici olarak kullanılamıyor/],
  ])('shows the %s answer as its own state, never as an empty list', async (status, title) => {
    installPoolFetch((call) => (call.url.includes('/sales/dispatches?') ? problem(status) : notFound(call)), BOTH)
    renderPool('/satis/sevkiyatlar')
    expect(await screen.findByText(title)).toBeTruthy()
    expect(screen.queryByText(/Kayıt yok/)).toBeNull()
    expect(screen.queryByRole('table', { name: T.dispatches.caption })).toBeNull()
  })

  it('refuses a note of another company as a malformed answer, and an empty list is "Kayıt yok"', async () => {
    const foreign = clone(dispatchesResponse)
    foreign.items[0].companyId = '11111111-1111-4111-8111-111111111111'
    installPoolFetch((call) => (call.url.includes('/sales/dispatches?') ? json(foreign) : notFound(call)), BOTH)
    renderPool('/satis/sevkiyatlar')
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByText('IRS-000002')).toBeNull()
    cleanup()

    installPoolFetch((call) => (call.url.includes('/sales/dispatches?') ? json({ items: [], nextCursor: null }) : notFound(call)), BOTH)
    renderPool('/satis/sevkiyatlar')
    expect(await screen.findByText(/Kayıt yok/)).toBeTruthy()
  })
})

describe('S-08 Faturaya Hazır', () => {
  it('shows each group with its customer, unresolved key parts, per-unit totals and shares — no amount, no invoice command', async () => {
    const fetch = installPoolFetch((call) => (call.url.includes('/sales/invoice-candidates') ? json(candidatesResponse) : notFound(call)), BOTH)
    renderPool('/satis/faturaya-hazir')
    const group = await screen.findByRole('region', { name: T.candidates.group(1) })
    expect(within(group).getByText('CAR-000101 — SENTETIK Sevk Müşterisi A.Ş.')).toBeTruthy()
    expect(within(group).getByText(T.candidates.rateNotApplicable)).toBeTruthy()
    expect(within(within(group).getByRole('list', { name: T.candidates.totals })).getByText('4.5 kg')).toBeTruthy()
    // SAT-D101: no due-date source — "vade eksik", not invoiceable; the invoice type is the only unresolved part (SAT-D102: no
    // e-document part).
    expect(within(group).getByTestId('sales-invoice-group-1-due-date-missing').textContent).toBe(T.candidates.dueDateMissing)
    const incomplete = within(group).getByTestId('sales-invoice-group-1-incomplete')
    expect(within(incomplete).getByText(T.candidates.notInvoiceable)).toBeTruthy()
    expect(within(within(incomplete).getByRole('list', { name: T.candidates.incomplete })).getAllByRole('listitem').map((item) => item.textContent))
      .toEqual(['Fatura tipi (SATIŞ / TEVKİFAT / İSTİSNA / İHRAÇ KAYITLI)'])
    // The foreign-currency group of one shipment day: its rate value is unresolved.
    const foreign = screen.getByRole('region', { name: T.candidates.group(2) })
    expect(within(foreign).getByText(/sevk gününden önceki iş günü TCMB kuru — kur kaydı henüz yok/)).toBeTruthy()
    expect(within(within(foreign).getByRole('list', { name: T.candidates.incomplete })).getAllByRole('listitem')).toHaveLength(2)
    const shares = within(group).getByRole('table', { name: T.candidates.sharesCaption(1) })
    expect(within(shares).getByText('IRS-000002 / 1')).toBeTruthy()
    expect(within(shares).getByText('1.5 kg')).toBeTruthy()
    expect(screen.getByText(T.common.noAmounts)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /fatura/i })).toBeNull()
    expect(screen.queryByTestId('sales-invoice-candidates-truncated')).toBeNull()
    expect(fetch.calls).toHaveLength(1)
    expect(fetch.calls[0].companyId).toBe(COMPANY_A)
    expect(fetch.calls[0].url).toMatch(/\/sales\/invoice-candidates$/)
  })

  it('narrows to one customer on the server and back to all', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch((call) => (call.url.includes('/sales/invoice-candidates') ? json(candidatesResponse) : notFound(call)), BOTH)
    const { router } = renderPool('/satis/faturaya-hazir')
    const group = await screen.findByRole('region', { name: T.candidates.group(1) })
    await user.click(within(group).getByRole('link', { name: T.candidates.onlyCustomer }))
    await waitFor(() => expect(fetch.calls).toHaveLength(2))
    expect(fetch.calls[1].url).toContain('companyAccountId=44444444-4444-4444-8444-444444444444')
    expect(router.state.location.search).toBe('?cari=44444444-4444-4444-8444-444444444444')
    expect(await screen.findByText(T.candidates.filteredTo('SENTETIK Sevk Müşterisi A.Ş.'))).toBeTruthy()
    await user.click(screen.getByRole('link', { name: T.candidates.allCustomers }))
    expect(router.state.location.search).toBe('')
  })

  it('says the list was cut when the server truncated it, and shows the 403, 503 and empty answers as their own states', async () => {
    const truncated = clone(candidatesResponse)
    truncated.truncated = true
    installPoolFetch((call) => (call.url.includes('/sales/invoice-candidates') ? json(truncated) : notFound(call)), BOTH)
    renderPool('/satis/faturaya-hazir')
    expect((await screen.findByTestId('sales-invoice-candidates-truncated')).textContent).toBe(T.candidates.truncated)
    cleanup()

    for (const [answer, title] of [
      [problem(403), /Yetkiniz yok|Bulunamadı — sayfa/],
      [problem(503), /Servis geçici olarak kullanılamıyor/],
      [json({ groups: [], truncated: false, invoicedState: 'NoSource' }), /Kayıt yok/],
    ] as const) {
      installPoolFetch((call) => (call.url.includes('/sales/invoice-candidates') ? answer.clone() : notFound(call)), BOTH)
      renderPool('/satis/faturaya-hazir')
      expect(await screen.findByText(title)).toBeTruthy()
      cleanup()
      vi.unstubAllGlobals()
    }
  })

  it('never sends a malformed customer from the address and refuses another company\'s group', async () => {
    const foreign = clone(candidatesResponse)
    foreign.groups[0].key.companyId = '11111111-1111-4111-8111-111111111111'
    const fetch = installPoolFetch((call) => (call.url.includes('/sales/invoice-candidates') ? json(foreign) : notFound(call)), BOTH)
    renderPool('/satis/faturaya-hazir?cari=not-an-id')
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(fetch.calls[0].url).toMatch(/\/sales\/invoice-candidates$/)
  })
})
