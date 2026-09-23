import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { InventoryOpeningCreatePage } from './InventoryOpeningCreatePage'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const ACTOR = 'actor-a'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const SITE = '44444444-4444-4444-8444-444444444444'
const PRODUCT = '88888888-8888-4888-8888-888888888888'
const UNIT = '99999999-9999-4999-8999-999999999999'
const PROFILE = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'

const api = vi.hoisted(() => ({ locations: vi.fn(), products: vi.fn(), record: vi.fn() }))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return {
    ...actual,
    fetchOpeningLocations: api.locations,
    fetchOpeningProducts: api.products,
    recordOpeningCount: api.record,
  }
})
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({
    sessionEpoch: 1, routeEntryGeneration: 1,
    activeContext: { userId: ACTOR, companyId: COMPANY, scopeMode: 'single', capabilities: [] },
  }),
}))
vi.mock('../shell/shellState', () => ({ contextKey: () => ['fixture'], contextKeyString: () => 'fixture' }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: vi.fn() }) }))

beforeEach(() => {
  sessionStorage.clear()
  api.locations.mockReset().mockResolvedValue({
    type: 'ok-single', data: {
      items: [{
        locationId: LOCATION, locationCode: 'A-01', locationName: 'Ana depo',
        siteId: SITE, siteCode: 'TESIS-1', siteName: 'Merkez',
      }],
      searchWindowFull: false,
    },
  })
  api.products.mockReset().mockResolvedValue({
    type: 'ok-single', data: {
      items: [{
        productId: PRODUCT, productVersion: '7', code: 'PRD-1', name: 'Galvaniz bant',
        stockUnitId: UNIT, stockUnitCode: 'kg', profileId: PROFILE, profileVersion: '3',
      }],
      searchWindowFull: false,
    },
  })
  api.record.mockReset().mockImplementation(async (payload: import('./contract').OpeningCountRequest) => ({
    type: 'applied', replayed: false,
    response: {
      countId: payload.countId, companyId: COMPANY, locationId: payload.locationId,
      countedOn: payload.countedOn, sheetReference: payload.sheetReference,
      lines: payload.lines.map((line) => ({
        lineId: line.lineId, labelId: line.labelId, labelCode: line.labelCode, lotId: line.lotId,
        heatNumber: line.heatNumber, coilNumber: line.coilNumber, productId: line.productId,
        quantityKg: line.quantityKg,
      })),
    },
  }))
})

async function fillOpening(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Sayım tarihi'), '2026-09-23')
  await user.type(screen.getByLabelText('Belge / sayım referansı'), 'SAYIM-1')
  await user.click(screen.getByRole('button', { name: 'Lokasyonları getir' }))
  await user.selectOptions(await screen.findByLabelText('Lokasyon'), LOCATION)
  await user.click(screen.getByRole('button', { name: 'Ürünleri getir' }))
  await user.selectOptions(await screen.findByLabelText('Ürün'), PRODUCT)
  await user.type(screen.getByLabelText('Etiket kodu'), 'LBL-1')
  await user.type(screen.getByLabelText('Sayım kg'), '12,5')
  await user.type(screen.getByLabelText('Rulo no (opsiyonel)'), 'COIL-1')
  await user.click(screen.getByRole('button', { name: 'Açılış sayımını kaydet' }))
}

describe('Inventory opening create', () => {
  it('uses real candidate identities and preserves the stored record for reopen', async () => {
    const user = userEvent.setup()
    render(<MemoryRouter><InventoryOpeningCreatePage /></MemoryRouter>)

    await fillOpening(user)

    await screen.findByRole('heading', { name: 'Açılış sayımı kaydedildi' })
    await waitFor(() => expect(api.record).toHaveBeenCalledTimes(1))
    const payload = api.record.mock.calls[0][0]
    expect(payload).toMatchObject({
      locationId: LOCATION, countedOn: '2026-09-23', sheetReference: 'SAYIM-1',
      lines: [{ productId: PRODUCT, productVersion: '7', labelCode: 'LBL-1', quantityKg: '12.5', coilNumber: 'COIL-1' }],
    })
    expect(screen.getByRole('link', { name: 'Aynı kaydı aç' }).getAttribute('href')).toContain(payload.countId)
    expect(sessionStorage.getItem(`goymen.inventory.opening-last.v1:1:${ACTOR}:${COMPANY}`)).toContain(payload.countId)
  })

  it('retries an unknown result with the same payload and idempotency key', async () => {
    const applied = api.record.getMockImplementation()!
    api.record.mockReset()
      .mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
      .mockResolvedValueOnce({ type: 'failed', status: 403, code: 'forbidden', replayed: false })
      .mockImplementationOnce(applied)
    const user = userEvent.setup()
    render(<MemoryRouter><InventoryOpeningCreatePage /></MemoryRouter>)

    await fillOpening(user)
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await screen.findByText(/Önceki gönderimin sonucu hâlâ bilinmiyor/)
    await user.click(screen.getByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await screen.findByRole('heading', { name: 'Açılış sayımı kaydedildi' })

    expect(api.record).toHaveBeenCalledTimes(3)
    expect(api.record.mock.calls[1][0]).toEqual(api.record.mock.calls[0][0])
    expect(api.record.mock.calls[2][0]).toEqual(api.record.mock.calls[0][0])
    expect(api.record.mock.calls[1][1].idempotencyKey).toBe(api.record.mock.calls[0][1].idempotencyKey)
    expect(api.record.mock.calls[2][1].idempotencyKey).toBe(api.record.mock.calls[0][1].idempotencyKey)
  })
})
