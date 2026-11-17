import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { SETTINGS_CAPABILITY } from './capabilities'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt } from './useCommandAttempt'
import { noBody, readJson, send } from './discountLimitApi'
import { NUMBER_SERIES_PATH, decodeNumberFormat, decodeNumberGaps, type NumberFormat, type NumberGap, type RecordNumberFormat } from './documentNumberSeriesContract'
import { NUMBER_SERIES_TEXT as T } from './documentNumberSeriesText'
import styles from './settings.module.css'
export function DocumentNumberSeriesPage() {
  const shell = useShellState(); const key = contextKey(shell); const namespace = key === null ? null : contextKeyString(key)
  return <Screen key={`${namespace}::${shell.routeEntryGeneration}`} namespace={namespace} />
}
function Screen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState(); const company = shell.activeContext?.companyId ?? null
  const [series, setSeries] = useState('')
  const ready = namespace !== null && shell.activeContext?.scopeMode === 'single' && company !== null && hasCapability(shell, SETTINGS_CAPABILITY.documentNumberSeries)
  return <Frame title={T.title} intro={T.intro}>{!ready ? <StateBlock state={{ kind: company === null ? 'no-authorized-company' : 'not-found' }} subject={T.subject} />
    : <><Field label={T.series}>{(control) => <select {...control} value={series} onChange={(event) => setSeries(event.target.value)}><option value="">{T.choose}</option><option value="1">{T.salesOrder}</option><option value="2">{T.salesInvoice}</option><option value="3">{T.dispatch}</option><option value="4">{T.purchaseOrder}</option></select>}</Field>{series !== '' && <Series key={series} series={Number(series)} company={company!} namespace={namespace!} />}</>}</Frame>
}
function Series({ series, company, namespace }: { readonly series: number; readonly company: string; readonly namespace: string }) {
  const query = useBoundaryRead([namespace, 'number-format', series], true, (signal) => readJson(`${NUMBER_SERIES_PATH}/${series}`, company, (raw) => decodeNumberFormat(raw, company, series), signal))
  const state = stateOf<NumberFormat>(query.isPending, query.data)
  return state.kind === 'ready' ? <Format data={state.data} changed={() => void query.refetch()} namespace={namespace} /> : <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
}
function Format({ data, changed, namespace }: { readonly data: NumberFormat; readonly changed: () => void; readonly namespace: string }) {
  const [editing, setEditing] = useState(false); const [notice, setNotice] = useState<string | null>(null)
  return <section><p role="status">{notice}</p>{data.versions.length === 0 ? <p>{T.missing}</p> : <table className={styles.table}><caption>{T.history}</caption><thead><tr><th scope="col">{T.version}</th><th scope="col">{T.prefix}</th><th scope="col">{T.digits}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead><tbody>{data.versions.map((item) => <tr key={item.version}><td>{item.version}</td><td>{item.prefix}</td><td>{item.sequenceDigits}</td><td>{item.recordedBy}</td><td>{item.recordedAtUtc}</td><td>{item.reason}</td></tr>)}</tbody></table>}
    {editing ? <Editor data={data} finish={(message) => { setEditing(false); setNotice(message); if (message !== null) changed() }} /> : <Button type="button" onClick={() => setEditing(true)}>{T.edit}</Button>}
    <Gaps company={data.companyId} series={data.series} namespace={namespace} />
  </section>
}
function Editor({ data, finish }: { readonly data: NumberFormat; readonly finish: (message: string | null) => void }) {
  const [prefix, setPrefix] = useState(''); const [digits, setDigits] = useState(''); const [reason, setReason] = useState(''); const [frozen, setFrozen] = useState<RecordNumberFormat | null>(null)
  const attempt = useCommandAttempt<true>(); const boundary = useAuthBoundary(); const busy = attempt.status.kind === 'busy'
  const valid = /^[A-Z0-9]{1,16}$/.test(prefix) && /^(?:[1-9]|1[0-8])$/.test(digits) && reason.trim().length > 0
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (busy || (!valid && frozen === null)) return
    const payload = frozen ?? { series: data.series, expectedVersion: data.versions[0]?.version ?? '0', prefix, sequenceDigits: Number(digits), reason: reason.trim() }
    const snapshot = boundary.capture()
    const result = await attempt.run(payload, (idempotencyKey) => send('POST', `${NUMBER_SERIES_PATH}/versions`, { companyId: data.companyId, idempotencyKey }, payload, 204, noBody))
    if (result.type === 'unknown') { setFrozen(payload); return }; setFrozen(null)
    if (result.type === 'applied') { finish(result.replayed ? T.replay : T.saved); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, snapshot)
    if (result.status === 409 && result.code === 'settings_number_series_version_conflict') finish(T.conflict)
  }
  return <form className={styles.form} onSubmit={(event) => void submit(event)} aria-busy={busy}><fieldset disabled={busy || frozen !== null}>
    <Field label={T.prefix}>{(control) => <input {...control} required maxLength={16} value={prefix} onChange={(event) => setPrefix(event.target.value)} />}</Field>
    <Field label={T.digits}>{(control) => <input {...control} required inputMode="numeric" value={digits} onChange={(event) => setDigits(event.target.value)} />}</Field><p>{T.effect}</p>
    <Field label={T.reason}>{(control) => <textarea {...control} required maxLength={4000} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>
    </fieldset>{attempt.status.kind === 'unknown' && <p role="alert">{T.unknown}</p>}{attempt.status.kind === 'refused' && <p role="alert">{T.refused}</p>}
    <Button type="submit" variant="primary" disabled={busy || (!valid && frozen === null)}>{busy ? T.pending : frozen !== null ? T.retry : T.save}</Button><Button type="button" disabled={busy || frozen !== null} onClick={() => finish(null)}>{T.cancel}</Button>
  </form>
}
function Gaps({ company, series, namespace }: { readonly company: string; readonly series: number; readonly namespace: string }) {
  const [input, setInput] = useState(''); const [year, setYear] = useState<number | null>(null)
  const query = useBoundaryRead([namespace, 'number-gaps', series, year], year !== null, (signal) => readJson(`${NUMBER_SERIES_PATH}/${series}/gaps/${year}`, company, (raw) => decodeNumberGaps(raw, series, year!), signal))
  const state = stateOf<readonly NumberGap[]>(query.isPending, query.data)
  return <section><form onSubmit={(event) => { event.preventDefault(); if (/^[1-9]\d{0,3}$/.test(input)) setYear(Number(input)) }}><Field label={T.year}>{(control) => <input {...control} inputMode="numeric" required value={input} onChange={(event) => setInput(event.target.value)} />}</Field><Button type="submit" disabled={!/^[1-9]\d{0,3}$/.test(input)}>{T.readGaps}</Button></form>
    {year !== null && (state.kind === 'ready' ? state.data.length === 0 ? <p>{T.noGaps}</p> : <table className={styles.table}><caption>{T.gaps}</caption><thead><tr><th scope="col">{T.number}</th><th scope="col">{T.document}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead><tbody>{state.data.map((item) => <tr key={item.sequence}><td>{item.number}</td><td>{item.documentId}</td><td>{item.cancelledAtUtc}</td><td>{item.reason}</td></tr>)}</tbody></table> : <StateBlock state={state} subject={T.gaps} onRetry={() => void query.refetch()} />)}
  </section>
}
