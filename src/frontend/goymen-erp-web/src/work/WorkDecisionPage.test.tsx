import type { ReactElement } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter, matchRoutes } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { shellRoutes } from '../shell/router'
import { ApprovalInboxDetailPage } from './ApprovalInboxPages'
import type { CounterpartyWorkDecisionKind } from './workDecisionApi'
import { WorkDecisionPage } from './WorkDecisionPage'
import { workRoutes } from './workRoutes'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
const id='cccccccc-0000-4000-8000-0000000000f1',kind='finance.cash-account.v1',cap='finance.cash-account.work-decision'
const path=`/work/decisions/${kind}/${id}`
const preview={companyId:COMPANY_A,kind,itemId:id,documentLabel:'SENTETIK kasa',sourceVersion:'7',pending:true,approvalReasonRequired:false,references:[],rules:[]}
afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
function mount(route=`/onaylarim/${kind}/${id}`, fullShell=false){
 const router=createMemoryRouter(fullShell?shellRoutes:[{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[route]})
 render(<ShellProvider queryClient={new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})}><RouterProvider router={router}/></ShellProvider>)
 return router
}
describe('WP8 screenless owner decision',()=>{
 it('freezes an unknown payload and reuses its original key across a remount',async()=>{
  let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{
   if(!url.includes(path))return undefined
   if(init?.method==='POST'){posts++;return posts===1?problem(503):json({companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK'})}
   return json(preview)
  }})
  const baseFetch=globalThis.fetch
  vi.stubGlobal('fetch',vi.fn((input:RequestInfo|URL,init?:RequestInit)=>String(input).endsWith('/identity/me')
    ? Promise.resolve(json({userId:'99999999-9999-4999-8999-999999999999',displayName:'SENTETIK',email:null,allowedCompanyIds:[COMPANY_A],mustChangePassword:false,localCredentialVersion:null,authenticationSessionBinding:'SENTETIK-session-binding'}))
    : baseFetch(input,init)))
  mount();await screen.findByText('SENTETIK kasa',{exact:false});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  await screen.findByText(T.unknown);cleanup();mount();await screen.findByText(T.unknown)
  await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.url.includes(path)&&call.method==='POST')
  expect(sent).toHaveLength(2);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key']);expect(sent[0]!.body).toEqual(sent[1]!.body)
 })
 it('does not read an owner record without its exact capability',async()=>{
  const calls=installFetch({capabilities:[],respond:url=>url.includes(path)?json(preview):undefined})
  mount();await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
  expect(screen.queryByText('SENTETIK kasa',{exact:false})).toBeNull();expect(calls.some(call=>call.url.includes(path))).toBe(false)
 })
 it('opens the existing rule, delegation and follow-up areas only for their own capabilities',async()=>{
  const calls=installFetch({capabilities:['work.delegations.manage'],respond:url=>url.includes('/work/delegations?')?json({companyId:COMPANY_A,items:[],page:0,hasMore:false}):undefined})
  mount('/calisma-yonetimi');expect(await screen.findByRole('heading',{name:'Work yönetimi'})).toBeTruthy();await screen.findByText('Vekâlet kaydı yok.')
  expect(screen.queryByRole('button',{name:'Kurallar'})).toBeNull();expect(screen.queryByRole('button',{name:'Geciken ve sahipsiz'})).toBeNull()
  expect(calls.some(call=>call.url.includes('/work/approval-follow-ups'))).toBe(false)
 })
 it('keeps the selected delegator while searching another delegate and sends no role assignment',async()=>{
  const first='aaaaaaaa-0000-4000-8000-000000000041',second='aaaaaaaa-0000-4000-8000-000000000042'
  const calls=installFetch({capabilities:['work.delegations.manage'],respond:(url,init)=>{
   if(url.includes('/work/delegations/people?')){
    const q=new URL(url,'https://example.invalid').searchParams.get('q')
    return json({companyId:COMPANY_A,kind,items:q==='Ali'?[{userId:first,displayName:'SENTETIK Ali'}]:[{userId:second,displayName:'SENTETIK Bora'}],hasMore:false})
   }
   if(url.endsWith('/work/delegations')&&init?.method==='POST'){
    const body=JSON.parse(init.body as string) as Record<string,unknown>
    return json({...body,id,companyId:COMPANY_A,approverPermission:'finance.cash-account.approve',version:'1',recordedByUserId:'99999999-9999-4999-8999-999999999999',recordedAtUtc:'2026-11-26T08:00:00Z',revokedByUserId:null,revokedAtUtc:null,revocationReason:null},201)
   }
   return url.includes('/work/delegations?')?json({companyId:COMPANY_A,items:[],page:0,hasMore:false}):undefined
  }})
  mount('/calisma-yonetimi');await screen.findByRole('heading',{name:'Work yönetimi'})
  const user=userEvent.setup();await user.selectOptions(screen.getByLabelText('Onay türü'),kind)
  await user.type(screen.getByLabelText('Ad ile ara'),'Ali');await screen.findAllByRole('option',{name:'SENTETIK Ali'})
  await user.selectOptions(screen.getByLabelText('Adına vekâlet oluşturulacak onaycı'),first)
  await user.clear(screen.getByLabelText('Ad ile ara'));await user.type(screen.getByLabelText('Ad ile ara'),'Bora')
  await screen.findAllByRole('option',{name:'SENTETIK Bora'});await user.selectOptions(screen.getByLabelText('Vekil'),second)
  expect((screen.getByLabelText('Adına vekâlet oluşturulacak onaycı') as HTMLSelectElement).value).toBe(first)
  fireEvent.change(screen.getByLabelText('Başlangıç'),{target:{value:'2026-11-27T09:00'}})
  fireEvent.change(screen.getByLabelText('Bitiş'),{target:{value:'2026-11-28T09:00'}})
  await user.type(screen.getByLabelText('Gerekçe'),'SENTETIK süreli vekâlet');await user.click(screen.getByRole('button',{name:'Vekâlet oluştur'}))
  await screen.findByText('İşlem kaydedildi.')
  const sent=calls.find(call=>call.method==='POST'&&call.url.endsWith('/work/delegations'))!
  expect(sent.body).toMatchObject({kind,delegatorUserId:first,delegateUserId:second,reason:'SENTETIK süreli vekâlet'})
  expect(sent.headers['X-Company-Id']).toBe(COMPANY_A);expect(sent.headers['Idempotency-Key']).toBeTruthy()
  expect(calls.some(call=>call.url.includes('/identity/assignments'))).toBe(false)
 })

})

