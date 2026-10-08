import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, RouterProvider, createMemoryRouter, useLocation } from 'react-router'
import { QueryClient } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryStockPositionPage } from './InventoryStockPositionPage'
import { CAPABILITY } from '../shell/capabilities'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { STOCK_DISPLAY_HEADER } from './stockDisplayApi'
import type { StockPositionFilters } from './stockDisplayApi'
import { INVENTORY_STOCK_POSITIONS_CAPABILITY } from './capabilities'
import tableStyles from '../ui/dataTable.module.css'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const GROUP_COMPANY = '22222222-2222-4222-8222-2222222222bb'
const COUNTERPARTY = '99999999-9999-4999-8999-999999999999'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const LABEL = '66666666-6666-4666-8666-666666666666'
const LABEL_2 = '66666666-6666-4666-8666-666666666667'
const LABEL_3 = '66666666-6666-4666-8666-666666666668'
const LOT = '77777777-7777-4777-8777-777777777777'
const PRODUCT = '88888888-8888-4888-8888-888888888888'
const FAMILY = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const TEMPLATE = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'

const api = vi.hoisted(() => ({ page: vi.fn(), legacy: vi.fn() }))
const auth = vi.hoisted(() => ({
  capture: vi.fn(() => ({ token: 1 })),
  report: vi.fn(),
  /** The real `useAuthBoundary` answers a NEW `{ capture, report }` object on every render. */
  freshPerRender: false,
}))
const shell = vi.hoisted(() => ({ capabilities: [] as string[], scopeMode: 'single' as string }))
/**
 * Test kind, fixed for a whole test and reset before each one. `fixture` (the default) draws the page
 * alone over a fixed shell context and a recording boundary. `real` mounts the page through the real
 * `ShellProvider`, router, `CapabilityRoute`, reducer and `useAuthBoundary`, and reads through the real
 * `fetchStockDisplayPage` over a stubbed `fetch`: every mock below then hands the call to the original.
 */
const mode = vi.hoisted(() => ({ real: false }))

vi.mock('./stockDisplayApi', async (importOriginal) => {
  const real = await importOriginal<typeof import('./stockDisplayApi')>()
  return {
    ...real,
    fetchStockDisplayPage: (...args: Parameters<typeof real.fetchStockDisplayPage>) =>
      mode.real ? real.fetchStockDisplayPage(...args) : api.page(...args),
  }
})
// The previously published read must never be used by this screen, not even as a fallback.
vi.mock('./api', async (importOriginal) => ({
  ...await importOriginal<typeof import('./api')>(),
  fetchStockPositions: api.legacy,
}))
vi.mock('../shell/shellContext', async (importOriginal) => {
  const real = await importOriginal<typeof import('../shell/shellContext')>()
  return {
    ...real,
    useShellState: () => mode.real ? real.useShellState() : ({
      sessionEpoch: 1, routeEntryGeneration: 1,
      activeContext: { userId: 'actor-a', companyId: COMPANY, scopeMode: shell.scopeMode, capabilities: shell.capabilities },
    } as unknown as ReturnType<typeof real.useShellState>),
  }
})
vi.mock('../shell/shellState', async (importOriginal) => {
  const real = await importOriginal<typeof import('../shell/shellState')>()
  return {
    ...real,
    contextKey: (...args: Parameters<typeof real.contextKey>) => mode.real ? real.contextKey(...args) : ['fixture'] as unknown as ReturnType<typeof real.contextKey>,
    contextKeyString: (...args: Parameters<typeof real.contextKeyString>) => mode.real ? real.contextKeyString(...args) : 'fixture',
    hasCapability: (...args: Parameters<typeof real.hasCapability>) => mode.real
      ? real.hasCapability(...args)
      : (args[0] as { readonly activeContext?: { readonly capabilities?: readonly string[] } }).activeContext?.capabilities?.includes(args[1]) ?? false,
  }
})
vi.mock('../shell/useAuthBoundary', async (importOriginal) => {
  const real = await importOriginal<typeof import('../shell/useAuthBoundary')>()
  return {
    ...real,
    useAuthBoundary: () => mode.real
      ? real.useAuthBoundary()
      : (auth.freshPerRender ? { capture: auth.capture, report: auth.report } : auth) as unknown as ReturnType<typeof real.useAuthBoundary>,
  }
})

/** The #597 producer fixture (`InventoryStockPositionHttpAcceptanceTests`) in this company. */
function row(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, labelId: LABEL, labelVersion: '3', labelCode: 'ROLL-1', productId: PRODUCT, productVersion: '7',
    classification: 'Standard', lot: { id: LOT, heatNumber: 'HEAT-1', coilNumber: 'COIL-1' }, heatNumber: 'HEAT-1',
    coilNumber: 'COIL-1', documentStatus: 'Complete', ownerKind: 'Company', ownerId: COMPANY,
    custodianKind: 'Company', custodianId: COMPANY, locationId: LOCATION,
    physicalKg: '10', freePhysicalKg: '8', blockedPhysicalKg: '2', activeAllocationKg: '1', availableKg: '7',
    productDisplay: {
      productId: PRODUCT, expectedProductVersion: '7',
      identity: { productId: PRODUCT, productVersion: '7', code: 'P-1', name: 'Galvaniz bant' },
      technical: {
        family: { familyId: FAMILY, familyCode: 'coil', sourceRevision: '#66/stock-display' },
        template: { templateId: TEMPLATE, templateVersion: '1', templateCode: 'coil-v1', sourceRevision: '#66/stock-display' },
        thicknessMm: '1.5', widthMm: '1250',
      },
      errors: [],
    },
    ...overrides,
  }
}

function ready(items: readonly unknown[], extra: { searchWindowFull?: boolean; nextCursor?: string | null } = {}) {
  const nextCursor = extra.nextCursor ?? null
  return { type: 'ok-single', data: { items, searchWindowFull: extra.searchWindowFull ?? nextCursor !== null, nextCursor } }
}

