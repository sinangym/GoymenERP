import type { WorkInventoryCostPreview } from './workInventoryCostDecision'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import { INVENTORY_COST_TEXT as C } from '../accounting/inventoryCostText'
import styles from './Work.module.css'

const labels={...T.invoiceLineCogsFields,...T.inventoryCostFields}
const object=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
// The transport is a closed typed whitelist. This presentation allowlist also excludes opaque proof fields.
export function WorkInventoryCostBusinessValues({value}:{readonly value:unknown}) {
  if(Array.isArray(value))return value.length===0?<p>{T.reversalAbsent}</p>:<ol>{value.map((row,index)=><li key={index}><WorkInventoryCostBusinessValues value={row}/></li>)}</ol>
  if(!object(value))return <>{typeof value==='boolean'?value?T.invoiceLineCogsYes:T.invoiceLineCogsNo:value===null||value===undefined?T.reversalAbsent:String(value)}</>
  if(Object.keys(value).length===2&&typeof value.numerator==='string'&&typeof value.denominator==='string')return <>{value.numerator} / {value.denominator}</>
  return <dl className={styles.facts}>{Object.entries(value).filter(([key])=>Object.hasOwn(labels,key)).map(([key,fact])=><div key={key}>
    <dt>{labels[key as keyof typeof labels]}</dt><dd>{key==='status'&&typeof fact==='string'?C.statuses[fact]??T.inventoryCostCancellationStatuses[fact as keyof typeof T.inventoryCostCancellationStatuses]??fact
      :key==='kind'?fact===1?C.salesTitle:C.differenceTitle:key==='classification'&&typeof fact==='number'?fact===1?C.trade:C.manufactured
      :key==='origin'&&typeof fact==='string'?T.inventoryCostOrigins[fact as keyof typeof T.inventoryCostOrigins]:<WorkInventoryCostBusinessValues value={fact}/>}</dd>
  </div>)}</dl>
}
export function WorkInventoryCostFacts({human}:{readonly human:WorkInventoryCostPreview}) {
  return <section aria-label={T.inventoryCostTitle}><h3>{T.inventoryCostTitle}</h3><p>{T.inventoryCostReadOnly}</p><WorkInventoryCostBusinessValues value={human}/></section>
}
