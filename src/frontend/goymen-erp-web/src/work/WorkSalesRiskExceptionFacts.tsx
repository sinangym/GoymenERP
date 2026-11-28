import type {WorkSalesRiskExceptionPreview} from './workSalesRiskExceptionDecision'
import {SALES_RISK_EXCEPTION_WORK_TEXT as T} from './workSalesRiskExceptionText'
import styles from './Work.module.css'
type Row=readonly [string,string|number|boolean|null]
function Facts({rows}:{readonly rows:readonly Row[]}) {return <dl className={styles.facts}>{rows.map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value===null?T.none:typeof value==='boolean'?value?T.yes:T.no:String(value)}</dd></div>)}</dl>}
export function WorkSalesRiskExceptionFacts({human:h}:{readonly human:WorkSalesRiskExceptionPreview}) {
 const r=h.exception,f=T.fields
 return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p><h4>{T.exception}</h4>
 <Facts rows={[[f.companyId,h.companyId],[f.exceptionId,h.exceptionId],[f.orderId,r.orderId],[f.exceptionNo,r.exceptionNo],[f.loadingInstructionId,r.loadingInstructionId],[f.state,T.states[r.state]],[f.version,r.version],[f.reason,r.reason],[f.requestedBy,r.requestedBy],[f.requestedAtUtc,r.requestedAtUtc],[f.decidedBy,r.decidedBy],[f.decidedAtUtc,r.decidedAtUtc],[f.rejectionReason,r.rejectionReason],[f.validOn,r.validOn],[f.isValidToday,r.isValidToday],[f.approvalSupported,h.approvalSupported],[f.rejectionSupported,h.rejectionSupported]]}/>
 <h4>{T.order}</h4><Facts rows={[[f.orderDocumentNo,h.orderDocumentNo],[f.orderVersion,h.orderVersion],[f.orderState,T.states[h.orderState]],[f.orderApprovalState,T.states[h.orderApprovalState]],[f.hasApprovedRevision,h.hasApprovedRevision]]}/></section>
}
