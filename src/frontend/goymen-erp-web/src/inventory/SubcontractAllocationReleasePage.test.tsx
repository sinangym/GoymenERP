import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react'
import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest'
import {SubcontractAllocationReleasePage} from './SubcontractAllocationReleasePage'
import {SUBCONTRACT_RELEASE_TEXT as T} from './subcontractAllocationReleaseText'
import {RELEASE_ALLOCATION as id,releaseCandidateFixture} from './subcontractAllocationReleaseTestData'
import {resetPendingPoolAttempts} from '../sales/pendingPoolAttempts'
const auth=vi.hoisted(()=>({capture:vi.fn(()=>({token:1})),report:vi.fn()}));const enabled=vi.hoisted(()=>({capabilities:['inventory.subcontract-allocations.release-unused']}));const dispatch=vi.hoisted(()=>vi.fn())
vi.mock('../shell/useAuthBoundary',()=>({useAuthBoundary:()=>auth}))
vi.mock('../shell/shellContext',async original=>({...await original<typeof import('../shell/shellContext')>(),useShellState:()=>({sessionEpoch:1,routeEntryGeneration:0,requestFenceGeneration:0,committedContextRevision:1,activeContext:{scopeMode:'single',companyId:'11111111-1111-4111-8111-111111111111',userId:'SENTETIK-user',capabilityGeneration:'1',capabilities:enabled.capabilities}}),useShellDispatch:()=>dispatch}))
vi.mock('../shell/shellState',async original=>({...await original<typeof import('../shell/shellState')>(),hasCapability:(_state:unknown,name:string)=>enabled.capabilities.includes(name)}))
beforeEach(()=>{enabled.capabilities=['inventory.subcontract-allocations.release-unused'];auth.report.mockClear()})
afterEach(()=>{cleanup();resetPendingPoolAttempts();vi.unstubAllGlobals()})
const response=(body:unknown,status=200)=>new Response(JSON.stringify(body),{status,headers:{'Content-Type':'application/json'}})
describe('STK-S3 explicit unused release',()=>{
 it('uses actual allocation choices without a GUID input or a general stock read',async()=>{
  const urls:string[]=[];vi.stubGlobal('fetch',vi.fn(async(url:string)=>{urls.push(url);return response(releaseCandidateFixture())}))
  render(<SubcontractAllocationReleasePage/>);await screen.findByRole('option',{name:/SENTETIK-FASON-1/});expect(screen.queryByRole('textbox')).toBeNull()
  fireEvent.change(screen.getByRole('combobox',{name:T.allocation}),{target:{value:id}});await screen.findByRole('textbox',{name:T.reason})
  expect(urls).toHaveLength(1);expect(urls[0]).toContain('/subcontract-allocations/release-candidates');expect(screen.getByText(`${T.remaining}: 20`)).toBeTruthy()
 })
 it('keeps unknown outcomes across remount and retries exactly the held reason key and payload',async()=>{
  const posts:RequestInit[]=[];vi.stubGlobal('fetch',vi.fn(async(_url:string,init:RequestInit={})=>{if(init.method!=='POST')return response(releaseCandidateFixture());posts.push(init);return response({status:503},503)}))
  const first=render(<SubcontractAllocationReleasePage/>);await screen.findByRole('option',{name:/SENTETIK-FASON-1/});fireEvent.change(screen.getByRole('combobox',{name:T.allocation}),{target:{value:id}})
  fireEvent.change(await screen.findByRole('textbox',{name:T.reason}),{target:{value:'SENTETIK explicit'}});fireEvent.click(screen.getByRole('button',{name:T.release}));await screen.findByRole('button',{name:T.retry})
  first.unmount();render(<SubcontractAllocationReleasePage/>);expect((await screen.findByRole('textbox',{name:T.reason}) as HTMLTextAreaElement).value).toBe('SENTETIK explicit')
  fireEvent.click(await screen.findByRole('button',{name:T.retry}));await waitFor(()=>expect(posts).toHaveLength(2));expect(posts[1].body).toBe(posts[0].body);expect(new Headers(posts[1].headers).get('Idempotency-Key')).toBe(new Headers(posts[0].headers).get('Idempotency-Key'))
 })
 it('does not read or offer a release without the actual write capability',()=>{
  enabled.capabilities=[];const fetcher=vi.fn();vi.stubGlobal('fetch',fetcher);render(<SubcontractAllocationReleasePage/>);expect(screen.getByText(T.forbidden)).toBeTruthy();expect(fetcher).not.toHaveBeenCalled()
 })
})
