import type {WorkSalesQuoteConversionPreview} from './workSalesQuoteConversionDecision'
import {SALES_QUOTE_CONVERSION_WORK_TEXT as T} from './workSalesQuoteConversionText'
import styles from './Work.module.css'
type Value=string|number|boolean|null
type Row=readonly [string,Value]
function Facts({rows}:{readonly rows:readonly Row[]}){return <dl className={styles.facts}>{rows.map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value===null?T.none:typeof value==='boolean'?value?T.yes:T.no:String(value)}</dd></div>)}</dl>}
export function WorkSalesQuoteConversionFacts({human:h}:{readonly human:WorkSalesQuoteConversionPreview}) {
 const d=h.quote,q=d.quote,r=h.request,f=T.fields
 return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p><h4>{T.request}</h4>
 <Facts rows={[[f.requestId,h.requestId],[f.kind,T.request],[f.subjectId,r.subjectId],[f.requestNo,r.requestNo],[f.subjectRevision,r.subjectRevision],[f.status,T.states[r.status]],[f.version,r.version],
 [f.lineId,r.lineId],[f.ordered,r.ordered],[f.quantity,r.quantity],[f.requestedTotal,r.requestedTotal],[f.loadingInstructionId,r.loadingInstructionId],[f.shipmentId,r.shipmentId],[f.note,r.note],[f.requestedBy,r.requestedBy],[f.requestedAtUtc,r.requestedAtUtc],[f.decidedBy,r.decidedBy],[f.decidedAtUtc,r.decidedAtUtc],[f.decisionReason,r.decisionReason],[f.approvalSupported,h.approvalSupported],[f.rejectionSupported,h.rejectionSupported]]}/>
 <h4>{T.quote}</h4><Facts rows={[[f.id,q.id],[f.companyId,q.companyId],[f.documentNo,q.documentNo],[f.state,T.states[q.state]],[f.version,q.version],[f.revision,q.revision],[f.partyId,q.partyId],[f.companyAccountId,q.companyAccountId],[f.currencyId,q.currencyId],[f.businessDate,q.businessDate],[f.validUntil,q.validUntil],[f.createdAtUtc,q.createdAtUtc],[f.lineCount,q.lineCount],[f.convertedDraftId,q.convertedDraftId],[f.requestedDeliveryDate,d.requestedDeliveryDate],[f.note,d.note],[f.createdBy,d.createdBy]]}/>
 <h4>{T.requests}</h4>{d.requestIds.length?<ul>{d.requestIds.map(id=><li key={id}>{id}</li>)}</ul>:<p>{T.none}</p>}
 <h4>{T.lines}</h4>{d.lines.map(l=><Facts key={l.lineId} rows={[[f.lineId,l.lineId],[f.lineNo,l.lineNo],[f.productId,l.productId],[f.productVersion,l.productVersion],[f.productCode,l.productCode],[f.productName,l.productName],[f.orderUnitId,l.orderUnitId],[f.orderUnitCode,l.orderUnitCode],[f.quantity,l.quantity],
 [f.unitPrice,l.priceDisclosure==='Withheld'?T.hidden:l.unitPrice],[f.priceUnitId,l.priceDisclosure==='Withheld'?T.hidden:l.priceUnitId],[f.discountPercent,l.priceDisclosure==='Withheld'?T.hidden:l.discountPercent],[f.priceDisclosure,l.priceDisclosure==='Visible'?T.visible:T.hidden],[f.requestedDeliveryDate,l.requestedDeliveryDate],[f.note,l.note]]}/>)}
 <h4>{T.revisions}</h4>{d.revisions.map(x=><Facts key={x.revision} rows={[[f.revision,x.revision],[f.validUntil,x.validUntil],[f.revisedBy,x.revisedBy],[f.revisedAtUtc,x.revisedAtUtc]]}/>)}
 <h4>{T.approvals}</h4>{d.expiryApprovals.length?d.expiryApprovals.map(x=><Facts key={x.revision} rows={[[f.revision,x.revision],[f.validUntil,x.validUntil],[f.businessDay,x.businessDay],[f.approvedBy,x.approvedBy],[f.approvedAtUtc,x.approvedAtUtc],[f.note,x.note]]}/>):<p>{T.none}</p>}
 <h4>{T.expiry}</h4><Facts rows={[[f.businessDay,d.expiry.businessDay],[f.isExpired,d.expiry.isExpired],[f.isApproved,d.expiry.isApproved]]}/>
 <h4>{T.loss}</h4>{d.loss?<Facts rows={[[f.reason,d.loss.reason],[f.lostBy,d.loss.lostBy],[f.lostAtUtc,d.loss.lostAtUtc],[f.reasonId,d.loss.reasonId],[f.reasonCode,d.loss.reasonCode],[f.reasonLabel,d.loss.reasonLabel]]}/>:<p>{T.none}</p>}
 <h4>{T.conversion}</h4>{d.conversion?<Facts rows={[[f.draftId,d.conversion.draftId],[f.convertedBy,d.conversion.convertedBy],[f.convertedAtUtc,d.conversion.convertedAtUtc]]}/>:<p>{T.none}</p>}
 <h4>{T.terms}</h4>{d.dueTermsDisclosure==='Withheld'?<p>{T.hidden}</p>:d.dueTerms===null?<p>{T.none}</p>:d.dueTerms.instalments.map(x=><Facts key={x.sequenceNo} rows={[[f.sequenceNo,x.sequenceNo],[f.basis,T.bases[x.basis]],[f.daysAfter,x.daysAfter],[f.sharePercent,x.sharePercent]]}/>)}
 {r.status==='Approved'?<p role="note">{T.approved}</p>:null}</section>
}
