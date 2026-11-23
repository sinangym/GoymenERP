import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { CariIdentityHistoryPanel } from './CariIdentityHistoryPanel'
import { IDENTITY_HISTORY_TEXT as T } from './identityHistory'
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
const SUCCESSOR = '22222222-2222-2222-2222-222222222222'
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

const successorLink = {
  predecessorPartyId: PARTY,
  successorPartyId: SUCCESSOR,
  otherPartyId: SUCCESSOR,
  otherPartyCode: 'C-000002',
  otherPartyLegalName: 'Yeni Örnek A.Ş.',
  kind: 'Conversion',
  legalBasis: 'Ticaret sicili ilanı',
  effectiveOn: '2026-11-01',
  recordedAtUtc: '2026-11-02T09:00:00+00:00',
}

const view = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  kind: 2,
  accountVersion: '5',
  accountIsActive: true,
  taxIdentityNumber: '12345678901',
  legalName: 'Örnek Şahıs İşletmesi',
  revisions: [],
  eDocumentStatusRevisions: [],
  successor: null,
  predecessors: [],
  viewer: { canRecordSuccession: true },
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

function renderPanel(onSucceeded: () => void = () => undefined) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
  render(
    <QueryClientProvider client={queryClient}>
      <ShellDispatchContext.Provider value={() => undefined}>
        <CariIdentityHistoryPanel
          partyId={PARTY}
          companyId={COMPANY}
          namespace="ns-test"
          contractContext={CONTRACT}
          onSucceeded={onSucceeded}
        />
      </ShellDispatchContext.Provider>
    </QueryClientProvider>,
  )
}

const puts = (calls: readonly Call[]) => calls.filter((call) => call.method === 'PUT')
const reads = (calls: readonly Call[]) =>
  calls.filter((call) => call.method === 'GET' && call.url.endsWith('/identity-history'))

async function fillForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText(T.successorIdLabel), SUCCESSOR)
  await user.selectOptions(screen.getByLabelText(T.kindLabel), 'Conversion')
  await user.type(screen.getByLabelText(T.legalBasisLabel), '  Ticaret sicili ilanı  ')
  await user.type(screen.getByLabelText(T.effectiveOnLabel), '2026-11-01')
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

