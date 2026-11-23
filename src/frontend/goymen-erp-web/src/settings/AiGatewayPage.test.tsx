/** AY-YZG-01 "Yapay zekâ yönetimi" (WP-A5, AI-20) over the real shell and a stubbed fetch. Synthetic data (SENTETIK). */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_AI_GATEWAY_CONTROL, SETTINGS_CAPABILITY } from './capabilities'
import { decodeAiGatewayPolicy, decodeAiGatewayUsage, decodeAiSecurityEventPage, placementAllows } from './aiGatewayContract'
import { AI_FEATURE_LABEL, AI_GATEWAY_TEXT as T } from './aiGatewayText'

const READ = '/settings/company-policies/ai-gateway'
const USAGE = `${READ}/usage`
const VERSIONS = `${READ}/versions`
const EVENTS = '/security-events'
const PATH = '/ayarlar/yapay-zeka-ag-gecidi'
const MANAGE = [SETTINGS_CAPABILITY.aiGateway]
const WITH_EVENTS = [SETTINGS_CAPABILITY.aiGateway, SETTINGS_AI_GATEWAY_CONTROL.events]

const closed = () => ({
  companyId: COMPANY_A, version: '0', monthlyUsageCeiling: null, maxUnitsPerRequest: null, enabledFeatures: [], routes: [],
  currentMonth: { budgetMonth: 202611, committedUnits: 0, entryCount: 0 }, versions: [],
})
const recorded = () => ({
  companyId: COMPANY_A, version: '2', monthlyUsageCeiling: 1000, maxUnitsPerRequest: 100, enabledFeatures: ['Panel'],
  routes: [
    { taskKind: 'Answer', dataClass: 'Public', placement: 'Cloud', modelDeployment: 'SENTETIK-bulut' },
    { taskKind: 'Answer', dataClass: 'Confidential', placement: 'OnPremises', modelDeployment: 'SENTETIK-yerel' },
  ],
  currentMonth: { budgetMonth: 202611, committedUnits: 140, entryCount: 2 },
  versions: [
    { version: '2', monthlyUsageCeiling: 1000, maxUnitsPerRequest: 100, featureCount: 1, routeCount: 2, recordedBy: 'SENTETIK Yönetici', recordedAtUtc: '2026-11-20T07:00:00+00:00', reason: 'SENTETIK açılış' },
    { version: '1', monthlyUsageCeiling: null, maxUnitsPerRequest: null, featureCount: 0, routeCount: 0, recordedBy: 'SENTETIK Yönetici', recordedAtUtc: '2026-11-10T07:00:00+00:00', reason: 'SENTETIK kapalı' },
  ],
})
const usage = (overrides: Record<string, unknown> = {}) => ({
  companyId: COMPANY_A, budgetMonth: 202611, committedUnits: 140, entryCount: 2,
  features: [{ feature: 'Panel', entryCount: 2, openReservationCount: 1, committedUnits: 140 }],
  acceptanceMeasured: false, acceptance: [], ...overrides,
})
const event = (overrides: Record<string, unknown> = {}) => ({
  id: 'e1000000-0000-4000-8000-000000000001', schemaVersion: 1, occurredAtUtc: '2026-11-21T08:00:00+00:00', companyId: COMPANY_A,
  requestedCompanyId: null, actorType: 'User', actorId: 'SENTETIK', eventType: 'ai.request.refused', phase: 'Completed',
  outcome: 'Denied', reasonCode: 'data_class_refused', severity: 'Warning', permissionCode: null, policyId: null, policyVersion: null,
  correlationId: 'corr-sentetik-1', causationId: null, sourceChannel: 'Api', aiToolId: null, aiToolVersion: null, aiRiskClass: null,
  aiDataClass: 'Confidential', retainUntilUtc: '2027-11-21T08:00:00+00:00', occurrenceCount: 1, aggregationWindowStartUtc: null,
  aggregationWindowEndUtc: null, integrityHash: 'sha256:SENTETIK', ...overrides,
})
const page = (items: unknown[], nextCursor: string | null = null) => ({ items, pageSize: 25, nextCursor })

