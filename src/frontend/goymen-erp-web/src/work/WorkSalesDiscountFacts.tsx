import type {WorkSalesDiscountPreview} from './workSalesDiscountDecision'
import {SALES_DISCOUNT_WORK_TEXT as T} from './workSalesDiscountText'
import styles from './Work.module.css'
type Value=string|number|boolean|null
type Row=readonly [string,Value]
function Facts({rows}:{readonly rows:readonly Row[]}){return <dl className={styles.facts}>{rows.map(([label,value])=><div key={label}><dt>{label}</dt><dd style={{whiteSpace:'pre-wrap'}}>{value===null?T.none:typeof value==='boolean'?value?T.yes:T.no:String(value)}</dd></div>)}</dl>}
export function WorkSalesDiscountFacts({human:h}:{readonly human:WorkSalesDiscountPreview}){
 const f=T.fields
 return <section aria-label={T.title}><h3>{T.title}</h3><p>{T.readOnly}</p>
 <Facts rows={[[f.companyId,h.companyId],[f.draftId,h.draftId],[f.documentNo,h.documentNo],[f.version,h.version],[f.revision,h.revision],[f.state,T.states[h.state]],[f.approvalState,T.states[h.approvalState]],[f.partyId,h.partyId],[f.companyAccountId,h.companyAccountId],[f.currencyId,h.currencyId],[f.businessDate,h.businessDate],[f.isSubcontractService,h.isSubcontractService],[f.priceDisclosure,h.priceDisclosure==='Visible'?T.visible:T.hidden],[f.approvalSupported,h.approvalSupported]]}/>
 {h.lines.map(l=><section key={l.lineId} aria-label={`${T.line} ${l.lineNo}`}><h4>{T.line} {l.lineNo}</h4>
 <Facts rows={[[f.lineId,l.lineId],[f.lineNo,l.lineNo],[f.productId,l.productId],[f.productVersion,l.productVersion],[f.productCode,l.productCode],[f.productName,l.productName],[f.orderUnitId,l.orderUnitId],[f.quantity,l.quantity]]}/>
 <h5>{T.discount}</h5>{l.discountCheck===null?<p>{T.none}</p>:<Facts rows={[[f.checkedBy,l.discountCheck.checkedBy],[f.checkedAtUtc,l.discountCheck.checkedAtUtc],[f.listUnitPrice,l.discountCheck.listUnitPrice],[f.limitPercent,l.discountCheck.limitPercent],[f.requiresApproval,l.discountCheck.requiresApproval],[f.approvedBy,l.discountCheck.approvedBy],[f.approvedAtUtc,l.discountCheck.approvedAtUtc],[f.reason,l.discountCheck.reason]]}/>}
 <h5>{T.pricing}</h5>{l.pricing===null?<p>{T.hidden}</p>:<>
 <Facts rows={[[f.lineId,l.pricing.lineId],[f.lineNo,l.pricing.lineNo],[f.unitPrice,l.pricing.unitPrice],[f.priceUnitId,l.pricing.priceUnitId],[f.priceSource,l.pricing.priceSource===null?null:T.priceSources[l.pricing.priceSource]],[f.discountPercent,l.pricing.discountPercent],[f.discountAmount,l.pricing.discountAmount],[f.surchargePercent,l.pricing.surchargePercent],[f.surchargeAmount,l.pricing.surchargeAmount],[f.netUnitPrice,l.pricing.netUnitPrice]]}/>
 <h5>{T.reference}</h5>{l.pricing.priceReference===null?<p>{T.none}</p>:<Facts rows={[[f.priceListId,l.pricing.priceReference.priceListId],[f.priceListName,l.pricing.priceReference.priceListName],[f.versionNo,l.pricing.priceReference.versionNo],[f.validFrom,l.pricing.priceReference.validFrom],[f.validThrough,l.pricing.priceReference.validThrough],[f.currencyCode,l.pricing.priceReference.currencyCode],[f.priceUnitId,l.pricing.priceReference.priceUnitId],[f.unitPrice,l.pricing.priceReference.unitPrice],[f.floorUnitPrice,l.pricing.priceReference.floorUnitPrice],[f.vatBasis,T.vatBasis],[f.lowerQuantity,l.pricing.priceReference.lowerQuantity],[f.upperQuantity,l.pricing.priceReference.upperQuantity],[f.tier,l.pricing.priceReference.tier===null?null:T.tiers[l.pricing.priceReference.tier]]]}/>}
 <h5>{T.exception}</h5>{l.pricing.floorException===null?<p>{T.none}</p>:<Facts rows={[[f.requestedBy,l.pricing.floorException.requestedBy],[f.requestedAtUtc,l.pricing.floorException.requestedAtUtc],[f.approvedBy,l.pricing.floorException.approvedBy],[f.approvedAtUtc,l.pricing.floorException.approvedAtUtc],[f.reason,l.pricing.floorException.reason]]}/>}
 </>}</section>)}</section>
}
