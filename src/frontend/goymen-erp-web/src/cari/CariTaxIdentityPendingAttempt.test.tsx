import { useEffect, useReducer } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { CariTaxIdentityPanel } from './CariTaxIdentityPanel'
import {
  COUNTERPARTY_CONTRACT_HEADER,
  COUNTERPARTY_CONTRACT_VERSION,
  beginContractRead,
  recordContractRead,
  resetContractReadiness,
} from './clientContract'
import {
  rememberTaxIdentityAttempt,
  resetPendingTaxIdentityAttempts,
  taxIdentityBindingForShell,
  type FrozenTaxIdentityAttempt,
} from './pendingTaxIdentityAttempt'
import { ShellDispatchContext, ShellStateContext } from '../shell/shellContext'
import {
  contextKey,
  contextKeyString,
  initialShellState,
  shellReducer,
  type CompanyContext,
  type ShellState,
} from '../shell/shellState'

const PARTY = '11111111-1111-1111-1111-111111111111'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const USER = '22222222-2222-2222-2222-222222222222'

function companyContext(generation: string): CompanyContext {
  return { userId: USER, scopeMode: 'single', companyId: COMPANY, capabilityGeneration: generation, capabilities: [] }
}

const ready = shellReducer(initialShellState, {
  type: 'SESSION_READY',
  forSessionEpoch: initialShellState.sessionEpoch,
  context: companyContext('generation-1'),
})

function namespaceOf(state: ShellState): string {
  const key = contextKey(state)
  if (key === null) throw new Error('test shell must be bound')
  return contextKeyString(key)
}

function rememberContract(namespace: string) {
  const contract = { origin: '/api/v1', namespace }
  beginContractRead(contract)
  recordContractRead(contract, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
}

function Harness({ onReauthorize }: { readonly onReauthorize: () => void }) {
  const [shell, dispatch] = useReducer(shellReducer, ready)
  const activeKey = contextKey(shell)
  const activeNamespace = activeKey === null || shell.activeContext?.capabilityGeneration === null
    ? null
    : contextKeyString(activeKey)
  useEffect(() => {
    if (shell.activeContext?.capabilityGeneration !== null) return
    dispatch({
      type: 'ROUTE_ENTRY_VERIFIED',
      forEntry: shell.routeEntryGeneration,
      generation: 'generation-2',
      capabilities: [],
    })
  }, [shell.activeContext?.capabilityGeneration, shell.routeEntryGeneration])

  useEffect(() => {
    if (activeNamespace === null) return
    rememberContract(activeNamespace)
  }, [activeNamespace])

  if (activeNamespace === null) return <p>Yetki yeniden doğrulanıyor</p>
  const namespace = activeNamespace
  return (
    <ShellStateContext.Provider value={shell}>
      <ShellDispatchContext.Provider value={dispatch}>
        {shell.activeContext?.capabilityGeneration === 'generation-2' ? (
          <button
            type="button"
            onClick={() => {
              onReauthorize()
              dispatch({
                type: 'ROUTE_ENTRY_VERIFIED',
                forEntry: shell.routeEntryGeneration,
                generation: 'generation-3',
                capabilities: [],
              })
            }}
          >
            Test yetkisini yenile
          </button>
        ) : null}
        <CariTaxIdentityPanel
          key={`${namespace}:${PARTY}`}
          partyId={PARTY}
          companyId={COMPANY}
          namespace={namespace}
          contractContext={{ origin: '/api/v1', namespace }}
        />
      </ShellDispatchContext.Provider>
    </ShellStateContext.Provider>
  )
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  resetContractReadiness()
  resetPendingTaxIdentityAttempts()
})

