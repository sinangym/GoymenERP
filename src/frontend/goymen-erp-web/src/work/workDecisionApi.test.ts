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
 expect(WORK_DECISION_KINDS).toHaveLength(27)
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
 expect(WORK_DECISION_KINDS).toHaveLength(27)
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
 expect(WORK_DECISION_KINDS).toHaveLength(27)
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


const weighingKind='logistics.weighing-difference.v1'
const weighingNote='dddddddd-0000-4000-8000-000000000031',weighingInstruction='eeeeeeee-0000-4000-8000-000000000031',weighingProduct='ffffffff-0000-4000-8000-000000000031'
const weighingEvaluation={dispatchNoteId:weighingNote,instructionId:weighingInstruction,theoreticalKg:'100',scaleKg:'103',differenceKg:'3',bandLevel:'NotEvaluated' as const,
 warningBandPercent:null,approvalBandPercent:null,blockBandPercent:null,totalExcessKg:'1',approvalRatePercent:null,decision:'ApprovalRequired' as const,
 products:[{productId:weighingProduct,noteKg:'100',scaleKg:'103',differenceKg:'3',remainingPhysicalKg:'2',physicallyCoveredKg:'2',excessKg:'1',targetLineNo:1}],
 enteredBy:['SENTETIK-recorder','SENTETIK-scale-user'],enteredByComplete:true,recordedBy:'SENTETIK-recorder',recordedAtUtc:'2026-11-27T08:00:00Z'}
const weighingPreview={...preview,kind:weighingKind,sourceVersion:'1',rejectionSupported:false,
 references:[{role:'dispatch-note',label:'SENTETIK irsaliye',recordId:weighingNote,link:null},{role:'loading-instruction',label:'SENTETIK talimat',recordId:weighingInstruction,link:null}],logisticsWeighingDifference:weighingEvaluation}
it('keeps exactly seventeen immutable evaluation fields and eight native product fields without numeric coercion',()=>{
 expect(WORK_DECISION_KINDS.filter(kind=>kind===weighingKind)).toEqual([weighingKind]);expect(WORK_DECISION_KINDS).toHaveLength(27)
 expect(Object.keys(weighingEvaluation)).toHaveLength(17);expect(Object.keys(weighingEvaluation.products[0]!)).toHaveLength(8)
 expect(decodeWorkDecisionPreview(weighingPreview,company,weighingKind,id)).toEqual(weighingPreview)
 for(const bandLevel of ['NotEvaluated','None','Warning','Approval','Block'])
  expect(decodeWorkDecisionPreview({...weighingPreview,logisticsWeighingDifference:{...weighingEvaluation,bandLevel,warningBandPercent:'0.125',approvalBandPercent:'1.25',blockBandPercent:'5.5',approvalRatePercent:'0.5'}},company,weighingKind,id)).not.toBeNull()
 expect(decodeWorkDecisionPreview({...weighingPreview,pending:false,sourceVersion:'2'},company,weighingKind,id)).not.toBeNull()
 expect(decodeWorkDecisionPreview({...weighingPreview,sourceVersion:'2147483647'},company,weighingKind,id)).not.toBeNull()
})
it('refuses incomplete actor sets, source bindings, enum/decimal drift and other owner payloads',()=>{
 for(const delta of [{companyId:id},{itemId:company},{sourceVersion:'2147483648'},{sourceVersion:'01'},{approvalReasonRequired:true},{rejectionSupported:true},
  {logisticsWeighingDifference:null},{logisticsWeighingDifference:undefined},{logisticsWeighingDifferenceProof:{}},{logisticsPlanExcess:excessProposal},
  {references:[]},{references:[...weighingPreview.references,weighingPreview.references[0]]}])
  expect(decodeWorkDecisionPreview({...weighingPreview,...delta},company,weighingKind,id)).toBeNull()
 for(const delta of [{dispatchNoteId:id},{instructionId:id},{theoreticalKg:100},{scaleKg:'-1'},{differenceKg:'NaN'},{totalExcessKg:'1e2'},
  {bandLevel:'Unknown'},{decision:'Unknown'},{decision:'WithinRate'},{warningBandPercent:1},{approvalBandPercent:undefined},{approvalRatePercent:'bad'},
  {enteredByComplete:false},{enteredBy:[]},{enteredBy:['other']},{enteredBy:['SENTETIK-recorder','SENTETIK-recorder']},
  {enteredBy:['SENTETIK-recorder',' maker ']},{enteredBy:['SENTETIK-recorder',...Array.from({length:17},(_,i)=>`maker${i}`)]},
  {recordedBy:''},{recordedAtUtc:'2026-11-27T08:00:00+03:00'},{extra:true},{products:[]},{products:[...weighingEvaluation.products,...weighingEvaluation.products]}])
  expect(decodeWorkDecisionPreview({...weighingPreview,logisticsWeighingDifference:{...weighingEvaluation,...delta}},company,weighingKind,id)).toBeNull()
 for(const delta of [{productId:'00000000-0000-0000-0000-000000000000'},
  {noteKg:-1},{scaleKg:'1.0001'},{differenceKg:'wrong'},{remainingPhysicalKg:'-1'},{physicallyCoveredKg:'1e2'},
  {excessKg:undefined},{targetLineNo:0},{targetLineNo:2147483648},{extra:true}])
  expect(decodeWorkDecisionPreview({...weighingPreview,logisticsWeighingDifference:{...weighingEvaluation,products:[{...weighingEvaluation.products[0],...delta}]}},company,weighingKind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...preview,logisticsWeighingDifference:weighingEvaluation},company,kind,id)).toBeNull()
})
it('requires only the four native fields for weighing approval, null reason and canonical positive Int32 version',()=>{
 const command={...request,expectedVersion:'1'}
 expect(isWorkDecisionRequest(command,weighingKind)).toBe(true)
 expect(isWorkDecisionRequest({...command,expectedVersion:'2147483647'},weighingKind)).toBe(true)
 for(const delta of [{approve:false},{reason:''},{reason:'onay'},{expectedVersion:'0'},{expectedVersion:'01'},{expectedVersion:'2147483648'},
  {expectedVersion:'9007199254740993'},{logisticsWeighingDifference:weighingEvaluation},{inventoryTransferOverage:overageRequest.inventoryTransferOverage},
  {inventoryTransferLoss:lossRequest.inventoryTransferLoss},{logisticsPlanExcess:{expectedInstructionVersion:'3'}},{manufacturingPlanReapproval:null}])
  expect(isWorkDecisionRequest({...command,...delta},weighingKind)).toBe(false)
})
it('preserves the original weighing expected version/key and accepts only exact immutable Approved receipt',async()=>{
 const command={...request,expectedVersion:'1'}
 const result={companyId:company,kind:weighingKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(result),{status:200,headers}))
 vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,weighingKind,id,command,'weigh-key')).type).toBe('applied')
 expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual(command)
 for(const delta of [{state:'Rejected'},{state:'PendingApproval'},{state:'Recorded'},{companyId:id},{itemId:company},{kind:excessKind},{extra:true}]){
  fetch.mockResolvedValueOnce(new Response(JSON.stringify({...result,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,weighingKind,id,command,'weigh-key')).type).toBe('unknown')
 }
 const count=fetch.mock.calls.length
 expect((await sendWorkDecision(company,weighingKind,id,{...command,approve:false},'weigh-key')).type).toBe('unknown')
 expect(fetch.mock.calls).toHaveLength(count)
})


