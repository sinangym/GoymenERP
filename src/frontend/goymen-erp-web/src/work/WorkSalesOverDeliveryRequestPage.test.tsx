import {afterEach,describe,it,expect,vi} from 'vitest'
import {act,cleanup,fireEvent,render,screen,within,waitFor} from '@testing-library/react'
import {RouterProvider,createMemoryRouter} from 'react-router'
import {QueryClient} from '@tanstack/react-query'
import userEvent from '@testing-library/user-event'
import {AppShell} from '../shell/AppShell'
import {ShellProvider} from '../shell/ShellProvider'
import {workRoutes} from './workRoutes'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import {WorkSalesOverDeliveryRequestFacts} from './WorkSalesOverDeliveryRequestFacts'
import {SALES_OVER_DELIVERY_REQUEST_WORK_TEXT as T} from './workSalesOverDeliveryRequestText'
import {SALES_OVER_DELIVERY_REQUEST_WORK_KIND as kind,SALES_OVER_DELIVERY_REQUEST_WORK_CAPABILITY as cap} from './workSalesOverDeliveryRequestDecision'
import {salesOverDeliveryRequestHuman as human,salesOverDeliveryRequestFixture as fixture,salesOverDeliveryRequestId as id} from './salesOverDeliveryRequestDecisionFixture'
import {COMPANY_A,COMPANY_B,IDENTITY,installFetch,json,problem} from '../settings/testHarness'
const originalActor=IDENTITY.userId
afterEach(()=>{cleanup();sessionStorage.clear();vi.unstubAllGlobals();vi.restoreAllMocks();Object.assign(IDENTITY,{authenticationSessionBinding:null,userId:originalActor})})
const ownerPath=`/work/decisions/${kind}/${id}`
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 // Actual paired production route; no legacy or test-only route override.
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return client
}
function result(approve=true,replayed=false){return json({companyId:COMPANY_A,kind,itemId:id,state:approve?'Approved':'Rejected',replayed,originalCorrelationId:'SENTETIK-original-over'},200,replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original-over'}:undefined)}
describe('quantity-only numbered over-delivery Work human and frozen native decisions',()=>{
 it('displays all native request/order/line facts in separate readonly sections without price or private proof',()=>{
  const h=human();render(<WorkSalesOverDeliveryRequestFacts human={h}/>);const region=screen.getByRole('region',{name:T.title})
  for(const value of [h.requestId,h.request.subjectId,h.request.requestNo.toString(),h.request.subjectRevision,h.request.version,h.request.lineId!,h.request.ordered!,h.request.quantity!,h.request.requestedTotal!,h.request.loadingInstructionId!,h.request.shipmentId!,h.request.requestedBy,h.request.requestedAtUtc,h.order.documentNo,h.order.version,h.order.partyId,h.order.companyAccountId,h.order.currencyId,h.order.businessDate,h.order.line!.lineNo.toString(),h.order.line!.productId,h.order.line!.productCode,h.order.line!.productName,h.order.line!.orderUnitId,h.order.line!.orderUnitCode,h.order.line!.currentQuantity])expect(region.textContent).toContain(value)
  for(const title of [T.request,T.order,T.line])expect(within(region).getByRole('heading',{name:title})).toBeTruthy()
  for(const role of ['button','textbox','checkbox','combobox'] as const)expect(within(region).queryByRole(role)).toBeNull()
  for(const field of ['sourceFactsDigest','previewDigest','originalOverDeliveryReceipt','payloadHash','unitPrice','salesOverDeliveryRequestProof'])expect(region.textContent).not.toContain(field)
 })
 it.each([true,false])('uses exact purpose only and freezes REQUEST version rather than current order version approve=%s',async approve=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(approve):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});const raw='  SENTETIK ret  '
  if(!approve)fireEvent.change(screen.getByRole('textbox',{name:W.reason}),{target:{value:raw}})
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:human().request.version,approve,reason:approve?null:raw,delegationId:null}])
  expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(calls.some(c=>c.url.includes('/sales/'))).toBe(false);expect(screen.queryByRole('region',{name:T.title})).toBeNull()
 })
 it('requires a literal nonblank rejection reason and preserves the native raw4000 bound',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(false):json(fixture(COMPANY_A)):undefined});mount();await screen.findByRole('region',{name:T.title})
  const input=screen.getByRole('textbox',{name:W.reason}),reject=screen.getByRole('button',{name:W.reject});expect(reject.matches(':disabled')).toBe(true)
  fireEvent.change(input,{target:{value:' '+'x'.repeat(4000)+' '}});expect(reject.matches(':disabled')).toBe(true)
  const raw=' '+'x'.repeat(3998)+' ';fireEvent.change(input,{target:{value:raw}});expect(reject.matches(':disabled')).toBe(false);fireEvent.click(reject);await screen.findByText(W.done)
  expect((calls.find(c=>c.method==='POST')!.body as {reason:string}).reason).toBe(raw)
 })
 it.each([{capabilities:[]},{capabilities:['sales.orders.over-delivery.approve']},{capabilities:['sales.drafts.detail']},{capabilities:['sales.order-draft.work-decision']},{capabilities:['sales.quote-conversion-request.work-decision']}])('never fetches with native/general/wrong-purpose capabilities %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A)):undefined});mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(ownerPath))).toBe(false)
 })
 it.each(['Approved','Rejected'] as const)('keeps terminal %s current human readonly with original decision details',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,'2',status)):undefined});mount();await screen.findByRole('region',{name:T.title})
  expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(screen.queryByRole('button',{name:W.reject})).toBeNull();expect(screen.queryByRole('textbox')).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('keeps max-version human visible while the server disables approval and offers no rejection action',async()=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,'9223372036854775807')):undefined})
  mount();await screen.findByRole('region',{name:T.title})
  const approve=screen.getByRole('button',{name:W.approve})
  expect(approve.matches(':disabled')).toBe(true)
  expect(screen.queryByRole('button',{name:W.reject})).toBeNull()
  expect(screen.queryByRole('textbox',{name:W.reason})).toBeNull()
  fireEvent.click(approve)
  expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each([true,false])('recovers unknown on remount with the identical four-field body/key and no current GET approve=%s',async approve=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-over-recovery'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(approve,true):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});if(!approve)fireEvent.change(screen.getByRole('textbox',{name:W.reason}),{target:{value:'  SENTETIK ret  '}})
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.unknown);const gets=calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET').length
  expect(screen.queryByRole('region',{name:T.title})).toBeNull();for(const raw of ['SENTETIK Fazla Teslim Ürünü','sourceFactsDigest','originalOverDeliveryReceipt','currentQuantity','requestedTotal'])expect(JSON.stringify(sessionStorage)).not.toContain(raw)
  cleanup();mount();await screen.findByText(W.unknown);expect(screen.getByText(T.retained)).toBeTruthy();expect(screen.queryByRole('region',{name:T.title})).toBeNull();fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(gets)
 })
 it('suppresses late current facts during unknown recovery and never replaces the old version/key',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-over-late'});let gets=0,posts=0,release!:(r:Response)=>void;const pending=new Promise<Response>(r=>{release=r}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(true,true):++gets===1?json(fixture(COMPANY_A,'1')):pending:undefined})
  mount(client);await screen.findByRole('region',{name:T.title});let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2));fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  await act(async()=>{release(json(fixture(COMPANY_A,'3')));await refetch});expect(screen.queryByRole('region',{name:T.title})).toBeNull();fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent[1]!.body).toEqual(sent[0]!.body);expect((sent[1]!.body as {expectedVersion:string}).expectedVersion).toBe('1');expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it('refreshes once after a fresh native409, clears the former rejection intent and uses a new key',async()=>{
  let gets=0,posts=0;const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(409):result(false):json(fixture(COMPANY_A,++gets===1?'1':'3')):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.change(screen.getByRole('textbox',{name:W.reason}),{target:{value:'old'}});fireEvent.click(screen.getByRole('button',{name:W.reject}));expect((await screen.findByRole('alert')).textContent).toBe(`${W.refused} (409)`)
  await waitFor(()=>{expect(gets).toBe(2);expect((screen.getByRole('textbox',{name:W.reason}) as HTMLTextAreaElement).value).toBe('');expect(screen.getByRole('button',{name:W.reject}).matches(':disabled')).toBe(true)})
  fireEvent.change(screen.getByRole('textbox',{name:W.reason}),{target:{value:'  new  '}});fireEvent.click(screen.getByRole('button',{name:W.reject}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.headers['Idempotency-Key']).not.toBe(sent[0]!.headers['Idempotency-Key']);expect(sent[1]!.body).toEqual({expectedVersion:'3',approve:false,reason:'  new  ',delegationId:null});expect(gets).toBe(2)
 })
 it('clears rejection intent when only the current order changes while the numbered request version stays the same',async()=>{
  let gets=0;const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:url=>{
   if(!url.includes(ownerPath))return undefined
   const v=fixture(COMPANY_A)
   return json(++gets===1?v:{...v,salesOverDeliveryRequest:{...v.salesOverDeliveryRequest!,order:{...v.salesOverDeliveryRequest!.order,version:'9007199254740996'}}})
  }})
  mount(client);await screen.findByRole('region',{name:T.title});fireEvent.change(screen.getByRole('textbox',{name:W.reason}),{target:{value:'SENTETIK former intent'}});expect(screen.getByRole('button',{name:W.reject}).matches(':disabled')).toBe(false)
  await act(async()=>{await client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})})
  await waitFor(()=>{expect((screen.getByRole('textbox',{name:W.reason}) as HTMLTextAreaElement).value).toBe('');expect(screen.getByRole('button',{name:W.reject}).matches(':disabled')).toBe(true)})
  expect(calls.some(c=>c.method==='POST')).toBe(false);client.clear()
 })
 it.each(['company','actor','session'] as const)('does not recover a prior unknown under another %s',async change=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-over-old'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST'){posts++;return problem(503)}const company=new Headers(init?.headers).get('X-Company-Id')??COMPANY_A;return json(fixture(company))}})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  if(change==='company')await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
  else{cleanup();if(change==='actor')Object.assign(IDENTITY,{userId:'88888888-8888-4888-8888-888888888888'});else Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-over-new'});mount()}
  await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(posts).toBe(1);expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 // A business 403 invalidates shell capabilities; the real route uses its neutral non-disclosure block.
 it.each([403,404,503])('shows native read refusal %s without exposing cached human facts or a command',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?problem(status):undefined});mount();const state=status===403||status===404?'not-found':'service-unavailable';await waitFor(()=>expect(document.querySelector(`[data-state="${state}"]`)).not.toBeNull());expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
})
