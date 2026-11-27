/**
 * W1-A Work inbox ("Benden karar bekleyen") in the REAL shell (router, CapabilityRoute, ShellProvider, auth boundary) over
 * a stubbed fetch playing `GET /api/v1/work/inbox`, `POST /api/v1/work/inbox/events` and the B-25 `GET /api/v1/work/queue`.
 * Component evidence only: authority, maker ≠ checker and the due time are the server's (WorkInboxReadQueryV1 tests).
 * Synthetic data (SENTETIK).
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { shellRoutes } from '../shell/router'
import { SHELL_CAPABILITY_CONTRACT_VERSION } from '../shell/api'
import { CAPABILITY } from '../shell/capabilities'
import { FINANCE_CAPABILITY } from '../finance/capabilities'
import { LOGISTICS_CAPABILITY } from '../logistics/capabilities'
import { COMPANY } from './approvalInboxFixtures'
import { WORK_INBOX_TEXT as T, INBOX_FILTER_PARAM } from './workInboxText'

const USER = '11111111-1111-1111-1111-111111111111'
const DIRECTORY = [{ id: COMPANY, code: 'GDC', legalName: 'Göymen A', isPlaceholder: false }]
const INBOX_CAP = FINANCE_CAPABILITY.creditLimitsApprove
const LEGACY_CAP = CAPABILITY.financeOfficialInvoiceImportApprove

const LIMIT_KIND = 'finance.party-credit-limit.v1'
const PAYMENT_KIND = 'finance.manual-bank-payment.v1'
const DISPATCH_KIND = 'logistics.dispatch-note.v1'
const PLAN_KIND = 'logistics.plan-excess.v1'
const LIMIT_ID = 'd1000000-0000-4000-8000-000000000001'
const PAYMENT_ID = 'd1000000-0000-4000-8000-000000000002'
const DISPATCH_ID = 'd1000000-0000-4000-8000-000000000003'
const PLAN_ID = 'd1000000-0000-4000-8000-000000000004'
const PARTY_ID = 'e1000000-0000-4000-8000-000000000001'

/** WP1: the YOL-D18 default rule version of a kind. */
const RV = (kind: string) => `work.approval-rule:${kind}:0`
const none = (durationClass: string, state: string) =>
  ({ class: durationClass, state, limit: null, dueAtUtc: null, isOverdue: null, policyVersion: null, holidayCalendar: null })
const definedDays = (isOverdue: boolean, holidayCalendar = 'Defined') => ({
  class: 'BusinessDays', state: 'Defined', limit: 2, dueAtUtc: '2026-11-04T07:00:00+00:00', isOverdue,
  policyVersion: 'work.approval-duration-business-days:1', holidayCalendar,
})

