import { CapabilityRoute } from '../shell/CapabilityRoute'
import { DISPATCH_NOTE_WORK_DECISION_CAPABILITY } from './workDecisionApi'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from '../shell/capabilities'
import { COUNTERPARTY_WORK_DECISION_CAPABILITY } from './workDecisionApi'
import type { ReactElement } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter, matchRoutes } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { shellRoutes } from '../shell/router'
import type { CounterpartyWorkDecisionKind, WorkDecisionPreview } from './workDecisionApi'
import { WorkDecisionPage } from './WorkDecisionPage'
import { workRoutes } from './workRoutes'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import { WORK_INBOX_TEXT as INBOX } from './workInboxText'
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


// Native purposes mount under the owner's exact independent capabilities; no browse capability is required.
const cpKinds=['counterparty.tax-identity-change.v1','counterparty.legal-name-change.v1'] as const
const cpProofPreview = (cp: typeof cpKinds[number]): WorkDecisionPreview => ({
 ...preview,kind:cp,rejectionSupported:true,references:[{role:'party',label:'CAR-000001',recordId:id,link:null}],
 ...(cp==='counterparty.tax-identity-change.v1'
  ? {counterpartyTaxIdentityChange:{partyId:id,changeRequestId:id,currentTaxIdentityNumber:'10000000147',proposedTaxIdentityNumber:'10000000146',proposedKind:'tckn' as const,reason:'SENTETIK vergi düzeltme gerekçesi'}}
  : {counterpartyLegalNameChange:{partyId:id,changeRequestId:id,currentLegalName:'E'.repeat(250),proposedLegalName:'Y'.repeat(250),reason:'SENTETIK unvan değişikliği gerekçesi'}}),
})
function mountCounterpartyCandidate(cp:CounterpartyWorkDecisionKind){ return mount(`/onaylarim/${cp}/${id}`,true) }

