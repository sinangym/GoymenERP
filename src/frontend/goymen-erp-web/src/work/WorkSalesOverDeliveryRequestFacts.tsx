import type {WorkSalesOverDeliveryRequestPreview} from './workSalesOverDeliveryRequestDecision'
import {SALES_OVER_DELIVERY_REQUEST_WORK_TEXT as T} from './workSalesOverDeliveryRequestText'
import styles from './Work.module.css'
type Row=readonly [string,string|number|boolean|null]
function Facts({rows}:{readonly rows:readonly Row[]}) {return <dl className={styles.facts}>{rows.map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value===null?T.none:typeof value==='boolean'?value?T.yes:T.no:String(value)}</dd></div>)}</dl>}
export function WorkSalesOverDeliveryRequestFacts({human:h}:{readonly human:WorkSalesOverDeliveryRequestPreview}) {
 const r=h.request,o=h.order,l=o.line,f=T.fields
 return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p><h4>{T.request}</h4>
 <Facts rows={[[f.requestId,h.requestId],[f.kind,T.request],[f.subjectId,r.subjectId],[f.requestNo,r.requestNo],[f.subjectRevision,r.subjectRevision],[f.status,T.states[r.status]],[f.version,r.version],[f.lineId,r.lineId],[f.ordered,r.ordered],[f.quantity,r.quantity],[f.requestedTotal,r.requestedTotal],[f.loadingInstructionId,r.loadingInstructionId],[f.shipmentId,r.shipmentId],[f.note,r.note],[f.requestedBy,r.requestedBy],[f.requestedAtUtc,r.requestedAtUtc],[f.decidedBy,r.decidedBy],[f.decidedAtUtc,r.decidedAtUtc],[f.decisionReason,r.decisionReason],[f.approvalSupported,h.approvalSupported],[f.rejectionSupported,h.rejectionSupported]]}/>
 <h4>{T.order}</h4><Facts rows={[[f.id,o.id],[f.documentNo,o.documentNo],[f.orderVersion,o.version],[f.revision,o.revision],[f.state,T.states[o.state]],[f.approvalState,T.states[o.approvalState]],[f.partyId,o.partyId],[f.companyAccountId,o.companyAccountId],[f.currencyId,o.currencyId],[f.businessDate,o.businessDate]]}/>
 <h4>{T.line}</h4>{l===null?<p>{T.none}</p>:<Facts rows={[[f.currentLineId,l.id],[f.lineNo,l.lineNo],[f.productId,l.productId],[f.productCode,l.productCode],[f.productName,l.productName],[f.orderUnitId,l.orderUnitId],[f.orderUnitCode,l.orderUnitCode],[f.currentQuantity,l.currentQuantity]]}/>}</section>
}
