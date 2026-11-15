import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CariLegalNamePanel } from './CariLegalNamePanel'
import { LEGAL_NAME_MAX, LEGAL_NAME_TEXT as T } from './legalName'
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

const view = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  version: '3',
  legalName: LEGAL,
  viewer: { canUpdateLegalName: true },
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

const puts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'PUT')
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

describe('F-027 / ALS-D95 Cari card legal name change', () => {
  it('shows loading, then saves a new legal name in one step with one key and the party version', async () => {
    let saved = LEGAL
    const onChanged = vi.fn()
    const calls = install((call) => {
      if (call.method === 'PUT') {
        saved = (call.body as { legalName: string }).legalName
        return noContent()
      }
      return json(saved === LEGAL ? view() : view({ version: '4', legalName: saved }))
    })
    renderPanel(onChanged)
    expect(screen.getByText(T.loading)).toBeTruthy()

    const input = (await screen.findByLabelText(T.legalNameLabel)) as HTMLInputElement
    expect(input.value).toBe(LEGAL)
    expect(input.maxLength).toBe(LEGAL_NAME_MAX)
    // Nothing changed yet: nothing to send.
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)

    const user = userEvent.setup()
    await user.clear(input)
    await user.type(input, '  Yeni Örnek Sanayi Anonim Şirketi  ')
    await user.click(screen.getByRole('button', { name: T.save }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [put] = puts(calls)
    expect(puts(calls)).toHaveLength(1)
    expect(put.url).toBe(`/api/v1/counterparties/${PARTY}/legal-name`)
    // Trimmed as the server will store it; there is no approval step and no other field travels.
    expect(put.body).toEqual({ legalName: 'Yeni Örnek Sanayi Anonim Şirketi', expectedVersion: '3' })
    expect(put.headers['Idempotency-Key']).toMatch(/^[0-9a-f]{32}$/)
    expect(put.headers['X-Company-Id']).toBe(COMPANY)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
    expect(onChanged).toHaveBeenCalledTimes(1)
  })

  it('refuses a blank legal name in the form and sends nothing', async () => {
    const calls = install(() => json(view()))
    renderPanel()
    const user = userEvent.setup()
    const input = await screen.findByLabelText(T.legalNameLabel)
    await user.clear(input)
    await user.type(input, '   ')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.required)
    expect(puts(calls)).toHaveLength(0)
  })

  it('does not send a value that only differs by surrounding blanks', async () => {
    const calls = install(() => json(view()))
    renderPanel()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.legalNameLabel), '   ')
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
    expect(puts(calls)).toHaveLength(0)
  })

  it('names a version conflict and reads the legal name again', async () => {
    const calls = install((call) => (call.method === 'PUT' ? json({ code: 'version_conflict' }, 409) : json(view())))
    renderPanel()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.legalNameLabel), ' Ltd')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.refusal.version_conflict)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
  })

  it('says the command is forbidden on a 403 and reports it to the auth boundary', async () => {
    install((call) => (call.method === 'PUT' ? json({}, 403) : json(view())))
    renderPanel()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.legalNameLabel), ' Ltd')
    await user.click(screen.getByRole('button', { name: T.save }))
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
    await user.type(await screen.findByLabelText(T.legalNameLabel), ' Ltd')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    expect((screen.getByLabelText(T.legalNameLabel) as HTMLInputElement).disabled).toBe(true)

    await user.click(screen.getByRole('button', { name: T.retrySame }))
    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [first, second] = puts(calls)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('offers no field without the separate legal-name grant and shows the stored legal name', async () => {
    const calls = install(() => json(view({ viewer: { canUpdateLegalName: false } })))
    renderPanel()
    expect(await screen.findByText(T.readOnlyNote)).toBeTruthy()
    expect(screen.getByText(LEGAL)).toBeTruthy()
    expect(screen.queryByLabelText(T.legalNameLabel)).toBeNull()
    expect(screen.queryByRole('button', { name: T.save })).toBeNull()
    expect(puts(calls)).toHaveLength(0)
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
    expect(screen.queryByLabelText(T.legalNameLabel)).toBeNull()
  })
})