const cpCases=cpKinds.flatMap(cp=>[true,false].map(approve=>({cp,approve})))
it.each(cpCases)('allows empty native optional note for $cp approve=$approve and retains four-field wire', async ({cp,approve}) => {
 const calls=installFetch({capabilities:[COUNTERPARTY_WORK_DECISION_CAPABILITY[cp]],respond:(url,init)=>url.includes(`/work/decisions/${cp}/${id}`)
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
 const calls=installFetch({capabilities:[COUNTERPARTY_WORK_DECISION_CAPABILITY[cp]],respond:(url,init)=>url.includes(`/work/decisions/${cp}/${id}`)
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
 const calls=installFetch({capabilities:[COUNTERPARTY_WORK_DECISION_CAPABILITY[cp]],respond:(url,init)=>{
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
it.each(cpKinds)('mounts %s under its exact native capability and preserves Cari browse routes', cp => {
 const route=matchRoutes(workRoutes,`/onaylarim/${cp}/${id}`)?.at(-1)?.route
 expect(route).toBeDefined()
 expect(route!.path).toBe(`onaylarim/${cp}/:approvalId`)
 const element=route!.element as ReactElement<{capability:readonly string[];children:ReactElement}>
 expect(element.props.capability).toEqual([COUNTERPARTY_WORK_DECISION_CAPABILITY[cp]])
 expect(element.props.children.type).toBe(WorkDecisionPage)
 expect(CAPABILITY_ROUTE[COUNTERPARTY_WORK_DECISION_CAPABILITY[cp]]).toBe(`/onaylarim/${cp}/:approvalId`)
 expect(CAPABILITY_NAV_LABEL[COUNTERPARTY_WORK_DECISION_CAPABILITY[cp]]).toBe('')
 expect(CAPABILITY.cariTaxIdentityWorkDecision).toBe('cari.tax-identity-change.work-decision')
 expect(CAPABILITY.cariLegalNameWorkDecision).toBe('cari.legal-name-change.work-decision')
 for(const [path,capability] of [['/cari',CAPABILITY.cariPartiesList],[`/cari/${id}`,CAPABILITY.cariPartiesDetail]]){
  const native=matchRoutes(shellRoutes,path)?.at(-1)?.route
  expect(native).toBeDefined()
  expect((native!.element as ReactElement<{capability:string}>).props.capability).toBe(capability)
 }
})
it.each(cpKinds.flatMap(cp=>[{companyId:COMPANY_B},{kind:'finance.cash-account.v1'},{approvalReasonRequired:true},{sourceVersion:'0'}].map(delta=>({cp,delta}))))('refuses foreign or malformed $cp preview $delta without a decision', async ({cp,delta}) => {
 const calls=installFetch({capabilities:[COUNTERPARTY_WORK_DECISION_CAPABILITY[cp]],respond:url=>url.includes(`/work/decisions/${cp}/${id}`)?json({...cpProofPreview(cp),...delta}):undefined})
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
  const calls=installFetch({capabilities:[COUNTERPARTY_WORK_DECISION_CAPABILITY[cp]],respond:url=>url.includes(`/work/decisions/${cp}/${id}`)?json({...value,...delta}):undefined})
  mountCounterpartyCandidate(cp)
  await waitFor(()=>expect(calls.some(call=>call.url.includes(`/work/decisions/${cp}/`))).toBe(true))
  expect(screen.queryByRole('region',{name:T.identityProof})).toBeNull()
  expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
  expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  expect(calls.some(call=>call.method==='POST')).toBe(false)
  cleanup()
 }
})

it.each(cpKinds.flatMap(cp=>[[],['counterparty.'+(cp.includes('tax-identity')?'tax-identity-change':'legal-name-change')+'.work-decision'],
 [cp==='counterparty.tax-identity-change.v1'?'cari.legal-name-change.work-decision':'cari.tax-identity-change.work-decision'],
 [CAPABILITY.cariPartiesDetail]].map(capabilities=>({cp,capabilities}))))('never reads $cp for missing, guessed, other-kind, or browse-only capabilities $capabilities',async({cp,capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(`/work/decisions/${cp}/${id}`)?json(cpProofPreview(cp)):undefined})
 mountCounterpartyCandidate(cp)
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
 expect(calls.some(call=>call.url.includes(`/work/decisions/${cp}/`))).toBe(false)
 expect(screen.queryByRole('region',{name:T.identityProof})).toBeNull()
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
})
it.each(cpKinds)('does not grant native Cari browse through the standalone %s decision purpose',async cp=>{
 const calls=installFetch({capabilities:[COUNTERPARTY_WORK_DECISION_CAPABILITY[cp]],respond:url=>url.includes(`/work/decisions/${cp}/${id}`)?json(cpProofPreview(cp)):undefined})
 const router=mountCounterpartyCandidate(cp)
 await screen.findByLabelText(T.optionalNote)
 await act(()=>router.navigate('/cari'))
 await waitFor(()=>expect(calls.filter(call=>call.url.endsWith('/shell/capabilities')).length).toBeGreaterThan(1))
 expect(calls.some(call=>call.url.includes('/counterparties'))).toBe(false)
})


const excessKind='logistics.plan-excess.v1', excessCap='logistics.plan-excess.work-decision'
const excessInstruction='dddddddd-0000-4000-8000-000000000001',excessLine='eeeeeeee-0000-4000-8000-000000000001'
const excessProposal={instructionVersion:'9007199254740993',instructionId:excessInstruction,lineId:excessLine,quantity:'20.125',reason:'R'.repeat(500),proposedBy:'SENTETIK-maker',proposedAtUtc:'2026-11-27T08:00:00Z'}
const excessPreview={...preview,kind:excessKind,sourceVersion:'1',rejectionSupported:false,
 references:[{role:'loading-instruction',label:'SENTETIK talimat',recordId:excessInstruction,link:null}],logisticsPlanExcess:excessProposal}
const excessPath=`/work/decisions/${excessKind}/${id}`
const mountExcess=()=>mount(`/onaylarim/${excessKind}/${id}`,true)
it('shows the full native proposal and only approval, forwarding both versions without proposal metadata',async()=>{
 const calls=installFetch({capabilities:[excessCap],respond:(url,init)=>url.includes(excessPath)
  ?init?.method==='POST'?json({companyId:COMPANY_A,kind:excessKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'original'}):json(excessPreview):undefined})
 mountExcess()
 await screen.findByRole('heading',{name:T.planExcessProposal})
 for(const value of [excessInstruction,excessLine,excessProposal.quantity,excessProposal.reason,excessProposal.proposedBy,excessProposal.proposedAtUtc])expect(screen.getByText(value)).toBeTruthy()
 expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
 expect(screen.queryByRole('textbox')).toBeNull()
 expect(screen.getByText(T.planExcessApprovalOnly)).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(excessPath))
 expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null,logisticsPlanExcess:{expectedInstructionVersion:excessProposal.instructionVersion}})
 expect(sent[0]!.headers['X-Company-Id']).toBe(COMPANY_A)
 expect(calls.some(call=>call.url.includes('/logistics/plan-excess/'))).toBe(false)
})
it.each([true,false])('keeps both frozen versions/key after unknown remount with pending=%s, without journaling proposal text',async pending=>{
 let reads=0,posts=0
 const calls=installFetch({capabilities:[excessCap],respond:(url,init)=>{
  if(!url.includes(excessPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:excessKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(++reads===1?excessPreview:{...excessPreview,sourceVersion:'2',pending,logisticsPlanExcess:{...excessProposal,instructionVersion:'9007199254740995'}})
 }})
 const baseFetch=globalThis.fetch
 vi.stubGlobal('fetch',vi.fn((input:RequestInfo|URL,init?:RequestInit)=>String(input).endsWith('/identity/me')
  ?Promise.resolve(json({userId:'99999999-9999-4999-8999-999999999999',displayName:'SENTETIK',email:null,allowedCompanyIds:[COMPANY_A],mustChangePassword:false,localCredentialVersion:null,authenticationSessionBinding:'SENTETIK-excess-session'}))
  :baseFetch(input,init)))
 mountExcess();await screen.findByRole('heading',{name:T.planExcessProposal})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 const journal=Object.values(sessionStorage).join('')
 expect(journal).not.toContain(excessProposal.reason);expect(journal).not.toContain(excessProposal.proposedBy)
 expect(journal).not.toContain(excessInstruction);expect(journal).not.toContain(excessLine)
 cleanup();mountExcess();await screen.findByText(T.unknown);await screen.findByRole('heading',{name:T.planExcessProposal})
 expect(screen.getByText(`${T.version}: 1`)).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(excessPath))
 expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body)
 expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect(sent[1]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null,logisticsPlanExcess:{expectedInstructionVersion:excessProposal.instructionVersion}})
})
it.each([{companyId:COMPANY_B},{rejectionSupported:true},{logisticsPlanExcess:null},{logisticsPlanExcess:{...excessProposal,instructionId:id}},
 {logisticsPlanExcess:{...excessProposal,reason:''}},{logisticsPlanExcessProof:{reason:'PRIVATE'}}])('does not show a partial or foreign plan excess proposal %j',async delta=>{
 const calls=installFetch({capabilities:[excessCap],respond:url=>url.includes(excessPath)?json({...excessPreview,...delta}):undefined})
 mountExcess();await waitFor(()=>expect(calls.some(call=>call.url.includes(excessPath))).toBe(true))
 expect(screen.queryByRole('heading',{name:T.planExcessProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 expect(calls.some(call=>call.method==='POST')).toBe(false)
})
it.each([{capabilities:[]},{capabilities:['logistics.shipments.plan-excess.approve']},{capabilities:['logistics.instructions.detail']},{capabilities:['finance.cash-account.work-decision']}])('does not fetch a plan excess purpose without its exact capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(excessPath)?json(excessPreview):undefined})
 mountExcess();await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
 expect(calls.some(call=>call.url.includes(excessPath))).toBe(false)
 expect(screen.queryByRole('heading',{name:T.planExcessProposal})).toBeNull()
})
it('mounts the exact plan excess purpose capability while retaining both identity purposes',()=>{
 const route=matchRoutes(workRoutes,`/onaylarim/${excessKind}/${id}`)?.at(-1)?.route
 expect(route?.path).toBe('onaylarim/logistics.plan-excess.v1/:approvalId')
 const element=route?.element as ReactElement<{capability:readonly string[];children:ReactElement<{kind:string}>}>
 expect(element.props.capability).toEqual([excessCap]);expect(element.props.children.props.kind).toBe(excessKind)
 for(const cp of cpKinds)expect(matchRoutes(workRoutes,`/onaylarim/${cp}/${id}`)?.at(-1)?.route.path).toBe(`onaylarim/${cp}/:approvalId`)
})


const overageKind='inventory.transfer-overage.v1',overageCap='inventory.transfer-overage.work-decision'
const overageTransfer='dddddddd-0000-4000-8000-000000000021',overageLine='eeeeeeee-0000-4000-8000-000000000021'
const overageProposal={requestNumber:2,requestedAtUtc:'2026-11-27T08:00:00Z',transferId:overageTransfer,lineId:overageLine,kilograms:'20.125',pieceCount:0,
 reason:'SENTETIK fazla kabul talep gerekçesi',requestedBy:'SENTETIK-requester',receivedBy:'SENTETIK-receiver'}
const overagePreview={...preview,kind:overageKind,approvalReasonRequired:true,rejectionSupported:true,
 references:[{role:'inventory-transfer',label:'SENTETIK transfer',recordId:overageTransfer,link:null}],inventoryTransferOverage:overageProposal}
const overagePath=`/work/decisions/${overageKind}/${id}`
const mountOverage=()=>mount(`/onaylarim/${overageKind}/${id}`,true)
it.each([{approve:true,pieceCount:0},{approve:false,pieceCount:null}])('shows exact human overage context with pieces=$pieceCount and requires explicit time/reason approve=$approve',async({approve,pieceCount})=>{
 const calls=installFetch({capabilities:[overageCap],respond:(url,init)=>url.includes(overagePath)
  ?init?.method==='POST'?json({companyId:COMPANY_A,kind:overageKind,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'original'}):json({...overagePreview,inventoryTransferOverage:{...overageProposal,pieceCount}}):undefined})
 mountOverage();await screen.findByRole('heading',{name:T.overageProposal})
 for(const value of [overageTransfer,overageLine,overageProposal.kilograms,overageProposal.reason,overageProposal.requestedBy,overageProposal.receivedBy])expect(screen.getByText(value)).toBeTruthy()
 expect(screen.getByText(pieceCount===null?T.overageNoPieces:'0',{exact:true})).toBeTruthy()
 expect(screen.getByText(`${T.overageRequestNumber}: 2`)).toBeTruthy()
 expect(screen.getByText(`${T.lossRequestedAt}: ${overageProposal.requestedAtUtc}`)).toBeTruthy()
 const button=screen.getByRole('button',{name:approve?T.approve:T.reject}) as HTMLButtonElement
 const time=screen.getByLabelText(T.lossDecisionTime),reason=screen.getByLabelText(T.reason)
 expect(button.disabled).toBe(true)
 fireEvent.change(reason,{target:{value:'İnsan kararı'}});expect(button.disabled).toBe(true)
 fireEvent.change(time,{target:{value:'2026-11-26T12:30:00'}});expect(button.disabled).toBe(true)
 const local='2026-11-27T12:30:00'
 fireEvent.change(time,{target:{value:local}})
 fireEvent.change(reason,{target:{value:'R'.repeat(501)}});expect(button.disabled).toBe(true)
 for(const control of ['\n','\u0085','\u009f']){
  fireEvent.change(reason,{target:{value:`karar${control}gerekçesi`}});expect(button.disabled).toBe(true)
  fireEvent.click(button);expect(calls.some(call=>call.method==='POST'&&call.url.includes(overagePath))).toBe(false)
 }
 fireEvent.change(reason,{target:{value:'R'.repeat(500)}});expect(button.disabled).toBe(false)
 fireEvent.click(button);await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(overagePath))
 expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'7',approve,reason:'R'.repeat(500),delegationId:null,inventoryTransferOverage:{requestNumber:2,decidedAtUtc:new Date(local).toISOString()}})
 expect(sent[0]!.headers['X-Company-Id']).toBe(COMPANY_A)
})
it.each([{approve:true,pending:true},{approve:false,pending:false}])('keeps old numbered request/time/reason/key after an unknown overage result pending=$pending approve=$approve',async({approve,pending})=>{
 let reads=0,posts=0
 const calls=installFetch({capabilities:[overageCap],respond:(url,init)=>{
  if(!url.includes(overagePath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:overageKind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(++reads===1?overagePreview:{...overagePreview,sourceVersion:'9',pending,inventoryTransferOverage:{...overageProposal,requestNumber:3,requestedAtUtc:'2026-11-28T08:00:00Z',reason:'SENTETIK yeni talep'}})
 }})
 const baseFetch=globalThis.fetch
 vi.stubGlobal('fetch',vi.fn((input:RequestInfo|URL,init?:RequestInit)=>String(input).endsWith('/identity/me')
  ?Promise.resolve(json({userId:'99999999-9999-4999-8999-999999999999',displayName:'SENTETIK',email:null,allowedCompanyIds:[COMPANY_A],mustChangePassword:false,localCredentialVersion:null,authenticationSessionBinding:'SENTETIK-overage-session'}))
  :baseFetch(input,init)))
 mountOverage();await screen.findByRole('heading',{name:T.overageProposal})
 const local='2026-11-27T12:30:00'
 fireEvent.change(screen.getByLabelText(T.lossDecisionTime),{target:{value:local}})
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'İnsan karar gerekçesi'}})
 fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.unknown)
 const journal=Object.values(sessionStorage).join('')
 expect(journal).toContain('İnsan karar gerekçesi')
 for(const value of [overageProposal.reason,overageProposal.requestedBy,overageProposal.receivedBy,overageProposal.kilograms,overageTransfer,overageLine])expect(journal).not.toContain(value)
 cleanup();mountOverage();await screen.findByText(T.unknown);await screen.findByText(T.priorDecisionPending)
 expect(screen.queryByRole('heading',{name:T.overageProposal})).toBeNull()
 expect(reads).toBe(1)
 expect(screen.getByText(`${T.overageRequestNumber}: 2`)).toBeTruthy()
 expect(screen.getByText(`${T.lossFrozenTime}: ${new Date(local).toISOString()}`)).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(overagePath))
 expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body)
 expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect(sent[1]!.body).toEqual({expectedVersion:'7',approve,reason:'İnsan karar gerekçesi',delegationId:null,inventoryTransferOverage:{requestNumber:2,decidedAtUtc:new Date(local).toISOString()}})
})
it.each([{companyId:COMPANY_B},{inventoryTransferOverage:null},{inventoryTransferOverage:{...overageProposal,transferId:id}},
 {inventoryTransferOverage:{...overageProposal,pieceCount:undefined}},{inventoryTransferOverageProof:{}},{approvalReasonRequired:false}])('does not render partial or foreign overage proof %j',async delta=>{
 const calls=installFetch({capabilities:[overageCap],respond:url=>url.includes(overagePath)?json({...overagePreview,...delta}):undefined})
 mountOverage();await waitFor(()=>expect(calls.some(call=>call.url.includes(overagePath))).toBe(true))
 expect(screen.queryByRole('heading',{name:T.overageProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 expect(calls.some(call=>call.method==='POST')).toBe(false)
})
it.each([{capabilities:[]},{capabilities:['inventory.stock-transfer.overage-approve']},{capabilities:['inventory.transfer-loss.work-decision']},
 {capabilities:['inventory.transfers.read']}])('does not fetch overage without the exact purpose capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(overagePath)?json(overagePreview):undefined})
 mountOverage();await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
 expect(calls.some(call=>call.url.includes(overagePath))).toBe(false)
 expect(screen.queryByRole('heading',{name:T.overageProposal})).toBeNull()
})

it.each(['inventory.transfer-loss.v1','inventory.transfer-overage.v1'] as const)('restores only the old native command without reading a newer maker-blocked preview for %s',async receiptKind=>{
 const overage=receiptKind===overageKind,receiptCap=overage?overageCap:lossCap,receiptPath=`/work/decisions/${receiptKind}/${id}`
 let reads=0,posts=0
 const calls=installFetch({capabilities:[receiptCap],respond:(url,init)=>{
  if(!url.includes(receiptPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:receiptKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return ++reads===1?json(overage?overagePreview:lossPreview):problem(403)
 }})
 const baseFetch=globalThis.fetch
 vi.stubGlobal('fetch',vi.fn((input:RequestInfo|URL,init?:RequestInit)=>String(input).endsWith('/identity/me')
  ?Promise.resolve(json({userId:'99999999-9999-4999-8999-999999999999',displayName:'SENTETIK',email:null,allowedCompanyIds:[COMPANY_A],mustChangePassword:false,localCredentialVersion:null,authenticationSessionBinding:'SENTETIK-historical-receipt-session'}))
  :baseFetch(input,init)))
 mount(`/onaylarim/${receiptKind}/${id}`,true);await screen.findByLabelText(T.lossDecisionTime)
 fireEvent.change(screen.getByLabelText(T.lossDecisionTime),{target:{value:'2026-11-27T12:30:00'}})
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'İnsan ilk karar gerekçesi'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();mount(`/onaylarim/${receiptKind}/${id}`,true);await screen.findByText(T.priorDecisionPending)
 expect(reads).toBe(1);expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 expect(screen.queryByRole('button',{name:T.reject})).toBeNull();expect(screen.queryByLabelText(T.lossDecisionTime)).toBeNull()
 const capabilityReads=calls.filter(call=>call.url.endsWith('/shell/capabilities')).length
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 await act(async()=>{await Promise.resolve();await Promise.resolve()})
 expect(reads).toBe(1)
 expect(calls.filter(call=>call.url.endsWith('/shell/capabilities'))).toHaveLength(capabilityReads)
 expect(screen.getByText(T.done)).toBeTruthy();expect(screen.getByText(T.priorDecisionResolved)).toBeTruthy()
 expect(document.querySelector('[data-state="not-found"]')).toBeNull()
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(receiptPath))
 expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body)
 expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
})
it.each(['capability','session'] as const)('cannot restore or send an old overage command after a foreign %s boundary',async changed=>{
 let allowed=true,session='SENTETIK-bound-overage'
 const calls=installFetch({capabilities:[overageCap],respond:(url,init)=>{
  if(!url.includes(overagePath))return undefined
  return init?.method==='POST'?problem(503):json(overagePreview)
 }})
 const baseFetch=globalThis.fetch
 vi.stubGlobal('fetch',vi.fn((input:RequestInfo|URL,init?:RequestInit)=>{
  const url=String(input)
  if(url.endsWith('/identity/me'))return Promise.resolve(json({userId:'99999999-9999-4999-8999-999999999999',displayName:'SENTETIK',email:null,allowedCompanyIds:[COMPANY_A,COMPANY_B],mustChangePassword:false,localCredentialVersion:null,authenticationSessionBinding:session}))
  if(!allowed&&url.endsWith('/shell/capabilities'))return baseFetch(input,init).then(async response=>{
   const value=await response.json() as Record<string,unknown>
   return json({...value,capabilities:[]})
  })
  return baseFetch(input,init)
 }))
 mountOverage();await screen.findByLabelText(T.lossDecisionTime)
 fireEvent.change(screen.getByLabelText(T.lossDecisionTime),{target:{value:'2026-11-27T12:30:00'}})
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'İnsan karar gerekçesi'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup()
 if(changed==='capability')allowed=false
 if(changed==='session')session='SENTETIK-new-session'
 mountOverage()
 await waitFor(()=>expect(calls.filter(call=>call.url.endsWith('/shell/capabilities')).length).toBeGreaterThan(1))
 if(changed==='capability')expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
 if(changed==='session')expect(screen.queryByText(T.priorDecisionPending)).toBeNull()
 expect(calls.filter(call=>call.method==='POST'&&call.url.includes(overagePath))).toHaveLength(1)
})

it('keeps the unresolved overage command bound to its original company when the user switches company',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-company-bound-overage'})
 const calls=installFetch({capabilities:[overageCap],respond:(url,init)=>{
  if(!url.includes(overagePath))return undefined
  const company=(init?.headers as Record<string,string>)?.['X-Company-Id']??COMPANY_A
  return init?.method==='POST'?problem(503):json({...overagePreview,companyId:company})
 }})
 mountOverage();await screen.findByLabelText(T.lossDecisionTime)
 fireEvent.change(screen.getByLabelText(T.lossDecisionTime),{target:{value:'2026-11-27T12:30:00'}})
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'İnsan karar gerekçesi'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
 await waitFor(()=>expect(calls.some(call=>call.url.includes(overagePath)&&call.method==='GET'&&call.headers['X-Company-Id']===COMPANY_B)).toBe(true))
 expect(screen.queryByText(T.priorDecisionPending)).toBeNull()
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
 expect(calls.filter(call=>call.url.includes(overagePath)&&call.method==='POST')).toHaveLength(1)
 expect(calls.find(call=>call.url.includes(overagePath)&&call.method==='POST')?.headers['X-Company-Id']).toBe(COMPANY_A)
})
it('does not turn a fresh overage authority refusal into success or a new decision',async()=>{
 const calls=installFetch({capabilities:[overageCap],respond:(url,init)=>url.includes(overagePath)?init?.method==='POST'?problem(403):json(overagePreview):undefined})
 mountOverage();await screen.findByLabelText(T.lossDecisionTime)
 fireEvent.change(screen.getByLabelText(T.lossDecisionTime),{target:{value:'2026-11-27T12:30:00'}})
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'İnsan karar gerekçesi'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}))
 await waitFor(()=>expect(screen.queryByRole('button',{name:T.approve})).toBeNull())
 expect(screen.queryByText(T.done)).toBeNull()
 expect(calls.filter(call=>call.url.includes(overagePath)&&call.method==='POST')).toHaveLength(1)
})


