import { useState } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { stateOf, useBoundaryRead } from '../settings/SalesPolicyParts'
import { useCommandAttempt } from '../settings/useCommandAttempt'
import { type DraftDetail, type DraftMutation } from './contract'
import { SALES_CAPABILITY } from './capabilities'
import { LineTaxFields } from './SalesTaxFields'
import { KEEP_LINE_TAX, buildLineTaxChoice } from './orderTax'
import { fetchSalesSubcontractTaxOptions, reviseSalesSubcontractTax, type SubcontractTaxOptions, type SubcontractTaxRevision } from './subcontractTax'
import { SUBCONTRACT_TAX_TEXT as T } from '../settings/subcontractTaxText'
export function SalesSubcontractTaxRevision({ detail, onRecorded }: { readonly detail: DraftDetail; readonly onRecorded: () => void }) {
  const shell = useShellState(), key = contextKey(shell), namespace = key === null ? null : contextKeyString(key)
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const enabled = namespace !== null && company === detail.draft.companyId && detail.draft.subcontractService === true && hasCapability(shell, SALES_CAPABILITY.ordersApprove)
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'subcontract-tax', detail.draft.id, detail.draft.version], enabled,
    signal => fetchSalesSubcontractTaxOptions(company ?? '', detail, signal))
  if (!enabled) return null
  const state = stateOf<SubcontractTaxOptions>(query.isPending || query.isFetching, query.data)
  return <section><h2>{T.taxRevision}</h2>{state.kind !== 'ready' ? <StateBlock state={state} subject={T.sales} onRetry={() => void query.refetch()} /> :
    <Editor key={`${namespace}:${shell.routeEntryGeneration}:${detail.draft.version}`} company={company!} detail={detail} source={state.data} onRecorded={onRecorded} />}</section>
}
function Editor({ company, detail, source, onRecorded }: { readonly company: string; readonly detail: DraftDetail; readonly source: SubcontractTaxOptions; readonly onRecorded: () => void }) {
  const [line, setLine] = useState(''), [choice, setChoice] = useState({ ...KEEP_LINE_TAX, mode: 'default' as 'default' | 'override', buyerSituation: source.buyerSituation })
  const [reason, setReason] = useState(''), [invalid, setInvalid] = useState(false), [frozen, setFrozen] = useState<SubcontractTaxRevision | null>(null)
  const boundary = useAuthBoundary(), attempt = useCommandAttempt<DraftMutation>(), busy = attempt.status.kind === 'busy'
  async function submit() {
    const built = buildLineTaxChoice(choice), auditReason = choice.mode === 'override' ? choice.reason.trim() : reason.trim()
    if (busy || frozen === null && (!built.ok || built.tax === null || !detail.lines.some(x => x.lineId === line) || !auditReason)) { setInvalid(true); return }
    const body = frozen ?? { expectedVersion: source.version, lineId: line, tax: built.ok ? built.tax! : { mode: 'Default' as const }, reason: auditReason }
    setInvalid(false); const captured = boundary.capture()
    const result = await attempt.run(body, key => reviseSalesSubcontractTax({ companyId: company, idempotencyKey: key }, detail, body))
    if (result.type === 'unknown') { setFrozen(body); return }
    setFrozen(null)
    if (result.type === 'applied') onRecorded()
    else { if (result.status === 401 || result.status === 403) boundary.report(result.status, captured); if (result.status === 409) onRecorded() }
  }
  return <form onSubmit={event => { event.preventDefault(); void submit() }}><p>{T.revisionHelp}</p><fieldset disabled={busy || frozen !== null}>
    <Field label={T.change}>{p => <select {...p} required value={line} onChange={e => setLine(e.target.value)}><option value="">{T.choose}</option>
      {detail.lines.map(x => <option key={x.lineId} value={x.lineId}>{x.productCode} — {x.productName}</option>)}</select>}</Field>
    <LineTaxFields allowKeep={false} idPrefix={`subcontract-${detail.draft.id}`} value={choice} options={{ ...source.options, itemTypes: source.options.itemTypes.filter(x => x === 'Service') }}
      onChange={value => { if (value.mode !== 'keep') setChoice({ ...value, mode: value.mode }) }} />
    {choice.mode === 'default' && <Field label={T.reason}>{p => <textarea {...p} required maxLength={4000} value={reason} onChange={e => setReason(e.target.value)} />}</Field>}
  </fieldset>{invalid && <p role="alert">{T.invalid}</p>}{attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}{attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" disabled={busy}>{busy ? T.pending : frozen === null ? T.save : T.retry}</Button></form>
}
