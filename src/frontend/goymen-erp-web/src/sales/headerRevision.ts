/**
 * API-04 (DEC-SAL-002, DEC-SAL-008): the order header revision contract — the only file that spells its wire names.
 *
 *  - `GET  /sales/drafts/{id}/header`          → `SalesDraftHeaderV1` (the revisable header as it stands; `sales.drafts.read`,
 *    the due terms only with `sales.orders.price.read`, otherwise `dueTermsDisclosure: "Withheld"` and `dueTerms: null`);
 *  - `POST /sales/drafts/{id}/header-revision` ← `SalesDraftHeaderRevisionRequest` (every member present; the whole new header),
 *    answered with the order detail like every revision command;
 *  - `GET  /sales/references/sales-owners`     → `SalesOwnerOptionsV1` (SAT-D27: the people who may be the sales owner).
 *
 * The screen computes no due date: the backend calculator does (SAT-D86). Decimal and version text stays text. A decoder
 * returns null for anything it cannot vouch for; the caller fails closed.
 */

import { salesDraftDetailPath } from './contract'
import { isReferenceId, isVersionString, parseDecimalInput } from './wirePrimitives'

export const salesDraftHeaderPath = (id: string) => `${salesDraftDetailPath(id)}/header`
export const salesDraftHeaderRevisionPath = (id: string) => `${salesDraftDetailPath(id)}/header-revision`
export const SALES_REFERENCE_SALES_OWNERS_PATH = '/sales/references/sales-owners'

/**
 * The two due types (`SalesDueBasisNamesV1`, SAT-D129): `OrderDate` "Nokta vade" (order date + N days) and `ShipmentDate`
 * "Sevk vadesi" (each shipment's date + N days). There is no invoice-date type; any other name is refused by the decoder.
 */
export const DUE_BASES = ['OrderDate', 'ShipmentDate'] as const
export type DueBasis = (typeof DUE_BASES)[number]

/** Technical bounds the server enforces (`SalesDueTerms`); mirrored for the form, never a business default. */
export const DUE_LIMITS = { maximumInstalments: 120, maximumDaysAfter: 36600, reason: 4000 } as const

export interface DueInstalment {
  readonly sequenceNo: number
  readonly basis: DueBasis
  readonly daysAfter: number
  readonly sharePercent: string
}

export interface DueTerms {
  readonly instalments: readonly DueInstalment[]
}

export interface OwnerRef {
  readonly id: string
  readonly expectedVersion: string
}

export interface DraftHeader {
  readonly draftVersion: string
  readonly revision: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly deliveryAddress: OwnerRef | null
  readonly contact: OwnerRef | null
  readonly salesOwnerUserId: string | null
  readonly salesOwnerDisplayName: string | null
  readonly dueTermsDisclosure: 'Visible' | 'Withheld'
  readonly dueTerms: DueTerms | null
  readonly customerOrCurrencyRevisionPending: boolean
}

export interface HeaderRevisionRequest {
  readonly expectedVersion: string
  readonly reason: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly deliveryAddress: OwnerRef | null
  readonly contact: OwnerRef | null
  readonly salesOwnerUserId: string | null
  readonly dueTerms: DueTerms | null
}

export interface SalesOwnerOption {
  readonly userId: string
  readonly displayName: string
}

export interface SalesOwnerOptions {
  readonly owners: readonly SalesOwnerOption[]
  readonly hasMore: boolean
}

type Obj = Record<string, unknown>

function exact(value: unknown, keys: readonly string[]): Obj | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const o = value as Obj
  const own = Object.keys(o)
  if (own.length !== keys.length || keys.some((key) => !Object.hasOwn(o, key))) return null
  return o
}

const isDueBasis = (v: unknown): v is DueBasis => typeof v === 'string' && (DUE_BASES as readonly string[]).includes(v)
const isShare = (v: unknown): v is string => typeof v === 'string' && /^[0-9]+(\.[0-9]+)?$/.test(v) && v.length <= 128

function decodeOwnerRef(value: unknown): OwnerRef | null | undefined {
  if (value === null) return null
  const o = exact(value, ['id', 'expectedVersion'])
  if (o === null || !isReferenceId(o.id) || typeof o.expectedVersion !== 'string' || o.expectedVersion.trim() === ''
    || o.expectedVersion.length > 128) return undefined
  return { id: o.id, expectedVersion: o.expectedVersion }
}

