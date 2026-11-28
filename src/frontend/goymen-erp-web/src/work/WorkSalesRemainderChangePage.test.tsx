import {afterEach,describe,it,expect,vi} from 'vitest'
import {act,cleanup,fireEvent,render,screen,within,waitFor} from '@testing-library/react'
import {RouterProvider,createMemoryRouter} from 'react-router'
import {QueryClient} from '@tanstack/react-query'
import userEvent from '@testing-library/user-event'
import {AppShell} from '../shell/AppShell'
import {ShellProvider} from '../shell/ShellProvider'
import {workRoutes} from './workRoutes'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import {WorkSalesRemainderChangeFacts} from './WorkSalesRemainderChangeFacts'
import {SALES_REMAINDER_CHANGE_WORK_TEXT as T} from './workSalesRemainderChangeText'
import {SALES_REMAINDER_CHANGE_WORK_KIND as kind,SALES_REMAINDER_CHANGE_WORK_CAPABILITY as cap} from './workSalesRemainderChangeDecision'
import {salesRemainderChangeHuman as human,salesRemainderChangeFixture as fixture,salesRemainderChangeId as id} from './salesRemainderChangeDecisionFixture'
import {COMPANY_A,COMPANY_B,IDENTITY,installFetch,json,problem} from '../settings/testHarness'
const originalActor=IDENTITY.userId

