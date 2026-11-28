import type {CustomerReturnAmounts} from '../sales/customerReturnContract'
import {CUSTOMER_RETURN_STATE_TEXT} from '../sales/customerReturnText'
import type {WorkSalesCustomerReturnPreview} from './workSalesCustomerReturnDecision'
import {SALES_CUSTOMER_RETURN_WORK_TEXT as T} from './workSalesCustomerReturnText'
import styles from './Work.module.css'
type Row = readonly [string, string | number | boolean | null]
function Facts({rows}: {readonly rows: readonly Row[]}) {return <dl className={styles.facts}>{rows.map(([label, value]) => <div key={label}><dt>{label}</dt><dd style={{whiteSpace: 'pre-wrap'}}>{value === null ? T.none : typeof value === 'boolean' ? value ? T.yes : T.no : String(value)}</dd></div>)}</dl>}
function Amounts({value, heading}: {readonly value: CustomerReturnAmounts | null; readonly heading: string}) {
  const f = T.fields
  return <section aria-label={heading}><h4>{heading}</h4>{value === null ? <p>{T.none}</p> : <Facts rows={[[f.netAmount, value.netAmount], [f.vatAmount, value.vatAmount], [f.withheldVatAmount, value.withheldVatAmount], [f.lineTotalAmount, value.lineTotalAmount], [f.derivedPayableAmount, value.derivedPayableAmount], [f.driftAmount, value.driftAmount]]}/>}</section>
}
export function WorkSalesCustomerReturnFacts({human}: {readonly human: WorkSalesCustomerReturnPreview}) {
  const c = human.customerReturn, f = T.fields
  return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p>
    <Facts rows={[[f.id,c.id],[f.state,CUSTOMER_RETURN_STATE_TEXT[c.state]],[f.version,c.version],[f.originalInvoiceId,c.originalInvoiceId],[f.originalDocumentNo,c.originalDocumentNo],
      [f.shipmentId,c.shipmentId],[f.customerDocumentNo,c.customerDocumentNo],[f.customerDocumentDate,c.customerDocumentDate],[f.partyId,c.partyId],[f.companyAccountId,c.companyAccountId],
      [f.currencyId,c.currencyId],[f.amountScale,c.amountScale],[f.rate,c.rate],[f.functionalCurrencyId,c.functionalCurrencyId],[f.functionalScale,c.functionalScale],[f.needsReview,c.needsReview],
      [f.createdBy,c.createdBy],[f.createdAtUtc,c.createdAtUtc],[f.submittedBy,c.submittedBy],[f.submittedAtUtc,c.submittedAtUtc],[f.approvedBy,c.approvedBy],[f.approvedAtUtc,c.approvedAtUtc],
      [f.finalizedBy,c.finalizedBy],[f.finalizedAtUtc,c.finalizedAtUtc],[f.rejectedBy,c.rejectedBy],[f.rejectedAtUtc,c.rejectedAtUtc],[f.rejectionReason,c.rejectionReason],
      [f.cancelledBy,c.cancelledBy],[f.cancelledAtUtc,c.cancelledAtUtc],[f.cancellationReason,c.cancellationReason],[f.updatedAtUtc,c.updatedAtUtc],
      [f.approvalSupported,human.approvalSupported],[f.rejectionSupported,human.rejectionSupported]]}/>
    <Amounts value={c.totals} heading={T.totals}/><Amounts value={c.functionalTotals} heading={T.functionalTotals}/>
    <section aria-label={T.lines}><h4>{T.lines}</h4>{c.lines.map(l => <article key={l.lineNo} aria-label={`${T.line} ${l.lineNo}`}>
      <Facts rows={[[f.lineNo,l.lineNo],[f.originalInvoiceLineId,l.originalInvoiceLineId],[f.originalLineNo,l.originalLineNo],[f.shipmentId,l.shipmentId],[f.productId,l.productId],
        [f.unitId,l.unitId],[f.quantity,l.quantity],[f.netUnitPrice,l.netUnitPrice],[f.vatPercent,l.vatPercent],[f.withholdingShare,l.withholdingShare],[f.isLastReturn,l.isLastReturn],[f.needsReview,l.needsReview]]}/>
      <Amounts value={l.amounts} heading={T.amounts}/><Amounts value={l.functionalAmounts} heading={T.functionalAmounts}/>
    </article>)}</section>
  </section>
}
