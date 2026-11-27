import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { shellRoutes } from '../shell/router'
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
