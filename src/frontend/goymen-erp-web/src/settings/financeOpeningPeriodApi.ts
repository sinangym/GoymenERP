import { readJson, send, noBody, type CommandContext } from './discountLimitApi'
import { FINANCE_OPENING_PERIOD_PATH, decodeFinanceOpeningPeriod, type RecordFinanceOpeningPeriodBody } from './financeOpeningPeriodContract'
export const fetchFinanceOpeningPeriod = (company: string, signal?: AbortSignal) => readJson(FINANCE_OPENING_PERIOD_PATH, company, (raw) => decodeFinanceOpeningPeriod(raw, company), signal)
export const recordFinanceOpeningPeriod = (context: CommandContext, body: RecordFinanceOpeningPeriodBody) => send('POST', `${FINANCE_OPENING_PERIOD_PATH}/versions`, context, body, 204, noBody)
