/**
 * WP1 (YOL-D17, YOL-D18) — the read-only "Onay kural tablosu" section of AY-OS-01 (`settings.work-approval-duration.manage`):
 * one row per approval kind the Work inbox knows, with its owner module, the owner's approver permission, the duration class
 * and escalation permission in force, the version (0 = the decided default) and who recorded it, when and why. The section
 * reads on its own: its failure never hides the duration classes or the holiday calendar beside it. Nothing is computed or
 * decided here; changing a rule is the server's versioned command.
 */

import { StateBlock } from '../shell/StateBlock'
import { inboxKindText, inboxModuleText, inboxPermissionText } from '../work/workInboxText'
import { stateOf, useBoundaryRead } from './SalesPolicyParts'
import { fetchWorkApprovalRules } from './workApprovalRulesApi'
import { RULE_STATE_APPROVER_PERMISSION_CHANGED, type WorkApprovalRule, type WorkApprovalRuleTable } from './workApprovalRulesContract'
import { WORK_APPROVAL_RULES_TEXT as T, ruleClassText } from './workApprovalRulesText'
import styles from './settings.module.css'

const instantText = (value: string) => {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

export function WorkApprovalRulesSection({ companyId, namespace, routeEntryGeneration }: {
  readonly companyId: string
  readonly namespace: string | null
  readonly routeEntryGeneration: number
}) {
  const query = useBoundaryRead([namespace, routeEntryGeneration, 'work-approval-rules'], namespace !== null,
    (signal) => fetchWorkApprovalRules(companyId, signal))
  const state = stateOf<WorkApprovalRuleTable>(query.isPending, query.data)
  return <section aria-labelledby="work-approval-rules-heading" data-testid="work-approval-rules">
    <h2 id="work-approval-rules-heading">{T.heading}</h2>
    <p className={styles.muted}>{T.intro}</p>
    {state.kind !== 'ready'
      ? <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
      : state.data.rules.length === 0
        ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} />
        : <RuleTable rules={state.data.rules} />}
  </section>
}

function RuleTable({ rules }: { readonly rules: readonly WorkApprovalRule[] }) {
  return <div role="region" aria-label={T.caption} tabIndex={0}>
    <table className={styles.table} data-testid="work-approval-rules-table">
      <caption>{T.caption}</caption>
      <thead>
        <tr>
          <th scope="col">{T.kind}</th>
          <th scope="col">{T.owner}</th>
          <th scope="col">{T.approver}</th>
          <th scope="col">{T.durationClass}</th>
          <th scope="col">{T.escalation}</th>
          <th scope="col">{T.version}</th>
          <th scope="col">{T.recorded}</th>
          <th scope="col">{T.reason}</th>
        </tr>
      </thead>
      <tbody>
        {rules.map((rule) => <tr key={rule.kind} data-kind={rule.kind} data-state={rule.state}>
          <th scope="row">
            {inboxKindText(rule.kind)}
            {rule.state === RULE_STATE_APPROVER_PERMISSION_CHANGED
              ? <p role="note" data-testid="work-approval-rule-approver-changed">{T.approverChanged}</p>
              : null}
          </th>
          <td>{inboxModuleText(rule.ownerModule)}</td>
          <td>{inboxPermissionText(rule.approverPermission)}</td>
          <td>{ruleClassText(rule.durationClass)}</td>
          <td>{rule.escalationPermission === null ? T.escalationUndefined : inboxPermissionText(rule.escalationPermission)}</td>
          <td><span title={rule.versionText}>{rule.isDefault && rule.defaultBasis !== null ? T.defaultVersion(rule.defaultBasis) : rule.version}</span></td>
          <td>{rule.recordedBy === null || rule.recordedAtUtc === null
            ? '—'
            : <>{rule.recordedBy} · <time dateTime={rule.recordedAtUtc}>{instantText(rule.recordedAtUtc)}</time></>}</td>
          <td>{rule.reason ?? '—'}</td>
        </tr>)}
      </tbody>
    </table>
  </div>
}
