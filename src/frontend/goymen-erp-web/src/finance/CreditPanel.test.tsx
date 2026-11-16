// SENTETIK — synthetic identities and amounts only.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import type { ReactNode } from 'react'
import { CreditPanel } from './CreditPanel'
import { creditPanelUrl } from './CreditPanelHttp'
import { CREDIT_PANEL_TEXT as T, CREDIT_PANEL_REASON } from './CreditPanelText'
import { FINANCE_ROUTE } from './capabilities'

const authBoundary = vi.hoisted(() => {
  const snapshot = { actorId: 'actor-a', namespace: 'ns-test' }
  return { snapshot, capture: vi.fn(() => snapshot), report: vi.fn() }
})

vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: authBoundary.capture, report: authBoundary.report }),
}))

// Outside the Finance routes no currency code is read: the labelled reference is shown.
vi.mock('./currencyCodes', async (original) => {
  const actual = await original<typeof import('./currencyCodes')>()
  return {
    ...actual,
    WithCurrencyCodes: ({ children }: { readonly children: (codes: null) => ReactNode }) => <>{children(null)}</>,
  }
})

const COMPANY = 'aaaaaaaa-0000-4000-8000-000000000001'
const PARTY = '11111111-1111-4111-8111-111111111111'
const ACCOUNT = '44444444-4444-4444-8444-444444444444'
const TRY = '22222222-2222-4222-8222-222222222222'
const USD = '33333333-3333-4333-8333-333333333333'

const available = (...amounts: (readonly [string, string])[]) => ({
  state: 'Available',
  amounts: amounts.map(([currencyId, amount]) => ({ currencyId, amount })),
  reasonCode: null,
})
const notShown = (state: string, reasonCode: string) => ({ state, amounts: [], reasonCode })

const panel = (over: Record<string, unknown> = {}) => ({
  companyId: COMPANY,
  partyId: PARTY,
  companyAccountId: ACCOUNT,
  asOf: '2026-11-15',
  creditLimit: available([TRY, '250000.5']),
  openReceivables: available([TRY, '1500.00'], [USD, '20']),
  heldCollateral: available([TRY, '1000']),
  weightedCollateral: notShown('Unavailable', 'finance_collateral_type_rate_unavailable'),
  collateralUtilization: notShown('Unavailable', 'finance_collateral_utilization_source_unavailable'),
  documentaryRisk: available(),
  ...over,
})

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

interface Call {
  readonly url: string
  readonly init: RequestInit | undefined
}

function install(handler: () => Response | Promise<Response>) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: typeof input === 'string' ? input : input.toString(), init })
    return handler()
  }))
  return calls
}

function renderPanel(links: { readonly limits?: boolean; readonly collaterals?: boolean } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  const router = createMemoryRouter([
    {
      path: '*',
      element: (
        <CreditPanel partyId={PARTY} companyAccountId={ACCOUNT} companyId={COMPANY} namespace="ns-test"
          canOpenCreditLimits={links.limits ?? false} canOpenCollaterals={links.collaterals ?? false} />
      ),
    },
  ])
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return queryClient
}

const panelState = () => screen.getByTestId('credit-panel').getAttribute('data-state')

