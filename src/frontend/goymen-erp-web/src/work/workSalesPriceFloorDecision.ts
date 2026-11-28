import {decodeDraftLinePricing,type LinePricing,type PriceFloorException} from '../sales/linePricing'
import {hasExactKeys} from '../finance/wireShape'
import {isBoundUuid} from '../finance/contract'
import {salesRequestVersion,salesRequestIncrementable} from './workSalesRequestDecision'
export const SALES_PRICE_FLOOR_WORK_KIND='sales.price-floor-exception.v1'
export const SALES_PRICE_FLOOR_WORK_CAPABILITY='sales.price-floor-exception.work-decision'
export interface WorkSalesPriceFloorLine {readonly lineId:string;readonly lineNo:number;readonly productId:string;readonly productVersion:string;readonly productCode:string;readonly productName:string;readonly orderUnitId:string;readonly quantity:string;readonly floorException:PriceFloorException|null;readonly pricing:LinePricing|null}
export interface WorkSalesPriceFloorPreview {readonly companyId:string;readonly draftId:string;readonly documentNo:string;readonly version:string;readonly revision:number;readonly state:'Draft'|'Closed'|'Cancelled';readonly approvalState:'Preparing'|'PendingApproval'|'Approved'|'Rejected'|'Withdrawn';readonly partyId:string;readonly companyAccountId:string;readonly currencyId:string;readonly businessDate:string;readonly lines:readonly WorkSalesPriceFloorLine[];readonly priceDisclosure:'Visible'|'Withheld';readonly approvalSupported:boolean}
export interface SalesPriceFloorSelection {readonly lines:readonly {readonly lineId:string;readonly reason:string}[]}
export interface SalesPriceFloorWorkCommand {readonly expectedVersion:string;readonly approve:true;readonly reason:null;readonly delegationId:string|null;readonly salesPriceFloor:SalesPriceFloorSelection}
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''
const actor=(v:unknown):v is string=>text(v)&&v===v.trim()&&v.length<=128
const int32=(v:unknown,min=1):v is number=>typeof v==='number'&&Number.isInteger(v)&&v>=min&&v<=2147483647
// Validate the native text representation without converting quantities/prices, calculating or changing their scale.
const decimal=(v:unknown):v is string=>typeof v==='string'&&v===v.trim()&&!/[\u0000-\u001f\u007f]/.test(v)&&v.length<=64&&/^[0-9]+(?:\.[0-9]+)?$/.test(v)
const nullableDecimal=(v:unknown)=>v===null||decimal(v)
const utc=(v:unknown,date:(v:unknown)=>boolean):v is string=>typeof v==='string'&&v===v.trim()&&date(v.slice(0,10))&&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
export const validFloorReason=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''&&v.trim().length<=4000
function exception(v:unknown,date:(v:unknown)=>boolean):v is PriceFloorException {
 if(!record(v)||!hasExactKeys(v,['requestedBy','requestedAtUtc','approvedBy','approvedAtUtc','reason'])||!actor(v.requestedBy)||!utc(v.requestedAtUtc,date))return false
 return v.approvedBy===null&&v.approvedAtUtc===null&&v.reason===null||actor(v.approvedBy)&&v.approvedBy!==v.requestedBy&&utc(v.approvedAtUtc,date)&&validFloorReason(v.reason)&&v.reason===v.reason.trim()
}
const sameException=(a:unknown,b:unknown)=>a===null?b===null:record(a)&&record(b)&&['requestedBy','requestedAtUtc','approvedBy','approvedAtUtc','reason'].every(k=>a[k]===b[k])
function pricing(v:unknown,line:Record<string,unknown>,date:(v:unknown)=>boolean,version:string):LinePricing|null {
 if(!record(v)||!hasExactKeys(v,['lineId','lineNo','unitPrice','priceUnitId','priceSource','priceReference','discountPercent','discountAmount','surchargePercent','surchargeAmount','netUnitPrice','floorException','discountCheck'])
  ||!same(v.lineId,line.lineId as string)||v.lineNo!==line.lineNo||v.priceUnitId!==null&&!guid(v.priceUnitId)
  ||!['unitPrice','discountPercent','discountAmount','surchargePercent','surchargeAmount','netUnitPrice'].every(k=>nullableDecimal(v[k]))
  ||v.floorException!==null&&!exception(v.floorException,date)||!sameException(v.floorException,line.floorException))return null
 if(v.priceReference!==null){const r=v.priceReference;if(!record(r)||!guid(r.priceListId)||!guid(r.priceUnitId)||!int32(r.versionNo)||!date(r.validFrom)||r.validThrough!==null&&!date(r.validThrough)
  ||!['unitPrice','floorUnitPrice','lowerQuantity','upperQuantity'].every(k=>nullableDecimal(r[k])))return null}
 if(v.discountCheck!==null){const c=v.discountCheck;if(!record(c)||!actor(c.checkedBy)||!utc(c.checkedAtUtc,date)||c.approvedBy!==null&&!actor(c.approvedBy)
  ||c.approvedAtUtc!==null&&!utc(c.approvedAtUtc,date)||!['listUnitPrice','limitPercent'].every(k=>nullableDecimal(c[k])))return null}
 // Reuse the unchanged native line decoder. The withheld margin is only its validation context, never Work data.
 return decodeDraftLinePricing({version,lines:[v],margin:{disclosure:'Withheld',costBasis:null,kind:null,lines:null}})?.lines[0]??null
}
export function decodeWorkSalesPriceFloorPreview(v:unknown,company:string,id:string,date:(v:unknown)=>boolean):WorkSalesPriceFloorPreview|null {
 if(!record(v)||!hasExactKeys(v,['companyId','draftId','documentNo','version','revision','state','approvalState','partyId','companyAccountId','currencyId','businessDate','lines','priceDisclosure','approvalSupported'])
  ||!guid(company)||!guid(id)||!same(v.companyId,company)||!same(v.draftId,id)||!text(v.documentNo)||v.documentNo!==v.documentNo.trim()||v.documentNo.length>200
  ||!salesRequestVersion(v.version)||!int32(v.revision,0)||v.state!=='Draft'&&v.state!=='Closed'&&v.state!=='Cancelled'
  ||v.approvalState!=='Preparing'&&v.approvalState!=='PendingApproval'&&v.approvalState!=='Approved'&&v.approvalState!=='Rejected'&&v.approvalState!=='Withdrawn'
  ||![v.partyId,v.companyAccountId,v.currencyId].every(guid)||!date(v.businessDate)||v.priceDisclosure!=='Visible'&&v.priceDisclosure!=='Withheld'
  ||typeof v.approvalSupported!=='boolean'||!Array.isArray(v.lines)||v.lines.length===0||v.lines.length>500)return null
 const lines:WorkSalesPriceFloorLine[]=[]
 for(const l of v.lines){
  if(!record(l)||!hasExactKeys(l,['lineId','lineNo','productId','productVersion','productCode','productName','orderUnitId','quantity','floorException','pricing'])
   ||![l.lineId,l.productId,l.orderUnitId].every(guid)||!int32(l.lineNo)||!salesRequestVersion(l.productVersion)||!text(l.productCode)||!text(l.productName)||!decimal(l.quantity)||!/[1-9]/.test(l.quantity)
   ||l.floorException!==null&&!exception(l.floorException,date))return null
  const p=v.priceDisclosure==='Visible'?pricing(l.pricing,l,date,v.version):null
  if(v.priceDisclosure==='Visible'?p===null:l.pricing!==null)return null
  lines.push({lineId:l.lineId as string,lineNo:l.lineNo,productId:l.productId as string,productVersion:l.productVersion,productCode:l.productCode,productName:l.productName,orderUnitId:l.orderUnitId as string,quantity:l.quantity,floorException:l.floorException as PriceFloorException|null,pricing:p})
 }
 if(new Set(lines.map(l=>l.lineId.toLowerCase())).size!==lines.length||new Set(lines.map(l=>l.lineNo)).size!==lines.length)return null
 const pending=lines.filter(l=>l.floorException!==null&&l.floorException.approvedBy===null)
 if(v.approvalSupported&&(!salesRequestIncrementable(v.version)||v.state!=='Draft'||v.approvalState!=='Preparing'&&v.approvalState!=='Rejected'||pending.length===0))return null
 return {companyId:v.companyId as string,draftId:v.draftId as string,documentNo:v.documentNo,version:v.version,revision:v.revision,state:v.state,approvalState:v.approvalState,partyId:v.partyId as string,companyAccountId:v.companyAccountId as string,currencyId:v.currencyId as string,businessDate:v.businessDate as string,lines,priceDisclosure:v.priceDisclosure,approvalSupported:v.approvalSupported}
}
export const isSalesPriceFloorWorkCommand=(v:unknown):v is SalesPriceFloorWorkCommand=>record(v)&&hasExactKeys(v,['expectedVersion','approve','reason','delegationId','salesPriceFloor'])
 &&salesRequestIncrementable(v.expectedVersion)&&v.approve===true&&v.reason===null&&(v.delegationId===null||guid(v.delegationId))&&record(v.salesPriceFloor)&&hasExactKeys(v.salesPriceFloor,['lines'])
 &&Array.isArray(v.salesPriceFloor.lines)&&v.salesPriceFloor.lines.length>0&&v.salesPriceFloor.lines.length<=500
 &&v.salesPriceFloor.lines.every(l=>record(l)&&hasExactKeys(l,['lineId','reason'])&&guid(l.lineId)&&validFloorReason(l.reason))
 &&new Set(v.salesPriceFloor.lines.map(l=>(l as {lineId:string}).lineId.toLowerCase())).size===v.salesPriceFloor.lines.length
