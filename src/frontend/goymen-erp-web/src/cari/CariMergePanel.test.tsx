import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CariMergePanel } from './CariMergePanel'
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

function install(handler: (call: Call) => Response | Promise<Response>) {
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

function renderPanel(onChanged: () => void = () => undefined) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
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
}

const puts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'PUT')
const reads = (calls: readonly Call[]) => calls.filter((call) => call.method === 'GET' && call.url.endsWith('/merge'))

async function fillAndApprove(user: ReturnType<typeof userEvent.setup>, reason = '  Aynı firma iki kez açılmış.  ') {
  await user.selectOptions(await screen.findByLabelText(T.candidateLabel), DUPLICATE)
  await user.type(screen.getByLabelText(T.reasonLabel), reason)
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
})
