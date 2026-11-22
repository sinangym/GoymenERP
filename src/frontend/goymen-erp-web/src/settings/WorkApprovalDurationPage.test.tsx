/** AY-OS-01 "Onay süreleri" (W1-A, YOL-D18) over the real shell and a stubbed fetch. Synthetic data (SENTETIK). */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { decodeWorkApprovalDurationPolicy } from './workApprovalDurationContract'
import { WORK_APPROVAL_DURATION_TEXT as T } from './workApprovalDurationText'
import { decodeWorkApprovalRuleTable } from './workApprovalRulesContract'
import { WORK_APPROVAL_RULES_TEXT as R } from './workApprovalRulesText'

const READ = '/settings/company-policies/work-approval-duration'
const PATH = '/ayarlar/onay-sureleri'
/** WP1 D3: a class never recorded carries the decided YOL-D18 default (2 business days) as version "0". */
const empty = { value: 2, version: '0', versions: [] }
const recorded = {
  value: 4, version: '2', versions: [
    { version: '2', value: 4, recordedBy: 'SENTETIK Yönetici', recordedAtUtc: '2026-11-10T07:00:00+00:00', reason: 'SENTETIK ikinci' },
    { version: '1', value: 6, recordedBy: 'SENTETIK Yönetici', recordedAtUtc: '2026-11-01T07:00:00+00:00', reason: 'SENTETIK ilk' },
  ],
}
const RULES = '/work/approval-rules'
const PLAN_KIND = 'manufacturing.plan.v1'
const PAYMENT_KIND = 'finance.manual-bank-payment.v1'
const RECORDER = 'a1000000-0000-4000-8000-000000000001'
const paymentVersion = (version: string, durationClass: string, approverPermission: string, reason: string, at: string) =>
  ({ version, durationClass, approverPermission, escalationPermission: 'finance.manual-bank-payment.approve', recordedBy: RECORDER, recordedAtUtc: at, reason })
const ruleTable = () => ({
  companyId: COMPANY_A,
  rules: [
    {
      kind: PLAN_KIND, ownerModule: 'manufacturing', approverPermission: 'manufacturing.plans.approve', durationClass: 'Hours',
      escalationPermission: null, version: '0', versionText: `work.approval-rule:${PLAN_KIND}:0`, isDefault: true, defaultBasis: 'YOL-D18',
      state: 'Current', recordedBy: null, recordedAtUtc: null, reason: null, versions: [],
    },
    {
      kind: PAYMENT_KIND, ownerModule: 'finance', approverPermission: 'finance.manual-bank-payment.approve', durationClass: 'BusinessDays',
      escalationPermission: 'finance.manual-bank-payment.approve', version: '3', versionText: `work.approval-rule:${PAYMENT_KIND}:3`,
      isDefault: false, defaultBasis: null, state: 'ApproverPermissionChanged', recordedBy: RECORDER,
      recordedAtUtc: '2026-11-20T07:00:00+00:00', reason: 'SENTETIK yükseltme kararı',
      versions: [
        paymentVersion('3', 'BusinessDays', 'finance.old-approve', 'SENTETIK yükseltme kararı', '2026-11-20T07:00:00+00:00'),
        paymentVersion('1', 'Hours', 'finance.old-approve', 'SENTETIK ilk', '2026-11-10T07:00:00+00:00'),
      ],
    },
  ],
})
const view = (overrides: Record<string, unknown> = {}) => ({ companyId: COMPANY_A, hours: recorded, businessDays: empty, ...overrides })

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('decodeWorkApprovalDurationPolicy', () => {
  it('accepts a recorded class and a never-recorded class at the decided default', () => {
    expect(decodeWorkApprovalDurationPolicy(view(), COMPANY_A)?.hours.value).toBe(4)
    expect(decodeWorkApprovalDurationPolicy(view(), COMPANY_A)?.businessDays).toEqual({ value: 2, version: '0', versions: [] })
    expect(decodeWorkApprovalDurationPolicy(view({ hours: { value: 4, version: '0', versions: [] } }), COMPANY_A)?.hours.value).toBe(4)
  })

  it.each([
    ['extra member', { ...view(), extra: true }],
    ['another company', view({ companyId: 'bbbbbbbb-0000-4000-8000-000000000002' })],
    ['never recorded with another value than the decided default', view({ businessDays: { ...empty, value: 3 } })],
    ['never recorded without the decided default', view({ businessDays: { ...empty, value: null } })],
    ['hours default given as the business-day default', view({ hours: { value: 2, version: '0', versions: [] } })],
    ['never recorded with history', view({ businessDays: { ...empty, versions: recorded.versions } })],
    ['value not the newest version', view({ hours: { ...recorded, value: 6 } })],
    ['history out of order', view({ hours: { ...recorded, versions: [...recorded.versions].reverse() } })],
    ['zero value', view({ hours: { value: 0, version: '1', versions: [{ ...recorded.versions[1], value: 0 }] } })],
    ['value as text', view({ hours: { ...recorded, value: '4' } })],
    ['missing class member', view({ hours: { value: 4, version: '2' } })],
    ['bad instant', view({ hours: { ...recorded, versions: [{ ...recorded.versions[0], recordedAtUtc: '2026-11-10' }, recorded.versions[1]] } })],
  ])('refuses %s', (_, raw) => {
    expect(decodeWorkApprovalDurationPolicy(raw, COMPANY_A)).toBeNull()
  })
})

