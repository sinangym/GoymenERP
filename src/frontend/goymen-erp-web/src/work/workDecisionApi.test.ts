import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeWorkDecisionPreview, sendWorkDecision, isWorkDecisionRequest, isWorkDecisionKind, isSalesWorkDecisionKind, workDecisionReasonLimit } from './workDecisionApi'
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
