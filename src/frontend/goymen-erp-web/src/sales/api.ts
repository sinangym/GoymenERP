/**
 * Sales draft transport, pinned to #257 @ 2c1239e. NOT live: no route is mapped.
 *
 *   POST /api/v1/sales/drafts          → 201 SalesOrderDraftResponse (isReplay=false)
 *                                       → 200 same body, isReplay=true (same accepted attempt)
 *   GET  /api/v1/sales/drafts?pageSize&cursor → SalesOrderDraftListResponse
 *   GET  /api/v1/sales/drafts/{id}     → SalesOrderDraftDetailView
 *
 * The transport invents no field and carries no business rule. What it does add is
 * the consumer check the contract asks for — "validate nonempty server IDs, positive
 * version text, company/attempt binding, kind/state/revision and line identities
 * before treating a response as success":
 *  - a create answer is success only when status and `isReplay` agree (201/false,
 *    200/true) AND the body describes THIS attempt: the company it was sent under
 *    and, by value, every request member the detail reflects — header, owner
 *    references, each line and each slice's content, not just their ids. Anything
 *    else is `malformed` — the draft may exist, so the caller keeps the key;
 *  - a list or detail read is data only when every record belongs to the company it
 *    was read under, and a detail only when it is the record that was asked for.
 *
 * `X-Company-Id` is the committed shell company — request context, never a body member.
 */

import {
  decodeDraftLinePricing,
  decodeLinePriceReferences,
  salesDraftLinePriceReferencesPath,
  salesDraftLinePricingPath,
  type DraftLinePricing,
  type LinePriceReferences,
} from './linePricing'
import {
  CREATED_STATUS,
  REPLAY_HEADER,
  OWNER_REFERENCE_FIELDS,
  REV_0,
  SALES_DRAFT_PATH,
  STATE_COMMAND_STATUS,
  decodeContactInformationOptions,
  decodeCurrencyOptions,
  SALES_READ_CONTRACT_HEADER,
  SALES_READ_CONTRACT_PARTY_DISPLAY,
  decodeDraftDetailWithParty,
  decodeDraftMutation,
  decodeDraftPageWithParty,
  decodeDraftRevisions,
  decodeDeliverySchedules,
  decodeDeliveryProgress,
  decodeCancellation,
  decodeCancellationImpact,
  decodeProblem,
  salesDraftCommercialRevisionPath,
  salesDraftPriceFloorExceptionApprovalPath,
  salesDraftDeliverySchedulePath,
  salesDraftDeliverySchedulesPath,
  salesDraftDeliveryProgressPath,
  salesDraftCancellationImpactPath,
  salesDraftCancellationPath,
  salesDraftCommandPath,
  salesDraftDetailPath,
  salesDraftRevisionsPath,
  salesDraftQuantityRevisionPath,
  type ApprovalState,
  type ContactInformationOptions,
  type CurrencyOption,
  type DeliverySlice,
  type DraftCreateRequest,
  type DraftCommercialRevisionRequest,
  type DraftPriceFloorExceptionApprovalRequest,
  type DraftQuantityRevisionRequest,
  type DraftDeliveryScheduleRequest,
  type DeliverySchedules,
  type DeliveryProgress,
  type CancellationImpact,
  type CancellationRead,
  type DraftCancelRequest,
  type DraftDetail,
  type DraftDetailWithParty,
  type DraftMutation,
  type DraftPageWithParty,
  type DraftRevisions,
  type DraftStateCommand,
  type DraftStateCommandRequest,
  type OwnerReference,
} from './contract'
import type { OrderCommandOutcome } from './orderAttempt'
import type { ViewOutcome } from '../shell/uiState'
import type { OrderDraftOptions } from './orderDraftForm'
import { isReferenceId } from './wirePrimitives'
import {
  decodeRemainderChanges,
  remainderChangesPath,
  remainderDecisionPath,
  type RemainderChangeRequest,
  type RemainderChanges,
  type RemainderDecisionRequest,
} from './remainderChange'

