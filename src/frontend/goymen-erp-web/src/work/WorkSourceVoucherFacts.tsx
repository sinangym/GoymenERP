import type { WorkSourceVoucherPreview } from './workSourceVoucherDecision'
import { SOURCE_VOUCHER_WORK_TEXT as T } from './workSourceVoucherDecision'
import { SOURCE_VOUCHER_TEXT as N } from '../accounting/sourceVoucherText'
import { REVERSAL_STATUS_LABEL } from '../accounting/accountingText'
import styles from './Work.module.css'

const headerFields=['id','companyId','documentKind','sourceDocumentId','invoiceId','finalizedVersion','sourceActorId','partyId','documentCurrencyId',
  'exchangeRate','documentDate','dueDate','status','version','mappingVersion','periodId','journalId','approvedBy','approvedAtUtc','cancellationReason',
  'officialInvoiceNo','functionalPayable','cancellationId'] as const
const lineFields=['lineNumber','mappingKey','side','accountId','functionalAmount','sourceLineNumber','vatRateCode','accountCode','accountName'] as const
const reversalFields=['proposalId','companyId','sourceJournalId','reversalJournalId','periodId','transactionDate','accountingDate','valueDate','reason',
  'proposedBy','proposedAtUtc','status','decidedBy','decidedAtUtc','decisionReason','version'] as const
export function WorkSourceVoucherFacts({human}:{readonly human:WorkSourceVoucherPreview}) {
  const v=human.voucher,reversal=v.reversalProposal
  return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p><dl className={styles.facts}>
    {headerFields.map(key=><div key={key}><dt>{T.fields[key]}</dt><dd>{key==='status'?N.statuses[v.status]:key==='documentKind'?v.documentKind==='sales-invoice'?N.sale:N.purchase:v[key]??T.absent}</dd></div>)}
    <div><dt>{T.fields.createdAtUtc}</dt><dd>{human.createdAtUtc}</dd></div>
    <div><dt>{T.fields.sourceCancelled}</dt><dd>{human.sourceCancelled?T.yes:T.no}</dd></div>
    <div><dt>{T.fields.periodOpen}</dt><dd>{human.periodOpen?T.yes:T.no}</dd></div>
  </dl><table aria-label={T.lines}><thead><tr>{lineFields.map(key=><th key={key} scope="col">{T.fields[key]}</th>)}</tr></thead>
    <tbody>{v.lines.map(line=><tr key={line.lineNumber}>{lineFields.map(key=><td key={key}>{key==='side'?line.side===1?N.debit:N.credit:line[key]??T.absent}</td>)}</tr>)}</tbody></table>
    {v.status==='PeriodWaiting'?<p role="note">{N.period}</p>:null}
    {reversal==null?null:<><h4>{T.reversal}</h4><dl className={styles.facts}>{reversalFields.map(key=><div key={key}><dt>{T.fields[key]}</dt>
      <dd>{key==='status'?REVERSAL_STATUS_LABEL[reversal.status]:reversal[key]??T.absent}</dd></div>)}</dl></>}
  </section>
}
