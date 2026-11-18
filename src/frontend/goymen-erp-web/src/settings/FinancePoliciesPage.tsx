import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { fetchFinancePolicies, commandFinancePolicy, previewFinancePolicy } from './financePolicyApi'
import { FINANCE_POLICY_CAPS as C, type FinanceApprovalRule, type FinancePolicyFamily, type FinancePolicyHeader, type FinancePolicyPreview, type FinancePolicyWorkspace } from './financePolicyContract'
import { financePolicyPendingKey, recallFinancePolicy, reserveFinancePolicy, releaseFinancePolicy } from './pendingFinancePolicies'
import { FINANCE_POLICY_TEXT as T } from './financePolicyText'
import styles from './settings.module.css'

export function FinanceApprovalPoliciesPage() { return <FinancePoliciesPage family="approval-matrices" /> }
export function CashCountDifferencePoliciesPage() { return <FinancePoliciesPage family="cash-count-differences" /> }
export function FinancePoliciesPage({ family }: { readonly family: FinancePolicyFamily }) {
  const shell = useShellState(); const key = contextKey(shell); const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}:${shell.routeEntryGeneration}:${family}`} namespace={namespace} family={family} />
}
function Screen({ namespace, family }: { readonly namespace: string | null; readonly family: FinancePolicyFamily }) {
  const shell = useShellState(); const company = shell.activeContext?.companyId ?? null
  const single = company !== null && shell.activeContext?.scopeMode === 'single'
  const allowed = namespace !== null && single && hasCapability(shell, C.prepare)
  const read = useBoundaryRead([namespace, shell.routeEntryGeneration, family, 'finance-policy-workspace'], allowed, signal => fetchFinancePolicies(company ?? '', signal))
  const state = stateOf<FinancePolicyWorkspace>(read.isPending, read.data)
  return <Frame title={family === 'approval-matrices' ? T.matrix : T.cash} intro={T.intro}>
    {!single ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /> : !allowed ? <StateBlock state={{ kind: 'forbidden' }} subject={T.subject} />
      : state.kind === 'ready' ? <Workspace key={`${family}:${state.data.companyId}`} data={state.data} family={family} changed={() => void read.refetch()} />
        : <StateBlock state={state} subject={T.subject} onRetry={() => void read.refetch()} />}
  </Frame>
}
type RuleInput = Omit<FinanceApprovalRule, 'ruleId' | 'approverRoleCode' | 'roleSourceVersion'> & { readonly expectedRoleSourceVersion: string }
function Workspace({ data, family, changed }: { readonly data: FinancePolicyWorkspace; readonly family: FinancePolicyFamily; readonly changed: () => void }) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const matrix = family === 'approval-matrices'
  const identity = financePolicyPendingKey(shell.sessionEpoch, shell.activeContext?.userId ?? null, data.companyId, family)
  const alive = useRef(true); useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  const [held, setHeld] = useState(() => recallFinancePolicy(identity)); const [busy, setBusy] = useState(false); const [notice, setNotice] = useState<string | null>(null)
  const [from, setFrom] = useState(data.businessDate); const [through, setThrough] = useState(''); const [reason, setReason] = useState('')
  const [operation, setOperation] = useState(''); const [currency, setCurrency] = useState(''); const [role, setRole] = useState(''); const [step, setStep] = useState<1 | 2>(1)
  const [amount, setAmount] = useState(''); const [cash, setCash] = useState(''); const [rules, setRules] = useState<readonly RuleInput[]>([])
  const [target, setTarget] = useState<FinancePolicyHeader | null>(null); const [retireFrom, setRetireFrom] = useState(data.businessDate)
  const [previewDate, setPreviewDate] = useState(data.businessDate); const [previewAmount, setPreviewAmount] = useState(''); const [previewAnswer, setPreview] = useState<{ readonly key: string; readonly value: ViewOutcome<FinancePolicyPreview> } | null>(null)
  const policies = matrix ? data.approvalPolicies : data.cashCountDifferencePolicies
  const selectedCash = data.references.cashAccounts.find(x => x.cashAccountId === cash)
  const selectedRole = data.references.roles.find(x => x.roleId === role)
  const previewKey = JSON.stringify([operation, currency, cash, selectedCash?.version, previewDate, previewAmount, data.approvalPolicies, data.cashCountDifferencePolicies])
  const previewFence = useRef(previewKey); previewFence.current = previewKey
  const previewSequence = useRef(0)
  const preview = previewAnswer?.key === previewKey ? previewAnswer.value : null
  const canPublish = hasCapability(shell, C.publish) && (!matrix || hasCapability(shell, C.publishCritical))
  const latest = matrix ? Math.max(0, ...data.approvalPolicies.map(x => x.version)) : Math.max(0, ...data.cashCountDifferencePolicies.filter(x => x.cashAccountId === cash && x.currencyId === selectedCash?.currencyId).map(x => x.version))
  const frozen = busy || held !== null
  async function execute(suffix: string, body: object) {
    if (busy || identity === null) return
    const pending = recallFinancePolicy(identity) ?? { suffix, body, key: crypto.randomUUID() }
    reserveFinancePolicy(identity, pending); setHeld(pending); setBusy(true); const captured = boundary.capture()
    const result = await commandFinancePolicy({ companyId: data.companyId, idempotencyKey: pending.key }, pending.suffix, pending.body)
    if (result.type !== 'unknown' && !(result.type === 'refused' && (result.status === 401 || result.status === 403))) releaseFinancePolicy(identity, pending.key)
    if (!alive.current) return
    setBusy(false)
    if (result.type === 'unknown') { setNotice(T.unknown); return }
    if (result.type === 'refused' && (result.status === 401 || result.status === 403)) { boundary.report(result.status, captured); return }
    setHeld(null)
    if (result.type === 'applied') { setNotice(result.replayed ? T.replay : T.saved); setTarget(null); changed() }
    else { setNotice(T.refused); if (result.status === 409) changed() }
  }
  function addRule() {
    if (!selectedRole || !operation || !currency || !amount) return
    setRules([...rules, { operationPermission: operation, currencyId: currency, approverRoleId: role,
      expectedRoleSourceVersion: selectedRole.sourceVersion, approvalStep: step, amountThreshold: amount }]); setAmount('')
  }
  function prepare(event: FormEvent) {
    event.preventDefault()
    const body = matrix ? { expectedVersion: String(latest), validFrom: from, validThrough: through || null, rules, reason: reason.trim() }
      : { expectedVersion: String(latest), cashAccountId: cash, expectedCashAccountVersion: selectedCash?.version, currencyId: selectedCash?.currencyId, amountThreshold: amount, validFrom: from, validThrough: through || null, reason: reason.trim() }
    void execute(family, body)
  }
  async function inspect() {
    if (busy) return
    const captured = boundary.capture(); const capturedKey = previewKey; const sequence = ++previewSequence.current; setPreview({ key: capturedKey, value: { type: 'loading' } })
    const result = await previewFinancePolicy(data.companyId, family, matrix ? { operationPermission: operation, currencyId: currency, businessDate: previewDate, amount: previewAmount }
      : { cashAccountId: cash, currencyId: selectedCash?.currencyId, businessDate: previewDate, differenceAmount: previewAmount })
    if (!alive.current || previewFence.current !== capturedKey || previewSequence.current !== sequence) return
    if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, captured)
    setPreview({ key: capturedKey, value: result })
  }
  const currencyName = (id: string) => data.references.currencies.find(x => x.currencyId === id)?.code ?? id
  return <section>
    <p>{T.matrixRule}</p><p>{T.permissions}</p><p>{T.noActivation}</p><p>{T.sourceChanged}</p>
    {(data.references.currencies.length === 0 || (matrix ? data.references.roles.length === 0 || data.references.operations.length === 0 : data.references.cashAccounts.length === 0)) && <p role="status">{T.noSources}</p>}
    {notice && <p role={held ? 'alert' : 'status'}>{notice}</p>}
    {held && <><p role="alert">{T.unknown}</p><Button type="button" disabled={busy} onClick={() => void execute(held.suffix, held.body)}>{busy ? T.pending : T.retry}</Button></>}
    {policies.length === 0 && <p>{T.empty}</p>}
    <table className={styles.table}><caption>{T.history}</caption><thead><tr><th scope="col">{T.version}</th><th scope="col">{T.stateVersion}</th><th scope="col">{T.state}</th><th scope="col">{T.from}</th><th scope="col">{T.through}</th><th scope="col">{T.by}</th><th scope="col">{T.publishedBy}</th><th scope="col">{T.reason}</th><th scope="col">{T.publish}</th></tr></thead>
      <tbody>{policies.map(row => <tr key={row.policyId}><td>{row.version}</td><td>{row.stateVersion}</td><td>{row.state === 'Draft' ? T.draft : row.state === 'Published' ? T.published : T.retired}</td><td>{row.validFrom}</td><td>{row.retiredFrom ?? row.validThrough ?? '—'}</td><td>{row.preparedBy}</td><td>{row.publishedBy ?? '—'}</td><td>{row.reason}</td><td>{row.state !== 'Retired' && canPublish && <Button type="button" disabled={frozen} onClick={() => setTarget(row)}>{row.state === 'Draft' ? T.publish : T.retire}</Button>}</td></tr>)}</tbody></table>
    {matrix ? data.approvalPolicies.map(row => <table key={row.policyId} className={styles.table}><caption>{T.matrix} {row.version}</caption><thead><tr><th scope="col">{T.operation}</th><th scope="col">{T.currency}</th><th scope="col">{T.role}</th><th scope="col">{T.step}</th><th scope="col">{T.amount}</th><th scope="col">{T.source}</th></tr></thead><tbody>{row.rules.map(r => <tr key={r.ruleId}><td>{data.references.operations.find(x => x.permission === r.operationPermission)?.name ?? r.operationPermission}</td><td>{currencyName(r.currencyId)}</td><td>{r.approverRoleCode}</td><td>{r.approvalStep}</td><td>{r.amountThreshold}</td><td>{r.roleSourceVersion}</td></tr>)}</tbody></table>)
      : data.cashCountDifferencePolicies.map(row => <p key={row.policyId}>{T.version} {row.version}: {data.references.cashAccounts.find(x => x.cashAccountId === row.cashAccountId)?.name ?? row.cashAccountId} / {currencyName(row.currencyId)} / {row.amountThreshold} / {T.source}: {row.cashAccountVersion}</p>)}
    {target && <form className={styles.form} onSubmit={event => { event.preventDefault(); void execute(`${family}/${target.policyId}/${target.state === 'Draft' ? 'publish' : 'retire'}`, { expectedStateVersion: String(target.stateVersion), ...(target.state === 'Published' ? { retiredFrom: retireFrom } : {}), reason: reason.trim() }) }}>
      <fieldset disabled={frozen}><legend>{target.state === 'Draft' ? T.publish : T.retire} — {T.version} {target.version}</legend>
        {target.state === 'Published' && <Field label={T.retireFrom}>{props => <input {...props} type="date" required min={data.businessDate} value={retireFrom} onChange={event => setRetireFrom(event.target.value)} />}</Field>}
        <Field label={T.reason}>{props => <textarea {...props} required maxLength={4000} value={reason} onChange={event => setReason(event.target.value)} />}</Field>
        <Button type="submit" disabled={!reason.trim() || !canPublish}>{target.state === 'Draft' ? T.publish : T.retire}</Button></fieldset></form>}
    <form className={styles.form} onSubmit={prepare}><fieldset disabled={frozen}><legend>{T.prepare}</legend>
      {matrix ? <>
        <Field label={T.operation}>{props => <select {...props} value={operation} onChange={event => setOperation(event.target.value)}><option value="">{T.choose}</option>{data.references.operations.map(x => <option key={x.permission} value={x.permission}>{x.name}</option>)}</select>}</Field>
        <Field label={T.currency}>{props => <select {...props} value={currency} onChange={event => setCurrency(event.target.value)}><option value="">{T.choose}</option>{data.references.currencies.map(x => <option key={x.currencyId} value={x.currencyId}>{x.code}</option>)}</select>}</Field>
        <Field label={T.role}>{props => <select {...props} value={role} onChange={event => setRole(event.target.value)}><option value="">{T.choose}</option>{data.references.roles.map(x => <option key={x.roleId} value={x.roleId}>{x.name} ({x.code})</option>)}</select>}</Field>
        {selectedRole && <p>{T.source}: {selectedRole.sourceVersion}</p>}
        <Field label={T.step}>{props => <select {...props} value={step} onChange={event => setStep(event.target.value === '2' ? 2 : 1)}><option value={1}>{T.first}</option><option value={2}>{T.second}</option></select>}</Field>
      </> : <><Field label={T.cashAccount}>{props => <select {...props} required value={cash} onChange={event => setCash(event.target.value)}><option value="">{T.choose}</option>{data.references.cashAccounts.map(x => <option key={x.cashAccountId} value={x.cashAccountId}>{x.name} ({currencyName(x.currencyId)})</option>)}</select>}</Field>{selectedCash && <p>{T.source}: {selectedCash.version}</p>}</>}
      <Field label={T.amount}>{props => <input {...props} required={!matrix} inputMode="decimal" maxLength={64} value={amount} onChange={event => setAmount(event.target.value)} />}</Field>
      {matrix && <><Button type="button" disabled={!operation || !currency || !role || !amount} onClick={addRule}>{T.addRule}</Button><ul>{rules.map((r, i) => <li key={i}>{data.references.operations.find(x => x.permission === r.operationPermission)?.name} / {currencyName(r.currencyId)} / {data.references.roles.find(x => x.roleId === r.approverRoleId)?.name} / {r.approvalStep} / {r.amountThreshold} <Button type="button" onClick={() => setRules(rules.filter((_, j) => j !== i))}>{T.remove}</Button></li>)}</ul></>}
      <Field label={T.from}>{props => <input {...props} type="date" required min={data.businessDate} value={from} onChange={event => setFrom(event.target.value)} />}</Field>
      <Field label={T.through}>{props => <input {...props} type="date" min={from} value={through} onChange={event => setThrough(event.target.value)} />}</Field>
      <Field label={T.reason}>{props => <textarea {...props} required maxLength={4000} value={reason} onChange={event => setReason(event.target.value)} />}</Field>
      <Button type="submit" disabled={!reason.trim() || matrix && rules.length === 0 || !matrix && (!selectedCash || !amount)}>{T.prepare}</Button>
    </fieldset></form>
    <fieldset disabled={frozen}><legend>{T.preview}</legend><Field label={T.previewDate}>{props => <input {...props} type="date" required value={previewDate} onChange={event => setPreviewDate(event.target.value)} />}</Field>
      <Field label={T.previewAmount}>{props => <input {...props} inputMode="decimal" maxLength={65} value={previewAmount} onChange={event => setPreviewAmount(event.target.value)} />}</Field>
      <Button type="button" disabled={!previewAmount || matrix && (!operation || !currency) || !matrix && !selectedCash} onClick={() => void inspect()}>{T.preview}</Button>
    </fieldset>
    {preview && (preview.type === 'ok-single' ? preview.data.outcome === 'Unavailable' ? <p role="status">{T.noPolicy}</p> : <p role="status">{matrix ? `${T.matches}: ${preview.data.matchingRules?.map(r => `${r.approverRoleCode} / ${r.approvalStep} / ${r.amountThreshold}`).join('; ') || '—'}` : preview.data.exceedsThreshold ? T.exceeds : T.within} ({T.version}: {preview.data.version})</p>
      : <StateBlock state={classifyOutcome(preview)} subject={T.subject} />)}
    <Button type="button" disabled={frozen} onClick={changed}>{T.reload}</Button>
  </section>
}
