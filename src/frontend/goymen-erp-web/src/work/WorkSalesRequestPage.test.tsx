import {afterEach,describe,it,expect,vi} from 'vitest'
import {act,cleanup,fireEvent,render,screen,within,waitFor} from '@testing-library/react'
import {RouterProvider,createMemoryRouter} from 'react-router'
import {QueryClient} from '@tanstack/react-query'
import {AppShell} from '../shell/AppShell'
import {ShellProvider} from '../shell/ShellProvider'
import {workRoutes} from './workRoutes'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import {WorkSalesRequestFacts} from './WorkSalesRequestFacts'
import {SALES_REQUEST_WORK_TEXT as T} from './workSalesRequestText'
import {SALES_REQUEST_WORK_KIND as kind,SALES_REQUEST_WORK_CAPABILITY as cap} from './workSalesRequestDecision'
import {salesRequestHuman as human,salesRequestFixture as fixture,salesRequestId as id} from './salesRequestDecisionFixture'
import {COMPANY_A,IDENTITY,installFetch,json,problem} from '../settings/testHarness'
afterEach(()=>{cleanup();sessionStorage.clear();vi.unstubAllGlobals();vi.restoreAllMocks();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
const ownerPath=`/work/decisions/${kind}/${id}`
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/> </ShellProvider>);return client
}
function result(approve:boolean,replayed=false){return json({companyId:COMPANY_A,kind,itemId:id,state:approve?'Approved':'Rejected',replayed,originalCorrelationId:'original-sales-request'},200,replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original-sales-request'}:undefined)}
// Actual Work page/router over synthetic HTTP transport. This is not native SQL/browser acceptance.
describe('Sales request full human facts and generic Work lifecycle',()=>{
 it('shows all native header/detail/line and decision fields without price/proof or truncating lines',()=>{
  const h=human(COMPANY_A,'Approved','9223372036854775807'),first=h.detail.lines[0]!
  const lines=Array.from({length:13},(_,i)=>({...first,lineId:`d4d4d4d4-0000-4000-8000-${String(i+1).padStart(12,'0')}`,lineNo:i+1,productName:`SENTETIK product ${i+1}`}))
  render(<WorkSalesRequestFacts human={{...h,detail:{...h.detail,request:{...h.detail.request,lineCount:lines.length},lines}}}/> )
  const region=screen.getByRole('region',{name:T.title});expect(region.querySelectorAll('dt')).toHaveLength(12+13*11+3)
  for(const value of [h.detail.request.version,first.productVersion,first.quantity,h.detail.note!,first.note!,h.detail.decision!.decidedBy,h.detail.decision!.decidedAtUtc,'SENTETIK product 13'])expect(region.textContent).toContain(value)
  for(const key of ['unitPrice','sourceFactsDigest','previewDigest','salesRequestProof','originalDecisionReceipt'])expect(region.textContent).not.toContain(key)
  for(const role of ['button','textbox','checkbox','combobox'] as const)expect(within(region).queryByRole(role)).toBeNull()
 })
 it('uses only the standalone purpose capability and permits a preparer to approve without a new SoD gate',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(true):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null}])
  expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(calls.filter(c=>c.url.includes('/sales/'))).toEqual([])
  expect(screen.queryByRole('region',{name:T.title})).toBeNull()
 })
 it('keeps Approved pending=false actionable only for raw mandatory rejection up to4000',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(false):json(fixture(COMPANY_A,'Approved')):undefined})
  mount();await screen.findByRole('region',{name:T.title});expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(true)
  const reject=screen.getByRole('button',{name:W.reject});expect(reject.matches(':disabled')).toBe(true)
  const reason=' '+ 'r'.repeat(3998)+' ',field=screen.getByRole('textbox',{name:W.reason});expect(field.getAttribute('maxlength')).toBe('4000')
  fireEvent.change(field,{target:{value:reason}});expect(reject.matches(':disabled')).toBe(false);fireEvent.click(reject);await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST')[0]!.body).toEqual({expectedVersion:'2',approve:false,reason,delegationId:null})
 })
 it('keeps Rejected terminal read-only',async()=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,'Rejected')):undefined});mount()
  await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(screen.queryByRole('button',{name:W.reject})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('shows MaxInt64 facts but disables both incrementing decisions',async()=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,'Submitted','9223372036854775807')):undefined});mount();await screen.findByRole('region',{name:T.title})
  fireEvent.change(screen.getByRole('textbox',{name:W.reason}),{target:{value:'SENTETIK reject'}})
  for(const name of [W.approve,W.reject]){const button=screen.getByRole('button',{name});expect(button.matches(':disabled')).toBe(true);fireEvent.click(button)}
  expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each([{capabilities:[]},{capabilities:['sales.requests.approve']},{capabilities:['sales.requests.detail']},{capabilities:['sales.order-draft.work-decision']}])('does not read with unrelated/native caps %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A)):undefined});mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(calls.some(c=>c.url.includes(ownerPath))).toBe(false)
 })
 it.each([true,false])('retains the original4body and key across unknown/remount; current native facts cannot replace it approve=%s',async approve=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK sales request session'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(approve,true):json(posts?fixture(COMPANY_A,'Rejected','3'):fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});if(!approve)fireEvent.change(screen.getByRole('textbox',{name:W.reason}),{target:{value:'  SENTETIK raw reason  '}})
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.unknown)
  const gets=calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET').length
  for(const value of ['0001.2500','SENTETIK full request note','SENTETIK line note','productName','sourceFactsDigest','originalDecisionReceipt'])expect(JSON.stringify(sessionStorage)).not.toContain(value)
  cleanup();mount();await screen.findByText(W.unknown);expect(screen.getByText(T.retained)).toBeTruthy();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
  expect(sent[1]!.body).toEqual({expectedVersion:'9007199254740993',approve,reason:approve?null:'  SENTETIK raw reason  ',delegationId:null});expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(gets)
 })
 it('suppresses cached/inflight later Rejected3 human facts while reconciling original Approved2',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK sales request late'});let gets=0,posts=0,release!:(r:Response)=>void
  const pending=new Promise<Response>(r=>{release=r}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(true,true):++gets===1?json(fixture(COMPANY_A,'Submitted','1')):pending:undefined})
  mount(client);await screen.findByRole('region',{name:T.title});let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  const later=fixture(COMPANY_A,'Rejected','3') // retained current state/version differ from the original decision
  await act(async()=>{release(json(later));await refetch});expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.body).toEqual({expectedVersion:'1',approve:true,reason:null,delegationId:null});expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
})