describe('F-027 / KLN-D38 Cari card identity history and succession', () => {
  it('shows loading, then says an empty history means the current values apply since always', async () => {
    install(() => json(view()))
    renderPanel()
    expect(screen.getByText(T.loading)).toBeTruthy()
    expect(await screen.findByText(T.noRevisions)).toBeTruthy()
    expect(screen.getByText(T.noStatusRevisions)).toBeTruthy()
    expect(screen.getByText(T.balanceNote)).toBeTruthy()
  })

  it('records a succession with one key, the account version and the trimmed legal basis', async () => {
    let recorded = false
    const onSucceeded = vi.fn()
    const calls = install((call) => {
      if (call.method === 'PUT') {
        recorded = true
        return noContent()
      }
      return json(recorded ? view({ accountVersion: '6', accountIsActive: false, successor: successorLink }) : view())
    })
    renderPanel(onSucceeded)
    const user = userEvent.setup()
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.submit }))

    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [put] = puts(calls)
    expect(puts(calls)).toHaveLength(1)
    expect(put.url).toBe(`/api/v1/counterparties/${PARTY}/succession`)
    expect(put.body).toEqual({
      successorPartyId: SUCCESSOR,
      kind: 'Conversion',
      legalBasis: 'Ticaret sicili ilanı',
      effectiveOn: '2026-11-01',
      expectedVersion: '5',
    })
    expect(put.headers['Idempotency-Key']).toMatch(/^[0-9a-f]{32}$/)
    expect(put.headers['X-Company-Id']).toBe(COMPANY)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
    expect(onSucceeded).toHaveBeenCalledTimes(1)
    expect(await screen.findByText(T.successorNote)).toBeTruthy()
    expect(screen.queryByRole('button', { name: T.submit })).toBeNull()
  })

  it('ALS-D99: a 422 acknowledgement refusal shows the old account warning and only the acknowledged resend carries the flag', async () => {
    let recorded = false
    const warning = {
      companyId: COMPANY,
      partyId: PARTY,
      accountVersion: '5',
      isActive: true,
      acknowledgementRequired: true,
      items: [
        { kind: 'finance-open-balance', outcome: 'open', openItemCount: 2 },
        { kind: 'sales-open-documents', outcome: 'unavailable', openItemCount: null },
        { kind: 'purchasing-open-documents', outcome: 'clear', openItemCount: null },
      ],
    }
    const calls = install((call) => {
      if (call.method === 'PUT') {
        const body = call.body as Record<string, unknown>
        if (body.exposureWarningAcknowledged !== true) {
          return json({ code: 'deactivation_warning_acknowledgement_required' }, 422)
        }
        recorded = true
        return noContent()
      }
      if (call.url.endsWith('/deactivation-warning')) return json(warning)
      return json(recorded ? view({ accountVersion: '6', accountIsActive: false, successor: successorLink }) : view())
    })
    renderPanel()
    const user = userEvent.setup()
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.submit }))

    expect(await screen.findByText(T.refusal.deactivation_warning_acknowledgement_required)).toBeTruthy()
    const acknowledge = await screen.findByLabelText(T.warningAcknowledge)
    expect(document.querySelectorAll('[data-warning-items] li')).toHaveLength(3)

    // Without the tick nothing more is sent.
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByText(T.warningAcknowledgeRequired)).toBeTruthy()
    expect(puts(calls)).toHaveLength(1)

    await user.click(acknowledge)
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [first, second] = puts(calls)
    expect(puts(calls)).toHaveLength(2)
    expect(first.body).not.toHaveProperty('exposureWarningAcknowledged')
    expect(second.body).toEqual({ ...(first.body as object), exposureWarningAcknowledged: true })
    expect(second.headers['Idempotency-Key']).not.toBe(first.headers['Idempotency-Key'])
  })

  it('ALS-D99: an unreadable warning sends nothing further', async () => {
    const calls = install((call) => {
      if (call.method === 'PUT') return json({ code: 'deactivation_warning_acknowledgement_required' }, 422)
      if (call.url.endsWith('/deactivation-warning')) return json({ code: 'service_unavailable' }, 503)
      return json(view())
    })
    renderPanel()
    const user = userEvent.setup()
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByText(T.warningUnavailable)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(puts(calls)).toHaveLength(1)
  })

  it('refuses a missing legal basis in the form and sends nothing', async () => {
    const calls = install(() => json(view()))
    renderPanel()
    const user = userEvent.setup()
    await user.type(await screen.findByLabelText(T.successorIdLabel), SUCCESSOR)
    await user.selectOptions(screen.getByLabelText(T.kindLabel), 'Merger')
    await user.type(screen.getByLabelText(T.effectiveOnLabel), '2026-11-01')
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.legalBasisRequired)
    expect(puts(calls)).toHaveLength(0)
  })

  it('names the same-VKN refusal: a type change stays on the same cari', async () => {
    install((call) =>
      call.method === 'PUT' ? json({ code: 'succession_same_tax_identity' }, 422) : json(view()),
    )
    renderPanel()
    const user = userEvent.setup()
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByRole('alert')).toHaveProperty(
      'textContent',
      T.refusal.succession_same_tax_identity,
    )
  })

  it('names a version conflict and reads the history again', async () => {
    const calls = install((call) => (call.method === 'PUT' ? json({ code: 'version_conflict' }, 409) : json(view())))
    renderPanel()
    const user = userEvent.setup()
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.refusal.version_conflict)
    await waitFor(() => expect(reads(calls).length).toBeGreaterThanOrEqual(2))
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
    await fillForm(user)
    await user.click(screen.getByRole('button', { name: T.submit }))
    expect(await screen.findByText(T.unknownOutcome)).toBeTruthy()
    expect((screen.getByLabelText(T.successorIdLabel) as HTMLInputElement).disabled).toBe(true)

    await user.click(screen.getByRole('button', { name: T.retrySame }))
    expect(await screen.findByText(T.saved)).toBeTruthy()
    const [first, second] = puts(calls)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('offers no form without the server-measured grant', async () => {
    const calls = install(() => json(view({ viewer: { canRecordSuccession: false } })))
    renderPanel()
    expect(await screen.findByText(T.readOnlyNote)).toBeTruthy()
    expect(screen.queryByLabelText(T.successorIdLabel)).toBeNull()
    expect(puts(calls)).toHaveLength(0)
  })

  it('shows a correction with its reason and a revision recorded elsewhere without one', async () => {
    install(() =>
      json(
        view({
          revisions: [
            { attribute: 'TaxIdentityNumber', kind: 'Baseline', sequence: '1', value: '12345678900',
              effectiveFrom: null, reason: null, recordedInActiveCompany: false,
              recordedAtUtc: '2026-11-01T08:00:00+00:00' },
            { attribute: 'TaxIdentityNumber', kind: 'Correction', sequence: '2', value: '12345678901',
              effectiveFrom: '2026-11-05', reason: 'Yanlış girilmiş TCKN', recordedInActiveCompany: true,
              recordedAtUtc: '2026-11-05T08:00:00+00:00' },
            { attribute: 'LegalName', kind: 'DatedChange', sequence: '3', value: 'Örnek Şahıs İşletmesi',
              effectiveFrom: '2026-11-06', reason: null, recordedInActiveCompany: false,
              recordedAtUtc: '2026-11-06T08:00:00+00:00' },
          ],
        }),
      ),
    )
    renderPanel()
    expect(await screen.findByText(`${T.reasonLabel}: Yanlış girilmiş TCKN`)).toBeTruthy()
    expect(screen.getByText(T.sinceAlways)).toBeTruthy()
    expect(screen.getAllByText(`(${T.recordedElsewhere})`, { exact: false })).toHaveLength(1)
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
    expect(screen.queryByLabelText(T.successorIdLabel)).toBeNull()
  })
})
