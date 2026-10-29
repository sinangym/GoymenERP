import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CariEDocumentPanel } from './CariEDocumentPanel'
import { E_DOCUMENT_TEXT as T } from './eDocument'
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
const CONTRACT = { origin: '/api/v1', namespace: 'ns-test' }
const PROFILE_URL = `/api/v1/counterparties/${PARTY}/company-account/e-document`

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

const unresolved = { outcome: 'Unresolved', source: 'None', scenarioCode: null, unresolvedReason: 'SettingsDefaultNotSet' }
const profile = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  accountId: ACCOUNT,
  companyId: COMPANY,
  version: '1',
  accountIsActive: true,
  status: 'Unknown',
  statusCheckedAtUtc: null,
  statusCheckedByUserId: null,
  scenarioOverrideCode: null,
  evaluatedOn: '2026-10-29',
  scenarioCodes: { state: 'NotSet', codes: [] },
  settingsDefault: { state: 'NotSet', scenarioCode: null },
  effectiveScenario: unresolved,
  viewer: { canUpdate: true },
  ...over,
})
const listed = { state: 'Set', codes: ['CODE_A', 'CODE_B'] }

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
  render(
    <QueryClientProvider client={queryClient}>
      <ShellDispatchContext.Provider value={() => undefined}>
        <CariEDocumentPanel partyId={PARTY} companyId={COMPANY} namespace="ns-test" contractContext={CONTRACT} />
      </ShellDispatchContext.Provider>
    </QueryClientProvider>,
  )
}

const puts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'PUT')
const reads = (calls: readonly Call[]) => calls.filter((call) => call.method === 'GET' && call.url === PROFILE_URL)

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