const NO_FILTERS: StockPositionFilters = { q: '', productId: null, locationId: null, classification: null, owner: null }
/** Every supported filter at once, as the URL holds it and as the read must send it on every page. */
const ALL_FILTERS_URL = `/stok/pozisyonlar?etiket=ROLL&urun=${PRODUCT}&lokasyon=${LOCATION}&sinif=hurda&sahipTuru=cari&sahip=${COUNTERPARTY}`
const ALL_FILTERS: StockPositionFilters = {
  q: 'ROLL', productId: PRODUCT, locationId: LOCATION, classification: 'Scrap', owner: { kind: 'Counterparty', id: COUNTERPARTY },
}

function Address() {
  const location = useLocation()
  return <output aria-label="Adres">{`${location.pathname}${location.search}`}</output>
}

function mount(path = '/stok/pozisyonlar') {
  return render(<MemoryRouter initialEntries={[path]}><InventoryStockPositionPage /><Address /></MemoryRouter>)
}

const address = () => screen.getByRole('status', { name: 'Adres' }).textContent
const bodyRow = (labelCode: string) => screen.getByRole('link', { name: labelCode }).closest('tr')!
const cellTexts = (tr: HTMLElement) => within(tr).getAllByRole('cell').map((cell) => cell.textContent)
const headerTexts = () => screen.getAllByRole('columnheader').map((header) => header.textContent)
const main = () => screen.getByRole('main')

/** A controllable viewport for the shell's phone width; `matches: false` is an explicit desktop. */
function stubViewport(phone: boolean) {
  const viewport = { phone }
  const listeners = new Set<() => void>()
  vi.stubGlobal('matchMedia', vi.fn((query: string) => ({
    get matches() { return viewport.phone },
    media: query, onchange: null,
    addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
    removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
    addListener: () => {}, removeListener: () => {}, dispatchEvent: () => false,
  })))
  return {
    set(next: boolean) {
      viewport.phone = next
      act(() => { for (const listener of [...listeners]) listener() })
    },
  }
}

beforeEach(() => {
  mode.real = false
  shell.capabilities = []
  shell.scopeMode = 'single'
  auth.freshPerRender = false
  auth.report.mockReset()
  api.legacy.mockReset()
  api.page.mockReset().mockResolvedValue(ready([row()]))
})
afterEach(() => vi.unstubAllGlobals())

