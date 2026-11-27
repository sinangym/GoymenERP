import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeJournalReversalProposal } from '../accounting/journalReversalContract'
import { journalReversalFixture } from './journalReversalDecisionFixture'
import { JOURNAL_REVERSAL_WORK_KIND as kind } from './workJournalReversalDecision'
import { decodeWorkDecisionPreview, isWorkDecisionKind, isWorkDecisionRequest, sendWorkDecision, WORK_DECISION_KINDS } from './workDecisionApi'
const company='aaaaaaaa-0000-4000-8000-000000000001',id='aaaaaaaa-0000-4000-8000-000000000002'
const request={expectedVersion:'1',approve:true,reason:null,delegationId:null}
afterEach(()=>vi.unstubAllGlobals())
describe('staged Work journal reversal native contract',()=>{
 it('keeps exact native sixteen numeric fields and full Int32 in the paired published purpose',()=>{
  const value=journalReversalFixture(company,id,2147483647)
  expect(decodeWorkDecisionPreview(value,company,kind,id)?.accountingJournalReversal).toEqual(value.accountingJournalReversal)
  expect(Object.keys(value.accountingJournalReversal.proposal)).toHaveLength(16)
  expect(isWorkDecisionKind(kind)).toBe(true);expect(WORK_DECISION_KINDS.filter(value=>value===kind)).toEqual([kind]);expect(WORK_DECISION_KINDS).toHaveLength(49)
 })
 it.each(['text-version','unsafe','overflow','fraction','zero','version-LF','status','partial','source-equal','foreign','id','reason-trim','reason-over','decision-reason','date-default','bad-calendar','utc','private','cross-proof','rule','actual','caption','ref-id','ref-role','ref-label','ref-link','reject','reason-required'] as const)('rejects malformed native proof: %s',fault=>{
  const value=journalReversalFixture(company,id),p=value.accountingJournalReversal.proposal;let raw:unknown=value
  if(fault==='text-version')raw={...value,accountingJournalReversal:{...value.accountingJournalReversal,proposal:{...p,version:'1'}}}
  else if(fault==='unsafe')p.version=9007199254740992
  else if(fault==='overflow')p.version=2147483648
  else if(fault==='fraction')p.version=1.5
  else if(fault==='zero')p.version=0
  else if(fault==='version-LF')value.sourceVersion='1\n'
  else if(fault==='status')p.status='Rejected'
  else if(fault==='partial')p.decidedBy=company
  else if(fault==='source-equal'){p.status='Approved';value.pending=false;p.reversalJournalId=p.sourceJournalId;p.decidedBy=company;p.decidedAtUtc=p.proposedAtUtc}
  else if(fault==='foreign')p.companyId=id
  else if(fault==='id')p.proposalId=company
  else if(fault==='reason-trim')p.reason=' reason '
  else if(fault==='reason-over')p.reason='x'.repeat(1025)
  else if(fault==='decision-reason')p.decisionReason='not-native'
  else if(fault==='date-default')p.accountingDate='0001-01-01'
  else if(fault==='bad-calendar')p.transactionDate='2026-02-30'
  else if(fault==='utc')p.proposedAtUtc+='\n'
  else if(fault==='private')raw={...value,accountingJournalReversal:{...value.accountingJournalReversal,cancellationSourceActorId:company}}
  else if(fault==='cross-proof')raw={...value,accountingPeriodOpening:{}}
  else if(fault==='rule')value.rules[0]!.ruleCode='wrong'
  else if(fault==='actual')raw={...value,rules:[{...value.rules[0],actual:'1'}]}
  else if(fault==='caption')value.documentLabel='wrong'
  else if(fault==='ref-id')value.references[0]!.recordId=id
  else if(fault==='ref-role')value.references[0]!.role='other'
  else if(fault==='ref-label')value.references[0]!.label='wrong'
  else if(fault==='ref-link')value.references[0]!.link='/wrong'
  else if(fault==='reject')value.rejectionSupported=true
  else value.approvalReasonRequired=true
  expect(decodeWorkDecisionPreview(raw,company,kind,id)).toBeNull()
 })
 it.each(['sourceFactsDigest','previewDigest'] as const)('requires exact uppercase64 %s without LF/CR replacement',field=>{
  for(const suffix of ['\n','\r','\r\n']){
   const value=journalReversalFixture(company,id);value.accountingJournalReversal[field]='A'.repeat(64-suffix.length)+suffix
   expect(decodeWorkDecisionPreview(value,company,kind,id)).toBeNull()
  }
  const lower=journalReversalFixture(company,id);lower.accountingJournalReversal[field]='a'.repeat(64)
  expect(decodeWorkDecisionPreview(lower,company,kind,id)).toBeNull()
 })
 it('shows the native Approved original proposal without a new chronology policy',()=>{
  const value=journalReversalFixture(company,id,2),p=value.accountingJournalReversal.proposal
  value.pending=false;p.status='Approved';p.reversalJournalId=company;p.decidedBy=company;p.decidedAtUtc='2026-11-01T00:00:00Z'
  expect(decodeWorkDecisionPreview(value,company,kind,id)?.accountingJournalReversal?.proposal).toEqual(p)
  p.decidedBy=p.proposedBy
  expect(decodeWorkDecisionPreview(value,company,kind,id)).toBeNull()
 })
 it('accepts native non-default year1 DateOnly only through Work while preserving the default decoder',()=>{
  const value=journalReversalFixture(company,id),p=value.accountingJournalReversal.proposal
  expect(decodeJournalReversalProposal(p,company)).not.toBeNull()
  p.transactionDate=p.accountingDate='0001-01-02';p.valueDate='0099-12-31'
  expect(decodeJournalReversalProposal(p,company)).toBeNull()
  expect(decodeWorkDecisionPreview(value,company,kind,id)?.accountingJournalReversal?.proposal.transactionDate).toBe('0001-01-02')
 })
 it('keeps only four command fields and canonical incrementable Int32 versions',()=>{
  expect(isWorkDecisionRequest(request,kind)).toBe(true)
  expect(isWorkDecisionRequest({...request,expectedVersion:'2147483646'},kind)).toBe(true)
  for(const version of ['0','01','+1','1\n','1\r','2147483647','2147483648','1.0'])expect(isWorkDecisionRequest({...request,expectedVersion:version},kind)).toBe(false)
  for(const extra of [{approve:false},{reason:''},{reason:'onay'},{normalJournalDraft:{periodId:id}},{accountingPeriodReopening:{expectedPeriodVersion:'3'}}])
   expect(isWorkDecisionRequest({...request,...extra},kind)).toBe(false)
 })
 it('accepts only exact six-field Approved receipt and never copies human proof into the command',async()=>{
  const result={companyId:company,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK original'}
  const response=(extra:object={})=>new Response(JSON.stringify({...result,...extra}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}})
  const fetch=vi.fn().mockResolvedValue(response());vi.stubGlobal('fetch',fetch)
  expect((await sendWorkDecision(company,kind,id,request,'same-key')).type).toBe('applied')
  expect(JSON.parse(String(fetch.mock.calls[0]![1].body))).toEqual(request)
  for(const extra of [{state:'Rejected'},{state:'Pending'},{companyId:id},{itemId:company},{kind:'accounting.period-opening.v1'},{replayed:false},{originalCorrelationId:'wrong'},{accountingJournalReversal:{}}]){
   fetch.mockResolvedValue(response(extra));expect((await sendWorkDecision(company,kind,id,request,'same-key')).type).toBe('unknown')
  }
  expect((await sendWorkDecision(company,kind,id,{...request,approve:false},'same-key')).type).toBe('unknown')
 })
})