const purchaseKind='purchasing.request.v1',purchaseCap='purchasing.request.work-decision'
const purchasePath=`/work/decisions/${purchaseKind}/${id}`,purchaseRoute=`/onaylarim/${purchaseKind}/${id}`
const purchasePreview={...preview,kind:purchaseKind,documentLabel:'SENTETIK talep'}
describe('WP8 native Purchasing request purpose',()=>{
 it('opens for the purpose-only approver without browse/create/price access and keeps blank rejection closed',async()=>{
  const calls=installFetch({capabilities:[purchaseCap],respond:url=>url.includes(purchasePath)?json(purchasePreview):undefined})
  const router=mount(purchaseRoute,true);await screen.findByText('SENTETIK talep',{exact:false})
  expect((screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).maxLength).toBe(4000)
  expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'   '}})
  expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
  expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(false)
  for(const route of ['/satinalma/talepler','/satinalma/talepler/yeni',`/satinalma/talepler/${id}`]){
   await act(async()=>{await router.navigate(route)})
   await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  }
  expect(calls.some(call=>call.url.includes('/purchasing/'))).toBe(false)
  expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('sends the complete native 4000-character rejection with the displayed version, trimming only the edges',async()=>{
  let decided=false
  const calls=installFetch({capabilities:[purchaseCap],respond:(url,init)=>{
   if(!url.includes(purchasePath))return undefined
   if(init?.method==='POST'){decided=true;return json({companyId:COMPANY_A,kind:purchaseKind,itemId:id,state:'Rejected',replayed:false,originalCorrelationId:'SENTETIK'})}
   return json(decided?{...purchasePreview,pending:false,sourceVersion:'8'}:purchasePreview)
  }})
  mount(purchaseRoute);await screen.findByText('SENTETIK talep',{exact:false})
  const reason='R'.repeat(4000)
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:`  ${reason}  `}})
  await userEvent.setup().click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.done);await screen.findByText(T.closed)
  const sent=calls.filter(call=>call.method==='POST')
  expect(sent).toHaveLength(1);expect(sent[0].body).toEqual({expectedVersion:'7',approve:false,reason,delegationId:null})
  expect(sent[0].headers['X-Company-Id']).toBe(COMPANY_A)
  expect(sent[0].headers['Idempotency-Key']).toBeTruthy()
 })
 it('does not send or silently cut an overlong native rejection',async()=>{
  const calls=installFetch({capabilities:[purchaseCap],respond:url=>url.includes(purchasePath)?json(purchasePreview):undefined})
  mount(purchaseRoute);await screen.findByText('SENTETIK talep',{exact:false})
  const input=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement
  fireEvent.change(input,{target:{value:'R'.repeat(4001)}})
  expect(input.value).toHaveLength(4001)
  expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
  await userEvent.setup().click(screen.getByRole('button',{name:T.reject}))
  expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('keeps the original rejection version/body/key across remount and a newer closed owner projection',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-purchase-decision'})
  let posts=0
  const calls=installFetch({capabilities:[purchaseCap],respond:(url,init)=>{
   if(!url.includes(purchasePath))return undefined
   if(init?.method==='POST')return ++posts===1?problem(503):new Response(JSON.stringify({companyId:COMPANY_A,kind:purchaseKind,itemId:id,state:'Rejected',replayed:true,originalCorrelationId:'original'}),{status:200,headers:{'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}})
   return json(posts?{...purchasePreview,pending:false,sourceVersion:'8'}:purchasePreview)
  }})
  mount(purchaseRoute);await screen.findByText('SENTETIK talep',{exact:false})
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'Native ret gerekçesi'}})
  await userEvent.setup().click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.unknown)
  cleanup();mount(purchaseRoute);await screen.findByText(T.closed);await screen.findByText(T.unknown)
  await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2)
  expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].body).toMatchObject({expectedVersion:'7',reason:'Native ret gerekçesi'})
  expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
 })
 it('keeps maker/submitter refusal on the server and closes on a fresh command 403',async()=>{
  const calls=installFetch({capabilities:[purchaseCap],respond:(url,init)=>url.includes(purchasePath)?init?.method==='POST'?problem(403):json(purchasePreview):undefined})
  mount(purchaseRoute);await screen.findByText('SENTETIK talep',{exact:false})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(screen.queryByRole('button',{name:T.approve})).toBeNull())
  expect(screen.queryByText(T.done)).toBeNull()
  expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
  expect(calls.find(call=>call.method==='POST')?.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null})
 })
 it('refuses a foreign-company preview without displaying or deciding its record',async()=>{
  const calls=installFetch({capabilities:[purchaseCap],respond:url=>url.includes(purchasePath)?json({...purchasePreview,companyId:COMPANY_B}):undefined})
  mount(purchaseRoute)
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  expect(screen.queryByText('SENTETIK talep',{exact:false})).toBeNull()
  expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('does not read the owner when the actor has only the old browse-dependent approval control or no purpose capability',async()=>{
  const calls=installFetch({capabilities:['purchasing.requests.approve'],respond:url=>url.includes(purchasePath)?json(purchasePreview):undefined})
  mount(purchaseRoute,true)
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(calls.some(call=>call.url.includes(purchasePath))).toBe(false)
 })
})


const orderKind='purchasing.order.v1',orderCap='purchasing.order.work-decision'
const orderPath=`/work/decisions/${orderKind}/${id}`,orderRoute=`/onaylarim/${orderKind}/${id}`
const orderPreview={...preview,kind:orderKind,documentLabel:'SENTETIK siparis',rejectionSupported:false}

