import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseOrderCreatePage } from './PurchaseOrderCreatePage'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const PARTY = '44444444-4444-4444-8444-444444444444'
const ACCOUNT = '55555555-5555-4555-8555-555555555555'
const PRODUCT = '66666666-6666-4666-8666-666666666666'
const PRODUCT_B = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const UNIT = '77777777-7777-4777-8777-777777777777'
const ORDER = '88888888-8888-4888-8888-888888888888'

const api = vi.hoisted(() => ({ create: vi.fn(), suppliers: vi.fn(), products: vi.fn() }))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return { ...actual, createPurchaseOrder: api.create, fetchPurchasingSupplierOptions: api.suppliers, fetchPurchasingProductCandidates: api.products }
})
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: 1, activeContext: { userId: ACTOR, companyId: COMPANY, scopeMode: 'single', capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({ contextKey: () => ['fixture'], contextKeyString: () => 'fixture', hasCapability: () => true }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: vi.fn() }) }))

function applied() {
  return { type: 'applied' as const, replayed: false, response: { detail: { order: { id: ORDER, companyId: COMPANY, status: 'Draft' as const, version: '1', supplierPartyId: PARTY, companyAccountId: ACCOUNT, sourceRequestId: null, effectiveOn: '2026-09-23', createdAtUtc: '2026-09-23T10:00:00Z', updatedAtUtc: '2026-09-23T10:00:00Z', lineCount: 1 }, lines: [] }, isReplay: false, originalCorrelationId: 'corr-1' } }
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole('option', { name: 'CAR-42 — Tedarikçi AŞ' })
  await user.selectOptions(screen.getByLabelText('Tedarikçi cari hesabı'), `${PARTY}|${ACCOUNT}`)
  await user.type(screen.getByLabelText('İş tarihi'), '2026-09-23')
  await user.click(screen.getAllByRole('button', { name: 'Ara' })[1])
  await screen.findByRole('option', { name: 'PRD-7 — Bant · kg' })
  await user.selectOptions(screen.getByLabelText('Ürün ve alış birimi'), PRODUCT)
  await user.type(screen.getByLabelText('Miktar'), '12,5')
  await user.click(screen.getByRole('button', { name: 'Taslak siparişi kaydet' }))
}

beforeEach(() => {
  sessionStorage.clear()
  api.create.mockReset()
  api.suppliers.mockReset().mockResolvedValue({ type: 'ok-single', data: { items: [{ supplierPartyId: PARTY, companyAccountId: ACCOUNT, label: 'CAR-42 — Tedarikçi AŞ' }], nextCursor: null } })
  api.products.mockReset().mockResolvedValue({ type: 'ok-single', data: { items: [{ productId: PRODUCT, productVersion: '7', code: 'PRD-7', name: 'Bant', unitId: UNIT, unitCode: 'kg', profileId: '99999999-9999-4999-8999-999999999999', profileVersion: '3' }], searchWindowFull: false } })
})

