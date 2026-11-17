import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react'
import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest'
import {SalesOfficialInvoiceCancellationPage} from './SalesOfficialInvoiceCancellationPage'
import {OFFICIAL_CANCELLATION_TEXT as T} from './officialInvoiceCancellationText'
import {cancellationCandidatesFixture,OFFICIAL_TEST_INVOICE as invoice} from './officialInvoiceCancellationTestData'
import {resetPendingPoolAttempts} from './pendingPoolAttempts'
const auth=vi.hoisted(()=>({capture:vi.fn(()=>({token:1})),report:vi.fn()}));const enabled=vi.hoisted(()=>({capabilities:['sales.invoices.finalize']}));const dispatch=vi.hoisted(()=>vi.fn())
vi.mock('../shell/useAuthBoundary',()=>({useAuthBoundary:()=>auth}))
vi.mock('../shell/shellContext',async original=>({...await original<typeof import('../shell/shellContext')>(),useShellState:()=>({sessionEpoch:1,routeEntryGeneration:0,requestFenceGeneration:0,committedContextRevision:1,activeContext:{scopeMode:'single',companyId:'11111111-1111-4111-8111-111111111111',userId:'SENTETIK-user',capabilityGeneration:'1',capabilities:enabled.capabilities}}),useShellDispatch:()=>dispatch}))
vi.mock('../shell/shellState',async original=>({...await original<typeof import('../shell/shellState')>(),hasCapability:(_state:unknown,name:string)=>enabled.capabilities.includes(name)}))
beforeEach(()=>{enabled.capabilities=['sales.invoices.finalize'];auth.report.mockClear()})
afterEach(()=>{cleanup();resetPendingPoolAttempts();vi.unstubAllGlobals()})
const response=(body:unknown,status=200)=>new Response(JSON.stringify(body),{status,headers:{'Content-Type':'application/json'}})
describe('SAT-SC34 finalizer-purpose workflow',()=>{
 it('does not request invoice or price grants and uses an actual bounded candidate selection',async()=>{
  const urls:string[]=[];vi.stubGlobal('fetch',vi.fn(async(url:string)=>{urls.push(url);return response(cancellationCandidatesFixture())}))
  render(<SalesOfficialInvoiceCancellationPage/>);await screen.findByRole('option',{name:/SF-SENTETIK-1/})
  expect(screen.queryByRole('textbox')).toBeNull();fireEvent.change(screen.getByRole('combobox',{name:T.invoice}),{target:{value:invoice}})
  await screen.findByRole('textbox',{name:T.reason});expect(urls).toHaveLength(1);expect(urls[0]).toContain('/official-cancellations/candidates?kind=Normal')
 })
 it('keeps unknown provider outcome and resends exactly the same key and body',async()=>{
  const posts:RequestInit[]=[];vi.stubGlobal('fetch',vi.fn(async(_url:string,init:RequestInit={})=>{if(init.method!=='POST')return response(cancellationCandidatesFixture());posts.push(init);return response({status:503,code:'official_cancellation_evidence_unavailable'},503)}))
  const first=render(<SalesOfficialInvoiceCancellationPage/>);await screen.findByRole('option',{name:/SF-SENTETIK-1/});fireEvent.change(screen.getByRole('combobox',{name:T.invoice}),{target:{value:invoice}})
  fireEvent.change(await screen.findByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK'}});fireEvent.click(screen.getByRole('button',{name:T.record}));await screen.findByText(T.proofUnavailable)
  first.unmount();render(<SalesOfficialInvoiceCancellationPage/>);await screen.findByRole('option',{name:/SF-SENTETIK-1/});fireEvent.change(screen.getByRole('combobox',{name:T.invoice}),{target:{value:invoice}})
  expect((await screen.findByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).value).toBe('SENTETIK');
  fireEvent.click(await screen.findByRole('button',{name:T.retrySame}));await waitFor(()=>expect(posts).toHaveLength(2));expect(posts[1].body).toBe(posts[0].body);expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'))
 })
 it('does not show controls or read candidates when finalize capability is absent',()=>{
  enabled.capabilities=['sales.invoices.read'];const fetcher=vi.fn();vi.stubGlobal('fetch',fetcher);render(<SalesOfficialInvoiceCancellationPage/>);expect(screen.getByText(T.noAuthority)).toBeTruthy();expect(fetcher).not.toHaveBeenCalled()
 })
})