describe('ST-01 on the #597 opt-in contract', () => {
  it('draws the producer row under the kept heading: label, Product, size, "—" location, owner, four quantities', async () => {
    shell.capabilities = [CAPABILITY.productItemsDetail, CAPABILITY.organizationLocationsDetail]
    stubViewport(false)
    mount()
    const tr = (await screen.findByRole('link', { name: 'ROLL-1' })).closest('tr')!
    expect(screen.getByRole('heading', { level: 1, name: 'Stok / Malzeme Pozisyonu' })).toBeTruthy()
    expect(screen.getByRole('table', { name: 'Stok / Malzeme Pozisyonu' })).toBeTruthy()
    expect(headerTexts()).toEqual([
      'Etiket / lot', 'Ürün', 'Ölçü (mm)', 'Lokasyon', 'Mal sahibi',
      'Fiziksel kg', 'Bloklu kg', 'Tahsisli kg', 'Kullanılabilir kg', 'İşlemler',
    ])
    expect(cellTexts(tr).slice(0, 5)).toEqual([
      'ROLL-1Döküm HEAT-1 · Rulo COIL-1',
      'P-1Galvaniz bantÜrün kartı',
      '1,5 × 1.250',
      '—Lokasyon kartı',
      'Şirket malı',
    ])
    expect(screen.getByRole('link', { name: 'ROLL-1' }).getAttribute('href')).toBe(`/stok/etiket/${LABEL}`)
    expect(within(tr).getByRole('link', { name: 'Ürün kartı' }).getAttribute('href')).toBe(`/urun/${PRODUCT}`)
    expect(within(tr).getByRole('link', { name: 'Lokasyon kartı' }).getAttribute('href'))
      .toBe(`/ayarlar/organizasyon/lokasyon/${LOCATION}`)
    // No raw id, API value or internal code is shown as text.
    for (const hidden of [PRODUCT, LOCATION, COMPANY, 'Complete', 'Standard', 'Company', 'ST-01', 'coil-v1']) {
      expect(main().textContent, hidden).not.toContain(hidden)
    }
    expect(api.page).toHaveBeenCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal))
    expect(api.legacy).not.toHaveBeenCalled()
  })

  it('maps the five quantities from their exact wire fields, in Turkish grouping, never rounded', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValue(ready([row({
      physicalKg: '18450.251', blockedPhysicalKg: '0.001', freePhysicalKg: '18450.25',
      activeAllocationKg: '450.25', availableKg: '18000',
    })]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(headerTexts().slice(5, 9)).toEqual(['Fiziksel kg', 'Bloklu kg', 'Tahsisli kg', 'Kullanılabilir kg'])
    expect(cellTexts(bodyRow('ROLL-1')).slice(5, 9)).toEqual(['18.450,251', '0,001', '450,25', '18.000'])
    // Blokajsız fiziksel (freePhysicalKg) is a secondary column, off by default and never in another's place.
    expect(screen.queryByRole('columnheader', { name: 'Blokajsız fiziksel kg' })).toBeNull()
    expect(cellTexts(bodyRow('ROLL-1'))).not.toContain('18.450,25')

    await user.click(screen.getByRole('button', { name: 'Sütunlar' }))
    await user.click(screen.getByRole('checkbox', { name: 'Blokajsız fiziksel kg sütunu' }))
    expect(headerTexts().slice(5, 10)).toEqual(['Fiziksel kg', 'Bloklu kg', 'Blokajsız fiziksel kg', 'Tahsisli kg', 'Kullanılabilir kg'])
    expect(cellTexts(bodyRow('ROLL-1')).slice(5, 10)).toEqual(['18.450,251', '0,001', '18.450,25', '450,25', '18.000'])
  })

  it('shows nominal millimetres under Ö2: trailing zeros dropped, nothing rounded', async () => {
    const technical = { ...row().productDisplay.technical, thicknessMm: '1.50', widthMm: '1250.0' }
    api.page.mockResolvedValue(ready([row({ productDisplay: { ...row().productDisplay, technical } })]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(cellTexts(bodyRow('ROLL-1'))[2]).toBe('1,5 × 1.250')
  })

  it('says "—" where the Product owner published no identity or nominal size, and keeps the authorised card link', async () => {
    shell.capabilities = [CAPABILITY.productItemsDetail]
    api.page.mockResolvedValue(ready([row({
      productDisplay: {
        productId: PRODUCT, expectedProductVersion: '7', identity: null, technical: null,
        errors: [{ field: 'productId', code: 'invalid_reference' }],
      },
    })]))
    mount()
    const tr = (await screen.findByRole('link', { name: 'ROLL-1' })).closest('tr')!
    expect(cellTexts(tr).slice(1, 3)).toEqual(['—Ürün kartı', '—'])
    expect(tr.textContent).not.toContain('invalid_reference')
  })

  it('names a missing heat or coil number instead of a bare dash', async () => {
    api.page.mockResolvedValue(ready([row({ lot: { id: LOT, heatNumber: null, coilNumber: null }, heatNumber: null, coilNumber: null })]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(cellTexts(bodyRow('ROLL-1'))[0]).toBe('ROLL-1Döküm — · Rulo —')
  })

  it('marks a non-standard class next to Kullanılabilir by default, and shows the Sınıf column on request', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValue(ready([
      row(),
      row({ labelId: LABEL_2, labelCode: 'ROLL-2', classification: 'Scrap' }),
      row({ labelId: LABEL_3, labelCode: 'ROLL-3', classification: 'SecondQuality' }),
      row({ labelId: '66666666-6666-4666-8666-666666666669', labelCode: 'ROLL-4', classification: null }),
    ]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(screen.queryByRole('columnheader', { name: 'Sınıf' })).toBeNull()
    expect(['ROLL-1', 'ROLL-2', 'ROLL-3', 'ROLL-4'].map((code) => cellTexts(bodyRow(code))[8]))
      .toEqual(['7', '7Hurda', '7İkinci kalite', '7'])

    await user.click(screen.getByRole('button', { name: 'Sütunlar' }))
    await user.click(screen.getByRole('checkbox', { name: 'Sınıf sütunu' }))
    expect(headerTexts()[3]).toBe('Sınıf')
    expect(['ROLL-1', 'ROLL-2', 'ROLL-3', 'ROLL-4'].map((code) => cellTexts(bodyRow(code))[3]))
      .toEqual(['Standart', 'Hurda', 'İkinci kalite', 'Sınıflandırılmamış'])
    // With its own column shown, the class is not repeated beside the quantity.
    expect(cellTexts(bodyRow('ROLL-2'))[9]).toBe('7')
    expect(main().textContent).not.toContain('Hurda sınıfı')
  })

  it('names ownership "Şirket malı" / "Cari malı", "—" for a group company, and a differing holder as "Malı tutan"', async () => {
    api.page.mockResolvedValue(ready([
      row(),
      row({ labelId: LABEL_2, labelCode: 'ROLL-2', ownerKind: 'Counterparty', ownerId: COUNTERPARTY }),
      row({ labelId: LABEL_3, labelCode: 'ROLL-3', ownerId: GROUP_COMPANY, custodianId: GROUP_COMPANY }),
      row({ labelId: '66666666-6666-4666-8666-666666666669', labelCode: 'ROLL-4', custodianKind: 'Counterparty', custodianId: COUNTERPARTY }),
      row({ labelId: '66666666-6666-4666-8666-66666666666a', labelCode: 'ROLL-5', custodianId: GROUP_COMPANY }),
    ]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(['ROLL-1', 'ROLL-2', 'ROLL-3', 'ROLL-4', 'ROLL-5'].map((code) => cellTexts(bodyRow(code))[4])).toEqual([
      'Şirket malı',
      'Cari malıMalı tutan: Şirket',
      '—',
      'Şirket malıMalı tutan: Cari',
      'Şirket malıMalı tutan: —',
    ])
    expect(main().textContent).not.toContain('Emanet')
  })

  it.each([
    { capabilities: [] as string[], productLink: false, locationLink: false },
    { capabilities: [CAPABILITY.productItemsDetail], productLink: true, locationLink: false },
    { capabilities: [CAPABILITY.organizationLocationsDetail], productLink: false, locationLink: true },
  ])('links each related card only with its own server capability ($capabilities)', async ({ capabilities, productLink, locationLink }) => {
    shell.capabilities = capabilities
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(screen.queryByRole('link', { name: 'Ürün kartı' }) !== null).toBe(productLink)
    expect(screen.queryByRole('link', { name: 'Lokasyon kartı' }) !== null).toBe(locationLink)
  })
})

describe('ST-01 filters — only what #597 supports, from the URL', () => {
  it('keeps the "Etiket kodu" search and its ?etiket= parameter', async () => {
    const user = userEvent.setup()
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    await user.type(screen.getByLabelText('Etiket kodu'), ' ROLL ')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(COMPANY, { ...NO_FILTERS, q: 'ROLL' }, null, expect.any(AbortSignal)))
    expect(address()).toBe('/stok/pozisyonlar?etiket=ROLL')
  })

  it('reads a filter already in the URL on arrival, and refuses a label code longer than the server accepts', async () => {
    const first = mount('/stok/pozisyonlar?etiket=ROLL')
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(api.page).toHaveBeenCalledTimes(1)
    expect(api.page).toHaveBeenCalledWith(COMPANY, { ...NO_FILTERS, q: 'ROLL' }, null, expect.any(AbortSignal))
    expect((screen.getByLabelText('Etiket kodu') as HTMLInputElement).value).toBe('ROLL')
    first.unmount()

    api.page.mockClear()
    mount(`/stok/pozisyonlar?etiket=${'X'.repeat(65)}`)
    expect((await screen.findByRole('alert')).textContent).toBe('Etiket kodu en çok 64 karakter olabilir.')
    expect(api.page).not.toHaveBeenCalled()
  })

  it('filters by Sınıf under "Diğer filtreler", with a removable chip, and "Tümü" clears it', async () => {
    const user = userEvent.setup()
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    const others = screen.getByRole('button', { name: 'Diğer filtreler' })
    expect(others.getAttribute('aria-expanded')).toBe('false')
    await user.click(others)
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sınıf' }), 'İkinci kalite')
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(
      COMPANY, { ...NO_FILTERS, classification: 'SecondQuality' }, null, expect.any(AbortSignal)))
    expect(address()).toBe('/stok/pozisyonlar?sinif=ikinci-kalite')
    const chips = await screen.findByRole('list', { name: 'Etkin filtreler' })
    await user.click(within(chips).getByRole('button', { name: 'Sınıf: İkinci kalite filtresini kaldır' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal)))
    expect(address()).toBe('/stok/pozisyonlar')
    // Focus lands on the result region, not on the page after its button went away.
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Stok listesi' }))

    await user.selectOptions(screen.getByRole('combobox', { name: 'Sınıf' }), 'Hurda')
    await waitFor(() => expect(address()).toBe('/stok/pozisyonlar?sinif=hurda'))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sınıf' }), 'Tümü')
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal)))
  })

  it('filters by a row’s exact Product, location and counterparty owner — no picker, no typed or guessed id', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValue(ready([row({ ownerKind: 'Counterparty', ownerId: COUNTERPARTY })]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })

    await user.click(screen.getByRole('button', { name: 'ROLL-1: bu ürüne göre filtrele' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(
      COMPANY, { ...NO_FILTERS, productId: PRODUCT }, null, expect.any(AbortSignal)))
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Stok listesi' }))
    await user.click(await screen.findByRole('button', { name: 'ROLL-1: bu lokasyona göre filtrele' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(
      COMPANY, { ...NO_FILTERS, productId: PRODUCT, locationId: LOCATION }, null, expect.any(AbortSignal)))
    await user.click(await screen.findByRole('button', { name: 'ROLL-1: bu mal sahibine göre filtrele' }))
    const all = { ...NO_FILTERS, productId: PRODUCT, locationId: LOCATION, owner: { kind: 'Counterparty', id: COUNTERPARTY } }
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(COMPANY, all, null, expect.any(AbortSignal)))
    expect(address()).toBe(`/stok/pozisyonlar?urun=${PRODUCT}&lokasyon=${LOCATION}&sahipTuru=cari&sahip=${COUNTERPARTY}`)

    const chips = await screen.findByRole('list', { name: 'Etkin filtreler' })
    // An exact owner filter names one owner: the contract has no counterparty name, so it says "seçili cari".
    expect(within(chips).getAllByRole('listitem').map((item) => item.firstChild?.textContent)).toEqual([
      'Ürün: P-1 Galvaniz bant', 'Lokasyon: seçili lokasyon', 'Mal sahibi: seçili cari',
    ])
    // A filter already applied is not offered again on the row.
    expect(screen.queryByRole('button', { name: 'ROLL-1: bu ürüne göre filtrele' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'ROLL-1: bu lokasyona göre filtrele' })).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Filtreleri temizle' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal)))
    expect(address()).toBe('/stok/pozisyonlar')
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Stok listesi' }))
  })

  it('filters by another group company’s exact id — never by the company on screen', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValue(ready([row({ ownerId: GROUP_COMPANY, custodianId: GROUP_COMPANY })]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    await user.click(screen.getByRole('button', { name: 'ROLL-1: bu mal sahibine göre filtrele' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(
      COMPANY, { ...NO_FILTERS, owner: { kind: 'Company', id: GROUP_COMPANY } }, null, expect.any(AbortSignal)))
    expect(address()).toBe(`/stok/pozisyonlar?sahipTuru=sirket&sahip=${GROUP_COMPANY}`)
    expect(within(await screen.findByRole('list', { name: 'Etkin filtreler' })).getByText('Mal sahibi: seçili şirket')).toBeTruthy()
  })

  it('names a filtered Product it cannot see on screen as "seçili ürün"', async () => {
    api.page.mockResolvedValue(ready([]))
    mount(`/stok/pozisyonlar?urun=${PRODUCT}`)
    expect(await screen.findByText('Filtrelere uyan fiziksel stok yok.')).toBeTruthy()
    expect(within(screen.getByRole('list', { name: 'Etkin filtreler' })).getByText('Ürün: seçili ürün')).toBeTruthy()
    expect(main().textContent).not.toContain(PRODUCT)
  })

  it('drops a URL filter outside its grammar, and an owner id without its kind, instead of sending them', async () => {
    mount(`/stok/pozisyonlar?urun=not-a-guid&sinif=Standard&sahipTuru=cari&lokasyon=00000000-0000-0000-0000-000000000000&sahip=${GROUP_COMPANY}x`)
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(api.page).toHaveBeenLastCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal))
  })

  it('never builds an owner pair from an id alone', async () => {
    mount(`/stok/pozisyonlar?sahip=${COUNTERPARTY}`)
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(api.page).toHaveBeenLastCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal))
  })
})

describe('ST-01 paging follows the server cursor', () => {
  it('appends the next page with every filter and the server’s cursor, focuses the first new row, resets on a change', async () => {
    const user = userEvent.setup()
    api.page
      .mockResolvedValueOnce(ready([row()], { nextCursor: 'cursor-1' }))
      .mockResolvedValueOnce(ready([row({ labelId: LABEL_2, labelCode: 'ROLL-2' })]))
      .mockResolvedValue(ready([row()]))
    mount(ALL_FILTERS_URL)
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(api.page).toHaveBeenNthCalledWith(1, COMPANY, ALL_FILTERS, null, expect.any(AbortSignal))

    await user.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    const appended = await screen.findByRole('link', { name: 'ROLL-2' })
    expect(api.page).toHaveBeenNthCalledWith(2, COMPANY, ALL_FILTERS, 'cursor-1', expect.any(AbortSignal))
    await waitFor(() => expect(document.activeElement).toBe(appended))
    expect(screen.getAllByRole('row')).toHaveLength(3)
    expect(screen.queryByRole('button', { name: 'Daha fazla göster' })).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Diğer filtreler' }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sınıf' }), 'Standart')
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(
      COMPANY, { ...ALL_FILTERS, classification: 'Standard' }, null, expect.any(AbortSignal)))
  })

  it('offers only a fresh start, with every filter, once the server no longer accepts the cursor', async () => {
    const user = userEvent.setup()
    api.page
      .mockResolvedValueOnce(ready([row()], { nextCursor: 'cursor-1' }))
      .mockResolvedValueOnce({ type: 'http', status: 400, body: { code: 'inventory_stock_position_invalid_query' } })
      .mockResolvedValue(ready([row()]))
    mount(ALL_FILTERS_URL)
    await screen.findByRole('link', { name: 'ROLL-1' })
    await user.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Sonraki sayfa artık açılamıyor; listeyi baştan yükleyin.')
    expect(screen.getByRole('link', { name: 'ROLL-1' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Daha fazla göster' })).toBeNull()
    await user.click(within(alert).getByRole('button', { name: 'Listeyi baştan yükle' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(COMPANY, ALL_FILTERS, null, expect.any(AbortSignal)))
    expect(api.legacy).not.toHaveBeenCalled()
  })

  it('refuses a next page that repeats a label already listed (the window moved under the cursor)', async () => {
    const user = userEvent.setup()
    api.page
      .mockResolvedValueOnce(ready([row()], { nextCursor: 'cursor-1' }))
      .mockResolvedValueOnce(ready([row()]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    await user.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Sonraki sayfa artık açılamıyor')
    expect(screen.getAllByRole('link', { name: 'ROLL-1' })).toHaveLength(1)
  })

  it.each([401, 403])('reports a %i on "Daha fazla göster" to the auth boundary and keeps the rows', async (status) => {
    const user = userEvent.setup()
    api.page
      .mockResolvedValueOnce(ready([row()], { nextCursor: 'cursor-1' }))
      .mockResolvedValueOnce({ type: 'http', status, body: null })
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    await user.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Sonraki stok satırları okunamadı.')
    expect(auth.report).toHaveBeenCalledWith(status, { token: 1 })
    expect(screen.getByRole('link', { name: 'ROLL-1' })).toBeTruthy()
    expect(api.legacy).not.toHaveBeenCalled()
  })

  it('drops a next page that answers after the filters changed', async () => {
    const user = userEvent.setup()
    let answerMore: (value: unknown) => void = () => {}
    api.page
      .mockResolvedValueOnce(ready([row()], { nextCursor: 'cursor-1' }))
      .mockImplementationOnce(() => new Promise((resolve) => { answerMore = resolve }))
      .mockResolvedValue(ready([row({ labelId: LABEL_3, labelCode: 'ROLL-3', classification: 'Scrap' })]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    await user.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    await user.click(screen.getByRole('button', { name: 'Diğer filtreler' }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sınıf' }), 'Hurda')
    await screen.findByRole('link', { name: 'ROLL-3' })
    await act(async () => { answerMore(ready([row({ labelId: LABEL_2, labelCode: 'ROLL-2' })])) })
    expect(screen.queryByRole('link', { name: 'ROLL-2' })).toBeNull()
    expect(screen.getAllByRole('row')).toHaveLength(2)
  })
})

describe('ST-01 states — standard, never a silent legacy fallback', () => {
  it('says plainly when nothing matches, with or without filters', async () => {
    api.page.mockResolvedValueOnce(ready([]))
    const first = mount()
    expect(await screen.findByText('Bu şirkette eşleşen fiziksel stok yok.')).toBeTruthy()
    first.unmount()
    api.page.mockResolvedValueOnce(ready([]))
    mount(`/stok/pozisyonlar?lokasyon=${LOCATION}`)
    expect(await screen.findByText('Filtrelere uyan fiziksel stok yok.')).toBeTruthy()
  })

  it('answers a 403 with the standard forbidden state, reports it, and reads nothing else', async () => {
    api.page.mockResolvedValue({ type: 'http', status: 403, body: { code: 'inventory_stock_position_forbidden' } })
    mount()
    const block = await screen.findByRole('alert')
    expect(block.getAttribute('data-state')).toBe('forbidden')
    expect(block.textContent).toContain('Yetkiniz yok — stok pozisyonları')
    // No identified reason is invented: the code-less 403 does not say which permission was missing.
    expect(main().textContent).not.toMatch(/Ürün[^]*yetki/)
    expect(auth.report).toHaveBeenCalledWith(403, { token: 1 })
    expect(api.page).toHaveBeenCalledTimes(1)
    expect(api.legacy).not.toHaveBeenCalled()
  })

  it('answers a 503 (e.g. an unconfigured or synthetic Product source) as unavailable; retry repeats the same read', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValueOnce({ type: 'http', status: 503, body: { code: 'inventory_stock_position_unavailable' } })
    mount(ALL_FILTERS_URL)
    const block = await screen.findByRole('alert')
    expect(block.getAttribute('data-state')).toBe('service-unavailable')
    expect(api.legacy).not.toHaveBeenCalled()
    await user.click(within(block).getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('link', { name: 'ROLL-1' })).toBeTruthy()
    expect(api.page).toHaveBeenLastCalledWith(COMPANY, ALL_FILTERS, null, expect.any(AbortSignal))
    expect(api.page).toHaveBeenCalledTimes(2)
    expect(api.legacy).not.toHaveBeenCalled()
  })

  it('offers a fresh start after a transient 409 read conflict', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValueOnce({ type: 'http', status: 409, body: { code: 'inventory_stock_position_read_conflict' } })
    mount()
    expect((await screen.findByRole('alert')).getAttribute('data-state')).toBe('conflict')
    await user.click(screen.getByRole('button', { name: 'Listeyi baştan yükle' }))
    expect(await screen.findByRole('link', { name: 'ROLL-1' })).toBeTruthy()
    expect(api.page).toHaveBeenCalledTimes(2)
  })

  it('refuses a response it cannot verify', async () => {
    api.page.mockResolvedValue({ type: 'malformed' })
    mount()
    expect((await screen.findByRole('alert')).getAttribute('data-state')).toBe('malformed-response')
  })

  it('reads nothing outside a single authorised company', async () => {
    shell.scopeMode = 'group'
    mount()
    expect((await screen.findByRole('alert')).textContent).toBe('Yetkili tek şirket bağlamı seçilmeden stok pozisyonları görüntülenemez.')
    expect(api.page).not.toHaveBeenCalled()
  })

  it('reads once per company and keeps a search in flight when the auth boundary is a new object every render', async () => {
    // R19 r14 run 36317283526: the real boundary is a fresh object per render. Keyed on it, the read effect re-ran
    // after every render and its cleanup aborted the search just submitted, so the search never answered.
    auth.freshPerRender = true
    const user = userEvent.setup()
    const signals: AbortSignal[] = []
    api.page.mockImplementation(async (_company: string, filters: StockPositionFilters, _cursor: string | null, signal: AbortSignal) => {
      signals.push(signal)
      return ready([filters.q === 'ROLL' ? row({ labelId: LABEL_2, labelCode: 'ROLL-2' }) : row()])
    })
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(api.page).toHaveBeenCalledTimes(1)

    await user.type(screen.getByLabelText('Etiket kodu'), 'ROLL')
    expect(api.page).toHaveBeenCalledTimes(1)
    await user.click(screen.getByRole('button', { name: 'Ara' }))

    expect(await screen.findByRole('link', { name: 'ROLL-2' })).toBeTruthy()
    expect(api.page).toHaveBeenCalledTimes(2)
    expect(signals[1].aborted).toBe(false)
    expect((screen.getByLabelText('Etiket kodu') as HTMLInputElement).value).toBe('ROLL')
  })
})

describe('ST-01 on a phone', () => {
  it('draws one readable card per row with Product, size, location, owner and the four quantities', async () => {
    stubViewport(true)
    api.page.mockResolvedValue(ready([row(), row({ labelId: LABEL_2, labelCode: 'ROLL-2', classification: 'Scrap' })]))
    mount()
    const cards = await screen.findByTestId('stock-position-cards')
    expect(screen.queryByRole('table')).toBeNull()
    const items = within(cards).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    const facts = (item: HTMLElement) => within(item).getAllByRole('term').map((term) => [term.textContent, term.nextElementSibling?.textContent])
    expect(facts(items[0])).toEqual([
      ['Ürün', 'P-1Galvaniz bant'],
      ['Ölçü (mm)', '1,5 × 1.250'],
      ['Lokasyon', '—'],
      ['Mal sahibi', 'Şirket malı'],
      ['Fiziksel kg', '10'],
      ['Bloklu kg', '2'],
      ['Tahsisli kg', '1'],
      ['Kullanılabilir kg', '7'],
    ])
    expect(facts(items[1]).at(-1)).toEqual(['Kullanılabilir kg', '7Hurda'])
    // One DOM per row: no hidden duplicate table rows or menus.
    expect(screen.getAllByRole('link', { name: 'ROLL-1' })).toHaveLength(1)
  })

  it('switches between table and cards when the window crosses the phone width', async () => {
    const viewport = stubViewport(false)
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(screen.getByRole('table')).toBeTruthy()
    viewport.set(true)
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.getByTestId('stock-position-cards')).toBeTruthy()
    viewport.set(false)
    expect(screen.getByRole('table')).toBeTruthy()
    expect(api.page).toHaveBeenCalledTimes(1)
  })
})

/**
 * STOCK-DS-01: the same page on the GOY-DS-001 primitives. Only the presentation moved — the facts, their order,
 * the read, the URL and the row actions are the ones every case above already pins.
 */
describe('ST-01 on GOY-DS-001 (STOCK-DS-01)', () => {
  it('draws the rows in the shared DataTable: label pinned at the start, Kullanılabilir and İşlemler at the end', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY]
    stubViewport(false)
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    const table = screen.getByRole('table', { name: 'Stok / Malzeme Pozisyonu' })
    expect(table.parentElement!.classList.contains(tableStyles.scroll)).toBe(true)
    const header = (name: string) => screen.getByRole('columnheader', { name })
    expect(header('Etiket / lot').classList.contains(tableStyles.pinStart)).toBe(true)
    expect(header('Kullanılabilir kg').classList.contains(tableStyles.pinEnd)).toBe(true)
    expect(header('İşlemler').classList.contains(tableStyles.pinEnd)).toBe(true)
    // Kullanılabilir sits left of the pinned actions column; the actions column sits on the edge.
    expect(header('Kullanılabilir kg').style.right).toBe(header('İşlemler').style.width)
    expect(header('İşlemler').style.right).toBe('0px')
    for (const name of ['Fiziksel kg', 'Bloklu kg', 'Tahsisli kg', 'Kullanılabilir kg']) {
      expect(header(name).classList.contains(tableStyles.end), name).toBe(true)
    }
    for (const name of ['Ürün', 'Ölçü (mm)', 'Lokasyon', 'Mal sahibi', 'Fiziksel kg', 'Bloklu kg', 'Tahsisli kg']) {
      expect(header(name).classList.contains(tableStyles.pinStart) || header(name).classList.contains(tableStyles.pinEnd), name).toBe(false)
    }
    // The row's menu lives in the pinned actions cell, and the row keeps its label id for the browser specs.
    const tr = bodyRow('ROLL-1')
    expect(tr.getAttribute('data-label-id')).toBe(LABEL)
    const actionsCell = within(tr).getAllByRole('cell').at(-1)!
    expect(within(actionsCell).getByRole('button', { name: 'ROLL-1 işlemleri' })).toBeTruthy()
  })

  it('shows a long real Product name, a long code and long quantities whole: visible wrapping, no cut, no rounding', async () => {
    const LONG_NAME = 'Sıcak haddelenmiş dekapajlı yağlı rulo sac S235JR+AR EN 10025-2 kenarı kesilmiş, Çağlayan Öğütücü İşçiliği'
    const LONG_CODE = 'SAC-DKP-S235JR-1,50X1250-HRC-2026-ÖZEL'
    stubViewport(false)
    api.page.mockResolvedValue(ready([row({
      labelCode: 'ETK-2026-0001234567',
      physicalKg: '123456789.125', blockedPhysicalKg: '12345678.5', freePhysicalKg: '111111110.625',
      activeAllocationKg: '98765432.25', availableKg: '12345678.375',
      productDisplay: { ...row().productDisplay, identity: { productId: PRODUCT, productVersion: '7', code: LONG_CODE, name: LONG_NAME } },
    })]))
    mount()
    const tr = (await screen.findByRole('link', { name: 'ETK-2026-0001234567' })).closest('tr')!
    const name = within(tr).getByText(LONG_NAME)
    // The whole name is the element's own visible text — not a shortened text with the rest in a hover title.
    expect(name.textContent).toBe(LONG_NAME)
    expect(name.getAttribute('title')).toBeNull()
    expect(within(tr).getByText(LONG_CODE).textContent).toBe(LONG_CODE)
    expect(cellTexts(tr).slice(5, 9)).toEqual(['123.456.789,125', '12.345.678,5', '98.765.432,25', '12.345.678,375'])

    // The stylesheet the browser draws with: the name and the label code wrap, nothing in a cell is clipped to one
    // line, and a quantity never wraps. (Visible-in-a-browser evidence is the STOCK-DS-01 P1 probe.)
    const nodeProcess = (globalThis as unknown as {
      readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
    }).process
    const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
    // Vitest runs from the web package (`npm test` in src/frontend/goymen-erp-web).
    const css = fs.readFileSync(`${nodeProcess.cwd()}/src/inventory/stockPosition.module.css`, 'utf8')
    const rule = (selector: string) => {
      const match = new RegExp(`(^|\\n)${selector.replace('.', '\\.')} \\{([^}]*)\\}`).exec(css)
      expect(match, selector).not.toBeNull()
      return match![2]
    }
    expect(css).not.toMatch(/text-overflow:\s*ellipsis/)
    expect(css).not.toMatch(/line-clamp/)
    expect(rule('.name')).not.toMatch(/white-space:\s*nowrap|overflow:\s*hidden/)
    expect(rule('.name')).toMatch(/overflow-wrap:\s*break-word/)
    expect(rule('.labelLink')).toMatch(/overflow-wrap:\s*anywhere/)
    expect(rule('.labelCol')).not.toMatch(/nowrap/)
    expect(rule('.sub')).not.toMatch(/nowrap/)
  })

  it('keeps Kullanılabilir the only pinned quantity when Blokajsız fiziksel is shown', async () => {
    const user = userEvent.setup()
    stubViewport(false)
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    await user.click(screen.getByRole('button', { name: 'Sütunlar' }))
    await user.click(screen.getByRole('checkbox', { name: 'Blokajsız fiziksel kg sütunu' }))
    const free = screen.getByRole('columnheader', { name: 'Blokajsız fiziksel kg' })
    expect(free.classList.contains(tableStyles.end)).toBe(true)
    expect(free.classList.contains(tableStyles.pinEnd)).toBe(false)
    expect(screen.getByRole('columnheader', { name: 'Kullanılabilir kg' }).classList.contains(tableStyles.pinEnd)).toBe(true)
    expect(screen.getAllByRole('table')).toHaveLength(1)
  })

  it('builds the toolbar from the design-system controls without changing what they do', async () => {
    const user = userEvent.setup()
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    const search = screen.getByRole('search')
    const input = within(search).getByLabelText('Etiket kodu') as HTMLInputElement
    expect(input.getAttribute('placeholder')).toBe('Etiket kodu')
    expect(input.maxLength).toBe(64)
    expect(within(search).getByRole('button', { name: 'Ara' }).getAttribute('type')).toBe('submit')
    expect(screen.getAllByRole('button', { name: 'Ara' })).toHaveLength(1)

    const filters = screen.getByRole('button', { name: 'Diğer filtreler' })
    const columns = screen.getByRole('button', { name: 'Sütunlar' })
    for (const button of [filters, columns]) {
      expect(button.getAttribute('type')).toBe('button')
      expect(button.getAttribute('aria-expanded')).toBe('false')
    }
    expect(filters.getAttribute('aria-controls')).toBe('stock-other-filters')
    expect(columns.getAttribute('aria-controls')).toBe('stock-columns')
    await user.click(filters)
    expect(filters.getAttribute('aria-expanded')).toBe('true')
    const select = screen.getByRole('combobox', { name: 'Sınıf' }) as HTMLSelectElement
    expect(select.id).toBe('stock-classification-filter')
    expect([...select.options].map((option) => option.textContent)).toEqual(['Tümü', 'Standart', 'İkinci kalite', 'Hurda'])
    // Opening a panel reads nothing.
    expect(api.page).toHaveBeenCalledTimes(1)
  })

  it('keeps "Daha fazla göster" and "Filtreleri temizle" plain buttons that submit nothing', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValue(ready([row()], { nextCursor: 'opaque-1' }))
    mount('/stok/pozisyonlar?etiket=ROLL')
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(screen.getByRole('button', { name: 'Daha fazla göster' }).getAttribute('type')).toBe('button')
    const clear = screen.getByRole('button', { name: 'Filtreleri temizle' })
    expect(clear.getAttribute('type')).toBe('button')
    await user.click(clear)
    await waitFor(() => expect(address()).toBe('/stok/pozisyonlar'))
    expect(api.page).toHaveBeenLastCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal))
  })

  it('keeps the phone card free of a table, with the menu in its head', async () => {
    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY]
    stubViewport(true)
    mount()
    const cards = await screen.findByTestId('stock-position-cards')
    expect(screen.queryByRole('table')).toBeNull()
    const card = within(cards).getAllByRole('listitem')[0]
    expect(within(card).getByRole('button', { name: 'ROLL-1 işlemleri' })).toBeTruthy()
    expect(within(card).getByRole('link', { name: 'ROLL-1' })).toBeTruthy()
  })
})

