/**
 * The Organization master edit: create and edit sites, locations and work centres.
 *
 *   POST /api/v1/organization/sites                 {code, name, type, reason}              -> 201 {id, version}
 *   PUT  /api/v1/organization/sites/{id}            {code, name, expectedVersion, reason}   -> 204
 *   POST /api/v1/organization/locations             {siteId, code, name, reason}            -> 201 {id, version}
 *   PUT  /api/v1/organization/locations/{id}        {code, name, expectedVersion, reason}   -> 204
 *   POST /api/v1/organization/work-centers          {locationId, code, name, kind, reason}  -> 201 {id, version}
 *   PUT  /api/v1/organization/work-centers/{id}     {code, name, expectedVersion, reason}   -> 204
 *
 * The wire rules are the active-state command's (see ./commandApi.ts), and so is the classification:
 *
 *  - `Idempotency-Key` is the caller's, one per submitted snapshot. The transport never mints or rotates it: a
 *    repeated key replays the stored terminal answer (`Idempotency-Replayed: true`) and applies nothing again;
 *  - a create answers EXACTLY 201 with the new record's `{id, version}`; an edit answers EXACTLY 204 with no body,
 *    so the fresh state is always re-read and never predicted;
 *  - 409 is two answers told apart by the `conflict` object (stale version) or its absence (the key was used for
 *    another payload); 422 carrying `code: organization_code_in_use` is "the code is taken";
 *  - no response, a 5xx, a 2xx that is not the contract's one success, or an answer we cannot read is UNKNOWN —
 *    the command may have applied. Nothing here parses a localized title or detail.
 */

import { decodeCreatedRecord, decodeVersionConflict, versionToken, type CreatedRecord } from './contract'
import { API_BASE, devHeaders } from './api'
import type { CommandFailure, CommandUnknown } from './commandApi'

export const CODE_IN_USE = 'organization_code_in_use'

export type MasterCommand =
  | { readonly kind: 'create-site'; readonly code: string; readonly name: string; readonly type: string; readonly reason: string }
  | {
      readonly kind: 'update-site'
      readonly id: string
      readonly code: string
      readonly name: string
      readonly expectedVersion: number
      readonly reason: string
    }
  | { readonly kind: 'create-location'; readonly siteId: string; readonly code: string; readonly name: string; readonly reason: string }
  | {
      readonly kind: 'update-location'
      readonly id: string
      readonly code: string
      readonly name: string
      readonly expectedVersion: number
      readonly reason: string
    }
  | {
      readonly kind: 'create-work-center'
      readonly locationId: string
      readonly code: string
      readonly name: string
      readonly workCenterKind: string
      readonly reason: string
    }
  | {
      readonly kind: 'update-work-center'
      readonly id: string
      readonly code: string
      readonly name: string
      readonly expectedVersion: number
      readonly reason: string
    }

/** A definite refusal: the command's own terminal answer, or this request turned away at the gate. */
export type MasterFailure = CommandFailure | { readonly kind: 'code-in-use' }

export type MasterOutcome =
  | { readonly type: 'created'; readonly record: CreatedRecord; readonly replayed: boolean }
  | { readonly type: 'updated'; readonly replayed: boolean }
  | { readonly type: 'failed'; readonly failure: MasterFailure; readonly replayed: boolean }
  | { readonly type: 'unknown'; readonly unknown: CommandUnknown }

interface WireRequest {
  readonly method: 'POST' | 'PUT'
  readonly path: string
  readonly body: Record<string, unknown>
  readonly success: 201 | 204
}

/** The exact wire body per command, trimmed so the fingerprint the server hashes is the canonical one. */
export function wireRequest(command: MasterCommand): WireRequest {
  const text = { code: command.code.trim(), name: command.name.trim() }
  const reason = command.reason.trim()
  switch (command.kind) {
    case 'create-site':
      return {
        method: 'POST',
        path: '/organization/sites',
        body: { ...text, type: command.type, reason },
        success: 201,
      }
    case 'update-site':
      return {
        method: 'PUT',
        path: `/organization/sites/${encodeURIComponent(command.id)}`,
        body: { ...text, expectedVersion: versionToken(command.expectedVersion), reason },
        success: 204,
      }
    case 'create-location':
      return {
        method: 'POST',
        path: '/organization/locations',
        body: { siteId: command.siteId, ...text, reason },
        success: 201,
      }
    case 'update-location':
      return {
        method: 'PUT',
        path: `/organization/locations/${encodeURIComponent(command.id)}`,
        body: { ...text, expectedVersion: versionToken(command.expectedVersion), reason },
        success: 204,
      }
    case 'create-work-center':
      return {
        method: 'POST',
        path: '/organization/work-centers',
        body: { locationId: command.locationId, ...text, kind: command.workCenterKind, reason },
        success: 201,
      }
    case 'update-work-center':
      return {
        method: 'PUT',
        path: `/organization/work-centers/${encodeURIComponent(command.id)}`,
        body: { ...text, expectedVersion: versionToken(command.expectedVersion), reason },
        success: 204,
      }
  }
}

function isObjectBody(body: unknown): body is Record<string, unknown> {
  return typeof body === 'object' && body !== null && !Array.isArray(body)
}

function classify(status: number, body: unknown, bodyParsed: boolean, replayed: boolean): MasterFailure | null {
  if (status === 400) return { kind: 'validation' }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'unsupported-media' }
  if (status === 422) {
    // The one coded answer on this surface; a 422 without it is off-contract, not a guess at "code in use".
    return isObjectBody(body) && body.code === CODE_IN_USE ? { kind: 'code-in-use' } : null
  }
  if (status === 409) {
    const conflict = decodeVersionConflict(body)
    if (conflict !== null) return { kind: 'version-conflict', conflict, conflictIsCurrent: !replayed }
    return bodyParsed && isObjectBody(body) ? { kind: 'idempotency-conflict' } : null
  }
  return null
}

/** Sends one master-data command with the caller's key. Never retries on its own and never rotates the key. */
export async function sendMasterCommand(
  companyId: string,
  idempotencyKey: string,
  command: MasterCommand,
  signal?: AbortSignal,
): Promise<MasterOutcome> {
  const request = wireRequest(command)
  let response: Response
  try {
    response = await fetch(`${API_BASE}${request.path}`, {
      method: request.method,
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'X-Company-Id': companyId,
        'Idempotency-Key': idempotencyKey,
      },
      body: JSON.stringify(request.body),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'unknown', unknown: { kind: 'aborted' } }
    if (!navigator.onLine) return { type: 'unknown', unknown: { kind: 'offline' } }
    return { type: 'unknown', unknown: { kind: 'network-error' } }
  }

  const replayed = (response.headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'

  if (response.status === request.success) {
    if (request.success === 204) return { type: 'updated', replayed }
    let created: CreatedRecord | null = null
    try {
      created = decodeCreatedRecord(await response.json())
    } catch {
      created = null
    }
    // A 201 whose record we cannot read did create SOMETHING: that is unknown, never a refusal.
    return created === null
      ? { type: 'unknown', unknown: { kind: 'undecodable', status: 201 } }
      : { type: 'created', record: created, replayed }
  }

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
