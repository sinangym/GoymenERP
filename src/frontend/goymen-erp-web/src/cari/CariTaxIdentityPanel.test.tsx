import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { CariTaxIdentityPanel } from './CariTaxIdentityPanel'
import {
  COUNTERPARTY_CONTRACT_HEADER,
  COUNTERPARTY_CONTRACT_VERSION,
  beginContractRead,
  recordContractRead,
  resetContractReadiness,
} from './clientContract'

const PARTY = '11111111-1111-1111-1111-111111111111'
const OTHER = '22222222-2222-2222-2222-222222222222'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const REQUEST = '33333333-3333-3333-3333-333333333333'
const NAMESPACE = 'ns-test'
const CONTRACT = { origin: '/api/v1', namespace: NAMESPACE }

interface Call {
  readonly method: string
  readonly url: string
  readonly headers: Record<string, string>
  readonly body: unknown
}

type Handler = (call: Call) => Response | Promise<Response>

function json(body: unknown, status = 200, ack = true): Response {
  return new Response(body === null ? null : JSON.stringify(body), {
    status,
    headers: {
      'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json',
      ...(ack ? { [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION } : {}),
    },
  })
}

const noContent = () =>
  new Response(null, { status: 204, headers: { [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION } })

function taxView(over: Record<string, unknown> = {}) {
  return {
    partyId: PARTY,
    companyId: COMPANY,
    version: '1',
    taxIdentityNumber: null,
    kind: null,
    duplicates: [],
    pendingChange: null,
    lastDecidedChange: null,
    viewer: { canUpdate: true, canApprove: false, isRequesterOfPending: false },
    ...over,
  }
}

const pendingChange = {
  id: REQUEST,
  status: 'pending',
  currentTaxIdentityNumber: '1234567890',
  proposedTaxIdentityNumber: '9876543217',
  proposedKind: 'vkn',
  reason: 'Vergi levhası yenilendi',
  requestedBy: 'maker',
  requestedAtUtc: '2026-09-30T10:00:00+00:00',
  decidedBy: null,
  decidedAtUtc: null,
  decisionNote: null,
  proposedDuplicates: [],
}

function install(handler: Handler) {
  const calls: Call[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const call: Call = {
        method: init?.method ?? 'GET',
        url: typeof input === 'string' ? input : input.toString(),
        headers: (init?.headers as Record<string, string> | undefined) ?? {},
        body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
      }
      calls.push(call)
      return handler(call)
    }),
  )
  return calls
}

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const router = createMemoryRouter(
    [
      {
        path: '*',
        element: (
          <CariTaxIdentityPanel partyId={PARTY} companyId={COMPANY} namespace={NAMESPACE} contractContext={CONTRACT} />
        ),
      },
    ],
    { initialEntries: [`/cari/${PARTY}`] },
  )
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

const puts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'PUT')

