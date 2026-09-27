/**
 * DEC-FIN-016 portfolio screens — receive, list, detail, the server-offered next steps, the one-key collection and
 * redaction. Component/DOM evidence only (the real API and SQL are the backend acceptance tests; the browser is fin01).
 */

import { describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { CustomerInstrumentPortfolio, CustomerInstrumentReceiveForm } from './CustomerInstruments'
import {
  INSTRUMENT_BANK,
  INSTRUMENT_COMPANY,
  INSTRUMENT_CURRENCY,
  INSTRUMENT_ID,
  INSTRUMENT_SOURCE,
  detailBody,
  instrumentBody,
  movementBody,
} from './customerInstrumentFixtures'
import { PORTFOLIO_TITLE, PORTFOLIO_UNAVAILABLE } from './customerInstrumentText'
import { AMOUNT_REDACTED } from './financeText'
import { NO_REPLY, bankPage, body, found, recordingPort, renderFinance, succeeded } from './testHarness'

const PARTY = { partyId: 'c5000000-0000-4000-8000-000000000005', companyAccountId: 'c6000000-0000-4000-8000-000000000006' }

function portfolioPort(detail: () => Record<string, unknown>, list?: () => Record<string, unknown>[]) {
  return recordingPort((method, scope) => {
    if (method === 'listBankAccounts') return found(bankPage(scope.companyId))
    if (method === 'listCustomerInstruments') {
      return found({ companyId: scope.companyId, items: list?.() ?? [instrumentBody()], nextCursor: null })
    }
    if (method === 'customerInstrumentDetail') return found(detail())
    return NO_REPLY
  })
}

describe('CustomerInstrumentPortfolio', () => {
  it('lists the portfolio and opens one instrument', async () => {
    const onSelect = vi.fn()
    const { port } = portfolioPort(() => detailBody())
    renderFinance(<CustomerInstrumentPortfolio selectedId={null} onSelect={onSelect} />, { port, router: true })
    const row = (await screen.findByRole('rowheader', { name: 'SENTETIK-CEK-1' })).closest('tr') as HTMLElement
    within(row).getByText('Müşteri çeki')
    within(row).getByText('Portföyde')
    within(row).getByText('1.000,00')
    fireEvent.click(within(row).getByRole('button', { name: 'Aç: SENTETIK-CEK-1' }))
    expect(onSelect).toHaveBeenCalledWith(INSTRUMENT_ID)
  })

  it('shows nothing to an actor without the portfolio grant and says so when the service is unavailable', async () => {
    const denied = recordingPort(() => body({ outcome: 'Denied', resource: null }))
    const first = renderFinance(<CustomerInstrumentPortfolio selectedId={null} onSelect={() => {}} />,
      { port: denied.port, router: true })
    await waitFor(() => expect(denied.reads.length).toBeGreaterThan(0))
    await waitFor(() => expect(screen.queryByRole('heading', { name: PORTFOLIO_TITLE })).toBeNull())
    first.unmount()

    const unavailable = recordingPort(() => body({ outcome: 'Unavailable', resource: null }))
    renderFinance(<CustomerInstrumentPortfolio selectedId={null} onSelect={() => {}} />, { port: unavailable.port, router: true })
    await screen.findByText(PORTFOLIO_UNAVAILABLE)
  })

  it('an actor without the amount grant sees no digits in the list or the detail', async () => {
    const redacted = instrumentBody({ amount: { disclosure: 'Redacted', money: null } })
    const { port } = portfolioPort(() => detailBody(redacted, []), () => [redacted])
    const view = renderFinance(<CustomerInstrumentPortfolio selectedId={INSTRUMENT_ID} onSelect={() => {}} />,
      { port, router: true })
    await screen.findByRole('region', { name: 'Müşteri çeki · SENTETIK-CEK-1' })
    expect(screen.getAllByText(AMOUNT_REDACTED).length).toBe(2)
    expect(view.container.textContent).not.toMatch(/1\.000|1000/)
    // No next step is offered when the server offers none.
    expect(screen.queryByRole('button', { name: 'Tahsil edildi olarak kaydet' })).toBeNull()
  })

  it('deposits to a bank with exactly the server contract and reloads the instrument', async () => {
    let current = detailBody()
    const { port, commands, reads } = portfolioPort(() => current)
    renderFinance(<CustomerInstrumentPortfolio selectedId={INSTRUMENT_ID} onSelect={() => {}} />, { port, router: true })
    const panel = await screen.findByRole('region', { name: 'Müşteri çeki · SENTETIK-CEK-1' })
    // Only the steps the server offered.
    expect(within(panel).queryByRole('button', { name: 'Bankadan portföye geri al' })).toBeNull()
    fireEvent.click(within(panel).getByRole('button', { name: 'Bankaya tahsile ver' }))
    const form = within(panel).getByRole('form', { name: 'Bankaya tahsile ver' })
    fireEvent.change(within(form).getByLabelText('İşlem tarihi'), { target: { value: '2026-09-25' } })
    await waitFor(() => expect(within(form).getAllByRole('option').length).toBeGreaterThan(1))
    fireEvent.change(within(form).getByLabelText('Banka hesabı'), { target: { value: INSTRUMENT_BANK } })
    fireEvent.click(within(form).getByRole('button', { name: 'Onayla' }))

    await waitFor(() => expect(commands).toHaveLength(1))
    expect(commands[0].method).toBe('changeCustomerInstrumentStatus')
    expect(commands[0].scope).toEqual({ companyId: INSTRUMENT_COMPANY })
    expect(commands[0].request).toEqual({
      instrumentId: INSTRUMENT_ID, expectedVersion: '1', transition: 'DepositForCollection', occurredOn: '2026-09-25',
      treasuryAccountId: INSTRUMENT_BANK, note: null,
    })
    const detailReads = reads.filter((read) => read.method === 'customerInstrumentDetail').length
    const deposited = instrumentBody({ status: 'AtBankForCollection', version: '2', treasuryAccountId: INSTRUMENT_BANK,
      statusChangedOn: '2026-09-25' })
    current = detailBody(deposited, ['WithdrawFromBank', 'Dishonour', 'Collect'],
      [movementBody(), movementBody({ version: '2', fromStatus: 'InPortfolio', toStatus: 'AtBankForCollection',
        occurredOn: '2026-09-25', treasuryAccountId: INSTRUMENT_BANK })])
    await commands[0].resolve(succeeded(deposited))
    await screen.findByText('Bankaya tahsile ver: İşlem kaydedildi.')
    await waitFor(() => expect(reads.filter((read) => read.method === 'customerInstrumentDetail').length).toBeGreaterThan(detailReads))
    await within(panel).findByText('Portföyde → Bankada (tahsilde)', { exact: false })
    within(panel).getByRole('button', { name: 'Bankadan portföye geri al' })
  })

  it('collects with one key: an unknown answer keeps it, the retry reuses it, and the collection source opens', async () => {
    let current = detailBody(instrumentBody({ status: 'AtBankForCollection', version: '2', treasuryAccountId: INSTRUMENT_BANK }),
      ['WithdrawFromBank', 'Dishonour', 'Collect'])
    const { port, commands } = portfolioPort(() => current)
    renderFinance(<CustomerInstrumentPortfolio selectedId={INSTRUMENT_ID} onSelect={() => {}} />, { port, router: true })
    const panel = await screen.findByRole('region', { name: 'Müşteri çeki · SENTETIK-CEK-1' })
    fireEvent.click(within(panel).getByRole('button', { name: 'Tahsil edildi olarak kaydet' }))
    const form = within(panel).getByRole('form', { name: 'Tahsil edildi olarak kaydet' })
    fireEvent.change(within(form).getByLabelText('Tahsil tarihi'), { target: { value: '2026-10-16' } })
    // A cheque at the bank is collected into that bank: the choice is fixed.
    const bank = within(form).getByLabelText('Banka hesabı') as HTMLSelectElement
    await waitFor(() => expect(bank.value).toBe(INSTRUMENT_BANK))
    expect(bank.disabled).toBe(true)
    fireEvent.click(within(form).getByRole('button', { name: 'Onayla' }))
    await waitFor(() => expect(commands).toHaveLength(1))
    expect(commands[0].request).toEqual({
      instrumentId: INSTRUMENT_ID, expectedVersion: '2', treasuryAccountId: INSTRUMENT_BANK, collectedOn: '2026-10-16',
      valueOn: null,
    })
    await commands[0].resolve(NO_REPLY)
    await within(form).findByText('Sonuç bilinmiyor. Aynı işlem aynı anahtarla yeniden gönderilebilir.')

    fireEvent.click(within(form).getByRole('button', { name: 'Onayla' }))
    await waitFor(() => expect(commands).toHaveLength(2))
    expect(commands[1].key).toBe(commands[0].key)
    const collected = instrumentBody({ status: 'Collected', version: '3', treasuryAccountId: INSTRUMENT_BANK,
      collectionSourceId: INSTRUMENT_SOURCE, statusChangedOn: '2026-10-16' })
    current = detailBody(collected, [])
    await commands[1].resolve(succeeded(collected, true))
    await within(panel).findByRole('link', { name: 'Tahsilat kaydını aç' })
    expect(within(panel).getByRole('link', { name: 'Tahsilat kaydını aç' }).getAttribute('href'))
      .toBe(`/finans/tahsilatlar/${INSTRUMENT_SOURCE}`)
    expect(within(panel).queryByRole('button', { name: 'Tahsil edildi olarak kaydet' })).toBeNull()
  })

  it('shows a refusal in the user\'s words and keeps the instrument as it was', async () => {
    const { port, commands } = portfolioPort(() => detailBody())
    renderFinance(<CustomerInstrumentPortfolio selectedId={INSTRUMENT_ID} onSelect={() => {}} />, { port, router: true })
    const panel = await screen.findByRole('region', { name: 'Müşteri çeki · SENTETIK-CEK-1' })
    fireEvent.click(within(panel).getByRole('button', { name: 'Karşılıksız olarak işaretle' }))
    const form = within(panel).getByRole('form', { name: 'Karşılıksız olarak işaretle' })
    fireEvent.change(within(form).getByLabelText('İşlem tarihi'), { target: { value: '2026-09-01' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Onayla' }))
    await waitFor(() => expect(commands).toHaveLength(1))
    await commands[0].resolve(body({
      outcome: 'ValidationFailed', resource: null, isReplay: false, originalCorrelationId: 'SENTETIK-1',
      issues: [{ field: 'occurredOn', code: 'instrument_date_before_previous_movement' }],
    }))
    await within(form).findByText('İşlem reddedildi: Tarih önceki hareketten önce olamaz.')
    within(panel).getByText('Portföyde')
  })
})

describe('CustomerInstrumentReceiveForm', () => {
  it('receives a cheque with the exact contract (tr-TR amount, trimmed texts) and links to the portfolio', async () => {
    const { port, commands } = recordingPort((method, scope) =>
      method === 'listBankAccounts' ? found(bankPage(scope.companyId)) : NO_REPLY)
    renderFinance(<CustomerInstrumentReceiveForm party={PARTY} />, { port, router: true })

    fireEvent.click(await screen.findByRole('button', { name: 'Portföye al' }))
    await screen.findByText('Alanları kontrol edin.')
    expect(commands).toHaveLength(0)

    fireEvent.change(screen.getByLabelText('Seri / belge no'), { target: { value: ' SENTETIK-CEK-9 ' } })
    fireEvent.change(screen.getByLabelText('Keşideci / borçlu'), { target: { value: 'SENTETIK Keşideci' } })
    fireEvent.change(screen.getByLabelText('Çekin bankası'), { target: { value: 'SENTETIK Banka' } })
    fireEvent.change(screen.getByLabelText('Vade tarihi'), { target: { value: '2026-10-15' } })
    fireEvent.change(screen.getByLabelText('Alış tarihi'), { target: { value: '2026-09-20' } })
    fireEvent.change(screen.getByLabelText('Nominal tutar'), { target: { value: '1000,50' } })
    await waitFor(() => expect((screen.getByLabelText('Para birimi') as HTMLSelectElement).disabled).toBe(false))
    fireEvent.change(screen.getByLabelText('Para birimi'), { target: { value: INSTRUMENT_CURRENCY } })
    fireEvent.click(screen.getByRole('button', { name: 'Portföye al' }))

    await waitFor(() => expect(commands).toHaveLength(1))
    expect(commands[0].method).toBe('receiveCustomerInstrument')
    expect(commands[0].request).toEqual({
      kind: 'Cheque', partyId: PARTY.partyId, companyAccountId: PARTY.companyAccountId, serialNumber: 'SENTETIK-CEK-9',
      drawer: 'SENTETIK Keşideci', bankName: 'SENTETIK Banka', branchName: null, dueOn: '2026-10-15',
      receivedOn: '2026-09-20', money: { amount: '1000.50', currencyId: INSTRUMENT_CURRENCY }, description: null,
    })
    await commands[0].resolve(succeeded(instrumentBody({ serialNumber: 'SENTETIK-CEK-9',
      amount: { disclosure: 'Visible', money: { amount: '1000.50', currencyId: INSTRUMENT_CURRENCY } } })))
    await screen.findByText('Portföye alındı — para etkisi yok.')
    expect(screen.getByRole('link', { name: 'Portföyde aç' }).getAttribute('href'))
      .toBe(`/finans/tahsilatlar?cekSenet=${INSTRUMENT_ID}`)
  })

  it('a note needs no bank', async () => {
    const { port, commands } = recordingPort((method, scope) =>
      method === 'listBankAccounts' ? found(bankPage(scope.companyId)) : NO_REPLY)
    renderFinance(<CustomerInstrumentReceiveForm party={PARTY} />, { port, router: true })
    fireEvent.change(await screen.findByLabelText('Tür'), { target: { value: 'PromissoryNote' } })
    fireEvent.change(screen.getByLabelText('Seri / belge no'), { target: { value: 'SENTETIK-SNT-1' } })
    fireEvent.change(screen.getByLabelText('Keşideci / borçlu'), { target: { value: 'SENTETIK Borçlu' } })
    fireEvent.change(screen.getByLabelText('Vade tarihi'), { target: { value: '2026-11-15' } })
    fireEvent.change(screen.getByLabelText('Alış tarihi'), { target: { value: '2026-09-20' } })
    fireEvent.change(screen.getByLabelText('Nominal tutar'), { target: { value: '250' } })
    await waitFor(() => expect((screen.getByLabelText('Para birimi') as HTMLSelectElement).disabled).toBe(false))
    fireEvent.change(screen.getByLabelText('Para birimi'), { target: { value: INSTRUMENT_CURRENCY } })
    fireEvent.click(screen.getByRole('button', { name: 'Portföye al' }))
    await waitFor(() => expect(commands).toHaveLength(1))
    expect(commands[0].request).toMatchObject({ kind: 'PromissoryNote', bankName: null, money: { amount: '250' } })
  })
})
