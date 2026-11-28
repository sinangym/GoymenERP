import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CariMergePanel } from './CariMergePanel'
import { MERGE_IMPACT_TEXT as I } from './mergeImpact'
import { MERGE_REASON_MAX, MERGE_TEXT as T } from './merge'
import { ShellDispatchContext } from '../shell/shellContext'
import {
  COUNTERPARTY_CONTRACT_HEADER,
  COUNTERPARTY_CONTRACT_VERSION,
  beginContractRead,
  recordContractRead,
  resetContractReadiness,
} from './clientContract'

const authBoundary = vi.hoisted(() => {
  const snapshot = { actorId: 'actor-a', namespace: 'ns-test' }
  return { snapshot, capture: vi.fn(() => snapshot), report: vi.fn() }
})

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: authBoundary.capture, report: authBoundary.report }),
}))

const PARTY = '11111111-1111-1111-1111-111111111111'
const DUPLICATE = '22222222-2222-2222-2222-222222222222'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const CONTRACT = { origin: '/api/v1', namespace: 'ns-test' }

interface Call {
  readonly method: string
  readonly url: string
  readonly headers: Record<string, string>
  readonly body: unknown
}

const ack = { [COUNTERPARTY_CONTRACT_HEADER]: COUNTERPARTY_CONTRACT_VERSION }
const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...ack } })
const noContent = () => new Response(null, { status: 204, headers: ack })

const candidate = {
  partyId: DUPLICATE,
  code: 'C-000002',
  legalName: 'Örnek Ticaret A.Ş. (mükerrer)',
  version: '5',
}

const view = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  version: '3',
  legalName: 'Örnek Ticaret A.Ş.',
  mergedInto: null,
  candidates: [candidate],
  viewer: { canApproveMerge: true },
  ...over,
})

const impact = () => ({ companyId: COMPANY,
  survivor: { partyId: PARTY, code: 'C-000001', legalName: 'Örnek Ticaret A.Ş.', version: '3', isActive: true, mergedIntoPartyId: null },
  merged: { ...candidate, isActive: true, mergedIntoPartyId: null },
  holdingAccounts: [
    { companyId: COMPANY, accountId: '33333333-3333-3333-3333-333333333333', partyId: PARTY, version: '1', isActive: true },
    { companyId: COMPANY, accountId: '44444444-4444-4444-4444-444444444444', partyId: DUPLICATE, version: '1', isActive: true },
  ], changes: { partyId: DUPLICATE, mergedIntoPartyId: PARTY, isActive: false },
})

function install(handler: (call: Call) => Response | Promise<Response>, preview: (call: Call) => Response | Promise<Response> = () => json(impact())) {
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
      return call.url.endsWith('/merge/preview') ? preview(call) : handler(call)
    }),
  )
  return calls
}

function renderPanel(onChanged: () => void = () => undefined) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const rendered = render(
    <QueryClientProvider client={queryClient}>
      <ShellDispatchContext.Provider value={() => undefined}>
        <CariMergePanel
          partyId={PARTY}
          companyId={COMPANY}
          namespace="ns-test"
          contractContext={CONTRACT}
          onChanged={onChanged}
        />
      </ShellDispatchContext.Provider>
    </QueryClientProvider>,
  )
  return { ...rendered, queryClient }
}

const puts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'PUT')
const reads = (calls: readonly Call[]) => calls.filter((call) => call.method === 'GET' && call.url.endsWith('/merge'))

async function fillAndApprove(user: ReturnType<typeof userEvent.setup>, reason = '  Aynı firma iki kez açılmış.  ') {
  await user.selectOptions(await screen.findByLabelText(T.candidateLabel), DUPLICATE)
  await user.type(screen.getByLabelText(T.reasonLabel), reason)
  await screen.findByRole('region', { name: I.heading })
  await user.click(screen.getByLabelText(T.confirmLabel))
  await user.click(screen.getByRole('button', { name: T.approve }))
}

beforeEach(() => {
  authBoundary.report.mockClear()
  vi.stubGlobal('navigator', { onLine: true })
  resetContractReadiness()
  beginContractRead(CONTRACT)
  recordContractRead(CONTRACT, { status: 200, bodyValidated: true, ack: COUNTERPARTY_CONTRACT_VERSION })
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  resetContractReadiness()
})

