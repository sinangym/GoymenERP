import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, capabilityFailClosed } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useScopedCommandAttempt } from '../settings/useCommandAttempt'
import { Button } from '../ui/Button'
import { inboxKindText, inboxRoleText } from './workInboxText'
import { fetchWorkDecision, sendWorkDecision, isWorkDecisionRequest, type WorkDecisionKind } from './workDecisionApi'
import { fetchDelegations } from './workDelegationApi'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import styles from './Work.module.css'

export function WorkDecisionPage({ kind }: { readonly kind: WorkDecisionKind }) {
  const shell = useShellState(); const context = contextKey(shell); const { approvalId = '' } = useParams()
  return <Decision key={`${context === null ? 'none' : contextKeyString(context)}:${shell.routeEntryGeneration}:${kind}:${approvalId}`} kind={kind} id={approvalId} />
}
function Decision({ kind, id }: { readonly kind: WorkDecisionKind; readonly id: string }) {
  const shell = useShellState(); const context = contextKey(shell); const boundary = useAuthBoundary()
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const namespace = context === null ? null : contextKeyString(context)
  const [reason, setReason] = useState('')
  const [useDelegation, setUseDelegation] = useState(false); const [delegationId, setDelegationId] = useState('')
  const delegations = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'work-decision-delegations'],
    enabled: useDelegation && company !== null && namespace !== null && !capabilityFailClosed(shell), queryFn: async ({ signal }) => {
      const captured = boundary.capture(); const answer = await fetchDelegations(company!, false, 0, signal)
      if (answer.type === 'http') boundary.report(answer.status, captured)
      return answer
    } })
  const candidates = delegations.data?.type === 'ok-single' ? delegations.data.data.items.filter(row => row.kind === kind
    && row.delegateUserId === shell.activeContext?.userId && row.revokedAtUtc === null) : []
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'work-decision', kind, id],
    enabled: company !== null && namespace !== null && !capabilityFailClosed(shell), queryFn: async ({ signal }) => {
      const captured = boundary.capture(); const result = await fetchWorkDecision(company!, kind, id, signal)
      if (result.type === 'http') boundary.report(result.status, captured)
      return result
    } })
  const attempt = useScopedCommandAttempt(`work-decision:${kind}:${id}`,
    (payload, key) => sendWorkDecision(company!, kind, id, payload, key), isWorkDecisionRequest)
  if (company === null || capabilityFailClosed(shell)) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.title} />
  const preview = query.data?.type === 'ok-single' ? query.data.data : null
  const frozen = attempt.status.kind === 'busy' || attempt.hasPendingRetry
  const submit = async (approve: boolean) => {
    if (preview === null || frozen) return
    const answer = await attempt.run({ expectedVersion: preview.sourceVersion, approve,
      reason: preview.approvalReasonRequired || !approve ? reason.trim() : null, delegationId: useDelegation ? delegationId : null })
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
      {preview.pending ? <fieldset disabled={frozen}><label>{T.reason}<textarea value={reason} maxLength={500} onChange={event => setReason(event.target.value)} /></label>
        <label><input type="checkbox" checked={useDelegation} onChange={event => { setUseDelegation(event.target.checked); setDelegationId('') }} />{T.delegation}</label>
        {useDelegation ? <><label>{T.delegationChoice}<select value={delegationId} onChange={event => setDelegationId(event.target.value)}><option value="">{T.choose}</option>
          {candidates.map(row => <option key={row.id} value={row.id}>{new Date(row.startsAtUtc).toLocaleString('tr-TR')} – {new Date(row.endsAtUtc).toLocaleString('tr-TR')} · {row.reason}</option>)}</select></label>
          {delegations.data !== undefined && delegations.data.type !== 'ok-single' ? <StateBlock state={classifyOutcome(delegations.data) as RemoteViewState<unknown>} subject={T.delegationChoice} onRetry={() => void delegations.refetch()} /> : candidates.length === 0 ? <p>{T.delegationEmpty}</p> : null}</> : null}
        <div className={styles.actions}><Button type="button" disabled={useDelegation && !delegationId || preview.approvalReasonRequired && reason.trim() === ''} onClick={() => void submit(true)}>{T.approve}</Button>
          <Button type="button" variant="secondary" disabled={useDelegation && !delegationId || reason.trim() === ''} onClick={() => void submit(false)}>{T.reject}</Button></div></fieldset> : <p>{T.closed}</p>}
    </>}
    {attempt.status.kind === 'unknown' ? <div role="alert"><p>{T.unknown}</p><Button type="button" onClick={() => void attempt.retry()}>{T.retry}</Button></div> : null}
    {attempt.status.kind === 'refused' ? <p role="alert">{T.refused} ({attempt.status.status})</p> : null}
    {attempt.status.kind === 'applied' ? <p role="status">{T.done}</p> : null}
  </section>
}