const weighingKind='logistics.weighing-difference.v1',weighingCap='logistics.weighing-difference.work-decision'
const weighingNote='dddddddd-0000-4000-8000-000000000031',weighingInstruction='eeeeeeee-0000-4000-8000-000000000031',weighingProduct='ffffffff-0000-4000-8000-000000000031'
const weighingEvaluation={dispatchNoteId:weighingNote,instructionId:weighingInstruction,theoreticalKg:'100',scaleKg:'103.125',differenceKg:'3.125',bandLevel:'NotEvaluated',
 warningBandPercent:null,approvalBandPercent:null,blockBandPercent:null,totalExcessKg:'1.125',approvalRatePercent:null,decision:'ApprovalRequired',
 products:[{productId:weighingProduct,noteKg:'100',scaleKg:'103.125',differenceKg:'3.125',remainingPhysicalKg:'2',physicallyCoveredKg:'2',excessKg:'1.125',targetLineNo:1}],
 enteredBy:['SENTETIK-recorder','SENTETIK-scale-user'],enteredByComplete:true,recordedBy:'SENTETIK-recorder',recordedAtUtc:'2026-11-27T08:00:00Z'}
const weighingPreview={...preview,kind:weighingKind,sourceVersion:'1',rejectionSupported:false,
 references:[{role:'dispatch-note',label:'SENTETIK irsaliye',recordId:weighingNote,link:null},{role:'loading-instruction',label:'SENTETIK talimat',recordId:weighingInstruction,link:null}],logisticsWeighingDifference:weighingEvaluation}