const respond = (policy: () => unknown, extra?: (url: string, init: RequestInit | undefined) => Response | undefined) =>
  (url: string, init: RequestInit | undefined) => {
    const handled = extra?.(url, init)
    if (handled !== undefined) return handled
    if (url.endsWith(USAGE)) return json(usage())
    if (url.endsWith(READ)) return json(policy())
    return undefined
  }

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('decodeAiGatewayPolicy', () => {
  it('accepts a closed (never recorded) and a recorded policy', () => {
    expect(decodeAiGatewayPolicy(closed(), COMPANY_A)?.version).toBe('0')
    expect(decodeAiGatewayPolicy(recorded(), COMPANY_A)?.enabledFeatures).toEqual(['Panel'])
  })

  it.each([
    ['extra member', { ...recorded(), extra: true }],
    ['another company', { ...recorded(), companyId: 'bbbbbbbb-0000-4000-8000-000000000002' }],
    ['a cloud route above Internal (fixed rule)', { ...recorded(), routes: [{ taskKind: 'Answer', dataClass: 'Confidential', placement: 'Cloud', modelDeployment: 'x' }], versions: [{ ...recorded().versions[0], routeCount: 1 }, recorded().versions[1]] }],
    ['a duplicate task × data class cell', { ...recorded(), routes: [recorded().routes[0], recorded().routes[0]] }],
    ['an unknown feature', { ...recorded(), enabledFeatures: ['panel'] }],
    ['a feature open without a budget (YOL-D21)', { ...recorded(), monthlyUsageCeiling: null, versions: [{ ...recorded().versions[0], monthlyUsageCeiling: null }, recorded().versions[1]] }],
    ['never recorded with an open feature', { ...closed(), enabledFeatures: ['Panel'] }],
    ['never recorded with history', { ...closed(), versions: recorded().versions }],
    ['the newest version not the one in force', { ...recorded(), monthlyUsageCeiling: 999 }],
    ['history out of order', { ...recorded(), versions: [...recorded().versions].reverse() }],
    ['a month that is not a month', { ...recorded(), currentMonth: { budgetMonth: 202613, committedUnits: 0, entryCount: 0 } }],
    ['a ceiling as text', { ...recorded(), monthlyUsageCeiling: '1000' }],
  ])('refuses %s', (_, raw) => {
    expect(decodeAiGatewayPolicy(raw, COMPANY_A)).toBeNull()
  })

  it('restates the fixed placement rule: cloud never above Internal', () => {
    expect(placementAllows('Cloud', 'Internal')).toBe(true)
    expect(placementAllows('Cloud', 'Confidential')).toBe(false)
    expect(placementAllows('Cloud', 'StrictlyConfidential')).toBe(false)
    expect(placementAllows('OnPremises', 'StrictlyConfidential')).toBe(true)
  })
})

describe('decodeAiGatewayUsage', () => {
  it('accepts not-measured acceptance and measured counts', () => {
    expect(decodeAiGatewayUsage(usage(), COMPANY_A)?.acceptanceMeasured).toBe(false)
    expect(decodeAiGatewayUsage(usage({ acceptanceMeasured: true, acceptance: [{ feature: 'Panel', accepted: 3, corrected: 1, rejected: 0, reverted: 0 }] }), COMPANY_A)?.acceptance).toHaveLength(1)
  })

  it.each([
    ['lines that do not add up to the month', usage({ committedUnits: 141 })],
    ['acceptance lines while not measured', usage({ acceptance: [{ feature: 'Panel', accepted: 1, corrected: 0, rejected: 0, reverted: 0 }] })],
    ['more open reservations than entries', usage({ features: [{ feature: 'Panel', entryCount: 2, openReservationCount: 3, committedUnits: 140 }] })],
    ['another company', usage({ companyId: 'bbbbbbbb-0000-4000-8000-000000000002' })],
  ])('refuses %s', (_, raw) => {
    expect(decodeAiGatewayUsage(raw, COMPANY_A)).toBeNull()
  })
})

