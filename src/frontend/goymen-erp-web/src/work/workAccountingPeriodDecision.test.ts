import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodePeriod } from '../accounting/contract'
import { decodePeriodClosure, decodePeriodReopening } from '../accounting/periodLifecycleApi'
import { accountingOpeningFixture, accountingReopeningFixture, openingPeriodKind, reopeningPeriodKind } from './accountingPeriodDecisionFixture'
import { accountingPeriodVersion } from './workAccountingPeriodDecision'
import { decodeWorkDecisionPreview, isWorkDecisionRequest, sendWorkDecision, WORK_DECISION_KINDS, type WorkDecisionRequest } from './workDecisionApi'
const company='aaaaaaaa-0000-4000-8000-000000000001', id='aaaaaaaa-0000-4000-8000-000000000002'
const request: WorkDecisionRequest={expectedVersion:'1',approve:true,reason:null,delegationId:null}
afterEach(()=>vi.unstubAllGlobals())
describe('Work period native projections',()=>{
 it('publishes both paired kinds and reuses exact full native shapes',()=>{
  expect(WORK_DECISION_KINDS).toHaveLength(62)
  const opening=accountingOpeningFixture(company,id,'9007199254740993'),reopening=accountingReopeningFixture(company,id,'9007199254740993')
  expect(decodeWorkDecisionPreview(opening,company,openingPeriodKind,id)?.accountingPeriodOpening).toEqual(opening.accountingPeriodOpening)
  expect(decodeWorkDecisionPreview(reopening,company,reopeningPeriodKind,id)?.accountingPeriodReopening).toEqual(reopening.accountingPeriodReopening)
 })
 it.each(['numeric','zero','leading-zero','LF','CR','overflow','foreign','caption','ref-id','ref-role','ref-label','ref-link','rule','actual','own','private','reject','reason','cross-proof','date','UTC'] as const)('rejects malformed opening owner: %s',fault=>{
  const value=accountingOpeningFixture(company,id),p=value.accountingPeriodOpening.period
  let raw:unknown=value
  if(fault==='numeric')raw={...value,sourceVersion:1,accountingPeriodOpening:{...value.accountingPeriodOpening,period:{...p,version:1}}}
  else if(fault==='zero')p.version=value.sourceVersion='0'
  else if(fault==='leading-zero')p.version=value.sourceVersion='01'
  else if(fault==='LF')p.version=value.sourceVersion='1\n'
  else if(fault==='CR')p.version=value.sourceVersion='1\r'
  else if(fault==='overflow')p.version=value.sourceVersion='9223372036854775808'
  else if(fault==='foreign')p.companyId=id
  else if(fault==='caption')value.documentLabel='wrong'
  else if(fault==='ref-id')value.references[0]!.recordId=company
  else if(fault==='ref-role')value.references[0]!.role='other'
  else if(fault==='ref-label')value.references[0]!.label='wrong'
  else if(fault==='ref-link')value.references[0]!.link='/wrong'
  else if(fault==='rule')value.rules[0]!.ruleCode='wrong'
  else if(fault==='actual')raw={...value,rules:[{...value.rules[0],actual:'1'}]}
  else if(fault==='own')p.isOwnProposal=true
  else if(fault==='private')raw={...value,accountingPeriodOpening:{...value.accountingPeriodOpening,approvalReceipt:{}}}
  else if(fault==='reject')value.rejectionSupported=true
  else if(fault==='reason')value.approvalReasonRequired=true
  else if(fault==='cross-proof')raw={...value,accountingPeriodReopening:accountingReopeningFixture(company,id).accountingPeriodReopening}
  else if(fault==='date')p.startsOn='2026-02-30'
  else p.preparedAtUtc='2026-11-27T09:00:00+03:00'
  expect(decodeWorkDecisionPreview(raw,company,openingPeriodKind,id)).toBeNull()
 })
 it.each(['sourceFactsDigest','previewDigest'] as const)('rejects all replacement line endings in %s',field=>{
  for(const suffix of ['\n','\r','\r\n'])for(const reopen of [false,true]){
   const value=reopen?accountingReopeningFixture(company,id):accountingOpeningFixture(company,id)
   const human='accountingPeriodReopening'in value?value.accountingPeriodReopening:value.accountingPeriodOpening
   human[field]='A'.repeat(64-suffix.length)+suffix
   expect(decodeWorkDecisionPreview(value,company,reopen?reopeningPeriodKind:openingPeriodKind,id)).toBeNull()
  }
 })
 it.each(['proposal-company','proposal-id','closure-id','closure-period','closed-version','numeric-closed','later-missing','later-rule','approval-supported','future-version','partial-approval','closure-approval','line-count','hash-LF'] as const)('rejects malformed reopening owner: %s',fault=>{
  const value=accountingReopeningFixture(company,id),h=value.accountingPeriodReopening
  let raw:unknown=value
  if(fault==='proposal-company')h.reopening.companyId=id
  else if(fault==='proposal-id')h.reopening.id=company
  else if(fault==='closure-id')h.closure.id=company
  else if(fault==='closure-period')h.closure.periodId=id
  else if(fault==='closed-version')h.reopening.closedPeriodVersion='4'
  else if(fault==='numeric-closed')raw={...value,accountingPeriodReopening:{...h,reopening:{...h.reopening,closedPeriodVersion:3}}}
  else if(fault==='later-missing')raw={...value,accountingPeriodReopening:{...h,laterPeriodClosed:undefined}}
  else if(fault==='later-rule')h.laterPeriodClosed=true
  else if(fault==='approval-supported')value.approvalSupported=false
  else if(fault==='future-version')h.reopening.version=value.sourceVersion='3'
  else if(fault==='partial-approval')h.reopening.approvedBy=company
  else if(fault==='closure-approval')h.closure.reopeningId=id
  else if(fault==='line-count')h.closure.lineCount=2147483648
  else h.closure.sourceHash='E'.repeat(63)+'\n'
  expect(decodeWorkDecisionPreview(raw,company,reopeningPeriodKind,id)).toBeNull()
 })
 it('keeps terminal proposal2 separate from current period version and uses native chronology without new ordering policy',()=>{
  const value=accountingReopeningFixture(company,id),h=value.accountingPeriodReopening
  value.pending=false;value.sourceVersion=h.reopening.version='2';h.period.version='9007199254740993'
  h.reopening.approvedBy=h.closure.reopenedBy=company;h.reopening.approvedAtUtc=h.closure.reopenedAtUtc='2026-11-01T00:00:00Z';h.closure.reopeningId=id
  expect(decodeWorkDecisionPreview(value,company,reopeningPeriodKind,id)).not.toBeNull()
  h.period.isOwnProposal=true
  expect(decodeWorkDecisionPreview(value,company,reopeningPeriodKind,id)).toBeNull()
 })
 it('preserves native default numeric and chronology guards, while Work uses canonical text',()=>{
  const o=accountingOpeningFixture(company,id).accountingPeriodOpening.period,h=accountingReopeningFixture(company,id).accountingPeriodReopening
  expect(decodePeriod(o,company)).toBeNull();expect(decodePeriod({...o,version:1},company)?.version).toBe(1)
  expect(decodePeriodReopening(h.reopening,company,h.period.periodId)).toBeNull()
  expect(decodePeriodReopening({...h.reopening,version:1,closedPeriodVersion:3},company,h.period.periodId)?.version).toBe(1)
  expect(decodePeriodClosure(h.closure,company,h.period.periodId)).toBeNull()
  expect(decodePeriodClosure({...h.closure,closedPeriodVersion:3},company,h.period.periodId)?.closedPeriodVersion).toBe(3)
  expect(accountingPeriodVersion('9223372036854775807')).toBe('9223372036854775807')
 })
 it('requires only the seen closed-period version on reopening and rejects every cross-kind selection',()=>{
  const selected={...request,accountingPeriodReopening:{expectedPeriodVersion:'9007199254740993'}}
  expect(isWorkDecisionRequest(request,openingPeriodKind)).toBe(true);expect(isWorkDecisionRequest(selected,reopeningPeriodKind)).toBe(true)
  for(const k of WORK_DECISION_KINDS.filter(k=>k!==reopeningPeriodKind))expect(isWorkDecisionRequest(selected,k)).toBe(false)
  expect(isWorkDecisionRequest(selected,openingPeriodKind)).toBe(false);expect(isWorkDecisionRequest(request,reopeningPeriodKind)).toBe(false)
  for(const body of [{...selected,approve:false},{...selected,reason:''},{...selected,expectedVersion:'01'},
    {...selected,normalJournalDraft:{periodId:id}},{...selected,accountingPeriodReopening:{expectedPeriodVersion:'9223372036854775807'}},
    {...selected,accountingPeriodReopening:{expectedPeriodVersion:'3\n'}},{...selected,accountingPeriodReopening:{expectedPeriodVersion:'3',extra:true}}])
    expect(isWorkDecisionRequest(body,reopeningPeriodKind)).toBe(false)
 })
 it.each([openingPeriodKind,reopeningPeriodKind] as const)('requires an exact Approved six-field native result for %s',async kind=>{
  const body=kind===reopeningPeriodKind?{...request,accountingPeriodReopening:{expectedPeriodVersion:'3'}}:request
  const result={companyId:company,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK original'}
  const response=(extra:object={})=>new Response(JSON.stringify({...result,...extra}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}})
  const fetch=vi.fn().mockResolvedValue(response());vi.stubGlobal('fetch',fetch)
  expect((await sendWorkDecision(company,kind,id,body,'same-key')).type).toBe('applied')
  expect(JSON.parse(String(fetch.mock.calls[0]![1].body))).toEqual(body)
  for(const bad of [{state:'Open'},{state:'Rejected'},{companyId:id},{itemId:company},{originalCorrelationId:'wrong'},{approvalReceipt:{}}, {kind:'accounting.normal-journal-draft.v1'}]){
   fetch.mockResolvedValue(response(bad));expect((await sendWorkDecision(company,kind,id,body,'same-key')).type).toBe('unknown')
  }
  expect((await sendWorkDecision(company,kind,id,{...body,approve:false},'same-key')).type).toBe('unknown')
 })
})

it.each(['e'.repeat(64),'aB'.repeat(32)])('preserves native lower/mixed-case closure SourceHash %s',sourceHash=>{
 const value=accountingReopeningFixture(company,id);value.accountingPeriodReopening.closure.sourceHash=sourceHash
 expect(decodeWorkDecisionPreview(value,company,reopeningPeriodKind,id)?.accountingPeriodReopening?.closure.sourceHash).toBe(sourceHash)
})
it.each(['\n','\r','\r\n'])('rejects closure SourceHash replacement ending %j without normalizing it',suffix=>{
 const value=accountingReopeningFixture(company,id);value.accountingPeriodReopening.closure.sourceHash='a'.repeat(64-suffix.length)+suffix
 expect(decodeWorkDecisionPreview(value,company,reopeningPeriodKind,id)).toBeNull()
})
