/** YOL-D25 "Günlük özet saatleri" section of AY-OS-01, over the real shell and a stubbed fetch (SENTETIK data). */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { clockText, decodeWorkDigestSchedule, parseClock } from './workDigestScheduleContract'
import { WORK_DIGEST_SCHEDULE_TEXT as T } from './workDigestScheduleText'
import { WORK_APPROVAL_DURATION_TEXT } from './workApprovalDurationText'

const BASE = '/settings/company-policies/work-digest-schedule'
const DURATION = '/settings/company-policies/work-approval-duration'
const PATH = '/ayarlar/onay-sureleri'
const durationView = { companyId: COMPANY_A, hours: { value: 4, version: '0', versions: [] }, businessDays: { value: 2, version: '0', versions: [] } }
const decided = () => ({
  companyId: COMPANY_A, businessDaysOnly: true, times: [
    { slot: 'morning', minute: 540, version: '0', versions: [] },
    { slot: 'midday', minute: 780, version: '0', versions: [] },
    { slot: 'evening', minute: 1020, version: '0', versions: [] },
  ],
})
const recordedMorning = () => {
  const view = decided()
  view.times[0] = { slot: 'morning', minute: 510, version: '1', versions: [
    { version: '1', value: 510, recordedBy: 'SENTETIK Yönetici', recordedAtUtc: '2026-11-22T07:00:00+00:00', reason: 'SENTETIK 08:30' },
  ] as never[] }
  return view
}

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

function serve(schedule: (url: string, init: RequestInit | undefined) => Response | undefined) {
  return installFetch({ capabilities: [SETTINGS_CAPABILITY.workApprovalDuration], respond: (url, init) => {
    if (url.endsWith(DURATION)) return json(durationView)
    return schedule(url, init)
  } })
}
const section = () => screen.findByTestId('work-digest-schedule')

describe('decodeWorkDigestSchedule', () => {
  it('accepts the decided defaults and a recorded slot', () => {
    expect(decodeWorkDigestSchedule(decided(), COMPANY_A)?.times.map((time) => clockText(time.minute))).toEqual(['09:00', '13:00', '17:00'])
    expect(decodeWorkDigestSchedule(recordedMorning(), COMPANY_A)?.times[0]?.version).toBe('1')
  })

  it.each([
    ['extra member', { ...decided(), extra: 1 }],
    ['another company', { ...decided(), companyId: 'bbbbbbbb-0000-4000-8000-000000000002' }],
    ['not business days only', { ...decided(), businessDaysOnly: false }],
    ['slots out of order', { ...decided(), times: [...decided().times].reverse() }],
    ['a missing slot', { ...decided(), times: decided().times.slice(0, 2) }],
    ['version 0 off the default', { ...decided(), times: [{ ...decided().times[0], minute: 600 }, ...decided().times.slice(1)] }],
    ['a minute past the day', { ...recordedMorning(), times: [{ ...recordedMorning().times[0], minute: 1440 }, ...decided().times.slice(1)] }],
    ['value not the newest history row', { ...recordedMorning(), times: [{ ...recordedMorning().times[0], minute: 520 }, ...decided().times.slice(1)] }],
  ])('refuses %s', (_, raw) => {
    expect(decodeWorkDigestSchedule(raw, COMPANY_A)).toBeNull()
  })

  it('reads and writes the clock as HH:mm only', () => {
    expect([parseClock('08:30'), parseClock('23:59'), parseClock('24:00'), parseClock('8:30'), parseClock('ab')]).toEqual([510, 1439, null, null, null])
    expect(clockText(5)).toBe('00:05')
  })
})

describe('/ayarlar/onay-sureleri — günlük özet saatleri', () => {
  it('shows the decided defaults and saves a new morning time with the expected version, a reason and an Idempotency-Key', async () => {
    let saved = false
    const calls = serve((url, init) => {
      if (url.endsWith(`${BASE}/morning/versions`) && init?.method === 'POST') { saved = true; return noContent() }
      if (url.endsWith(BASE)) return json(saved ? recordedMorning() : decided())
      return undefined
    })
    renderAt(PATH)
    const digest = await section()
    const morning = await within(digest).findByTestId('work-digest-morning')
    expect(morning.textContent).toContain(`${T.current}: 09:00 (${T.decidedDefault})`)
    const user = userEvent.setup()
    await user.click(within(morning).getByRole('button', { name: T.edit(T.slot.morning) }))
    // Nothing is preselected; an invalid clock sends nothing.
    expect((within(morning).getByLabelText(T.newValue) as HTMLInputElement).value).toBe('')
    await user.type(within(morning).getByLabelText(T.newValue), '25:00')
    await user.type(within(morning).getByLabelText(T.reason), 'SENTETIK erken özet')
    expect(within(morning).getByRole('button', { name: T.save })).toHaveProperty('disabled', true)
    await user.clear(within(morning).getByLabelText(T.newValue))
    await user.type(within(morning).getByLabelText(T.newValue), '08:30')
    await user.click(within(morning).getByRole('button', { name: T.save }))
    expect(await within(digest).findByText(T.saved)).toBeTruthy()
    await waitFor(() => expect(within(digest).getByTestId('work-digest-morning').textContent).toContain(`${T.current}: 08:30`))
    const sent = commands(calls)
    expect(sent).toHaveLength(1)
    expect(sent[0].url).toBe(`/api/v1${BASE}/morning/versions`)
    expect(sent[0].body).toEqual({ expectedVersion: '0', minute: 510, reason: 'SENTETIK erken özet' })
    expect(sent[0].headers['Idempotency-Key']).toBeTruthy()
    expect(sent[0].headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('keeps the same key and payload after an unknown outcome and reports a version conflict', async () => {
    let posts = 0
    const calls = serve((url, init) => {
      if (url.endsWith(`${BASE}/evening/versions`) && init?.method === 'POST') {
        posts += 1
        return posts === 1 ? problem(503) : problem(409, { code: 'settings_company_policy_version_conflict' })
      }
      if (url.endsWith(BASE)) return json(decided())
      return undefined
    })
    renderAt(PATH)
    const evening = await within(await section()).findByTestId('work-digest-evening')
    const user = userEvent.setup()
    await user.click(within(evening).getByRole('button', { name: T.edit(T.slot.evening) }))
    await user.type(within(evening).getByLabelText(T.newValue), '17:30')
    await user.type(within(evening).getByLabelText(T.reason), 'SENTETIK akşam')
    await user.click(within(evening).getByRole('button', { name: T.save }))
    expect(await within(evening).findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(within(evening).getByRole('button', { name: T.retry }))
    expect(await within(evening).findByText(T.conflict)).toBeTruthy()
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[1].body).toEqual(sent[0].body)
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  })

  it.each([
    ['service-unavailable', () => problem(503)],
    ['malformed-response', () => json({ companyId: COMPANY_A })],
  ])('shows the %s state without hiding the duration sections', async (kind, served) => {
    serve((url) => url.endsWith(BASE) ? served() : undefined)
    renderAt(PATH)
    const digest = await section()
    await waitFor(() => expect(digest.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
    expect(await screen.findByRole('heading', { level: 2, name: WORK_APPROVAL_DURATION_TEXT.hoursHeading })).toBeTruthy()
  })
})