/**
 * The real shell around ST-01 (#606 integration regression; #612 composite ST-01 capability).
 *
 * Every case above draws the page alone over a recording boundary, so they can only show that the
 * page REPORTS a 401/403. These cases mount the real `ShellProvider`, router (`shellRoutes`),
 * `CapabilityRoute`, reducer and `useAuthBoundary`, and read through the real `fetchStockDisplayPage`
 * over a stubbed `fetch`: what is asserted is what the shell does with the report. The `fetch` stub
 * stands in for the server, so this is no HTTP/SQL/browser evidence, and it does not prove how the
 * server composes the ST-01 capability (#612 backend acceptance owns that).
 */
describe('ST-01 inside the real shell: capability route, auth boundary, no legacy read', () => {
  const ST01 = CAPABILITY.inventoryStockPositionsList
  const OTHER = CAPABILITY.productItemsList
  const OTHER_LABEL = 'Ürünler'
  const ST01_LABEL = 'Stok / Malzeme Pozisyonu'
  const USER = '11111111-1111-4111-8111-111111111111'

  interface Server {
    capabilities: string[]
    generation: string
    identityStatus: number
    positions: (url: URL) => Response
  }
  const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
  const positionCalls: { url: string; optIn: string | null }[] = []

  function installServer(initial: Partial<Server>): Server {
    const server: Server = {
      capabilities: [ST01, OTHER], generation: 'gen-1', identityStatus: 200,
      positions: () => json(ready([row()]).data), ...initial,
    }
    positionCalls.length = 0
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      if (url.pathname.endsWith('/identity/me')) {
        return server.identityStatus === 200
          ? json({ userId: USER, displayName: 'Kullanıcı A', email: null, allowedCompanyIds: [COMPANY] })
          : json({ code: 'unauthenticated' }, server.identityStatus)
      }
      if (url.pathname.endsWith('/organization/companies')) {
        return json([{ id: COMPANY, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false }])
      }
      if (url.pathname.endsWith('/shell/capabilities')) {
        return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: server.generation, capabilities: server.capabilities })
      }
      if (url.pathname.endsWith('/inventory/positions')) {
        positionCalls.push({ url: url.toString(), optIn: new Headers(init?.headers).get(STOCK_DISPLAY_HEADER) })
        return server.positions(url)
      }
      return json({ code: 'unavailable' }, 503)
    }))
    return server
  }

  async function mountShell(path = '/stok/pozisyonlar') {
    const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000 } } })
    render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
    await screen.findByText('Kullanıcı A', undefined, { timeout: 5000 })
    return router
  }

  const nav = () => screen.queryByRole('navigation', { name: 'Modül navigasyonu' })
  const navLink = (name: string) => {
    const region = nav()
    return region ? within(region).queryByRole('link', { name }) : null
  }
  const stockHeading = () => screen.queryByRole('heading', { level: 1, name: ST01_LABEL })
  const notFound = () => document.querySelector('[data-state="not-found"]')
  /** No read of this screen may go out without the #597 opt-in, and the legacy reader is never called. */
  function expectNoLegacyRead() {
    expect(api.legacy).not.toHaveBeenCalled()
    expect(positionCalls.every((call) => call.optIn === '1.0')).toBe(true)
  }

  beforeEach(() => {
    mode.real = true
    try { window.localStorage.clear() } catch { /* ignore */ }
    vi.stubGlobal('navigator', { onLine: true })
    stubViewport(false)
  })

  it('without ST-01 (an Inventory-only snapshot) the route is neutral, the entry is absent and nothing is read', async () => {
    installServer({ capabilities: [OTHER] })
    await mountShell()

    await waitFor(() => expect(notFound()).not.toBeNull())
    expect(stockHeading()).toBeNull()
    expect(navLink(ST01_LABEL)).toBeNull()
    expect(navLink(OTHER_LABEL)).not.toBeNull()
    expect(positionCalls).toEqual([])
    expectNoLegacyRead()
  })

  it('a current generic 403 clears the snapshot: ST-01 and its stale row unmount; a fresh snapshot brings back only what it grants', async () => {
    const server = installServer({})
    const router = await mountShell()
    expect(await screen.findByRole('link', { name: 'ROLL-1' }, { timeout: 5000 })).toBeTruthy()
    expect(navLink(ST01_LABEL)).not.toBeNull()

    server.capabilities = [OTHER]
    server.generation = 'gen-2'
    server.positions = () => json({ code: 'forbidden' }, 403)
    await userEvent.type(screen.getByLabelText('Etiket kodu'), 'ROLL{Enter}')

    await waitFor(() => expect(notFound()).not.toBeNull(), { timeout: 5000 })
    expect(screen.queryByRole('link', { name: 'ROLL-1' })).toBeNull()
    expect(stockHeading()).toBeNull()
    // The revocation clears the whole snapshot: no entry is assumed to survive it.
    expect(navLink(ST01_LABEL)).toBeNull()
    expect(navLink(OTHER_LABEL)).toBeNull()

    // The next route entry verifies a FRESH snapshot; only what it grants comes back.
    await act(async () => { await router.navigate('/urun') })
    await waitFor(() => expect(navLink(OTHER_LABEL)).not.toBeNull(), { timeout: 5000 })
    expect(navLink(ST01_LABEL)).toBeNull()
    expect(positionCalls.length).toBe(2)
    expectNoLegacyRead()
  })

  it('a generic 403 on the next page does not keep the earlier rows under the shell', async () => {
    const server = installServer({ positions: () => json(ready([row()], { nextCursor: 'cursor-1' }).data) })
    await mountShell()
    expect(await screen.findByRole('link', { name: 'ROLL-1' }, { timeout: 5000 })).toBeTruthy()

    server.capabilities = [OTHER]
    server.generation = 'gen-2'
    server.positions = () => json({ code: 'forbidden' }, 403)
    await userEvent.click(screen.getByRole('button', { name: 'Daha fazla göster' }))

    await waitFor(() => expect(notFound()).not.toBeNull(), { timeout: 5000 })
    expect(screen.queryByRole('link', { name: 'ROLL-1' })).toBeNull()
    expect(stockHeading()).toBeNull()
    expect(positionCalls.at(-1)?.url).toContain('cursor=cursor-1')
    expectNoLegacyRead()
  })

  it('a 401 resets the session through the real boundary: the stock content is gone and nothing falls back', async () => {
    const server = installServer({})
    await mountShell()
    expect(await screen.findByRole('link', { name: 'ROLL-1' }, { timeout: 5000 })).toBeTruthy()

    server.identityStatus = 401
    server.positions = () => json({ code: 'unauthenticated' }, 401)
    await userEvent.type(screen.getByLabelText('Etiket kodu'), 'ROLL{Enter}')

    await waitFor(() => expect(screen.queryByRole('link', { name: 'ROLL-1' })).toBeNull(), { timeout: 5000 })
    expect(stockHeading()).toBeNull()
    expect(nav()).toBeNull()
    expectNoLegacyRead()
  })

  it('a 503 keeps the snapshot: the page stays mounted with its retryable state and the entry stays', async () => {
    const server = installServer({})
    await mountShell()
    expect(await screen.findByRole('link', { name: 'ROLL-1' }, { timeout: 5000 })).toBeTruthy()

    server.positions = () => json({ code: 'unavailable' }, 503)
    await userEvent.type(screen.getByLabelText('Etiket kodu'), 'ROLL{Enter}')

    await waitFor(() => expect(document.querySelector('[data-state="service-unavailable"]')).not.toBeNull(), { timeout: 5000 })
    expect(stockHeading()).not.toBeNull()
    expect(notFound()).toBeNull()
    expect(navLink(ST01_LABEL)).not.toBeNull()
    expectNoLegacyRead()
  })
})
