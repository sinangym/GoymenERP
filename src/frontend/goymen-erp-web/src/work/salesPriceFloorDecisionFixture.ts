import type {WorkSalesPriceFloorPreview} from './workSalesPriceFloorDecision'
import {SALES_PRICE_FLOOR_WORK_KIND} from './workSalesPriceFloorDecision'
export const salesPriceFloorId='f2020000-0000-4000-8000-000000000001',floorUnit='f2020000-0000-4000-8000-000000000002',floorList='f2020000-0000-4000-8000-000000000003'
export function salesPriceFloorHuman(company:string,visible=true,version='9007199254740993',pending=true):WorkSalesPriceFloorPreview {
 const lines=Array.from({length:2},(_,i)=>{const lineId=`f2020000-0000-4000-8001-${String(i+1).padStart(12,'0')}`,floorException={requestedBy:'SENTETIK-requester-'+i,requestedAtUtc:'2026-11-28T09:00:00.0000001Z',approvedBy:pending?null:'SENTETIK-checker',approvedAtUtc:pending?null:'2026-11-28T09:01:00Z',reason:pending?null:'SENTETIK kayıtlı gerekçe'}
 return {lineId,lineNo:i+1,productId:`f2020000-0000-4000-8002-${String(i+1).padStart(12,'0')}`,productVersion:'9007199254740993',productCode:'SNT-'+i,productName:'SENTETIK Floor Ürün '+i,orderUnitId:floorUnit,quantity:'0001.2500',floorException,
 pricing:visible?{lineId,lineNo:i+1,unitPrice:'90.5000',priceUnitId:floorUnit,priceSource:'Manual' as const,priceReference:{priceListId:floorList,priceListName:'SENTETIK Liste',versionNo:3,validFrom:'2026-01-01',validThrough:null,currencyCode:'TRY',priceUnitId:floorUnit,unitPrice:'100.0000',floorUnitPrice:'95.00',vatBasis:'ExcludingVat' as const,lowerQuantity:'0',upperQuantity:null,tier:'Company' as const},discountPercent:null,discountAmount:null,surchargePercent:null,surchargeAmount:null,netUnitPrice:'90.5',floorException,discountCheck:null}:null}})
 return {companyId:company,draftId:salesPriceFloorId,documentNo:'SENTETIK-SO-FLOOR',version,revision:0,state:'Draft',approvalState:'Preparing',partyId:company,companyAccountId:company,currencyId:company,businessDate:'2026-11-28',lines,priceDisclosure:visible?'Visible':'Withheld',approvalSupported:pending&&version!=='9223372036854775807'}
}
export function salesPriceFloorFixture(company:string,visible=true,version='9007199254740993',pending=true){
 const h=salesPriceFloorHuman(company,visible,version,pending)
 return {companyId:company,kind:SALES_PRICE_FLOOR_WORK_KIND,itemId:salesPriceFloorId,documentLabel:h.documentNo,sourceVersion:h.version,pending:h.approvalSupported,approvalReasonRequired:false,approvalSupported:h.approvalSupported,rejectionSupported:false,
 references:[{role:'sales-order-draft',label:h.documentNo,recordId:h.draftId,link:`/satis/siparisler/${h.draftId}`},{role:'price-list',label:'SENTETIK Liste',recordId:floorList,link:null},...h.lines.map(l=>({role:'product',label:l.productCode+' '+l.productName,recordId:l.productId,link:null}))],
 rules:h.lines.map(()=>({ruleCode:'sales.price-below-floor',threshold:visible?'95':null,actual:visible?'90.5':null,deviation:visible?'4.5':null,unit:visible?'TRY':null})),salesPriceFloor:h}
}