describe('WP8 native purchase order approve-only purpose',()=>{
 it('shows approval alone for a purpose-only approver and grants no order browse or create access',async()=>{
  const calls=installFetch({capabilities:[orderCap],respond:url=>url.includes(orderPath)?json(orderPreview):undefined})
  const router=mount(orderRoute,true);await screen.findByText('SENTETIK siparis',{exact:false})
  expect(screen.getByRole('button',{name:T.approve})).toBeTruthy()
  expect(screen.getByText(T.approvalOnly)).toBeTruthy()
  expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull()
  for(const route of ['/satinalma/siparisler','/satinalma/siparisler/yeni',`/satinalma/siparisler/${id}`]) {
   await act(async()=>{await router.navigate(route)})
   await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  }
  expect(calls.some(call=>call.url.includes('/purchasing/')||call.method==='POST')).toBe(false)
 })
 it.each(['Draft','Cancelled'])('retries unknown approval with the same key and body after remount at current %s',async state=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-order-decision'})
  let posts=0
  const calls=installFetch({capabilities:[orderCap],respond:(url,init)=>{
   if(!url.includes(orderPath))return undefined
   if(init?.method==='POST')return ++posts===1?problem(503):new Response(JSON.stringify({companyId:COMPANY_A,kind:orderKind,itemId:id,state,replayed:true,originalCorrelationId:'original'}),{status:200,headers:{'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}})
   return json(posts?{...orderPreview,pending:false,sourceVersion:'9'}:orderPreview)
  }})
  mount(orderRoute);await screen.findByText('SENTETIK siparis',{exact:false})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  cleanup();mount(orderRoute);await screen.findByText(T.closed);await screen.findByText(T.unknown)
  await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2)
  expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null})
  expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  expect(calls.filter(call=>call.url.includes(orderPath)&&call.method==='GET').length).toBeGreaterThanOrEqual(3)
 })
 it.each([{companyId:COMPANY_B},{rejectionSupported:true},{rejectionSupported:undefined}])('rejects an untrusted order preview %j before any decision',async delta=>{
  const calls=installFetch({capabilities:[orderCap],respond:url=>url.includes(orderPath)?json({...orderPreview,...delta}):undefined})
  mount(orderRoute);await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  expect(screen.queryByText('SENTETIK siparis',{exact:false})).toBeNull()
  expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('never reads the owner for a browse-dependent control without the purpose capability',async()=>{
  const calls=installFetch({capabilities:['purchasing.orders.approve'],respond:url=>url.includes(orderPath)?json(orderPreview):undefined})
  mount(orderRoute,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(calls.some(call=>call.url.includes(orderPath))).toBe(false)
 })
 it('closes after fresh owner denial without displaying success',async()=>{
  const calls=installFetch({capabilities:[orderCap],respond:(url,init)=>url.includes(orderPath)?init?.method==='POST'?problem(403):json(orderPreview):undefined})
  mount(orderRoute);await screen.findByText('SENTETIK siparis',{exact:false})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(screen.queryByRole('button',{name:T.approve})).toBeNull())
  expect(screen.queryByText(T.done)).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
 })
})


const serviceKind='purchasing.service-order.v1',serviceCap='purchasing.service-order.work-decision'
const servicePath=`/work/decisions/${serviceKind}/${id}`,serviceRoute=`/onaylarim/${serviceKind}/${id}`
const servicePreview={...preview,kind:serviceKind,documentLabel:'SENTETIK hizmet',approvalReasonRequired:true,rejectionSupported:false}

describe('WP8 native service order purpose',()=>{
 it('requires the actual human reason for approval and keeps browse/create/price and rejection unavailable',async()=>{
  let decided=false
  const calls=installFetch({capabilities:[serviceCap],respond:(url,init)=>{
   if(!url.includes(servicePath))return undefined
   if(init?.method==='POST'){decided=true;return json({companyId:COMPANY_A,kind:serviceKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK'})}
   return json(decided?{...servicePreview,pending:false,sourceVersion:'8'}:servicePreview)
  }})
  const router=mount(serviceRoute,true);await screen.findByText('SENTETIK hizmet',{exact:false})
  const input=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement
  expect(input.maxLength).toBe(1000);expect(input.value).toBe('')
  expect(screen.getByText(T.approvalReasonRequired(1000))).toBeTruthy()
  expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  const button=screen.getByRole('button',{name:T.approve}) as HTMLButtonElement
  expect(button.disabled).toBe(true)
  fireEvent.change(input,{target:{value:'   '}});expect(button.disabled).toBe(true)
  const reason='R'.repeat(1000)
  fireEvent.change(input,{target:{value:`  ${reason}  `}});expect(button.disabled).toBe(false)
  await userEvent.setup().click(button);await screen.findByText(T.done);await screen.findByText(T.closed)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(1)
  expect(sent[0].body).toEqual({expectedVersion:'7',approve:true,reason,delegationId:null})
  expect(sent[0].headers['X-Company-Id']).toBe(COMPANY_A);expect(sent[0].headers['Idempotency-Key']).toBeTruthy()
  for(const route of ['/satinalma/hizmet-siparisleri','/satinalma/hizmet-siparisleri/yeni',`/satinalma/hizmet-siparisleri/${id}`]) {
   await act(async()=>{await router.navigate(route)})
   await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  }
  expect(calls.some(call=>call.url.includes('/purchasing/'))).toBe(false)
 })
 it('does not truncate or send an overlong service approval reason',async()=>{
  const calls=installFetch({capabilities:[serviceCap],respond:url=>url.includes(servicePath)?json(servicePreview):undefined})
  mount(serviceRoute);await screen.findByText('SENTETIK hizmet',{exact:false})
  const input=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement
  fireEvent.change(input,{target:{value:'R'.repeat(1001)}});expect(input.value).toHaveLength(1001)
  expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('preserves the original human reason/version/key through unknown remount and a newer Draft preview',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-service-decision'})
  let posts=0
  const calls=installFetch({capabilities:[serviceCap],respond:(url,init)=>{
   if(!url.includes(servicePath))return undefined
   if(init?.method==='POST')return ++posts===1?problem(503):new Response(JSON.stringify({companyId:COMPANY_A,kind:serviceKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'}),{status:200,headers:{'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}})
   return json(posts?{...servicePreview,sourceVersion:'9'}:servicePreview)
  }})
  mount(serviceRoute);await screen.findByText('SENTETIK hizmet',{exact:false})
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'  Original human reason  '}})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  cleanup();mount(serviceRoute);await screen.findByText(T.unknown);await screen.findByText('SENTETIK hizmet',{exact:false})
  expect(screen.getByRole('textbox',{name:T.reason}).closest('fieldset')?.disabled).toBe(true)
  await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2)
  expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].body).toEqual({expectedVersion:'7',approve:true,reason:'Original human reason',delegationId:null})
  expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  expect(calls.filter(call=>call.url.includes(servicePath)&&call.method==='GET').length).toBeGreaterThanOrEqual(3)
 })
 it.each([{companyId:COMPANY_B},{rejectionSupported:true},{rejectionSupported:undefined},{approvalReasonRequired:false}])('fails closed on untrusted service preview %j',async delta=>{
  const calls=installFetch({capabilities:[serviceCap],respond:url=>url.includes(servicePath)?json({...servicePreview,...delta}):undefined})
  mount(serviceRoute);await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  expect(screen.queryByText('SENTETIK hizmet',{exact:false})).toBeNull();expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('does not read service owner without its purpose capability',async()=>{
  const calls=installFetch({capabilities:['purchasing.service-orders.approve'],respond:url=>url.includes(servicePath)?json(servicePreview):undefined})
  mount(serviceRoute,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(calls.some(call=>call.url.includes(servicePath))).toBe(false)
 })
 it('keeps fresh native refusal authoritative after the human reason is entered',async()=>{
  const calls=installFetch({capabilities:[serviceCap],respond:(url,init)=>url.includes(servicePath)?init?.method==='POST'?problem(403):json(servicePreview):undefined})
  mount(serviceRoute);await screen.findByText('SENTETIK hizmet',{exact:false})
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'Actual human reason'}})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(screen.queryByRole('button',{name:T.approve})).toBeNull())
  expect(screen.queryByText(T.done)).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
 })
})