const weighingPath=`/work/decisions/${weighingKind}/${id}`
const mountWeighing=()=>mount(`/onaylarim/${weighingKind}/${id}`,true)
it('shows the immutable server evaluation and product rows with no editable number/reason or rejection',async()=>{
 const calls=installFetch({capabilities:[weighingCap],respond:(url,init)=>url.includes(weighingPath)
  ?init?.method==='POST'?json({companyId:COMPANY_A,kind:weighingKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'original'}):json(weighingPreview):undefined})
 mountWeighing();await screen.findByRole('heading',{name:T.weighingEvaluation})
 expect(screen.getByRole('table',{name:T.weighingProducts})).toBeTruthy()
 for(const value of [weighingPreview.documentLabel,weighingInstruction,weighingProduct,weighingEvaluation.recordedAtUtc,T.weighingEnteredComplete,T.weighingReadOnly])expect(screen.getByText(value)).toBeTruthy()
 expect(screen.getAllByText(weighingEvaluation.scaleKg).length).toBeGreaterThan(0)
 expect(screen.getAllByText(weighingEvaluation.differenceKg).length).toBeGreaterThan(0)
 expect(screen.getAllByText(T.weighingUnavailable)).toHaveLength(4)
 expect(screen.queryByRole('textbox')).toBeNull();expect(screen.queryByRole('spinbutton')).toBeNull()
 expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(weighingPath))
 expect(sent).toHaveLength(1);expect(sent[0]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null})
 expect(sent[0]!.headers['X-Company-Id']).toBe(COMPANY_A)
})
it('keeps the original weighing approval version and key after a terminal version two preview on unknown remount',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-weighing-bound-session'})
 let reads=0,posts=0
 const calls=installFetch({capabilities:[weighingCap],respond:(url,init)=>{
  if(!url.includes(weighingPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:weighingKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(++reads===1?weighingPreview:{...weighingPreview,sourceVersion:'2',pending:false})
 }})
 mountWeighing();await screen.findByRole('heading',{name:T.weighingEvaluation})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 const journal=Object.values(sessionStorage).join('')
 for(const value of [weighingEvaluation.recordedBy,'SENTETIK-scale-user',weighingEvaluation.scaleKg,weighingEvaluation.totalExcessKg,weighingProduct,weighingNote,weighingInstruction])expect(journal).not.toContain(value)
 cleanup();mountWeighing();await screen.findByText(T.unknown);await screen.findByText(T.closed)
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST'&&call.url.includes(weighingPath))
 expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body)
 expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect(sent[1]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null})
})
it.each([{companyId:COMPANY_B},{sourceVersion:'2147483648'},{rejectionSupported:true},{logisticsWeighingDifference:null},
 {logisticsWeighingDifference:{...weighingEvaluation,dispatchNoteId:id}},{logisticsWeighingDifference:{...weighingEvaluation,enteredByComplete:false}},
 {logisticsWeighingDifference:{...weighingEvaluation,products:[]}},{logisticsWeighingDifferenceProof:{}}])('refuses incomplete or foreign weighing preview %j',async delta=>{
 const calls=installFetch({capabilities:[weighingCap],respond:url=>url.includes(weighingPath)?json({...weighingPreview,...delta}):undefined})
 mountWeighing();await waitFor(()=>expect(calls.some(call=>call.url.includes(weighingPath))).toBe(true))
 expect(screen.queryByRole('heading',{name:T.weighingEvaluation})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 expect(calls.some(call=>call.method==='POST')).toBe(false)
})
it.each([{capabilities:[]},{capabilities:['logistics.weighing-differences.approve']},{capabilities:['logistics.plan-excess.work-decision']},
 {capabilities:['logistics.instructions.detail']}])('does not read weighing records under a wrong or missing purpose capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(weighingPath)?json(weighingPreview):undefined})
 mountWeighing();await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
 expect(calls.some(call=>call.url.includes(weighingPath))).toBe(false)
 expect(screen.queryByRole('heading',{name:T.weighingEvaluation})).toBeNull()
})
it('does not restore a weighing approval in a different authenticated session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-weighing-old-session'})
 const calls=installFetch({capabilities:[weighingCap],respond:(url,init)=>url.includes(weighingPath)?init?.method==='POST'?problem(503):json(weighingPreview):undefined})
 mountWeighing();await screen.findByRole('heading',{name:T.weighingEvaluation})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-weighing-new-session'})
 mountWeighing();await screen.findByRole('heading',{name:T.weighingEvaluation})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
 expect(calls.filter(call=>call.url.includes(weighingPath)&&call.method==='POST')).toHaveLength(1)
})


const supplierKind='purchasing.supplier-return-cost-exception.v1',supplierCap='purchasing.supplier-return-cost-exception.work-decision'
const supplierOrder='dddddddd-0000-4000-8000-000000000041',supplierLine='eeeeeeee-0000-4000-8000-000000000041'
const supplierHuman={purchaseOrderId:supplierOrder,purchaseOrderLineId:supplierLine,goodsAcceptanceId:'ffffffff-0000-4000-8000-000000000041',inventoryMovementId:'aaaaaaaa-0000-4000-8000-000000000041',
 sourceBlock:'Free',quantityKg:'12.125',replacementExpected:true,reason:'SENTETIK kalıcı iade gerekçesi',occurredAtUtc:'2026-11-27T08:00:00Z',requestedBy:'SENTETIK-iade-giren',requestedAtUtc:'2026-11-27T09:00:00Z',
 recordedExpectedPurchaseOrderVersion:'4',currentPurchaseOrderVersion:'9',undoQuantity:'2',priceDisclosure:'Withheld'}
const supplierPreview={...preview,kind:supplierKind,documentLabel:'SENTETIK tedarikçi iadesi',sourceVersion:'1',rejectionSupported:true,
 references:[{role:'purchase-order',recordId:supplierOrder,label:'SENTETIK alış siparişi',link:null}],
 rules:[{ruleCode:'purchasing.supplier-return-manual-cost',threshold:null,actual:null,deviation:null,unit:null},{ruleCode:'purchasing.supplier-return-quantity',threshold:null,actual:'12.125',deviation:null,unit:'kg'}],supplierReturnCostException:supplierHuman}
