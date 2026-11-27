import type { WorkNormalJournalPreview } from './workNormalJournalDecision'
import { NORMAL_JOURNAL_TEXT as N } from '../accounting/accountingText'
import { WORK_DECISION_TEXT as T } from './workDecisionText'

export function WorkNormalJournalFacts({ human }: { readonly human: WorkNormalJournalPreview }) {
  const d = human.draft
  return <section aria-label={T.normalJournalProposal}><h3>{T.normalJournalProposal}</h3><dl>
    <dt>{T.normalJournalId}</dt><dd>{d.draftId}</dd><dt>{T.importCompany}</dt><dd>{d.companyId}</dd>
    <dt>{N.status}</dt><dd>{d.status === 'Draft' ? T.normalJournalDraft : T.normalJournalApproved}</dd>
    <dt>{N.version}</dt><dd>{d.version}</dd><dt>{N.sourceRef}</dt><dd>{d.sourceId}</dd>
    <dt>{N.postingRule}</dt><dd>{d.postingPurpose} · {d.postingRuleVersion}</dd>
    <dt>{T.normalJournalAccountPlan}</dt><dd>{d.accountPlanSourceVersion}</dd><dt>{T.importCurrency}</dt><dd>{d.currencyId}</dd>
    <dt>{N.transactionDate}</dt><dd>{d.transactionDate}</dd><dt>{N.accountingDate}</dt><dd>{d.accountingDate}</dd>
    <dt>{N.valueDate}</dt><dd>{d.valueDate ?? T.packageAbsent}</dd>
    <dt>{T.packagePreparedBy}</dt><dd>{d.preparedByUserId}</dd><dt>{T.packagePreparedAt}</dt><dd>{d.preparedAtUtc}</dd>
    <dt>{T.normalJournalApprover}</dt><dd>{d.approvedByUserId ?? T.packageAbsent}</dd><dt>{T.normalJournalApprovedAt}</dt><dd>{d.approvedAtUtc ?? T.packageAbsent}</dd>
    <dt>{N.period}</dt><dd>{d.periodId ?? T.packageAbsent}</dd><dt>{T.normalJournalPosted}</dt><dd>{d.postedJournalId ?? T.packageAbsent}</dd>
  </dl><table><thead><tr><th>{N.lineNumber}</th><th>{T.normalJournalMapping}</th><th>{N.lineAccount}</th><th>{N.lineSide}</th><th>{N.amount}</th></tr></thead>
    <tbody>{d.lines.map(line => <tr key={line.lineNumber}><td>{line.lineNumber}</td><td>{line.mappingKey}</td><td>{line.accountId}</td><td>{line.side === 'Debit' ? N.debit : N.credit}</td><td>{line.amount}</td></tr>)}</tbody>
  </table></section>
}
