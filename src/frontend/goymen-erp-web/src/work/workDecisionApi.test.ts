import { checkTaxIdentity } from '../cari/taxIdentity'
import type { WorkDecisionPreview } from './workDecisionApi'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeWorkDecisionPreview, sendWorkDecision, isWorkDecisionRequest, isWorkDecisionKind, isSalesWorkDecisionKind, workDecisionReasonLimit, workDecisionLocalUtc, WORK_DECISION_KINDS, COUNTERPARTY_WORK_DECISION_CAPABILITY, isCounterpartyWorkDecisionKind } from './workDecisionApi'
import { decodeDelegation, decodeDelegationPage, decodePeople } from './workDelegationApi'
const company='aaaaaaaa-0000-4000-8000-000000000001', id='cccccccc-0000-4000-8000-000000000001'
const kind='finance.cash-account.v1' as const
const preview={companyId:company,kind,itemId:id,documentLabel:'SENTETIK kasa',sourceVersion:'7',pending:true,approvalReasonRequired:false,references:[],rules:[]}
const request={expectedVersion:'7',approve:true,reason:null,delegationId:null}
const delegation={id,companyId:company,kind,approverPermission:'finance.cash-account.approve',delegatorUserId:company,delegateUserId:id,
 startsAtUtc:'2026-11-26T08:00:00Z',endsAtUtc:'2026-11-27T08:00:00Z',version:'1',recordedByUserId:company,recordedAtUtc:'2026-11-26T07:00:00Z',reason:'SENTETIK',revokedByUserId:null,revokedAtUtc:null,revocationReason:null}
afterEach(()=>vi.unstubAllGlobals())
describe('WP8 bounded owner contracts',()=>{
 it('rejects scope, version and unknown-field drift instead of rendering partial data',()=>{
  expect(decodeWorkDecisionPreview(preview,company,kind,id)).not.toBeNull()
  for(const change of [{companyId:id},{itemId:company},{kind:'payment.execute.v1'},{sourceVersion:'0'},{sourceVersion:'9223372036854775808'},{extra:true},{references:[{role:'party',label:'SENTETIK'}]}])
   expect(decodeWorkDecisionPreview({...preview,...change},company,kind,id)).toBeNull()
  expect(decodeWorkDecisionPreview({...preview,kind:'inventory.opening-cost.v1',approvalReasonRequired:true,sourceVersion:`submission/${id}`},company,'inventory.opening-cost.v1',id)).not.toBeNull()
  expect(isWorkDecisionRequest({...request,companyId:company})).toBe(false)
 })
 it('sends the displayed version, delegation and original key; accepts only the matching owner result',async()=>{
  const fetch=vi.fn().mockResolvedValue(new Response(JSON.stringify({companyId:company,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-original'}),
    {status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'}}))
  vi.stubGlobal('fetch',fetch)
  const body={...request,delegationId:id};expect((await sendWorkDecision(company,kind,id,body,'same-key')).type).toBe('applied')
  const init=fetch.mock.calls[0]![1] as RequestInit
  expect(JSON.parse(init.body as string)).toEqual(body);expect((init.headers as Record<string,string>)['Idempotency-Key']).toBe('same-key')
  fetch.mockResolvedValue(new Response(JSON.stringify({companyId:id,kind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK'}),{status:200}))
  expect((await sendWorkDecision(company,kind,id,request,'same-key')).type).toBe('unknown')
 })
 it('does not accept a replay without its matching original correlation header',async()=>{
  vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(JSON.stringify({companyId:company,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK'}),{status:200})))
  expect((await sendWorkDecision(company,kind,id,request,'key')).type).toBe('unknown')
 })
 it('rejects foreign, incomplete, duplicate and inconsistent delegation data',()=>{
  expect(decodeDelegation(delegation,company)).not.toBeNull()
  for(const change of [{companyId:id},{endsAtUtc:delegation.startsAtUtc},{version:'2'},{revokedAtUtc:delegation.startsAtUtc},{extra:true}]) expect(decodeDelegation({...delegation,...change},company)).toBeNull()
  const page={companyId:company,items:[delegation],page:0,hasMore:false}
  expect(decodeDelegationPage(page,company,0)?.items).toHaveLength(1)
  expect(decodeDelegationPage({...page,items:[delegation,delegation]},company,0)).toBeNull()
  expect(decodeDelegationPage({...page,page:1},company,0)).toBeNull()
  expect(decodePeople({companyId:company,kind,items:[{userId:id,displayName:'SENTETIK'}],hasMore:false},company,kind)).not.toBeNull()
  expect(decodePeople({companyId:id,kind,items:[],hasMore:false},company,kind)).toBeNull()
 })
})

it('accepts the native Purchasing purpose shape while preserving strict company/version and its required 4000 reason family', async () => {
 const purchaseKind='purchasing.request.v1'
 expect(isWorkDecisionKind(purchaseKind)).toBe(true)
 expect(isSalesWorkDecisionKind(purchaseKind)).toBe(false)
 expect(workDecisionReasonLimit(purchaseKind)).toBe(4000)
 const value={...preview,kind:purchaseKind}
 expect(decodeWorkDecisionPreview(value,company,purchaseKind,id)).not.toBeNull()
 for(const delta of [{companyId:id},{itemId:company},{sourceVersion:'0'},{sourceVersion:'9223372036854775808'},{approvalReasonRequired:true},{extra:true}])
  expect(decodeWorkDecisionPreview({...value,...delta},company,purchaseKind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...value,pending:false},company,purchaseKind,id)).not.toBeNull()
 const body={companyId:company,kind:purchaseKind,itemId:id,state:'Rejected',replayed:true,originalCorrelationId:'original'}
 const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}}))
 vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,purchaseKind,id,{...request,approve:false,reason:'Ret gerekçesi'},'original-key')).type).toBe('applied')
 for(const delta of [{companyId:id},{kind},{itemId:company},{state:'Approved'},{replayed:false},{extra:true}]) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}}))
  expect((await sendWorkDecision(company,purchaseKind,id,{...request,approve:false,reason:'Ret gerekçesi'},'original-key')).type).toBe('unknown')
 }
})