const supplierPath=`/work/decisions/${supplierKind}/${id}`
const mountSupplier=()=>mount(`/onaylarim/${supplierKind}/${id}`,true)
it('shows the retained supplier return without a price grant, starts the measured PO selection empty and sends the human selected version separately',async()=>{
 const calls=installFetch({capabilities:[supplierCap],respond:(url,init)=>url.includes(supplierPath)?init?.method==='POST'
  ?json({companyId:COMPANY_A,kind:supplierKind,itemId:id,state:'Recorded',replayed:false,originalCorrelationId:'original'}):json(supplierPreview):undefined})
 mountSupplier();await screen.findByRole('heading',{name:T.supplierReturnProposal})
 for(const fact of [supplierHuman.purchaseOrderId,supplierHuman.purchaseOrderLineId,supplierHuman.goodsAcceptanceId,supplierHuman.inventoryMovementId,supplierHuman.reason,supplierHuman.requestedBy,supplierHuman.occurredAtUtc,supplierHuman.requestedAtUtc])expect(screen.getByText(fact)).toBeTruthy()
 expect(screen.getByText('Fiyat görme yetkiniz bulunmuyor.')).toBeTruthy()
 const input=screen.getByRole('textbox',{name:T.supplierReturnSelectedOrderVersion}) as HTMLInputElement
 expect(input.value).toBe('');expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)
 for(const value of ['0','09','9223372036854775807']){fireEvent.change(input,{target:{value}});expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)}
 expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.change(input,{target:{value:'9'}});fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'Onayda gönderilmeyecek'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(supplierPath)&&call.method==='POST')
 expect(sent).toHaveLength(1);expect(sent[0]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null,supplierReturnCostException:{expectedPurchaseOrderVersion:'9'}})
 expect(calls.some(call=>call.url.includes('/purchasing/')||call.url.includes('/identity/access'))).toBe(false)
})
it('shows an explicitly disclosed cost and approves an unmeasured return without inventing a PO version',async()=>{
 const data={...supplierPreview,rules:[{...supplierPreview.rules[0]!,actual:'3.125',unit:'TRY'},supplierPreview.rules[1]!],supplierReturnCostException:{...supplierHuman,replacementExpected:false,recordedExpectedPurchaseOrderVersion:null,currentPurchaseOrderVersion:null,undoQuantity:null,priceDisclosure:'Visible',unitCostTryPerKg:'3.125'}}
 const calls=installFetch({capabilities:[supplierCap],respond:(url,init)=>url.includes(supplierPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:supplierKind,itemId:id,state:'Recorded',replayed:false,originalCorrelationId:'original'}):json(data):undefined})
 mountSupplier();await screen.findByRole('heading',{name:T.supplierReturnProposal});expect(screen.getByText('3.125')).toBeTruthy()
 expect(screen.queryByRole('textbox',{name:T.supplierReturnSelectedOrderVersion})).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 expect(calls.find(call=>call.url.includes(supplierPath)&&call.method==='POST')?.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null})
})
it('rejects without a PO selection, requires a native bounded reason and preserves permitted line breaks',async()=>{
 const calls=installFetch({capabilities:[supplierCap],respond:(url,init)=>url.includes(supplierPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:supplierKind,itemId:id,state:'Rejected',replayed:false,originalCorrelationId:'original'}):json(supplierPreview):undefined})
 mountSupplier();await screen.findByRole('heading',{name:T.supplierReturnProposal})
 const reject=screen.getByRole('button',{name:T.reject}) as HTMLButtonElement,reason=screen.getByRole('textbox',{name:T.reason})
 for(const value of ['  ','Ret\u0085yasak']){fireEvent.change(reason,{target:{value}});expect(reject.disabled).toBe(true)}
 fireEvent.change(reason,{target:{value:'  Ret\nİkinci satır  '}});expect(reject.disabled).toBe(false)
 fireEvent.click(reject);await screen.findByText(T.done)
 expect(calls.find(call=>call.url.includes(supplierPath)&&call.method==='POST')?.body).toEqual({expectedVersion:'1',approve:false,reason:'Ret\nİkinci satır',delegationId:null})
})
it('freezes both supplier versions and original key across an unknown remount after current PO and terminal return versions change',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-supplier-session'})
 let reads=0,posts=0
 const calls=installFetch({capabilities:[supplierCap],respond:(url,init)=>{
  if(!url.includes(supplierPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:supplierKind,itemId:id,state:'Recorded',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(++reads===1?supplierPreview:{...supplierPreview,pending:false,sourceVersion:'2',supplierReturnCostException:{...supplierHuman,currentPurchaseOrderVersion:'10'}})
 }})
 mountSupplier();await screen.findByRole('heading',{name:T.supplierReturnProposal});fireEvent.change(screen.getByRole('textbox',{name:T.supplierReturnSelectedOrderVersion}),{target:{value:'9'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 const journal=Object.values(sessionStorage).join('')
 for(const value of [supplierHuman.reason,supplierHuman.requestedBy,supplierHuman.quantityKg,supplierOrder,supplierLine,supplierHuman.goodsAcceptanceId,supplierHuman.inventoryMovementId])expect(journal).not.toContain(value)
 cleanup();mountSupplier();await screen.findByText(T.unknown);await screen.findByText(T.closed)
 expect((screen.getByRole('textbox',{name:T.supplierReturnSelectedOrderVersion}) as HTMLInputElement).value).toBe('9')
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(supplierPath)&&call.method==='POST');expect(sent).toHaveLength(2)
 expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 expect(sent[1]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null,supplierReturnCostException:{expectedPurchaseOrderVersion:'9'}})
})
it.each([{companyId:COMPANY_B},{sourceVersion:'9223372036854775807'},{supplierReturnCostException:null},{supplierReturnCostExceptionProof:{}},
 {supplierReturnCostException:{...supplierHuman,purchaseOrderId:id}},{supplierReturnCostException:{...supplierHuman,unitCostTryPerKg:'3.125'}},{supplierReturnCostException:{...supplierHuman,currentPurchaseOrderVersion:null}},{supplierReturnCostException:{...supplierHuman,recordedExpectedPurchaseOrderVersion:null,currentPurchaseOrderVersion:null,undoQuantity:null}}])('does not render a foreign or malformed supplier proposal %j',async delta=>{
 const calls=installFetch({capabilities:[supplierCap],respond:url=>url.includes(supplierPath)?json({...supplierPreview,...delta}):undefined})
 mountSupplier();await waitFor(()=>expect(calls.some(call=>call.url.includes(supplierPath))).toBe(true))
 expect(screen.queryByRole('heading',{name:T.supplierReturnProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(call=>call.method==='POST')).toBe(false)
})
it.each([{capabilities:[]},{capabilities:['purchasing.orders.approve']},{capabilities:['purchasing.orders.detail']},{capabilities:['purchasing.goods-receipt-batch.work-decision']}])('refuses supplier reads without the exact purpose capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(supplierPath)?json(supplierPreview):undefined})
 mountSupplier();await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
 expect(calls.some(call=>call.url.includes(supplierPath))).toBe(false);expect(screen.queryByRole('heading',{name:T.supplierReturnProposal})).toBeNull()
})
it('does not restore a supplier frozen command under a different authenticated session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-supplier-session-old'})
 const calls=installFetch({capabilities:[supplierCap],respond:(url,init)=>url.includes(supplierPath)?init?.method==='POST'?problem(503):json(supplierPreview):undefined})
 mountSupplier();await screen.findByRole('heading',{name:T.supplierReturnProposal});fireEvent.change(screen.getByRole('textbox',{name:T.supplierReturnSelectedOrderVersion}),{target:{value:'9'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-supplier-session-new'});mountSupplier();await screen.findByRole('heading',{name:T.supplierReturnProposal})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.url.includes(supplierPath)&&call.method==='POST')).toHaveLength(1)
 expect((screen.getByRole('textbox',{name:T.supplierReturnSelectedOrderVersion}) as HTMLInputElement).value).toBe('')
})

it('preserves a native expired supplier decision refusal without displaying success or rewriting either version',async()=>{
 const calls=installFetch({capabilities:[supplierCap],respond:(url,init)=>url.includes(supplierPath)?init?.method==='POST'?problem(409):json(supplierPreview):undefined})
 mountSupplier();await screen.findByRole('heading',{name:T.supplierReturnProposal});fireEvent.change(screen.getByRole('textbox',{name:T.supplierReturnSelectedOrderVersion}),{target:{value:'9'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByRole('alert')
 expect(screen.queryByText(T.done)).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
 expect(calls.find(call=>call.url.includes(supplierPath)&&call.method==='POST')?.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null,supplierReturnCostException:{expectedPurchaseOrderVersion:'9'}})
})


const countKind='inventory.stock-count.v1',countCap='inventory.stock-count.work-decision'
const countLabel='dddddddd-0000-4000-8000-000000000051',countProduct='eeeeeeee-0000-4000-8000-000000000051',countLocation='ffffffff-0000-4000-8000-000000000051'
const countHuman={labelId:countLabel,labelVersion:'7',ownerKind:'Company',ownerId:COMPANY_A,custodianKind:'Company',custodianId:COMPANY_A,locationId:countLocation,productId:countProduct,
 block:'Blocked',bookKg:'10',countedKg:'0',differenceKg:'-10',countedAtUtc:'2026-11-27T08:00:00Z',reason:'SENTETIK kayıtlı sayım gerekçesi',recordedBy:'SENTETIK-sayan',recordedAtUtc:'2026-11-27T09:00:00Z',bookPieceCount:2,countedPieceCount:0}
const countRules=[{ruleCode:'inventory.stock-count-difference',threshold:null,actual:'0',deviation:'-10',unit:'kg'}]
const countPreview={...preview,kind:countKind,documentLabel:'SENTETIK sayım',sourceVersion:'1',approvalReasonRequired:true,rejectionSupported:true,rules:countRules,
 references:[{role:'inventory-label',label:'SENTETIK etiket',recordId:countLabel,link:null},{role:'product',label:'SENTETIK ürün',recordId:countProduct,link:null},{role:'location',label:'SENTETIK konum',recordId:countLocation,link:null}],inventoryStockCount:countHuman}
const countPath=`/work/decisions/${countKind}/${id}`
const mountCount=()=>mount(`/onaylarim/${countKind}/${id}`,true)
it.each([true,false])('shows all price-free immutable count facts and posts only the explicit mandatory decision reason, approve=%s',async approve=>{
 const calls=installFetch({capabilities:[countCap],respond:(url,init)=>url.includes(countPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:countKind,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'original'}):json(countPreview):undefined})
 mountCount();await screen.findByRole('heading',{name:T.countProposal})
 const region=screen.getByRole('region',{name:T.countProposal})
 expect(region.querySelectorAll('dt')).toHaveLength(18)
 for(const fact of [countLabel,countProduct,countLocation,countHuman.reason,countHuman.recordedBy,countHuman.countedAtUtc,countHuman.recordedAtUtc,T.countBlocks.Blocked])expect(region.textContent).toContain(fact)
 expect(screen.getAllByText('0').length).toBeGreaterThanOrEqual(2)
 expect(screen.queryByRole('spinbutton')).toBeNull();expect(screen.getAllByRole('textbox')).toHaveLength(1)
 expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true);expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
 const reason=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement
 expect(reason.maxLength).toBe(1000);fireEvent.change(reason,{target:{value:'x'.repeat(1001)}})
 expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.change(reason,{target:{value:'  Karar\nİkinci satır  '}});fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(countPath)&&call.method==='POST');expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'1',approve,reason:'Karar\nİkinci satır',delegationId:null});expect(sent[0]!.headers['X-Company-Id']).toBe(COMPANY_A)
 expect(calls.some(call=>call.url.includes('/inventory/labels/')||call.url.includes('/inventory/stock-counts/'))).toBe(false)
})
it('shows zero-book count evidence without invoking the legacy positive-book decoder or editing kg/pieces',async()=>{
 const zero={...countPreview,rules:[{...countRules[0]!,actual:'2',deviation:'2'}],inventoryStockCount:{...countHuman,bookKg:'0',countedKg:'2',differenceKg:'2',bookPieceCount:0,countedPieceCount:2}}
 installFetch({capabilities:[countCap],respond:url=>url.includes(countPath)?json(zero):undefined});mountCount();await screen.findByRole('heading',{name:T.countProposal})
 expect(screen.getByRole('region',{name:T.countProposal}).textContent).toContain(T.countReadOnly)
 expect(screen.getAllByText('0').length).toBeGreaterThanOrEqual(2);expect(screen.queryByRole('spinbutton')).toBeNull()
})
it('renders null piece tracking explicitly without turning it into zero',async()=>{
 installFetch({capabilities:[countCap],respond:url=>url.includes(countPath)?json({...countPreview,inventoryStockCount:{...countHuman,bookPieceCount:null,countedPieceCount:null}}):undefined})
 mountCount();await screen.findByRole('heading',{name:T.countProposal});expect(screen.getAllByText(T.countNoPieces)).toHaveLength(2)
})
it('reconciles the original count version-one command and key after a terminal version-two remount, keeping human facts out of the journal',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-count-session'});let reads=0,posts=0
 const calls=installFetch({capabilities:[countCap],respond:(url,init)=>{
  if(!url.includes(countPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:countKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(++reads===1?countPreview:{...countPreview,sourceVersion:'2',pending:false})
 }})
 mountCount();await screen.findByRole('heading',{name:T.countProposal});fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK karar notu'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 const journal=Object.values(sessionStorage).join('');for(const fact of [countLabel,countProduct,countLocation,countHuman.reason,countHuman.recordedBy,countHuman.countedAtUtc,countHuman.recordedAtUtc])expect(journal).not.toContain(fact)
 cleanup();mountCount();await screen.findByText(T.unknown);await screen.findByText(T.closed)
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull();fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(countPath)&&call.method==='POST');expect(sent).toHaveLength(2)
 expect(sent[0]!.body).toEqual(sent[1]!.body);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect(sent[1]!.body).toEqual({expectedVersion:'1',approve:true,reason:'SENTETIK karar notu',delegationId:null})
})
it.each([{companyId:COMPANY_B},{sourceVersion:'7'},{sourceVersion:'2'},{inventoryStockCount:null},{inventoryStockCountProof:{}},{sourceModule:'inventory'},
 {inventoryStockCount:{...countHuman,bookPieceCount:null}},{inventoryStockCount:{...countHuman,unitCost:'100'}},{inventoryStockCount:{...countHuman,labelId:id}},{rules:[{...countRules[0]!,unit:'TRY'}]}])('refuses foreign, incomplete or price-bearing count evidence %j',async delta=>{
 const calls=installFetch({capabilities:[countCap],respond:url=>url.includes(countPath)?json({...countPreview,...delta}):undefined});mountCount()
 await waitFor(()=>expect(calls.some(call=>call.url.includes(countPath))).toBe(true))
 expect(screen.queryByRole('heading',{name:T.countProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(call=>call.method==='POST')).toBe(false)
})
it.each([{capabilities:[]},{capabilities:['inventory.stock-count.approve']},{capabilities:['inventory.stock-positions.list']},{capabilities:['inventory.opening-cost.work-decision']}])('does not read count evidence without its exact purpose capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(countPath)?json(countPreview):undefined});mountCount()
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true));expect(calls.some(call=>call.url.includes(countPath))).toBe(false)
})
it('does not restore an old count command into another authenticated session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-count-old'});const calls=installFetch({capabilities:[countCap],respond:(url,init)=>url.includes(countPath)?init?.method==='POST'?problem(503):json(countPreview):undefined})
 mountCount();await screen.findByRole('heading',{name:T.countProposal});fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'Karar'}});fireEvent.click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.unknown)
 cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-count-new'});mountCount();await screen.findByRole('heading',{name:T.countProposal})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect((screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).value).toBe('')
 expect(calls.filter(call=>call.url.includes(countPath)&&call.method==='POST')).toHaveLength(1)
})


