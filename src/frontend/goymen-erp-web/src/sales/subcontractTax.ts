import { readJson, send, type CommandContext, type CommandOutcome } from '../settings/discountLimitApi'
import { decodeDraftMutation, salesDraftDetailPath, STATE_COMMAND_STATUS, type DraftDetail, type DraftMutation } from './contract'
import { decodeTaxOptions, type LineTaxChoiceRequest, type TaxOptions } from './orderTax'
export interface SubcontractTaxOptions { readonly version: string; readonly businessDate: string; readonly buyerSituation: string; readonly options: TaxOptions }
export interface SubcontractTaxRevision { readonly expectedVersion: string; readonly lineId: string; readonly tax: LineTaxChoiceRequest; readonly reason: string }
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
export function decodeSubcontractTaxOptions(v: unknown, detail: DraftDetail): SubcontractTaxOptions | null {
  if (!object(v) || Object.keys(v).length !== 4 || !['version', 'businessDate', 'buyerSituation', 'options'].every(x => Object.hasOwn(v, x))
    || v.version !== detail.draft.version || v.businessDate !== detail.draft.businessDate || typeof v.buyerSituation !== 'string' || !/^[A-Za-z][A-Za-z0-9]{0,63}$/.test(v.buyerSituation)) return null
  const options = decodeTaxOptions(v.options, detail.draft.businessDate)
  return options === null ? null : { version: v.version, businessDate: v.businessDate, buyerSituation: v.buyerSituation, options }
}
export const fetchSalesSubcontractTaxOptions = (company: string, detail: DraftDetail, signal?: AbortSignal) =>
  readJson(`${salesDraftDetailPath(detail.draft.id)}/subcontract-tax-options`, company, v => decodeSubcontractTaxOptions(v, detail), signal)
export async function reviseSalesSubcontractTax(context: CommandContext, detail: DraftDetail, request: SubcontractTaxRevision): Promise<CommandOutcome<DraftMutation>> {
  const result = await send('POST', `${salesDraftDetailPath(detail.draft.id)}/subcontract-tax-revision`, context, request, STATE_COMMAND_STATUS, raw => {
    const mutation = decodeDraftMutation(raw)
    if (mutation === null || mutation.detail.draft.companyId !== context.companyId || mutation.detail.draft.id !== detail.draft.id
      || BigInt(mutation.detail.draft.version) !== BigInt(request.expectedVersion) + 1n || mutation.detail.lines.length !== detail.lines.length
      || detail.lines.some(x => !mutation.detail.lines.some(y => y.lineId === x.lineId && y.productId === x.productId && y.orderUnitId === x.orderUnitId && y.quantity === x.quantity))) return null
    return mutation
  })
  return result.type === 'applied' && result.data.isReplay !== result.replayed ? { type: 'unknown', reason: 'unexpected' } : result
}