const supplierKind='purchasing.supplier-return-cost-exception.v1'
const supplierOrder='dddddddd-0000-4000-8000-000000000041',supplierLine='eeeeeeee-0000-4000-8000-000000000041'
const supplierProof={purchaseOrderId:supplierOrder,purchaseOrderLineId:supplierLine,goodsAcceptanceId:company,inventoryMovementId:id,
 sourceBlock:'Free',quantityKg:'12.125',replacementExpected:true,reason:'SENTETIK iade gerekçesi',occurredAtUtc:'2026-11-27T08:00:00Z',requestedBy:'SENTETIK-requester',requestedAtUtc:'2026-11-27T09:00:00Z',
 recordedExpectedPurchaseOrderVersion:'4',currentPurchaseOrderVersion:'9',undoQuantity:'2',priceDisclosure:'Withheld'}
const supplierRules=[{ruleCode:'purchasing.supplier-return-manual-cost',threshold:null,actual:null,deviation:null,unit:null},
 {ruleCode:'purchasing.supplier-return-quantity',threshold:null,actual:'12.125',deviation:null,unit:'kg'}]
const supplierPreview={...preview,kind:supplierKind,sourceVersion:'1',rejectionSupported:true,references:[{role:'purchase-order',recordId:supplierOrder,label:'SENTETIK sipariş',link:null}],rules:supplierRules,supplierReturnCostException:supplierProof}
describe('supplier return cost exception human-only contract',()=>{
 it('binds the complete measured proposal and omits withheld cost; accepts native positive decimal bounds without calculations',()=>{
  expect(decodeWorkDecisionPreview(supplierPreview,company,supplierKind,id)).not.toBeNull()
  const visible={...supplierPreview,rules:[{...supplierRules[0]!,actual:'99999999999999.9999',unit:'TRY'},supplierRules[1]!],supplierReturnCostException:{...supplierProof,priceDisclosure:'Visible',unitCostTryPerKg:'99999999999999.9999'}}
  expect(decodeWorkDecisionPreview(visible,company,supplierKind,id)).not.toBeNull()
  expect(decodeWorkDecisionPreview({...supplierPreview,supplierReturnCostException:{...supplierProof,replacementExpected:false,recordedExpectedPurchaseOrderVersion:null,currentPurchaseOrderVersion:null,undoQuantity:null}},company,supplierKind,id)).not.toBeNull()
 })
 it.each([{unitCostTryPerKg:null},{unitCostTryPerKg:'1'},{priceDisclosure:'Visible'},{priceDisclosure:'Other'},{purchaseOrderId:id},{sourceBlock:'Other'},{quantityKg:'0'},{quantityKg:'1.000'},{quantityKg:'1000000000000000'},
  {replacementExpected:null},{reason:''},{reason:'x'.repeat(501)},{reason:'x\u0085y'},{requestedAtUtc:'2026-11-27T09:00:00+03:00'},{goodsAcceptanceId:''},{currentPurchaseOrderVersion:null},
  {recordedExpectedPurchaseOrderVersion:null},{recordedExpectedPurchaseOrderVersion:null,currentPurchaseOrderVersion:null,undoQuantity:null},{currentPurchaseOrderVersion:'9223372036854775807'},{undoQuantity:'0'},{extra:true}])('refuses malformed supplier proposal %j',delta=>{
  expect(decodeWorkDecisionPreview({...supplierPreview,supplierReturnCostException:{...supplierProof,...delta}},company,supplierKind,id)).toBeNull()
 })
 it.each([{companyId:id},{sourceVersion:'9223372036854775807'},{supplierReturnCostException:null},{supplierReturnCostExceptionProof:{}},{logisticsPlanExcess:{}},{references:[]},{rules:[supplierRules[1]]},{rules:[{...supplierRules[0]!,actual:'5',unit:'TRY'},supplierRules[1]!]}])('refuses foreign, extra, unbound and leaking supplier metadata %j',delta=>{
  expect(decodeWorkDecisionPreview({...supplierPreview,...delta},company,supplierKind,id)).toBeNull()
 })
 it('accepts only frozen version selection on approval; native optional controls and rejection bounds remain exact',()=>{
  const approve={...request,expectedVersion:'1',supplierReturnCostException:{expectedPurchaseOrderVersion:'9'}}
  expect(isWorkDecisionRequest(approve,supplierKind)).toBe(true)
  expect(isWorkDecisionRequest({...request,expectedVersion:'1'},supplierKind)).toBe(true)
  expect(isWorkDecisionRequest({...approve,reason:'note'},supplierKind)).toBe(false)
  for(const selected of [null,{}, {expectedPurchaseOrderVersion:'0'},{expectedPurchaseOrderVersion:'09'},{expectedPurchaseOrderVersion:'9223372036854775807'},{expectedPurchaseOrderVersion:'9',purchaseOrderId:supplierOrder}])
   expect(isWorkDecisionRequest({...approve,supplierReturnCostException:selected},supplierKind)).toBe(false)
  expect(isWorkDecisionRequest(approve,'purchasing.request.v1')).toBe(false)
  expect(isWorkDecisionRequest({...approve,approve:false,reason:'Ret'},supplierKind)).toBe(false)
  for(const reason of [null,'',' '.repeat(5),'x'.repeat(501),'x\u0085y','x\u0000y'])expect(isWorkDecisionRequest({...request,expectedVersion:'1',approve:false,reason},supplierKind)).toBe(false)
  for(const reason of ['Ret','x'.repeat(500),'Satır1\nSatır2\r\tAçıklama'])expect(isWorkDecisionRequest({...request,expectedVersion:'1',approve:false,reason},supplierKind)).toBe(true)
 })
 it.each([true,false])('accepts only immutable native supplier terminal state and matching original correlation, approve=%s',async approve=>{
  const state=approve?'Recorded':'Rejected',body={companyId:company,kind:supplierKind,itemId:id,state,replayed:true,originalCorrelationId:'original'}
  const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}}));vi.stubGlobal('fetch',fetch)
  const command={...request,expectedVersion:'1',approve,reason:approve?null:'Ret'}
  expect((await sendWorkDecision(company,supplierKind,id,command,'old-key')).type).toBe('applied')
  for(const delta of [{companyId:id},{itemId:company},{state:'Approved'},{state:approve?'Rejected':'Recorded'},{originalCorrelationId:'other'},{extra:true}]){
   fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers:{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}}))
   expect((await sendWorkDecision(company,supplierKind,id,command,'old-key')).type).toBe('unknown')
  }
 })
})


