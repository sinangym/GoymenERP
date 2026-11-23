import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useFinanceBinding } from './financePort'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { isPositiveDecimalText, parseAmountInput, formatDecimalTextTr } from './decimalText'
import { fetchPaymentAllocationCandidates, fetchPaymentAllocationReleaseVersion, sendPaymentAllocation, type PaymentAllocationRequest } from './PaymentAllocationHttp'
import { recallPaymentAllocation, recoverPaymentAllocation, reservePaymentAllocation, retryPaymentAllocation,
  settlePaymentAllocation, unknownPaymentAllocation, type PaymentAllocationAttempt } from './PaymentAllocationAttempt'
import { PAYMENT_ALLOCATION_TEXT as T } from './PaymentAllocationText'
export function PaymentAllocationPanel({ paymentId, canWrite }: { readonly paymentId: string; readonly canWrite: boolean }) {
  const bound = useFinanceBinding()
  if (bound === null) return <StateBlock subject={T.heading} state={{ kind: 'no-authorized-company' }} />
  return <Panel key={`${bound.namespace}:${paymentId}`} bound={bound} paymentId={paymentId} canWrite={canWrite} />
}
function Panel({ bound, paymentId, canWrite }: { readonly bound: NonNullable<ReturnType<typeof useFinanceBinding>>; readonly paymentId: string; readonly canWrite: boolean }) {
  const companyId = bound.binding.companyId
  const slot = JSON.stringify([bound.binding.sessionEpoch, bound.binding.actorId, companyId, 'payment-allocation', paymentId])
  const boundary = useAuthBoundary()
  const client = useQueryClient()
  const mounted = useRef(false)
  const [page, setPage] = useState(0)
  const [accountId, setAccountId] = useState('')
  const [openItemId, setOpenItemId] = useState('')
  const [amount, setAmount] = useState('')
  const [reason, setReason] = useState('')
  const [attempt, setAttempt] = useState<PaymentAllocationAttempt | null>(() => recoverPaymentAllocation(slot))
  const [preparing, setPreparing] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [releaseRead, setReleaseRead] = useState<ViewOutcome<unknown> | null>(null)
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const scope = [bound.namespace, 'finance', 'payment-allocation-candidates', paymentId]
  const read = useQuery({ queryKey: [...scope, page], gcTime: 0,
    queryFn: async ({ signal }) => {
      const fence = boundary.capture()
      const answer = await fetchPaymentAllocationCandidates(companyId, paymentId, page, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, fence)
      return answer
    },
  })
  const data = !read.isFetching && read.data?.type === 'ok-single' ? read.data.data : null
  const positions = data?.positions.filter((item) => isPositiveDecimalText(item.availableAmount)) ?? []
  const position = positions.find((item) => item.companyAccountId === accountId) ?? null
  const items = data?.openItems.filter((item) => item.positions.some((value) => value.companyAccountId === accountId
    && value.partyId === position?.partyId && isPositiveDecimalText(value.availableAmount))) ?? []
  const item = items.find((value) => value.openItemId === openItemId) ?? null
  const parsed = parseAmountInput(amount)
  const frozen = attempt !== null || preparing
  /**
   * `final` (K3 P2-1): the user's explicit way out of an unknown attempt. The same key and body are sent once more;
   * any decided server answer closes the attempt (a replay shows the recorded allocation). Only no answer or a 401/403
   * — the session or authority, not this attempt — keeps it open, so leaving never mints a second key blindly.
   */
  const run = async (value: PaymentAllocationAttempt, wasUnknown: boolean, final = false) => {
    const fence = boundary.capture()
    const answer = await sendPaymentAllocation(companyId, value.key, value.request)
    if (answer.kind === 'refused' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, fence)
    if (!mounted.current || recallPaymentAllocation(slot)?.key !== value.key) return
    if (answer.kind === 'answered' && answer.result.outcome === 'Succeeded') {
      settlePaymentAllocation(slot, value.key); setAttempt(null); setMessage(T.saved); setOpenItemId('')
      void client.invalidateQueries({ queryKey: [bound.namespace, 'finance'] })
    } else if (final && (answer.kind === 'unknown' || (answer.kind === 'refused' && (answer.status === 401 || answer.status === 403)))) {
      setAttempt(unknownPaymentAllocation(slot, value.key)); setMessage(T.finalOpen)
    } else if (final) {
      settlePaymentAllocation(slot, value.key); setAttempt(null); setMessage(T.abandoned)
      void client.invalidateQueries({ queryKey: [bound.namespace, 'finance'] })
    } else if (answer.kind === 'unknown' || (wasUnknown && (answer.kind !== 'answered' || !answer.result.isReplay))) {
      setAttempt(unknownPaymentAllocation(slot, value.key)); setMessage(T.unknown)
    } else {
      settlePaymentAllocation(slot, value.key); setAttempt(null)
      setMessage(answer.kind === 'answered' && answer.result.outcome === 'VersionConflict' ? T.conflict : T.refused)
      void client.invalidateQueries({ queryKey: scope })
    }
  }
  const send = (request: PaymentAllocationRequest) => {
    const value = reservePaymentAllocation(slot, request)
    if (value === null) { setAttempt(recoverPaymentAllocation(slot)); return }
    setAttempt(value); void run(value, false)
  }
  const create = () => {
    if (!canWrite || frozen || data === null || position === null || item === null || !parsed.ok || reason.trim() === '') return
    send({ operation: 'create', body: { allocationId: crypto.randomUUID(), paymentId, expectedPaymentVersion: data.paymentVersion,
      openItemId: item.openItemId, expectedOpenItemVersion: item.version, amount: parsed.wire, reason: reason.trim(),
      allocationPartyId: position.partyId, allocationAccountId: position.companyAccountId } })
  }
  const release = async (allocationId: string) => {
    if (!canWrite || frozen || reason.trim() === '') return
    setPreparing(true); setReleaseRead({ type: 'loading' })
    const fence = boundary.capture()
    const answer = await fetchPaymentAllocationReleaseVersion(companyId, allocationId, paymentId)
    if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, fence)
    if (!mounted.current) return
    setPreparing(false); setReleaseRead(answer)
    if (answer.type !== 'ok-single') return
    send({ operation: 'release', body: { allocationId, expectedAllocationVersion: answer.data.allocationVersion,
      expectedOpenItemVersion: answer.data.openItemVersion, reason: reason.trim() } })
  }
  return <section aria-label={T.heading}><h3>{T.heading}</h3>
    {read.isFetching ? <StateBlock subject={T.heading} state={{ kind: 'initial-loading' }} /> : read.data !== undefined && read.data.type !== 'ok-single'
      ? <StateBlock subject={T.heading} state={classifyOutcome(read.data)} onRetry={() => void read.refetch()} /> : null}
    {releaseRead === null || releaseRead.type === 'ok-single' ? null : <StateBlock subject={T.heading} state={classifyOutcome(releaseRead)} />}
    <button type="button" disabled={frozen} onClick={() => void read.refetch()}>{T.refresh}</button>
    {data === null ? null : <>
      <label>{T.account}<select value={accountId} disabled={frozen || !canWrite} onChange={(event) => { setAccountId(event.target.value); setOpenItemId('') }}>
        <option value="">{T.choose}</option>{positions.map((value) => <option value={value.companyAccountId} key={value.companyAccountId}>
          {value.transferId === null ? T.original : `${T.transferred} ${value.transferId.slice(0, 8)}`} — {T.available} {formatDecimalTextTr(value.availableAmount)}
        </option>)}
      </select></label>
      <label>{T.item}<select value={openItemId} disabled={frozen || !canWrite} onChange={(event) => setOpenItemId(event.target.value)}>
        <option value="">{T.choose}</option>{items.map((value) => <option key={value.openItemId} value={value.openItemId}>
          {value.dueOn ?? T.noDueDate} — {formatDecimalTextTr(value.positions.find((x) => x.companyAccountId === accountId)!.availableAmount)}
        </option>)}
      </select></label>{items.length === 0 ? <p>{T.empty}</p> : null}
      <label>{T.amount}<input inputMode="decimal" value={amount} disabled={frozen || !canWrite} onChange={(event) => setAmount(event.target.value)} /></label>
      <label>{T.reason}<input value={reason} maxLength={1000} disabled={frozen || !canWrite} onChange={(event) => setReason(event.target.value)} /></label>
      <button type="button" disabled={!canWrite || frozen || item === null || position === null || !parsed.ok || reason.trim() === ''} onClick={create}>{T.submit}</button>
      <ul>{data.allocations.map((value) => <li key={value.id}>{formatDecimalTextTr(value.amount)} — {T.statuses[value.status]}
        {value.status === 'Active' && canWrite ? <button type="button" disabled={frozen || reason.trim() === ''} onClick={() => void release(value.id)}>{T.release}</button> : null}
      </li>)}</ul>
      <button type="button" disabled={page === 0 || frozen} onClick={() => { setPage((value) => value - 1); setOpenItemId('') }}>{T.previous}</button>
      <button type="button" disabled={(!data.hasMore && !data.hasMoreAllocations) || frozen} onClick={() => { setPage((value) => value + 1); setOpenItemId('') }}>{T.next}</button>
    </>}
    {attempt?.phase !== 'unknown' ? null : <>
      <button type="button" onClick={() => { const next = retryPaymentAllocation(slot); if (next) { setAttempt(next); void run(next, true) } }}>{T.retry}</button>
      <button type="button" aria-describedby={`payment-allocation-final-help-${paymentId}`}
        onClick={() => { const next = retryPaymentAllocation(slot); if (next) { setAttempt(next); void run(next, true, true) } }}>{T.finalCheck}</button>
      <p id={`payment-allocation-final-help-${paymentId}`}>{T.finalCheckHelp}</p>
    </>}
    {message !== null ? <p role="status">{message}</p> : attempt?.phase === 'unknown' ? <p role="status">{T.unknown}</p> : null}
  </section>
}
