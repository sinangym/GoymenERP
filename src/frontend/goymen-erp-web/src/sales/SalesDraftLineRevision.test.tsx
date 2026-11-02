/**
 * SAT-D167 (EK24) on S-04 (Order 360): "Satır ekle / çıkar / ürün değiştir" is offered only behind the
 * `sales.orders.quantity.write` control, for a Preparing / Rejected / Approved order; it sends the added lines (chosen from the
 * Product owner's list) and the removals (a product change names its replacement) with a required reason under one idempotency
 * key; a recorded revision re-reads the order (Rev.1) and shows the added line's revision and the passive lines. A shipped
 * line's refusal names its row; a stale version re-reads; an unknown outcome retries with the same key; an unreadable product
 * list offers no new line; a fason order asks for the service products only. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_PRICE_WRITE_CAPABILITY, SALES_QUANTITY_WRITE_CAPABILITY } from './capabilities'
import { decodeDraftDetail, type DraftDetail } from './contract'
import { buildLineRevisionRequest, lineRevisionFromDetail, rowOfServerField } from './lineRevision'
import { resetPendingLineRevisions } from './pendingLineRevisions'
import type { UseReferenceOptions } from './referenceSource'
import { ORDER_DETAIL_TEXT } from './salesText'
import { todayText } from '../product/priceLists/contract'
import { ALL_SALES, COMPANY_A, KG, OPTIONS_A, PRODUCT, installFetch, json, renderSalesApp } from './testHarness'

const T = ORDER_DETAIL_TEXT.lineRevision
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE = '66666666-6666-6666-6666-666666666666'
const PRODUCT_2 = '22222222-2222-2222-2222-222222222223'
const WITH_LINES = [...ALL_SALES, SALES_QUANTITY_WRITE_CAPABILITY]

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}
const NOT_YET = { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null }

const OPTIONS = {
  ...OPTIONS_A,
  products: [...OPTIONS_A.products, {
    productId: PRODUCT_2, productVersion: '3', code: 'SRV-1', name: 'Sentetik hizmet', label: 'SRV-1 — Sentetik hizmet',
    orderUnits: OPTIONS_A.products[0].orderUnits,
  }],
}

/** Offers both products to company A and records the item type (and, when given, the day) each read asked for. */
function sourceRecording(asked: string[], days: string[] = []): UseReferenceOptions {
  return (companyId, date, _accounts, _q, itemType) => {
    if (companyId === COMPANY_A) {
      asked.push(itemType ?? 'all')
      days.push(date)
    }
    return companyId === COMPANY_A ? { status: 'ready', options: OPTIONS } : { status: 'unavailable' }
  }
}