const countKind='inventory.stock-count.v1'
const countLabel='dddddddd-0000-4000-8000-000000000051',countProduct='eeeeeeee-0000-4000-8000-000000000051',countLocation='ffffffff-0000-4000-8000-000000000051'
const countHuman={labelId:countLabel,labelVersion:'7',ownerKind:'Company',ownerId:company,custodianKind:'Company',custodianId:company,locationId:countLocation,productId:countProduct,
 block:'Free',bookKg:'10',countedKg:'0',differenceKg:'-10',countedAtUtc:'2026-11-27T08:00:00Z',reason:'SENTETIK sayım gerekçesi',recordedBy:'SENTETIK-sayan',recordedAtUtc:'2026-11-27T09:00:00Z',bookPieceCount:2,countedPieceCount:0}
const countRefs=[{role:'inventory-label',recordId:countLabel,label:'SENTETIK etiket',link:null},{role:'product',recordId:countProduct,label:'SENTETIK ürün',link:null},{role:'location',recordId:countLocation,label:'SENTETIK konum',link:null}]
const countRules=[{ruleCode:'inventory.stock-count-difference',threshold:null,actual:'0',deviation:'-10',unit:'kg'}]
const countPreview={...preview,kind:countKind,sourceVersion:'1',approvalReasonRequired:true,rejectionSupported:true,references:countRefs,rules:countRules,inventoryStockCount:countHuman}
describe('native stock count Work human evidence',()=>{
 it('reads exactly eighteen immutable facts including counted zero and nullable pieces; terminal version is not the label version',()=>{
  expect(decodeWorkDecisionPreview(countPreview,company,countKind,id)).not.toBeNull()
  expect(decodeWorkDecisionPreview({...countPreview,pending:false,sourceVersion:'2'},company,countKind,id)).not.toBeNull()
  expect(decodeWorkDecisionPreview({...countPreview,inventoryStockCount:{...countHuman,bookPieceCount:null,countedPieceCount:null}},company,countKind,id)).not.toBeNull()
  expect(decodeWorkDecisionPreview({...countPreview,rules:[{...countRules[0]!,actual:'2',deviation:'2'}],inventoryStockCount:{...countHuman,bookKg:'0',countedKg:'2',differenceKg:'2',bookPieceCount:0,countedPieceCount:2}},company,countKind,id)).not.toBeNull()
 })
 it.each([{labelId:''},{labelVersion:'0'},{labelVersion:'07'},{ownerKind:'Unknown'},{custodianKind:'Unknown'},{ownerId:id+'x'},{locationId:id},{productId:id},{block:'Other'},
  {bookKg:'-1'},{countedKg:'1.000'},{differenceKg:'-0'},{differenceKg:'0'},{differenceKg:'-1.000'},{reason:''},{reason:'x'.repeat(1001)},
  {recordedBy:''},{recordedBy:'x'.repeat(129)},{countedAtUtc:'2026-11-27T09:00:00+03:00'},{recordedAtUtc:null},
  {bookPieceCount:null},{countedPieceCount:null},{countedPieceCount:-1},{bookPieceCount:2147483648},{countedPieceCount:'0'},{bookPieceCount:undefined},
  {cost:'1'},{costStatus:'Known'},{unitCostTryPerKg:'10'},{extra:true}])('refuses malformed or leaking count evidence %j',delta=>{
  expect(decodeWorkDecisionPreview({...countPreview,inventoryStockCount:{...countHuman,...delta}},company,countKind,id)).toBeNull()
 })
 it.each([{companyId:id},{itemId:company},{sourceVersion:'2'},{sourceVersion:'9223372036854775807'},{pending:false},{rejectionSupported:false},{approvalReasonRequired:false},
  {inventoryStockCount:null},{inventoryStockCountProof:{}},{rules:[]},{rules:[{...countRules[0]!,actual:'10'}]},{rules:[{...countRules[0]!,unit:'TRY'}]},{references:countRefs.slice(1)},{references:[{...countRefs[0]!,link:'/stok/etiket/x'},...countRefs.slice(1)]}])('refuses mismatched count scope, logical version and privacy shape %j',delta=>{
  expect(decodeWorkDecisionPreview({...countPreview,...delta},company,countKind,id)).toBeNull()
 })
 it('uses only the old four-field command and native mandatory reason limit on both decisions',()=>{
  for(const approve of [true,false]){
   expect(isWorkDecisionRequest({expectedVersion:'1',approve,reason:'x'.repeat(1000),delegationId:null},countKind)).toBe(true)
   expect(isWorkDecisionRequest({expectedVersion:'1',approve,reason:'Satır1\nSatır2',delegationId:null},countKind)).toBe(true)
   for(const delta of [{expectedVersion:'2'},{expectedVersion:'01'},{reason:null},{reason:' '},{reason:'x'.repeat(1001)},
    {inventoryStockCount:{}},{supplierReturnCostException:{expectedPurchaseOrderVersion:'9'}},{logisticsPlanExcess:{expectedInstructionVersion:'1'}},{manufacturingPlanReapproval:{selectedLabelIds:[id]}}])
    expect(isWorkDecisionRequest({expectedVersion:'1',approve,reason:'Karar',delegationId:null,...delta},countKind)).toBe(false)
  }
  expect(workDecisionReasonLimit(countKind)).toBe(1000)
  expect(decodeWorkDecisionPreview({...preview,inventoryStockCount:countHuman},company,kind,id)).toBeNull()
 })
 it.each([true,false])('accepts matching immutable Approved/Rejected receipt and original replay correlation only, approval=%s',async approve=>{
  const body={companyId:company,kind:countKind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'}
  const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
  const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}));vi.stubGlobal('fetch',fetch)
  const command={expectedVersion:'1',approve,reason:'Karar',delegationId:null}
  expect((await sendWorkDecision(company,countKind,id,command,'old-key')).type).toBe('applied')
  for(const delta of [{companyId:id},{itemId:company},{kind},{state:'PendingApproval'},{state:approve?'Rejected':'Approved'},{state:'NoDifference'},{originalCorrelationId:'different'},{extra:true}]){
   fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers}))
   expect((await sendWorkDecision(company,countKind,id,command,'old-key')).type).toBe('unknown')
  }
 })
})