export function decodeDueTerms(value: unknown): DueTerms | null | undefined {
  if (value === null) return null
  const o = exact(value, ['instalments'])
  if (o === null || !Array.isArray(o.instalments) || o.instalments.length < 1 || o.instalments.length > DUE_LIMITS.maximumInstalments) {
    return undefined
  }
  const instalments: DueInstalment[] = []
  for (const [index, entry] of o.instalments.entries()) {
    const i = exact(entry, ['sequenceNo', 'basis', 'daysAfter', 'sharePercent'])
    if (i === null || i.sequenceNo !== index + 1 || !isDueBasis(i.basis) || typeof i.daysAfter !== 'number'
      || !Number.isInteger(i.daysAfter) || i.daysAfter < 0 || i.daysAfter > DUE_LIMITS.maximumDaysAfter || !isShare(i.sharePercent)) {
      return undefined
    }
    instalments.push({ sequenceNo: i.sequenceNo, basis: i.basis, daysAfter: i.daysAfter, sharePercent: i.sharePercent })
  }
  return { instalments }
}

/** The header of THIS draft at a positive version; anything else (another shape, a withheld answer carrying terms) is null. */
export function decodeDraftHeader(value: unknown): DraftHeader | null {
  const o = exact(value, [
    'draftVersion', 'revision', 'partyId', 'companyAccountId', 'currencyId', 'deliveryAddress', 'contact', 'salesOwnerUserId',
    'salesOwnerDisplayName', 'dueTermsDisclosure', 'dueTerms', 'customerOrCurrencyRevisionPending',
  ])
  if (o === null || !isVersionString(o.draftVersion) || typeof o.revision !== 'string' || !/^Rev\.(0|[1-9][0-9]*)$/.test(o.revision)
    || !isReferenceId(o.partyId) || !isReferenceId(o.companyAccountId) || !isReferenceId(o.currencyId)
    || (o.salesOwnerUserId !== null && !isReferenceId(o.salesOwnerUserId))
    || (o.salesOwnerDisplayName !== null && (typeof o.salesOwnerDisplayName !== 'string' || o.salesOwnerDisplayName.trim() === ''))
    || (o.dueTermsDisclosure !== 'Visible' && o.dueTermsDisclosure !== 'Withheld')
    || typeof o.customerOrCurrencyRevisionPending !== 'boolean') return null
  const deliveryAddress = decodeOwnerRef(o.deliveryAddress)
  const contact = decodeOwnerRef(o.contact)
  const dueTerms = decodeDueTerms(o.dueTerms)
  if (deliveryAddress === undefined || contact === undefined || dueTerms === undefined) return null
  if (o.dueTermsDisclosure === 'Withheld' && dueTerms !== null) return null
  if (o.salesOwnerUserId === null && o.salesOwnerDisplayName !== null) return null
  return {
    draftVersion: o.draftVersion, revision: o.revision, partyId: o.partyId, companyAccountId: o.companyAccountId,
    currencyId: o.currencyId, deliveryAddress, contact, salesOwnerUserId: o.salesOwnerUserId as string | null,
    salesOwnerDisplayName: o.salesOwnerDisplayName as string | null, dueTermsDisclosure: o.dueTermsDisclosure,
    dueTerms, customerOrCurrencyRevisionPending: o.customerOrCurrencyRevisionPending,
  }
}

/** SAT-D27: the eligible people of THIS company, each once. */
export function decodeSalesOwnerOptions(value: unknown, companyId: string): SalesOwnerOptions | null {
  const o = exact(value, ['companyId', 'owners', 'hasMore'])
  if (o === null || o.companyId !== companyId || !Array.isArray(o.owners) || typeof o.hasMore !== 'boolean') return null
  const owners: SalesOwnerOption[] = []
  const seen = new Set<string>()
  for (const entry of o.owners) {
    const item = exact(entry, ['userId', 'displayName'])
    if (item === null || !isReferenceId(item.userId) || seen.has(item.userId) || typeof item.displayName !== 'string'
      || item.displayName.trim() === '') return null
    seen.add(item.userId)
    owners.push({ userId: item.userId, displayName: item.displayName })
  }
  return { owners, hasMore: o.hasMore }
}

/** One editable instalment row: the text a person typed, read only when the form is sent. */
export interface InstalmentRow {
  readonly key: string
  readonly basis: DueBasis
  readonly daysAfter: string
  readonly sharePercent: string
}

