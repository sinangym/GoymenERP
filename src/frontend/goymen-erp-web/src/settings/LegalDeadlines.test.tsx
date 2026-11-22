/** WP15 (1.16): legal deadline calendar (AY-YST-01) and upcoming legal deadlines (AY-YS-01). Synthetic data (SENTETIK). */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { COMPANY_A, commands, installFetch, json, noContent, problem, renderAt } from './testHarness'
import { SETTINGS_CAPABILITY } from './capabilities'
import { decodeLegalDeadlineCalendar } from './legalDeadlineCalendarContract'
import { decodeWorkDeadlines } from './workDeadlinesContract'
import { LEGAL_DEADLINE_TEXT as T, OBLIGATION_TEXT } from './legalDeadlineText'

const READ = '/settings/company-policies/legal-deadline-calendar'
const DEADLINES = '/work/deadlines'
const CALENDAR_PATH = '/ayarlar/yasal-sure-takvimi'
const DEADLINES_PATH = '/ayarlar/yasal-sureler'
const undefinedCalendar = { companyId: COMPANY_A, isDefined: false, version: '0', entries: [], versions: [] }
const definedCalendar = {
  companyId: COMPANY_A, isDefined: true, version: '1',
  entries: [{ obligation: 'VatReturn', periodMonths: 1, monthsAfterPeriodEnd: 1, dueDayOfMonth: 28 }],
  versions: [{ version: '1', entryCount: 1, recordedBy: 'SENTETIK Yönetici', recordedAtUtc: '2026-11-20T07:00:00+00:00', reason: 'SENTETIK teyit' }],
}
const undefinedItem = (code: string) => ({ module: 'settings', kind: 'settings.legal-deadline.v1', code, state: 'undefined',
  periodStart: null, periodEnd: null, dueDate: null, daysRemaining: null, definitionVersion: null, link: CALENDAR_PATH })
const definedItem = { module: 'settings', kind: 'settings.legal-deadline.v1', code: 'settings.legal-deadline.vat-return', state: 'defined',
  periodStart: '2026-10-01', periodEnd: '2026-10-31', dueDate: '2026-11-28', daysRemaining: 6, definitionVersion: 'legal.deadline-calendar:1', link: CALENDAR_PATH }
const settingsSource = { module: 'settings', kind: 'settings.legal-deadline.v1', outcome: 'found', hasMore: false }
const page = (items: unknown[], sources: unknown[] = [settingsSource]) => ({ companyId: COMPANY_A, items, sources })

afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('decodeLegalDeadlineCalendar', () => {
  it('accepts an undefined and a defined calendar', () => {
    expect(decodeLegalDeadlineCalendar(undefinedCalendar, COMPANY_A)?.isDefined).toBe(false)
    expect(decodeLegalDeadlineCalendar(definedCalendar, COMPANY_A)?.entries[0].dueDayOfMonth).toBe(28)
  })

  it.each([
    ['extra member', { ...definedCalendar, extra: 1 }],
    ['another company', { ...definedCalendar, companyId: 'bbbbbbbb-0000-4000-8000-000000000002' }],
    ['undefined with entries', { ...undefinedCalendar, entries: definedCalendar.entries }],
    ['defined without history', { ...definedCalendar, versions: [] }],
    ['unknown obligation', { ...definedCalendar, entries: [{ ...definedCalendar.entries[0], obligation: 'Other' }] }],
    ['period of two months', { ...definedCalendar, entries: [{ ...definedCalendar.entries[0], periodMonths: 2 }] }],
    ['day 32', { ...definedCalendar, entries: [{ ...definedCalendar.entries[0], dueDayOfMonth: 32 }] }],
    ['duplicate obligation', { ...definedCalendar, entries: [definedCalendar.entries[0], definedCalendar.entries[0]],
      versions: [{ ...definedCalendar.versions[0], entryCount: 2 }] }],
    ['entry count mismatch', { ...definedCalendar, versions: [{ ...definedCalendar.versions[0], entryCount: 2 }] }],
  ])('refuses %s', (_, raw) => {
    expect(decodeLegalDeadlineCalendar(raw, COMPANY_A)).toBeNull()
  })
})

describe('decodeWorkDeadlines', () => {
  it('accepts undefined and defined items of a found source', () => {
    const decoded = decodeWorkDeadlines(page([definedItem, undefinedItem('settings.legal-deadline.ba-bs-forms')]), COMPANY_A)
    expect(decoded?.items.map((item) => item.state)).toEqual(['defined', 'undefined'])
  })

  it.each([
    ['an undefined item with a date', page([{ ...undefinedItem('settings.legal-deadline.vat-return'), dueDate: '2026-11-28' }])],
    ['an undefined item with a counter', page([{ ...undefinedItem('settings.legal-deadline.vat-return'), daysRemaining: 3 }])],
    ['a defined item without a date', page([{ ...definedItem, dueDate: null }])],
    ['a negative counter', page([{ ...definedItem, daysRemaining: -1 }])],
    ['an impossible date', page([{ ...definedItem, dueDate: '2026-02-30' }])],
    ['an item of an unavailable source', page([definedItem], [{ ...settingsSource, outcome: 'unavailable' }])],
    ['an item of an unlisted source', page([definedItem], [])],
    ['a code outside its module', page([{ ...definedItem, code: 'finance.x' }])],
    ['another company', { ...page([]), companyId: 'bbbbbbbb-0000-4000-8000-000000000002' }],
  ])('refuses %s', (_, raw) => {
    expect(decodeWorkDeadlines(raw, COMPANY_A)).toBeNull()
  })
})

