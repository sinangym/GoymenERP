/**
 * Product (Ürün) transport — U-01 list, U-01 360 read and U-02 Draft create.
 *
 * PROPOSED_JIT: see ./contract.ts for why every path and field name below is a
 * request to CODEX-BACKEND-URUN rather than a merged contract.
 *
 * The transport carries no business logic and invents no field:
 *  - NO `X-Company-Id` on any Product request. `PRD-SCOPE-001=A` makes the Product
 *    master GLOBAL — one UUID/code/card shared by both Göymen companies — and JIT
 *    spec §15 is explicit: "Frontend Product'a CompanyId eklemez". Sending one
 *    would be the client asserting a scope the locked decision removed. Company
 *    scope begins on child records (price, package, stock, label), none of which
 *    this slice reads;
 *  - ONE `Idempotency-Key` per logical create attempt, supplied by the caller. The
 *    transport never mints or rotates one;
 *  - success for the create is EXACTLY 201. A 200 or 202 is not in the request and
 *    is reported as an unknown outcome, never as success;
 *  - success for a read is EXACTLY 200 — `Response.ok` spans the whole 2xx range,
 *    and a 202/206 with a well-formed body is not a resolved read.
 *
 * DELIBERATELY NOT COPIED from the Cari transport: the `X-ERP-Counterparty-Contract`
 * client-contract handshake. That gate exists because the Cari server publishes an
 * acknowledgment header; inventing a Product equivalent would be a compatibility
 * claim no server makes. When the Product backend publishes one, it lands here.
 */

import {
  decodeProductDetail,
  decodeProductDraftCreated,
  decodeProductPage,
  productProblemCodeOf,
  type ProductDetail,
  type ProductDraftCreated,
  type ProductPage,
  type ProductProblemCode,
  type ProductRoleName,
} from './contract'
import type { ViewOutcome } from '../shell/uiState'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

/* ── Query (spec §8.1) ────────────────────────────────────────────────────── */

export type LifecycleFilter = 'Draft' | 'Active' | 'Inactive' | 'all'
/** §8.1: "Role filter'ında `any/all` semantiği UI'da açık olur." */
export type RoleMatch = 'any' | 'all'
export type CompletenessFilter = 'all' | 'incomplete'
export type ProductSortField = 'code' | 'name' | 'lastChangedAt'
export type SortDirection = 'asc' | 'desc'

export interface ProductQuery {
  /** Free search over code, alias and name (§8.1). Trim follows the server standard. */
  readonly q?: string
  readonly lifecycle?: LifecycleFilter
  readonly family?: string
  readonly role?: readonly ProductRoleName[]
  readonly roleMatch?: RoleMatch
  /** Narrows to products with an unmet template/unit/package requirement (§8.1). */
  readonly completeness?: CompletenessFilter
  readonly sort?: ProductSortField
  readonly direction?: SortDirection
  readonly pageSize?: number
  /** Opaque server token, echoed verbatim. The client never builds one (§8.3). */
  readonly cursor?: string
  readonly includeCount?: boolean
}

export function buildProductQuery(query: ProductQuery): string {
  const params = new URLSearchParams()
  if (query.q !== undefined && query.q.trim().length > 0) params.set('q', query.q.trim())
  if (query.lifecycle !== undefined && query.lifecycle !== 'all') {
    params.set('lifecycle', query.lifecycle)
  }
  if (query.family !== undefined && query.family.trim().length > 0) {
    params.set('family', query.family.trim())
  }
  // `role` is repeatable — never comma-joined into a single value.
  for (const role of query.role ?? []) params.append('role', role)
  if (query.roleMatch !== undefined) params.set('roleMatch', query.roleMatch)
  if (query.completeness !== undefined && query.completeness !== 'all') {
    params.set('completeness', query.completeness)
  }
  if (query.sort !== undefined) params.set('sort', query.sort)
  if (query.direction !== undefined) params.set('direction', query.direction)
  if (query.pageSize !== undefined) params.set('pageSize', String(query.pageSize))
  if (query.cursor !== undefined) params.set('cursor', query.cursor)
  if (query.includeCount !== undefined) params.set('includeCount', String(query.includeCount))
  const serialized = params.toString()
  return serialized.length > 0 ? `?${serialized}` : ''
}

/* ── Reads ────────────────────────────────────────────────────────────────── */

interface RawResult {
  readonly status: number
  readonly body: unknown
}

async function rawRead(
  path: string,
  signal: AbortSignal | undefined,
): Promise<RawResult | { readonly failure: 'aborted' | 'offline' | 'unreachable' }> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { headers: devHeaders(), signal })
  } catch {
    if (signal?.aborted) return { failure: 'aborted' }
    if (!navigator.onLine) return { failure: 'offline' }
    // Online, but nothing came back. That is a different fact from a bad response
    // body, and reporting it as `malformed` would blame the server's payload for a
    // request that never produced one.
    return { failure: 'unreachable' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    // The headers arrived, so the abort branch above never ran — but the body can
    // still be cut off mid-stream when the caller cancels.
    if (signal?.aborted) return { failure: 'aborted' }
    body = null
  }
  return { status: response.status, body }
}