describe('CreditPanel', () => {
  beforeEach(() => {
    authBoundary.capture.mockClear()
    authBoundary.report.mockClear()
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
    // Drops the own `onLine` an offline test put on the navigator; the prototype's answer is back.
    Reflect.deleteProperty(navigator, 'onLine')
  })

  it('reads the asked account in the shell company, without a cache, and shows every figure as the server sent it', async () => {
    const calls = install(() => json(panel()))
    renderPanel()

    expect(await screen.findByTestId('credit-panel-figures')).toBeTruthy()
    expect(calls).toHaveLength(1)
    expect(calls[0].url).toBe(creditPanelUrl(PARTY, ACCOUNT))
    expect(calls[0].init?.cache).toBe('no-store')
    const headers = (calls[0].init?.headers ?? {}) as Record<string, string>
    expect(headers['X-Company-Id']).toBe(COMPANY)

    expect(screen.getByTestId('credit-panel-creditLimit').textContent).toContain('250.000,5')
    const receivables = screen.getByTestId('credit-panel-openReceivables')
    expect(receivables.querySelectorAll('li')).toHaveLength(2)
    expect(receivables.textContent).toContain('1.500,00')
    expect(screen.getByTestId('credit-panel-heldCollateral').textContent).toContain('1.000')
    expect(screen.getByTestId('credit-panel-documentaryRisk').textContent).toBe(T.zero)
    expect(screen.getByTestId('credit-panel-as-of').textContent).toContain('2026-11-15')
  })

  it('says "hesaplanamadı" with the reason when the collateral cannot be computed, never a zero or a partial sum', async () => {
    install(() => json(panel()))
    renderPanel()

    const weighted = await screen.findByTestId('credit-panel-weightedCollateral')
    expect(weighted.getAttribute('data-figure-state')).toBe('Unavailable')
    expect(weighted.textContent).toContain(T.unavailable)
    expect(weighted.textContent).toContain(CREDIT_PANEL_REASON.finance_collateral_type_rate_unavailable)
    const utilization = screen.getByTestId('credit-panel-collateralUtilization')
    expect(utilization.textContent).toContain(T.unavailable)
    expect(utilization.textContent).not.toContain(T.zero)
  })

  it('shows collateral in another currency as zero cover, never "hesaplanamadı", and says why (FNS-D74)', async () => {
    install(() => json(panel({
      openReceivables: available([TRY, '900']),
      heldCollateral: available([USD, '20']),
      weightedCollateral: available([USD, '20']),
      collateralUtilization: available(),
    })))
    renderPanel()

    const utilization = await screen.findByTestId('credit-panel-collateralUtilization')
    expect(utilization.getAttribute('data-figure-state')).toBe('Available')
    expect(utilization.textContent).toBe(T.zero)
    expect(utilization.textContent).not.toContain(T.unavailable)
    // Each currency on its own line: the dollar collateral is not converted into the lira receivable.
    expect(screen.getByTestId('credit-panel-weightedCollateral').querySelectorAll('li')).toHaveLength(1)
    expect(screen.getByTestId('credit-panel-openReceivables').querySelectorAll('li')).toHaveLength(1)
    expect(screen.getByText(T.notice)).toBeTruthy()
    expect(T.notice).toContain('başka para birimindeki teminat karşılık sayılmaz')
    expect(screen.getByTestId('credit-panel-as-of').textContent).toContain(T.asOf)
  })

  it('names a missing limit as not set and a withheld figure as withheld', async () => {
    install(() => json(panel({
      creditLimit: notShown('NotSet', 'finance_credit_limit_not_set'),
      heldCollateral: notShown('Withheld', 'finance_collateral_withheld'),
    })))
    renderPanel()

    const limit = await screen.findByTestId('credit-panel-creditLimit')
    expect(limit.textContent).toContain(T.notSet)
    expect(limit.textContent).not.toContain(T.zero)
    expect(screen.getByTestId('credit-panel-heldCollateral').textContent).toContain(T.withheld)
  })

  it('names an unknown reason code instead of guessing it', async () => {
    install(() => json(panel({ documentaryRisk: notShown('Unavailable', 'finance_future_reason') })))
    renderPanel()

    expect((await screen.findByTestId('credit-panel-documentaryRisk')).textContent).toContain('finance_future_reason')
  })

  it('shows the loading state until the read answers', async () => {
    let answer: (response: Response) => void = () => undefined
    install(() => new Promise<Response>((resolve) => { answer = resolve }))
    renderPanel()

    expect(panelState()).toBe('initial-loading')
    answer(json(panel()))
    await waitFor(() => expect(panelState()).toBe('ready'))
  })

  it('reports a 401 to the shell boundary with the snapshot taken before the read', async () => {
    install(() => json({}, 401))
    renderPanel()

    await waitFor(() => expect(panelState()).toBe('unauthenticated'))
    expect(authBoundary.report).toHaveBeenCalledWith(401, authBoundary.snapshot)
  })

  it('shows a non-disclosing refusal on 403 and reports it', async () => {
    install(() => json({}, 403))
    renderPanel()

    expect(await screen.findByText(T.forbidden)).toBeTruthy()
    expect(panelState()).toBe('forbidden')
    expect(authBoundary.report).toHaveBeenCalledWith(403, authBoundary.snapshot)
    expect(screen.queryByTestId('credit-panel-figures')).toBeNull()
  })

  it('treats 404 as not found without reporting it', async () => {
    install(() => json({}, 404))
    renderPanel()

    await waitFor(() => expect(panelState()).toBe('not-found'))
    expect(authBoundary.report).not.toHaveBeenCalled()
  })

  it('offers a retry on 503 and shows the figures once the service answers', async () => {
    let status = 503
    install(() => (status === 503 ? json({}, 503) : json(panel())))
    renderPanel()

    await waitFor(() => expect(panelState()).toBe('service-unavailable'))
    status = 200
    await userEvent.click(screen.getByRole('button'))
    expect(await screen.findByTestId('credit-panel-figures')).toBeTruthy()
  })

  it('never shows an answer for another account as data', async () => {
    install(() => json(panel({ companyAccountId: '44444444-4444-4444-8444-444444444445' })))
    renderPanel()

    await waitFor(() => expect(panelState()).toBe('malformed-response'))
    expect(screen.queryByTestId('credit-panel-figures')).toBeNull()
  })

  it('separates a failed request from an offline browser', async () => {
    install(() => { throw new TypeError('failed') })
    renderPanel()
    await waitFor(() => expect(panelState()).toBe('network-error'))
    cleanup()

    Object.defineProperty(navigator, 'onLine', { configurable: true, get: () => false })
    renderPanel()
    await waitFor(() => expect(panelState()).toBe('offline'))
  })

  it('draws the Finance links only for the granted capability', async () => {
    install(() => json(panel()))
    renderPanel({ limits: true })

    await screen.findByTestId('credit-panel-figures')
    expect(screen.getByRole('link', { name: T.openCreditLimits }).getAttribute('href')).toBe(FINANCE_ROUTE.creditLimitsList)
    expect(screen.queryByRole('link', { name: T.openCollaterals })).toBeNull()
  })
})
