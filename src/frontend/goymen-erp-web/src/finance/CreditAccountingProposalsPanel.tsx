import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { API_BASE, devHeaders } from './http'
import { isBoundUuid, isInstant } from './contract'
import { isDateOnly } from './dateOnly'
import { isNonNegativeDecimalText } from './decimalText'
import { CREDIT_MOVEMENT_TEXT as T, CREDIT_POSTING_TEXT as P } from './creditMovementText'

interface CreditPostingProposal {
  readonly proposalId: string; readonly companyId: string; readonly status: 'AwaitingMapping'; readonly mappingStatus: 'MappingMissing' | 'MappingDetailsPending'
  readonly mappingVersion: number | null; readonly waitingReasons: readonly string[]; readonly receivedAtUtc: string
  readonly source: { readonly eventId: string; readonly companyId: string; readonly movementId: string; readonly creditAccountId: string; readonly currencyId: string
    readonly kind: keyof typeof T.kinds; readonly sourceDocument: { readonly reference: string; readonly documentDate: string; readonly revision: string }
    readonly transactionDate: string; readonly accountingDate: string; readonly reversesMovementId: string | null
    readonly approvedBy: string; readonly reason: string
    readonly bankEffect: { readonly bankAccountId: string; readonly amount: string; readonly sourceReference: string } | null
    readonly components: readonly { readonly kind: keyof typeof T.components; readonly amount: string; readonly functionalAmount: string | null }[] }
}
const record = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.trim() !== ''
const date = (value: unknown) => typeof value === 'string' && isDateOnly(value)
const reasons = P.reasons
export function decodeCreditPostingProposals(value: unknown, company: string): readonly CreditPostingProposal[] | null {
  if (!record(value) || value.companyId !== company || !Array.isArray(value.items)) return null
  const ids = new Set<string>()
  for (const row of value.items) {
    if (!record(row) || !isBoundUuid(row.proposalId) || ids.has(row.proposalId) || row.companyId !== company || row.status !== 'AwaitingMapping'
      || !['MappingMissing', 'MappingDetailsPending'].includes(row.mappingStatus as string) || !isInstant(row.receivedAtUtc)
      || !Array.isArray(row.waitingReasons) || row.waitingReasons.length === 0 || row.waitingReasons.some(x => typeof x !== 'string' || !Object.hasOwn(reasons, x))) return null
    if (row.mappingStatus === 'MappingMissing' ? row.mappingVersion !== null : !Number.isSafeInteger(row.mappingVersion) || (row.mappingVersion as number) < 1) return null
    const source = row.source
    if (!record(source) || source.companyId !== company || source.eventId !== row.proposalId || !isBoundUuid(source.movementId)
      || !isBoundUuid(source.creditAccountId) || !isBoundUuid(source.currencyId) || typeof source.kind !== 'string' || !Object.hasOwn(T.kinds, source.kind)
      || !record(source.sourceDocument) || !text(source.sourceDocument.reference) || !date(source.sourceDocument.documentDate) || !text(source.sourceDocument.revision)
      || !date(source.transactionDate) || !date(source.accountingDate) || source.reversesMovementId !== null && source.reversesMovementId !== source.movementId
      || !text(source.approvedBy) || !text(source.reason) || source.reversesMovementId !== null && source.kind !== 'OpeningBalance'
      || !Array.isArray(source.components) || source.components.length === 0 || source.components.some(x => !record(x) || typeof x.kind !== 'string'
        || !Object.hasOwn(T.components, x.kind) || !isNonNegativeDecimalText(x.amount) || x.functionalAmount !== null && !isNonNegativeDecimalText(x.functionalAmount))) return null
    if (new Set(source.components.map(x => x.kind)).size !== source.components.length) return null
    if (source.bankEffect !== null && (!record(source.bankEffect) || !isBoundUuid(source.bankEffect.bankAccountId)
      || !isNonNegativeDecimalText(source.bankEffect.amount) || !/[1-9]/.test(source.bankEffect.amount)
      || !text(source.bankEffect.sourceReference) || !['Drawdown', 'Repayment'].includes(source.kind))) return null
    ids.add(row.proposalId)
  }
  return value.items as unknown as CreditPostingProposal[]
}
async function read(company: string, signal: AbortSignal): Promise<ViewOutcome<readonly CreditPostingProposal[]>> {
  try { const response = await fetch(`${API_BASE}/accounting/credit-movement-proposals`, { headers: { ...devHeaders(), 'X-Company-Id': company }, cache: 'no-store', signal })
    const body: unknown = await response.json().catch(() => null)
    if (response.status !== 200) return { type: 'http', status: response.status, body }
    const data = decodeCreditPostingProposals(body, company); return data === null ? { type: 'malformed' } : { type: 'ok-single', data }
  } catch { return signal.aborted ? { type: 'fence-stale' } : navigator.onLine ? { type: 'network-error' } : { type: 'offline' } }
}
export function CreditAccountingProposalsPanel({ company, accountId, namespace }: { readonly company: string; readonly accountId: string; readonly namespace: string }) {
  const [opened, setOpened] = useState(false)
  const query = useQuery({ queryKey: [namespace, 'credit-accounting-proposals'], enabled: opened, queryFn: ({ signal }) => read(company, signal) })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<readonly CreditPostingProposal[]>
  const rows = state.kind === 'ready' ? state.data.filter(x => x.source.creditAccountId === accountId) : []
  return <section><button onClick={() => { setOpened(true); if (opened) void query.refetch() }}>{P.show}</button>
    {opened ? state.kind !== 'ready' ? <StateBlock state={state} subject={P.subject} onRetry={() => void query.refetch()} /> : rows.length === 0
      ? <p>{P.empty}</p> : rows.map(row => <article key={row.proposalId}>
        <h4>{T.kinds[row.source.kind]} · {row.source.sourceDocument.reference}</h4><p>{P.waiting}</p>
        {row.source.reversesMovementId ? <p>{P.reversal}</p> : null}
        <ul>{row.waitingReasons.map(code => <li key={code}>{reasons[code as keyof typeof reasons]}</li>)}</ul>
        <ul>{row.source.components.map(part => <li key={part.kind}>{T.components[part.kind]}: {part.amount} · {P.functional}: {part.functionalAmount ?? T.unknownTl}</li>)}</ul>
      </article>) : null}
  </section>
}