export const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api/v1'
const DEV_USER = (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user'
const DEV_IDENTITY_ENABLED = import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true'

function headers(companyId: string): Record<string, string> {
  return {
    ...(DEV_IDENTITY_ENABLED ? { 'X-Development-User': DEV_USER } : {}),
    'X-Company-Id': companyId,
  }
}

type Raw =
  | {
      readonly status: number
      readonly body: unknown
      readonly location: string | null
      readonly replayed: boolean
      /** The Sales read contract the server says it answered with (absent on a legacy answer). */
      readonly readContract: string | null
    }
  | { readonly failure: 'aborted' | 'offline' | 'unreachable' }

async function request(path: string, init: RequestInit, signal: AbortSignal | undefined): Promise<Raw> {
  let response: Response
  try {
    response = await fetch(`${API_BASE}${path}`, { ...init, signal })
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
  return {
    status: response.status,
    body,
    location: response.headers.get('Location'),
    replayed: response.headers.get(REPLAY_HEADER) === 'true',
    readContract: response.headers.get(SALES_READ_CONTRACT_HEADER),
  }
}

/* ── Reads ────────────────────────────────────────────────────────────────── */

function readFailure<T>(failure: 'aborted' | 'offline' | 'unreachable'): ViewOutcome<T> {
  if (failure === 'aborted') return { type: 'fence-stale' }
  if (failure === 'offline') return { type: 'offline' }
  return { type: 'network-error' }
}

/**
 * The two reads that show the record-bound Cari display send the Sales read selector, and an answer counts only when the
 * server acknowledges exactly that contract: a 200 without the acknowledgment is not read as the legacy body instead.
 */
function partyDisplayReadHeaders(companyId: string): Record<string, string> {
  return { ...headers(companyId), [SALES_READ_CONTRACT_HEADER]: SALES_READ_CONTRACT_PARTY_DISPLAY }
}

const acknowledgesPartyDisplay = (raw: { readonly readContract: string | null }) =>
  raw.readContract === SALES_READ_CONTRACT_PARTY_DISPLAY

/** Source bound: 1..100, default 50. */
export const PAGE_SIZE = 50

export async function fetchDraftPage(
  cursor: string | null,
  companyId: string,
  signal?: AbortSignal,
  approvalState: ApprovalState | null = null,
): Promise<ViewOutcome<DraftPageWithParty>> {
  const params = new URLSearchParams({ pageSize: String(PAGE_SIZE) })
  if (cursor !== null) params.set('cursor', cursor)
  // SALES-A1: an exact approval state narrows the list on the server; no client-side filtering.
  if (approvalState !== null) params.set('approvalState', approvalState)
  const raw = await request(`${SALES_DRAFT_PATH}?${params.toString()}`, { headers: partyDisplayReadHeaders(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  if (!acknowledgesPartyDisplay(raw)) return { type: 'malformed' }
  const page = decodeDraftPageWithParty(raw.body)
  // A row of another company is a tenant-binding breach, not a row to show.
  if (page === null || page.items.some((item) => item.companyId !== companyId)) return { type: 'malformed' }
  return { type: 'ok-single', data: page }
}

export async function fetchDraftDetail(
  draftId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DraftDetailWithParty>> {
  const raw = await request(salesDraftDetailPath(draftId), { headers: partyDisplayReadHeaders(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  if (!acknowledgesPartyDisplay(raw)) return { type: 'malformed' }
  const detail = decodeDraftDetailWithParty(raw.body, draftId)
  if (detail === null || detail.draft.companyId !== companyId) return { type: 'malformed' }
  return { type: 'ok-single', data: detail }
}

/** Read-only history: authority and company filtering remain on the existing server endpoint. */
export async function fetchDraftRevisions(
  draftId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DraftRevisions>> {
  const raw = await request(salesDraftRevisionsPath(draftId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  // Unlike detail, this wire has no company/id member to compare. Keep the read
  // tied to its request and context namespace; never fabricate a response binding.
  const history = decodeDraftRevisions(raw.body)
  return history === null ? { type: 'malformed' } : { type: 'ok-single', data: history }
}

/** AT-018 / AT-021: every line's list-price reference, adjustments and the server's net (price read). */
export async function fetchDraftLinePricing(
  draftId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DraftLinePricing>> {
  const raw = await request(salesDraftLinePricingPath(draftId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const pricing = decodeDraftLinePricing(raw.body)
  return pricing === null ? { type: 'malformed' } : { type: 'ok-single', data: pricing }
}

/** AT-018: the Product owner's list prices for one line, in the draft's currency on its business date (price write). */
export async function fetchLinePriceReferences(
  draftId: string,
  lineId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<LinePriceReferences>> {
  const raw = await request(salesDraftLinePriceReferencesPath(draftId, lineId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const references = decodeLinePriceReferences(raw.body, lineId)
  return references === null ? { type: 'malformed' } : { type: 'ok-single', data: references }
}

/** DEC-SAL-003: the schedule history, read-only under the draft read; company filtering stays on the server. */
export async function fetchDraftDeliverySchedules(
  draftId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DeliverySchedules>> {
  const raw = await request(salesDraftDeliverySchedulesPath(draftId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const history = decodeDeliverySchedules(raw.body)
  return history === null ? { type: 'malformed' } : { type: 'ok-single', data: history }
}

/**
 * AT-024: the delivered / remaining quantities of `detail`'s lines, read-only under the draft read; the server reads
 * them from the delivery owner (Logistics) afresh on every request. Data only when it describes exactly this detail.
 */
export async function fetchDraftDeliveryProgress(
  detail: DraftDetail,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DeliveryProgress>> {
  const raw = await request(salesDraftDeliveryProgressPath(detail.draft.id), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const progress = decodeDeliveryProgress(raw.body, detail)
  return progress === null ? { type: 'malformed' } : { type: 'ok-single', data: progress }
}

/** Partial-delivery remainder facts; owner quantities stay nullable when the Logistics read is unavailable. */
export async function fetchDraftRemainderChanges(
  detail: DraftDetail,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<RemainderChanges>> {
  const raw = await request(remainderChangesPath(detail.draft.id), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const changes = decodeRemainderChanges(raw.body)
  return changes === null || changes.draftVersion !== detail.draft.version ? { type: 'malformed' } : { type: 'ok-single', data: changes }
}

/**
 * DEC-SAL-005: the impact preview of cancelling `detail`, read-only under the cancel control's name; the server reads every
 * category afresh from its owner (Inventory allocations, Logistics loading). Data only when it describes exactly this detail.
 */
export async function fetchCancellationImpact(
  detail: DraftDetail,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<CancellationImpact>> {
  const raw = await request(salesDraftCancellationImpactPath(detail.draft.id), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const impact = decodeCancellationImpact(raw.body, detail)
  return impact === null ? { type: 'malformed' } : { type: 'ok-single', data: impact }
}

/** AT-007: the recorded cancellation (or null), read-only under the draft read; company filtering stays on the server. */
export async function fetchDraftCancellation(
  draftId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<CancellationRead>> {
  const raw = await request(salesDraftCancellationPath(draftId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const read = decodeCancellation(raw.body)
  return read === null ? { type: 'malformed' } : { type: 'ok-single', data: read }
}

/* ── Reference options (S03) ─────────────────────────────────────────────── */

/**
 * The Sales reference routes on main: `/sales/references/accounts` is a LIST (the Cari owner's
 * authorized party accounts, bound to `sales.drafts.create`); `/sales/references/currency` and
 * `/sales/references/product` VALIDATE one chosen id for a business date and offer no list. So only
 * the accounts options can be read from the real API. SALES-A2a adds the currency and contact lists;
 * Product E1 adds the bounded product candidate search consumed by the create form.
 */
export const SALES_REFERENCE_ACCOUNTS_PATH = '/sales/references/accounts'

export type AccountOption = OrderDraftOptions['accounts'][number]

export type AccountOptionsOutcome =
  | { readonly type: 'ready'; readonly accounts: readonly AccountOption[]; readonly nextCursor: string | null }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'network' }
  | { readonly type: 'malformed' }

/** What the accounts read was asked for: the server's own search (`q`, ≤200 runes, matched server-side) and its cursor. */
export interface AccountQuery {
  readonly q?: string
  readonly cursor?: string
}

function decodeAccountOptions(value: unknown, companyId: string): { readonly accounts: readonly AccountOption[]; readonly nextCursor: string | null } | null {
  if (typeof value !== 'object' || value === null) return null
  const o = value as Record<string, unknown>
  if (o.companyId !== companyId || !Array.isArray(o.accounts)) return null
  if (o.nextCursor !== null && o.nextCursor !== undefined && typeof o.nextCursor !== 'string') return null
  const accounts: AccountOption[] = []
  for (const item of o.accounts) {
    if (typeof item !== 'object' || item === null) return null
    const a = item as Record<string, unknown>
    if (!isReferenceId(a.partyId) || !isReferenceId(a.companyAccountId)) return null
    if (typeof a.label !== 'string' || a.label.trim() === '' || a.label.length > 400) return null
    accounts.push({ partyId: a.partyId, companyAccountId: a.companyAccountId, label: a.label })
  }
  return { accounts, nextCursor: typeof o.nextCursor === 'string' && o.nextCursor.length > 0 ? o.nextCursor : null }
}

/** The first page of the Cari owner's authorized accounts for the form's customer select; never a default. */
export async function fetchAccountOptions(companyId: string, signal?: AbortSignal, query: AccountQuery = {}): Promise<AccountOptionsOutcome> {
  // One page of the Cari owner's authorized accounts. `q` is the API's own search, sent as typed (trimmed) and matched
  // by the SERVER; `cursor` is the previous page's, sent back unchanged. The client filters nothing itself (#361: a
  // company with more than one page of customers could not reach the rest from the first page alone).
  const params = new URLSearchParams({ pageSize: String(PAGE_SIZE) })
  const q = query.q?.trim() ?? ''
  if (q !== '') params.set('q', q)
  if (query.cursor !== undefined && query.cursor !== '') params.set('cursor', query.cursor)
  const raw = await request(`${SALES_REFERENCE_ACCOUNTS_PATH}?${params.toString()}`, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return { type: 'network' }
  if (raw.status !== 200) {
    const problem = decodeProblem(raw.body, raw.status)
    return { type: 'http', status: raw.status, code: problem?.code ?? null }
  }
  const page = decodeAccountOptions(raw.body, companyId)
  return page === null ? { type: 'malformed' } : { type: 'ready', accounts: page.accounts, nextCursor: page.nextCursor }
}

/* ── SALES-A2a: the two LIST reads of the create form's selects ───────────── */

/**
 * `/sales/references/currencies` LISTS the company's currencies usable for a sales draft on a business
 * date (the Settings owner's candidates, each revalidated by the Sales owner); `/sales/references/
 * contact-information` LISTS the Cari owner's active delivery addresses and contact persons of one party
 * account. Both are bound to `sales.drafts.create` on the server. Products still have no list (E1).
 */
export const SALES_REFERENCE_CURRENCIES_PATH = '/sales/references/currencies'
export const SALES_REFERENCE_CONTACT_INFORMATION_PATH = '/sales/references/contact-information'
export const SALES_REFERENCE_PRODUCTS_PATH = '/sales/references/products'

/** One shape for every reference list read: the real answer, never a default. */
export type ReferenceListOutcome<T> =
  | { readonly type: 'ready'; readonly data: T }
  | { readonly type: 'http'; readonly status: number; readonly code: string | null }
  | { readonly type: 'network' }
  | { readonly type: 'malformed' }

async function readReferenceList<T>(
  path: string,
  companyId: string,
  decode: (body: unknown) => T | null,
  signal: AbortSignal | undefined,
): Promise<ReferenceListOutcome<T>> {
  const raw = await request(path, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return { type: 'network' }
  if (raw.status !== 200) {
    const problem = decodeProblem(raw.body, raw.status)
    return { type: 'http', status: raw.status, code: problem?.code ?? null }
  }
  const data = decode(raw.body)
  return data === null ? { type: 'malformed' } : { type: 'ready', data }
}

export type CurrencyOptionsOutcome = ReferenceListOutcome<readonly CurrencyOption[]>

/** The currencies usable on `effectiveOn` (the form's business date); an empty list is "none", not an error. */
export function fetchCurrencyOptions(effectiveOn: string, companyId: string, signal?: AbortSignal): Promise<CurrencyOptionsOutcome> {
  const params = new URLSearchParams({ effectiveOn })
  return readReferenceList(
    `${SALES_REFERENCE_CURRENCIES_PATH}?${params.toString()}`,
    companyId,
    (body) => decodeCurrencyOptions(body, companyId, effectiveOn),
    signal,
  )
}

export type ContactInformationOutcome = ReferenceListOutcome<ContactInformationOptions>

/** The chosen account's active addresses and contacts; empty lists mean the owner lists none. */
export function fetchContactInformationOptions(
  partyId: string,
  companyAccountId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ContactInformationOutcome> {
  const params = new URLSearchParams({ partyId, companyAccountId })
  return readReferenceList(
    `${SALES_REFERENCE_CONTACT_INFORMATION_PATH}?${params.toString()}`,
    companyId,
    (body) => decodeContactInformationOptions(body, companyId, partyId, companyAccountId),
    signal,
  )
}

export type ProductOption = OrderDraftOptions['products'][number]

export interface ProductOffer {
  readonly items: readonly ProductOption[]
  /** True means only the bounded search window was returned, not that more items are definitely usable. */
  readonly searchWindowFull: boolean
}

export type ProductOptionsOutcome = ReferenceListOutcome<ProductOffer>

const PRODUCT_CANDIDATE_LIMIT = 25
const isOwnerVersion = (value: unknown): value is string =>
  typeof value === 'string' && /^[1-9]\d*$/u.test(value)
const isCandidateText = (value: unknown): value is string =>
  typeof value === 'string' && value.trim() !== '' && value.length <= 400
const hasExactMembers = (value: Record<string, unknown>, expected: readonly string[]) =>
  Object.keys(value).length === expected.length && expected.every((name) => Object.hasOwn(value, name))

function decodeProductOffer(value: unknown): ProductOffer | null {
  if (typeof value !== 'object' || value === null) return null
  const root = value as Record<string, unknown>
  if (!hasExactMembers(root, ['items', 'searchWindowFull']) || !Array.isArray(root.items) || typeof root.searchWindowFull !== 'boolean') return null
  const products: ProductOption[] = []
  const productIds = new Set<string>()
  for (const item of root.items) {
    if (typeof item !== 'object' || item === null) return null
    const product = item as Record<string, unknown>
    if (!hasExactMembers(product, ['productId', 'productVersion', 'code', 'name', 'orderUnits'])) return null
    if (!isReferenceId(product.productId) || productIds.has(product.productId) || !isOwnerVersion(product.productVersion)) return null
    if (!isCandidateText(product.code) || !isCandidateText(product.name) || !Array.isArray(product.orderUnits)) return null
    const units: ProductOption['orderUnits'][number][] = []
    const unitIds = new Set<string>()
    for (const itemUnit of product.orderUnits) {
      if (typeof itemUnit !== 'object' || itemUnit === null) return null
      const unit = itemUnit as Record<string, unknown>
      if (!hasExactMembers(unit, ['unitId', 'code', 'profileId', 'profileVersion'])) return null
      if (!isReferenceId(unit.unitId) || unitIds.has(unit.unitId) || !isCandidateText(unit.code)) return null
      if (!isReferenceId(unit.profileId) || !isOwnerVersion(unit.profileVersion)) return null
      unitIds.add(unit.unitId)
      units.push({
        unitId: unit.unitId,
        code: unit.code,
        label: unit.code,
        profileId: unit.profileId,
        profileVersion: unit.profileVersion,
      })
    }
    productIds.add(product.productId)
    products.push({
      productId: product.productId,
      productVersion: product.productVersion,
      code: product.code,
      name: product.name,
      label: `${product.code} — ${product.name}`,
      orderUnits: units,
    })
  }
  return { items: products, searchWindowFull: root.searchWindowFull }
}

/** Product owner's bounded operational candidates for this company/date/search. Save-time exact validation remains separate. */
export async function fetchProductOptions(
  effectiveOn: string,
  companyId: string,
  signal?: AbortSignal,
  q = '',
): Promise<ProductOptionsOutcome> {
  const params = new URLSearchParams({ effectiveOn, limit: String(PRODUCT_CANDIDATE_LIMIT) })
  const search = q.trim()
  if (search !== '') params.set('q', search)
  const raw = await request(
    `${SALES_REFERENCE_PRODUCTS_PATH}?${params.toString()}`,
    { headers: headers(companyId), cache: 'no-store' },
    signal,
  )
  if ('failure' in raw) return { type: 'network' }
  if (raw.status !== 200) {
    const problem = decodeProblem(raw.body, raw.status)
    return { type: 'http', status: raw.status, code: problem?.code ?? null }
  }
  const data = decodeProductOffer(raw.body)
  return data === null ? { type: 'malformed' } : { type: 'ready', data }
}

/* ── Create ───────────────────────────────────────────────────────────────── */

export interface CreateContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
}

const sameOwnerReference = (a: OwnerReference | null, b: OwnerReference | null) =>
  a === null || b === null ? a === b : a.id === b.id && a.expectedVersion === b.expectedVersion

/**
 * The same slices, compared by CONTENT and matched by `sliceId`: an answer that keeps
 * the ids but moves 400+600 to 500+500, or shifts a date, is a different plan.
 */
function sameSlices(answered: readonly DeliverySlice[] | null, sent: readonly DeliverySlice[] | null): boolean {
  if (answered === null || sent === null) return answered === sent
  if (answered.length !== sent.length) return false
  return sent.every((slice) => {
    const match = answered.find((s) => s.sliceId === slice.sliceId)
    return (
      match !== undefined &&
      match.orderUnitId === slice.orderUnitId &&
      match.quantity === slice.quantity &&
      match.requestedDeliveryDate === slice.requestedDeliveryDate
    )
  })
}

/**
 * Whether a decoded answer describes the attempt that was sent.
 *
 * Every request member the detail reflects is compared by value — ids alone prove
 * nothing. Decimal text is retained verbatim by the server, so it must come back as
 * sent. `productVersion` is the owner's snapshot of the version the line expected.
 * Price, discount and owner references are compared too: the client sends them null
 * today, and an answer that fills one in is not the draft this person saved.
 */
export function describesAttempt(mutation: DraftMutation, sent: DraftCreateRequest, companyId: string): boolean {
  const { detail } = mutation
  const draft = detail.draft
  if (
    draft.companyId !== companyId ||
    draft.kind !== sent.kind ||
    draft.revision !== REV_0 ||
    draft.partyId !== sent.partyId ||
    draft.companyAccountId !== sent.companyAccountId ||
    draft.currencyId !== sent.currencyId ||
    draft.businessDate !== sent.businessDate ||
    detail.requestedDeliveryDate !== sent.requestedDeliveryDate ||
    detail.note !== sent.note ||
    !OWNER_REFERENCE_FIELDS.every((field) => sameOwnerReference(detail[field], sent[field])) ||
    detail.lines.length !== sent.lines.length
  ) {
    return false
  }
  return sent.lines.every((line) => {
    const answered = detail.lines.find((l) => l.lineId === line.lineId)
    return (
      answered !== undefined &&
      answered.productId === line.productId &&
      answered.productVersion === line.expectedProductVersion &&
      answered.orderUnitId === line.orderUnitId &&
      answered.quantity === line.quantity &&
      answered.unitPrice === line.unitPrice &&
      answered.priceUnitId === line.priceUnitId &&
      answered.requestedDeliveryDate === line.requestedDeliveryDate &&
      answered.note === line.note &&
      answered.discountPercent === line.discountPercent &&
      sameSlices(answered.requestedDeliverySlices, line.requestedDeliverySlices)
    )
  })
}

export async function createDraft(
  payload: DraftCreateRequest,
  context: CreateContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(
    SALES_DRAFT_PATH,
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== CREATED_STATUS) {
    // Any other 2xx is outside the contract: the draft may exist, so the outcome is unknown.
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (
    mutation === null ||
    // 201 both times; the replay of the same accepted attempt carries Idempotency-Replayed and
    // says so in the body. A disagreement means this client cannot say which one happened.
    mutation.isReplay !== raw.replayed ||
    !describesAttempt(mutation, payload, context.companyId) ||
    (raw.location !== null &&
      !raw.location.endsWith(`/api/v1${salesDraftDetailPath(mutation.detail.draft.id)}`))
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: mutation }
}

/* ── SALES-A1: internal approval commands ─────────────────────────────────── */

export interface CommandContext {
  readonly idempotencyKey: string
  /** The company frozen with the attempt, not whatever the shell holds at retry time. */
  readonly companyId: string
  /** The draft the command was sent for; an answer about another record is not this command's. */
  readonly draftId: string
}

async function sendDraftRemainderMutation(
  path: string,
  payload: RemainderChangeRequest | RemainderDecisionRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(path, {
    method: 'POST',
    headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
    body: JSON.stringify(payload),
  }, signal)
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (mutation === null || mutation.isReplay !== raw.replayed || mutation.detail.draft.id !== context.draftId
    || mutation.detail.draft.companyId !== context.companyId) return { type: 'malformed' }
  return { type: 'succeeded', data: mutation }
}

export const sendRemainderChangeRequest = (payload: RemainderChangeRequest, context: CommandContext, signal?: AbortSignal) =>
  sendDraftRemainderMutation(remainderChangesPath(context.draftId), payload, context, signal)

export const sendRemainderChangeDecision = (
  changeNo: number,
  decision: 'approval' | 'rejection',
  payload: RemainderDecisionRequest,
  context: CommandContext,
  signal?: AbortSignal,
) => sendDraftRemainderMutation(remainderDecisionPath(context.draftId, changeNo, decision), payload, context, signal)

/**
 * One state command (submission / approval / rejection): 200 with the draft after the transition
 * is the pinned success; the same key again is 200 + `Idempotency-Replayed`, told apart by the
 * header and `isReplay` agreeing. The answer must describe THIS draft under THIS company, or the
 * outcome is unknown and the key is kept (the transition may have been recorded).
 */
export async function sendDraftCommand(
  command: DraftStateCommand,
  payload: DraftStateCommandRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(
    salesDraftCommandPath(context.draftId, command),
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (
    mutation === null ||
    mutation.isReplay !== raw.replayed ||
    mutation.detail.draft.id !== context.draftId ||
    mutation.detail.draft.companyId !== context.companyId
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: mutation }
}

/** K3 commercial revision: the same keyed-attempt and strict response rules as every Sales mutation. */
export async function sendDraftCommercialRevision(
  payload: DraftCommercialRevisionRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(
    salesDraftCommercialRevisionPath(context.draftId),
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (
    mutation === null ||
    mutation.isReplay !== raw.replayed ||
    mutation.detail.draft.id !== context.draftId ||
    mutation.detail.draft.companyId !== context.companyId
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: mutation }
}

/** Reasoned maker-checker approval of the current below-floor snapshots; same keyed mutation envelope. */
export async function sendDraftPriceFloorExceptionApproval(
  payload: DraftPriceFloorExceptionApprovalRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(
    salesDraftPriceFloorExceptionApprovalPath(context.draftId),
    {
      method: 'POST',
      headers: { ...headers(context.companyId), 'Content-Type': 'application/json', 'Idempotency-Key': context.idempotencyKey },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (mutation === null || mutation.isReplay !== raw.replayed || mutation.detail.draft.id !== context.draftId
    || mutation.detail.draft.companyId !== context.companyId) return { type: 'malformed' }
  return { type: 'succeeded', data: mutation }
}

/**
 * DEC-SAL-002: the quantity revision — the commercial revision's keyed-attempt and strict response rules. Success is the
 * draft after the change, of this draft and this company, with the replay header and `isReplay` agreeing.
 */
export async function sendDraftQuantityRevision(
  payload: DraftQuantityRevisionRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(
    salesDraftQuantityRevisionPath(context.draftId),
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (
    mutation === null ||
    mutation.isReplay !== raw.replayed ||
    mutation.detail.draft.id !== context.draftId ||
    mutation.detail.draft.companyId !== context.companyId
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: mutation }
}

/**
 * DEC-SAL-003: the delivery-schedule update — the commercial revision's keyed-attempt and strict response rules. Success is
 * the draft after the change, of this draft and this company, with the replay header and `isReplay` agreeing.
 */
export async function sendDraftDeliverySchedule(
  payload: DraftDeliveryScheduleRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(
    salesDraftDeliverySchedulePath(context.draftId),
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (
    mutation === null ||
    mutation.isReplay !== raw.replayed ||
    mutation.detail.draft.id !== context.draftId ||
    mutation.detail.draft.companyId !== context.companyId
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: mutation }
}

/**
 * DEC-SAL-005 / AT-007: the cancellation — the delivery schedule's keyed-attempt and strict response rules. Success is the
 * draft after the change, of this draft and this company, with the replay header and `isReplay` agreeing.
 */
export async function sendDraftCancel(
  payload: DraftCancelRequest,
  context: CommandContext,
  signal?: AbortSignal,
): Promise<OrderCommandOutcome<DraftMutation>> {
  const raw = await request(
    salesDraftCancellationPath(context.draftId),
    {
      method: 'POST',
      headers: {
        ...headers(context.companyId),
        'Content-Type': 'application/json',
        'Idempotency-Key': context.idempotencyKey,
      },
      body: JSON.stringify(payload),
    },
    signal,
  )
  if ('failure' in raw) {
    if (raw.failure === 'aborted') return { type: 'aborted' }
    if (raw.failure === 'offline') return { type: 'offline' }
    return { type: 'network-error' }
  }
  if (raw.status !== STATE_COMMAND_STATUS) {
    if (raw.status >= 200 && raw.status < 300) return { type: 'malformed' }
    return { type: 'http', status: raw.status, problem: decodeProblem(raw.body, raw.status) }
  }
  const mutation = decodeDraftMutation(raw.body)
  if (
    mutation === null ||
    mutation.isReplay !== raw.replayed ||
    mutation.detail.draft.id !== context.draftId ||
    mutation.detail.draft.companyId !== context.companyId
  ) {
    return { type: 'malformed' }
  }
  return { type: 'succeeded', data: mutation }
}
