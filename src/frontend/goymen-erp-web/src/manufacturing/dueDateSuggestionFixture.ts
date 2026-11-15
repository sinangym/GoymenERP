/**
 * Test-only synthetic `PlanDueDateSuggestionViewV1` bodies in the server's JSON shape (URT-S10, URT-SC33). Every figure (kg,
 * kg/h, queue, margin, shift hours, days) is a component-test value, never a business default or real fixture evidence.
 */

import { PLAN, WORK_CENTER } from './testHarness'

export function suggestedDueDate(overrides: Record<string, unknown> = {}) {
  return {
    contractVersion: 2,
    methodCode: 'calendar_capacity_queue_from_now',
    planId: PLAN,
    planVersion: 1,
    dueDateRevision: 0,
    workCenterId: WORK_CENTER,
    status: 'suggested',
    suggestedDueDate: '2026-09-22',
    completesAtLocal: '2026-09-22T11:00',
    requiredKg: '1500.625',
    requiredWorkingMinutes: 901,
    queuedPlanCount: 2,
    queuedKg: '400.5',
    queueWorkingMinutes: 241,
    safetyMarginHours: '1.5',
    safetyMarginDays: 1,
    totalWorkingMinutes: 1232,
    capacityKgPerHour: '100',
    capacityProfileVersion: '3',
    capacitySource: 'Settings',
    calendarVersion: 7,
    calendarSource: 'WorkCenter',
    timeZoneId: 'Europe/Istanbul',
    reasonCode: null,
    businessDate: '2026-09-18',
    calculatedAtUtc: '2026-09-18T07:00:00+00:00',
    ...overrides,
  }
}

export function unavailableDueDate(reasonCode = 'capacity_not_configured', overrides: Record<string, unknown> = {}) {
  return suggestedDueDate({
    status: 'unavailable',
    suggestedDueDate: null,
    completesAtLocal: null,
    requiredWorkingMinutes: null,
    queuedPlanCount: null,
    queuedKg: null,
    queueWorkingMinutes: null,
    safetyMarginHours: null,
    safetyMarginDays: null,
    totalWorkingMinutes: null,
    capacityKgPerHour: null,
    capacityProfileVersion: null,
    capacitySource: null,
    calendarVersion: null,
    calendarSource: null,
    timeZoneId: null,
    reasonCode,
    ...overrides,
  })
}
