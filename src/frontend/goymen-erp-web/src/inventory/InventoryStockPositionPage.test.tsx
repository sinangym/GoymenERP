import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryStockPositionPage } from './InventoryStockPositionPage'
import { CAPABILITY } from '../shell/capabilities'
import type { StockPositionFilters } from './stockDisplayApi'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const GROUP_COMPANY = '22222222-2222-4222-8222-2222222222bb'
const COUNTERPARTY = '99999999-9999-4999-8999-999999999999'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const LABEL = '66666666-6666-4666-8666-666666666666'
const LABEL_2 = '66666666-6666-4666-8666-666666666667'
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

vi.mock('./stockDisplayApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('./stockDisplayApi')>(),
  fetchStockDisplayPage: api.page,
}))
// The previously published read must never be used by this screen, not even as a fallback.
vi.mock('./api', async (importOriginal) => ({
  ...await importOriginal<typeof import('./api')>(),
  fetchStockPositions: api.legacy,
}))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: COMPANY, scopeMode: shell.scopeMode, capabilities: shell.capabilities },
  }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => ['fixture'],
  contextKeyString: () => 'fixture',
  hasCapability: (
    state: { readonly activeContext?: { readonly capabilities?: readonly string[] } },
    capability: string,
  ) => state.activeContext?.capabilities?.includes(capability) ?? false,
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => (auth.freshPerRender ? { capture: auth.capture, report: auth.report } : auth),
}))

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
  return { type: 'ok-single', data: { items, searchWindowFull: extra.searchWindowFull ?? false, nextCursor: extra.nextCursor ?? null } }
}

const NO_FILTERS: StockPositionFilters = { q: '', productId: null, locationId: null, classification: null, owner: null }

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

function stubPhone() {
  vi.stubGlobal('matchMedia', vi.fn((query: string) => ({
    matches: true, media: query, onchange: null,
    addEventListener: () => {}, removeEventListener: () => {}, addListener: () => {}, removeListener: () => {},
    dispatchEvent: () => false,
  })))
}

beforeEach(() => {
  shell.capabilities = []
  shell.scopeMode = 'single'
  auth.freshPerRender = false
  auth.report.mockReset()
  api.legacy.mockReset()
  api.page.mockReset().mockResolvedValue(ready([row()]))
})
afterEach(() => vi.unstubAllGlobals())