const bankKind='finance.bank-account.v1',bankCurrency='dddddddd-0000-4000-8000-000000000061'
const bankSafeAccount={id,companyId:company,currencyId:bankCurrency,displayName:'SENTETIK banka hesabı',isActive:false,isUsableForReceipt:false,version:'1'}
const bankHuman={account:bankSafeAccount,proposedBy:'SENTETIK-öneren',proposedAtUtc:'2026-11-27T08:00:00Z'}
const bankRefs=[{role:'bank-account',recordId:id,label:'SENTETIK hesap',link:null},{role:'currency',recordId:bankCurrency,label:'SENTETIK para birimi',link:null}]
const bankRules=[{ruleCode:'finance.bank-account-approval-required',threshold:null,actual:null,deviation:null,unit:null}]
const bankPreview={...preview,kind:bankKind,sourceVersion:'1',rejectionSupported:false,references:bankRefs,rules:bankRules,bankAccount:bankHuman}
describe('safe bank approval Work purpose',()=>{
 it('reuses the native safe seven-field account decoder and reads later active revision without rewriting the command version',()=>{
  expect(decodeWorkDecisionPreview(bankPreview,company,bankKind,id)).not.toBeNull()
  expect(decodeWorkDecisionPreview({...bankPreview,pending:false,sourceVersion:'4',bankAccount:{...bankHuman,account:{...bankSafeAccount,isActive:true,isUsableForReceipt:false,version:'4'}}},company,bankKind,id)).not.toBeNull()
 })
 it.each([{id:company},{companyId:id},{currencyId:''},{displayName:''},{displayName:'x'.repeat(201)},{isActive:true},{isUsableForReceipt:true},{version:'2'},{version:'01'},{version:'9223372036854775807'},
  {bankName:'SENTETIK banka'},{iban:'TR-SENTETIK'},{accountNumber:'SENTETIK'},{card:{}},{extra:true}])('refuses native safe-account scope drift or a card/IBAN disclosure %j',delta=>{
  expect(decodeWorkDecisionPreview({...bankPreview,bankAccount:{...bankHuman,account:{...bankSafeAccount,...delta}}},company,bankKind,id)).toBeNull()
 })
 it.each([{companyId:id},{kind:'finance.bank-account-iban-change.v1'},{sourceVersion:'0'},{sourceVersion:'9223372036854775807'},{rejectionSupported:true},{approvalReasonRequired:true},{bankAccount:null},{bankAccountProof:{}},
  {bankAccount:{...bankHuman,proposedBy:''}},{bankAccount:{...bankHuman,proposedAtUtc:'2026-11-27T08:00:00+03:00'}},{bankAccount:{...bankHuman,iban:'TR-SENTETIK'}},{references:[]},{rules:[{...bankRules[0]!,actual:'100',unit:'TRY'}]}])('refuses malformed, foreign or price-bearing bank preview %j',delta=>{
  expect(decodeWorkDecisionPreview({...bankPreview,...delta},company,bankKind,id)).toBeNull()
 })
 it('allows only approve with a null reason, canonical native version and no selection/proof request members',()=>{
  const body={expectedVersion:'1',approve:true,reason:null,delegationId:null};expect(isWorkDecisionRequest(body,bankKind)).toBe(true)
  for(const delta of [{approve:false},{reason:''},{reason:'not'},{expectedVersion:'01'},{expectedVersion:'0'},{expectedVersion:'9223372036854775807'},
   {bankAccount:bankHuman},{supplierReturnCostException:{expectedPurchaseOrderVersion:'1'}},{inventoryTransferLoss:{requestNumber:1,decidedAtUtc:'2026-11-27T08:00:00Z'}}])expect(isWorkDecisionRequest({...body,...delta},bankKind)).toBe(false)
  expect(decodeWorkDecisionPreview({...preview,bankAccount:bankHuman},company,kind,id)).toBeNull()
 })
 it('accepts only the immutable original Approved outcome and matching company/item/correlation',async()=>{
  const body={companyId:company,kind:bankKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'}
  const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
  const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}));vi.stubGlobal('fetch',fetch)
  const command={expectedVersion:'1',approve:true,reason:null,delegationId:null}
  expect((await sendWorkDecision(company,bankKind,id,command,'same-key')).type).toBe('applied')
  for(const delta of [{companyId:id},{itemId:company},{state:'Proposed'},{state:'Rejected'},{kind:'finance.bank-account-iban-change.v1'},{originalCorrelationId:'other'},{account:bankSafeAccount}]){
   fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers}))
   expect((await sendWorkDecision(company,bankKind,id,command,'same-key')).type).toBe('unknown')
  }
 })
})