it('accepts only the native current Rejected projection for a replayed request approval with matching receipt identity', async () => {
 const purchaseKind='purchasing.request.v1'
 const body={companyId:company,kind:purchaseKind,itemId:id,state:'Rejected',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 const approve={...request,approve:true}
 expect((await sendWorkDecision(company,purchaseKind,id,approve,'original-key')).type).toBe('applied')
 for(const delta of [{replayed:false},{state:'Draft'},{state:'Submitted'},{state:'Withdrawn'},{state:'Cancelled'},{state:'Unknown'},{companyId:id},{itemId:company},{kind:'sales.invoice.v1'},{originalCorrelationId:'other'},{extra:true}]){
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,purchaseKind,id,approve,'original-key')).type).toBe('unknown')
 }
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,replayed:false}),{status:200}))
 expect((await sendWorkDecision(company,purchaseKind,id,approve,'original-key')).type).toBe('unknown')
 fetch.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200}))
 expect((await sendWorkDecision(company,purchaseKind,id,approve,'original-key')).type).toBe('unknown')
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,state:'Approved'}),{status:200,headers}))
 expect((await sendWorkDecision(company,purchaseKind,id,{...approve,approve:false,reason:'Ret'},'original-key')).type).toBe('unknown')
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,kind:'sales.invoice.v1'}),{status:200,headers}))
 expect((await sendWorkDecision(company,'sales.invoice.v1',id,approve,'original-key')).type).toBe('unknown')
})

it.each(['Finalized', 'OfficiallyCancelled'])('accepts ordinary invoice approval replay at native current %s only with matching receipt scope and correlation', async state => {
 const invoiceKind='sales.invoice.v1'
 const body={companyId:company,kind:invoiceKind,itemId:id,state,replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,invoiceKind,id,request,'original-key')).type).toBe('applied')
 for(const delta of [{companyId:id},{itemId:company},{kind:'sales.subcontract-invoice.v1'},{replayed:false},{originalCorrelationId:'other'},{extra:true},
  ...['Draft','PendingApproval','Rejected','Cancelled','Matched','Unknown'].map(state=>({state}))]) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,invoiceKind,id,request,'original-key')).type).toBe('unknown')
 }
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,replayed:false}),{status:200}))
 expect((await sendWorkDecision(company,invoiceKind,id,request,'original-key')).type).toBe('unknown')
 const invalidReceiptHeaders: Record<string,string>[]=[{}, {'Idempotency-Replayed':'true'}, {...headers,'Idempotency-Original-Correlation-Id':'other'}]
 for(const invalidHeaders of invalidReceiptHeaders) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200,headers:invalidHeaders}))
  expect((await sendWorkDecision(company,invoiceKind,id,request,'original-key')).type).toBe('unknown')
 }
 fetch.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200,headers}))
 expect((await sendWorkDecision(company,invoiceKind,id,{...request,approve:false,reason:'Ret'},'original-key')).type).toBe('unknown')
})

it('keeps subcontract immutable approval and other owner results strict when ordinary invoice current state support is added', async () => {
 for(const kind of ['sales.subcontract-invoice.v1','sales.order-draft.v1','purchasing.request.v1','finance.cash-account.v1'] as const) {
  for(const state of ['Finalized','OfficiallyCancelled']) {
   vi.stubGlobal('fetch',vi.fn(async()=>new Response(JSON.stringify({companyId:company,kind,itemId:id,state,replayed:true,originalCorrelationId:'original'}),
    {status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}})))
   expect((await sendWorkDecision(company,kind,id,request,'original-key')).type).toBe('unknown')
  }
 }
})

it.each([true,false])('accepts only reachable current SalesOrder states for recorded approve=%s with a complete replay receipt', async approve => {
 const orderKind='sales.order-draft.v1'
 const command={...request,approve,reason:approve?null:'Ret'}
 const expected=approve?'Approved':'Rejected'
 const reachable=approve?['Approved','Preparing','PendingApproval','Rejected']:['Rejected','Preparing','PendingApproval','Approved','Withdrawn']
 const body={companyId:company,kind:orderKind,itemId:id,state:'Preparing',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 for(const state of reachable) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,state}),{status:200,headers}))
  expect((await sendWorkDecision(company,orderKind,id,command,'original-key')).type).toBe('applied')
  if(state!==expected) {
   fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,state,replayed:false}),{status:200}))
   expect((await sendWorkDecision(company,orderKind,id,command,'original-key')).type).toBe('unknown')
  }
 }
 for(const delta of [{companyId:id},{itemId:company},{kind:'sales.subcontract-invoice.v1'},{replayed:false},{originalCorrelationId:'other'},{extra:true},
  ...['Draft','Submitted','Cancelled','Closed','Finalized','OfficiallyCancelled','Unknown',...(approve?['Withdrawn']:[])].map(state=>({state}))]) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,orderKind,id,command,'original-key')).type).toBe('unknown')
 }
 const invalidReceiptHeaders: Record<string,string>[]=[{}, {'Idempotency-Replayed':'true'}, {...headers,'Idempotency-Original-Correlation-Id':'other'}]
 for(const invalidHeaders of invalidReceiptHeaders) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200,headers:invalidHeaders}))
  expect((await sendWorkDecision(company,orderKind,id,command,'original-key')).type).toBe('unknown')
 }
})