describe('ST-01 on the #597 opt-in contract', () => {
  it('draws the producer row: label, Product, nominal size, "—" location, owner and the four quantities', async () => {
    shell.capabilities = [CAPABILITY.productItemsDetail, CAPABILITY.organizationLocationsDetail]
    mount()
    const tr = (await screen.findByRole('link', { name: 'ROLL-1' })).closest('tr')!
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
      expect(screen.getByRole('main').textContent, hidden).not.toContain(hidden)
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
    expect(bodyRow('ROLL-1').textContent).not.toContain('18.450,25 ')

    await user.click(screen.getByRole('button', { name: 'Sütunlar' }))
    await user.click(screen.getByRole('checkbox', { name: 'Blokajsız fiziksel kg' }))
    expect(headerTexts().slice(5, 10)).toEqual(['Fiziksel kg', 'Bloklu kg', 'Blokajsız fiziksel kg', 'Tahsisli kg', 'Kullanılabilir kg'])
    expect(cellTexts(bodyRow('ROLL-1')).slice(5, 10)).toEqual(['18.450,251', '0,001', '18.450,25', '450,25', '18.000'])
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

  it('shows the Sınıf column on request, with the accepted words and "Hurda" only for Scrap', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValue(ready([
      row(),
      row({ labelId: LABEL_2, labelCode: 'ROLL-2', classification: 'Scrap' }),
      row({ labelId: '66666666-6666-4666-8666-666666666668', labelCode: 'ROLL-3', classification: 'SecondQuality' }),
      row({ labelId: '66666666-6666-4666-8666-666666666669', labelCode: 'ROLL-4', classification: null }),
    ]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(screen.queryByRole('columnheader', { name: 'Sınıf' })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Sütunlar' }))
    await user.click(screen.getByRole('checkbox', { name: 'Sınıf' }))
    expect(headerTexts()[3]).toBe('Sınıf')
    expect(['ROLL-1', 'ROLL-2', 'ROLL-3', 'ROLL-4'].map((code) => cellTexts(bodyRow(code))[3]))
      .toEqual(['Standart', 'Hurda', 'İkinci kalite', 'Sınıflandırılmamış'])
    expect(screen.queryByText('Hurda sınıfı')).toBeNull()
  })

  it('names ownership "Şirket malı" / "Cari malı", and "—" for another group company it cannot name', async () => {
    api.page.mockResolvedValue(ready([
      row(),
      row({ labelId: LABEL_2, labelCode: 'ROLL-2', ownerKind: 'Counterparty', ownerId: COUNTERPARTY }),
      row({ labelId: '66666666-6666-4666-8666-666666666668', labelCode: 'ROLL-3', ownerId: GROUP_COMPANY, custodianId: GROUP_COMPANY }),
    ]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(cellTexts(bodyRow('ROLL-1'))[4]).toBe('Şirket malı')
    // A counterparty's goods held by this company: the holder is drawn because it differs from the owner.
    expect(cellTexts(bodyRow('ROLL-2'))[4]).toBe('Cari malıMalı tutan: Şirket')
    expect(cellTexts(bodyRow('ROLL-3'))[4]).toBe('—')
    expect(screen.getByRole('main').textContent).not.toContain('Emanet')
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

  it('filters by Sınıf under "Diğer filtreler", with a removable chip', async () => {
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
  })

  it('filters by a row’s exact Product, location and owner — no picker, no typed or guessed id', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValue(ready([row({ ownerKind: 'Counterparty', ownerId: COUNTERPARTY })]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })

    await user.click(screen.getByRole('button', { name: 'ROLL-1: bu ürüne göre filtrele' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(
      COMPANY, { ...NO_FILTERS, productId: PRODUCT }, null, expect.any(AbortSignal)))
    await user.click(await screen.findByRole('button', { name: 'ROLL-1: bu lokasyona göre filtrele' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(
      COMPANY, { ...NO_FILTERS, productId: PRODUCT, locationId: LOCATION }, null, expect.any(AbortSignal)))
    await user.click(await screen.findByRole('button', { name: 'ROLL-1: bu mal sahibine göre filtrele' }))
    const all = { ...NO_FILTERS, productId: PRODUCT, locationId: LOCATION, owner: { kind: 'Counterparty', id: COUNTERPARTY } }
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(COMPANY, all, null, expect.any(AbortSignal)))
    expect(address()).toBe(`/stok/pozisyonlar?urun=${PRODUCT}&lokasyon=${LOCATION}&sahipTuru=cari&sahip=${COUNTERPARTY}`)

    const chips = await screen.findByRole('list', { name: 'Etkin filtreler' })
    expect(within(chips).getAllByRole('listitem').map((item) => item.firstChild?.textContent)).toEqual([
      'Ürün: P-1 Galvaniz bant', 'Lokasyon: seçili lokasyon', 'Mal sahibi: Cari malı',
    ])
    // A filter already applied is not offered again on the row.
    expect(screen.queryByRole('button', { name: 'ROLL-1: bu ürüne göre filtrele' })).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Filtreleri temizle' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal)))
    expect(address()).toBe('/stok/pozisyonlar')
  })

  it('drops a URL filter outside its grammar instead of sending it', async () => {
    mount('/stok/pozisyonlar?urun=not-a-guid&sinif=Standard&sahipTuru=cari&lokasyon=00000000-0000-0000-0000-000000000000')
    await screen.findByRole('link', { name: 'ROLL-1' })
    expect(api.page).toHaveBeenCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal))
  })
})

describe('ST-01 paging follows the server cursor', () => {
  it('appends the next page with the same filters and the server’s cursor, and resets on a filter change', async () => {
    const user = userEvent.setup()
    api.page
      .mockResolvedValueOnce(ready([row()], { searchWindowFull: true, nextCursor: 'cursor-1' }))
      .mockResolvedValueOnce(ready([row({ labelId: LABEL_2, labelCode: 'ROLL-2' })]))
      .mockResolvedValue(ready([row()]))
    mount('/stok/pozisyonlar?etiket=ROLL')
    await screen.findByRole('link', { name: 'ROLL-1' })

    await user.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    expect(await screen.findByRole('link', { name: 'ROLL-2' })).toBeTruthy()
    expect(api.page).toHaveBeenNthCalledWith(2, COMPANY, { ...NO_FILTERS, q: 'ROLL' }, 'cursor-1', expect.any(AbortSignal))
    expect(screen.getAllByRole('row')).toHaveLength(3)
    expect(screen.queryByRole('button', { name: 'Daha fazla göster' })).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Diğer filtreler' }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Sınıf' }), 'Hurda')
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(
      COMPANY, { ...NO_FILTERS, q: 'ROLL', classification: 'Scrap' }, null, expect.any(AbortSignal)))
  })

  it('keeps the rows and offers a fresh start when the next page can no longer be read (expired cursor)', async () => {
    const user = userEvent.setup()
    api.page
      .mockResolvedValueOnce(ready([row()], { searchWindowFull: true, nextCursor: 'cursor-1' }))
      .mockResolvedValueOnce({ type: 'http', status: 400, body: { code: 'inventory_stock_position_invalid_query' } })
      .mockResolvedValue(ready([row()]))
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    await user.click(screen.getByRole('button', { name: 'Daha fazla göster' }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Sonraki stok satırları okunamadı.')
    expect(screen.getByRole('link', { name: 'ROLL-1' })).toBeTruthy()
    await user.click(within(alert).getByRole('button', { name: 'Listeyi baştan yükle' }))
    await waitFor(() => expect(api.page).toHaveBeenLastCalledWith(COMPANY, NO_FILTERS, null, expect.any(AbortSignal)))
    expect(api.legacy).not.toHaveBeenCalled()
  })
})

describe('ST-01 states — standard, never a silent legacy fallback', () => {
  it('says plainly when nothing matches, with or without filters', async () => {
    api.page.mockResolvedValueOnce(ready([]))
    const first = mount()
    expect(await screen.findByText('Bu şirkette eşleşen fiziksel stok yok.')).toBeTruthy()
    first.unmount()
    api.page.mockResolvedValueOnce(ready([]))
    mount('/stok/pozisyonlar?etiket=YOK')
    expect(await screen.findByText('Filtrelere uyan fiziksel stok yok.')).toBeTruthy()
  })

  it('answers a 403 with the standard forbidden state, reports it, and reads nothing else', async () => {
    api.page.mockResolvedValue({ type: 'http', status: 403, body: { code: 'inventory_stock_position_forbidden' } })
    mount()
    const block = await screen.findByRole('alert')
    expect(block.getAttribute('data-state')).toBe('forbidden')
    expect(block.textContent).toContain('Yetkiniz yok — stok pozisyonları')
    // No identified reason is invented: the code-less 403 does not say which permission was missing.
    expect(block.textContent).not.toContain('Ürün')
    expect(auth.report).toHaveBeenCalledWith(403, { token: 1 })
    expect(api.page).toHaveBeenCalledTimes(1)
    expect(api.legacy).not.toHaveBeenCalled()
  })

  it('answers a 503 (e.g. an unconfigured or synthetic Product source) as unavailable, with a retry of the same read', async () => {
    const user = userEvent.setup()
    api.page.mockResolvedValueOnce({ type: 'http', status: 503, body: { code: 'inventory_stock_position_unavailable' } })
    mount()
    const block = await screen.findByRole('alert')
    expect(block.getAttribute('data-state')).toBe('service-unavailable')
    expect(api.legacy).not.toHaveBeenCalled()
    await user.click(within(block).getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByRole('link', { name: 'ROLL-1' })).toBeTruthy()
    expect(api.page).toHaveBeenCalledTimes(2)
    expect(api.legacy).not.toHaveBeenCalled()
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
      return ready([row()], { searchWindowFull: filters.q === 'ROLL' })
    })
    mount()
    await screen.findByRole('link', { name: 'ROLL-1' })
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(api.page).toHaveBeenCalledTimes(1)

    await user.type(screen.getByLabelText('Etiket kodu'), 'ROLL')
    expect(api.page).toHaveBeenCalledTimes(1)
    await user.click(screen.getByRole('button', { name: 'Ara' }))

    expect(await screen.findByText('Liste penceresi doldu; filtreleri daraltın.')).toBeTruthy()
    expect(api.page).toHaveBeenCalledTimes(2)
    expect(signals[1].aborted).toBe(false)
    expect((screen.getByLabelText('Etiket kodu') as HTMLInputElement).value).toBe('ROLL')
  })
})

describe('ST-01 on a phone', () => {
  it('draws one readable card per row with Product, size, location, owner and the four quantities', async () => {
    stubPhone()
    api.page.mockResolvedValue(ready([row(), row({ labelId: LABEL_2, labelCode: 'ROLL-2' })]))
    mount()
    const cards = await screen.findByTestId('stock-position-cards')
    expect(screen.queryByRole('table')).toBeNull()
    const items = within(cards).getAllByRole('listitem')
    expect(items).toHaveLength(2)
    const facts = within(items[0]).getAllByRole('term').map((term) => [term.textContent, term.nextElementSibling?.textContent])
    expect(facts).toEqual([
      ['Ürün', 'P-1Galvaniz bant'],
      ['Ölçü (mm)', '1,5 × 1.250'],
      ['Lokasyon', '—'],
      ['Mal sahibi', 'Şirket malı'],
      ['Fiziksel kg', '10'],
      ['Bloklu kg', '2'],
      ['Tahsisli kg', '1'],
      ['Kullanılabilir kg', '7'],
    ])
    // One DOM per row: no hidden duplicate table rows or menus.
    expect(screen.getAllByRole('link', { name: 'ROLL-1' })).toHaveLength(1)
  })
})
