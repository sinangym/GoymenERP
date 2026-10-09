import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryLabelDetailPage } from './InventoryLabelDetailPage'
import { InventoryStockPositionPage } from './InventoryStockPositionPage'
import {
  INVENTORY_LABEL_TRANSFORMATION_CAPABILITY,
  INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY,
  INVENTORY_STOCK_COUNT_RECORD_CAPABILITY,
  INVENTORY_STOCK_POSITIONS_CAPABILITY,
  INVENTORY_STOCK_SCRAP_RECORD_CAPABILITY,
} from './capabilities'
import { CAPABILITY } from '../shell/capabilities'
import { MANUFACTURING_CAPABILITY } from '../manufacturing/capabilities'
import { PURCHASE_SUPPLY_SCROLL_LABEL } from './PurchaseSupplySection'
import inventoryStyles from './inventory.module.css'

/**
 * AT-063 / AT-064 on the real ST-01 and ST-02 components: a row's action menu shows only what the server's capability
 * snapshot permits, each action opens the existing ST-02 panel for that row's label, the back link returns to ST-01
 * with the filter kept, and the purchase remainder is a separate future-supply section that never adds to physical kg.
 * The panels themselves are stubs here (each has its own tests); what is measured is the navigation and gating.
 */
const COMPANY = '22222222-2222-4222-8222-222222222222'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const LABEL = '66666666-6666-4666-8666-666666666666'
const LOT = '77777777-7777-4777-8777-777777777777'
const PRODUCT = '88888888-8888-4888-8888-888888888888'
const ORDER = '99999999-9999-4999-8999-999999999999'
const LINE = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const LINE_2 = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const SUPPLIER = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'
const UNIT = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd'

const api = vi.hoisted(() => ({ positions: vi.fn(), legacy: vi.fn(), detail: vi.fn(), purchase: vi.fn() }))
const shell = vi.hoisted(() => ({ capabilities: [] as string[], report: vi.fn() }))

// ST-01 reads the #597 opt-in page (Stok UX FAZ-1); the previously published read must stay unused by it.
vi.mock('./stockDisplayApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('./stockDisplayApi')>(),
  fetchStockDisplayPage: api.positions,
}))
vi.mock('./api', async (importOriginal) => ({
  ...await importOriginal<typeof import('./api')>(),
  fetchStockPositions: api.legacy,
}))
vi.mock('./labelDetailApi', () => ({ fetchInventoryLabelDetail: api.detail }))
vi.mock('../logistics/api', () => ({ fetchPurchaseWork: api.purchase }))
vi.mock('./LabelAllocationPanel', () => ({
  LabelAllocationPanel: ({ startRelease }: { readonly startRelease?: boolean }) =>
    <section aria-label="Satış tahsisleri" data-start-release={String(startRelease === true)}><h2>Satış tahsisleri</h2></section>,
}))
vi.mock('./StockCountPanel', () => ({ StockCountPanel: () => <section aria-label="Sayım ve fark (ST-05)"><h2>Sayım ve fark (ST-05)</h2></section> }))
vi.mock('./StockScrapPanel', () => ({ StockScrapPanel: () => <section aria-label="Hurda / fire çıkışı"><h2>Hurda / fire çıkışı</h2></section> }))
vi.mock('./LabelTransformationPanel', () => ({ LabelTransformationPanel: () => <section aria-label="Bölme / birleştirme"><h2>Bölme / birleştirme</h2></section> }))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: COMPANY, scopeMode: 'single', capabilities: shell.capabilities },
  }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => ['fixture'],
  contextKeyString: () => 'fixture',
  hasCapability: (state: { readonly activeContext?: { readonly capabilities?: readonly string[] } }, capability: string) =>
    state.activeContext?.capabilities?.includes(capability) ?? false,
}))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: shell.report }) }))