const scrapKind='inventory.stock-scrap.v1'
const scrapHuman={labelId:countLabel,labelVersion:'7',ownerKind:'Company',ownerId:company,custodianKind:'Company',custodianId:company,locationId:countLocation,productId:countProduct,
 block:'Free',bookKg:'20.125',scrapKg:'12.125',remainingKg:'8',scrappedAtUtc:'2026-11-27T08:00:00Z',reason:'SENTETIK hurda gerekçesi',recordedBy:'SENTETIK-hurda-kaydeden',recordedAtUtc:'2026-11-27T09:00:00Z',bookPieceCount:2,scrapPieceCount:0}
const scrapRules=[{ruleCode:'inventory.stock-scrap-request',threshold:null,actual:'12.125',deviation:null,unit:'kg'}]
const scrapPreview={...preview,kind:scrapKind,sourceVersion:'1',approvalReasonRequired:true,rejectionSupported:true,references:countRefs,rules:scrapRules,inventoryStockScrap:scrapHuman}
describe('native stock scrap Work evidence',()=>{
 it('reuses the immutable stock evidence shape while preserving zero scrapped pieces versus null and zero remaining kg',()=>{
  expect(decodeWorkDecisionPreview(scrapPreview,company,scrapKind,id)).not.toBeNull()
  expect(decodeWorkDecisionPreview({...scrapPreview,sourceVersion:'2',pending:false},company,scrapKind,id)).not.toBeNull()
  expect(decodeWorkDecisionPreview({...scrapPreview,inventoryStockScrap:{...scrapHuman,bookPieceCount:null,scrapPieceCount:null}},company,scrapKind,id)).not.toBeNull()
  expect(decodeWorkDecisionPreview({...scrapPreview,rules:[{...scrapRules[0]!,actual:'20.125'}],inventoryStockScrap:{...scrapHuman,scrapKg:'20.125',remainingKg:'0',scrapPieceCount:2}},company,scrapKind,id)).not.toBeNull()
 })
 it.each([{bookKg:'0'},{scrapKg:'0'},{scrapKg:'-1'},{scrapKg:'01'},{remainingKg:'-1'},{remainingKg:'1.000'},{labelVersion:'0'},
  {labelId:id},{productId:id},{locationId:id},{block:'Unknown'},{ownerKind:'Unknown'},{custodianKind:'Unknown'},
  {bookPieceCount:null},{scrapPieceCount:null},{scrapPieceCount:3},{scrapPieceCount:-1},{scrapPieceCount:'0'},{scrapPieceCount:2147483648},
  {reason:''},{reason:'x'.repeat(1001)},{recordedBy:''},{recordedBy:'x'.repeat(129)},{scrappedAtUtc:'2026-11-27T08:00:00+03:00'},
  {countedKg:'1'},{differenceKg:'2'},{unitCost:'100'},{costStatus:'Known'}])('refuses malformed, misbound or price-bearing scrap facts %j',delta=>{
  expect(decodeWorkDecisionPreview({...scrapPreview,inventoryStockScrap:{...scrapHuman,...delta}},company,scrapKind,id)).toBeNull()
 })
 it.each([{companyId:id},{sourceVersion:'2'},{sourceVersion:'7'},{pending:false},{inventoryStockScrap:null},{inventoryStockScrapProof:{}},{inventoryStockCount:countHuman},
  {approvalReasonRequired:false},{rejectionSupported:false},{rules:[]},{rules:[{...scrapRules[0]!,deviation:'1'}]},{rules:[{...scrapRules[0]!,actual:'1'}]},{references:[]}])('refuses wrong owner/logical state and extra metadata %j',delta=>{
  expect(decodeWorkDecisionPreview({...scrapPreview,...delta},company,scrapKind,id)).toBeNull()
 })
 it('keeps both native decisions in the four-field command with a mandatory 1000-character reason',()=>{
  for(const approve of [true,false]){
   const body={expectedVersion:'1',approve,reason:'x'.repeat(1000),delegationId:null};expect(isWorkDecisionRequest(body,scrapKind)).toBe(true)
   for(const delta of [{expectedVersion:'2'},{reason:null},{reason:' '},{reason:'x'.repeat(1001)},{inventoryStockScrap:scrapHuman},{inventoryStockCount:countHuman},{inventoryTransferLoss:{requestNumber:1,decidedAtUtc:'2026-11-27T08:00:00Z'}}])expect(isWorkDecisionRequest({...body,...delta},scrapKind)).toBe(false)
  }
  expect(workDecisionReasonLimit(scrapKind)).toBe(1000)
  expect(decodeWorkDecisionPreview({...countPreview,inventoryStockScrap:scrapHuman},company,countKind,id)).toBeNull()
 })
 it.each([true,false])('accepts only matching immutable native scrap outcome and replay correlation, approve=%s',async approve=>{
  const body={companyId:company,kind:scrapKind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'}
  const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
  const fetch=vi.fn(async()=>new Response(JSON.stringify(body),{status:200,headers}));vi.stubGlobal('fetch',fetch)
  const command={expectedVersion:'1',approve,reason:'Karar',delegationId:null}
  expect((await sendWorkDecision(company,scrapKind,id,command,'same-key')).type).toBe('applied')
  for(const delta of [{companyId:id},{itemId:company},{kind:countKind},{state:'PendingApproval'},{state:approve?'Rejected':'Approved'},{originalCorrelationId:'wrong'},{extra:true}]){
   fetch.mockImplementation(async()=>new Response(JSON.stringify({...body,...delta}),{status:200,headers}))
   expect((await sendWorkDecision(company,scrapKind,id,command,'same-key')).type).toBe('unknown')
  }
 })
})

const dispatchKind='logistics.dispatch-note.v1' as const
const dispatchLine={lineNo:1,shipmentId:'bbbbbbbb-0000-4000-8000-000000000001',instructionLineId:id,sourceOrderId:id,sourceOrderLineId:id,productId:id,unitId:id,quantity:'10.000'}
const dispatchProof={instructionId:company,dispatchNoteId:id,customerPartyId:company,companyAccountId:id,effectiveOn:'2026-11-27',status:'Draft',number:null,eDispatchNumber:null,createdBy:'SENTETIK hazırlayan',createdAtUtc:'2026-11-27T08:00:00Z',lines:[dispatchLine]}
const dispatchPreview={...preview,kind:dispatchKind,rejectionSupported:false,logisticsDispatchNote:dispatchProof,references:[{role:'loading-instruction',label:'Talimat',recordId:company,link:'/lojistik/talimatlar/'+company}]}
it.each([1,500,501])('preserves all %i native dispatch lines, raw quantity and optional kg without a Work reference cap',count=>{
 const lines=Array.from({length:count},(_,i)=>({...dispatchLine,lineNo:i+1,shipmentId:`bbbbbbbb-0000-4000-8000-${(i+1).toString().padStart(12,'0')}`,...(i%2?{quantityKg:'0.125'}:{})}))
 const decoded=decodeWorkDecisionPreview({...dispatchPreview,logisticsDispatchNote:{...dispatchProof,lines}},company,dispatchKind,id)
 expect(decoded?.logisticsDispatchNote?.lines).toEqual(lines)
 expect(WORK_DECISION_KINDS).toContain(dispatchKind)
 expect(isWorkDecisionKind(dispatchKind)).toBe(true)
})
it.each([
 {logisticsDispatchNote:undefined},{rejectionSupported:true},{rejectionSupported:undefined},{approvalReasonRequired:true},{companyId:id},{itemId:company},
 ...[{dispatchNoteId:company},{instructionId:id},{createdBy:''},{effectiveOn:'2026-02-30'},{effectiveOn:'2026-99-99'},{status:'Future'},
 {number:'IRS-1'},{eDispatchNumber:'GYM2026000000001'},{lines:[]},{lines:[{...dispatchLine,unitId:''}]},
 {lines:[dispatchLine,{...dispatchLine,lineNo:2}]},{lines:[{...dispatchLine,quantity:'0'}]},{lines:[{...dispatchLine,quantity:'1.0001'}]},
 {lines:[{...dispatchLine,quantityKg:null}]},{lines:[{...dispatchLine,quantityKg:'0'}]},{lines:[{...dispatchLine,price:'5'}]}].map(delta=>({logisticsDispatchNote:{...dispatchProof,...delta}}))
])('rejects malformed or foreign dispatch preview %j',delta=>{
 expect(decodeWorkDecisionPreview({...dispatchPreview,...delta},company,dispatchKind,id)).toBeNull()
})
it('keeps historical dispatch metadata readable and accepts only strict original Approved receipt identity',async()=>{
 expect(decodeWorkDecisionPreview({...dispatchPreview,pending:false,sourceVersion:'9223372036854775807',logisticsDispatchNote:{...dispatchProof,status:'Cancelled',number:'IRS-1',eDispatchNumber:'GYM2026000000001'}},company,dispatchKind,id)).not.toBeNull()
 const response={companyId:company,kind:dispatchKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(response),{status:200,headers}));vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,dispatchKind,id,request,'same-key')).type).toBe('applied')
 expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual(request)
 for(const delta of [{state:'Cancelled'},{state:'Rejected'},{companyId:id},{itemId:company},{kind},{replayed:false},{originalCorrelationId:'other'},{extra:true}]) {
  fetch.mockResolvedValue(new Response(JSON.stringify({...response,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,dispatchKind,id,request,'same-key')).type).toBe('unknown')
 }
 for(const delta of [{approve:false},{reason:'İnsan gerekçesi'},{expectedVersion:'9223372036854775807'},{expectedVersion:'01'},{manufacturingPlanReapproval:{selectedLabelIds:[id]}}]) {
  fetch.mockClear();expect((await sendWorkDecision(company,dispatchKind,id,{...request,...delta},'same-key')).type).toBe('unknown');expect(fetch).not.toHaveBeenCalled()
 }
 expect(decodeWorkDecisionPreview({...preview,logisticsDispatchNote:dispatchProof},company,kind,id)).toBeNull()
})

