import {decodeDraftLinePricing,type LinePricing,type PriceFloorException,type DiscountLimitCheck} from '../sales/linePricing'
import {hasExactKeys} from '../finance/wireShape'
import {isBoundUuid} from '../finance/contract'
import {salesRequestVersion,salesRequestIncrementable} from './workSalesRequestDecision'
export const SALES_DISCOUNT_WORK_KIND='sales.discount-exception.v1'
export const SALES_DISCOUNT_WORK_CAPABILITY='sales.discount-exception.work-decision'
export interface WorkSalesDiscountLine {readonly lineId:string;readonly lineNo:number;readonly productId:string;readonly productVersion:string;readonly productCode:string;readonly productName:string;readonly orderUnitId:string;readonly quantity:string;readonly discountCheck:DiscountLimitCheck|null;readonly pricing:LinePricing|null}
export interface WorkSalesDiscountPreview {readonly companyId:string;readonly draftId:string;readonly documentNo:string;readonly version:string;readonly revision:number;readonly state:'Draft'|'Closed'|'Cancelled';readonly approvalState:'Preparing'|'PendingApproval'|'Approved'|'Rejected'|'Withdrawn';readonly partyId:string;readonly companyAccountId:string;readonly currencyId:string;readonly businessDate:string;readonly isSubcontractService:boolean;readonly lines:readonly WorkSalesDiscountLine[];readonly priceDisclosure:'Visible'|'Withheld';readonly approvalSupported:boolean}
export interface SalesDiscountSelection {readonly lines:readonly {readonly lineId:string;readonly reason:string}[]}
export interface SalesDiscountWorkCommand {readonly expectedVersion:string;readonly approve:true;readonly reason:null;readonly delegationId:string|null;readonly salesDiscount:SalesDiscountSelection}
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''
const actor=(v:unknown):v is string=>text(v)&&v===v.trim()&&v.length<=128
const int32=(v:unknown,min=1):v is number=>typeof v==='number'&&Number.isInteger(v)&&v>=min&&v<=2147483647
// Validate the native text representation without converting quantities/prices, calculating or changing their scale.
const decimal=(v:unknown,nativeAmount:(v:unknown)=>boolean):v is string=>typeof v==='string'&&v===v.trim()&&!/[\u0000-\u001f\u007f]/.test(v)&&v.length<=64&&/^[0-9]+(?:\.[0-9]+)?$/.test(v)&&nativeAmount(v)
const nullableDecimal=(v:unknown,nativeAmount:(v:unknown)=>boolean)=>v===null||decimal(v,nativeAmount)
const utc=(v:unknown,date:(v:unknown)=>boolean):v is string=>typeof v==='string'&&v===v.trim()&&date(v.slice(0,10))&&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
export const validDiscountReason=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''&&v.trim().length<=4000
function exception(v:unknown,date:(v:unknown)=>boolean):v is PriceFloorException {
 if(!record(v)||!hasExactKeys(v,['requestedBy','requestedAtUtc','approvedBy','approvedAtUtc','reason'])||!actor(v.requestedBy)||!utc(v.requestedAtUtc,date))return false
 return v.approvedBy===null&&v.approvedAtUtc===null&&v.reason===null||actor(v.approvedBy)&&v.approvedBy!==v.requestedBy&&utc(v.approvedAtUtc,date)&&validDiscountReason(v.reason)&&v.reason===v.reason.trim()
}
const checkKeys=['checkedBy','checkedAtUtc','listUnitPrice','limitPercent','requiresApproval','approvedBy','approvedAtUtc','reason'] as const
function check(v:unknown,date:(v:unknown)=>boolean,nativeAmount:(v:unknown)=>boolean):v is DiscountLimitCheck {
 if(!record(v)||!hasExactKeys(v,checkKeys)||!actor(v.checkedBy)||!utc(v.checkedAtUtc,date)||!nullableDecimal(v.listUnitPrice,nativeAmount)||!nullableDecimal(v.limitPercent,nativeAmount)||typeof v.requiresApproval!=='boolean')return false
 return v.approvedBy===null&&v.approvedAtUtc===null&&v.reason===null||v.requiresApproval&&actor(v.approvedBy)&&utc(v.approvedAtUtc,date)&&validDiscountReason(v.reason)&&v.reason===v.reason.trim()
}
const sameCheck=(a:unknown,b:unknown)=>a===null?b===null:record(a)&&record(b)&&checkKeys.every(k=>a[k]===b[k])
function pricing(v:unknown,line:Record<string,unknown>,date:(v:unknown)=>boolean,version:string,nativeAmount:(v:unknown)=>boolean):LinePricing|null {
 if(!record(v)||!hasExactKeys(v,['lineId','lineNo','unitPrice','priceUnitId','priceSource','priceReference','discountPercent','discountAmount','surchargePercent','surchargeAmount','netUnitPrice','floorException','discountCheck'])
  ||!same(v.lineId,line.lineId as string)||v.lineNo!==line.lineNo||v.priceUnitId!==null&&!guid(v.priceUnitId)
  ||!['unitPrice','discountPercent','discountAmount','surchargePercent','surchargeAmount','netUnitPrice'].every(k=>nullableDecimal(v[k],nativeAmount))
  ||v.floorException!==null&&!exception(v.floorException,date)||!sameCheck(v.discountCheck,line.discountCheck))return null
 if(v.priceReference!==null){const r=v.priceReference;if(!record(r)||!guid(r.priceListId)||!guid(r.priceUnitId)||!int32(r.versionNo)||!date(r.validFrom)||r.validThrough!==null&&!date(r.validThrough)
  ||!['unitPrice','floorUnitPrice','lowerQuantity','upperQuantity'].every(k=>nullableDecimal(r[k],nativeAmount)))return null}
 if(v.discountCheck!==null&&!check(v.discountCheck,date,nativeAmount))return null
 // Reuse the unchanged native line decoder. The withheld margin is only its validation context, never Work data.
 return decodeDraftLinePricing({version,lines:[v],margin:{disclosure:'Withheld',costBasis:null,kind:null,lines:null}})?.lines[0]??null
}
export function decodeWorkSalesDiscountPreview(v:unknown,company:string,id:string,date:(v:unknown)=>boolean,nativeAmount:(v:unknown)=>boolean):WorkSalesDiscountPreview|null {
 if(!record(v)||!hasExactKeys(v,['companyId','draftId','documentNo','version','revision','state','approvalState','partyId','companyAccountId','currencyId','businessDate','isSubcontractService','lines','priceDisclosure','approvalSupported'])
  ||!guid(company)||!guid(id)||!same(v.companyId,company)||!same(v.draftId,id)||!text(v.documentNo)||v.documentNo!==v.documentNo.trim()||v.documentNo.length>200
  ||!salesRequestVersion(v.version)||!int32(v.revision,0)||v.state!=='Draft'&&v.state!=='Closed'&&v.state!=='Cancelled'
  ||v.approvalState!=='Preparing'&&v.approvalState!=='PendingApproval'&&v.approvalState!=='Approved'&&v.approvalState!=='Rejected'&&v.approvalState!=='Withdrawn'
  ||![v.partyId,v.companyAccountId,v.currencyId].every(guid)||!date(v.businessDate)||v.priceDisclosure!=='Visible'&&v.priceDisclosure!=='Withheld'
  ||typeof v.isSubcontractService!=='boolean'||typeof v.approvalSupported!=='boolean'||!Array.isArray(v.lines)||v.lines.length===0||v.lines.length>500)return null
 const lines:WorkSalesDiscountLine[]=[]
 for(const l of v.lines){
  if(!record(l)||!hasExactKeys(l,['lineId','lineNo','productId','productVersion','productCode','productName','orderUnitId','quantity','discountCheck','pricing'])
   ||![l.lineId,l.productId,l.orderUnitId].every(guid)||!int32(l.lineNo)||!salesRequestVersion(l.productVersion)||!text(l.productCode)||!text(l.productName)||!decimal(l.quantity,nativeAmount)||!/[1-9]/.test(l.quantity)
   ||l.discountCheck!==null&&!check(l.discountCheck,date,nativeAmount)||v.priceDisclosure==='Withheld'&&record(l.discountCheck)&&l.discountCheck.listUnitPrice!==null)return null
  const p=v.priceDisclosure==='Visible'?pricing(l.pricing,l,date,v.version,nativeAmount):null
  if(v.priceDisclosure==='Visible'?p===null:l.pricing!==null)return null
  lines.push({lineId:l.lineId as string,lineNo:l.lineNo,productId:l.productId as string,productVersion:l.productVersion,productCode:l.productCode,productName:l.productName,orderUnitId:l.orderUnitId as string,quantity:l.quantity,discountCheck:l.discountCheck as DiscountLimitCheck|null,pricing:p})
 }
 if(new Set(lines.map(l=>l.lineId.toLowerCase())).size!==lines.length||new Set(lines.map(l=>l.lineNo)).size!==lines.length)return null
 const required=lines.filter(l=>l.discountCheck?.requiresApproval===true)
 const pending=required.filter(l=>l.discountCheck!.approvedBy===null)
 if(v.approvalSupported&&(v.isSubcontractService||required.length!==pending.length||!salesRequestIncrementable(v.version)||v.state!=='Draft'||v.approvalState!=='Preparing'&&v.approvalState!=='Rejected'||pending.length===0))return null
 return {companyId:v.companyId as string,draftId:v.draftId as string,documentNo:v.documentNo,version:v.version,revision:v.revision,state:v.state,approvalState:v.approvalState,partyId:v.partyId as string,companyAccountId:v.companyAccountId as string,currencyId:v.currencyId as string,businessDate:v.businessDate as string,isSubcontractService:v.isSubcontractService,lines,priceDisclosure:v.priceDisclosure,approvalSupported:v.approvalSupported}
}
export const isSalesDiscountWorkCommand=(v:unknown):v is SalesDiscountWorkCommand=>record(v)&&hasExactKeys(v,['expectedVersion','approve','reason','delegationId','salesDiscount'])
 &&salesRequestIncrementable(v.expectedVersion)&&v.approve===true&&v.reason===null&&(v.delegationId===null||guid(v.delegationId))&&record(v.salesDiscount)&&hasExactKeys(v.salesDiscount,['lines'])
 &&Array.isArray(v.salesDiscount.lines)&&v.salesDiscount.lines.length>0&&v.salesDiscount.lines.length<=500
 &&v.salesDiscount.lines.every(l=>record(l)&&hasExactKeys(l,['lineId','reason'])&&guid(l.lineId)&&validDiscountReason(l.reason))
 &&new Set(v.salesDiscount.lines.map(l=>(l as {lineId:string}).lineId.toLowerCase())).size===v.salesDiscount.lines.length
