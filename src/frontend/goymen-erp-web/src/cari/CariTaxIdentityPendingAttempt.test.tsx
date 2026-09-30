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
import { resetPendingTaxIdentityAttempts } from './pendingTaxIdentityAttempt'
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
  useEffect(() => {
    if (shell.activeContext?.capabilityGeneration !== null) return
    dispatch({
      type: 'ROUTE_ENTRY_VERIFIED',
      forEntry: shell.routeEntryGeneration,
      generation: 'generation-2',
      capabilities: [],
    })
  }, [shell.activeContext?.capabilityGeneration, shell.routeEntryGeneration])

  const key = contextKey(shell)
  if (key === null || shell.activeContext?.capabilityGeneration === null) return <p>Yetki yeniden doğrulanıyor</p>
  const namespace = contextKeyString(key)
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
  it('keeps the original key and body after a retry 403 re-fences and remounts the detail', async () => {
    vi.stubGlobal('navigator', { onLine: true })
    rememberContract(namespaceOf(ready))
    const reverified: ShellState = {
      ...ready,
      activeContext: companyContext('generation-2'),
    }
    rememberContract(namespaceOf(reverified))
    rememberContract(namespaceOf({ ...ready, activeContext: companyContext('generation-3') }))

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