it.each(['\n','\r','\r\n'])('rejects dispatch trailing line endings across native strings %j',ending=>{
 for(const field of ['quantity','quantityKg']) {
  const line={...dispatchLine,[field]:'1.250'+ending}
  expect(decodeWorkDecisionPreview({...dispatchPreview,logisticsDispatchNote:{...dispatchProof,lines:[line]}},company,dispatchKind,id)).toBeNull()
 }
 for(const delta of [{status:'Approved',number:'IRS-1',eDispatchNumber:'GYM2026000000001'+ending},{instructionId:company+ending},
  {customerPartyId:company+ending},{companyAccountId:id+ending},{createdAtUtc:dispatchProof.createdAtUtc+ending},{effectiveOn:dispatchProof.effectiveOn+ending},
  {lines:[{...dispatchLine,shipmentId:dispatchLine.shipmentId+ending}]},{lines:[{...dispatchLine,unitId:id+ending}]}])
  expect(decodeWorkDecisionPreview({...dispatchPreview,logisticsDispatchNote:{...dispatchProof,...delta}},company,dispatchKind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...dispatchPreview,sourceVersion:'7'+ending},company,dispatchKind,id)).toBeNull()
 expect(isWorkDecisionRequest({...request,expectedVersion:'7'+ending},dispatchKind)).toBe(false)
 expect(isWorkDecisionRequest({...request,delegationId:id+ending},dispatchKind)).toBe(false)
})

const customerKind='logistics.customer-return.v1' as const
const customerFacts={loadingInstructionId:company,loadingInstructionLineId:id,sourceDeliveryId:id,sourceShipmentId:id,sourceOrderId:company,sourceOrderLineId:id,unitId:id,
 quantityUnit:'Kilogram',quantity:'1.250',sourceDeliveredQuantity:'100.000',reason:'SENTETIK değişmez iade gerekçesi',requestedBy:'SENTETIK hazırlayan',occurredAtUtc:'0001-01-01T00:00:00+00:00',requestedAtUtc:'2026-11-27T08:00:00Z',status:'Pending'}
const customerPreview={...preview,kind:customerKind,sourceVersion:'1',rejectionSupported:true,approvalSupported:true,logisticsCustomerReturn:customerFacts,
 references:[{role:'loading-instruction',label:'Talimat',recordId:company,link:'/lojistik/yukleme-talimatlari/'+company}],
 rules:[{ruleCode:'logistics.customer-return-request',threshold:'100',actual:'1.25',deviation:null,unit:'kg'}]}
