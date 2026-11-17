import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import { subcontractTaxPath, decodeSubcontractTaxOptions, decodeSubcontractTaxPolicy, type RecordSubcontractTaxBody, type SubcontractTaxSide } from './subcontractTaxContract'
export const fetchSubcontractTaxPolicy = (side: SubcontractTaxSide, company: string, signal?: AbortSignal) => readJson(subcontractTaxPath(side), company, v => decodeSubcontractTaxPolicy(v, company), signal)
export const fetchSubcontractTaxOptions = (side: SubcontractTaxSide, company: string, signal?: AbortSignal) => readJson(`${subcontractTaxPath(side)}/options`, company, v => decodeSubcontractTaxOptions(v, company, side), signal)
export const recordSubcontractTaxPolicy = (side: SubcontractTaxSide, context: CommandContext, body: RecordSubcontractTaxBody) => send('POST', `${subcontractTaxPath(side)}/versions`, context, body, 204, noBody)
