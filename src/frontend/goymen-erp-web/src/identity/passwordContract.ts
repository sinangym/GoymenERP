/** ID-2F consumes the existing ID-2 endpoint; passwords stay only in component memory and the POST body. */
export interface PasswordChangeRequest {
  readonly expectedVersion: string
  readonly currentPassword: string
  readonly newPassword: string
}

export type PasswordChangeOutcome =
  | { readonly kind: 'changed' }
  | { readonly kind: 'unauthenticated' }
  | { readonly kind: 'refused'; readonly code: string }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'forbidden' }
  | { readonly kind: 'not-found' }
  | { readonly kind: 'unknown'; readonly reason: 'offline' | 'network-error' | 'service-unavailable' | 'malformed' }

const REQUIRED = ['type', 'title', 'status', 'detail', 'code']
const ALLOWED = new Set([...REQUIRED, 'correlationId', 'conflict', 'field', 'violations', 'expectedVersion', 'currentVersion'])
const BAD_REQUEST_CODES = new Set([
  'antiforgery_token_invalid', 'idempotency_key_invalid',
  'request_invalid', 'password_policy_violation', 'current_password_invalid',
])
const CONFLICT_CODES = new Set(['version_conflict', 'idempotency_conflict'])

function problem(value: unknown, status: number): Record<string, unknown> | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const body = value as Record<string, unknown>
  if (Object.keys(body).some((key) => !ALLOWED.has(key)) || REQUIRED.some((key) => !Object.hasOwn(body, key))) return null
  if (typeof body.type !== 'string' || typeof body.title !== 'string' || typeof body.detail !== 'string' ||
      body.status !== status || typeof body.code !== 'string') return null
  if (body.correlationId !== undefined && body.correlationId !== null && typeof body.correlationId !== 'string') return null
  if (body.conflict !== undefined && body.conflict !== null) return null
  if (body.field !== undefined && typeof body.field !== 'string') return null
  if (body.violations !== undefined && (!Array.isArray(body.violations) || !body.violations.every((item) => typeof item === 'string'))) return null
  for (const key of ['expectedVersion', 'currentVersion']) {
    const version = body[key]
    if (version !== undefined && (typeof version !== 'string' || !/^[1-9]\d*$/.test(version) || BigInt(version) > 9223372036854775807n)) return null
  }
  return body
}

export function classifyPasswordChange(status: number, body: unknown): PasswordChangeOutcome {
  if (status === 204) return { kind: 'changed' }
  if (status === 401) return { kind: 'unauthenticated' }
  if (status === 403) return { kind: 'forbidden' }
  if (status === 404) return { kind: 'not-found' }
  if (status === 415) return { kind: 'refused', code: 'request_invalid' }
  if (status >= 500) return { kind: 'unknown', reason: 'service-unavailable' }
  const decoded = problem(body, status)
  if (decoded && status === 400 && BAD_REQUEST_CODES.has(decoded.code as string)) return { kind: 'refused', code: decoded.code as string }
  if (decoded && status === 409 && CONFLICT_CODES.has(decoded.code as string)) return { kind: 'conflict' }
  return { kind: 'unknown', reason: 'malformed' }
}