const bankKind='finance.bank-account.v1',bankCap='finance.bank-account.work-decision',bankCurrency='dddddddd-0000-4000-8000-000000000061'
const bankSafeAccount={id,companyId:COMPANY_A,currencyId:bankCurrency,displayName:'SENTETIK güvenli banka hesabı',isActive:false,isUsableForReceipt:false,version:'1'}
const bankHuman={account:bankSafeAccount,proposedBy:'SENTETIK-hesap-öneren',proposedAtUtc:'2026-11-27T08:00:00Z'}
const bankPreview={...preview,kind:bankKind,sourceVersion:'1',rejectionSupported:false,rules:[{ruleCode:'finance.bank-account-approval-required',threshold:null,actual:null,deviation:null,unit:null}],
 references:[{role:'bank-account',recordId:id,label:'SENTETIK hesap',link:null},{role:'currency',recordId:bankCurrency,label:'SENTETIK para birimi',link:null}],bankAccount:bankHuman}
const bankPath=`/work/decisions/${bankKind}/${id}`,mountBank=()=>mount(`/onaylarim/${bankKind}/${id}`,true)
it('shows the native safe account proposal without card/IBAN reads, editable values, a rejection or note',async()=>{
 const calls=installFetch({capabilities:[bankCap],respond:(url,init)=>url.includes(bankPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:bankKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'original'}):json(bankPreview):undefined})
 mountBank();await screen.findByRole('heading',{name:T.bankProposal});const region=screen.getByRole('region',{name:T.bankProposal})
 for(const fact of [bankSafeAccount.displayName,bankHuman.proposedBy,bankHuman.proposedAtUtc,T.bankCurrentVersion])expect(region.textContent).toContain(fact)
 expect(region.querySelectorAll('dt')).toHaveLength(9);expect(screen.getAllByText(T.bankNo)).toHaveLength(2)
 expect(screen.queryByRole('textbox')).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull();expect(screen.getByText(T.bankApprovalOnly)).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(bankPath)&&call.method==='POST');expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null})
 expect(calls.some(call=>call.url.includes('/finance/bank-accounts')||call.url.includes('/finance/currencies'))).toBe(false)
})
it('keeps the original bank approval version and key after a later IBAN/card revision while exposing no private proposal in the journal',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-bank-session'});let reads=0,posts=0
 const calls=installFetch({capabilities:[bankCap],respond:(url,init)=>{
  if(!url.includes(bankPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:bankKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(++reads===1?bankPreview:{...bankPreview,pending:false,sourceVersion:'4',bankAccount:{...bankHuman,account:{...bankSafeAccount,isActive:true,isUsableForReceipt:false,version:'4'}}})
 }})
 mountBank();await screen.findByRole('heading',{name:T.bankProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 const journal=Object.values(sessionStorage).join('');for(const fact of [bankSafeAccount.displayName,bankCurrency,bankHuman.proposedBy,bankHuman.proposedAtUtc])expect(journal).not.toContain(fact)
 cleanup();mountBank();await screen.findByText(T.unknown);await screen.findByText(T.closed)
 expect(screen.getByText(`${T.version}: 1`)).toBeTruthy();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(bankPath)&&call.method==='POST');expect(sent).toHaveLength(2)
 expect(sent[0]!.body).toEqual(sent[1]!.body);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect(sent[1]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null})
})
it.each([{companyId:COMPANY_B},{sourceVersion:'9223372036854775807'},{bankAccount:null},{bankAccountProof:{}},{rejectionSupported:true},
 {bankAccount:{...bankHuman,iban:'TR-SENTETIK'}},{bankAccount:{...bankHuman,account:{...bankSafeAccount,iban:'TR-SENTETIK'}}},{bankAccount:{...bankHuman,account:{...bankSafeAccount,bankName:'SENTETIK banka'}}},
 {bankAccount:{...bankHuman,account:{...bankSafeAccount,companyId:COMPANY_B}}},{bankAccount:{...bankHuman,account:{...bankSafeAccount,isUsableForReceipt:true}}},{bankAccount:{...bankHuman,proposedBy:''}}])('refuses bank scope drift, malformed proof and extra card disclosure %j',async delta=>{
 const calls=installFetch({capabilities:[bankCap],respond:url=>url.includes(bankPath)?json({...bankPreview,...delta}):undefined});mountBank()
 await waitFor(()=>expect(calls.some(call=>call.url.includes(bankPath))).toBe(true));expect(screen.queryByRole('heading',{name:T.bankProposal})).toBeNull()
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(call=>call.method==='POST')).toBe(false);expect(screen.queryByText('TR-SENTETIK')).toBeNull()
})
it.each([{capabilities:[]},{capabilities:['finance.bank-accounts.approve']},{capabilities:['finance.bank-accounts.list']},{capabilities:['finance.cash-account.work-decision']}])('makes no bank purpose read with missing or wrong capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(bankPath)?json(bankPreview):undefined});mountBank()
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true));expect(calls.some(call=>call.url.includes(bankPath))).toBe(false)
})
it('does not restore a bank approval from another authentication session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-bank-old'});const calls=installFetch({capabilities:[bankCap],respond:(url,init)=>url.includes(bankPath)?init?.method==='POST'?problem(503):json(bankPreview):undefined})
 mountBank();await screen.findByRole('heading',{name:T.bankProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-bank-new'});mountBank();await screen.findByRole('heading',{name:T.bankProposal})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.url.includes(bankPath)&&call.method==='POST')).toHaveLength(1)
})


const scrapKind='inventory.stock-scrap.v1',scrapCap='inventory.stock-scrap.work-decision'
const scrapHuman={labelId:countLabel,labelVersion:'7',ownerKind:'Company',ownerId:COMPANY_A,custodianKind:'Company',custodianId:COMPANY_A,locationId:countLocation,productId:countProduct,
 block:'Free',bookKg:'20.125',scrapKg:'12.125',remainingKg:'8',scrappedAtUtc:'2026-11-27T08:00:00Z',reason:'SENTETIK kayıtlı hurda gerekçesi',recordedBy:'SENTETIK-hurda-kaydeden',recordedAtUtc:'2026-11-27T09:00:00Z',bookPieceCount:2,scrapPieceCount:0}
