/**
 * MOD-03 → Sales (SAT-D93/D97/D140/D147/D158): the order's tax — the contracts of
 * `GET /api/v1/sales/drafts/{id}/tax` (`GetSalesOrderDraftTaxV1`, `SalesOrderDraftTaxResponse`, under the draft read AND the
 * price read) and `GET /api/v1/sales/tax-options?on=` (`GetSalesTaxOptionsV1`, `SalesTaxOptionsResponse`, under the draft read).
 *
 * Every rate, code and state is the SERVER's (the Settings line tax port, kept as the line's snapshot); this file only reads what
 * it answered and never computes a tax (AGENTS.md: "Fiyat/formül/toleransı ekran içinde yeniden yazma"). Rules taken from the
 * backend contract (`SalesDraftTaxContractsV1.cs`), not assumed:
 *  - every member is present, nullable ones as explicit null (the buyer tax's optional members may be omitted); unknown members
 *    are refused — a body that does not fit is never shown;
 *  - the answer is bound to the detail on screen: the same version and revision, and every line of that detail exactly once;
 *  - a line without a snapshot (`hasSnapshot` false) carries no part of one and is `NoSource` with `tax_snapshot_missing`;
 *  - `rateState` is `Computed` exactly when no `unavailableReason` is given.
 */

import { salesDraftDetailPath, type DraftDetail } from './contract'
import { ORDER_TAX_TEXT } from './salesText'
import type { ViewOutcome } from '../shell/uiState'
import { isDateOnly, isReferenceId } from './wirePrimitives'

export const salesDraftTaxPath = (id: string) => `${salesDraftDetailPath(id)}/tax`
export const salesTaxOptionsPath = (on: string) => `/sales/tax-options?on=${encodeURIComponent(on)}`

/** `SalesDraftLineTaxModesV1`. */
export const LINE_TAX_MODE = { default: 'Default', override: 'Override' } as const
export type LineTaxMode = (typeof LINE_TAX_MODE)[keyof typeof LINE_TAX_MODE]

/** `TaxBuyerSituationVatModesV1`: a situation's effect on the VAT. */
export const VAT_MODE = { normal: 'Normal', zeroWithExemptionCode: 'ZeroWithExemptionCode' } as const

/** SAT-D140: a new order starts "Normal" (the server's start value when none is sent). */
export const START_BUYER_SITUATION = 'Normal'

export interface BuyerTax {
  readonly situation: string
  readonly exemptionCode: string | null
  readonly documentNo: string | null
  readonly documentDate: string | null
}

/** The wire body of a buyer tax (create, header revision): optional members are omitted when empty. */
export interface BuyerTaxRequest {
  readonly situation: string
  readonly exemptionCode?: string
  readonly documentNo?: string
  readonly documentDate?: string
}

export interface LineTaxOverride {
  readonly itemType: string | null
  readonly buyerSituation: string
  readonly exemptionCode: string | null
  readonly reason: string
  readonly changedBy: string
  readonly changedAtUtc: string
  /** SAT-D181: the picked entries of the Settings VAT rate / withholding ratio lists (by code). */
  readonly vatRateCode: string | null
  readonly withholdingRatioCode: string | null
}

export interface LineTax {
  readonly lineId: string
  readonly lineNo: number
  readonly hasSnapshot: boolean
  readonly origin: LineTaxMode | null
  readonly resolvedOn: string | null
  readonly buyerSituation: string | null
  readonly itemType: string | null
  readonly state: string | null
  readonly reason: string | null
  readonly vatState: string | null
  readonly vatPercent: string | null
  readonly exemptionCodeState: string | null
  readonly exemptionCode: string | null
  readonly exemptionCodeReason: string | null
  readonly withholdingState: string | null
  readonly withholdingPercent: string | null
  readonly withholdingCodeState: string | null
  readonly withholdingCode: string | null
  readonly withholdingCodeReason: string | null
  readonly buyerAdjustment: string | null
  readonly buyerDocumentRequired: boolean
  readonly rateState: 'Computed' | 'NoSource'
  readonly unavailableReason: string | null
  readonly invoiceBlockers: readonly string[]
  readonly override: LineTaxOverride | null
}

