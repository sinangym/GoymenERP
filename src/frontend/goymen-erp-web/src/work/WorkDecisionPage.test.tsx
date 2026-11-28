import { JOURNAL_REVERSAL_WORK_KIND as journalReversalKind, JOURNAL_REVERSAL_WORK_CAPABILITY as journalReversalCap } from './workJournalReversalDecision'
import { journalReversalFixture } from './journalReversalDecisionFixture'
import { accountingOpeningFixture, accountingReopeningFixture, openingPeriodKind, reopeningPeriodKind } from './accountingPeriodDecisionFixture'
import { ACCOUNTING_PERIOD_WORK_CAPABILITY, type AccountingPeriodWorkKind } from './workAccountingPeriodDecision'
import { normalJournalFixture } from './normalJournalDecisionFixture'
import { NORMAL_JOURNAL_WORK_KIND as journalKind, NORMAL_JOURNAL_WORK_CAPABILITY as journalCap } from './workNormalJournalDecision'
import { NORMAL_JOURNAL_TEXT as JOURNAL } from '../accounting/accountingText'
import { OFFICIAL_SALES_INVOICE_TEXT as OFFICIAL } from '../finance/officialSalesInvoiceText'
import { priceKind, priceCap, priceHuman } from './priceListDecisionFixture'
import { PRICE_LIST_TEXT as PRICE } from '../product/priceLists/priceListText'
import { AI_PANEL_CAPABILITY } from '../ai/aiCapabilities'
import { AI_FEEDBACK_TEXT as FEEDBACK } from '../ai/aiFeedbackText'
import { WORK_SALES_SUMMARY_TEXT as SUMMARY } from './workSalesSummaryText'
import { customerReturnText as CUSTOMER_RETURN } from '../logistics/customerReturnText'
import { CUSTOMER_RETURN_WORK_DECISION_CAPABILITY } from './workDecisionApi'
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
function mount(route=`/onaylarim/${kind}/${id}`, fullShell=false, client?: QueryClient){
 const router=createMemoryRouter(fullShell?shellRoutes:[{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[route]})
 render(<ShellProvider queryClient={client ?? new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})}><RouterProvider router={router}/></ShellProvider>)
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
it.each([
 {decisionKind:overageKind,capability:overageCap,module:'inventory',permission:'inventory.stock-transfer.overage-approve',human:overagePreview,heading:T.overageProposal,ruleCode:'inventory.transfer-overage-received'},
 {decisionKind:excessKind,capability:excessCap,module:'logistics',permission:'logistics.shipments.plan-excess.approve',human:excessPreview,heading:T.planExcessProposal,ruleCode:'logistics.plan-excess-quantity'},
])('opens the real inbox and follows $decisionKind with only its standalone purpose capability',async({decisionKind,capability,module,permission,human,heading,ruleCode})=>{
 const decisionPath=`/work/decisions/${decisionKind}/${id}`,detailPath=`/onaylarim/${decisionKind}/${id}`
 const ruleVersion=`work.approval-rule:${decisionKind}:0`
 const calls=installFetch({capabilities:[capability],respond:(url,init)=>{
  if(url.includes('/work/inbox/events')){
   const event=JSON.parse(String(init?.body)) as {clientEventId:string}
   return json({clientEventId:event.clientEventId,replayed:false})
  }
  if(url.includes('/work/inbox'))return json({companyId:COMPANY_A,view:'awaiting-my-decision',generatedAtUtc:'2026-11-27T08:00:00Z',
   items:[{module,kind:decisionKind,itemId:id,documentLabel:human.documentLabel,link:detailPath,waitingSinceUtc:'2026-11-27T08:00:00Z',awaitingPermission:permission,
    duration:{class:'Hours',state:'Undefined',limit:null,dueAtUtc:null,isOverdue:null,policyVersion:null,holidayCalendar:null},ruleVersion,
    explanation:{rules:[{ruleCode,threshold:null,actual:null,deviation:null,unit:null}],references:human.references}}],
   sources:[{module,kind:decisionKind,awaitingPermission:permission,durationClass:'Hours',ruleVersion,state:'Ready',hasMore:false}]})
  return url.includes(decisionPath)?json(human):undefined
 }})
 const router=mount('/onaylarim/kuyruk',true)
 await screen.findByTestId('work-inbox-list')
 const card=screen.getByTestId('work-inbox-item'),link=within(card).getByRole('link')
 expect(card.getAttribute('data-kind')).toBe(decisionKind);expect(link.getAttribute('href')).toBe(detailPath)
 const inboxReads=calls.filter(call=>call.url.includes('/work/inbox?'))
 expect(inboxReads).toHaveLength(1);expect(inboxReads[0]!.headers['X-Company-Id']).toBe(COMPANY_A)
 expect(calls.some(call=>call.url.includes(decisionPath))).toBe(false)
 await userEvent.setup().click(link)
 await screen.findByRole('heading',{name:heading})
 expect(router.state.location.pathname).toBe(detailPath)
 const detailReads=calls.filter(call=>call.url.includes(decisionPath))
 expect(detailReads).toHaveLength(1);expect(detailReads[0]!.method).toBe('GET');expect(detailReads[0]!.headers['X-Company-Id']).toBe(COMPANY_A)
 expect(calls.some(call=>call.url.includes('/work/queue')||call.url.includes('/inventory/')||call.url.includes('/logistics/'))).toBe(false)
})
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
// Published production route: use the actual shared route/capability guard rather than a test-only mount.
function mountDispatchPurpose(){ return mount(`/onaylarim/${dispatchKind}/${id}`,true) }
it('renders all dispatch header/line evidence and sends only native approve without reason or a copied proof',async()=>{
 const calls=installFetch({capabilities:[dispatchCap],respond:(url,init)=>url.includes(dispatchPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:dispatchKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK'}):json(dispatchPreview):undefined})
 mountDispatchPurpose();const region=await screen.findByRole('region',{name:T.dispatchProposal})
 expect(within(region).getByText('SENTETIK irsaliye hazırlayan')).toBeTruthy();expect(within(region).getByText('10.000')).toBeTruthy();expect(within(region).getByText(T.dispatchNoKg)).toBeTruthy()
 expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
 await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 expect(calls.filter(call=>call.url.includes(dispatchPath)&&call.method==='POST').map(call=>call.body)).toEqual([{expectedVersion:'7',approve:true,reason:null,delegationId:null}])
 expect(calls.some(call=>call.url.includes('/logistics/instructions/'))).toBe(false)
})
it('preserves all 501 dispatch lines and source-native optional kg without truncation or calculation',async()=>{
 const lines=Array.from({length:501},(_,i)=>({...dispatchLine,lineNo:i+1,shipmentId:`bbbbbbbb-0000-4000-8000-${(i+1).toString().padStart(12,'0')}`,...(i===500?{quantity:'999.125',quantityKg:'0.001'}:{})}))
 installFetch({capabilities:[dispatchCap],respond:url=>url.includes(dispatchPath)?json({...dispatchPreview,logisticsDispatchNote:{...dispatchProof,lines}}):undefined})
 mountDispatchPurpose();const region=await screen.findByRole('region',{name:T.dispatchProposal})
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
 mountDispatchPurpose();await screen.findByRole('region',{name:T.dispatchProposal});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 expect(JSON.stringify(sessionStorage)).not.toContain('SENTETIK irsaliye hazırlayan');expect(JSON.stringify(sessionStorage)).not.toContain('10.000')
 cleanup();mountDispatchPurpose();await screen.findByText(T.unknown);await screen.findByText(T.closed);expect(screen.getByText(`${T.version}: 7`)).toBeTruthy()
 await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(dispatchPath)&&call.method==='POST');expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
})
it.each([{capabilities:[]},{capabilities:['logistics.dispatch-notes.approve']},{capabilities:['logistics.instructions.detail']}])('keeps dispatch purpose reads closed without its exact capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(dispatchPath)?json(dispatchPreview):undefined});mountDispatchPurpose()
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true));expect(calls.some(call=>call.url.includes(dispatchPath))).toBe(false)
})
it('mounts dispatch as the 25th native purpose under its exact standalone capability',()=>{
 const route=matchRoutes(workRoutes,`/onaylarim/${dispatchKind}/${id}`)?.at(-1)?.route
 expect(route?.path).toBe(`onaylarim/${dispatchKind}/:approvalId`)
 const element=route?.element as ReactElement<{capability:readonly string[];children:ReactElement<{kind:string}>}>
 expect(element.props.capability).toEqual([dispatchCap]);expect(element.props.children.type).toBe(WorkDecisionPage);expect(element.props.children.props.kind).toBe(dispatchKind)
 expect(workRoutes.filter(candidate=>candidate.path?.startsWith('onaylarim/')&&candidate.path.endsWith('/:approvalId')&&candidate.path!=='onaylarim/:kind/:approvalId')).toHaveLength(59)
})
it.each([{companyId:COMPANY_B},{logisticsDispatchNote:{...dispatchProof,dispatchNoteId:COMPANY_A}},{rejectionSupported:true}])('does not expose malformed/foreign dispatch proof %j',async(delta)=>{
 installFetch({capabilities:[dispatchCap],respond:url=>url.includes(dispatchPath)?json({...dispatchPreview,...delta}):undefined});mountDispatchPurpose()
 await screen.findByText(T.title,{selector:'h1'});await waitFor(()=>expect(screen.queryByRole('region',{name:T.dispatchProposal})).toBeNull());expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
})

it('does not revive a dispatch unknown command across a new authentication session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-dispatch-old'})
 const calls=installFetch({capabilities:[dispatchCap],respond:(url,init)=>url.includes(dispatchPath)?init?.method==='POST'?problem(503):json(dispatchPreview):undefined})
 mountDispatchPurpose();await screen.findByRole('region',{name:T.dispatchProposal});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-dispatch-new'});mountDispatchPurpose();await screen.findByRole('region',{name:T.dispatchProposal})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.url.includes(dispatchPath)&&call.method==='POST')).toHaveLength(1)
})
it('keeps a native dispatch 403 refusal separate from success without retrying a mutation',async()=>{
 const calls=installFetch({capabilities:[dispatchCap],respond:(url,init)=>url.includes(dispatchPath)?init?.method==='POST'?problem(403):json(dispatchPreview):undefined})
 mountDispatchPurpose();await screen.findByRole('region',{name:T.dispatchProposal});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
 // Fresh 403 invalidates the current authority and the guarded route becomes not-found.
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByText(T.done)).toBeNull();expect(calls.filter(call=>call.url.includes(dispatchPath)&&call.method==='POST')).toHaveLength(1)
})

const customerKind='logistics.customer-return.v1' as const,customerCap=CUSTOMER_RETURN_WORK_DECISION_CAPABILITY,customerPath=`/work/decisions/${customerKind}/${id}`
const customerFacts={loadingInstructionId:COMPANY_A,loadingInstructionLineId:id,sourceDeliveryId:id,sourceShipmentId:id,sourceOrderId:COMPANY_A,sourceOrderLineId:id,unitId:id,
 quantityUnit:'Kilogram',quantity:'1.250',sourceDeliveredQuantity:'100.000',reason:'SENTETIK privatehuman iade nedeni',requestedBy:'SENTETIK iade hazırlayan',occurredAtUtc:'0001-01-01T00:00:00+00:00',requestedAtUtc:'2026-11-27T08:00:00Z',status:'Pending'}
const customerPreview={...preview,kind:customerKind,sourceVersion:'1',rejectionSupported:true,approvalSupported:true,logisticsCustomerReturn:customerFacts,
 references:[{role:'loading-instruction',label:'Talimat',recordId:COMPANY_A,link:'/lojistik/yukleme-talimatlari/'+COMPANY_A}],rules:[{ruleCode:'logistics.customer-return-request',threshold:'100',actual:'1.25',deviation:null,unit:'kg'}]}
function mountCustomerCandidate(){return mount(`/onaylarim/${customerKind}/${id}`)}
it.each([true,false])('shows all 15 native return facts and submits an optional unbounded trimmed note; approve=%s',async approve=>{
 const calls=installFetch({capabilities:[customerCap],respond:(url,init)=>url.includes(customerPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:customerKind,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'SENTETIK'}):json(customerPreview):undefined})
 mountCustomerCandidate();const region=await screen.findByRole('region',{name:T.customerReturnProposal})
 expect(region.querySelectorAll('dt')).toHaveLength(15);expect(within(region).getByText(customerFacts.reason)).toBeTruthy();expect(within(region).getByText(customerFacts.occurredAtUtc)).toBeTruthy();expect(within(region).getByText('1.250')).toBeTruthy();expect(within(region).getByText('100.000')).toBeTruthy()
 const note=screen.getByRole('textbox',{name:CUSTOMER_RETURN.note});expect(note.hasAttribute('maxlength')).toBe(false)
 fireEvent.change(note,{target:{value:'  '+ 'x'.repeat(5001)+'  '}});fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(customerPath)&&call.method==='POST');expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toMatchObject({expectedVersion:'1',approve,reason:'x'.repeat(5001),delegationId:null})
 const body=sent[0]!.body as {logisticsCustomerReturn?:{inventoryMovementId:string}}
 if(approve)expect(body.logisticsCustomerReturn?.inventoryMovementId).toMatch(/^[0-9a-f-]{36}$/i);else expect(Object.hasOwn(body,'logisticsCustomerReturn')).toBe(false)
 expect(calls.some(call=>call.url.includes('/logistics/customer-returns/'))).toBe(false)
})
it('keeps Piece approval disabled while an empty-note rejection remains native and omits movement selection',async()=>{
 const value={...customerPreview,approvalSupported:false,logisticsCustomerReturn:{...customerFacts,quantityUnit:'Piece',quantity:'1.000',sourceDeliveredQuantity:'2.000'},rules:[{...customerPreview.rules[0],actual:'1',threshold:'2',unit:'adet'}]}
 const calls=installFetch({capabilities:[customerCap],respond:(url,init)=>url.includes(customerPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:customerKind,itemId:id,state:'Rejected',replayed:false,originalCorrelationId:'SENTETIK'}):json(value):undefined})
 mountCustomerCandidate();await screen.findByRole('region',{name:T.customerReturnProposal});expect(screen.getByText(T.customerReturnPieceUnavailable)).toBeTruthy()
 expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(true);expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(false)
 fireEvent.click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.done)
 expect(calls.filter(call=>call.url.includes(customerPath)&&call.method==='POST')[0]!.body).toEqual({expectedVersion:'1',approve:false,reason:null,delegationId:null})
})
it.each([true,false])('freezes the original return selection/body/key through unknown remount and terminal version2, approve=%s',async approve=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-return-session'});let posts=0
 const calls=installFetch({capabilities:[customerCap],respond:(url,init)=>{
  if(!url.includes(customerPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:customerKind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(posts===0?customerPreview:{...customerPreview,pending:false,sourceVersion:'2',logisticsCustomerReturn:{...customerFacts,status:approve?'Approved':'Rejected'}})
 }})
 mountCustomerCandidate();await screen.findByRole('region',{name:T.customerReturnProposal});fireEvent.change(screen.getByRole('textbox',{name:CUSTOMER_RETURN.note}),{target:{value:'SENTETIK karar notu'}});fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.unknown)
 expect(JSON.stringify(sessionStorage)).not.toContain(customerFacts.reason);expect(JSON.stringify(sessionStorage)).not.toContain('loadingInstructionId')
 cleanup();mountCustomerCandidate();await screen.findByText(T.unknown);await screen.findByText(T.closed);expect(screen.getByText(`${T.version}: 1`)).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(customerPath)&&call.method==='POST');expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 const body=sent[0]!.body as Record<string,unknown>;expect(Object.keys(body).sort()).toEqual((approve?['approve','delegationId','expectedVersion','logisticsCustomerReturn','reason']:['approve','delegationId','expectedVersion','reason']).sort())
})
it.each([{companyId:COMPANY_B},{approvalSupported:undefined},{approvalSupported:false},{logisticsCustomerReturn:{...customerFacts,unitId:''}},{logisticsCustomerReturn:{...customerFacts,quantity:'1.250\n'}}])('does not expose malformed customer return evidence %j',async delta=>{
 const calls=installFetch({capabilities:[customerCap],respond:url=>url.includes(customerPath)?json({...customerPreview,...delta}):undefined});mountCustomerCandidate()
 await waitFor(()=>expect(calls.some(call=>call.url.includes(customerPath))).toBe(true));expect(screen.queryByRole('region',{name:T.customerReturnProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
})
it.each([{capabilities:[]},{capabilities:['logistics.customer-returns.approve']},{capabilities:['logistics.customer-returns.decision']},{capabilities:['logistics.instructions.detail']}])('never reads a return purpose through native controls/browse or missing capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(customerPath)?json(customerPreview):undefined});mountCustomerCandidate()
 await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true));expect(calls.some(call=>call.url.includes(customerPath))).toBe(false)
})
it('mounts the customer return purpose under its exact standalone capability among 26 decisions',()=>{
 const route=matchRoutes(workRoutes,`/onaylarim/${customerKind}/${id}`)?.at(-1)?.route
 expect(route?.path).toBe(`onaylarim/${customerKind}/:approvalId`)
 expect(route).toBeDefined()
 expect((route!.element as ReactElement<{capability:readonly string[]}>).props.capability).toEqual([customerCap])
 expect(workRoutes.filter(candidate=>candidate.path?.startsWith('onaylarim/')&&candidate.path.endsWith('/:approvalId')&&candidate.path!=='onaylarim/:kind/:approvalId')).toHaveLength(59)
})
it('does not restore a customer-return unknown attempt under another authentication session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-return-old'})
 const calls=installFetch({capabilities:[customerCap],respond:(url,init)=>url.includes(customerPath)?init?.method==='POST'?problem(503):json(customerPreview):undefined})
 mountCustomerCandidate();await screen.findByRole('region',{name:T.customerReturnProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-return-new'});mountCustomerCandidate();await screen.findByRole('region',{name:T.customerReturnProposal})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.url.includes(customerPath)&&call.method==='POST')).toHaveLength(1)
})

