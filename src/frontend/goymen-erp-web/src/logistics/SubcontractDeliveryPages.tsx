import { useLayoutEffect, useRef, useState, useSyncExternalStore, type ReactNode, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { isBoundUuid } from '../product/wireGuards'
import { issuePath, weighingsPath } from './contract'
import { beginSubcontractAttempt, fetchSubcontractCandidates, fetchSubcontractInstruction, fetchSubcontractList, fetchSubcontractSalesLines,
  finishSubcontractAttempt, leaveSubcontractAttempt, retrySubcontractAttempt, sendSubcontractAttempt, subcontractAttempt, subcontractCreateLine,
  subcontractFrozen, subscribeSubcontractAttempt, type SubcontractAttempt } from './subcontractDeliveryApi'
import { SUBCONTRACT_CAPABILITY as CAP, SUBCONTRACT_DELIVERY_PATH as PATH, SUBCONTRACT_DELIVERY_ROUTE as ROUTE, subcontractKg,
  type SubcontractCandidate, type SubcontractCreateLine, type SubcontractInstruction } from './subcontractDeliveryContract'
import { SUBCONTRACT_DELIVERY_TEXT as T } from './subcontractDeliveryMessages.tr'
import styles from './LogisticsPages.module.css'
import { formatInstant } from './format'

const statusText = { Draft: T.draft, Issued: T.issued, Cancelled: T.cancelled }
const commandCapability = (snapshot: SubcontractAttempt) => snapshot.path === PATH ? CAP.create : snapshot.path.endsWith('/ship') ? CAP.ship
  : snapshot.path.endsWith('/weighing-corrections') ? CAP.correct : snapshot.path.endsWith('/issue') ? CAP.issue
    : snapshot.path.endsWith('/loads') ? CAP.load : CAP.weigh
function Frame({ children }: { readonly children: ReactNode }) { return <section className={styles.page} aria-labelledby="subcontract-title">
  <h1 id="subcontract-title">{T.title}</h1><p>{T.intro}</p>{children}</section> }
function Boundary({ capability, children }: { readonly capability: string; readonly children: (company: string, binding: string, shell: ShellState) => ReactNode }) {
  const shell = useShellState(); const key = contextKey(shell); const context = shell.activeContext
  if (key === null || context?.scopeMode !== 'single' || context.companyId === null) return <Frame><StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} /></Frame>
  if (!hasCapability(shell, capability)) return <Frame><StateBlock subject={T.subject} state={{ kind: 'forbidden' }} /></Frame>
  const binding = JSON.stringify([context.userId, context.companyId, shell.sessionEpoch])
  return <Frame><div key={JSON.stringify([contextKeyString(key), shell.routeEntryGeneration])}>{children(context.companyId, binding, shell)}</div></Frame>
}
function Remote<T>({ value, fetching, retry }: { readonly value: ViewOutcome<T> | undefined; readonly fetching: boolean; readonly retry: () => void }) {
  return <StateBlock subject={T.subject} state={fetching ? { kind: 'initial-loading' } : classifyOutcome(value ?? { type: 'malformed' })} onRetry={retry} />
}
function useCommands(binding: string, shell: ShellState, onApplied: (view: SubcontractInstruction | null) => void) {
  const boundary = useAuthBoundary(); const alive = useRef(false)
  const state = useSyncExternalStore(callback => subscribeSubcontractAttempt(binding, callback), () => subcontractAttempt(binding), () => subcontractAttempt(binding))
  useLayoutEffect(() => { alive.current = true; return () => { alive.current = false; leaveSubcontractAttempt(binding) } }, [binding])
  async function send(snapshot: SubcontractAttempt | null) {
    if (snapshot === null) return
    const security = boundary.capture(); const outcome = await sendSubcontractAttempt(snapshot)
    if (outcome.type === 'refused') boundary.report(outcome.status, security)
    finishSubcontractAttempt(snapshot, outcome)
    if (alive.current && outcome.type === 'applied') onApplied(outcome.view)
  }
  const notice = state.phase === 'sending' ? <p role="status">{T.pending}</p> : state.phase === 'unknown' ? <div>
    <p role="alert">{T.unknown}</p><StateBlock subject={T.subject} state={{ kind: state.outcome.reason }} />
    {hasCapability(shell, commandCapability(state.snapshot))
      ? <Button type="button" onClick={() => void send(retrySubcontractAttempt(binding))}>{T.retry}</Button> : null}</div>
    : state.phase === 'completed' ? state.outcome.type === 'applied' ? <p role="status">{state.outcome.replayed ? T.replayed : T.saved}</p>
      : state.outcome.type === 'refused' ? <div role="alert"><p>{state.outcome.code === 'logistics_subcontract_delivery_active_invoice' ? T.activeInvoice : T.refused}</p>
        {hasCapability(shell, commandCapability(state.snapshot)) && <>
          {state.outcome.status === 409 && state.outcome.replayed && <p>{T.replayedRefusal}</p>}
          {state.outcome.status === 409 && state.outcome.code === 'inventory_physical_stock_insufficient'
            && state.outcome.detail !== null && <p>{state.outcome.detail}</p>}
        </>}
        {state.outcome.status !== 400 && state.outcome.status !== 422 ? <StateBlock subject={T.subject} state={classifyOutcome({ type: 'http', status: state.outcome.status })} /> : null}</div> : null : null
  return { state, frozen: subcontractFrozen(state), send, notice }
}
export function SubcontractDeliveryListPage() { return <Boundary capability={CAP.list}>{(company, binding, shell) => <List company={company} binding={binding} shell={shell} />}</Boundary> }
function List({ company, binding, shell }: { readonly company: string; readonly binding: string; readonly shell: ShellState }) {
  const [cursor, setCursor] = useState<string | null>(null); const boundary = useAuthBoundary()
  const query = useQuery({ queryKey: [binding, shell.routeEntryGeneration, 'subcontract-list', cursor], retry: false, queryFn: async ({ signal }) => {
    const fence = boundary.capture(); const result = await fetchSubcontractList(company, cursor, signal); if (result.type === 'http') boundary.report(result.status, fence); return result
  } })
  const view = query.data?.type === 'ok-single' ? query.data.data : null
  return <><h2>{T.list}</h2>{hasCapability(shell, CAP.create) ? <Link to={`${ROUTE}/yeni`}>{T.create}</Link> : null}
    <Button type="button" onClick={() => void query.refetch()}>{T.refresh}</Button>
    {view === null || query.isFetching ? <Remote value={query.data} fetching={query.isFetching} retry={() => void query.refetch()} /> : <>
      {view.items.length === 0 ? <p role="status">{T.empty}</p> : <ul>{view.items.map(item => <li key={item.instructionId}>
        {hasCapability(shell, CAP.detail) ? <Link to={`${ROUTE}/${item.instructionId}`}>{item.labelCodes.join(', ')} · {formatInstant(item.createdAtUtc)}</Link>
          : `${item.labelCodes.join(', ')} · ${formatInstant(item.createdAtUtc)}`} · {statusText[item.status]}</li>)}</ul>}
      <Button type="button" disabled={cursor === null} onClick={() => setCursor(null)}>{T.previous}</Button><Button type="button" disabled={view.nextCursor === null} onClick={() => setCursor(view.nextCursor)}>{T.next}</Button>
    </>}</>
}
export function SubcontractDeliveryCreatePage() { return <Boundary capability={CAP.create}>{(company, binding, shell) => <Create company={company} binding={binding} shell={shell} />}</Boundary> }
function Create({ company, binding, shell }: { readonly company: string; readonly binding: string; readonly shell: ShellState }) {
  const boundary = useAuthBoundary(); const [cursor, setCursor] = useState<string | null>(null); const [line, setLine] = useState(''); const [page, setPage] = useState(0)
  const [output, setOutput] = useState(''); const [kg, setKg] = useState(''); const [reason, setReason] = useState(''); const [date, setDate] = useState('')
  const [cart, setCart] = useState<readonly { readonly candidate: SubcontractCandidate; readonly line: SubcontractCreateLine }[]>([])
  const [error, setError] = useState<string | null>(null); const [created, setCreated] = useState<SubcontractInstruction | null>(null)
  const commands = useCommands(binding, shell, view => { setCreated(view); setCart([]); void choices.refetch() })
  const choices = useQuery({ queryKey: [binding, shell.routeEntryGeneration, 'subcontract-sales', cursor], retry: false, queryFn: async ({ signal }) => {
    const fence = boundary.capture(); const result = await fetchSubcontractSalesLines(company, cursor, signal); if (result.type === 'http') boundary.report(result.status, fence); return result
  } })
  const lines = choices.data?.type === 'ok-single' ? choices.data.data : null; const selected = lines?.items.find(item => item.salesOrderLineId === line) ?? null
  const outputs = useQuery({ queryKey: [binding, shell.routeEntryGeneration, 'subcontract-outputs', selected?.salesOrderId, line, page], enabled: selected !== null && !choices.isFetching,
    retry: false, queryFn: async ({ signal }) => { if (selected === null) return { type: 'malformed' as const }
      const fence = boundary.capture(); const result = await fetchSubcontractCandidates(company, selected.salesOrderId, selected.salesOrderLineId, page, signal)
      if (result.type === 'http') boundary.report(result.status, fence); return result
    } })
  const candidates = outputs.data?.type === 'ok-single' ? outputs.data.data : null
  const candidate = candidates?.items.find(item => `${item.manufacturingOrderId}:${item.outputFactId}` === output) ?? null
  const locked = commands.frozen || choices.isFetching || outputs.isFetching
  function add() {
    if (locked || candidate === null || !candidate.isReleasable || !subcontractKg(kg) || kg === '0' || reason.trim() === '' || reason.trim().length > 500) { setError(T.invalid); return }
    if (cart.some(item => item.candidate.labelId === candidate.labelId)) { setError(T.invalid); return }
    if (cart.some(item => item.candidate.customerPartyId !== candidate.customerPartyId || item.candidate.customerAccountId !== candidate.customerAccountId)) { setError(T.mixedCustomer); return }
    setCart([...cart, { candidate, line: subcontractCreateLine(candidate, kg, reason.trim()) }]); setKg(''); setReason(''); setError(null)
  }
  function submit(event: FormEvent) { event.preventDefault(); if (locked || cart.length === 0 || !/^\d{4}-\d{2}-\d{2}$/.test(date)) { setError(T.invalid); return }
    const id = crypto.randomUUID(); void commands.send(beginSubcontractAttempt(binding, company, id, PATH, 'subcontract', { instructionId: id, effectiveOn: date, lines: cart.map(item => item.line) })) }
  return <><h2>{T.create}</h2>{commands.notice}{created === null ? null : <p>{hasCapability(shell, CAP.detail) ? <Link to={`${ROUTE}/${created.instructionId}`}>{T.detail}</Link> : T.saved}</p>}
    {error === null ? null : <p role="alert">{error}</p>}<Button type="button" disabled={commands.frozen} onClick={() => { void choices.refetch(); if (selected !== null) void outputs.refetch() }}>{T.refresh}</Button>
    {lines === null || choices.isFetching ? <Remote value={choices.data} fetching={choices.isFetching} retry={() => void choices.refetch()} /> : <>
      {lines.items.length === 0 ? <p role="status">{T.empty}</p> : <Field label={T.salesLine}>{control => <select {...control} disabled={locked} value={line} onChange={event => { setLine(event.currentTarget.value); setOutput(''); setPage(0); setKg('') }}>
        <option value="">{T.choose}</option>{lines.items.map(item => <option key={item.salesOrderLineId} value={item.salesOrderLineId}>{item.documentNo} · {item.serviceCode} · {item.serviceName}</option>)}</select>}</Field>}
      <Button type="button" disabled={locked || cursor === null} onClick={() => { setCursor(null); setLine(''); setOutput('') }}>{T.previous}</Button>
      <Button type="button" disabled={locked || lines.nextCursor === null} onClick={() => { setCursor(lines.nextCursor); setLine(''); setOutput('') }}>{T.next}</Button>
    </>}
    {selected === null ? null : candidates === null || outputs.isFetching ? <Remote value={outputs.data} fetching={outputs.isFetching} retry={() => void outputs.refetch()} /> : <>
      {candidates.items.length === 0 ? <p role="status">{T.empty}</p> : <Field label={T.output}>{control => <select {...control} disabled={locked} value={output} onChange={event => { setOutput(event.currentTarget.value); setKg('') }}>
        <option value="">{T.choose}</option>{candidates.items.map(item => <option key={`${item.manufacturingOrderId}:${item.outputFactId}`} value={`${item.manufacturingOrderId}:${item.outputFactId}`} disabled={!item.isReleasable}>{item.labelCode} · {T.available}: {item.availableKg}</option>)}</select>}</Field>}
      <Button type="button" disabled={locked || page === 0} onClick={() => { setPage(0); setOutput('') }}>{T.previous}</Button><Button type="button" disabled={locked || !candidates.hasNext} onClick={() => { setPage(page + 1); setOutput('') }}>{T.next}</Button>
      {candidate === null ? null : <><dl><dt>{T.declared}</dt><dd>{candidate.declaredKg}</dd><dt>{T.physical}</dt><dd>{candidate.physicalKg}</dd><dt>{T.reserved}</dt><dd>{candidate.reservedKg}</dd><dt>{T.available}</dt><dd>{candidate.availableKg}</dd></dl>
        <Field label={T.quantity}>{control => <input {...control} inputMode="decimal" disabled={locked} value={kg} onChange={event => setKg(event.currentTarget.value)} />}</Field>
        <Field label={T.reason}>{control => <input {...control} disabled={locked} maxLength={500} value={reason} onChange={event => setReason(event.currentTarget.value)} />}</Field>
        <Button type="button" disabled={locked || cart.length >= 500} onClick={add}>{T.add}</Button></>}
    </>}
    <form onSubmit={submit}><fieldset disabled={locked}><legend>{T.cart}</legend>{cart.length === 0 ? <p>{T.empty}</p> : <ul>{cart.map(item => <li key={item.line.lineId}>{item.candidate.labelCode} · {item.line.quantityKg} kg <Button type="button" onClick={() => setCart(cart.filter(value => value !== item))}>{T.remove}</Button></li>)}</ul>}
      <Field label={T.date}>{control => <input {...control} type="date" value={date} onChange={event => setDate(event.currentTarget.value)} />}</Field><Button type="submit" disabled={cart.length === 0}>{T.save}</Button></fieldset></form>
  </>
}

