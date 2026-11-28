import {afterEach,describe,it,expect,vi} from 'vitest'
import {decodeDraftLinePricing} from '../sales/linePricing'
import {decodeWorkSalesPriceFloorPreview,SALES_PRICE_FLOOR_WORK_KIND as kind,isSalesPriceFloorWorkCommand,salesPriceFloorWorkCommand} from './workSalesPriceFloorDecision'
import {decodeWorkDecisionPreview,isWorkDecisionRequest,isWorkPackageEffectiveDate,sendWorkDecision,fetchWorkDecision,WORK_DECISION_KINDS} from './workDecisionApi'
import {salesPriceFloorFixture as fixture,salesPriceFloorHuman as human,salesPriceFloorId as id} from './salesPriceFloorDecisionFixture'
const company='aaaaaaaa-0000-4000-8000-000000000001'
afterEach(()=>{vi.unstubAllGlobals();vi.restoreAllMocks()})
const clone=<T,>(v:T):T=>structuredClone(v)
describe('Floor exact Work contract with unchanged native pricing decoder',()=>{
 it.each([true,false])('accepts exact14/10 and raw decimal/version text without price co-grant visible=%s',visible=>{
  const f=fixture(company,visible),h=decodeWorkSalesPriceFloorPreview(f.salesPriceFloor,company,id,isWorkPackageEffectiveDate)
  expect(h).toEqual(f.salesPriceFloor);expect(decodeWorkDecisionPreview(f,company,kind,id)).toEqual(f)
  expect(h?.lines[0]?.quantity).toBe('0001.2500');expect(h?.version).toBe('9007199254740993');expect(h?.lines[0]?.productVersion).toBe('9007199254740993')
  if(visible)expect(h?.lines[0]?.pricing?.unitPrice).toBe('90.5000');else expect(h?.lines.every(l=>l.pricing===null)).toBe(true)
 })
 it.each(['1','9223372036854775807'])('retains fullInt64 terminal bounds %s',version=>{const f=fixture(company,false,version);expect(decodeWorkDecisionPreview(f,company,kind,id)).toEqual(f)})
 it('leaves native default decoder compatibility untouched while Work requires exact discountCheck',()=>{
  const p=clone(human(company).lines[0]!.pricing)!;const {discountCheck:omitted,...legacy}=p;expect(omitted).toBeNull()
  expect(decodeDraftLinePricing({version:'9',lines:[legacy],margin:{disclosure:'Withheld',costBasis:null,kind:null,lines:null}})).not.toBeNull()
  const f=fixture(company);Object.assign(f.salesPriceFloor.lines[0]!,{pricing:legacy});expect(decodeWorkDecisionPreview(f,company,kind,id)).toBeNull()
 })
 const faults:readonly [string,(f:ReturnType<typeof fixture>)=>void][]=[
  ['root private',f=>Object.assign(f,{salesPriceFloorProof:{}})],['human private',f=>Object.assign(f.salesPriceFloor,{sourceFactsDigest:'a'.repeat(64)})],
  ['receipt',f=>Object.assign(f.salesPriceFloor,{originalFloorApprovalReceipt:{}})],['source',f=>Object.assign(f.salesPriceFloor,{source:{}})],
  ['line private',f=>Object.assign(f.salesPriceFloor.lines[0]!,{sourceJson:'SENTETIK'})],['numeric version',f=>Object.assign(f.salesPriceFloor,{version:9007199254740993})],
  ['version LF',f=>Object.assign(f.salesPriceFloor,{version:'9\n'})],['version zero',f=>Object.assign(f.salesPriceFloor,{version:'0'})],['version overflow',f=>Object.assign(f.salesPriceFloor,{version:'9223372036854775808'})],
  ['version padded',f=>Object.assign(f.salesPriceFloor,{version:'09'})],['source version',f=>{f.sourceVersion='9'}],['company',f=>{Object.assign(f.salesPriceFloor,{companyId:id})}],['id',f=>{Object.assign(f.salesPriceFloor,{draftId:company})}],
  ['lineNoInt32',f=>Object.assign(f.salesPriceFloor.lines[0]!,{lineNo:2147483648})],['revisionInt32',f=>Object.assign(f.salesPriceFloor,{revision:2147483648})],
  ['line duplicate',f=>Object.assign(f.salesPriceFloor,{lines:[f.salesPriceFloor.lines[0]!,f.salesPriceFloor.lines[0]!]})],['null line',f=>Object.assign(f.salesPriceFloor,{lines:[null]})],
  ['product numeric version',f=>Object.assign(f.salesPriceFloor.lines[0]!,{productVersion:9007199254740993})],['product version LF',f=>Object.assign(f.salesPriceFloor.lines[0]!,{productVersion:'1\n'})],
  ['quantity LF',f=>Object.assign(f.salesPriceFloor.lines[0]!,{quantity:'1\n'})],['quantity zero',f=>Object.assign(f.salesPriceFloor.lines[0]!,{quantity:'0.000'})],
  ['floor without reference',f=>Object.assign(f.salesPriceFloor.lines[0]!.pricing!,{priceReference:null})],['floor without unit',f=>Object.assign(f.salesPriceFloor.lines[0]!.pricing!,{priceUnitId:null})],
  ['price LF',f=>Object.assign(f.salesPriceFloor.lines[0]!.pricing!,{unitPrice:'90.5\n'})],['floor CR',f=>Object.assign(f.salesPriceFloor.lines[0]!.pricing!.priceReference!,{floorUnitPrice:'95\r'})],
  ['lower LF',f=>Object.assign(f.salesPriceFloor.lines[0]!.pricing!.priceReference!,{lowerQuantity:'0\n'})],['upper CR',f=>Object.assign(f.salesPriceFloor.lines[0]!.pricing!.priceReference!,{upperQuantity:'2\r'})],
  ['referenceInt32',f=>Object.assign(f.salesPriceFloor.lines[0]!.pricing!.priceReference!,{versionNo:2147483648})],['invalid calendar',f=>{Object.assign(f.salesPriceFloor,{businessDate:'2026-02-30'})}],
  ['offset time',f=>Object.assign(f.salesPriceFloor.lines[0]!.floorException!,{requestedAtUtc:'2026-11-28T09:00:00+03:00'})],['missing nullable',f=>{Reflect.deleteProperty(f.salesPriceFloor.lines[0]!.floorException!,'approvedBy')}],
  ['mixed price exception',f=>Object.assign(f.salesPriceFloor.lines[0]!.pricing!,{floorException:{...f.salesPriceFloor.lines[0]!.floorException,requestedBy:'another'}})],['pending',f=>{f.pending=false}],
  ['approve flag',f=>{f.approvalSupported=false}],['reject',f=>{f.rejectionSupported=true}],['reason flag',f=>{f.approvalReasonRequired=true}],
  ['rule count',f=>{f.rules=[]}],['rule code',f=>{f.rules[0]!.ruleCode='sales.other'}],['order reference',f=>{f.references[0]!.recordId=company}],['order link',f=>{f.references[0]!.link='/elsewhere'}]
 ]
 it.each(faults)('fails closed %s without throwing',(_,change)=>{const f=clone(fixture(company));change(f);expect(()=>decodeWorkDecisionPreview(f,company,kind,id)).not.toThrow();expect(decodeWorkDecisionPreview(f,company,kind,id)).toBeNull()})
 it('refuses withheld pricing and visible-null pricing rather than painting fake zeroes',()=>{
  const f=fixture(company,false);Object.assign(f.salesPriceFloor.lines[0]!,{pricing:human(company).lines[0]!.pricing});expect(decodeWorkDecisionPreview(f,company,kind,id)).toBeNull()
  const visible=fixture(company);Object.assign(visible.salesPriceFloor.lines[0]!,{pricing:null});expect(decodeWorkDecisionPreview(visible,company,kind,id)).toBeNull()
 })
 it('builds all pending exact lines in native order with literal raw reasons and no arithmetic',()=>{
  const h=human(company),reasons=Object.fromEntries(h.lines.map(l=>[l.lineId,'  SENTETIK '+l.lineNo+'  '])),q=salesPriceFloorWorkCommand(h,reasons,null)
  expect(q).toEqual({expectedVersion:h.version,approve:true,reason:null,delegationId:null,salesPriceFloor:{lines:h.lines.map(l=>({lineId:l.lineId,reason:reasons[l.lineId]}))}})
  expect(isWorkDecisionRequest(q,kind)).toBe(true);expect(salesPriceFloorWorkCommand(h,{},null)).toBeNull()
  expect(isSalesPriceFloorWorkCommand({...q,salesPriceFloor:{lines:[{lineId:h.lines[0]!.lineId,reason:' '+ 'r'.repeat(4000)+' '}]}})).toBe(true)
 })
 it.each(['blank','long','duplicate','zero','extra','reject','reason','missing','501'])('rejects malformed frozen command %s before fetch',async fault=>{
  const h=human(company),q=salesPriceFloorWorkCommand(h,Object.fromEntries(h.lines.map(l=>[l.lineId,'SENTETIK'])),null)!,fetch=vi.fn();vi.stubGlobal('fetch',fetch)
  const bad:unknown=fault==='blank'?{...q,salesPriceFloor:{lines:[{lineId:h.lines[0]!.lineId,reason:' '}]}}:fault==='long'?{...q,salesPriceFloor:{lines:[{lineId:h.lines[0]!.lineId,reason:'r'.repeat(4001)}]}}:fault==='duplicate'?{...q,salesPriceFloor:{lines:[q.salesPriceFloor.lines[0],q.salesPriceFloor.lines[0]]}}:fault==='zero'?{...q,expectedVersion:'0'}:fault==='extra'?{...q,salesQuoteConversion:{}}:fault==='reject'?{...q,approve:false}:fault==='reason'?{...q,reason:'SENTETIK'}:fault==='missing'?{...q,salesPriceFloor:undefined}:{...q,salesPriceFloor:{lines:Array.from({length:501},()=>({lineId:crypto.randomUUID(),reason:'SENTETIK'}))}}
  expect(isWorkDecisionRequest(bad,kind)).toBe(false);expect(await sendWorkDecision(company,kind,id,bad as typeof q,'SENTETIK')).toEqual({type:'unknown',reason:'unexpected'});expect(fetch).not.toHaveBeenCalled()
 })
 it('rejects every foreign kind before IO and accepts genuine Preparing/Rejected native response axes',async()=>{
  const h=human(company),q=salesPriceFloorWorkCommand(h,Object.fromEntries(h.lines.map(l=>[l.lineId,'SENTETIK'])),null)!
  for(const other of WORK_DECISION_KINDS.filter(k=>k!==kind))expect(isWorkDecisionRequest(q,other)).toBe(false)
  for(const state of ['Preparing','Rejected']){const fetch=vi.fn<typeof globalThis.fetch>(async()=>new Response(JSON.stringify({companyId:company,kind,itemId:id,state,replayed:false,originalCorrelationId:'SENTETIK-original'}),{status:200}));vi.stubGlobal('fetch',fetch);expect((await sendWorkDecision(company,kind,id,q,'SENTETIK')).type).toBe('applied');expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual(q)}
 })
 it.each([403,404,503])('propagates actual GET HTTP%s without substituting human facts',async status=>{vi.stubGlobal('fetch',vi.fn(async()=>new Response('{}',{status})));expect((await fetchWorkDecision(company,kind,id)).type).toBe('http')})
})
