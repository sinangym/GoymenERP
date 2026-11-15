/**
 * F-029 S9 (SAT-D181) rate list transport over the shared Settings transport (./discountLimitApi.ts `readJson` / `send`):
 * the read sends the selected company only (`X-Company-Id`); a command carries one caller-owned `Idempotency-Key`, success
 * is exactly 204, a 5xx or an off-contract answer is an UNKNOWN outcome (retry with the same key), a refusal is read by its
 * status and stable `code` (the tax master's problem codes).
 */

import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import {
  RATE_LISTS_PATH,
  decodeRateLists,
  rateListVersionsPath,
  rateListWithdrawalPath,
  type PublishRateListBody,
  type RateListKind,
  type RateLists,
  type WithdrawRateListBody,
} from './taxRateListContract'
import type { ViewOutcome } from '../shell/uiState'

export function fetchRateLists(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<RateLists>> {
  return readJson(RATE_LISTS_PATH, companyId, (raw) => decodeRateLists(raw, companyId), signal)
}

/** A whole list version (SAT-D181): the entries replace the previous version from its date. */
export function publishRateList(context: CommandContext, kind: RateListKind, body: PublishRateListBody) {
  return send<true>('POST', rateListVersionsPath(kind), context, body, 204, noBody)
}

/** Withdraw a version that is not yet effective. */
export function withdrawRateList(context: CommandContext, kind: RateListKind, listVersion: string, body: WithdrawRateListBody) {
  return send<true>('POST', rateListWithdrawalPath(kind, listVersion), context, body, 204, noBody)
}
