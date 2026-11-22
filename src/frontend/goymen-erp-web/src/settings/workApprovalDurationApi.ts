import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import {
  WORK_APPROVAL_DURATION_PATH,
  decodeWorkApprovalDurationPolicy,
  workApprovalDurationVersionsPath,
  type RecordWorkApprovalDurationBody,
  type WorkApprovalDurationClassKey,
} from './workApprovalDurationContract'

export const fetchWorkApprovalDuration = (company: string, signal?: AbortSignal) =>
  readJson(WORK_APPROVAL_DURATION_PATH, company, (raw) => decodeWorkApprovalDurationPolicy(raw, company), signal)

export const recordWorkApprovalDuration = (key: WorkApprovalDurationClassKey, context: CommandContext, body: RecordWorkApprovalDurationBody) =>
  send('POST', workApprovalDurationVersionsPath(key), context, body, 204, noBody)