const retainedReturnWeighing={weighingId:id,direction:'TareFirst',firstWeighingKg:'10.125',secondWeighingKg:'14.25',netKg:'4.125',recordedBy:'SENTETIK kaydedilmiş tartımcı',recordedAtUtc:'2026-11-27T10:00:00Z'}
const retainedPieceReturn={...customerPreview,sourceVersion:'2',logisticsCustomerReturn:{...customerFacts,quantityUnit:'Piece',quantity:'1.000',sourceDeliveredQuantity:'2.000',weighing:retainedReturnWeighing},rules:[{...customerPreview.rules[0],unit:'adet'}]}
it.each([true,false])('shows the seven retained weighing facts without a weighing input and freezes native version2, approve=%s',async approve=>{
 const calls=installFetch({capabilities:[customerCap],respond:(url,init)=>url.includes(customerPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:customerKind,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'SENTETIK'}):json(retainedPieceReturn):undefined})
 mountCustomerCandidate();const region=await screen.findByRole('region',{name:T.customerReturnWeighing})
 expect(region.querySelectorAll('dt')).toHaveLength(7)
 for(const text of [retainedReturnWeighing.weighingId,retainedReturnWeighing.firstWeighingKg,retainedReturnWeighing.secondWeighingKg,retainedReturnWeighing.netKg,retainedReturnWeighing.recordedBy,retainedReturnWeighing.recordedAtUtc,T.customerReturnDirections.TareFirst])expect(within(region).getByText(text)).toBeTruthy()
 expect(within(region).queryByRole('spinbutton')).toBeNull();expect(within(region).queryByRole('textbox')).toBeNull()
 expect(screen.queryByText(T.customerReturnPieceUnavailable)).toBeNull();expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(false)
 fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
 const body=calls.filter(call=>call.url.includes(customerPath)&&call.method==='POST')[0]!.body as Record<string,unknown>
 expect(body).toMatchObject({expectedVersion:'2',approve,reason:null,delegationId:null});expect(Object.keys(body).sort()).toEqual((approve?['approve','delegationId','expectedVersion','logisticsCustomerReturn','reason']:['approve','delegationId','expectedVersion','reason']).sort())
 expect(JSON.stringify(body)).not.toContain('weighing');expect(JSON.stringify(sessionStorage)).not.toContain(retainedReturnWeighing.recordedBy)
})
it.each([true,false])('replays original version2 and movement/body/key after weighed return becomes terminal3, approve=%s',async approve=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-piece-weighing'});let posts=0
 const calls=installFetch({capabilities:[customerCap],respond:(url,init)=>{
  if(!url.includes(customerPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:customerKind,itemId:id,state:approve?'Approved':'Rejected',replayed:true,originalCorrelationId:'original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original'})
  return json(posts===0?retainedPieceReturn:{...retainedPieceReturn,pending:false,sourceVersion:'3',logisticsCustomerReturn:{...retainedPieceReturn.logisticsCustomerReturn,status:approve?'Approved':'Rejected'}})
 }})
 mountCustomerCandidate();await screen.findByRole('region',{name:T.customerReturnWeighing});fireEvent.change(screen.getByRole('textbox',{name:CUSTOMER_RETURN.note}),{target:{value:'SENTETIK özgün karar'}});fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.unknown)
 expect(JSON.stringify(sessionStorage)).not.toContain(retainedReturnWeighing.recordedBy);expect(JSON.stringify(sessionStorage)).not.toContain('firstWeighingKg')
 cleanup();mountCustomerCandidate();await screen.findByText(T.closed);await screen.findByText(T.unknown);expect(screen.getByText(`${T.version}: 2`)).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(customerPath)&&call.method==='POST');expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key'])
 expect(sent[1]!.body).toMatchObject({expectedVersion:'2',reason:'SENTETIK özgün karar'})
})
it('keeps a prior version1 rejection frozen after another native command records weighing at version2',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-piece-old-key'});let posts=0
 const unweighed={...retainedPieceReturn,sourceVersion:'1',approvalSupported:false,logisticsCustomerReturn:{...retainedPieceReturn.logisticsCustomerReturn,weighing:undefined}}
 const calls=installFetch({capabilities:[customerCap],respond:(url,init)=>{
  if(!url.includes(customerPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):problem(409)
  return json(posts===0?unweighed:retainedPieceReturn)
 }})
 mountCustomerCandidate();await screen.findByText(T.customerReturnPieceUnavailable);fireEvent.click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.unknown)
 cleanup();mountCustomerCandidate();await screen.findByRole('region',{name:T.customerReturnWeighing});await screen.findByText(T.unknown);expect(screen.getByText(`${T.version}: 1`)).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.refused,{exact:false})
 const sent=calls.filter(call=>call.url.includes(customerPath)&&call.method==='POST');expect(sent).toHaveLength(2);expect(sent[0]!.body).toEqual(sent[1]!.body);expect(sent[0]!.headers['Idempotency-Key']).toBe(sent[1]!.headers['Idempotency-Key']);expect(sent[1]!.body).toEqual({expectedVersion:'1',approve:false,reason:null,delegationId:null})
})
it.each([{approvalSupported:false},{sourceVersion:'1'},{logisticsCustomerReturn:{...retainedPieceReturn.logisticsCustomerReturn,weighing:{...retainedReturnWeighing,netKg:'4.1250'}}}])('does not display malformed weighing/support/version proof %#',async delta=>{
 const calls=installFetch({capabilities:[customerCap],respond:url=>url.includes(customerPath)?json({...retainedPieceReturn,...delta}):undefined});mountCustomerCandidate()
 await waitFor(()=>expect(calls.some(call=>call.url.includes(customerPath))).toBe(true));expect(screen.queryByRole('region',{name:T.customerReturnWeighing})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
})

const summaryKind='sales.order-draft.v1',summaryCap='sales.order-draft.work-decision',summaryPath=`/work/decisions/${summaryKind}/${id}`
const summaryVersion='9007199254740993',summaryAnswerId='dddddddd-0000-4000-8000-000000000002',summaryDigest='sha256:'+'a'.repeat(64)
const summaryPreview={...preview,kind:summaryKind,sourceVersion:summaryVersion,references:[{role:'order-draft',label:'SENTETIK kaynak satış taslağı',recordId:id,link:null}]}
const summaryReady={availability:'ready',panelAvailable:true,tools:[{toolId:'sales.draft-summary.v1',version:'1',ownerModule:'Sales',dataClass:'Internal'}]}
function salesSummaryAnswer(version=summaryVersion){
 const source={targetKind:'sales.order.draft-summary',targetId:id,title:'SENTETIK kaynak taslak',sourceVersion:summaryDigest,asOfUtc:'2026-11-27T08:00:00Z',dataClass:'Internal'}
 return {outcome:'answered',toolResults:[{toolId:'sales.draft-summary.v1',toolVersion:'1',outcome:'succeeded',contentJson:JSON.stringify({draftId:id,version,revision:'Rev.0',state:'Draft',approvalState:'PendingApproval',lines:[]}),source}],
 answer:{text:'SENTETIK kaynaklı Work satış özeti',citations:[{toolId:'sales.draft-summary.v1',toolVersion:'1',targetKind:source.targetKind,targetId:id,title:source.title,sourceVersion:summaryDigest,asOfUtc:source.asOfUtc}],modelDeployment:'SENTETIK-local',policyVersion:'SENTETIK-policy',answerId:summaryAnswerId}}
}
it.each([{extras:[]},{extras:[AI_PANEL_CAPABILITY]},{extras:['sales.drafts.detail']}])('does not mount sales summary without both separate capabilities and leaves Work approval usable: %j',async({extras})=>{
 const calls=installFetch({capabilities:[summaryCap,...extras],respond:(url,init)=>url.includes(summaryPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:summaryKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK'}):json(summaryPreview):undefined})
 mount(`/onaylarim/${summaryKind}/${id}`);await screen.findByRole('button',{name:T.approve})
 expect(screen.queryByRole('region',{name:SUMMARY.heading})).toBeNull();expect(calls.some(call=>call.url.includes('/ai/'))).toBe(false)
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 expect(calls.filter(call=>call.url.includes(summaryPath)&&call.method==='POST')[0]!.body).toEqual({expectedVersion:summaryVersion,approve:true,reason:null,delegationId:null})
})
it('keeps native Work approval usable while the optional summary runtime is unavailable',async()=>{
 const calls=installFetch({capabilities:[summaryCap,AI_PANEL_CAPABILITY,'sales.drafts.detail'],respond:(url,init)=>{
  if(url.includes(summaryPath))return init?.method==='POST'?json({companyId:COMPANY_A,kind:summaryKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK'}):json(summaryPreview)
  if(url.endsWith('/ai/capabilities'))return json({availability:'ready',panelAvailable:true,tools:[]})
  return undefined
 }})
 mount(`/onaylarim/${summaryKind}/${id}`);await screen.findByText(SUMMARY.unavailable)
 expect(screen.queryByRole('button',{name:SUMMARY.request})).toBeNull();expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(false)
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done);expect(calls.some(call=>call.url.endsWith('/ai/ask'))).toBe(false)
})
it.each([summaryVersion,'7'])('mounts the existing optional source-bound component after evidence and before decisions, owner content version=%s',async version=>{
 const calls=installFetch({capabilities:[summaryCap,AI_PANEL_CAPABILITY,'sales.drafts.detail'],respond:(url,init)=>{
  if(url.includes(summaryPath))return json(summaryPreview)
  if(url.endsWith('/ai/capabilities'))return json(summaryReady)
  if(url.endsWith('/ai/ask')&&init?.method==='POST')return json(salesSummaryAnswer(version))
  if(url.includes(`/ai/answers/${summaryAnswerId}/feedback`))return json({answerId:summaryAnswerId,version:'0',kind:null,reasonCode:null})
  return undefined
 }})
 mount(`/onaylarim/${summaryKind}/${id}`);const summary=await screen.findByRole('region',{name:SUMMARY.heading}),button=await screen.findByRole('button',{name:SUMMARY.request})
 const reference=screen.getByText('SENTETIK kaynak satış taslağı',{exact:false}),approve=screen.getByRole('button',{name:T.approve})
 expect(reference.compareDocumentPosition(summary)&Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();expect(summary.compareDocumentPosition(approve)&Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
 expect(calls.some(call=>call.url.endsWith('/ai/ask'))).toBe(false);fireEvent.click(button)
 if(version===summaryVersion){await screen.findByText('SENTETIK kaynaklı Work satış özeti');await screen.findByRole('region',{name:FEEDBACK.heading})}
 else{await screen.findByText(SUMMARY.stale);expect(screen.queryByRole('region',{name:FEEDBACK.heading})).toBeNull()}
 const request=calls.filter(call=>call.url.endsWith('/ai/ask'));expect(request).toHaveLength(1);expect(request[0]!.headers['X-Company-Id']).toBe(COMPANY_A)
 expect(request[0]!.body).toMatchObject({openRecordContext:{targetKind:'sales.order.draft-summary',targetId:id}})
 expect((approve as HTMLButtonElement).disabled).toBe(false);expect(calls.some(call=>call.url.includes(summaryPath)&&call.method==='POST')).toBe(false)
})
it.each(['finance.cash-account.v1','sales.invoice.v1','sales.subcontract-invoice.v1'])('does not mount the draft summary in another decision kind %s',async otherKind=>{
 const otherPath=`/work/decisions/${otherKind}/${id}`,otherCap=otherKind.replace('.v1','.work-decision')
 const calls=installFetch({capabilities:[otherCap,AI_PANEL_CAPABILITY,'sales.drafts.detail'],respond:url=>url.includes(otherPath)?json({...preview,kind:otherKind}):undefined})
 mount(`/onaylarim/${otherKind}/${id}`);await screen.findByRole('button',{name:T.approve})
 expect(screen.queryByRole('region',{name:SUMMARY.heading})).toBeNull();expect(calls.some(call=>call.url.includes('/ai/'))).toBe(false)
})
it('does not mount the summary or read AI for a foreign-company Work preview',async()=>{
 const calls=installFetch({capabilities:[summaryCap,AI_PANEL_CAPABILITY,'sales.drafts.detail'],respond:url=>url.includes(summaryPath)?json({...summaryPreview,companyId:COMPANY_B}):undefined})
 mount(`/onaylarim/${summaryKind}/${id}`);await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
 expect(screen.queryByRole('region',{name:SUMMARY.heading})).toBeNull();expect(calls.some(call=>call.url.includes('/ai/'))).toBe(false)
})

const manualPaymentKind='finance.manual-bank-payment.v1',manualPaymentCap='finance.manual-bank-payment.work-decision'
const manualPaymentPath=`/work/decisions/${manualPaymentKind}/${id}`
const manualPaymentPreview={...preview,kind:manualPaymentKind,documentLabel:'SENTETIK banka ödemesi',rejectionSupported:false}
const mountManualPayment=()=>mount(`/onaylarim/${manualPaymentKind}/${id}`,true)
it('opens manual bank payment approval with only its standalone capability and never records a money movement',async()=>{
 const calls=installFetch({capabilities:[manualPaymentCap],respond:(url,init)=>url.includes(manualPaymentPath)?init?.method==='POST'
  ?json({companyId:COMPANY_A,kind:manualPaymentKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK-original'}):json(manualPaymentPreview):undefined})
 mountManualPayment();await screen.findByRole('button',{name:T.approve})
 expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(1);expect(sent[0]!.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null})
 expect(calls.some(call=>call.url.includes('/finance/'))).toBe(false)
})
it('keeps the original manual payment version, null reason and key across unknown remount even when the current preview is terminal',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-manual-payment-session'});let posts=0
 const calls=installFetch({capabilities:[manualPaymentCap],respond:(url,init)=>url.includes(manualPaymentPath)?init?.method==='POST'
  ?++posts===1?problem(503):json({companyId:COMPANY_A,kind:manualPaymentKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'})
  :json({...manualPaymentPreview,pending:posts===0,sourceVersion:posts===0?'7':'9'}):undefined})
 mountManualPayment();await screen.findByRole('button',{name:T.approve});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();mountManualPayment();await screen.findByRole('button',{name:T.retry});expect(await screen.findByText(`${T.version}: 7`)).toBeTruthy();fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body)
 expect(sent[1]!.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null});expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
})
it.each([{capabilities:[]},{capabilities:['finance.manual-bank-payments.approve']},{capabilities:['finance.manual-bank-payments.detail']}])('does not read manual payment under missing or native browse/control capabilities %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(manualPaymentPath)?json(manualPaymentPreview):undefined});mountManualPayment()
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.includes(manualPaymentPath))).toBe(false)
})
it('clears manual payment approval when a fresh server 403 removes its standalone capability',async()=>{
 const capabilities=[manualPaymentCap]
 const calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(manualPaymentPath))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(403)}return json(manualPaymentPreview)}})
 mountManualPayment();await screen.findByRole('button',{name:T.approve});fireEvent.click(screen.getByRole('button',{name:T.approve}))
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
})


const ibanKind='finance.bank-account-iban-change.v1',ibanCap='finance.bank-account-iban-change.work-decision'
const ibanPath=`/work/decisions/${ibanKind}/${id}`
const ibanHuman={accountId:id,companyId:COMPANY_A,companyLegalName:'SENTETIK şirketin hukuki sahibi',currentIban:'SENTETIK eski IBAN',proposedIban:'TR060006100519786457841327',reason:'SENTETIK özel değişiklik nedeni',proposedBy:'SENTETIK IBAN hazırlayan',proposedAtUtc:'2026-11-27T08:00:00Z'}
const ibanPreview={...preview,kind:ibanKind,rejectionSupported:false,bankAccountIbanChange:ibanHuman,
 references:[{role:'bank-account',recordId:id,label:'SENTETIK banka hesabı',link:null}],
 rules:[{ruleCode:'finance.bank-account-iban-change-approval-required',threshold:null,actual:null,deviation:null,unit:null}]}
const mountIban=()=>mount(`/onaylarim/${ibanKind}/${id}`,true)
it('shows the exact IBAN human proof under approve-only authority and sends no raw proof or money command',async()=>{
 const calls=installFetch({capabilities:[ibanCap],respond:(url,init)=>url.includes(ibanPath)?init?.method==='POST'
  ?json({companyId:COMPANY_A,kind:ibanKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK-original'}):json(ibanPreview):undefined})
 mountIban();const region=await screen.findByRole('region',{name:T.ibanProposal})
 for(const value of Object.values(ibanHuman))expect(within(region).getByText(value)).toBeTruthy()
 expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null})
 expect(calls.some(call=>call.url.includes('/finance/')||call.url.includes('/ai/'))).toBe(false)
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 expect(calls.filter(call=>call.url.includes(ibanPath)&&call.method!=='POST')).toHaveLength(1)
})
it.each(['unavailable','replacement'])('restores only the original IBAN command across unknown/remount with %s current GET',async mode=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-iban-session'});let posts=0
 const calls=installFetch({capabilities:[ibanCap],respond:(url,init)=>{
  if(!url.includes(ibanPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:ibanKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'})
  return posts===0?json(ibanPreview):mode==='unavailable'?problem(503):json({...ibanPreview,sourceVersion:'9',bankAccountIbanChange:{...ibanHuman,proposedBy:IDENTITY.userId,proposedIban:'TR330006100519786457841326'}})
 }})
 mountIban();await screen.findByRole('button',{name:T.approve});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 const journal=JSON.stringify(sessionStorage)
 for(const value of [ibanHuman.currentIban,ibanHuman.proposedIban,ibanHuman.companyLegalName,ibanHuman.reason])expect(journal).not.toContain(value)
 cleanup();mountIban();await screen.findByRole('button',{name:T.retry});expect(await screen.findByText(`${T.version}: 7`)).toBeTruthy()
 expect(screen.queryByRole('region',{name:T.ibanProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 expect(calls.filter(call=>call.url.includes(ibanPath)&&call.method!=='POST')).toHaveLength(1)
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2)
 expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null})
 expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
})
it.each([{capabilities:[]},{capabilities:['finance.bank-accounts.approve']},{capabilities:['finance.bank-accounts.list']}])('does not read an IBAN purpose without its standalone capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(ibanPath)?json(ibanPreview):undefined});mountIban()
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.includes(ibanPath))).toBe(false)
})
it('does not turn a new unavailable IBAN preview into an approval or disclose a foreign-company proof',async()=>{
 const calls=installFetch({capabilities:[ibanCap],respond:url=>url.includes(ibanPath)?problem(503):undefined});mountIban()
 await waitFor(()=>expect(document.querySelector('[data-state="service-unavailable"]')).not.toBeNull())
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(0)
 cleanup();installFetch({capabilities:[ibanCap],respond:url=>url.includes(ibanPath)?json({...ibanPreview,bankAccountIbanChange:{...ibanHuman,companyId:COMPANY_B}}):undefined});mountIban()
 await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByText(ibanHuman.proposedIban)).toBeNull()
})
it('closes a retained IBAN retry when fresh purpose authority is gone and never restores it into another session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-iban-old-session'});const capabilities=[ibanCap]
 const calls=installFetch({capabilities,respond:(url,init)=>url.includes(ibanPath)?init?.method==='POST'?problem(503):json(ibanPreview):undefined})
 mountIban();await screen.findByRole('button',{name:T.approve});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();capabilities.splice(0);mountIban();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
 cleanup();capabilities.push(ibanCap);Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-iban-new-session'});mountIban()
 await screen.findByRole('button',{name:T.approve});expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
})
it('applies fresh server 403 to an IBAN decision instead of retaining raw human proof on screen',async()=>{
 const capabilities=[ibanCap]
 installFetch({capabilities,respond:(url,init)=>{if(!url.includes(ibanPath))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(403)}return json(ibanPreview)}})
 mountIban();await screen.findByRole('button',{name:T.approve});fireEvent.click(screen.getByRole('button',{name:T.approve}))
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByText(ibanHuman.proposedIban)).toBeNull()
})