const customerRequest={expectedVersion:'1',approve:true,reason:null,delegationId:null,logisticsCustomerReturn:{inventoryMovementId:id}}
it('publishes customer-return among the exact 27 purposes and preserves all native human fields and the minimum occurrence instant',()=>{
 const decoded=decodeWorkDecisionPreview(customerPreview,company,customerKind,id)
 expect(decoded?.logisticsCustomerReturn).toEqual(customerFacts);expect(Object.keys(decoded!.logisticsCustomerReturn!)).toHaveLength(15)
 expect(isWorkDecisionKind(customerKind)).toBe(true);expect(WORK_DECISION_KINDS.filter(kind=>kind===customerKind)).toEqual([customerKind]);expect(WORK_DECISION_KINDS).toHaveLength(27)
 expect(workDecisionReasonLimit(customerKind)).toBeUndefined()
 for(const status of ['Approved','Rejected'])expect(decodeWorkDecisionPreview({...customerPreview,pending:false,sourceVersion:'2',logisticsCustomerReturn:{...customerFacts,status}},company,customerKind,id)).not.toBeNull()
 expect(decodeWorkDecisionPreview({...preview,logisticsCustomerReturn:customerFacts},company,kind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...preview,approvalSupported:true},company,kind,id)).toBeNull()
})
it('validates Piece without converting its units and advertises approval unsupported while preserving rejection',()=>{
 const value={...customerPreview,approvalSupported:false,logisticsCustomerReturn:{...customerFacts,quantityUnit:'Piece',quantity:'1.000',sourceDeliveredQuantity:'2.000'},rules:[{...customerPreview.rules[0],actual:'1',threshold:'2',unit:'adet'}]}
 expect(decodeWorkDecisionPreview(value,company,customerKind,id)?.approvalSupported).toBe(false)
 expect(decodeWorkDecisionPreview({...value,approvalSupported:true},company,customerKind,id)).toBeNull()
 expect(decodeWorkDecisionPreview({...value,logisticsCustomerReturn:{...value.logisticsCustomerReturn,quantity:'1.250'}},company,customerKind,id)).toBeNull()
})
it.each([
 {approvalSupported:undefined},{approvalSupported:false},{rejectionSupported:false},{approvalReasonRequired:true},{sourceVersion:'2'},{companyId:id},{itemId:company},
 ...[{loadingInstructionId:id},{unitId:''},{quantityUnit:'Litre'},{quantity:'0'},{quantity:'100.001'},{quantity:'1.250\n'},{sourceDeliveredQuantity:'100.000\r'},
 {reason:''},{requestedBy:'x'.repeat(129)},{occurredAtUtc:'2026-02-30T08:00:00Z'},{occurredAtUtc:'0001-01-01T00:00:00Z\n'},{requestedAtUtc:'0001-01-01T00:00:00Z'},
 {status:'Future'},{price:'100'}].map(delta=>({logisticsCustomerReturn:{...customerFacts,...delta}}))
])('refuses malformed or foreign customer-return proof %j',delta=>{
 expect(decodeWorkDecisionPreview({...customerPreview,...delta},company,customerKind,id)).toBeNull()
})
it.each([true,false])('sends only native customer-return selection and optional unbounded notes, approve=%s',async approve=>{
 const reason='x'.repeat(5001),body={expectedVersion:'1',approve,reason,delegationId:null,...(approve?{logisticsCustomerReturn:{inventoryMovementId:id}}:{})}
 expect(isWorkDecisionRequest(body,customerKind)).toBe(true);expect(isWorkDecisionRequest({...body,reason:null},customerKind)).toBe(true)
 const result={companyId:company,kind:customerKind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}
 const fetch=vi.fn(async(_input:RequestInfo|URL,_init?:RequestInit)=>new Response(JSON.stringify(result),{status:200,headers}));vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,customerKind,id,body,'same-key')).type).toBe('applied');expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual(body)
 for(const delta of [{state:'Pending'},{state:approve?'Rejected':'Approved'},{companyId:id},{kind},{itemId:company},{replayed:false},{originalCorrelationId:'wrong'},{extra:true}]){
  fetch.mockResolvedValue(new Response(JSON.stringify({...result,...delta}),{status:200,headers}));expect((await sendWorkDecision(company,customerKind,id,body,'same-key')).type).toBe('unknown')
 }
})
it.each([{logisticsCustomerReturn:undefined},{logisticsCustomerReturn:{inventoryMovementId:''}},{logisticsCustomerReturn:{inventoryMovementId:id+'\n'}},{logisticsCustomerReturn:{inventoryMovementId:id,other:true}},
 {expectedVersion:'0'},{expectedVersion:'9223372036854775807'},{expectedVersion:'01'},{expectedVersion:'1\n'},{inventoryTransferLoss:{requestNumber:1,decidedAtUtc:'2026-11-27T08:00:00Z'}}])('rejects a changed customer-return command shape %j',delta=>{
 expect(isWorkDecisionRequest({...customerRequest,...delta},customerKind)).toBe(false)
})
it('requires customer-return movement identity only on approve and excludes it from every other kind',()=>{
 expect(isWorkDecisionRequest({...customerRequest,approve:false},customerKind)).toBe(false)
 expect(isWorkDecisionRequest({...customerRequest,approve:false,logisticsCustomerReturn:null},customerKind)).toBe(false)
 expect(isWorkDecisionRequest({...request,logisticsCustomerReturn:{inventoryMovementId:id}},kind)).toBe(false)
})