export function salesPriceFloorWorkCommand(h:WorkSalesPriceFloorPreview,reasons:Readonly<Record<string,string>>,delegationId:string|null):SalesPriceFloorWorkCommand|null {
 if(!h.approvalSupported)return null
 const lines=h.lines.filter(l=>l.floorException!==null&&l.floorException.approvedBy===null).map(l=>({lineId:l.lineId,reason:reasons[l.lineId]??''}))
 const body={expectedVersion:h.version,approve:true as const,reason:null,delegationId,salesPriceFloor:{lines}}
 return isSalesPriceFloorWorkCommand(body)?body:null
}
export function validSalesPriceFloorPreview(v:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>boolean):boolean {
 const h=decodeWorkSalesPriceFloorPreview(v.salesPriceFloor,company,id,date)
 if(h===null||!hasExactKeys(v,['companyId','kind','itemId','documentLabel','sourceVersion','pending','approvalReasonRequired','references','rules','approvalSupported','rejectionSupported','salesPriceFloor'])
  ||!same(v.companyId,company)||!same(v.itemId,id)||v.kind!==SALES_PRICE_FLOOR_WORK_KIND||v.documentLabel!==h.documentNo||v.sourceVersion!==h.version||v.pending!==h.approvalSupported
  ||v.approvalSupported!==h.approvalSupported||v.rejectionSupported!==false||v.approvalReasonRequired!==false||!Array.isArray(v.references)||!Array.isArray(v.rules))return false
 const floor=h.lines.filter(l=>l.floorException!==null).sort((a,b)=>a.lineNo-b.lineNo)
 if(floor.length===0||v.rules.length!==Math.min(floor.length,8)||v.references.length===0||v.references.length>12)return false
 const first=v.references[0]
 if(!record(first)||first.role!=='sales-order-draft'||first.label!==h.documentNo||!same(first.recordId,id)||first.link!==`/satis/siparisler/${id}`)return false
 return v.references.every(r=>record(r)&&hasExactKeys(r,['role','label','recordId','link'])&&text(r.label)&&r.label.length<=200&&guid(r.recordId)
  &&(r.role==='sales-order-draft'?same(r.recordId,id)&&r.label===h.documentNo&&r.link===first.link:r.link===null&&(r.role==='price-list'||r.role==='product'&&h.lines.some(l=>same(r.recordId,l.productId)))))
  &&v.rules.every(r=>record(r)&&hasExactKeys(r,['ruleCode','threshold','actual','deviation','unit'])&&r.ruleCode==='sales.price-below-floor'
   &&(h.priceDisclosure==='Withheld'?[r.threshold,r.actual,r.deviation,r.unit].every(x=>x===null):decimal(r.threshold)&&decimal(r.actual)&&decimal(r.deviation)&&(r.unit===null||typeof r.unit==='string'&&/^[A-Za-z]{1,16}$/.test(r.unit))))
}