describe('F-027 / EK-18 #76-#77 Cari duplicate merge', () => {
  it('shows loading, then approves the merge with one key, both party versions and the trimmed reason', async () => {
    let merged = false
    const onChanged = vi.fn()
    const calls = install((call) => {
      if (call.method === 'PUT') {
        merged = true
        return noContent()
      }
      return json(merged ? view({ version: '4', candidates: [] }) : view())
    })
    renderPanel(onChanged)
    expect(screen.getByText(T.loading)).toBeTruthy()

    const reason = (await screen.findByLabelText(T.reasonLabel)) as HTMLTextAreaElement
    expect(reason.maxLength).toBe(MERGE_REASON_MAX)
    expect(screen.getByText(T.warning)).toBeTruthy()

    const user = userEvent.setup()
    await fillAndApprove(user)

    expect(await screen.findByText(T.approved)).toBeTruthy()
    const [put] = puts(calls)
    expect(puts(calls)).toHaveLength(1)
    expect(put.url).toBe(`/api/v1/counterparties/${PARTY}/merge`)
    expect(put.body).toEqual({
      mergedPartyId: DUPLICATE,
      mergedPartyExpectedVersion: '5',
      reason: 'Aynı firma iki kez açılmış.',
      expectedVersion: '3',
    })
    expect(put.headers['Idempotency-Key']).toMatch(/^[0-9a-f]{32}$/)
    expect(put.headers['X-Company-Id']).toBe(COMPANY)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
    expect(onChanged).toHaveBeenCalledTimes(1)
    expect(await screen.findByText(T.noCandidates)).toBeTruthy()
  })

  it('sends nothing without a candidate, a reason or the irreversibility confirmation', async () => {
    const calls = install(() => json(view()))
    renderPanel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.approve }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.candidateRequired)

    await user.selectOptions(screen.getByLabelText(T.candidateLabel), DUPLICATE)
    await screen.findByRole('region', { name: I.heading })
    await user.type(screen.getByLabelText(T.reasonLabel), '   ')
    await user.click(screen.getByRole('button', { name: T.approve }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.reasonRequired)

    await user.type(screen.getByLabelText(T.reasonLabel), 'Mükerrer kayıt')
    await user.click(screen.getByRole('button', { name: T.approve }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.confirmRequired)
    expect(puts(calls)).toHaveLength(0)
  })

  it.each([
    ['version_conflict'],
    ['party_already_merged'],
    ['party_merge_target_merged'],
    ['party_merge_tax_identity_mismatch'],
  ])('names the %s refusal and reads the merge view again', async (code) => {
    const calls = install((call) => (call.method === 'PUT' ? json({ code }, 409) : json(view())))
    renderPanel()
    const user = userEvent.setup()
    await fillAndApprove(user)
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.refusal[code])
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
  })

  it('says the command is forbidden on a 403 and reports it to the auth boundary', async () => {
    install((call) => (call.method === 'PUT' ? json({}, 403) : json(view())))
    renderPanel()
    const user = userEvent.setup()
    await fillAndApprove(user)
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.commandForbidden)
    expect(authBoundary.report).toHaveBeenCalledWith(403, authBoundary.snapshot)
  })

  it('keeps the same key and body for an explicit retry when the answer was lost', async () => {
    let attempts = 0
    const calls = install((call) => {
      if (call.method === 'PUT') {
        attempts += 1
        if (attempts === 1) throw new TypeError('connection reset')
        return noContent()
      }
      return json(view())
    })
    renderPanel()
    const user = userEvent.setup()
    await fillAndApprove(user)
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    expect((screen.getByLabelText(T.reasonLabel) as HTMLTextAreaElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: T.retrySame }))
    expect(await screen.findByText(T.approved)).toBeTruthy()
    const [first, second] = puts(calls)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('offers no approval without the merge grant and lists the candidates read-only', async () => {
    const calls = install(() => json(view({ viewer: { canApproveMerge: false } })))
    renderPanel()
    expect(await screen.findByText(T.readOnlyNote)).toBeTruthy()
    expect(screen.getByText(`${candidate.code} — ${candidate.legalName}`)).toBeTruthy()
    expect(screen.queryByLabelText(T.reasonLabel)).toBeNull()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(puts(calls)).toHaveLength(0)
  })

  it('shows the empty state when the company holds no same-VKN duplicate', async () => {
    install(() => json(view({ candidates: [] })))
    renderPanel()
    expect(await screen.findByText(T.noCandidates)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
  })

  it('shows the target of a merged card, without its code when this company does not hold it, and no unmerge', async () => {
    install(() =>
      json(view({ mergedInto: { partyId: DUPLICATE, code: 'C-000002' }, candidates: [], viewer: { canApproveMerge: false } })),
    )
    renderPanel()
    expect(await screen.findByText(T.mergedInto)).toBeTruthy()
    expect(screen.getByText('C-000002')).toBeTruthy()
    expect(screen.queryByRole('button')).toBeNull()
    cleanup()

    install(() =>
      json(view({ mergedInto: { partyId: DUPLICATE, code: null }, candidates: [], viewer: { canApproveMerge: false } })),
    )
    renderPanel()
    expect(await screen.findByText(T.mergedIntoHidden)).toBeTruthy()
  })

  it('says forbidden on a 403 read, not-found on a 404 read, and offers a retry on any other failure', async () => {
    install(() => json({ code: 'forbidden' }, 403))
    renderPanel()
    expect(await screen.findByText(T.forbidden)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.readAgain })).toBeNull()
    expect(authBoundary.report).toHaveBeenCalledWith(403, authBoundary.snapshot)
    cleanup()

    install(() => json({ code: 'not_found' }, 404))
    renderPanel()
    expect(await screen.findByText(T.notFound)).toBeTruthy()
    cleanup()

    install(() => json({}, 500))
    renderPanel()
    expect(await screen.findByText(T.loadError)).toBeTruthy()
    expect(screen.getByRole('button', { name: T.readAgain })).toBeTruthy()
  })

  it('treats a read for another company as malformed, never as this record', async () => {
    install(() => json(view({ companyId: 'bbbbbbbb-0000-0000-0000-000000000002' })))
    renderPanel()
    expect(await screen.findByText(T.loadError)).toBeTruthy()
    expect(screen.queryByLabelText(T.reasonLabel)).toBeNull()
  })
  it('reads the exact selected pair and shows complete holding accounts and unchanged-history effects before confirmation', async () => {
    const calls = install(() => json(view()))
    renderPanel(); const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.candidateLabel), DUPLICATE)
    const region = await screen.findByRole('region', { name: I.heading })
    expect(region.textContent).toContain(I.history); expect(region.textContent).toContain(I.effect)
    for (const row of impact().holdingAccounts) expect(region.textContent).toContain(row.accountId)
    const request = calls.find(call => call.url.endsWith('/preview'))!
    expect(request.method).toBe('POST'); expect(request.headers['X-Company-Id']).toBe(COMPANY)
    expect(request.headers['Idempotency-Key']).toBeUndefined()
    expect(request.body).toEqual({ mergedPartyId: DUPLICATE, expectedVersion: '3', mergedPartyExpectedVersion: '5' })
    expect(puts(calls)).toHaveLength(0)
  })
  it.each([403,409,503,204])('blocks NEW confirmation on preview HTTP %s, without treating it as an empty impact', async status => {
    const calls = install(() => json(view()), () => status === 204 ? noContent() : json({}, status))
    renderPanel(); const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.candidateLabel), DUPLICATE)
    await screen.findByText(I.unavailable)
    expect((screen.getByLabelText(T.confirmLabel) as HTMLInputElement).disabled).toBe(true)
    await user.type(screen.getByLabelText(T.reasonLabel), 'Mükerrer kayıt')
    await user.click(screen.getByRole('button', { name: T.approve }))
    expect(puts(calls)).toHaveLength(0)
    if (status === 403) expect(authBoundary.report).toHaveBeenCalledWith(403, authBoundary.snapshot)
  })
  it.each(['foreign-company','stale-version','partial'])('refuses %s preview rather than allowing confirmation', async failure => {
    const calls = install(() => json(view()), () => {
      const body = impact()
      return json(failure === 'foreign-company' ? { ...body, companyId: DUPLICATE }
        : failure === 'stale-version' ? { ...body, merged: { ...body.merged, version: '6' } }
          : { ...body, holdingAccounts: [] })
    })
    renderPanel(); const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.candidateLabel), DUPLICATE)
    await screen.findByText(I.unavailable)
    expect((screen.getByLabelText(T.confirmLabel) as HTMLInputElement).disabled).toBe(true)
    expect(puts(calls)).toHaveLength(0)
  })
  it('hides late/cached current preview and does not reread it to retry a frozen unknown PUT', async () => {
    let attempts = 0
    const calls = install(call => {
      if (call.method !== 'PUT') return json(view({ candidates: attempts > 1 ? [] : [candidate] }))
      attempts++; if (attempts === 1) throw new TypeError('lost reply')
      return noContent()
    })
    renderPanel(); const user = userEvent.setup(); await fillAndApprove(user)
    await screen.findByText(T.unknownOutcome)
    expect(screen.queryByRole('region', { name: I.heading })).toBeNull()
    const previewsBeforeRetry = calls.filter(call => call.url.endsWith('/preview')).length
    await user.click(screen.getByRole('button', { name: T.retrySame }))
    await screen.findByText(T.approved)
    expect(calls.filter(call => call.url.endsWith('/preview'))).toHaveLength(previewsBeforeRetry)
    expect(puts(calls)).toHaveLength(2)
    expect(puts(calls)[1].body).toEqual(puts(calls)[0].body)
    expect(puts(calls)[1].headers['Idempotency-Key']).toBe(puts(calls)[0].headers['Idempotency-Key'])
  })

  it('hides a cached current native response received after the original command became unknown', async () => {
    const calls = install(call => {
      if (call.method === 'PUT') throw new TypeError('lost response')
      return json(view())
    })
    const { queryClient } = renderPanel(); const user = userEvent.setup(); await fillAndApprove(user)
    await screen.findByText(T.unknownOutcome)
    // A disabled query can still retain or receive cached data. It must not replace the attempted pair.
    await act(async () => {
      queryClient.setQueryData(['ns-test', 0, 'counterparty-party-merge', PARTY, COMPANY], { type: 'ok', data: view({ version: '999', candidates: [] }) })
      queryClient.setQueryData(['ns-test', 0, 'counterparty-merge-impact', COMPANY, PARTY, '3', DUPLICATE, '5'], { type: 'ok', data: { ...impact(), merged: { ...impact().merged, legalName: 'SONRAKI CARİ' } } })
    })
    expect(screen.queryByRole('region', { name: I.heading })).toBeNull()
    expect(screen.queryByText('SONRAKI CARİ')).toBeNull()
    expect(screen.getByRole('button', { name: T.retrySame })).toBeTruthy()
    expect(puts(calls)[0].body).toMatchObject({ expectedVersion: '3', mergedPartyExpectedVersion: '5' })
  })

  it('clears selected impact across company/session namespaces and does not reuse an old confirmation', async () => {
    const calls = install(() => json(view()))
    const { rerender, queryClient } = renderPanel(); const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.candidateLabel), DUPLICATE)
    await screen.findByRole('region', { name: I.heading })
    await user.click(screen.getByLabelText(T.confirmLabel))
    rerender(<QueryClientProvider client={queryClient}><ShellDispatchContext.Provider value={() => undefined}>
      <CariMergePanel partyId={PARTY} companyId={DUPLICATE} namespace="different-company-session" entryGeneration={2} contractContext={null} />
    </ShellDispatchContext.Provider></QueryClientProvider>)
    expect(screen.queryByRole('region', { name: I.heading })).toBeNull()
    await screen.findByText(T.loadError)
    expect(screen.queryByLabelText(T.confirmLabel)).toBeNull()
    expect(puts(calls)).toHaveLength(0)
  })

  it('does not promote cached successful impact after an unexpected refetch error', async () => {
    let reads = 0
    install(() => json(view()), () => { if (++reads === 1) return json(impact()); throw new Error('SENTETIK read failure') })
    const { queryClient } = renderPanel(); const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.candidateLabel), DUPLICATE)
    await screen.findByRole('region', { name: I.heading }); await user.click(screen.getByLabelText(T.confirmLabel))
    await act(async () => { await queryClient.refetchQueries({ queryKey: ['ns-test', 0, 'counterparty-merge-impact'] }) })
    await screen.findByText(I.unavailable)
    expect(screen.queryByRole('region', { name: I.heading })).toBeNull()
    expect((screen.getByLabelText(T.confirmLabel) as HTMLInputElement).disabled).toBe(true)
  })

})