it('requires the purchase order approve-only preview flag without weakening other owner previews', () => {
 const kind='purchasing.order.v1'
 const value={...preview,kind,rejectionSupported:false}
 expect(decodeWorkDecisionPreview(value,company,kind,id)).not.toBeNull()
 for(const delta of [{rejectionSupported:undefined},{rejectionSupported:true},{rejectionSupported:null},{companyId:id},{itemId:company},{sourceVersion:'0'},{extra:true}])
  expect(decodeWorkDecisionPreview({...value,...delta},company,kind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...preview,rejectionSupported:false},company,preview.kind,id)).toBeNull()
})

it.each(['Approved','Draft','Cancelled'])('accepts purchase order approval %s only within the native receipt contract and never accepts a rejection', async state => {
 const kind='purchasing.order.v1'
 const body={companyId:company,kind,itemId:id,state,replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,kind,id,request,'original-key')).type).toBe('applied')
 for(const delta of [{companyId:id},{itemId:company},{kind:'purchasing.request.v1'},{originalCorrelationId:'other'},{extra:true},
  ...['Rejected','Submitted','PendingApproval','Unknown'].map(state=>({state}))]) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,kind,id,request,'original-key')).type).toBe('unknown')
 }
 const invalidReceiptHeaders: Record<string,string>[]=[{}, {'Idempotency-Replayed':'true'}, {...headers,'Idempotency-Original-Correlation-Id':'other'}]
 for(const invalidHeaders of invalidReceiptHeaders) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200,headers:invalidHeaders}))
  expect((await sendWorkDecision(company,kind,id,request,'original-key')).type).toBe('unknown')
 }
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,replayed:false}),{status:200}))
 expect((await sendWorkDecision(company,kind,id,request,'original-key')).type).toBe(state==='Approved'?'applied':'unknown')
 for(const rejectedState of ['Approved','Rejected','Draft','Cancelled']) {
  for(const replayed of [true,false]) {
   fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,state:rejectedState,replayed}),{status:200,headers:replayed?headers:{}}))
   expect((await sendWorkDecision(company,kind,id,{...request,approve:false,reason:'Ret'},'original-key')).type).toBe('unknown')
  }
 }
})


it('requires the service order human approval reason metadata and the native thousand-character bound',()=>{
 const kind='purchasing.service-order.v1'
 const value={...preview,kind,approvalReasonRequired:true,rejectionSupported:false}
 expect(workDecisionReasonLimit(kind)).toBe(1000)
 expect(workDecisionReasonLimit('finance.credit-account.v1')).toBe(500)
 expect(workDecisionReasonLimit('inventory.opening-cost.v1')).toBe(500)
 expect(decodeWorkDecisionPreview(value,company,kind,id)).not.toBeNull()
 for(const delta of [{approvalReasonRequired:false},{rejectionSupported:undefined},{rejectionSupported:true},{rejectionSupported:null},{companyId:id},{itemId:company},{sourceVersion:'0'},{extra:true}])
  expect(decodeWorkDecisionPreview({...value,...delta},company,kind,id)).toBeNull()
})

it('keeps service order approval receipts immutable and never treats its absent rejection as success',async()=>{
 const kind='purchasing.service-order.v1'
 const command={...request,reason:'Human approval reason'}
 const body={companyId:company,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('applied')
 for(const delta of [{companyId:id},{itemId:company},{kind:'purchasing.order.v1'},{originalCorrelationId:'other'},{extra:true},
  ...['Draft','Cancelled','Rejected','FutureState'].map(state=>({state}))]) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('unknown')
 }
 const invalidHeaders: Record<string,string>[]=[{}, {'Idempotency-Replayed':'true'}, {...headers,'Idempotency-Original-Correlation-Id':'other'}]
 for(const bad of invalidHeaders) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200,headers:bad}))
  expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('unknown')
 }
 for(const replayed of [false,true]) for(const state of ['Approved','Rejected','Draft','Cancelled']) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,state,replayed}),{status:200,headers:replayed?headers:{}}))
  expect((await sendWorkDecision(company,kind,id,{...command,approve:false},'original-key')).type).toBe('unknown')
 }
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,replayed:false}),{status:200}))
 expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('applied')
})


it('requires native invoice variance approval metadata and the exact two-thousand-character owner bound',()=>{
 const kind='purchasing.incoming-invoice-variance.v1'
 const value={...preview,kind,approvalReasonRequired:true,rejectionSupported:false}
 expect(workDecisionReasonLimit(kind)).toBe(2000)
 expect(decodeWorkDecisionPreview(value,company,kind,id)).not.toBeNull()
 for(const delta of [{approvalReasonRequired:false},{rejectionSupported:undefined},{rejectionSupported:true},{rejectionSupported:null},{companyId:id},{itemId:company},{sourceVersion:'0'},{extra:true}])
  expect(decodeWorkDecisionPreview({...value,...delta},company,kind,id)).toBeNull()
})

