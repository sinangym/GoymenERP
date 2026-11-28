import {afterEach,describe,it,expect,vi} from 'vitest'
import {act,cleanup,fireEvent,render,screen,within,waitFor} from '@testing-library/react'
import {RouterProvider,createMemoryRouter} from 'react-router'
import userEvent from '@testing-library/user-event'
import {QueryClient} from '@tanstack/react-query'
import {AppShell} from '../shell/AppShell'
import {CapabilityRoute} from '../shell/CapabilityRoute'
import {ShellProvider} from '../shell/ShellProvider'
import {WorkDecisionPage} from './WorkDecisionPage'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import {WorkIdentityAccessGrantFacts} from './WorkIdentityAccessGrantFacts'
import {IDENTITY_ACCESS_GRANT_WORK_TEXT as T} from './workIdentityAccessGrantText'
import {IDENTITY_ACCESS_GRANT_WORK_KIND as kind,IDENTITY_ACCESS_GRANT_WORK_CAPABILITY as cap} from './workIdentityAccessGrantDecision'
import {identityAccessGrantHuman as human,identityAccessGrantFixture as fixture,identityAccessGrantId as id,identityAccessGrantImpact as token} from './identityAccessGrantDecisionFixture'
import {COMPANY_A,COMPANY_B,IDENTITY,installFetch,json,problem} from '../settings/testHarness'
afterEach(()=>{cleanup();sessionStorage.clear();vi.unstubAllGlobals();vi.restoreAllMocks();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
const path=`/work/decisions/${kind}/${id}`
// Candidate-only real shell/auth/cap route. Publication is deliberately a separate root-coordinated change.
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:[{path:`onaylarim/${kind}/:approvalId`,element:<CapabilityRoute capability={[cap]}><WorkDecisionPage kind={kind}/></CapabilityRoute>}]}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return {client,router}
}
const result=(approve:boolean,replayed=false)=>json({companyId:COMPANY_A,kind,itemId:id,state:approve?'Approved':'Rejected',replayed,originalCorrelationId:'original-identity-access'},200,replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'original-identity-access'}:undefined)
describe('Identity access Work client-seen permission binding over synthetic HTTP, not SQL/browser acceptance',()=>{
 it('renders all native request metadata and current permission codes with no proof/token or controls in human region',()=>{
  const h=human();render(<WorkIdentityAccessGrantFacts human={h}/>);const region=screen.getByRole('region',{name:T.title})
  expect(region.querySelectorAll('dt')).toHaveLength(18)
  for(const value of [h.request.requestId,h.request.requestedBy.userId,h.request.requestedBy.displayName,h.request.target.userId,h.request.target.displayName,h.request.roleId,h.request.roleName,h.request.roleCode,h.request.version,h.request.reason!,h.request.requestedAtUtc,h.request.validFromUtc!,h.request.validUntilUtc!,...h.currentPermissions])expect(region.textContent).toContain(value)
  expect(region.textContent).toContain(T.readOnly);expect(region.textContent).not.toContain(token)
  for(const name of ['sourceFactsDigest','previewDigest','appliedImpactDigest','decisionReceipt','expectedImpactDigest'])expect(region.textContent).not.toContain(name)
  for(const role of ['button','textbox','checkbox','combobox'] as const)expect(within(region).queryByRole(role)).toBeNull()
 })
 it('shows an explicit empty current list without claiming an empty historical applied set',()=>{
  render(<WorkIdentityAccessGrantFacts human={{...human(),currentPermissions:[],currentRoleIsCritical:false}}/>);expect(screen.getByText(T.emptyPermissions)).toBeTruthy();expect(screen.getByText(T.readOnly)).toBeTruthy()
 })
 it('approves only the client-seen token in exact5 fields without general role/user reads',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?result(true):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.done)
  const post=calls.filter(c=>c.method==='POST');expect(post).toHaveLength(1);expect(post[0]!.body).toEqual({expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null,identityAccessGrant:{expectedImpactDigest:token}})
  expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET')).toHaveLength(1);expect(calls.filter(c=>c.url.includes('/identity/access/')||c.url.includes('/identity/roles/'))).toEqual([])
  expect(screen.queryByRole('region',{name:T.title})).toBeNull()
 })
 it('requires native canonical rejection reason code up to64 while approval remains null-reason',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?result(false):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});const field=screen.getByRole('textbox',{name:T.reasonCode}),reject=screen.getByRole('button',{name:W.reject})
  expect(field.getAttribute('maxlength')).toBe('64');expect(reject.matches(':disabled')).toBe(true)
  for(const bad of ['bad code','BAD',' reason','reason ','bad/code']){fireEvent.change(field,{target:{value:bad}});expect(reject.matches(':disabled')).toBe(true)}
  const reason='r'.repeat(64);fireEvent.change(field,{target:{value:reason}});fireEvent.click(reject);await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST')[0]!.body).toEqual({expectedVersion:'9007199254740993',approve:false,reason,delegationId:null,identityAccessGrant:{expectedImpactDigest:token}})
 })
 it.each(['targetActive','roleActive'] as const)('inactive %s can only reject the pending request',async field=>{
  const v=fixture(COMPANY_A);v.identityAccessGrant={...v.identityAccessGrant,[field]:false};v.approvalSupported=false
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(v):undefined});mount();await screen.findByRole('region',{name:T.title})
  expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(true);fireEvent.change(screen.getByRole('textbox',{name:T.reasonCode}),{target:{value:'not_required'}})
  expect(screen.getByRole('button',{name:W.reject}).matches(':disabled')).toBe(false);expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('MaxInt64 is visible but both incrementing commands remain unavailable',async()=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(fixture(COMPANY_A,'9223372036854775807')):undefined});mount();await screen.findByRole('region',{name:T.title})
  fireEvent.change(screen.getByRole('textbox',{name:T.reasonCode}),{target:{value:'not_required'}})
  for(const name of [W.approve,W.reject])expect(screen.getByRole('button',{name}).matches(':disabled')).toBe(true);expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['Approved','Rejected','Withdrawn'] as const)('terminal %s has no mutation actions',async status=>{
  installFetch({capabilities:[cap],respond:url=>url.includes(path)?json(fixture(COMPANY_A,'2',status)):undefined});mount();await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(screen.queryByRole('button',{name:W.reject})).toBeNull()
 })
 it.each([{capabilities:[]},{capabilities:['identity.access.approve']},{capabilities:['identity.access.assignments.read']},{capabilities:['sales.request.work-decision']}])('does not fetch under unrelated/native capability %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(path)?json(fixture(COMPANY_A)):undefined});mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(path))).toBe(false)
 })
 it.each([403,503])('does not promote owner failure %s to human facts or a command',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(path)?problem(status):undefined});mount();await waitFor(()=>expect(document.querySelector(`[data-state="${status===403?'not-found':'service-unavailable'}"]`)).not.toBeNull());expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('rejects malformed server token without showing permissions',async()=>{
  const v=fixture(COMPANY_A);installFetch({capabilities:[cap],respond:url=>url.includes(path)?json({...v,identityAccessGrant:{...v.identityAccessGrant,expectedImpactDigest:'a'.repeat(64)}}):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.title})).toBeNull()
 })
 it.each([true,false])('retains exact original5body/key/token across unknown + route remount approve=%s',async approve=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK identity access session'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?++posts===1?problem(503):result(approve,true):json(posts?{...fixture(COMPANY_A,'2','Approved'),identityAccessGrant:{...human('2','Approved'),expectedImpactDigest:'B'.repeat(64),currentPermissions:['SENTETIK.changed']}}:fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});if(!approve)fireEvent.change(screen.getByRole('textbox',{name:T.reasonCode}),{target:{value:'not_required'}})
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.unknown)
  const gets=calls.filter(c=>c.url.includes(path)&&c.method==='GET').length
  for(const value of [human().request.reason!,human().request.roleCode,...human().currentPermissions,'sourceFactsDigest','previewDigest','appliedImpactDigest'])expect(JSON.stringify(sessionStorage)).not.toContain(value)
  cleanup();mount();await screen.findByText(W.unknown);expect(screen.getByText(T.retained)).toBeTruthy();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
  expect(sent[1]!.body).toEqual({expectedVersion:'9007199254740993',approve,reason:approve?null:'not_required',delegationId:null,identityAccessGrant:{expectedImpactDigest:token}});expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET')).toHaveLength(gets)
 })
 it('hides a late in-flight changed permission list and retries only the earlier client-seen token',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK identity late'});let posts=0,gets=0,release!:(r:Response)=>void;const pending=new Promise<Response>(r=>{release=r})
  const client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?++posts===1?problem(503):result(true,true):++gets===1?json(fixture(COMPANY_A)):pending:undefined})
  mount(client);await screen.findByRole('region',{name:T.title});let refetch!:Promise<void>;act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  const later=fixture(COMPANY_A);later.identityAccessGrant={...later.identityAccessGrant,expectedImpactDigest:'B'.repeat(64),currentPermissions:['SENTETIK.changed']}
  await act(async()=>{release(json(later));await refetch});expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(screen.queryByText('SENTETIK.changed')).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.body).toEqual({expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null,identityAccessGrant:{expectedImpactDigest:token}});client.clear()
 })
 it('unbound historical receipt503 stays unknown and never grants a new current token automatically',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK unbound receipt'});const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown);fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.unknown)
  await waitFor(()=>expect(calls.filter(c=>c.method==='POST')).toHaveLength(2));const posts=calls.filter(c=>c.method==='POST');expect(posts[1]!.body).toEqual(posts[0]!.body);expect(posts[1]!.headers['Idempotency-Key']).toBe(posts[0]!.headers['Idempotency-Key']);expect(screen.queryByRole('region',{name:T.title})).toBeNull()
 })
 it('a company switch cannot retry the old company command and reads only the new company scope',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK company isolation'});const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):json(fixture(new Headers(init?.headers).get('X-Company-Id')??COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B);await screen.findByRole('region',{name:T.title})
  expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
  expect(calls.filter(c=>c.url.includes(path)&&c.method==='GET').at(-1)?.headers['X-Company-Id']).toBe(COMPANY_B)
 })
 it('revoking the exact purpose capability hides a retained unknown retry',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cap isolation'});const capabilities=[cap],calls=installFetch({capabilities,respond:(url,init)=>url.includes(path)?init?.method==='POST'?problem(503):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown);cleanup();capabilities.splice(0);mount()
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('does not restore an earlier actor/session command into a fresh authentication session',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK before session'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(path)?init?.method==='POST'?(posts++,problem(503)):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown);cleanup()
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK after session'});mount();await screen.findByRole('region',{name:T.title});expect(screen.queryByText(W.unknown)).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1);expect(posts).toBe(1)
 })
})
