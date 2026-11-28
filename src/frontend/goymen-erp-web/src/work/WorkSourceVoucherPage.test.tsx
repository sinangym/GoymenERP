import {afterEach,describe,expect,it,vi} from 'vitest'
import {act,cleanup,fireEvent,render,renderHook,screen,waitFor,within} from '@testing-library/react'
import {WorkSourceVoucherFacts} from './WorkSourceVoucherFacts'
import {SOURCE_VOUCHER_WORK_KIND as kind,SOURCE_VOUCHER_WORK_CAPABILITY as cap,SOURCE_VOUCHER_WORK_TEXT as T,isSourceVoucherDecisionRequest} from './workSourceVoucherDecision'
import {sourceVoucherHuman,sourceVoucherReversalHuman,sourceVoucherFixture as fixture,sourceVoucherId as id,sourceVoucherActor as actor} from './sourceVoucherDecisionFixture'
import {SOURCE_VOUCHER_TEXT as N} from '../accounting/sourceVoucherText'
import {COMPANY_A,COMPANY_B,IDENTITY,installFetch,json,problem} from '../settings/testHarness'
import {settingsAttemptStoreKey,useCommandAttempt} from '../settings/useCommandAttempt'
import userEvent from '@testing-library/user-event'
import {QueryClient} from '@tanstack/react-query'
import {RouterProvider,createMemoryRouter} from 'react-router'
import {AppShell} from '../shell/AppShell'
import {ShellProvider} from '../shell/ShellProvider'
import {workRoutes} from './workRoutes'
import {WORK_DECISION_TEXT as W} from './workDecisionText'
import type {CommandOutcome} from '../settings/discountLimitApi'

afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
// Original core cohort: Facts + existing journal protocol. The separate published cohort below exercises the real generic router.
describe('staged source voucher human facts and existing frozen command protocol',()=>{
 it('shows every complete line and raw public header without a financial calculation or an action',()=>{
  const h=sourceVoucherHuman(COMPANY_A);render(<WorkSourceVoucherFacts human={h}/>)
  const region=screen.getByRole('region',{name:T.title}),table=within(region).getByRole('table',{name:T.lines})
  expect(table.querySelectorAll('thead th')).toHaveLength(9);expect(table.querySelectorAll('tbody tr')).toHaveLength(137)
  expect(table.querySelectorAll('tbody tr')[136]?.textContent).toContain('SENTETIK full account 136')
  for(const raw of [h.voucher.finalizedVersion,h.voucher.mappingVersion,h.createdAtUtc,'1.0000','0001-01-02','9999-12-31','SENTETIK-57'])expect(region.textContent).toContain(raw)
  expect(within(region).getByText(N.statuses.AwaitingApproval)).toBeTruthy();expect(within(region).queryByRole('button')).toBeNull()
  expect(within(region).queryByRole('textbox')).toBeNull();expect(within(region).queryByRole('combobox')).toBeNull()
  for(const privateKey of ['sourceFactsDigest','previewDigest','approvalReceipt','sourceVoucherProof'])expect(region.textContent).not.toContain(privateKey)
 })
 it('shows native period waiting without selecting another period or replacing the full source reason',()=>{
  const h=sourceVoucherHuman(COMPANY_A,false);render(<WorkSourceVoucherFacts human={h}/>)
  expect(screen.getByRole('note').textContent).toBe(N.period);expect(screen.getByText(N.statuses.PeriodWaiting)).toBeTruthy()
  expect(screen.queryByRole('combobox')).toBeNull();expect(screen.queryByRole('button')).toBeNull()
 })
 it.each([true,false])('shows all16 original reversal fields and untruncated cancellation reason, approved=%s',approved=>{
  const h=sourceVoucherReversalHuman(COMPANY_A,approved);render(<WorkSourceVoucherFacts human={h}/>)
  const region=screen.getByRole('region',{name:T.title}),reversal=screen.getByRole('heading',{name:T.reversal})
  expect(reversal.nextElementSibling?.querySelectorAll('dt')).toHaveLength(16)
  expect(region.textContent).toContain(h.voucher.cancellationReason);expect(region.textContent).toContain('SENTETIK retained reversal reason')
  expect(region.textContent).toContain('2147483647');expect(region.textContent).toContain(h.voucher.reversalProposal!.proposedAtUtc)
  expect(region.textContent).toContain(N.statuses[h.voucher.status]);expect(screen.queryByRole('button')).toBeNull()
 })
 it('uses the existing journal to restore only the original four-field body/key after current Posted2/cancelled facts',async()=>{
  const binding={sessionBinding:'SENTETIK source voucher session',actorId:actor,companyId:COMPANY_A},operation=`work-decision:${kind}:${id}`
  const storeKey=settingsAttemptStoreKey(binding,operation),body={expectedVersion:'1',approve:true,reason:null,delegationId:null} as const
  const unknown:CommandOutcome<true>={type:'unknown',reason:'server'},applied:CommandOutcome<true>={type:'applied',data:true,replayed:true}
  const firstSend=vi.fn<(key:string)=>Promise<CommandOutcome<true>>>().mockResolvedValue(unknown)
  const restoreSend=vi.fn<(payload:unknown,key:string)=>Promise<CommandOutcome<true>>>().mockResolvedValue(applied)
  const first=renderHook(()=>useCommandAttempt<true>({storeKey,execute:restoreSend,accepts:isSourceVoucherDecisionRequest}))
  await act(async()=>{await first.result.current.run(body,firstSend)})
  const key=firstSend.mock.calls[0]![0];expect(first.result.current.hasPendingRetry).toBe(true)
  const pendingJournal=JSON.stringify(sessionStorage);for(const secret of ['\"voucher\"','\"lines\"','cancellationReason','sourceFactsDigest','previewDigest','SENTETIK full account','SENTETIK-57'])expect(pendingJournal).not.toContain(secret)
  first.unmount()
  const current=sourceVoucherReversalHuman(COMPANY_A);expect(current.voucher.version).toBe('3');expect(current.periodOpen).toBe(false);expect(current.sourceCancelled).toBe(true)
  const next=renderHook(()=>useCommandAttempt<true>({storeKey,execute:restoreSend,accepts:isSourceVoucherDecisionRequest}))
  expect(next.result.current.pendingPayload).toEqual(body)
  await act(async()=>{await next.result.current.run({...body,expectedVersion:current.voucher.version},firstSend)})
  expect(restoreSend).not.toHaveBeenCalled();expect(firstSend).toHaveBeenCalledTimes(1)
  await act(async()=>{await next.result.current.retry()})
  expect(restoreSend).toHaveBeenCalledExactlyOnceWith(body,key);expect(next.result.current.status.kind).toBe('applied')
  const journal=JSON.stringify(sessionStorage);for(const secret of ['voucher','lines','cancellationReason','sourceFactsDigest','previewDigest',current.voucher.cancellationReason!])expect(journal).not.toContain(secret)
 })
 it.each(['company','actor','session','operation'] as const)('does not restore an unresolved body into another %s binding',async fault=>{
  const binding={sessionBinding:'SENTETIK original session',actorId:actor,companyId:COMPANY_A},operation=`work-decision:${kind}:${id}`
  const storeKey=settingsAttemptStoreKey(binding,operation),body={expectedVersion:'1',approve:true,reason:null,delegationId:null} as const
  const send=vi.fn<(payload:unknown,key:string)=>Promise<CommandOutcome<true>>>().mockResolvedValue({type:'unknown',reason:'server'})
  const first=renderHook(()=>useCommandAttempt<true>({storeKey,execute:send,accepts:isSourceVoucherDecisionRequest}))
  await act(async()=>{await first.result.current.run(body,key=>send(body,key))});first.unmount()
  const other={...binding,companyId:fault==='company'?COMPANY_B:binding.companyId,actorId:fault==='actor'?id:binding.actorId,sessionBinding:fault==='session'?'SENTETIK new session':binding.sessionBinding}
  const next=renderHook(()=>useCommandAttempt<true>({storeKey:settingsAttemptStoreKey(other,fault==='operation'?operation+':other':operation),execute:send,accepts:isSourceVoucherDecisionRequest}))
  expect(next.result.current.hasPendingRetry).toBe(false);expect(next.result.current.pendingPayload).toBeNull();expect(send).toHaveBeenCalledTimes(1)
 })
 it('refuses a journal carrying human facts instead of treating them as an approval body',()=>{
  const binding={sessionBinding:'SENTETIK poisoned session',actorId:actor,companyId:COMPANY_A},storeKey=settingsAttemptStoreKey(binding,`work-decision:${kind}:${id}`)!
  sessionStorage.setItem(storeKey,JSON.stringify({key:crypto.randomUUID(),claim:crypto.randomUUID(),payload:{expectedVersion:'1',approve:true,reason:null,delegationId:null,sourceVoucher:sourceVoucherHuman(COMPANY_A)}}))
  const send=vi.fn<(payload:unknown,key:string)=>Promise<CommandOutcome<true>>>().mockResolvedValue({type:'unknown',reason:'server'})
  const result=renderHook(()=>useCommandAttempt<true>({storeKey,execute:send,accepts:isSourceVoucherDecisionRequest}))
  expect(result.result.current.hasPendingRetry).toBe(false);expect(send).not.toHaveBeenCalled()
 })
})


