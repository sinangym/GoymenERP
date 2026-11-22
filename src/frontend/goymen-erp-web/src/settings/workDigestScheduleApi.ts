import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import {
  WORK_DIGEST_SCHEDULE_PATH, decodeWorkDigestSchedule, workDigestTimeVersionsPath, type RecordWorkDigestTimeBody, type WorkDigestSlot,
} from './workDigestScheduleContract'

export const fetchWorkDigestSchedule = (company: string, signal?: AbortSignal) =>
  readJson(WORK_DIGEST_SCHEDULE_PATH, company, (raw) => decodeWorkDigestSchedule(raw, company), signal)

export const recordWorkDigestTime = (slot: WorkDigestSlot, context: CommandContext, body: RecordWorkDigestTimeBody) =>
  send('POST', workDigestTimeVersionsPath(slot), context, body, 204, noBody)
