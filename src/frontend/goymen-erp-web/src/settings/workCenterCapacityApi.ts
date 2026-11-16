/**
 * URT-D89 client — reads the company's work center capacities and records a new version of one work center through the
 * shared Settings transport (`readJson` / `send`). The write is a 204: reaching the exact status IS the answer, and the
 * screen reads the view again afterwards. The work center names come from the Organization owner's own read
 * (`../organization/api.ts`), never from this route.
 */

import type { ViewOutcome } from '../shell/uiState'
import { noBody, readJson, send, type CommandContext, type CommandOutcome } from './discountLimitApi'
import {
  WORK_CENTER_CAPACITY_PATH,
  decodeWorkCenterCapacityView,
  workCenterCapacityVersionsPath,
  type RecordWorkCenterCapacityBody,
  type WorkCenterCapacityView,
} from './workCenterCapacityContract'

export function fetchWorkCenterCapacities(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<WorkCenterCapacityView>> {
  return readJson(WORK_CENTER_CAPACITY_PATH, companyId, decodeWorkCenterCapacityView, signal)
}

export function recordWorkCenterCapacityVersion(
  context: CommandContext,
  workCenterId: string,
  body: RecordWorkCenterCapacityBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', workCenterCapacityVersionsPath(workCenterId), context, body, 204, noBody, signal)
}
