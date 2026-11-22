/** YOL-D22 "Resmî tatil ve arife takvimi" section of AY-OS-01, over the real shell and a stubbed fetch (SENTETIK data). */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { decodeWorkHolidayCalendar } from './workHolidayCalendarContract'
import { WORK_HOLIDAY_CALENDAR_TEXT as T } from './workHolidayCalendarText'
import { WORK_APPROVAL_DURATION_TEXT } from './workApprovalDurationText'

const YEAR = new Date().getFullYear()
const NEXT = YEAR + 1
const BASE = '/settings/company-policies/work-holiday-calendar'
const DURATION = '/settings/company-policies/work-approval-duration'
const PATH = '/ayarlar/onay-sureleri'
const durationView = { companyId: COMPANY_A, hours: { value: null, version: '0', versions: [] }, businessDays: { value: null, version: '0', versions: [] } }
const undefinedYear = (year = YEAR) => ({ companyId: COMPANY_A, year, isDefined: false, version: '0', days: [], versions: [] })
const entry = (version: string, dayCount: number) =>
  ({ version, dayCount, recordedBy: 'SENTETIK Yönetici', recordedAtUtc: `${YEAR}-01-0${version}T07:00:00+00:00`, reason: `SENTETIK ${version}` })
const definedYear = () => ({
  companyId: COMPANY_A, year: YEAR, isDefined: true, version: '2',
  days: [{ date: `${YEAR}-04-22`, kind: 'Eve' }, { date: `${YEAR}-04-23`, kind: 'Holiday' }],
  versions: [entry('2', 2), entry('1', 0)],
})
const definedEmpty = () => ({ companyId: COMPANY_A, year: YEAR, isDefined: true, version: '1', days: [], versions: [entry('1', 0)] })

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

function serve(calendar: (url: string, init: RequestInit | undefined) => Response | undefined) {
  return installFetch({ capabilities: [SETTINGS_CAPABILITY.workApprovalDuration], respond: (url, init) => {
    if (url.endsWith(DURATION)) return json(durationView)
    return calendar(url, init)
  } })
}
const section = () => screen.findByTestId('work-holiday-calendar')

describe('decodeWorkHolidayCalendar', () => {
  it('accepts an undefined year, a defined list and a defined empty list', () => {
    expect(decodeWorkHolidayCalendar(undefinedYear(), COMPANY_A, YEAR)?.isDefined).toBe(false)
    expect(decodeWorkHolidayCalendar(definedYear(), COMPANY_A, YEAR)?.days).toHaveLength(2)
    expect(decodeWorkHolidayCalendar(definedEmpty(), COMPANY_A, YEAR)?.days).toEqual([])
  })

  it.each([
    ['extra member', { ...definedYear(), extra: 1 }],
    ['another company', { ...definedYear(), companyId: 'bbbbbbbb-0000-4000-8000-000000000002' }],
    ['another year', { ...definedYear(), year: NEXT }],
    ['undefined with days', { ...undefinedYear(), days: [{ date: `${YEAR}-01-01`, kind: 'Holiday' }] }],
    ['undefined with a version', { ...undefinedYear(), version: '1' }],
    ['defined with version 0', { ...definedEmpty(), version: '0', versions: [] }],
    ['day count differs from the list', { ...definedYear(), versions: [entry('2', 3), entry('1', 0)] }],
    ['day outside the year', { ...definedYear(), days: [{ date: `${NEXT}-01-01`, kind: 'Holiday' }, definedYear().days[1]] }],
    ['not a real day', { ...definedYear(), days: [{ date: `${YEAR}-02-30`, kind: 'Holiday' }, definedYear().days[1]] }],
    ['unknown kind', { ...definedYear(), days: [definedYear().days[0], { date: `${YEAR}-04-23`, kind: 'Bridge' }] }],
    ['days out of order', { ...definedYear(), days: [...definedYear().days].reverse() }],
    ['duplicate day', { ...definedYear(), days: [definedYear().days[1], definedYear().days[1]] }],
    ['history out of order', { ...definedYear(), versions: [entry('1', 0), entry('2', 2)] }],
    ['extra day member', { ...definedYear(), days: [{ ...definedYear().days[0], note: 'x' }, definedYear().days[1]] }],
  ])('refuses %s', (_, raw) => {
    expect(decodeWorkHolidayCalendar(raw, COMPANY_A, YEAR)).toBeNull()
  })
})