const ownerPath=`/work/decisions/${kind}/${id}`
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})){
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return {router,client}
}
const applied=(replayed=false)=>json({companyId:COMPANY_A,kind,itemId:id,state:'Posted',replayed,originalCorrelationId:'SENTETIK original voucher'},200,
 replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original voucher'}:undefined)
function currentFixture(company:string){const value=fixture(company,false);return {...value,pending:false,sourceVersion:'3',sourceVoucher:sourceVoucherReversalHuman(company,false)}}
// Actual generated route and Work adapter over synthetic fetch. No browser, actual HTTP or native transaction acceptance.
describe('published source voucher generic Work page',()=>{
 it('shows all human lines, sends only the original four fields and resolves Posted without a current read',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?applied():json(fixture(COMPANY_A)):undefined})
  mount();const region=await screen.findByRole('region',{name:T.title});expect(within(region).getByRole('table',{name:T.lines}).querySelectorAll('tbody tr')).toHaveLength(137)
  expect(screen.queryByRole('button',{name:W.reject})).toBeNull();expect(screen.queryByRole('textbox',{name:W.reason})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'1',approve:true,reason:null,delegationId:null}])
  expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1)
  expect(calls.some(c=>c.url.includes('/accounting/'))).toBe(false)
 })
 it('preserves native period waiting as read-only fresh action without choosing another period',async()=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A,false)):undefined});mount()
  await screen.findByRole('region',{name:T.title});expect(screen.getByRole('button',{name:W.approve}).matches(':disabled')).toBe(true)
  expect(screen.queryByRole('combobox',{name:N.period})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('waits for the complete owner human response before exposing a decision',async()=>{
  let release!:(response:Response)=>void;const pending=new Promise<Response>(resolve=>{release=resolve})
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?pending:undefined});mount()
  await waitFor(()=>expect(calls.some(c=>c.url.includes(ownerPath))).toBe(true));expect(screen.queryByRole('button',{name:W.approve})).toBeNull()
  await act(async()=>release(json(fixture(COMPANY_A))));await screen.findByRole('region',{name:T.title});expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('restores an unknown original key/body without GET despite later cancellation3 and a closed period',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK voucher retained'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):applied(true):json(posts?currentFixture(COMPANY_A):fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  const gets=calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET').length,journal=JSON.stringify(sessionStorage)
  for(const raw of ['SENTETIK-57','SENTETIK full account','sourceFactsDigest','previewDigest','functionalPayable','cancellationReason'])expect(journal).not.toContain(raw)
  cleanup();mount();await screen.findByText(W.unknown);expect(screen.getByText(T.retained)).toBeTruthy();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(gets)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 })
 it('hides cached and already in-flight current human facts while an original receipt is unknown',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK voucher late'});let gets=0,release!:(response:Response)=>void
  const late=new Promise<Response>(resolve=>{release=resolve}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(503):++gets===1?json(fixture(COMPANY_A)):late:undefined})
  mount(client);await screen.findByRole('region',{name:T.title});let refetch!:Promise<void>
  act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  const current=currentFixture(COMPANY_A);current.sourceVoucher.voucher={...current.sourceVoucher.voucher,cancellationReason:'SENTETIK LATE OTHER VOUCHER'}
  await act(async()=>{release(json(current));await refetch});expect(screen.queryByText('SENTETIK LATE OTHER VOUCHER')).toBeNull();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.unknown)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each([{capabilities:[]},{capabilities:['accounting.source-vouchers.approve']},{capabilities:['accounting.source-vouchers.list']},{capabilities:['accounting.normal-journal-draft.work-decision']}])('does not read the owner without the exact purpose cap %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A)):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(ownerPath))).toBe(false)
 })
 it.each([401,403,404,503])('does not expose human facts or a decision for owner HTTP %s',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?problem(status):undefined});mount()
  await waitFor(()=>expect(calls.some(c=>c.url.includes(ownerPath))).toBe(true));expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['foreign','missing','private','crossKind'] as const)('rejects malformed current human %s',async fault=>{
  const value=fixture(COMPANY_A)
  if(fault==='foreign')Object.assign(value.sourceVoucher.voucher,{companyId:COMPANY_B});if(fault==='missing')Reflect.deleteProperty(value,'sourceVoucher')
  if(fault==='private')Object.assign(value.sourceVoucher,{approvalReceipt:{}});if(fault==='crossKind')Object.assign(value,{inventoryCostCancellation:{}})
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(value):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['session','actor','company'] as const)('does not restore an old unknown into another %s boundary',async change=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK voucher original'});const priorActor=IDENTITY.userId
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST')return problem(503)
   const company=new Headers(init?.headers).get('X-Company-Id');if(company===null)throw new Error('Company required');return json(fixture(company))}})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  try{if(change==='company')await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
   else{cleanup();Object.assign(IDENTITY,change==='session'?{authenticationSessionBinding:'SENTETIK voucher replacement'}:{userId:'ac570000-0000-4000-8000-000000000099'});mount()}
   await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
  }finally{Object.assign(IDENTITY,{userId:priorActor})}
 })
 it('hides a retained attempt after purpose revocation without changing its original command',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK voucher revoked'});const capabilities=[cap]
  const calls=installFetch({capabilities,respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(503):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  cleanup();capabilities.splice(0);mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('does not turn a fresh POST403 into a receipt authority bypass',async()=>{
  const capabilities=[cap],calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(403)}return json(fixture(COMPANY_A))}})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
})
