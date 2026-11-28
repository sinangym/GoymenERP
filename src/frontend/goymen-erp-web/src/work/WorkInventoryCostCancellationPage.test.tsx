import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { inventoryCostCancellationFixture as fixture, cancellationWorkId as id } from './inventoryCostCancellationDecisionFixture'
import { WorkInventoryCostCancellationFacts } from './WorkInventoryCostCancellationFacts'
import { INVENTORY_COST_CANCELLATION_WORK_KIND as kind, INVENTORY_COST_CANCELLATION_WORK_CAPABILITY as cap, INVENTORY_COST_CANCELLATION_WORK_TEXT as T } from './workInventoryCostCancellationDecision'
import { WORK_DECISION_TEXT as W } from './workDecisionText'

import userEvent from '@testing-library/user-event'
import { QueryClient } from '@tanstack/react-query'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { AppShell } from '../shell/AppShell'
import { ShellProvider } from '../shell/ShellProvider'
import { COMPANY_A, COMPANY_B, IDENTITY, installFetch, json, problem } from '../settings/testHarness'
import { workRoutes } from './workRoutes'

const company='ac560000-0000-4000-8000-000000000001'
afterEach(()=>{cleanup();vi.unstubAllGlobals();vi.restoreAllMocks();sessionStorage.clear();Object.assign(IDENTITY,{authenticationSessionBinding:null})})
// Pure human rendering and the actual generated Work route with synthetic transport. Not browser/SQL/HTTP acceptance.
describe('inventory cost cancellation human facts',()=>{
 it('shows the full retained original, prepared/current components, independent versions and complete legal cancellation reason read-only',()=>{
  const v=fixture(company,'Prepared','Prepared','9007199254740993','7'),h=v.inventoryCostCancellation
  render(<WorkInventoryCostCancellationFacts human={h}/>);const region=screen.getByRole('region',{name:T.title})
  const original=within(region).getByRole('region',{name:T.original}),prepared=within(region).getByRole('region',{name:T.prepared}),current=within(region).getByRole('region',{name:T.current}),source=within(region).getByRole('region',{name:T.source})
  for(const raw of ['SENTETIK INV-55','SENTETIK maliyet hazırlama gerekçesi','SENTETIK maliyet karşı hesabı','SENTETIK kayıtlı plan','1 / 3','9007199254740993','9223372036854775807'])expect(original.textContent).toContain(raw)
  for(const raw of ['SENTETIK kayıtlı maliyet paylarının iptal gerekçesi','SENTETIK üretim payı','SENTETIK sıfır alış payı','100.01','-0.01','9007199254740993','9223372036854775807']){expect(prepared.textContent).toContain(raw);expect(current.textContent).toContain(raw)}
  expect(prepared.textContent).toContain(W.inventoryCostFields.preparedRecordedAtUtc)
  expect(source.textContent).toContain(h.sourceCancellation.reason)
  for(const [key,value] of Object.entries(h.sourceCancellation)){expect(source.textContent).toContain(T.sourceFields[key as keyof typeof T.sourceFields]);if(key!=='documentKind')expect(source.textContent).toContain(value)}
  expect(source.textContent).toContain(T.salesInvoice)
  expect(within(region).queryByRole('button')).toBeNull();expect(within(region).queryByRole('textbox')).toBeNull();expect(within(region).queryByRole('combobox')).toBeNull()
  for(const raw of ['sourceProofJson','physicalProofJson','sourceFactsDigest','previewDigest','fingerprint','economicKey','approvalReceipt'])expect(region.textContent).not.toContain(raw)
 })
 it.each(['AwaitingPreparation','NoCost','AlreadyAccounted'] as const)('does not fabricate a preparation for original N1 %s',status=>{
  const h=fixture(company,'Prepared',status).inventoryCostCancellation;render(<WorkInventoryCostCancellationFacts human={h}/>)
  const original=screen.getByRole('region',{name:T.original});expect(within(original).queryByText(W.inventoryCostFields.preparation)).toBeNull()
  expect(original.textContent).toContain(h.originalProposal.proposalId);expect(original.textContent).toContain(h.originalProposal.version)
  expect(screen.getByRole('region',{name:T.prepared}).textContent).toContain(h.preparedCancellation.reason)
 })
 it('retains every component row and money profile without a 12-row truncation',()=>{
  const h=fixture(company).inventoryCostCancellation,base=h.preparedCancellation.components[0]!
  const components=Array.from({length:13},(_,index)=>({...base,postingPurpose:`SENTETIK pay ${index+1}`}))
  Object.assign(h.preparedCancellation,{components});Object.assign(h.cancellation,{components:structuredClone(components)})
  render(<WorkInventoryCostCancellationFacts human={h}/>);
  for(const label of [T.prepared,T.current]){const region=screen.getByRole('region',{name:label});for(const c of components)expect(region.textContent).toContain(c.postingPurpose)}
 })
 it('shows the genuine approved M+1 result, unchanged original N and zero acknowledgement without an invented journal',()=>{
  const h=fixture(company,'Approved','Prepared','9223372036854775806','9007199254740993').inventoryCostCancellation
  render(<WorkInventoryCostCancellationFacts human={h}/>)
  expect(screen.getByRole('region',{name:T.original}).textContent).toContain('9007199254740993')
  expect(screen.getByRole('region',{name:T.prepared}).textContent).toContain('9223372036854775806')
  const result=screen.getByRole('region',{name:T.current});expect(result.textContent).toContain('9223372036854775807');expect(result.textContent).toContain('Onaylandı')
  expect(h.cancellation.components[2]?.reversingJournalId).toBeUndefined();expect(within(result).queryByRole('button')).toBeNull()
 })
 it('keeps an explicit presentation allowlist even if a caller bypasses transport validation',()=>{
  const h=fixture(company).inventoryCostCancellation
  Object.assign(h.originalProposal.component,{sourceProofJson:'SENTETIK PRIVATE ORIGINAL',fingerprint:'SENTETIK PRIVATE HASH'})
  Object.assign(h.preparedCancellation,{approvalReceipt:'SENTETIK PRIVATE RECEIPT'})
  Object.assign(h.sourceCancellation,{requestHash:'SENTETIK PRIVATE SOURCE',correlationId:'SENTETIK PRIVATE CORRELATION'})
  render(<WorkInventoryCostCancellationFacts human={h}/>);expect(document.body.textContent).not.toContain('SENTETIK PRIVATE')
 })
})

