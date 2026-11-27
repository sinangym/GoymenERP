import {afterEach,describe,expect,it,vi} from 'vitest'
import {act,cleanup,fireEvent,render,screen,waitFor,within} from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {QueryClient} from '@tanstack/react-query'
import {RouterProvider,createMemoryRouter} from 'react-router'
import {AppShell} from '../shell/AppShell'
import {CapabilityRoute} from '../shell/CapabilityRoute'
import {ShellProvider} from '../shell/ShellProvider'
import {COMPANY_A,COMPANY_B,IDENTITY,installFetch,json,problem} from '../settings/testHarness'
import {WorkDecisionPage} from './WorkDecisionPage'
import {WORK_DECISION_TEXT as T} from './workDecisionText'
import {MANUFACTURING_COST_WORK_PURPOSE as staged} from './workManufacturingCostDifferenceDecision'
import {manufacturingCostFixture as fixture,manufacturingCostId as id} from './manufacturingCostDifferenceDecisionFixture'
import {workRoutes} from './workRoutes'
const kind=staged.kind,cap=staged.capabilityId,path=`/work/decisions/${kind}/${id}`
afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
// Candidate-only real AppShell/auth/CapabilityRoute harness; no production route is added by this core slice.
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:[{path:staged.routePath.slice(1),element:<CapabilityRoute capability={[cap]}><WorkDecisionPage kind={kind}/></CapabilityRoute>}]}],{initialEntries:[staged.routePath.replace(':approvalId',id)]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return {router,client}
}
describe('staged manufacturing cost Work human decision',()=>{
 it('shows every complete output and retained plan with raw amounts/reason, approve only and no owner browse',async()=>{
  const value=fixture(COMPANY_A)
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?json({companyId:COMPANY_A,kind,itemId:id,state:'Posted',replayed:false,originalCorrelationId:'SENTETIK posted'}):json(value):undefined})
  mount();const region=await screen.findByRole('region',{name:T.manufacturingCostTitle})
  expect(within(region).getByRole('table',{name:T.manufacturingCostOutputs}).querySelectorAll('tbody tr')).toHaveLength(13)
  expect(within(region).getByRole('table',{name:T.manufacturingCostOutputs}).querySelectorAll('thead th')).toHaveLength(11)
  expect(within(region).getByText('SENTETIK source reason')).toBeTruthy();expect(region.textContent).toContain('SENTETIK debit');expect(region.textContent).toContain('SENTETIK credit')
  expect(region.textContent).toContain('2 / 3');expect(region.textContent).toContain('1 / 3');expect(region.textContent).toContain('600')
  for(const hash of [value.manufacturingCostDifference.sourceFactsDigest,value.manufacturingCostDifference.previewDigest,value.manufacturingCostDifference.proposal.accountPlan.sha256])expect(region.textContent).not.toContain(hash)
  expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  expect(within(region).queryByRole('button')).toBeNull();expect(within(region).queryByRole('combobox')).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null}])
  expect(calls.some(c=>c.url.includes('/accounting/'))).toBe(false);expect(screen.queryByRole('region',{name:T.manufacturingCostTitle})).toBeNull()
  expect(workRoutes.some(r=>r.path===staged.routePath.slice(1))).toBe(false)
 })
 it('keeps exact original body/key on unknown remount without GET, human journal data or new selection',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cost retained'});let posts=0
  const value=fixture(COMPANY_A)
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?++posts===1?problem(503):
   json({companyId:COMPANY_A,kind,itemId:id,state:'Posted',replayed:true,originalCorrelationId:'SENTETIK original'},200,{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}):json(posts?fixture(COMPANY_A,'9223372036854775807','Posted'):value):undefined})
  mount();await screen.findByRole('region',{name:T.manufacturingCostTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const gets=calls.filter(c=>c.url.includes(path)&&c.method==='GET').length,journal=JSON.stringify(sessionStorage)
  for(const secret of ['SENTETIK source reason','SENTETIK debit','sourceFactsDigest','previewDigest','outputs',value.manufacturingCostDifference.proposal.accountPlan.sha256])expect(journal).not.toContain(secret)
  cleanup();mount();await screen.findByText(T.unknown);expect(screen.getByText(T.manufacturingCostRetained)).toBeTruthy()
  expect(screen.queryByRole('region',{name:T.manufacturingCostTitle})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
  expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET')).toHaveLength(gets)
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET')).toHaveLength(gets)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 })
 it('suppresses already-inflight later facts after unknown and retries only the retained body',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cost late'});let gets=0,release!:(v:Response)=>void
  const late=new Promise<Response>(resolve=>{release=resolve}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):++gets===1?json(fixture(COMPANY_A)):late:undefined})
  mount(client);await screen.findByRole('region',{name:T.manufacturingCostTitle})
  let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const current=fixture(COMPANY_A,'9223372036854775807','Posted');current.manufacturingCostDifference.proposal.reason='SENTETIK LATE CURRENT FACT'
  await act(async()=>{release(json(current));await refetch})
  expect(screen.queryByText('SENTETIK LATE CURRENT FACT')).toBeNull();expect(screen.queryByRole('region',{name:T.manufacturingCostTitle})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.unknown)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each([403,503])('releases no human or action on owner refusal %s',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?problem(status):undefined});mount()
  await waitFor(()=>expect(calls.some(c=>c.url.includes(path))).toBe(true));expect(screen.queryByRole('region',{name:T.manufacturingCostTitle})).toBeNull()
  expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['foreign','missing','private'])('does not render unbound human %s',async fault=>{
  const value=fixture(COMPANY_A),raw=fault==='foreign'?{...value,companyId:COMPANY_B}:fault==='missing'?{...value,manufacturingCostDifference:undefined}:{...value,manufacturingCostDifference:{...value.manufacturingCostDifference,approvalReceipt:{}}}
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(raw):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.manufacturingCostTitle})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each([{capabilities:[]},{capabilities:['accounting.manufacturing-cost-differences']},{capabilities:['accounting.journal.reverse.approve']}])('does not read without actual purpose cap %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(path)?json(fixture(COMPANY_A)):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(path))).toBe(false)
 })
 it('shows a completed Posted view read-only and disables pending Max version without inventing a smaller version',async()=>{
  const value=fixture(COMPANY_A,'9223372036854775807')
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(value):undefined});mount()
  await screen.findByRole('region',{name:T.manufacturingCostTitle});expect(screen.getByRole('button',{name:T.approve}).matches(':disabled')).toBe(true)
  cleanup();Object.assign(value,{pending:false});Object.assign(value.manufacturingCostDifference.proposal,{status:'Posted',approvedBy:'ac530000-0000-4000-8000-000000000003',journalId:id})
  mount();await screen.findByRole('region',{name:T.manufacturingCostTitle});expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('does not restore unknown controls after purpose revocation or session replacement',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cost old'});const capabilities=[cap]
  const calls=installFetch({capabilities,respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.manufacturingCostTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  cleanup();capabilities.splice(0);mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
  cleanup();capabilities.push(cap);Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cost new'});mount()
  await screen.findByRole('region',{name:T.manufacturingCostTitle});expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('preserves the ordinary fresh POST403 boundary without a receipt retry bypass',async()=>{
  const capabilities=[cap]
  const calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(path))return undefined
   if(init?.method==='POST'){capabilities.splice(0);return problem(403)}return json(fixture(COMPANY_A))}})
  mount();await screen.findByRole('region',{name:T.manufacturingCostTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(screen.queryByRole('region',{name:T.manufacturingCostTitle})).toBeNull();expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
  expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('keeps unknown body scoped to its original company',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cost company'})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{if(!url.includes(path))return undefined;if(init?.method==='POST')return problem(503)
   const company=new Headers(init?.headers).get('X-Company-Id');if(company===null)throw new Error('Captured company required');return json(fixture(company))}})
  mount();await screen.findByRole('region',{name:T.manufacturingCostTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
  await screen.findByRole('region',{name:T.manufacturingCostTitle});expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
})
