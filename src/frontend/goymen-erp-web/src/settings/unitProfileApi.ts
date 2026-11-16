/**
 * F-029 S1/S2 client — reads the company's unit profiles, transfers the approved baseline once and records a new unit
 * version through the shared Settings transport (`readJson` / `send`). A write is a 204: reaching the exact status IS the
 * answer, and the screen reads the view again afterwards. Cookie writes get the antiforgery header from the shared fetch
 * wrapper; nothing here adds it.
 */

import type { ViewOutcome } from '../shell/uiState'
import { noBody, readJson, send, type CommandContext, type CommandOutcome } from './discountLimitApi'
import {
  UNIT_PROFILES_PATH,
  UNIT_PROFILE_BASELINE_PATH,
  UNIT_PROFILE_VERSIONS_PATH,
  decodeUnitProfileView,
  type RecordUnitProfileBody,
  type TransferUnitProfileBaselineBody,
  type UnitProfileView,
} from './unitProfileContract'

export function fetchUnitProfiles(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<UnitProfileView>> {
  return readJson(UNIT_PROFILES_PATH, companyId, (raw) => decodeUnitProfileView(raw, companyId), signal)
}

export function transferUnitProfileBaseline(
  context: CommandContext,
  body: TransferUnitProfileBaselineBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', UNIT_PROFILE_BASELINE_PATH, context, body, 204, noBody, signal)
}

export function recordUnitProfileVersion(
  context: CommandContext,
  body: RecordUnitProfileBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', UNIT_PROFILE_VERSIONS_PATH, context, body, 204, noBody, signal)
}