const position = {
  companyId: COMPANY, labelId: LABEL, labelVersion: '3', labelCode: 'LBL-1', productId: PRODUCT,
  lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: null }, heatNumber: null, coilNumber: 'COIL-1',
  documentStatus: 'Complete', ownerKind: 'Company', ownerId: COMPANY,
  custodianKind: 'Company', custodianId: COMPANY, locationId: LOCATION,
  physicalKg: '100', freePhysicalKg: '90', blockedPhysicalKg: '10', activeAllocationKg: '30', availableKg: '60',
  productVersion: '1', classification: null,
  productDisplay: { productId: PRODUCT, expectedProductVersion: '1', identity: null, technical: null, errors: [] },
  locationDisplay: { locationId: LOCATION, locationCode: 'DEPO-A', locationName: 'Ana depo' },
}

/** The filters ST-01 sends with only a label-code search. */
const search = (q: string) => ({ q, productId: null, locationId: null, classification: null, owner: null })

function detail() {
  return {
    labelVersion: '3',
    identity: {
      labelId: LABEL, companyId: COMPANY, code: 'LBL-1', productId: PRODUCT, lotId: LOT, heatNumber: null,
      coilNumber: 'COIL-1', classification: null, documentStatus: 'Complete', requiredDocuments: [], documents: [], prints: [],
      lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: null },
    },
    balances: { labelId: LABEL, physicalKg: '100', freeKg: '90', blockedKg: '10', positions: [] },
    genealogy: { labelId: LABEL, labelCode: 'LBL-1', windowFull: false, edges: [] },
    movementTimeline: { labelId: LABEL, windowFull: false, entries: [] },
  }
}

function purchaseRow(lineId: string, lineNo: number, remaining: string) {
  return {
    orderId: ORDER, orderEffectiveOn: '2026-09-20', lineId, lineNo, kind: 'General', supplierPartyId: SUPPLIER,
    supplierName: 'SENTETIK Tedarikçi', productId: PRODUCT, unitId: UNIT, committed: '500', accepted: '120', remaining,
  }
}

function LocationProbe() {
  const location = useLocation()
  return <output aria-label="Adres">{`${location.pathname}${location.search}`}</output>
}

function mount(initial = '/stok/pozisyonlar') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[initial]}>
    <Routes>
      <Route path="/stok/pozisyonlar" element={<InventoryStockPositionPage />} />
      <Route path="/stok/etiket/:labelId" element={<InventoryLabelDetailPage />} />
      <Route path="/uretim/plan/yeni" element={<h1>Yeni taslak plan</h1>} />
    </Routes>
    <LocationProbe />
  </MemoryRouter></QueryClientProvider>)
}

const address = () => screen.getByRole('status', { name: 'Adres' }).textContent

beforeEach(() => {
  shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY]
  shell.report.mockReset()
  api.positions.mockReset().mockResolvedValue({ type: 'ok-single', data: { items: [position], searchWindowFull: false, nextCursor: null } })
  api.detail.mockReset().mockResolvedValue({ type: 'ok-single', data: detail() })
  api.purchase.mockReset().mockResolvedValue({ type: 'ok-single', data: { items: [purchaseRow(LINE, 1, '380')], nextCursor: null } })
})
afterEach(() => cleanup())

