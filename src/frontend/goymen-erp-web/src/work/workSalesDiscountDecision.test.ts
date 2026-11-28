import {afterEach,describe,it,expect,vi} from 'vitest'
import {decodeDraftLinePricing} from '../sales/linePricing'
import {decodeWorkSalesDiscountPreview,SALES_DISCOUNT_WORK_KIND as kind,isSalesDiscountWorkCommand,salesDiscountWorkCommand} from './workSalesDiscountDecision'
import {decodeWorkDecisionPreview,exactNativeClrAmount,isWorkDecisionRequest,isWorkPackageEffectiveDate,sendWorkDecision,WORK_DECISION_KINDS} from './workDecisionApi'
import {salesDiscountFixture as fixture,salesDiscountId as id} from './salesDiscountDecisionFixture'
const company='aaaaaaaa-0000-4000-8000-000000000001'
const decode=(v:unknown)=>decodeWorkDecisionPreview(v,company,kind,id)
afterEach(()=>{vi.unstubAllGlobals();vi.restoreAllMocks()})
describe('Discount exact selected-owner human and native ordered command',()=>{
 it.each([[true,true],[true,false],[false,true],[false,false]] as const)('preserves full15/10/8, raw money and versions visible=%s listed=%s',(visible,listed)=>{
  const f=fixture(company,visible,'9007199254740993',true,listed),raw=JSON.stringify(f)
  expect(decode(f)).toEqual(f);expect(Object.keys(f.salesDiscount)).toHaveLength(15);expect(Object.keys(f.salesDiscount.lines[0]!)).toHaveLength(10);expect(Object.keys(f.salesDiscount.lines[0]!.discountCheck!)).toHaveLength(8)
  expect(f.salesDiscount.lines[0]!.productVersion).toBe('9007199254740993');expect(f.salesDiscount.lines[0]!.quantity).toBe('0001.2500');expect(JSON.stringify(f)).toBe(raw)
  if(!visible)expect(f.salesDiscount.lines.every(l=>l.pricing===null&&l.discountCheck!.listUnitPrice===null)).toBe(true)
 })
 it('accepts equivalent upper-case route GUIDs with the retained canonical native reference',()=>{const f=fixture(company);expect(decodeWorkDecisionPreview(f,company.toUpperCase(),kind,id.toUpperCase())).toEqual(f)})
 it.each(['1','9223372036854775807'])('retains terminal full Int64 version %s',version=>{const f=fixture(company,false,version,false);expect(decode(f)).toEqual(f)})
 const faults:readonly [string,(f:ReturnType<typeof fixture>)=>void][]=[
  ['root private',f=>Object.assign(f,{salesDiscountProof:{}})],['human digest',f=>Object.assign(f.salesDiscount,{previewDigest:'a'.repeat(64)})],['human source',f=>Object.assign(f.salesDiscount,{source:{}})],['receipt',f=>Object.assign(f.salesDiscount,{originalDiscountApprovalReceipt:{}})],
  ['line extra',f=>Object.assign(f.salesDiscount.lines[0]!,{sourceJson:'SENTETIK'})],['null-line',f=>Object.assign(f.salesDiscount,{lines:[null]})],['duplicate',f=>Object.assign(f.salesDiscount,{lines:[f.salesDiscount.lines[0],f.salesDiscount.lines[0]]})],
  ['numericversion',f=>Object.assign(f.salesDiscount,{version:9007199254740993})],['maxoverflow',f=>{Object.assign(f.salesDiscount,{version:'9223372036854775808'})}],['padded',f=>{Object.assign(f.salesDiscount,{version:'09'})}],['newline',f=>{Object.assign(f.salesDiscount,{version:'9\n'})}],['numericproduct',f=>Object.assign(f.salesDiscount.lines[0]!,{productVersion:9007199254740993})],
  ['lineint32',f=>{Object.assign(f.salesDiscount.lines[0]!,{lineNo:2147483648})}],['revisionint32',f=>{Object.assign(f.salesDiscount,{revision:2147483648})}],['quantityzero',f=>{Object.assign(f.salesDiscount.lines[0]!,{quantity:'0.000'})}],['quantitycontrol',f=>{Object.assign(f.salesDiscount.lines[0]!,{quantity:'1\n'})}],
  ['company',f=>{Object.assign(f.salesDiscount,{companyId:id})}],['id',f=>{Object.assign(f.salesDiscount,{draftId:company})}],['sourceversion',f=>{f.sourceVersion='9'}],['date',f=>{Object.assign(f.salesDiscount,{businessDate:'2026-02-30'})}],
  ['checkmissing',f=>{Reflect.deleteProperty(f.salesDiscount.lines[0]!.discountCheck!,'approvedBy')}],['checkextra',f=>Object.assign(f.salesDiscount.lines[0]!.discountCheck!,{sourceHash:'a'.repeat(64)})],['checkflag',f=>Object.assign(f.salesDiscount.lines[0]!.discountCheck!,{requiresApproval:'true'})],['checktime',f=>{Object.assign(f.salesDiscount.lines[0]!.discountCheck!,{checkedAtUtc:'2026-11-28T24:00:00Z'})}],
  ['nestedcheck',f=>Object.assign(f.salesDiscount.lines[0]!.pricing!,{discountCheck:{...f.salesDiscount.lines[0]!.discountCheck,checkedBy:'another'}})],['pricingextra',f=>Object.assign(f.salesDiscount.lines[0]!.pricing!,{margin:{}})],['subcontracttype',f=>Object.assign(f.salesDiscount,{isSubcontractService:'false'})],['subcontractapprove',f=>{Object.assign(f.salesDiscount,{isSubcontractService:true})}],
  ['pending',f=>{f.pending=false}],['approveflag',f=>{f.approvalSupported=false}],['rejectflag',f=>{f.rejectionSupported=true}],['reasonflag',f=>{f.approvalReasonRequired=true}],['rulecount',f=>{f.rules=[]}],['rulecode',f=>{f.rules[0]!.ruleCode='sales.other'}],['reference',f=>{f.references[0]!.recordId=company}],['referencelink',f=>{f.references[0]!.link='/elsewhere'}]
 ]
 it.each(faults)('rejects malformed/private/mixed native facts %s without throwing',(_,change)=>{const f=fixture(company);change(f);expect(()=>decode(f)).not.toThrow();expect(decode(f)).toBeNull()})
 it('does not infer unlisted status or require approval merely because a withheld sibling list price is null',()=>{
  const f=fixture(company,false);Object.assign(f.salesDiscount.lines[1]!.discountCheck!,{requiresApproval:false});f.rules.pop();expect(decode(f)).toEqual(f)
 })
 it('rejects numeric price disclosure in a withheld check or pricing member',()=>{const f=fixture(company,false);Object.assign(f.salesDiscount.lines[0]!.discountCheck!,{listUnitPrice:'100'});expect(decode(f)).toBeNull();const p=fixture(company,false);Object.assign(p.salesDiscount.lines[0]!,{pricing:fixture(company).salesDiscount.lines[0]!.pricing});expect(decode(p)).toBeNull()})
 it.each(['79228162514264337593543950335','0.0000000000000000000000000001','0001.2500','79228162514264337593543950335.0000','0.0000000000000000000000000001000'])('preserves native exact CLR text %s without normalization',value=>{
  const f=fixture(company);Object.assign(f.salesDiscount.lines[0]!.pricing!,{unitPrice:value,netUnitPrice:value});const raw=JSON.stringify(f);expect(decode(f)).toEqual(f);expect(JSON.stringify(f)).toBe(raw)
 })
 it.each(['quantity','price','reference','check','rule'])('rejects native CLR overflow/underflow at %s',field=>{
  for(const value of ['79228162514264337593543950336','0.00000000000000000000000000001']){
   const f=fixture(company),l=f.salesDiscount.lines[0]!
   if(field==='quantity')Object.assign(l,{quantity:value});if(field==='price')Object.assign(l.pricing!,{unitPrice:value,netUnitPrice:value});if(field==='reference')Object.assign(l.pricing!.priceReference!,{unitPrice:value})
   if(field==='check'){Object.assign(l.discountCheck!,{limitPercent:value});Object.assign(f.rules[0]!,{threshold:value})}if(field==='rule')Object.assign(f.rules[0]!,{actual:value})
   expect(decode(f)).toBeNull()
  }
 })
 it('leaves the native decoder unchanged and uses the exact callback only at the Work boundary',()=>{
  const f=fixture(company),p=f.salesDiscount.lines[0]!.pricing!;Object.assign(p,{unitPrice:'79228162514264337593543950336',netUnitPrice:'79228162514264337593543950336'})
  expect(decodeDraftLinePricing({version:'9',lines:[p],margin:{disclosure:'Withheld',costBasis:null,kind:null,lines:null}})).not.toBeNull()
  expect(decodeWorkSalesDiscountPreview(f.salesDiscount,company,id,isWorkPackageEffectiveDate,v=>exactNativeClrAmount(v,false))).toBeNull()
 })
 it('preserves native unlisted self approval even on a retained same-writer approved check',()=>{
  const f=fixture(company,false,'9',false,false);for(const l of f.salesDiscount.lines)Object.assign(l.discountCheck!,{approvedBy:l.discountCheck!.checkedBy});expect(decode(f)).toEqual(f)
 })
 it('builds every pending native line in original order and sends raw reasons without trimming',()=>{
  const h=fixture(company).salesDiscount,raw=' '+ 'r'.repeat(4000)+' ',reasons=Object.fromEntries(h.lines.map(l=>[l.lineId,raw]))
  const q=salesDiscountWorkCommand(h,reasons,null)!;expect(q.salesDiscount.lines).toEqual(h.lines.map(l=>({lineId:l.lineId,reason:raw})));expect(isWorkDecisionRequest(q,kind)).toBe(true);expect(salesDiscountWorkCommand(h,{},null)).toBeNull()
 })
 it.each(['blank','long','duplicate','zero','extra','reject','reason','missing','501'])('rejects invalid or foreign frozen command %s before IO',async fault=>{
  const h=fixture(company).salesDiscount,q=salesDiscountWorkCommand(h,Object.fromEntries(h.lines.map(l=>[l.lineId,'SENTETIK'])),null)!,fetch=vi.fn();vi.stubGlobal('fetch',fetch)
  const bad:unknown=fault==='blank'?{...q,salesDiscount:{lines:[{lineId:h.lines[0]!.lineId,reason:' '}]}}:fault==='long'?{...q,salesDiscount:{lines:[{lineId:h.lines[0]!.lineId,reason:'r'.repeat(4001)}]}}:fault==='duplicate'?{...q,salesDiscount:{lines:[q.salesDiscount.lines[0],q.salesDiscount.lines[0]]}}:fault==='zero'?{...q,expectedVersion:'0'}:fault==='extra'?{...q,salesPriceFloor:{}}:fault==='reject'?{...q,approve:false}:fault==='reason'?{...q,reason:'SENTETIK'}:fault==='missing'?{...q,salesDiscount:undefined}:{...q,salesDiscount:{lines:Array.from({length:501},()=>({lineId:crypto.randomUUID(),reason:'SENTETIK'}))}}
  expect(isSalesDiscountWorkCommand(bad)).toBe(false);expect(isWorkDecisionRequest(bad,kind)).toBe(false);expect((await sendWorkDecision(company,kind,id,bad as typeof q,'SENTETIK')).type).toBe('unknown');expect(fetch).not.toHaveBeenCalled()
 })
 it('refuses foreign-kind selection and preserves the actual Preparing/Rejected native result axes',async()=>{
  const h=fixture(company).salesDiscount,q=salesDiscountWorkCommand(h,Object.fromEntries(h.lines.map(l=>[l.lineId,'SENTETIK'])),null)!
  for(const other of WORK_DECISION_KINDS.filter(k=>k!==kind))expect(isWorkDecisionRequest(q,other)).toBe(false)
  for(const state of ['Preparing','Rejected']){const fetch=vi.fn<typeof globalThis.fetch>(async()=>new Response(JSON.stringify({companyId:company,kind,itemId:id,state,replayed:false,originalCorrelationId:'SENTETIK-original'}),{status:200}));vi.stubGlobal('fetch',fetch);expect((await sendWorkDecision(company,kind,id,q,'SENTETIK')).type).toBe('applied');expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual(q)}
 })
})
