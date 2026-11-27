import { useEffect, useRef } from 'react'
import type { CounterpartyMutationResult } from '../cari/mutationContract'
import { PARTY_ROLE_FLAG } from '../cari/contract'
/** Synthetic UI sources; real HTTP/SQL/browser runtime acceptance NOT_RUN. */
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { initialShellState, type ShellState } from '../shell/shellState'
import { InvoiceImportAdmissionPanel } from './InvoiceImportAdmissionPanel'
import { fetchImportMappingReferences, fetchImportProducts, resolveImportSupplier, recordInvoiceImport } from './invoiceImportAdmissionApi'
import { fetchPurchasingSupplierOptions } from './api'
import { admissionFacts, admissionReferences, admissionProducts, admissionSupplier } from './invoiceImportAdmissionFixtures.testSupport'
import { importRequestFixture, importTestId as id } from './invoiceImportFixtures.testSupport'
import { resetAdjustmentAttemptsForTests } from './useReceiptAdjustmentAttempt'
import { INVOICE_IMPORT_TEXT as T } from './invoiceImportText'
import { DUPLICATE_REASON_REQUIRED, INVOICE_DUPLICATE_TEXT as D } from './invoiceDuplicateText'

let shell: ShellState
const auth = vi.hoisted(() => ({ capture: vi.fn(() => ({ synthetic: true })), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => shell }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => auth }))
vi.mock('./invoiceImportAdmissionApi', () => ({ fetchImportMappingReferences: vi.fn(), fetchImportProducts: vi.fn(), resolveImportSupplier: vi.fn(), recordInvoiceImport: vi.fn() }))
vi.mock('./api', () => ({ fetchPurchasingSupplierOptions: vi.fn() }))
const clients: QueryClient[] = []
function mount(binding = { company: id(2), namespace: 'synthetic-import:actor:company', entry: 1 }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0, refetchOnWindowFocus: false } } }); clients.push(client)
  return render(<MemoryRouter><QueryClientProvider client={client}><InvoiceImportAdmissionPanel binding={binding}
    id={id(1)} source={importRequestFixture()} invoice={admissionFacts()} /></QueryClientProvider></MemoryRouter>)
}
async function map() {
  fireEvent.click(screen.getByRole('button', { name: T.mapAndRecord }))
  await screen.findByRole('option', { name: 'SENTETIK tedarikçi' })
  fireEvent.change(screen.getByLabelText(T.supplierSelection), { target: { value: `${id(14)}:${id(15)}` } })
  await waitFor(() => expect(resolveImportSupplier).toHaveBeenCalledTimes(1))
  fireEvent.change(screen.getByLabelText(T.currencySelection), { target: { value: id(12) } })
  fireEvent.change(screen.getByLabelText(T.lineKind), { target: { value: 'product' } })
  fireEvent.change(screen.getByLabelText(T.productSearch), { target: { value: 'SYN' } })
  fireEvent.click(screen.getAllByRole('button', { name: T.find })[1])
  await screen.findByRole('option', { name: 'SYN · SENTETIK ürün · KGM' })
  fireEvent.change(screen.getByLabelText(T.productSelection), { target: { value: id(16) } })
  fireEvent.change(screen.getByLabelText(T.purchasePurpose), { target: { value: 'resale' } })
  await waitFor(() => expect((screen.getByRole('button', { name: T.recordInvoice }) as HTMLButtonElement).disabled).toBe(false))
}
beforeEach(() => {
  vi.clearAllMocks(); resetAdjustmentAttemptsForTests(); quick.result = null; quick.signal = null; quick.companyOverride = null
  shell = { ...initialShellState, phase: 'idle', activeContext: { companyId: id(2), userId: 'synthetic-actor', scopeMode: 'single', capabilityGeneration: 'g1',
    capabilities: ['purchasing.invoices.import'] } }
  vi.mocked(fetchImportMappingReferences).mockResolvedValue({ type: 'ok-single', data: admissionReferences() })
  vi.mocked(fetchImportProducts).mockResolvedValue({ type: 'ok-single', data: admissionProducts() })
  vi.mocked(resolveImportSupplier).mockResolvedValue({ type: 'ok-single', data: admissionSupplier() })
  vi.mocked(fetchPurchasingSupplierOptions).mockResolvedValue({ type: 'ok-single', data: { items: [{ supplierPartyId: id(14), companyAccountId: id(15), label: 'SENTETIK tedarikçi' }], nextCursor: null } })
  vi.mocked(recordInvoiceImport).mockResolvedValue({ type: 'unknown', reason: 'network-error' })
})
afterEach(() => { cleanup(); clients.splice(0).forEach(x => x.clear()); resetAdjustmentAttemptsForTests() })
describe('actual-reference invoice admission workspace', () => {
  it('requires the import control and an explicit open before reading source references', () => {
    shell = { ...shell, activeContext: { ...shell.activeContext!, capabilities: [] } }; mount()
    expect(screen.queryByRole('button', { name: T.mapAndRecord })).toBeNull(); expect(fetchImportMappingReferences).not.toHaveBeenCalled()
  })
  it('selects actual supplier versions and product identities without GUID or JSON inputs or amount recomputation', async () => {
    mount(); expect(fetchImportMappingReferences).not.toHaveBeenCalled(); await map()
    expect(resolveImportSupplier).toHaveBeenCalledWith(id(2), id(1), importRequestFixture(), id(14), id(15), expect.any(AbortSignal))
    expect((screen.getByLabelText(T.amount) as HTMLInputElement).readOnly).toBe(true)
    expect(screen.getByText(`${T.actualItemType}: ${T.steel} · ${T.sourceVersion}: 7`)).not.toBeNull()
    expect(screen.queryByRole('textbox', { name: /guid|json|kimlik/i })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: T.recordInvoice })); await screen.findByText(T.unknown)
    expect(recordInvoiceImport).toHaveBeenCalledWith(id(2), id(1), expect.objectContaining({ request: expect.objectContaining({ expectedPartyVersion: '4', expectedAccountVersion: '5',
      lines: [expect.objectContaining({ productId: id(16), expectedProductVersion: '6', itemType: 'Steel', expectedItemTypeVersion: '7', linePurpose: 'resale' })],
      installments: [expect.objectContaining({ amount: '12', functionalAmount: null })] }) }), expect.any(String))
  })
  it('ALS-D125: a duplicate warning asks for a reason, never blocks, and resends with it under a new key', async () => {
    vi.mocked(recordInvoiceImport).mockResolvedValueOnce({ type: 'failed', status: 422, code: DUPLICATE_REASON_REQUIRED })
    mount(); await map()
    fireEvent.click(screen.getByRole('button', { name: T.recordInvoice }))
    const warning = await screen.findByTestId('import-duplicate-warning')
    expect(within(warning).getByRole('alert').textContent).toBe(D.importWarning)
    expect(screen.queryByText(T.rejected)).toBeNull()
    expect(vi.mocked(recordInvoiceImport).mock.calls[0][2].request.duplicateReason).toBeUndefined()
    // Without a reason nothing is sent.
    fireEvent.click(screen.getByRole('button', { name: T.recordInvoice }))
    expect(await screen.findByText(D.importReasonRequired)).not.toBeNull()
    expect(recordInvoiceImport).toHaveBeenCalledTimes(1)
    fireEvent.change(screen.getByLabelText(D.importReason), { target: { value: '  SENTETIK düzeltme faturası  ' } })
    fireEvent.click(screen.getByRole('button', { name: T.recordInvoice }))
    await waitFor(() => expect(recordInvoiceImport).toHaveBeenCalledTimes(2))
    const [first, second] = vi.mocked(recordInvoiceImport).mock.calls
    expect(second[2].request.duplicateReason).toBe('SENTETIK düzeltme faturası')
    expect(second[3]).not.toBe(first[3])
  })
  it('freezes the original request and retries with the same key after an unknown response', async () => {
    const view = mount(); await map(); fireEvent.click(screen.getByRole('button', { name: T.recordInvoice })); await screen.findByText(T.unknown)
    const first = vi.mocked(recordInvoiceImport).mock.calls[0]
    expect(screen.getByLabelText(T.currencySelection).closest('fieldset')?.disabled).toBe(true)
    view.unmount(); mount(); fireEvent.click(screen.getByRole('button', { name: T.mapAndRecord }))
    fireEvent.click(await screen.findByRole('button', { name: T.retry })); await waitFor(() => expect(recordInvoiceImport).toHaveBeenCalledTimes(2))
    expect(vi.mocked(recordInvoiceImport).mock.calls[1]).toEqual(first)
  })
  it('shows an actual Service product but directs the actor to the independent Settings service source instead of making it goods', async () => {
    const products = admissionProducts()
    vi.mocked(fetchImportProducts).mockResolvedValue({ type: 'ok-single', data: { ...products, items: [{ ...products.items[0], itemType: 'Service' }] } })
    mount(); await map(); await screen.findByText(T.productServiceMapping)
    expect((screen.getByLabelText(T.purchasePurpose) as HTMLSelectElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: T.recordInvoice })); await screen.findByText(T.admissionInvalid)
    expect(recordInvoiceImport).not.toHaveBeenCalled()
  })
  it.each([403, 404, 409, 503])('renders mapping source status %s and refuses unavailable mapping', async status => {
    vi.mocked(fetchImportMappingReferences).mockResolvedValue({ type: 'http', status }); mount()
    fireEvent.click(screen.getByRole('button', { name: T.mapAndRecord }))
    const state: Record<number, string> = { 403: 'forbidden', 404: 'not-found', 409: 'conflict', 503: 'service-unavailable' }
    await waitFor(() => expect(document.querySelector(`[data-state="${state[status]}"]`)).not.toBeNull())
    expect(screen.queryByRole('button', { name: T.recordInvoice })).toBeNull(); expect(recordInvoiceImport).not.toHaveBeenCalled()
    if (status === 403) expect(auth.report).toHaveBeenCalledWith(403, expect.anything())
  })
  it.each(['offline', 'malformed'] as const)('renders %s without converting missing source to an empty successful list', async kind => {
    vi.mocked(fetchImportMappingReferences).mockResolvedValue({ type: kind }); mount(); fireEvent.click(screen.getByRole('button', { name: T.mapAndRecord }))
    await waitFor(() => expect(document.querySelector(`[data-state="${kind === 'malformed' ? 'malformed-response' : kind}"]`)).not.toBeNull()); expect(recordInvoiceImport).not.toHaveBeenCalled()
  })
})

