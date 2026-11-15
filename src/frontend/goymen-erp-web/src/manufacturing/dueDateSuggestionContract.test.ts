/**
 * URT-S10 / URT-SC33 termin suggestion decoder: strict keys, lossless text figures, the plan asked for, a day only when
 * `suggested` (never before the business day), the queue and the margin named whole, and a reason only when
 * `unavailable`.
 */

import { describe, expect, it } from 'vitest'
import { decodeDueDateSuggestion } from './dueDateSuggestionContract'
import { suggestedDueDate, unavailableDueDate } from './dueDateSuggestionFixture'
import { PLAN, ROLL_1 } from './testHarness'

describe('decodeDueDateSuggestion', () => {
  it('decodes a suggestion and keeps every figure as the server text', () => {
    const suggestion = decodeDueDateSuggestion(suggestedDueDate(), PLAN)

    expect(suggestion).not.toBeNull()
    expect(suggestion?.suggestedDueDate).toBe('2026-09-22')
    expect(suggestion?.completesAtLocal).toBe('2026-09-22T11:00')
    expect(suggestion?.requiredKg).toBe('1500.625')
    expect(suggestion?.capacityKgPerHour).toBe('100')
    expect(suggestion?.calendarVersion).toBe(7)
    expect(suggestion?.queuedPlanCount).toBe(2)
    expect(suggestion?.queuedKg).toBe('400.5')
    expect(suggestion?.queueWorkingMinutes).toBe(241)
    expect(suggestion?.safetyMarginHours).toBe('1.5')
    expect(suggestion?.safetyMarginDays).toBe(1)
    expect(suggestion?.totalWorkingMinutes).toBe(1232)
  })

  it('decodes an empty queue and no margin as zero', () => {
    const suggestion = decodeDueDateSuggestion(suggestedDueDate({
      queuedPlanCount: 0,
      queuedKg: '0',
      queueWorkingMinutes: 0,
      safetyMarginHours: '0',
      safetyMarginDays: 0,
      totalWorkingMinutes: 901,
    }), PLAN)

    expect(suggestion?.queuedPlanCount).toBe(0)
    expect(suggestion?.safetyMarginHours).toBe('0')
  })

  it('decodes a queue that could not be read (queue_unavailable) with its capacity and margin', () => {
    const suggestion = decodeDueDateSuggestion(unavailableDueDate('queue_unavailable', {
      capacityKgPerHour: '100',
      capacityProfileVersion: '3',
      capacitySource: 'Settings',
      safetyMarginHours: '0',
      safetyMarginDays: 0,
    }), PLAN)

    expect(suggestion?.reasonCode).toBe('queue_unavailable')
    expect(suggestion?.queuedPlanCount).toBeNull()
  })

  it('decodes an unavailable answer with its reason and no day', () => {
    const suggestion = decodeDueDateSuggestion(unavailableDueDate('no_working_time', {
      requiredWorkingMinutes: 901,
      capacityKgPerHour: '100',
      capacityProfileVersion: '3',
      capacitySource: 'Settings',
      calendarVersion: 7,
      calendarSource: 'WorkCenter',
      timeZoneId: 'Europe/Istanbul',
      queuedPlanCount: 2,
      queuedKg: '400.5',
      queueWorkingMinutes: 241,
      safetyMarginHours: '1.5',
      safetyMarginDays: 1,
      totalWorkingMinutes: 1232,
    }), PLAN)

    expect(suggestion?.status).toBe('unavailable')
    expect(suggestion?.suggestedDueDate).toBeNull()
    expect(suggestion?.reasonCode).toBe('no_working_time')
  })

  it('accepts a profile named without a usable capacity value (capacity_invalid)', () => {
    expect(decodeDueDateSuggestion(unavailableDueDate('capacity_invalid', {
      capacityProfileVersion: '3',
      capacitySource: 'Settings',
    }), PLAN)).not.toBeNull()
  })

  it.each([
    ['another plan', suggestedDueDate({ planId: ROLL_1 })],
    ['an unknown key', { ...suggestedDueDate(), extra: true }],
    ['a missing key', (() => {
      const body: Record<string, unknown> = { ...suggestedDueDate() }
      delete body.reasonCode
      return body
    })()],
    ['the first contract version', suggestedDueDate({ contractVersion: 1 })],
    ['a suggestion without its queue', suggestedDueDate({ queuedPlanCount: null, queuedKg: null })],
    ['a half-named queue', suggestedDueDate({ queuedKg: null })],
    ['queued kilograms without a queued plan', suggestedDueDate({ queuedPlanCount: 0 })],
    ['a numeric queued kilogram', suggestedDueDate({ queuedKg: 400.5 })],
    ['a suggestion without its margin', suggestedDueDate({ safetyMarginHours: null, safetyMarginDays: null })],
    ['a half-named margin', suggestedDueDate({ safetyMarginDays: null })],
    ['a negative margin', suggestedDueDate({ safetyMarginHours: '-1' })],
    ['a fractional margin day', suggestedDueDate({ safetyMarginDays: 1.5 })],
    ['a plan time without the total', suggestedDueDate({ totalWorkingMinutes: null })],
    ['a plan time without the queue time', suggestedDueDate({ queueWorkingMinutes: null })],
    ['an unknown status', suggestedDueDate({ status: 'estimated' })],
    ['a numeric kilogram', suggestedDueDate({ requiredKg: 1500.625 })],
    ['a trailing zero', suggestedDueDate({ capacityKgPerHour: '100.0' })],
    ['a zero capacity', suggestedDueDate({ capacityKgPerHour: '0' })],
    ['a day before the business day', suggestedDueDate({ suggestedDueDate: '2026-09-17' })],
    ['a suggestion without a day', suggestedDueDate({ suggestedDueDate: null })],
    ['a suggestion with a reason', suggestedDueDate({ reasonCode: 'no_working_time' })],
    ['a suggestion without its calendar', suggestedDueDate({
      calendarVersion: null, calendarSource: null, timeZoneId: null,
    })],
    ['a half-named calendar', suggestedDueDate({ calendarSource: null })],
    ['a capacity without its profile', suggestedDueDate({ capacityProfileVersion: null, capacitySource: null })],
    ['an invalid local moment', suggestedDueDate({ completesAtLocal: '2026-09-22T24:00' })],
    ['a local moment with an offset', suggestedDueDate({ completesAtLocal: '2026-09-22T11:00+03:00' })],
    ['an invalid day', suggestedDueDate({ suggestedDueDate: '2026-02-30' })],
    ['an unavailable answer without a reason', unavailableDueDate('no_working_time', { reasonCode: null })],
    ['an unavailable answer with a day', unavailableDueDate('no_working_time', { suggestedDueDate: '2026-09-22' })],
    ['a reason that is not a code', unavailableDueDate('Not Configured')],
    ['a nil work center', suggestedDueDate({ workCenterId: '00000000-0000-0000-0000-000000000000' })],
    ['a plan version zero', suggestedDueDate({ planVersion: 0 })],
    ['a negative minute count', suggestedDueDate({ requiredWorkingMinutes: -1 })],
    ['a calculation moment without offset', suggestedDueDate({ calculatedAtUtc: '2026-09-18T07:00:00' })],
  ])('refuses %s', (_name, body) => {
    expect(decodeDueDateSuggestion(body, PLAN)).toBeNull()
  })

  it('refuses a body that is not an object', () => {
    expect(decodeDueDateSuggestion(null, PLAN)).toBeNull()
    expect(decodeDueDateSuggestion([suggestedDueDate()], PLAN)).toBeNull()
  })
})
