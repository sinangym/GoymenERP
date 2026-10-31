/**
 * MOD-03 tax master transport over the shared Settings transport (./discountLimitApi.ts `readJson` / `send`): reads send
 * the selected company only (`X-Company-Id`); commands carry one caller-owned `Idempotency-Key`, success is exactly the
 * contract's status, a 5xx or an off-contract answer is an UNKNOWN outcome (retry with the same key), a refusal is read by
 * its status and stable `code`.
 */

import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import {
  TAX_MASTER_PATH,
  WITHHOLDING_THRESHOLD_PATH,
  codeListPath,
  codeListWithdrawalPath,
  decodeTaxMaster,
  itemTypeDefaultPath,
  itemTypeDefaultWithdrawalPath,
  thresholdWithdrawalPath,
  type CodeListKind,
  type ItemType,
  type PublishCodeListBody,
  type RecordDefaultBody,
  type RecordThresholdBody,
  type TaxMaster,
  type WithdrawBody,
} from './taxMasterContract'
import type { ViewOutcome } from '../shell/uiState'

export function fetchTaxMaster(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<TaxMaster>> {
  return readJson(TAX_MASTER_PATH, companyId, (raw) => decodeTaxMaster(raw, companyId), signal)
}

export function recordItemTypeDefault(context: CommandContext, itemType: ItemType, body: RecordDefaultBody) {
  return send<true>('POST', itemTypeDefaultPath(itemType), context, body, 204, noBody)
}

export function withdrawItemTypeDefault(context: CommandContext, recordId: string, body: WithdrawBody) {
  return send<true>('POST', itemTypeDefaultWithdrawalPath(recordId), context, body, 204, noBody)
}

export function publishCodeList(context: CommandContext, kind: CodeListKind, body: PublishCodeListBody) {
  return send<true>('POST', codeListPath(kind), context, body, 204, noBody)
}

export function withdrawCodeList(context: CommandContext, kind: CodeListKind, listVersion: string, body: WithdrawBody) {
  return send<true>('POST', codeListWithdrawalPath(kind, listVersion), context, body, 204, noBody)
}

export function recordWithholdingThreshold(context: CommandContext, body: RecordThresholdBody) {
  return send<true>('POST', WITHHOLDING_THRESHOLD_PATH, context, body, 204, noBody)
}

export function withdrawWithholdingThreshold(context: CommandContext, recordId: string, body: WithdrawBody) {
  return send<true>('POST', thresholdWithdrawalPath(recordId), context, body, 204, noBody)
}