const varianceKind='purchasing.incoming-invoice-variance.v1',varianceCap='purchasing.invoice-variance.work-decision'
const variancePath=`/work/decisions/${varianceKind}/${id}`,varianceRoute=`/onaylarim/${varianceKind}/${id}`
const variancePreview={...preview,kind:varianceKind,documentLabel:'SENTETIK fark',approvalReasonRequired:true,rejectionSupported:false}

describe('WP8 native invoice variance purpose',()=>{
 it('uses its purpose capability and human reason while keeping staged invoice routes and rejection unavailable',async()=>{
  let decided=false
  const calls=installFetch({capabilities:[varianceCap],respond:(url,init)=>{
   if(!url.includes(variancePath))return undefined
   if(init?.method==='POST'){decided=true;return json({companyId:COMPANY_A,kind:varianceKind,itemId:id,state:'Matched',replayed:false,originalCorrelationId:'SENTETIK'})}
   return json(decided?{...variancePreview,pending:false,sourceVersion:'8'}:variancePreview)
  }})
  const router=mount(varianceRoute,true);await screen.findByText('SENTETIK fark',{exact:false})
  const input=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement
  expect(input.maxLength).toBe(2000);expect(input.value).toBe('')
  expect(screen.getByText(T.approvalReasonRequired(2000))).toBeTruthy()
  expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  const button=screen.getByRole('button',{name:T.approve}) as HTMLButtonElement
  expect(button.disabled).toBe(true)
  fireEvent.change(input,{target:{value:'   '}});expect(button.disabled).toBe(true)
  const reason='R'.repeat(2000)
  fireEvent.change(input,{target:{value:`  ${reason}  `}});expect(button.disabled).toBe(false)
  await userEvent.setup().click(button);await screen.findByText(T.done);await screen.findByText(T.closed)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(1)
  expect(sent[0].body).toEqual({expectedVersion:'7',approve:true,reason,delegationId:null})
  expect(sent[0].headers['X-Company-Id']).toBe(COMPANY_A);expect(sent[0].headers['Idempotency-Key']).toBeTruthy()
  for(const route of ['/satinalma/faturalar',`/satinalma/faturalar/${id}`]) {
   await act(async()=>{await router.navigate(route)})
   await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  }
  expect(calls.some(call=>call.url.includes('/purchasing/'))).toBe(false)
 })
 it('does not truncate or send an overlong invoice variance approval reason',async()=>{
  const calls=installFetch({capabilities:[varianceCap],respond:url=>url.includes(variancePath)?json(variancePreview):undefined})
  mount(varianceRoute);await screen.findByText('SENTETIK fark',{exact:false})
  const input=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement
  fireEvent.change(input,{target:{value:'R'.repeat(2001)}});expect(input.value).toHaveLength(2001)
  expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('preserves the original human reason/version/key through unknown remount and a newer closed Matched preview',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-variance-decision'})
  let posts=0
  const calls=installFetch({capabilities:[varianceCap],respond:(url,init)=>{
   if(!url.includes(variancePath))return undefined
   if(init?.method==='POST')return ++posts===1?problem(503):new Response(JSON.stringify({companyId:COMPANY_A,kind:varianceKind,itemId:id,state:'Matched',replayed:true,originalCorrelationId:'original'}),{status:200,headers:{'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}})
   return json(posts?{...variancePreview,pending:false,sourceVersion:'9'}:variancePreview)
  }})
  mount(varianceRoute);await screen.findByText('SENTETIK fark',{exact:false})
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'  Original human reason  '}})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  cleanup();mount(varianceRoute);await screen.findByText(T.unknown);await screen.findByText('SENTETIK fark',{exact:false})
  await screen.findByText(T.closed)
  expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
  await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2)
  expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].body).toEqual({expectedVersion:'7',approve:true,reason:'Original human reason',delegationId:null})
  expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  expect(calls.filter(call=>call.url.includes(variancePath)&&call.method==='GET').length).toBeGreaterThanOrEqual(3)
 })
 it.each([{companyId:COMPANY_B},{rejectionSupported:true},{rejectionSupported:undefined},{approvalReasonRequired:false}])('fails closed on untrusted variance preview %j',async delta=>{
  const calls=installFetch({capabilities:[varianceCap],respond:url=>url.includes(variancePath)?json({...variancePreview,...delta}):undefined})
  mount(varianceRoute);await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  expect(screen.queryByText('SENTETIK fark',{exact:false})).toBeNull();expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('does not read variance owner without its purpose capability',async()=>{
  const calls=installFetch({capabilities:['purchasing.invoices.approve-variance'],respond:url=>url.includes(variancePath)?json(variancePreview):undefined})
  mount(varianceRoute,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(calls.some(call=>call.url.includes(variancePath))).toBe(false)
 })
 it('keeps fresh native refusal authoritative after the human reason is entered',async()=>{
  const calls=installFetch({capabilities:[varianceCap],respond:(url,init)=>url.includes(variancePath)?init?.method==='POST'?problem(403):json(variancePreview):undefined})
  mount(varianceRoute);await screen.findByText('SENTETIK fark',{exact:false})
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'Actual human reason'}})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(screen.queryByRole('button',{name:T.approve})).toBeNull())
  expect(screen.queryByText(T.done)).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
 })
})


it('freezes a foreign variance success response as unknown without automatically retrying or showing success',async()=>{
 const calls=installFetch({capabilities:[varianceCap],respond:(url,init)=>url.includes(variancePath)?init?.method==='POST'
  ?json({companyId:COMPANY_B,kind:varianceKind,itemId:id,state:'Matched',replayed:false,originalCorrelationId:'SENTETIK'}):json(variancePreview):undefined})
 mount(varianceRoute);await screen.findByText('SENTETIK fark',{exact:false})
 fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'Actual human variance reason'}})
 await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 expect(screen.queryByText(T.done)).toBeNull()
 expect(screen.getByRole('button',{name:T.approve}).closest('fieldset')?.disabled).toBe(true)
 await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
 expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
})


const batchKind='purchasing.goods-receipt-batch.v1',batchCap='purchasing.goods-receipt-batch.work-decision'
const batchPath=`/work/decisions/${batchKind}/${id}`,batchRoute=`/onaylarim/${batchKind}/${id}`
const batchPreview={...preview,kind:batchKind,documentLabel:'SENTETIK parti',rejectionSupported:true,
 references:[{role:'purchase-order',label:'SENTETIK owner order',recordId:COMPANY_B,link:null}]}