it('does not carry an unknown IBAN command or its private preview into a different company',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-iban-company-session'})
 const calls=installFetch({capabilities:[ibanCap],respond:(url,init)=>{
  if(!url.includes(ibanPath))return undefined
  const company=(init?.headers as Record<string,string>)?.['X-Company-Id']??COMPANY_A
  return init?.method==='POST'?problem(503):json({...ibanPreview,companyId:company,bankAccountIbanChange:{...ibanHuman,companyId:company,companyLegalName:company===COMPANY_A?ibanHuman.companyLegalName:'SENTETIK başka şirket',proposedIban:company===COMPANY_A?ibanHuman.proposedIban:'TR330006100519786457841326'}})
 }})
 mountIban();await screen.findByRole('button',{name:T.approve});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
 await waitFor(()=>expect(calls.some(call=>call.url.includes(ibanPath)&&call.method==='GET'&&call.headers['X-Company-Id']===COMPANY_B)).toBe(true))
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(screen.queryByText(ibanHuman.proposedIban)).toBeNull()
 const sent=calls.filter(call=>call.url.includes(ibanPath)&&call.method==='POST');expect(sent).toHaveLength(1);expect(sent[0]!.headers['X-Company-Id']).toBe(COMPANY_A)
})


it('suppresses cached IBAN proof and a late replacement refetch throughout the original receipt retry',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-iban-inflight-session'})
 const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
 let gets=0,posts=0,release!: (response:Response)=>void
 const late=new Promise<Response>(resolve=>{release=resolve})
 const calls=installFetch({capabilities:[ibanCap],respond:(url,init)=>{
  if(!url.includes(ibanPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:ibanKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'})
  return ++gets===1?json(ibanPreview):late
 }})
 mount(`/onaylarim/${ibanKind}/${id}`,true,client)
 await screen.findByRole('region',{name:T.ibanProposal})
 const matching=(query:{readonly queryKey:readonly unknown[]})=>query.queryKey.includes('work-decision')
 let refetch!:Promise<void>
 act(()=>{refetch=client.refetchQueries({predicate:matching})})
 await waitFor(()=>expect(gets).toBe(2))
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 // Even the previously cached human proof is hidden once the command is frozen.
 expect(screen.queryByRole('region',{name:T.ibanProposal})).toBeNull()
 const replacement={...ibanPreview,sourceVersion:'9',bankAccountIbanChange:{...ibanHuman,proposedBy:'SENTETIK replacement maker',proposedIban:'TR330006100519786457841326'}}
 await act(async()=>{release(json(replacement));await refetch})
 expect(client.getQueryCache().findAll({predicate:matching})[0]?.state.data).toMatchObject({type:'ok-single',data:{sourceVersion:'9'}})
 expect(screen.queryByRole('region',{name:T.ibanProposal})).toBeNull()
 expect(screen.queryByText(replacement.bankAccountIbanChange.proposedBy)).toBeNull()
 expect(screen.queryByText(replacement.bankAccountIbanChange.proposedIban)).toBeNull()
 expect(screen.getByText(`${T.version}: 7`)).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 expect(screen.queryByRole('region',{name:T.ibanProposal})).toBeNull()
 const sent=calls.filter(call=>call.url.includes(ibanPath)&&call.method==='POST')
 expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null})
 expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 client.clear()
})

const packageKind='product.package-standard-version.v1', packageCap='price-lists.package-standard-version.work-decision'
const packagePath=`/work/decisions/${packageKind}/${id}`, packageRoute=`/onaylarim/${packageKind}/${id}`
const packageTable={sourceKind:'Edit',sourceLabel:null,fileName:null,reason:'SENTETIK paket hazırlama',preparedBy:'SENTETIK-paket-maker',preparedAtUtc:'2026-11-27T08:00:00Z',entryCount:1,customerEntryCount:1,
 entries:[{productId:id,customerId:null,piecesPerPackage:12},{productId:id,customerId:COMPANY_B,piecesPerPackage:6}]}
const packagePreview={...preview,kind:packageKind,documentLabel:'v7',approvalReasonRequired:true,rejectionSupported:true,productPackageStandard:packageTable}
it('shows the complete price-free package table and requires an explicitly chosen date and human reason',async()=>{
 const calls=installFetch({capabilities:[packageCap],respond:(url,init)=>url.includes(packagePath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:packageKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK'}):json(packagePreview):undefined})
 mount(packageRoute);await screen.findByRole('region',{name:T.packageProposal})
 const table=screen.getByRole('table',{name:T.packageEntries});expect(within(table).getAllByRole('row')).toHaveLength(3)
 expect(screen.getByText(packageTable.reason)).toBeTruthy();expect(screen.getByText(packageTable.preparedBy)).toBeTruthy()
 expect((screen.getByLabelText(T.packageEffectiveFrom) as HTMLInputElement).value).toBe('')
 expect(screen.getByRole('button',{name:T.approve}).hasAttribute('disabled')).toBe(true)
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'  SENTETIK karar  '}})
 expect(screen.getByRole('button',{name:T.approve}).hasAttribute('disabled')).toBe(true)
 fireEvent.change(screen.getByLabelText(T.packageEffectiveFrom),{target:{value:'2026-12-01'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 expect(calls.filter(call=>call.method==='POST').map(call=>call.body)).toEqual([{expectedVersion:'7',approve:true,reason:'SENTETIK karar',delegationId:null,productPackageStandard:{effectiveFrom:'2026-12-01'}}])
 expect(calls.some(call=>call.url.includes('/product-package-standards')||call.url.includes('/ai/'))).toBe(false)
})
it('rejects a package standard with the native 4000-character reason and no effective-date payload',async()=>{
 const calls=installFetch({capabilities:[packageCap],respond:(url,init)=>url.includes(packagePath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:packageKind,itemId:id,state:'Rejected',replayed:false,originalCorrelationId:'SENTETIK'}):json(packagePreview):undefined})
 mount(packageRoute);await screen.findByLabelText(T.packageEffectiveFrom)
 const reason=screen.getByLabelText(T.reason) as HTMLTextAreaElement;expect(reason.maxLength).toBe(4000)
 fireEvent.change(screen.getByLabelText(T.packageEffectiveFrom),{target:{value:'2026-12-01'}})
 fireEvent.change(reason,{target:{value:'x'.repeat(4000)}});fireEvent.click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.done)
 expect(calls.filter(call=>call.method==='POST').map(call=>call.body)).toEqual([{expectedVersion:'7',approve:false,reason:'x'.repeat(4000),delegationId:null}])
})
it('restores the original package date, reason, version and key after unknown/remount even when a newer preview is terminal',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-package-session'});let posts=0
 const calls=installFetch({capabilities:[packageCap],respond:(url,init)=>{
  if(!url.includes(packagePath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:packageKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'})
  return json({...packagePreview,pending:posts===0})
 }})
 mount(packageRoute);await screen.findByLabelText(T.packageEffectiveFrom)
 fireEvent.change(screen.getByLabelText(T.reason),{target:{value:'SENTETIK karar'}});fireEvent.change(screen.getByLabelText(T.packageEffectiveFrom),{target:{value:'2026-12-01'}})
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 expect(JSON.stringify(sessionStorage)).not.toContain(packageTable.reason);expect(JSON.stringify(sessionStorage)).not.toContain(packageTable.preparedBy)
 cleanup();mount(packageRoute);await screen.findByRole('button',{name:T.retry});await screen.findByText(T.closed)
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2)
 expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
})
it.each([{capabilities:[]},{capabilities:['product.items.detail']},{capabilities:['price-lists.publish']}])('does not read package proof without the exact standalone approve purpose %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(packagePath)?json(packagePreview):undefined});mount(packageRoute)
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.includes(packagePath))).toBe(false)
})
it('does not expose foreign package proof or create a decision from malformed counts',async()=>{
 installFetch({capabilities:[packageCap],respond:url=>url.includes(packagePath)?json({...packagePreview,companyId:COMPANY_B}):undefined});mount(packageRoute)
 await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByText(packageTable.reason)).toBeNull()
 cleanup();installFetch({capabilities:[packageCap],respond:url=>url.includes(packagePath)?json({...packagePreview,productPackageStandard:{...packageTable,entryCount:2}}):undefined});mount(packageRoute)
 await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
})

const pricePath=`/work/decisions/${priceKind}/${id}`,priceRoute=`/onaylarim/${priceKind}/${id}`
const pricePreview={...preview,kind:priceKind,rejectionSupported:false,productPriceList:priceHuman}
it('shows owner prices without calculation and sends four publication fields without date or reason',async()=>{
 const calls=installFetch({capabilities:[priceCap],respond:(url,init)=>url.includes(pricePath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:priceKind,itemId:id,state:'Published',replayed:false,originalCorrelationId:'SENTETIK'}):json(pricePreview):undefined})
 mount(priceRoute);await screen.findByRole('region',{name:PRICE.listHeading})
 expect(screen.getByText('41000.125')).toBeTruthy();expect(screen.getByText('40000.0001')).toBeTruthy();expect(screen.getByText('999.123')).toBeTruthy()
 expect(within(screen.getByRole('table',{name:PRICE.linesHeading})).getAllByRole('row')).toHaveLength(2)
 expect(screen.queryByRole('button',{name:T.reject})).toBeNull();expect(screen.queryByLabelText(T.reason)).toBeNull();expect(screen.queryByLabelText(T.packageEffectiveFrom)).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
 expect(calls.filter(call=>call.url.includes(pricePath)&&call.method==='POST')[0]?.body).toEqual({expectedVersion:'7',approve:true,reason:null,delegationId:null})
 expect(calls.some(call=>call.url.includes('/publication')||call.url.includes('/ai/'))).toBe(false)
})
it('keeps original price key/body/version across unknown/remount without showing later price proof',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-price-session'});let posts=0,gets=0
 const calls=installFetch({capabilities:[priceCap],respond:(url,init)=>{
 if(!url.includes(pricePath))return undefined
 if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:priceKind,itemId:id,state:'Published',replayed:true,originalCorrelationId:'SENTETIK-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'})
 gets++;return json({...pricePreview,sourceVersion:posts===0?'7':'10'})
 }})
 mount(priceRoute);await screen.findByRole('region',{name:PRICE.listHeading});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 expect(screen.queryByRole('region',{name:PRICE.listHeading})).toBeNull();expect(JSON.stringify(sessionStorage)).not.toContain('41000.125');expect(JSON.stringify(sessionStorage)).not.toContain('SENTETIK fiyat')
 cleanup();mount(priceRoute);await screen.findByRole('button',{name:T.retry});expect(gets).toBe(1)
 expect(screen.getByText(T.priceListRetained)).toBeTruthy()
 expect(screen.queryByText(T.lossRequestNumber,{exact:false})).toBeNull();expect(screen.queryByText(T.lossFrozenTime,{exact:false})).toBeNull()
 expect(screen.queryByLabelText(T.reason)).toBeNull();expect(screen.queryByLabelText(T.packageEffectiveFrom)).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(pricePath)&&call.method==='POST');expect(sent).toHaveLength(2)
 expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 expect(screen.queryByRole('region',{name:PRICE.listHeading})).toBeNull()
})
it.each([[],['price-lists.publish'],['price-lists.detail'],['price-lists.package-standard-version.work-decision'],['product.items.detail']].map(capabilities=>({capabilities})))('requires exact standalone price purpose %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(pricePath)?json(pricePreview):undefined});mount(priceRoute)
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.includes(pricePath))).toBe(false)
})
it('hides foreign owner price data',async()=>{
 installFetch({capabilities:[priceCap],respond:url=>url.includes(pricePath)?json({...pricePreview,companyId:COMPANY_B}):undefined});mount(priceRoute)
 await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByText('41000.125')).toBeNull()
})