export interface HeaderFormValues {
  readonly reason: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly deliveryAddress: OwnerRef | null
  readonly contact: OwnerRef | null
  readonly salesOwnerUserId: string | null
  /** null = no due terms ("vade yok"); otherwise at least one row. */
  readonly instalments: readonly InstalmentRow[] | null
}

export type HeaderFormError =
  | { readonly kind: 'reason-required' }
  | { readonly kind: 'no-instalment' }
  | { readonly kind: 'days'; readonly row: number }
  | { readonly kind: 'share'; readonly row: number }
  | { readonly kind: 'no-change' }

export type BuiltHeaderRevision =
  | { readonly ok: true; readonly payload: HeaderRevisionRequest }
  | { readonly ok: false; readonly error: HeaderFormError }

/** The form's starting values: exactly the header as the server answered it. */
export function headerFormValues(header: DraftHeader, nextKey: () => string): HeaderFormValues {
  return {
    reason: '',
    partyId: header.partyId,
    companyAccountId: header.companyAccountId,
    currencyId: header.currencyId,
    deliveryAddress: header.deliveryAddress,
    contact: header.contact,
    salesOwnerUserId: header.salesOwnerUserId,
    instalments: header.dueTerms === null ? null : header.dueTerms.instalments.map((instalment) => ({
      key: nextKey(), basis: instalment.basis, daysAfter: String(instalment.daysAfter), sharePercent: instalment.sharePercent,
    })),
  }
}

const sameRef = (a: OwnerRef | null, b: OwnerRef | null) =>
  a === null ? b === null : b !== null && a.id === b.id && a.expectedVersion === b.expectedVersion

function sameTerms(a: DueTerms | null, b: DueTerms | null): boolean {
  if (a === null || b === null) return a === b
  return a.instalments.length === b.instalments.length && a.instalments.every((x, i) => {
    const y = b.instalments[i]
    return y !== undefined && x.basis === y.basis && x.daysAfter === y.daysAfter && Number(x.sharePercent) === Number(y.sharePercent)
  })
}

/**
 * The strict request from the form, or the first local error. Shares are read as decimal text (`,` or `.`), never through a
 * JS number on the wire; whether they add up to 100 is the server's rule (the screen does no arithmetic it would have to trust).
 * When the due terms are withheld from this caller the current terms are unknown here, so the form never offers to send them.
 */
export function buildHeaderRevisionRequest(header: DraftHeader, values: HeaderFormValues): BuiltHeaderRevision {
  if (values.reason.trim() === '') return { ok: false, error: { kind: 'reason-required' } }
  let dueTerms: DueTerms | null = null
  if (values.instalments !== null) {
    if (values.instalments.length === 0) return { ok: false, error: { kind: 'no-instalment' } }
    const instalments: DueInstalment[] = []
    for (const [index, row] of values.instalments.entries()) {
      const days = row.daysAfter.trim()
      if (!/^[0-9]{1,5}$/.test(days) || Number(days) > DUE_LIMITS.maximumDaysAfter) {
        return { ok: false, error: { kind: 'days', row: index + 1 } }
      }
      const share = parseDecimalInput(row.sharePercent)
      if (!share.ok || !/[1-9]/.test(share.value)) return { ok: false, error: { kind: 'share', row: index + 1 } }
      instalments.push({ sequenceNo: index + 1, basis: row.basis, daysAfter: Number(days), sharePercent: share.value })
    }
    dueTerms = { instalments }
  }
  const payload: HeaderRevisionRequest = {
    expectedVersion: header.draftVersion,
    reason: values.reason,
    partyId: values.partyId,
    companyAccountId: values.companyAccountId,
    currencyId: values.currencyId,
    deliveryAddress: values.deliveryAddress,
    contact: values.contact,
    salesOwnerUserId: values.salesOwnerUserId,
    dueTerms,
  }
  const unchanged = payload.partyId === header.partyId && payload.companyAccountId === header.companyAccountId
    && payload.currencyId === header.currencyId && sameRef(payload.deliveryAddress, header.deliveryAddress)
    && sameRef(payload.contact, header.contact) && payload.salesOwnerUserId === header.salesOwnerUserId
    && sameTerms(payload.dueTerms, header.dueTerms)
  return unchanged ? { ok: false, error: { kind: 'no-change' } } : { ok: true, payload }
}
