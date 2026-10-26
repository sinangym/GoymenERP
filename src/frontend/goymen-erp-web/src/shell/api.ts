/**
 * Thin typed transport for the shell endpoints.
 *
 * HANDOFF: this hand-written client is temporary. When the canonical backend
 * OpenAPI + generated client land (Issue #122), these types + fetch calls move to
 * `src/api/generated/**` and a contract-drift check enforces the boundary.
 * Until then the shapes mirror the merged backend (PR #132 / d3f2b30):
 *   GET /api/v1/organization/companies  -> CompanySummary[]
 *   GET /api/v1/shell/capabilities      -> ShellCapabilitySnapshot | 503
 *
 * The client carries no business logic. It maps a transport result to a
 * `ViewOutcome` (see ./uiState.ts) and never invents DTO fields.
 */

import type { ViewOutcome } from './uiState'

export interface IdentitySummary {
  readonly userId: string
  readonly displayName: string
  readonly email: string | null
  readonly allowedCompanyIds: readonly string[]
  /**
   * The server's opaque identity of this authenticated session (`IdentityProfile.authenticationSessionBinding`,
   * required-nullable; M01.3 r5 #66/6187473245). Stable across an F5 or a token refresh of the same login, different
   * after a real logout/login or an actor change — as the SERVER decides. `null` is the server's fail-closed answer
   * (Production/OIDC until OPEN-013): normal reads go on, but nothing persisted may be bound to this session.
   *
   * It is compared for equality only. It is not a credential, not a permission and not an idempotency key, and the
   * client never derives, trims, reshapes or substitutes a value for it.
   */
  readonly authenticationSessionBinding: string | null
}

export interface CompanySummary {
  readonly id: string
  readonly code: string
  readonly legalName: string
  readonly isPlaceholder: boolean
}

/** Opaque capability snapshot — the `generation` is compared for equality only. */
export interface ShellCapabilitySnapshot {
  readonly contractVersion: string
  readonly generation: string
  readonly capabilities: readonly string[]
}

/**
 * The exact backend canonical version (ShellCapabilityRegistry.ContractVersion).
 * A snapshot with any other version, or an empty generation, is NOT a valid
 * capability resolution — it fails closed (Codex #139 P1-3).
 */
export const SHELL_CAPABILITY_CONTRACT_VERSION = 'SHELL-CAPABILITY-V1'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

interface RawResult {
  readonly status: number
  /**
   * EXACT 200 only. Codex #139 P1-2: `Response.ok` is true for the whole 2xx range,
   * so a 202/206 with a well-formed body would have been accepted as a resolved
   * context. The accepted contract and OpenAPI define 200 as the only success.
   */
  readonly isOk200: boolean
  /** null when the body could not be parsed as JSON. */
  readonly body: unknown
}

async function rawRequest(
  path: string,
  headers: Record<string, string>,
  signal?: AbortSignal,
): Promise<RawResult> {
  const response = await fetch(`${API_BASE}${path}`, {
    headers: { ...devHeaders(), ...headers },
    signal,
  })
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    body = null
  }
  return { status: response.status, isOk200: response.status === 200, body }
}

/**
 * `authenticationSessionBinding` is required-nullable: the property must be PRESENT, and be `null` or a non-blank
 * string. An absent property, another type or a blank string is a malformed response — never read as `null`, never
 * filled in.
 */
function isSessionBinding(identity: object): boolean {
  if (!Object.hasOwn(identity, 'authenticationSessionBinding')) return false
  const binding = (identity as { readonly authenticationSessionBinding: unknown }).authenticationSessionBinding
  return binding === null || (typeof binding === 'string' && binding.trim().length > 0)
}

function isIdentitySummary(value: unknown): value is IdentitySummary {
  if (typeof value !== 'object' || value === null) return false
  const identity = value as IdentitySummary
  return (
    typeof identity.userId === 'string' &&
    typeof identity.displayName === 'string' &&
    (identity.email === null || typeof identity.email === 'string') &&
    Array.isArray(identity.allowedCompanyIds) &&
    identity.allowedCompanyIds.every((id) => typeof id === 'string') &&
    isSessionBinding(identity)
  )
}

