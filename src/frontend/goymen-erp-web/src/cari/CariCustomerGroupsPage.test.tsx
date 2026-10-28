import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { CariCustomerGroupsPage } from './CariCustomerGroupsPage'
import { CUSTOMER_GROUP_TEXT as T } from './customerGroup'
import { COUNTERPARTY_CONTRACT_HEADER, COUNTERPARTY_CONTRACT_VERSION, resetContractReadiness } from './clientContract'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const WHOLESALE = '55555555-5555-4555-8555-555555555555'

const shell = vi.hoisted(() => ({
  state: { activeContext: { companyId: 'aaaaaaaa-0000-0000-0000-000000000001', scopeMode: 'single' } },
}))

vi.mock('../shell/shellContext', () => ({
  useShellState: () => shell.state,
  useShellDispatch: () => () => undefined,
}))

vi.mock('../shell/shellState', async (original) => ({
  ...(await original<typeof import('../shell/shellState')>()),
  contextKey: () => ({ namespace: 'ns-test' }),
  contextKeyString: () => 'ns-test',
  hasCapability: () => true,
}))

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({}), report: () => undefined }),
}))

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

const list = (items: unknown[], canManage = true) => ({ companyId: COMPANY, items, viewer: { canManage } })

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

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  const router = createMemoryRouter([{ path: '*', element: <CariCustomerGroupsPage /> }], {
    initialEntries: ['/cari/musteri-gruplari'],
  })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

beforeEach(() => {
  vi.stubGlobal('navigator', { onLine: true })
  resetContractReadiness()
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  resetContractReadiness()
})

describe('SAT-D57 company customer group list', () => {
  it('starts empty (nothing seeded), creates a group with a fresh UUID and one key, then reads again', async () => {
    const stored: { id: string; name: string }[] = []
    const calls = install((call) => {
      if (call.method === 'POST') {
        stored.push(call.body as { id: string; name: string })
        return noContent()
      }
      return json(list(stored.map((group) => ({ ...group, isActive: true, version: '1' }))))
    })
    renderPage()
    expect(screen.getByRole('status')).toHaveProperty('textContent', T.loading)
    expect(await screen.findByText(new RegExp(T.empty))).toBeTruthy()

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: T.add }))
    expect(await screen.findByText(T.nameEmpty)).toBeTruthy()
    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(0)

    await user.type(screen.getByLabelText(T.nameLabel), '  Toptancı ')
    await user.click(screen.getByRole('button', { name: T.add }))
    expect(await screen.findByText(T.created)).toBeTruthy()

    const post = calls.find((call) => call.method === 'POST')!
    expect(post.url).toBe('/api/v1/counterparties/customer-groups')
    const body = post.body as { id: string; name: string }
    expect(body.name).toBe('Toptancı')
    expect(body.id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(post.headers['Idempotency-Key']).toMatch(/^[0-9a-f]{32}$/)
    expect(post.headers['X-Company-Id']).toBe(COMPANY)
    expect(await screen.findByRole('cell', { name: 'Toptancı' })).toBeTruthy()
  })

  it('renames and deactivates with the group version, and names a taken name', async () => {
    let renamed = false
    const calls = install((call) => {
      if (call.method === 'PUT' && call.url.endsWith('/name')) {
        if (!renamed) {
          renamed = true
          return json({ code: 'customer_group_name_taken' }, 409)
        }
        return noContent()
      }
      if (call.method === 'PUT') return noContent()
      return json(list([{ id: WHOLESALE, name: 'Toptancı', isActive: true, version: '7' }]))
    })
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.renameButtonLabel('Toptancı') }))
    const input = screen.getByLabelText(T.renameLabel('Toptancı'))
    await user.clear(input)
    await user.type(input, 'Perakendeci')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.refusal.customer_group_name_taken)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.renamed)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: T.deactivateLabel('Toptancı') }))
    expect(await screen.findByText(T.deactivated)).toBeTruthy()
    const putsSent = calls.filter((call) => call.method === 'PUT')
    expect(putsSent[0].url).toBe(`/api/v1/counterparties/customer-groups/${WHOLESALE}/name`)
    expect(putsSent[0].body).toEqual({ name: 'Perakendeci', expectedVersion: '7' })
    // A definite refusal ends the attempt: the next submit is a new attempt with a new key.
    expect(putsSent[1].headers['Idempotency-Key']).not.toBe(putsSent[0].headers['Idempotency-Key'])
    expect(putsSent[2].url).toBe(`/api/v1/counterparties/customer-groups/${WHOLESALE}/active-state`)
    expect(putsSent[2].body).toEqual({ isActive: false, expectedVersion: '7' })
  })

  it('is read-only without the manage grant and marks passive groups', async () => {
    install(() => json(list([{ id: WHOLESALE, name: 'Toptancı', isActive: false, version: '2' }], false)))
    renderPage()
    const row = (await screen.findByRole('cell', { name: 'Toptancı' })).closest('tr')!
    expect(within(row).getByText(T.passive)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.add })).toBeNull()
    expect(screen.queryByRole('button', { name: T.reactivateLabel('Toptancı') })).toBeNull()
    expect(screen.getByText(T.readOnlyNote)).toBeTruthy()
  })

  it('says forbidden on a 403 and offers a retry on another failure', async () => {
    install(() => json({}, 403))
    renderPage()
    expect(await screen.findByText(T.forbidden)).toBeTruthy()
    cleanup()

    let failed = false
    install(() => {
      if (!failed) {
        failed = true
        return json({}, 500)
      }
      return json(list([]))
    })
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: T.readAgain }))
    await waitFor(() => expect(screen.getByText(new RegExp(T.empty))).toBeTruthy())
  })
})
