import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CariLegalNamePanel } from './CariLegalNamePanel'
import { LEGAL_NAME_MAX, LEGAL_NAME_REASON_MAX, LEGAL_NAME_TEXT as T } from './legalName'
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
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const CONTRACT = { origin: '/api/v1', namespace: 'ns-test' }
const LEGAL = 'Örnek Ticaret Anonim Şirketi'

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

const REQUEST_ID = 'cccccccc-0000-0000-0000-000000000001'
const PROPOSED = 'Yeni Örnek Sanayi Anonim Şirketi'

const pendingChange = (over: Record<string, unknown> = {}) => ({
  id: REQUEST_ID,
  status: 'pending',
  currentLegalName: LEGAL,
  proposedLegalName: PROPOSED,
  reason: 'Ticaret sicili değişikliği',
  requestedBy: 'maker-user',
  requestedAtUtc: '2026-11-16T09:00:00+00:00',
  decidedBy: null,
  decidedAtUtc: null,
  decisionNote: null,
  ...over,
})

const viewer = (over: Record<string, unknown> = {}) => ({
  canPropose: false,
  canApprove: false,
  isRequesterOfPending: false,
  ...over,
})

const view = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  version: '3',
  legalName: LEGAL,
  pendingChange: null,
  lastDecidedChange: null,
  viewer: viewer({ canPropose: true }),
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
        <CariLegalNamePanel
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

const posts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'POST')
const reads = (calls: readonly Call[]) =>
  calls.filter((call) => call.method === 'GET' && call.url.endsWith('/legal-name'))

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