function detail(approvalState = 'Approved', version = '4'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft.approvalState = approvalState
  body.draft.version = version
  body.approval = approvalState === 'Approved' ? APPROVED
    : approvalState === 'PendingApproval' ? { ...NOT_YET, submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z' }
      : NOT_YET
  return body
}

/** Rev.1 after the product change of line 1 (replaced by line 2) and one added line (line 3). */
function revised(replacement: string, added: string): Body {
  const body = detail('Preparing', '5')
  body.draft.revision = 'Rev.1'
  body.draft.lineCount = 2
  const line = body.lines[0]
  body.lines = [
    { ...line, lineId: replacement, lineNo: 2, productId: PRODUCT_2, productVersion: '3', productCode: 'SRV-1', productName: 'Sentetik hizmet',
      quantity: '1000', requestedDeliverySlices: null, addedInRevision: 'Rev.1' },
    { ...line, lineId: added, lineNo: 3, productCode: 'PRF-40X40', quantity: '5', requestedDeliveryDate: null, requestedDeliverySlices: null,
      addedInRevision: 'Rev.1' },
  ]
  body.removedLines = [{
    lineId: LINE, lineNo: 1, productId: PRODUCT, productCode: line.productCode, productName: line.productName, quantity: '1000',
    orderUnitCode: 'kg', removedInRevision: 'Rev.1', replacedByLineId: replacement,
  }]
  return body
}

afterEach(() => {
  cleanup()
  resetPendingLineRevisions()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const linePosts = <C extends { readonly url: string; readonly method: string }>(calls: readonly C[]) =>
  calls.filter((call) => call.url.endsWith('/line-revision') && call.method === 'POST')

async function openForm() {
  fireEvent.click(within(await screen.findByTestId('sales-line-revision')).getByRole('button', { name: T.open }))
  return screen.findByTestId('sales-line-revision-form')
}

function choose(scope: HTMLElement, label: string, value: string) {
  fireEvent.change(within(scope).getByLabelText(label), { target: { value } })
}

describe('S-04 line revision (SAT-D167, EK24)', () => {
  it('is not offered without the quantity control and reads no product list while closed', async () => {
    const asked: string[] = []
    installFetch({ capabilities: ALL_SALES, onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`, sourceRecording(asked))
    await screen.findByTestId('sales-draft-revision')
    expect(screen.queryByTestId('sales-line-revision')).toBeNull()
    expect(screen.queryByText(T.open)).toBeNull()
    expect(asked).toEqual([])
  })

  it('changes a product and adds a line under one key, re-reads Rev.1 and shows the added and passive lines', async () => {
    let body: Body | null = null
    const harness = installFetch({
      capabilities: WITH_LINES,
      onDetail: () => json(body ?? detail()),
      onLineRevision: (call) => {
        const sent = JSON.parse(call.body as string) as Body
        body = revised(sent.added[0].lineId, sent.added[1].lineId)
        return json({ detail: body, isReplay: false, originalCorrelationId: 'c-1' })
      },
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`, sourceRecording([]))

    const form = await openForm()
    expect(within(form).getByText(T.approvedWarning)).toBeTruthy()
    expect(within(form).getByText(T.linkedWarning)).toBeTruthy()

    // Nothing changed is not a revision; the same product is not a product change.
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    expect(await within(form).findByText(T.localError(null, 'no-change'))).toBeTruthy()
    const row = within(form).getByTestId('sales-line-revision-row-1')
    choose(row, T.action, 'replace')
    choose(row, T.product, PRODUCT)
    choose(row, T.unit, KG)
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    expect(await within(form).findByText(T.localError('1. EXAMPLE-ONLY — Controlled schema example', 'same-product'))).toBeTruthy()

    choose(row, T.product, PRODUCT_2)
    choose(row, T.unit, KG)
    fireEvent.click(within(form).getByRole('button', { name: T.addLine }))
    const added = within(form).getByTestId('sales-line-revision-added-1')
    choose(added, T.product, PRODUCT)
    choose(added, T.unit, KG)
    choose(added, T.quantity, '5')
    // A reason is required; nothing is sent without one.
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))
    expect(await within(form).findByText(T.localError(null, 'reason-required'))).toBeTruthy()
    expect(linePosts(harness.calls)).toHaveLength(0)

    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'SENTETIK müşteri ürün değiştirdi' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))

    await waitFor(() => expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.1'))
    const [sent] = linePosts(harness.calls)
    expect(sent.companyId).toBe(COMPANY_A)
    expect(sent.idempotencyKey).toMatch(/.+/)
    const payload = JSON.parse(sent.body as string) as Body
    expect(payload).toEqual({
      expectedVersion: '4',
      reason: 'SENTETIK müşteri ürün değiştirdi',
      added: [
        { lineId: expect.any(String), productId: PRODUCT_2, expectedProductVersion: '3', orderUnitId: KG, quantity: '1000', unitPrice: null,
          priceUnitId: null, requestedDeliveryDate: '2026-09-20', note: null, requestedDeliverySlices: null, discountPercent: null },
        { lineId: expect.any(String), productId: PRODUCT, expectedProductVersion: '7', orderUnitId: KG, quantity: '5', unitPrice: null,
          priceUnitId: null, requestedDeliveryDate: null, note: null, requestedDeliverySlices: null, discountPercent: null },
      ],
      removed: [{ lineId: LINE, replacedByLineId: payload.added[0].lineId }],
    })
    expect(await screen.findByText(T.completed)).toBeTruthy()
    expect((await screen.findByTestId('sales-line-2-added-in')).textContent).toBe(ORDER_DETAIL_TEXT.addedInRevision('Rev.1'))
    const passive = screen.getByTestId('sales-removed-line-1')
    expect(passive.textContent).toContain(ORDER_DETAIL_TEXT.removedLines.removedIn('Rev.1'))
    expect(passive.textContent).toContain(ORDER_DETAIL_TEXT.removedLines.replacedBy(2))
    expect(screen.queryByTestId('sales-line-1')).toBeNull()
  })

  it('names the row a shipped line refusal is about and changes nothing', async () => {
    const harness = installFetch({
      capabilities: WITH_LINES,
      onDetail: () => json(detail()),
      onLineRevision: () => json({
        status: 422, title: 'Unprocessable', code: 'validation_failed', correlationId: 'c-2',
        errors: [{ field: 'removed[0].lineId', code: 'sales_line_revision_line_shipped' }],
      }, 422),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`, sourceRecording([]))
    const form = await openForm()
    choose(within(form).getByTestId('sales-line-revision-row-1'), T.action, 'remove')
    fireEvent.click(within(form).getByRole('button', { name: T.addLine }))
    const added = within(form).getByTestId('sales-line-revision-added-1')
    choose(added, T.product, PRODUCT_2)
    choose(added, T.unit, KG)
    choose(added, T.quantity, '2')
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'neden' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))

    const alert = await within(form).findByRole('alert')
    expect(alert.textContent).toContain(T.validation)
    expect(alert.textContent).toContain(T.rowError('1. EXAMPLE-ONLY — Controlled schema example', T.ruleCode.sales_line_revision_line_shipped))
    expect(linePosts(harness.calls)).toHaveLength(1)
    expect(JSON.parse(linePosts(harness.calls)[0].body as string).removed).toEqual([{ lineId: LINE, replacedByLineId: null }])
    expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.0')
  })

  it('re-reads the order on a stale version and retries an unknown outcome with the same key', async () => {
    let answer: 'conflict' | 'unavailable' = 'unavailable'
    const harness = installFetch({
      capabilities: WITH_LINES,
      onDetail: () => json(detail()),
      onLineRevision: () => answer === 'unavailable'
        ? json({ status: 503, title: 'Unavailable', code: 'revision_impact_unavailable', correlationId: 'c-3' }, 503)
        : json({ status: 409, title: 'Conflict', code: 'version_conflict', correlationId: 'c-4' }, 409),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`, sourceRecording([]))
    const form = await openForm()
    fireEvent.click(within(form).getByRole('button', { name: T.addLine }))
    const added = within(form).getByTestId('sales-line-revision-added-1')
    choose(added, T.product, PRODUCT_2)
    choose(added, T.unit, KG)
    choose(added, T.quantity, '2')
    fireEvent.change(within(form).getByLabelText(T.reason), { target: { value: 'neden' } })
    fireEvent.click(within(form).getByRole('button', { name: T.submit }))

    expect(await within(form).findByText(`${T.unavailable} ${T.unknown}`)).toBeTruthy()
    const detailReads = () => harness.calls.filter((call) => call.method === 'GET' && call.url.endsWith(`/sales/drafts/${DRAFT}`)).length
    const readsBefore = detailReads()
    answer = 'conflict'
    fireEvent.click(within(form).getByRole('button', { name: T.retry }))

    const alert = await within(form).findByRole('alert')
    expect(alert.textContent).toContain(T.conflict)
    expect(alert.textContent).toContain(T.versionConflict)
    const [first, second] = linePosts(harness.calls)
    expect(second.idempotencyKey).toBe(first.idempotencyKey)
    expect(second.body).toBe(first.body)
    await waitFor(() => expect(detailReads()).toBeGreaterThan(readsBefore))
  })

  it('offers no new line while the product list cannot be read, and is closed while the order waits for a decision', async () => {
    const unreadable: UseReferenceOptions = (companyId) => companyId === COMPANY_A
      ? { status: 'unavailable', missing: [{ source: 'products', reason: 'http-503' }],
        header: { accounts: OPTIONS_A.accounts, accountsOffer: { items: OPTIONS_A.accounts, nextCursor: null, loadingMore: false, moreFailed: null, loadMore: () => {} },
          currencies: { status: 'idle' }, products: { status: 'unavailable', reason: 'http-503', retry: () => {} } } }
      : { status: 'unavailable' }
    installFetch({ capabilities: WITH_LINES, onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`, unreadable)
    const form = await openForm()
    expect(within(form).getByTestId('sales-line-revision-products-unavailable').textContent).toContain(T.productsUnavailable)
    expect((within(form).getByRole('button', { name: T.addLine }) as HTMLButtonElement).disabled).toBe(true)
    expect(within(form).getByText(T.noAdded)).toBeTruthy()
    cleanup()

    installFetch({ capabilities: WITH_LINES, onDetail: () => json(detail('PendingApproval', '3')) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`, sourceRecording([]))
    const entry = await screen.findByTestId('sales-line-revision')
    expect((within(entry).getByRole('button', { name: T.open }) as HTMLButtonElement).disabled).toBe(true)
    expect(within(entry).getByText(T.stateUnavailable)).toBeTruthy()
  })

  it('lists the products of the revision day, not of the order business date (SAT-D212)', async () => {
    const days: string[] = []
    installFetch({ capabilities: WITH_LINES, onDetail: () => json(detail()) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`, sourceRecording([], days))
    const form = await openForm()
    await waitFor(() => expect(days.length).toBeGreaterThan(0))
    expect(new Set(days)).toEqual(new Set([todayText()]))
    expect(days).not.toContain('2026-09-13')
    expect(within(form).getByTestId('sales-line-revision-day').textContent).toBe(T.revisionDay(todayText()))
  })

  it('asks a fason order for the service products only and says its rules; a price needs the price-write control', async () => {
    const asked: string[] = []
    const fason = detail('Preparing', '2')
    fason.draft.subcontractService = true
    installFetch({ capabilities: [...WITH_LINES, SALES_PRICE_WRITE_CAPABILITY], onDetail: () => json(fason) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`, sourceRecording(asked))
    const form = await openForm()
    expect(within(form).getByTestId('sales-line-revision-subcontract').textContent).toBe(T.subcontractNote)
    await waitFor(() => expect(asked).toContain('Service'))
    expect(asked).not.toContain('all')
    fireEvent.click(within(form).getByRole('button', { name: T.addLine }))
    expect(within(within(form).getByTestId('sales-line-revision-added-1')).getByLabelText(T.unitPrice)).toBeTruthy()
  })
})

describe('line revision request and contract', () => {
  const base = decodeDraftDetail({ ...JSON.parse(detailResponseRaw) }, null) as DraftDetail
  let n = 0
  const mint = () => `00000000-0000-4000-8000-00000000000${++n}`

  it('builds removals, replacements and added lines; a price is read only with the price-write control', () => {
    n = 0
    const values = lineRevisionFromDetail(base, mint)
    const changed = {
      ...values,
      reason: 'neden',
      existing: [{ ...values.existing[0], action: 'replace' as const,
        replacement: { ...values.existing[0].replacement, productId: PRODUCT_2, orderUnitId: KG, unitPriceText: '12,5', priceUnitId: KG } }],
    }
    const priced = buildLineRevisionRequest('9', changed, OPTIONS.products, true)
    expect(priced.ok && priced.payload.added[0]).toMatchObject({ unitPrice: '12.5', priceUnitId: KG, quantity: '1000' })
    expect(priced.ok && priced.payload.removed).toEqual([{ lineId: LINE, replacedByLineId: '00000000-0000-4000-8000-000000000001' }])
    const unpriced = buildLineRevisionRequest('9', changed, OPTIONS.products, false)
    expect(unpriced.ok && unpriced.payload.added[0]).toMatchObject({ unitPrice: null, priceUnitId: null })

    const removedOnly = { ...values, reason: 'neden', existing: [{ ...values.existing[0], action: 'remove' as const }] }
    expect(buildLineRevisionRequest('9', removedOnly, OPTIONS.products, true)).toEqual({ ok: false, problem: 'no-lines-left', row: null })
    const noUnit = { ...values, reason: 'neden', added: [{ lineId: mint(), productId: PRODUCT, orderUnitId: '', quantityText: '1', unitPriceText: '', priceUnitId: '' }] }
    expect(buildLineRevisionRequest('9', noUnit, OPTIONS.products, true)).toEqual({ ok: false, problem: 'unit-required', row: '+1' })
    const ok = buildLineRevisionRequest('9', { ...noUnit, added: [{ ...noUnit.added[0], orderUnitId: KG }] }, OPTIONS.products, true)
    expect(ok.ok).toBe(true)
    expect(ok.ok ? rowOfServerField('added[0].productId', ok.payload, { ...noUnit, added: [{ ...noUnit.added[0], orderUnitId: KG }] }) : null).toBe('+1')
  })

  it('decodes the added revision and the passive lines, and refuses a passive line that is also active or replaces itself', () => {
    const raw = revised('00000000-0000-4000-8000-0000000000a1', '00000000-0000-4000-8000-0000000000a2')
    const decoded = decodeDraftDetail(raw, null)
    expect(decoded?.lines.map((line) => line.addedInRevision)).toEqual(['Rev.1', 'Rev.1'])
    expect(decoded?.removedLines?.[0]).toMatchObject({ lineId: LINE, lineNo: 1, removedInRevision: 'Rev.1' })
    expect(decodeDraftDetail({ ...raw, removedLines: [] }, null)).toBeNull()
    expect(decodeDraftDetail({ ...raw, removedLines: [{ ...raw.removedLines[0], lineId: raw.lines[0].lineId }] }, null)).toBeNull()
    expect(decodeDraftDetail({ ...raw, removedLines: [{ ...raw.removedLines[0], replacedByLineId: LINE }] }, null)).toBeNull()
    expect(decodeDraftDetail({ ...raw, removedLines: [{ ...raw.removedLines[0], lineNo: 2 }] }, null)).toBeNull()
    expect(decodeDraftDetail({ ...raw, lines: [{ ...raw.lines[0], addedInRevision: 'R1' }, raw.lines[1]] }, null)).toBeNull()
    // A detail without either member still decodes exactly as before.
    expect(base.removedLines).toBeUndefined()
    expect(base.lines[0].addedInRevision).toBeUndefined()
  })
})
