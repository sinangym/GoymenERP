/**
 * UI-65 transport — API-37 `GET /api/v1/sales/control-center`. Reads only, sends no query member (the route refuses one), and
 * is bound to the committed shell company (`X-Company-Id`). A body is data only when the strict decoder vouches for it.
 */

import { headers, readFailure, request } from './api'
import { SALES_CONTROL_CENTER_PATH, decodeControlCenter, type ControlCenter } from './controlCenterContract'
import type { ViewOutcome } from '../shell/uiState'

export async function fetchControlCenter(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<ControlCenter>> {
  const raw = await request(SALES_CONTROL_CENTER_PATH, { headers: headers(companyId) }, signal)
  if ('failure' in raw) return readFailure(raw.failure)
  if (raw.status !== 200) return { type: 'http', status: raw.status, body: raw.body }
  const data = decodeControlCenter(raw.body)
  return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
}
