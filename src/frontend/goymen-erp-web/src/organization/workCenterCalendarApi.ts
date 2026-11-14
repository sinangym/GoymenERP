/**
 * B-15 — the work-centre calendar command:
 *
 *   PUT /api/v1/organization/work-centers/{id}/calendar  {shifts, closedDates, expectedVersion, reason}  -> 204
 *
 * The wire rules are the master edit's (./masterDataApi.ts), with two differences read off the endpoint:
 *
 *  - `expectedVersion` may be 0 (the first save, while the work centre is on the URT-D11 default), so it goes out as
 *    a JSON number, never as the master edit's `^[1-9][0-9]*$` string token;
 *  - the body replaces the WHOLE calendar. The server canonicalizes it (shifts by weekday and start, dates ascending)
 *    before it fingerprints the key, so the order the form sends is not part of the command's identity; it sends
 *    them sorted anyway so the body is the canonical one.
 *
 * Classification is the same: 204 is the one success; 409 with `conflict` is a stale version, without it a key used
 * for another payload; no response, a 5xx, another 2xx or an answer we cannot read is UNKNOWN — the command may have
 * applied. Nothing here parses a localized title or detail.
 */

import { API_BASE, devHeaders } from './api'
import type { CommandFailure, CommandUnknown } from './commandApi'
import { decodeCalendarVersionConflict, sortShifts, type CalendarShift } from './workCenterCalendarContract'

export interface SetWorkCenterCalendarCommand {
  readonly workCenterId: string
  readonly shifts: readonly CalendarShift[]
  readonly closedDates: readonly string[]
  /** The version READ from the server (0 on the default). Never a predicted one. */
  readonly expectedVersion: number
  /** Raw user text; trimmed here so the wire value is the canonical one. */
  readonly reason: string
}

export type CalendarCommandOutcome =
  | { readonly type: 'updated'; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly failure: CommandFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

/** The exact wire body: sorted, trimmed, nothing the server does not take (no company, no id, no next version). */
export function calendarWireBody(command: SetWorkCenterCalendarCommand): Record<string, unknown> {
  return {
    shifts: sortShifts(command.shifts).map((shift) => ({ day: shift.day, start: shift.start, end: shift.end })),
    closedDates: [...command.closedDates].sort(),
    expectedVersion: command.expectedVersion,
    reason: command.reason.trim(),
  }
}

export function calendarPath(workCenterId: string): string {
  return `/organization/work-centers/${encodeURIComponent(workCenterId)}/calendar`
}

function isObjectBody(body: unknown): body is Record<string, unknown> {
  return typeof body === 'object' && body !== null && !Array.isArray(body)
}

function classify(status: number, body: unknown, bodyParsed: boolean, replayed: boolean): CommandFailure | null {
  if (status === 400) return { kind: 'validation' }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'unsupported-media' }
  if (status === 409) {
    const conflict = decodeCalendarVersionConflict(body)
    if (conflict !== null) return { kind: 'version-conflict', conflict, conflictIsCurrent: !replayed }
    return bodyParsed && isObjectBody(body) ? { kind: 'idempotency-conflict' } : null
  }
  return null
}

/** Sends one calendar command with the caller's key. Never retries on its own and never rotates the key. */
export async function sendWorkCenterCalendar(
  companyId: string,
  idempotencyKey: string,
  command: SetWorkCenterCalendarCommand,
  signal?: AbortSignal,
): Promise<CalendarCommandOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${calendarPath(command.workCenterId)}`, {
      method: 'PUT',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(calendarWireBody(command)),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', unknown: { kind: 'aborted' } }
    if (!navigator.onLine) return { type: 'unknown', unknown: { kind: 'offline' } }
    return { type: 'unknown', unknown: { kind: 'network-error' } }
  }

  const replayed = (response.headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'

  if (response.status === 204) return { type: 'updated', replayed }

  if (response.status >= 200 && response.status < 300) {
    return { type: 'unknown', unknown: { kind: 'unexpected-success', status: response.status } }
  }

  if (response.status >= 500) {
    return { type: 'unknown', unknown: { kind: 'service-unavailable', status: response.status } }
  }

  let parsed: unknown = null
  let bodyParsed = false
  try {
    parsed = await response.json()
    bodyParsed = true
  } catch {
    parsed = null
  }

  const failure = classify(response.status, parsed, bodyParsed, replayed)
  return failure === null
    ? { type: 'unknown', unknown: { kind: 'undecodable', status: response.status } }
    : { type: 'failed', failure, replayed }
}