export interface OrderTax {
  readonly draftVersion: string
  readonly revision: string
  readonly businessDate: string
  readonly buyerTax: BuyerTax | null
  readonly lines: readonly LineTax[]
}

export interface BuyerSituationOption {
  readonly code: string
  readonly name: string
  readonly withholdingApplies: boolean
  readonly vatMode: string
  readonly documentRequired: boolean
}

export interface TaxCodeOption {
  readonly code: string
  readonly name: string
}

/** SAT-D181: one entry of a Settings rate list — its percent is the list's (exact text); a ratio names its GİB code. */
export interface TaxRateOption {
  readonly code: string
  readonly name: string
  readonly percent: string
  readonly taxCode: string | null
}

export interface TaxOptions {
  readonly on: string
  readonly buyerSituationsState: 'Resolved' | 'Unresolved'
  readonly buyerSituationsReason: string | null
  readonly buyerSituations: readonly BuyerSituationOption[]
  readonly exemptionCodesState: 'Resolved' | 'Unresolved'
  readonly exemptionCodesReason: string | null
  readonly exemptionCodes: readonly TaxCodeOption[]
  readonly itemTypes: readonly string[]
  readonly vatRatesState: 'Resolved' | 'Unresolved'
  readonly vatRatesReason: string | null
  readonly vatRates: readonly TaxRateOption[]
  readonly withholdingRatiosState: 'Resolved' | 'Unresolved'
  readonly withholdingRatiosReason: string | null
  readonly withholdingRatios: readonly TaxRateOption[]
}

type Obj = Record<string, unknown>

function object(value: unknown): Obj | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Obj) : null
}

function exact(value: unknown, keys: readonly string[]): Obj | null {
  const obj = object(value)
  if (obj === null || Object.keys(obj).length !== keys.length) return null
  for (const key of keys) if (!Object.hasOwn(obj, key)) return null
  return obj
}

const text = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= 4000
const textOrNull = (v: unknown): v is string | null => v === null || text(v)
const DECIMAL = /^(0|[1-9][0-9]*)(?:\.[0-9]+)?$/
const percentOrNull = (v: unknown): v is string | null => v === null || (typeof v === 'string' && v.length <= 32 && DECIMAL.test(v))
const VERSION = /^[1-9][0-9]*$/
const versionOrNull = (v: unknown) => v === null || (typeof v === 'number' && Number.isInteger(v) && v >= 0)
const resolution = (v: unknown): v is 'Resolved' | 'Unresolved' => v === 'Resolved' || v === 'Unresolved'

const BUYER_KEYS = ['situation', 'exemptionCode', 'documentNo', 'documentDate'] as const

/** A buyer tax as the server answers it: `situation` always, the other members omitted when empty. */
export function decodeBuyerTax(value: unknown): BuyerTax | null {
  const o = object(value)
  if (o === null || Object.keys(o).some((key) => !(BUYER_KEYS as readonly string[]).includes(key)) || !text(o.situation)) return null
  const optional = (key: string) => (Object.hasOwn(o, key) ? o[key] : null)
  const exemptionCode = optional('exemptionCode')
  const documentNo = optional('documentNo')
  const documentDate = optional('documentDate')
  if (!textOrNull(exemptionCode) || !textOrNull(documentNo) || !(documentDate === null || isDateOnly(documentDate))
    || (documentNo === null) !== (documentDate === null)) return null
  return { situation: o.situation, exemptionCode, documentNo, documentDate: documentDate as string | null }
}

const OVERRIDE_KEYS = ['itemType', 'buyerSituation', 'exemptionCode', 'reason', 'changedBy', 'changedAtUtc', 'vatRateCode',
  'withholdingRatioCode'] as const

function decodeOverride(value: unknown): LineTaxOverride | null | undefined {
  if (value === null) return null
  const o = exact(value, OVERRIDE_KEYS)
  if (o === null || !textOrNull(o.itemType) || !text(o.buyerSituation) || !textOrNull(o.exemptionCode) || !text(o.reason)
    || !text(o.changedBy) || typeof o.changedAtUtc !== 'string' || Number.isNaN(Date.parse(o.changedAtUtc))
    || !textOrNull(o.vatRateCode) || !textOrNull(o.withholdingRatioCode)) return undefined
  return {
    itemType: o.itemType, buyerSituation: o.buyerSituation, exemptionCode: o.exemptionCode, reason: o.reason, changedBy: o.changedBy,
    changedAtUtc: o.changedAtUtc, vatRateCode: o.vatRateCode, withholdingRatioCode: o.withholdingRatioCode,
  }
}