describe('/ayarlar/onay-sureleri', () => {
  it('shows both classes, a recorded one with its history and an unrecorded one as not recorded', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.workApprovalDuration], respond: (url) => url.endsWith(READ) ? json(view()) : undefined })
    renderAt(PATH)
    expect(await screen.findByRole('heading', { level: 1, name: T.title })).toBeTruthy()
    expect(screen.getByText(T.intro)).toBeTruthy()
    const hours = await screen.findByTestId('work-approval-duration-hours')
    expect(within(hours).getByText(`${T.current}: 4 ${T.hoursUnit}`)).toBeTruthy()
    expect(within(hours).getAllByRole('row')).toHaveLength(3)
    const days = screen.getByTestId('work-approval-duration-businessDays')
    expect(within(days).getByText(`${T.current}: 2 ${T.businessDaysUnit} (${T.decidedDefault})`)).toBeTruthy()
    expect(within(days).getByText(T.empty)).toBeTruthy()
    expect(commands(calls)).toHaveLength(0)
  })

  it('records a business-day length with the expected version and keeps the same key after an unknown outcome', async () => {
    let count = 0
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.workApprovalDuration], respond: (url, init) => {
      if (url.endsWith(READ)) return json(view())
      if (url.endsWith(`${READ}/business-days/versions`) && init?.method === 'POST') return ++count === 1 ? problem(503) : noContent()
      return undefined
    } })
    renderAt(PATH)
    const days = await screen.findByTestId('work-approval-duration-businessDays')
    const user = userEvent.setup()
    await user.click(within(days).getByRole('button', { name: T.edit(T.businessDaysHeading) }))
    const input = within(days).getByLabelText(T.newValue(T.businessDaysUnit, 1, 30))
    expect(input).toHaveProperty('value', '')
    // Out of range: not sendable.
    await user.type(input, '31')
    await user.type(within(days).getByLabelText(T.reason), 'SENTETIK karar')
    expect(within(days).getByRole('button', { name: T.save })).toHaveProperty('disabled', true)
    await user.clear(input)
    await user.type(input, '2')
    await user.click(within(days).getByRole('button', { name: T.save }))
    expect(await within(days).findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(within(days).getByRole('button', { name: T.retry }))
    await within(days).findByText(T.saved)
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[0].url).toBe(`/api/v1${READ}/business-days/versions`)
    expect(sent[0].body).toEqual({ expectedVersion: '0', value: 2, reason: 'SENTETIK karar' })
    expect(sent[1].body).toEqual(sent[0].body)
    expect(sent[0].headers['Idempotency-Key']).toBeTruthy()
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  })

  it('a version conflict closes the editor and refreshes', async () => {
    let reads = 0
    installFetch({ capabilities: [SETTINGS_CAPABILITY.workApprovalDuration], respond: (url, init) => {
      if (url.endsWith(READ)) { reads += 1; return json(view()) }
      if (url.endsWith(`${READ}/hours/versions`) && init?.method === 'POST') return problem(409, { code: 'settings_company_policy_version_conflict' })
      return undefined
    } })
    renderAt(PATH)
    const hours = await screen.findByTestId('work-approval-duration-hours')
    const user = userEvent.setup()
    await user.click(within(hours).getByRole('button', { name: T.edit(T.hoursHeading) }))
    await user.type(within(hours).getByLabelText(T.newValue(T.hoursUnit, 1, 168)), '8')
    await user.type(within(hours).getByLabelText(T.reason), 'SENTETIK')
    await user.click(within(hours).getByRole('button', { name: T.save }))
    expect(await within(hours).findByText(T.conflict)).toBeTruthy()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
  })

  it.each([
    ['service-unavailable', () => problem(503)],
    ['malformed-response', () => json({ ...view(), extra: 1 })],
  ])('shows the %s state', async (kind, served) => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.workApprovalDuration], respond: (url) => url.endsWith(READ) ? served() : undefined })
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
  })

  it('WP1: shows the versioned rule table read-only — owner module, approver permission, class, escalation, version', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.workApprovalDuration], respond: (url) =>
      url.endsWith(READ) ? json(view()) : url.endsWith(RULES) ? json(ruleTable()) : undefined })
    renderAt(PATH)
    const table = await screen.findByTestId('work-approval-rules-table')
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => row.getAttribute('data-kind'))).toEqual([PLAN_KIND, PAYMENT_KIND])
    expect(within(rows[0]).getByText('Üretim planı onayı', { selector: 'th' })).toBeTruthy()
    expect(within(rows[0]).getByText('Üretim')).toBeTruthy()
    expect(within(rows[0]).getByText('Saat ölçekli')).toBeTruthy()
    expect(within(rows[0]).getByText(R.escalationUndefined)).toBeTruthy()
    expect(within(rows[0]).getByText(R.defaultVersion('YOL-D18'))).toBeTruthy()
    expect(within(rows[1]).getByText('İş günü ölçekli')).toBeTruthy()
    expect(within(rows[1]).getByText('3')).toBeTruthy()
    expect(within(rows[1]).getByText('SENTETIK yükseltme kararı')).toBeTruthy()
    expect(within(rows[1]).getByTestId('work-approval-rule-approver-changed').textContent).toBe(R.approverChanged)
    // Read only: no command is sent and no edit control is drawn in the rule table.
    expect(within(screen.getByTestId('work-approval-rules')).queryByRole('button')).toBeNull()
    expect(commands(calls)).toHaveLength(0)
    expect(calls.filter((call) => call.url.endsWith(RULES)).map((call) => call.headers['X-Company-Id'])).toEqual([COMPANY_A])
  })

  it.each([
    ['service-unavailable', () => problem(503)],
    ['malformed-response', () => json({ ...ruleTable(), extra: 1 })],
  ])('the rule table shows the %s state without hiding the duration classes', async (kind, served) => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.workApprovalDuration], respond: (url) =>
      url.endsWith(READ) ? json(view()) : url.endsWith(RULES) ? served() : undefined })
    renderAt(PATH)
    expect(await screen.findByTestId('work-approval-duration-hours')).toBeTruthy()
    await waitFor(() => expect(screen.getByTestId('work-approval-rules').querySelector(`[data-state="${kind}"]`)).not.toBeNull())
  })

  it('without its capability the route mounts nothing and reads nothing', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.notificationRetention] })
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(calls.filter((call) => call.url.includes('work-approval-duration'))).toEqual([])
    expect(calls.filter((call) => call.url.endsWith(RULES))).toEqual([])
  })
})

