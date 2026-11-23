import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CariGroupCompanyLinkPanel } from './CariGroupCompanyLinkPanel'
import { GROUP_COMPANY_LINK_TEXT as T } from './groupCompanyLink'
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

const SISTER = 'bbbbbbbb-0000-0000-0000-000000000002'
const view = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  version: '3',
  legalName: 'SENTETIK Kardeş Sanayi Anonim Şirketi',
  groupCompanyId: null,
  viewer: { canUpdateGroupCompanyLink: true },
  ...over,
})
const directory = [
  { id: COMPANY, code: 'GSP', legalName: 'SENTETIK GSP', isPlaceholder: false },
  { id: SISTER, code: 'GKS', legalName: 'SENTETIK Kardeş', isPlaceholder: false },
]

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
      if (call.url.endsWith('/organization/companies')) return json(directory)
      return handler(call)
    }),
  )
  return calls
}

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
    <QueryClientProvider client={queryClient}>
      <ShellDispatchContext.Provider value={() => undefined}>
        <CariGroupCompanyLinkPanel partyId={PARTY} companyId={COMPANY} namespace="ns-test" contractContext={CONTRACT} />
      </ShellDispatchContext.Provider>
    </QueryClientProvider>,
  )
}

const puts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'PUT')
const reads = (calls: readonly Call[]) => calls.filter((call) => call.method === 'GET' && call.url.endsWith('/group-company-link'))

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

describe('YOL-D31 Cari card group-company link', () => {
  it('links the party to a group company from the directory with one key and the party version, then reads again', async () => {
    let linked: string | null = null
    const calls = install((call) => {
      if (call.method === 'PUT') {
        linked = (call.body as { groupCompanyId: string | null }).groupCompanyId
        return noContent()
      }
      return json(linked === null ? view() : view({ version: '4', groupCompanyId: linked }))
    })
    renderPanel()
    expect(screen.getByText(T.loading)).toBeTruthy()
    const select = (await screen.findByLabelText(T.label)) as HTMLSelectElement
    await waitFor(() => expect(select.options.length).toBe(3))
    expect(select.value).toBe('')
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
    const user = userEvent.setup()
    await user.selectOptions(select, SISTER)
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [put] = puts(calls)
    expect(put.url).toBe(`/api/v1/counterparties/${PARTY}/group-company-link`)
    expect(put.body).toEqual({ groupCompanyId: SISTER, expectedVersion: '3' })
    expect(put.headers['Idempotency-Key']).toMatch(/^[0-9a-f]{32}$/)
    expect(put.headers['X-Company-Id']).toBe(COMPANY)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
  })

  it('clears the link with an explicit null and names a refused target', async () => {
    const calls = install((call) => (call.method === 'PUT' ? noContent() : json(view({ groupCompanyId: SISTER }))))
    renderPanel()
    const user = userEvent.setup()
    const select = (await screen.findByLabelText(T.label)) as HTMLSelectElement
    await waitFor(() => expect(select.value).toBe(SISTER))
    await user.selectOptions(select, '')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.cleared)).toBeTruthy()
    expect(puts(calls)[0].body).toEqual({ groupCompanyId: null, expectedVersion: '3' })
    cleanup()

    install((call) => (call.method === 'PUT' ? json({ code: 'validation_failed' }, 400) : json(view())))
    renderPanel()
    const again = (await screen.findByLabelText(T.label)) as HTMLSelectElement
    await waitFor(() => expect(again.options.length).toBe(3))
    await user.selectOptions(again, SISTER)
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.refusal.validation_failed)
  })

  it('keeps the same key for an explicit retry when the answer was lost', async () => {
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
    const select = (await screen.findByLabelText(T.label)) as HTMLSelectElement
    await waitFor(() => expect(select.options.length).toBe(3))
    await user.selectOptions(select, SISTER)
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.retrySame }))
    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [first, second] = puts(calls)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('shows the stored link read-only without the grant and never offers a change', async () => {
    const calls = install(() => json(view({ groupCompanyId: SISTER, viewer: { canUpdateGroupCompanyLink: false } })))
    renderPanel()
    expect(await screen.findByText(T.readOnlyNote)).toBeTruthy()
    await waitFor(() => expect(screen.getByText('SENTETIK Kardeş')).toBeTruthy())
    expect(screen.queryByLabelText(T.label, { selector: 'select' })).toBeNull()
    expect(puts(calls)).toHaveLength(0)
  })

  it('says forbidden on a 403 read and treats another company\'s answer as malformed', async () => {
    install(() => json({ code: 'forbidden' }, 403))
    renderPanel()
    expect(await screen.findByText(T.forbidden)).toBeTruthy()
    expect(authBoundary.report).toHaveBeenCalledWith(403, authBoundary.snapshot)
    cleanup()

    install(() => json(view({ companyId: SISTER })))
    renderPanel()
    expect(await screen.findByText(T.loadError)).toBeTruthy()
  })
})