describe('/ayarlar/onay-sureleri — tatil takvimi', () => {
  it('says only weekends are deducted for an undefined year, seeds nothing, and records an explicit empty list', async () => {
    let saved = false
    const calls = serve((url, init) => {
      if (url.endsWith(`${BASE}/${YEAR}/versions`) && init?.method === 'POST') { saved = true; return noContent() }
      if (url.endsWith(`${BASE}/${YEAR}`)) return json(saved ? definedEmpty() : undefinedYear())
      return undefined
    })
    renderAt(PATH)
    const calendar = await section()
    expect(within(calendar).getByRole('heading', { level: 2, name: T.heading })).toBeTruthy()
    expect((within(calendar).getByLabelText(T.year) as HTMLSelectElement).value).toBe(String(YEAR))
    expect((await within(calendar).findByTestId('work-holiday-calendar-undefined')).textContent).toBe(T.notDefined)
    const user = userEvent.setup()
    await user.click(within(calendar).getByRole('button', { name: T.edit(YEAR) }))
    // Nothing is prefilled: no row exists until the user adds one.
    expect(within(calendar).queryByTestId('work-holiday-calendar-rows')).toBeNull()
    expect(within(calendar).getByText(T.noRows)).toBeTruthy()
    expect(within(calendar).getByTestId('work-holiday-calendar-problems').textContent).toContain(T.problemReason)
    await user.type(within(calendar).getByLabelText(T.reason), 'SENTETIK bu yıl tatil yok')
    await user.click(within(calendar).getByRole('button', { name: T.save }))
    expect(await within(calendar).findByText(T.saved)).toBeTruthy()
    expect((await within(calendar).findByTestId('work-holiday-calendar-empty')).textContent).toBe(T.definedEmpty)
    const sent = commands(calls)
    expect(sent).toHaveLength(1)
    expect(sent[0].url).toBe(`/api/v1${BASE}/${YEAR}/versions`)
    expect(sent[0].body).toEqual({ expectedVersion: '0', days: [], reason: 'SENTETIK bu yıl tatil yok' })
    expect(sent[0].headers['Idempotency-Key']).toBeTruthy()
    expect(sent[0].headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('shows a defined list with its history, edits from the list in force and keeps the same key after an unknown outcome', async () => {
    let posts = 0
    const calls = serve((url, init) => {
      if (url.endsWith(`${BASE}/${YEAR}/versions`) && init?.method === 'POST') return ++posts === 1 ? problem(503) : noContent({ 'Idempotency-Replayed': 'true' })
      if (url.endsWith(`${BASE}/${YEAR}`)) return json(definedYear())
      return undefined
    })
    renderAt(PATH)
    const calendar = await section()
    const days = await within(calendar).findByRole('table', { name: T.daysCaption(YEAR) })
    expect(within(days).getAllByRole('row').slice(1).map((row) => row.textContent))
      .toEqual([`22.04.${YEAR}${T.kindEve}`, `23.04.${YEAR}${T.kindHoliday}`])
    expect(within(within(calendar).getByRole('table', { name: T.history(YEAR) })).getAllByRole('row')).toHaveLength(3)

    const user = userEvent.setup()
    await user.click(within(calendar).getByRole('button', { name: T.edit(YEAR) }))
    expect((within(calendar).getByLabelText(`${T.rowLabel(1)} — ${T.date}`) as HTMLInputElement).value).toBe(`${YEAR}-04-22`)
    // The year cannot change under an open editor.
    expect(within(calendar).getByLabelText(T.year)).toHaveProperty('disabled', true)
    await user.click(within(calendar).getByRole('button', { name: T.addDay }))
    fireEvent.change(within(calendar).getByLabelText(`${T.rowLabel(3)} — ${T.date}`), { target: { value: `${YEAR}-01-01` } })
    await user.selectOptions(within(calendar).getByLabelText(`${T.rowLabel(3)} — ${T.kind}`), 'Holiday')
    await user.type(within(calendar).getByLabelText(T.reason), 'SENTETIK yılbaşı')
    await user.click(within(calendar).getByRole('button', { name: T.save }))
    expect(await within(calendar).findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(within(calendar).getByRole('button', { name: T.retry }))
    expect(await within(calendar).findByText(T.replay)).toBeTruthy()
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[0].body).toEqual({
      expectedVersion: '2',
      days: [{ date: `${YEAR}-01-01`, kind: 'Holiday' }, { date: `${YEAR}-04-22`, kind: 'Eve' }, { date: `${YEAR}-04-23`, kind: 'Holiday' }],
      reason: 'SENTETIK yılbaşı',
    })
    expect(sent[1].body).toEqual(sent[0].body)
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  })

  it('names every validation problem and sends nothing until they are fixed', async () => {
    const calls = serve((url) => url.endsWith(`${BASE}/${YEAR}`) ? json(definedYear()) : undefined)
    renderAt(PATH)
    const calendar = await section()
    const user = userEvent.setup()
    await user.click(await within(calendar).findByRole('button', { name: T.edit(YEAR) }))
    await user.click(within(calendar).getByRole('button', { name: T.addDay }))
    const problems = () => within(calendar).getByTestId('work-holiday-calendar-problems').textContent ?? ''
    expect(problems()).toContain(T.problemDate(3, YEAR))
    expect(problems()).toContain(T.problemKind(3))
    expect(problems()).toContain(T.problemReason)
    // A day of another year, then a repeated day.
    fireEvent.change(within(calendar).getByLabelText(`${T.rowLabel(3)} — ${T.date}`), { target: { value: `${NEXT}-01-01` } })
    expect(problems()).toContain(T.problemDate(3, YEAR))
    fireEvent.change(within(calendar).getByLabelText(`${T.rowLabel(3)} — ${T.date}`), { target: { value: `${YEAR}-04-23` } })
    await user.selectOptions(within(calendar).getByLabelText(`${T.rowLabel(3)} — ${T.kind}`), 'Eve')
    await user.type(within(calendar).getByLabelText(T.reason), 'SENTETIK')
    expect(problems()).toBe(T.problemDuplicate(`23.04.${YEAR}`))
    expect(within(calendar).getByRole('button', { name: T.save })).toHaveProperty('disabled', true)
    // Removing the repeated row clears it.
    await user.click(within(calendar).getByRole('button', { name: T.removeDay(3) }))
    expect(within(calendar).queryByTestId('work-holiday-calendar-problems')).toBeNull()
    expect(within(calendar).getByRole('button', { name: T.save })).toHaveProperty('disabled', false)
    expect(commands(calls)).toHaveLength(0)
  })

  it('a version conflict closes the editor and reads the calendar afresh', async () => {
    let reads = 0
    serve((url, init) => {
      if (url.endsWith(`${BASE}/${YEAR}/versions`) && init?.method === 'POST') return problem(409, { code: 'settings_company_policy_version_conflict' })
      if (url.endsWith(`${BASE}/${YEAR}`)) { reads += 1; return json(definedYear()) }
      return undefined
    })
    renderAt(PATH)
    const calendar = await section()
    const user = userEvent.setup()
    await user.click(await within(calendar).findByRole('button', { name: T.edit(YEAR) }))
    await user.type(within(calendar).getByLabelText(T.reason), 'SENTETIK')
    await user.click(within(calendar).getByRole('button', { name: T.save }))
    expect(await within(calendar).findByText(T.conflict)).toBeTruthy()
    await waitFor(() => expect(reads).toBeGreaterThanOrEqual(2))
    expect(within(calendar).getByRole('button', { name: T.edit(YEAR) })).toBeTruthy()
  })

  it('a refused list shows the refusal and keeps the editor open', async () => {
    serve((url, init) => {
      if (url.endsWith(`${BASE}/${YEAR}/versions`) && init?.method === 'POST') return problem(400, { code: 'settings_company_policy_request_invalid' })
      if (url.endsWith(`${BASE}/${YEAR}`)) return json(definedYear())
      return undefined
    })
    renderAt(PATH)
    const calendar = await section()
    const user = userEvent.setup()
    await user.click(await within(calendar).findByRole('button', { name: T.edit(YEAR) }))
    await user.type(within(calendar).getByLabelText(T.reason), 'SENTETIK')
    await user.click(within(calendar).getByRole('button', { name: T.save }))
    expect(await within(calendar).findByRole('alert')).toHaveProperty('textContent', T.refused)
  })

  it('reads another year when the year changes', async () => {
    const calls = serve((url) => {
      if (url.endsWith(`${BASE}/${YEAR}`)) return json(definedYear())
      if (url.endsWith(`${BASE}/${NEXT}`)) return json(undefinedYear(NEXT))
      return undefined
    })
    renderAt(PATH)
    const calendar = await section()
    await within(calendar).findByRole('table', { name: T.daysCaption(YEAR) })
    await userEvent.setup().selectOptions(within(calendar).getByLabelText(T.year), String(NEXT))
    expect(await within(calendar).findByTestId('work-holiday-calendar-undefined')).toBeTruthy()
    expect(calls.some((call) => call.url === `/api/v1${BASE}/${NEXT}`)).toBe(true)
  })

  it.each([
    ['service-unavailable', () => problem(503, { code: 'settings_provider_unavailable' })],
    ['malformed-response', () => json({ ...definedYear(), extra: 1 })],
  ])('shows the %s state without hiding the duration sections', async (kind, served) => {
    serve((url) => url.endsWith(`${BASE}/${YEAR}`) ? served() : undefined)
    renderAt(PATH)
    const calendar = await section()
    await waitFor(() => expect(calendar.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
    expect(await screen.findByRole('heading', { level: 2, name: WORK_APPROVAL_DURATION_TEXT.hoursHeading })).toBeTruthy()
  })
})