beforeEach(() => {
  vi.stubGlobal('navigator', { onLine: true })
  resetContractReadiness()
  // The card's own detail read opened the write gate for this context.
  beginContractRead(CONTRACT)
  recordContractRead(CONTRACT, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  resetContractReadiness()
})

describe('AT-009 VKN/TCKN on the Cari card', () => {
  it('refuses an invalid number on the client, warns about a duplicate, saves on the explicit second click and re-reads', async () => {
    let stored: string | null = null
    const calls = install((call) => {
      if (call.method === 'GET' && call.url.includes('/duplicates')) {
        return json({
          taxIdentityNumber: '1234567890',
          kind: 'vkn',
          duplicates: [{ partyId: OTHER, code: 'CAR-000002', legalName: 'Örnek Şube', accountIsActive: true }],
        })
      }
      if (call.method === 'GET') {
        return json(
          stored === null
            ? taxView()
            : taxView({
                version: '2',
                taxIdentityNumber: stored,
                kind: 'vkn',
                duplicates: [{ partyId: OTHER, code: 'CAR-000002', legalName: 'Örnek Şube', accountIsActive: true }],
              }),
        )
      }
      stored = (call.body as { taxIdentityNumber: string }).taxIdentityNumber
      return noContent()
    })
    renderPanel()
    const user = userEvent.setup()

    expect(await screen.findByText('Girilmemiş')).toBeTruthy()
    const input = screen.getByLabelText('VKN (10 hane) veya TCKN (11 hane)')
    await user.type(input, '1234567891')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('VKN geçersiz: kontrol hanesi tutmuyor.')).toBeTruthy()
    expect(calls.filter((call) => call.url.includes('/duplicates'))).toHaveLength(0)
    expect(puts(calls)).toHaveLength(0)

    await user.clear(input)
    await user.type(input, '1234567890')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('Aynı numara bu şirketin başka cari kartında da kayıtlı')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'CAR-000002 — Örnek Şube' }).getAttribute('href')).toBe(`/cari/${OTHER}`)
    expect(puts(calls)).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: 'Uyarıya rağmen kaydet' }))
    await waitFor(() => expect(puts(calls)).toHaveLength(1))
    const [put] = puts(calls)
    expect(put.url).toBe(`/api/v1/counterparties/${PARTY}/tax-identity`)
    expect(put.body).toEqual({ taxIdentityNumber: '1234567890', expectedVersion: '1' })
    expect(put.headers['X-Company-Id']).toBe(COMPANY)
    expect(put.headers['Idempotency-Key']).toMatch(/\S/)
    expect(await screen.findByText('Numara kaydedildi.')).toBeTruthy()
    // The re-read, not the request, is what the card shows.
    await waitFor(() =>
      expect(document.querySelector('[data-tax-identity="1234567890"]')).not.toBeNull(),
    )
    expect(document.querySelector('[data-state="tax-identity-duplicate"]')).not.toBeNull()
  })

  it('offers a change request for an existing number, never a direct overwrite, and sends the reason', async () => {
    const calls = install((call) => {
      if (call.method === 'GET' && call.url.includes('/duplicates')) {
        return json({ taxIdentityNumber: '9876543217', kind: 'vkn', duplicates: [] })
      }
      if (call.method === 'GET') {
        return json(taxView({ version: '2', taxIdentityNumber: '1234567890', kind: 'vkn' }))
      }
      return noContent()
    })
    renderPanel()
    const user = userEvent.setup()
    await screen.findByText('1234567890')
    expect(screen.queryByLabelText('VKN (10 hane) veya TCKN (11 hane)')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Numara değişikliği talep et' }))
    await user.type(screen.getByLabelText('Yeni VKN / TCKN'), '9876543217')
    await user.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    expect(await screen.findByText('Değişiklik için gerekçe yazın (en çok 1000 karakter).')).toBeTruthy()
    await user.type(screen.getByLabelText('Değişiklik gerekçesi'), 'Tüzel kişiliğe geçiş')
    await user.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    await waitFor(() => expect(puts(calls)).toHaveLength(1))
    expect(puts(calls)[0].url).toBe(`/api/v1/counterparties/${PARTY}/tax-identity/change-request`)
    expect(puts(calls)[0].body).toEqual({
      proposedTaxIdentityNumber: '9876543217',
      reason: 'Tüzel kişiliğe geçiş',
      expectedVersion: '2',
    })
    expect(await screen.findByText(/Değişiklik talebi onaya gönderildi/)).toBeTruthy()
  })

  it('shows the proposer their pending request without any decision control', async () => {
    const calls = install(() =>
      json(
        taxView({
          version: '2',
          taxIdentityNumber: '1234567890',
          kind: 'vkn',
          pendingChange,
          viewer: { canUpdate: true, canApprove: true, isRequesterOfPending: true },
        }),
      ),
    )
    renderPanel()
    expect(await screen.findByText('Onay bekleyen değişiklik')).toBeTruthy()
    expect(screen.getByText(/Bu talebi siz açtınız/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reddet' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Numara değişikliği talep et' })).toBeNull()
    expect(puts(calls)).toHaveLength(0)
  })

  it('lets another person approve, and says so plainly when the server refuses', async () => {
    let refuse = true
    const calls = install((call) => {
      if (call.method === 'GET') {
        return json(
          taxView({
            version: '2',
            taxIdentityNumber: '1234567890',
            kind: 'vkn',
            pendingChange,
            viewer: { canUpdate: false, canApprove: true, isRequesterOfPending: false },
          }),
        )
      }
      if (refuse) {
        refuse = false
        return json({ code: 'tax_identity_self_decision', status: 409 }, 409)
      }
      return noContent()
    })
    renderPanel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Onayla' }))
    expect(await screen.findByText('Kendi açtığınız talebi onaylayamaz veya reddedemezsiniz.')).toBeTruthy()
    await user.type(screen.getByLabelText('Karar notu (isteğe bağlı)'), 'Levha görüldü')
    await user.click(screen.getByRole('button', { name: 'Onayla' }))
    await waitFor(() => expect(puts(calls)).toHaveLength(2))
    const decision = puts(calls)[1]
    expect(decision.url).toBe(`/api/v1/counterparties/${PARTY}/tax-identity/change-request/decision`)
    expect(decision.body).toEqual({
      changeRequestId: REQUEST,
      decision: 'approve',
      note: 'Levha görüldü',
      expectedVersion: '2',
    })
    // A definite refusal ended the first attempt: the next one has its own key.
    expect(puts(calls)[0].headers['Idempotency-Key']).not.toBe(decision.headers['Idempotency-Key'])
    expect(await screen.findByText('Değişiklik onaylandı; yeni numara kaydedildi.')).toBeTruthy()
  })

  it('keeps the same key for an explicit retry after an answer it could not read', async () => {
    let first = true
    const calls = install((call) => {
      if (call.method === 'GET' && call.url.includes('/duplicates')) {
        return json({ taxIdentityNumber: '10000000146', kind: 'tckn', duplicates: [] })
      }
      if (call.method === 'GET') return json(taxView())
      if (first) {
        first = false
        throw new TypeError('network down')
      }
      return noContent()
    })
    renderPanel()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText('VKN (10 hane) veya TCKN (11 hane)'), '10000000146')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı işlemi yeniden gönder' }))
    await waitFor(() => expect(puts(calls)).toHaveLength(2))
    expect(puts(calls)[0].headers['Idempotency-Key']).toBe(puts(calls)[1].headers['Idempotency-Key'])
    expect(puts(calls)[1].body).toEqual(puts(calls)[0].body)
    expect(await screen.findByText('Numara kaydedildi.')).toBeTruthy()
  })

  it('offers no form to a reader without the grant', async () => {
    install(() =>
      json(taxView({ viewer: { canUpdate: false, canApprove: false, isRequesterOfPending: false } })),
    )
    renderPanel()
    expect(await screen.findByText('Girilmemiş')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Kaydet' })).toBeNull()
  })
})