const LINE_KEYS = ['lineId', 'lineNo', 'hasSnapshot', 'origin', 'resolvedOn', 'buyerSituation', 'itemType', 'state', 'reason', 'vatState',
  'vatPercent', 'exemptionCodeState', 'exemptionCode', 'exemptionCodeReason', 'withholdingState', 'withholdingPercent',
  'withholdingCodeState', 'withholdingCode', 'withholdingCodeReason', 'buyerAdjustment', 'buyerDocumentRequired', 'settingsSource',
  'settingsRecordId', 'settingsRecordVersion', 'buyerSituationListVersion', 'rateState', 'unavailableReason', 'invoiceBlockers',
  'override'] as const

const SNAPSHOT_TEXT_KEYS = ['buyerSituation', 'itemType', 'state', 'reason', 'vatState', 'exemptionCodeState', 'exemptionCode',
  'exemptionCodeReason', 'withholdingState', 'withholdingCodeState', 'withholdingCode', 'withholdingCodeReason', 'buyerAdjustment',
  'settingsSource'] as const

function decodeLine(value: unknown): LineTax | null {
  const o = exact(value, LINE_KEYS)
  if (o === null || !isReferenceId(o.lineId) || typeof o.lineNo !== 'number' || !Number.isInteger(o.lineNo) || o.lineNo < 1
    || typeof o.hasSnapshot !== 'boolean' || typeof o.buyerDocumentRequired !== 'boolean'
    || (o.rateState !== 'Computed' && o.rateState !== 'NoSource') || !textOrNull(o.unavailableReason)
    || (o.rateState === 'Computed') !== (o.unavailableReason === null)
    || !Array.isArray(o.invoiceBlockers) || !o.invoiceBlockers.every(text)
    || !SNAPSHOT_TEXT_KEYS.every((key) => textOrNull(o[key])) || !percentOrNull(o.vatPercent) || !percentOrNull(o.withholdingPercent)
    || !(o.settingsRecordId === null || isReferenceId(o.settingsRecordId)) || !versionOrNull(o.settingsRecordVersion)
    || !versionOrNull(o.buyerSituationListVersion) || !(o.resolvedOn === null || isDateOnly(o.resolvedOn))) return null
  const override = decodeOverride(o.override)
  if (override === undefined) return null
  if (o.hasSnapshot) {
    if ((o.origin !== LINE_TAX_MODE.default && o.origin !== LINE_TAX_MODE.override) || o.resolvedOn === null || o.buyerSituation === null
      || o.state === null || (o.origin === LINE_TAX_MODE.override) !== (override !== null)) return null
  } else if (o.origin !== null || o.state !== null || override !== null || o.rateState !== 'NoSource') {
    return null
  }
  return {
    lineId: o.lineId, lineNo: o.lineNo, hasSnapshot: o.hasSnapshot, origin: o.origin as LineTaxMode | null,
    resolvedOn: o.resolvedOn as string | null, buyerSituation: o.buyerSituation as string | null, itemType: o.itemType as string | null,
    state: o.state as string | null, reason: o.reason as string | null, vatState: o.vatState as string | null,
    vatPercent: o.vatPercent as string | null, exemptionCodeState: o.exemptionCodeState as string | null,
    exemptionCode: o.exemptionCode as string | null, exemptionCodeReason: o.exemptionCodeReason as string | null,
    withholdingState: o.withholdingState as string | null, withholdingPercent: o.withholdingPercent as string | null,
    withholdingCodeState: o.withholdingCodeState as string | null, withholdingCode: o.withholdingCode as string | null,
    withholdingCodeReason: o.withholdingCodeReason as string | null, buyerAdjustment: o.buyerAdjustment as string | null,
    buyerDocumentRequired: o.buyerDocumentRequired, rateState: o.rateState, unavailableReason: o.unavailableReason,
    invoiceBlockers: o.invoiceBlockers as string[], override,
  }
}