const ownerPath=`/work/decisions/${kind}/${id}`
function mount(client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})) {
 const router=createMemoryRouter([{path:'/',element:<AppShell/>,children:workRoutes}],{initialEntries:[`/onaylarim/${kind}/${id}`]})
 render(<ShellProvider queryClient={client}><RouterProvider router={router}/></ShellProvider>);return {router,client}
}
const applied=(replayed=false)=>json({companyId:COMPANY_A,kind,itemId:id,state:'CancelledByComponentReversal',replayed,originalCorrelationId:'SENTETIK original cancellation'},200,
 replayed?{'Idempotency-Replayed':'true','Idempotency-Original-Correlation-Id':'SENTETIK original cancellation'}:undefined)
describe('published cancellation Work page protocol',()=>{
 it('keeps loading free of actions until the whole authorized human reply arrives',async()=>{
  let release!:(response:Response)=>void;const pending=new Promise<Response>(resolve=>{release=resolve})
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?pending:undefined});mount()
  await waitFor(()=>expect(calls.some(c=>c.url.includes(ownerPath))).toBe(true));expect(document.querySelector('[data-state="initial-loading"]')).not.toBeNull();expect(screen.queryByRole('button',{name:W.approve})).toBeNull()
  await act(async()=>release(json(fixture(COMPANY_A))));await screen.findByRole('region',{name:T.title});expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('keeps a native missing proposal distinct from a successful empty human response',async()=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?problem(404):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('retains a native conflict as refusal rather than a successful cancellation receipt',async()=>{
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(409):json(fixture(COMPANY_A)):undefined});mount()
  await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(`${W.refused} (409)`)
  expect(screen.queryByText(W.done)).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })

 it('uses the actual purpose route, shows both native versions and freezes exactly five command fields',async()=>{
  const value=fixture(COMPANY_A,'Prepared','Prepared','9007199254740993','9223372036854775807')
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?applied():json(value):undefined});mount()
  const region=await screen.findByRole('region',{name:T.title});expect(region.textContent).toContain('9007199254740993');expect(region.textContent).toContain('9223372036854775807')
  expect(within(region).queryByRole('button')).toBeNull();expect(screen.queryByRole('button',{name:W.reject})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.method==='POST').map(c=>c.body)).toEqual([{expectedVersion:'9007199254740993',approve:true,reason:null,delegationId:null,inventoryCostCancellation:{expectedProposalVersion:'9223372036854775807'}}])
  expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(1)
  expect(calls.some(c=>c.url.includes('/accounting/'))).toBe(false)
 })
 it.each(['Approved','not-pending','max'] as const)('keeps %s read-only without a new command or incrementing original N',async mode=>{
  const value=fixture(COMPANY_A,mode==='Approved'?'Approved':'Prepared','AwaitingPreparation',mode==='max'?'9223372036854775807':'7')
  if(mode==='not-pending')value.pending=false
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(value):undefined});mount();await screen.findByRole('region',{name:T.title})
  const approve=screen.queryByRole('button',{name:W.approve});if(mode==='max')expect((approve as HTMLButtonElement).disabled).toBe(true);else expect(approve).toBeNull()
  expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it('remounts an unknown using original M/N/body/key without fetching a later current preparation',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cancellation session'});let posts=0
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?++posts===1?problem(503):applied(true):json(fixture(COMPANY_A,'Prepared','AwaitingPreparation',posts?'99':'7')):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  const gets=calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET').length,journal=JSON.stringify(sessionStorage)
  for(const raw of ['100.01','SENTETIK yasal','SENTETIK kayıtlı maliyet','originalProposal','sourceCancellation','components','sourceFactsDigest','previewDigest'])expect(journal).not.toContain(raw)
  cleanup();mount();await screen.findByText(W.unknown);expect(screen.getByText(T.retained)).toBeTruthy();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.done)
  expect(calls.filter(c=>c.url.includes(ownerPath)&&c.method==='GET')).toHaveLength(gets)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key'])
 })
 it('suppresses cached and late current human facts while retrying the original unknown receipt',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK cancellation late'});let gets=0,release!:(value:Response)=>void
  const late=new Promise<Response>(resolve=>{release=resolve}),client=new QueryClient({defaultOptions:{queries:{retry:false,gcTime:0,staleTime:0}}})
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(503):++gets===1?json(fixture(COMPANY_A)):late:undefined})
  mount(client);await screen.findByRole('region',{name:T.title});let refetch!:Promise<void>
  act(()=>{refetch=client.refetchQueries({predicate:q=>q.queryKey.includes('work-decision')})});await waitFor(()=>expect(gets).toBe(2))
  fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  const current=fixture(COMPANY_A,'Prepared','AwaitingPreparation','99');Object.assign(current.inventoryCostCancellation.sourceCancellation,{reason:'SENTETIK LATER CURRENT CANCELLATION'})
  await act(async()=>{release(json(current));await refetch});expect(screen.queryByText('SENTETIK LATER CURRENT CANCELLATION')).toBeNull();expect(screen.queryByRole('region',{name:T.title})).toBeNull()
  fireEvent.click(screen.getByRole('button',{name:W.retry}));await screen.findByText(W.unknown)
  const sent=calls.filter(c=>c.method==='POST');expect(sent).toHaveLength(2);expect(sent[1]!.body).toEqual(sent[0]!.body);expect(sent[1]!.headers['Idempotency-Key']).toBe(sent[0]!.headers['Idempotency-Key']);client.clear()
 })
 it.each([{capabilities:[]},{capabilities:['accounting.invoiced-shipment-costs.reverse-approve']},{capabilities:['accounting.invoiced-shipment-cost.work-decision']},{capabilities:['accounting.journals.read']}])('does not fetch the owner without the exact purpose capability %j',async({capabilities})=>{
  const calls=installFetch({capabilities,respond:url=>url.includes(ownerPath)?json(fixture(COMPANY_A)):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull());expect(calls.some(c=>c.url.includes(ownerPath))).toBe(false)
 })
 it.each([401,403,503])('releases no human facts on native owner status %s',async status=>{
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?problem(status):undefined});mount()
  await waitFor(()=>expect(calls.some(c=>c.url.includes(ownerPath))).toBe(true));await waitFor(()=>expect(document.querySelector('[data-state]')).not.toBeNull())
  expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(screen.queryByRole('button',{name:W.approve})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['foreign','private','crossKind','missing'] as const)('fails closed on malformed human %s',async fault=>{
  const value=fixture(COMPANY_A)
  if(fault==='foreign')Object.assign(value.inventoryCostCancellation.originalProposal,{companyId:COMPANY_B})
  if(fault==='private')Object.assign(value.inventoryCostCancellation.preparedCancellation,{receipt:'SENTETIK private'})
  if(fault==='crossKind')Object.assign(value.inventoryCostCancellation.originalProposal,{kind:2})
  if(fault==='missing')Reflect.deleteProperty(value,'inventoryCostCancellation')
  const calls=installFetch({capabilities:[cap],respond:url=>url.includes(ownerPath)?json(value):undefined});mount()
  await waitFor(()=>expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull());expect(screen.queryByRole('region',{name:T.title})).toBeNull();expect(calls.some(c=>c.method==='POST')).toBe(false)
 })
 it.each(['session','company','actor'] as const)('does not expose an old unknown command after %s changes',async change=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK old cancellation'});const priorActor=IDENTITY.userId
  const calls=installFetch({capabilities:[cap],respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST')return problem(503)
   const active=new Headers(init?.headers).get('X-Company-Id');if(active===null)throw new Error('Company required');return json(fixture(active))}})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  try{if(change==='company')await userEvent.setup().selectOptions(screen.getByRole('combobox',{name:'Şirket'}),COMPANY_B)
   else{cleanup();Object.assign(IDENTITY,change==='session'?{authenticationSessionBinding:'SENTETIK new cancellation'}:{userId:'ac560000-0000-4000-8000-000000000099'});mount()}
   await screen.findByRole('region',{name:T.title});expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
  }finally{Object.assign(IDENTITY,{userId:priorActor})}
 })
 it('hides a retained retry after capability revocation and preserves the ordinary POST403 boundary',async()=>{
  Object.assign(IDENTITY,{authenticationSessionBinding:'SENTETIK revoked cancellation'});const capabilities=[cap]
  const calls=installFetch({capabilities,respond:(url,init)=>url.includes(ownerPath)?init?.method==='POST'?problem(503):json(fixture(COMPANY_A)):undefined})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await screen.findByText(W.unknown)
  cleanup();capabilities.splice(0);mount();await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
 it('does not turn a fresh POST403 into an old-key privilege bypass',async()=>{
  const capabilities=[cap],calls=installFetch({capabilities,respond:(url,init)=>{if(!url.includes(ownerPath))return undefined;if(init?.method==='POST'){capabilities.splice(0);return problem(403)}return json(fixture(COMPANY_A))}})
  mount();await screen.findByRole('region',{name:T.title});fireEvent.click(screen.getByRole('button',{name:W.approve}));await waitFor(()=>expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
  expect(screen.queryByRole('button',{name:W.retry})).toBeNull();expect(calls.filter(c=>c.method==='POST')).toHaveLength(1)
 })
})
