import type {WorkSalesRequestPreview} from './workSalesRequestDecision'
import {SALES_REQUEST_WORK_TEXT as T} from './workSalesRequestText'
import {POOL_TEXT,REQUEST_STATE_TEXT} from '../sales/poolText'
import styles from './Work.module.css'
export function WorkSalesRequestFacts({human}:{readonly human:WorkSalesRequestPreview}) {
 const d=human.detail,h=d.request,n=POOL_TEXT.common,f=T.fields
 const headers=[[f.id,h.id],[f.companyId,h.companyId],[n.documentNo,h.documentNo],[n.state,REQUEST_STATE_TEXT[h.state]],[n.version,h.version],[f.partyId,h.partyId],
  [f.companyAccountId,h.companyAccountId],[n.businessDate,h.businessDate],[f.createdAtUtc,h.createdAtUtc],[f.lineCount,String(h.lineCount)],[n.requestedDeliveryDate,d.requestedDeliveryDate],[n.note,d.note]]
 return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p><h4>{T.header}</h4>
  <dl className={styles.facts}>{headers.map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value??T.none}</dd></div>)}</dl>
  <h4>{T.lines}</h4>{d.lines.map(l=><dl className={styles.facts} key={l.lineId}>{[
   [f.lineId,l.lineId],[f.lineNo,String(l.lineNo)],[f.productId,l.productId],[f.productVersion,l.productVersion],[f.productCode,l.productCode],[f.productName,l.productName],
   [f.orderUnitId,l.orderUnitId],[f.orderUnitCode,l.orderUnitCode],[f.quantity,l.quantity],[n.requestedDeliveryDate,l.requestedDeliveryDate],[n.note,l.note]
  ].map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value??T.none}</dd></div>)}</dl>)}
  <h4>{T.decision}</h4>{d.decision===null?<p>{T.none}</p>:<dl className={styles.facts}>{[[f.decidedBy,d.decision.decidedBy],[f.decidedAtUtc,d.decision.decidedAtUtc],[f.rejectionReason,d.decision.rejectionReason]].map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value??T.none}</dd></div>)}</dl>}
  {h.state==='Approved'?<p role="note">{T.approved}</p>:null}
 </section>
}
