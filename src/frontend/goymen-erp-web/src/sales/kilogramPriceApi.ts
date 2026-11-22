import { headers, readFailure, request } from './api'
import { decodeOrderKilogramPrices, salesDraftKilogramPricesPath, type OrderKilogramPrices } from './kilogramPriceContract'
import type { ViewOutcome } from '../shell/uiState'

/** SAT-D436 (AK-5): the order's kg prices and realization (the server measures the draft read AND the price read). */
export async function fetchDraftKilogramPrices(
  draftId: string,
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<OrderKilogramPrices>> {
  const raw = await request(salesDraftKilogramPricesPath(draftId), { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const prices = decodeOrderKilogramPrices(raw.body)
  return prices === null ? { type: 'malformed' } : { type: 'ok-single', data: prices }
}