it('accepts only the host-proved immutable Matched variance receipt for approval with exact scope, shape and replay headers',async()=>{
 const kind='purchasing.incoming-invoice-variance.v1',command={...request,reason:'Actual human reason'}
 const body={companyId:company,kind,itemId:id,state:'Matched',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('applied')
 for(const replayed of [true,false]) {
  for(const delta of [{companyId:id},{itemId:company},{kind:'purchasing.order.v1'},{extra:true},{varianceDecision:{}},
   ...['Approved','Rejected','Imported','Draft','Finalized','Cancelled','FutureState'].map(state=>({state}))]) {
   fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,replayed,...delta}),{status:200,headers:replayed?headers:{}}))
   expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('unknown')
  }
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,replayed}),{status:200,headers:replayed?headers:{}}))
  expect((await sendWorkDecision(company,kind,id,{...command,approve:false},'original-key')).type).toBe('unknown')
 }
 const invalidHeaders: Record<string,string>[]=[{}, {'Idempotency-Replayed':'true'}, {...headers,'Idempotency-Original-Correlation-Id':'other'}]
 for(const bad of invalidHeaders) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200,headers:bad}))
  expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('unknown')
 }
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,originalCorrelationId:'other'}),{status:200,headers}))
 expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('unknown')
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,replayed:false}),{status:200}))
 expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('applied')
 expect((await sendWorkDecision(company,'purchasing.service-order.v1',id,command,'original-key')).type).toBe('unknown')
})


it('requires the exact batch preview flags and preserves the native five-hundred-character reason bound',()=>{
 const kind='purchasing.goods-receipt-batch.v1',value={...preview,kind,rejectionSupported:true}
 expect(workDecisionReasonLimit(kind)).toBe(500)
 expect(decodeWorkDecisionPreview(value,company,kind,id)).not.toBeNull()
 for(const delta of [{approvalReasonRequired:true},{rejectionSupported:false},{rejectionSupported:undefined},{companyId:id},{itemId:company},{sourceVersion:'0'},{orderId:id}])
  expect(decodeWorkDecisionPreview({...value,...delta},company,kind,id)).toBeNull()
})

it.each([true,false])('accepts only the immutable batch receipt for approve=%s with exact scope and original replay correlation',async approve=>{
 const kind='purchasing.goods-receipt-batch.v1',command={...request,approve,reason:approve?null:'Actual rejection reason'}
 const state=approve?'Posted':'Rejected'
 const body={companyId:company,kind,itemId:id,state,replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('applied')
 for(const replayed of [true,false]) {
  for(const delta of [{companyId:id},{itemId:company},{kind:'purchasing.request.v1'},{orderId:id},
   ...['Approved','PendingApproval','Draft','Matched','FutureState',approve?'Rejected':'Posted'].map(state=>({state}))]) {
   fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,replayed,...delta}),{status:200,headers:replayed?headers:{}}))
   expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('unknown')
  }
 }
 const invalidHeaders: Record<string,string>[]=[{}, {'Idempotency-Replayed':'true'}, {...headers,'Idempotency-Original-Correlation-Id':'other'}]
 for(const bad of invalidHeaders) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify(body),{status:200,headers:bad}))
  expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('unknown')
 }
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,originalCorrelationId:'other'}),{status:200,headers}))
 expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('unknown')
 fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,replayed:false}),{status:200}))
 expect((await sendWorkDecision(company,kind,id,command,'original-key')).type).toBe('applied')
})


it('decodes only scoped Manufacturing full explicit reapproval options and keeps other owners unchanged', () => {
 const mfg='manufacturing.plan.v1'
 const rule={ruleCode:'manufacturing.plan-reapproval-after-order-cancel',threshold:null,actual:null,deviation:null,unit:null}
 const value={...preview,kind:mfg,rejectionSupported:false,rules:[rule],manufacturingPlanReapproval:{parentRollIds:[id,company]}}
 expect(decodeWorkDecisionPreview(value,company,mfg,id)).not.toBeNull()
 expect(workDecisionReasonLimit(mfg)).toBe(256)
 for(const delta of [{companyId:id},{sourceVersion:'0'},{pending:false},{rejectionSupported:true},{rules:[]},
  {manufacturingPlanReapproval:null},{manufacturingPlanReapproval:{parentRollIds:[]}},
  {manufacturingPlanReapproval:{parentRollIds:[id,id]}},{manufacturingPlanReapproval:{parentRollIds:['bad']}},
  {manufacturingPlanReapproval:{parentRollIds:[id],extra:true}}])
  expect(decodeWorkDecisionPreview({...value,...delta},company,mfg,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...preview,manufacturingPlanReapproval:null},company,kind,id)).toBeNull()
 expect(isWorkDecisionRequest(request,mfg)).toBe(true)
 const typed={...request,manufacturingPlanReapproval:{selectedLabelIds:[id]}}
 expect(isWorkDecisionRequest(typed,mfg)).toBe(true)
 expect(isWorkDecisionRequest(typed,kind)).toBe(false)
 for(const selection of [{selectedLabelIds:[id,id]},{selectedLabelIds:['bad']},{selectedLabelIds:[]},{selectedLabelIds:[id],extra:true}])
  expect(isWorkDecisionRequest({...request,manufacturingPlanReapproval:selection},mfg)).toBe(false)
 expect(isWorkDecisionRequest({...typed,approve:false},mfg)).toBe(false)
})

