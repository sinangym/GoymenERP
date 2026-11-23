/**
 * F-025 (B-11) exchange rate transport over the shared Settings transport (./discountLimitApi.ts `readJson` / `send`):
 * reads send the selected company only (`X-Company-Id`) with exactly the query the endpoint accepts; commands carry no
 * query string and one caller-owned `Idempotency-Key`, success is exactly the contract's status, a 5xx (including the
 * code-less 503 of a disabled bulletin source) or an off-contract answer is an UNKNOWN outcome (retry with the same key),
 * a refusal is read by its status and stable `code`.
 */

import { noBody, readJson, send, type CommandContext } from './discountLimitApi'
import {
  EXCHANGE_RATE_IMPORTS_PATH,
  decodeExchangeRateDayList,
  decodeImportSummary,
  decodeResolution,
  exchangeRateCorrectionPath,
  exchangeRateHalfDayMarkPath,
  exchangeRateListPath,
  exchangeRateResolutionPath,
  exchangeRateReverificationPath,
  type CorrectRateBody,
  type ExchangeRateDayList,
  type ExchangeRateImportSummary,
  type ExchangeRateResolution,
  type ImportRatesBody,
  type MarkHalfDayBody,
  type ReverifyDayBody,
} from './exchangeRateContract'
import type { ViewOutcome } from '../shell/uiState'

export function fetchExchangeRateDays(
  companyId: string,
  from: string,
  to: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ExchangeRateDayList>> {
  return readJson(
    exchangeRateListPath(from, to),
    companyId,
    (raw) => decodeExchangeRateDayList(raw, companyId, from, to),
    signal,
  )
}

/** The one backend calculator's answer (SAT-D188): the screen shows it and never derives a rate of its own. */
export function fetchExchangeRateResolution(
  companyId: string,
  rateDay: string,
  currencyCode: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ExchangeRateResolution>> {
  return readJson(
    exchangeRateResolutionPath(rateDay, currencyCode),
    companyId,
    (raw) => decodeResolution(raw, rateDay, currencyCode),
    signal,
  )
}

export function correctExchangeRate(context: CommandContext, date: string, body: CorrectRateBody) {
  return send<true>('POST', exchangeRateCorrectionPath(date), context, body, 204, noBody)
}

export function markExchangeRateHalfDay(context: CommandContext, date: string, body: MarkHalfDayBody) {
  return send<true>('POST', exchangeRateHalfDayMarkPath(date), context, body, 204, noBody)
}

export function reverifyExchangeRateDay(context: CommandContext, date: string, body: ReverifyDayBody) {
  return send<true>('POST', exchangeRateReverificationPath(date), context, body, 204, noBody)
}

export function importExchangeRates(context: CommandContext, body: ImportRatesBody) {
  return send<ExchangeRateImportSummary>('POST', EXCHANGE_RATE_IMPORTS_PATH, context, body, 200, (raw) =>
    decodeImportSummary(raw, body.from, body.to),
  )
}