describe('WP8 native goods receipt batch purpose',()=>{
 it('opens with its purpose capability, allows reasonless approval, requires human rejection reason and opens no browse workspace',async()=>{
  let decided=false
  const calls=installFetch({capabilities:[batchCap],respond:(url,init)=>{
   if(!url.includes(batchPath))return undefined
   if(init?.method==='POST'){decided=true;return json({companyId:COMPANY_A,kind:batchKind,itemId:id,state:'Posted',replayed:false,originalCorrelationId:'SENTETIK'})}
   return json(decided?{...batchPreview,pending:false,sourceVersion:'8'}:batchPreview)
  }})
  const router=mount(batchRoute,true);await screen.findByText('SENTETIK parti',{exact:false})
  expect((screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).maxLength).toBe(500)
  expect(screen.getByText(T.batchReason(500))).toBeTruthy()
  expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'   '}})
  expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:''}})
  expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(false)
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done);await screen.findByText(T.closed)
  expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
  expect(calls.find(call=>call.method==='POST')?.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null})
  for(const route of ['/satinalma/siparisler',`/satinalma/siparisler/${id}`]) {
   await act(async()=>{await router.navigate(route)})
   await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  }
  expect(calls.some(call=>call.url.includes('/purchasing/'))).toBe(false)
 })
 it.each([true,false])('forwards the exact native 500-character input for approve=%s without adding the owner order argument',async approve=>{
  const calls=installFetch({capabilities:[batchCap],respond:(url,init)=>url.includes(batchPath)?init?.method==='POST'
   ?json({companyId:COMPANY_A,kind:batchKind,itemId:id,state:approve?'Posted':'Rejected',replayed:false,originalCorrelationId:'SENTETIK'}):json(batchPreview):undefined})
  mount(batchRoute);await screen.findByText('SENTETIK parti',{exact:false})
  const reason='  R'+String.fromCharCode(10)+'R'.repeat(494)+'  '
  expect(reason).toHaveLength(500)
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:reason}})
  await userEvent.setup().click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(1)
  expect(sent[0].body).toEqual({expectedVersion:'7',approve,reason,delegationId:null})
  expect(sent[0].headers['X-Company-Id']).toBe(COMPANY_A);expect(sent[0].headers['Idempotency-Key']).toBeTruthy()
 })
 it.each([true,false])('does not trim a raw 501-character input into the native bound for approve=%s',async approve=>{
  const calls=installFetch({capabilities:[batchCap],respond:url=>url.includes(batchPath)?json(batchPreview):undefined})
  mount(batchRoute);await screen.findByText('SENTETIK parti',{exact:false})
  const reason=' '+'R'.repeat(499)+' '
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:reason}})
  expect((screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).value).toHaveLength(501)
  const button=screen.getByRole('button',{name:approve?T.approve:T.reject}) as HTMLButtonElement
  expect(button.disabled).toBe(true);await userEvent.setup().click(button)
  expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it.each([true,false])('leaves forbidden edge-control validation to the native owner without silently removing it for approve=%s',async approve=>{
  const calls=installFetch({capabilities:[batchCap],respond:(url,init)=>url.includes(batchPath)?init?.method==='POST'?problem(400):json(batchPreview):undefined})
  mount(batchRoute);await screen.findByText('SENTETIK parti',{exact:false})
  const reason=String.fromCharCode(11)+'Actual reason'+String.fromCharCode(11)
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:reason}})
  await userEvent.setup().click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(`${T.refused} (400)`)
  expect(calls.find(call=>call.method==='POST')?.body).toEqual({expectedVersion:'7',approve,reason,delegationId:null})
  expect(screen.queryByText(T.done)).toBeNull()
 })
 it.each([true,false])('replays an unknown approve=%s attempt with its original body/key after a newer closed preview',async approve=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-batch-decision'})
  let posts=0
  const state=approve?'Posted':'Rejected',reason='Actual batch reason'
  const calls=installFetch({capabilities:[batchCap],respond:(url,init)=>{
   if(!url.includes(batchPath))return undefined
   if(init?.method==='POST')return ++posts===1?problem(503):new Response(JSON.stringify({companyId:COMPANY_A,kind:batchKind,itemId:id,state,replayed:true,originalCorrelationId:'original'}),{status:200,headers:{'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'}})
   return json(posts?{...batchPreview,pending:false,sourceVersion:'8'}:batchPreview)
  }})
  mount(batchRoute);await screen.findByText('SENTETIK parti',{exact:false})
  fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:reason}})
  await userEvent.setup().click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.unknown)
  expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
  cleanup();mount(batchRoute);await screen.findByText(T.unknown);await screen.findByText(T.closed)
  await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2)
  expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].body).toEqual({expectedVersion:'7',approve,reason,delegationId:null})
  expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  expect(calls.filter(call=>call.url.includes(batchPath)&&call.method==='GET').length).toBeGreaterThanOrEqual(3)
 })
 it.each([{companyId:COMPANY_B},{rejectionSupported:false},{rejectionSupported:undefined},{approvalReasonRequired:true}])('rejects a malformed or foreign batch preview %j',async delta=>{
  const calls=installFetch({capabilities:[batchCap],respond:url=>url.includes(batchPath)?json({...batchPreview,...delta}):undefined})
  mount(batchRoute);await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  expect(screen.queryByText('SENTETIK parti',{exact:false})).toBeNull();expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('does not read the batch owner for an old control without the exact purpose capability',async()=>{
  const calls=installFetch({capabilities:['purchasing.goods-receipts.approve-variance'],respond:url=>url.includes(batchPath)?json(batchPreview):undefined})
  mount(batchRoute,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(calls.some(call=>call.url.includes(batchPath))).toBe(false)
 })
 it('keeps a fresh native 403 authoritative without showing batch success',async()=>{
  const calls=installFetch({capabilities:[batchCap],respond:(url,init)=>url.includes(batchPath)?init?.method==='POST'?problem(403):json(batchPreview):undefined})
  mount(batchRoute);await screen.findByText('SENTETIK parti',{exact:false})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(screen.queryByRole('button',{name:T.approve})).toBeNull())
  expect(screen.queryByText(T.done)).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
 })
})


const mfgKind='manufacturing.plan.v1', mfgCap='manufacturing.plans.work-decision'
const labels=['aaaaaaaa-0000-4000-8000-000000000071','aaaaaaaa-0000-4000-8000-000000000072']
const mfgPreview={...preview,kind:mfgKind,documentLabel:'SENTETIK üretim planı',rejectionSupported:false,
 rules:[{ruleCode:'manufacturing.plan-reapproval-after-order-cancel',threshold:null,actual:null,deviation:null,unit:null}],
 manufacturingPlanReapproval:{parentRollIds:labels}}