describe('Cari tax identity pending attempt across the real shell boundary', () => {
  it.each([
    {
      name: 'enter',
      operation: 'enter' as const,
      view: {
        partyId: PARTY,
        companyId: COMPANY,
        version: '2',
        taxIdentityNumber: '1234567890',
        kind: 'vkn',
        duplicates: [],
        pendingChange: null,
        lastDecidedChange: null,
        viewer: { canUpdate: true, canApprove: false, isRequesterOfPending: false },
      },
    },
    {
      name: 'request',
      operation: 'request' as const,
      view: {
        partyId: PARTY,
        companyId: COMPANY,
        version: '2',
        taxIdentityNumber: '1234567890',
        kind: 'vkn',
        duplicates: [],
        pendingChange: {
          id: '33333333-3333-3333-3333-333333333333',
          status: 'pending',
          currentTaxIdentityNumber: '1234567890',
          proposedTaxIdentityNumber: '9876543217',
          proposedKind: 'vkn',
          reason: 'Levha değişti',
          requestedBy: 'maker',
          requestedAtUtc: '2026-09-30T12:00:00Z',
          decidedBy: null,
          decidedAtUtc: null,
          decisionNote: null,
          proposedDuplicates: [],
        },
        lastDecidedChange: null,
        viewer: { canUpdate: true, canApprove: false, isRequesterOfPending: true },
      },
    },
    {
      name: 'decision',
      operation: 'decide:33333333-3333-3333-3333-333333333333' as const,
      view: {
        partyId: PARTY,
        companyId: COMPANY,
        version: '3',
        taxIdentityNumber: '9876543217',
        kind: 'vkn',
        duplicates: [],
        pendingChange: null,
        lastDecidedChange: {
          id: '33333333-3333-3333-3333-333333333333',
          status: 'approved',
          currentTaxIdentityNumber: '1234567890',
          proposedTaxIdentityNumber: '9876543217',
          proposedKind: 'vkn',
          reason: 'Levha değişti',
          requestedBy: 'maker',
          requestedAtUtc: '2026-09-30T12:00:00Z',
          decidedBy: 'checker',
          decidedAtUtc: '2026-09-30T12:05:00Z',
          decisionNote: null,
          proposedDuplicates: [],
        },
        viewer: { canUpdate: false, canApprove: true, isRequesterOfPending: false },
      },
    },
  ])('replays a committed $name response-lost attempt after remount even when the fresh view already changed', async ({ operation, view }) => {
    vi.stubGlobal('navigator', { onLine: true })
    const namespace = namespaceOf(ready)
    rememberContract(namespace)
    const binding = taxIdentityBindingForShell(ready, PARTY, '/api/v1')
    if (binding === null) throw new Error('test shell must produce a tax-identity attempt binding')
    const key = `response-lost-${operation}`
    const body = { operation, originalExpectedVersion: '1' }
    const sends: Array<{ key: string; companyId: string; body: typeof body }> = []
    const frozen: FrozenTaxIdentityAttempt = {
      operation,
      fingerprint: JSON.stringify(body),
      key,
      binding: binding.key,
      companyId: COMPANY,
      contract: { origin: '/api/v1', namespace },
      send: async (context) => {
        sends.push({ key: context.idempotencyKey, companyId: context.companyId, body })
        return { type: 'applied', data: null, replayed: true, originalCorrelationId: 'original-correlation' }
      },
      okText: 'Stored answer resolved.',
    }
    rememberTaxIdentityAttempt(binding, {
      phase: 'unknown',
      snapshot: {
        key,
        operation: `counterparty.tax-identity:${operation}`,
        binding: binding.key,
        payload: frozen,
      },
      reason: 'network-error',
      resends: 0,
    })
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify(view), {
        status: 200,
        headers: { 'Content-Type': 'application/json', [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION },
      })),
    )

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
    queryClient.setQueryData(['shell', 'identity'], { type: 'ok-single', data: { userId: USER } })
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <Harness onReauthorize={() => undefined} />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Aynı işlemi yeniden gönder' }))
    await waitFor(() => expect(sends).toHaveLength(1))
    expect(sends[0]).toEqual({ key, companyId: COMPANY, body })
    expect(await screen.findByText('Stored answer resolved.')).toBeTruthy()
  })

  it('keeps the original key and body after a retry 403 re-fences and remounts the detail', async () => {
    vi.stubGlobal('navigator', { onLine: true })
    rememberContract(namespaceOf(ready))

    const calls: Array<{ method: string; headers: Record<string, string>; body: unknown }> = []
    let putCount = 0
    let viewerAuthorized = true
    vi.stubGlobal(
      'fetch',
      vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
        const method = init?.method ?? 'GET'
        calls.push({
          method,
          headers: (init?.headers as Record<string, string> | undefined) ?? {},
          body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
        })
        if (method === 'POST') {
          return new Response(JSON.stringify({ taxIdentityNumber: '10000000146', kind: 'tckn', duplicates: [] }), {
            status: 200,
            headers: { 'Content-Type': 'application/json', [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION },
          })
        }
        if (method === 'GET') {
          return new Response(JSON.stringify({
            partyId: PARTY,
            companyId: COMPANY,
            version: '1',
            taxIdentityNumber: null,
            kind: null,
            duplicates: [],
            pendingChange: null,
            lastDecidedChange: null,
            viewer: { canUpdate: viewerAuthorized, canApprove: false, isRequesterOfPending: false },
          }), {
            status: 200,
            headers: { 'Content-Type': 'application/json', [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION },
          })
        }
        putCount += 1
        if (putCount === 1) throw new TypeError('response lost')
        if (putCount === 2) {
          viewerAuthorized = false
          return new Response(JSON.stringify({ code: 'forbidden', status: 403 }), {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          })
        }
        return new Response(null, {
          status: 204,
          headers: { [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION },
        })
      }),
    )

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
    queryClient.setQueryData(['shell', 'identity'], { type: 'ok-single', data: { userId: USER } })
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <Harness onReauthorize={() => { viewerAuthorized = true }} />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    const user = userEvent.setup()
    await user.type(await screen.findByLabelText('VKN (10 hane) veya TCKN (11 hane)'), '10000000146')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı işlemi yeniden gönder' }))
    await waitFor(() => expect(calls.filter((call) => call.method === 'PUT')).toHaveLength(2))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Aynı işlemi yeniden gönder' })).toBeNull())
    expect(calls.filter((call) => call.method === 'PUT')).toHaveLength(2)
    await user.click(await screen.findByRole('button', { name: 'Test yetkisini yenile' }))
    await user.click(await screen.findByRole('button', { name: 'Aynı işlemi yeniden gönder' }))
    await waitFor(() => expect(calls.filter((call) => call.method === 'PUT')).toHaveLength(3))

    const attempts = calls.filter((call) => call.method === 'PUT')
    expect(attempts[1].headers['Idempotency-Key']).toBe(attempts[0].headers['Idempotency-Key'])
    expect(attempts[2].headers['Idempotency-Key']).toBe(attempts[0].headers['Idempotency-Key'])
    expect(attempts[1].body).toEqual(attempts[0].body)
    expect(attempts[2].body).toEqual(attempts[0].body)
  })
})