it('keeps a price proof already in flight hidden after the original command becomes unknown',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-price-flight'});let gets=0;let release!:(value:Response)=>void
 const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
 installFetch({capabilities:[priceCap],respond:(url,init)=>{
 if(!url.includes(pricePath))return undefined
 if(init?.method==='POST')return problem(503)
 return ++gets===1?json(pricePreview):new Promise<Response>(resolve=>{release=resolve})
 }})
 mount(priceRoute,false,client);await screen.findByRole('region',{name:PRICE.listHeading})
 const matching=(query:{queryKey:readonly unknown[]})=>query.queryKey.includes('work-decision')
 let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:matching})});await waitFor(()=>expect(gets).toBe(2))
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 await act(async()=>{release(json({...pricePreview,sourceVersion:'10',productPriceList:{...priceHuman,name:'SENTETIK replacement price'}}));await refetch})
 expect(screen.queryByRole('region',{name:PRICE.listHeading})).toBeNull();expect(screen.queryByText('SENTETIK replacement price')).toBeNull();expect(screen.getByText(`${T.version}: 7`)).toBeTruthy()
 client.clear()
})
it('invalidates current authority when a fresh price preview returns 403',async()=>{
 const calls=installFetch({capabilities:[priceCap],respond:url=>url.includes(pricePath)?problem(403):undefined});mount(priceRoute)
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
 expect(screen.queryByText('41000.125')).toBeNull();expect(calls.some(call=>call.method==='POST')).toBe(false)
})

const limitKind='finance.party-credit-limit.v1',limitCap='finance.party-credit-limit.work-decision'
const limitPath=`/work/decisions/${limitKind}/${id}`,limitRoute=`/onaylarim/${limitKind}/${id}`
const limitHuman={partyId:'dddddddd-0000-4000-8000-000000000071',companyAccountId:'dddddddd-0000-4000-8000-000000000072',kind:'Set',currencyId:'dddddddd-0000-4000-8000-000000000073',amount:'00123.4500',baseRevision:'0',currentRevision:'0',proposedBy:'SENTETIK limit proposer',proposedAtUtc:'2026-11-27T08:00:00Z'}
const limitRefs=[{role:'party',recordId:limitHuman.partyId,label:'SENTETIK cari',link:null},{role:'company-account',recordId:limitHuman.companyAccountId,label:'SENTETIK hesap',link:null},{role:'currency',recordId:limitHuman.currencyId,label:'SENTETIK para',link:null}]
const limitPreview={companyId:COMPANY_A,kind:limitKind,itemId:id,documentLabel:'SENTETIK limit önerisi',sourceVersion:'1',pending:true,approvalReasonRequired:false,rejectionSupported:true,references:limitRefs,rules:[],partyCreditLimit:limitHuman}
const limitResult=(approve=true,replayed=false)=>json({companyId:COMPANY_A,kind:limitKind,itemId:id,state:approve?'Approved':'Rejected',replayed,originalCorrelationId:'SENTETIK original'},200,replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}:undefined)

it.each([{remove:false,approve:true},{remove:false,approve:false},{remove:true,approve:true},{remove:true,approve:false}])('renders the native credit limit proposal and sends only its explicit four-field decision %j',async({remove,approve})=>{
 const facts=remove?{...limitHuman,kind:'Remove',currencyId:null,amount:null}:limitHuman
 const calls=installFetch({capabilities:[limitCap],respond:(url,init)=>url.includes(limitPath)?init?.method==='POST'?limitResult(approve):json({...limitPreview,references:remove?limitRefs.slice(0,2):limitRefs,partyCreditLimit:facts}):undefined})
 mount(limitRoute,true);const region=await screen.findByRole('region',{name:T.creditLimitProposal})
 for(const value of [facts.partyId,facts.companyAccountId,facts.proposedBy,facts.proposedAtUtc])expect(within(region).getByText(value)).toBeTruthy()
 expect(within(region).getByText(remove?'Limit yok':limitHuman.amount)).toBeTruthy()
 expect(within(region).getByText(T.creditLimitDecisionHelp)).toBeTruthy()
 expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
 if(!approve){fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'  SENTETIK ret  '}})}
 fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
 const sent=calls.filter(call=>call.url.includes(limitPath)&&call.method==='POST');expect(sent).toHaveLength(1)
 expect(sent[0]!.body).toEqual({expectedVersion:'1',approve,reason:approve?null:'SENTETIK ret',delegationId:null})
 expect(screen.queryByRole('region',{name:T.creditLimitProposal})).toBeNull()
 expect(calls.filter(call=>call.url.includes(limitPath)&&call.method==='GET')).toHaveLength(1)
})
it.each([true,false])('keeps an unknown credit limit decision original after remount and later superseding direct Set (%s)',async approve=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-limit-session'});let posts=0
 const calls=installFetch({capabilities:[limitCap],respond:(url,init)=>{
  if(!url.includes(limitPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):limitResult(approve,true)
  return json(posts===0?limitPreview:{...limitPreview,pending:false,sourceVersion:'3',partyCreditLimit:{...limitHuman,currentRevision:'7'}})
 }})
 mount(limitRoute,true);await screen.findByRole('region',{name:T.creditLimitProposal})
 if(!approve)fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK ret'}})
 fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.unknown)
 const journal=JSON.stringify(sessionStorage);for(const value of [limitHuman.amount,limitHuman.partyId,limitHuman.currencyId,limitHuman.proposedBy,'partyCreditLimit'])expect(journal).not.toContain(value)
 cleanup();mount(limitRoute,true);await screen.findByText(T.unknown);await screen.findByText(T.creditLimitRetained)
 expect(screen.getByText(`${T.version}: 1`)).toBeTruthy();expect(screen.queryByRole('region',{name:T.creditLimitProposal})).toBeNull()
 expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done);await screen.findByText(T.priorDecisionResolved)
 const sent=calls.filter(call=>call.url.includes(limitPath)&&call.method==='POST');expect(sent).toHaveLength(2)
 expect(sent[0]!.body).toEqual({expectedVersion:'1',approve,reason:approve?null:'SENTETIK ret',delegationId:null})
 expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 expect(calls.filter(call=>call.url.includes(limitPath)&&call.method==='GET')).toHaveLength(1)
 expect(screen.queryByRole('region',{name:T.creditLimitProposal})).toBeNull()
})
it('hides cached and late current credit revisions throughout an original decision receipt reconciliation',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-limit-late-session'})
 const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}});let gets=0,posts=0,release!:(reply:Response)=>void
 const late=new Promise<Response>(resolve=>{release=resolve})
 const calls=installFetch({capabilities:[limitCap],respond:(url,init)=>{
  if(!url.includes(limitPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):limitResult(true,true)
  return ++gets===1?json(limitPreview):late
 }})
 mount(limitRoute,true,client);await screen.findByRole('region',{name:T.creditLimitProposal})
 const matching=(query:{readonly queryKey:readonly unknown[]})=>query.queryKey.includes('work-decision')
 let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:matching})});await waitFor(()=>expect(gets).toBe(2))
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 expect(screen.queryByRole('region',{name:T.creditLimitProposal})).toBeNull()
 await act(async()=>{release(json({...limitPreview,pending:false,sourceVersion:'3',partyCreditLimit:{...limitHuman,currentRevision:'7'}}));await refetch})
 expect(client.getQueryCache().findAll({predicate:matching})[0]?.state.data).toMatchObject({type:'ok-single',data:{sourceVersion:'3',partyCreditLimit:{currentRevision:'7'}}})
 expect(screen.queryByRole('region',{name:T.creditLimitProposal})).toBeNull();expect(screen.getByText(`${T.version}: 1`)).toBeTruthy()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done);await screen.findByText(T.priorDecisionResolved)
 expect(screen.queryByRole('region',{name:T.creditLimitProposal})).toBeNull();expect(gets).toBe(2)
 const sent=calls.filter(call=>call.url.includes(limitPath)&&call.method==='POST');expect(sent).toHaveLength(2)
 expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
})
it.each([{capabilities:[]},{capabilities:['finance.credit-limits.approve']},{capabilities:['finance.credit-limits.list']},{capabilities:['finance.bank-account.work-decision']},{capabilities:['finance.credit-account.work-decision']}])('requires the exact credit limit purpose capability %j',async({capabilities})=>{
 const calls=installFetch({capabilities,respond:url=>url.includes(limitPath)?json(limitPreview):undefined});mount(limitRoute,true)
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.includes(limitPath))).toBe(false)
})
it.each([{companyId:COMPANY_B},{partyCreditLimit:null},{partyCreditLimit:{...limitHuman,amount:'1\n'}}])('withholds malformed or foreign credit limit human facts %j',async delta=>{
 installFetch({capabilities:[limitCap],respond:url=>url.includes(limitPath)?json({...limitPreview,...delta}):undefined});mount(limitRoute,true)
 await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
 expect(screen.queryByRole('region',{name:T.creditLimitProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
})
it('does not restore the old credit decision into revoked capability or a changed authentication session',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-limit-old'});const capabilities=[limitCap]
 const calls=installFetch({capabilities,respond:(url,init)=>url.includes(limitPath)?init?.method==='POST'?problem(503):json(limitPreview):undefined})
 mount(limitRoute,true);await screen.findByRole('region',{name:T.creditLimitProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();capabilities.splice(0);mount(limitRoute,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
 cleanup();capabilities.push(limitCap);Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-limit-new'});mount(limitRoute,true);await screen.findByRole('region',{name:T.creditLimitProposal})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.url.includes(limitPath)&&call.method==='POST')).toHaveLength(1)
})
it.each([401,403])('preserves the ordinary credit decision HTTP %s boundary',async status=>{
 const capabilities=[limitCap]
 const calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(limitPath))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(status)}return json(limitPreview)}})
 mount(limitRoute,true);await screen.findByRole('region',{name:T.creditLimitProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}))
 await waitFor(()=>expect(document.querySelector(`[data-state="${status===401?'unauthenticated':'not-found'}"]`)).not.toBeNull())
 expect(screen.queryByRole('region',{name:T.creditLimitProposal})).toBeNull()
 expect(calls.filter(call=>call.url.includes(limitPath)&&call.method==='POST')).toHaveLength(1);expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
})
it('does not transfer a retained credit limit decision or its facts to a different company',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-limit-company-session'})
 const calls=installFetch({capabilities:[limitCap],respond:(url,init)=>{
  if(!url.includes(limitPath))return undefined
  const company=(init?.headers as Record<string,string>)?.['X-Company-Id']??COMPANY_A
  return init?.method==='POST'?problem(503):json({...limitPreview,companyId:company,partyCreditLimit:{...limitHuman,amount:company===COMPANY_A?limitHuman.amount:'98765.4000'}})
 }})
 mount(limitRoute,true);await screen.findByRole('region',{name:T.creditLimitProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
 await waitFor(()=>expect(calls.some(call=>call.url.includes(limitPath)&&call.method==='GET'&&call.headers['X-Company-Id']===COMPANY_B)).toBe(true))
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(screen.queryByText(limitHuman.amount)).toBeNull()
 expect(calls.filter(call=>call.url.includes(limitPath)&&call.method==='POST')).toHaveLength(1)
})
it('validates the native rejection note boundary without adding a reason to approval',async()=>{
 const calls=installFetch({capabilities:[limitCap],respond:(url,init)=>url.includes(limitPath)?init?.method==='POST'?limitResult(false):json(limitPreview):undefined})
 mount(limitRoute,true);await screen.findByRole('region',{name:T.creditLimitProposal})
 const input=screen.getByRole('textbox',{name:T.reason}),reject=screen.getByRole('button',{name:T.reject}) as HTMLButtonElement
 fireEvent.change(input,{target:{value:'x'.repeat(501)}});expect(reject.disabled).toBe(true)
 fireEvent.change(input,{target:{value:'x'.repeat(500)}});expect(reject.disabled).toBe(false)
 fireEvent.click(reject);await screen.findByText(T.done)
 expect(calls.find(call=>call.url.includes(limitPath)&&call.method==='POST')?.body).toEqual({expectedVersion:'1',approve:false,reason:'x'.repeat(500),delegationId:null})
})


const importKind='finance.official-invoice-import.v1',importCap='finance.official-invoice-import.work-decision'
const importPath=`/work/decisions/${importKind}/${id}`,importRoute=`/onaylarim/${importKind}/${id}`
const importBatch={batchId:id,companyId:COMPANY_A,batchVersion:'SENTETIK-file/7',batchChecksum:'c'.repeat(64),templateVersion:'finance-official-invoice-installment-csv.v2',status:'Pending',version:'1',rowCount:13,uploadedBy:'SENTETIK invoice uploader',uploadedAtUtc:'2026-11-27T08:00:00Z',approvedBy:null,approvedAtUtc:null}
const importRows=Array.from({length:13},(_,i)=>({openItemId:`eeeeeeee-0000-4000-8000-${String(i+1).padStart(12,'0')}`,sourceDocumentId:'eeeeeeee-0000-4000-8000-000000000020',sourceInstallmentId:`ffffffff-0000-4000-8000-${String(i+1).padStart(12,'0')}`,sourceVersion:'SENTETIK-document/3',documentKind:'official-invoice-installment',partyId:'eeeeeeee-0000-4000-8000-000000000021',companyAccountId:'eeeeeeee-0000-4000-8000-000000000022',currencyId:'eeeeeeee-0000-4000-8000-000000000023',amount:i===12?'000999999999999999999999999999999999.0001':'0001250.5000',dueOn:i===12?null:'2026-12-31'}))
const importWarning={code:'finance_official_invoice_tax_inconsistent',sourceDocumentId:importRows[0]!.sourceDocumentId,expectedPayableFunctionalAmount:'1200',payableFunctionalAmount:'1200.01',differenceFunctionalAmount:'0.01'}
const importHuman={batch:importBatch,preview:{templateVersion:importBatch.templateVersion,sourceSha256:importBatch.batchChecksum,isValid:true,batch:{batchId:id,companyId:COMPANY_A,batchVersion:importBatch.batchVersion,batchChecksum:importBatch.batchChecksum,rows:importRows},issues:[],taxWarnings:[importWarning]}}
const importPreview={companyId:COMPANY_A,kind:importKind,itemId:id,documentLabel:'SENTETIK immutable import',sourceVersion:'1',pending:true,approvalReasonRequired:false,rejectionSupported:false,references:[],rules:[{ruleCode:'finance.official-invoice-import-approval',threshold:null,actual:null,deviation:null,unit:null}],officialInvoiceImport:importHuman}
const importTerminal={...importPreview,pending:false,sourceVersion:'2',officialInvoiceImport:{...importHuman,batch:{...importBatch,status:'Approved',version:'2',approvedBy:'SENTETIK checker',approvedAtUtc:'2026-11-27T09:00:00Z'}}}
const importResult=(replayed=false)=>json({companyId:COMPANY_A,kind:importKind,itemId:id,state:'Approved',replayed,originalCorrelationId:'SENTETIK import original'},200,replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK import original'}:undefined)

describe('WP8 official invoice import approval purpose',()=>{
 it('shows the complete native header, all 13 rows and warning before an approve-only four-field decision',async()=>{
  const calls=installFetch({capabilities:[importCap],respond:(url,init)=>url.includes(importPath)?init?.method==='POST'?importResult():json(importPreview):undefined})
  mount(importRoute,true);const region=await screen.findByRole('region',{name:T.importProposal})
  for(const value of [importBatch.batchId,importBatch.companyId,importBatch.batchVersion,importBatch.batchChecksum,importBatch.uploadedBy,importBatch.uploadedAtUtc])expect(within(region).getByText(value)).toBeTruthy()
  expect(within(region).getByText(T.importPending)).toBeTruthy()
  const table=within(region).getByRole('table',{name:T.importRows})
  expect(within(table).getAllByRole('row')).toHaveLength(14)
  expect(within(table).getAllByRole('columnheader')).toHaveLength(10)
  expect(within(table).getByText(importRows[12]!.amount)).toBeTruthy();expect(within(table).getByText(T.importNoDue)).toBeTruthy()
  expect(within(region).getByText(/1200\.01/)).toBeTruthy()
  expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
  expect(calls.filter(call=>call.url.includes(importPath)&&call.method==='POST')).toHaveLength(1)
  expect(calls.find(call=>call.url.includes(importPath)&&call.method==='POST')?.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null})
  expect(calls.filter(call=>call.url.includes(importPath)&&call.method==='GET')).toHaveLength(1)
  expect(calls.some(call=>call.url.includes('/finance/'))).toBe(false)
  expect(screen.queryByRole('region',{name:T.importProposal})).toBeNull()
 })
 it('keeps a terminal Approved2 detail read-only without inventing a new approval',async()=>{
  const calls=installFetch({capabilities:[importCap],respond:url=>url.includes(importPath)?json(importTerminal):undefined})
  mount(importRoute,true);const region=await screen.findByRole('region',{name:T.importProposal})
  expect(within(region).getByText('SENTETIK checker')).toBeTruthy();expect(within(region).getByText(T.importApproved)).toBeTruthy()
  expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it('restores only the frozen key and four-field original request, not current terminal facts or private source data',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-import-session'});let posts=0
  const calls=installFetch({capabilities:[importCap],respond:(url,init)=>url.includes(importPath)?init?.method==='POST'?++posts===1?problem(503):importResult(true):json(posts===0?importPreview:importTerminal):undefined})
  mount(importRoute,true);await screen.findByRole('region',{name:T.importProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const journal=JSON.stringify(sessionStorage)
  for(const secret of [importRows[12]!.amount,importRows[0]!.partyId,importRows[0]!.sourceDocumentId,importBatch.batchChecksum,importBatch.uploadedBy,'officialInvoiceImport'])expect(journal).not.toContain(secret)
  cleanup();mount(importRoute,true);await screen.findByText(T.unknown);await screen.findByText(T.importRetained)
  expect(screen.getByText(`${T.version}: 1`)).toBeTruthy();expect(screen.queryByRole('region',{name:T.importProposal})).toBeNull()
  expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done);await screen.findByText(T.priorDecisionResolved)
  const sent=calls.filter(call=>call.url.includes(importPath)&&call.method==='POST');expect(sent).toHaveLength(2)
  expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null})
  expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
  expect(calls.filter(call=>call.url.includes(importPath)&&call.method==='GET')).toHaveLength(1)
  expect(screen.queryByRole('region',{name:T.importProposal})).toBeNull()
 })
 it('hides cached and late terminal GET facts before, during and after original receipt reconciliation',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-import-late-session'})
  const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}});let gets=0,posts=0,release!:(reply:Response)=>void
  const late=new Promise<Response>(resolve=>{release=resolve})
  const calls=installFetch({capabilities:[importCap],respond:(url,init)=>{
   if(!url.includes(importPath))return undefined
   if(init?.method==='POST')return ++posts===1?problem(503):importResult(true)
   return ++gets===1?json(importPreview):late
  }})
  mount(importRoute,true,client);await screen.findByRole('region',{name:T.importProposal})
  const matching=(query:{readonly queryKey:readonly unknown[]})=>query.queryKey.includes('work-decision')
  let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:matching})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  expect(screen.queryByRole('region',{name:T.importProposal})).toBeNull()
  await act(async()=>{release(json(importTerminal));await refetch})
  expect(client.getQueryCache().findAll({predicate:matching})[0]?.state.data).toMatchObject({type:'ok-single',data:{sourceVersion:'2',officialInvoiceImport:{batch:{status:'Approved'}}}})
  expect(screen.queryByRole('region',{name:T.importProposal})).toBeNull();expect(screen.getByText(`${T.version}: 1`)).toBeTruthy()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done);await screen.findByText(T.priorDecisionResolved)
  expect(screen.queryByRole('region',{name:T.importProposal})).toBeNull();expect(gets).toBe(2)
  const sent=calls.filter(call=>call.url.includes(importPath)&&call.method==='POST');expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each([{capabilities:[]},{capabilities:['finance.official-invoice-import.approve']},{capabilities:['finance.official-invoice-import.upload']},{capabilities:['finance.party-credit-limit.work-decision']}])('requires the exact import purpose capability %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(importPath)?json(importPreview):undefined});mount(importRoute,true)
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.includes(importPath))).toBe(false)
 })
 it.each([{companyId:COMPANY_B},{officialInvoiceImport:null},{officialInvoiceImport:{...importHuman,preview:{...importHuman.preview,sourceSha256:'d'.repeat(64)}}}])('withholds missing, foreign and inconsistent import proof %j',async delta=>{
  installFetch({capabilities:[importCap],respond:url=>url.includes(importPath)?json({...importPreview,...delta}):undefined});mount(importRoute,true)
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  expect(screen.queryByRole('region',{name:T.importProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 })
 it.each([401,403])('preserves the native import command HTTP %s authority boundary',async status=>{
  const capabilities=[importCap]
  const calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(importPath))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(status)}return json(importPreview)}})
  mount(importRoute,true);await screen.findByRole('region',{name:T.importProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(document.querySelector(`[data-state="${status===401?'unauthenticated':'not-found'}"]`)).not.toBeNull())
  expect(screen.queryByRole('region',{name:T.importProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
  expect(calls.filter(call=>call.url.includes(importPath)&&call.method==='POST')).toHaveLength(1)
 })
 it('does not restore the import decision into a revoked capability or a different session',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-import-old'});const capabilities=[importCap]
  const calls=installFetch({capabilities,respond:(url,init)=>url.includes(importPath)?init?.method==='POST'?problem(503):json(importPreview):undefined})
  mount(importRoute,true);await screen.findByRole('region',{name:T.importProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  cleanup();capabilities.splice(0);mount(importRoute,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
  cleanup();capabilities.push(importCap);Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-import-new'});mount(importRoute,true);await screen.findByRole('region',{name:T.importProposal})
  expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.url.includes(importPath)&&call.method==='POST')).toHaveLength(1)
 })
})

