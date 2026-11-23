import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { isReferenceId, parseQuantityInput } from './contract'
import { attemptBinding } from './instructionAttempt'
import { fetchPlanExcess, type PlanExcessUse } from './planExcessApi'
import { PLAN_EXCESS_TEXT as T } from './planExcessText'

/** Input normalization only; limits, allocation and capacity calculations stay on the server. */
export function planExcessUsePayload(value: readonly PlanExcessUse[]): { readonly planExcessUses?: readonly PlanExcessUse[] } | null {
  if (value.length === 0) return {}
  const uses: PlanExcessUse[] = []
  for (const item of value) {
    const quantity = parseQuantityInput(item.quantity)
    if (!quantity.ok || !isReferenceId(item.exceptionId) || !/^[1-9]\d{0,18}$/.test(item.expectedVersion)
      || BigInt(item.expectedVersion) > 9223372036854775807n || uses.some(x => x.exceptionId === item.exceptionId)) return null
    uses.push({ exceptionId: item.exceptionId, expectedVersion: item.expectedVersion, quantity: quantity.value })
  }
  return { planExcessUses: uses }
}
export function PlanExcessUseEditor(props: { readonly instructionId: string; readonly lineId: string; readonly purpose: 'load' | 'ship'; readonly value: readonly PlanExcessUse[]; readonly onChange: (value: readonly PlanExcessUse[]) => void; readonly disabled?: boolean }) {
  const shell = useShellState(), context = shell.activeContext
  const [open, setOpen] = useState(props.value.length > 0)
  const binding = attemptBinding(shell, `plan-excess.use:${props.instructionId}:${props.lineId}:${props.purpose}`)
  if (binding === null || context?.scopeMode !== 'single' || context.companyId === null) return null
  if (!open) return <Button type="button" disabled={props.disabled} onClick={() => setOpen(true)}>{T.use}</Button>
  return <Editor key={`${binding}|${shell.requestFenceGeneration}|${shell.routeEntryGeneration}`} {...props} company={context.companyId} binding={binding} />
}
function Editor(props: Parameters<typeof PlanExcessUseEditor>[0] & { readonly company: string; readonly binding: string }) {
  const boundary = useAuthBoundary(), [cursor, setCursor] = useState<string | null>(null)
  const query = useQuery({ queryKey: ['logistics-plan-excess', props.binding, props.purpose, cursor], retry: false, queryFn: async ({ signal }) => {
    const capture = boundary.capture(), answer = await fetchPlanExcess(props.company, props.purpose, props.instructionId, cursor, signal)
    if (answer.type === 'http') boundary.report(answer.status, capture)
    return answer
  } })
  const page = query.data?.type === 'ok-single' ? query.data.data : null
  const choices = page?.items.filter(item => item.lineId === props.lineId && item.status === 'Approved') ?? []
  return <section aria-label={T.use}><h3>{T.use}</h3><p>{T.useNote}</p>
    {props.value.length > 0 ? <p role="note">{T.useWarning}</p> : null}
    {page === null || query.isFetching ? <StateBlock subject={T.subject} state={query.isFetching ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'loading' })} onRetry={() => void query.refetch()} /> : null}
    {page !== null && !query.isFetching && choices.length === 0 ? <p role="status">{T.noApproved}</p> : null}
    <fieldset disabled={props.disabled}><legend>{T.use}</legend>{choices.map(item => {
      const selected = props.value.find(x => x.exceptionId === item.exceptionId)
      return <div key={item.exceptionId}><label><input type="checkbox" checked={selected !== undefined} onChange={event => props.onChange(event.target.checked
        ? [...props.value, { exceptionId: item.exceptionId, expectedVersion: item.version, quantity: '' }] : props.value.filter(x => x.exceptionId !== item.exceptionId))} />{T.exception} {item.exceptionId}</label>
        <dl><dt>{T.instructed}</dt><dd>{item.instructed}</dd><dt>{T.quantity}</dt><dd>{item.quantity}</dd><dt>{props.purpose === 'load' ? T.availableLoad : T.availableShip}</dt><dd>{props.purpose === 'load' ? item.availableToLoad : item.availableToShip}</dd></dl>
        {selected === undefined ? null : <Field label={T.useQuantity}>{control => <input {...control} inputMode="decimal" value={selected.quantity} onChange={event => props.onChange(props.value.map(x => x.exceptionId === item.exceptionId ? { ...x, quantity: event.target.value } : x))} />}</Field>}
      </div>
    })}</fieldset>
    <Button type="button" disabled={props.disabled || query.isFetching} onClick={() => void query.refetch()}>{T.refresh}</Button>
    {page?.nextCursor !== null && page?.nextCursor !== undefined ? <Button type="button" disabled={props.disabled || query.isFetching} onClick={() => setCursor(page.nextCursor)}>{T.next}</Button> : null}
    {cursor !== null ? <Button type="button" disabled={props.disabled || query.isFetching} onClick={() => setCursor(null)}>{T.previous}</Button> : null}
  </section>
}
