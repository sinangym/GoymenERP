import { EditedAttemptNotice } from './EditedAttemptNotice'
import { useState } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt } from './useCommandAttempt'
import { fetchSubcontractTaxOptions, fetchSubcontractTaxPolicy, recordSubcontractTaxPolicy } from './subcontractTaxApi'
import { SUBCONTRACT_TAX_CAPABILITY, selectedSubcontractTax, type RecordSubcontractTaxBody, type SubcontractTaxOptions, type SubcontractTaxPolicy, type SubcontractTaxSide } from './subcontractTaxContract'
import { SubcontractTaxChoiceFields, emptySubcontractTaxChoice } from './SubcontractTaxChoiceFields'
import { SUBCONTRACT_TAX_TEXT as T } from './subcontractTaxText'
import styles from './settings.module.css'
export function SubcontractTaxDefaultPage({ side }: { readonly side: SubcontractTaxSide }) {
  const shell = useShellState(), context = contextKey(shell), namespace = context === null ? null : contextKeyString(context)
  return <Screen key={`${namespace}:${shell.routeEntryGeneration}:${side}`} side={side} namespace={namespace} />
}
function Screen({ side, namespace }: { readonly side: SubcontractTaxSide; readonly namespace: string | null }) {
  const shell = useShellState(), company = shell.activeContext?.companyId ?? ''
  const ready = namespace !== null && shell.activeContext?.scopeMode === 'single' && hasCapability(shell, SUBCONTRACT_TAX_CAPABILITY[side])
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'subcontract-tax', side], ready, signal => fetchSubcontractTaxPolicy(side, company, signal))
  const options = useBoundaryRead([namespace, shell.routeEntryGeneration, 'subcontract-tax-options', side], ready, signal => fetchSubcontractTaxOptions(side, company, signal))
  const state = stateOf<SubcontractTaxPolicy>(query.isPending || query.isFetching, query.data), source = stateOf<SubcontractTaxOptions>(options.isPending || options.isFetching, options.data)
  return <Frame title={T[side]} intro={T.intro}>{!ready ? <StateBlock subject={T[side]} state={{ kind: namespace === null ? 'no-authorized-company' : 'forbidden' }} />
    : state.kind !== 'ready' ? <StateBlock subject={T[side]} state={state} onRetry={() => void query.refetch()} />
      : source.kind !== 'ready' ? <StateBlock subject={T[side]} state={source} onRetry={() => void options.refetch()} />
        : state.data.version !== source.data.defaultVersion ? <p role="alert">{T.conflict}<Button type="button" onClick={() => { void query.refetch(); void options.refetch() }}>{T.retry}</Button></p>
          : <Editor key={state.data.version} side={side} data={state.data} options={source.data} changed={() => { void query.refetch(); void options.refetch() }} />}</Frame>
}
function Editor({ side, data, options, changed }: { readonly side: SubcontractTaxSide; readonly data: SubcontractTaxPolicy; readonly options: SubcontractTaxOptions; readonly changed: () => void }) {
  const [choice, setChoice] = useState(emptySubcontractTaxChoice), [reason, setReason] = useState(''), [invalid, setInvalid] = useState(false)
  const [frozen, setFrozen] = useState<RecordSubcontractTaxBody | null>(null), [notice, setNotice] = useState('')
  const boundary = useAuthBoundary(), attempt = useCommandAttempt<true>(), busy = attempt.status.kind === 'busy'
  const current = data.current
  async function submit() {
    const value = selectedSubcontractTax(options, choice)
    if (busy || frozen === null && (value === null || !reason.trim())) { setInvalid(true); return }
    const body = frozen ?? { ...value!, expectedVersion: data.version, reason: reason.trim() }
    setInvalid(false); const captured = boundary.capture()
    const result = await attempt.run(body, key => recordSubcontractTaxPolicy(side, { companyId: data.companyId, idempotencyKey: key }, body))
    if (result.type === 'unknown') { setFrozen(body); return }
    setFrozen(null)
    if (result.type === 'applied') { setNotice(result.replayed ? T.replay : T.saved); changed() }
    else { if (result.status === 401 || result.status === 403) boundary.report(result.status, captured); if (result.status === 409) changed() }
  }
  return <section>{notice && <p role="status">{notice}</p>}<h2>{T.current}</h2>{current === null ? <p>{T.empty}</p> : <p>{T.vat}: {current.vatPercent ?? current.exemptionCode ?? T.noSource}; {T.withholding}: {current.withholdingPercent ?? T.none} {current.withholdingCode}</p>}
    <form onSubmit={event => { event.preventDefault(); void submit() }}><fieldset disabled={busy || frozen !== null}>
      <SubcontractTaxChoiceFields options={options} value={choice} onChange={setChoice} />
      {options.vatRates.length === 0 && <p role="status">{T.noSource}</p>}
      <Field label={T.reason}>{p => <textarea {...p} required maxLength={4000} value={reason} onChange={e => setReason(e.target.value)} />}</Field>
    </fieldset>{invalid && <p role="alert">{T.invalid}</p>}{attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}<EditedAttemptNotice status={attempt.status} />{attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
      <Button type="submit" disabled={busy || options.vatRates.length === 0}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button></form>
    <table className={styles.table}><caption>{T.history}</caption><thead><tr><th scope="col">{T.version}</th><th scope="col">{T.vat}</th><th scope="col">{T.withholding}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead>
      <tbody>{data.versions.map(x => <tr key={x.version}><td>{x.version}</td><td>{x.value.vatPercent ?? x.value.exemptionCode ?? T.noSource}</td><td>{x.value.withholdingPercent ?? T.none} {x.value.withholdingCode}</td><td>{x.recordedBy}</td><td>{x.recordedAtUtc}</td><td>{x.reason}</td></tr>)}</tbody></table>
  </section>
}
