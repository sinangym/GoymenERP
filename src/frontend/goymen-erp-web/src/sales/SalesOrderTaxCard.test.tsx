/**
 * MOD-03 → Sales on S-04: the "Vergi" card shows the order's buyer tax situation and every line's tax snapshot verbatim — the
 * Settings default or the reasoned line change with who and why, the VAT and withholding as the server resolved them, the codes
 * and what makes the invoice wait — and a tax the Settings values do not give yet as "Hesaplanamıyor" with its reason, never a
 * guessed rate. A caller whose prices are withheld (SAT-D150) is not asked. Loading, 503 and malformed answers show no rate.
 * Component evidence, NOT_ENV: the API answers here are the harness's; every value is SENTETIK.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { ORDER_TAX_TEXT as T, taxReasonText } from './salesText'
import { COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE_1 = 'aaaaaaaa-1111-4111-8111-000000000001'
const LINE_2 = 'aaaaaaaa-1111-4111-8111-000000000002'
const UNIT = 'bbbbbbbb-1111-4111-8111-000000000002'
const PATH = `/satis/siparisler/${DRAFT}`

type Body = Record<string, any>

function detail(disclosure: 'Visible' | 'Withheld' = 'Visible'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, companyId: COMPANY_A, version: '4', lineCount: 2 }
  const base = { ...body.lines[0], orderUnitId: UNIT, orderUnitCode: 'KG', requestedDeliverySlices: [], requestedDeliveryDate: null }
  const priced = disclosure === 'Visible'
  body.lines = [
    { ...base, lineId: LINE_1, lineNo: 1, quantity: '10', unitPrice: priced ? '100' : null, priceUnitId: priced ? UNIT : null,
      discountPercent: null, priceDisclosure: disclosure },
    { ...base, lineId: LINE_2, lineNo: 2, productCode: 'URUN-2', productName: 'İkinci ürün', quantity: '2', priceDisclosure: disclosure },
  ]
  return body
}

function tax(): Body {
  const d = detail()
  return {
    draftVersion: d.draft.version,
    revision: d.draft.revision,
    businessDate: '2026-11-02',
    buyerTax: { situation: 'ExportRegistered', documentNo: 'IK-1', documentDate: '2026-11-01' },
    lines: [
      {
        lineId: LINE_1, lineNo: 1, hasSnapshot: true, origin: 'Override', resolvedOn: '2026-11-02', buyerSituation: 'ExportRegistered',
        itemType: 'Steel', state: 'Resolved', reason: null, vatState: 'ZeroRated', vatPercent: '0', exemptionCodeState: 'Unresolved',
        exemptionCode: null, exemptionCodeReason: 'exemption_code_missing', withholdingState: 'NotApplied', withholdingPercent: null,
        withholdingCodeState: 'NotRequired', withholdingCode: null, withholdingCodeReason: null,
        buyerAdjustment: 'buyer_zero_rated_with_exemption_code', buyerDocumentRequired: true, settingsSource: 'Record',
        settingsRecordId: 'dddddddd-1111-4111-8111-000000000009', settingsRecordVersion: 1, buyerSituationListVersion: 0,
        rateState: 'Computed', unavailableReason: null, invoiceBlockers: ['exemption_code_missing'],
        override: { itemType: null, buyerSituation: 'ExportRegistered', exemptionCode: null, reason: 'SENTETIK ihraç kayıtlı',
          changedBy: 'synthetic-actor', changedAtUtc: '2026-11-02T09:00:00Z' },
      },
      {
        lineId: LINE_2, lineNo: 2, hasSnapshot: true, origin: 'Default', resolvedOn: '2026-11-02', buyerSituation: 'ExportRegistered',
        itemType: null, state: 'Unresolved', reason: 'item_type_default_missing', vatState: null, vatPercent: null,
        exemptionCodeState: null, exemptionCode: null, exemptionCodeReason: null, withholdingState: null, withholdingPercent: null,
        withholdingCodeState: null, withholdingCode: null, withholdingCodeReason: null, buyerAdjustment: null,
        buyerDocumentRequired: false, settingsSource: null, settingsRecordId: null, settingsRecordVersion: null,
        buyerSituationListVersion: 0, rateState: 'NoSource', unavailableReason: 'item_type_default_missing',
        invoiceBlockers: ['item_type_default_missing'], override: null,
      },
    ],
  }
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const taxCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.endsWith('/tax'))
const card = () => screen.findByRole('region', { name: T.heading })

describe('S-04 order tax card (MOD-03 → Sales)', () => {
  it('shows the buyer tax and each line’s snapshot, the change’s reason and what the invoice waits for, through one read-only GET', async () => {
    const harness = installFetch({ onDetail: () => json(detail()), onTax: () => json(tax()) })
    renderSalesApp(PATH)

    const section = await card()
    await within(section).findByRole('table', { name: T.linesCaption })
    expect(within(section).getByTestId('sales-tax-buyer-situation').textContent).toBe('ExportRegistered')
    expect(within(section).getByTestId('sales-tax-buyer-document').textContent).toBe(T.documentValue('IK-1', '2026-11-01'))
    expect(within(section).getByTestId('sales-tax-line-1-vat').textContent).toBe('KDV %0')
    expect(within(section).getByTestId('sales-tax-line-1-withholding').textContent).toBe('Uygulanmaz')
    expect(within(section).getByTestId('sales-tax-line-1-override').textContent).toContain('SENTETIK ihraç kayıtlı')
    expect(within(section).getByTestId('sales-tax-line-1-invoice').textContent).toBe(taxReasonText('exemption_code_missing'))
    // SAT-D148: a value not entered in Settings is named, never a guessed rate.
    expect(within(section).getByTestId('sales-tax-line-2-vat').textContent).toBe('Hesaplanamıyor')
    expect(within(section).getByTestId('sales-tax-line-2-state').textContent).toContain(T.rateNoSource)
    expect(within(section).getByTestId('sales-tax-line-2-reason').textContent).toContain(taxReasonText('item_type_default_missing'))

    const reads = taxCalls(harness.calls)
    expect(reads).toHaveLength(1)
    expect(reads[0]).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
  })

  it('does not ask when the detail withholds prices (SAT-D150), and says why nothing is shown', async () => {
    const harness = installFetch({ onDetail: () => json(detail('Withheld')) })
    renderSalesApp(PATH)

    const section = await card()
    expect(within(section).getByTestId('sales-tax-forbidden').textContent).toBe(T.forbidden)
    await waitFor(() => expect(taxCalls(harness.calls)).toHaveLength(0))
  })

  it('reads the legacy answer (no snapshot) as "kaynak yok" per line', async () => {
    installFetch({ onDetail: () => json(detail()) })
    renderSalesApp(PATH)

    const section = await card()
    await within(section).findByRole('table', { name: T.linesCaption })
    expect(within(section).getByTestId('sales-tax-buyer-situation').textContent).toBe(T.buyerSituationMissing)
    expect(within(section).getByTestId('sales-tax-line-1-reason').textContent).toContain(taxReasonText('tax_snapshot_missing'))
  })

  it('on a 503 shows no rate and names the outage; a malformed answer shows nothing either', async () => {
    installFetch({ onDetail: () => json(detail()), onTax: () => json({ status: 503, title: 'Service Unavailable', code: 'sales_tax_provider_unavailable' }, 503) })
    renderSalesApp(PATH)
    const section = await card()
    expect((await within(section).findByTestId('sales-tax-unreadable')).textContent).toBe(T.serviceUnavailable)
    expect(within(section).queryByRole('table')).toBeNull()
    cleanup()

    installFetch({ onDetail: () => json(detail()), onTax: () => json({ ...tax(), extra: 1 }) })
    renderSalesApp(PATH)
    const malformed = await card()
    expect((await within(malformed).findByTestId('sales-tax-unreadable')).textContent).toBe(T.unreadable)
    expect(within(malformed).queryByRole('table')).toBeNull()
  })
})
