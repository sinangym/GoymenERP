/**
 * The first-release party ledger (cari hesap ekstresi): the decoder's fail-closed rules and the screen — rows per
 * currency with the server's running balance from zero (OPEN-018), totals, every row's link to its source, redaction,
 * the refusals, and the user path: open the ledger from a party account, pick a range, open a row's source, come back
 * to the same range. Component evidence only (SENTETIK); the browser evidence is fin01's FIN01-07.
 */

import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router'
import { ShellDispatchContext, ShellStateContext } from '../shell/shellContext'
import { FinancePortProvider } from './financePort'
import { AMOUNT_REDACTED } from './financeText'
import { partyLedgerPath } from './capabilities'
import { PartyLedger } from './PartyLedger'
import { decodePartyLedgerAnswer } from './partyLedgerContract'
import { IDS, LEDGER_ACCOUNT, LEDGER_PARTY, ledgerBody } from './partyLedgerFixtures'
import {
  LEDGER_EMPTY,
  LEDGER_FORBIDDEN,
  LEDGER_NO_CARRY_FORWARD,
  LEDGER_OPEN,
  LEDGER_RANGE_INVALID,
  LEDGER_TOO_MANY,
  LEDGER_UNAVAILABLE,
} from './partyLedgerText'
import { ReceiptDraftPage, ReceiptListPage } from './ReceiptPages'
import { COMPANY_A, body, found, recordingPort, renderFinance, shellFor } from './testHarness'

const RANGE = { from: '2026-09-01', to: '2026-09-30' }
/** A range the default (this month up to today) can never equal, so choosing it always makes one history entry. */
const ROUTE_RANGE = { from: '2025-01-01', to: '2025-12-31' }

function ledger(options: Parameters<typeof ledgerBody>[0] = {}) {
  return ledgerBody(options) as unknown as Record<string, unknown>
}

function currencies(value: Record<string, unknown>) {
  return value.currencies as Record<string, unknown>[]
}

function entries(value: Record<string, unknown>, index = 0) {
  return currencies(value)[index].entries as Record<string, unknown>[]
}

describe('party ledger decoder', () => {
  it('accepts the server shape, a fully redacted ledger and the two refusal markers', () => {
    expect(decodePartyLedgerAnswer(ledger(), COMPANY_A).ok).toBe(true)
    expect(decodePartyLedgerAnswer(ledger({ redacted: true }), COMPANY_A).ok).toBe(true)
    expect(decodePartyLedgerAnswer({ partyLedgerRefusal: 'TooManyEntries' }, COMPANY_A))
      .toEqual({ ok: true, value: { kind: 'refused', refusal: 'TooManyEntries' } })
    expect(decodePartyLedgerAnswer({ partyLedgerRefusal: 'Invalid' }, COMPANY_A).ok).toBe(true)
    expect(decodePartyLedgerAnswer({ partyLedgerRefusal: 'Other' }, COMPANY_A).ok).toBe(false)
  })

  it('refuses another company, a carried-in balance, a crossed column and a row outside the range', () => {
    expect(decodePartyLedgerAnswer(ledger(), 'bbbbbbbb-0000-4000-8000-000000000002').ok).toBe(false)
    expect(decodePartyLedgerAnswer({ ...ledger(), carryForward: 'Opening' }, COMPANY_A).ok).toBe(false)
    const crossed = ledger()
    entries(crossed)[1].side = 'Debit'
    expect(decodePartyLedgerAnswer(crossed, COMPANY_A).ok).toBe(false)
    const outside = ledger()
    entries(outside)[0].occurredOn = '2026-08-31'
    expect(decodePartyLedgerAnswer(outside, COMPANY_A).ok).toBe(false)
    const unordered = ledger()
    entries(unordered)[1].occurredOn = '2026-09-02'
    expect(decodePartyLedgerAnswer(unordered, COMPANY_A).ok).toBe(false)
  })

  it('refuses a withheld amount with a side or digits, a closing balance that is not the last row, and loose links', () => {
    const sided = ledger({ redacted: true })
    ;(entries(sided)[0].runningBalance as Record<string, unknown>).side = 'Debit'
    expect(decodePartyLedgerAnswer(sided, COMPANY_A).ok).toBe(false)
    const digits = ledger({ redacted: true })
    entries(digits)[0].amount = { disclosure: 'Redacted', money: { amount: '1000.00', currencyId: 'x' } }
    expect(decodePartyLedgerAnswer(digits, COMPANY_A).ok).toBe(false)
    const closing = ledger()
    currencies(closing)[0].closingBalance = { side: 'Debit', amount: { disclosure: 'Visible', money: { amount: '1.00', currencyId: currencies(closing)[0].currencyId } } }
    expect(decodePartyLedgerAnswer(closing, COMPANY_A).ok).toBe(false)
    const reversalWithoutOriginal = ledger()
    entries(reversalWithoutOriginal)[2].reversesSourceId = null
    expect(decodePartyLedgerAnswer(reversalWithoutOriginal, COMPANY_A).ok).toBe(false)
    const openItemWithoutDocument = ledger()
    entries(openItemWithoutDocument)[0].documentId = null
    expect(decodePartyLedgerAnswer(openItemWithoutDocument, COMPANY_A).ok).toBe(false)
    const zeroSided = ledger()
    currencies(zeroSided)[1].debitTotal = { disclosure: 'Visible', money: { amount: '0.00', currencyId: currencies(zeroSided)[1].currencyId } }
    expect(decodePartyLedgerAnswer(zeroSided, COMPANY_A).ok).toBe(true)
    const duplicate = ledger()
    currencies(duplicate).push(currencies(duplicate)[0])
    expect(decodePartyLedgerAnswer(duplicate, COMPANY_A).ok).toBe(false)
  })
})