const RESPONSE_KEYS = ['draftVersion', 'revision', 'businessDate', 'buyerTax', 'lines'] as const

/** The tax of `detail`, or null when the body is malformed or describes anything but exactly that detail. */
export function decodeOrderTax(value: unknown, detail: DraftDetail): OrderTax | null {
  const o = exact(value, RESPONSE_KEYS)
  if (o === null || o.draftVersion !== detail.draft.version || typeof o.draftVersion !== 'string' || !VERSION.test(o.draftVersion)
    || o.revision !== detail.draft.revision || !isDateOnly(o.businessDate) || !Array.isArray(o.lines)
    || o.lines.length !== detail.lines.length) return null
  const buyerTax = o.buyerTax === null ? null : decodeBuyerTax(o.buyerTax)
  if (o.buyerTax !== null && buyerTax === null) return null
  const byId = new Map<string, LineTax>()
  for (const raw of o.lines) {
    const line = decodeLine(raw)
    if (line === null || byId.has(line.lineId.toLowerCase())) return null
    byId.set(line.lineId.toLowerCase(), line)
  }
  const lines: LineTax[] = []
  for (const expected of detail.lines) {
    const line = byId.get(expected.lineId.toLowerCase())
    if (line === undefined || line.lineNo !== expected.lineNo) return null
    lines.push({ ...line, lineId: expected.lineId })
  }
  return { draftVersion: o.draftVersion, revision: o.revision, businessDate: o.businessDate, buyerTax, lines }
}

const OPTIONS_KEYS = ['on', 'buyerSituationsState', 'buyerSituationsReason', 'buyerSituationListVersion', 'buyerSituations',
  'exemptionCodesState', 'exemptionCodesReason', 'exemptionCodes', 'itemTypes', 'vatRatesState', 'vatRatesReason', 'vatRates',
  'withholdingRatiosState', 'withholdingRatiosReason', 'withholdingRatios'] as const
const RATE_KEYS = ['code', 'name', 'percent', 'taxCode'] as const

/** A rate list of the options: entries with exact percents; Resolved ⇔ no reason; Unresolved ⇔ empty. Undefined = malformed. */
function decodeRateList(state: unknown, reason: unknown, entries: unknown): readonly TaxRateOption[] | undefined {
  if (!resolution(state) || !textOrNull(reason) || !Array.isArray(entries) || (state === 'Resolved') !== (reason === null)) return undefined
  const rates: TaxRateOption[] = []
  for (const raw of entries) {
    const r = exact(raw, RATE_KEYS)
    if (r === null || !text(r.code) || !text(r.name) || !percentOrNull(r.percent) || r.percent === null || !textOrNull(r.taxCode)) return undefined
    rates.push({ code: r.code, name: r.name, percent: r.percent as string, taxCode: r.taxCode })
  }
  return state === 'Unresolved' && rates.length > 0 ? undefined : rates
}
const SITUATION_KEYS = ['code', 'name', 'withholdingApplies', 'vatMode', 'documentRequired'] as const
const CODE_KEYS = ['code', 'name'] as const