const limitItem = {
  module: 'finance', kind: LIMIT_KIND, itemId: LIMIT_ID, documentLabel: 'LIM-0001', link: `/finans/kredi-limitleri?oneri=${LIMIT_ID}`,
  waitingSinceUtc: '2026-11-01T07:00:00+00:00', awaitingPermission: 'finance.credit-limits.approve', duration: definedDays(true),
  ruleVersion: RV(LIMIT_KIND),
  explanation: {
    rules: [
      { ruleCode: 'finance.credit-limit-change', threshold: '1000000.5', actual: '1250000', deviation: '249999.5', unit: 'TRY' },
      { ruleCode: 'finance.future-rule', threshold: null, actual: null, deviation: null, unit: null },
    ],
    references: [
      { role: 'party', label: 'SENTETIK Cari A.Ş.', recordId: PARTY_ID, link: `/cari/${PARTY_ID}` },
      { role: 'currency', label: 'TRY', recordId: null, link: null },
      { role: 'future-role', label: 'SENTETIK kayıt', recordId: null, link: null },
    ],
  },
}
const paymentItem = {
  module: 'finance', kind: PAYMENT_KIND, itemId: PAYMENT_ID, documentLabel: PAYMENT_ID, link: `/finans/elle-banka-odemeleri/${PAYMENT_ID}`,
  waitingSinceUtc: '2026-11-02T07:00:00+00:00', awaitingPermission: 'finance.manual-bank-payment.approve', duration: definedDays(false, 'Undefined'),
  ruleVersion: RV(PAYMENT_KIND),
  explanation: { rules: [{ ruleCode: 'finance.manual-bank-payment-approval-required', threshold: null, actual: null, deviation: null, unit: null }], references: [] },
}
const dispatchItem = {
  module: 'logistics', kind: DISPATCH_KIND, itemId: DISPATCH_ID, documentLabel: 'IRS-TASLAK-1', link: `/lojistik/talimatlar/${DISPATCH_ID}`,
  waitingSinceUtc: '2026-11-03T07:00:00+00:00', awaitingPermission: 'logistics.dispatch-notes.approve', duration: none('Hours', 'Undefined'),
  ruleVersion: RV(DISPATCH_KIND),
  explanation: { rules: [{ ruleCode: 'logistics.dispatch-note-draft', threshold: null, actual: null, deviation: null, unit: null }], references: [] },
}
const planItem = {
  module: 'logistics', kind: PLAN_KIND, itemId: PLAN_ID, documentLabel: 'PLN-7', link: `/lojistik/plan-ustu-sevk-onay?id=${PLAN_ID}`,
  waitingSinceUtc: '2026-11-04T07:00:00+00:00', awaitingPermission: 'logistics.shipments.plan-excess.approve', duration: none('Hours', 'Unavailable'),
  ruleVersion: RV(PLAN_KIND),
  explanation: { rules: [{ ruleCode: 'logistics.plan-excess-quantity', threshold: '10', actual: '12.5', deviation: '2.5', unit: 'kg' }], references: [] },
}
const src = (module: string, kind: string, awaitingPermission: string, durationClass: string, state = 'Ready', hasMore = false) =>
  ({ module, kind, awaitingPermission, durationClass, ruleVersion: RV(kind), state, hasMore })
const SOURCES = [
  src('finance', LIMIT_KIND, 'finance.credit-limits.approve', 'BusinessDays'),
  src('finance', PAYMENT_KIND, 'finance.manual-bank-payment.approve', 'BusinessDays'),
  src('logistics', DISPATCH_KIND, 'logistics.dispatch-notes.approve', 'Hours'),
  src('logistics', PLAN_KIND, 'logistics.shipments.plan-excess.approve', 'Hours'),
]
const inbox = (overrides: Record<string, unknown> = {}) => ({
  companyId: COMPANY, view: 'awaiting-my-decision', generatedAtUtc: '2026-11-05T07:00:00+00:00',
  items: [limitItem, paymentItem, dispatchItem, planItem], sources: SOURCES, ...overrides,
})
const LEGACY_KIND = 'finance.official-invoice-import.v1'
const LEGACY_ID = 'c6000000-0000-4000-8000-000000000001'
const legacyQueue = {
  companyId: COMPANY,
  items: [{ module: 'finance', itemId: LEGACY_ID, kind: LEGACY_KIND, createdAtUtc: '2026-11-01T07:00:00+00:00', link: `/onaylarim/${LEGACY_KIND}/${LEGACY_ID}` }],
  sources: [{ module: 'finance', kind: LEGACY_KIND, state: 'Ready', hasMore: false }],
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

interface Call { readonly url: string; readonly method: string; readonly companyId: string | null; readonly body: unknown }

function installFetch(options: {
  readonly granted: readonly string[]
  readonly inbox?: () => Response | Promise<Response>
  readonly queue?: () => Response | Promise<Response>
  readonly event?: () => Response | Promise<Response>
}) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const headers = (init?.headers ?? {}) as Record<string, string>
    calls.push({
      url, method: init?.method ?? 'GET', companyId: headers['X-Company-Id'] ?? null,
      body: typeof init?.body === 'string' ? JSON.parse(init.body) : null,
    })
    if (url.includes('/identity/me')) return json({ userId: USER, displayName: 'Onaycı', email: null, allowedCompanyIds: [COMPANY], mustChangePassword: false, localCredentialVersion: null, authenticationSessionBinding: null })
    if (url.includes('/organization/companies')) return json(DIRECTORY)
    if (url.includes('/shell/capabilities')) return json({ contractVersion: SHELL_CAPABILITY_CONTRACT_VERSION, generation: 'gen-1', capabilities: options.granted })
    if (url.includes('/work/inbox/events')) {
      if (options.event !== undefined) return options.event()
      const body = JSON.parse(String(init?.body)) as { clientEventId: string }
      return json({ clientEventId: body.clientEventId, replayed: false })
    }
    if (url.includes('/work/inbox')) return (options.inbox ?? (() => json(inbox())))()
    if (url.includes('/work/queue')) return (options.queue ?? (() => json(legacyQueue)))()
    return json({ code: 'unavailable' }, 503)
  }))
  return {
    inboxReads: () => calls.filter((call) => call.url.includes('/work/inbox') && !call.url.includes('/events')),
    events: () => calls.filter((call) => call.url.includes('/work/inbox/events')),
    queueReads: () => calls.filter((call) => call.url.includes('/work/queue')),
  }
}

