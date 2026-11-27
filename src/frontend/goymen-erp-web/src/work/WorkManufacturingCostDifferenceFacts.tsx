import type { WorkManufacturingCostDifferencePreview } from './workManufacturingCostDifferenceDecision'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import { COST_DIFFERENCE_TEXT as C } from '../accounting/manufacturingCostDifferenceText'
import styles from './Work.module.css'
const outputFields=['outputId','receiptMovementId','labelId','netQuantityKg','remainingQuantityKg','consumedQuantityKg','tlUnitCostPerKg',
  'remainingTlValue','consumedTlDifference','usdUnitCostPerKg','originalTlAmount'] as const
export function WorkManufacturingCostDifferenceFacts({human}:{readonly human:WorkManufacturingCostDifferencePreview}) {
  const p=human.proposal,a=p.accountPlan
  const fields={proposalId:p.proposalId,companyId:p.companyId,manufacturingOrderId:p.manufacturingOrderId,generationId:p.generationId,
    generationVersion:p.generationVersion,status:C.statuses[p.status],version:p.version,signedTlDifference:p.signedTlDifference,
    sourceOccurredAtUtc:p.sourceOccurredAtUtc,periodId:p.periodId,accountingDate:p.accountingDate,transactionDate:p.transactionDate,
    debitAccountId:p.debitAccountId,creditAccountId:p.creditAccountId,preparedBy:p.preparedBy,preparedAtUtc:human.preparedAtUtc,
    approvedBy:p.approvedBy,journalId:p.journalId,reason:p.reason}
  return <section aria-label={T.manufacturingCostTitle}><h3>{T.manufacturingCostTitle}</h3><p>{T.manufacturingCostReadOnly}</p>
    <dl className={styles.facts}>{Object.entries(fields).map(([key,value])=><div key={key}><dt>{T.manufacturingCostFields[key as keyof typeof T.manufacturingCostFields]}</dt><dd>{value??T.reversalAbsent}</dd></div>)}</dl>
    <h4>{T.manufacturingCostPlan}</h4><dl className={styles.facts}>
      <dt>{T.manufacturingCostFields.packageId}</dt><dd>{a.packageId}</dd><dt>{T.manufacturingCostFields.packageVersion}</dt><dd>{a.packageVersion}</dd>
      <dt>{T.manufacturingCostFields.catalogVersion}</dt><dd>{a.catalogVersion??T.reversalAbsent}</dd><dt>{T.manufacturingCostFields.sourceVersion}</dt><dd>{a.sourceVersion}</dd>
      <dt>{C.debit}</dt><dd>{a.debit.accountId} · {a.debit.code} · {a.debit.name}</dd>
      <dt>{C.credit}</dt><dd>{a.credit.accountId} · {a.credit.code} · {a.credit.name}</dd>
    </dl><table aria-label={T.manufacturingCostOutputs}><thead><tr>{outputFields.map(key=><th key={key}>{T.manufacturingCostFields[key]}</th>)}</tr></thead>
      <tbody>{p.outputs.map(row=><tr key={row.outputId}>{outputFields.map(key=><td key={key}>{row[key]??T.reversalAbsent}</td>)}</tr>)}</tbody></table>
  </section>
}