/** The Settings lists in force on `on`, or null when the body does not fit (an empty list carries its reason). */
export function decodeTaxOptions(value: unknown, on: string): TaxOptions | null {
  const o = exact(value, OPTIONS_KEYS)
  if (o === null || o.on !== on || !resolution(o.buyerSituationsState) || !textOrNull(o.buyerSituationsReason)
    || !resolution(o.exemptionCodesState) || !textOrNull(o.exemptionCodesReason) || !versionOrNull(o.buyerSituationListVersion)
    || !Array.isArray(o.buyerSituations) || !Array.isArray(o.exemptionCodes) || !Array.isArray(o.itemTypes)
    || !o.itemTypes.every(text)) return null
  const situations: BuyerSituationOption[] = []
  for (const raw of o.buyerSituations) {
    const s = exact(raw, SITUATION_KEYS)
    if (s === null || !text(s.code) || !text(s.name) || typeof s.withholdingApplies !== 'boolean' || !text(s.vatMode)
      || typeof s.documentRequired !== 'boolean') return null
    situations.push({ code: s.code, name: s.name, withholdingApplies: s.withholdingApplies, vatMode: s.vatMode, documentRequired: s.documentRequired })
  }
  const codes: TaxCodeOption[] = []
  for (const raw of o.exemptionCodes) {
    const c = exact(raw, CODE_KEYS)
    if (c === null || !text(c.code) || !text(c.name)) return null
    codes.push({ code: c.code, name: c.name })
  }
  // A resolved list says so with entries and no reason; an unresolved one is empty with its reason (never a guessed list).
  if ((o.buyerSituationsState === 'Resolved') !== (o.buyerSituationsReason === null)
    || (o.buyerSituationsState === 'Unresolved' && situations.length > 0)
    || (o.exemptionCodesState === 'Resolved') !== (o.exemptionCodesReason === null)
    || (o.exemptionCodesState === 'Unresolved' && codes.length > 0)) return null
  const vatRates = decodeRateList(o.vatRatesState, o.vatRatesReason, o.vatRates)
  const withholdingRatios = decodeRateList(o.withholdingRatiosState, o.withholdingRatiosReason, o.withholdingRatios)
  if (vatRates === undefined || withholdingRatios === undefined) return null
  return {
    on: o.on, buyerSituationsState: o.buyerSituationsState, buyerSituationsReason: o.buyerSituationsReason,
    buyerSituations: situations, exemptionCodesState: o.exemptionCodesState, exemptionCodesReason: o.exemptionCodesReason,
    exemptionCodes: codes, itemTypes: o.itemTypes as string[],
    vatRatesState: o.vatRatesState as 'Resolved' | 'Unresolved', vatRatesReason: o.vatRatesReason as string | null, vatRates,
    withholdingRatiosState: o.withholdingRatiosState as 'Resolved' | 'Unresolved',
    withholdingRatiosReason: o.withholdingRatiosReason as string | null, withholdingRatios,
  }
}

/** The form's buyer tax inputs (text as typed). */
export interface BuyerTaxInput {
  readonly situation: string
  readonly exemptionCode: string
  readonly documentNo: string
  readonly documentDate: string
}

export const EMPTY_BUYER_TAX_INPUT: BuyerTaxInput = { situation: START_BUYER_SITUATION, exemptionCode: '', documentNo: '', documentDate: '' }

export type BuyerTaxBuild =
  | { readonly ok: true; readonly buyerTax: BuyerTaxRequest }
  | { readonly ok: false; readonly error: 'situation-required' | 'document-incomplete' }

/**
 * The wire buyer tax of the form's inputs: the situation, the exemption code only for a "KDV %0 with an exemption code"
 * situation, the document (number and date together) only for a situation that requires one. Whether the values are in the
 * Settings lists is the server's check (422); this only keeps the shape.
 */
export function buildBuyerTax(input: BuyerTaxInput, situation: BuyerSituationOption | undefined): BuyerTaxBuild {
  const code = input.situation.trim()
  if (code === '') return { ok: false, error: 'situation-required' }
  const exemption = situation?.vatMode === VAT_MODE.zeroWithExemptionCode ? input.exemptionCode.trim() : ''
  const documentNo = situation?.documentRequired ? input.documentNo.trim() : ''
  const documentDate = situation?.documentRequired ? input.documentDate.trim() : ''
  if ((documentNo === '') !== (documentDate === '')) return { ok: false, error: 'document-incomplete' }
  return {
    ok: true,
    buyerTax: {
      situation: code,
      ...(exemption === '' ? {} : { exemptionCode: exemption }),
      ...(documentNo === '' ? {} : { documentNo, documentDate }),
    },
  }
}

/** The form inputs of a stored buyer tax (the header revision form's starting point). */
export function buyerTaxInputOf(buyer: BuyerTax | null): BuyerTaxInput {
  return buyer === null
    ? EMPTY_BUYER_TAX_INPUT
    : { situation: buyer.situation, exemptionCode: buyer.exemptionCode ?? '', documentNo: buyer.documentNo ?? '', documentDate: buyer.documentDate ?? '' }
}