it('starts Manufacturing selection empty, requires the full options beyond references, and allows clearing', async () => {
 const calls=installFetch({capabilities:[mfgCap],respond:(url,init)=>{
  if(!url.includes(`/work/decisions/${mfgKind}/${id}`))return undefined
  return init?.method==='POST'?problem(503):json(mfgPreview)
 }})
 mount(`/onaylarim/${mfgKind}/${id}`)
 await screen.findByText('SENTETIK üretim planı',{exact:false})
 expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)
 expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
 for(const label of labels) expect((screen.getByRole('checkbox',{name:label}) as HTMLInputElement).checked).toBe(false)
 fireEvent.click(screen.getByRole('checkbox',{name:labels[0]}))
 expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.click(screen.getByRole('button',{name:T.clearLabels}))
 expect((screen.getByRole('checkbox',{name:labels[0]}) as HTMLInputElement).checked).toBe(false)
 for(const label of labels) fireEvent.click(screen.getByRole('checkbox',{name:label}))
 fireEvent.click(screen.getByRole('button',{name:T.approve}))
 await screen.findByText(T.unknown)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(`/work/decisions/${mfgKind}/`))
 expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null,manufacturingPlanReapproval:{selectedLabelIds:labels}})
 for(const label of labels) expect((screen.getByRole('checkbox',{name:label}) as HTMLInputElement).disabled).toBe(true)
})

it.each(['closed','changed-options','initial-approval'] as const)('replays the original Manufacturing journal after %s preview drift', async scenario => {
 let posts=0,reads=0
 const calls=installFetch({capabilities:[mfgCap],respond:(url,init)=>{
  if(!url.includes(`/work/decisions/${mfgKind}/${id}`))return undefined
  if(init?.method==='POST') {posts++;return posts===1?problem(503):json({companyId:COMPANY_A,kind:mfgKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})}
  reads++
  if(reads===1) return json(scenario==='initial-approval'?{...preview,kind:mfgKind,rejectionSupported:true}:mfgPreview)
  if(scenario==='closed') return json({...preview,kind:mfgKind,pending:false,rejectionSupported:true,sourceVersion:'8'})
  return json({...mfgPreview,sourceVersion:'9',manufacturingPlanReapproval:{parentRollIds:[COMPANY_B]}})
 }})
 const baseFetch=globalThis.fetch
 vi.stubGlobal('fetch',vi.fn((input:RequestInfo|URL,init?:RequestInit)=>String(input).endsWith('/identity/me')
  ? Promise.resolve(json({userId:'99999999-9999-4999-8999-999999999999',displayName:'SENTETIK',email:null,allowedCompanyIds:[COMPANY_A],mustChangePassword:false,localCredentialVersion:null,authenticationSessionBinding:'SENTETIK-mfg-session'}))
  : baseFetch(input,init)))
 mount(`/onaylarim/${mfgKind}/${id}`)
 await screen.findByText(scenario==='initial-approval'?'SENTETIK kasa':'SENTETIK üretim planı',{exact:false})
 if(scenario!=='initial-approval') for(const label of labels) fireEvent.click(screen.getByRole('checkbox',{name:label}))
 fireEvent.click(screen.getByRole('button',{name:T.approve}))
 await screen.findByText(T.unknown)
 cleanup();mount(`/onaylarim/${mfgKind}/${id}`)
 await screen.findByText(T.unknown)
 fireEvent.click(screen.getByRole('button',{name:T.retry}))
 await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(`/work/decisions/${mfgKind}/`))
 expect(sent).toHaveLength(2)
 expect(sent[1]!.body).toEqual(sent[0]!.body)
 expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 expect((sent[1]!.body as {expectedVersion:string}).expectedVersion).toBe('7')
 expect(Object.hasOwn(sent[0]!.body as object,'manufacturingPlanReapproval')).toBe(scenario!=='initial-approval')
})

it.each([{capabilities:['manufacturing.plans.detail','manufacturing.plans.approve']}, {capabilities:['manufacturing.plan.work-decision']}])('does not fetch the Manufacturing owner with native browse/control or an invented singular capability %j', async ({capabilities}) => {
 const calls=installFetch({capabilities,respond:url=>url.includes(`/work/decisions/${mfgKind}/`)?json(mfgPreview):undefined})
 mount(`/onaylarim/${mfgKind}/${id}`)
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
 expect(calls.some(call=>call.url.includes(`/work/decisions/${mfgKind}/`))).toBe(false)
})


it('labels the native 256-character reason as rejection-only and sends no reason on initial approval', async () => {
 const calls=installFetch({capabilities:[mfgCap],respond:(url,init)=>{
  if(!url.includes(`/work/decisions/${mfgKind}/${id}`))return undefined
  return init?.method==='POST'?problem(503):json({...preview,kind:mfgKind,rejectionSupported:true})
 }})
 mount(`/onaylarim/${mfgKind}/${id}`)
 const input=await screen.findByLabelText(T.manufacturingRejectReason)
 expect((input as HTMLTextAreaElement).maxLength).toBe(256)
 expect(screen.getByText(T.manufacturingRejectHelp)).toBeTruthy()
 fireEvent.change(input,{target:{value:'Yalnız ret için insan gerekçesi'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}))
 await screen.findByText(T.unknown)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(`/work/decisions/${mfgKind}/`))
 expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null})
})


it.each([{companyId:COMPANY_B},{sourceVersion:'0'},{manufacturingPlanReapproval:{parentRollIds:[]}},
 {manufacturingPlanReapproval:{parentRollIds:[labels[0],labels[0]]}}])('refuses untrusted Manufacturing preview %j before selection or mutation', async delta => {
 const calls=installFetch({capabilities:[mfgCap],respond:url=>url.includes(`/work/decisions/${mfgKind}/${id}`)?json({...mfgPreview,...delta}):undefined})
 mount(`/onaylarim/${mfgKind}/${id}`)
 await waitFor(()=>expect(calls.some(call=>call.url.includes(`/work/decisions/${mfgKind}/`))).toBe(true))
 expect(screen.queryByRole('checkbox',{name:labels[0]})).toBeNull()
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 expect(calls.some(call=>call.method==='POST')).toBe(false)
})

it('retains the complete 256-character Manufacturing rejection and rejects an overlong human reason', async () => {
 const calls=installFetch({capabilities:[mfgCap],respond:(url,init)=>{
  if(!url.includes(`/work/decisions/${mfgKind}/${id}`))return undefined
  return init?.method==='POST'?json({companyId:COMPANY_A,kind:mfgKind,itemId:id,state:'Draft',replayed:false,originalCorrelationId:'original'}):json({...preview,kind:mfgKind,rejectionSupported:true})
 }})
 mount(`/onaylarim/${mfgKind}/${id}`)
 const input=await screen.findByLabelText(T.manufacturingRejectReason)
 fireEvent.change(input,{target:{value:'R'.repeat(257)}})
 expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
 const reason='R'.repeat(256)
 fireEvent.change(input,{target:{value:`  ${reason}  `}})
 fireEvent.click(screen.getByRole('button',{name:T.reject}))
 await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(`/work/decisions/${mfgKind}/`))
 expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'7',approve:false,reason,delegationId:null})
})


// Final publication candidate: exercise the real dedicated route and native purpose capability.
const lossKind='inventory.transfer-loss.v1',lossCap='inventory.transfer-loss.work-decision'
const lossPreview={...preview,kind:lossKind,approvalReasonRequired:true,rejectionSupported:true,
 inventoryTransferLoss:{requestNumber:1,requestedAtUtc:'2026-11-26T08:00:00Z'}}
