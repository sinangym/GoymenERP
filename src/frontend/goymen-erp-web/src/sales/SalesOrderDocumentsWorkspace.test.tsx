/**
 * UI-71 — S-11 Satış İletişimi ve Sözleşmeler in the S-04 "Sözleşme / Belgeler" tab. Component evidence (NOT_ENV): the
 * Document Vault answers are the harness's. Proves: the order's and (only under the Cari card's capability) the customer's
 * documents are read once each, merged into one newest-first timeline with a relation filter, and the loading, empty,
 * read-failed (with retry), hidden-owner and forbidden states are explicit.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { OWNER_KIND, type DocumentRow } from '../documents/contract'
import { CAPABILITY } from '../shell/capabilities'
import { mergeTimeline } from './documentTimeline'
import { SALES_DOCUMENTS_TEXT as TEXT } from './salesText'
import { DOCUMENTS_TEXT } from '../documents/documentsText'
import { ALL_SALES, COMPANY_A, HARNESS_PARTY_DISPLAY, installFetch, json, renderSalesApp } from './testHarness'

const RAW = JSON.parse(detailResponseRaw)
const DRAFT: string = RAW.draft.id
const PARTY: string = RAW.draft.partyId
const DOCUMENT_NO: string = RAW.draft.documentNo
const PATH = `/satis/siparisler/${DRAFT}?sekme=belgeler`
const WITH_CARI = [...ALL_SALES, CAPABILITY.cariPartiesDetail]
const LIMITS = {
  maximumBytes: 20_971_520,
  extensions: ['.pdf', '.png', '.jpg', '.jpeg'],
  contentTypes: ['application/pdf', 'image/png', 'image/jpeg'],
}

function row(ownerKind: string, ownerId: string, over: Partial<DocumentRow> = {}): DocumentRow {
  return {
    id: 'd0000000-0000-4000-8000-000000000001',
    companyId: COMPANY_A,
    ownerKind,
    ownerId,
    versionNo: 1,
    fileName: 'Belge.pdf',
    contentType: 'application/pdf',
    sizeBytes: 2048,
    sha256: 'a'.repeat(64),
    scanState: 'Clean',
    downloadable: true,
    uploadedBy: 'company-a-user',
    uploadedAtUtc: '2026-09-30T09:00:00+00:00',
    ...over,
  }
}

const orderDoc = (over: Partial<DocumentRow> = {}) => row(OWNER_KIND.salesOrder, DRAFT, over)
const customerDoc = (over: Partial<DocumentRow> = {}) => row(OWNER_KIND.counterpartyParty, PARTY, over)
const list = (items: readonly DocumentRow[], canUpload = false) => json({ items, canUpload, limits: LIMITS })

const ownerOf = (url: string) => new URLSearchParams(url.slice(url.indexOf('?') + 1)).get('ownerKind')

function documentsBy(handlers: Partial<Record<string, () => Response | Promise<Response>>>) {
  return (url: string) => {
    const handler = handlers[ownerOf(url) ?? '']
    if (handler === undefined) throw new Error(`unexpected documents read ${url}`)
    return handler()
  }
}

const documentReads = (calls: readonly { readonly url: string; readonly method: string }[]) =>
  calls.filter((call) => call.method === 'GET' && call.url.includes('/documents?'))

async function timeline() {
  const heading = await screen.findByRole('heading', { name: TEXT.timelineHeading })
  return heading.closest('section') as HTMLElement
}

afterEach(() => {
  cleanup()
  try {
    window.localStorage.clear()
  } catch {
    // no storage here
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('S-11 timeline order', () => {
  it('puts the newest first across both owners, and breaks equal instants by file name, then id', () => {
    const rows = mergeTimeline(
      [
        orderDoc({ id: 'o1', fileName: 'Sipariş onayı.pdf', uploadedAtUtc: '2026-09-28T10:00:00Z' }),
        orderDoc({ id: 'o2', fileName: 'B.pdf', uploadedAtUtc: '2026-10-01T08:00:00Z' }),
      ],
      [
        customerDoc({ id: 'c1', fileName: 'Çerçeve sözleşme.pdf', uploadedAtUtc: '2026-09-30T09:00:00Z' }),
        customerDoc({ id: 'c2', fileName: 'A.pdf', uploadedAtUtc: '2026-10-01T08:00:00Z' }),
      ],
    )
    expect(rows.map((r) => `${r.relation}:${r.document.id}`)).toEqual(['customer:c2', 'order:o2', 'customer:c1', 'order:o1'])
  })
})

describe('S-11 in the "Sözleşme / Belgeler" tab', () => {
  it('reads the order and, with the Cari card capability, the customer once each, and merges them newest first', async () => {
    const harness = installFetch({
      capabilities: WITH_CARI,
      onDetail: () => json(RAW),
      onDocuments: documentsBy({
        [OWNER_KIND.salesOrder]: () =>
          list([orderDoc({ id: 'd0000000-0000-4000-8000-0000000000a1', fileName: 'Müşteri sipariş onayı.pdf', uploadedAtUtc: '2026-09-28T10:00:00Z' })]),
        [OWNER_KIND.counterpartyParty]: () =>
          list([customerDoc({ id: 'd0000000-0000-4000-8000-0000000000b1', fileName: 'Çerçeve sözleşme.pdf', uploadedAtUtc: '2026-10-01T08:00:00Z', versionNo: 2 })]),
      }),
    })
    renderSalesApp(PATH)

    const section = await timeline()
    const table = await within(section).findByRole('table', { name: TEXT.timelineCaption })
    const bodyRows = within(table).getAllByRole('row').slice(1)
    expect(bodyRows.map((r) => r.getAttribute('data-timeline-relation'))).toEqual(['customer', 'order'])
    expect(within(bodyRows[0]).getByRole('rowheader').textContent).toBe('Çerçeve sözleşme.pdf')
    expect(bodyRows[0].textContent).toContain(TEXT.relationCustomer(`${HARNESS_PARTY_DISPLAY.code} — ${HARNESS_PARTY_DISPLAY.legalName} (${HARNESS_PARTY_DISPLAY.tradeName})`))
    expect(bodyRows[0].textContent).toContain('Sürüm 2')
    expect(bodyRows[1].textContent).toContain(TEXT.relationOrder(DOCUMENT_NO))

    // Both owners' own panels, each under its own heading; the timeline shared their reads (one GET per owner).
    expect(await screen.findByRole('heading', { name: TEXT.orderPanelHeading })).toBeTruthy()
    expect(await screen.findByRole('heading', { name: TEXT.customerPanelHeading })).toBeTruthy()
    const reads = documentReads(harness.calls)
    expect(reads.map((call) => ownerOf(call.url)).sort()).toEqual([OWNER_KIND.counterpartyParty, OWNER_KIND.salesOrder].sort())
    const customerRead = reads.find((call) => ownerOf(call.url) === OWNER_KIND.counterpartyParty)!
    expect(new URLSearchParams(customerRead.url.slice(customerRead.url.indexOf('?') + 1)).get('ownerId')).toBe(PARTY)
    expect(customerRead).toMatchObject({ companyId: COMPANY_A })
  })

  it('marks a scanned file the vault still withholds the same way the panel does (downloadable decides, not the scan state)', async () => {
    installFetch({
      capabilities: WITH_CARI,
      onDetail: () => json(RAW),
      onDocuments: documentsBy({
        [OWNER_KIND.salesOrder]: () =>
          list([orderDoc({ id: 'd0000000-0000-4000-8000-0000000000a1', fileName: 'Bekleyen.pdf', scanState: 'Clean', downloadable: false })]),
        [OWNER_KIND.counterpartyParty]: () => list([]),
      }),
    })
    renderSalesApp(PATH)

    const section = await timeline()
    const table = await within(section).findByRole('table', { name: TEXT.timelineCaption })
    const [timelineRow] = within(table).getAllByRole('row').slice(1)
    expect(timelineRow.textContent).toContain(DOCUMENTS_TEXT.scanPending)
    expect(timelineRow.textContent).not.toContain(DOCUMENTS_TEXT.scanClean)
  })

  it('filters the timeline by the bound record with pressed-state buttons', async () => {
    installFetch({
      capabilities: WITH_CARI,
      onDetail: () => json(RAW),
      onDocuments: documentsBy({
        [OWNER_KIND.salesOrder]: () => list([orderDoc({ id: 'd0000000-0000-4000-8000-0000000000a1', fileName: 'Sipariş.pdf' })]),
        [OWNER_KIND.counterpartyParty]: () =>
          list([
            customerDoc({ id: 'd0000000-0000-4000-8000-0000000000b1', fileName: 'Sözleşme.pdf' }),
            customerDoc({ id: 'd0000000-0000-4000-8000-0000000000b2', fileName: 'Ek protokol.pdf' }),
          ]),
      }),
    })
    renderSalesApp(PATH)
    const section = await timeline()
    const group = await within(section).findByRole('group', { name: TEXT.filterLabel })
    const all = within(group).getByRole('button', { name: TEXT.filter.all(3) })
    const order = within(group).getByRole('button', { name: TEXT.filter.order(1) })
    const customer = within(group).getByRole('button', { name: TEXT.filter.customer(2) })
    expect(all.getAttribute('aria-pressed')).toBe('true')

    fireEvent.click(customer)
    expect(customer.getAttribute('aria-pressed')).toBe('true')
    expect(all.getAttribute('aria-pressed')).toBe('false')
    const rows = () => within(within(section).getByRole('table')).getAllByRole('row').slice(1)
    expect(rows().map((r) => r.getAttribute('data-timeline-relation'))).toEqual(['customer', 'customer'])

    fireEvent.click(order)
    expect(rows().map((r) => within(r).getByRole('rowheader').textContent)).toEqual(['Sipariş.pdf'])
  })

  it('without the Cari card capability asks only for the order, and says the customer documents are not shown', async () => {
    const harness = installFetch({
      capabilities: ALL_SALES,
      onDetail: () => json(RAW),
      onDocuments: documentsBy({ [OWNER_KIND.salesOrder]: () => list([orderDoc()]) }),
    })
    renderSalesApp(PATH)
    const section = await timeline()
    expect(await within(section).findByText(TEXT.customerHidden)).toBeTruthy()
    expect(within(section).queryByRole('button', { name: /Müşteri kartı/ })).toBeNull()
    expect(screen.queryByRole('heading', { name: TEXT.customerPanelHeading })).toBeNull()
    expect(documentReads(harness.calls).map((call) => ownerOf(call.url))).toEqual([OWNER_KIND.salesOrder])
  })

  it('shows a loading status until the vault answers', async () => {
    let release: (response: Response) => void = () => {}
    installFetch({
      capabilities: ALL_SALES,
      onDetail: () => json(RAW),
      onDocuments: () => new Promise<Response>((resolve) => { release = resolve }),
    })
    renderSalesApp(PATH)
    const section = await timeline()
    expect(within(section).getByRole('status').textContent).toBe(TEXT.loading)
    release(list([]))
    expect(await within(section).findByText(TEXT.empty)).toBeTruthy()
    expect(within(section).queryByRole('status')).toBeNull()
  })

  it('says so when neither the order nor the customer has a document yet', async () => {
    installFetch({
      capabilities: WITH_CARI,
      onDetail: () => json(RAW),
      onDocuments: documentsBy({ [OWNER_KIND.salesOrder]: () => list([]), [OWNER_KIND.counterpartyParty]: () => list([]) }),
    })
    renderSalesApp(PATH)
    const section = await timeline()
    expect(await within(section).findByText(TEXT.empty)).toBeTruthy()
    expect(within(section).queryByRole('table')).toBeNull()
  })

  it('a refused read of every owner is the forbidden state, and no panel is drawn', async () => {
    installFetch({
      capabilities: WITH_CARI,
      onDetail: () => json(RAW),
      onDocuments: documentsBy({
        [OWNER_KIND.salesOrder]: () => json({ code: 'forbidden' }, 403),
        [OWNER_KIND.counterpartyParty]: () => json({ code: 'not_found' }, 404),
      }),
    })
    renderSalesApp(PATH)
    const section = await timeline()
    expect(await within(section).findByText(TEXT.forbidden)).toBeTruthy()
    expect(screen.queryByRole('heading', { name: TEXT.orderPanelHeading })).toBeNull()
    expect(screen.queryByRole('heading', { name: TEXT.customerPanelHeading })).toBeNull()
  })

  it('keeps the order documents when only the customer read is refused, and names the hidden owner', async () => {
    installFetch({
      capabilities: WITH_CARI,
      onDetail: () => json(RAW),
      onDocuments: documentsBy({
        [OWNER_KIND.salesOrder]: () => list([orderDoc({ fileName: 'Sipariş.pdf' })]),
        [OWNER_KIND.counterpartyParty]: () => json({ code: 'not_found' }, 404),
      }),
    })
    renderSalesApp(PATH)
    const section = await timeline()
    expect(await within(section).findByText(TEXT.customerHidden)).toBeTruthy()
    expect(within(section).getByRole('rowheader').textContent).toBe('Sipariş.pdf')
    expect(screen.queryByRole('heading', { name: TEXT.customerPanelHeading })).toBeNull()
  })

  it('a failed read is an alert with a retry that asks only the failed owner again', async () => {
    let customerAnswers = 0
    const harness = installFetch({
      capabilities: WITH_CARI,
      onDetail: () => json(RAW),
      onDocuments: documentsBy({
        [OWNER_KIND.salesOrder]: () => list([orderDoc({ fileName: 'Sipariş.pdf' })]),
        [OWNER_KIND.counterpartyParty]: () => {
          customerAnswers += 1
          return customerAnswers === 1 ? json({}, 500) : list([customerDoc({ id: 'd0000000-0000-4000-8000-0000000000b1', fileName: 'Sözleşme.pdf' })])
        },
      }),
    })
    renderSalesApp(PATH)
    const section = await timeline()
    const alert = await within(section).findByRole('alert')
    expect(alert.textContent).toContain(TEXT.readFailed)
    // What was read stays visible beside the alert.
    expect(within(section).getByRole('rowheader').textContent).toBe('Sipariş.pdf')
    const orderReadsBefore = documentReads(harness.calls).filter((call) => ownerOf(call.url) === OWNER_KIND.salesOrder).length

    fireEvent.click(within(alert).getByRole('button', { name: TEXT.readAgain }))

    await waitFor(() => expect(within(section).queryByRole('alert')).toBeNull())
    expect(within(section).getAllByRole('rowheader').map((cell) => cell.textContent).sort()).toEqual(['Sipariş.pdf', 'Sözleşme.pdf'])
    expect(documentReads(harness.calls).filter((call) => ownerOf(call.url) === OWNER_KIND.salesOrder)).toHaveLength(orderReadsBefore)
  })
})
