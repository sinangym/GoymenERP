import {afterEach,describe,it,expect,vi} from 'vitest'
import {act,cleanup,fireEvent,render,screen,within,waitFor} from '@testing-library/react'
import {RouterProvider,createMemoryRouter} from 'react-router'
import {QueryClient} from '@tanstack/react-query'
import {AppShell} from '../shell/AppShell'
import {ShellProvider} from '../shell/ShellProvider'
import {workRoutes} from './workRoutes'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import {WorkSalesQuoteConversionFacts} from './WorkSalesQuoteConversionFacts'
import {SALES_QUOTE_CONVERSION_WORK_TEXT as T} from './workSalesQuoteConversionText'
import {SALES_QUOTE_CONVERSION_WORK_KIND as kind,SALES_QUOTE_CONVERSION_WORK_CAPABILITY as cap} from './workSalesQuoteConversionDecision'
import {salesQuoteConversionHuman as human,salesQuoteConversionFixture as fixture,salesQuoteConversionId as id} from './salesQuoteConversionDecisionFixture'
import {COMPANY_A,IDENTITY,installFetch,json,problem} from '../settings/testHarness'
afterEach(()=>{cleanup();sessionStorage.clear();vi.unstubAllGlobals();vi.restoreAllMocks();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
const ownerPath=`/work/decisions/${kind}/${id}`
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/> </ShellProvider>);return client
}
function result(approve:boolean,replayed=false){return json({companyId:COMPANY_A,kind,itemId:id,state:approve?'Approved':'Rejected',replayed,originalCorrelationId:'original-sales-quote'},200,replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original-sales-quote'}:undefined)}
// Actual Work page/router over synthetic HTTP transport. This is not native SQL/browser acceptance.
describe('Selected quote full human facts and generic Work lifecycle',()=>{
 it('shows every native header/request/line and nested collection without proof or truncation',()=>{
  const h=human(COMPANY_A,'Approved','9223372036854775807','1',true),first=h.quote.lines[0]!
  const lines=Array.from({length:13},(_,i)=>({...first,lineId:`d4d4d4d4-0000-4000-8000-${String(i+1).padStart(12,'0')}`,lineNo:i+1,productName:`SENTETIK quote product ${i+1}`}))
  render(<WorkSalesQuoteConversionFacts human={{...h,quote:{...h.quote,quote:{...h.quote.quote,lineCount:lines.length},lines}}}/> )
  const region=screen.getByRole('region',{name:T.title});expect(region.querySelectorAll('dt')).toHaveLength(21+17+13*15+4+6+3)
  for(const value of [h.quote.quote.version,first.productVersion,first.quantity,h.quote.note!,first.note!,h.request.note!,h.request.requestedAtUtc,h.quote.expiryApprovals[0]!.approvedBy,'SENTETIK quote product 13','00042.5000'])expect(region.textContent).toContain(value)
  for(const key of ['sourceFactsDigest','previewDigest','salesQuoteConversionProof','originalDecisionReceipt'])expect(region.textContent).not.toContain(key)
  for(const role of ['button','textbox','checkbox','combobox'] as const)expect(within(region).queryByRole(role)).toBeNull()
 })
 it('uses only the standalone purpose capability and permits a preparer to approve without a new SoD gate',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(true):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null,salesQuoteConversion:{quoteId:human(COMPANY_A).quote.quote.id,requestNo:3,subjectRevision:0,requestVersion:'1'}}])
  expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(calls.filter(c=>c.url.includes('/sales/'))).toEqual([])
  expect(screen.queryByRole('region',{name:T.title})).toBeNull()
 })
 it('keeps Approved pending=false actionable only for raw mandatory rejection up to4000',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(false):json(fixture(COMPANY_A,'Approved')):undefined})
  mount();await screen.findByRole('region',{name:T.title});expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(true)
  const reject=screen.getByRole('button',{name:W.reject});expect(reject.matches(':disabled')).toBe(true)
  const reason=' '+ 'r'.repeat(3998)+' ',field=screen.getByRole('textbox',{name:W.reason});expect(field.getAttribute('maxlength')).toBe('4000')
  fireEvent.change(field,{target:{value:reason}});expect(reject.matches(':disabled')).toBe(false);fireEvent.click(reject);await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST')[0]!.body).toEqual({expectedVersion:'1',approve:false,reason,delegationId:null,salesQuoteConversion:{quoteId:human(COMPANY_A).quote.quote.id,requestNo:3,subjectRevision:0,requestVersion:'1'}})
 })
 it('keeps Rejected terminal read-only',async()=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,'Rejected')):undefined});mount()
  await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(screen.queryByRole('button',{name:W.reject})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('shows Max quote version and disables only quote approval while request rejection remains available',async()=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,'Pending','9223372036854775807')):undefined});mount();await screen.findByRole('region',{name:T.title})
  fireEvent.change(screen.getByRole('textbox',{name:W.reason}),{target:{value:'SENTETIK reject'}})
  expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(true)
  expect(screen.getByRole('button',{name:W.reject}).matches(':disabled')).toBe(false);expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each([{capabilities:[]},{capabilities:['sales.quotes.approve-expired']},{capabilities:['sales.quotes.detail']},{capabilities:['sales.order-draft.work-decision']}])('does not read with unrelated/native caps %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A)):undefined});mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(calls.some(c=>c.url.includes(ownerPath))).toBe(false)
 })
 it.each([true,false])('retains the original5body/4field target and key across unknown/remount; current quote/request facts cannot replace it approve=%s',async approve=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK sales request session'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(approve,true):json(posts?fixture(COMPANY_A,'Rejected','10','2'):fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});if(!approve)fireEvent.change(screen.getByRole('textbox',{name:W.reason}),{target:{value:'  SENTETIK raw reason  '}})
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.unknown)
  const gets=calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET').length
  for(const value of ['0001.2500','SENTETIK full quote note','SENTETIK full quote line','productName','sourceFactsDigest','originalDecisionReceipt'])expect(JSON.stringify(sessionStorage)).not.toContain(value)
  cleanup();mount();await screen.findByText(W.unknown);expect(screen.getByText(T.retained)).toBeTruthy();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
  expect(sent[1]!.body).toEqual({expectedVersion:approve?'9007199254740993':'1',approve,reason:approve?null:'  SENTETIK raw reason  ',delegationId:null,salesQuoteConversion:{quoteId:human(COMPANY_A).quote.quote.id,requestNo:3,subjectRevision:0,requestVersion:'1'}});expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(gets)
 })
 it('suppresses cached/inflight later Rejected request2/quote10 human facts while reconciling original Approved2',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK sales request late'});let gets=0,posts=0,release!:(r:Response)=>void
  const pending=new Promise<Response>(r=>{release=r}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(true,true):++gets===1?json(fixture(COMPANY_A,'Pending','9','1')):pending:undefined})
  mount(client);await screen.findByRole('region',{name:T.title});let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  const later=fixture(COMPANY_A,'Rejected','10','2') // retained current state/version differ from the original decision
  await act(async()=>{release(json(later));await refetch});expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.body).toEqual({expectedVersion:'9',approve:true,reason:null,delegationId:null,salesQuoteConversion:{quoteId:human(COMPANY_A).quote.quote.id,requestNo:3,subjectRevision:0,requestVersion:'1'}});expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
})
