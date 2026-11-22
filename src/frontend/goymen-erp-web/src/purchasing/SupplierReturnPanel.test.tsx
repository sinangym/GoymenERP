import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SupplierReturnPanel } from './SupplierReturnPanel'
import type { SupplierReturn } from './supplierReturnContract'
import { SUPPLIER_RETURN_TEXT as T } from './supplierReturnText'

// SAT-D454/SAT-D455: a quantity return shows the decoded supplier price, amount and document pieces as given. SENTETIK, NOT_ENV.
const api = vi.hoisted(() => ({ fetch: vi.fn() }))
vi.mock('./supplierReturnApi', () => ({ fetchSupplierReturns: api.fetch }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: vi.fn() }) }))

const id = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`
const quantityReturn = (overrides: Partial<SupplierReturn> = {}, part: Record<string, unknown> = {}): SupplierReturn => ({
  companyId: id(1), returnId: id(2), returnLineId: id(3), purchaseOrderId: id(4), purchaseOrderLineId: id(5), goodsAcceptanceId: id(6),
  inventoryMovementId: id(7), status: 'Recorded', version: '1', sourceBlock: 'Free', quantityKg: '80', costBasis: 'InvoiceUnitCost',
  unitCostTryPerKg: '20', priceDisclosure: 'Visible', replacementExpected: false, reason: 'SENTETIK', occurredAtUtc: '2026-11-20T10:00:00Z',
  requestedBy: 'SENTETIK-user', requestedAtUtc: '2026-11-20T10:00:00Z', decidedBy: null, decidedAtUtc: null, decisionReason: null,
  sourceLabelId: id(8), lineageMode: 'Quantity',
  lineageParts: [{ goodsAcceptanceId: id(6), purchaseOrderId: id(4), purchaseOrderLineId: id(5), returnLineId: id(3), inventoryMovementId: id(7),
    quantityKg: '80', costBasis: 'InvoiceUnitCost', unitCostTryPerKg: '20', supplierUnitPriceTry: '205', supplierAmountTry: '820',
    supplierPieceQuantity: 4, ...part }],
  ...overrides,
})

async function open(item: SupplierReturn) {
  api.fetch.mockResolvedValue({ type: 'ok-single', data: { items: [item], searchWindowFull: false } })
  render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })}>
    <SupplierReturnPanel fenceKey="SENTETIK" companyId={id(1)} orderId={id(4)} orderVersion="3" />
  </QueryClientProvider>)
  await userEvent.click(screen.getByRole('button', { name: T.show }))
  return within(await screen.findByTestId('purchase-order-supplier-return-1'))
}

afterEach(() => { cleanup(); api.fetch.mockReset() })

describe('SupplierReturnPanel supplier figures', () => {
  it('shows the supplier price, amount and document pieces of a quantity return', async () => {
    const facts = await open(quantityReturn())
    expect(facts.getByRole('columnheader', { name: T.partSupplierPrice })).toBeTruthy()
    expect(facts.getByTestId('supplier-return-part-supplier-price').textContent).toBe('205')
    expect(facts.getByText('820')).toBeTruthy()
    expect(facts.getByTestId('supplier-return-part-supplier-pieces').textContent).toBe('4')
  })

  it('keeps the document pieces and says the price is withheld when the caller may not see prices', async () => {
    const facts = await open(quantityReturn({ priceDisclosure: 'Withheld', unitCostTryPerKg: null },
      { unitCostTryPerKg: null, supplierUnitPriceTry: undefined, supplierAmountTry: undefined }))
    expect(facts.getByTestId('supplier-return-part-supplier-price').textContent).toBe(T.priceWithheld)
    expect(facts.getByTestId('supplier-return-part-supplier-pieces').textContent).toBe('4')
  })

  it('has no supplier columns on a lineage return', async () => {
    const facts = await open(quantityReturn({ lineageMode: 'Lineage' }, { supplierUnitPriceTry: undefined, supplierAmountTry: undefined,
      supplierPieceQuantity: undefined }))
    expect(facts.queryByRole('columnheader', { name: T.partSupplierPrice })).toBeNull()
  })
})