function mountLossPurpose(){ return mount(`/onaylarim/${lossKind}/${id}`) }
it.each([true,false])('requires a human time and complete 500-character reason for loss approve=%s', async approve => {
 const calls=installFetch({capabilities:[lossCap],respond:(url,init)=>url.includes(`/work/decisions/${lossKind}/${id}`)
  ?init?.method==='POST'?json({companyId:COMPANY_A,kind:lossKind,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'original'}):json(lossPreview):undefined})
 mountLossPurpose()
 const time=await screen.findByLabelText(T.lossDecisionTime)
 const button=screen.getByRole('button',{name:approve?T.approve:T.reject}) as HTMLButtonElement
 expect(button.disabled).toBe(true)
 const local='2026-11-26T11:30:00'
 fireEvent.change(time,{target:{value:local}})
 expect(button.disabled).toBe(true)
 const reason=screen.getByLabelText(T.reason)
 fireEvent.change(reason,{target:{value:'R'.repeat(501)}})
 expect(button.disabled).toBe(true)
 fireEvent.change(reason,{target:{value:'R'.repeat(500)}})
 fireEvent.click(button)
 await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(`/work/decisions/${lossKind}/`))
 expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'7',approve,reason:'R'.repeat(500),delegationId:null,inventoryTransferLoss:{requestNumber:1,decidedAtUtc:new Date(local).toISOString()}})
})
it.each(['renewed','closed'] as const)('replays the frozen loss number/time/reason/body/key after %s preview', async state => {
 let reads=0,posts=0
 const calls=installFetch({capabilities:[lossCap],respond:(url,init)=>{
  if(!url.includes(`/work/decisions/${lossKind}/${id}`))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:lossKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(++reads===1?lossPreview:{...lossPreview,pending:state!=='closed',sourceVersion:'9',inventoryTransferLoss:{requestNumber:2,requestedAtUtc:'2026-11-27T08:00:00Z'}})
 }})
 const baseFetch=globalThis.fetch
 vi.stubGlobal('fetch',vi.fn((input:RequestInfo|URL,init?:RequestInit)=>String(input).endsWith('/identity/me')
  ? Promise.resolve(json({userId:'99999999-9999-4999-8999-999999999999',displayName:'SENTETIK',email:null,allowedCompanyIds:[COMPANY_A],mustChangePassword:false,localCredentialVersion:null,authenticationSessionBinding:'SENTETIK-loss-session'}))
  : baseFetch(input,init)))
 mountLossPurpose()
 const time=await screen.findByLabelText(T.lossDecisionTime)
 fireEvent.change(time,{target:{value:'2026-11-26T11:30:00'}})
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'İnsan kayıp kararı'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}))
 await screen.findByText(T.unknown)
 cleanup();mountLossPurpose();await screen.findByText(T.unknown)
 await screen.findByText(T.lossFrozenTime,{exact:false})
 fireEvent.click(screen.getByRole('button',{name:T.retry}))
 await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(`/work/decisions/${lossKind}/`))
 expect(sent).toHaveLength(2)
 expect(sent[0]!.body).toEqual(sent[1]!.body)
 expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect((sent[1]!.body as {inventoryTransferLoss:{requestNumber:number}}).inventoryTransferLoss.requestNumber).toBe(1)
})


it('mounts the exact loss purpose and component under the actual native purpose capability', () => {
 const route=matchRoutes(workRoutes,`/onaylarim/${lossKind}/${id}`)?.at(-1)?.route
 expect(route?.path).toBe('onaylarim/inventory.transfer-loss.v1/:approvalId')
 const element=route?.element as ReactElement<{capability:readonly string[];children:ReactElement<{kind:string}>}>
 expect(element.props.capability).toEqual(['inventory.transfer-loss.work-decision'])
 expect(element.props.children.type).toBe(WorkDecisionPage)
 expect(element.props.children.props.kind).toBe(lossKind)
})
it.each([{companyId:COMPANY_B},{inventoryTransferLoss:null},{inventoryTransferLoss:{requestNumber:1,requestedAtUtc:'bad'}}])('fails closed on malformed or foreign loss preview %j', async delta => {
 const calls=installFetch({capabilities:[lossCap],respond:url=>url.includes(`/work/decisions/${lossKind}/${id}`)?json({...lossPreview,...delta}):undefined})
 mountLossPurpose()
 await waitFor(()=>expect(calls.some(call=>call.url.includes(`/work/decisions/${lossKind}/`))).toBe(true))
 expect(screen.queryByLabelText(T.lossDecisionTime)).toBeNull()
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 expect(calls.some(call=>call.method==='POST')).toBe(false)
})


it.each([{capabilities:[]},{capabilities:['inventory.stock-transfer.loss-approve']},{capabilities:['inventory.transfer-loss-decision']},
 {capabilities:['finance.cash-account.work-decision']}])('does not fetch a loss owner under missing or wrong capability %j', async ({capabilities}) => {
 const calls=installFetch({capabilities,respond:url=>url.includes(`/work/decisions/${lossKind}/`)?json(lossPreview):undefined})
 mountLossPurpose()
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
 expect(calls.some(call=>call.url.includes(`/work/decisions/${lossKind}/`))).toBe(false)
 expect(screen.queryByLabelText(T.lossDecisionTime)).toBeNull()
})


