import {afterEach,describe,expect,it,vi} from 'vitest'
import {act,cleanup,fireEvent,render,renderHook,screen,within,waitFor} from '@testing-library/react'
import {WorkOrganizationCompanyTaxIdFacts} from './WorkOrganizationCompanyTaxIdFacts'
import {ORGANIZATION_COMPANY_TAX_ID_WORK_TEXT as T,ORGANIZATION_COMPANY_TAX_ID_WORK_KIND as kind,
 organizationCompanyTaxIdDecisionRequest,isOrganizationCompanyTaxIdDecisionRequest,type OrganizationCompanyTaxIdDecisionRequest} from './workOrganizationCompanyTaxIdDecision'
import {organizationCompanyTaxIdHuman as human,companyTaxIdRequestId as id,companyTaxIdActor as actor} from './organizationCompanyTaxIdDecisionFixture'
import {COMPANY_A,COMPANY_B,IDENTITY,installFetch,json,problem} from '../settings/testHarness'
import {RouterProvider,createMemoryRouter} from 'react-router'
import {QueryClient} from '@tanstack/react-query'
import {AppShell} from '../shell/AppShell'
import {ShellProvider} from '../shell/ShellProvider'
import {workRoutes} from './workRoutes'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import userEvent from '@testing-library/user-event'
import {ORGANIZATION_COMPANY_TAX_ID_WORK_CAPABILITY as cap} from './workOrganizationCompanyTaxIdDecision'
import {organizationCompanyTaxIdFixture as fixture} from './organizationCompanyTaxIdDecisionFixture'
import {COMPANY_TAX_ID_TEXT as N} from '../organization/companyTaxIdText'
import {settingsAttemptStoreKey,useCommandAttempt} from '../settings/useCommandAttempt'
import type {CommandOutcome} from '../settings/discountLimitApi'