async function renderAt(path: string) {
  const router = createMemoryRouter(shellRoutes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  render(<ShellProvider queryClient={queryClient}><RouterProvider router={router} /></ShellProvider>)
  await screen.findByText('Onaycı', undefined, { timeout: 5000 })
  return router
}

const inboxSection = () => screen.getByTestId('work-inbox')
const inboxState = (kind: string) => inboxSection().querySelector(`[data-state="${kind}"]`)
const cards = () => within(screen.getByTestId('work-inbox-list')).getAllByTestId('work-inbox-item')
const cardOf = (kind: string) => cards().find((card) => card.getAttribute('data-kind') === kind) as HTMLElement

beforeEach(() => {
  try {
    window.localStorage.clear()
    window.sessionStorage.clear()
  } catch {
    // no storage here
  }
  vi.stubGlobal('navigator', { onLine: true })
})
afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('/onaylarim/kuyruk — Benden karar bekleyen', () => {
  it('shows the item cards oldest first, with who, since when, duration and the owner link — and no decision control', async () => {
    const api = installFetch({ granted: [INBOX_CAP] })
    await renderAt('/onaylarim/kuyruk')
    expect(await screen.findByRole('heading', { level: 1, name: T.heading })).toBeTruthy()
    expect(screen.getByRole('heading', { level: 2, name: T.viewAwaitingMyDecision })).toBeTruthy()
    const views = screen.getByTestId('work-inbox-views')
    expect(within(views).getByText(T.viewAwaitingMyDecision).getAttribute('aria-current')).toBe('page')
    expect(within(views).getAllByText(`(${T.soon})`)).toHaveLength(3)

    await screen.findByTestId('work-inbox-list')
    expect(cards().map((card) => card.getAttribute('data-kind'))).toEqual([LIMIT_KIND, PAYMENT_KIND, DISPATCH_KIND, PLAN_KIND])
    const limit = cardOf(LIMIT_KIND)
    expect(within(limit).getByRole('heading', { level: 3, name: 'Cari kredi limiti teklifi' })).toBeTruthy()
    expect(within(limit).getByText('Finans')).toBeTruthy()
    expect(within(limit).getByText('LIM-0001')).toBeTruthy()
    expect(within(limit).getByTestId('work-inbox-awaiting').textContent).toBe(T.awaitingText('Cari kredi limiti onayı'))
    const owner = within(limit).getByRole('link', { name: T.openOwnerLabel('Cari kredi limiti teklifi', 'LIM-0001') })
    expect(owner.getAttribute('href')).toBe(`/finans/kredi-limitleri?oneri=${LIMIT_ID}`)
    expect(owner.textContent).toBe(T.openOwner)
    // A bare GUID label is shortened; the full value stays in the title.
    const shortened = within(cardOf(PAYMENT_KIND)).getByText(`…${PAYMENT_ID.slice(-8)}`)
    expect(shortened.getAttribute('title')).toBe(PAYMENT_ID)

    // Read only: no approve / reject control anywhere.
    expect(screen.queryByRole('button', { name: /onayla|reddet/i })).toBeNull()
    expect(api.inboxReads()).toEqual([{ url: '/api/v1/work/inbox?view=awaiting-my-decision', method: 'GET', companyId: COMPANY, body: null }])
    // Holding no B-25 source capability: the queue is neither read nor drawn.
    expect(api.queueReads()).toEqual([])
    expect(screen.queryByTestId('work-queue-legacy')).toBeNull()
    expect(api.events()).toEqual([])
  })

  it('draws the duration indicator for Defined (overdue and not), Undefined and Unavailable', async () => {
    installFetch({ granted: [INBOX_CAP] })
    await renderAt('/onaylarim/kuyruk')
    await screen.findByTestId('work-inbox-list')
    const duration = (kind: string) => within(cardOf(kind)).getByTestId('work-inbox-duration')
    expect(duration(LIMIT_KIND).getAttribute('data-state')).toBe('Defined')
    expect(duration(LIMIT_KIND).textContent).toContain('Son: ')
    expect(duration(LIMIT_KIND).textContent).toContain('2 iş günü')
    expect(within(duration(LIMIT_KIND)).getByText(T.overdue)).toBeTruthy()
    expect(within(duration(PAYMENT_KIND)).queryByText(T.overdue)).toBeNull()
    expect(duration(PAYMENT_KIND).querySelector('time')?.getAttribute('dateTime')).toBe('2026-11-04T07:00:00+00:00')
    expect(duration(DISPATCH_KIND).textContent).toBe(T.durationUndefined)
    expect(duration(PLAN_KIND).textContent).toBe(T.durationUnavailable)
    // YOL-D22: the holiday calendar behind a business-day due time; never on the hour class.
    expect(within(duration(LIMIT_KIND)).getByTestId('work-inbox-holiday-calendar').textContent).toContain(T.holidaysDeducted)
    expect(within(duration(PAYMENT_KIND)).getByTestId('work-inbox-holiday-calendar').textContent).toBe(T.holidayCalendarUndefined)
    expect(within(duration(DISPATCH_KIND)).queryByTestId('work-inbox-holiday-calendar')).toBeNull()
  })

  it('warns when the holiday calendar could not be read (only weekends deducted)', async () => {
    installFetch({ granted: [INBOX_CAP], inbox: () => json(inbox({ items: [{ ...limitItem, duration: definedDays(false, 'Unavailable') }] })) })
    await renderAt('/onaylarim/kuyruk')
    await screen.findByTestId('work-inbox-list')
    const note = within(cardOf(LIMIT_KIND)).getByTestId('work-inbox-holiday-calendar')
    expect(note.textContent).toBe(T.holidayCalendarUnavailable)
    expect(note.getAttribute('role')).toBe('note')
  })

  it('opens the explanation card with rules, numbers and references, and counts the opening once', async () => {
    const api = installFetch({ granted: [INBOX_CAP] })
    await renderAt('/onaylarim/kuyruk')
    await screen.findByTestId('work-inbox-list')
    const limit = cardOf(LIMIT_KIND)
    const toggle = within(limit).getByRole('button', { name: T.explanationToggle })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    const panel = within(limit).getByTestId('work-inbox-explanation')
    expect(panel.hidden).toBe(true)
    const user = userEvent.setup()
    await user.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(toggle.getAttribute('aria-controls')).toBe(panel.id)
    expect(panel.hidden).toBe(false)
    expect(within(panel).getByRole('heading', { level: 4, name: T.rulesHeading })).toBeTruthy()
    const rules = within(panel).getAllByTestId('work-inbox-rule')
    expect(within(rules[0]).getByText('Limit değişikliği önerisi')).toBeTruthy()
    expect(rules[0].textContent).toContain(`${T.threshold}1.000.000,5 TRY`)
    expect(rules[0].textContent).toContain(`${T.actual}1.250.000 TRY`)
    expect(rules[0].textContent).toContain(`${T.deviation}249.999,5 TRY`)
    // Unknown rule code: fail-closed generic text with its code.
    expect(rules[1].textContent).toBe(`${T.ruleUnknown} finance.future-rule`)
    expect(within(rules[1]).getByText('finance.future-rule').tagName).toBe('CODE')
    expect(within(panel).getByRole('heading', { level: 4, name: T.referencesHeading })).toBeTruthy()
    const references = within(panel).getAllByTestId('work-inbox-reference')
    expect(within(references[0]).getByRole('link', { name: 'SENTETIK Cari A.Ş.' }).getAttribute('href')).toBe(`/cari/${PARTY_ID}`)
    expect(references[0].textContent).toBe('Cari: SENTETIK Cari A.Ş.')
    expect(references[1].textContent).toBe('Para birimi: TRY')
    expect(within(references[1]).queryByRole('link')).toBeNull()
    expect(references[2].textContent).toBe(`${T.roleUnknown} future-role: SENTETIK kayıt`)
    expect(within(panel).getByText(T.explanationNote)).toBeTruthy()

    await waitFor(() => expect(api.events()).toHaveLength(1))
    const event = api.events()[0]
    expect(event.method).toBe('POST')
    expect(event.companyId).toBe(COMPANY)
    expect(event.body).toEqual({
      clientEventId: (event.body as { clientEventId: string }).clientEventId,
      kind: LIMIT_KIND, itemId: LIMIT_ID, waitingSinceUtc: limitItem.waitingSinceUtc, eventType: 'Opened',
    })
    expect((event.body as { clientEventId: string }).clientEventId).toMatch(/^[0-9a-f-]{36}$/)
    // Closing and reopening the same card does not count again.
    await user.click(toggle)
    await user.click(toggle)
    expect(api.events()).toHaveLength(1)
  })

  it('shows numbers with a percent or kg unit in Turkish digits', async () => {
    installFetch({ granted: [INBOX_CAP] })
    await renderAt('/onaylarim/kuyruk')
    await screen.findByTestId('work-inbox-list')
    const plan = cardOf(PLAN_KIND)
    await userEvent.setup().click(within(plan).getByRole('button', { name: T.explanationToggle }))
    const rule = within(plan).getByTestId('work-inbox-rule')
    expect(rule.textContent).toContain('Talimat miktarının üzerinde sevk önerildi')
    expect(rule.textContent).toContain(`${T.actual}12,5 kg`)
    expect(within(plan).getByText(T.noReferences)).toBeTruthy()
  })

  it('counts the owner-link click and navigates; a failing count is never shown and never blocks', async () => {
    const api = installFetch({ granted: [INBOX_CAP], event: () => json({ status: 503 }, 503) })
    const router = await renderAt('/onaylarim/kuyruk')
    await screen.findByTestId('work-inbox-list')
    await userEvent.setup().click(within(cardOf(LIMIT_KIND)).getByRole('link', { name: T.openOwnerLabel('Cari kredi limiti teklifi', 'LIM-0001') }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/finans/kredi-limitleri'))
    expect(router.state.location.search).toBe(`?oneri=${LIMIT_ID}`)
    await waitFor(() => expect(api.events()).toHaveLength(1))
    expect((api.events()[0].body as { eventType: string }).eventType).toBe('OwnerScreenOpened')
    expect((api.events()[0].body as { itemId: string }).itemId).toBe(LIMIT_ID)
  })

  it('ignores a measurement call that throws: the explanation still opens and nothing is announced', async () => {
    const api = installFetch({ granted: [INBOX_CAP], event: () => { throw new TypeError('failed to fetch') } })
    await renderAt('/onaylarim/kuyruk')
    await screen.findByTestId('work-inbox-list')
    const limit = cardOf(LIMIT_KIND)
    await userEvent.setup().click(within(limit).getByRole('button', { name: T.explanationToggle }))
    await waitFor(() => expect(api.events()).toHaveLength(1))
    expect(within(limit).getByTestId('work-inbox-explanation').hidden).toBe(false)
    expect(within(inboxSection()).queryByRole('alert')).toBeNull()
  })

  it('filters by kind, module and overdue, keeps the filter in the address, and has a filtered-empty state', async () => {
    installFetch({ granted: [INBOX_CAP] })
    const router = await renderAt('/onaylarim/kuyruk')
    await screen.findByTestId('work-inbox-list')
    const user = userEvent.setup()
    const filters = screen.getByTestId('work-inbox-filters')
    const kindSelect = within(filters).getByLabelText(T.filterKind)
    // The kind options are the kinds the server listed as sources.
    expect(within(kindSelect).getAllByRole('option').map((option) => option.textContent))
      .toEqual([T.filterAll, 'Cari kredi limiti teklifi', 'Elle banka ödemesi', 'İrsaliye onayı', 'Plan üstü sevk'])
    await user.selectOptions(kindSelect, PAYMENT_KIND)
    expect(cards().map((card) => card.getAttribute('data-kind'))).toEqual([PAYMENT_KIND])
    expect(new URLSearchParams(router.state.location.search).get(INBOX_FILTER_PARAM.kind)).toBe(PAYMENT_KIND)

    await user.selectOptions(kindSelect, '')
    await user.selectOptions(within(filters).getByLabelText(T.filterModule), 'logistics')
    expect(cards().map((card) => card.getAttribute('data-kind'))).toEqual([DISPATCH_KIND, PLAN_KIND])

    await user.click(within(filters).getByLabelText(T.filterOverdue))
    expect(new URLSearchParams(router.state.location.search).get(INBOX_FILTER_PARAM.overdue)).toBe('1')
    // No logistics item is overdue: the filtered-empty state, not the no-data one.
    expect(inboxState('empty-filtered')).not.toBeNull()
    expect(inboxState('empty-no-data')).toBeNull()

    await user.click(within(filters).getByRole('button', { name: T.clearFilters }))
    expect(cards()).toHaveLength(4)
    expect(router.state.location.search).toBe('')
  })

  it('reads the filter from the address on entry', async () => {
    installFetch({ granted: [INBOX_CAP] })
    await renderAt(`/onaylarim/kuyruk?${INBOX_FILTER_PARAM.overdue}=1`)
    await screen.findByTestId('work-inbox-list')
    expect(cards().map((card) => card.getAttribute('data-kind'))).toEqual([LIMIT_KIND])
    expect((within(screen.getByTestId('work-inbox-filters')).getByLabelText(T.filterOverdue) as HTMLInputElement).checked).toBe(true)
  })

  it('shows the loading state while the inbox is read', async () => {
    installFetch({ granted: [INBOX_CAP], inbox: () => new Promise<Response>(() => {}) })
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(inboxState('initial-loading')).not.toBeNull())
  })

  it('an empty inbox is the no-data state', async () => {
    installFetch({ granted: [INBOX_CAP], inbox: () => json(inbox({ items: [] })) })
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(inboxState('empty-no-data')).not.toBeNull())
    expect(screen.queryByTestId('work-inbox-filters')).toBeNull()
  })

  it('names an unavailable source and a source with more records, still listing the others', async () => {
    installFetch({ granted: [INBOX_CAP], inbox: () => json(inbox({
      items: [limitItem],
      sources: [
        src('finance', LIMIT_KIND, 'finance.credit-limits.approve', 'BusinessDays', 'Ready', true),
        src('finance', PAYMENT_KIND, 'finance.manual-bank-payment.approve', 'BusinessDays', 'Unavailable'),
      ],
    })) })
    await renderAt('/onaylarim/kuyruk')
    expect((await screen.findByTestId('work-inbox-unavailable')).textContent).toBe(T.unavailable(['Elle banka ödemesi']))
    expect(screen.getByTestId('work-inbox-has-more').textContent).toBe(T.hasMore(['Cari kredi limiti teklifi']))
    expect(cards()).toHaveLength(1)
  })

  it('empty with an unreadable source says only the readable ones are empty', async () => {
    installFetch({ granted: [INBOX_CAP], inbox: () => json(inbox({
      items: [], sources: [src('finance', PAYMENT_KIND, 'finance.manual-bank-payment.approve', 'BusinessDays', 'Unavailable')],
    })) })
    await renderAt('/onaylarim/kuyruk')
    expect(await screen.findByTestId('work-inbox-empty-readable')).toBeTruthy()
    expect(inboxState('empty-no-data')).toBeNull()
  })

  it.each([
    ['forbidden', () => json({ status: 403 }, 403)],
    ['service-unavailable', () => json({ status: 503 }, 503)],
    ['malformed-response', () => json({ ...inbox(), extra: true })],
    ['malformed-response', () => json(inbox({ companyId: '10000000-0000-0000-0000-000000000009' }))],
  ])('shows the %s state and no card', async (kind, served) => {
    installFetch({ granted: [INBOX_CAP], inbox: served })
    await renderAt('/onaylarim/kuyruk')
    // A 403 goes through the auth boundary, which closes the gated route until a fresh snapshot: either block is fine.
    await waitFor(() => expect(document.querySelector(`[data-state="${kind}"], [data-state="not-found"]`)).not.toBeNull())
    expect(screen.queryByTestId('work-inbox-list')).toBeNull()
  })

  it('a 503 inbox is retryable and no second list is read in its place (WP1 D5: one box)', async () => {
    let failing = true
    const api = installFetch({ granted: [INBOX_CAP, LEGACY_CAP], inbox: () => failing ? json({ status: 503 }, 503) : json(inbox()) })
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(inboxState('service-unavailable')).not.toBeNull())
    expect(screen.queryByTestId('work-queue-legacy')).toBeNull()
    expect(api.queueReads()).toEqual([])
    failing = false
    await userEvent.setup().click(within(inboxSection()).getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByTestId('work-inbox-list')).toBeTruthy()
    expect(api.inboxReads()).toHaveLength(2)
  })

  it('WP1 D5: a native kind is a card of the same list, with its rule version, and the B-25 queue is never read', async () => {
    const nativeItem = {
      module: 'finance', kind: LEGACY_KIND, itemId: LEGACY_ID, documentLabel: `…${LEGACY_ID.slice(-12)}`,
      link: `/onaylarim/${LEGACY_KIND}/${LEGACY_ID}`, waitingSinceUtc: '2026-10-31T07:00:00+00:00',
      awaitingPermission: 'finance.official-invoice-import.approve', duration: definedDays(false), ruleVersion: RV(LEGACY_KIND),
      explanation: { rules: [{ ruleCode: 'finance.official-invoice-import-approval', threshold: null, actual: null, deviation: null, unit: null }], references: [] },
    }
    const api = installFetch({
      granted: [INBOX_CAP, LEGACY_CAP],
      inbox: () => json(inbox({
        items: [nativeItem, limitItem],
        sources: [src('finance', LIMIT_KIND, 'finance.credit-limits.approve', 'BusinessDays'),
          src('finance', LEGACY_KIND, 'finance.official-invoice-import.approve', 'BusinessDays')],
      })),
    })
    await renderAt('/onaylarim/kuyruk')
    await screen.findByTestId('work-inbox-list')
    expect(cards().map((card) => card.getAttribute('data-kind'))).toEqual([LEGACY_KIND, LIMIT_KIND])
    const native = cardOf(LEGACY_KIND)
    expect(within(native).getByRole('heading', { level: 3, name: 'Resmî fatura içe alma onayı' })).toBeTruthy()
    expect(within(native).getByTestId('work-inbox-awaiting').textContent).toBe(T.awaitingText('Resmî fatura içe alma onayı'))
    expect(within(native).getByRole('link', { name: T.openOwnerLabel('Resmî fatura içe alma onayı', `…${LEGACY_ID.slice(-12)}`) })
      .getAttribute('href')).toBe(`/onaylarim/${LEGACY_KIND}/${LEGACY_ID}`)
    expect(screen.queryByTestId('work-queue-legacy')).toBeNull()
    expect(api.queueReads()).toEqual([])
  })

  it('WP1 D5: a holder of only a native "Onaylarım" capability reads the one inbox (no second list)', async () => {
    const api = installFetch({ granted: [LEGACY_CAP] })
    await renderAt('/onaylarim/kuyruk')
    expect(await screen.findByTestId('work-inbox-list')).toBeTruthy()
    expect(screen.queryByTestId('work-inbox-no-authority')).toBeNull()
    expect(api.inboxReads()).toHaveLength(1)
    expect(api.queueReads()).toEqual([])
  })

  it('opens for another inbox kind\'s existing approve capability', async () => {
    installFetch({ granted: [LOGISTICS_CAPABILITY.planExcessApprove] })
    await renderAt('/onaylarim/kuyruk')
    expect(await screen.findByTestId('work-inbox-list')).toBeTruthy()
  })

  it('an actor with no inbox or queue capability gets the neutral not-found page and nothing is read', async () => {
    const api = installFetch({ granted: [CAPABILITY.cariPartiesList] })
    await renderAt('/onaylarim/kuyruk')
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(api.inboxReads()).toEqual([])
    expect(api.queueReads()).toEqual([])
  })
})

