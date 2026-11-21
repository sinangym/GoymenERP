/**
 * KIM-D34 client — reads and records the group-wide login attempt limit through the shared Settings transport
 * (`readJson` / `send`). The write is a 204: reaching the exact status IS the answer, and the screen reads the view again
 * afterwards. The company header names the company the person works in (the server authorizes there and records it in the
 * audit trail); the value itself is group-wide. Cookie writes get the antiforgery header from the shared fetch wrapper.
 */

import type { ViewOutcome } from '../shell/uiState'
import { noBody, readJson, send, type CommandContext, type CommandOutcome } from './discountLimitApi'
import {
  LOGIN_RATE_LIMIT_PATH,
  LOGIN_RATE_LIMIT_VERSIONS_PATH,
  decodeLoginRateLimitView,
  type LoginRateLimitView,
  type RecordLoginRateLimitBody,
} from './loginRateLimitContract'

export function fetchLoginRateLimit(companyId: string, signal?: AbortSignal): Promise<ViewOutcome<LoginRateLimitView>> {
  return readJson(LOGIN_RATE_LIMIT_PATH, companyId, decodeLoginRateLimitView, signal)
}

export function recordLoginRateLimitVersion(
  context: CommandContext,
  body: RecordLoginRateLimitBody,
  signal?: AbortSignal,
): Promise<CommandOutcome<true>> {
  return send<true>('POST', LOGIN_RATE_LIMIT_VERSIONS_PATH, context, body, 204, noBody, signal)
}
