/**
 * ID-1F local sign-in / sign-out contract (`POST /identity/session/login`, `POST /identity/session/logout`;
 * `contracts/openapi/goymen-erp-v1.json` operations `LoginLocalAccountV1` / `LogoutLocalAccountV1`).
 *
 * Both succeed with 204 and no body. The only refusal body this client reads is the 429 problem of KIM-D29
 * (`login_rate_limited`); every other refusal is classified by status alone. In particular a 401 on sign-in is never
 * inspected: the server answers a wrong password, an unknown user, a passive account and a locked account with the
 * same uniform 401 (KIM-D2/D4), and the screen shows one message for all of them, so nothing here may tell them apart.
 */

/** `LocalAccountLoginRequestV1` — sent as typed; the server normalizes the user name, the password is never trimmed. */
export interface LocalLoginRequest {
  readonly userName: string
  readonly password: string
}

export const LOGIN_RATE_LIMITED_CODE = 'login_rate_limited'

const PROBLEM_REQUIRED = ['type', 'title', 'status', 'detail', 'code'] as const
const PROBLEM_OPTIONAL = ['correlationId', 'conflict'] as const

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Record<string, unknown>) : null
}

/**
 * `ErpProblemDetailsContract` with the expected status and code: every required key present and typed, no key the
 * schema does not name, `correlationId` string-or-null when present, `conflict` null when present. Anything else is
 * not that problem.
 */
export function isProblem(value: unknown, status: number, code: string): boolean {
  const body = record(value)
  if (body === null) return false
  const allowed = new Set<string>([...PROBLEM_REQUIRED, ...PROBLEM_OPTIONAL])
  if (Object.keys(body).some((key) => !allowed.has(key))) return false
  if (PROBLEM_REQUIRED.some((key) => !Object.hasOwn(body, key))) return false
  if (typeof body.type !== 'string' || typeof body.title !== 'string' || typeof body.detail !== 'string') return false
  if (body.status !== status || body.code !== code) return false
  if (Object.hasOwn(body, 'correlationId') && body.correlationId !== null && typeof body.correlationId !== 'string') {
    return false
  }
  if (Object.hasOwn(body, 'conflict') && body.conflict !== null) return false
  return true
}

/** The KIM-D29 rate-limit refusal: a 429 whose body is the `login_rate_limited` problem. */
export function isLoginRateLimited(status: number, body: unknown): boolean {
  return status === 429 && isProblem(body, 429, LOGIN_RATE_LIMITED_CODE)
}

/** What a sign-in attempt produced, as the screen needs it. */
export type LoginOutcome =
  | { readonly kind: 'signed-in' }
  /** 401 — one uniform refusal (wrong password, unknown, passive or locked account; KIM-D2/D4). */
  | { readonly kind: 'refused' }
  /** 429 `login_rate_limited` (KIM-D29) — the server did not read the credentials. */
  | { readonly kind: 'rate-limited' }
  /** 404 — the server has no local-account scheme in this environment. */
  | { readonly kind: 'not-enabled' }
  /** 400 / 415 — the request itself was refused (metadata or request-verification header). */
  | { readonly kind: 'request-rejected' }
  | { readonly kind: 'service-unavailable' }
  | { readonly kind: 'offline' }
  | { readonly kind: 'network-error' }
  /** A status the contract does not name, or a 429 without the expected problem body. Fail closed. */
  | { readonly kind: 'unexpected' }

export function classifyLogin(status: number, body: unknown): LoginOutcome {
  if (status === 204) return { kind: 'signed-in' }
  if (status === 401) return { kind: 'refused' }
  if (status === 429) return isLoginRateLimited(status, body) ? { kind: 'rate-limited' } : { kind: 'unexpected' }
  if (status === 404) return { kind: 'not-enabled' }
  if (status === 400 || status === 415) return { kind: 'request-rejected' }
  if (status >= 500) return { kind: 'service-unavailable' }
  return { kind: 'unexpected' }
}

/** What a sign-out attempt produced. */
export type LogoutOutcome =
  | { readonly kind: 'signed-out' }
  /** 401 — the session had already ended (expired, or ended in another tab; KIM-D30). Nothing is left to close. */
  | { readonly kind: 'already-ended' }
  /** 404 — the server has no local-account scheme in this environment. */
  | { readonly kind: 'not-enabled' }
  /** 400 / 403 — refused; the session may still be open. */
  | { readonly kind: 'request-rejected' }
  | { readonly kind: 'service-unavailable' }
  | { readonly kind: 'offline' }
  | { readonly kind: 'network-error' }
  | { readonly kind: 'unexpected' }

export function classifyLogout(status: number): LogoutOutcome {
  if (status === 204) return { kind: 'signed-out' }
  if (status === 401) return { kind: 'already-ended' }
  if (status === 404) return { kind: 'not-enabled' }
  if (status === 400 || status === 403) return { kind: 'request-rejected' }
  if (status >= 500) return { kind: 'service-unavailable' }
  return { kind: 'unexpected' }
}
