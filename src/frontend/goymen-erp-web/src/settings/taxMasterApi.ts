/**
 * MOD-03 tax master transport over the shared Settings transport (./discountLimitApi.ts `readJson` / `send`): reads send
 * the selected company only (`X-Company-Id`); commands carry one caller-owned `Idempotency-Key`, success is exactly the
 * contract's status, a 5xx or an off-contract answer is an UNKNOWN outcome (retry with the same key), a refusal is read by
 * its status and stable `code`.
 */

import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import {
  TAX_CATEGORIES_PATH,
  TAX_MASTER_PATH,
  codeListPath,
  codeListWithdrawalPath,
  decodeCategoryCreated,
  decodeTaxMaster,
  rateWithdrawalPath,
  vatRatePath,
  withholdingRatePath,
  type CodeListKind,
  type CreateCategoryBody,
  type PublishCodeListBody,
  type RecordVatRateBody,
  type RecordWithholdingRateBody,
  type TaxMaster,
  type WithdrawBody,
} from './taxMasterContract'
import type { ViewOutcome } from '../shell/uiState'

export function fetchTaxMaster(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<TaxMaster>> {
  return readJson(TAX_MASTER_PATH, companyId, (raw) => decodeTaxMaster(raw, companyId), signal)
}

export function createTaxCategory(context: CommandContext, body: CreateCategoryBody) {
  return send('POST', TAX_CATEGORIES_PATH, context, body, 201, decodeCategoryCreated)
}

export function recordVatRate(context: CommandContext, classId: string, body: RecordVatRateBody) {
  return send<true>('POST', vatRatePath(classId), context, body, 204, noBody)
}

export function recordWithholdingRate(context: CommandContext, classId: string, body: RecordWithholdingRateBody) {
  return send<true>('POST', withholdingRatePath(classId), context, body, 204, noBody)
}

export function withdrawRateRecord(context: CommandContext, recordId: string, body: WithdrawBody) {
  return send<true>('POST', rateWithdrawalPath(recordId), context, body, 204, noBody)
}

export function publishCodeList(context: CommandContext, kind: CodeListKind, body: PublishCodeListBody) {
  return send<true>('POST', codeListPath(kind), context, body, 204, noBody)
}

export function withdrawCodeList(context: CommandContext, kind: CodeListKind, listVersion: string, body: WithdrawBody) {
  return send<true>('POST', codeListWithdrawalPath(kind, listVersion), context, body, 204, noBody)
}
