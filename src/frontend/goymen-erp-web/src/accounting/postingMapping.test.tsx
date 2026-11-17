import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CogsMappingPage } from './CogsMappingPage'
import { decodePostingMapping, decodePostingMappingWorkspace, sendPostingMapping } from './postingMappingApi'
import { POSTING_MAPPING_TEXT as T } from './postingMappingText'

const state = vi.hoisted(() => ({ company: 'e0419000-0000-4000-8000-000000000001', actor: 'e0419000-0000-4000-8000-000000000002' }))
const maker = 'e0419000-0000-4000-8000-000000000003'
const trade = 'e0419000-0000-4000-8000-000000000621'; const manufactured = 'e0419000-0000-4000-8000-000000000620'
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ routeEntryGeneration: 1,
  activeContext: { companyId: state.company, userId: state.actor, scopeMode: 'single' } }) }))
vi.mock('../shell/shellState', () => ({ contextKey: () => state.company, contextKeyString: (key: string) => key }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => 'SENTETIK', report: vi.fn() }) }))
const row = (mappingKey = 'trade-goods', accountId = trade, accountCode = '621') => ({ purpose: 'cost-of-goods-sold', mappingKey,
  accountId, accountCode, itemType: null, linePurpose: null, isExport: null, vatRateCode: null })
const mapping = (patch: Record<string, unknown> = {}) => ({ companyId: state.company, mappingVersion: 1, effectiveFrom: '2026-11-17',
  status: 'Prepared', version: 1, accountPlanSourceVersion: 'SENTETIK-plan', accountPlanSourceSha256: 'a'.repeat(64),
  preparedByUserId: maker, preparedAtUtc: '2026-11-17T09:00:00Z', activatedByUserId: null, activatedAtUtc: null,
  rows: [row(), row('manufactured', manufactured, '620')], ...patch })
const workspace = (patch: Record<string, unknown> = {}) => ({ companyId: state.company, latestMappingVersion: 1, accountPlanState: 'Available',
  accountPlanSourceVersion: 'SENTETIK-plan', accountPlanSourceSha256: 'a'.repeat(64), accounts: [
    { accountId: trade, code: '621', name: 'SENTETIK ticari' }, { accountId: manufactured, code: '620', name: 'SENTETIK mamul' }],
  cogsDefaults: [{ mappingKey: 'trade-goods', accountCode: '621', accountId: trade }, { mappingKey: 'manufactured', accountCode: '620', accountId: manufactured }],
  active: null, prepared: [mapping()], ...patch })
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const mount = (canPrepare = true, canActivate = true) => render(<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
  <CogsMappingPage canPrepare={canPrepare} canActivate={canActivate} /></QueryClientProvider>)
afterEach(() => { cleanup(); vi.unstubAllGlobals(); state.actor = 'e0419000-0000-4000-8000-000000000002' })

describe('COGS mapping workspace', () => {
  it('rejects foreign or fabricated account choices and mismatched mapping provenance', () => {
    expect(decodePostingMappingWorkspace(workspace(), state.company)?.cogsDefaults[0].accountId).toBe(trade)
    for (const patch of [{ companyId: maker }, { accountPlanSourceSha256: 'missing' }, { latestMappingVersion: 0 },
      { cogsDefaults: [{ mappingKey: 'trade-goods', accountCode: '621', accountId: maker }, { mappingKey: 'manufactured', accountCode: '620', accountId: manufactured }] },
      { accountPlanState: 'Unavailable' }, { prepared: [mapping(), mapping()] }])
      expect(decodePostingMappingWorkspace(workspace(patch), state.company)).toBeNull()
    expect(decodePostingMapping(mapping({ rows: [{ ...row(), itemType: 'Steel' }] }), state.company)).toBeNull()
    expect(decodePostingMapping(mapping({ status: 'Active', activatedByUserId: maker, activatedAtUtc: '2026-11-17T10:00:00Z' }), state.company)).toBeNull()
  })
  it('only fills server-proven defaults after the operator asks', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(workspace())))
    mount(); const tradeInput = await screen.findByLabelText(T.trade) as HTMLSelectElement
    expect(tradeInput.value).toBe('')
    fireEvent.click(screen.getByRole('button', { name: T.defaults }))
    expect(tradeInput.value).toBe(trade); expect((screen.getByLabelText(T.manufactured) as HTMLSelectElement).value).toBe(manufactured)
  })
  it('does not expose writer actions in read mode and does not allow self activation', async () => {
    state.actor = maker; vi.stubGlobal('fetch', vi.fn(async () => json(workspace())))
    mount(false, true); await screen.findByText(T.self)
    expect(screen.queryByRole('button', { name: T.activate })).toBeNull()
    expect(screen.queryByRole('button', { name: T.prepare })).toBeNull()
  })
  it('freezes an unknown activation and repeats the identical payload and key', async () => {
    const writes: RequestInit[] = []
    vi.stubGlobal('fetch', vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') { writes.push(init); return writes.length === 1 ? json({}, 503) : json(mapping({ status: 'Active', version: 2,
        activatedByUserId: state.actor, activatedAtUtc: '2026-11-17T10:00:00Z' })) }
      return json(workspace())
    }))
    mount(); fireEvent.click(await screen.findByRole('button', { name: T.activate }))
    const retry = await screen.findByRole('button', { name: T.retry })
    expect((screen.getByRole('button', { name: T.refresh }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(retry); await waitFor(() => expect(writes).toHaveLength(2))
    expect(writes[0].body).toBe(writes[1].body); expect(writes[0].headers).toEqual(writes[1].headers)
  })
  it('does not treat a foreign actor or mismatched successful write as applied', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(mapping({ status: 'Active', version: 2, activatedByUserId: manufactured, activatedAtUtc: '2026-11-17T10:00:00Z' }))))
    expect(await sendPostingMapping(state.company, { kind: 'activate', key: 'SENTETIK-key', mappingVersion: 1,
      request: { expectedVersion: 1, expectedActiveMappingVersion: 0 } }, state.actor)).toEqual({ type: 'unknown' })
  })
})