it('opens the request inbox for the approve-only purpose capability and follows the owner purpose link', async () => {
  const kind='purchasing.request.v1',permission='purchasing.requests.approve',id='cccccccc-0000-4000-8000-0000000000f1'
  const link=`/onaylarim/${kind}/${id}`
  const item={...limitItem,module:'purchasing',kind,itemId:id,documentLabel:'SENTETIK talep',link,awaitingPermission:permission,ruleVersion:RV(kind),
    explanation:{rules:[{ruleCode:'purchasing.request-approval-required',threshold:null,actual:null,deviation:null,unit:null}],references:[]}}
  const api=installFetch({granted:[CAPABILITY.purchasingRequestWorkDecision],inbox:()=>json({companyId:COMPANY,view:'awaiting-my-decision',generatedAtUtc:'2026-11-26T08:00:00Z',items:[item],
    sources:[{module:'purchasing',kind,awaitingPermission:permission,durationClass:'BusinessDays',ruleVersion:RV(kind),state:'Ready',hasMore:false}]})})
  await renderAt('/onaylarim/kuyruk');await screen.findByTestId('work-inbox-list')
  const card=cardOf(kind)
  expect(within(card).getByRole('link',{name:T.openOwnerLabel('Satın alma talebi onayı','SENTETIK talep')}).getAttribute('href')).toBe(link)
  expect(api.inboxReads()).toHaveLength(1)
  expect(api.inboxReads()[0].companyId).toBe(COMPANY)
  expect(api.queueReads()).toHaveLength(0)
})

