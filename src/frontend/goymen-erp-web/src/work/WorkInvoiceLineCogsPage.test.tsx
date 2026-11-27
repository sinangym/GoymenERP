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
import {STAGED_INVOICE_LINE_COGS_PURPOSE as staged} from './workInvoiceLineCogsDecision'
import {invoiceLineCogsFixture as fixture,invoiceLineCogsId as id} from './invoiceLineCogsDecisionFixture'
const kind=staged.kind,cap=staged.capabilityId,path=`/work/decisions/${kind}/${id}`
afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
// Explicit source-ready purpose harness, not publication or real browser evidence.
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:[{path:staged.routePath.slice(1),element:<CapabilityRoute capability={[cap]}><WorkDecisionPage kind={kind}/></CapabilityRoute>}]}],{initialEntries:[staged.routePath.replace(':approvalId',id)]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return {router,client}
}
const applied=(replayed=false)=>json({companyId:COMPANY_A,kind,itemId:id,state:'Approved',replayed,originalCorrelationId:'SENTETIK original'},200,
 replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original'}:undefined)
describe('source-ready invoice-line COGS human Work decision',()=>{
 it('shows full retained source/plan/evidence/returns without JSON/digests or calculation and sends only original four fields',async()=>{
  const value=fixture(COMPANY_A)
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?applied():json(value):undefined})
  mount();const region=await screen.findByRole('region',{name:T.invoiceLineCogsTitle})
  expect(within(region).getByText(value.invoiceLineCogs.proposal.reason)).toBeTruthy()
  for(const raw of ['SENTETIK retained account','SENTETIK retained plan','SENTETIK INV-54','9007199254740993','9223372036854775807','100.0100','10001 / 300','1 / 3','0.01'])expect(region.textContent).toContain(raw)
  for(const title of [T.invoiceLineCogsSource,T.invoiceLineCogsPlan,T.invoiceLineCogsPosted,T.invoiceLineCogsManufacturing,T.invoiceLineCogsPurchase,T.invoiceLineCogsReturns,T.invoiceLineCogsRounding])expect(region.textContent).toContain(title)
  for(const raw of [value.invoiceLineCogs.sourceFactsDigest,value.invoiceLineCogs.previewDigest,value.invoiceLineCogs.proposal.accountPlan.sourceSha256,
   value.invoiceLineCogs.proposal.attributedEvidence[0]!.source.fingerprint,'PRIVATE PHYSICAL JSON','physicalProofJson','sourceFactsDigest','previewDigest'])expect(document.body.textContent).not.toContain(raw)
  expect(screen.queryByRole('textbox',{name:T.reason})).toBeNull();expect(screen.queryByRole('button',{name:T.reject})).toBeNull()
  expect(within(region).queryByRole('button')).toBeNull();expect(within(region).queryByRole('combobox')).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'1',approve:true,reason:null,delegationId:null}])
  expect(calls.some(c=>c.url.includes('/accounting/'))).toBe(false);expect(screen.queryByRole('region',{name:T.invoiceLineCogsTitle})).toBeNull()
 })
 it('keeps original proposal version1/body/key after unknown and remount without GET despite current line revision advancing',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cogs retained'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?++posts===1?problem(503):applied(true):json(posts?fixture(COMPANY_A,true,'9223372036854775807'):fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.invoiceLineCogsTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const gets=calls.filter(c=>c.url.includes(path)&&c.method==='GET').length,journal=JSON.stringify(sessionStorage)
  for(const raw of ['SENTETIK retained COGS reason','SENTETIK retained account','100.0100','sourceFactsDigest','previewDigest','physicalProofJson','attributedEvidence'])expect(journal).not.toContain(raw)
  cleanup();mount();await screen.findByText(T.unknown);expect(screen.getByText(T.invoiceLineCogsRetained)).toBeTruthy()
  expect(screen.queryByRole('region',{name:T.invoiceLineCogsTitle})).toBeNull();expect(screen.queryByRole('button',{name:T.approve})).toBeNull()
  expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET')).toHaveLength(gets)
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.done)
  expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET')).toHaveLength(gets)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 })
 it('hides an already-inflight later human reply during unknown reconciliation',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cogs late'});let gets=0,release!:(v:Response)=>void
  const late=new Promise<Response>(resolve=>{release=resolve}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):++gets===1?json(fixture(COMPANY_A)):late:undefined})
  mount(client);await screen.findByRole('region',{name:T.invoiceLineCogsTitle})
  let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  const current=fixture(COMPANY_A,true,'9223372036854775807');current.invoiceLineCogs.proposal.reason='SENTETIK LATE COGS CURRENT FACT'
  await act(async()=>{release(json(current));await refetch})
  expect(screen.queryByText('SENTETIK LATE COGS CURRENT FACT')).toBeNull();expect(screen.queryByRole('region',{name:T.invoiceLineCogsTitle})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:T.retry}));await screen.findByText(T.unknown)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each([403,503])('renders no human or action on owner refusal %s',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?problem(status):undefined});mount()
  await waitFor(()=>expect(calls.some(c=>c.url.includes(path))).toBe(true));expect(screen.queryByRole('region',{name:T.invoiceLineCogsTitle})).toBeNull()
  expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['foreign','missing','private'])('does not release malformed human %s',async fault=>{
  const value=fixture(COMPANY_A),raw=fault==='foreign'?{...value,companyId:COMPANY_B}:fault==='missing'?{...value,invoiceLineCogs:undefined}:{...value,invoiceLineCogs:{...value.invoiceLineCogs,approvalReceipt:{}}}
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(raw):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.invoiceLineCogsTitle})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each([{capabilities:[]},{capabilities:['accounting.invoice-line-cogs.approve']},{capabilities:['accounting.journal.reverse.approve']}])('requires exact purpose capability %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(path)?json(fixture(COMPANY_A)):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(path))).toBe(false)
 })
 it.each([false,true])('keeps stalePrepared/approved with later line revision read-only: approved=%s',async approved=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(fixture(COMPANY_A,approved,'9223372036854775807')):undefined});mount()
  await screen.findByRole('region',{name:T.invoiceLineCogsTitle});expect(screen.queryByRole('button',{name:T.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('does not restore unknown controls after capability revocation or a different session',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cogs old'});const capabilities=[cap]
  const calls=installFetch({capabilities,respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.invoiceLineCogsTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  cleanup();capabilities.splice(0);mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
  cleanup();capabilities.push(cap);Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cogs new'});mount()
  await screen.findByRole('region',{name:T.invoiceLineCogsTitle});expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('preserves fresh POST403 as an authority boundary',async()=>{
  const capabilities=[cap],calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(path))return undefined
   if(init?.method==='POST'){capabilities.splice(0);return problem(403)}return json(fixture(COMPANY_A))}})
  mount();await screen.findByRole('region',{name:T.invoiceLineCogsTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}))
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:T.retry})).toBeNull()
  expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('scopes retained unknown command to its original company',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cogs company'})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{if(!url.includes(path))return undefined;if(init?.method==='POST')return problem(503)
   const company=new Headers(init?.headers).get('X-Company-Id');if(company===null)throw new Error('Captured company required');return json(fixture(company))}})
  mount();await screen.findByRole('region',{name:T.invoiceLineCogsTitle});fireEvent.click(screen.getByRole('button',{name:T.approve}));await screen.findByText(T.unknown)
  await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
  await screen.findByRole('region',{name:T.invoiceLineCogsTitle});expect(screen.queryByRole('button',{name:T.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
})
