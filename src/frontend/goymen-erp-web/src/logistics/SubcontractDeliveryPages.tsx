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
import { issuePath, parseQuantityInput, weighingsPath } from './contract'
import { generateClientId } from './instructionAttempt'
import { REALIZATION_CAPABILITY } from './realizationContract'
import { beginSubcontractAttempt, fetchSubcontractCandidates, fetchSubcontractInstruction, fetchSubcontractList, fetchSubcontractSalesLines,
  finishSubcontractAttempt, leaveSubcontractAttempt, resolveSubcontractAttempt, retrySubcontractAttempt, sendSubcontractAttempt, subcontractAttempt, subcontractCreateLine,
  subcontractFrozen, subscribeSubcontractAttempt, type SubcontractAttempt } from './subcontractDeliveryApi'
import { SUBCONTRACT_CAPABILITY as CAP, SUBCONTRACT_DELIVERY_PATH as PATH, SUBCONTRACT_DELIVERY_ROUTE as ROUTE, subcontractKg,
  type SubcontractCandidate, type SubcontractCreateLine, type SubcontractInstruction, type SubcontractLine, type SubcontractAdditionalAllocationUse } from './subcontractDeliveryContract'
import { SUBCONTRACT_DELIVERY_TEXT as T } from './subcontractDeliveryMessages.tr'
import styles from './LogisticsPages.module.css'
import { formatInstant } from './format'
import { PlanExcessPreparationPanel } from './PlanExcessPreparationPanel'
import { PlanExcessUseEditor, planExcessUsePayload } from './PlanExcessUseEditor'
import { PLAN_EXCESS_CAPABILITY, fetchPlanExcess, type PlanExcessUse } from './planExcessApi'
import { PLAN_EXCESS_TEXT } from './planExcessText'

const statusText = { Draft: T.draft, Issued: T.issued, Cancelled: T.cancelled }
const commandCapability = (snapshot: SubcontractAttempt) => snapshot.path === PATH ? CAP.create : snapshot.path.endsWith('/ship') ? CAP.ship
  : snapshot.path.endsWith('/weighing-corrections') ? CAP.correct : snapshot.path.endsWith('/issue') ? CAP.issue
    : snapshot.path.endsWith('/loads') ? CAP.load : snapshot.path.endsWith('/reserve') ? PLAN_EXCESS_CAPABILITY.prepare : CAP.weigh