describe('AT-063 ST-01 row actions', () => {
  it('shows only the actions the server capability snapshot permits', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_STOCK_COUNT_RECORD_CAPABILITY]
    const user = userEvent.setup()
    mount()
    const toggle = await screen.findByRole('button', { name: 'LBL-1 işlemleri' })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    await user.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    const menu = screen.getByRole('list', { name: 'LBL-1 için işlemler' })
    // STOCK-DS-01: the toggle and its list stay together in the row's own pinned actions cell (its last cell).
    const actionsCell = toggle.closest('td')!
    expect(actionsCell.closest('tr')!.lastElementChild).toBe(actionsCell)
    expect(actionsCell.contains(menu)).toBe(true)
    expect(within(menu).getAllByRole('link').map((link) => link.textContent))
      .toEqual(['Etiket 360’ı aç', 'Sayım / fark', 'Etiket / lot soy ağacı'])
    expect(within(menu).queryByRole('link', { name: 'Tahsisi görüntüle / revize et' })).toBeNull()
    expect(within(menu).queryByRole('link', { name: 'Hurda / fire çıkışı' })).toBeNull()
    expect(within(menu).queryByRole('link', { name: 'Böl / birleştir' })).toBeNull()
  })

  it('offers every decided action to an actor holding every gate, each on the row label', async () => {
    shell.capabilities = [
      INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY, INVENTORY_STOCK_COUNT_RECORD_CAPABILITY,
      INVENTORY_STOCK_SCRAP_RECORD_CAPABILITY, INVENTORY_LABEL_TRANSFORMATION_CAPABILITY, MANUFACTURING_CAPABILITY.planCreate,
    ]
    const user = userEvent.setup()
    mount()
    await user.click(await screen.findByRole('button', { name: 'LBL-1 işlemleri' }))
    const links = within(screen.getByRole('list', { name: 'LBL-1 için işlemler' })).getAllByRole('link')
    expect(links.map((link) => [link.textContent, link.getAttribute('href')])).toEqual([
      ['Etiket 360’ı aç', `/stok/etiket/${LABEL}`],
      ['Tahsisi görüntüle / revize et', `/stok/etiket/${LABEL}?islem=tahsis`],
      ['Tahsisi serbest bırak', `/stok/etiket/${LABEL}?islem=serbest-birak`],
      ['Üretim planla', `/uretim/plan/yeni?etiket=${LABEL}&kod=LBL-1`],
      ['Sayım / fark', `/stok/etiket/${LABEL}?islem=sayim`],
      ['Hurda / fire çıkışı', `/stok/etiket/${LABEL}?islem=hurda`],
      ['Böl / birleştir', `/stok/etiket/${LABEL}?islem=bol-birlestir`],
      ['Etiket / lot soy ağacı', `/stok/etiket/${LABEL}?islem=soy-agaci`],
    ])
  })

  it('offers release only on a row holding active allocation kg', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY]
    api.positions.mockReset().mockResolvedValue({ type: 'ok-single', data: {
      items: [{ ...position, activeAllocationKg: '0', availableKg: '90' }], searchWindowFull: false, nextCursor: null,
    } })
    const user = userEvent.setup()
    mount()
    await user.click(await screen.findByRole('button', { name: 'LBL-1 işlemleri' }))
    expect(within(screen.getByRole('list', { name: 'LBL-1 için işlemler' })).getAllByRole('link').map((link) => link.textContent))
      .toEqual(['Etiket 360’ı aç', 'Tahsisi görüntüle / revize et', 'Etiket / lot soy ağacı'])
  })

  it('opens the allocation panel in release mode from the release action, focused on the row label', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY]
    const user = userEvent.setup()
    mount()
    await user.click(await screen.findByRole('button', { name: 'LBL-1 işlemleri' }))
    await user.click(screen.getByRole('link', { name: 'Tahsisi serbest bırak' }))
    await screen.findByRole('heading', { level: 1, name: 'LBL-1' })
    expect(address()).toBe(`/stok/etiket/${LABEL}?islem=serbest-birak`)
    const target = document.getElementById('islem-tahsis')
    await waitFor(() => expect(document.activeElement).toBe(target))
    expect(within(target!).getByRole('region', { name: 'Satış tahsisleri' }).getAttribute('data-start-release')).toBe('true')
    expect(screen.queryByText('İstenen işlem bu etikette sizin için açık değil; etiket bilgileri gösteriliyor.')).toBeNull()
  })

  it('opens the allocation panel for revision without the release form from the view/revise action', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_PHYSICAL_ALLOCATION_WRITE_CAPABILITY]
    mount(`/stok/etiket/${LABEL}?islem=tahsis`)
    await screen.findByRole('heading', { level: 1, name: 'LBL-1' })
    expect(screen.getByRole('region', { name: 'Satış tahsisleri' }).getAttribute('data-start-release')).toBe('false')
  })

  it('says the release is not open to an actor without the allocation capability', async () => {
    mount(`/stok/etiket/${LABEL}?islem=serbest-birak`)
    await screen.findByRole('heading', { level: 1, name: 'LBL-1' })
    expect(screen.getByText('İstenen işlem bu etikette sizin için açık değil; etiket bilgileri gösteriliyor.')).toBeTruthy()
    expect(screen.queryByRole('region', { name: 'Satış tahsisleri' })).toBeNull()
  })

  it('opens the Manufacturing plan form with the row roll named, only for the plan-create capability', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, MANUFACTURING_CAPABILITY.planCreate]
    const user = userEvent.setup()
    mount()
    await user.click(await screen.findByRole('button', { name: 'LBL-1 işlemleri' }))
    await user.click(screen.getByRole('link', { name: 'Üretim planla' }))
    await screen.findByRole('heading', { level: 1, name: 'Yeni taslak plan' })
    expect(address()).toBe(`/uretim/plan/yeni?etiket=${LABEL}&kod=LBL-1`)
  })

  it('draws no menu at all when no action is permitted', async () => {
    shell.capabilities = []
    mount()
    await screen.findByText('LBL-1')
    expect(screen.queryByRole('button', { name: 'LBL-1 işlemleri' })).toBeNull()
  })

  it('closes on Escape and returns focus to its button', async () => {
    const user = userEvent.setup()
    mount()
    const toggle = await screen.findByRole('button', { name: 'LBL-1 işlemleri' })
    await user.click(toggle)
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('list', { name: 'LBL-1 için işlemler' })).toBeNull()
    expect(document.activeElement).toBe(toggle)
  })

  it('opens the existing panel with the row preselected, and back returns to ST-01 with the filter kept', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_STOCK_COUNT_RECORD_CAPABILITY]
    const user = userEvent.setup()
    mount()
    await screen.findByText('LBL-1')
    await user.type(screen.getByLabelText('Etiket kodu'), 'LBL')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await waitFor(() => expect(api.positions).toHaveBeenLastCalledWith(COMPANY, search('LBL'), null, expect.any(AbortSignal)))
    expect(address()).toBe('/stok/pozisyonlar?etiket=LBL')
    expect(api.positions).toHaveBeenCalledTimes(2)

    await user.click(await screen.findByRole('button', { name: 'LBL-1 işlemleri' }))
    await user.click(screen.getByRole('link', { name: 'Sayım / fark' }))
    await screen.findByRole('heading', { level: 1, name: 'LBL-1' })
    expect(address()).toBe(`/stok/etiket/${LABEL}?islem=sayim`)
    expect(api.detail).toHaveBeenCalledWith(COMPANY, LABEL, expect.any(AbortSignal))
    const target = document.getElementById('islem-sayim')
    expect(target).not.toBeNull()
    await waitFor(() => expect(document.activeElement).toBe(target))
    expect(within(target!).getByRole('heading', { name: 'Sayım ve fark (ST-05)' })).toBeTruthy()

    await user.click(screen.getByRole('link', { name: '← Stok pozisyonlarına dön' }))
    await screen.findByText('LBL-1')
    expect(address()).toBe('/stok/pozisyonlar?etiket=LBL')
    expect((screen.getByLabelText('Etiket kodu') as HTMLInputElement).value).toBe('LBL')
    expect(api.positions).toHaveBeenLastCalledWith(COMPANY, search('LBL'), null, expect.any(AbortSignal))
  })

  it('keeps the filter when the label code itself opens ST-02 and its back link is followed', async () => {
    const user = userEvent.setup()
    mount('/stok/pozisyonlar?etiket=LBL')
    await user.click(await screen.findByRole('link', { name: 'LBL-1' }))
    await screen.findByRole('heading', { level: 1, name: 'LBL-1' })
    expect(address()).toBe(`/stok/etiket/${LABEL}`)
    await user.click(screen.getByRole('link', { name: '← Stok pozisyonlarına dön' }))
    await screen.findByRole('link', { name: 'LBL-1' })
    expect(address()).toBe('/stok/pozisyonlar?etiket=LBL')
    expect(api.positions).toHaveBeenLastCalledWith(COMPANY, search('LBL'), null, expect.any(AbortSignal))
  })

  it('reads a filter already in the URL on arrival', async () => {
    mount('/stok/pozisyonlar?etiket=LBL')
    await screen.findByText('LBL-1')
    expect(api.positions).toHaveBeenCalledTimes(1)
    expect(api.positions).toHaveBeenCalledWith(COMPANY, search('LBL'), null, expect.any(AbortSignal))
  })

  it('says so when a named panel is not open to the actor, and shows no back link without an ST-01 origin', async () => {
    mount(`/stok/etiket/${LABEL}?islem=hurda`)
    await screen.findByRole('heading', { level: 1, name: 'LBL-1' })
    expect(screen.getByText('İstenen işlem bu etikette sizin için açık değil; etiket bilgileri gösteriliyor.')).toBeTruthy()
    expect(document.getElementById('islem-hurda')).toBeNull()
    expect(screen.queryByRole('link', { name: '← Stok pozisyonlarına dön' })).toBeNull()
  })
})

