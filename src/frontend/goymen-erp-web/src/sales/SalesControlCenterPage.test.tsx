/**
 * UI-65 "Kontrol Merkezi" in the real shell (`AppShell`, `CapabilityRoute`) against the wire body pinned to API-37's schema
 * (./examples). Component evidence, NOT_ENV: the route opens only on the S-08 id that stands on the same `sales.invoices.read`;
 * the read is company-bound and sends no query member; the server's order, severity and ages are shown as given; the kind filter
 * narrows the answer on screen only; loading, empty, filtered-empty, unavailable, truncated, forbidden, 503 and malformed each have
 * their state; a 401 goes through the real auth boundary, offline and an unreachable server keep their own retryable states, and a
 * response that arrives for a company the shell has already left is never shown (#1014 P3). Data SENTETIK.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import controlCenterResponse from './examples/control-center-response.json'
import { SALES_CAPABILITY } from './capabilities'
import { CONTROL_CENTER_KIND_TEXT, CONTROL_CENTER_STATE_TEXT, CONTROL_CENTER_TEXT as T } from './controlCenterText'
import { COMPANY_A, COMPANY_B, installPoolFetch, json, type PoolCall } from './poolFixtures'
import { renderPool } from './poolTestHarness'
import { deferred } from './testHarness'
import { CAPABILITY } from '../shell/capabilities'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const PATH = '/satis/kontrol-merkezi'
const GRANTED = [SALES_CAPABILITY.invoiceCandidatesList, SALES_CAPABILITY.draftsDetail]
const notFound = (call: PoolCall): Response => {
  throw new Error(`unexpected ${call.method} ${call.url}`)
}
const problem = (status: number) => json({ status }, status, { 'Content-Type': 'application/problem+json' })
const clone = <V,>(value: V): V => JSON.parse(JSON.stringify(value)) as V
type Body = typeof controlCenterResponse

function controlCenter(answer: () => Response = () => json(controlCenterResponse)) {
  return (call: PoolCall): Response => (call.url.includes('/sales/control-center') ? answer() : notFound(call))
}

const itemRows = async () => within(await screen.findByRole('table', { name: T.itemsCaption })).getAllByRole('row').slice(1)

describe('route gate (S-08 id, sales.invoices.read)', () => {
  it('closes the route onto the neutral block and sends no read when the shell does not grant the id', async () => {
    const fetch = installPoolFetch(notFound, [SALES_CAPABILITY.dispatchesList, SALES_CAPABILITY.draftsDetail])
    renderPool(PATH)
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(fetch.calls).toHaveLength(0)
  })
})

describe('Kontrol Merkezi (API-37)', () => {
  it('reads the company-bound route with no query member and shows the checks and the items in the server order', async () => {
    const fetch = installPoolFetch(controlCenter(), GRANTED)
    renderPool(PATH)
    const rows = await itemRows()
    expect(screen.getByRole('heading', { level: 1, name: T.heading })).toBeTruthy()
    expect(fetch.calls).toHaveLength(1)
    expect(fetch.calls[0].method).toBe('GET')
    expect(fetch.calls[0].companyId).toBe(COMPANY_A)
    expect(fetch.calls[0].url).toMatch(/\/api\/v1\/sales\/control-center$/)

    expect(rows.map((row) => row.getAttribute('data-reference'))).toEqual(controlCenterResponse.items.map((item) => item.reference))
    expect(within(rows[0]).getByText('Kritik')).toBeTruthy()
    expect(within(rows[0]).getByText('11')).toBeTruthy()
    expect(within(rows[2]).getByText('Onay gerekli')).toBeTruthy()
    expect(within(rows[4]).getByText('Bilgi')).toBeTruthy()
    expect(screen.getByTestId('sales-control-center-grace-days').textContent).toBe(T.graceDaysValue(7))
    // No amount is shown anywhere.
    expect(screen.queryByText(/TRY|₺/)).toBeNull()

    const checks = screen.getByRole('table', { name: T.checksCaption })
    expect(within(checks).getAllByRole('row').slice(1)).toHaveLength(6)
    expect(screen.getByTestId('sales-control-center-check-UninvoicedDispatchOverdue-count').textContent).toBe('1')
    expect(screen.getByTestId('sales-control-center-check-InvoiceFinalizationBlocked-count').textContent).toBe('0')
    expect(screen.getByTestId('sales-control-center-check-InvoiceNeedsReview-state').textContent).toBe(CONTROL_CENTER_STATE_TEXT.Measured)
    expect(screen.queryByTestId('sales-control-center-truncated')).toBeNull()
    expect(screen.queryByTestId('sales-control-center-unavailable')).toBeNull()

    const tabs = screen.getByRole('navigation', { name: 'Satış irsaliye ve fatura ekranları' })
    expect(within(tabs).getByRole('link', { name: 'Kontrol Merkezi' }).getAttribute('aria-current')).toBe('page')
  })

  it('links the overdue dispatch to its order and to the invoice candidates of its account; invoice rows carry no order link', async () => {
    installPoolFetch(controlCenter(), GRANTED)
    renderPool(PATH)
    const rows = await itemRows()
    expect(within(rows[0]).getByRole('link', { name: T.openOrder }).getAttribute('href')).toContain('c3c3c3c3-0000-4000-8000-000000000007')
    expect(within(rows[0]).getByRole('link', { name: T.openCandidates }).getAttribute('href')).toBe(
      '/satis/faturaya-hazir?cari=44444444-4444-4444-8444-444444444444',
    )
    expect(within(rows[1]).queryByRole('link', { name: T.openOrder })).toBeNull()
    expect(within(rows[1]).queryByRole('link', { name: T.openCandidates })).toBeNull()
    // Without the Cari card capability the account identifier stands in its place.
    expect(within(rows[0]).getByText('44444444-4444-4444-8444-444444444444')).toBeTruthy()
    expect(within(rows[0]).queryByRole('link', { name: T.openCari })).toBeNull()
  })

  it('shows no order link without the order detail id, and a Cari card link with its own id', async () => {
    installPoolFetch(controlCenter(), [SALES_CAPABILITY.invoiceCandidatesList, CAPABILITY.cariPartiesDetail])
    renderPool(PATH)
    const rows = await itemRows()
    expect(within(rows[0]).queryByRole('link', { name: T.openOrder })).toBeNull()
    expect(within(rows[0]).getByRole('link', { name: T.openCari }).getAttribute('href')).toContain('33333333-3333-4333-8333-333333333333')
  })

  it('narrows the list on screen by kind without a new read, and an empty kind is filtered-empty', async () => {
    const user = userEvent.setup()
    const fetch = installPoolFetch(controlCenter(), GRANTED)
    const { router } = renderPool(PATH)
    await itemRows()
    await user.click(screen.getByRole('link', { name: T.showOnlyLabel(CONTROL_CENTER_KIND_TEXT.InvoiceNeedsReview) }))
    await waitFor(() => expect(router.state.location.search).toBe('?tur=InvoiceNeedsReview'))
    const rows = await itemRows()
    expect(rows.map((row) => row.getAttribute('data-reference'))).toEqual(['FTR-SENTETIK-000006'])
    expect(screen.getByText(T.filteredTo(CONTROL_CENTER_KIND_TEXT.InvoiceNeedsReview))).toBeTruthy()
    expect(fetch.calls).toHaveLength(1)
    // A kind with nothing listed offers no "show only" link.
    expect(screen.queryByRole('link', { name: T.showOnlyLabel(CONTROL_CENTER_KIND_TEXT.InvoiceFinalizationBlocked) })).toBeNull()
    await user.click(screen.getByRole('link', { name: T.allKinds }))
    await waitFor(() => expect(router.state.location.search).toBe(''))
    expect(await itemRows()).toHaveLength(5)
    cleanup()
    vi.unstubAllGlobals()

    installPoolFetch(controlCenter(), GRANTED)
    renderPool(`${PATH}?tur=InvoiceFinalizationBlocked`)
    expect(await screen.findByText(/^Sonuç yok/)).toBeTruthy()
    expect(screen.queryByRole('table', { name: T.itemsCaption })).toBeNull()
    cleanup()
    vi.unstubAllGlobals()

    // An unknown kind in the address narrows nothing.
    installPoolFetch(controlCenter(), GRANTED)
    renderPool(`${PATH}?tur=Other`)
    expect(await itemRows()).toHaveLength(5)
  })

  it('says an unavailable check has no result and a truncated check may be incomplete, never an empty list', async () => {
    const body = clone(controlCenterResponse) as Body
    body.items = body.items.filter((item) => item.kind !== 'UninvoicedDispatchOverdue')
    body.checks[0] = { kind: 'UninvoicedDispatchOverdue', state: 'Unavailable', truncated: false }
    body.checks[1] = { kind: 'InvoiceNeedsReview', state: 'NoSource', truncated: true }
    installPoolFetch(controlCenter(() => json(body)), GRANTED)
    renderPool(PATH)
    await itemRows()
    expect(screen.getByTestId('sales-control-center-unavailable').textContent).toBe(T.unavailableNotice)
    expect(screen.getByTestId('sales-control-center-truncated').textContent).toBe(T.truncatedNotice)
    expect(screen.getByTestId('sales-control-center-check-UninvoicedDispatchOverdue-state').textContent).toBe(
      CONTROL_CENTER_STATE_TEXT.Unavailable,
    )
    expect(screen.queryByTestId('sales-control-center-check-UninvoicedDispatchOverdue-count')).toBeNull()
    expect(screen.getByTestId('sales-control-center-check-InvoiceNeedsReview-state').textContent).toBe(CONTROL_CENTER_STATE_TEXT.NoSource)
  })

  it('shows empty, 403, 503 with retry and a malformed body as their own states', async () => {
    const user = userEvent.setup()
    const empty = clone(controlCenterResponse) as Body
    empty.items = []
    installPoolFetch(controlCenter(() => json(empty)), GRANTED)
    renderPool(PATH)
    expect(await screen.findByText(/^Kayıt yok/)).toBeTruthy()
    expect(screen.getByRole('table', { name: T.checksCaption })).toBeTruthy()
    cleanup()
    vi.unstubAllGlobals()

    const extra = clone(controlCenterResponse) as Body & { total?: string }
    extra.total = '0'
    for (const [answer, title] of [
      [() => problem(403), /Yetkiniz yok|Bulunamadı — sayfa/],
      [() => json(extra), /Beklenmeyen yanıt/],
    ] as const) {
      installPoolFetch(controlCenter(answer), GRANTED)
      renderPool(PATH)
      expect(await screen.findByText(title)).toBeTruthy()
      expect(screen.queryByRole('table', { name: T.itemsCaption })).toBeNull()
      cleanup()
      vi.unstubAllGlobals()
    }

    let attempts = 0
    const fetch = installPoolFetch(controlCenter(() => (++attempts === 1 ? problem(503) : json(controlCenterResponse))), GRANTED)
    renderPool(PATH)
    expect(await screen.findByText(/Servis geçici olarak kullanılamıyor/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await itemRows()).toHaveLength(5)
    expect(fetch.calls).toHaveLength(2)
  })
})

describe('session, connection and company context (#1014 P3)', () => {
  it('routes a 401 through the real auth boundary: the session ends and no list is shown', async () => {
    const fetch = installPoolFetch(controlCenter(() => problem(401)), GRANTED)
    renderPool(PATH)
    expect(await screen.findByText('Oturum sonlandı — oturum')).toBeTruthy()
    expect(screen.queryByRole('table', { name: T.itemsCaption })).toBeNull()
    expect(screen.queryByRole('table', { name: T.checksCaption })).toBeNull()
    expect(fetch.calls).toHaveLength(1)
  })

  it('shows offline as its own state and retries the same company-bound read once the connection is back', async () => {
    const user = userEvent.setup()
    let online = false
    vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online)
    let attempts = 0
    const fetch = installPoolFetch(
      controlCenter(() => {
        if (++attempts === 1) throw new TypeError('SENTETIK offline')
        return json(controlCenterResponse)
      }),
      GRANTED,
    )
    renderPool(PATH)
    expect(await screen.findByText(`Bağlantı yok — ${T.subject}`)).toBeTruthy()
    expect(screen.queryByText(/Sunucuya ulaşılamadı/)).toBeNull()
    expect(screen.queryByRole('table', { name: T.itemsCaption })).toBeNull()
    online = true
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await itemRows()).toHaveLength(5)
    expect(fetch.calls).toHaveLength(2)
    expect(fetch.calls[1]).toEqual(fetch.calls[0])
  })

  it('shows an unreachable server as network-error, not offline, and retries the same read', async () => {
    const user = userEvent.setup()
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true)
    let attempts = 0
    const fetch = installPoolFetch(
      controlCenter(() => {
        if (++attempts === 1) throw new TypeError('SENTETIK network failure')
        return json(controlCenterResponse)
      }),
      GRANTED,
    )
    renderPool(PATH)
    expect(await screen.findByText(`Sunucuya ulaşılamadı — ${T.subject}`)).toBeTruthy()
    expect(screen.queryByText(/Bağlantı yok/)).toBeNull()
    expect(screen.queryByRole('table', { name: T.itemsCaption })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await itemRows()).toHaveLength(5)
    expect(fetch.calls).toHaveLength(2)
    expect(fetch.calls[1]).toEqual(fetch.calls[0])
  })

  it.each([200, 401, 403])(
    'never shows a departed company response (%s) after a company switch, nor after switching back',
    async (status) => {
      const user = userEvent.setup()
      const pending = deferred<Response>()
      const late = clone(controlCenterResponse) as Body
      late.items = [{ ...late.items[0], reference: 'IRS-SENTETIK-ESKI-SIRKET' }]
      const empty = clone(controlCenterResponse) as Body
      empty.items = []
      let aReads = 0
      const fetch = installPoolFetch((call) => {
        if (!call.url.includes('/sales/control-center')) return notFound(call)
        if (call.companyId === COMPANY_A && ++aReads === 1) return pending.promise
        return json(empty)
      }, GRANTED)
      renderPool(PATH)
      await waitFor(() => expect(fetch.calls.some((call) => call.companyId === COMPANY_A)).toBe(true))

      await user.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
      expect(await screen.findByText(/^Kayıt yok/)).toBeTruthy()
      expect(fetch.calls.some((call) => call.companyId === COMPANY_B)).toBe(true)

      await act(async () => {
        pending.resolve(status === 200 ? json(late) : problem(status))
      })
      // The late answer for company A neither renders nor moves the session or the page of company B.
      expect(screen.queryByText('IRS-SENTETIK-ESKI-SIRKET')).toBeNull()
      expect(screen.queryByText('Oturum sonlandı — oturum')).toBeNull()
      expect(screen.queryByText(/Yetkiniz yok|Bulunamadı — sayfa/)).toBeNull()
      expect(screen.getByText(/^Kayıt yok/)).toBeTruthy()

      await user.selectOptions(screen.getByLabelText('Şirket'), COMPANY_A)
      await waitFor(() => expect(aReads).toBe(2))
      expect(await screen.findByText(/^Kayıt yok/)).toBeTruthy()
      expect(screen.queryByText('IRS-SENTETIK-ESKI-SIRKET')).toBeNull()
      expect(fetch.calls.every((call) => call.method === 'GET' && call.idempotencyKey === null)).toBe(true)
    },
  )
})
