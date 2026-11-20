import { isPurchasingGuid } from './contract'

export interface ExactSourceQuantity { readonly numerator: string; readonly denominator: string }
export interface ReturnSourceSelection {
  readonly takeAll: boolean
  readonly quantityKg: string | null
  readonly undoQuantity: string | null
}
export interface SupplierReturnCostAllocation {
  readonly expectedSourceVersion: string
  readonly pending: ReturnSourceSelection
  readonly invoicedSources: readonly (ReturnSourceSelection & { readonly sourceEventId: string })[]
}
export interface SupplierReturnSourceLabel {
  readonly sourceEventId: string
  readonly invoiceId: string
  readonly invoiceLineId: string
  readonly invoiceNumber: string
  readonly invoiceLineNo: number
}
export interface SupplierReturnCostSources {
  readonly companyId: string
  readonly goodsAcceptanceId: string
  readonly labelId: string
  readonly sourceVersion: string
  readonly pendingQuantityKg: ExactSourceQuantity
  readonly pendingUndoQuantity: ExactSourceQuantity
  readonly pendingWholePairAvailable: boolean
  readonly invoicedSources: readonly {
    readonly sourceEventId: string
    readonly invoiceId: string
    readonly invoiceLineId: string
    readonly invoiceNumber: string
    readonly invoiceLineNo: number
    readonly availableQuantityKg: ExactSourceQuantity
    readonly availableUndoQuantity: ExactSourceQuantity
    readonly wholePairAvailable: boolean
  }[]
}

type Obj = Record<string, unknown>
const object = (value: unknown): Obj | null => typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Obj : null
const exact = (value: Obj, names: readonly string[]) => Object.keys(value).length === names.length && names.every(name => Object.hasOwn(value, name))
const text = (value: unknown): value is string => typeof value === 'string' && value.trim() === value && value.length > 0
const integer = (value: unknown): value is string => typeof value === 'string' && value.length <= 512 && /^(0|[1-9][0-9]*)$/.test(value)
const quantity = (value: unknown): value is string => typeof value === 'string' && /^(0|[1-9][0-9]{0,14})(\.[0-9]{0,2}[1-9])?$/.test(value)
function ratio(value: unknown): value is ExactSourceQuantity {
  const source = object(value)
  return source !== null && exact(source, ['numerator', 'denominator'])
    && integer(source.numerator) && integer(source.denominator) && source.denominator !== '0'
}

/** Shape and source identity only; availability and all quantity sums are evaluated by the backend. */
export function decodeSupplierReturnCostSources(value: unknown, companyId: string, acceptanceId: string, labelId: string): SupplierReturnCostSources | null {
  const source = object(value)
  if (source === null || !exact(source, ['companyId', 'goodsAcceptanceId', 'labelId', 'sourceVersion', 'pendingQuantityKg', 'pendingUndoQuantity', 'pendingWholePairAvailable', 'invoicedSources'])
    || source.companyId !== companyId || source.goodsAcceptanceId !== acceptanceId || source.labelId !== labelId
    || !text(source.sourceVersion) || !ratio(source.pendingQuantityKg) || !ratio(source.pendingUndoQuantity)
    || typeof source.pendingWholePairAvailable !== 'boolean' || !Array.isArray(source.invoicedSources)) return null
  const seen = new Set<string>()
  for (const raw of source.invoicedSources) {
    const row = object(raw)
    if (row === null || !exact(row, ['sourceEventId', 'invoiceId', 'invoiceLineId', 'invoiceNumber', 'invoiceLineNo', 'availableQuantityKg', 'availableUndoQuantity', 'wholePairAvailable'])
      || !isPurchasingGuid(row.sourceEventId) || !isPurchasingGuid(row.invoiceId) || !isPurchasingGuid(row.invoiceLineId)
      || !text(row.invoiceNumber) || !Number.isSafeInteger(row.invoiceLineNo) || (row.invoiceLineNo as number) <= 0
      || !ratio(row.availableQuantityKg) || !ratio(row.availableUndoQuantity) || typeof row.wholePairAvailable !== 'boolean'
      || seen.has(row.sourceEventId.toLowerCase())) return null
    seen.add(row.sourceEventId.toLowerCase())
  }
  return source as unknown as SupplierReturnCostSources
}

export function decodeSupplierReturnCostAllocation(value: unknown): SupplierReturnCostAllocation | null {
  const source = object(value)
  if (source === null || !exact(source, ['expectedSourceVersion', 'pending', 'invoicedSources'])
    || !text(source.expectedSourceVersion) || !Array.isArray(source.invoicedSources)) return null
  function selection(raw: unknown, invoiced: boolean): boolean {
    const row = object(raw)
    return row !== null && exact(row, invoiced ? ['sourceEventId', 'takeAll', 'quantityKg', 'undoQuantity'] : ['takeAll', 'quantityKg', 'undoQuantity'])
      && (row.takeAll === true ? row.quantityKg === null && (row.undoQuantity === null || quantity(row.undoQuantity) && row.undoQuantity !== '0')
        : row.takeAll === false && quantity(row.quantityKg) && quantity(row.undoQuantity)
          && (row.quantityKg === '0') === (row.undoQuantity === '0')
          && (!invoiced || row.quantityKg !== '0' && row.undoQuantity !== '0'))
  }
  if (!selection(source.pending, false)) return null
  const seen = new Set<string>()
  for (const raw of source.invoicedSources) {
    const row = object(raw)
    if (row === null || !selection(raw, true) || !isPurchasingGuid(row.sourceEventId) || seen.has(row.sourceEventId.toLowerCase())) return null
    seen.add(row.sourceEventId.toLowerCase())
  }
  return source as unknown as SupplierReturnCostAllocation
}

/** Immutable, price-free labels must describe exactly the explicitly selected invoice sources. */
export function decodeSupplierReturnSourceLabels(value: unknown, allocation: SupplierReturnCostAllocation): readonly SupplierReturnSourceLabel[] | null {
  if (!Array.isArray(value) || value.length !== allocation.invoicedSources.length) return null
  const selected = new Set(allocation.invoicedSources.map(source => source.sourceEventId.toLowerCase()))
  for (const raw of value) {
    const row = object(raw)
    if (row === null || !exact(row, ['sourceEventId', 'invoiceId', 'invoiceLineId', 'invoiceNumber', 'invoiceLineNo'])
      || !isPurchasingGuid(row.sourceEventId) || !selected.delete(row.sourceEventId.toLowerCase())
      || !isPurchasingGuid(row.invoiceId) || !isPurchasingGuid(row.invoiceLineId) || !text(row.invoiceNumber)
      || !Number.isSafeInteger(row.invoiceLineNo) || (row.invoiceLineNo as number) <= 0) return null
  }
  return value as readonly SupplierReturnSourceLabel[]
}