function renderLedger(reply: ReturnType<typeof found>, onRange = vi.fn()) {
  const { port, reads } = recordingPort(() => reply)
  const view = renderFinance(
    <PartyLedger partyId={LEDGER_PARTY} companyAccountId={LEDGER_ACCOUNT} range={RANGE} onRange={onRange} />,
    { port, router: true },
  )
  return { ...view, reads, onRange }
}

function row(container: HTMLElement, sourceKind: string): HTMLElement {
  return container.querySelector(`[data-ledger-kind="${sourceKind}"]`) as HTMLElement
}

describe('PartyLedger', () => {
  it('shows each currency apart with the server balances from zero, the totals and each row\'s source', async () => {
    const { container, reads } = renderLedger(found(ledgerBody()))
    await waitFor(() => expect(container.querySelectorAll('table[data-ledger-currency]')).toHaveLength(2))
    expect(screen.getByText(LEDGER_NO_CARRY_FORWARD)).toBeTruthy()
    expect(reads.find((read) => read.method === 'partyLedger')?.request)
      .toEqual({ partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, ...RANGE })

    const tl = container.querySelector('table[data-ledger-currency^="c7"]') as HTMLElement
    const texts = (element: HTMLElement) => within(element).getAllByRole('cell').map((cell) => cell.textContent)
    expect(texts(row(tl, 'OpenItem'))).toEqual(['10.09.2026', expect.stringContaining('…000000d1'), '10.10.2026',
      '1.000,00', '', '1.000,00 (B)'])
    expect(texts(row(tl, 'ManualReceipt'))).toEqual(['15.09.2026', expect.any(String), '', '', '400,00', '600,00 (B)'])
    expect(texts(row(tl, 'CustomerInstrumentCollection')).slice(-2)).toEqual(['1.200,00', '150,00 (A)'])
    const totals = tl.querySelector('[data-ledger-totals]') as HTMLElement
    expect(within(totals).getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['1.450,00', '1.600,00', ''])
    expect((tl.querySelector('[data-ledger-closing] td') as HTMLElement).textContent).toBe('150,00 (A)')

    // Every row opens its source on the existing screens; the open item has none yet and is shown by reference.
    const hrefs = (kind: string) => within(row(tl, kind)).queryAllByRole('link').map((link) => link.getAttribute('href'))
    expect(hrefs('ManualReceipt')).toEqual([`/finans/tahsilatlar/${IDS.receipt}`])
    expect(hrefs('ManualReceiptReversal')).toEqual([`/finans/tahsilatlar/${IDS.reversal}`, `/finans/tahsilatlar/${IDS.receipt}`])
    expect(hrefs('CustomerInstrumentCollection'))
      .toEqual([`/finans/musteri-cek-senetleri?cekSenet=${IDS.instrument}`, `/finans/tahsilatlar/${IDS.collection}`])
    expect(hrefs('ManualBankPayment')).toEqual([`/finans/odemeler/${IDS.payment}`])
    expect(hrefs('OpenItem')).toEqual([])
  })

  it('without the amount grant shows the rows but no digit and no balance side', async () => {
    const { container } = renderLedger(found(ledgerBody({ redacted: true })))
    await waitFor(() => expect(container.querySelectorAll('[data-ledger-entry]')).toHaveLength(6))
    expect(container.querySelectorAll('[data-amount-kind="visible"]')).toHaveLength(0)
    expect(screen.getAllByText(AMOUNT_REDACTED).length).toBeGreaterThanOrEqual(6 * 2 + 2 * 3)
    const tables = [...container.querySelectorAll('table')].map((table) => table.textContent ?? '')
    tables.forEach((text) => {
      expect(text).not.toMatch(/\(B\)|\(A\)/)
      expect(text).not.toMatch(/1\.000,00|400,00|150,00|75,50/)
    })
  })

  it('names each refusal and the empty range', async () => {
    const tooMany = renderLedger(found({ partyLedgerRefusal: 'TooManyEntries' }))
    await screen.findByText(LEDGER_TOO_MANY)
    tooMany.unmount()
    const denied = renderLedger(body({ outcome: 'Denied', resource: null }))
    await screen.findByText(LEDGER_FORBIDDEN)
    denied.unmount()
    const unavailable = renderLedger(body({ outcome: 'Unavailable', resource: null }))
    await screen.findByText(LEDGER_UNAVAILABLE)
    unavailable.unmount()
    renderLedger(found({ ...ledgerBody(), currencies: [] }))
    await screen.findByText(LEDGER_EMPTY)
  })

  it('asks once for a valid range and refuses a reversed one without asking', async () => {
    const { onRange } = renderLedger(found(ledgerBody()))
    fireEvent.change(screen.getByLabelText('Başlangıç tarihi'), { target: { value: '2026-09-16' } })
    fireEvent.change(screen.getByLabelText('Bitiş tarihi'), { target: { value: '2026-09-24' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ekstreyi göster' }))
    expect(onRange).toHaveBeenCalledTimes(1)
    expect(onRange).toHaveBeenCalledWith({ from: '2026-09-16', to: '2026-09-24' })
    fireEvent.change(screen.getByLabelText('Başlangıç tarihi'), { target: { value: '2026-09-30' } })
    fireEvent.change(screen.getByLabelText('Bitiş tarihi'), { target: { value: '2026-09-01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ekstreyi göster' }))
    expect(onRange).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('alert').textContent).toBe(LEDGER_RANGE_INVALID)
  })
})

function DetailStandIn() {
  const navigate = useNavigate()
  return (
    <section>
      <h1>Tahsilat ayrıntısı</h1>
      <button type="button" onClick={() => navigate(-1)}>Geri</button>
    </section>
  )
}

describe('the ledger on F-02', () => {
  it('opens from the party account, keeps the chosen range across a source and back, and reopens the same', async () => {
    const { port, reads } = recordingPort((method) =>
      method === 'partyLedger' ? found(ledgerBody()) : body({ outcome: 'Unavailable', resource: null }))
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
    const shell = shellFor(COMPANY_A)
    const draftEntry = `/finans/tahsilatlar/yeni?${new URLSearchParams({ partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT })}`
    render(
      <QueryClientProvider client={queryClient}>
        <ShellDispatchContext.Provider value={() => undefined}>
          <ShellStateContext.Provider value={shell}>
            <FinancePortProvider port={port}>
              <MemoryRouter initialEntries={[draftEntry]}>
                <Routes>
                  <Route path="/finans/tahsilatlar" element={<ReceiptListPage />} />
                  <Route path="/finans/tahsilatlar/yeni" element={<ReceiptDraftPage />} />
                  <Route path="/finans/tahsilatlar/:sourceId" element={<DetailStandIn />} />
                </Routes>
              </MemoryRouter>
            </FinancePortProvider>
          </ShellStateContext.Provider>
        </ShellDispatchContext.Provider>
      </QueryClientProvider>,
    )

    // From the party context, "Cari ekstre" opens this account's ledger.
    const open = await screen.findByRole('link', { name: LEDGER_OPEN })
    expect(open.getAttribute('href')).toBe(partyLedgerPath(LEDGER_PARTY, LEDGER_ACCOUNT))
    fireEvent.click(open)
    await screen.findByRole('heading', { level: 1, name: 'Cari ekstre' })

    // Pick the range: one request for exactly it.
    fireEvent.change(screen.getByLabelText('Başlangıç tarihi'), { target: { value: ROUTE_RANGE.from } })
    fireEvent.change(screen.getByLabelText('Bitiş tarihi'), { target: { value: ROUTE_RANGE.to } })
    fireEvent.click(screen.getByRole('button', { name: 'Ekstreyi göster' }))
    await waitFor(() => expect(reads.filter((read) => read.method === 'partyLedger').at(-1)?.request)
      .toEqual({ partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, ...ROUTE_RANGE }))
    const receiptLink = await screen.findByRole('link', { name: new RegExp(`^Tahsilatı aç · ref\\. …${IDS.receipt.slice(-8)}$`) })

    // Open a row's source, then come back: the same range, the same rows.
    fireEvent.click(receiptLink)
    await screen.findByRole('heading', { name: 'Tahsilat ayrıntısı' })
    fireEvent.click(screen.getByRole('button', { name: 'Geri' }))
    await screen.findByRole('heading', { level: 1, name: 'Cari ekstre' })
    expect((screen.getByLabelText('Başlangıç tarihi') as HTMLInputElement).value).toBe(ROUTE_RANGE.from)
    expect((screen.getByLabelText('Bitiş tarihi') as HTMLInputElement).value).toBe(ROUTE_RANGE.to)
    await screen.findByRole('link', { name: new RegExp(`^Tahsilatı aç · ref\\. …${IDS.receipt.slice(-8)}$`) })
    const ledgerReads = reads.filter((read) => read.method === 'partyLedger')
    expect(ledgerReads.at(-1)?.request).toEqual({ partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, ...ROUTE_RANGE })
  })

  it('reopening the ledger address shows the same range; an address without an account reads nothing', async () => {
    const { port, reads } = recordingPort(() => found(ledgerBody()))
    const shell = shellFor(COMPANY_A)
    const address = partyLedgerPath(LEDGER_PARTY, LEDGER_ACCOUNT, ROUTE_RANGE)
    // A fresh client per mount: reopening is a new visit, never an answer kept in memory.
    const tree = (entry: string) => (
      <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })}>
        <ShellDispatchContext.Provider value={() => undefined}>
          <ShellStateContext.Provider value={shell}>
            <FinancePortProvider port={port}>
              <MemoryRouter initialEntries={[entry]}>
                <Routes>
                  <Route path="/finans/tahsilatlar" element={<ReceiptListPage />} />
                </Routes>
              </MemoryRouter>
            </FinancePortProvider>
          </ShellStateContext.Provider>
        </ShellDispatchContext.Provider>
      </QueryClientProvider>
    )
    const first = render(tree(address))
    await screen.findByRole('link', { name: /^Ödemeyi aç/ })
    first.unmount()
    render(tree(address))
    await screen.findByRole('link', { name: /^Ödemeyi aç/ })
    expect(reads.filter((read) => read.method === 'partyLedger').map((read) => read.request))
      .toEqual([
        { partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, ...ROUTE_RANGE },
        { partyId: LEDGER_PARTY, companyAccountId: LEDGER_ACCOUNT, ...ROUTE_RANGE },
      ])
  })
})
