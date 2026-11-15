/**
 * F-004 / KIM-D37 client — reads and records the company's unit conversion policy through the shared Settings transport
 * (`readJson` / `send`). A write is a 204: reaching the exact status IS the answer, and the screen reads the view again
 * afterwards. Cookie writes get the antiforgery header from the shared fetch wrapper (KIM-S24); nothing here adds it.
 */

import type { ViewOutcome } from '../shell/uiState'
import { noBody, readJson, send, type CommandContext, type CommandOutcome } from './discountLimitApi'
import {
  UNIT_CONVERSION_POLICY_PATH,
  UNIT_CONVERSION_POLICY_VERSIONS_PATH,
  decodeUnitConversionPolicyView,
  type RecordUnitConversionPolicyBody,
  type UnitConversionPolicyView,
} from './unitConversionPolicyContract'

export function fetchUnitConversionPolicy(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<UnitConversionPolicyView>> {
  return readJson(
    UNIT_CONVERSION_POLICY_PATH,
    companyId,
    (raw) => decodeUnitConversionPolicyView(raw, companyId),
    signal,
  )
}

export function recordUnitConversionPolicyVersion(
  context: CommandContext,
  body: RecordUnitConversionPolicyBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', UNIT_CONVERSION_POLICY_VERSIONS_PATH, context, body, 204, noBody, signal)
}