it.each([true,false])('keeps Manufacturing immutable decision states strict (approve=%s)', async approve => {
 const mfg='manufacturing.plan.v1'
 const body={companyId:company,kind:mfg,itemId:id,state:approve?'Approved':'Draft',replayed:false,originalCorrelationId:'original'}
 const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200}))
 vi.stubGlobal('fetch',fetch)
 const command={...request,approve,reason:approve?null:'Ret gerekçesi'}
 expect((await sendWorkDecision(company,mfg,id,command,'key')).type).toBe('applied')
 for(const delta of [{state:approve?'Draft':'Approved'},{state:'Released'},{state:'Rejected'},{companyId:id},{itemId:company},{extra:true},{replayed:true}]) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200}))
  expect((await sendWorkDecision(company,mfg,id,command,'key')).type).toBe('unknown')
 }
})


const lossKind='inventory.transfer-loss.v1'
const lossOptions={requestNumber:2,requestedAtUtc:'2026-11-26T08:00:00Z'}
const lossRequest={...request,reason:'İnsan kararı',inventoryTransferLoss:{requestNumber:2,decidedAtUtc:'2026-11-26T09:00:00Z'}}
it('decodes exact loss options among the mounted owner kinds without changing other owner wires', () => {
 const value={...preview,kind:lossKind,approvalReasonRequired:true,rejectionSupported:true,inventoryTransferLoss:lossOptions}
 expect(isWorkDecisionKind(lossKind)).toBe(true)
 expect(WORK_DECISION_KINDS.filter(kind=>kind===lossKind)).toHaveLength(1)
 expect(WORK_DECISION_KINDS).toHaveLength(19)
 expect(decodeWorkDecisionPreview(value,company,lossKind,id)).not.toBeNull()
 for(const delta of [{companyId:id},{itemId:company},{sourceVersion:'0'},{approvalReasonRequired:false},{rejectionSupported:false},
  {inventoryTransferLoss:null},{inventoryTransferLoss:{...lossOptions,requestNumber:0}},{inventoryTransferLoss:{...lossOptions,requestNumber:1.5}},
  {inventoryTransferLoss:{...lossOptions,requestedAtUtc:'2026-11-26T08:00:00+03:00'}},{inventoryTransferLoss:{...lossOptions,extra:true}},
  {manufacturingPlanReapproval:{parentRollIds:[id]}}])
  expect(decodeWorkDecisionPreview({...value,...delta},company,lossKind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...preview,inventoryTransferLoss:lossOptions},company,kind,id)).toBeNull()
 expect(isWorkDecisionRequest(lossRequest,lossKind)).toBe(true)
 for(const delta of [{reason:''},{reason:'R'.repeat(501)},{inventoryTransferLoss:null},
  {inventoryTransferLoss:{...lossRequest.inventoryTransferLoss,requestNumber:2147483648}},
  {inventoryTransferLoss:{...lossRequest.inventoryTransferLoss,decidedAtUtc:'bad'}},
  {inventoryTransferLoss:{...lossRequest.inventoryTransferLoss,extra:true}},
  {manufacturingPlanReapproval:{selectedLabelIds:[id]}}])
  expect(isWorkDecisionRequest({...lossRequest,...delta},lossKind)).toBe(false)
 expect(isWorkDecisionRequest(lossRequest,kind)).toBe(false)
 expect(isWorkDecisionRequest(lossRequest,'manufacturing.plan.v1')).toBe(false)
})
it('converts a human local time with the native UTC validation and never substitutes current time', () => {
 const input='2026-11-26T11:30:00'
 expect(workDecisionLocalUtc(input)).toBe(new Date(input).toISOString())
 for(const input of ['', 'bad', '0000-01-01T00:00:00']) expect(workDecisionLocalUtc(input)).toBeNull()
})
it.each([true,false])('accepts only the matching immutable native loss state, action and scope (approve=%s)', async approve => {
 const result={companyId:company,kind:lossKind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async(_input: RequestInfo | URL, _init?: RequestInit)=>new Response(JSON.stringify(result),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 const command={...lossRequest,approve}
 expect((await sendWorkDecision(company,lossKind,id,command,'frozen-key')).type).toBe('applied')
 expect(JSON.parse((fetch.mock.calls[0]![1] as RequestInit).body as string)).toEqual(command)
 for(const delta of [{state:approve?'Rejected':'Approved'},{state:'Pending'},{state:'Withdrawn'},{companyId:id},{kind},{itemId:company},{extra:true},{originalCorrelationId:'other'}]) {
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...result,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,lossKind,id,command,'frozen-key')).type).toBe('unknown')
 }
})