function isCompanySummaryArray(value: unknown): value is CompanySummary[] {
  return (
    Array.isArray(value) &&
    value.every(
      (entry) =>
        typeof entry === 'object' &&
        entry !== null &&
        typeof (entry as CompanySummary).id === 'string' &&
        typeof (entry as CompanySummary).code === 'string' &&
        typeof (entry as CompanySummary).legalName === 'string' &&
        typeof (entry as CompanySummary).isPlaceholder === 'boolean',
    )
  )
}

function isCapabilitySnapshot(value: unknown): value is ShellCapabilitySnapshot {
  if (typeof value !== 'object' || value === null) return false
  const snapshot = value as ShellCapabilitySnapshot
  return (
    // Exact canonical version only — an unknown version fails closed.
    snapshot.contractVersion === SHELL_CAPABILITY_CONTRACT_VERSION &&
    typeof snapshot.generation === 'string' &&
    // A non-empty opaque generation token is required.
    snapshot.generation.trim().length > 0 &&
    Array.isArray(snapshot.capabilities) &&
    snapshot.capabilities.every((capability) => typeof capability === 'string')
  )
}

/**
 * Current authenticated user, with the server's session binding from the SAME response. Context-free.
 *
 * In Development/Testing the server derives the binding from a strict `X-Development-Session` header that the TEST
 * context sends; this client sends none and invents none. Without it the server answers `null`.
 */
export async function fetchIdentity(
  signal?: AbortSignal,
): Promise<ViewOutcome<IdentitySummary>> {
  let result: RawResult
  try {
    result = await rawRequest('/identity/me', {}, signal)
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    // Online, but nothing came back. Rethrowing made TanStack leave `data` undefined,
    // which the caller then read as a MALFORMED RESPONSE — blaming the server's body
    // for a request that never produced one. Found running against the real API.
    return { type: 'network-error' }
  }
  if (!result.isOk200) return { type: 'http', status: result.status }
  if (!isIdentitySummary(result.body)) return { type: 'malformed' }
  return { type: 'ok-single', data: result.body }
}

/** Authorized company directory. Context-free (no X-Company-Id). */
export async function fetchCompanyDirectory(
  signal?: AbortSignal,
): Promise<ViewOutcome<CompanySummary>> {
  let result: RawResult
  try {
    result = await rawRequest('/organization/companies', {}, signal)
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    // Online, but nothing came back. Rethrowing made TanStack leave `data` undefined,
    // which the caller then read as a MALFORMED RESPONSE — blaming the server's body
    // for a request that never produced one. Found running against the real API.
    return { type: 'network-error' }
  }
  if (!result.isOk200) return { type: 'http', status: result.status }
  if (!isCompanySummaryArray(result.body)) return { type: 'malformed' }
  return { type: 'ok', data: result.body, filterActive: false }
}

/** Opaque server-backed capability snapshot for the active company context. */
export async function fetchShellCapabilities(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ShellCapabilitySnapshot>> {
  let result: RawResult
  try {
    result = await rawRequest('/shell/capabilities', { 'X-Company-Id': companyId }, signal)
  } catch {
    if (signal?.aborted) return { type: 'fence-stale' }
    if (!navigator.onLine) return { type: 'offline' }
    // Online, but nothing came back. Rethrowing made TanStack leave `data` undefined,
    // which the caller then read as a MALFORMED RESPONSE — blaming the server's body
    // for a request that never produced one. Found running against the real API.
    return { type: 'network-error' }
  }
  if (!result.isOk200) return { type: 'http', status: result.status }
  if (!isCapabilitySnapshot(result.body)) return { type: 'malformed' }
  return { type: 'ok-single', data: result.body }
}
