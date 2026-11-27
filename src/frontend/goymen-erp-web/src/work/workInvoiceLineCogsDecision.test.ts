import {afterEach,describe,expect,it,vi} from 'vitest'
import {invoiceLineCogsFixture as fixture,invoiceLineCogsId as id,cogsInvoice,cogsLine} from './invoiceLineCogsDecisionFixture'
import {INVOICE_LINE_COGS_WORK_KIND as kind,cogsWorkLong} from './workInvoiceLineCogsDecision'
import {decodeWorkDecisionPreview,isWorkDecisionRequest,isWorkDecisionKind,sendWorkDecision} from './workDecisionApi'
import {decodeCogsProposal,validCogsProposalWire} from '../accounting/invoiceLineCogsApi'
import {postedManufacturingCostShare} from '../accounting/postedManufacturingCostProof'
import {postedPurchaseCostShare} from '../accounting/postedPurchaseCostProof'
const company='aaaaaaaa-0000-4000-8000-000000000001',other='bbbbbbbb-0000-4000-8000-000000000001'
const request={expectedVersion:'1',approve:true,reason:null,delegationId:null}
afterEach(()=>vi.unstubAllGlobals())
describe('source-ready invoice-line COGS Work transport',()=>{
 it('retains full wrapper5/header25/source14 and every nested long string',()=>{
  const v=fixture(company,true,'9223372036854775807'),h=v.invoiceLineCogs,before=JSON.stringify(v)
  expect(decodeWorkDecisionPreview(v,company,kind,id)?.invoiceLineCogs).toEqual(h)
  expect(Object.keys(h)).toHaveLength(5);expect(Object.keys(h.proposal)).toHaveLength(25);expect(Object.keys(h.source)).toHaveLength(14)
  expect(h.proposal.finalizedVersion).toBe('9007199254740993');expect(h.proposal.accountPlan.packageVersion).toBe('9223372036854775807')
  expect(h.proposal.attributedEvidence[0]!.source.moneyProfileVersion).toBe('9007199254740993')
  expect(h.proposal.attributedEvidence[0]!.source.shipment.provenOriginalSequence).toBe('9223372036854775807')
  expect(h.proposal.purchaseAttributedEvidence[0]!.source.approval.recordedRounding.moneyProfileVersion).toBe('9223372036854775807')
  expect(h.proposal.purchaseAttributedEvidence[0]!.source.candidates[0]!.shipment.provenOriginalSequence).toBe('9223372036854775807')
  expect(JSON.stringify(v)).toBe(before);expect(decodeWorkDecisionPreview(v,company.toUpperCase(),kind,id.toUpperCase())).not.toBeNull()
  expect(isWorkDecisionKind(kind)).toBe(true)
 })
 it('preserves legacy optional absence, zero current revision, raw amount text, year-one dates and UTC ticks',()=>{
  const v=fixture(company),p=v.invoiceLineCogs.proposal
  Reflect.deleteProperty(p,'attributedEvidence');Reflect.deleteProperty(p,'purchaseAttributedEvidence')
  p.accountPlan.catalogVersion=null;p.transactionDate='0001-01-02';p.accountingDate='9999-12-31';p.preparedAtUtc='0001-01-01T00:00:00.0000001Z'
  const result=decodeWorkDecisionPreview(v,company,kind,id)?.invoiceLineCogs
  expect(result).toEqual(v.invoiceLineCogs);expect(Object.keys(p)).toHaveLength(22);expect(result?.proposal.postedEvidence?.amount).toBe('100.0100')
  expect(result?.currentLineVersion).toBe('0')
 })
 it('keeps every retained evidence row and the native zero-cost purchase branch without inventing a journal',()=>{
  const v=fixture(company),p=v.invoiceLineCogs.proposal,m=p.attributedEvidence[0]!
  p.attributedEvidence=Array.from({length:13},(_,index)=>({...structuredClone(m),source:{...structuredClone(m.source),fingerprint:String(index+1).padStart(64,'0')}}))
  const q=p.purchaseAttributedEvidence[0]!,a=q.source.approval
  Object.assign(a,{status:'RoundedNoCost',journalId:null,signedAmount:'0'});q.currentJournalId=null;q.currentJournalTotal='0';q.source.signedAmount='0'
  a.recordedRounding.rounding.roundedSignedAmount='0'
  const decoded=decodeWorkDecisionPreview(v,company,kind,id)?.invoiceLineCogs
  expect(decoded?.proposal.attributedEvidence).toHaveLength(13);expect(decoded?.proposal.purchaseAttributedEvidence?.[0].currentJournalId).toBeNull()
  expect(decoded?.proposal.purchaseAttributedEvidence?.[0].source.approval.status).toBe('RoundedNoCost')
 })
 it('shows a stale prepared proposal read-only and distinguishes current line revision from proposal version',()=>{
  const v=fixture(company,false,'9007199254740993')
  expect(v.pending).toBe(false);expect(decodeWorkDecisionPreview(v,company,kind,id)?.sourceVersion).toBe('1')
  v.pending=true;expect(decodeWorkDecisionPreview(v,company,kind,id)).toBeNull()
 })
 it.each(['0','2','01','+1','1\n','1\r','9223372036854775807','9223372036854775808'])('refuses every non-original proposal command version %j',version=>{
  expect(isWorkDecisionRequest({...request,expectedVersion:version},kind)).toBe(false)
 })
 it('has only four command fields, no reject/reason or foreign selection/private evidence',()=>{
  expect(isWorkDecisionRequest(request,kind)).toBe(true)
  for(const extra of [{approve:false},{reason:''},{reason:'x'},{invoiceLineCogs:{}},{approvalReceipt:{}},{sourceFactsDigest:'A'.repeat(64)},
    {postingAccountMappingSelection:{expectedActiveMappingVersion:'0'}},{approvedSourceCatalogSelection:{expectedActiveCatalogVersion:'0'}},
    {normalJournalDraft:{periodId:id}},{delegationId:id+'\n'}])expect(isWorkDecisionRequest({...request,...extra},kind)).toBe(false)
 })
 it.each(['missing','foreign','id','header','extra','cross','status','pending','maker','reason','ref','caption','rule','source','source-version','posted-version','shipment','date','utc','correction'])('refuses malformed, unbound or foreign facts %s',fault=>{
  const v=fixture(company),h=v.invoiceLineCogs,p=h.proposal;let raw:unknown=v
  if(fault==='missing')raw={...v,invoiceLineCogs:undefined}
  else if(fault==='foreign')p.companyId=other
  else if(fault==='id')p.proposalId=other
  else if(fault==='header')Reflect.deleteProperty(p,'reason')
  else if(fault==='extra')Object.assign(p,{approvalReceipt:{}})
  else if(fault==='cross')raw={...v,manufacturingCostDifference:{}}
  else if(fault==='status')Object.assign(p,{status:['Prepared']})
  else if(fault==='pending')v.pending=false
  else if(fault==='maker'){Object.assign(p,{status:'Approved',version:'2',approvedBy:p.preparedBy,approvedAtUtc:p.preparedAtUtc});v.sourceVersion='2';v.pending=false;h.currentLineVersion='1'}
  else if(fault==='reason')p.reason=' bad '
  else if(fault==='ref')v.references[0]!.recordId=other
  else if(fault==='caption')v.documentLabel='wrong'
  else if(fault==='rule')Object.assign(v.rules[0]!,{actual:'1'})
  else if(fault==='source')h.source.invoiceLineId=other
  else if(fault==='source-version')h.source.finalizedVersion='1'
  else if(fault==='posted-version')p.postedEvidence.finalizedVersion='1'
  else if(fault==='shipment')h.source.shipmentId=other
  else if(fault==='date')p.accountingDate='2026-02-30'
  else if(fault==='utc')p.preparedAtUtc='2026-11-28T24:00:00Z'
  else Object.assign(p,{attributedCorrections:[{attributionFingerprint:'A'.repeat(64),sourceJournalId:other,reversingCorrectionJournalId:id,replacementCorrectionJournalId:id}]})
  expect(decodeWorkDecisionPreview(raw,company,kind,id)).toBeNull()
 })
 it.each(['finalizedVersion','plan','profile','sequence','purchase-version','purchase-profile','rounding-profile','purchase-sequence','current'])('refuses numeric, noncanonical and overflowing Work long at %s',field=>{
  for(const invalid of [1,'01','+1','1\n','9223372036854775808']){
   const v=fixture(company),h=v.invoiceLineCogs,p=h.proposal
   const target=field==='plan'?p.accountPlan:field==='profile'?p.attributedEvidence[0]!.source:field==='sequence'?p.attributedEvidence[0]!.source.shipment:
    field==='purchase-version'||field==='purchase-profile'?p.purchaseAttributedEvidence[0]!.source.approval:field==='rounding-profile'?p.purchaseAttributedEvidence[0]!.source.approval.recordedRounding:
    field==='purchase-sequence'?p.purchaseAttributedEvidence[0]!.source.candidates[0]!.shipment:field==='current'?h:p
   const key=field==='plan'?'packageVersion':field==='profile'||field==='purchase-profile'||field==='rounding-profile'?'moneyProfileVersion':
    field==='sequence'||field==='purchase-sequence'?'provenOriginalSequence':field==='purchase-version'?'proposalVersion':field==='current'?'currentLineVersion':'finalizedVersion'
   Object.assign(target,{[key]:invalid});expect(decodeWorkDecisionPreview(v,company,kind,id),`${field}/${String(invalid)}`).toBeNull()
  }
 })
 it('closes final LF anchors in raw money, ratio, UUID and fingerprint without rewriting native text',()=>{
  for(const field of ['money','ratio','uuid','hash']){
   const v=fixture(company),m=v.invoiceLineCogs.proposal.attributedEvidence[0]!.source
   if(field==='money')m.signedAmount+='\n';else if(field==='ratio')m.exactSignedAmount.numerator+='\n';else if(field==='uuid')m.journalId+='\n';else m.fingerprint+='\n'
   expect(decodeWorkDecisionPreview(v,company,kind,id)).toBeNull()
  }
 })
 it('retains source-only native numeric defaults; alternate representation is accepted only by boolean Work validation',()=>{
  const v=fixture(company),p=v.invoiceLineCogs.proposal
  expect(decodeCogsProposal(p,company,cogsInvoice,cogsLine)).toBeNull()
  expect(postedManufacturingCostShare(p.attributedEvidence[0]!.source,company)).toBe(false)
  expect(postedPurchaseCostShare(p.purchaseAttributedEvidence[0]!.source,company,v.invoiceLineCogs.source.productId)).toBe(false)
  // Independent short native values; no Work validation is performed through a numeric substitute.
  const native={...p,finalizedVersion:3,version:1,expectedLineVersion:0,accountPlan:{...p.accountPlan,packageVersion:3,catalogVersion:3},
    postedEvidence:{...p.postedEvidence,finalizedVersion:3}}
  Reflect.deleteProperty(native,'attributedEvidence');Reflect.deleteProperty(native,'purchaseAttributedEvidence')
  expect(validCogsProposalWire(native,company,cogsInvoice,cogsLine)).toBe(true)
  expect(decodeCogsProposal(native,company,cogsInvoice,cogsLine)).toBe(native)
  const m=p.attributedEvidence[0]!.source,q=p.purchaseAttributedEvidence[0]!.source
  expect(postedManufacturingCostShare({...m,moneyProfileVersion:2,shipment:{...m.shipment,provenOriginalSequence:null}},company)).toBe(true)
  const nativePurchase={...q,approval:{...q.approval,proposalVersion:3,moneyProfileVersion:1,
    recordedRounding:{...q.approval.recordedRounding,moneyProfileVersion:2}},
    candidates:q.candidates.map(c=>({...c,shipment:{...c.shipment,provenOriginalSequence:null}}))}
  expect(postedPurchaseCostShare(nativePurchase,company,v.invoiceLineCogs.source.productId)).toBe(true)
  expect(cogsWorkLong('0',0)).toBe(true);expect(cogsWorkLong('0')).toBe(false)
 })
 it('sends only the frozen four fields and accepts the exact native Approved receipt wire',async()=>{
  const fetch=vi.fn().mockResolvedValue(new Response(JSON.stringify({companyId:company,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK original'}),
   {headers:{'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}}));vi.stubGlobal('fetch',fetch)
  expect((await sendWorkDecision(company,kind,id,request,'SENTETIK fixed key')).type).toBe('applied')
  expect(JSON.parse(fetch.mock.calls[0]![1].body)).toEqual(request);expect(new Headers(fetch.mock.calls[0]![1].headers).get('Idempotency-Key')).toBe('SENTETIK fixed key')
 })
})
