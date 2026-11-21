import { readJson, send, noBody, type CommandContext } from './discountLimitApi'
import { SALES_CONTRACT_DISCOUNT_LIMIT_PATH as PATH, decodeContractDiscountLimit, type RecordContractDiscountLimit } from './salesContractDiscountLimitContract'
export const fetchContractDiscountLimit = (company: string, signal?: AbortSignal) => readJson(PATH, company, raw => decodeContractDiscountLimit(raw, company), signal)
export const recordContractDiscountLimit = (context: CommandContext, body: RecordContractDiscountLimit) => send('POST', `${PATH}/versions`, context, body, 204, noBody)