// The shared Cari form/atomic/unknown engine remains covered by its own suites. This seam exercises only
// this existing invoice workspace's owner eligibility/resolve reads and unresolved parent-submit boundary.
const quick = vi.hoisted(()=>({result:null as boolean|null,signal:null as AbortSignal|null,companyOverride:null as string|null}))
vi.mock('../cari/CariQuickCreate',()=>({CariQuickCreate:({initialRoles,disabled,onCreated,onUnresolvedChange}:{initialRoles:number;disabled?:boolean;onCreated:(record:CounterpartyMutationResult,signal:AbortSignal)=>Promise<boolean>;onUnresolvedChange?:(pending:boolean)=>void})=>{
 const controller=useRef<AbortController|null>(null)
 useEffect(()=>()=>controller.current?.abort(),[])
 return <>
  <button type="button" disabled={disabled} onClick={()=>{
   controller.current?.abort();const next=new AbortController();controller.current=next;quick.signal=next.signal;onUnresolvedChange?.(true)
   void onCreated({id:id(34),code:'SENTETIK-CAR-NEW',kind:1,legalName:'SENTETIK new supplier',tradeName:null,isActive:true,version:'2',account:{id:id(35),companyId:quick.companyOverride??id(2),roles:initialRoles,isActive:true,version:'1'}},next.signal).then(selected=>{if(!next.signal.aborted){quick.result=selected;onUnresolvedChange?.(false)}})
  }}>SENTETIK quick create</button>
  <button type="button" onClick={()=>onUnresolvedChange?.(true)}>SENTETIK unresolved create</button>
  <button type="button" onClick={()=>onUnresolvedChange?.(false)}>SENTETIK create resolved</button>
  <span data-testid="quick-initial-roles">{initialRoles}</span>
 </>
}}))
const createdOffer={supplierPartyId:id(34),companyAccountId:id(35),label:'SENTETIK newly eligible supplier'}
const createdResolution={...admissionSupplier(),supplierPartyId:id(34),companyAccountId:id(35),partyVersion:'19',accountVersion:'23'}
describe('incoming invoice shared quick Cari entry',()=>{
 it('re-reads all current owner option pages and document-bound eligibility before selecting the new company account',async()=>{
  mount();await map()
  vi.mocked(fetchPurchasingSupplierOptions).mockResolvedValueOnce({type:'ok-single',data:{items:[],nextCursor:'SENTETIK-next'}}).mockResolvedValueOnce({type:'ok-single',data:{items:[createdOffer],nextCursor:null}})
  vi.mocked(resolveImportSupplier).mockResolvedValue({type:'ok-single',data:createdResolution})
  const amount=screen.getByLabelText(T.amount) as HTMLInputElement,originalAmount=amount.value
  fireEvent.click(screen.getByRole('button',{name:'SENTETIK quick create'}))
  await waitFor(()=>expect(quick.result).toBe(true))
  expect(fetchPurchasingSupplierOptions).toHaveBeenCalledWith(id(2),{q:'SENTETIK-CAR-NEW',cursor:'SENTETIK-next'},expect.any(AbortSignal))
  expect(resolveImportSupplier).toHaveBeenCalledWith(id(2),id(1),importRequestFixture(),id(34),id(35),expect.any(AbortSignal))
  expect((screen.getByLabelText(T.supplierSelection) as HTMLSelectElement).value).toBe(`${id(34)}:${id(35)}`)
  expect(screen.getByTestId('quick-initial-roles').textContent).toBe(String(PARTY_ROLE_FLAG.Supplier))
  expect(amount.value).toBe(originalAmount);expect((screen.getByLabelText(T.purchasePurpose) as HTMLSelectElement).value).toBe('resale')
  await waitFor(()=>expect((screen.getByRole('button',{name:T.recordInvoice}) as HTMLButtonElement).disabled).toBe(false))
  fireEvent.click(screen.getByRole('button',{name:T.recordInvoice}));await screen.findByText(T.unknown)
  expect(recordInvoiceImport).toHaveBeenCalledWith(id(2),id(1),expect.objectContaining({request:expect.objectContaining({supplierPartyId:id(34),companyAccountId:id(35),expectedPartyVersion:'19',expectedAccountVersion:'23',lines:[expect.objectContaining({productId:id(16),linePurpose:'resale'})]})}),expect.any(String))
 })
 it.each(['missing','other-account','forbidden','malformed','unavailable'] as const)('preserves the original selection when the supplier owner cannot offer the exact created account: %s',async refusal=>{
  mount();await map();const reads=vi.mocked(resolveImportSupplier).mock.calls.length
  vi.mocked(fetchPurchasingSupplierOptions).mockResolvedValue(refusal==='forbidden'?{type:'http',status:403}:refusal==='unavailable'?{type:'http',status:503}:refusal==='malformed'?{type:'malformed'}:{type:'ok-single',data:{items:refusal==='other-account'?[{...createdOffer,companyAccountId:id(36)}]:[],nextCursor:null}})
  fireEvent.click(screen.getByRole('button',{name:'SENTETIK quick create'}));await waitFor(()=>expect(quick.result).toBe(false))
  expect((screen.getByLabelText(T.supplierSelection) as HTMLSelectElement).value).toBe(`${id(14)}:${id(15)}`);expect(resolveImportSupplier).toHaveBeenCalledTimes(reads);expect(recordInvoiceImport).not.toHaveBeenCalled()
  if(refusal==='forbidden')expect(auth.report).toHaveBeenCalledWith(403,expect.anything())
 })
 it.each([403,409,503])('preserves existing data if document-specific resolution refuses the created account (%s)',async status=>{
  mount();await map();vi.mocked(fetchPurchasingSupplierOptions).mockResolvedValue({type:'ok-single',data:{items:[createdOffer],nextCursor:null}})
  vi.mocked(resolveImportSupplier).mockResolvedValue({type:'http',status})
  fireEvent.click(screen.getByRole('button',{name:'SENTETIK quick create'}));await waitFor(()=>expect(quick.result).toBe(false))
  expect((screen.getByLabelText(T.supplierSelection) as HTMLSelectElement).value).toBe(`${id(14)}:${id(15)}`);expect((screen.getByLabelText(T.purchasePurpose) as HTMLSelectElement).value).toBe('resale');expect(recordInvoiceImport).not.toHaveBeenCalled()
  if(status===403)expect(auth.report).toHaveBeenCalledWith(403,expect.anything())
 })
 it('blocks the parent record handler while the shared create result is unresolved, without discarding the invoice mapping',async()=>{
  mount();await map();fireEvent.click(screen.getByRole('button',{name:'SENTETIK unresolved create'}))
  expect((screen.getByRole('button',{name:T.recordInvoice}) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.click(screen.getByRole('button',{name:T.recordInvoice}));expect(recordInvoiceImport).not.toHaveBeenCalled()
  expect((screen.getByLabelText(T.purchasePurpose) as HTMLSelectElement).value).toBe('resale')
  fireEvent.click(screen.getByRole('button',{name:'SENTETIK create resolved'}));expect((screen.getByRole('button',{name:T.recordInvoice}) as HTMLButtonElement).disabled).toBe(false)
 })
 it('does not query owner eligibility for a mutation result from a foreign company',async()=>{
  mount();await map();const reads=vi.mocked(fetchPurchasingSupplierOptions).mock.calls.length;quick.companyOverride=id(3)
  fireEvent.click(screen.getByRole('button',{name:'SENTETIK quick create'}));await waitFor(()=>expect(quick.result).toBe(false))
  expect(fetchPurchasingSupplierOptions).toHaveBeenCalledTimes(reads);expect((screen.getByLabelText(T.supplierSelection) as HTMLSelectElement).value).toBe(`${id(14)}:${id(15)}`);expect(recordInvoiceImport).not.toHaveBeenCalled()
 })
 it.each(['company','session'] as const)('aborts a late eligibility reply after %s context replacement instead of selecting it',async change=>{
  const view=mount();await map();let release!:(value:Awaited<ReturnType<typeof fetchPurchasingSupplierOptions>>)=>void
  const late=new Promise<Awaited<ReturnType<typeof fetchPurchasingSupplierOptions>>>(resolve=>{release=resolve})
  vi.mocked(fetchPurchasingSupplierOptions).mockImplementation((_company,query)=>query?.q==='SENTETIK-CAR-NEW'?late:Promise.resolve<Awaited<ReturnType<typeof fetchPurchasingSupplierOptions>>>({type:'ok-single',data:{items:[],nextCursor:null}}))
  const reads=vi.mocked(resolveImportSupplier).mock.calls.length
  fireEvent.click(screen.getByRole('button',{name:'SENTETIK quick create'}));await waitFor(()=>expect(quick.signal).not.toBeNull())
  view.unmount();expect(quick.signal?.aborted).toBe(true)
  mount({company:change==='company'?id(3):id(2),namespace:`synthetic-import:new-${change}`,entry:2});fireEvent.click(screen.getByRole('button',{name:T.mapAndRecord}))
  release({type:'ok-single',data:{items:[createdOffer],nextCursor:null}})
  await waitFor(()=>expect(vi.mocked(fetchPurchasingSupplierOptions).mock.calls.length).toBeGreaterThan(1))
  expect(resolveImportSupplier).toHaveBeenCalledTimes(reads);expect(screen.queryByRole('option',{name:createdOffer.label})).toBeNull();expect(recordInvoiceImport).not.toHaveBeenCalled()
 })
})
