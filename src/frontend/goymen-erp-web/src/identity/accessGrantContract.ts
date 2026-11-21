import { isWellFormedId } from './capabilities'
export const ACCESS_ASSIGNMENTS_PATH = '/identity/access/assignments'
export const ACCESS_REQUESTS_PATH = '/identity/access/grant-requests'
export interface AccessGrantPerson { readonly userId: string; readonly displayName: string }
export interface AccessGrantRequest { readonly requestId: string; readonly requestedBy: AccessGrantPerson; readonly target: AccessGrantPerson; readonly roleId: string; readonly roleCode: string; readonly roleName: string; readonly status: 'Pending'; readonly requestedAtUtc: string; readonly validFromUtc: string | null; readonly validUntilUtc: string | null; readonly reason: string | null; readonly version: string }
export interface AccessGrantOutcome { readonly id: string; readonly version: string; readonly status: string; readonly roleAssignmentId: string | null }
export interface AssignAccessBody { readonly userId: string; readonly roleId: string; readonly validFromUtc: string | null; readonly validUntilUtc: string | null; readonly reason: string | null }
export type AccessGrantAction = 'assign' | 'request' | 'approve' | 'reject' | 'withdraw' | 'revoke'
const record = (raw: unknown): raw is Record<string, unknown> => typeof raw === 'object' && raw !== null && !Array.isArray(raw)
const text = (raw: unknown): raw is string => typeof raw === 'string' && raw.trim().length > 0
export const accessGrantVersion = (raw: unknown): raw is string => typeof raw === 'string' && raw.length <= 19 && /^[1-9]\d*$/.test(raw) && BigInt(raw) <= 9223372036854775807n
const instant = (raw: unknown): raw is string => typeof raw === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|[+-]\d\d:\d\d)$/.test(raw) && Number.isFinite(Date.parse(raw))
const nullableInstant = (raw: unknown): raw is string | null => raw === null || instant(raw)
const person = (raw: unknown): AccessGrantPerson | null => record(raw) && isWellFormedId(raw.userId as string) && text(raw.displayName) ? { userId: raw.userId as string, displayName: raw.displayName } : null

export function decodeAccessGrantRequests(raw: unknown): readonly AccessGrantRequest[] | null {
  if (!Array.isArray(raw)) return null
  const result: AccessGrantRequest[] = []
  for (const item of raw) {
    if (!record(item) || !isWellFormedId(item.requestId as string) || !isWellFormedId(item.roleId as string) || !text(item.roleCode) || !text(item.roleName)
      || item.status !== 'Pending' || !instant(item.requestedAtUtc) || !nullableInstant(item.validFromUtc) || !nullableInstant(item.validUntilUtc)
      || (item.reason !== null && !text(item.reason)) || !accessGrantVersion(item.version) || result.some((entry) => entry.requestId === item.requestId)) return null
    const requestedBy = person(item.requestedBy), target = person(item.target)
    if (requestedBy === null || target === null) return null
    result.push({ requestId: item.requestId as string, roleId: item.roleId as string, roleCode: item.roleCode, roleName: item.roleName, requestedBy, target,
      status: 'Pending', requestedAtUtc: item.requestedAtUtc, validFromUtc: item.validFromUtc, validUntilUtc: item.validUntilUtc, reason: item.reason as string | null, version: item.version })
  }
  return result
}

export function decodeAccessGrantOutcome(raw: unknown): AccessGrantOutcome | null {
  if (!record(raw) || !isWellFormedId(raw.id as string) || !accessGrantVersion(raw.version)
    || typeof raw.status !== 'string' || !['Active', 'Revoked', 'Pending', 'Approved', 'Rejected', 'Withdrawn'].includes(raw.status)
    || (raw.roleAssignmentId !== null && !isWellFormedId(raw.roleAssignmentId as string))) return null
  return { id: raw.id as string, version: raw.version, status: raw.status, roleAssignmentId: raw.roleAssignmentId as string | null }
}