afterEach(()=>{cleanup();sessionStorage.clear();vi.unstubAllGlobals();vi.restoreAllMocks();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
// Staged Facts + existing attempt-hook tests. No generic Page/transport/router or native command acceptance claim.
describe('staged company VKN facts and existing frozen decision protocol',()=>{
 it('renders all6/native10 facts, full reason and both distinct company versions without a form',()=>{
  const h=human(COMPANY_A);h.companyVersion='9223372036854775807';h.companyVkn='1111111114';h.isCurrent=false
  render(<WorkOrganizationCompanyTaxIdFacts human={h}/>);const region=screen.getByRole('region',{name:T.title})
  expect(region.querySelectorAll('dt')).toHaveLength(15)
  for(const value of [h.companyVersion,h.companyVersionAtRequest,h.companyVkn,h.request.currentVkn!,h.request.proposedVkn,h.request.reason,h.request.requestedBy,h.request.requestedAtUtc])expect(region.textContent).toContain(value)
  expect(within(region).getByRole('note').textContent).toBe(T.stale)
  for(const role of ['button','textbox','checkbox','combobox'] as const)expect(within(region).queryByRole(role)).toBeNull()
  for(const key of ['sourceFactsDigest','previewDigest','approvalReceipt','organizationCompanyTaxIdProof','rowVersion'])expect(region.textContent).not.toContain(key)
 })
 it('uses the existing native placeholder text for first VKN without inventing an old value',()=>{
  render(<WorkOrganizationCompanyTaxIdFacts human={human(COMPANY_A,true)}/>);expect(screen.getAllByText(N.noVkn)).toHaveLength(2)
  expect(screen.getByText('9876543217')).toBeTruthy();expect(screen.queryByRole('note')).toBeNull()
 })
 it.each(['approved','rejected'] as const)('shows native lowercase %s decision and its full optional note without interpreting a company-row version as request version',status=>{
  const h=human(COMPANY_A);h.companyVersion='9007199254740994';h.isCurrent=false;h.request={...h.request,status,decidedBy:actor,decidedAtUtc:'2026-11-28T09:00:00Z',decisionNote:'n'.repeat(1000)}
  render(<WorkOrganizationCompanyTaxIdFacts human={h}/>);const region=screen.getByRole('region',{name:T.title})
  expect(region.textContent).toContain(h.request.decisionNote);expect(region.textContent).toContain(actor);expect(region.textContent).toContain(h.request.decidedAtUtc)
  expect(screen.getByText(status==='approved'?'Onaylandı':'Reddedildi')).toBeTruthy();expect(screen.queryByRole('note')).toBeNull();expect(screen.queryByRole('button')).toBeNull()
 })
 it('restores only the original four fields, note and company version after a different current/stale proposal appears',async()=>{
  const original=organizationCompanyTaxIdDecisionRequest(human(COMPANY_A),true,false,'SENTETIK frozen decision note',null)!
  const binding={sessionBinding:'SENTETIK org59 session',actorId:actor,companyId:COMPANY_A},operation=`work-decision:${kind}:${id}`
  const storeKey=settingsAttemptStoreKey(binding,operation),send=vi.fn<(key:string)=>Promise<CommandOutcome<true>>>().mockResolvedValue({type:'unknown',reason:'server'})
  const restore=vi.fn<(payload:unknown,key:string)=>Promise<CommandOutcome<true>>>().mockResolvedValue({type:'applied',data:true,replayed:true})
  const first=renderHook(()=>useCommandAttempt<true>({storeKey,execute:restore,accepts:isOrganizationCompanyTaxIdDecisionRequest}))
  await act(async()=>{await first.result.current.run(original,send)});const key=send.mock.calls[0]![0]
  const journal=JSON.stringify(sessionStorage);for(const raw of [human(COMPANY_A).request.reason,'9876543217','1234567890','requestedBy','sourceFactsDigest','previewDigest'])expect(journal).not.toContain(raw)
  first.unmount();const current=human(COMPANY_A);current.companyVersion='9223372036854775807';current.companyVkn='1111111114';current.isCurrent=false
  const changed=organizationCompanyTaxIdDecisionRequest(current,true,false,'different note',null)!
  expect(changed.expectedVersion).not.toBe(original.expectedVersion)
  const next=renderHook(()=>useCommandAttempt<true>({storeKey,execute:restore,accepts:isOrganizationCompanyTaxIdDecisionRequest}))
  expect(next.result.current.pendingPayload).toEqual(original)
  await act(async()=>{await next.result.current.run(changed,send)});expect(send).toHaveBeenCalledTimes(1);expect(restore).not.toHaveBeenCalled()
  await act(async()=>{await next.result.current.retry()});expect(restore).toHaveBeenCalledExactlyOnceWith(original,key)
  expect(next.result.current.status.kind).toBe('applied');expect(Object.keys(original)).toEqual(['expectedVersion','approve','reason','delegationId'])
 })
 it.each(['company','actor','session','operation'] as const)('keeps the original unknown inaccessible to another %s binding',async fault=>{
  const binding={sessionBinding:'SENTETIK original org59',actorId:actor,companyId:COMPANY_A},operation=`work-decision:${kind}:${id}`
  const body:OrganizationCompanyTaxIdDecisionRequest={expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null}
  const send=vi.fn<(payload:unknown,key:string)=>Promise<CommandOutcome<true>>>().mockResolvedValue({type:'unknown',reason:'server'})
  const first=renderHook(()=>useCommandAttempt<true>({storeKey:settingsAttemptStoreKey(binding,operation),execute:send,accepts:isOrganizationCompanyTaxIdDecisionRequest}))
  await act(async()=>{await first.result.current.run(body,key=>send(body,key))});first.unmount()
  const other={...binding,companyId:fault==='company'?COMPANY_B:binding.companyId,actorId:fault==='actor'?'SENTETIK other':actor,sessionBinding:fault==='session'?'SENTETIK new session':binding.sessionBinding}
  const next=renderHook(()=>useCommandAttempt<true>({storeKey:settingsAttemptStoreKey(other,fault==='operation'?operation+':other':operation),execute:send,accepts:isOrganizationCompanyTaxIdDecisionRequest}))
  expect(next.result.current.hasPendingRetry).toBe(false);expect(next.result.current.pendingPayload).toBeNull();expect(send).toHaveBeenCalledTimes(1)
 })
 it('refuses a journal payload containing human VKN/request details rather than the exact command',()=>{
  const storeKey=settingsAttemptStoreKey({sessionBinding:'SENTETIK invalid org59',actorId:actor,companyId:COMPANY_A},`work-decision:${kind}:${id}`)!
  sessionStorage.setItem(storeKey,JSON.stringify({key:crypto.randomUUID(),claim:crypto.randomUUID(),payload:{expectedVersion:'1',approve:true,reason:null,delegationId:null,organizationCompanyTaxId:human(COMPANY_A)}}))
  const send=vi.fn<(payload:unknown,key:string)=>Promise<CommandOutcome<true>>>().mockResolvedValue({type:'unknown',reason:'server'})
  const hook=renderHook(()=>useCommandAttempt<true>({storeKey,execute:send,accepts:isOrganizationCompanyTaxIdDecisionRequest}))
  expect(hook.result.current.hasPendingRetry).toBe(false);expect(send).not.toHaveBeenCalled()
 })
})


const ownerPath=`/work/decisions/${kind}/${id}`
function mountCompanyTaxId(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return {client,router}
}
const result=(approve:boolean,replayed=false)=>json({companyId:COMPANY_A,kind,itemId:id,state:approve?'approved':'rejected',replayed,originalCorrelationId:'SENTETIK original company VKN'},200,
 replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original company VKN'}:undefined)
function staleFixture(company:string,version='9007199254740994'){
 const v=fixture(company);v.sourceVersion=version;v.organizationCompanyTaxId.companyVersion=version;v.organizationCompanyTaxId.isCurrent=false;v.approvalSupported=false
 v.rules.push({ruleCode:'organization.company-tax-id-change-stale',threshold:null,actual:null,deviation:null,unit:null});return v
}
// Actual generic Page and generated capability route over synthetic fetch; not a browser/native transaction acceptance.
describe('paired company VKN generic Work page',()=>{
 it.each([true,false])('sends an optional blank note as null on approve=%s and consumes only lowercase original state',async approve=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(approve):json(fixture(COMPANY_A)):undefined})
  mountCompanyTaxId();await screen.findByRole('region',{name:T.title});const note=screen.getByRole('textbox',{name:W.optionalNote})
  expect(note.getAttribute('maxlength')).toBe('1000');fireEvent.change(note,{target:{value:'   '}})
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'9007199254740993',approve,reason:null,delegationId:null}])
  expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1);expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  // Shell directory/setup admission remain; neither is the general native tax-id read.
  expect(calls.filter(c=>c.url.includes('/organization/')&&!c.url.endsWith('/organization/companies')&&!c.url.endsWith('/organization/company-setup/capabilities'))).toEqual([])
  expect(calls.some(c=>c.url.endsWith('/organization/companies'))).toBe(true)
 })
 it.each([true,false])('keeps a full1000-character note and permits it on approve=%s',async approve=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(approve):json(fixture(COMPANY_A)):undefined})
  mountCompanyTaxId();await screen.findByRole('region',{name:T.title});fireEvent.change(screen.getByRole('textbox',{name:W.optionalNote}),{target:{value:' '+ 'n'.repeat(1000)+' '}})
  expect(screen.getByRole('button',{name:approve?W.approve:W.reject}).matches(':disabled')).toBe(false)
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST')[0]!.body).toEqual({expectedVersion:'9007199254740993',approve,reason:'n'.repeat(1000),delegationId:null})
 })
 it('keeps stale first VKN reject-only and sends the CURRENT company version without requiring a rejection note',async()=>{
  const value=staleFixture(COMPANY_A);const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(false):json(value):undefined})
  mountCompanyTaxId();await screen.findByRole('region',{name:T.title});expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(true)
  expect(screen.getByRole('button',{name:W.reject}).matches(':disabled')).toBe(false)
  fireEvent.click(screen.getByRole('button',{name:W.reject}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST')[0]!.body).toEqual({expectedVersion:value.sourceVersion,approve:false,reason:null,delegationId:null})
 })
 it('permits maxInt64 rejection but never an overflowing approval',async()=>{
  const value=fixture(COMPANY_A);value.sourceVersion='9223372036854775807';value.organizationCompanyTaxId.companyVersion=value.sourceVersion;value.organizationCompanyTaxId.companyVersionAtRequest=value.sourceVersion;value.approvalSupported=false
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?result(false):json(value):undefined});mountCompanyTaxId()
  await screen.findByRole('region',{name:T.title});expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(true)
  fireEvent.click(screen.getByRole('button',{name:W.reject}));await screen.findByText(W.done);expect(calls.filter(c=>c.method==='POST')[0]!.body).toEqual({expectedVersion:value.sourceVersion,approve:false,reason:null,delegationId:null})
 })
 it.each(['bad\nnote','n'.repeat(1001)])('does not send a malformed native optional note %s',async note=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A)):undefined});mountCompanyTaxId();await screen.findByRole('region',{name:T.title})
  fireEvent.change(screen.getByRole('textbox',{name:W.optionalNote}),{target:{value:note}})
  for(const name of [W.approve,W.reject]){const button=screen.getByRole('button',{name});expect(button.matches(':disabled')).toBe(true);fireEvent.click(button)}
  expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each([true,false])('keeps unknown original body/version/note/key after remount despite current company version change, approve=%s',async approve=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK org59 retained'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):result(approve,true):json(posts?staleFixture(COMPANY_A):fixture(COMPANY_A)):undefined})
  mountCompanyTaxId();await screen.findByRole('region',{name:T.title});fireEvent.change(screen.getByRole('textbox',{name:W.optionalNote}),{target:{value:'SENTETIK original decision note'}})
  fireEvent.click(screen.getByRole('button',{name:approve?W.approve:W.reject}));await screen.findByText(W.unknown)
  const gets=calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET').length,journal=JSON.stringify(sessionStorage)
  for(const raw of ['9876543217','1234567890',human(COMPANY_A).request.reason,'requestedBy','sourceFactsDigest','previewDigest'])expect(journal).not.toContain(raw)
  cleanup();mountCompanyTaxId();await screen.findByText(W.unknown);expect(screen.getByText(T.retained)).toBeTruthy();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
  expect(sent[1]!.body).toEqual({expectedVersion:'9007199254740993',approve,reason:'SENTETIK original decision note',delegationId:null});expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(gets)
 })
 it('suppresses cached and late current human facts while reconciling an unknown original decision',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK org59 cached'});let gets=0,release!:(value:Response)=>void
  const pending=new Promise<Response>(resolve=>{release=resolve}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(503):++gets===1?json(fixture(COMPANY_A)):pending:undefined})
  mountCompanyTaxId(client);await screen.findByRole('region',{name:T.title});let refetch!:Promise<void>
  act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:W.reject}));await screen.findByText(W.unknown)
  const current=staleFixture(COMPANY_A);current.organizationCompanyTaxId.request={...current.organizationCompanyTaxId.request,reason:'SENTETIK LATE OTHER REQUEST'}
  await act(async()=>{release(json(current));await refetch});expect(screen.queryByText('SENTETIK LATE OTHER REQUEST')).toBeNull();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.unknown)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each([{capabilities:[]},{capabilities:['organization.company.tax-id']},{capabilities:['organization.company.tax-id.approve']},{capabilities:['organization.sites.list']}])('does not fetch a human preview with an unrelated/native capability %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A)):undefined});mountCompanyTaxId()
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(ownerPath))).toBe(false)
 })
 it.each([401,403,404,503])('does not render a partial human view or decision after HTTP %s',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?problem(status):undefined});mountCompanyTaxId()
  // A current403 re-fences the shell capability; the existing route boundary then hides the page.
  await waitFor(()=>expect(document.querySelector(`[data-state="${status===401?'unauthenticated':status===403||status===404?'not-found':'service-unavailable'}"]`)).not.toBeNull());expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['foreign','missing','private','wrongSupport'] as const)('refuses malformed/foreign current human %s',async fault=>{
  const v=fixture(COMPANY_A);if(fault==='foreign')v.organizationCompanyTaxId.companyId=COMPANY_B;if(fault==='missing')Reflect.deleteProperty(v,'organizationCompanyTaxId')
  if(fault==='private')Object.assign(v.organizationCompanyTaxId,{decisionReceipt:{}});if(fault==='wrongSupport')v.approvalSupported=false
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(v):undefined});mountCompanyTaxId()
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['company','actor','session'] as const)('keeps an old unknown inaccessible after %s transition',async change=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK org59 original'});const priorActor=IDENTITY.userId
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST')return problem(503)
   const company=new Headers(init?.headers).get('X-Company-Id');if(company===null)throw new Error('Company required');return json(fixture(company))}})
  mountCompanyTaxId();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.reject}));await screen.findByText(W.unknown)
  try{if(change==='company')await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
   else{cleanup();Object.assign(IDENTITY,change==='actor'?{userId:'ac590000-0000-4000-8000-000000000099'}:{authenticationSessionBinding:'SENTETIK org59 replacement'});mountCompanyTaxId()}
   await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
  }finally{Object.assign(IDENTITY,{userId:priorActor})}
 })
 it('does not retry an old unknown after the exact purpose capability is revoked',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK org59 revoked'});const capabilities=[cap]
  const calls=installFetch({capabilities,respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(503):json(fixture(COMPANY_A)):undefined})
  mountCompanyTaxId();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.reject}));await screen.findByText(W.unknown)
  cleanup();capabilities.splice(0);mountCompanyTaxId();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
})
