/** AY-OS-01 "Onay süreleri" (W1-A, YOL-D18) over the real shell and a stubbed fetch. Synthetic data (SENTETIK). */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { decodeWorkApprovalDurationPolicy } from './workApprovalDurationContract'
import { WORK_APPROVAL_DURATION_TEXT as T } from './workApprovalDurationText'

const READ = '/settings/company-policies/work-approval-duration'
const PATH = '/ayarlar/onay-sureleri'
const empty = { value: null, version: '0', versions: [] }
const recorded = {
  value: 4, version: '2', versions: [
    { version: '2', value: 4, recordedBy: 'SENTETIK Yönetici', recordedAtUtc: '2026-11-10T07:00:00+00:00', reason: 'SENTETIK ikinci' },
    { version: '1', value: 6, recordedBy: 'SENTETIK Yönetici', recordedAtUtc: '2026-11-01T07:00:00+00:00', reason: 'SENTETIK ilk' },
  ],
}
const view = (overrides: Record<string, unknown> = {}) => ({ companyId: COMPANY_A, hours: recorded, businessDays: empty, ...overrides })

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('decodeWorkApprovalDurationPolicy', () => {
  it('accepts a recorded and a never-recorded class', () => {
    expect(decodeWorkApprovalDurationPolicy(view(), COMPANY_A)?.hours.value).toBe(4)
    expect(decodeWorkApprovalDurationPolicy(view(), COMPANY_A)?.businessDays.value).toBeNull()
  })

  it.each([
    ['extra member', { ...view(), extra: true }],
    ['another company', view({ companyId: 'bbbbbbbb-0000-4000-8000-000000000002' })],
    ['never recorded with a value', view({ businessDays: { ...empty, value: 3 } })],
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
    expect(within(days).getByText(`${T.current}: ${T.notRecorded}`)).toBeTruthy()
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

  it('without its capability the route mounts nothing and reads nothing', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.notificationRetention] })
    renderAt(PATH)
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(calls.filter((call) => call.url.includes('work-approval-duration'))).toEqual([])
  })
})