function failureOutcome<T>(failure: 'aborted' | 'offline' | 'unreachable'): ViewOutcome<T> {
  if (failure === 'aborted') return { type: 'fence-stale' }
  if (failure === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

/** U-01 list. Global scope by design — no company header (see the module note). */
export async function fetchProducts(
  query: ProductQuery,
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductPage>> {
  const result = await rawRead(`/products${buildProductQuery(query)}`, signal)
  if ('failure' in result) return failureOutcome(result.failure)
  if (result.status !== 200) return { type: 'http', status: result.status, body: result.body }
  const page = decodeProductPage(result.body, query.includeCount === true)
  if (page === null) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}

/** U-01 Product 360. The decoder binds the response to `productId` (§9). */
export async function fetchProductDetail(
  productId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductDetail>> {
  const result = await rawRead(`/products/${encodeURIComponent(productId)}`, signal)
  if ('failure' in result) return failureOutcome(result.failure)
  if (result.status !== 200) return { type: 'http', status: result.status, body: result.body }
  const detail = decodeProductDetail(result.body, productId)
  if (detail === null) return { type: 'malformed' }
  return { type: 'ok-single', data: detail }
}

/* ── U-02 Draft create ────────────────────────────────────────────────────── */

/**
 * The Draft create body.
 *
 * NOT here, and each for a stated reason:
 *  - `code` — server-generated from a group prefix + sequence (§5.1, §7). The admin
 *    manual-code exception needs a permission string that does not exist yet, and
 *    §6.6 keeps ungated Product commands fail-closed, so this slice does not offer
 *    it at all rather than offering a field the server would refuse;
 *  - `lifecycle` — §6.1, a new record is always born Draft. Sending a state would
 *    be the client choosing one;
 *  - `companyId` — §15, the frontend never adds one;
 *  - units, package standards, template field VALUES — U-07/U-08/U-09 contracts,
 *    each with its own open gate (`G-PRD-UNIT-01`, `G-PRD-PACK-01`,
 *    `G-PRD-TEMPLATE-01`);
 *  - price, stock, lot/label — out of scope by §3, and by DEC-PRD-003 the label
 *    facts belong to the physical package record, never to the Product master.
 */
export interface CreateProductDraftPayload {
  readonly name: string
  readonly classification: {
    readonly class: string
    readonly group: string | null
    readonly family: string | null
  }
  /**
   * Only the roles the user actually decided. §5.2 forbids persisting an
   * unevaluated role as `false`, so an untouched role is ABSENT from the payload
   * rather than sent as `false` — the server keeps it `not-evaluated`.
   */
  readonly roles: Partial<Readonly<Record<ProductRoleName, boolean>>>
  readonly trackingPolicy: string | null
}

/**
 * What one create attempt produced. A write has three classes of result and the
 * difference decides whether a retry is safe:
 *
 *  - `created`: the server confirmed the Draft (possibly as a replay);
 *  - `http`: a DEFINITE answer that is not success — the stable `code` says what;
 *  - everything else: the outcome is UNKNOWN. The same logical attempt is retried
 *    with the SAME idempotency key, which replays instead of duplicating.
 */
export type CreateOutcome =
  | {
      readonly type: 'created'
      readonly data: ProductDraftCreated
      readonly replayed: boolean
    }
  | {
      readonly type: 'http'
      readonly status: number
      readonly code: ProductProblemCode | null
      readonly body: unknown
    }
  /** A 201 arrived and its body did not match the contract. The record may exist. */
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

export interface CreateContext {
  /**
   * The attempt's single key, created once per submitted snapshot and never per
   * click. Re-sending the same snapshot with the same key replays; changing the
   * form mints a new one.
   */
  readonly idempotencyKey: string
}

function readReplayed(headers: Headers): boolean {
  return (headers.get('Idempotency-Replayed') ?? '').trim().toLowerCase() === 'true'
}

export async function createProductDraft(
  payload: CreateProductDraftPayload,
  context: CreateContext,
  signal?: AbortSignal,
): Promise<CreateOutcome> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}/products`, {
      method: 'POST',
      headers: {
        ...devHeaders(),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
      signal,
    })
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    if (!navigator.onLine) return { type: 'offline' }
    return { type: 'network-error' }
  }

  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { type: 'aborted' }
    body = null
  }

  // EXACT 201. A POST answered 200/202 did something this client cannot describe,
  // and calling it success would navigate the user to a record that may not exist.
  if (response.status !== 201) {
    return {
      type: 'http',
      status: response.status,
      code: productProblemCodeOf(body),
      body,
    }
  }

  const created = decodeProductDraftCreated(body)
  if (created === null) return { type: 'malformed' }
  return { type: 'created', data: created, replayed: readReplayed(response.headers) }
}
