import { API_BASE, readJson, type CommandOutcome } from './discountLimitApi'
import { WORK_APPROVAL_RULES_PATH, decodeWorkApprovalRuleTable, decodeWorkApprovalRule, type WorkApprovalRule, type WorkApprovalRuleClass } from './workApprovalRulesContract'

export const fetchWorkApprovalRules = (company: string, signal?: AbortSignal) =>
  readJson(WORK_APPROVAL_RULES_PATH, company, (raw) => decodeWorkApprovalRuleTable(raw, company), signal)

export interface RecordWorkApprovalRule {
  readonly expectedVersion: string
  readonly durationClass: WorkApprovalRuleClass
  readonly escalationPermission: string | null
  readonly reason: string
}

export function isRecordWorkApprovalRule(raw: unknown): raw is RecordWorkApprovalRule {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return false
  const v = raw as Record<string, unknown>
  return Object.keys(v).length === 4 && ['expectedVersion', 'durationClass', 'escalationPermission', 'reason'].every(k => Object.hasOwn(v,k))
    && typeof v.expectedVersion === 'string' && /^(0|[1-9][0-9]{0,18})$/.test(v.expectedVersion) && BigInt(v.expectedVersion) <= 9223372036854775807n
    && (v.durationClass === 'Hours' || v.durationClass === 'BusinessDays')
    && (v.escalationPermission === null || typeof v.escalationPermission === 'string' && v.escalationPermission.length <= 128 && /^[a-z][a-z0-9]*(?:[-_][a-z0-9]+)*(?:\.[a-z0-9]+(?:[-_][a-z0-9]+)*)+$/.test(v.escalationPermission))
    && typeof v.reason === 'string' && v.reason.trim().length > 0 && v.reason.length <= 500
}

/** The owner endpoint returns a rule (no company field); company scope is carried by the request header. */
export async function recordWorkApprovalRule(company: string, kind: string, body: RecordWorkApprovalRule, key: string): Promise<CommandOutcome<WorkApprovalRule>> {
  const unexpected = { type: 'unknown', reason: 'unexpected' } as const
  if (!isRecordWorkApprovalRule(body)) return unexpected
  let response: Response
  try {
    response = await fetch(`${API_BASE}${WORK_APPROVAL_RULES_PATH}/${encodeURIComponent(kind)}`, {
      method: 'PUT', headers: {
        ...((import.meta.env.DEV || import.meta.env.VITE_BOOTSTRAP_IDENTITY_ENABLED === 'true')
          ? { 'X-Development-User': (import.meta.env.VITE_DEV_USER as string | undefined) ?? 'company-a-user' } : {}),
        'X-Company-Id': company, 'Content-Type': 'application/json', 'Idempotency-Key': key,
      }, body: JSON.stringify(body),
    })
  } catch { return { type: 'unknown', reason: navigator.onLine ? 'network-error' : 'offline' } }
  const replay = response.headers.get('Idempotency-Replayed')
  const original = response.headers.get('Idempotency-Original-Correlation-Id')
  // Only the endpoint's exact header pair is a complete replay receipt.
  if (replay !== null && replay !== 'true' || (replay === 'true') !== (original !== null && original.trim().length > 0)) return unexpected
  let raw: unknown = null
  try { raw = await response.json() } catch { /* Malformed success remains unresolved. */ }
  if (response.status === 200) {
    const row = decodeWorkApprovalRule(raw)
    // Replays may return a newer current view; never replace the frozen expected version with it during retry.
    return row !== null && row.kind === kind && BigInt(row.version) > BigInt(body.expectedVersion)
      ? { type: 'applied', data: row, replayed: replay === 'true' } : unexpected
  }
  if (response.status >= 500) return { type: 'unknown', reason: 'server' }
  if (![400,401,403,404,409,415,422].includes(response.status)) return unexpected
  const code = typeof raw === 'object' && raw !== null && 'code' in raw && typeof raw.code === 'string' ? raw.code : null
  return { type: 'refused', status: response.status, code, companyId: null, groupCode: null, replayed: replay === 'true' }
}