const returnWeighing={weighingId:id,direction:'TareFirst',firstWeighingKg:'10.125',secondWeighingKg:'14.25',netKg:'4.125',recordedBy:'SENTETIK tartımcı',recordedAtUtc:'2026-11-27T10:00:00Z'}
const weighedReturnPreview={...customerPreview,sourceVersion:'2',logisticsCustomerReturn:{...customerFacts,quantityUnit:'Piece',quantity:'1.000',sourceDeliveredQuantity:'2.000',weighing:returnWeighing},rules:[{...customerPreview.rules[0],unit:'adet'}]}
it('accepts retained native weighing without calculating kg and binds the owner logical version and approval support',()=>{
 expect(decodeWorkDecisionPreview(weighedReturnPreview,company,customerKind,id)?.logisticsCustomerReturn?.weighing).toEqual(returnWeighing)
 for(const status of ['Approved','Rejected'])expect(decodeWorkDecisionPreview({...weighedReturnPreview,pending:false,sourceVersion:'3',logisticsCustomerReturn:{...weighedReturnPreview.logisticsCustomerReturn,status}},company,customerKind,id)).not.toBeNull()
 expect(isWorkDecisionRequest({...customerRequest,expectedVersion:'2'},customerKind)).toBe(true)
 expect(isWorkDecisionRequest({...customerRequest,expectedVersion:'9223372036854775806'},customerKind)).toBe(true)
 expect(decodeWorkDecisionPreview(customerPreview,company,customerKind,id)?.logisticsCustomerReturn).not.toHaveProperty('weighing')
})
it.each([null,{}, {...returnWeighing,companyId:company},{...returnWeighing,weighingId:id+'\n'},{...returnWeighing,direction:'Unknown'},
 {...returnWeighing,firstWeighingKg:'10.125\n'},{...returnWeighing,secondWeighingKg:'014.25'},{...returnWeighing,netKg:'4.1250'},
 {...returnWeighing,netKg:'0'},{...returnWeighing,netKg:'1e3'},{...returnWeighing,netKg:'1000000000000000'},
 {...returnWeighing,recordedBy:' '},{...returnWeighing,recordedBy:'x'.repeat(129)},{...returnWeighing,recordedAtUtc:'2026-11-27T10:00:00+03:00'}])('rejects partial, foreign-shaped or noncanonical retained weighing %#',weighing=>{
 expect(decodeWorkDecisionPreview({...weighedReturnPreview,logisticsCustomerReturn:{...weighedReturnPreview.logisticsCustomerReturn,weighing}},company,customerKind,id)).toBeNull()
})
it.each([{sourceVersion:'1'},{sourceVersion:'3'},{sourceVersion:'02'},{approvalSupported:false},{companyId:id},
 {logisticsCustomerReturn:{...weighedReturnPreview.logisticsCustomerReturn,quantityUnit:'Kilogram'}},
 {pending:false,sourceVersion:'2',logisticsCustomerReturn:{...weighedReturnPreview.logisticsCustomerReturn,status:'Approved'}}])('refuses mismatching return weighing scope/version/support %#',delta=>{
 expect(decodeWorkDecisionPreview({...weighedReturnPreview,...delta},company,customerKind,id)).toBeNull()
})

const manualPaymentKind='finance.manual-bank-payment.v1'
const manualPaymentPreview={...preview,kind:manualPaymentKind,rejectionSupported:false,
 references:[{role:'party',recordId:id,label:'SENTETIK cari',link:null},{role:'company-account',recordId:id,label:'SENTETIK şirket hesabı',link:null},
 {role:'bank-account',recordId:id,label:'SENTETIK banka hesabı',link:null},{role:'currency',recordId:id,label:'SENTETIK döviz',link:null}],
 rules:[{ruleCode:'finance.manual-bank-payment-approval-required',threshold:null,actual:'125.25',deviation:null,unit:null}]}
it('publishes the 27th manual payment purpose with the existing generic owner-authorized preview and no new typed body',()=>{
 expect(WORK_DECISION_KINDS).toHaveLength(27);expect(WORK_DECISION_KINDS.filter(kind=>kind===manualPaymentKind)).toEqual([manualPaymentKind])
 expect(decodeWorkDecisionPreview(manualPaymentPreview,company,manualPaymentKind,id)).not.toBeNull()
 expect(decodeWorkDecisionPreview({...manualPaymentPreview,pending:false},company,manualPaymentKind,id)).not.toBeNull()
 for(const delta of [{companyId:id},{itemId:company},{kind:'finance.cash-account.v1'},{sourceVersion:'0'},{sourceVersion:'01'},{sourceVersion:'7\n'},
 {sourceVersion:'9223372036854775807'},{rejectionSupported:true},{rejectionSupported:undefined},{approvalReasonRequired:true},{bankAccount:{}},{manualBankPayment:{}}])
  expect(decodeWorkDecisionPreview({...manualPaymentPreview,...delta},company,manualPaymentKind,id)).toBeNull()
 expect(isWorkDecisionRequest(request,manualPaymentKind)).toBe(true)
 for(const delta of [{approve:false},{reason:''},{reason:'SENTETIK not'},{expectedVersion:'0'},{expectedVersion:'7\n'},{expectedVersion:'9223372036854775807'},
 {manualBankPayment:{}},{inventoryTransferLoss:{requestNumber:1,decidedAtUtc:'2026-11-27T08:00:00Z'}}])expect(isWorkDecisionRequest({...request,...delta},manualPaymentKind)).toBe(false)
})
it('accepts only the exact native Approved manual payment receipt, including original immutable replay after a newer preview',async()=>{
 const receipt={companyId:company,kind:manualPaymentKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-original'}
 const headers={'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'}
 const fetch=vi.fn<(input:RequestInfo|URL,init?:RequestInit)=>Promise<Response>>(async()=>new Response(JSON.stringify(receipt),{status:200,headers}));vi.stubGlobal('fetch',fetch)
 expect((await sendWorkDecision(company,manualPaymentKind,id,request,'SENTETIK-key')).type).toBe('applied')
 expect(JSON.parse(fetch.mock.calls[0]![1]!.body as string)).toEqual(request)
 for(const delta of [{companyId:id},{itemId:company},{kind:'finance.cash-account.v1'},{state:'Recorded'},{state:'Rejected'},{state:'Draft'},{extra:true},{originalCorrelationId:'different'}]){
  fetch.mockImplementation(async()=>new Response(JSON.stringify({...receipt,...delta}),{status:200,headers}))
  expect((await sendWorkDecision(company,manualPaymentKind,id,request,'SENTETIK-key')).type).toBe('unknown')
 }
 const calls=fetch.mock.calls.length
 expect((await sendWorkDecision(company,manualPaymentKind,id,{...request,approve:false},'SENTETIK-key')).type).toBe('unknown');expect(fetch.mock.calls).toHaveLength(calls)
})
