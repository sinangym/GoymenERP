/**
 * M-04 journals and M-01 queues — component/DOM tests through the real shell and gate, over SENTETIK
 * fixtures. NOT real-API, real-SQL or browser acceptance.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import {
  ACCOUNT_CREDIT,
  ACCOUNT_DEBIT,
  JOURNAL,
  PERIOD_SEPTEMBER,
  READER_CAPABILITIES,
  installFetch,
  journalDetail,
  journalSummary,
  json,
  renderAt,
  requestsFor,
  SOURCE,
} from './testHarness'
import { ACCOUNTING_CAPABILITY } from './capabilities'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('M-04 journals', () => {
  it('lists a period’s journals with the source link and the server’s totals', async () => {
    const recorded = installFetch({
      respond: (url) => (url.includes('/accounting/journals') ? json({ items: [journalSummary()], nextCursor: null }) : undefined),
    })
    renderAt(`/muhasebe/fisler?periodId=${PERIOD_SEPTEMBER}`)
    expect(await screen.findByText('Asıl')).toBeTruthy()
    expect(screen.getAllByText('1.250,5')).toHaveLength(2)
    expect(screen.getByText('synthetic-purpose-a · synthetic-rule-1')).toBeTruthy()
    expect(requestsFor(recorded, `/accounting/journals?periodId=${PERIOD_SEPTEMBER}`, 'GET')).toHaveLength(1)
  })

  // ACC-V0c: the detail is published only once a slice has accepted that route (acc01 opened no journal).
  it('offers the journal link only where the server publishes the detail, and never a dead link', async () => {
    const withDetail = installFetch({
      respond: (url) => (url.includes('/accounting/journals') ? json({ items: [journalSummary()], nextCursor: null }) : undefined),
      capabilities: [ACCOUNTING_CAPABILITY.periodsList, ACCOUNTING_CAPABILITY.journalsList, ACCOUNTING_CAPABILITY.journalsDetail],
    })
    const opened = renderAt(`/muhasebe/fisler?periodId=${PERIOD_SEPTEMBER}`)
    const row = await screen.findByText('Asıl')
    expect(within(row.closest('tr') as HTMLElement).getByRole('link', { name: /Aç/ })).toBeTruthy()
    expect(requestsFor(withDetail, `/accounting/journals?periodId=${PERIOD_SEPTEMBER}`, 'GET')).toHaveLength(1)
    opened.unmount()

    // Without the detail capability the row still SHOWS the journal — by its labelled reference — and offers
    // no link the shell would close onto its neutral not-found block.
    installFetch({
      respond: (url) => (url.includes('/accounting/journals') ? json({ items: [journalSummary()], nextCursor: null }) : undefined),
      capabilities: [ACCOUNTING_CAPABILITY.periodsList, ACCOUNTING_CAPABILITY.journalsList],
    })
    renderAt(`/muhasebe/fisler?periodId=${PERIOD_SEPTEMBER}`)
    const closedRow = (await screen.findByText('Asıl')).closest('tr') as HTMLElement
    expect(within(closedRow).queryByRole('link', { name: /Aç/ })).toBeNull()
    expect(within(closedRow).getByText(/^ref\. …/)).toBeTruthy()
    expect(closedRow.textContent).not.toContain(JOURNAL)
  })

  it('shows one journal read-only: lines, balanced totals, the derived-record note, no command', async () => {
    const recorded = installFetch({
      respond: (url) => (url.endsWith(`/accounting/journals/${JOURNAL}`) ? json(journalDetail()) : undefined),
    })
    renderAt(`/muhasebe/fisler/${JOURNAL}`)
    expect(
      await screen.findByText('Bu fiş bir kaynak kaydının türev muhasebe kaydıdır; ikinci bir para hareketi değildir.'),
    ).toBeTruthy()
    const totals = screen.getByText('Toplam').closest('tr') as HTMLElement
    expect(within(totals).getAllByText('1.250,5')).toHaveLength(2)
    // The shell has its own buttons; the journal screen itself offers none (no voucher command, ACC-V1).
    const page = screen.getByRole('heading', { name: 'Muhasebe fişi' }).closest('section') as HTMLElement
    expect(within(page).queryAllByRole('button')).toHaveLength(0)
    expect(recorded.filter((request) => request.method === 'POST')).toHaveLength(0)
  })

  it('presents account and source ids as labelled unmapped references, never as names (Business 5745076917)', async () => {
    installFetch({
      respond: (url) => (url.endsWith(`/accounting/journals/${JOURNAL}`) ? json(journalDetail()) : undefined),
    })
    renderAt(`/muhasebe/fisler/${JOURNAL}`)
    expect(
      await screen.findByText('Hesap planı henüz tanımlı değil: satırlar hesap adı değil, eşlenmemiş bir hesap referansı taşır.'),
    ).toBeTruthy()
    expect(screen.getByRole('columnheader', { name: 'Hesap referansı' })).toBeTruthy()
    expect(screen.getAllByText(/^Eşlenmemiş hesap/)).toHaveLength(2)
    // The full ids are never the visible text: each is a short, labelled reference with the full id as its title.
    for (const id of [ACCOUNT_DEBIT, ACCOUNT_CREDIT, SOURCE]) {
      expect(screen.queryByText(id)).toBeNull()
      const references = screen.getAllByText(`ref. …${id.slice(-8)}`)
      expect(references.map((reference) => reference.getAttribute('title'))).toContain(id)
    }
  })

  it('closes the journal screens to a period reader without the journal capability', async () => {
    const recorded = installFetch({ capabilities: READER_CAPABILITIES })
    renderAt('/muhasebe/fisler')
    expect((await screen.findAllByText(/bulunamadı/i)).length).toBeGreaterThan(0)
    expect(requestsFor(recorded, '/accounting/journals')).toHaveLength(0)
  })
})

describe('M-01 posting queues', () => {
  it('renders both queues with their notes', async () => {
    installFetch({
      respond: (url) =>
        url.endsWith('/accounting/posting-queues')
          ? json({
              exceptions: [
                {
                  sourceId: 'a4000000-0000-4000-8000-000000000001',
                  postingPurpose: 'synthetic-purpose-a',
                  postingRuleVersion: 'synthetic-rule-1',
                  missingMappingKeys: ['synthetic-credit-key'],
                  firstMessageId: 'm-1',
                },
              ],
              pending: [
                {
                  sourceId: 'a4000000-0000-4000-8000-000000000002',
                  postingPurpose: 'synthetic-purpose-a',
                  postingRuleVersion: 'synthetic-rule-1',
                  linesAwaitingAmount: 2,
                  firstMessageId: 'm-2',
                },
              ],
            })
          : undefined,
    })
    renderAt('/muhasebe/kontrol')
    expect(await screen.findByText('Eşleme bekleyenler')).toBeTruthy()
    expect(screen.getByText('Eksik eşleme hiçbir hesaba tahminle kaydedilmez.')).toBeTruthy()
    expect(screen.getByText('synthetic-credit-key')).toBeTruthy()
    expect(screen.getByText('Tutar bekleyenler')).toBeTruthy()
    expect(screen.getByText('Bilinmeyen tutar sıfır sayılmaz.')).toBeTruthy()
    const pendingQueue = screen.getByText('Tutar bekleyenler').closest('section') as HTMLElement
    expect(within(pendingQueue).getByText('2')).toBeTruthy()
  })
})