it('does not transfer retained import proof or its original operation to another company',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-import-company-session'})
 const calls=installFetch({capabilities:[importCap],respond:(url,init)=>{
  if(!url.includes(importPath))return undefined
  const company=(init?.headers as Record<string,string>)?.['X-Company-Id']??COMPANY_A
  const native={...importHuman,batch:{...importBatch,companyId:company},preview:{...importHuman.preview,batch:{...importHuman.preview.batch,companyId:company}}}
  return init?.method==='POST'?problem(503):json({...importPreview,companyId:company,officialInvoiceImport:native})
 }})
 mount(importRoute,true);await screen.findByRole('region',{name:T.importProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
 await waitFor(()=>expect(calls.some(call=>call.url.includes(importPath)&&call.method==='GET'&&call.headers['X-Company-Id']===COMPANY_B)).toBe(true))
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(screen.queryByText(T.importRetained)).toBeNull()
 expect(calls.filter(call=>call.url.includes(importPath)&&call.method==='POST')).toHaveLength(1)
})


const officialKind='finance.official-sales-invoice.v1',officialCap='finance.official-sales-invoice.work-decision'
const officialPath=`/work/decisions/${officialKind}/${id}`,officialRoute=`/onaylarim/${officialKind}/${id}`
const officialHuman={invoiceId:id,companyId:COMPANY_A,officialNo:'SENTETIK-RESMI-001',ettn:'eeeeeeee-0000-4000-8000-000000000040',customerPartyId:'eeeeeeee-0000-4000-8000-000000000041',currencyId:'eeeeeeee-0000-4000-8000-000000000042',payableAmount:'00123.4500',payableFunctionalAmount:'00123.4500',taxBaseFunctionalAmount:'0',vatFunctionalAmount:'0',withholdingFunctionalAmount:'0',pdfDocumentId:'eeeeeeee-0000-4000-8000-000000000043',pdfDocumentVersionNo:7,pdfSha256:'d'.repeat(64),status:'PendingApproval',version:'1',recordedBy:'SENTETIK official recorder',recordedAtUtc:'2026-11-27T08:00:00.0000001Z',approvedBy:null,approvedAtUtc:null,rejectedBy:null,rejectedAtUtc:null,rejectionReason:null,withdrawnBy:null,withdrawnAtUtc:null,invoiceDate:'2026-11-26',sourceDifferences:[{importBatchId:'eeeeeeee-0000-4000-8000-000000000044',sourceDocumentId:'eeeeeeee-0000-4000-8000-000000000045',officialNo:'SENTETIK-CSV-002',ettn:'eeeeeeee-0000-4000-8000-000000000046',fields:['invoiceDate','payableFunctionalAmount'],invoiceDate:'2026-11-25',payableFunctionalAmount:'120.0000',winner:'manual'}],taxWarning:{code:'finance_official_invoice_tax_inconsistent',sourceDocumentId:null,expectedPayableFunctionalAmount:'0',payableFunctionalAmount:'123.4500',differenceFunctionalAmount:'123.4500'}}
const officialRefs=[{role:'customer',recordId:officialHuman.customerPartyId,label:'SENTETIK müşteri',link:null},{role:'currency',recordId:officialHuman.currencyId,label:'SENTETIK para',link:null},{role:'pdf-document',recordId:officialHuman.pdfDocumentId,label:'SENTETIK PDF',link:null}]
const officialPreview={companyId:COMPANY_A,kind:officialKind,itemId:id,documentLabel:officialHuman.officialNo,sourceVersion:'1',pending:true,approvalReasonRequired:false,rejectionSupported:true,references:officialRefs,rules:[{ruleCode:'finance.official-sales-invoice-approval-required',threshold:null,actual:'123.45',deviation:null,unit:null}],officialSalesInvoice:officialHuman}
const officialTerminal={...officialPreview,pending:false,sourceVersion:'2',officialSalesInvoice:{...officialHuman,status:'Approved',version:'2',approvedBy:'SENTETIK official checker',approvedAtUtc:'2026-11-27T09:00:00Z'}}
const officialResult=(approve=true,replayed=false)=>json({companyId:COMPANY_A,kind:officialKind,itemId:id,state:approve?'Approved':'Rejected',replayed,originalCorrelationId:'SENTETIK official original'},200,replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK official original'}:undefined)

describe('WP8 official sales invoice purpose',()=>{
 it.each([true,false])('renders all 28 native human fields and sends only the explicit four-field decision (approve=%s)',async approve=>{
  const calls=installFetch({capabilities:[officialCap],respond:(url,init)=>url.includes(officialPath)?init?.method==='POST'?officialResult(approve):json(officialPreview):undefined})
  mount(officialRoute,true);const region=await screen.findByRole('region',{name:T.officialSalesProposal})
  for(const value of [officialHuman.companyId,officialHuman.ettn,officialHuman.customerPartyId,officialHuman.currencyId,officialHuman.pdfDocumentId,officialHuman.pdfSha256,officialHuman.recordedBy,officialHuman.recordedAtUtc,officialHuman.invoiceDate])expect(within(region).getByText(value)).toBeTruthy()
  expect(within(region).getByText(officialHuman.officialNo)).toBeTruthy();expect(within(region).getAllByText(officialHuman.payableAmount)).toHaveLength(2)
  expect(within(region).getByText(OFFICIAL.statuses.PendingApproval)).toBeTruthy()
  const differences=within(region).getByRole('region',{name:OFFICIAL.differences})
  for(const value of [officialHuman.sourceDifferences[0]!.importBatchId,officialHuman.sourceDifferences[0]!.sourceDocumentId,officialHuman.sourceDifferences[0]!.ettn,'120.0000'])expect(within(differences).getByText(value)).toBeTruthy()
  expect(within(within(region).getByRole('note')).getByText(/123\.4500/)).toBeTruthy()
  const reason=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement
  expect(reason.maxLength).toBe(1000);expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
  if(!approve)fireEvent.change(reason,{target:{value:'  SENTETIK official ret  '}})
  fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.url.includes(officialPath)&&call.method==='POST');expect(sent).toHaveLength(1)
  expect(sent[0]!.body).toEqual({expectedVersion:'1',approve,reason:approve?null:'SENTETIK official ret',delegationId:null})
  expect(calls.some(call=>call.url.includes('/finance/')||call.url.includes('/documents/')||call.url.includes('/cari/'))).toBe(false)
  expect(calls.filter(call=>call.url.includes(officialPath)&&call.method==='GET')).toHaveLength(1)
  expect(screen.queryByRole('region',{name:T.officialSalesProposal})).toBeNull()
 })
 it.each([
  {status:'Approved',approvedBy:'SENTETIK checker',approvedAtUtc:'2026-11-27T09:00:00Z'},
  {status:'Rejected',rejectedBy:'SENTETIK checker',rejectedAtUtc:'2026-11-27T09:00:00Z',rejectionReason:'SENTETIK original reason'},
  {status:'Withdrawn',withdrawnBy:officialHuman.recordedBy,withdrawnAtUtc:'2026-11-27T09:00:00Z'}
 ])('shows terminal2 native facts read-only %j',async change=>{
  const calls=installFetch({capabilities:[officialCap],respond:url=>url.includes(officialPath)?json({...officialPreview,pending:false,sourceVersion:'2',officialSalesInvoice:{...officialHuman,...change,version:'2'}}):undefined})
  mount(officialRoute,true);const region=await screen.findByRole('region',{name:T.officialSalesProposal})
  expect(within(region).getByText(OFFICIAL.statuses[change.status as keyof typeof OFFICIAL.statuses])).toBeTruthy()
  expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull();expect(calls.some(call=>call.method==='POST')).toBe(false)
 })
 it.each([true,false])('restores only original key/version/decision across a terminal preview remount (approve=%s)',async approve=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-official-session'});let posts=0
  const calls=installFetch({capabilities:[officialCap],respond:(url,init)=>url.includes(officialPath)?init?.method==='POST'?++posts===1?problem(503):officialResult(approve,true):json(posts===0?officialPreview:officialTerminal):undefined})
  mount(officialRoute,true);await screen.findByRole('region',{name:T.officialSalesProposal})
  if(!approve)fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK original ret'}})
  fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.unknown)
  const journal=JSON.stringify(sessionStorage);for(const secret of [officialHuman.ettn,officialHuman.pdfSha256,officialHuman.payableAmount,officialHuman.customerPartyId,officialHuman.recordedBy,'officialSalesInvoice'])expect(journal).not.toContain(secret)
  cleanup();mount(officialRoute,true);await screen.findByText(T.unknown);await screen.findByText(T.officialSalesRetained)
  expect(screen.getByText(`${T.version}: 1`)).toBeTruthy();expect(screen.queryByRole('region',{name:T.officialSalesProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done);await screen.findByText(T.priorDecisionResolved)
  const sent=calls.filter(call=>call.url.includes(officialPath)&&call.method==='POST');expect(sent).toHaveLength(2)
  expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.body).toEqual({expectedVersion:'1',approve,reason:approve?null:'SENTETIK original ret',delegationId:null});expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
  expect(calls.filter(call=>call.url.includes(officialPath)&&call.method==='GET')).toHaveLength(1);expect(screen.queryByRole('region',{name:T.officialSalesProposal})).toBeNull()
 })
 it('hides cached and late terminal invoice facts throughout receipt reconciliation and after success',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-official-late'})
  const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}});let gets=0,posts=0,release!:(reply:Response)=>void
  const late=new Promise<Response>(resolve=>{release=resolve})
  const calls=installFetch({capabilities:[officialCap],respond:(url,init)=>{if(!url.includes(officialPath))return undefined;if(init?.method==='POST')return ++posts===1?problem(503):officialResult(true,true);return ++gets===1?json(officialPreview):late}})
  mount(officialRoute,true,client);await screen.findByRole('region',{name:T.officialSalesProposal})
  const matching=(query:{readonly queryKey:readonly unknown[]})=>query.queryKey.includes('work-decision')
  let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:matching})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  await act(async()=>{release(json(officialTerminal));await refetch})
  expect(client.getQueryCache().findAll({predicate:matching})[0]?.state.data).toMatchObject({type:'ok-single',data:{sourceVersion:'2',officialSalesInvoice:{status:'Approved'}}})
  expect(screen.queryByRole('region',{name:T.officialSalesProposal})).toBeNull();expect(screen.getByText(`${T.version}: 1`)).toBeTruthy()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done);await screen.findByText(T.priorDecisionResolved)
  expect(screen.queryByRole('region',{name:T.officialSalesProposal})).toBeNull();expect(gets).toBe(2)
  const sent=calls.filter(call=>call.url.includes(officialPath)&&call.method==='POST');expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it('keeps the native rejection limit without cutting content or requiring a note for approval',async()=>{
  const calls=installFetch({capabilities:[officialCap],respond:(url,init)=>url.includes(officialPath)?init?.method==='POST'?officialResult(false):json(officialPreview):undefined})
  mount(officialRoute,true);await screen.findByRole('region',{name:T.officialSalesProposal})
  const input=screen.getByRole('textbox',{name:T.reason}),reject=screen.getByRole('button',{name:T.reject}) as HTMLButtonElement
  fireEvent.change(input,{target:{value:'x'.repeat(1001)}});expect(reject.disabled).toBe(true)
  expect((screen.getByRole('button',{name:T.approve}) as HTMLButtonElement).disabled).toBe(false)
  fireEvent.change(input,{target:{value:'x'.repeat(1000)}});expect(reject.disabled).toBe(false);fireEvent.click(reject);await screen.findByText(T.done)
  expect(calls.find(call=>call.url.includes(officialPath)&&call.method==='POST')?.body).toEqual({expectedVersion:'1',approve:false,reason:'x'.repeat(1000),delegationId:null})
 })
 it.each([{capabilities:[]},{capabilities:['finance.official-sales-invoices.approve']},{capabilities:['finance.official-sales-invoices.record']},{capabilities:['finance.official-invoice-import.work-decision']}])('requires the exact purpose capability %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(officialPath)?json(officialPreview):undefined});mount(officialRoute,true)
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.includes(officialPath))).toBe(false)
 })
 it.each([{companyId:COMPANY_B},{officialSalesInvoice:null},{officialSalesInvoice:{...officialHuman,payableAmount:'123.4501'}}])('does not render missing, foreign or mismatched invoice proof %j',async change=>{
  installFetch({capabilities:[officialCap],respond:url=>url.includes(officialPath)?json({...officialPreview,...change}):undefined});mount(officialRoute,true)
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.officialSalesProposal})).toBeNull()
 })
 it.each([401,403])('preserves the native official decision HTTP %s boundary',async status=>{
  const capabilities=[officialCap]
  const calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(officialPath))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(status)}return json(officialPreview)}})
  mount(officialRoute,true);await screen.findByRole('region',{name:T.officialSalesProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(document.querySelector(`[data-state="${status===401?'unauthenticated':'not-found'}"]`)).not.toBeNull())
  expect(screen.queryByRole('region',{name:T.officialSalesProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
 })
 it('does not transfer a retained decision to a revoked purpose or a changed authentication session',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-official-old'});const capabilities=[officialCap]
  const calls=installFetch({capabilities,respond:(url,init)=>url.includes(officialPath)?init?.method==='POST'?problem(503):json(officialPreview):undefined})
  mount(officialRoute,true);await screen.findByRole('region',{name:T.officialSalesProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  cleanup();capabilities.splice(0);mount(officialRoute,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
  cleanup();capabilities.push(officialCap);Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-official-new'});mount(officialRoute,true);await screen.findByRole('region',{name:T.officialSalesProposal})
  expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
 })
})

it('does not carry a retained official invoice command into a different company context',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-official-company'})
 const calls=installFetch({capabilities:[officialCap],respond:(url,init)=>{
  if(!url.includes(officialPath))return undefined
  const company=(init?.headers as Record<string,string>)?.['X-Company-Id']??COMPANY_A
  const amount=company===COMPANY_A?officialHuman.payableAmount:'987.6500'
  return init?.method==='POST'?problem(503):json({...officialPreview,companyId:company,rules:[{...officialPreview.rules[0],actual:company===COMPANY_A?'123.45':'987.65'}],officialSalesInvoice:{...officialHuman,companyId:company,payableAmount:amount,payableFunctionalAmount:amount}})
 }})
 mount(officialRoute,true);await screen.findByRole('region',{name:T.officialSalesProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
 await waitFor(()=>expect(calls.some(call=>call.url.includes(officialPath)&&call.method==='GET'&&call.headers['X-Company-Id']===COMPANY_B)).toBe(true))
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(screen.queryByText(T.officialSalesRetained)).toBeNull();expect(screen.queryByText(officialHuman.payableAmount)).toBeNull()
 expect(calls.filter(call=>call.url.includes(officialPath)&&call.method==='POST')).toHaveLength(1)
})

const reversalKinds = ['finance.manual-bank-payment-reversal.v1','finance.manual-receipt-reversal.v1'] as const
const reversalOriginal='dddddddd-0000-4000-8000-000000000001', reversalParty='dddddddd-0000-4000-8000-000000000002', reversalAccount='dddddddd-0000-4000-8000-000000000003', reversalBank='dddddddd-0000-4000-8000-000000000004', reversalCurrency='dddddddd-0000-4000-8000-000000000005', reversalEvent='dddddddd-0000-4000-8000-000000000006'
function reversalPreview(k: typeof reversalKinds[number], companyId: string, itemId: string) {
 const payment=k===reversalKinds[0]
 const proposal={id:itemId,...(payment?{paymentId:reversalOriginal}:{originalSourceId:reversalOriginal}),occurredOn:'0001-01-01',reason:'SENTETIK immutable reversal reason',status:'Pending',proposedBy:'SENTETIK preparer',proposedAtUtc:'0001-01-01T00:00:00Z',decidedBy:null,decidedAtUtc:null,decisionReason:null,...(payment?{reversalEventId:null}:{reversalSourceId:null}),version:'1'}
 const original={partyId:reversalParty,companyAccountId:reversalAccount,treasuryAccountId:reversalBank,currencyId:reversalCurrency,originalEventId:reversalEvent}
 return {companyId,kind:k,itemId,documentLabel:'SENTETIK reversal',sourceVersion:'1',pending:true,approvalReasonRequired:false,rejectionSupported:true,
 references:[[payment?'manual-bank-payment':'manual-receipt',reversalOriginal],[ 'party',reversalParty],['bank-account',reversalBank],['currency',reversalCurrency]].map(([role,recordId])=>({role:role!,recordId:recordId!,label:'SENTETIK '+role,link:recordId===reversalOriginal?(payment?'/finans/odemeler/':'/finans/tahsilatlar/')+recordId:null})),
 rules:[{ruleCode:payment?'finance.manual-bank-payment-reversal-approval-required':'finance.manual-receipt-reversal-approval-required',threshold:null,actual:'123.45',deviation:null,unit:null}],
 ...(payment?{manualBankPaymentReversal:{companyId,proposal,paymentDocumentNumber:'SENTETIK PAY-1',...original,amount:'000123.4500',paymentOccurredOn:'0001-01-01',paymentValueOn:null,paymentRecordedBy:'SENTETIK payment recorder',paymentRecordedAtUtc:'0001-01-01T00:00:00Z',recordedFactsDigest:'a'.repeat(64),treasuryAccountLabel:'SENTETIK named bank'}}:
 {manualReceiptReversal:{companyId,proposal,originalReceipt:{sourceId:reversalOriginal,sourceVersion:'9223372036854775807',documentNumber:'SENTETIK REC-1',...original,amountText:'000123.4500',occurredOn:'0001-01-01',valueOn:null,posCommissionAmountText:'000.000',posBankNetAmountText:'000123.4500',posCommissionBearer:'Company',recordedBy:'SENTETIK receipt recorder',recordedAtUtc:'0001-01-01T00:00:00Z'},treasuryAccountLabel:'SENTETIK named bank'}})}
}

describe('native reversal decisions',()=>{
 it.each(reversalKinds.flatMap(kind=>[true,false].map(approve=>({kind,approve}))))('shows immutable native human values and sends no human proof %j',async({kind:k,approve})=>{
  const ownerPath=`/work/decisions/${k}/${id}`,value=reversalPreview(k,COMPANY_A,id)
  const calls=installFetch({capabilities:[k.replace('.v1','.work-decision')],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:k,itemId:id,state:approve?'Approved':'Rejected',replayed:false,originalCorrelationId:'SENTETIK reversal'}):json(value):undefined})
  mount(`/onaylarim/${k}/${id}`,true);const region=await screen.findByRole('region',{name:T.reversalProposal})
  for(const text of ['000123.4500','SENTETIK named bank','SENTETIK immutable reversal reason','SENTETIK preparer','0001-01-01T00:00:00Z'])expect(within(region).getAllByText(text).length).toBeGreaterThan(0)
  expect(within(region).getByText(reversalAccount)).toBeTruthy();expect(within(region).getByText(reversalEvent)).toBeTruthy()
  expect(screen.queryByText('a'.repeat(64))).toBeNull()
  const input=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement;expect(input.maxLength).toBe(1000)
  expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.change(input,{target:{value:'x'.repeat(1001)}});expect((screen.getByRole('button',{name:T.reject}) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.change(input,{target:{value:'  SENTETIK rejection  '}})
  fireEvent.click(screen.getByRole('button',{name:approve?T.approve:T.reject}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.url.includes(ownerPath)&&call.method==='POST');expect(sent).toHaveLength(1);expect(sent[0]!.body).toEqual({expectedVersion:'1',approve,reason:approve?null:'SENTETIK rejection',delegationId:null})
  expect(screen.queryByRole('region',{name:T.reversalProposal})).toBeNull();expect(calls.some(call=>call.url.includes('/finance/'))).toBe(false)
 })
 it.each(reversalKinds)('restores frozen command without GET, human journal or new maker/version binding: %s',async k=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-reversal-session'})
  let posts=0;const ownerPath=`/work/decisions/${k}/${id}`
  const calls=installFetch({capabilities:[k.replace('.v1','.work-decision')],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):json({companyId:COMPANY_A,kind:k,itemId:id,state:'Rejected',replayed:true,originalCorrelationId:'SENTETIK-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'}):posts?problem(503):json(reversalPreview(k,COMPANY_A,id)):undefined})
  mount(`/onaylarim/${k}/${id}`,true);await screen.findByRole('region',{name:T.reversalProposal});fireEvent.change(screen.getByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK original decision'}});fireEvent.click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.unknown)
  for(const secret of ['000123.4500','SENTETIK named bank','SENTETIK immutable reversal reason',reversalAccount,'manualReceiptReversal','manualBankPaymentReversal'])expect(JSON.stringify(sessionStorage)).not.toContain(secret)
  cleanup();mount(`/onaylarim/${k}/${id}`,true);await screen.findByText(T.unknown);await screen.findByText(T.reversalRetained)
  expect(screen.queryByRole('region',{name:T.reversalProposal})).toBeNull();expect(screen.getByText(`${T.version}: 1`)).toBeTruthy()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.url.includes(ownerPath)&&call.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);expect(calls.filter(call=>call.url.includes(ownerPath)&&call.method==='GET')).toHaveLength(1)
 })
 it.each(reversalKinds)('requires actual purpose capability rather than native browse or another decision: %s',async k=>{
  const calls=installFetch({capabilities:['finance.manual-bank-payment.reverse.approve','finance.manual-receipt.reverse.approve','finance.cash-account.work-decision'],respond:url=>url.includes(`/work/decisions/${k}/`)?json(reversalPreview(k,COMPANY_A,id)):undefined})
  mount(`/onaylarim/${k}/${id}`,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.includes(`/work/decisions/${k}/`))).toBe(false)
 })
})

it.each(reversalKinds)('hides cached and in-flight replacement proof through original reversal receipt reconciliation: %s',async k=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-reversal-late'})
 const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}});let gets=0,posts=0,release!:(reply:Response)=>void
 const late=new Promise<Response>(resolve=>{release=resolve}),ownerPath=`/work/decisions/${k}/${id}`
 const calls=installFetch({capabilities:[k.replace('.v1','.work-decision')],respond:(url,init)=>{
  if(!url.includes(ownerPath))return undefined
  if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:k,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'})
  return ++gets===1?json(reversalPreview(k,COMPANY_A,id)):late
 }})
 mount(`/onaylarim/${k}/${id}`,true,client);await screen.findByRole('region',{name:T.reversalProposal})
 const matching=(query:{readonly queryKey:readonly unknown[]})=>query.queryKey.includes('work-decision')
 let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:matching})});await waitFor(()=>expect(gets).toBe(2))
 fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 const replacement=reversalPreview(k,COMPANY_A,id),h=k===reversalKinds[0]?'manualBankPaymentReversal':'manualReceiptReversal'
 const human=(replacement as unknown as Record<string,{treasuryAccountLabel:string;proposal:{proposedBy:string}}>)[h]!
 human.treasuryAccountLabel='SENTETIK DIFFERENT BANK';human.proposal.proposedBy='SENTETIK DIFFERENT PREPARER'
 await act(async()=>{release(json(replacement));await refetch})
 expect(client.getQueryCache().findAll({predicate:matching})[0]?.state.data).toMatchObject({type:'ok-single',data:{[h]:{treasuryAccountLabel:'SENTETIK DIFFERENT BANK'}}})
 expect(screen.queryByRole('region',{name:T.reversalProposal})).toBeNull();expect(screen.queryByText('SENTETIK DIFFERENT BANK')).toBeNull()
 fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
 expect(screen.queryByRole('region',{name:T.reversalProposal})).toBeNull();expect(gets).toBe(2)
 const sent=calls.filter(call=>call.url.includes(ownerPath)&&call.method==='POST');expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
})
it.each(reversalKinds.flatMap(kind=>[401,403].map(status=>({kind,status}))))('keeps reversal authority loss closed %j',async({kind:k,status})=>{
 const capabilities=[k.replace('.v1','.work-decision')],ownerPath=`/work/decisions/${k}/${id}`
 const calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(status)}return json(reversalPreview(k,COMPANY_A,id))}})
 mount(`/onaylarim/${k}/${id}`,true);await screen.findByRole('region',{name:T.reversalProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}))
 await waitFor(()=>expect(document.querySelector(`[data-state="${status===401?'unauthenticated':'not-found'}"]`)).not.toBeNull())
 expect(screen.queryByRole('region',{name:T.reversalProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
})

it.each(reversalKinds)('does not restore a reversal request in a new session or with a revoked purpose: %s',async k=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK reversal old'})
 const capabilities=[k.replace('.v1','.work-decision')],ownerPath=`/work/decisions/${k}/${id}`
 const calls=installFetch({capabilities,respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(503):json(reversalPreview(k,COMPANY_A,id)):undefined})
 mount(`/onaylarim/${k}/${id}`,true);await screen.findByRole('region',{name:T.reversalProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();capabilities.splice(0);mount(`/onaylarim/${k}/${id}`,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
 cleanup();capabilities.push(k.replace('.v1','.work-decision'));Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK reversal new'});mount(`/onaylarim/${k}/${id}`,true);await screen.findByRole('region',{name:T.reversalProposal})
 expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
})
it.each(reversalKinds)('does not carry original reversal command or human proof into another company: %s',async k=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK reversal company'})
 const ownerPath=`/work/decisions/${k}/${id}`
 const calls=installFetch({capabilities:[k.replace('.v1','.work-decision')],respond:(url,init)=>{
  if(!url.includes(ownerPath))return undefined
  const company=(init?.headers as Record<string,string>)?.['X-Company-Id']??COMPANY_A
  const value=reversalPreview(k,company,id),h=k===reversalKinds[0]?'manualBankPaymentReversal':'manualReceiptReversal'
  const human=(value as unknown as Record<string,{treasuryAccountLabel:string}>)[h]!
  human.treasuryAccountLabel=company===COMPANY_A?'SENTETIK company A bank':'SENTETIK company B bank'
  return init?.method==='POST'?problem(503):json(value)
 }})
 mount(`/onaylarim/${k}/${id}`,true);await screen.findByRole('region',{name:T.reversalProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
 await screen.findByText('SENTETIK company B bank');expect(screen.queryByText('SENTETIK company A bank')).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
})

const expenseKind='finance.expense-payment.v1' as const,expenseCap='finance.expense-payment.work-decision'
function expensePreview(companyId:string,itemId:string,version='9007199254740993') {
 return {companyId,kind:expenseKind,itemId,documentLabel:'SENTETIK expense draft',sourceVersion:version,pending:true,approvalReasonRequired:false,rejectionSupported:false,
 references:[{role:'bank-account',recordId:reversalBank,label:'SENTETIK expense bank',link:null},{role:'expense-item',recordId:reversalParty,label:'SENTETIK expense item',link:null},{role:'currency',recordId:reversalCurrency,label:'SENTETIK currency',link:null}],
 rules:[{ruleCode:'finance.expense-payment-approval-required',threshold:null,actual:'123.45',deviation:null,unit:null}],
 expensePayment:{payment:{id:itemId,companyId,status:'Draft',preparation:{sourceKind:'Bank',treasuryAccountId:reversalBank,expenseItemId:reversalParty,money:{amount:'000123.4500',currencyId:reversalCurrency},profile:{profileId:reversalAccount,version:'3'},occurredOn:'0001-01-01',valueOn:'9999-12-31',reference:'SENTETIK expense reference',description:'SENTETIK private expense description'},preparedBy:'SENTETIK expense preparer',preparedAtUtc:'0001-01-01T00:00:00Z',approvedBy:null,approvedAtUtc:null,recordedBy:null,recordedAtUtc:null,paymentConfirmed:false,version},treasuryAccountLabel:'SENTETIK named expense bank',revisers:['SENTETIK expense editor'],draftFactsDigest:'b'.repeat(64)}}
}

describe('expense payment native approval-only Work purpose',()=>{
 it('shows full native preparation/profile and human timestamps but no digest or payment/record action',async()=>{
  const ownerPath=`/work/decisions/${expenseKind}/${id}`,value=expensePreview(COMPANY_A,id)
  const calls=installFetch({capabilities:[expenseCap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:expenseKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK expense'}):json(value):undefined})
  mount(`/onaylarim/${expenseKind}/${id}`,true);const region=await screen.findByRole('region',{name:T.expenseProposal})
  for(const text of ['000123.4500','SENTETIK named expense bank','SENTETIK expense editor','SENTETIK private expense description','SENTETIK expense reference','0001-01-01','9999-12-31',reversalBank,reversalParty,reversalCurrency,reversalAccount,'0001-01-01T00:00:00Z'])expect(within(region).getAllByText(text).length).toBeGreaterThan(0)
  expect(screen.queryByText('b'.repeat(64))).toBeNull();expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull();expect(screen.queryByRole('button',{name:'Kesin kayda al'})).toBeNull();expect(within(region).queryByRole('checkbox')).toBeNull();expect(screen.getAllByRole('checkbox')).toHaveLength(1);expect(screen.getByRole('checkbox',{name:T.delegation})).toBeTruthy()
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.url.includes(ownerPath)&&call.method==='POST');expect(sent).toHaveLength(1);expect(sent[0]!.body).toEqual({expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null})
  expect(calls.some(call=>call.url.includes('/finance/expense-payments'))).toBe(false);expect(screen.queryByRole('region',{name:T.expenseProposal})).toBeNull()
 })
 it('restores only the same original N/key/body without reading current recorded N+2 facts',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK expense retry'});let posts=0
  const ownerPath=`/work/decisions/${expenseKind}/${id}`,value=expensePreview(COMPANY_A,id)
  const calls=installFetch({capabilities:[expenseCap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):json({companyId:COMPANY_A,kind:expenseKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-expense-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-expense-original'}):posts?problem(503):json(value):undefined})
  mount(`/onaylarim/${expenseKind}/${id}`,true);await screen.findByRole('region',{name:T.expenseProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  for(const secret of ['SENTETIK private expense description','000123.4500','SENTETIK named expense bank','draftFactsDigest','expensePayment'])expect(JSON.stringify(sessionStorage)).not.toContain(secret)
  cleanup();mount(`/onaylarim/${expenseKind}/${id}`,true);await screen.findByText(T.unknown);await screen.findByText(T.expenseRetained)
  expect(screen.queryByRole('region',{name:T.expenseProposal})).toBeNull();expect(screen.getByText(`${T.version}: 9007199254740993`)).toBeTruthy()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.url.includes(ownerPath)&&call.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);expect(calls.filter(call=>call.url.includes(ownerPath)&&call.method==='GET')).toHaveLength(1)
 })
 it('suppresses cached and in-flight later Recorded facts throughout approval reconciliation',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK expense late'})
  const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}});let gets=0,posts=0,release!:(reply:Response)=>void
  const late=new Promise<Response>(resolve=>{release=resolve}),ownerPath=`/work/decisions/${expenseKind}/${id}`,value=expensePreview(COMPANY_A,id)
  const calls=installFetch({capabilities:[expenseCap],respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST')return ++posts===1?problem(503):json({companyId:COMPANY_A,kind:expenseKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-expense-original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-expense-original'});return ++gets===1?json(value):late}})
  mount(`/onaylarim/${expenseKind}/${id}`,true,client);await screen.findByRole('region',{name:T.expenseProposal})
  const matching=(query:{readonly queryKey:readonly unknown[]})=>query.queryKey.includes('work-decision')
  let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:matching})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const payment={...value.expensePayment.payment,status:'Recorded',version:'9007199254740995',approvedBy:'SENTETIK reviewer',approvedAtUtc:'0001-01-01T00:00:00Z',recordedBy:'SENTETIK recorder',recordedAtUtc:'0001-01-01T00:00:00Z',paymentConfirmed:true}
  await act(async()=>{release(json({...value,pending:false,sourceVersion:payment.version,expensePayment:{...value.expensePayment,payment}}));await refetch})
  expect(client.getQueryCache().findAll({predicate:matching})[0]?.state.data).toMatchObject({type:'ok-single',data:{sourceVersion:'9007199254740995',expensePayment:{payment:{status:'Recorded'}}}})
  expect(screen.queryByRole('region',{name:T.expenseProposal})).toBeNull();expect(screen.getByText(`${T.version}: 9007199254740993`)).toBeTruthy()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done);expect(gets).toBe(2)
  const sent=calls.filter(call=>call.url.includes(ownerPath)&&call.method==='POST');expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each([{capabilities:[]},{capabilities:['finance.expense-payments.approve']},{capabilities:['finance.expense-payments.workspace']},{capabilities:['finance.manual-bank-payment.work-decision']}])('does not fetch owner data with missing or unrelated purpose: %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(`/work/decisions/${expenseKind}/`)?json(expensePreview(COMPANY_A,id)):undefined})
  mount(`/onaylarim/${expenseKind}/${id}`,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(call=>call.url.includes(`/work/decisions/${expenseKind}/`))).toBe(false)
 })
 it.each([401,403])('preserves expense approval authority boundary %s',async status=>{
  const capabilities=[expenseCap],ownerPath=`/work/decisions/${expenseKind}/${id}`
  const calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(status)}return json(expensePreview(COMPANY_A,id))}})
  mount(`/onaylarim/${expenseKind}/${id}`,true);await screen.findByRole('region',{name:T.expenseProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(document.querySelector(`[data-state="${status===401?'unauthenticated':'not-found'}"]`)).not.toBeNull());expect(screen.queryByRole('region',{name:T.expenseProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
 })
})

it.each([{companyId:COMPANY_B},{expensePayment:null},{sourceVersion:'1'}])('does not display foreign, absent or mismatched expense proof %j',async change=>{
 installFetch({capabilities:[expenseCap],respond:url=>url.includes(`/work/decisions/${expenseKind}/`)?json({...expensePreview(COMPANY_A,id),...change}):undefined})
 mount(`/onaylarim/${expenseKind}/${id}`,true);await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.expenseProposal})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
})
it('never restores an expense approval across authentication sessions or a revoked purpose',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK expense old'})
 const capabilities=[expenseCap],ownerPath=`/work/decisions/${expenseKind}/${id}`
 const calls=installFetch({capabilities,respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(503):json(expensePreview(COMPANY_A,id)):undefined})
 mount(`/onaylarim/${expenseKind}/${id}`,true);await screen.findByRole('region',{name:T.expenseProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 cleanup();capabilities.splice(0);mount(`/onaylarim/${expenseKind}/${id}`,true);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
 cleanup();capabilities.push(expenseCap);Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK expense new'});mount(`/onaylarim/${expenseKind}/${id}`,true);await screen.findByRole('region',{name:T.expenseProposal});expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
})
it('does not carry frozen expense approval or private description to another company',async()=>{
 Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK expense company'})
 const ownerPath=`/work/decisions/${expenseKind}/${id}`
 const calls=installFetch({capabilities:[expenseCap],respond:(url,init)=>{
  if(!url.includes(ownerPath))return undefined
  const company=(init?.headers as Record<string,string>)?.['X-Company-Id']??COMPANY_A,value=expensePreview(company,id)
  value.expensePayment.payment.preparation.description=company===COMPANY_A?'SENTETIK company A expense':'SENTETIK company B expense'
  return init?.method==='POST'?problem(503):json(value)
 }})
 mount(`/onaylarim/${expenseKind}/${id}`,true);await screen.findByRole('region',{name:T.expenseProposal});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
 await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B);await screen.findByText('SENTETIK company B expense')
 expect(screen.queryByText('SENTETIK company A expense')).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
})


