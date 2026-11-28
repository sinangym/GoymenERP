import {afterEach,describe,it,expect,vi} from 'vitest'
import {act,cleanup,fireEvent,render,screen,within,waitFor} from '@testing-library/react'
import {RouterProvider,createMemoryRouter} from 'react-router'
import {QueryClient} from '@tanstack/react-query'
import userEvent from '@testing-library/user-event'
import {AppShell} from '../shell/AppShell'
import {ShellProvider} from '../shell/ShellProvider'
import {workRoutes} from './workRoutes'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import {WorkSalesDiscountFacts} from './WorkSalesDiscountFacts'
import {SALES_DISCOUNT_WORK_TEXT as T} from './workSalesDiscountText'
import {SALES_DISCOUNT_WORK_KIND as kind,SALES_DISCOUNT_WORK_CAPABILITY as cap} from './workSalesDiscountDecision'
import {salesDiscountHuman as human,salesDiscountFixture as fixture,salesDiscountId as id} from './salesDiscountDecisionFixture'
import {COMPANY_A,COMPANY_B,IDENTITY,installFetch,json,problem} from '../settings/testHarness'
const originalActor=IDENTITY.userId
afterEach(()=>{cleanup();sessionStorage.clear();vi.unstubAllGlobals();vi.restoreAllMocks();Object.assign(IDENTITY,{authenticationSessionBinding:null,userId:originalActor})})
const ownerPath=`/work/decisions/${kind}/${id}`
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/> </ShellProvider>);return client
}
function result(replayed=false){return json({companyId:COMPANY_A,kind,itemId:id,state:'Preparing',replayed,originalCorrelationId:'original-sales-discount'},200,replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original-sales-discount'}:undefined)}
function reasons(){for(const n of [1,2])fireEvent.change(screen.getByRole('textbox',{name:T.lineReason(n)}),{target:{value:`  SENTETIK satır ${n}  `}})}
// Actual Work router/page with synthetic HTTP; not native SQL/browser acceptance.
describe('Discount full human and frozen all-line Work lifecycle',()=>{
 it('shows every supplied line without truncation and keeps all raw money read-only',()=>{
  const h=human(COMPANY_A),first=h.lines[0]!,lines=Array.from({length:13},(_,i)=>({...first,lineId:`f3030000-0000-4000-8003-${String(i+1).padStart(12,'0')}`,lineNo:i+1,productName:`SENTETIK İskonto Ürün ${i+1}`}))
  render(<WorkSalesDiscountFacts human={{...h,lines}}/>);const region=screen.getByRole('region',{name:T.title})
  expect(within(region).getAllByRole('region')).toHaveLength(13)
  for(const value of [h.version,first.productVersion,first.quantity,first.discountCheck!.checkedAtUtc,'SENTETIK İskonto Ürün 13','100.0000','15.000','85'])expect(region.textContent).toContain(value)
  for(const role of ['button','textbox','checkbox','combobox'] as const)expect(within(region).queryByRole(role)).toBeNull()
  for(const value of ['sourceFactsDigest','previewDigest','originalDiscountApprovalReceipt','commercial','salesDiscountProof'])expect(region.textContent).not.toContain(value)
 })
 it.each([true,false])('requires a raw reason for every pending line with only the exact purpose cap visible=%s',async visible=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result():json(fixture(COMPANY_A,visible)):undefined})
  mount();await screen.findByRole('region',{name:T.title});const button=screen.getByRole('button',{name:W.approve})
  expect(button.matches(':disabled')).toBe(true);expect(screen.queryByRole('button',{name:W.reject})).toBeNull()
  fireEvent.change(screen.getByRole('textbox',{name:T.lineReason(1)}),{target:{value:'  SENTETIK satır 1  '}});expect(button.matches(':disabled')).toBe(true)
  fireEvent.change(screen.getByRole('textbox',{name:T.lineReason(2)}),{target:{value:'  SENTETIK satır 2  '}});expect(button.matches(':disabled')).toBe(false)
  fireEvent.click(button);await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null,salesDiscount:{lines:human(COMPANY_A).lines.map(l=>({lineId:l.lineId,reason:`  SENTETIK satır ${l.lineNo}  `}))}}])
  expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(calls.filter(c=>c.url.includes('/sales/'))).toEqual([])
  expect(screen.queryByRole('region',{name:T.title})).toBeNull()
 })
 it('enforces native trim-length without trimming the transmitted literal reasons',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result():json(fixture(COMPANY_A)):undefined});mount();await screen.findByRole('region',{name:T.title});reasons()
  const input=screen.getByRole('textbox',{name:T.lineReason(1)}),button=screen.getByRole('button',{name:W.approve})
  fireEvent.change(input,{target:{value:'r'.repeat(4001)}});expect(button.matches(':disabled')).toBe(true)
  const raw=' '+ 'r'.repeat(4000)+' ';fireEvent.change(input,{target:{value:raw}});expect(button.matches(':disabled')).toBe(false);fireEvent.click(button);await screen.findByText(W.done)
  expect((calls.find(c=>c.method==='POST')!.body as {salesDiscount:{lines:{reason:string}[]}}).salesDiscount.lines[0]!.reason).toBe(raw)
 })
 it.each([{capabilities:[]},{capabilities:['sales.orders.discount-exception.approve']},{capabilities:['sales.drafts.detail']},{capabilities:['sales.order-draft.work-decision']},{capabilities:['sales.quote-conversion-request.work-decision']}])('does not fetch for wrong/native capabilities %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A)):undefined});mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(ownerPath))).toBe(false)
 })
 it.each([false,true])('keeps terminal/max human read-only max=%s',async max=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,true,max?'9223372036854775807':'10',false)):undefined});mount();await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(screen.queryByRole('button',{name:W.reject})).toBeNull();expect(screen.queryByRole('textbox')).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('restores unknown with the same five-field body, all literal line reasons and key; no fresh GET on remount',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK Discount session'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(true):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});reasons();fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  const gets=calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET').length;expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  for(const value of ['100.0000','SENTETIK İskonto Ürün','sourceFactsDigest','originalDiscountApprovalReceipt','pricing'])expect(JSON.stringify(sessionStorage)).not.toContain(value)
  cleanup();mount();await screen.findByText(W.unknown);expect(screen.getByText(T.retained)).toBeTruthy();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(gets)
 })
 it('suppresses an in-flight/cached later snapshot throughout original-key reconciliation',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK Discount late'});let gets=0,posts=0,release!:(r:Response)=>void
  const pending=new Promise<Response>(r=>{release=r}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(true):++gets===1?json(fixture(COMPANY_A,true,'9')):pending:undefined})
  mount(client);await screen.findByRole('region',{name:T.title});reasons();let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  await act(async()=>{release(json(fixture(COMPANY_A,true,'11')));await refetch});expect(screen.queryByRole('region',{name:T.title})).toBeNull();fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent[1]!.body).toEqual(sent[0]!.body);expect((sent[1]!.body as {expectedVersion:string}).expectedVersion).toBe('9');expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it('invalidates all entered reasons when a fresh current human snapshot changes',async()=>{
  let gets=0;const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,true,++gets===1?'9':'11')):undefined});mount(client);await screen.findByRole('region',{name:T.title});reasons();expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(false)
  await act(async()=>{await client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})})
  // The refetch promise settles before the QueryObserver's scheduled React notification.
  await waitFor(()=>{
   expect(within(screen.getByRole('region',{name:T.title})).getByText('11',{exact:true})).toBeTruthy()
   for(const n of [1,2])expect((screen.getByRole('textbox',{name:T.lineReason(n)}) as HTMLTextAreaElement).value).toBe('')
   expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(true)
  });expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(2);expect(calls.some(c=>c.method==='POST')).toBe(false);client.clear()
 })
 it('requires new line confirmation after native409 and refreshes only the fresh refused attempt',async()=>{
  let gets=0;const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(409):json(fixture(COMPANY_A,true,++gets===1?'9':'11')):undefined});mount();await screen.findByRole('region',{name:T.title});reasons();fireEvent.click(screen.getByRole('button',{name:W.approve}));expect((await screen.findByRole('alert')).textContent).toBe(`${W.refused} (409)`)
  await waitFor(()=>{
   expect(gets).toBe(2);expect(within(screen.getByRole('region',{name:T.title})).getByText('11',{exact:true})).toBeTruthy()
   for(const n of [1,2])expect((screen.getByRole('textbox',{name:T.lineReason(n)}) as HTMLTextAreaElement).value).toBe('')
   expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(true)
  });expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it.each(['79228162514264337593543950336','0.00000000000000000000000000001'])('keeps malformed native CLR GET %s out of the human panel and approval controls',async value=>{
  const bad=fixture(COMPANY_A);Object.assign(bad.salesDiscount.lines[0]!.pricing!,{unitPrice:value,netUnitPrice:value})
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(bad):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
  expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(screen.queryByRole('textbox')).toBeNull()
  expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 // A business 403 invalidates shell capabilities; the real route then uses its neutral non-disclosure block.
 it.each([403,404,503])('renders GET refusal/unavailable without human facts HTTP%s',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?problem(status):undefined});mount();await waitFor(()=>expect(document.querySelector(`[data-state="${status===403||status===404?'not-found':'service-unavailable'}"]`)).not.toBeNull());expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(screen.queryByRole('textbox')).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['company','actor'])('does not expose or replay another %s context pending attempt',async change=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK Discount context'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST'){posts++;return problem(503)}return json(fixture(new Headers(init?.headers).get('X-Company-Id')??COMPANY_A))}})
  mount();await screen.findByRole('region',{name:T.title});reasons();fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  if(change==='company')await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
  else {cleanup();Object.assign(IDENTITY,{userId:'88888888-8888-4888-8888-888888888888'});mount()}
  await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(posts).toBe(1)
  expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('preserves the native unlisted writer self-approval exception without a universal UI maker ban',async()=>{
  const f=fixture(COMPANY_A,false,'9',true,false);for(const line of f.salesDiscount.lines)Object.assign(line.discountCheck!,{checkedBy:IDENTITY.userId})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result():json(f):undefined});mount();await screen.findByRole('region',{name:T.title});reasons()
  expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(false);fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('renders the native subcontract fact read-only without offering a discount command',async()=>{
  const f=fixture(COMPANY_A);Object.assign(f.salesDiscount,{isSubcontractService:true,approvalSupported:false});f.pending=false;f.approvalSupported=false
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(f):undefined});mount();await screen.findByRole('region',{name:T.title})
  expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(screen.queryByRole('textbox')).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('does not recover an unknown attempt under a different authentication session' ,async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK Discount old'});let posts=0
  installFetch({capabilities:[cap],respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST'){posts++;return problem(503)}return json(fixture(COMPANY_A))}});mount();await screen.findByRole('region',{name:T.title});reasons();fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  cleanup();Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK Discount replacement'});mount();await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(posts).toBe(1)
 })
})
