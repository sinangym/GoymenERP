import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, installFetch, json, problem } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
const id='cccccccc-0000-4000-8000-0000000000f1',kind='finance.cash-account.v1',cap='finance.cash-account.work-decision'
const path=`/work/decisions/${kind}/${id}`
const preview={companyId:COMPANY_A,kind,itemId:id,documentLabel:'SENTETIK kasa',sourceVersion:'7',pending:true,approvalReasonRequired:false,references:[],rules:[]}
afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear()})
function mount(route=`/onaylarim/${kind}/${id}`){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[route]})
 render(<ShellProvider queryClient={new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})}><RouterProvider router={router}/></ShellProvider>)
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
