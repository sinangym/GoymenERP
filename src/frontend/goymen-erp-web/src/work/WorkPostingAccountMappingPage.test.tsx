import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { AppShell } from '../shell/AppShell'
import { CapabilityRoute } from '../shell/CapabilityRoute'
import { ShellProvider } from '../shell/ShellProvider'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { WorkDecisionPage } from './WorkDecisionPage'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import { WORK_MAPPING_TEXT as C } from './workPostingAccountMappingText'
import { POSTING_MAPPING_PURPOSE as staged } from './workPostingAccountMappingDecision'
import { postingMappingFixture as fixture } from './postingAccountMappingDecisionFixture'
import { workRoutes } from './workRoutes'
const nativeId='9007199254740993',id=fixture(COMPANY_A,nativeId).itemId,kind=staged.kind,cap=staged.capabilityId,path=`/work/decisions/${kind}/${id}`
afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
// The real shell entry gate is retained alongside the exact paired-route assertions.
function mount(client = new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})) {
  const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:[{path:staged.routePath.slice(1),
    element:<CapabilityRoute capability={[cap]}><WorkDecisionPage kind={kind}/></CapabilityRoute>}]}],{initialEntries:[staged.routePath.replace(':approvalId',id)]})
  render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return {router,client}
}
describe('paired Work posting mapping purpose',()=>{
  it('shows full historical rows and separate current plan before approve-only action without general reads',async()=>{
    const value=fixture(COMPANY_A,nativeId)
    const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?
      json({companyId:COMPANY_A,kind,itemId:id,state:'Active',replayed:false,originalCorrelationId:'SENTETIK activation'}):json(value):undefined})
    mount();const region=await screen.findByRole('region',{name:C.title})
    expect(within(region).getByRole('table',{name:C.accounts}).querySelectorAll('tbody tr')).toHaveLength(13)
    expect(within(region).getByRole('table',{name:C.rows}).querySelectorAll('tbody tr')).toHaveLength(13)
    expect(within(region).getByText('SENTETIK HISTORICAL-13')).toBeTruthy();expect(within(region).getByText('SENTETIK current account 13')).toBeTruthy()
    expect(within(region).getByText('Type12')).toBeTruthy();expect(within(region).getByText('Line12')).toBeTruthy();expect(within(region).getByText('VAT-12')).toBeTruthy()
    expect(within(region).getByText('SENTETIK prepared plan')).toBeTruthy();expect(within(region).getByText('SENTETIK current replacement plan')).toBeTruthy()
    expect(region.textContent).not.toContain(value.postingAccountMapping.sourceFactsDigest);expect(region.textContent).not.toContain(value.postingAccountMapping.previewDigest)
    expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
    expect(screen.getByRole('button',{name:T.approve}).matches(':disabled')).toBe(false)
    fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
    expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'1',approve:true,reason:null,delegationId:null,postingAccountMappingSelection:{expectedActiveMappingVersion:'0'}}])
    expect(calls.some(c=>c.url.includes('/accounting/'))).toBe(false);expect(screen.queryByRole('region',{name:C.title})).toBeNull()
    expect(workRoutes.filter(r=>r.path?.startsWith('onaylarim/')&&r.path.endsWith('/:approvalId')&&r.path!=='onaylarim/:kind/:approvalId')).toHaveLength(58)
    expect(workRoutes.some(r=>r.path===staged.routePath.slice(1))).toBe(true)
  })
  it('restores only the original two versions/body/key after unknown remount, without current GET or human data in storage',async()=>{
    Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK mapping original session'});let posts=0
    const value=fixture(COMPANY_A,nativeId,'9007199254740992')
    const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?++posts===1?problem(503):
      json({companyId:COMPANY_A,kind,itemId:id,state:'Active',replayed:true,originalCorrelationId:'SENTETIK original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}):
      json(posts?fixture(COMPANY_A,nativeId,'9223372036854775806','Superseded'):value):undefined})
    mount();await screen.findByRole('region',{name:C.title});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
    const gets=calls.filter(c=>c.url.includes(path)&&c.method==='GET').length
    const journal=JSON.stringify(sessionStorage)
    for(const secret of ['SENTETIK HISTORICAL','SENTETIK current account','sourceFactsDigest',value.postingAccountMapping.mapping.accountPlanSourceSha256])expect(journal).not.toContain(secret)
    cleanup();mount();await screen.findByText(T.unknown);expect(screen.getByText(C.retained)).toBeTruthy()
    expect(screen.queryByRole('region',{name:C.title})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
    expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET')).toHaveLength(gets)
    fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
    await waitFor(()=>expect(screen.queryByRole('button',{name:T.retry})).toBeNull())
    expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET')).toHaveLength(gets)
    const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body)
    expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
    expect(sent[0]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null,postingAccountMappingSelection:{expectedActiveMappingVersion:'9007199254740992'}})
  })
  it('suppresses a late current human response after unknown and reconciles without substituting its mapping',async()=>{
    Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK mapping late'});let gets=0,release!:(value:Response)=>void
    const late=new Promise<Response>(resolve=>{release=resolve}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
    const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):++gets===1?json(fixture(COMPANY_A,nativeId)):late:undefined})
    mount(client);await screen.findByRole('region',{name:C.title})
    let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
    fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
    const current=fixture(COMPANY_A,nativeId,'9223372036854775806','Superseded');Object.assign(current.postingAccountMapping.currentActiveAccountPlan.accounts[0]!,{name:'SENTETIK LATE CURRENT NAME'})
    await act(async()=>{release(json(current));await refetch})
    expect(screen.queryByText('SENTETIK LATE CURRENT NAME')).toBeNull();expect(screen.queryByRole('region',{name:C.title})).toBeNull()
    fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.unknown)
    const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body)
    expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
  })
  it.each([403,503])('shows no human or action for owner refusal %s',async status=>{
    const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?problem(status):undefined})
    mount();await waitFor(()=>expect(calls.some(c=>c.url.includes(path))).toBe(true))
    expect(screen.queryByRole('region',{name:C.title})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
    expect(calls.some(c=>c.method==='POST')).toBe(false)
  })
  it.each(['foreign','missing','private'])('refuses unbound or private human shape %s',async fault=>{
    const value=fixture(COMPANY_A,nativeId)
    const raw=fault==='foreign'?{...value,companyId:COMPANY_B}:fault==='missing'?{...value,postingAccountMapping:undefined}:
      {...value,postingAccountMapping:{...value.postingAccountMapping,activationReceipt:{}}}
    const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(raw):undefined});mount()
    await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('region',{name:C.title})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
  })
  it.each([{capabilities:[]},{capabilities:['accounting.posting-mappings.activate']},{capabilities:['accounting.account-plan']}])('does not read or restore without the exact purpose capability: %j',async({capabilities})=>{
    const calls=installFetch({capabilities,respond:url=>url.includes(path)?json(fixture(COMPANY_A,nativeId)):undefined});mount()
    await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(calls.some(c=>c.url.includes(path))).toBe(false)
  })
  it('displays completed native rows read-only without a fresh activation',async()=>{
    const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(fixture(COMPANY_A,nativeId,'9223372036854775807','Superseded')):undefined})
    mount();await screen.findByRole('region',{name:C.title});expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
    expect(calls.some(c=>c.method==='POST')).toBe(false)
  })
  it('drops old unknown controls when the purpose is revoked or the authentication session changes',async()=>{
    Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK mapping old'});const capabilities=[cap]
    const calls=installFetch({capabilities,respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):json(fixture(COMPANY_A,nativeId)):undefined})
    mount();await screen.findByRole('region',{name:C.title});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
    cleanup();capabilities.splice(0);mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
    cleanup();capabilities.push(cap);Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK mapping new'});mount()
    await screen.findByRole('region',{name:C.title});expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
  })
  it('preserves the ordinary POST403 boundary instead of offering a bypass retry',async()=>{
    const capabilities=[cap]
    const calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(path))return undefined
      if(init?.method==='POST'){capabilities.splice(0);return problem(403)}return json(fixture(COMPANY_A,nativeId))}})
    mount();await screen.findByRole('region',{name:C.title});fireEvent.click(screen.getByRole('button',{name:T.approve}))
    await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByRole('region',{name:C.title})).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
    expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
  })
  it('cannot restore a pending mapping command into another company even in the same authenticated session',async()=>{
    Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK mapping company boundary'})
    const calls=installFetch({capabilities:[cap],respond:(url,init)=>{
      if(!url.includes(path))return undefined
      if(init?.method==='POST')return problem(503)
      const company=new Headers(init?.headers).get('X-Company-Id');if(company===null)throw new Error('Captured company required')
      return json(fixture(company,nativeId))
    }})
    mount();await screen.findByRole('region',{name:C.title});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
    await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
    await screen.findByRole('region',{name:C.title});expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
    expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
    expect(calls.some(c=>c.url.includes(path)&&c.method==='GET'&&c.headers['X-Company-Id']===COMPANY_B)).toBe(true)
  })
})
