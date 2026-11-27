import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useInfiniteQuery, useQuery, type InfiniteData } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, capabilityFailClosed } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useScopedCommandAttempt } from '../settings/useCommandAttempt'
import { Button } from '../ui/Button'
import { inboxKindText, inboxRoleText } from './workInboxText'
import { fetchWorkDecision, sendWorkDecision, workDecisionLocalUtc, isWorkDecisionRequest, isSalesWorkDecisionKind, isCounterpartyWorkDecisionKind, workDecisionReasonLimit, type WorkDecisionKind, type WorkDecisionRequest, type WorkDecisionResult } from './workDecisionApi'
import { fetchDelegations, type DelegationPage } from './workDelegationApi'
import { delegationPersonLabel } from './delegationPersonLabel'
import type { ViewOutcome } from '../shell/uiState'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import styles from './Work.module.css'

class DelegationReadFailure extends Error {
  readonly outcome: ViewOutcome<unknown>
  constructor(outcome: ViewOutcome<unknown>) { super('delegation-read-failed'); this.outcome = outcome }
}

export function WorkDecisionPage({ kind }: { readonly kind: WorkDecisionKind }) {
  const shell = useShellState(); const context = contextKey(shell); const { approvalId = '' } = useParams()
  return <Decision key={`${context === null ? 'none' : contextKeyString(context)}:${shell.routeEntryGeneration}:${kind}:${approvalId}`} kind={kind} id={approvalId} />
}
function Decision({ kind, id }: { readonly kind: WorkDecisionKind; readonly id: string }) {
  const shell = useShellState(); const context = contextKey(shell); const boundary = useAuthBoundary()
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const namespace = context === null ? null : contextKeyString(context)
  const [selectedLabels, setSelectedLabels] = useState<readonly string[]>([])
  const [selectionVersion, setSelectionVersion] = useState<string | null>(null)
  const [decisionTime, setDecisionTime] = useState('')
  const [lossTimeBinding, setLossTimeBinding] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const [useDelegation, setUseDelegation] = useState(false); const [delegationId, setDelegationId] = useState('')
  const delegations = useInfiniteQuery<DelegationPage,DelegationReadFailure,InfiniteData<DelegationPage>,readonly unknown[],number>({ queryKey: [namespace, shell.routeEntryGeneration, 'work-decision-delegations'],
    initialPageParam: 0, retry: false,
    getNextPageParam: last => last.hasMore ? last.page + 1 : undefined,
    enabled: useDelegation && company !== null && namespace !== null && !capabilityFailClosed(shell), queryFn: async ({ signal, pageParam }) => {
      const captured = boundary.capture(); const answer = await fetchDelegations(company!, false, pageParam, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      if (answer.type !== 'ok-single') throw new DelegationReadFailure(answer)
      return answer.data
    } })
  const pages = delegations.data?.pages ?? []
  const candidates = [...new Map(pages.flatMap(page => page.items).map(row => [row.id,row])).values()].filter(row => row.kind === kind
    && row.delegateUserId === shell.activeContext?.userId && row.revokedAtUtc === null)
  const people = pages.flatMap(page => page.people)
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'work-decision', kind, id],
    enabled: company !== null && namespace !== null && !capabilityFailClosed(shell), queryFn: async ({ signal }) => {
      const captured = boundary.capture(); const result = await fetchWorkDecision(company!, kind, id, signal)
      if (result.type === 'http') boundary.report(result.status, captured)
      return result
    } })
  const attempt = useScopedCommandAttempt<WorkDecisionRequest, WorkDecisionResult>(`work-decision:${kind}:${id}`,
    (payload, key) => sendWorkDecision(company!, kind, id, payload, key), payload => isWorkDecisionRequest(payload, kind))
  if (company === null || capabilityFailClosed(shell)) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.title} />
  const preview = query.data?.type === 'ok-single' ? query.data.data : null
  const frozen = attempt.status.kind === 'busy' || attempt.hasPendingRetry
  const options = preview?.manufacturingPlanReapproval?.parentRollIds
  const selected = frozen ? attempt.pendingPayload?.manufacturingPlanReapproval?.selectedLabelIds ?? [] : selectionVersion === preview?.sourceVersion ? selectedLabels : []
  const selectionInvalid = options !== undefined && (selectionVersion !== preview?.sourceVersion || selectedLabels.length !== options.length
    || selectedLabels.some(id => !options.includes(id)))
  const loss = kind === 'inventory.transfer-loss.v1'
  const lossRequest = frozen ? attempt.pendingPayload?.inventoryTransferLoss?.requestNumber : preview?.inventoryTransferLoss?.requestNumber
  const lossReason = loss && frozen ? attempt.pendingPayload?.reason ?? '' : reason
  const currentLossBinding = preview?.inventoryTransferLoss === undefined ? null : `${preview.sourceVersion}:${preview.inventoryTransferLoss.requestNumber}`
  const invalidLossTime = loss && (currentLossBinding === null || lossTimeBinding !== currentLossBinding || workDecisionLocalUtc(decisionTime) === null)
  const reasonLimit = workDecisionReasonLimit(kind)
  const batch = kind === 'purchasing.goods-receipt-batch.v1'
  const counterparty = isCounterpartyWorkDecisionKind(kind)
  const reasonRequired = (approve: boolean) => preview?.approvalReasonRequired || !approve && !isSalesWorkDecisionKind(kind) && !counterparty
  const invalidReason = (approve: boolean) => reasonRequired(approve) && reason.trim() === '' || (preview?.approvalReasonRequired || !approve || batch || counterparty) && (batch ? reason.length : reason.trim().length) > reasonLimit
  const submit = async (approve: boolean) => {
    if (preview === null || !approve && preview.rejectionSupported === false || frozen || approve && selectionInvalid || invalidLossTime || invalidReason(approve) || useDelegation && !delegationId) return
    const answer = await attempt.run({ expectedVersion: preview.sourceVersion, approve,
      reason: batch ? reason === '' ? null : reason : preview.approvalReasonRequired || !approve || counterparty ? reason.trim() || null : null, delegationId: useDelegation ? delegationId : null,
      ...(loss ? { inventoryTransferLoss: { requestNumber: preview.inventoryTransferLoss!.requestNumber, decidedAtUtc: workDecisionLocalUtc(decisionTime)! } } : {}),
      ...(kind === 'manufacturing.plan.v1' && options !== undefined ? { manufacturingPlanReapproval: { selectedLabelIds: [...selectedLabels] } } : {}) })
    if (answer.type === 'applied') void query.refetch()
  }
  const retry = async () => {
    const answer = await attempt.retry()
    if (answer.type === 'applied') void query.refetch()
  }
  const cost = preview?.rules.find(rule => rule.ruleCode === 'inventory.opening-cost-unit-cost')?.actual
  return <section className={styles.page} aria-labelledby="work-decision-heading"><h1 id="work-decision-heading">{T.title}</h1><p>{T.intro}</p>
    <Link to="/onaylarim/kuyruk">{T.back}</Link>
    {preview === null ? <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>}
      subject={T.title} onRetry={() => void query.refetch()} /> : <>
      <h2>{inboxKindText(kind)} · {preview.documentLabel}</h2><p>{T.version}: {preview.sourceVersion}</p>
      {kind === 'inventory.opening-cost.v1' ? <p>{cost == null ? T.hiddenCost : `${T.cost}: ${cost}`}</p> : null}
      <h3>{T.reference}</h3><ul>{preview.references.map((ref, i) => <li key={i}>{inboxRoleText(ref.role) ?? T.reference}: {ref.label}</li>)}</ul>
      {(options !== undefined || frozen && selected.length > 0) ? <fieldset disabled={frozen}><legend>{T.reapprovalLabels}</legend><p>{T.reapprovalHelp}</p>
        {(frozen ? selected : options ?? []).map(labelId => <label key={labelId}><input type="checkbox" disabled={frozen} checked={selected.includes(labelId)}
          onChange={event => {
            setSelectionVersion(preview.sourceVersion)
            setSelectedLabels(event.target.checked ? [...selected, labelId] : selected.filter(id => id !== labelId))
          }} />{labelId}</label>)}
        <Button type="button" onClick={() => { setSelectedLabels([]); setSelectionVersion(null) }}>{T.clearLabels}</Button>
      </fieldset> : null}
      {loss ? <div><p>{T.lossRequestNumber}: {lossRequest}</p>
        {frozen && attempt.pendingPayload?.inventoryTransferLoss ? <p>{T.lossFrozenTime}: {attempt.pendingPayload.inventoryTransferLoss.decidedAtUtc}</p>
          : <p>{T.lossRequestedAt}: {preview.inventoryTransferLoss?.requestedAtUtc}</p>}
      </div> : null}
      {preview.pending ? <fieldset disabled={frozen}>{preview.rejectionSupported === false ? <p>{kind === 'manufacturing.plan.v1' ? T.manufacturingReapprovalOnly : kind === 'purchasing.incoming-invoice-variance.v1' ? T.varianceApprovalOnly : T.approvalOnly}</p> : null}
        {preview.rejectionSupported !== false || preview.approvalReasonRequired ? <label>{counterparty ? T.optionalNote : kind === 'manufacturing.plan.v1' ? T.manufacturingRejectReason : T.reason}<textarea value={lossReason} maxLength={reasonLimit} onChange={event => setReason(event.target.value)} /></label> : null}
        {kind === 'purchasing.service-order.v1' || kind === 'purchasing.incoming-invoice-variance.v1' ? <p>{T.approvalReasonRequired(reasonLimit)}</p> : null}
        {kind === 'manufacturing.plan.v1' && preview.rejectionSupported ? <p>{T.manufacturingRejectHelp}</p> : null}
        {kind === 'purchasing.request.v1' ? <p>{T.rejectionReasonRequired(reasonLimit)}</p> : null}
        {counterparty ? <p>{T.optionalNoteHelp}</p> : null}
        {loss ? <><label>{T.lossDecisionTime}<input type="datetime-local" step="1" disabled={frozen} value={decisionTime} onChange={event => { setDecisionTime(event.target.value); setLossTimeBinding(currentLossBinding) }} /></label><p>{T.lossReason}</p></> : null}
        {batch ? <p>{T.batchReason(reasonLimit)}</p> : null}
        <label><input type="checkbox" checked={useDelegation} onChange={event => { setUseDelegation(event.target.checked); setDelegationId('') }} />{T.delegation}</label>
        {useDelegation ? <><label>{T.delegationChoice}<select value={delegationId} onChange={event => setDelegationId(event.target.value)}><option value="">{T.choose}</option>
          {candidates.map(row => <option key={row.id} value={row.id}>{delegationPersonLabel(people,row.delegatorUserId)} · {new Date(row.startsAtUtc).toLocaleString('tr-TR')} – {new Date(row.endsAtUtc).toLocaleString('tr-TR')} · {row.reason}</option>)}</select></label>
          {delegations.isPending ? <StateBlock state={{kind:'initial-loading'}} subject={T.delegationChoice} />
            : delegations.error instanceof DelegationReadFailure ? <StateBlock state={classifyOutcome(delegations.error.outcome) as RemoteViewState<unknown>} subject={T.delegationChoice}
              onRetry={() => void (pages.length === 0 ? delegations.refetch() : delegations.fetchNextPage())} />
            : candidates.length === 0 ? <p>{delegations.hasNextPage ? T.delegationNoMatchesYet : T.delegationEmpty}</p> : null}
          {delegations.hasNextPage ? <><p>{T.delegationMoreAvailable}</p><Button type="button" disabled={delegations.isFetchingNextPage}
            onClick={() => void delegations.fetchNextPage()}>{T.delegationMore}</Button></> : null}</> : null}
        <div className={styles.actions}><Button type="button" disabled={invalidLossTime || selectionInvalid || useDelegation && !delegationId || invalidReason(true)} onClick={() => void submit(true)}>{T.approve}</Button>
          {preview.rejectionSupported !== false ? <Button type="button" variant="secondary" disabled={invalidLossTime || useDelegation && !delegationId || invalidReason(false)} onClick={() => void submit(false)}>{T.reject}</Button> : null}</div></fieldset> : <p>{T.closed}</p>}
    </>}
    {attempt.status.kind === 'unknown' ? <div role="alert"><p>{T.unknown}</p><Button type="button" onClick={() => void retry()}>{T.retry}</Button></div> : null}
    {attempt.status.kind === 'refused' ? <p role="alert">{T.refused} ({attempt.status.status})</p> : null}
    {attempt.status.kind === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}