describe('decodeAiSecurityEventPage', () => {
  it('accepts the requested type in the company and refuses another type or company', () => {
    expect(decodeAiSecurityEventPage(page([event()]), COMPANY_A, 'ai.request.refused')?.items).toHaveLength(1)
    expect(decodeAiSecurityEventPage(page([event()]), COMPANY_A, 'ai.tool.called')).toBeNull()
    expect(decodeAiSecurityEventPage(page([event({ companyId: 'bbbbbbbb-0000-4000-8000-000000000002' })]), COMPANY_A, 'ai.request.refused')).toBeNull()
    expect(decodeAiSecurityEventPage({ ...page([]), extra: 1 }, COMPANY_A, 'ai.request.refused')).toBeNull()
  })
})

describe('/ayarlar/yapay-zeka-ag-gecidi', () => {
  it('a closed company shows everything closed, the fixed rules and the data class decisions, and sends no command', async () => {
    const calls = installFetch({ capabilities: MANAGE, respond: respond(closed) })
    renderAt(PATH)
    expect(await screen.findByRole('heading', { level: 1, name: T.title })).toBeTruthy()
    const policy = await screen.findByTestId('ai-policy')
    expect(within(policy).getByText(T.notRecorded)).toBeTruthy()
    expect(within(policy).getAllByText(T.closed)).toHaveLength(9)
    expect(within(policy).getByText(T.routesEmpty)).toBeTruthy()
    const rules = screen.getByTestId('ai-fixed-rules')
    for (const rule of [...T.fixedRules, ...T.decisions]) expect(within(rules).getByText(rule)).toBeTruthy()
    expect(within(rules).queryByRole('button')).toBeNull()
    expect(within(rules).queryByRole('textbox')).toBeNull()
    expect(commands(calls)).toHaveLength(0)
    // Without the event control the security events are never read.
    expect(calls.filter((call) => call.url.includes(EVENTS))).toEqual([])
    expect(screen.queryByTestId('ai-events')).toBeNull()
  })

  it('shows the recorded policy, the task × data class matrix, the month usage and "acceptance not measured yet"', async () => {
    installFetch({ capabilities: MANAGE, respond: respond(recorded) })
    renderAt(PATH)
    const matrix = await screen.findByTestId('ai-route-matrix')
    expect(within(matrix).getByText('Bulut · SENTETIK-bulut')).toBeTruthy()
    expect(within(matrix).getByText('Şirket içi · SENTETIK-yerel')).toBeTruthy()
    const used = await screen.findByTestId('ai-usage')
    expect(within(used).getByText(AI_FEATURE_LABEL.Panel)).toBeTruthy()
    expect(within(used).getByTestId('ai-acceptance-not-measured').textContent).toBe(T.acceptanceNotMeasured)
  })

  it('without a budget the feature switches are locked and the sent snapshot keeps every feature closed', async () => {
    const calls = installFetch({ capabilities: MANAGE, respond: respond(closed, (url, init) =>
      url.endsWith(VERSIONS) && init?.method === 'POST' ? noContent() : undefined) })
    renderAt(PATH)
    const policy = await screen.findByTestId('ai-policy')
    const user = userEvent.setup()
    await user.click(within(policy).getByRole('button', { name: T.edit }))
    const form = within(policy).getByRole('form', { name: T.editorHeading })
    expect(within(form).getByText(T.featuresLocked)).toBeTruthy()
    const panel = within(form).getByRole('checkbox', { name: AI_FEATURE_LABEL.Panel })
    expect(panel).toHaveProperty('disabled', true)
    // Ceiling alone is not a budget: still locked.
    await user.type(within(form).getByLabelText(T.ceilingInput(1_000_000_000_000)), '500')
    expect(within(form).getByRole('checkbox', { name: AI_FEATURE_LABEL.Panel })).toHaveProperty('disabled', true)
    await user.type(within(form).getByLabelText(T.reason), 'SENTETIK hazırlık')
    await user.click(within(form).getByRole('button', { name: T.save }))
    await within(policy).findByText(T.saved)
    const sent = commands(calls)
    expect(sent).toHaveLength(1)
    expect(sent[0].url).toBe(`/api/v1${VERSIONS}`)
    expect(sent[0].body).toEqual({ expectedVersion: '0', monthlyUsageCeiling: 500, maxUnitsPerRequest: null, enabledFeatures: [], routes: [], reason: 'SENTETIK hazırlık' })
  })

  it('records a full snapshot with the expected version, never offers the cloud above Internal, and replays with the same key', async () => {
    let count = 0
    const calls = installFetch({ capabilities: MANAGE, respond: respond(recorded, (url, init) =>
      url.endsWith(VERSIONS) && init?.method === 'POST' ? (++count === 1 ? problem(503) : noContent()) : undefined) })
    renderAt(PATH)
    const policy = await screen.findByTestId('ai-policy')
    const user = userEvent.setup()
    await user.click(within(policy).getByRole('button', { name: T.edit }))
    const form = within(policy).getByRole('form', { name: T.editorHeading })
    await user.click(within(form).getByRole('checkbox', { name: AI_FEATURE_LABEL.Summary }))
    await user.click(within(form).getByRole('button', { name: T.addRoute }))
    const rows = within(form).getAllByTestId('ai-route-row')
    const added = rows[rows.length - 1]
    await user.selectOptions(within(added).getByLabelText(T.dataClass), 'StrictlyConfidential')
    const cloud = within(within(added).getByLabelText(T.placement)).getByRole('option', { name: 'Bulut' })
    expect(cloud).toHaveProperty('disabled', true)
    await user.selectOptions(within(added).getByLabelText(T.taskKind), 'Summarize')
    await user.type(within(added).getByLabelText(T.modelDeployment), 'SENTETIK-yerel-2')
    await user.type(within(form).getByLabelText(T.reason), 'SENTETIK özet açılışı')
    await user.click(within(form).getByRole('button', { name: T.save }))
    expect(await within(form).findByText(T.unknown)).toBeTruthy()
    await user.click(within(form).getByRole('button', { name: T.retry }))
    await within(policy).findByText(T.saved)
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[0].body).toEqual({
      expectedVersion: '2', monthlyUsageCeiling: 1000, maxUnitsPerRequest: 100, enabledFeatures: ['Panel', 'Summary'],
      routes: [
        { taskKind: 'Answer', dataClass: 'Public', placement: 'Cloud', modelDeployment: 'SENTETIK-bulut' },
        { taskKind: 'Answer', dataClass: 'Confidential', placement: 'OnPremises', modelDeployment: 'SENTETIK-yerel' },
        { taskKind: 'Summarize', dataClass: 'StrictlyConfidential', placement: 'OnPremises', modelDeployment: 'SENTETIK-yerel-2' },
      ],
      reason: 'SENTETIK özet açılışı',
    })
    expect(sent[1].body).toEqual(sent[0].body)
    expect(sent[0].headers['Idempotency-Key']).toBeTruthy()
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  })

  it('moving a cloud row above Internal moves it to a company-internal model; a duplicate cell is not sendable', async () => {
    installFetch({ capabilities: MANAGE, respond: respond(recorded) })
    renderAt(PATH)
    const policy = await screen.findByTestId('ai-policy')
    const user = userEvent.setup()
    await user.click(within(policy).getByRole('button', { name: T.edit }))
    const form = within(policy).getByRole('form', { name: T.editorHeading })
    const first = within(form).getAllByTestId('ai-route-row')[0]
    await user.selectOptions(within(first).getByLabelText(T.dataClass), 'Confidential')
    expect(within(first).getByLabelText(T.placement)).toHaveProperty('value', 'OnPremises')
    // Now two rows are Answer × Confidential.
    expect(within(form).getAllByText(T.duplicateRoute).length).toBeGreaterThan(0)
    await user.type(within(form).getByLabelText(T.reason), 'SENTETIK')
    expect(within(form).getByRole('button', { name: T.save })).toHaveProperty('disabled', true)
  })

  it('a version conflict closes the editor and refreshes the policy', async () => {
    let reads = 0
    installFetch({ capabilities: MANAGE, respond: respond(() => { reads += 1; return recorded() }, (url, init) =>
      url.endsWith(VERSIONS) && init?.method === 'POST' ? problem(409, { code: 'settings_company_policy_version_conflict' }) : undefined) })
    renderAt(PATH)
    const policy = await screen.findByTestId('ai-policy')
    const user = userEvent.setup()
    await user.click(within(policy).getByRole('button', { name: T.edit }))
    await user.type(within(policy).getByLabelText(T.reason), 'SENTETIK')
    await user.click(within(policy).getByRole('button', { name: T.save }))
    expect(await within(policy).findByText(T.conflict)).toBeTruthy()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it('with the event control lists AI events of the chosen type, read-only, and pages with the cursor', async () => {
    const calls = installFetch({ capabilities: WITH_EVENTS, respond: respond(recorded, (url) => {
      if (!url.includes(`${EVENTS}?`)) return undefined
      const query = new URL(url, 'http://local').searchParams
      if (query.get('eventType') === 'ai.tool.called') return json(page([event({ eventType: 'ai.tool.called', outcome: 'Allowed', reasonCode: null, aiToolId: 'stock.balance.read', aiToolVersion: '1' })]))
      return json(page([event()], query.get('cursor') === null ? 'c-2' : null))
    }) })
    renderAt(PATH)
    const events = await screen.findByTestId('ai-events')
    expect(await within(events).findByText('data_class_refused')).toBeTruthy()
    const user = userEvent.setup()
    await user.click(within(events).getByRole('button', { name: T.more }))
    await waitFor(() => expect(calls.some((call) => call.url.includes('cursor=c-2'))).toBe(true))
    await user.selectOptions(within(events).getByLabelText(T.eventType), 'ai.tool.called')
    expect(await within(events).findByText('stock.balance.read (1)')).toBeTruthy()
    const reads = calls.filter((call) => call.url.includes(`${EVENTS}?`))
    expect(reads.every((call) => call.method === 'GET' && call.headers['X-Company-Id'] === COMPANY_A)).toBe(true)
    expect(reads.map((call) => new URL(call.url, 'http://local').searchParams.get('eventType'))).toEqual(['ai.request.refused', 'ai.request.refused', 'ai.tool.called'])
    expect(commands(calls)).toHaveLength(0)
  })

  it.each([
    ['service-unavailable', () => problem(503)],
    ['malformed-response', () => json({ ...recorded(), extra: 1 })],
  ])('the policy shows the %s state without hiding the usage', async (kind, served) => {
    installFetch({ capabilities: MANAGE, respond: respond(recorded, (url) => url.endsWith(READ) ? served() : undefined) })
    renderAt(PATH)
    expect(await screen.findByTestId('ai-usage')).toBeTruthy()
    await waitFor(() => expect(document.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
  })

  it('the usage shows its own error state without hiding the policy', async () => {
    installFetch({ capabilities: MANAGE, respond: respond(recorded, (url) => url.endsWith(USAGE) ? problem(503) : undefined) })
    renderAt(PATH)
    expect(await screen.findByTestId('ai-policy')).toBeTruthy()
    await waitFor(() => expect(document.querySelector('[data-state="service-unavailable"]')).not.toBeNull())
  })

  it('without its capability the route mounts nothing and reads nothing', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.notificationRetention] })
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(calls.filter((call) => call.url.includes('ai-gateway') || call.url.includes(EVENTS))).toEqual([])
  })
})
