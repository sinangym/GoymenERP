import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import { decodeMoneyProfiles, MONEY_PROFILES_PATH, type RecordMoneyProfileBody } from './moneyProfileContract'

export const fetchMoneyProfiles = (companyId: string, signal?: AbortSignal) =>
  readJson(MONEY_PROFILES_PATH, companyId, (body) => decodeMoneyProfiles(body, companyId), signal)
export const recordMoneyProfile = (context: CommandContext, body: RecordMoneyProfileBody) =>
  send('POST', `${MONEY_PROFILES_PATH}/versions`, context, body, 204, noBody)