it.each(['finance.bank-credit-limit.v1','finance.opening-balance.v1','finance.opening-balance-reversal.v1'] as const)('mounts the published native Finance purpose only with its actual standalone capability: %s',async k=>{
 const ownerPath=`/work/decisions/${k}/${id}`,capability=k.replace('.v1','.work-decision')
 const route=matchRoutes(workRoutes,`/onaylarim/${k}/${id}`)?.at(-1)?.route
 expect(route?.path).toBe(`onaylarim/${k}/:approvalId`)
 const calls=installFetch({capabilities:[capability],respond:url=>url.includes(ownerPath)?problem(503):undefined})
 mount(`/onaylarim/${k}/${id}`,true)
 await waitFor(()=>expect(calls.filter(call=>call.url.includes(ownerPath)&&call.method==='GET')).toHaveLength(1))
 expect(calls.some(call=>call.url.includes('/finance/'))).toBe(false)
 cleanup()
 const denied=installFetch({capabilities:['finance.credits.workspace','finance.credits.approve','finance.opening-balances.list','finance.opening-balances.approve'],respond:url=>url.includes(ownerPath)?problem(503):undefined})
 mount(`/onaylarim/${k}/${id}`,true)
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
 expect(denied.some(call=>call.url.includes(ownerPath))).toBe(false)
})


