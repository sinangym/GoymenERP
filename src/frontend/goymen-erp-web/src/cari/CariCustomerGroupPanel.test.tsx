import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { CariCustomerGroupPanel } from './CariCustomerGroupPanel'
import { CUSTOMER_GROUP_TEXT as T } from './customerGroup'
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
const ACCOUNT = '44444444-4444-4444-4444-444444444444'
const WHOLESALE = '55555555-5555-4555-8555-555555555555'
const RETAIL = '66666666-6666-4666-8666-666666666666'
const OLD = '77777777-7777-4777-8777-777777777777'
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

const group = (id: string, name: string, isActive = true, version = '1') => ({ id, name, isActive, version })
const groups = (canManage = false) => ({
  companyId: COMPANY,
  items: [group(RETAIL, 'Perakendeci'), group(WHOLESALE, 'Toptancı'), group(OLD, 'Eski bayi', false)],
  viewer: { canManage },
})
const account = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  accountId: ACCOUNT,
  companyId: COMPANY,
  version: '1',
  accountIsActive: true,
  customerGroup: null,
  viewer: { canAssign: true },
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

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const router = createMemoryRouter(
    [
      {
        path: '*',
        element: (
          <CariCustomerGroupPanel
            partyId={PARTY}
            companyId={COMPANY}
            namespace="ns-test"
            contractContext={CONTRACT}
            canOpenGroupList
          />
        ),
      },
    ],
    { initialEntries: [`/cari/${PARTY}`] },
  )
  render(
    <QueryClientProvider client={queryClient}>
      <ShellDispatchContext.Provider value={() => undefined}>
        <RouterProvider router={router} />
      </ShellDispatchContext.Provider>
    </QueryClientProvider>,
  )
}

const puts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'PUT')
const accountReads = (calls: readonly Call[]) =>
  calls.filter((call) => call.method === 'GET' && call.url.endsWith('/company-account/customer-group'))

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

describe('SAT-D57 customer group on the Cari card', () => {
  it('shows loading, then "no group", and assigns an active group with one key and the account version', async () => {
    let assigned: string | null = null
    const calls = install((call) => {
      if (call.method === 'PUT') {
        assigned = (call.body as { customerGroupId: string | null }).customerGroupId
        return noContent()
      }
      if (call.url.endsWith('/customer-groups')) return json(groups())
      return json(
        assigned === null ? account() : account({ version: '2', customerGroup: group(WHOLESALE, 'Toptancı') }),
      )
    })
    renderPanel()
    expect(screen.getByText(T.panelLoading)).toBeTruthy()
    expect(await screen.findByText(T.none)).toBeTruthy()

    const select = await screen.findByLabelText(T.selectLabel)
    const options = within(select).getAllByRole('option').map((option) => option.textContent)
    // A passive group that is not the account's own is never offered.
    expect(options).toEqual([T.noneOption, 'Perakendeci', 'Toptancı'])
    expect((screen.getByRole('button', { name: T.assign }) as HTMLButtonElement).disabled).toBe(true)

    const user = userEvent.setup()
    await user.selectOptions(select, WHOLESALE)
    await user.click(screen.getByRole('button', { name: T.assign }))

    expect(await screen.findByText(T.assigned)).toBeTruthy()
    const [put] = puts(calls)
    expect(put.url).toBe(`/api/v1/counterparties/${PARTY}/company-account/customer-group`)
    expect(put.body).toEqual({ customerGroupId: WHOLESALE, expectedVersion: '1' })
    expect(put.headers['Idempotency-Key']).toMatch(/^[0-9a-f]{32}$/)
    expect(put.headers['X-Company-Id']).toBe(COMPANY)
    await waitFor(() => expect(screen.getByText('Toptancı', { selector: '[data-customer-group]' })).toBeTruthy())
    expect(accountReads(calls).length).toBeGreaterThanOrEqual(2)
  })

  it('clears the group with an explicit null and keeps a passive current group visible and marked', async () => {
    const calls = install((call) => {
      if (call.method === 'PUT') return noContent()
      if (call.url.endsWith('/customer-groups')) return json(groups())
      return json(account({ version: '4', customerGroup: group(OLD, 'Eski bayi', false) }))
    })
    renderPanel()
    expect(await screen.findByText(`Eski bayi${T.passiveSuffix}`, { selector: '[data-customer-group]' })).toBeTruthy()
    const select = await screen.findByLabelText(T.selectLabel)
    expect(within(select).getAllByRole('option').map((option) => option.textContent)).toContain(`Eski bayi${T.passiveSuffix}`)

    const user = userEvent.setup()
    await user.selectOptions(select, '')
    await user.click(screen.getByRole('button', { name: T.assign }))
    expect(await screen.findByText(T.cleared)).toBeTruthy()
    expect(puts(calls)[0].body).toEqual({ customerGroupId: null, expectedVersion: '4' })
  })

  it('names a version conflict and reads the account again', async () => {
    const calls = install((call) => {
      if (call.method === 'PUT') return json({ code: 'version_conflict' }, 409)
      if (call.url.endsWith('/customer-groups')) return json(groups())
      return json(account())
    })
    renderPanel()
    const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.selectLabel), RETAIL)
    await user.click(screen.getByRole('button', { name: T.assign }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.refusal.version_conflict)
    await waitFor(() => expect(accountReads(calls).length).toBeGreaterThanOrEqual(2))
  })

  it('keeps the same key for an explicit retry when the answer was lost', async () => {
    let attempts = 0
    const calls = install((call) => {
      if (call.method === 'PUT') {
        attempts += 1
        if (attempts === 1) throw new TypeError('connection reset')
        return noContent()
      }
      if (call.url.endsWith('/customer-groups')) return json(groups())
      return json(account())
    })
    renderPanel()
    const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.selectLabel), RETAIL)
    await user.click(screen.getByRole('button', { name: T.assign }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    expect((screen.getByLabelText(T.selectLabel) as HTMLSelectElement).disabled).toBe(true)

    await user.click(screen.getByRole('button', { name: T.retrySame }))
    expect(await screen.findByText(T.assigned)).toBeTruthy()
    const [first, second] = puts(calls)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('offers no selector without the assign grant and reads no list', async () => {
    const calls = install(() => json(account({ viewer: { canAssign: false }, customerGroup: group(RETAIL, 'Perakendeci') })))
    renderPanel()
    expect(await screen.findByText('Perakendeci')).toBeTruthy()
    expect(screen.queryByLabelText(T.selectLabel)).toBeNull()
    expect(calls.some((call) => call.url.endsWith('/customer-groups'))).toBe(false)
  })

  it('says forbidden on a 403 read and offers a retry on any other failure', async () => {
    install(() => json({ code: 'forbidden' }, 403))
    renderPanel()
    expect(await screen.findByText(T.panelForbidden)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.readAgain })).toBeNull()
    cleanup()

    install(() => json({}, 500))
    renderPanel()
    expect(await screen.findByText(T.panelError)).toBeTruthy()
    expect(screen.getByRole('button', { name: T.readAgain })).toBeTruthy()
  })
})
