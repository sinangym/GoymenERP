import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryShortageWarningPage } from './InventoryShortageWarningPage'
import { INVENTORY_SHORTAGE_WARNINGS_ROUTE } from './capabilities'
import { SHORTAGE_WARNING_INVALID_QUERY } from './shortageWarningApi'
import type { ShortageProduct, ShortageWarnings } from './shortageWarningContract'
import type { ViewOutcome } from '../shell/uiState'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const PRODUCT = '44444444-4444-4444-8444-444444444444'
const PRODUCT_2 = '46464646-4646-4646-8646-464646464646'
const SIMILAR = '45454545-4545-4545-8545-454545454545'
const ORDER = '55555555-5555-4555-8555-555555555555'
const LINE = '66666666-6666-4666-8666-666666666666'
const P1_TITLE = 'SENTETIK-P1 · Sentetik ürün'

const api = vi.hoisted(() => ({ warnings: vi.fn() }))
const scenario = vi.hoisted(() => ({
  companyId: '11111111-1111-4111-8111-111111111111', namespace: 'SENTETIK-a' as string | null, report: vi.fn(),
}))
vi.mock('./shortageWarningApi', async (importOriginal) => ({
  ...await importOriginal<typeof import('./shortageWarningApi')>(),
  fetchInventoryShortageWarnings: api.warnings,
}))
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: 'actor-a', companyId: scenario.companyId, scopeMode: 'single', capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({
  contextKey: () => scenario.namespace === null ? null : [scenario.namespace],
  contextKeyString: (key: readonly string[]) => key[0],
  hasCapability: () => true,
}))
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ namespace: scenario.namespace }), report: scenario.report }),
}))

const SHORTAGE: ShortageProduct = {
  productId: PRODUCT, productCode: 'SENTETIK-P1', productName: 'Sentetik ürün', status: 'Shortage',
  openDemandKg: '300', reservedKg: '0', netDemandKg: '300', freeStockKg: '100', openSupplyKg: '150', shortageKg: '50',
  earliestDueDate: '2026-11-20',
  lines: [{
    orderId: ORDER, documentNo: 'SENTETIK-SO-1', lineId: LINE, lineNo: 1, dueDate: '2026-11-20', openKg: '300',
    reservedKg: '0', fromStockKg: '100', fromSupplyKg: '150', uncoveredKg: '50', coverage: 'Uncovered',
    latestSupplyDate: null,
  }],
  similarStock: { status: 'NotConfigured', items: [] },
}

const UNCONVERTIBLE: ShortageProduct = {
  productId: PRODUCT_2, productCode: 'SENTETIK-P2', productName: 'Adetli ürün', status: 'Unconvertible',
  openDemandKg: null, reservedKg: null, netDemandKg: null, freeStockKg: null, openSupplyKg: null, shortageKg: null,
  earliestDueDate: null, lines: [],
  similarStock: {
    status: 'Answered',
    items: [{ productId: SIMILAR, productCode: 'SENTETIK-P3', productName: 'Benzer ürün', freeStockKg: '40' }],
  },
}