it.each([
 ['finance.credit-movement.v1','finance.credit-movement.work-decision'],
 ['finance.cash-exchange-book-value.v1','finance.cash-exchange-book-value.work-decision'],
] as const)('mounts %s through its exact purpose and never native browse capability',async(k,capability)=>{
 const ownerPath=`/work/decisions/${k}/${id}`
 const calls=installFetch({capabilities:[capability],respond:url=>url.includes(ownerPath)?problem(503):undefined})
 mount(`/onaylarim/${k}/${id}`,true)
 await waitFor(()=>expect(calls.filter(call=>call.url.includes(ownerPath)&&call.method==='GET')).toHaveLength(1))
 expect(calls.some(call=>call.url.includes('/finance/'))).toBe(false)
 cleanup()
 const denied=installFetch({capabilities:['finance.credits.workspace','finance.credits.approve','finance.cash-exchange-book-values.approve'],respond:url=>url.includes(ownerPath)?problem(503):undefined})
 mount(`/onaylarim/${k}/${id}`,true)
 await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(denied.some(call=>call.url.includes(ownerPath))).toBe(false)
})


// Paired publication exercises the actual shell router and its purpose capability guard.
function mountJournalPurpose(client?:QueryClient){return mount(`/onaylarim/${journalKind}/${id}`,true,client)}
const journalPeriod='aaaaaaaa-0000-4000-8000-000000000079'
const journalPeriodPage={items:[{periodId:journalPeriod,companyId:COMPANY_A,startsOn:'2026-11-01',endsOn:'2026-11-30',status:'Open',version:2,
 preparedByUserId:'aaaaaaaa-0000-4000-8000-000000000081',preparedAtUtc:'2026-11-01T08:00:00Z',approvedByUserId:'aaaaaaaa-0000-4000-8000-000000000082',approvedAtUtc:'2026-11-01T09:00:00Z',closedByUserId:null,closedAtUtc:null,isOwnProposal:false}],nextCursor:null}
