import type { FinancePolicyFamily } from './financePolicyContract'
type Family = FinancePolicyFamily | 'bank-institutions' | 'collateral-rates'
export interface PendingFinancePolicy { readonly key: string; readonly suffix: string; readonly body: object }
const pending = new Map<string, PendingFinancePolicy>()
export const financePolicyPendingKey = (epoch: number, actor: string | null, company: string | null, family: Family) => actor === null || company === null ? null : JSON.stringify([epoch, actor, company, family])
export const recallFinancePolicy = (scope: string | null) => scope === null ? null : pending.get(scope) ?? null
export const reserveFinancePolicy = (scope: string, command: PendingFinancePolicy) => { pending.set(scope, command) }
export const releaseFinancePolicy = (scope: string, key: string) => { if (pending.get(scope)?.key === key) pending.delete(scope) }
export const clearPendingFinancePolicies = () => { pending.clear() }
