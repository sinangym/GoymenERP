/**
 * B-15 work-centre calendar section — component/DOM tests on the ORG-04 card.
 *
 * Fixtures through the real shell, gate and fence (./testHarness). NOT real-API, real-SQL or browser acceptance,
 * and never reported as such: the persistence, replay, revision and audit facts are the host's SqlServerFact suite
 * (`OrganizationWorkCenterCalendarSqlServerRelationalAcceptanceTests`).
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ORGANIZATION_CAPABILITY, workCenterDetailPath } from './capabilities'
import {
  CALENDAR_LIMITS,
  decodeCalendarVersionConflict,
  decodeWorkCenterCalendar,
  isDateToken,
  sortShifts,
} from './workCenterCalendarContract'
import { calendarWireBody } from './workCenterCalendarApi'
import { validateCalendar } from './WorkCenterCalendarSection'
import { WORK_CENTER_CALENDAR_TEXT } from './organizationText'
import {
  COMPANY_A,
  LOC_H0,
  WC_PAINT,
  WORK_CENTER_CARD_CAPABILITIES,
  installFetch,
  json,
  noContent,
  problem,
  renderAt,
  workCenterCalendar,
  workCenterDetail,
  type RecordedRequest,
} from './testHarness'

const CARD_RE = /\/organization\/work-centers\/[^/?]+$/
const EDIT_CAPABILITIES = [...WORK_CENTER_CARD_CAPABILITIES, ORGANIZATION_CAPABILITY.workCenterCalendarEdit]

const SAVED = workCenterCalendar(WC_PAINT, {
  source: 'WorkCenter',
  version: 1,
  shifts: [
    { day: 'Monday', start: '06:00', end: '14:00' },
    { day: 'Monday', start: '14:00', end: '22:00' },
    { day: 'Saturday', start: '08:00', end: '12:00' },
  ],
  closedDates: ['2026-12-31'],
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function calendarWrites(recorded: readonly RecordedRequest[]) {
  return recorded.filter((request) => request.method === 'PUT' && request.url.endsWith(`/${WC_PAINT}/calendar`))
}

function stub(
  options: {
    readonly capabilities?: readonly string[]
    readonly reads?: readonly unknown[]
    readonly command?: ReadonlyArray<() => Response | Promise<Response>>
  } = {},
) {
  const reads = options.reads ?? [workCenterCalendar(WC_PAINT), SAVED]
  const command = options.command ?? [() => noContent()]
  let commands = 0
  let readIndex = 0
  return installFetch({
    capabilities: options.capabilities ?? EDIT_CAPABILITIES,
    respond: (url) => (CARD_RE.test(url) ? json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1')) : undefined),
    calendar: (_url, init) => {
      if (init?.method === 'PUT') {
        const answer = command[Math.min(commands, command.length - 1)]
        commands += 1
        // The calendar is re-read after an accepted command; it never predicts the saved state.
        readIndex = Math.min(readIndex + 1, reads.length - 1)
        return answer()
      }
      return json(reads[readIndex])
    },
  })
}

async function calendarSection() {
  return screen.findByRole('region', { name: WORK_CENTER_CALENDAR_TEXT.heading })
}

async function openForm() {
  const section = await calendarSection()
  await userEvent.click(await within(section).findByRole('button', { name: WORK_CENTER_CALENDAR_TEXT.edit }))
  return section
}

async function typeReason(text = 'Vardiya planı değişti') {
  const reason = screen.getByLabelText('Gerekçe')
  await userEvent.clear(reason)
  await userEvent.type(reason, text)
}

describe('B-15 calendar view', () => {
  it('shows the URT-D11 default as the default, with closed weekends and no edit without the control', async () => {
    const recorded = stub({ capabilities: WORK_CENTER_CARD_CAPABILITIES })
    renderAt(workCenterDetailPath(WC_PAINT))
    const section = await calendarSection()
    await within(section).findByText(WORK_CENTER_CALENDAR_TEXT.sourceDefault)
    expect(section.querySelector('[data-source="Default"]')).not.toBeNull()
    expect(within(section).getByText('Europe/Istanbul')).toBeTruthy()

    const monday = section.querySelector('[data-day="Monday"]') as HTMLElement
    expect(within(monday).getByText('08:00–16:00')).toBeTruthy()
    const sunday = section.querySelector('[data-day="Sunday"]') as HTMLElement
    expect(within(sunday).getByText(WORK_CENTER_CALENDAR_TEXT.closed)).toBeTruthy()
    expect(within(section).getByText(WORK_CENTER_CALENDAR_TEXT.noClosedDates)).toBeTruthy()

    // The edit is the server's control, not the card's: without it nothing is offered.
    expect(within(section).queryByRole('button', { name: WORK_CENTER_CALENDAR_TEXT.edit })).toBeNull()
    const read = recorded.find((request) => request.url.endsWith(`/${WC_PAINT}/calendar`))
    expect(read?.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(calendarWrites(recorded)).toHaveLength(0)
  })

  it('shows a saved calendar with its own source, its shifts per day and its closed dates', async () => {
    stub({ capabilities: WORK_CENTER_CARD_CAPABILITIES, reads: [SAVED] })
    renderAt(workCenterDetailPath(WC_PAINT))
    const section = await calendarSection()
    await within(section).findByText(WORK_CENTER_CALENDAR_TEXT.sourceWorkCenter)
    const monday = section.querySelector('[data-day="Monday"]') as HTMLElement
    expect(monday.textContent).toContain('06:00–14:00, 14:00–22:00')
    const friday = section.querySelector('[data-day="Friday"]') as HTMLElement
    expect(within(friday).getByText(WORK_CENTER_CALENDAR_TEXT.closed)).toBeTruthy()
    expect(within(section).getByText('2026-12-31')).toBeTruthy()
  })

  it('shows an overnight shift (KIM-D33) on the day it starts, marked as ending on the next day', async () => {
    const overnight = workCenterCalendar(WC_PAINT, {
      source: 'WorkCenter',
      version: 2,
      shifts: [
        { day: 'Monday', start: '06:00', end: '14:00' },
        { day: 'Monday', start: '22:00', end: '06:00' },
        { day: 'Sunday', start: '16:00', end: '00:00' },
      ],
      closedDates: [],
    })
    stub({ capabilities: WORK_CENTER_CARD_CAPABILITIES, reads: [overnight] })
    renderAt(workCenterDetailPath(WC_PAINT))
    const section = await calendarSection()
    await within(section).findByText(WORK_CENTER_CALENDAR_TEXT.sourceWorkCenter)
    const monday = section.querySelector('[data-day="Monday"]') as HTMLElement
    expect(monday.textContent).toContain(
      `06:00–14:00, ${WORK_CENTER_CALENDAR_TEXT.shiftWindow('22:00', '06:00', true)}`,
    )
    expect(monday.textContent).toContain('22:00–06:00 (ertesi gün)')
    const tuesday = section.querySelector('[data-day="Tuesday"]') as HTMLElement
    expect(within(tuesday).getByText(WORK_CENTER_CALENDAR_TEXT.closed)).toBeTruthy()
    const sunday = section.querySelector('[data-day="Sunday"]') as HTMLElement
    expect(sunday.textContent).toContain('16:00–00:00 (ertesi gün)')
  })

  it.each([
    ['another work centre', () => workCenterCalendar('cccccccc-3333-4333-8333-333333333333')],
    ['a default that claims a revision', () => workCenterCalendar(WC_PAINT, { version: 2 })],
    ['a saved calendar at version 0', () => workCenterCalendar(WC_PAINT, { source: 'WorkCenter', version: 0 })],
    ['an empty window', () => workCenterCalendar(WC_PAINT, { shifts: [{ day: 'Monday', start: '08:00', end: '08:00' }] })],
    ['an impossible date', () => workCenterCalendar(WC_PAINT, { closedDates: ['2026-02-30'] })],
    ['an extra field', () => ({ ...workCenterCalendar(WC_PAINT), capacity: 8 })],
  ])('shows no calendar for %s, while the card stays', async (_case, body) => {
    installFetch({
      capabilities: WORK_CENTER_CARD_CAPABILITIES,
      respond: (url) => (CARD_RE.test(url) ? json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1')) : undefined),
      calendar: () => json(body()),
    })
    renderAt(workCenterDetailPath(WC_PAINT))
    const section = await calendarSection()
    await waitFor(() => expect(section.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(section.querySelector('[data-source]')).toBeNull()
    expect(screen.getByText('BY-01')).toBeTruthy()
  })

  it('offers a retry when the calendar read fails and shows it on the next read', async () => {
    let calls = 0
    installFetch({
      capabilities: WORK_CENTER_CARD_CAPABILITIES,
      respond: (url) => (CARD_RE.test(url) ? json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1')) : undefined),
      calendar: () => {
        calls += 1
        if (calls === 1) throw new TypeError('Failed to fetch')
        return json(workCenterCalendar(WC_PAINT))
      },
    })
    renderAt(workCenterDetailPath(WC_PAINT))
    const section = await calendarSection()
    await waitFor(() => expect(section.querySelector('[data-state="network-error"]')).not.toBeNull())
    await userEvent.click(within(section).getByRole('button', { name: 'Tekrar dene' }))
    expect(await within(section).findByText(WORK_CENTER_CALENDAR_TEXT.sourceDefault)).toBeTruthy()
    expect(calls).toBe(2)
  })
})

describe('B-15 calendar edit', () => {
  it('saves the first calendar against version 0 with one key and re-reads it', async () => {
    const recorded = stub()
    renderAt(workCenterDetailPath(WC_PAINT))
    await openForm()

    // Add a Saturday morning shift to the default's five weekday rows.
    await userEvent.click(screen.getByRole('button', { name: WORK_CENTER_CALENDAR_TEXT.addShift }))
    const row = screen.getByRole('group', { name: WORK_CENTER_CALENDAR_TEXT.shiftRow(6) })
    await userEvent.selectOptions(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.day), 'Saturday')
    await userEvent.type(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.start), '08:00')
    await userEvent.type(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.end), '12:00')
    await userEvent.click(screen.getByRole('button', { name: WORK_CENTER_CALENDAR_TEXT.addClosedDate }))
    const date = screen.getByRole('group', { name: WORK_CENTER_CALENDAR_TEXT.closedDateRow(1) })
    await userEvent.type(within(date).getByLabelText(WORK_CENTER_CALENDAR_TEXT.closedDate), '2026-12-31')
    await typeReason()
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText(WORK_CENTER_CALENDAR_TEXT.savedTitle)).toBeTruthy()
    const writes = calendarWrites(recorded)
    expect(writes).toHaveLength(1)
    const [write] = writes
    expect(write.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(write.headers['Idempotency-Key']).toBeTruthy()
    const body = write.body as { shifts: unknown[]; closedDates: string[]; expectedVersion: unknown; reason: string }
    expect(body.expectedVersion).toBe(0)
    expect(body.shifts).toHaveLength(6)
    expect(body.shifts[5]).toEqual({ day: 'Saturday', start: '08:00', end: '12:00' })
    expect(body.closedDates).toEqual(['2026-12-31'])
    expect(body.reason).toBe('Vardiya planı değişti')
    expect(Object.keys(body).sort()).toEqual(['closedDates', 'expectedVersion', 'reason', 'shifts'])

    // The re-read calendar, not a prediction of it.
    const section = await calendarSection()
    expect(await within(section).findByText(WORK_CENTER_CALENDAR_TEXT.sourceWorkCenter)).toBeTruthy()
  })

  it('refuses overlapping shifts and a missing reason before minting a key', async () => {
    const recorded = stub()
    renderAt(workCenterDetailPath(WC_PAINT))
    await openForm()
    await userEvent.click(screen.getByRole('button', { name: WORK_CENTER_CALENDAR_TEXT.addShift }))
    const row = screen.getByRole('group', { name: WORK_CENTER_CALENDAR_TEXT.shiftRow(6) })
    await userEvent.type(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.start), '15:00')
    await userEvent.type(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.end), '18:00')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText(WORK_CENTER_CALENDAR_TEXT.shiftsOverlap)).toBeTruthy()
    expect(screen.getByLabelText('Gerekçe').getAttribute('aria-invalid')).toBe('true')
    expect(calendarWrites(recorded)).toHaveLength(0)
  })

  it('saves an overnight shift (KIM-D33) as typed and marks it as ending on the next day', async () => {
    const recorded = stub()
    renderAt(workCenterDetailPath(WC_PAINT))
    await openForm()
    await userEvent.click(screen.getByRole('button', { name: WORK_CENTER_CALENDAR_TEXT.addShift }))
    const row = screen.getByRole('group', { name: WORK_CENTER_CALENDAR_TEXT.shiftRow(6) })
    await userEvent.selectOptions(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.day), 'Friday')
    await userEvent.type(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.start), '22:00')
    expect(row.querySelector('[data-next-day]')).toBeNull()
    await userEvent.type(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.end), '06:00')
    expect(within(row).getByText(WORK_CENTER_CALENDAR_TEXT.nextDay)).toBeTruthy()
    await typeReason()
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText(WORK_CENTER_CALENDAR_TEXT.savedTitle)).toBeTruthy()
    const writes = calendarWrites(recorded)
    expect(writes).toHaveLength(1)
    const body = writes[0].body as { shifts: unknown[] }
    expect(body.shifts).toContainEqual({ day: 'Friday', start: '22:00', end: '06:00' })
  })

  it('refuses an overnight shift whose next-day part overlaps the next day before minting a key', async () => {
    const recorded = stub()
    renderAt(workCenterDetailPath(WC_PAINT))
    await openForm()
    // The default has Tuesday 08:00-16:00; Monday 22:00-09:00 runs into it.
    await userEvent.click(screen.getByRole('button', { name: WORK_CENTER_CALENDAR_TEXT.addShift }))
    const row = screen.getByRole('group', { name: WORK_CENTER_CALENDAR_TEXT.shiftRow(6) })
    await userEvent.type(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.start), '22:00')
    await userEvent.type(within(row).getByLabelText(WORK_CENTER_CALENDAR_TEXT.end), '09:00')
    await typeReason()
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText(WORK_CENTER_CALENDAR_TEXT.shiftsOverlap)).toBeTruthy()
    expect(calendarWrites(recorded)).toHaveLength(0)
  })

  it('refuses a calendar with no shift at all', async () => {
    const recorded = stub()
    renderAt(workCenterDetailPath(WC_PAINT))
    await openForm()
    for (let index = 5; index >= 1; index -= 1) {
      const row = screen.getByRole('group', { name: WORK_CENTER_CALENDAR_TEXT.shiftRow(index) })
      await userEvent.click(within(row).getByRole('button', { name: new RegExp(WORK_CENTER_CALENDAR_TEXT.removeShift) }))
    }
    await typeReason()
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText(WORK_CENTER_CALENDAR_TEXT.noShift)).toBeTruthy()
    expect(calendarWrites(recorded)).toHaveLength(0)
  })

  it('reports a stale version, keeps what was typed and saves against the re-read version', async () => {
    const stale = workCenterCalendar(WC_PAINT, { source: 'WorkCenter', version: 3 })
    const recorded = stub({
      reads: [workCenterCalendar(WC_PAINT), stale, stale],
      command: [
        () => problem(409, { conflict: { expectedVersion: 0, currentVersion: 3 } }),
        () => noContent(),
      ],
    })
    renderAt(workCenterDetailPath(WC_PAINT))
    await openForm()
    await typeReason('Yeni plan')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    const alert = await screen.findByRole('alert')
    expect(alert.getAttribute('data-outcome')).toBe('version-conflict')
    await userEvent.click(within(alert).getByRole('button', { name: 'Güncel kaydı yükle' }))
    expect(await screen.findByText(WORK_CENTER_CALENDAR_TEXT.conflictReloadedTitle)).toBeTruthy()
    expect((screen.getByLabelText('Gerekçe') as HTMLTextAreaElement).value).toBe('Yeni plan')

    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    await screen.findByText(WORK_CENTER_CALENDAR_TEXT.savedTitle)
    const writes = calendarWrites(recorded)
    expect(writes).toHaveLength(2)
    expect((writes[0].body as { expectedVersion: number }).expectedVersion).toBe(0)
    expect((writes[1].body as { expectedVersion: number }).expectedVersion).toBe(3)
    // Two separate attempts, two keys.
    expect(writes[1].headers['Idempotency-Key']).not.toBe(writes[0].headers['Idempotency-Key'])
  })

  it('freezes on an unknown outcome and resends the same snapshot with the same key', async () => {
    const recorded = stub({
      command: [() => problem(503), () => noContent({ 'Idempotency-Replayed': 'true' })],
    })
    renderAt(workCenterDetailPath(WC_PAINT))
    await openForm()
    await typeReason()
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    const alert = await screen.findByRole('alert')
    expect(alert.getAttribute('data-outcome')).toBe('unknown')
    expect(screen.queryByRole('button', { name: 'Kaydet' })).toBeNull()
    expect((screen.getByLabelText('Gerekçe') as HTMLTextAreaElement).readOnly).toBe(true)

    await userEvent.click(within(alert).getByRole('button', { name: 'Aynı işlem anahtarıyla tekrar gönder' }))
    await waitFor(() => expect(calendarWrites(recorded)).toHaveLength(2))
    const [first, second] = calendarWrites(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it.each([
    [400, 'validation'],
    [403, 'forbidden'],
    [404, 'not-found'],
  ])('closes the attempt on a definite %s refusal', async (status, kind) => {
    const recorded = stub({ command: [() => problem(status)] })
    renderAt(workCenterDetailPath(WC_PAINT))
    await openForm()
    await typeReason()
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    const alert = await waitFor(() => {
      const found = document.querySelector(`[data-outcome="${kind}"]`)
      expect(found).not.toBeNull()
      return found as HTMLElement
    })
    expect(alert).toBeTruthy()
    expect(calendarWrites(recorded)).toHaveLength(1)
    // Not frozen: the user may correct and try again.
    expect((screen.getByLabelText('Gerekçe') as HTMLTextAreaElement).readOnly).toBe(false)
  })
})

describe('B-15 calendar contract', () => {
  it('decodes the default at version 0 and a saved calendar at its revision', () => {
    expect(decodeWorkCenterCalendar(workCenterCalendar(WC_PAINT), WC_PAINT)?.version).toBe(0)
    expect(decodeWorkCenterCalendar(SAVED, WC_PAINT.toUpperCase())?.source).toBe('WorkCenter')
    expect(decodeWorkCenterCalendar({ ...SAVED, version: -1 }, WC_PAINT)).toBeNull()
    expect(decodeWorkCenterCalendar({ ...SAVED, version: 1.5 }, WC_PAINT)).toBeNull()
    expect(decodeWorkCenterCalendar({ ...SAVED, timeZoneId: ' ' }, WC_PAINT)).toBeNull()
    expect(decodeWorkCenterCalendar({ ...SAVED, shifts: [{ day: 'Mon', start: '08:00', end: '16:00' }] }, WC_PAINT)).toBeNull()
  })

  it('accepts a conflict at version 0 and refuses a negative one', () => {
    expect(decodeCalendarVersionConflict({ conflict: { expectedVersion: 2, currentVersion: 0 } })).toEqual({
      expectedVersion: 2,
      currentVersion: 0,
    })
    expect(decodeCalendarVersionConflict({ conflict: { expectedVersion: -1, currentVersion: 0 } })).toBeNull()
    expect(decodeCalendarVersionConflict({})).toBeNull()
  })

  it('sends the canonical body: sorted shifts and dates, a numeric version, a trimmed reason', () => {
    const body = calendarWireBody({
      workCenterId: WC_PAINT,
      shifts: [
        { day: 'Sunday', start: '08:00', end: '12:00' },
        { day: 'Monday', start: '14:00', end: '22:00' },
        { day: 'Monday', start: '06:00', end: '14:00' },
      ],
      closedDates: ['2027-01-01', '2026-12-31'],
      expectedVersion: 0,
      reason: '  Plan  ',
    })
    expect(body).toEqual({
      shifts: [
        { day: 'Monday', start: '06:00', end: '14:00' },
        { day: 'Monday', start: '14:00', end: '22:00' },
        { day: 'Sunday', start: '08:00', end: '12:00' },
      ],
      closedDates: ['2026-12-31', '2027-01-01'],
      expectedVersion: 0,
      reason: 'Plan',
    })
  })

  it('checks the definition rules the server enforces', () => {
    const shift = (day: string, start: string, end: string) => ({ day, start, end })
    expect(validateCalendar([shift('Monday', '08:00', '16:00')], [])).toEqual({})
    expect(validateCalendar([], []).shifts).toBe(WORK_CENTER_CALENDAR_TEXT.noShift)
    expect(validateCalendar([shift('Monday', '08:00', '08:00')], []).shifts).toBe(WORK_CENTER_CALENDAR_TEXT.shiftInvalid)
    expect(validateCalendar([shift('Monday', '00:00', '00:00')], []).shifts).toBe(WORK_CENTER_CALENDAR_TEXT.shiftInvalid)
    // KIM-D33: an end before the start spills into the next day; one that ends at midnight is overnight too.
    expect(validateCalendar([shift('Monday', '16:00', '08:00')], [])).toEqual({})
    expect(validateCalendar([shift('Monday', '16:00', '00:00'), shift('Tuesday', '00:00', '08:00')], [])).toEqual({})
    expect(
      validateCalendar([shift('Monday', '22:00', '06:00'), shift('Tuesday', '05:59', '14:00')], []).shifts,
    ).toBe(WORK_CENTER_CALENDAR_TEXT.shiftsOverlap)
    expect(
      validateCalendar([shift('Monday', '22:00', '06:00'), shift('Monday', '23:00', '23:30')], []).shifts,
    ).toBe(WORK_CENTER_CALENDAR_TEXT.shiftsOverlap)
    // A Sunday overnight shift wraps into Monday of the next week.
    expect(
      validateCalendar([shift('Sunday', '22:00', '06:00'), shift('Monday', '05:00', '13:00')], []).shifts,
    ).toBe(WORK_CENTER_CALENDAR_TEXT.shiftsOverlap)
    expect(validateCalendar([shift('Sunday', '22:00', '06:00'), shift('Monday', '06:00', '14:00')], [])).toEqual({})
    expect(
      validateCalendar(
        [shift('Monday', '06:00', '14:00'), shift('Monday', '14:00', '22:00'), shift('Monday', '22:00', '06:00'), shift('Tuesday', '06:00', '14:00')],
        [],
      ),
    ).toEqual({})
    expect(
      validateCalendar([shift('Monday', '08:00', '16:00'), shift('Monday', '12:00', '18:00')], []).shifts,
    ).toBe(WORK_CENTER_CALENDAR_TEXT.shiftsOverlap)
    // Touching windows do not overlap.
    expect(validateCalendar([shift('Monday', '08:00', '16:00'), shift('Monday', '16:00', '23:00')], [])).toEqual({})
    const tooManyOnOneDay = Array.from({ length: CALENDAR_LIMITS.shiftsPerDay + 1 }, (_, index) =>
      shift('Tuesday', `${String(index).padStart(2, '0')}:00`, `${String(index).padStart(2, '0')}:30`),
    )
    expect(validateCalendar(tooManyOnOneDay, []).shifts).toBe(WORK_CENTER_CALENDAR_TEXT.tooManyShifts)
    expect(validateCalendar([shift('Monday', '08:00', '16:00')], ['2026-12-31', '2026-12-31']).dates).toBe(
      WORK_CENTER_CALENDAR_TEXT.duplicateClosedDate,
    )
    expect(validateCalendar([shift('Monday', '08:00', '16:00')], ['2026-02-29']).dates).toBe(
      WORK_CENTER_CALENDAR_TEXT.dateInvalid,
    )
  })

  it('knows real dates and the Monday-first order', () => {
    expect(isDateToken('2028-02-29')).toBe(true)
    expect(isDateToken('2026-02-29')).toBe(false)
    expect(isDateToken('2026-13-01')).toBe(false)
    expect(
      sortShifts([
        { day: 'Sunday', start: '08:00', end: '09:00' },
        { day: 'Monday', start: '10:00', end: '11:00' },
      ]).map((shift) => shift.day),
    ).toEqual(['Monday', 'Sunday'])
  })
})