function ok(items: readonly ShortageProduct[], supplyDates: ShortageWarnings['supplyDates'] = 'NotAnswered')
  : ViewOutcome<ShortageWarnings> {
  return { type: 'ok-single', data: { companyId: COMPANY, supplyDates, items } }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

function mount() {
  return render(<MemoryRouter initialEntries={[INVENTORY_SHORTAGE_WARNINGS_ROUTE]}>
    <Routes>
      <Route path={INVENTORY_SHORTAGE_WARNINGS_ROUTE} element={<InventoryShortageWarningPage />} />
    </Routes>
  </MemoryRouter>)
}

beforeEach(() => {
  scenario.companyId = COMPANY; scenario.namespace = 'SENTETIK-a'
  scenario.report.mockReset()
  api.warnings.mockReset().mockResolvedValue(ok([SHORTAGE]))
})
afterEach(() => cleanup())

describe('InventoryShortageWarningPage', () => {
  it('hesaplayıcının eksik uyarısını olduğu gibi gösterir ve hiçbir talep oluşturmaz', async () => {
    mount()

    expect(await screen.findByRole('heading', { level: 1, name: 'Tarihli eksik uyarıları' })).not.toBeNull()
    const card = (await screen.findByText(P1_TITLE)).closest('li')!
    expect(api.warnings).toHaveBeenCalledWith(COMPANY, { onlyWarnings: true }, expect.any(AbortSignal))
    expect(card.getAttribute('data-status')).toBe('Shortage')
    expect(within(card).getByText('Eksik var')).not.toBeNull()
    expect(within(card).getAllByText('50 kg')).toHaveLength(2)
    const row = within(card).getAllByRole('row').find((candidate) => candidate.getAttribute('data-line-id') === LINE)!
    const cells = within(row).getAllByRole('cell').map((cell) => cell.textContent)
    expect(cells).toEqual(['SENTETIK-SO-1 / 1', '2026-11-20', '300 kg', '0 kg', '100 kg', '150 kg', '50 kg',
      'Karşılanmıyor', '—'])
    expect(screen.getByText(/Üretim emirleri hesaba katılmaz/)).not.toBeNull()
    expect(screen.getByText(/otomatik satın alma talebi/)).not.toBeNull()
    // The screen is read-only: no command button besides the filter toggle.
    expect(screen.queryAllByRole('button')).toEqual([])
    expect(within(card).getByText('Benzer ürün kuralı henüz tanımlı değil.')).not.toBeNull()
  })

  it('satın alma tarihleri yanıtlanmadığında bunu açıkça söyler', async () => {
    mount()
    await screen.findByText(P1_TITLE)
    expect(screen.getByRole('note').textContent).toContain('Satın alma teslim tarihleri henüz okunamıyor')
  })

  it('satın alma tarihleri yanıtlandığında uyarı bandını göstermez', async () => {
    api.warnings.mockResolvedValue(ok([SHORTAGE], 'Answered'))
    mount()
    await screen.findByText(P1_TITLE)
    expect(screen.queryByRole('note')).toBeNull()
  })

  it('birimi kilogram olmayan ürünü rakamsız gösterir ve benzer ürün stokunu yalnız bilgi olarak listeler', async () => {
    api.warnings.mockResolvedValue(ok([UNCONVERTIBLE]))
    mount()
    const card = (await screen.findByText('SENTETIK-P2 · Adetli ürün')).closest('li')!
    expect(within(card).getByText(/kilogram cinsinden değil/)).not.toBeNull()
    expect(within(card).queryByRole('row', { name: /SENTETIK-SO/ })).toBeNull()
    const similar = within(card).getByRole('region', { name: /Benzer ürün stoku/ })
    expect(within(similar).getByText('SENTETIK-P3 · Benzer ürün')).not.toBeNull()
    expect(within(similar).getByText('40 kg')).not.toBeNull()
  })

  it('uyarı yoksa filtreli boş durumu, tüm ürünler istendiğinde veri yok durumunu ayırır', async () => {
    const user = userEvent.setup()
    api.warnings.mockResolvedValue(ok([]))
    mount()
    expect((await screen.findByText(/Uyarı gerektiren ürün yok/)).getAttribute('data-state')).toBe('empty-filtered')

    await user.click(screen.getByRole('checkbox', { name: 'Karşılanan ürünleri de göster' }))
    expect(api.warnings).toHaveBeenLastCalledWith(COMPANY, { onlyWarnings: false }, expect.any(AbortSignal))
    expect((await screen.findByText('Açık satış siparişi olan ürün yok.')).getAttribute('data-state')).toBe('empty-no-data')
  })

  it('503 yanıtında hiçbir rakam göstermez ve yeniden denemeyi sunar', async () => {
    const user = userEvent.setup()
    api.warnings.mockResolvedValueOnce({ type: 'http', status: 503, body: { code: 'inventory_shortage_warning_unavailable' } })
    mount()
    const alert = await screen.findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('service-unavailable')
    expect(screen.queryByText(/SENTETIK-P1/)).toBeNull()

    await user.click(within(alert).getByRole('button'))
    expect(await screen.findByText(P1_TITLE)).not.toBeNull()
    expect(api.warnings).toHaveBeenCalledTimes(2)
  })

  it('401 ve 403 yanıtını yetki sınırına bildirir', async () => {
    api.warnings.mockResolvedValue({ type: 'http', status: 403, body: null })
    mount()
    expect((await screen.findByRole('alert')).getAttribute('data-state')).toBe('forbidden')
    expect(scenario.report).toHaveBeenCalledWith(403, { namespace: 'SENTETIK-a' })
  })

  it('yalnız sahibin kapalı kodu geçersiz istek sayılır; başka 400 güvenli biçimde durur', async () => {
    api.warnings.mockResolvedValue({ type: 'http', status: 400, body: { code: SHORTAGE_WARNING_INVALID_QUERY } })
    const first = mount()
    expect((await screen.findByRole('alert')).textContent).toContain('Eksik uyarıları isteği geçersiz bulundu')
    first.unmount()

    api.warnings.mockResolvedValue({ type: 'http', status: 400, body: { code: 'other' } })
    mount()
    expect((await screen.findByRole('alert')).getAttribute('data-state')).toBe('malformed-response')
  })

  it('bozuk yanıtı veri olarak göstermez', async () => {
    api.warnings.mockResolvedValue({ type: 'malformed' })
    mount()
    expect((await screen.findByRole('alert')).getAttribute('data-state')).toBe('malformed-response')
  })

  it('filtre değişince eski yanıtı ekrana uygulamaz', async () => {
    const user = userEvent.setup()
    const late = deferred<ViewOutcome<ShortageWarnings>>()
    api.warnings.mockReturnValueOnce(late.promise).mockResolvedValueOnce(ok([], 'Answered'))
    mount()
    await waitFor(() => expect(api.warnings).toHaveBeenCalledTimes(1))
    // The first read is still pending: the filter cannot change until it answers.
    expect((screen.getByRole('checkbox') as HTMLInputElement).disabled).toBe(true)
    late.resolve(ok([SHORTAGE]))
    await screen.findByText(P1_TITLE)

    await user.click(screen.getByRole('checkbox'))
    expect(await screen.findByText('Açık satış siparişi olan ürün yok.')).not.toBeNull()
    expect(screen.queryByText(/SENTETIK-P1/)).toBeNull()
  })

  it('tek şirket bağlamı yoksa okuma yapmaz', async () => {
    scenario.namespace = null
    mount()
    expect(screen.getByRole('alert').textContent).toContain('Yetkili tek şirket bağlamı seçilmeden')
    expect(api.warnings).not.toHaveBeenCalled()
  })
})
