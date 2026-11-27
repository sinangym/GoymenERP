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
import {INVENTORY_COST_WORK_PURPOSES as purposes,type InventoryCostWorkKind} from './workInventoryCostDecision'
import {inventoryCostFixture as fixture,successionCostFixture,inventoryCostId as id} from './inventoryCostDecisionFixture'
const kinds=Object.keys(purposes) as InventoryCostWorkKind[]
const path=(kind:string)=>`/work/decisions/${kind}/${id}`
afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
// Explicit staged-purpose harness. Root owns actual publication/mount parity; this is not browser acceptance.
function mount(kind:InventoryCostWorkKind,client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const purpose=purposes[kind],router=createMemoryRouter([{path:'/',element:<AppShell/>,children:[{path:purpose.routePath.slice(1),element:<CapabilityRoute capability={[purpose.capabilityId]}><WorkDecisionPage kind={kind}/></CapabilityRoute>}]}],{initialEntries:[purpose.routePath.replace(':approvalId',id)]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return {router,client}
}
const applied=(kind:InventoryCostWorkKind,replayed=false,state='Posted')=>json({companyId:COMPANY_A,kind,itemId:id,state,replayed,originalCorrelationId:'SENTETIK native original'},200,
 replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK native original'}:undefined)
describe('staged two-kind inventory cost human Work decisions',()=>{
 it.each(kinds)('shows all retained business facts read-only and sends exactly original four fields for %s',async kind=>{
  const value=fixture(COMPANY_A,kind),calls=installFetch({capabilities:[purposes[kind].capabilityId],respond:(url,init)=>url.includes(path(kind))?init?.method==='POST'?applied(kind):json(value):undefined})
  mount(kind);const region=await screen.findByRole('region',{name:T.inventoryCostTitle})
  for(const raw of ['SENTETIK INV-55','SENTETIK maliyet hazırlama gerekçesi','SENTETIK maliyet karşı hesabı','SENTETIK kayıtlı plan','9007199254740993','9223372036854775807','100.01'])expect(region.textContent).toContain(raw)
  if(kind===kinds[0])for(const raw of ['1 / 3','10001 / 300',T.inventoryCostFields.alreadyPostedComponents,T.inventoryCostFields.alreadyPostedPurchaseComponents,T.inventoryCostFields.returns,T.inventoryCostFields.recordedRounding])expect(region.textContent).toContain(raw)
  for(const raw of ['sourceProofJson','physicalProofJson','fingerprint','sourceFactsDigest','previewDigest','approvalReceipt','economicKey'])expect(document.body.textContent).not.toContain(raw)
  expect(within(region).queryByRole('button')).toBeNull();expect(within(region).queryByRole('textbox')).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'7',approve:true,reason:null,delegationId:null}])
  expect(screen.queryByRole('region',{name:T.inventoryCostTitle})).toBeNull();expect(calls.some(c=>c.url.includes('/accounting/'))).toBe(false)
 })
 it('shows succession, generations and renewal without exposing private hashes or enabling another operation',async()=>{
  const kind=kinds[0]!,v=successionCostFixture(COMPANY_A)
  installFetch({capabilities:[purposes[kind].capabilityId],respond:url=>url.includes(path(kind))?json(v):undefined});mount(kind)
  const region=await screen.findByRole('region',{name:T.inventoryCostTitle})
  for(const name of [T.inventoryCostFields.claimSuccession,T.inventoryCostFields.previousGeneration,T.inventoryCostFields.reversedComponents,T.inventoryCostFields.renewedFrom])expect(region.textContent).toContain(name)
  expect(region.textContent).toContain('9223372036854775807');expect(region.textContent).not.toContain('economicKey')
 })
 it.each(kinds)('restores unknown with original key/version/body and no GET despite later current terminal view for %s',async kind=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK inventory cost session'});let posts=0
  const calls=installFetch({capabilities:[purposes[kind].capabilityId],respond:(url,init)=>url.includes(path(kind))?init?.method==='POST'?++posts===1?problem(503):applied(kind,true):json(posts?fixture(COMPANY_A,kind,'Posted','9223372036854775807'):fixture(COMPANY_A,kind)):undefined})
  mount(kind);await screen.findByRole('region',{name:T.inventoryCostTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const gets=calls.filter(c=>c.url.includes(path(kind))&&c.method==='GET').length,journal=JSON.stringify(sessionStorage)
  for(const raw of ['100.01','SENTETIK maliyet hazırlama gerekçesi','SENTETIK maliyet karşı hesabı','proposal','claimSuccession','physicalProofJson','sourceFactsDigest'])expect(journal).not.toContain(raw)
  cleanup();mount(kind);await screen.findByText(T.unknown);expect(screen.getByText(T.inventoryCostRetained)).toBeTruthy()
  expect(screen.queryByRole('region',{name:T.inventoryCostTitle})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  expect(calls.filter(c=>c.url.includes(path(kind))&&c.method==='GET')).toHaveLength(gets)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 })
 it('hides cached/inflight later facts while reconciling the original command',async()=>{
  const kind=kinds[0]!;Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK inventory cost late'});let gets=0,release!:(v:Response)=>void
  const late=new Promise<Response>(resolve=>{release=resolve}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[purposes[kind].capabilityId],respond:(url,init)=>url.includes(path(kind))?init?.method==='POST'?problem(503):++gets===1?json(fixture(COMPANY_A,kind)):late:undefined})
  mount(kind,client);await screen.findByRole('region',{name:T.inventoryCostTitle})
  let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const current=fixture(COMPANY_A,kind,'Posted','9223372036854775807');Object.assign(current.inventoryCost.proposal.preparation,{reason:'SENTETIK LATER CURRENT COST'})
  await act(async()=>{release(json(current));await refetch});expect(screen.queryByText('SENTETIK LATER CURRENT COST')).toBeNull();expect(screen.queryByRole('region',{name:T.inventoryCostTitle})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.unknown)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each(['Posted','RoundedNoCost','Stale'] as const)('renders terminal %s read-only without approval',async status=>{
  const kind=kinds[0]!,calls=installFetch({capabilities:[purposes[kind].capabilityId],respond:url=>url.includes(path(kind))?json(fixture(COMPANY_A,kind,status,'8')):undefined});mount(kind)
  await screen.findByRole('region',{name:T.inventoryCostTitle});expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('keeps max-version and source-cancelled prepared proposals read-only without inventing an increment',async()=>{
  const kind=kinds[0]!,v=fixture(COMPANY_A,kind,'Prepared','9223372036854775807')
  const calls=installFetch({capabilities:[purposes[kind].capabilityId],respond:url=>url.includes(path(kind))?json(v):undefined});mount(kind)
  await screen.findByRole('region',{name:T.inventoryCostTitle});expect(screen.getByRole('button',{name:T.approve})).toBeDisabled();expect(calls.some(c=>c.method==='POST')).toBe(false)
  cleanup();v.pending=false;mount(kind);await screen.findByRole('region',{name:T.inventoryCostTitle});expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
 })
 it('keeps native Stale 409 as a refused conflict, never a successful Work result',async()=>{
  const kind=kinds[0]!,calls=installFetch({capabilities:[purposes[kind].capabilityId],respond:(url,init)=>url.includes(path(kind))?init?.method==='POST'?problem(409):json(fixture(COMPANY_A,kind)):undefined});mount(kind)
  await screen.findByRole('region',{name:T.inventoryCostTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(`${T.refused} (409)`)
  expect(screen.queryByText(T.done)).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it.each([{capabilities:[]},{capabilities:['accounting.journal.draft.approve']},{capabilities:['accounting.journal.reverse.approve']},{capabilities:[purposes['accounting.purchase-cost-difference.v1'].capabilityId]}])('refuses wrong capability %j without an owner GET',async({capabilities})=>{
  const kind=kinds[0]!,calls=installFetch({capabilities,respond:url=>url.includes(path(kind))?json(fixture(COMPANY_A,kind)):undefined});mount(kind)
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(path(kind)))).toBe(false)
 })
 it.each(['foreign','private','crossKind','missing'])('hides invalid human %s and sends no command',async fault=>{
  const kind=kinds[0]!,v=fixture(COMPANY_A,kind)
  if(fault==='foreign')Object.assign(v.inventoryCost.proposal,{companyId:COMPANY_B})
  if(fault==='private')Object.assign(v.inventoryCost.proposal.component,{sourceProofJson:'SENTETIK private'})
  if(fault==='crossKind')Object.assign(v.inventoryCost.proposal,{kind:2})
  if(fault==='missing')Reflect.deleteProperty(v,'inventoryCost')
  const calls=installFetch({capabilities:[purposes[kind].capabilityId],respond:url=>url.includes(path(kind))?json(v):undefined});mount(kind)
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.inventoryCostTitle})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('preserves current POST403 authority boundary and denies a retained command after capability revocation',async()=>{
  const kind=kinds[0]!,capabilities=[purposes[kind].capabilityId],calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(path(kind)))return undefined
   if(init?.method==='POST'){capabilities.splice(0);return problem(403)}return json(fixture(COMPANY_A,kind))}})
  mount(kind);await screen.findByRole('region',{name:T.inventoryCostTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('does not expose a restored unknown retry after its exact purpose capability was revoked',async()=>{
  const kind=kinds[0]!,capabilities=[purposes[kind].capabilityId];Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK retained cost cap'})
  const calls=installFetch({capabilities,respond:(url,init)=>url.includes(path(kind))?init?.method==='POST'?problem(503):json(fixture(COMPANY_A,kind)):undefined})
  mount(kind);await screen.findByRole('region',{name:T.inventoryCostTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  cleanup();capabilities.splice(0);mount(kind);await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(screen.queryByRole('region',{name:T.inventoryCostTitle})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it.each(['session','company'])('does not release a prior unknown attempt into another %s',async change=>{
  const kind=kinds[0]!;Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK old cost session'})
  const calls=installFetch({capabilities:[purposes[kind].capabilityId],respond:(url,init)=>{if(!url.includes(path(kind)))return undefined;if(init?.method==='POST')return problem(503)
   const company=new Headers(init?.headers).get('X-Company-Id');if(company===null)throw new Error('Captured company required');return json(fixture(company,kind))}})
  mount(kind);await screen.findByRole('region',{name:T.inventoryCostTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  if(change==='session'){cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK new cost session'});mount(kind)}
  else await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
  await screen.findByRole('region',{name:T.inventoryCostTitle});expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
})