const canRetryCommand = (shell: ShellState, snapshot: SubcontractAttempt) => hasCapability(shell, commandCapability(snapshot))
  && (!snapshot.path.endsWith('/reserve') || hasCapability(shell, REALIZATION_CAPABILITY.allocation))
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
  const [finalNotice, setFinalNotice] = useState<string | null>(null); const [checking, setChecking] = useState(false)
  async function send(snapshot: SubcontractAttempt | null) {
    if (snapshot === null) return
    setFinalNotice(null)
    const security = boundary.capture(); const outcome = await sendSubcontractAttempt(snapshot)
    if (outcome.type === 'refused') boundary.report(outcome.status, security)
    finishSubcontractAttempt(snapshot, outcome)
    if (alive.current && outcome.type === 'applied') onApplied(outcome.view)
  }
  /** K3 P2-6: leave an unresolved command by READING its instruction again; nothing is sent. */
  async function finalCheck() {
    const current = subcontractAttempt(binding)
    if (checking || current.phase !== 'unknown') return
    setChecking(true); setFinalNotice(null)
    const security = boundary.capture()
    const result = await fetchSubcontractInstruction(current.snapshot.company, current.snapshot.instructionId)
    if (result.type === 'http' && result.status !== 404) boundary.report(result.status, security)
    if (!alive.current) return
    setChecking(false)
    const view = result.type === 'ok-single' ? result.data : null
    if (view === null && !(result.type === 'http' && result.status === 404)) { setFinalNotice(T.finalOpen); return }
    const decision = resolveSubcontractAttempt(binding, view)
    setFinalNotice(decision === 'released' ? T.released : decision === 'open' ? T.finalOpen : null)
    if (decision === 'completed') onApplied(view)
  }
  const notice = state.phase === 'sending' ? <p role="status">{T.pending}</p> : state.phase === 'unknown' ? <div>
    <p role="alert">{T.unknown}</p><StateBlock subject={T.subject} state={{ kind: state.outcome.reason }} />
    {canRetryCommand(shell, state.snapshot)
      ? <Button type="button" disabled={checking} onClick={() => void send(retrySubcontractAttempt(binding))}>{T.retry}</Button> : null}
    <Button type="button" disabled={checking} aria-describedby="subcontract-final-help" onClick={() => void finalCheck()}>{T.finalCheck}</Button>
    <p id="subcontract-final-help">{T.finalCheckHelp}</p>{finalNotice === null ? null : <p role="status">{finalNotice}</p>}</div>
    : state.phase === 'completed' ? state.outcome.type === 'applied' ? <p role="status">{state.outcome.replayed ? T.replayed : T.saved}</p>
      : state.outcome.type === 'refused' ? <div role="alert"><p>{state.outcome.code === 'logistics_subcontract_delivery_active_invoice' ? T.activeInvoice : T.refused}</p>
        {canRetryCommand(shell, state.snapshot) && <>
          {state.outcome.status === 409 && state.outcome.replayed && <p>{T.replayedRefusal}</p>}
          {state.outcome.status === 409 && state.outcome.code === 'inventory_physical_stock_insufficient'
            && state.outcome.detail !== null && <p>{state.outcome.detail}</p>}
        </>}
        {state.outcome.status !== 400 && state.outcome.status !== 422 ? <StateBlock subject={T.subject} state={classifyOutcome({ type: 'http', status: state.outcome.status })} /> : null}</div> : null : null
  return { state, frozen: subcontractFrozen(state), send, notice: <>{notice}{state.phase !== 'unknown' && finalNotice !== null ? <p role="status">{finalNotice}</p> : null}</> }
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
  const [loadUses, setLoadUses] = useState<readonly PlanExcessUse[]>([])
  const [shipUses, setShipUses] = useState<Readonly<Record<string, readonly PlanExcessUse[]>>>({})
  const [extraUses, setExtraUses] = useState<Readonly<Record<string, readonly SubcontractAdditionalAllocationUse[]>>>({})
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
    if (loaded.some(item => planExcessUsePayload(shipUses[item.lineId] ?? []) === null)) { setError(PLAN_EXCESS_TEXT.invalidUse); return }
    if (loaded.some(item => additionalAllocationPayload(extraUses[item.lineId] ?? [], item) === null)) { setError(PLAN_EXCESS_TEXT.invalidUse); return }
    send(`${PATH}/${id}/ship`, 'subcontract', { expectedVersion: view.version, occurredAtUtc: date.toISOString(), lines: loaded.map(item => ({ lineId: item.lineId,
      shipmentId: crypto.randomUUID(), movementId: crypto.randomUUID(), expectedAllocationVersion: item.allocationVersion, expectedLabelVersion: item.labelVersion,
      salesOrderId: item.source.salesOrderId, salesOrderLineId: item.source.salesOrderLineId, manufacturingOrderId: item.source.manufacturingOrderId, outputFactId: item.source.outputFactId, labelId: item.source.labelId,
      ...planExcessUsePayload(shipUses[item.lineId] ?? []), ...additionalAllocationPayload(extraUses[item.lineId] ?? [], item) })) })
  }
  return <>{commands.notice}{error === null ? null : <p role="alert">{error}</p>}<Button type="button" disabled={commands.frozen} onClick={() => void query.refetch()}>{T.refresh}</Button>
    {view === null || query.isFetching ? <Remote value={query.data} fetching={query.isFetching} retry={() => void query.refetch()} /> : <>
      <p>{T.instruction} {view.instructionId} · {statusText[view.status]}</p><table><caption>{T.cart}</caption><thead><tr><th scope="col">{T.output}</th><th scope="col">{T.salesLine}</th><th scope="col">{T.instructed}</th><th scope="col">{T.loaded}</th><th scope="col">{T.shipped}</th><th scope="col">{PLAN_EXCESS_TEXT.approvedCapacity}</th></tr></thead><tbody>{view.lines.map(item => <tr key={item.lineId}>
        <td>{item.source.labelCode}</td><td>{item.source.salesDocumentNo} · {item.source.serviceCode} · {item.source.serviceName}</td><td>{item.instructedKg}</td><td>{item.loadedKg}</td><td>{item.shippedKg}</td><td>{item.approvedPlanExcess ?? PLAN_EXCESS_TEXT.unavailable}</td></tr>)}</tbody></table>
      {view.status === 'Issued' && hasCapability(shell, PLAN_EXCESS_CAPABILITY.prepare) ? view.lines.map((item, index) => <PlanExcessPreparationPanel key={item.lineId} instructionId={id} instructionVersion={view.version}
        lineId={item.lineId} lineNo={index + 1} instructed={item.instructedKg} onRecorded={() => void query.refetch()} />) : null}
      {view.status === 'Issued' && view.weighing === null && hasCapability(shell, PLAN_EXCESS_CAPABILITY.prepare) && hasCapability(shell, REALIZATION_CAPABILITY.allocation)
        ? view.lines.map(item => <ExtraReservationPanel key={item.lineId} company={company} instructionId={id} instructionVersion={view.version} line={item} frozen={frozen} send={send} />) : null}
      {view.status === 'Draft' && hasCapability(shell, CAP.issue) ? <Button type="button" disabled={frozen} onClick={() => send(issuePath(id), 'standard', { expectedVersion: view.version })}>{T.issue}</Button> : null}
      {view.status === 'Issued' && view.weighing === null && hasCapability(shell, CAP.load) ? <fieldset disabled={frozen}><legend>{T.load}</legend>
        <Field label={T.output}>{control => <select {...control} value={line} onChange={event => { setLine(event.currentTarget.value); setLoadUses([]) }}><option value="">{T.choose}</option>{view.lines.map(item => <option key={item.lineId} value={item.lineId}>{item.source.labelCode}</option>)}</select>}</Field>
        <Field label={T.quantity}>{control => <input {...control} inputMode="decimal" value={kg} onChange={event => setKg(event.currentTarget.value)} />}</Field>
        {line === '' ? null : <PlanExcessUseEditor instructionId={id} lineId={line} purpose="load" value={loadUses} onChange={setLoadUses} disabled={frozen} />}
        <Button type="button" onClick={() => { if (!subcontractKg(kg) || kg === '0' || !view.lines.some(item => item.lineId === line)) { setError(T.invalid); return }
          const selection = planExcessUsePayload(loadUses); if (selection === null) { setError(PLAN_EXCESS_TEXT.invalidUse); return }
          send(`${PATH}/${id}/lines/${line}/loads`, 'subcontract', { expectedVersion: view.version, loadId: crypto.randomUUID(), quantity: kg, measurement: 'Measured', ...selection }) }}>{T.load}</Button></fieldset> : null}
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
        <legend>{T.ship}</legend>{view.lines.filter(item => item.loadedKg !== '0').map(item => <div key={item.lineId}><PlanExcessUseEditor instructionId={id} lineId={item.lineId} purpose="ship" value={shipUses[item.lineId] ?? []}
          onChange={value => setShipUses(previous => ({ ...previous, [item.lineId]: value }))} disabled={frozen} />
          <AdditionalAllocationEditor line={item} value={extraUses[item.lineId] ?? []} onChange={value => setExtraUses(previous => ({ ...previous, [item.lineId]: value }))} disabled={frozen} /></div>)}
        <Field label={T.occurred}>{control => <input {...control} type="datetime-local" step="1" value={occurred} onChange={event => setOccurred(event.currentTarget.value)} />}</Field><Button type="button" onClick={ship}>{T.ship}</Button></fieldset> : null}
      {view.corrections.length === 0 ? null : <><h2>{T.history}</h2><ul>{view.corrections.map(item => <li key={item.correctionId}>{T.original}: {item.previousNetKg} · {T.corrected}: {item.netKg} · {item.reason} · {item.correctedAtUtc}</li>)}</ul></>}
      <p>{T.cancellation}</p>
    </>}</>
}