const cpKinds=['counterparty.tax-identity-change.v1','counterparty.legal-name-change.v1'] as const
const cpProofPreview = (cp: typeof cpKinds[number]): WorkDecisionPreview => ({
 ...preview,kind:cp,rejectionSupported:true,references:[{role:'party',label:'CAR-000001',recordId:id,link:null}],
 ...(cp==='counterparty.tax-identity-change.v1'
  ? {counterpartyTaxIdentityChange:{partyId:id,changeRequestId:id,currentTaxIdentityNumber:'10000000147',proposedTaxIdentityNumber:'10000000146',proposedKind:'tckn' as const,reason:'SENTETIK vergi düzeltme gerekçesi'}}
  : {counterpartyLegalNameChange:{partyId:id,changeRequestId:id,currentLegalName:'E'.repeat(250),proposedLegalName:'Y'.repeat(250),reason:'SENTETIK unvan değişikliği gerekçesi'}}),
})
it('mounts both Counterparty purposes once among seventeen and pins their actual cari capability ids', () => {
 expect(COUNTERPARTY_WORK_DECISION_CAPABILITY).toEqual({
  'counterparty.tax-identity-change.v1':'cari.tax-identity-change.work-decision',
  'counterparty.legal-name-change.v1':'cari.legal-name-change.work-decision',
 })
 expect(WORK_DECISION_KINDS).toHaveLength(19)
 for(const cp of cpKinds){
  expect(isWorkDecisionKind(cp)).toBe(true);expect(isCounterpartyWorkDecisionKind(cp)).toBe(true)
  expect(WORK_DECISION_KINDS.filter(kind=>kind===cp)).toEqual([cp])
  expect(workDecisionReasonLimit(cp)).toBe(1000)
 }
})
it.each(cpKinds)('strictly decodes scoped %s with exactly its owner identity proof', cp => {
 const value=cpProofPreview(cp)
 expect(decodeWorkDecisionPreview(value,company,cp,id)).not.toBeNull()
 expect(decodeWorkDecisionPreview({...value,pending:false},company,cp,id)).not.toBeNull()
 for(const delta of [{companyId:id},{itemId:company},{kind:cpKinds.find(kind=>kind!==cp)},{sourceVersion:'0'},
  {approvalReasonRequired:true},{rejectionSupported:false},{rejectionSupported:undefined},
  {references:[{...value.references[0],taxIdentityNumber:'SENTETIK'}]},
  {inventoryTransferLoss:{requestNumber:1,requestedAtUtc:'2026-11-26T08:00:00Z'}},
  {manufacturingPlanReapproval:{parentRollIds:[id]}}])
  expect(decodeWorkDecisionPreview({...value,...delta},company,cp,id)).toBeNull()
 expect(isWorkDecisionRequest({...request,reason:null},cp)).toBe(true)
 expect(isWorkDecisionRequest({...request,reason:'N'.repeat(1000)},cp)).toBe(true)
 expect(isWorkDecisionRequest({...request,reason:'N'.repeat(1001)},cp)).toBe(false)
})
it.each(cpKinds.flatMap(cp=>[true,false].map(approve=>({cp,approve}))))('keeps native optional note and immutable scoped result for $cp approve=$approve', async ({cp,approve}) => {
 const result={companyId:company,kind:cp,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(result),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 const command={...request,approve,reason:approve?null:'N'.repeat(1000)}
 expect((await sendWorkDecision(company,cp,id,command,'same-cp-key')).type).toBe('applied')
 expect(JSON.parse((fetch.mock.calls[0]![1] as RequestInit).body as string)).toEqual(command)
 for(const delta of [{companyId:id},{itemId:company},{kind},{state:approve?'Rejected':'Approved'},{state:'Pending'},
  {originalCorrelationId:'other'},{replayed:false},{resource:{}},{extra:true}]){
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...result,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,cp,id,command,'same-cp-key')).type).toBe('unknown')
 }
})

it.each(cpKinds)('requires one exact request-bound and party-bound human proof for %s', cp => {
 const value=cpProofPreview(cp)
 const field=cp==='counterparty.tax-identity-change.v1'?'counterpartyTaxIdentityChange':'counterpartyLegalNameChange'
 const other=cp==='counterparty.tax-identity-change.v1'?'counterpartyLegalNameChange':'counterpartyTaxIdentityChange'
 const proof=value[field]!
 for(const broken of [null,undefined,{}, {...proof,partyId:company}, {...proof,changeRequestId:company},
  {...proof,reason:''},{...proof,reason:'R'.repeat(1001)},{...proof,unexpected:'not permitted'}])
  expect(decodeWorkDecisionPreview({...value,[field]:broken},company,cp,id)).toBeNull()
 for(const references of [[],[...value.references,...value.references],[{...value.references[0],recordId:company}],
  [{...value.references[0],role:'other'}]])
  expect(decodeWorkDecisionPreview({...value,references},company,cp,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...value,[other]:null},company,cp,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...value,[other]:proof},company,cp,id)).toBeNull()
 expect(isWorkDecisionRequest({...request,[field]:proof},cp)).toBe(false)
 expect(decodeWorkDecisionPreview({...preview,[field]:proof},company,kind,id)).toBeNull()
})
it('validates native tax kind and number shape, and preserves both full 250-character legal names',()=>{
 const cp=cpKinds[0],value=cpProofPreview(cp),proof=value.counterpartyTaxIdentityChange!
 expect(checkTaxIdentity(proof.currentTaxIdentityNumber).ok).toBe(false)
 expect(decodeWorkDecisionPreview(value,company,cp,id)?.counterpartyTaxIdentityChange?.currentTaxIdentityNumber).toBe('10000000147')
 for(const delta of [{proposedKind:'VKN'},{proposedKind:1},{proposedKind:'vkn'},
  {proposedTaxIdentityNumber:'bad'},{proposedTaxIdentityNumber:'10000000147'},
  {proposedTaxIdentityNumber:' 10000000146'},{currentTaxIdentityNumber:null},{currentTaxIdentityNumber:'bad'},{currentTaxIdentityNumber:'1'.repeat(12)}])
  expect(decodeWorkDecisionPreview({...value,counterpartyTaxIdentityChange:{...proof,...delta}},company,cp,id)).toBeNull()
 const vkn={...proof,proposedTaxIdentityNumber:'9876543217',proposedKind:'vkn'}
 expect(decodeWorkDecisionPreview({...value,counterpartyTaxIdentityChange:vkn},company,cp,id)).not.toBeNull()
 const legal=cpProofPreview(cpKinds[1]),names=legal.counterpartyLegalNameChange!
 expect(decodeWorkDecisionPreview(legal,company,cpKinds[1],id)?.counterpartyLegalNameChange).toEqual(names)
 for(const delta of [{currentLegalName:'E'.repeat(251)},{proposedLegalName:'Y'.repeat(251)},{currentLegalName:''}])
  expect(decodeWorkDecisionPreview({...legal,counterpartyLegalNameChange:{...names,...delta}},company,cpKinds[1],id)).toBeNull()
})