describe('AT-064 ST-01 purchase remainder as future supply', () => {
  it('is not read or drawn without the purchase work capability', async () => {
    mount()
    await screen.findByText('LBL-1')
    expect(api.purchase).not.toHaveBeenCalled()
    expect(screen.queryByRole('heading', { name: 'Gelecek arz — Alış Siparişi' })).toBeNull()
  })

  it('shows the owner remainder with source Alış Siparişi and never adds it to physical or free kg', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, CAPABILITY.logisticsOperationsPurchase]
    mount()
    const table = await screen.findByTestId('stock-purchase-supply-table')
    expect(api.purchase).toHaveBeenCalledWith(null, COMPANY, expect.any(AbortSignal))
    const row = within(table).getAllByRole('row')[1]
    expect(within(row).getAllByRole('cell').map((cell) => cell.textContent)).toEqual([
      'Alış Siparişi', 'Satın alma siparişi …99999999', '20.09.2026', '1', 'Genel', 'SENTETIK Tedarikçi', 'Ürün kartı',
      '…dddddddd', '500', '120', '380',
    ])
    expect(within(row).queryByRole('link')).toBeNull()
    const physical = screen.getByText('LBL-1').closest('tr')!
    // The physical row keeps its own four quantities (Fiziksel, Bloklu, Tahsisli, Kullanılabilir) …
    expect(within(physical).getAllByRole('cell').map((cell) => cell.textContent).slice(5, 9)).toEqual(['100', '10', '30', '60'])
    expect(physical.textContent).not.toContain('380')
    expect(api.positions).toHaveBeenCalledTimes(1)
    expect(api.legacy).not.toHaveBeenCalled()
  })

  it('links the order and product only for their own capabilities, and pages with the owner cursor', async () => {
    shell.capabilities = [
      INVENTORY_STOCK_POSITIONS_CAPABILITY, CAPABILITY.logisticsOperationsPurchase, CAPABILITY.purchasingOrdersDetail,
      CAPABILITY.productItemsDetail,
    ]
    api.purchase.mockReset()
      .mockResolvedValueOnce({ type: 'ok-single', data: { items: [purchaseRow(LINE, 1, '380')], nextCursor: 'c1' } })
      .mockResolvedValueOnce({ type: 'ok-single', data: { items: [purchaseRow(LINE_2, 2, '7.5')], nextCursor: null } })
    const user = userEvent.setup()
    mount()
    const table = await screen.findByTestId('stock-purchase-supply-table')
    expect(within(table).getByRole('link', { name: 'Satın alma siparişi …99999999' }).getAttribute('href'))
      .toBe(`/satinalma/siparisler/${ORDER}`)
    expect(within(table).getByRole('link', { name: 'Ürün kartı' }).getAttribute('href')).toBe(`/urun/${PRODUCT}`)
    await user.click(screen.getByRole('button', { name: 'Daha fazla alış satırı' }))
    await waitFor(() => expect(within(table).getAllByRole('row')).toHaveLength(3))
    expect(api.purchase).toHaveBeenLastCalledWith('c1', COMPANY, expect.any(AbortSignal))
    expect(screen.queryByRole('button', { name: 'Daha fazla alış satırı' })).toBeNull()
  })

  it('reports a forbidden read to the auth boundary and keeps the physical list', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, CAPABILITY.logisticsOperationsPurchase]
    api.purchase.mockReset().mockResolvedValue({ type: 'http', status: 403, body: null })
    mount()
    expect((await screen.findByRole('alert')).textContent).toBe('Alış siparişi kalanları okunamadı (403).')
    expect(shell.report).toHaveBeenCalledWith(403, { token: 1 })
    expect(screen.getByText('LBL-1')).toBeTruthy()
  })

  it('keeps a wide table inside its own keyboard-reachable scroll surface, never widening the page (390px regression)', async () => {
    shell.capabilities = [
      INVENTORY_STOCK_POSITIONS_CAPABILITY, CAPABILITY.logisticsOperationsPurchase, CAPABILITY.purchasingOrdersDetail,
      CAPABILITY.productItemsDetail,
    ]
    mount()
    const table = await screen.findByTestId('stock-purchase-supply-table')
    // The scroll surface is a named, focusable region that holds the whole table and nothing hidden from it.
    const surface = screen.getByRole('region', { name: PURCHASE_SUPPLY_SCROLL_LABEL })
    expect(surface).toBe(table.parentElement)
    expect(surface.tabIndex).toBe(0)
    expect(surface.className.split(' ')).toEqual(expect.arrayContaining([inventoryStyles.tableWrap, inventoryStyles.supplyScroll]))
    // The section is contained in the page's grid, so the table — not the page — scrolls sideways on a phone.
    const section = screen.getByRole('heading', { name: 'Gelecek arz — Alış Siparişi' }).closest('section')!
    expect(section.className.split(' ')).toContain(inventoryStyles.supplySection)
    expect(within(table).getAllByRole('cell')).toHaveLength(11)
    expect(within(table).getAllByRole('link').map((link) => link.textContent)).toEqual(['Satın alma siparişi …99999999', 'Ürün kartı'])
    // The stylesheet the browser draws with: containment on the section and the surface, the surface scrolls, and no
    // rule hides or clips a cell. (Page/table overflow in a real browser is the STUB probe's and the real acceptance's.)
    const nodeProcess = (globalThis as unknown as {
      readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
    }).process
    const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
    // Vitest runs from the web package (`npm test` in src/frontend/goymen-erp-web).
    const css = fs.readFileSync(`${nodeProcess.cwd()}/src/inventory/inventory.module.css`, 'utf8')
    const rule = (selector: string) => new RegExp(`(^|\\n)\\.${selector} \\{([^}]*)\\}`).exec(css)?.[2] ?? ''
    expect(rule('supplySection')).toMatch(/min-width:\s*0/)
    expect(rule('supplySection')).toMatch(/max-width:\s*100%/)
    expect(rule('supplyScroll')).toMatch(/max-width:\s*100%/)
    expect(rule('tableWrap')).toMatch(/overflow-x:\s*auto/)
    expect(`${rule('supplySection')}${rule('supplyScroll')}`).not.toMatch(/overflow:\s*hidden|display:\s*none|visibility|clip/)
  })

  it('states an empty remainder plainly', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, CAPABILITY.logisticsOperationsPurchase]
    api.purchase.mockReset().mockResolvedValue({ type: 'ok-single', data: { items: [], nextCursor: null } })
    mount()
    expect(await screen.findByText('Bu şirkette mal kabulü bekleyen onaylı alış siparişi satırı yok.')).toBeTruthy()
  })
})

describe('STOCK-LOCATION-2 on the ST-01 row', () => {
  it('shows the 2.0 location code and name on the same row its actions act on', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_STOCK_COUNT_RECORD_CAPABILITY]
    mount()
    const toggle = await screen.findByRole('button', { name: 'LBL-1 işlemleri' })
    const tr = toggle.closest('tr')!
    expect(within(tr).getByText('DEPO-A')).toBeTruthy()
    expect(within(tr).getByText('Ana depo')).toBeTruthy()
    expect(within(tr).getAllByRole('cell').map((cell) => cell.textContent).slice(5, 9)).toEqual(['100', '10', '30', '60'])
  })
})