/** Only explicitly selected actual owner reservations are forwarded; no automatic quantity split. */
export function additionalAllocationPayload(uses: readonly SubcontractAdditionalAllocationUse[], line: SubcontractLine): { readonly additionalAllocations?: readonly SubcontractAdditionalAllocationUse[] } | null {
  if (uses.length === 0) return {}
  const selected: SubcontractAdditionalAllocationUse[] = []
  for (const use of uses) {
    const quantity = parseQuantityInput(use.quantityKg)
    if (!quantity.ok || selected.some(item => item.allocationId === use.allocationId) || !line.additionalAllocations?.some(item => item.allocationId === use.allocationId && item.version === use.expectedVersion)) return null
    selected.push({ allocationId: use.allocationId, expectedVersion: use.expectedVersion, quantityKg: quantity.value })
  }
  return { additionalAllocations: selected }
}
function AdditionalAllocationEditor({ line, value, onChange, disabled }: { readonly line: SubcontractLine; readonly value: readonly SubcontractAdditionalAllocationUse[]; readonly onChange: (value: readonly SubcontractAdditionalAllocationUse[]) => void; readonly disabled: boolean }) {
  if (line.additionalAllocations === undefined || line.additionalAllocations.length === 0) return null
  return <fieldset disabled={disabled}><legend>{PLAN_EXCESS_TEXT.selectAllocation} · {line.source.labelCode}</legend>{line.additionalAllocations.map(item => {
    const selected = value.find(use => use.allocationId === item.allocationId)
    return <div key={item.allocationId}><label><input type="checkbox" checked={selected !== undefined} onChange={event => onChange(event.target.checked
      ? [...value, { allocationId: item.allocationId, expectedVersion: item.version, quantityKg: '' }] : value.filter(use => use.allocationId !== item.allocationId))} />{PLAN_EXCESS_TEXT.allocation} {item.allocationId}</label>
      <dl><dt>{PLAN_EXCESS_TEXT.exception}</dt><dd>{item.exceptionId}</dd><dt>{PLAN_EXCESS_TEXT.allocated}</dt><dd>{item.allocatedKg}</dd><dt>{PLAN_EXCESS_TEXT.remaining}</dt><dd>{item.remainingKg}</dd></dl>
      {selected === undefined ? null : <Field label={PLAN_EXCESS_TEXT.reservationUseQuantity}>{control => <input {...control} inputMode="decimal" value={selected.quantityKg} onChange={event => onChange(value.map(use => use.allocationId === item.allocationId ? { ...use, quantityKg: event.target.value } : use))} />}</Field>}
    </div>
  })}</fieldset>
}
function ExtraReservationPanel(props: { readonly company: string; readonly instructionId: string; readonly instructionVersion: string; readonly line: SubcontractLine; readonly frozen: boolean; readonly send: (path: string, family: SubcontractAttempt['family'], body: object) => void }) {
  const [open, setOpen] = useState(false)
  return open ? <ExtraReservation {...props} /> : <Button type="button" disabled={props.frozen} onClick={() => setOpen(true)}>{PLAN_EXCESS_TEXT.reserve} · {props.line.source.labelCode}</Button>
}
function ExtraReservation(props: Parameters<typeof ExtraReservationPanel>[0]) {
  const boundary = useAuthBoundary(), shell = useShellState(), [cursor, setCursor] = useState<string | null>(null)
  const [exceptionId, setExceptionId] = useState(''), [quantity, setQuantity] = useState(''), [reason, setReason] = useState(''), [error, setError] = useState(false)
  const query = useQuery({ queryKey: ['logistics-plan-excess', contextKeyString(contextKey(shell)!), shell.routeEntryGeneration, 'subcontract-reserve', props.instructionId, cursor], retry: false, queryFn: async ({ signal }) => {
    const capture = boundary.capture(), answer = await fetchPlanExcess(props.company, 'load', props.instructionId, cursor, signal)
    if (answer.type === 'http') boundary.report(answer.status, capture)
    return answer
  } })
  const page = query.data?.type === 'ok-single' ? query.data.data : null
  const choices = page?.items.filter(item => item.lineId === props.line.lineId && item.status === 'Approved') ?? []
  function submit(event: FormEvent) {
    event.preventDefault(); const chosen = choices.find(item => item.exceptionId === exceptionId), parsed = parseQuantityInput(quantity), note = reason.trim()
    if (props.frozen || chosen === undefined || !parsed.ok || note.length === 0 || note.length > 500 || /[\u0000-\u001f\u007f]/.test(note) || props.line.labelVersion === null) { setError(true); return }
    setError(false)
    props.send(`${PATH}/${props.instructionId}/lines/${props.line.lineId}/plan-excess/${chosen.exceptionId}/reserve`, 'subcontract', {
      expectedInstructionVersion: props.instructionVersion, expectedExceptionVersion: chosen.version, allocationId: generateClientId(),
      expectedLabelVersion: props.line.labelVersion, quantityKg: parsed.value, reason: note,
    })
  }
  return <section aria-label={`${PLAN_EXCESS_TEXT.reserve} ${props.line.source.labelCode}`}><h3>{PLAN_EXCESS_TEXT.reserve} · {props.line.source.labelCode}</h3><p>{PLAN_EXCESS_TEXT.reserveNote}</p>
    {page === null || query.isFetching ? <Remote value={query.data} fetching={query.isFetching} retry={() => void query.refetch()} /> : choices.length === 0 ? <p>{PLAN_EXCESS_TEXT.noApproved}</p> : null}
    <form onSubmit={submit} noValidate><fieldset disabled={props.frozen || query.isFetching}><legend>{PLAN_EXCESS_TEXT.reserve}</legend>
      <Field label={PLAN_EXCESS_TEXT.exception}>{control => <select {...control} value={exceptionId} onChange={event => setExceptionId(event.target.value)}><option value="">{PLAN_EXCESS_TEXT.choose}</option>{choices.map(item => <option key={item.exceptionId} value={item.exceptionId}>{item.exceptionId} · {item.quantity}</option>)}</select>}</Field>
      <Field label={PLAN_EXCESS_TEXT.reservationQuantity} error={error ? PLAN_EXCESS_TEXT.invalid : undefined}>{control => <input {...control} inputMode="decimal" value={quantity} onChange={event => setQuantity(event.target.value)} />}</Field>
      <Field label={PLAN_EXCESS_TEXT.reason}>{control => <textarea {...control} maxLength={500} value={reason} onChange={event => setReason(event.target.value)} />}</Field>
      <Button type="submit" disabled={choices.length === 0 || props.line.labelVersion === null}>{PLAN_EXCESS_TEXT.reserve}</Button>
    </fieldset></form>
    {page?.nextCursor !== null && page?.nextCursor !== undefined ? <Button type="button" disabled={props.frozen || query.isFetching} onClick={() => setCursor(page.nextCursor)}>{PLAN_EXCESS_TEXT.next}</Button> : null}
    {cursor !== null ? <Button type="button" disabled={props.frozen || query.isFetching} onClick={() => setCursor(null)}>{PLAN_EXCESS_TEXT.previous}</Button> : null}
  </section>
}
