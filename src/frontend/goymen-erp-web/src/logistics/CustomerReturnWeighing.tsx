import { useState, type FormEvent } from 'react'
import { useScopedCommandAttempt } from '../settings/useCommandAttempt'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { LOGISTICS_CAPABILITY } from './capabilities'
import { parseQuantityInput, type WeighingDirection } from './contract'
import { CUSTOMER_RETURN_CAPABILITY, recordCustomerReturnWeighing, type CustomerReturnContext } from './customerReturnApi'
import { isCustomerReturnWeighingRequest, type CustomerReturnResponseV1, type CustomerReturnViewV1,
  type CustomerReturnWeighingViewV1, type RecordCustomerReturnWeighingV1 } from './customerReturnContract'
import { customerReturnText as T } from './customerReturnText'
import { WEIGHING_TEXT as WEIGHING } from './logisticsText'

export function CustomerReturnWeighingFacts({ value }: { readonly value: CustomerReturnWeighingViewV1 }) {
  return <section aria-label={T.weighingRetained}><h4>{T.weighingRetained}</h4><dl>
    <dt>{T.weighingIdentity}</dt><dd>{value.weighingId}</dd>
    <dt>{WEIGHING.direction}</dt><dd>{WEIGHING.directionShort[value.direction]}</dd>
    <dt>{WEIGHING.first}</dt><dd>{value.firstWeighingKg}</dd><dt>{WEIGHING.second}</dt><dd>{value.secondWeighingKg}</dd>
    <dt>{WEIGHING.net}</dt><dd>{value.netKg}</dd><dt>{WEIGHING.recordedBy}</dt><dd>{value.recordedBy}</dd>
    <dt>{WEIGHING.recordedAt}</dt><dd>{value.recordedAtUtc}</dd>
  </dl></section>
}

interface Props { readonly context: CustomerReturnContext; readonly customerReturn: CustomerReturnViewV1; readonly canRecord: boolean; readonly onRecorded: () => void }
export function CustomerReturnWeighing({ context, customerReturn, canRecord, onRecorded }: Props) {
  const shell = useShellState(), key = contextKey(shell)
  if (!canRecord || context.quantityUnit !== 'Piece' || key === null || capabilityFailClosed(shell)
    || shell.activeContext?.scopeMode !== 'single' || shell.activeContext.companyId !== context.companyId
    || customerReturn.companyId !== context.companyId || customerReturn.loadingInstructionId !== context.loadingInstructionId
    || customerReturn.loadingInstructionLineId !== context.loadingInstructionLineId || customerReturn.sourceOrderId !== context.salesOrderId
    || !hasCapability(shell, LOGISTICS_CAPABILITY.instructionsDetail) || !hasCapability(shell, CUSTOMER_RETURN_CAPABILITY.read)
    || !hasCapability(shell, CUSTOMER_RETURN_CAPABILITY.recordWeighing)) return null
  return <Weighing key={`${contextKeyString(key)}:${shell.routeEntryGeneration}:${customerReturn.customerReturnId}`}
    context={context} customerReturn={customerReturn} actorId={shell.activeContext.userId} onRecorded={onRecorded} />
}

function Weighing({ context, customerReturn: row, actorId, onRecorded }: Omit<Props, 'canRecord'> & { readonly actorId: string }) {
  const [direction, setDirection] = useState<WeighingDirection | ''>(''), [first, setFirst] = useState(''), [second, setSecond] = useState('')
  const attempt = useScopedCommandAttempt<RecordCustomerReturnWeighingV1, CustomerReturnResponseV1>(`logistics.customer-return.weighing:${row.customerReturnId}`,
    (request, key) => recordCustomerReturnWeighing(context, row.customerReturnId, actorId, request, key), isCustomerReturnWeighingRequest)
  const frozen = attempt.status.kind === 'busy' || attempt.hasPendingRetry
  const canStart = row.status === 'Pending' && row.version === '1' && row.weighing === undefined && attempt.status.kind !== 'applied'
  const firstValue = parseQuantityInput(first), secondValue = parseQuantityInput(second)
  const valid = direction !== '' && firstValue.ok && secondValue.ok
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (!canStart || frozen || direction === '' || !firstValue.ok || !secondValue.ok) return
    const result = await attempt.run({ expectedVersion: '1', weighingId: crypto.randomUUID(), direction,
      firstWeighingKg: firstValue.value, secondWeighingKg: secondValue.value })
    if (result.type === 'applied') onRecorded()
  }
  const retry = async () => { const result = await attempt.retry(); if (result.type === 'applied') onRecorded() }
  if (!canStart && !frozen && attempt.status.kind !== 'applied' && attempt.status.kind !== 'refused') return null
  const original = attempt.pendingPayload
  return <section aria-label={T.weighingTitle}><h4>{T.weighingTitle}</h4>
    {canStart || frozen ? <form onSubmit={event => void submit(event)}><p>{T.weighingHelp}</p><fieldset disabled={frozen || !canStart}>
      <label>{WEIGHING.direction}<select value={frozen ? original?.direction ?? '' : direction} onChange={event => setDirection(event.target.value as WeighingDirection | '')} required>
        <option value="">{WEIGHING.direction}</option><option value="TareFirst">{WEIGHING.directionShort.TareFirst}</option><option value="GrossFirst">{WEIGHING.directionShort.GrossFirst}</option>
      </select></label>
      <label>{WEIGHING.first}<input value={frozen ? original?.firstWeighingKg ?? '' : first} onChange={event => setFirst(event.target.value)} inputMode="decimal" required /></label>
      <label>{WEIGHING.second}<input value={frozen ? original?.secondWeighingKg ?? '' : second} onChange={event => setSecond(event.target.value)} inputMode="decimal" required /></label>
      {(!firstValue.ok && first !== '' || !secondValue.ok && second !== '') && !frozen ? <p role="alert">{T.weighingInvalid}</p> : null}
      <button type="submit" disabled={!valid || frozen || !canStart}>{T.weighingRecord}</button>
    </fieldset></form> : null}
    {attempt.status.kind === 'busy' ? <p role="status">{T.sending}</p> : null}
    {attempt.status.kind === 'unknown' ? <div role="alert"><p>{T.unknown}</p><p>{T.weighingVersion}: {original?.expectedVersion}</p><button type="button" onClick={() => void retry()}>{T.retry}</button></div> : null}
    {attempt.status.kind === 'refused' ? <p role="alert">{T.commandFailed} ({attempt.status.status})</p> : null}
    {attempt.status.kind === 'applied' ? <><p role="status">{attempt.status.replayed ? T.weighingReplay : T.commandSucceeded}</p>
      {row.weighing === undefined && attempt.status.data.return.weighing ? <CustomerReturnWeighingFacts value={attempt.status.data.return.weighing} /> : null}</> : null}
  </section>
}