export function salesDiscountWorkCommand(h:WorkSalesDiscountPreview,reasons:Readonly<Record<string,string>>,delegationId:string|null):SalesDiscountWorkCommand|null {
 if(!h.approvalSupported)return null
 const lines=h.lines.filter(l=>l.discountCheck?.requiresApproval===true&&l.discountCheck.approvedBy===null).map(l=>({lineId:l.lineId,reason:reasons[l.lineId]??''}))
 const body={expectedVersion:h.version,approve:true as const,reason:null,delegationId,salesDiscount:{lines}}
 return isSalesDiscountWorkCommand(body)?body:null
}
export function validSalesDiscountPreview(v:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>boolean,nativeAmount:(v:unknown)=>boolean):boolean {
 const h=decodeWorkSalesDiscountPreview(v.salesDiscount,company,id,date,nativeAmount)
 if(h===null||!hasExactKeys(v,['companyId','kind','itemId','documentLabel','sourceVersion','pending','approvalReasonRequired','references','rules','approvalSupported','rejectionSupported','salesDiscount'])
  ||!same(v.companyId,company)||!same(v.itemId,id)||v.kind!==SALES_DISCOUNT_WORK_KIND||v.documentLabel!==h.documentNo||v.sourceVersion!==h.version||v.pending!==h.approvalSupported
  ||v.approvalSupported!==h.approvalSupported||v.rejectionSupported!==false||v.approvalReasonRequired!==false||!Array.isArray(v.references)||!Array.isArray(v.rules))return false
 const required=h.lines.filter(l=>l.discountCheck?.requiresApproval===true).sort((a,b)=>a.lineNo-b.lineNo)
 if(required.length===0||v.rules.length!==Math.min(required.length,8)||v.references.length===0||v.references.length>12)return false
 const first=v.references[0]
 if(!record(first)||first.role!=='sales-order-draft'||first.label!==h.documentNo||!same(first.recordId,id)||typeof first.link!=='string'||first.link.toLowerCase()!==`/satis/siparisler/${id.toLowerCase()}`)return false
 return v.references.every(r=>record(r)&&hasExactKeys(r,['role','label','recordId','link'])&&text(r.label)&&r.label.length<=200&&guid(r.recordId)
  &&(r.role==='sales-order-draft'?same(r.recordId,id)&&r.label===h.documentNo&&r.link===first.link:r.link===null&&(r.role==='price-list'||r.role==='product'&&h.lines.some(l=>same(r.recordId,l.productId)))))
  &&v.rules.every((r,i)=>{
   if(!record(r)||!hasExactKeys(r,['ruleCode','threshold','actual','deviation','unit']))return false
   const line=required[i]!,c=line.discountCheck!
   if(h.priceDisclosure==='Withheld'&&(r.actual!==null||r.deviation!==null))return false
   if(r.ruleCode==='sales.discount-without-list-price')return [r.threshold,r.actual,r.deviation,r.unit].every(x=>x===null)&&(line.pricing===null||line.pricing.priceReference===null)
   if(line.pricing!==null&&line.pricing.priceReference===null)return false
   if(r.ruleCode==='sales.discount-over-limit')return c.limitPercent!==null&&r.threshold===c.limitPercent&&nullableDecimal(r.threshold,nativeAmount)&&nullableDecimal(r.actual,nativeAmount)&&nullableDecimal(r.deviation,nativeAmount)&&r.unit==='%'
   return r.ruleCode==='sales.discount-limit-undefined'&&c.limitPercent===null&&r.threshold===null&&r.deviation===null&&nullableDecimal(r.actual,nativeAmount)&&(r.actual===null?r.unit===null:r.unit==='%')
  })
}