const excessKind='logistics.plan-excess.v1'
const excessInstruction='dddddddd-0000-4000-8000-000000000001', excessLine='eeeeeeee-0000-4000-8000-000000000001'
const excessProposal={instructionVersion:'9007199254740993',instructionId:excessInstruction,lineId:excessLine,quantity:'20.125',reason:'SENTETIK tam teklif gerekçesi',proposedBy:'SENTETIK-maker',proposedAtUtc:'2026-11-27T08:00:00Z'}
const excessPreview={...preview,kind:excessKind,sourceVersion:'1',rejectionSupported:false,
 references:[{role:'loading-instruction',label:'SENTETIK talimat',recordId:excessInstruction,link:null}],logisticsPlanExcess:excessProposal}
const excessRequest={...request,expectedVersion:'1',logisticsPlanExcess:{expectedInstructionVersion:excessProposal.instructionVersion}}
it('decodes only the complete price-free plan excess proposal under the exact owner kind',()=>{
 expect(WORK_DECISION_KINDS.filter(kind=>kind===excessKind)).toEqual([excessKind])
 expect(decodeWorkDecisionPreview(excessPreview,company,excessKind,id)).toEqual(excessPreview)
 expect(decodeWorkDecisionPreview({...excessPreview,logisticsPlanExcess:{...excessProposal,reason:'R'.repeat(500),quantity:'999999999999999.999'}},company,excessKind,id)).not.toBeNull()
 for(const delta of [{companyId:id},{itemId:company},{rejectionSupported:true},{approvalReasonRequired:true},{logisticsPlanExcess:null},
  {sourceVersion:'01'},{references:[]},{references:[{...excessPreview.references[0],recordId:id}]},
  {references:[excessPreview.references[0],excessPreview.references[0]]},{logisticsPlanExcessProof:{reason:excessProposal.reason}},
  {inventoryTransferLoss:lossOptions},{counterpartyLegalNameChange:{}}])
  expect(decodeWorkDecisionPreview({...excessPreview,...delta},company,excessKind,id)).toBeNull()
 for(const delta of [{instructionVersion:'01'},{instructionVersion:'9223372036854775808'},{instructionId:id},{lineId:'00000000-0000-0000-0000-000000000000'},
  {quantity:'0'},{quantity:'20.0'},{quantity:'01'},{quantity:'1.0001'},{quantity:'1e2'},{quantity:'1000000000000000'},
  {reason:''},{reason:' x'},{reason:'R'.repeat(501)},{reason:'x\ny'},{proposedBy:''},{proposedBy:'M'.repeat(129)},
  {proposedAtUtc:'bad'},{proposedAtUtc:'2026-11-27T08:00:00+03:00'},{amount:'5'}])
  expect(decodeWorkDecisionPreview({...excessPreview,logisticsPlanExcess:{...excessProposal,...delta}},company,excessKind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...preview,logisticsPlanExcess:excessProposal},company,kind,id)).toBeNull()
})
it('requires the exact two frozen canonical versions, approve-only null reason and exclusive typed request',()=>{
 expect(isWorkDecisionRequest(excessRequest,excessKind)).toBe(true)
 for(const delta of [{approve:false},{reason:''},{reason:'insan gerekçesi'},{expectedVersion:'01'},{logisticsPlanExcess:undefined},
  {logisticsPlanExcess:null},{logisticsPlanExcess:{expectedInstructionVersion:'0'}},{logisticsPlanExcess:{expectedInstructionVersion:'9223372036854775808'}},
  {logisticsPlanExcess:{expectedInstructionVersion:'3',reason:excessProposal.reason}},{inventoryTransferLoss:lossRequest.inventoryTransferLoss},
  {manufacturingPlanReapproval:{selectedLabelIds:[id]}},{logisticsPlanExcessProof:{}}])
  expect(isWorkDecisionRequest({...excessRequest,...delta},excessKind)).toBe(false)
 expect(isWorkDecisionRequest(excessRequest,kind)).toBe(false)
 expect(isWorkDecisionRequest(excessRequest,'manufacturing.plan.v1')).toBe(false)
})
it('forwards frozen plan excess versions unchanged and accepts only the immutable Approved result',async()=>{
 const result={companyId:company,kind:excessKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(result),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,excessKind,id,excessRequest,'frozen-key')).type).toBe('applied')
 expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual(excessRequest)
 expect((fetch.mock.calls[0]![1]!.headers as Record<string,string>)['Idempotency-Key']).toBe('frozen-key')
 for(const delta of [{state:'Rejected'},{state:'Pending'},{companyId:id},{itemId:company},{kind},{extra:true}]){
  fetch.mockResolvedValueOnce(new Response(JSON.stringify({...result,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,excessKind,id,excessRequest,'frozen-key')).type).toBe('unknown')
 }
 const count=fetch.mock.calls.length
 expect((await sendWorkDecision(company,excessKind,id,{...excessRequest,approve:false},'frozen-key')).type).toBe('unknown')
 expect(fetch.mock.calls).toHaveLength(count)
})

it('rejects native C0 and C1 controls in the exact human proposal without changing printable text',()=>{
 for(const control of ['\u0000','\u001f','\u007f','\u0085','\u009f'])
  expect(decodeWorkDecisionPreview({...excessPreview,logisticsPlanExcess:{...excessProposal,reason:`before${control}after`}},company,excessKind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...excessPreview,logisticsPlanExcess:{...excessProposal,reason:'Türkçe teklif gerekçesi'}},company,excessKind,id)).not.toBeNull()
})


