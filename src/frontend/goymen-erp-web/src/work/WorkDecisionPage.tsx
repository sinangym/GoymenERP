import { KIND_LABEL, TAX_IDENTITY_TEXT } from '../cari/taxIdentity'
import { LEGAL_NAME_TEXT } from '../cari/legalName'
import { PLAN_EXCESS_TEXT as PLAN_EXCESS } from '../logistics/planExcessText'
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
  const attempt = useScopedCommandAttempt<WorkDecisionRequest, WorkDecisionResult>(`work-decision:${kind}:${id}`,
    (payload, key) => sendWorkDecision(company!, kind, id, payload, key), payload => isWorkDecisionRequest(payload, kind))
  // A validated historical command resolves through its native receipt, not a newer maker's preview.
  const historicalRetry = (kind === 'inventory.transfer-loss.v1' || kind === 'inventory.transfer-overage.v1') && attempt.hasPendingRetry
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'work-decision', kind, id],
    enabled: company !== null && namespace !== null && !capabilityFailClosed(shell) && !historicalRetry, queryFn: async ({ signal }) => {
      const captured = boundary.capture(); const result = await fetchWorkDecision(company!, kind, id, signal)
      if (result.type === 'http') boundary.report(result.status, captured)
      return result
    } })
  if (company === null || capabilityFailClosed(shell)) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.title} />
  const preview = query.data?.type === 'ok-single' ? query.data.data : null
  const frozen = attempt.status.kind === 'busy' || attempt.hasPendingRetry
  const options = preview?.manufacturingPlanReapproval?.parentRollIds
  const selected = frozen ? attempt.pendingPayload?.manufacturingPlanReapproval?.selectedLabelIds ?? [] : selectionVersion === preview?.sourceVersion ? selectedLabels : []
  const selectionInvalid = options !== undefined && (selectionVersion !== preview?.sourceVersion || selectedLabels.length !== options.length
    || selectedLabels.some(id => !options.includes(id)))
  const loss = kind === 'inventory.transfer-loss.v1'
  const overage = kind === 'inventory.transfer-overage.v1'
  const timedTransfer = loss || overage
  const transferRequest = overage ? preview?.inventoryTransferOverage : preview?.inventoryTransferLoss
  const frozenTransferRequest = overage ? attempt.pendingPayload?.inventoryTransferOverage : attempt.pendingPayload?.inventoryTransferLoss
  const lossRequest = frozen ? frozenTransferRequest?.requestNumber : transferRequest?.requestNumber
  const lossReason = timedTransfer && frozen ? attempt.pendingPayload?.reason ?? '' : reason
  const currentLossBinding = transferRequest === undefined ? null : `${preview?.sourceVersion}:${transferRequest.requestNumber}`
  const selectedUtc = workDecisionLocalUtc(decisionTime)
  const invalidLossTime = timedTransfer && (currentLossBinding === null || lossTimeBinding !== currentLossBinding || selectedUtc === null
    || overage && transferRequest !== undefined && selectedUtc !== null && Date.parse(selectedUtc) < Date.parse(transferRequest.requestedAtUtc))
  const reasonLimit = workDecisionReasonLimit(kind)
  const batch = kind === 'purchasing.goods-receipt-batch.v1'
  const counterparty = isCounterpartyWorkDecisionKind(kind)
  const reasonRequired = (approve: boolean) => preview?.approvalReasonRequired || !approve && !isSalesWorkDecisionKind(kind) && !counterparty
  const invalidReason = (approve: boolean) => reasonRequired(approve) && reason.trim() === '' || (preview?.approvalReasonRequired || !approve || batch || counterparty) && (batch ? reason.length : reason.trim().length) > reasonLimit
  const submit = async (approve: boolean) => {
    if (preview === null || !approve && preview.rejectionSupported === false || frozen || approve && selectionInvalid || invalidLossTime || invalidReason(approve) || useDelegation && !delegationId) return
    const answer = await attempt.run({ expectedVersion: preview.sourceVersion, approve,
      reason: batch ? reason === '' ? null : reason : preview.approvalReasonRequired || !approve || counterparty ? reason.trim() || null : null, delegationId: useDelegation ? delegationId : null,
      ...(kind === 'logistics.plan-excess.v1' ? { logisticsPlanExcess: { expectedInstructionVersion: preview.logisticsPlanExcess!.instructionVersion } } : {}),
      ...(overage ? { inventoryTransferOverage: { requestNumber: preview.inventoryTransferOverage!.requestNumber, decidedAtUtc: workDecisionLocalUtc(decisionTime)! } } : {}),
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
    {preview === null ? historicalRetry ? <div><p>{T.priorDecisionPending}</p>
      <p>{T.version}: {attempt.pendingPayload?.expectedVersion}</p>
      <p>{overage ? T.overageRequestNumber : T.lossRequestNumber}: {frozenTransferRequest?.requestNumber}</p>
      <p>{T.lossFrozenTime}: {frozenTransferRequest?.decidedAtUtc}</p>
    </div> : <StateBlock state={query.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(query.data) as RemoteViewState<unknown>}
      subject={T.title} onRetry={() => void query.refetch()} /> : <>
      <h2>{inboxKindText(kind)} · {preview.documentLabel}</h2><p>{T.version}: {(kind === 'logistics.plan-excess.v1' || overage) && frozen ? attempt.pendingPayload?.expectedVersion : preview.sourceVersion}</p>
      {kind === 'inventory.opening-cost.v1' ? <p>{cost == null ? T.hiddenCost : `${T.cost}: ${cost}`}</p> : null}
      {preview.inventoryTransferOverage ? <section aria-label={T.overageProposal}><h3>{T.overageProposal}</h3><dl>
        <dt>{T.overageTransfer}</dt><dd>{preview.inventoryTransferOverage.transferId}</dd>
        <dt>{T.overageLine}</dt><dd>{preview.inventoryTransferOverage.lineId}</dd>
        <dt>{T.overageKilograms}</dt><dd>{preview.inventoryTransferOverage.kilograms}</dd>
        <dt>{T.overagePieces}</dt><dd>{preview.inventoryTransferOverage.pieceCount === null ? T.overageNoPieces : preview.inventoryTransferOverage.pieceCount}</dd>
        <dt>{T.overageProposalReason}</dt><dd>{preview.inventoryTransferOverage.reason}</dd>
        <dt>{T.overageRequestedBy}</dt><dd>{preview.inventoryTransferOverage.requestedBy}</dd>
        <dt>{T.overageReceivedBy}</dt><dd>{preview.inventoryTransferOverage.receivedBy}</dd>
      </dl></section> : null}
      {preview.logisticsPlanExcess ? <section aria-label={T.planExcessProposal}><h3>{T.planExcessProposal}</h3><dl>
        <dt>{PLAN_EXCESS.instruction}</dt><dd>{preview.logisticsPlanExcess.instructionId}</dd>
        <dt>{PLAN_EXCESS.line}</dt><dd>{preview.logisticsPlanExcess.lineId}</dd>
        <dt>{T.instructionVersion}</dt><dd>{frozen ? attempt.pendingPayload?.logisticsPlanExcess?.expectedInstructionVersion : preview.logisticsPlanExcess.instructionVersion}</dd>
        <dt>{PLAN_EXCESS.quantity}</dt><dd>{preview.logisticsPlanExcess.quantity}</dd>
        <dt>{PLAN_EXCESS.reason}</dt><dd>{preview.logisticsPlanExcess.reason}</dd>
        <dt>{PLAN_EXCESS.proposer}</dt><dd>{preview.logisticsPlanExcess.proposedBy}</dd>
        <dt>{PLAN_EXCESS.proposedAt}</dt><dd>{preview.logisticsPlanExcess.proposedAtUtc}</dd>
      </dl><p>{PLAN_EXCESS.warning}</p></section> : null}
      {preview.counterpartyTaxIdentityChange ? <section aria-label={T.identityProof}><h3>{T.identityProof}</h3><dl>
        <dt>{TAX_IDENTITY_TEXT.pendingCurrent}</dt><dd>{preview.counterpartyTaxIdentityChange.currentTaxIdentityNumber}</dd>
        <dt>{TAX_IDENTITY_TEXT.pendingProposed}</dt><dd>{preview.counterpartyTaxIdentityChange.proposedTaxIdentityNumber} ({KIND_LABEL[preview.counterpartyTaxIdentityChange.proposedKind]})</dd>
        <dt>{TAX_IDENTITY_TEXT.reasonLabel}</dt><dd>{preview.counterpartyTaxIdentityChange.reason}</dd>
      </dl></section> : null}
      {preview.counterpartyLegalNameChange ? <section aria-label={T.identityProof}><h3>{T.identityProof}</h3><dl>
        <dt>{LEGAL_NAME_TEXT.pendingCurrent}</dt><dd>{preview.counterpartyLegalNameChange.currentLegalName}</dd>
        <dt>{LEGAL_NAME_TEXT.proposedLabel}</dt><dd>{preview.counterpartyLegalNameChange.proposedLegalName}</dd>
        <dt>{LEGAL_NAME_TEXT.reasonLabel}</dt><dd>{preview.counterpartyLegalNameChange.reason}</dd>
      </dl></section> : null}
      <h3>{T.reference}</h3><ul>{preview.references.map((ref, i) => <li key={i}>{inboxRoleText(ref.role) ?? T.reference}: {ref.label}</li>)}</ul>
      {(options !== undefined || frozen && selected.length > 0) ? <fieldset disabled={frozen}><legend>{T.reapprovalLabels}</legend><p>{T.reapprovalHelp}</p>
        {(frozen ? selected : options ?? []).map(labelId => <label key={labelId}><input type="checkbox" disabled={frozen} checked={selected.includes(labelId)}
          onChange={event => {
            setSelectionVersion(preview.sourceVersion)
            setSelectedLabels(event.target.checked ? [...selected, labelId] : selected.filter(id => id !== labelId))
          }} />{labelId}</label>)}
        <Button type="button" onClick={() => { setSelectedLabels([]); setSelectionVersion(null) }}>{T.clearLabels}</Button>
      </fieldset> : null}
      {timedTransfer ? <div><p>{overage ? T.overageRequestNumber : T.lossRequestNumber}: {lossRequest}</p>
        {frozen && frozenTransferRequest ? <p>{T.lossFrozenTime}: {frozenTransferRequest.decidedAtUtc}</p>
          : <p>{T.lossRequestedAt}: {transferRequest?.requestedAtUtc}</p>}
      </div> : null}
      {preview.pending ? <fieldset disabled={frozen}>{preview.rejectionSupported === false ? <p>{kind === 'logistics.plan-excess.v1' ? T.planExcessApprovalOnly : kind === 'manufacturing.plan.v1' ? T.manufacturingReapprovalOnly : kind === 'purchasing.incoming-invoice-variance.v1' ? T.varianceApprovalOnly : T.approvalOnly}</p> : null}
        {preview.rejectionSupported !== false || preview.approvalReasonRequired ? <label>{counterparty ? T.optionalNote : kind === 'manufacturing.plan.v1' ? T.manufacturingRejectReason : T.reason}<textarea value={lossReason} maxLength={reasonLimit} onChange={event => setReason(event.target.value)} /></label> : null}
        {kind === 'purchasing.service-order.v1' || kind === 'purchasing.incoming-invoice-variance.v1' ? <p>{T.approvalReasonRequired(reasonLimit)}</p> : null}
        {kind === 'manufacturing.plan.v1' && preview.rejectionSupported ? <p>{T.manufacturingRejectHelp}</p> : null}
        {kind === 'purchasing.request.v1' ? <p>{T.rejectionReasonRequired(reasonLimit)}</p> : null}
        {counterparty ? <p>{T.optionalNoteHelp}</p> : null}
        {timedTransfer ? <><label>{T.lossDecisionTime}<input type="datetime-local" step="1" disabled={frozen} value={decisionTime} onChange={event => { setDecisionTime(event.target.value); setLossTimeBinding(currentLossBinding) }} /></label><p>{overage ? T.overageReasonHelp : T.lossReason}</p></> : null}
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