// Component-only candidate harness: the two owner capabilities remain staged, with no production purpose mount.
const cpKinds=['counterparty.tax-identity-change.v1','counterparty.legal-name-change.v1'] as const
const cpProofPreview = (cp: typeof cpKinds[number]) => ({
 ...preview,kind:cp,rejectionSupported:true,references:[{role:'party',label:'CAR-000001',recordId:id,link:null}],
 ...(cp==='counterparty.tax-identity-change.v1'
  ? {counterpartyTaxIdentityChange:{partyId:id,changeRequestId:id,currentTaxIdentityNumber:'1234567890',proposedTaxIdentityNumber:'10000000146',proposedKind:'tckn',reason:'SENTETIK vergi düzeltme gerekçesi'}}
  : {counterpartyLegalNameChange:{partyId:id,changeRequestId:id,currentLegalName:'E'.repeat(250),proposedLegalName:'Y'.repeat(250),reason:'SENTETIK unvan değişikliği gerekçesi'}}),
})
function mountCounterpartyCandidate(cp:CounterpartyWorkDecisionKind){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:[{path:'cp-candidate/:approvalId',element:<WorkDecisionPage kind={cp}/>}]}],{initialEntries:[`/cp-candidate/${id}`]})
 render(<ShellProvider queryClient={new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})}><RouterProvider router={router}/></ShellProvider>)
 return router
}
const cpCases=cpKinds.flatMap(cp=>[true,false].map(approve=>({cp,approve})))
it.each(cpCases)('allows empty native optional note for $cp approve=$approve and retains four-field wire', async ({cp,approve}) => {
 const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(`/work/decisions/${cp}/${id}`)
  ?init?.method==='POST'?json({companyId:COMPANY_A,kind:cp,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'original'}):json(cpProofPreview(cp)):undefined})
 mountCounterpartyCandidate(cp)
 const note=await screen.findByLabelText(T.optionalNote)
 expect((note as HTMLTextAreaElement).maxLength).toBe(1000)
 expect(screen.getByText(T.optionalNoteHelp)).toBeTruthy()
 const button=screen.getByRole('button',{name:approve?T.approve:T.reject}) as HTMLButtonElement
 expect(button.disabled).toBe(false)
 fireEvent.click(button);await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(`/work/decisions/${cp}/`))
 expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'7',approve,reason:null,delegationId:null})
})
it.each(cpCases)('sends the full native optional note, trimming only edges for $cp approve=$approve', async ({cp,approve}) => {
 const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(`/work/decisions/${cp}/${id}`)
  ?init?.method==='POST'?json({companyId:COMPANY_A,kind:cp,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'original'}):json(cpProofPreview(cp)):undefined})
 mountCounterpartyCandidate(cp)
 const input=await screen.findByLabelText(T.optionalNote)
 const button=screen.getByRole('button',{name:approve?T.approve:T.reject}) as HTMLButtonElement
 fireEvent.change(input,{target:{value:'N'.repeat(1001)}});expect(button.disabled).toBe(true)
 const note='N'.repeat(1000)
 fireEvent.change(input,{target:{value:`  ${note}  `}});expect(button.disabled).toBe(false)
 fireEvent.click(button);await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(`/work/decisions/${cp}/`))
 expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'7',approve,reason:note,delegationId:null})
})
it.each(cpCases)('freezes the original native optional note/version/key across unknown remount for $cp approve=$approve', async ({cp,approve}) => {
 let posts=0,reads=0
 const calls=installFetch({capabilities:[cap],respond:(url,init)=>{
  if(!url.includes(`/work/decisions/${cp}/${id}`))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:cp,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json({...cpProofPreview(cp),sourceVersion:++reads===1?'7':'9',pending:reads===1})
 }})
 const baseFetch=globalThis.fetch
 vi.stubGlobal('fetch',vi.fn((input:RequestInfo|URL,init?:RequestInit)=>String(input).endsWith('/identity/me')
  ? Promise.resolve(json({userId:'99999999-9999-4999-8999-999999999999',displayName:'SENTETIK',email:null,allowedCompanyIds:[COMPANY_A],mustChangePassword:false,localCredentialVersion:null,authenticationSessionBinding:'SENTETIK-cp-session'}))
  : baseFetch(input,init)))
 mountCounterpartyCandidate(cp)
 const input=await screen.findByLabelText(T.optionalNote)
 const proof=cpProofPreview(cp)
 const values=cp==='counterparty.tax-identity-change.v1'
  ? [proof.counterpartyTaxIdentityChange!.currentTaxIdentityNumber,proof.counterpartyTaxIdentityChange!.proposedTaxIdentityNumber,proof.counterpartyTaxIdentityChange!.reason]
  : [proof.counterpartyLegalNameChange!.currentLegalName,proof.counterpartyLegalNameChange!.proposedLegalName,proof.counterpartyLegalNameChange!.reason]
 for(const value of values)expect(screen.getByText(value,{exact:false})).toBeTruthy()
 if(approve)fireEvent.change(input,{target:{value:'İnsan tarafından yazılan isteğe bağlı not'}})
 fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.unknown)
 const stored=[...Array(sessionStorage.length)].map((_,i)=>sessionStorage.getItem(sessionStorage.key(i)!)).join('')
 for(const value of values)expect(stored).not.toContain(value)
 const local=[...Array(localStorage.length)].map((_,i)=>localStorage.getItem(localStorage.key(i)!)).join('')
 for(const value of values)expect(local).not.toContain(value)
 cleanup();mountCounterpartyCandidate(cp);await screen.findByText(T.unknown)
 await screen.findByText(values[2]!)
 for(const value of values)expect(screen.getByText(value,{exact:false})).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(`/work/decisions/${cp}/`))
 expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body)
 expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect(sent[1]!.body).toEqual({expectedVersion:'7',approve,reason:approve?'İnsan tarafından yazılan isteğe bağlı not':null,delegationId:null})
})
it.each(cpKinds)('keeps staged %s on the existing legacy route without mounting the candidate', cp => {
 const route=matchRoutes(workRoutes,`/onaylarim/${cp}/${id}`)?.at(-1)?.route
 expect(route?.path).toBe('onaylarim/:kind/:approvalId')
 expect((route?.element as ReactElement<{children:ReactElement}>).props.children.type).toBe(ApprovalInboxDetailPage)
})
it.each(cpKinds.flatMap(cp=>[{companyId:COMPANY_B},{kind:'finance.cash-account.v1'},{approvalReasonRequired:true},{sourceVersion:'0'}].map(delta=>({cp,delta}))))('refuses foreign or malformed $cp preview $delta without a decision', async ({cp,delta}) => {
 const calls=installFetch({capabilities:[cap],respond:url=>url.includes(`/work/decisions/${cp}/${id}`)?json({...cpProofPreview(cp),...delta}):undefined})
 mountCounterpartyCandidate(cp)
 await waitFor(()=>expect(calls.some(call=>call.url.includes(`/work/decisions/${cp}/`))).toBe(true))
 expect(screen.queryByLabelText(T.optionalNote)).toBeNull()
 expect(calls.some(call=>call.method==='POST')).toBe(false)
})

it.each(cpKinds)('renders the complete owner proof for %s and refuses missing or cross-bound proof before decisions', async cp => {
 const value=cpProofPreview(cp)
 const field=cp==='counterparty.tax-identity-change.v1'?'counterpartyTaxIdentityChange':'counterpartyLegalNameChange'
 const other=cp==='counterparty.tax-identity-change.v1'?'counterpartyLegalNameChange':'counterpartyTaxIdentityChange'
 const proof=value[field]!
 for(const delta of [{[field]:null},{[field]:{...proof,changeRequestId:COMPANY_B}},
  {[field]:{...proof,partyId:COMPANY_B}},{[other]:proof}]){
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(`/work/decisions/${cp}/${id}`)?json({...value,...delta}):undefined})
  mountCounterpartyCandidate(cp)
  await waitFor(()=>expect(calls.some(call=>call.url.includes(`/work/decisions/${cp}/`))).toBe(true))
  expect(screen.queryByRole('region',{name:T.identityProof})).toBeNull()
  expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
  expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  expect(calls.some(call=>call.method==='POST')).toBe(false)
  cleanup()
 }
})