describe('/ayarlar/yasal-sure-takvimi', () => {
  it('shows an undefined calendar and says no counter runs', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.legalDeadlineCalendar], respond: (url) => url.endsWith(READ) ? json(undefinedCalendar) : undefined })
    renderAt(CALENDAR_PATH)
    expect(await screen.findByTestId('legal-deadline-calendar-undefined')).toHaveProperty('textContent', T.notDefined)
    expect(screen.getByText(T.noShift)).toBeTruthy()
    expect(commands(calls)).toHaveLength(0)
  })

  it('records a first version with expected version 0 and keeps the same key after an unknown outcome', async () => {
    let count = 0
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.legalDeadlineCalendar], respond: (url, init) => {
      if (url.endsWith(READ)) return json(undefinedCalendar)
      if (url.endsWith(`${READ}/versions`) && init?.method === 'POST') return ++count === 1 ? problem(503) : noContent()
      return undefined
    } })
    renderAt(CALENDAR_PATH)
    await screen.findByTestId('legal-deadline-calendar-undefined')
    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: T.edit }))
    await user.click(screen.getByRole('button', { name: T.addRow }))
    // Nothing is preselected: the row cannot be sent until every value is chosen.
    expect(screen.getByRole('button', { name: T.save })).toHaveProperty('disabled', true)
    const row = screen.getByRole('group', { name: T.rowLabel(1) })
    await user.selectOptions(within(row).getByLabelText(`${T.rowLabel(1)} — ${T.obligation}`), 'BaBsForms')
    await user.selectOptions(within(row).getByLabelText(`${T.rowLabel(1)} — ${T.periodMonths}`), '1')
    await user.type(within(row).getByLabelText(`${T.rowLabel(1)} — ${T.monthsAfterPeriodEnd}`), '1')
    await user.type(within(row).getByLabelText(`${T.rowLabel(1)} — ${T.dueDayOfMonth}`), '32')
    await user.type(screen.getByLabelText(T.reason), 'SENTETIK teyit')
    expect(screen.getByText(T.problemDay(1))).toBeTruthy()
    await user.clear(within(row).getByLabelText(`${T.rowLabel(1)} — ${T.dueDayOfMonth}`))
    await user.type(within(row).getByLabelText(`${T.rowLabel(1)} — ${T.dueDayOfMonth}`), '31')
    await user.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', T.unknown)
    await user.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.saved)
    const sent = commands(calls)
    expect(sent).toHaveLength(2)
    expect(sent[0].url).toBe(`/api/v1${READ}/versions`)
    expect(sent[0].body).toEqual({ expectedVersion: '0', entries: [{ obligation: 'BaBsForms', periodMonths: 1, monthsAfterPeriodEnd: 1, dueDayOfMonth: 31 }], reason: 'SENTETIK teyit' })
    expect(sent[1].body).toEqual(sent[0].body)
    expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  })

  it('shows a defined calendar with its history', async () => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.legalDeadlineCalendar], respond: (url) => url.endsWith(READ) ? json(definedCalendar) : undefined })
    renderAt(CALENDAR_PATH)
    expect(await screen.findByText(OBLIGATION_TEXT.VatReturn)).toBeTruthy()
    expect(screen.getByText('SENTETIK teyit')).toBeTruthy()
  })

  it('without its capability the route mounts nothing and reads nothing', async () => {
    const calls = installFetch({ capabilities: [SETTINGS_CAPABILITY.legalDeadlines] })
    renderAt(CALENDAR_PATH)
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(calls.filter((call) => call.url.includes('legal-deadline-calendar'))).toEqual([])
  })
})

describe('/ayarlar/yasal-sureler', () => {
  it('shows every obligation as undefined with no date while the calendar is empty', async () => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.legalDeadlines], respond: (url) => url.endsWith(DEADLINES)
      ? json(page([undefinedItem('settings.legal-deadline.vat-return'), undefinedItem('settings.legal-deadline.ba-bs-forms')])) : undefined })
    renderAt(DEADLINES_PATH)
    const panel = await screen.findByTestId('legal-deadlines')
    await within(panel).findByText(OBLIGATION_TEXT.VatReturn)
    const rows = panel.querySelectorAll('tbody tr')
    expect(rows).toHaveLength(2)
    rows.forEach((row) => {
      expect(row.getAttribute('data-state')).toBe('undefined')
      expect(within(row as HTMLElement).getByText(T.undefinedState)).toBeTruthy()
      expect(row.querySelector('time')).toBeNull()
    })
  })

  it('shows the server-computed due date and counter of a defined obligation', async () => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.legalDeadlines], respond: (url) => url.endsWith(DEADLINES) ? json(page([definedItem])) : undefined })
    renderAt(DEADLINES_PATH)
    expect(await screen.findByText('28.11.2026')).toBeTruthy()
    expect(screen.getByText(T.days(6))).toBeTruthy()
  })

  it('says when a deadline source could not be read', async () => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.legalDeadlines], respond: (url) => url.endsWith(DEADLINES)
      ? json(page([], [{ ...settingsSource, outcome: 'unavailable' }])) : undefined })
    renderAt(DEADLINES_PATH)
    expect(await screen.findByText(T.sourceUnavailable)).toBeTruthy()
  })

  it.each([
    ['service-unavailable', () => problem(503)],
    ['malformed-response', () => json({ ...page([]), extra: 1 })],
  ])('shows the %s state', async (kind, served) => {
    installFetch({ capabilities: [SETTINGS_CAPABILITY.legalDeadlines], respond: (url) => url.endsWith(DEADLINES) ? served() : undefined })
    renderAt(DEADLINES_PATH)
    await waitFor(() => expect(document.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
  })
})
