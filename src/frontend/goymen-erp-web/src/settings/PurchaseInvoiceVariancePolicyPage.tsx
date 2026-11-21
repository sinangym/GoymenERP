import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt } from './useCommandAttempt'
import { fetchPurchaseInvoiceVariancePolicy, publishPurchaseInvoiceVariancePolicy } from './purchaseInvoiceVariancePolicyApi'
import { PURCHASE_INVOICE_VARIANCE_POLICY_PERMISSION, invoiceToleranceInput, type PurchaseInvoiceVariancePolicy, type PublishPurchaseInvoiceVariancePolicyBody } from './purchaseInvoiceVariancePolicyContract'
import { PURCHASE_INVOICE_VARIANCE_POLICY_TEXT as T } from './purchaseInvoiceVariancePolicyText'
import styles from './settings.module.css'

export function PurchaseInvoiceVariancePolicyPage() {
  const shell = useShellState(); const key = contextKey(shell); const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}::${shell.routeEntryGeneration}`} namespace={namespace} />
}
function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState(); const company = shell.activeContext?.companyId ?? null
  const single = shell.activeContext?.scopeMode === 'single' && company !== null
  const authorized = hasCapability(shell, PURCHASE_INVOICE_VARIANCE_POLICY_PERMISSION)
  const enabled = namespace !== null && single && authorized
  const query = useBoundaryRead([namespace, shell.routeEntryGeneration, 'purchase-invoice-variance-policy'], enabled,
    (signal) => fetchPurchaseInvoiceVariancePolicy(company ?? '', signal))
  const state = stateOf<PurchaseInvoiceVariancePolicy>(query.isPending, query.data)
  const [notice, setNotice] = useState<string | null>(null)
  return <Frame title={T.title} intro={T.intro}>{notice !== null && <p role="status">{notice}</p>}
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /> : !authorized ? <StateBlock state={{ kind: 'forbidden' }} subject={T.subject} />
      : state.kind === 'ready' ? <Policy key={state.data.version} data={state.data} finish={(message) => { setNotice(message); void query.refetch() }} />
      : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />}
  </Frame>
}
function Policy({ data, finish }: { readonly data: PurchaseInvoiceVariancePolicy; readonly finish: (message: string) => void }) {
  const [editing, setEditing] = useState(false)
  return <section>{data.current === null ? <p>{T.empty}</p> : <p>{T.current}: {data.version}; {T.amount}: {data.current.amountTolerance}; {T.rate}: {data.current.rateTolerance}</p>}
    {data.versions.length > 0 && <table className={styles.table}><caption>{T.history}</caption><thead><tr><th scope="col">{T.version}</th><th scope="col">{T.amount}</th><th scope="col">{T.rate}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead>
      <tbody>{data.versions.map((row) => <tr key={row.policyId}><td>{row.version}</td><td>{row.amountTolerance}</td><td>{row.rateTolerance}</td><td>{row.publishedBy}</td><td>{row.publishedAtUtc}</td><td>{row.reason}</td></tr>)}</tbody></table>}
    {editing ? <Editor data={data} cancel={() => setEditing(false)} finish={(message) => { setEditing(false); finish(message) }} />
      : <Button type="button" onClick={() => setEditing(true)}>{T.edit}</Button>}
  </section>
}
export function PurchaseInvoiceVariancePolicyEditor({ data, finish, cancel }: { readonly data: PurchaseInvoiceVariancePolicy; readonly finish: (message: string) => void; readonly cancel: () => void }) {
  return <Editor data={data} finish={finish} cancel={cancel} />
}
function Editor({ data, finish, cancel }: { readonly data: PurchaseInvoiceVariancePolicy; readonly finish: (message: string) => void; readonly cancel: () => void }) {
  const [amount, setAmount] = useState(data.current?.amountTolerance ?? '0'); const [rate, setRate] = useState(data.current?.rateTolerance ?? '0')
  const [reason, setReason] = useState(''); const [frozen, setFrozen] = useState<PublishPurchaseInvoiceVariancePolicyBody | null>(null)
  const attempt = useCommandAttempt<true>(); const boundary = useAuthBoundary(); const busy = attempt.status.kind === 'busy'
  const valid = invoiceToleranceInput(amount) && invoiceToleranceInput(rate) && reason.trim().length > 0 && reason.trim().length <= 4000
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (busy || (!valid && frozen === null)) return
    const payload = frozen ?? { expectedVersion: data.version, amountTolerance: amount, rateTolerance: rate, reason: reason.trim() }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) => publishPurchaseInvoiceVariancePolicy({ companyId: data.companyId, idempotencyKey }, payload))
    if (result.type === 'unknown') { setFrozen(payload); return }
    setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_purchase_invoice_variance_policy_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy}>
    <fieldset disabled={busy || frozen !== null}>
      <Field label={T.amount}>{(control) => <input {...control} required inputMode="decimal" maxLength={25} value={amount} onChange={(event) => setAmount(event.target.value)} />}</Field>
      <Field label={T.rate}>{(control) => <input {...control} required inputMode="decimal" maxLength={25} value={rate} onChange={(event) => setRate(event.target.value)} />}</Field>
      <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={4000} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>
      <p>{T.effect}</p>
    </fieldset>
    {attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}{attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.publish}</Button>
    <Button type="button" disabled={busy || frozen !== null} onClick={cancel}>{T.cancel}</Button>
  </form>
}
