export const FINANCE_POLICY_PATH = '/settings/finance-policies'
export const FINANCE_POLICY_CAPS = { prepare: 'settings.finance-policies.prepare', publish: 'settings.finance-policies.publish', publishCritical: 'settings.finance-policies.publish-critical' } as const
export type FinancePolicyFamily = 'approval-matrices' | 'cash-count-differences'
export interface FinancePolicyRole { readonly roleId: string; readonly code: string; readonly name: string; readonly sourceVersion: string }
export interface FinancePolicyCash { readonly cashAccountId: string; readonly currencyId: string; readonly name: string; readonly version: string }
export interface FinanceApprovalRule { readonly ruleId: string; readonly operationPermission: string; readonly currencyId: string; readonly approverRoleId: string; readonly approverRoleCode: string; readonly roleSourceVersion: string; readonly approvalStep: 1 | 2; readonly amountThreshold: string }
export interface FinancePolicyHeader { readonly companyId: string; readonly policyId: string; readonly version: number; readonly stateVersion: number; readonly state: 'Draft' | 'Published' | 'Retired'; readonly validFrom: string; readonly validThrough: string | null; readonly retiredFrom: string | null; readonly preparedBy: string; readonly preparedAtUtc: string; readonly publishedBy: string | null; readonly publishedAtUtc: string | null; readonly reason: string }
export interface FinanceApprovalPolicy extends FinancePolicyHeader { readonly rules: readonly FinanceApprovalRule[] }
export interface CashDifferencePolicy extends FinancePolicyHeader { readonly cashAccountId: string; readonly cashAccountVersion: string; readonly currencyId: string; readonly amountThreshold: string }
export interface FinancePolicyWorkspace { readonly companyId: string; readonly businessDate: string; readonly references: { readonly companyId: string; readonly operations: readonly { readonly permission: string; readonly name: string }[]; readonly roles: readonly FinancePolicyRole[]; readonly cashAccounts: readonly FinancePolicyCash[]; readonly currencies: readonly { readonly currencyId: string; readonly code: string }[] }; readonly approvalPolicies: readonly FinanceApprovalPolicy[]; readonly cashCountDifferencePolicies: readonly CashDifferencePolicy[] }
const object = (raw: unknown): raw is Record<string, unknown> => raw !== null && typeof raw === 'object' && !Array.isArray(raw)
const exact = (raw: Record<string, unknown>, keys: readonly string[]) => Object.keys(raw).length === keys.length && keys.every(k => Object.hasOwn(raw, k))
const guid = (x: unknown): x is string => typeof x === 'string' && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(x) && x !== '00000000-0000-0000-0000-000000000000'
const text = (x: unknown): x is string => typeof x === 'string' && x.trim().length > 0
const integer = (x: unknown): x is number => typeof x === 'number' && Number.isSafeInteger(x) && x > 0
const version = (x: unknown): x is string => typeof x === 'string' && /^[1-9][0-9]*$/.test(x)
const date = (x: unknown): x is string => typeof x === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(x) && !x.startsWith('0000') && Number.isFinite(Date.parse(x)) && new Date(x).toISOString().slice(0, 10) === x
const utc = (x: unknown): x is string => typeof x === 'string' && /(?:Z|\+00:00)$/.test(x) && Number.isFinite(Date.parse(x))
const nullable = (x: unknown, check: (value: unknown) => boolean) => x === null || check(x)
const amount = (x: unknown): x is string => typeof x === 'string' && x.length <= 64 && /^(?:0|[1-9][0-9]*)(?:\.[0-9]*[1-9])?$/.test(x)
const unique = (items: readonly unknown[], key: (raw: unknown) => unknown) => new Set(items.map(key)).size === items.length
const headerKeys = ['companyId', 'policyId', 'version', 'stateVersion', 'state', 'validFrom', 'validThrough', 'retiredFrom', 'preparedBy', 'preparedAtUtc', 'publishedBy', 'publishedAtUtc', 'reason']
function header(raw: Record<string, unknown>, company: string) {
  if (raw.companyId !== company || !guid(raw.policyId) || !integer(raw.version) || !integer(raw.stateVersion) || !date(raw.validFrom)
    || !nullable(raw.validThrough, date) || (typeof raw.validThrough === 'string' && raw.validThrough < raw.validFrom)
    || !nullable(raw.retiredFrom, date) || !text(raw.preparedBy) || !utc(raw.preparedAtUtc) || !text(raw.reason)) return false
  return raw.state === 'Draft' ? raw.stateVersion === 1 && raw.publishedBy === null && raw.publishedAtUtc === null && raw.retiredFrom === null
    : (raw.state === 'Published' && raw.stateVersion === 2 && raw.retiredFrom === null || raw.state === 'Retired' && raw.stateVersion === 3 && date(raw.retiredFrom)) && text(raw.publishedBy) && utc(raw.publishedAtUtc)
}
function rule(raw: unknown): raw is FinanceApprovalRule {
  return object(raw) && exact(raw, ['ruleId', 'operationPermission', 'currencyId', 'approverRoleId', 'approverRoleCode', 'roleSourceVersion', 'approvalStep', 'amountThreshold'])
    && guid(raw.ruleId) && text(raw.operationPermission) && raw.operationPermission.startsWith('finance.') && guid(raw.currencyId) && guid(raw.approverRoleId)
    && text(raw.approverRoleCode) && text(raw.roleSourceVersion) && (raw.approvalStep === 1 || raw.approvalStep === 2) && amount(raw.amountThreshold)
}
export function decodeFinancePolicies(raw: unknown, company: string): FinancePolicyWorkspace | null {
  if (!object(raw) || !exact(raw, ['companyId', 'businessDate', 'references', 'approvalPolicies', 'cashCountDifferencePolicies']) || raw.companyId !== company || !date(raw.businessDate)
    || !object(raw.references) || !exact(raw.references, ['companyId', 'operations', 'roles', 'cashAccounts', 'currencies']) || raw.references.companyId !== company) return null
  const r = raw.references
  if (!Array.isArray(r.operations) || !r.operations.every(x => object(x) && exact(x, ['permission', 'name']) && text(x.permission) && x.permission.startsWith('finance.') && text(x.name)) || !unique(r.operations, x => (x as Record<string, unknown>).permission)
    || !Array.isArray(r.roles) || !r.roles.every(x => object(x) && exact(x, ['roleId', 'code', 'name', 'sourceVersion']) && guid(x.roleId) && text(x.code) && text(x.name) && text(x.sourceVersion)) || !unique(r.roles, x => (x as Record<string, unknown>).roleId)
    || !Array.isArray(r.cashAccounts) || !r.cashAccounts.every(x => object(x) && exact(x, ['cashAccountId', 'currencyId', 'name', 'version']) && guid(x.cashAccountId) && guid(x.currencyId) && text(x.name) && version(x.version)) || !unique(r.cashAccounts, x => (x as Record<string, unknown>).cashAccountId)
    || !Array.isArray(r.currencies) || !r.currencies.every(x => object(x) && exact(x, ['currencyId', 'code']) && guid(x.currencyId) && text(x.code)) || !unique(r.currencies, x => (x as Record<string, unknown>).currencyId)) return null
  if (!Array.isArray(raw.approvalPolicies) || !raw.approvalPolicies.every(x => object(x) && exact(x, [...headerKeys, 'rules']) && header(x, company) && Array.isArray(x.rules) && x.rules.length > 0 && x.rules.every(rule) && unique(x.rules, y => (y as FinanceApprovalRule).ruleId))
    || !unique(raw.approvalPolicies, x => (x as Record<string, unknown>).policyId) || !unique(raw.approvalPolicies, x => (x as Record<string, unknown>).version)
    || !Array.isArray(raw.cashCountDifferencePolicies) || !raw.cashCountDifferencePolicies.every(x => object(x) && exact(x, [...headerKeys, 'cashAccountId', 'cashAccountVersion', 'currencyId', 'amountThreshold']) && header(x, company) && guid(x.cashAccountId) && guid(x.currencyId) && version(x.cashAccountVersion) && amount(x.amountThreshold)) || !unique(raw.cashCountDifferencePolicies, x => (x as Record<string, unknown>).policyId)) return null
  return raw as unknown as FinancePolicyWorkspace
}
export interface FinancePolicyPreview { readonly outcome: 'Available' | 'Unavailable'; readonly policyId: string | null; readonly version: number | null; readonly matchingRules?: readonly FinanceApprovalRule[]; readonly exceedsThreshold?: boolean | null; readonly reason: string | null }
export function decodeFinancePolicyPreview(raw: unknown, family: FinancePolicyFamily): FinancePolicyPreview | null {
  const matrix = family === 'approval-matrices'
  if (!object(raw) || !exact(raw, ['outcome', 'policyId', 'version', matrix ? 'matchingRules' : 'exceedsThreshold', 'reason'])) return null
  if (matrix ? !Array.isArray(raw.matchingRules) || !raw.matchingRules.every(rule) : raw.exceedsThreshold !== null && typeof raw.exceedsThreshold !== 'boolean') return null
  if (raw.outcome === 'Available' ? !guid(raw.policyId) || !integer(raw.version) || raw.reason !== null || !matrix && raw.exceedsThreshold === null
    : raw.outcome !== 'Unavailable' || raw.policyId !== null || raw.version !== null || !text(raw.reason) || (matrix ? (raw.matchingRules as unknown[]).length !== 0 : raw.exceedsThreshold !== null)) return null
  return raw as unknown as FinancePolicyPreview
}
/** Shape validation only. These helpers do not calculate money, rates, maturity or policy precedence in the UI. */
export const financePolicyShape = { object, exact, guid, text, integer, version, date, utc, nullable, amount, unique, header, headerKeys }
