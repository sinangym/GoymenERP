/**
 * Product (Ürün) transport, pinned to the published backend contract
 * (PR #237 @ `cb75e2f`; see ./contract.ts for the exact sources).
 *
 * Routes, verbatim from `ProductApiV1` / `ProductEndpoints`:
 *   GET   /api/v1/products              → ProductPage
 *   GET   /api/v1/products/code-groups  → ProductCodeGroup[]
 *   GET   /api/v1/products/reference-options?groupId&classId → ProductReferenceOptions
 *   GET   /api/v1/products/{id}         → ProductCardView (404 not_found)
 *   POST  /api/v1/products              → 201 ProductMutationResponse
 *   PATCH /api/v1/products/{id}/draft   → 200 ProductMutationResponse
 *   GET   /api/v1/products/{id}/history → ProductHistoryView (module contract; host endpoint requested)
 *
 * The transport carries no business logic and invents no field:
 *  - `X-Company-Id` on EVERY Product request, from the shell's committed context.
 *    The actual endpoints (`ProductEndpoints.RequireCompanyContext()`, #251
 *    `643a6e1`) require it and the server validates it as the ORIGIN of the
 *    operation — §15 (`6a649ef`): it "carries the server-validated source of the
 *    operation; it creates neither Product ownership nor global authority". The
 *    master stays global (`PRD-SCOPE-001=A`): no `companyId` is ever put into a
 *    Product identity or ownership PAYLOAD, and the header grants nothing —
 *    `IProductService` still requires server-resolved global authorization;
 *  - the origin is FROZEN with the attempt: a retry of an unresolved command sends
 *    the same origin, key and payload (§19.4/§19.6), and an attempt never follows a
 *    company or session change into a new context;
 *  - ONE `Idempotency-Key` per logical attempt, supplied by the caller, meeting the
 *    source rule (1..128 printable ASCII, no whitespace or comma);
 *  - success is EXACTLY 201 for create and EXACTLY 200 for edit. Any other status
 *    is reported as a definite refusal or an unknown outcome, never as success.
 *
 * These routes are PINNED, not LIVE: the module is not mapped into the host and no
 * Product service implementation exists.
 */

import {
  decodeCodeGroupList,
  decodeProductCard,
  decodeProductHistory,
  decodeProductMutation,
  decodeProductPage,
  decodeReferenceOptions,
  productProblemCodeOf,
  type CompletenessEvaluation,
  type ProductCard,
  type ProductCodeGroup,
  type ProductHistory,
  type ProductLifecycle,
  type ProductMutationResult,
  type ProductPage,
  type ProductProblemCode,
  type ProductReferenceOptions,
  type ProductRoleName,
} from './contract'
import type { ViewOutcome } from '../shell/uiState'

/** Exported because the API origin is part of a pending attempt's binding (§19.4). */
export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED =
  import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function devHeaders(): Record<string, string> {
  return DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}
}

/**
 * The operation's origin: the company the shell has COMMITTED for this actor. Sent
 * as `X-Company-Id`, validated by the server, and nothing more — it is not the
 * Product's owner and it is not a grant.
 */
export interface ProductOrigin {
  readonly companyId: string
}

function originHeaders(origin: ProductOrigin): Record<string, string> {
  return { ...devHeaders(), 'X-Company-Id': origin.companyId }
}

/* ── List query (ProductListQuery) ────────────────────────────────────────── */

/** The endpoint validates `q` at max 200 and rejects control characters. */
export const SEARCH_MAX = 200

export type LifecycleFilter = ProductLifecycle | 'all'
/** `RoleMatch` — any/all over the submitted distinct roles. Source default: `any`. */
export type RoleMatch = 'any' | 'all'
export type ProductSort = 'code' | 'name' | 'updatedAtUtc'
export type SortDirection = 'asc' | 'desc'
/** `Completeness` filter, matching `ProductCompletenessView.Evaluation`. */
export type CompletenessFilter = CompletenessEvaluation | 'all'

export interface ProductQuery {
  readonly q?: string
  readonly lifecycle?: LifecycleFilter
  readonly groupId?: string
  readonly classId?: string
  readonly familyId?: string
  /**
   * Distinct role names, sent comma-separated, combined by `roleMatch`.
   *
   * Both the list and the semantics are the contract's, so the screen can offer
   * any/all without inventing a meaning. README: "A role filter matches known true
   * values only… Unknown is not false" — which is why an unevaluated role never
   * satisfies a filter.
   */
  readonly roles?: readonly ProductRoleName[]
  readonly roleMatch?: RoleMatch
  readonly sort?: ProductSort
  readonly direction?: SortDirection
  readonly completeness?: CompletenessFilter
  /** 1..100, default 50. */
  readonly pageSize?: number
  /** Opaque server token, echoed verbatim. The client never builds one. */
  readonly cursor?: string
}

