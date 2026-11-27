import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import { decodeWorkDecisionPreview, sendWorkDecision } from './workDecisionApi'

const id='cccccccc-0000-4000-8000-0000000000f1',kind='finance.credit-account.v1',cap='finance.credit-account.work-decision'
const clients:QueryClient[]=[]
const preview={companyId:COMPANY_A,kind,itemId:id,documentLabel:'SENTETIK kredi önerisi',sourceVersion:'1',pending:true,approvalReasonRequired:false,references:[],rules:[]}
function mount(){
 const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}});clients.push(client)
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>)
}
afterEach(()=>{cleanup();clients.splice(0).forEach(client=>client.clear());vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})

describe('WP8 native credit account decision',()=>{
 it('opens for the purpose-only approver, requires the native 500 reason and forwards it without cutting',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes('/work/decisions/')?init?.method==='POST'
   ?json({companyId:COMPANY_A,kind,itemId:id,state:'Rejected',replayed:false,originalCorrelationId:'SENTETIK'}):json(preview):undefined})
  mount();await screen.findByText(preview.documentLabel,{exact:false})
  const reject=screen.getByRole('button',{name:T.reject}) as HTMLButtonElement
  expect(reject.disabled).toBe(true)
  const reason=screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement
  expect(reason.maxLength).toBe(500)
  const text='R'.repeat(500)
  await userEvent.setup().click(reason)
  await userEvent.setup().paste(text)
  await userEvent.setup().click(reject);await screen.findByText(T.done)
  expect(calls.find(call=>call.method==='POST')?.body).toEqual({expectedVersion:'1',approve:false,reason:text,delegationId:null})
  expect(calls.some(call=>call.url.includes('/finance/credit-accounts'))).toBe(false)
 })
 it('replays an unknown approval with its same key/body/version after a completed projection remount',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-credit-decisions'})
  let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{
   if(!url.includes('/work/decisions/'))return undefined
   if(init?.method==='POST')return ++posts===1?problem(503):new Response(JSON.stringify({companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK-original'}),
    {status:200,headers:{'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'}})
   return json(posts?{...preview,sourceVersion:'2',pending:false}:preview)
  }})
  mount();await screen.findByText(preview.documentLabel,{exact:false});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  await screen.findByText(T.unknown);cleanup();mount();await screen.findByText(T.closed);await screen.findByText(T.unknown)
  await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  const sent=calls.filter(call=>call.method==='POST')
  expect(sent).toHaveLength(2);expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
  expect(sent[1].body).toMatchObject({expectedVersion:'1',approve:true,reason:null})
 })
 it('does not fetch the owner without its purpose capability and does not grant the native workspace',async()=>{
  const calls=installFetch({capabilities:['finance.credits.approve']});mount()
  await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
  expect(calls.some(call=>call.url.includes('/work/decisions/'))).toBe(false)
  expect(screen.queryByText(preview.documentLabel,{exact:false})).toBeNull()
 })
 it('handles native server denial through the auth boundary without another command or a native workspace fetch',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes('/work/decisions/')?init?.method==='POST'?problem(403):json(preview):undefined})
  mount();await screen.findByText(preview.documentLabel,{exact:false});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(screen.queryByText(preview.documentLabel,{exact:false})).toBeNull()
  expect(calls.filter(call=>call.method==='POST')).toHaveLength(1)
  expect(calls.some(call=>call.url.includes('/finance/credit-accounts'))).toBe(false)
 })
 it('rejects foreign company, version/shape drift and mismatched owner/replay responses',async()=>{
  expect(decodeWorkDecisionPreview(preview,COMPANY_A,kind,id)).not.toBeNull()
  for(const delta of [{companyId:COMPANY_B},{itemId:COMPANY_A},{sourceVersion:'0'},{approvalReasonRequired:true},{extra:true}])
   expect(decodeWorkDecisionPreview({...preview,...delta},COMPANY_A,kind,id)).toBeNull()
  for(const delta of [{companyId:COMPANY_B},{itemId:COMPANY_A},{state:'Proposed'},{state:'Finalized'},{kind:'finance.cash-account.v1'},{replayed:true}]){
   vi.stubGlobal('fetch',vi.fn(async()=>json({companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:false,originalCorrelationId:'SENTETIK',...delta})))
   expect((await sendWorkDecision(COMPANY_A,kind,id,{expectedVersion:'1',approve:true,reason:null,delegationId:null},'SENTETIK-key')).type).toBe('unknown')
  }
 })
})