afterEach(()=>{cleanup();sessionStorage.clear();vi.unstubAllGlobals();vi.restoreAllMocks();Object.assign(IDENTITY,{authenticationSessionBinding:null,userId:originalActor})})
const ownerPath=`/work/decisions/${kind}/${id}`
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 // Actual paired route, no legacy test-only route override.
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return client
}
function result(approve=true,replayed=false){return json({companyId:COMPANY_A,kind,itemId:id,state:approve?'Approved':'Rejected',replayed,originalCorrelationId:'SENTETIK-original-remainder'},200,replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK-original-remainder'}:undefined)}

describe('quantity-only remainder human and frozen native decisions',()=>{
 it('shows all10/19/9 current native fields in readonly sections without private proof or prices',()=>{
  const h=human();render(<WorkSalesRemainderChangeFacts human={h}/>);const region=screen.getByRole('region',{name:T.title})
  for(const value of [h.draftId,h.documentNo,h.version,h.revision.toString(),h.change.changeNo.toString(),h.change.revision,h.change.lineId,h.change.lineNo.toString(),h.change.orderedQuantity,h.change.requestedQuantity!,h.change.reason,h.change.requestedBy,h.change.requestedAtUtc,h.change.loadedAtRequest,h.change.shippedAtRequest,h.change.deliveredAtRequest,h.line.productId,h.line.productCode,h.line.productName,h.line.orderUnitId,h.line.orderUnitCode,h.line.currentQuantity])expect(region.textContent).toContain(value)
  for(const title of [T.change,T.order,T.line])expect(within(region).getByRole('heading',{name:title})).toBeTruthy()
  for(const role of ['button','textbox','checkbox','combobox'] as const)expect(within(region).queryByRole(role)).toBeNull()
  for(const field of ['sourceFactsDigest','previewDigest','originalRemainderDecisionReceipt','payloadHash','unitPrice','salesRemainderChangeProof'])expect(region.textContent).not.toContain(field)
 })
 it.each([true,false])('uses exact purpose and freezes selected current native version approve=%s',async approve=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(approve):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});const raw='  SENTETIK ret  '
  if(!approve)fireEvent.change(screen.getByRole('textbox',{name:T.optionalReason}),{target:{value:raw}})
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:human().version,approve,reason:approve?null:raw,delegationId:null}])
  expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(calls.some(c=>c.url.includes('/sales/'))).toBe(false);expect(screen.queryByRole('region',{name:T.title})).toBeNull()
 })
 it('keeps blank rejection optional but rejects a raw reason over4000 without trimming it into validity',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(false):json(fixture(COMPANY_A)):undefined});mount();await screen.findByRole('region',{name:T.title})
  const input=screen.getByRole('textbox',{name:T.optionalReason}),reject=screen.getByRole('button',{name:W.reject});expect(reject.matches(':disabled')).toBe(false)
  fireEvent.change(input,{target:{value:' '+'x'.repeat(4000)+' '}});expect(reject.matches(':disabled')).toBe(true)
  fireEvent.change(input,{target:{value:'   '}});expect(reject.matches(':disabled')).toBe(false);fireEvent.click(reject);await screen.findByText(W.done)
  expect((calls.find(c=>c.method==='POST')!.body as {reason:string}).reason).toBe('   ')
 })
 it.each([{capabilities:[]},{capabilities:['sales.orders.approve']},{capabilities:['sales.drafts.detail']},{capabilities:['sales.order-draft.work-decision']},{capabilities:['sales.over-delivery-request.work-decision']}])('never fetches under another capability %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A)):undefined});mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(ownerPath))).toBe(false)
 })
 it.each(['Approved','Rejected'] as const)('keeps terminal%s human readonly without substituting a new pending decision',async state=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,'9',state)):undefined});mount();await screen.findByRole('region',{name:T.title})
  expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(screen.queryByRole('button',{name:W.reject})).toBeNull();expect(screen.queryByRole('textbox')).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each([true,false])('recovers unknown on remount with the identical four-field body/key and no current GET approve=%s',async approve=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-remainder-recovery'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(approve,true):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});if(!approve)fireEvent.change(screen.getByRole('textbox',{name:T.optionalReason}),{target:{value:'  SENTETIK ret  '}})
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.unknown);const gets=calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET').length
  expect(screen.queryByRole('region',{name:T.title})).toBeNull();for(const raw of ['SENTETIK Kalan Ürünü','sourceFactsDigest','originalRemainderDecisionReceipt','currentQuantity','requestedQuantity'])expect(JSON.stringify(sessionStorage)).not.toContain(raw)
  cleanup();mount();await screen.findByText(W.unknown);expect(screen.getByText(T.retained)).toBeTruthy();expect(screen.queryByRole('region',{name:T.title})).toBeNull();fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(gets)
 })
 it('suppresses late current facts during unknown recovery and never replaces the old version/key',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-remainder-late'});let gets=0,posts=0,release!:(r:Response)=>void;const pending=new Promise<Response>(r=>{release=r}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(true,true):++gets===1?json(fixture(COMPANY_A,'1')):pending:undefined})
  mount(client);await screen.findByRole('region',{name:T.title});let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2));fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  await act(async()=>{release(json(fixture(COMPANY_A,'3')));await refetch});expect(screen.queryByRole('region',{name:T.title})).toBeNull();fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent[1]!.body).toEqual(sent[0]!.body);expect((sent[1]!.body as {expectedVersion:string}).expectedVersion).toBe('1');expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it('refreshes once after a fresh native409, clears the former rejection intent and uses a new key',async()=>{
  let gets=0,posts=0;const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(409):result(false):json(fixture(COMPANY_A,++gets===1?'1':'3')):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.change(screen.getByRole('textbox',{name:T.optionalReason}),{target:{value:'old'}});fireEvent.click(screen.getByRole('button',{name:W.reject}));expect((await screen.findByRole('alert')).textContent).toBe(`${W.refused} (409)`)
  await waitFor(()=>{expect(gets).toBe(2);expect((screen.getByRole('textbox',{name:T.optionalReason}) as HTMLTextAreaElement).value).toBe('');expect(screen.getByRole('button',{name:W.reject}).matches(':disabled')).toBe(false)})
  fireEvent.change(screen.getByRole('textbox',{name:T.optionalReason}),{target:{value:'  new  '}});fireEvent.click(screen.getByRole('button',{name:W.reject}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.headers['Idempotency-Key']).not.toBe(sent[0]!.headers['Idempotency-Key']);expect(sent[1]!.body).toEqual({expectedVersion:'3',approve:false,reason:'  new  ',delegationId:null});expect(gets).toBe(2)
 })
 it('clears rejection intent when current human line facts change with the same selected version',async()=>{
  let gets=0;const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:url=>{
   if(!url.includes(ownerPath))return undefined
   const v=fixture(COMPANY_A)
   return json(++gets===1?v:{...v,salesRemainderChange:{...v.salesRemainderChange!,line:{...v.salesRemainderChange!.line,currentQuantity:'3.0000'}}})
  }})
  mount(client);await screen.findByRole('region',{name:T.title});fireEvent.change(screen.getByRole('textbox',{name:T.optionalReason}),{target:{value:'SENTETIK former intent'}});expect(screen.getByRole('button',{name:W.reject}).matches(':disabled')).toBe(false)
  await act(async()=>{await client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})})
  await waitFor(()=>{expect((screen.getByRole('textbox',{name:T.optionalReason}) as HTMLTextAreaElement).value).toBe('');expect(screen.getByRole('button',{name:W.reject}).matches(':disabled')).toBe(false)})
  expect(calls.some(c=>c.method==='POST')).toBe(false);client.clear()
 })
 it.each(['company','actor','session'] as const)('does not recover a prior unknown under another %s',async change=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-remainder-old'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST'){posts++;return problem(503)}const company=new Headers(init?.headers).get('X-Company-Id')??COMPANY_A;return json(fixture(company))}})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  if(change==='company')await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
  else{cleanup();if(change==='actor')Object.assign(IDENTITY,{userId:'88888888-8888-4888-8888-888888888888'});else Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK-remainder-new'});mount()}
  await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(posts).toBe(1);expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 // A business 403 invalidates shell capabilities; the real route uses its neutral non-disclosure block.
 it.each([403,404,503])('shows native read refusal %s without exposing cached human facts or a command',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?problem(status):undefined});mount();const state=status===403||status===404?'not-found':'service-unavailable';await waitFor(()=>expect(document.querySelector(`[data-state="${state}"]`)).not.toBeNull());expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
})