/**
 * Control characters are stripped before the bound is applied.
 *
 * The endpoint rejects a `q` containing any `char.IsControl`, so sending one turns
 * a stray paste into a 400 the user cannot read. Removing them is safe here — this
 * is a SEARCH TERM, not a stored value, so nothing is silently persisted in a
 * changed form.
 */
// eslint-disable-next-line no-control-regex -- matching control characters is the point
const CONTROL_RE = /[\u0000-\u001F\u007F-\u009F]/g

export function buildProductQuery(query: ProductQuery): string {
  const params = new URLSearchParams()
  const q = query.q?.replace(CONTROL_RE, '').trim() ?? ''
  if (q.length > 0) params.set('q', q.slice(0, SEARCH_MAX))
  if (query.lifecycle !== undefined && query.lifecycle !== 'all') {
    params.set('lifecycle', query.lifecycle)
  }
  if (query.groupId !== undefined) params.set('groupId', query.groupId)
  if (query.classId !== undefined) params.set('classId', query.classId)
  if (query.familyId !== undefined) params.set('familyId', query.familyId)
  if (query.roles !== undefined && query.roles.length > 0) {
    // Comma-separated and DISTINCT, as the source requires: a repeated name is
    // rejected rather than merely redundant.
    params.set('roles', [...new Set(query.roles)].join(','))
    if (query.roleMatch !== undefined) params.set('roleMatch', query.roleMatch)
  }
  if (query.sort !== undefined) params.set('sort', query.sort)
  if (query.direction !== undefined) params.set('direction', query.direction)
  if (query.completeness !== undefined && query.completeness !== 'all') {
    params.set('completeness', query.completeness)
  }
  if (query.pageSize !== undefined) params.set('pageSize', String(query.pageSize))
  if (query.cursor !== undefined) params.set('cursor', query.cursor)
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
  origin: ProductOrigin,
  signal: AbortSignal | undefined,
): Promise<RawResult | { readonly failure: 'aborted' | 'offline' | 'unreachable' }> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { headers: originHeaders(origin), signal })
  } catch {
    if (signal?.aborted) return { failure: 'aborted' }
    if (!navigator.onLine) return { failure: 'offline' }
    // Online, but nothing came back — a different fact from a bad response body.
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

/** U-01 list. Global master; the committed company travels only as the validated origin header. */
export async function fetchProducts(
  query: ProductQuery,
  origin: ProductOrigin,
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductPage>> {
  const result = await rawRead(`/products${buildProductQuery(query)}`, origin, signal)
  if ('failure' in result) return failureOutcome(result.failure)
  if (result.status !== 200) return { type: 'http', status: result.status, body: result.body }
  const page = decodeProductPage(result.body)
  if (page === null) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}

/** U-01 Product 360. The decoder binds the card to `productId`. */
export async function fetchProductCard(
  productId: string,
  origin: ProductOrigin,
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductCard>> {
  const result = await rawRead(`/products/${encodeURIComponent(productId)}`, origin, signal)
  if ('failure' in result) return failureOutcome(result.failure)
  if (result.status !== 200) return { type: 'http', status: result.status, body: result.body }
  const card = decodeProductCard(result.body, productId)
  if (card === null) return { type: 'malformed' }
  return { type: 'ok-single', data: card }
}

/**
 * U-01 Product 360 change history, `GET /api/v1/products/{id}/history`.
 *
 * Bound to `productId` like the card. A 404 here, once the card itself has loaded,
 * means the history read is not available on this server — the caller must not
 * turn it into "this product does not exist".
 */
export async function fetchProductHistory(
  productId: string,
  origin: ProductOrigin,
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductHistory>> {
  const result = await rawRead(`/products/${encodeURIComponent(productId)}/history`, origin, signal)
  if ('failure' in result) return failureOutcome(result.failure)
  if (result.status !== 200) return { type: 'http', status: result.status, body: result.body }
  const history = decodeProductHistory(result.body, productId)
  if (history === null) return { type: 'malformed' }
  return { type: 'ok-single', data: history }
}

/**
 * The authorized code groups a Draft may be created under.
 *
 * §19.3 requires a REAL reference with a known prefix rule: "Sihirli/fake
 * grup/UUID/default şirket yok". The create form offers a choice from this read and
 * nothing else; when it fails or returns nothing, creation waits.
 */
export async function fetchCodeGroups(
  origin: ProductOrigin,
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductCodeGroup>> {
  const result = await rawRead('/products/code-groups', origin, signal)
  if ('failure' in result) return failureOutcome(result.failure)
  if (result.status !== 200) return { type: 'http', status: result.status, body: result.body }
  const groups = decodeCodeGroupList(result.body)
  if (groups === null) return { type: 'malformed' }
  return { type: 'ok', data: groups, filterActive: false }
}

/**
 * The class/family choices available under a code group.
 *
 * Scoped to the group (and optionally the class) the user actually picked, because
 * the server filters them that way — offering a family from another group would be
 * offering a choice the create then rejects.
 *
 * `unavailableCatalogs` is carried through rather than flattened into "empty": the
 * README makes an unavailable catalogue a DIFFERENT fact from a valid empty one,
 * and showing both as "nothing defined yet" would invent an answer for something we
 * could not read.
 */
export async function fetchReferenceOptions(
  groupId: string,
  classId: string | null,
  origin: ProductOrigin,
  signal?: AbortSignal,
): Promise<ViewOutcome<ProductReferenceOptions>> {
  const params = new URLSearchParams({ groupId })
  if (classId !== null) params.set('classId', classId)
  const result = await rawRead(`/products/reference-options?${params.toString()}`, origin, signal)
  if ('failure' in result) return failureOutcome(result.failure)
  if (result.status !== 200) return { type: 'http', status: result.status, body: result.body }
  const options = decodeReferenceOptions(result.body, groupId)
  if (options === null) return { type: 'malformed' }
  return { type: 'ok-single', data: options }
}

/* ── Writes ───────────────────────────────────────────────────────────────── */

/**
 * A role as WRITTEN. `ProductRoleInput` accepts only `manual`+boolean or
 * `not-evaluated`+null — `derived` is server-only and `PreserveDerived` rejects an
 * attempt to overwrite one.
 *
 * The tagged shape is the contract's, and it is what keeps §5.2 intact end to end:
 * a bare boolean could not distinguish "the user said no" from "nobody has looked".
 */
export type ProductRoleWrite =
  | { readonly value: boolean; readonly provenance: 'manual' }
  | { readonly value: null; readonly provenance: 'not-evaluated' }

export type ProductRoleWrites = Partial<Readonly<Record<ProductRoleName, ProductRoleWrite>>>

/**
 * `CreateProductDraftRequest{Name, GroupId, ClassId?, FamilyId?, Roles?, References?}`.
 *
 * NOT sent, each for a stated reason:
 *  - `code`, `lifecycle`, `version` — server-authoritative; the source's
 *    `JsonUnmappedMemberHandling.Disallow` would reject them outright;
 *  - `expectedVersion` — README: "Create has no expectedVersion";
 *  - `companyId` — §15;
 * Technical references use the released Product reference-options read; no id is invented.
 */
export interface CreateProductDraftPayload {
  readonly references?: ProductReferenceWrite
  readonly name: string
  readonly groupId: string
  readonly classId?: string | null
  readonly familyId?: string | null
  readonly roles?: ProductRoleWrites
}

/**
 * `ProductDraftChanges` — an `Optional<T>` per member: absent means unchanged,
 * present-and-null clears a nullable scalar reference.
 *
 * `JSON.stringify` drops an `undefined` property, so "absent" is expressed by
 * simply not setting the key. `name` and `roles` are never null: README says the
 * "name/roles/reference container and attribute/unit collections cannot be null".
 */
export interface ProductDraftChanges {
  readonly references?: ProductReferenceWrite
  readonly name?: string
  readonly classId?: string | null
  readonly familyId?: string | null
  readonly roles?: ProductRoleWrites
}

/** Omitted members are preserved on PATCH; null clears only a nullable member. */
export interface ProductReferenceWrite {
  readonly templateId?: string | null
  readonly templateVersion?: string | null
  readonly attributes?: Readonly<Record<string, unknown>>
  readonly units?: { readonly stock?: string | null; readonly purchase?: string | null; readonly sales?: string | null }
}

/**
 * `EditProductDraftRequest{ExpectedVersion, Changes, Reason}`.
 *
 * `reason` is trimmed, required, max 1000, no controls — and it exists because one
 * name change on a group-shared card is visible in both Göymen companies.
 */
export interface EditProductDraftPayload {
  readonly expectedVersion: string
  readonly changes: ProductDraftChanges
  readonly reason: string
}

export const REASON_MAX = 1000

/**
 * What one write produced. Three classes, and the difference decides whether a
 * retry is safe:
 *
 *  - `succeeded`: the server confirmed it (possibly as a replay);
 *  - `http`: a DEFINITE answer that is not success — the stable `code` says what;
 *  - everything else: the outcome is UNKNOWN. The same logical attempt is retried
 *    with the SAME key. README: "An unknown outcome must not be retried with a new
 *    key."
 */
export type CommandOutcome<T> =
  | { readonly type: 'succeeded'; readonly data: T }
  | {
      readonly type: 'http'
      readonly status: number
      readonly code: ProductProblemCode | null
      readonly body: unknown
    }
  /** A success status arrived and its body did not match the contract. It may have applied. */
  | { readonly type: 'malformed' }
  | { readonly type: 'offline' }
  | { readonly type: 'network-error' }
  | { readonly type: 'aborted' }

export interface CommandContext {
  /**
   * The attempt's single key, created once per submitted snapshot and never per
   * click. Source rule: 1..128 printable ASCII, no whitespace, no comma.
   */
  readonly idempotencyKey: string
  /**
   * The attempt's FROZEN origin — the company committed when the snapshot was
   * taken, not whatever the shell holds at retry time. A retry of an unresolved
   * command must reach the server under the same origin as the first send.
   */
  readonly origin: ProductOrigin
}

async function sendCommand(
  path: string,
  method: 'POST' | 'PATCH',
  payload: unknown,
  context: CommandContext,
  signal: AbortSignal | undefined,
): Promise<RawResult | { readonly failure: 'aborted' | 'offline' | 'unreachable' }> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, {
      method,
      headers: {
        ...originHeaders(context.origin),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
      signal,
    })
  } catch {
    if (signal?.aborted) return { failure: 'aborted' }
    if (!navigator.onLine) return { failure: 'offline' }
    return { failure: 'unreachable' }
  }
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    if (signal?.aborted) return { failure: 'aborted' }
    body = null
  }
  return { status: response.status, body }
}

function commandFailure<T>(
  failure: 'aborted' | 'offline' | 'unreachable',
): CommandOutcome<T> {
  if (failure === 'aborted') return { type: 'aborted' }
  if (failure === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

export async function createProductDraft(
  payload: CreateProductDraftPayload,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<CommandOutcome<ProductMutationResult>> {
  const result = await sendCommand('/products', 'POST', payload, context, signal)
  if ('failure' in result) return commandFailure(result.failure)

  // EXACT 201, replay included. README: "replay remains the original successful
  // operation status (create 201, edit 200)", so a 200 here is not a replay we can
  // recognise — it is an answer this client cannot describe.
  if (result.status !== 201) {
    return {
      type: 'http',
      status: result.status,
      code: productProblemCodeOf(result.body),
      body: result.body,
    }
  }

  // Created id is server-assigned, so there is nothing to bind it to — but the
  // lifecycle IS fixed: a new record is always born Draft.
  const mutation = decodeProductMutation(result.body, null, 'Draft')
  if (mutation === null) return { type: 'malformed' }
  return { type: 'succeeded', data: mutation }
}

export async function editProductDraft(
  productId: string,
  payload: EditProductDraftPayload,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<CommandOutcome<ProductMutationResult>> {
  const result = await sendCommand(
    `/products/${encodeURIComponent(productId)}/draft`,
    'PATCH',
    payload,
    context,
    signal,
  )
  if ('failure' in result) return commandFailure(result.failure)

  /**
   * EXACT 200, carrying the mutation envelope.
   *
   * A 204 would leave this client with no canonical value or version to show, and
   * painting the submitted draft as the server's result is what §19.4 forbids
   * ("Gönderilen taslak server sonucu gibi boyanmaz"). The source returns
   * `Results.Ok(...)`, so an empty body is drift — reported as a definite non-200
   * and handled as an unknown outcome by the caller.
   */
  if (result.status !== 200) {
    return {
      type: 'http',
      status: result.status,
      code: productProblemCodeOf(result.body),
      body: result.body,
    }
  }

  /**
   * Bound to the id we edited, and to `Draft`: the domain refuses to basic-edit
   * anything else (`lifecycle_conflict`), so a 200 describing a non-Draft record is
   * a state this screen cannot have caused.
   */
  const mutation = decodeProductMutation(result.body, productId, 'Draft')
  if (mutation === null) return { type: 'malformed' }
  return { type: 'succeeded', data: mutation }
}