const journalOwnerPath=`/work/decisions/${journalKind}/${id}`
describe('WP8 normal journal published human decision',()=>{
 it('shows every native line and date without proof hashes, and requires an explicit open period choice',async()=>{
  const value=normalJournalFixture(COMPANY_A,id)
  const calls=installFetch({capabilities:[journalCap,'accounting.periods.list'],respond:(url,init)=>url.includes(journalOwnerPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:journalKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK journal'}):json(value):url.includes('/accounting/periods')?json(journalPeriodPage):undefined})
  mountJournalPurpose();const region=await screen.findByRole('region',{name:T.normalJournalProposal})
  expect(within(region).getAllByRole('row')).toHaveLength(3)
  expect(within(region).getAllByText('1250.50')).toHaveLength(2)
  expect(within(region).getByText('SENTETIK plan-3')).toBeTruthy()
  expect(region.textContent).not.toContain(value.normalJournalDraft.sourceFactsDigest)
  expect(region.textContent).not.toContain(value.normalJournalDraft.previewDigest)
  expect(region.textContent).not.toContain(value.normalJournalDraft.draft.accountPlanSourceSha256)
  const select=await screen.findByLabelText(JOURNAL.period) as HTMLSelectElement
  await screen.findByRole('option',{name:/2026-11-01/});expect(select.value).toBe('')
  expect(screen.getByRole('button',{name:T.approve}).matches(':disabled')).toBe(true)
  expect(screen.queryByRole('button',{name:T.reject})).toBeNull();expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull()
  fireEvent.change(select,{target:{value:journalPeriod}})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
  expect(calls.filter(call=>call.method==='POST').map(call=>call.body)).toEqual([{expectedVersion:'7',approve:true,reason:null,delegationId:null,normalJournalDraft:{periodId:journalPeriod}}])
  expect(screen.queryByRole('region',{name:T.normalJournalProposal})).toBeNull()
 })
 it('publishes one dedicated component route before the generic legacy read route',()=>{
  const routes=workRoutes.filter(route=>route.path===`onaylarim/${journalKind}/:approvalId`);expect(routes).toHaveLength(1)
  expect(matchRoutes(shellRoutes,`/onaylarim/${journalKind}/${id}`)?.at(-1)?.route).toBe(routes[0])
 })
 it('keeps the original period/version/key after unknown remount, with no fresh human or period GET',async()=>{
  const value=normalJournalFixture(COMPANY_A,id,'9007199254740993');let posts=0
  const calls=installFetch({capabilities:[journalCap,'accounting.periods.list'],respond:(url,init)=>url.includes(journalOwnerPath)?init?.method==='POST'?++posts===1?problem(503):json({companyId:COMPANY_A,kind:journalKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}):json(value):url.includes('/accounting/periods')?json(journalPeriodPage):undefined})
  const baseFetch=globalThis.fetch
  vi.stubGlobal('fetch',vi.fn((input:RequestInfo|URL,init?:RequestInit)=>String(input).endsWith('/identity/me')?Promise.resolve(json({...IDENTITY,authenticationSessionBinding:'SENTETIK journal session'})):baseFetch(input,init)))
  mountJournalPurpose();await screen.findByRole('option',{name:/2026-11-01/});fireEvent.change(screen.getByLabelText(JOURNAL.period),{target:{value:journalPeriod}})
  await userEvent.setup().click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const reads=calls.filter(call=>call.method==='GET'&&(call.url.includes(journalOwnerPath)||call.url.includes('/accounting/periods'))).length
  cleanup();mountJournalPurpose();await screen.findByText(T.unknown)
  expect(screen.queryByRole('region',{name:T.normalJournalProposal})).toBeNull();expect(screen.queryByLabelText(JOURNAL.period)).toBeNull()
  expect(calls.filter(call=>call.method==='GET'&&(call.url.includes(journalOwnerPath)||call.url.includes('/accounting/periods')))).toHaveLength(reads)
  await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2)
  expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  expect(sent[0].body).toEqual({expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null,normalJournalDraft:{periodId:journalPeriod}})
  expect(JSON.stringify(sessionStorage)).not.toContain(value.normalJournalDraft.sourceFactsDigest)
  expect(JSON.stringify(sessionStorage)).not.toContain('1250.50')
 })
 it('does not fetch either owner or periods when the purpose capability is missing',async()=>{
  const calls=installFetch({capabilities:['accounting.periods.list'],respond:url=>url.includes(journalOwnerPath)?json(normalJournalFixture(COMPANY_A,id)):undefined})
  mountJournalPurpose();await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
  expect(calls.filter(call=>call.url.includes(journalOwnerPath)||call.url.includes('/accounting/periods'))).toHaveLength(0)
 })
 it('does not replace the period-list read permission with approve authority',async()=>{
  const calls=installFetch({capabilities:[journalCap],respond:url=>url.includes(journalOwnerPath)?json(normalJournalFixture(COMPANY_A,id)):undefined})
  mountJournalPurpose();await screen.findByText(JOURNAL.periodListUnavailable)
  expect(screen.getByRole('button',{name:T.approve}).matches(':disabled')).toBe(true)
  expect(calls.some(call=>call.url.includes('/accounting/periods'))).toBe(false)
 })
 it.each([403,503])('does not enable approval when the period read answers %s',async status=>{
  const calls=installFetch({capabilities:[journalCap,'accounting.periods.list'],respond:url=>url.includes(journalOwnerPath)?json(normalJournalFixture(COMPANY_A,id)):url.includes('/accounting/periods')?problem(status):undefined})
  mountJournalPurpose();await waitFor(()=>expect(calls.some(call=>call.url.includes('/accounting/periods'))).toBe(true))
  expect(calls.some(call=>call.method==='POST')).toBe(false)
  const approve=screen.queryByRole('button',{name:T.approve});expect(approve===null||approve.matches(':disabled')).toBe(true)
 })
})


describe('normal journal receipt context and late-read fences',()=>{
 it('hides a cached or late human revision while retrying the original command',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK journal late'})
  const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const value=normalJournalFixture(COMPANY_A,id);let gets=0;let release!:(response:Response)=>void
  const late=new Promise<Response>(resolve=>{release=resolve})
  const calls=installFetch({capabilities:[journalCap,'accounting.periods.list'],respond:(url,init)=>url.includes(journalOwnerPath)?init?.method==='POST'?problem(503):++gets===1?json(value):late:url.includes('/accounting/periods')?json(journalPeriodPage):undefined})
  mountJournalPurpose(client);await screen.findByRole('option',{name:/2026-11-01/});fireEvent.change(screen.getByLabelText(JOURNAL.period),{target:{value:journalPeriod}})
  let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:query=>query.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const newer=normalJournalFixture(COMPANY_A,id,'8');newer.normalJournalDraft.draft.postingPurpose='SENTETIK later private facts'
  await act(async()=>{release(json(newer));await refetch})
  expect(client.getQueryCache().findAll({predicate:query=>query.queryKey.includes('work-decision')})[0]?.state.data).toMatchObject({type:'ok-single',data:{sourceVersion:'8'}})
  expect(screen.queryByText('SENTETIK later private facts')).toBeNull();expect(screen.queryByRole('region',{name:T.normalJournalProposal})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.unknown)
  const sent=calls.filter(call=>call.method==='POST');expect(sent).toHaveLength(2);expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key']);client.clear()
 })
 it.each(['company','session'] as const)('does not restore an unknown journal approval into another %s',async change=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK journal original session'})
  const calls=installFetch({capabilities:[journalCap,'accounting.periods.list'],respond:(url,init)=>{
   if(url.includes(journalOwnerPath)){
    if(init?.method==='POST')return problem(503)
    const current=new Headers(init?.headers).get('X-Company-Id')
    expect(current).not.toBeNull()
    if(current===null)throw new Error('Owner GET requires its captured company header')
    return json(normalJournalFixture(current,id))
   }
   if(url.includes('/accounting/periods'))return json(journalPeriodPage)
   return undefined
  }})
  mountJournalPurpose();await screen.findByRole('option',{name:/2026-11-01/});fireEvent.change(screen.getByLabelText(JOURNAL.period),{target:{value:journalPeriod}})
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  if(change==='company')await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
  else {cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK journal different session'});mountJournalPurpose()}
  await screen.findByRole('region',{name:T.normalJournalProposal})
  expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
 })
})

// Paired routes use the actual shell router, auth boundary and standalone capability.
function mountAccountingPeriod(kind: AccountingPeriodWorkKind,client?:QueryClient){ return mount(`/onaylarim/${kind}/${id}`,true,client) }
describe('Work period paired native decision',()=>{
 it.each([openingPeriodKind,reopeningPeriodKind] as const)('shows complete native human facts without private digests or general reads: %s',async kind=>{
  const value=kind===openingPeriodKind?accountingOpeningFixture(COMPANY_A,id):accountingReopeningFixture(COMPANY_A,id)
  const path=`/work/decisions/${kind}/${id}`
  const calls=installFetch({capabilities:[ACCOUNTING_PERIOD_WORK_CAPABILITY[kind]],respond:(url,init)=>url.includes(path)?init?.method==='POST'?json({companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK period'}):json(value):undefined})
  mountAccountingPeriod(kind);const region=await screen.findByRole('region',{name:kind===openingPeriodKind?T.accountingPeriodTitle:T.accountingReopeningTitle})
  expect(region.textContent).toContain('2026-11-01');expect(region.textContent).toContain('2026-11-30')
  expect(region.textContent).not.toContain('A'.repeat(64));expect(region.textContent).not.toContain('B'.repeat(64))
  if('accountingPeriodReopening'in value){
   expect(region.textContent).toContain(value.accountingPeriodReopening.reopening.reason)
   expect(region.textContent).toContain(value.accountingPeriodReopening.closure.carryVoucherId)
   expect(region.textContent).not.toContain(value.accountingPeriodReopening.closure.sourceHash)
   expect(region.textContent).not.toContain(value.accountingPeriodReopening.sourceFactsDigest)
  }
  expect(screen.queryByRole('button',{name:T.reject})).toBeNull();expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull()
  expect(within(region).queryByRole('combobox')).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'1',approve:true,reason:null,delegationId:null,
   ...(kind===reopeningPeriodKind?{accountingPeriodReopening:{expectedPeriodVersion:'3'}}:{})}])
  expect(calls.some(c=>c.url.includes('/accounting/periods'))).toBe(false)
  expect(screen.queryByRole('region',{name:kind===openingPeriodKind?T.accountingPeriodTitle:T.accountingReopeningTitle})).toBeNull()
 })
 it('withholds reopening approval for the real later-closed rule without offering another operation',async()=>{
  const value=accountingReopeningFixture(COMPANY_A,id);value.accountingPeriodReopening.laterPeriodClosed=true;value.approvalSupported=false
  value.rules.push({...value.rules[0]!,ruleCode:'accounting.period-reopening-later-period-closed'})
  const calls=installFetch({capabilities:[ACCOUNTING_PERIOD_WORK_CAPABILITY[reopeningPeriodKind]],respond:url=>url.includes(`/work/decisions/${reopeningPeriodKind}/${id}`)?json(value):undefined})
  mountAccountingPeriod(reopeningPeriodKind);await screen.findByText(T.accountingLaterClosed)
  expect(screen.getByRole('button',{name:T.approve}).matches(':disabled')).toBe(true)
  expect(screen.queryByRole('button',{name:T.reject})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each([openingPeriodKind,reopeningPeriodKind] as const)('reconciles the frozen original versions/key across unknown remount without another GET: %s',async kind=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK period session'})
  const value=kind===openingPeriodKind?accountingOpeningFixture(COMPANY_A,id,'9007199254740993'):accountingReopeningFixture(COMPANY_A,id,'9007199254740993')
  const path=`/work/decisions/${kind}/${id}`;let posts=0
  const calls=installFetch({capabilities:[ACCOUNTING_PERIOD_WORK_CAPABILITY[kind]],respond:(url,init)=>url.includes(path)?init?.method==='POST'?++posts===1?problem(503):json({companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK period original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK period original'}):json(value):undefined})
  mountAccountingPeriod(kind);await screen.findByRole('region',{name:kind===openingPeriodKind?T.accountingPeriodTitle:T.accountingReopeningTitle})
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const reads=calls.filter(c=>c.url.includes(path)&&c.method==='GET').length
  cleanup();mountAccountingPeriod(kind);await screen.findByText(T.unknown)
  expect(screen.queryByRole('region',{name:T.accountingReopeningTitle})).toBeNull();expect(screen.queryByRole('region',{name:T.accountingPeriodTitle})).toBeNull()
  expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET')).toHaveLength(reads)
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body)
  expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
  expect(sent[0]!.body).toEqual({expectedVersion:kind===openingPeriodKind?'9007199254740993':'1',approve:true,reason:null,delegationId:null,
   ...(kind===reopeningPeriodKind?{accountingPeriodReopening:{expectedPeriodVersion:'9007199254740993'}}:{})})
  expect(JSON.stringify(sessionStorage)).not.toContain('sourceFactsDigest');expect(JSON.stringify(sessionStorage)).not.toContain('SENTETIK yeniden açma gerekçesi')
 })
 it.each([openingPeriodKind,reopeningPeriodKind] as const)('does not read with only legacy/native or missing approval capabilities: %s',async kind=>{
  const path=`/work/decisions/${kind}/${id}`
  const calls=installFetch({capabilities:['accounting.periods.list','accounting.periods.approve','accounting.periods.reopen'],respond:url=>url.includes(path)?problem(503):undefined})
  mountAccountingPeriod(kind);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(calls.some(c=>c.url.includes(path))).toBe(false)
 })
 it.each([403,503])('withholds facts/commands on owner %s',async status=>{
  const calls=installFetch({capabilities:[ACCOUNTING_PERIOD_WORK_CAPABILITY[openingPeriodKind]],respond:url=>url.includes(`/work/decisions/${openingPeriodKind}/${id}`)?problem(status):undefined})
  mountAccountingPeriod(openingPeriodKind);await waitFor(()=>expect(calls.some(c=>c.url.includes('/work/decisions/'))).toBe(true))
  expect(screen.queryByRole('region',{name:T.accountingPeriodTitle})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('never renders a late current period while the original unknown request is frozen',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK period late'})
  const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}}),path=`/work/decisions/${reopeningPeriodKind}/${id}`
  let gets=0,release!:(value:Response)=>void;const late=new Promise<Response>(resolve=>{release=resolve})
  const calls=installFetch({capabilities:[ACCOUNTING_PERIOD_WORK_CAPABILITY[reopeningPeriodKind]],respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):++gets===1?json(accountingReopeningFixture(COMPANY_A,id)):late:undefined})
  mountAccountingPeriod(reopeningPeriodKind,client);await screen.findByRole('region',{name:T.accountingReopeningTitle})
  let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const changed=accountingReopeningFixture(COMPANY_A,id,'5');changed.accountingPeriodReopening.reopening.reason='SENTETIK newer private reason'
  await act(async()=>{release(json(changed));await refetch})
  expect(screen.queryByText('SENTETIK newer private reason')).toBeNull();expect(screen.queryByRole('region',{name:T.accountingReopeningTitle})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.unknown)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each(['company','session'] as const)('never restores an unknown period request across %s',async change=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK period first session'})
  const path=`/work/decisions/${openingPeriodKind}/${id}`
  const calls=installFetch({capabilities:[ACCOUNTING_PERIOD_WORK_CAPABILITY[openingPeriodKind]],respond:(url,init)=>{
   if(!url.includes(path))return undefined
   if(init?.method==='POST')return problem(503)
   const company=new Headers(init?.headers).get('X-Company-Id');if(company===null)throw new Error('Captured company required')
   return json(accountingOpeningFixture(company,id))
  }})
  mountAccountingPeriod(openingPeriodKind);await screen.findByRole('region',{name:T.accountingPeriodTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  if(change==='company')await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
  else {cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK period new session'});mountAccountingPeriod(openingPeriodKind)}
  await screen.findByRole('region',{name:T.accountingPeriodTitle});expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
})

function mountJournalReversalCandidate(client?:QueryClient){ return mount(`/onaylarim/${journalReversalKind}/${id}`,true,client) }
const journalReversalPath=`/work/decisions/${journalReversalKind}/${id}`
describe('paired native journal reversal Work purpose',()=>{
 it('shows complete native facts but no hashes, period selection, rejection or new reason',async()=>{
  const value=journalReversalFixture(COMPANY_A,id)
  const calls=installFetch({capabilities:[journalReversalCap],respond:(url,init)=>url.includes(journalReversalPath)?init?.method==='POST'?json({companyId:COMPANY_A,kind:journalReversalKind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK reversal'}):json(value):undefined})
  mountJournalReversalCandidate();const region=await screen.findByRole('region',{name:T.journalReversalTitle})
  expect(within(region).getByText(value.accountingJournalReversal.proposal.reason)).toBeTruthy()
  expect(within(region).getByText('2026-11-25')).toBeTruthy();expect(within(region).getByText('2026-11-26')).toBeTruthy()
  expect(region.querySelectorAll('dt')).toHaveLength(16)
  expect(region.textContent).not.toContain(value.accountingJournalReversal.sourceFactsDigest);expect(region.textContent).not.toContain(value.accountingJournalReversal.previewDigest)
  expect(screen.queryByRole('button',{name:T.reject})).toBeNull();expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull()
  expect(within(region).queryByRole('combobox')).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'1',approve:true,reason:null,delegationId:null}])
  expect(calls.some(c=>c.url.includes('/accounting/'))).toBe(false);expect(screen.queryByRole('region',{name:T.journalReversalTitle})).toBeNull()
 })
 it('mounts one dedicated Work decision before the legacy reader route',()=>{
  const routes=workRoutes.filter(route=>route.path===`onaylarim/${journalReversalKind}/:approvalId`);expect(routes).toHaveLength(1)
  expect(matchRoutes(shellRoutes,`/onaylarim/${journalReversalKind}/${id}`)?.at(-1)?.route).toBe(routes[0])
 })
 it('retains the same original four fields and key across unknown remount without another human GET',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK journal reversal session'})
  const value=journalReversalFixture(COMPANY_A,id);let posts=0
  const calls=installFetch({capabilities:[journalReversalCap],respond:(url,init)=>url.includes(journalReversalPath)?init?.method==='POST'?++posts===1?problem(503):json({companyId:COMPANY_A,kind:journalReversalKind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}):json(value):undefined})
  mountJournalReversalCandidate();await screen.findByRole('region',{name:T.journalReversalTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const reads=calls.filter(c=>c.url.includes(journalReversalPath)&&c.method==='GET').length
  cleanup();mountJournalReversalCandidate();await screen.findByText(T.unknown)
  expect(calls.filter(c=>c.url.includes(journalReversalPath)&&c.method==='GET')).toHaveLength(reads);expect(screen.queryByRole('region',{name:T.journalReversalTitle})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body)
  expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
  expect(JSON.stringify(sessionStorage)).not.toContain('sourceFactsDigest');expect(JSON.stringify(sessionStorage)).not.toContain(value.accountingJournalReversal.proposal.reason)
 })
 it('suppresses an already inflight current preview after the command becomes unknown',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK journal reversal late'})
  const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  let gets=0,release!:(value:Response)=>void;const late=new Promise<Response>(resolve=>{release=resolve})
  const calls=installFetch({capabilities:[journalReversalCap],respond:(url,init)=>url.includes(journalReversalPath)?init?.method==='POST'?problem(503):++gets===1?json(journalReversalFixture(COMPANY_A,id)):late:undefined})
  mountJournalReversalCandidate(client);await screen.findByRole('region',{name:T.journalReversalTitle})
  let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const current=journalReversalFixture(COMPANY_A,id,2);current.pending=false;Object.assign(current.accountingJournalReversal.proposal,{status:'Approved',reversalJournalId:COMPANY_A,decidedBy:COMPANY_A,decidedAtUtc:'2026-11-27T10:00:00Z',reason:'SENTETIK late private reason'})
  await act(async()=>{release(json(current));await refetch})
  expect(screen.queryByText('SENTETIK late private reason')).toBeNull();expect(screen.queryByRole('region',{name:T.journalReversalTitle})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.unknown)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each([403,503])('does not show facts or dispatch on owner refusal %s',async status=>{
  const calls=installFetch({capabilities:[journalReversalCap],respond:url=>url.includes(journalReversalPath)?problem(status):undefined})
  mountJournalReversalCandidate();await waitFor(()=>expect(calls.some(c=>c.url.includes(journalReversalPath))).toBe(true))
  expect(screen.queryByRole('region',{name:T.journalReversalTitle})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('never reads the purpose using native approval or general journal read alone',async()=>{
  const calls=installFetch({capabilities:['accounting.journal-reversals.approve','accounting.journals.detail'],respond:url=>url.includes(journalReversalPath)?problem(503):undefined})
  mountJournalReversalCandidate();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(calls.some(c=>c.url.includes(journalReversalPath))).toBe(false)
 })
 it.each(['company','session'] as const)('does not restore the frozen reversal request into another %s',async change=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK reversal first session'})
  const calls=installFetch({capabilities:[journalReversalCap],respond:(url,init)=>{
   if(!url.includes(journalReversalPath))return undefined
   if(init?.method==='POST')return problem(503)
   const company=new Headers(init?.headers).get('X-Company-Id');if(company===null)throw new Error('Captured company required')
   return json(journalReversalFixture(company,id))
  }})
  mountJournalReversalCandidate();await screen.findByRole('region',{name:T.journalReversalTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  if(change==='company')await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
  else {cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK reversal new session'});mountJournalReversalCandidate()}
  await screen.findByRole('region',{name:T.journalReversalTitle});expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
})

it('shows full native Int32-max journal revision but cannot send a nonincrementable approval',async()=>{
 const value=journalReversalFixture(COMPANY_A,id,2147483647)
 const calls=installFetch({capabilities:[journalReversalCap],respond:url=>url.includes(journalReversalPath)?json(value):undefined})
 mountJournalReversalCandidate();await screen.findByRole('region',{name:T.journalReversalTitle})
 expect(screen.getByRole('button',{name:T.approve}).matches(':disabled')).toBe(true)
 fireEvent.click(screen.getByRole('button',{name:T.approve}));expect(calls.some(c=>c.method==='POST')).toBe(false)
})