const scrapRules=[{ruleCode:'inventory.stock-scrap-request',threshold:null,actual:'12.125',deviation:null,unit:'kg'}]
const scrapPreview={...preview,kind:scrapKind,documentLabel:'SENTETIK hurda',sourceVersion:'1',approvalReasonRequired:true,rejectionSupported:true,references:countPreview.references,rules:scrapRules,inventoryStockScrap:scrapHuman}
const scrapPath=`/work/decisions/${scrapKind}/${id}`,mountScrap=()=>mount(`/onaylarim/${scrapKind}/${id}`,true)
it.each([true,false])('shows the retained scrap, remaining kg and zero pieces without recalculation and submits the explicit decision, approve=%s',async approve=>{
 const calls=installFetch({capabilities:[scrapCap],respond:(url,init)=>url.includes(scrapPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:scrapKind,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'original'}):json(scrapPreview):undefined})
 mountScrap();await screen.findByRole('heading',{name:T.scrapProposal});const region=screen.getByRole('region',{name:T.scrapProposal})
 expect(region.querySelectorAll('dt')).toHaveLength(18)
 for(const fact of [countLabel,countProduct,countLocation,scrapHuman.bookKg,scrapHuman.remainingKg,scrapHuman.reason,scrapHuman.recordedBy,scrapHuman.scrappedAtUtc,T.scrapReadOnly,T.scrapLabelVersion])expect(region.textContent).toContain(fact)
 expect(screen.getByText('0')).toBeTruthy();expect(screen.queryByText(T.countNoPieces)).toBeNull();expect(screen.queryByRole('spinbutton')).toBeNull()
 const reason=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement;expect(reason.maxLength).toBe(1000)
 expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true);expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.change(reason,{target:{value:'x'.repeat(1001)}});expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)
 fireEvent.change(reason,{target:{value:'  Karar\nİkinci satır  '}});fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(scrapPath)&&call.method==='POST');expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'1',approve,reason:'Karar\nİkinci satır',delegationId:null})
 expect(calls.some(call=>call.url.includes('/inventory/labels/')||call.url.includes('/inventory/stock-scraps/'))).toBe(false)
})
it('shows null piece tracking separately from whole-scrap zero remaining kg',async()=>{
 installFetch({capabilities:[scrapCap],respond:url=>url.includes(scrapPath)?json({...scrapPreview,rules:[{...scrapRules[0]!,actual:'20.125'}],inventoryStockScrap:{...scrapHuman,scrapKg:'20.125',remainingKg:'0',bookPieceCount:null,scrapPieceCount:null}}):undefined})
 mountScrap();await screen.findByRole('heading',{name:T.scrapProposal});expect(screen.getAllByText(T.countNoPieces)).toHaveLength(2);expect(screen.getByText('0')).toBeTruthy()
})
it('reconciles the original scrap version-one command after terminal version two without persisting the human evidence',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-scrap-session'});let reads=0,posts=0
 const calls=installFetch({capabilities:[scrapCap],respond:(url,init)=>{
  if(!url.includes(scrapPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:scrapKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(++reads===1?scrapPreview:{...scrapPreview,sourceVersion:'2',pending:false})
 }})
 mountScrap();await screen.findByRole('heading',{name:T.scrapProposal});fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK karar notu'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 const journal=Object.values(sessionStorage).join('');for(const fact of [countLabel,countProduct,countLocation,scrapHuman.reason,scrapHuman.recordedBy,scrapHuman.scrappedAtUtc,scrapHuman.scrapKg,scrapHuman.bookKg])expect(journal).not.toContain(fact)
 cleanup();mountScrap();await screen.findByText(T.unknown);await screen.findByText(T.closed);expect(screen.getByText(`${T.version}: 1`)).toBeTruthy()
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull();fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(scrapPath)&&call.method==='POST');expect(sent).toHaveLength(2)
 expect(sent[0]!.body).toEqual(sent[1]!.body);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect(sent[1]!.body).toEqual({expectedVersion:'1',approve:true,reason:'SENTETIK karar notu',delegationId:null})
})
it.each([{companyId:COMPANY_B},{sourceVersion:'7'},{sourceVersion:'2'},{inventoryStockScrap:null},{inventoryStockScrapProof:{}},{inventoryStockCount:countHuman},
 {inventoryStockScrap:{...scrapHuman,scrapKg:'0'}},{inventoryStockScrap:{...scrapHuman,remainingKg:'-1'}},{inventoryStockScrap:{...scrapHuman,bookPieceCount:null}},
 {inventoryStockScrap:{...scrapHuman,unitCost:'100'}},{inventoryStockScrap:{...scrapHuman,labelId:id}},{rules:[{...scrapRules[0]!,unit:'TRY'}]}])('refuses a foreign, incomplete or price-bearing scrap preview %j',async delta=>{
 const calls=installFetch({capabilities:[scrapCap],respond:url=>url.includes(scrapPath)?json({...scrapPreview,...delta}):undefined});mountScrap()
 await waitFor(()=>expect(calls.some(call=>call.url.includes(scrapPath))).toBe(true));expect(screen.queryByRole('heading',{name:T.scrapProposal})).toBeNull()
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(call=>call.method==='POST')).toBe(false)
})
it.each([{capabilities:[]},{capabilities:['inventory.stock-scrap.approve']},{capabilities:['inventory.stock-count.work-decision']},{capabilities:['inventory.stock-positions.list']}])('does not read scrap evidence without its exact purpose capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(scrapPath)?json(scrapPreview):undefined});mountScrap()
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true));expect(calls.some(call=>call.url.includes(scrapPath))).toBe(false)
})
it('does not restore a scrap decision into another authenticated session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-scrap-old'});const calls=installFetch({capabilities:[scrapCap],respond:(url,init)=>url.includes(scrapPath)?init?.method==='POST'?problem(503):json(scrapPreview):undefined})
 mountScrap();await screen.findByRole('heading',{name:T.scrapProposal});fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'Karar'}});fireEvent.click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.unknown)
 cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-scrap-new'});mountScrap();await screen.findByRole('heading',{name:T.scrapProposal})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect((screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).value).toBe('')
 expect(calls.filter(call=>call.url.includes(scrapPath)&&call.method==='POST')).toHaveLength(1)
})