const overageKind='inventory.transfer-overage.v1'
const overageTransfer='dddddddd-0000-4000-8000-000000000021',overageLine='eeeeeeee-0000-4000-8000-000000000021'
const overageProposal={requestNumber:2,requestedAtUtc:'2026-11-27T08:00:00Z',transferId:overageTransfer,lineId:overageLine,kilograms:'20.125',pieceCount:0,
 reason:'SENTETIK fazla kabul talep gerekçesi',requestedBy:'SENTETIK-requester',receivedBy:'SENTETIK-receiver'}
const overagePreview={...preview,kind:overageKind,approvalReasonRequired:true,rejectionSupported:true,
 references:[{role:'inventory-transfer',label:'SENTETIK transfer',recordId:overageTransfer,link:null}],inventoryTransferOverage:overageProposal}
const overageRequest={...request,reason:'İnsan karar gerekçesi',inventoryTransferOverage:{requestNumber:2,decidedAtUtc:'2026-11-27T09:00:00Z'}}
it.each([null,0,2147483647])('keeps the exact nine-field human overage proposal and nullable pieces=%s',pieceCount=>{
 const value={...overagePreview,inventoryTransferOverage:{...overageProposal,pieceCount}}
 expect(WORK_DECISION_KINDS.filter(kind=>kind===overageKind)).toEqual([overageKind])
 expect(WORK_DECISION_KINDS).toHaveLength(19)
 expect(decodeWorkDecisionPreview(value,company,overageKind,id)).toEqual(value)
})
it('refuses malformed, foreign, unbound or hidden overage proof instead of rendering a partial proposal',()=>{
 for(const delta of [{companyId:id},{itemId:company},{sourceVersion:'01'},{approvalReasonRequired:false},{rejectionSupported:false},
  {inventoryTransferOverage:null},{inventoryTransferOverage:undefined},{inventoryTransferOverageProof:{}},{inventoryTransferLoss:lossOptions},
  {references:[]},{references:[{...overagePreview.references[0],recordId:id}]},{references:[...overagePreview.references,...overagePreview.references]}])
  expect(decodeWorkDecisionPreview({...overagePreview,...delta},company,overageKind,id)).toBeNull()
 for(const delta of [{requestNumber:0},{requestNumber:1.5},{requestNumber:2147483648},{requestedAtUtc:'bad'},{requestedAtUtc:'2026-11-27T08:00:00+03:00'},
  {transferId:id},{lineId:'00000000-0000-0000-0000-000000000000'},{kilograms:'0'},{kilograms:'1.0'},{kilograms:'1e2'},{kilograms:'1000000000000000'},
  {kilograms:'1.0001'},{pieceCount:undefined},{pieceCount:-1},{pieceCount:0.5},{pieceCount:2147483648},{pieceCount:'0'},
  {reason:''},{reason:' R'},{reason:'R'.repeat(501)},{reason:'x\u0085y'},{requestedBy:''},{requestedBy:'x'.repeat(129)},{receivedBy:''},{extra:true}])
  expect(decodeWorkDecisionPreview({...overagePreview,inventoryTransferOverage:{...overageProposal,...delta}},company,overageKind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...preview,inventoryTransferOverage:overageProposal},company,kind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...preview,kind:lossKind,approvalReasonRequired:true,rejectionSupported:true,inventoryTransferLoss:lossOptions,inventoryTransferOverage:overageProposal},company,lossKind,id)).toBeNull()
})
it.each([true,false])('preserves the explicit numbered overage command and required native reason for approve=%s',approve=>{
 const command={...overageRequest,approve,reason:'R'.repeat(500)}
 expect(isWorkDecisionRequest(command,overageKind)).toBe(true)
 for(const delta of [{reason:null},{reason:''},{reason:'R'.repeat(501)},{reason:'x\u0085y'},{expectedVersion:'0'},
  {inventoryTransferOverage:null},{inventoryTransferOverage:{requestNumber:0,decidedAtUtc:'2026-11-27T09:00:00Z'}},
  {inventoryTransferOverage:{requestNumber:2,decidedAtUtc:'bad'}},{inventoryTransferOverage:{...command.inventoryTransferOverage,extra:true}},
  {inventoryTransferLoss:lossRequest.inventoryTransferLoss},{logisticsPlanExcess:{expectedInstructionVersion:'3'}},{manufacturingPlanReapproval:{selectedLabelIds:[id]}}])
  expect(isWorkDecisionRequest({...command,...delta},overageKind)).toBe(false)
 expect(isWorkDecisionRequest(command,lossKind)).toBe(false)
 expect(isWorkDecisionRequest(command,kind)).toBe(false)
})
it.each([true,false])('accepts only the exact immutable overage decision result approve=%s',async approve=>{
 const result={companyId:company,kind:overageKind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(result),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 const command={...overageRequest,approve}
 expect((await sendWorkDecision(company,overageKind,id,command,'frozen-overage-key')).type).toBe('applied')
 expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual(command)
 for(const delta of [{state:approve?'Rejected':'Approved'},{state:'Pending'},{companyId:id},{kind:lossKind},{itemId:company},{extra:true}]){
  fetch.mockResolvedValueOnce(new Response(JSON.stringify({...result,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,overageKind,id,command,'frozen-overage-key')).type).toBe('unknown')
 }
})