describe('decodeWorkApprovalRuleTable', () => {
  it('accepts the default row and a recorded row whose newest version is in force', () => {
    const decoded = decodeWorkApprovalRuleTable(ruleTable(), COMPANY_A)
    expect(decoded?.rules.map((rule) => [rule.kind, rule.version, rule.isDefault, rule.state])).toEqual([
      [PLAN_KIND, '0', true, 'Current'], [PAYMENT_KIND, '3', false, 'ApproverPermissionChanged'],
    ])
  })

  const recordedRule = () => ruleTable().rules[1] as Record<string, unknown>
  it.each([
    ['extra member', { ...ruleTable(), extra: true }],
    ['another company', { ...ruleTable(), companyId: 'bbbbbbbb-0000-4000-8000-000000000002' }],
    ['a kind twice', { ...ruleTable(), rules: [ruleTable().rules[0], ruleTable().rules[0]] }],
    ['kind of another module', { ...ruleTable(), rules: [{ ...ruleTable().rules[0], ownerModule: 'sales' }] }],
    ['version text of another kind', { ...ruleTable(), rules: [{ ...ruleTable().rules[0], versionText: `work.approval-rule:${PAYMENT_KIND}:0` }] }],
    ['default with an escalation', { ...ruleTable(), rules: [{ ...ruleTable().rules[0], escalationPermission: 'finance.manual-bank-payment.approve' }] }],
    ['default with a recorder', { ...ruleTable(), rules: [{ ...ruleTable().rules[0], recordedBy: RECORDER }] }],
    ['unknown class', { ...ruleTable(), rules: [{ ...ruleTable().rules[0], durationClass: 'Weeks' }] }],
    ['in-force class differs from the newest version', { ...ruleTable(), rules: [ruleTable().rules[0], { ...recordedRule(), durationClass: 'Hours' }] }],
    ['state Current while the permission differs', { ...ruleTable(), rules: [ruleTable().rules[0], { ...recordedRule(), state: 'Current' }] }],
    ['history out of order', { ...ruleTable(), rules: [ruleTable().rules[0], { ...recordedRule(), versions: [...(recordedRule().versions as unknown[])].reverse() }] }],
  ])('refuses %s', (_, raw) => {
    expect(decodeWorkApprovalRuleTable(raw, COMPANY_A)).toBeNull()
  })
})