describe('SAT-D102 e-belge panel on the Cari card', () => {
  it('shows loading, then the explicit Unknown state, "kaynak yok" and an unresolved scenario', async () => {
    const calls = install(() => json(profile()))
    renderPanel()
    expect(screen.getByText(T.panelLoading)).toBeTruthy()
    expect(await screen.findByText(T.status.Unknown, { selector: 'dd' })).toBeTruthy()
    expect(screen.getByText(T.notChecked)).toBeTruthy()
    expect(screen.getByText(T.codeListMissing)).toBeTruthy()
    expect(screen.getByText(T.effectiveUnresolved)).toBeTruthy()
    expect(screen.getByText(T.defaultNotSet)).toBeTruthy()
    // No list and no override: nothing to choose, and no code is invented.
    expect(screen.queryByLabelText(T.scenarioSelectLabel)).toBeNull()
    // Unknown → Unknown records nothing.
    expect((screen.getByRole('button', { name: T.recordStatus }) as HTMLButtonElement).disabled).toBe(true)
    expect(calls[0].headers['X-Company-Id']).toBe(COMPANY)
  })

  it('records a status check with one key and the account version, then reads again', async () => {
    let recorded = false
    const calls = install((call) => {
      if (call.method === 'PUT') {
        recorded = true
        return noContent()
      }
      return json(
        recorded
          ? profile({
              version: '2',
              status: 'EInvoice',
              statusCheckedAtUtc: '2026-10-29T07:30:00+00:00',
              statusCheckedByUserId: 'actor-a',
            })
          : profile(),
      )
    })
    renderPanel()
    const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.statusSelectLabel), 'EInvoice')
    await user.click(screen.getByRole('button', { name: T.recordStatus }))

    expect(await screen.findByText(T.statusRecorded)).toBeTruthy()
    const [put] = puts(calls)
    expect(put.url).toBe(`${PROFILE_URL}/status`)
    expect(put.body).toEqual({ status: 'EInvoice', expectedVersion: '1' })
    expect(put.headers['Idempotency-Key']).toMatch(/^[0-9a-f]{32}$/)
    await waitFor(() => expect(screen.getByText(T.status.EInvoice, { selector: 'dd' })).toBeTruthy())
    expect(screen.getByText(/actor-a/)).toBeTruthy()
    expect(reads(calls).length).toBeGreaterThanOrEqual(2)
  })

  it('offers only the Settings-listed codes and sets the override; clearing sends an explicit null', async () => {
    let override: string | null = null
    const calls = install((call) => {
      if (call.method === 'PUT') {
        override = (call.body as { scenarioCode: string | null }).scenarioCode
        return noContent()
      }
      return json(
        override === null
          ? profile({ scenarioCodes: listed })
          : profile({
              version: '2',
              scenarioCodes: listed,
              scenarioOverrideCode: override,
              effectiveScenario: { outcome: 'Resolved', source: 'CustomerOverride', scenarioCode: override, unresolvedReason: 'None' },
            }),
      )
    })
    renderPanel()
    const select = await screen.findByLabelText(T.scenarioSelectLabel)
    expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual([
      T.useDefaultOption,
      'CODE_A',
      'CODE_B',
    ])
    const user = userEvent.setup()
    await user.selectOptions(select, 'CODE_B')
    await user.click(screen.getByRole('button', { name: T.saveScenario }))
    expect(await screen.findByText(T.scenarioSaved)).toBeTruthy()
    expect(puts(calls)[0]).toMatchObject({ url: `${PROFILE_URL}/scenario`, body: { scenarioCode: 'CODE_B', expectedVersion: '1' } })
    expect(await screen.findByText(T.effectiveFromOverride('CODE_B'))).toBeTruthy()

    await user.selectOptions(screen.getByLabelText(T.scenarioSelectLabel), '')
    await user.click(screen.getByRole('button', { name: T.saveScenario }))
    expect(await screen.findByText(T.scenarioCleared)).toBeTruthy()
    expect(puts(calls)[1].body).toEqual({ scenarioCode: null, expectedVersion: '2' })
  })

  it('keeps an override the list no longer carries visible and marked, and says the scenario is unresolved', async () => {
    install(() =>
      json(
        profile({
          scenarioCodes: { state: 'Set', codes: ['CODE_B'] },
          scenarioOverrideCode: 'CODE_A',
          effectiveScenario: {
            outcome: 'Unresolved',
            source: 'CustomerOverride',
            scenarioCode: null,
            unresolvedReason: 'CustomerOverrideNotListed',
          },
        }),
      ),
    )
    renderPanel()
    expect(await screen.findByText(T.effectiveOverrideNotListed)).toBeTruthy()
    const options = within(screen.getByLabelText(T.scenarioSelectLabel)).getAllByRole('option')
    expect(options.map((option) => option.textContent)).toContain(`CODE_A${T.notListedSuffix}`)
  })

  it('names a version conflict and reads the profile again', async () => {
    const calls = install((call) =>
      call.method === 'PUT' ? json({ code: 'version_conflict' }, 409) : json(profile({ scenarioCodes: listed })),
    )
    renderPanel()
    const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.scenarioSelectLabel), 'CODE_A')
    await user.click(screen.getByRole('button', { name: T.saveScenario }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.refusal.version_conflict)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
  })

  it('names a code the Settings list does not carry (422) and refreshes', async () => {
    const calls = install((call) =>
      call.method === 'PUT'
        ? json({ code: 'einvoice_scenario_not_listed' }, 422)
        : json(profile({ scenarioCodes: listed })),
    )
    renderPanel()
    const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.scenarioSelectLabel), 'CODE_A')
    await user.click(screen.getByRole('button', { name: T.saveScenario }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.refusal.einvoice_scenario_not_listed)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
  })

  it('keeps the same key for an explicit retry when Settings could not be read (503)', async () => {
    let attempts = 0
    const calls = install((call) => {
      if (call.method === 'PUT') {
        attempts += 1
        return attempts === 1 ? json({ code: 'service_unavailable' }, 503) : noContent()
      }
      return json(profile({ scenarioCodes: listed }))
    })
    renderPanel()
    const user = userEvent.setup()
    await user.selectOptions(await screen.findByLabelText(T.scenarioSelectLabel), 'CODE_A')
    await user.click(screen.getByRole('button', { name: T.saveScenario }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    expect((screen.getByLabelText(T.scenarioSelectLabel) as HTMLSelectElement).disabled).toBe(true)

    await user.click(screen.getByRole('button', { name: T.retrySame }))
    expect(await screen.findByText(T.scenarioSaved)).toBeTruthy()
    const [first, second] = puts(calls)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('offers no form without the update grant', async () => {
    install(() => json(profile({ viewer: { canUpdate: false }, scenarioCodes: listed })))
    renderPanel()
    expect(await screen.findByText(T.readOnlyNote)).toBeTruthy()
    expect(screen.queryByLabelText(T.statusSelectLabel)).toBeNull()
    expect(screen.queryByLabelText(T.scenarioSelectLabel)).toBeNull()
  })

  it('says Settings could not be read when the list is unavailable', async () => {
    install(() =>
      json(
        profile({
          scenarioCodes: { state: 'Unavailable', codes: [] },
          settingsDefault: { state: 'Unavailable', scenarioCode: null },
          effectiveScenario: { outcome: 'Unavailable', source: 'None', scenarioCode: null, unresolvedReason: 'None' },
        }),
      ),
    )
    renderPanel()
    expect(await screen.findByText(T.codeListUnavailable)).toBeTruthy()
    expect(screen.getByText(T.effectiveUnavailable)).toBeTruthy()
    expect(screen.queryByLabelText(T.scenarioSelectLabel)).toBeNull()
  })

  it('says forbidden on a 403 read, offers a retry on any other failure and refuses a malformed body', async () => {
    install(() => json({ code: 'forbidden' }, 403))
    renderPanel()
    expect(await screen.findByText(T.panelForbidden)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.readAgain })).toBeNull()
    cleanup()

    install(() => json({}, 500))
    renderPanel()
    expect(await screen.findByText(T.panelError)).toBeTruthy()
    expect(screen.getByRole('button', { name: T.readAgain })).toBeTruthy()
    cleanup()

    install(() => json(profile({ status: 'EInvoice' })))
    renderPanel()
    expect(await screen.findByText(T.panelError)).toBeTruthy()
  })
})
