import { useState } from 'react'
import { useShellState } from '../shell/shellContext'
import { capabilityFailClosed, contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { useBoundaryRead, stateOf } from '../settings/SalesPolicyParts'
import { useScopedCommandAttempt } from '../settings/useCommandAttempt'
import { fetchWorkApprovalRules, isRecordWorkApprovalRule, recordWorkApprovalRule } from '../settings/workApprovalRulesApi'
import { type WorkApprovalRule, type WorkApprovalRuleTable, type WorkApprovalRuleClass } from '../settings/workApprovalRulesContract'
import { ruleClassText } from '../settings/workApprovalRulesText'
import { inboxKindText, inboxPermissionText } from './workInboxText'
import { RULE_EDITOR_TEXT as T } from './WorkApprovalRuleEditorText'

/** Registered, not granted: ErpPermissions.WorkApprovalsEscalate. Other choices come from the owner's live rule table. */
const ESCALATION_PERMISSION = 'work.approvals.escalate'

export function WorkApprovalRuleEditor() {
  const shell = useShellState()
  const context = contextKey(shell)
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  if (context === null || company === null || capabilityFailClosed(shell) || !hasCapability(shell,'settings.work-approval-duration.manage')) return null
  const namespace = contextKeyString(context)
  return <Editor key={`${namespace}:${shell.routeEntryGeneration}`} company={company} namespace={namespace} generation={shell.routeEntryGeneration} />
}

function Editor({ company, namespace, generation }: { readonly company: string; readonly namespace: string; readonly generation: number }) {
  const [saved,setSaved] = useState<string | null>(null)
  // Shared key with WorkApprovalRulesSection: the strict table decoder and owner read remain the single source.
  const query = useBoundaryRead([namespace,generation,'work-approval-rules'],true,signal => fetchWorkApprovalRules(company,signal))
  const state = stateOf<WorkApprovalRuleTable>(query.isPending,query.data)
  if (state.kind !== 'ready') return null // The existing table section renders the read state and retry.
  const permissions = [...new Set([ESCALATION_PERMISSION,...state.data.rules.flatMap(row =>
    [row.approverPermission,...(row.escalationPermission === null ? [] : [row.escalationPermission])])])].sort()
  return <section aria-label={T.heading}><h2>{T.heading}</h2><p>{T.intro}</p>{saved === null ? null : <p role="status">{saved}</p>}
    {state.data.rules.map(rule => <RuleForm key={`${rule.kind}:${rule.version}:${rule.approverPermission}`} rule={rule} company={company}
      permissions={permissions} refresh={() => void query.refetch()} onSaved={setSaved} />)}
  </section>
}

function RuleForm({ rule, company, permissions, refresh, onSaved }: {
  readonly rule: WorkApprovalRule; readonly company: string; readonly permissions: readonly string[]; readonly refresh: () => void; readonly onSaved: (message: string) => void
}) {
  const shell = useShellState()
  const [duration,setDuration] = useState<WorkApprovalRuleClass>(rule.durationClass)
  const [escalation,setEscalation] = useState(rule.escalationPermission ?? '')
  const [reason,setReason] = useState('')
  const attempt = useScopedCommandAttempt(`work-approval-rule:record:${rule.kind}`,
    (payload,key) => recordWorkApprovalRule(company,rule.kind,payload,key),isRecordWorkApprovalRule)
  const frozen = attempt.status.kind === 'busy' || attempt.hasPendingRetry
  const conflict = attempt.status.kind === 'refused' && attempt.status.status === 409 && attempt.status.code !== 'work_approval_rule_unchanged'
  const allowed = !capabilityFailClosed(shell) && hasCapability(shell,'settings.work-approval-duration.manage')
    && shell.activeContext?.scopeMode === 'single' && shell.activeContext.companyId === company
  const shown = attempt.pendingPayload
  const settle = async (retry: boolean) => {
    if (!allowed) return
    const result = retry ? await attempt.retry() : await attempt.run({ expectedVersion: rule.version,
      durationClass: duration, escalationPermission: escalation || null, reason: reason.trim() })
    if (result.type === 'applied') { onSaved(result.replayed ? T.replayed : T.saved); refresh() }
  }
  return <details open={frozen || conflict || undefined}>
    <summary>{T.edit(inboxKindText(rule.kind))}</summary>
    <p>{T.approver}: {inboxPermissionText(rule.approverPermission)}</p>
    <p>{T.version}: <code>{shown?.expectedVersion ?? rule.version}</code></p>
    <fieldset disabled={frozen || conflict || !allowed}>
      <label>{T.duration}<select value={shown?.durationClass ?? duration} onChange={e => setDuration(e.target.value as WorkApprovalRuleClass)}>
        {(['Hours','BusinessDays'] as const).map(value => <option key={value} value={value}>{ruleClassText(value)}</option>)}
      </select></label>
      <label>{T.escalation}<select value={shown === null ? escalation : shown.escalationPermission ?? ''} onChange={e => setEscalation(e.target.value)}>
        {shown?.escalationPermission && !permissions.includes(shown.escalationPermission)
          ? <option value={shown.escalationPermission}>{inboxPermissionText(shown.escalationPermission)}</option> : null}
        <option value="">{T.none}</option>{permissions.map(permission => <option key={permission} value={permission}>
          {permission === ESCALATION_PERMISSION ? T.escalationDefault : inboxPermissionText(permission)}
        </option>)}
      </select></label>
      <label>{T.reason}<textarea maxLength={500} value={shown?.reason ?? reason} onChange={e => setReason(e.target.value)} /></label>
      <Button type="button" disabled={!reason.trim() || !permissions.includes(escalation) && escalation !== ''}
        onClick={() => void settle(false)}>{T.save}</Button>
    </fieldset>
    {attempt.status.kind === 'unknown' ? <div role="alert"><p>{T.unknown}</p><Button type="button" disabled={!allowed} onClick={() => void settle(true)}>{T.retry}</Button></div>
      : attempt.status.kind === 'busy' ? <p role="status">{T.busy}</p>
      : conflict ? <><StateBlock state={{kind:'conflict'}} subject={T.heading} /><p role="alert">{T.conflict}</p><Button type="button" onClick={refresh}>{T.refresh}</Button></>
      : attempt.status.kind === 'refused' ? <p role="alert">{T.refused(attempt.status.status)}</p> : null}
  </details>
}
