import { readJson, send, noBody, type CommandContext } from './discountLimitApi'
import { COLLATERAL_TYPES_PATH as PATH, decodeCollateralTypes, type CreateCollateralType, type RecordCollateralType } from './collateralTypesContract'
export const fetchCollateralTypes = (company: string, signal?: AbortSignal) => readJson(PATH, company, raw => decodeCollateralTypes(raw, company), signal)
export const createCollateralType = (context: CommandContext, body: CreateCollateralType) => send('POST', PATH, context, body, 204, noBody)
export const recordCollateralType = (context: CommandContext, id: string, body: RecordCollateralType) => send('POST', `${PATH}/${encodeURIComponent(id)}/versions`, context, body, 204, noBody)