export function SubcontractDeliveryDetailPage() { const { instructionId } = useParams(); return <Boundary capability={CAP.detail}>{(company, binding, shell) =>
  isBoundUuid(instructionId) ? <Detail company={company} binding={binding} shell={shell} id={instructionId} /> : <StateBlock subject={T.subject} state={{ kind: 'not-found' }} />}</Boundary> }
function Detail({ company, binding, shell, id }: { readonly company: string; readonly binding: string; readonly shell: ShellState; readonly id: string }) {
  const boundary = useAuthBoundary(); const [error, setError] = useState<string | null>(null); const [line, setLine] = useState(''); const [kg, setKg] = useState('')
  const [direction, setDirection] = useState<'TareFirst' | 'GrossFirst' | ''>(''); const [first, setFirst] = useState(''); const [second, setSecond] = useState(''); const [reason, setReason] = useState(''); const [occurred, setOccurred] = useState('')
  const commands = useCommands(binding, shell, () => { setError(null); setKg(''); setFirst(''); setSecond(''); setReason(''); void query.refetch() })
  const query = useQuery({ queryKey: [binding, shell.routeEntryGeneration, 'subcontract-detail', id], retry: false, queryFn: async ({ signal }) => {
    const fence = boundary.capture(); const result = await fetchSubcontractInstruction(company, id, signal); if (result.type === 'http') boundary.report(result.status, fence); return result
  } })
  const view = query.data?.type === 'ok-single' ? query.data.data : null; const frozen = commands.frozen || query.isFetching
  const send = (path: string, family: SubcontractAttempt['family'], body: object) => void commands.send(beginSubcontractAttempt(binding, company, id, path, family, body))
  function weighing(correct: boolean) {
    if (view === null || frozen || direction === '' || !subcontractKg(first) || first === '0' || !subcontractKg(second) || second === '0' || correct && reason.trim() === '') { setError(T.invalid); return }
    if (correct && view.weighing !== null) send(`${PATH}/${id}/weighing-corrections`, 'subcontract', { expectedVersion: view.version, weighingId: view.weighing.weighingId,
      expectedWeighingRevision: view.weighing.revision, correctionId: crypto.randomUUID(), direction, firstWeighingKg: first, secondWeighingKg: second, reason: reason.trim() })
    else send(weighingsPath(id), 'standard', { expectedVersion: view.version, weighingId: crypto.randomUUID(), direction, firstWeighingKg: first, secondWeighingKg: second })
  }
  function ship() {
    if (view === null || frozen) return
    const date = occurred === '' ? null : new Date(occurred); if (date === null || !Number.isFinite(date.getTime())) { setError(T.invalid); return }
    const loaded = view.lines.filter(item => item.loadedKg !== '0' && item.shippedKg === '0')
    if (loaded.length === 0 || loaded.some(item => item.labelVersion === null || item.allocationVersion === null)) { setError(T.sourceMissing); return }
    send(`${PATH}/${id}/ship`, 'subcontract', { expectedVersion: view.version, occurredAtUtc: date.toISOString(), lines: loaded.map(item => ({ lineId: item.lineId,
      shipmentId: crypto.randomUUID(), movementId: crypto.randomUUID(), expectedAllocationVersion: item.allocationVersion, expectedLabelVersion: item.labelVersion,
      salesOrderId: item.source.salesOrderId, salesOrderLineId: item.source.salesOrderLineId, manufacturingOrderId: item.source.manufacturingOrderId, outputFactId: item.source.outputFactId, labelId: item.source.labelId })) })
  }
  return <>{commands.notice}{error === null ? null : <p role="alert">{error}</p>}<Button type="button" disabled={commands.frozen} onClick={() => void query.refetch()}>{T.refresh}</Button>
    {view === null || query.isFetching ? <Remote value={query.data} fetching={query.isFetching} retry={() => void query.refetch()} /> : <>
      <p>{T.instruction} {view.instructionId} · {statusText[view.status]}</p><table><caption>{T.cart}</caption><thead><tr><th>{T.output}</th><th>{T.salesLine}</th><th>{T.instructed}</th><th>{T.loaded}</th><th>{T.shipped}</th></tr></thead><tbody>{view.lines.map(item => <tr key={item.lineId}>
        <td>{item.source.labelCode}</td><td>{item.source.salesDocumentNo} · {item.source.serviceCode} · {item.source.serviceName}</td><td>{item.instructedKg}</td><td>{item.loadedKg}</td><td>{item.shippedKg}</td></tr>)}</tbody></table>
      {view.status === 'Draft' && hasCapability(shell, CAP.issue) ? <Button type="button" disabled={frozen} onClick={() => send(issuePath(id), 'standard', { expectedVersion: view.version })}>{T.issue}</Button> : null}
      {view.status === 'Issued' && view.weighing === null && hasCapability(shell, CAP.load) ? <fieldset disabled={frozen}><legend>{T.load}</legend>
        <Field label={T.output}>{control => <select {...control} value={line} onChange={event => setLine(event.currentTarget.value)}><option value="">{T.choose}</option>{view.lines.map(item => <option key={item.lineId} value={item.lineId}>{item.source.labelCode}</option>)}</select>}</Field>
        <Field label={T.quantity}>{control => <input {...control} inputMode="decimal" value={kg} onChange={event => setKg(event.currentTarget.value)} />}</Field>
        <Button type="button" onClick={() => { if (!subcontractKg(kg) || kg === '0' || !view.lines.some(item => item.lineId === line)) { setError(T.invalid); return }
          send(`${PATH}/${id}/lines/${line}/loads`, 'subcontract', { expectedVersion: view.version, loadId: crypto.randomUUID(), quantity: kg, measurement: 'Measured' }) }}>{T.load}</Button></fieldset> : null}
      {view.weighing === null && view.status === 'Issued' && hasCapability(shell, CAP.weigh) || view.weighing !== null && view.lines.some(item => item.shippedKg !== '0') && hasCapability(shell, CAP.correct) ? <fieldset disabled={frozen}>
        <legend>{view.weighing === null ? T.weighing : T.correct}</legend>{view.weighing === null ? null : <p>{T.correctionWarning}</p>}
        <Field label={T.direction}>{control => <select {...control} value={direction} onChange={event => setDirection(event.currentTarget.value as typeof direction)}><option value="">{T.choose}</option><option value="TareFirst">{T.tareFirst}</option><option value="GrossFirst">{T.grossFirst}</option></select>}</Field>
        <Field label={T.first}>{control => <input {...control} inputMode="decimal" value={first} onChange={event => setFirst(event.currentTarget.value)} />}</Field><Field label={T.second}>{control => <input {...control} inputMode="decimal" value={second} onChange={event => setSecond(event.currentTarget.value)} />}</Field>
        {view.weighing === null ? null : <Field label={T.reason}>{control => <input {...control} value={reason} maxLength={500} onChange={event => setReason(event.currentTarget.value)} />}</Field>}
        <Button type="button" onClick={() => weighing(view.weighing !== null)}>{view.weighing === null ? T.recordWeighing : T.correct}</Button></fieldset> : null}
      {view.weighing === null ? null : <dl><dt>{T.net}</dt><dd>{view.weighing.netKg}</dd><dt>{T.difference}</dt><dd>{view.weighing.differenceKg}</dd><dt>{T.revision}</dt><dd>{view.weighing.revision}</dd></dl>}
      {view.originalWeighing === null ? null : <section aria-label={T.originalWeighing}><h2>{T.originalWeighing}</h2><dl><dt>{T.direction}</dt><dd>{view.originalWeighing.direction === 'TareFirst' ? T.tareFirst : T.grossFirst}</dd>
        <dt>{T.first}</dt><dd>{view.originalWeighing.firstKg}</dd><dt>{T.second}</dt><dd>{view.originalWeighing.secondKg}</dd><dt>{T.net}</dt><dd>{view.originalWeighing.netKg}</dd></dl></section>}
      {view.status === 'Issued' && view.weighing !== null && view.lines.every(item => item.shippedKg === '0') && hasCapability(shell, CAP.ship) ? <fieldset disabled={frozen}>
        <legend>{T.ship}</legend><Field label={T.occurred}>{control => <input {...control} type="datetime-local" step="1" value={occurred} onChange={event => setOccurred(event.currentTarget.value)} />}</Field><Button type="button" onClick={ship}>{T.ship}</Button></fieldset> : null}
      {view.corrections.length === 0 ? null : <><h2>{T.history}</h2><ul>{view.corrections.map(item => <li key={item.correctionId}>{T.original}: {item.previousNetKg} · {T.corrected}: {item.netKg} · {item.reason} · {item.correctedAtUtc}</li>)}</ul></>}
      <p>{T.cancellation}</p>
    </>}</>
}
