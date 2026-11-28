import type {WorkSalesRemainderChangePreview} from './workSalesRemainderChangeDecision'
import {SALES_REMAINDER_CHANGE_WORK_TEXT as T} from './workSalesRemainderChangeText'
import styles from './Work.module.css'
type Row=readonly [string,string|number|boolean|null]
function Facts({rows}:{readonly rows:readonly Row[]}) {return <dl className={styles.facts}>{rows.map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value===null?T.none:typeof value==='boolean'?value?T.yes:T.no:String(value)}</dd></div>)}</dl>}
export function WorkSalesRemainderChangeFacts({human:h}:{readonly human:WorkSalesRemainderChangePreview}) {
 const r=h.change,l=h.line,f=T.fields
 return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p><h4>{T.change}</h4>
 <Facts rows={[[f.changeNo,r.changeNo],[f.requestRevision,r.revision],[f.lineId,r.lineId],[f.lineNo,r.lineNo],[f.kind,T.states[r.kind]],[f.changeState,T.states[r.state]],[f.orderedQuantity,r.orderedQuantity],[f.requestedQuantity,r.requestedQuantity],[f.reason,r.reason],[f.requestedBy,r.requestedBy],[f.requestedAtUtc,r.requestedAtUtc],[f.loadedAtRequest,r.loadedAtRequest],[f.shippedAtRequest,r.shippedAtRequest],[f.deliveredAtRequest,r.deliveredAtRequest],[f.decidedBy,r.decidedBy],[f.decidedAtUtc,r.decidedAtUtc],[f.decisionReason,r.decisionReason],[f.resultQuantity,r.resultQuantity],[f.loadedAtDecision,r.loadedAtDecision]]}/>
 <h4>{T.order}</h4><Facts rows={[[f.draftId,h.draftId],[f.documentNo,h.documentNo],[f.version,h.version],[f.revision,h.revision],[f.state,T.states[h.state]],[f.approvalState,T.states[h.approvalState]],[f.approvalSupported,h.approvalSupported],[f.rejectionSupported,h.rejectionSupported]]}/>
 <h4>{T.line}</h4><Facts rows={[[f.currentLineId,l.id],[f.lineNo,l.lineNo],[f.productId,l.productId],[f.productCode,l.productCode],[f.productName,l.productName],[f.orderUnitId,l.orderUnitId],[f.orderUnitCode,l.orderUnitCode],[f.currentQuantity,l.currentQuantity],[f.remainder,T.states[l.remainder]]]}/></section>
}
