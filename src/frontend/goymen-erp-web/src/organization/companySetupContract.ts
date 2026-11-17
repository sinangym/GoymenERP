import { isWellFormedId } from '../identity/capabilities'
import { decodeAccessGrantOutcome, type AccessGrantOutcome } from '../identity/accessGrantContract'

export const COMPANY_SETUP_PATH = '/kurulum/sirketler'
export const isSetupId = (value: unknown): value is string => isWellFormedId(value as string) && value !== '00000000-0000-0000-0000-000000000000'
export type CompanySetupCapabilities = { readonly canOpenCompany: boolean; readonly canAssignFirstAdmin: boolean }
export type SetupCompany = { readonly companyId: string; readonly code: string; readonly legalName: string; readonly lifecycleState: 'SetupPending'; readonly version: number }
export type AdminCandidate = { readonly userId: string; readonly displayName: string }
export type OpenCompanyRequest = { readonly companyId: string; readonly code: string; readonly legalName: string; readonly vkn: string; readonly expectedVersion: '0'; readonly reason: string }
export type SelectFirstAdminRequest = { readonly userId: string; readonly expectedCompanyVersion: number; readonly reason: string }
export const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const exact = (value: Record<string, unknown>, fields: readonly string[]) => Object.keys(value).length === fields.length && fields.every(field => Object.hasOwn(value, field))
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
export function decodeSetupCapabilities(value: unknown): CompanySetupCapabilities | null {
  return record(value) && exact(value, ['canOpenCompany', 'canAssignFirstAdmin']) && typeof value.canOpenCompany === 'boolean' && typeof value.canAssignFirstAdmin === 'boolean'
    ? { canOpenCompany: value.canOpenCompany, canAssignFirstAdmin: value.canAssignFirstAdmin } : null
}
export function decodeSetupCompany(value: unknown): SetupCompany | null {
  if (!record(value) || !exact(value, ['companyId', 'code', 'legalName', 'lifecycleState', 'version']) || !isSetupId(value.companyId)
    || !text(value.code) || !text(value.legalName) || value.lifecycleState !== 'SetupPending' || !Number.isSafeInteger(value.version) || (value.version as number) < 1) return null
  return { companyId: value.companyId as string, code: value.code, legalName: value.legalName, lifecycleState: 'SetupPending', version: value.version as number }
}
export function decodeSetupCompanies(value: unknown): readonly SetupCompany[] | null {
  if (!Array.isArray(value)) return null
  const rows = value.map(decodeSetupCompany)
  return rows.some(row => row === null) || new Set(rows.map(row => row?.companyId)).size !== rows.length ? null : rows as SetupCompany[]
}
export function decodeAdminCandidates(value: unknown): readonly AdminCandidate[] | null {
  if (!Array.isArray(value)) return null
  const rows: AdminCandidate[] = []
  for (const row of value) {
    if (!record(row) || !exact(row, ['userId', 'displayName']) || !isSetupId(row.userId) || !text(row.displayName)
      || rows.some(existing => existing.userId === row.userId)) return null
    rows.push({ userId: row.userId as string, displayName: row.displayName })
  }
  return rows
}
export function decodeFirstAdminSelected(value: unknown): AccessGrantOutcome | null {
  if (!record(value) || !exact(value, ['id', 'version', 'status', 'roleAssignmentId'])) return null
  const answer = decodeAccessGrantOutcome(value)
  return answer !== null && isSetupId(answer.id) && answer.status === 'Active' && answer.roleAssignmentId === answer.id ? answer : null
}
