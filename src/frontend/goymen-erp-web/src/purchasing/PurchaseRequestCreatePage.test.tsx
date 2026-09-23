import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PurchaseRequestCreatePage } from './PurchaseRequestCreatePage'

const COMPANY = '22222222-2222-4222-8222-222222222222'
const ACTOR = '33333333-3333-4333-8333-333333333333'
const PRODUCT = '66666666-6666-4666-8666-666666666666'
const UNIT = '77777777-7777-4777-8777-777777777777'
const REQUEST = '88888888-8888-4888-8888-888888888888'

const api = vi.hoisted(() => ({ create: vi.fn(), products: vi.fn() }))

vi.mock('./api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./api')>()
  return { ...actual, createPurchaseRequest: api.create, fetchPurchasingProductCandidates: api.products }
})
vi.mock('../shell/shellContext', () => ({
  useShellState: () => ({ sessionEpoch: 1, routeEntryGeneration: 1, activeContext: { userId: ACTOR, companyId: COMPANY, scopeMode: 'single', capabilities: [] } }),
}))
vi.mock('../shell/shellState', () => ({ contextKey: () => ['fixture'], contextKeyString: () => 'fixture', hasCapability: () => true }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: vi.fn() }) }))

function applied() {
  return {
    type: 'applied' as const,
    replayed: true,
    response: {
      detail: {
        request: { id: REQUEST, companyId: COMPANY, status: 'Draft' as const, version: '1', effectiveOn: '2026-09-23' },
        lines: [],
      },
      isReplay: true,
      originalCorrelationId: 'corr-1',
    },
  }
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('İş tarihi'), '2026-09-23')
  await user.click(screen.getByRole('button', { name: 'Ara' }))
  await screen.findByRole('option', { name: 'PRD-7 — Bant · kg' })
  await user.selectOptions(screen.getByLabelText('Ürün 1'), PRODUCT)
  await user.type(screen.getByLabelText('Miktar'), '12,5')
  await user.click(screen.getByRole('button', { name: 'Taslak talebi kaydet' }))
}

beforeEach(() => {
  sessionStorage.clear()
  api.create.mockReset()
  api.products.mockReset().mockResolvedValue({
    type: 'ok-single',
    data: {
      items: [{ productId: PRODUCT, productVersion: '7', code: 'PRD-7', name: 'Bant', unitId: UNIT, unitCode: 'kg', profileId: '99999999-9999-4999-8999-999999999999', profileVersion: '3' }],
      searchWindowFull: false,
    },
  })
})

describe('P-01 purchase request create recovery', () => {
  it('keeps the original pending key through a refused retry, close and reopen', async () => {
    api.create.mockResolvedValueOnce({ type: 'unknown', reason: 'network-error' })
      .mockResolvedValueOnce({ type: 'failed', status: 403, code: 'forbidden' })
      .mockResolvedValueOnce(applied())
    const user = userEvent.setup()
    const first = render(<MemoryRouter><PurchaseRequestCreatePage /></MemoryRouter>)

    await fillAndSubmit(user)
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await screen.findByText(/Önceki gönderimin sonucu hâlâ bilinmiyor/)
    expect(screen.getByRole('link', { name: 'Formu kapat' })).toBeTruthy()

    first.unmount()
    render(<MemoryRouter><PurchaseRequestCreatePage /></MemoryRouter>)
    await user.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))

    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(3))
    expect(api.create.mock.calls[2][0]).toEqual(api.create.mock.calls[0][0])
    expect(api.create.mock.calls[2][1].idempotencyKey).toBe(api.create.mock.calls[0][1].idempotencyKey)
  })
})
