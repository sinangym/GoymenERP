import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { PaintSelectionEditor } from './PaintSelectionEditor'
import { PAINT_SELECTION_TEXT as T } from './paintSelectionText'
const boundary = vi.hoisted(() => ({ capture: () => ({}), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
const company = '10000000-0000-0000-0000-000000000001'
const product = { productId: '20000000-0000-0000-0000-000000000002', productVersion: '12', membershipVersion: '3', sourceVersion: 'source:3', code: 'PNT', name: 'Boya' }
const supplier = { partyId: '30000000-0000-0000-0000-000000000003', companyAccountId: '40000000-0000-0000-0000-000000000004', partyVersion: '4', accountVersion: '5', code: 'TED', name: 'Tedarikçi' }
const catalog = { companyId: company, products: [product], suppliers: [supplier], colors: [], supplierSearchWindowFull: false }
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.clearAllMocks() })
describe('paint selection editor', () => {
  it('rejects an unexpected successful status even with a valid catalog body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(catalog), { status: 201 })))
    render(<PaintSelectionEditor companyId={company} value={null} disabled={false} onChange={vi.fn()} />)
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.unavailable)
    expect(screen.queryByRole('combobox')).toBeNull()
  })
  it('selects neither the first product nor supplier automatically and carries their actual versions after explicit choices', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(catalog))))
    const change = vi.fn()
    render(<PaintSelectionEditor companyId={company} value={null} disabled={false} onChange={change} />)
    const products = await screen.findByRole('combobox', { name: T.product })
    expect((products as HTMLSelectElement).value).toBe('')
    expect((screen.getByRole('combobox', { name: T.supplier }) as HTMLSelectElement).value).toBe('')
    expect(change).not.toHaveBeenCalled()
    fireEvent.change(products, { target: { value: product.productId } })
    fireEvent.change(screen.getByRole('combobox', { name: T.supplier }), { target: { value: supplier.companyAccountId } })
    expect(change).toHaveBeenLastCalledWith(expect.objectContaining({ paintSelection: {
      productId: product.productId, productVersion: '12', membershipVersion: '3', productSourceVersion: 'source:3',
      supplierPartyId: supplier.partyId, supplierCompanyAccountId: supplier.companyAccountId,
      supplierPartyVersion: '4', supplierAccountVersion: '5', colorId: null, colorVersion: null,
    } }))
  })
  it('keeps failures distinct from an empty catalog and does not expose options after a company change', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(catalog))).mockImplementationOnce(() => new Promise(() => {})))
    const mounted = render(<PaintSelectionEditor companyId={company} value={null} disabled={false} onChange={vi.fn()} />)
    await screen.findByRole('combobox', { name: T.product })
    mounted.rerender(<PaintSelectionEditor companyId={product.productId} value={null} disabled={false} onChange={vi.fn()} />)
    expect(screen.queryByRole('combobox', { name: T.product })).toBeNull()
    expect(screen.getByRole('status').textContent).toBe(T.loading)
  })
  it('shows unavailable and unauthorized sources without substituting an empty successful selection', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response('{}', { status: 503 })).mockResolvedValueOnce(new Response('{}', { status: 403 })))
    render(<PaintSelectionEditor companyId={company} value={null} disabled={false} onChange={vi.fn()} />)
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.unavailable)
    expect(screen.queryByRole('combobox')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(T.denied))
    expect(boundary.report).toHaveBeenCalledWith(403, expect.anything())
  })
})