/** A line's tax choice in the price form: keep the current one, the Settings default, or a reasoned change. */
export interface LineTaxChoiceInput {
  readonly mode: 'keep' | 'default' | 'override'
  readonly buyerSituation: string
  readonly itemType: string
  readonly exemptionCode: string
  readonly reason: string
  /** SAT-D181: a code of the Settings VAT rate list ('' = the default's rate). */
  readonly vatRateCode?: string
  /** SAT-D181: a code of the Settings withholding ratio list ('' = the default's ratio). */
  readonly withholdingRatioCode?: string
}

export const KEEP_LINE_TAX: LineTaxChoiceInput = {
  mode: 'keep', buyerSituation: '', itemType: '', exemptionCode: '', reason: '', vatRateCode: '', withholdingRatioCode: '',
}

/** `SalesDraftLineTaxChoiceV1` on the wire; null keeps the line's current choice. */
export interface LineTaxChoiceRequest {
  readonly mode: LineTaxMode
  readonly buyerSituation?: string
  readonly reason?: string
  readonly itemType?: string
  readonly exemptionCode?: string
  readonly vatRateCode?: string
  readonly withholdingRatioCode?: string
}

export type LineTaxChoiceBuild =
  | { readonly ok: true; readonly tax: LineTaxChoiceRequest | null }
  | { readonly ok: false; readonly error: 'tax-situation-required' | 'tax-reason-required' }

/** The wire tax choice of one line (SAT-D93/D97: a change names a Settings situation and a reason). */
export function buildLineTaxChoice(input: LineTaxChoiceInput): LineTaxChoiceBuild {
  if (input.mode === 'keep') return { ok: true, tax: null }
  if (input.mode === 'default') return { ok: true, tax: { mode: LINE_TAX_MODE.default } }
  const situation = input.buyerSituation.trim()
  const reason = input.reason.trim()
  if (situation === '') return { ok: false, error: 'tax-situation-required' }
  if (reason === '') return { ok: false, error: 'tax-reason-required' }
  const itemType = input.itemType.trim()
  const exemptionCode = input.exemptionCode.trim()
  const vatRateCode = (input.vatRateCode ?? '').trim()
  const withholdingRatioCode = (input.withholdingRatioCode ?? '').trim()
  return {
    ok: true,
    tax: {
      mode: LINE_TAX_MODE.override,
      buyerSituation: situation,
      reason,
      ...(itemType === '' ? {} : { itemType }),
      ...(exemptionCode === '' ? {} : { exemptionCode }),
      ...(vatRateCode === '' ? {} : { vatRateCode }),
      ...(withholdingRatioCode === '' ? {} : { withholdingRatioCode }),
    },
  }
}

/* ── Screen helpers (no component) ─────────────────────────────────────────── */

export function situationOf(options: TaxOptions | null, code: string): BuyerSituationOption | undefined {
  return options?.buyerSituations.find((candidate) => candidate.code === code)
}

/** The read lists, or null with the text that says why there are none to offer. */
export function readOptions(outcome: ViewOutcome<TaxOptions> | undefined): { readonly options: TaxOptions | null; readonly notice: string | null } {
  if (outcome === undefined) return { options: null, notice: ORDER_TAX_TEXT.optionsLoading }
  if (outcome.type !== 'ok-single') return { options: null, notice: ORDER_TAX_TEXT.optionsUnavailable }
  if (outcome.data.buyerSituationsState !== 'Resolved') return { options: outcome.data, notice: ORDER_TAX_TEXT.optionsEmpty(outcome.data.buyerSituationsReason) }
  return { options: outcome.data, notice: null }
}

/** What the card shows: the read's outcome, or why nothing is read (no lines; prices withheld → 403 without a read). */
export type TaxCardOutcome = ViewOutcome<OrderTax> | { readonly type: 'no-lines' } | undefined

export function taxCardOutcome(detail: DraftDetail, read: ViewOutcome<OrderTax> | undefined): TaxCardOutcome {
  if (detail.lines.length === 0) return { type: 'no-lines' }
  return detail.lines.every((line) => line.priceDisclosure === 'Visible') ? read : { type: 'http', status: 403 }
}