describe('B2 deterministic evidence-first approval card',()=>{
 it('shows owner rules and references before the human decision, preserving exact server numbers without an AI call',async()=>{
  const evidence={...preview,rules:[{ruleCode:'SENTETIK-unmapped-rule',threshold:'1000.25',actual:'0',deviation:'-1000.25',unit:'kg'}],
   references:[{role:'purchase-order',recordId:COMPANY_B,label:'SENTETIK dayanak',link:null}]}
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(evidence):undefined})
  mount();const rules=await screen.findByRole('region',{name:INBOX.rulesHeading})
  const references=screen.getByRole('region',{name:T.reference}),reason=screen.getByRole('textbox',{name:T.reason})
  expect(rules.compareDocumentPosition(references)&Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
  expect(references.compareDocumentPosition(reason)&Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
  expect(within(rules).getByText(INBOX.ruleUnknown,{exact:false})).toBeTruthy()
  expect(within(rules).getByText('SENTETIK-unmapped-rule')).toBeTruthy()
  expect(within(rules).getByText('1.000,25 kg')).toBeTruthy();expect(within(rules).getByText('0 kg')).toBeTruthy()
  expect(within(rules).getByText('-1.000,25 kg')).toBeTruthy()
  expect(within(references).getByText('SENTETIK dayanak',{exact:false})).toBeTruthy()
  expect(calls.some(call=>call.url.includes('/ai/')||call.method==='POST')).toBe(false)
  expect(screen.queryByText(/yapay zekâ özeti|öneriye katılmıyorum|güven yüzdesi|hesap izi|ekler ve geçmiş/i)).toBeNull()
 })
 it('reports absent rule results and references without inventing an explanation or recommendation',async()=>{
  installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(preview):undefined});mount()
  const rules=await screen.findByRole('region',{name:INBOX.rulesHeading})
  expect(within(rules).getByText(T.rulesUnavailable)).toBeTruthy();expect(rules.querySelector('dl')).toBeNull()
  expect(within(screen.getByRole('region',{name:T.reference})).getByText(INBOX.noReferences)).toBeTruthy()
  expect(screen.queryByRole('button',{name:/öneri|katılmıyorum/i})).toBeNull()
 })
 it.each(['count','scrap'] as const)('renders %s rules once before the immutable owner facts and references',async owner=>{
  const count=owner==='count',ownerPath=count?countPath:scrapPath,ownerPreview=count?countPreview:scrapPreview
  const calls=installFetch({capabilities:[count?countCap:scrapCap],respond:url=>url.includes(ownerPath)?json(ownerPreview):undefined})
  if(count)mountCount();else mountScrap()
  const proof=await screen.findByRole('region',{name:count?T.countProposal:T.scrapProposal})
  const rules=screen.getByRole('region',{name:INBOX.rulesHeading}),references=screen.getByRole('region',{name:T.reference})
  expect(screen.getAllByRole('heading',{name:INBOX.rulesHeading})).toHaveLength(1)
  expect(proof.querySelectorAll('dt')).toHaveLength(18);expect(within(proof).queryByText(INBOX.actual)).toBeNull()
  expect(rules.compareDocumentPosition(proof)&Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
  expect(proof.compareDocumentPosition(references)&Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
  expect(references.compareDocumentPosition(screen.getByRole('textbox',{name:T.reason}))&Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
  expect(within(rules).getByText(count?'0 kg':'12,125 kg')).toBeTruthy()
  if(count)expect(within(rules).getByText('-10 kg')).toBeTruthy()
  expect(calls.some(call=>call.url.includes('/ai/')||call.method==='POST')).toBe(false)
 })
 it('keeps supplier version selection after the readonly proof and references, with no implicit approval version',async()=>{
  installFetch({capabilities:[supplierCap],respond:url=>url.includes(supplierPath)?json(supplierPreview):undefined})
  mountSupplier();const proof=await screen.findByRole('region',{name:T.supplierReturnProposal})
  const references=screen.getByRole('region',{name:T.reference}),version=screen.getByRole('textbox',{name:T.supplierReturnSelectedOrderVersion}) as HTMLInputElement
  expect(proof.compareDocumentPosition(references)&Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
  expect(references.compareDocumentPosition(version)&Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
  expect(version.value).toBe('');expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)
 })
 it('keeps withheld opening cost hidden in both the common rule section and the owner evidence',async()=>{
  const openingKind='inventory.opening-cost.v1',openingPath=`/work/decisions/${openingKind}/${id}`
  installFetch({capabilities:['inventory.opening-cost.work-decision'],respond:url=>url.includes(openingPath)?json({...preview,kind:openingKind,
   sourceVersion:`submission/${id}`,approvalReasonRequired:true,rules:[{ruleCode:'inventory.opening-cost-unit-cost',threshold:null,actual:null,deviation:null,unit:null}]}):undefined})
  mount(`/onaylarim/${openingKind}/${id}`);await screen.findByText(T.hiddenCost)
  const rules=screen.getByRole('region',{name:INBOX.rulesHeading})
  expect(rules.querySelector('dl')).toBeNull();expect(screen.queryByText(T.cost,{exact:false})).toBeNull()
  expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true)
 })
})

const dispatchKind='logistics.dispatch-note.v1' as const, dispatchCap=DISPATCH_NOTE_WORK_DECISION_CAPABILITY
const dispatchPath=`/work/decisions/${dispatchKind}/${id}`
const dispatchLine={lineNo:1,shipmentId:'bbbbbbbb-0000-4000-8000-000000000001',instructionLineId:id,sourceOrderId:id,sourceOrderLineId:id,productId:id,unitId:id,quantity:'10.000'}
const dispatchProof={instructionId:COMPANY_A,dispatchNoteId:id,customerPartyId:COMPANY_A,companyAccountId:id,effectiveOn:'2026-11-27',status:'Draft',number:null,eDispatchNumber:null,createdBy:'SENTETIK irsaliye hazırlayan',createdAtUtc:'2026-11-27T08:00:00Z',lines:[dispatchLine]}
const dispatchPreview={...preview,kind:dispatchKind,rejectionSupported:false,logisticsDispatchNote:dispatchProof,references:[{role:'loading-instruction',label:'Talimat',recordId:COMPANY_A,link:'/lojistik/talimatlar/'+COMPANY_A}]}
// Candidate-only mount: production registry stays at 24 kinds while its backend capability is staged false.
function mountDispatchCandidate(){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:[{path:`onaylarim/${dispatchKind}/:approvalId`,element:<CapabilityRoute capability={[dispatchCap]}><WorkDecisionPage kind={dispatchKind}/></CapabilityRoute>}]}],{initialEntries:[`/onaylarim/${dispatchKind}/${id}`]})
 render(<ShellProvider queryClient={new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})}><RouterProvider router={router}/></ShellProvider>)
 return router
}
it('renders all dispatch header/line evidence and sends only native approve without reason or a copied proof',async()=>{
 const calls=installFetch({capabilities:[dispatchCap],respond:(url,init)=>url.includes(dispatchPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:dispatchKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK'}):json(dispatchPreview):undefined})
 mountDispatchCandidate();const region=await screen.findByRole('region',{name:T.dispatchProposal})
 expect(within(region).getByText('SENTETIK irsaliye hazırlayan')).toBeTruthy();expect(within(region).getByText('10.000')).toBeTruthy();expect(within(region).getByText(T.dispatchNoKg)).toBeTruthy()
 expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
 await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 expect(calls.filter(call=>call.url.includes(dispatchPath)&&call.method==='POST').map(call=>call.body)).toEqual([{expectedVersion:'7',approve:true,reason:null,delegationId:null}])
 expect(calls.some(call=>call.url.includes('/logistics/instructions/'))).toBe(false)
})
it('preserves all 501 dispatch lines and source-native optional kg without truncation or calculation',async()=>{
 const lines=Array.from({length:501},(_,i)=>({...dispatchLine,lineNo:i+1,shipmentId:`bbbbbbbb-0000-4000-8000-${(i+1).toString().padStart(12,'0')}`,...(i===500?{quantity:'999.125',quantityKg:'0.001'}:{})}))
 installFetch({capabilities:[dispatchCap],respond:url=>url.includes(dispatchPath)?json({...dispatchPreview,logisticsDispatchNote:{...dispatchProof,lines}}):undefined})
 mountDispatchCandidate();const region=await screen.findByRole('region',{name:T.dispatchProposal})
 expect(within(region).getAllByRole('row')).toHaveLength(502);expect(within(region).getByText('999.125')).toBeTruthy();expect(within(region).getByText('0.001')).toBeTruthy()
})
it('retries the original dispatch key/version/body after remount and a later cancelled preview without persisting human proof',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-dispatch-session'})
 let posts=0
 const calls=installFetch({capabilities:[dispatchCap],respond:(url,init)=>{
  if(!url.includes(dispatchPath))return undefined
  if(init?.method==='POST'){posts++;return posts===1?problem(503):json({companyId:COMPANY_A,kind:dispatchKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})}
  return json(posts===0?dispatchPreview:{...dispatchPreview,sourceVersion:'10',pending:false,logisticsDispatchNote:{...dispatchProof,status:'Cancelled',number:'IRS-1'}})
 }})
 mountDispatchCandidate();await screen.findByRole('region',{name:T.dispatchProposal});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 expect(JSON.stringify(sessionStorage)).not.toContain('SENTETIK irsaliye hazırlayan');expect(JSON.stringify(sessionStorage)).not.toContain('10.000')
 cleanup();mountDispatchCandidate();await screen.findByText(T.unknown);await screen.findByText(T.closed);expect(screen.getByText(`${T.version}: 7`)).toBeTruthy()
 await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(dispatchPath)&&call.method==='POST');expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
})
it.each([{capabilities:[]},{capabilities:['logistics.dispatch-notes.approve']},{capabilities:['logistics.instructions.detail']}])('keeps dispatch candidate reads closed without its exact capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(dispatchPath)?json(dispatchPreview):undefined});mountDispatchCandidate()
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true));expect(calls.some(call=>call.url.includes(dispatchPath))).toBe(false)
})
it('keeps the dispatch candidate out of the 24 live purposes and preserves the native legacy detail fallthrough',()=>{
 const route=matchRoutes(workRoutes,`/onaylarim/${dispatchKind}/${id}`)?.at(-1)?.route
 expect(route?.path).toBe('onaylarim/:kind/:approvalId')
 const element=route?.element as ReactElement<{children:ReactElement}>
 expect(element.props.children.type).not.toBe(WorkDecisionPage)
 expect(workRoutes.filter(candidate=>candidate.path?.startsWith('onaylarim/')&&candidate.path.endsWith('/:approvalId')&&candidate.path!=='onaylarim/:kind/:approvalId')).toHaveLength(24)
})
it.each([{companyId:COMPANY_B},{logisticsDispatchNote:{...dispatchProof,dispatchNoteId:COMPANY_A}},{rejectionSupported:true}])('does not expose malformed/foreign dispatch proof %j',async(delta)=>{
 installFetch({capabilities:[dispatchCap],respond:url=>url.includes(dispatchPath)?json({...dispatchPreview,...delta}):undefined});mountDispatchCandidate()
 await screen.findByText(T.title,{selector:'h1'});await waitFor(()=>expect(screen.queryByRole('region',{name:T.dispatchProposal})).toBeNull());expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
})

it('does not revive a dispatch unknown command across a new authentication session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-dispatch-old'})
 const calls=installFetch({capabilities:[dispatchCap],respond:(url,init)=>url.includes(dispatchPath)?init?.method==='POST'?problem(503):json(dispatchPreview):undefined})
 mountDispatchCandidate();await screen.findByRole('region',{name:T.dispatchProposal});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-dispatch-new'});mountDispatchCandidate();await screen.findByRole('region',{name:T.dispatchProposal})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.url.includes(dispatchPath)&&call.method==='POST')).toHaveLength(1)
})
it('keeps a native dispatch 403 refusal separate from success without retrying a mutation',async()=>{
 const calls=installFetch({capabilities:[dispatchCap],respond:(url,init)=>url.includes(dispatchPath)?init?.method==='POST'?problem(403):json(dispatchPreview):undefined})
 mountDispatchCandidate();await screen.findByRole('region',{name:T.dispatchProposal});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
 // Fresh 403 invalidates the current authority and the guarded route becomes not-found.
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByText(T.done)).toBeNull();expect(calls.filter(call=>call.url.includes(dispatchPath)&&call.method==='POST')).toHaveLength(1)
})