describe('ALS-D111 + ALS-D113 Cari card legal name maker-checker', () => {
  it('shows loading, then proposes a new legal name with a reason, one key and the party version; the name stays', async () => {
    let proposed = false
    const onChanged = vi.fn()
    const calls = install((call) => {
      if (call.method === 'POST') {
        proposed = true
        return noContent()
      }
      return json(
        proposed
          ? view({ version: '4', pendingChange: pendingChange(), viewer: viewer({ canPropose: true, isRequesterOfPending: true }) })
          : view(),
      )
    })
    renderPanel(onChanged)
    expect(screen.getByText(T.loading)).toBeTruthy()

    const input = (await screen.findByLabelText(T.proposedLabel)) as HTMLInputElement
    expect(input.value).toBe('')
    expect(input.maxLength).toBe(LEGAL_NAME_MAX)
    expect((screen.getByLabelText(T.reasonLabel) as HTMLTextAreaElement).maxLength).toBe(LEGAL_NAME_REASON_MAX)

    const user = userEvent.setup()
    await user.type(input, `  ${PROPOSED}  `)
    await user.type(screen.getByLabelText(T.reasonLabel), ' Ticaret sicili değişikliği ')
    await user.click(screen.getByRole('button', { name: T.propose }))

    expect(await screen.findByText(T.proposed)).toBeTruthy()
    const [post] = posts(calls)
    expect(posts(calls)).toHaveLength(1)
    expect(post.url).toBe(`/api/v1/counterparties/${PARTY}/legal-name/change-requests`)
    expect(post.body).toEqual({
      proposedLegalName: PROPOSED,
      reason: 'Ticaret sicili değişikliği',
      expectedVersion: '3',
    })
    expect(post.headers['Idempotency-Key']).toMatch(/^[0-9a-f]{32}$/)
    expect(post.headers['X-Company-Id']).toBe(COMPANY)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
    expect(onChanged).toHaveBeenCalledTimes(1)

    // The name did not change; the proposer sees the pending request and no decision buttons.
    expect(await screen.findByText(T.ownPendingNote)).toBeTruthy()
    expect(document.querySelector('[data-legal-name]')?.textContent).toBe(LEGAL)
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
    expect(screen.queryByRole('button', { name: T.reject })).toBeNull()
    expect(screen.queryByLabelText(T.proposedLabel)).toBeNull()
  })

  it('refuses a blank name, the current name or a blank reason in the form and sends nothing', async () => {
    const calls = install(() => json(view()))
    renderPanel()
    const user = userEvent.setup()
    const input = await screen.findByLabelText(T.proposedLabel)
    await user.type(input, '   ')
    await user.click(screen.getByRole('button', { name: T.propose }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.required)

    await user.clear(input)
    await user.type(input, ` ${LEGAL} `)
    await user.click(screen.getByRole('button', { name: T.propose }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.sameAsCurrent)

    await user.clear(input)
    await user.type(input, PROPOSED)
    await user.click(screen.getByRole('button', { name: T.propose }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.reasonRequired)
    expect(posts(calls)).toHaveLength(0)
  })

  it('lets another approver approve the pending request with a note, fenced on the party version', async () => {
    let decided = false
    const calls = install((call) => {
      if (call.method === 'POST') {
        decided = true
        return noContent()
      }
      return json(
        decided
          ? view({
              version: '5',
              legalName: PROPOSED,
              lastDecidedChange: pendingChange({
                status: 'approved',
                decidedBy: 'checker-user',
                decidedAtUtc: '2026-11-16T10:00:00+00:00',
                decisionNote: 'Sicil gazetesi kontrol edildi',
              }),
              viewer: viewer({ canApprove: true }),
            })
          : view({ version: '4', pendingChange: pendingChange(), viewer: viewer({ canApprove: true }) }),
      )
    })
    renderPanel()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.noteLabel), ' Sicil gazetesi kontrol edildi ')
    await user.click(screen.getByRole('button', { name: T.approve }))

    expect(await screen.findByText(T.approved)).toBeTruthy()
    const [post] = posts(calls)
    expect(post.url).toBe(`/api/v1/counterparties/${PARTY}/legal-name/change-decisions`)
    expect(post.body).toEqual({
      changeRequestId: REQUEST_ID,
      decision: 'approve',
      note: 'Sicil gazetesi kontrol edildi',
      expectedVersion: '4',
    })
    await waitFor(() => expect(document.querySelector('[data-legal-name]')?.textContent).toBe(PROPOSED))
    expect(document.querySelector('[data-legal-name-last-decided="approved"]')).not.toBeNull()
  })

  it('rejects without a note by sending no note at all', async () => {
    const calls = install((call) =>
      call.method === 'POST'
        ? noContent()
        : json(view({ version: '4', pendingChange: pendingChange(), viewer: viewer({ canApprove: true }) })),
    )
    renderPanel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.reject }))
    expect(await screen.findByText(T.rejected)).toBeTruthy()
    expect(posts(calls)[0].body).toEqual({ changeRequestId: REQUEST_ID, decision: 'reject', expectedVersion: '4' })
  })

  it('shows a waiting note to a viewer who may propose but not approve, and offers no second proposal', async () => {
    install(() => json(view({ pendingChange: pendingChange(), viewer: viewer({ canPropose: true }) })))
    renderPanel()
    expect(await screen.findByText(T.waitingNote)).toBeTruthy()
    expect(screen.getByText(T.pendingBlocksPropose)).toBeTruthy()
    expect(screen.queryByLabelText(T.proposedLabel)).toBeNull()
    expect(screen.queryByRole('button', { name: T.approve })).toBeNull()
  })

  it('names a stale request and a self-decision from the server and reads again', async () => {
    let code = 'legal_name_change_stale'
    const calls = install((call) =>
      call.method === 'POST'
        ? json({ code }, 409)
        : json(view({ version: '4', pendingChange: pendingChange(), viewer: viewer({ canApprove: true }) })),
    )
    renderPanel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.approve }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.refusal.legal_name_change_stale)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))

    code = 'legal_name_self_decision'
    await user.click(await screen.findByRole('button', { name: T.reject }))
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveProperty('textContent', T.refusal.legal_name_self_decision),
    )
  })

  it('names a version conflict on a proposal and reads the legal name again', async () => {
    const calls = install((call) => (call.method === 'POST' ? json({ code: 'version_conflict' }, 409) : json(view())))
    renderPanel()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.proposedLabel), PROPOSED)
    await user.type(screen.getByLabelText(T.reasonLabel), 'Gerekçe')
    await user.click(screen.getByRole('button', { name: T.propose }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.refusal.version_conflict)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
  })

  it('says the command is forbidden on a 403 and reports it to the auth boundary', async () => {
    install((call) => (call.method === 'POST' ? json({}, 403) : json(view())))
    renderPanel()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.proposedLabel), PROPOSED)
    await user.type(screen.getByLabelText(T.reasonLabel), 'Gerekçe')
    await user.click(screen.getByRole('button', { name: T.propose }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.commandForbidden)
    expect(authBoundary.report).toHaveBeenCalledWith(403, authBoundary.snapshot)
  })

  it('keeps the same key and body for an explicit retry when the answer was lost', async () => {
    let attempts = 0
    const calls = install((call) => {
      if (call.method === 'POST') {
        attempts += 1
        if (attempts === 1) throw new TypeError('connection reset')
        return noContent()
      }
      return json(view())
    })
    renderPanel()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.proposedLabel), PROPOSED)
    await user.type(screen.getByLabelText(T.reasonLabel), 'Gerekçe')
    await user.click(screen.getByRole('button', { name: T.propose }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    expect((screen.getByLabelText(T.proposedLabel) as HTMLInputElement).disabled).toBe(true)

    await user.click(screen.getByRole('button', { name: T.retrySame }))
    expect(await screen.findByText(T.proposed)).toBeTruthy()
    const [first, second] = posts(calls)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('offers nothing without the separate grants and shows the stored legal name', async () => {
    const calls = install(() => json(view({ viewer: viewer() })))
    renderPanel()
    expect(await screen.findByText(T.readOnlyNote)).toBeTruthy()
    expect(document.querySelector('[data-legal-name]')?.textContent).toBe(LEGAL)
    expect(screen.queryByLabelText(T.proposedLabel)).toBeNull()
    expect(screen.queryByRole('button', { name: T.propose })).toBeNull()
    expect(posts(calls)).toHaveLength(0)
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
    expect(screen.queryByLabelText(T.proposedLabel)).toBeNull()
  })
})
