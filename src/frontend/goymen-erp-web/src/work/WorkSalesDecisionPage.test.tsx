import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { ShellProvider } from '../shell/ShellProvider'
import { AppShell } from '../shell/AppShell'
import { COMPANY_A, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { workRoutes } from './workRoutes'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import { decodeWorkDecisionPreview, sendWorkDecision } from './workDecisionApi'

const id='cccccccc-0000-4000-8000-0000000000f1'
const kinds=['sales.order-draft.v1','sales.invoice.v1','sales.subcontract-invoice.v1'] as const
const clients:QueryClient[]=[]
function mount(kind:typeof kinds[number]){
  const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}});clients.push(client)
  const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
  render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>)
}
const preview=(kind:string)=>({companyId:COMPANY_A,kind,itemId:id,documentLabel:'SENTETIK sales',sourceVersion:'7',pending:true,approvalReasonRequired:false,references:[],rules:[]})
afterEach(()=>{cleanup();clients.splice(0).forEach(client=>client.clear());vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})

describe('WP8 native Sales purpose decisions',()=>{
  it.each(kinds)('opens %s under its decision capability and retains native optional rejection reason',async kind=>{
    const calls=installFetch({capabilities:[kind.replace('.v1','.work-decision')],respond:(url,init)=>url.includes('/work/decisions/')
      ?init?.method==='POST'?json({companyId:COMPANY_A,kind,itemId:id,state:'Rejected',replayed:false,originalCorrelationId:'SENTETIK'}):json(preview(kind)):undefined})
    mount(kind);await screen.findByText('SENTETIK sales',{exact:false})
    expect((screen.getByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).maxLength).toBe(4000)
    await userEvent.setup().click(screen.getByRole('button',{name:T.reject}));await screen.findByText(T.done)
    expect(calls.find(call=>call.method==='POST')?.body).toEqual({expectedVersion:'7',approve:false,reason:null,delegationId:null})
    expect(calls.some(call=>call.url.includes('/sales/drafts')||call.url.includes('/sales/invoices'))).toBe(false)
  })
  it.each(kinds)('retains the old %s version/key after an unknown result and a later completed projection',async kind=>{
    Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-sales-decisions'})
    let posts=0
    const calls=installFetch({capabilities:[kind.replace('.v1','.work-decision')],respond:(url,init)=>{
      if(!url.includes('/work/decisions/'))return undefined
      if(init?.method==='POST')return ++posts===1?problem(503):new Response(JSON.stringify({companyId:COMPANY_A,kind,itemId:id,state:kind==='sales.order-draft.v1'?'Rejected':'Approved',replayed:true,originalCorrelationId:'SENTETIK-original'}),
        {status:200,headers:{'Content-Type':'application/json','Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original'}})
      return json(posts?{...preview(kind),sourceVersion:posts>=2?'9':'8',pending:false,documentLabel:posts>=2?'SENTETIK güncel karar sonrası':preview(kind).documentLabel}:preview(kind))
    }})
    mount(kind);await screen.findByText('SENTETIK sales',{exact:false});await userEvent.setup().click(screen.getByRole('button',{name:T.approve}))
    await screen.findByText(T.unknown);cleanup();mount(kind);await screen.findByText(T.unknown)
    await userEvent.setup().click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
    await screen.findByText('SENTETIK güncel karar sonrası',{exact:false})
    const sent=calls.filter(call=>call.method==='POST')
    expect(sent).toHaveLength(2);expect(sent[1].body).toEqual(sent[0].body);expect(sent[1].headers['Idempotency-Key']).toBe(sent[0].headers['Idempotency-Key'])
    expect(sent[1].body).toMatchObject({expectedVersion:'7'})
  })
  it.each(kinds)('refuses mismatched %s identity, scope, version and replay results',async kind=>{
    expect(decodeWorkDecisionPreview(preview(kind),COMPANY_A,kind,id)).not.toBeNull()
    for(const delta of [{companyId:id},{itemId:COMPANY_A},{sourceVersion:'0'},{approvalReasonRequired:true},{extra:true}])
      expect(decodeWorkDecisionPreview({...preview(kind),...delta},COMPANY_A,kind,id)).toBeNull()
    vi.stubGlobal('fetch',vi.fn(async()=>json({companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed:true,originalCorrelationId:'SENTETIK'})))
    expect((await sendWorkDecision(COMPANY_A,kind,id,{expectedVersion:'7',approve:true,reason:null,delegationId:null},'SENTETIK-key')).type).toBe('unknown')
  })
  it.each(kinds)('does not fetch %s without its capability',async kind=>{
    const calls=installFetch({capabilities:[]});mount(kind)
    await waitFor(()=>expect(calls.some(call=>call.url.endsWith('/shell/capabilities'))).toBe(true))
    expect(calls.some(call=>call.url.includes('/work/decisions/'))).toBe(false)
  })
})
