/**
 * F-029 S4b client — reads and records the Settings sales policies (SAT-D5 delivery tolerance, SAT-D6 approval amount
 * thresholds) through the shared Settings transport (`readJson` / `send`). Every write is a 204: reaching the exact status
 * IS the answer, and the screen reads the view again afterwards. Cookie writes get the antiforgery header from the shared
 * fetch wrapper (KIM-S24); nothing here adds it by hand.
 */

import type { ViewOutcome } from '../shell/uiState'
import { noBody, readJson, send, type CommandContext, type CommandOutcome } from './discountLimitApi'
import {
  APPROVAL_THRESHOLDS_BASELINE_PATH,
  APPROVAL_THRESHOLDS_PATH,
  DELIVERY_TOLERANCE_BASELINE_PATH,
  DELIVERY_TOLERANCE_PATH,
  DELIVERY_TOLERANCE_VERSIONS_PATH,
  approvalThresholdVersionsPath,
  decodeApprovalThresholdsView,
  decodeDeliveryToleranceView,
  type ApprovalThresholdsView,
  type DeliveryToleranceView,
  type RecordApprovalThresholdBody,
  type RecordDeliveryToleranceBody,
  type TransferBaselineBody,
} from './salesPolicyContract'

export function fetchDeliveryTolerance(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<DeliveryToleranceView>> {
  return readJson(DELIVERY_TOLERANCE_PATH, companyId, (raw) => decodeDeliveryToleranceView(raw, companyId), signal)
}

export function transferDeliveryToleranceBaseline(
  context: CommandContext,
  body: TransferBaselineBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', DELIVERY_TOLERANCE_BASELINE_PATH, context, body, 204, noBody, signal)
}

export function recordDeliveryToleranceVersion(
  context: CommandContext,
  body: RecordDeliveryToleranceBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', DELIVERY_TOLERANCE_VERSIONS_PATH, context, body, 204, noBody, signal)
}

export function fetchApprovalThresholds(
  companyId: string,
  signal?: AbortSignal,
): Promise<ViewOutcome<ApprovalThresholdsView>> {
  return readJson(APPROVAL_THRESHOLDS_PATH, companyId, (raw) => decodeApprovalThresholdsView(raw, companyId), signal)
}

export function transferApprovalThresholdBaseline(
  context: CommandContext,
  body: TransferBaselineBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', APPROVAL_THRESHOLDS_BASELINE_PATH, context, body, 204, noBody, signal)
}

export function recordApprovalThresholdVersion(
  context: CommandContext,
  currencyId: string,
  body: RecordApprovalThresholdBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', approvalThresholdVersionsPath(currencyId), context, body, 204, noBody, signal)
}