describe('P-04 purchase order create form', () => {
  it('discards a late product response after the effective date changes', async () => {
    let resolveOld!: (value: unknown) => void
    const oldResponse = new Promise((resolve) => { resolveOld = resolve })
    api.products.mockReset()
      .mockReturnValueOnce(oldResponse)
      .mockResolvedValueOnce({ type: 'ok-single', data: { items: [{ productId: PRODUCT_B, productVersion: '8', code: 'PRD-8', name: 'Yeni Bant', unitId: UNIT, unitCode: 'kg', profileId: '99999999-9999-4999-8999-999999999999', profileVersion: '4' }], searchWindowFull: false } })
    const user = userEvent.setup()
    render(<MemoryRouter><PurchaseOrderCreatePage /></MemoryRouter>)
    await screen.findByRole('option', { name: 'CAR-42 — Tedarikçi AŞ' })
    const effectiveOn = screen.getByLabelText('İş tarihi')
    await user.type(effectiveOn, '2026-09-23')
    await user.click(screen.getAllByRole('button', { name: 'Ara' })[1])
    await user.clear(effectiveOn)
    await user.type(effectiveOn, '2026-09-24')
    await user.click(screen.getAllByRole('button', { name: 'Ara' })[1])
    await screen.findByRole('option', { name: 'PRD-8 — Yeni Bant · kg' })
    resolveOld({ type: 'ok-single', data: { items: [{ productId: PRODUCT, productVersion: '7', code: 'PRD-7', name: 'Eski Bant', unitId: UNIT, unitCode: 'kg', profileId: '99999999-9999-4999-8999-999999999999', profileVersion: '3' }], searchWindowFull: false } })
    await waitFor(() => expect(screen.queryByRole('option', { name: 'PRD-7 — Eski Bant · kg' })).toBeNull())
    expect(screen.getByRole('option', { name: 'PRD-8 — Yeni Bant · kg' })).toBeTruthy()
  })

  it('selects owner references, saves one line and exposes the same record reopen link', async () => {
    api.create.mockResolvedValue(applied())
    const user = userEvent.setup()
    render(<MemoryRouter><PurchaseOrderCreatePage /></MemoryRouter>)
    await fillAndSubmit(user)
    expect(await screen.findByRole('heading', { name: 'Satın alma siparişi kaydedildi' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Aynı kaydı aç' }).getAttribute('href')).toBe(`/satinalma/siparisler/${ORDER}`)
    const [payload] = api.create.mock.calls[0]
    expect(payload).toMatchObject({ supplierPartyId: PARTY, companyAccountId: ACCOUNT, effectiveOn: '2026-09-23' })
    expect(payload.lines[0]).toMatchObject({ productId: PRODUCT, expectedProductVersion: '7', unitId: UNIT, quantity: '12.5' })
  })

  it('retries an unknown result with the same key and payload', async () => {
    api.create.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' }).mockResolvedValueOnce(applied())
    const user = userEvent.setup()
    render(<MemoryRouter><PurchaseOrderCreatePage /></MemoryRouter>)
    await fillAndSubmit(user)
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(2))
    expect(api.create.mock.calls[1][0]).toEqual(api.create.mock.calls[0][0])
    expect(api.create.mock.calls[1][1].idempotencyKey).toBe(api.create.mock.calls[0][1].idempotencyKey)
  })

  it('keeps the original pending key through a refused retry, close and reopen', async () => {
    api.create.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
      .mockResolvedValueOnce({ type: 'failed', status: 403, code: 'forbidden' })
      .mockResolvedValueOnce(applied())
    const user = userEvent.setup()
    const first = render(<MemoryRouter><PurchaseOrderCreatePage /></MemoryRouter>)
    await fillAndSubmit(user)
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await screen.findByText(/Önceki gönderimin sonucu hâlâ bilinmiyor/)
    expect(screen.getByRole('link', { name: 'Formu kapat' })).toBeTruthy()
    first.unmount()
    render(<MemoryRouter><PurchaseOrderCreatePage /></MemoryRouter>)
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(3))
    expect(api.create.mock.calls[2][0]).toEqual(api.create.mock.calls[0][0])
    expect(api.create.mock.calls[2][1].idempotencyKey).toBe(api.create.mock.calls[0][1].idempotencyKey)
  })

  it('does not expose abandon or allow a new-key save while the first result is unknown', async () => {
    api.create.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
    const user = userEvent.setup()
    render(<MemoryRouter><PurchaseOrderCreatePage /></MemoryRouter>)
    await fillAndSubmit(user)
    await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' })
    expect(screen.queryByRole('button', { name: 'Bu denemeyi bırak' })).toBeNull()
    expect((screen.getByRole('button', { name: 'Taslak siparişi kaydet' }) as HTMLButtonElement).disabled).toBe(true)
    expect(api.create).toHaveBeenCalledTimes(1)
  })
})