it('opens the credit inbox under the narrow purpose capability and retains the owner decision link', async () => {
 const kind='finance.credit-account.v1',permission='finance.credits.approve',id='cccccccc-0000-4000-8000-0000000000f2'
 const link=`/onaylarim/${kind}/${id}`
 const item={...limitItem,module:'finance',kind,itemId:id,documentLabel:'SENTETIK kredi',link,awaitingPermission:permission,ruleVersion:RV(kind),
  explanation:{rules:[{ruleCode:'finance.credit-account-approval-required',threshold:null,actual:'1000',deviation:null,unit:null}],references:[]}}
 const api=installFetch({granted:[CAPABILITY.financeCreditAccountWorkDecision],inbox:()=>json({companyId:COMPANY,view:'awaiting-my-decision',generatedAtUtc:'2026-11-26T08:00:00Z',items:[item],
  sources:[{module:'finance',kind,awaitingPermission:permission,durationClass:'BusinessDays',ruleVersion:RV(kind),state:'Ready',hasMore:false}]})})
 await renderAt('/onaylarim/kuyruk');await screen.findByTestId('work-inbox-list')
 expect(within(cardOf(kind)).getByRole('link',{name:T.openOwnerLabel('Kredi hesabı','SENTETIK kredi')}).getAttribute('href')).toBe(link)
 expect(api.inboxReads()).toHaveLength(1);expect(api.inboxReads()[0].companyId).toBe(COMPANY);expect(api.queueReads()).toHaveLength(0)
})
