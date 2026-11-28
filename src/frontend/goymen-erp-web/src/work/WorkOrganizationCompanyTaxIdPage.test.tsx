import {afterEach,describe,expect,it,vi} from 'vitest'
import {act,cleanup,render,renderHook,screen,within} from '@testing-library/react'
import {WorkOrganizationCompanyTaxIdFacts} from './WorkOrganizationCompanyTaxIdFacts'
import {ORGANIZATION_COMPANY_TAX_ID_WORK_TEXT as T,ORGANIZATION_COMPANY_TAX_ID_WORK_KIND as kind,
 organizationCompanyTaxIdDecisionRequest,isOrganizationCompanyTaxIdDecisionRequest,type OrganizationCompanyTaxIdDecisionRequest} from './workOrganizationCompanyTaxIdDecision'
import {organizationCompanyTaxIdHuman as human,companyTaxIdRequestId as id,companyTaxIdActor as actor} from './organizationCompanyTaxIdDecisionFixture'
import {COMPANY_A,COMPANY_B} from '../settings/testHarness'
import {COMPANY_TAX_ID_TEXT as N} from '../organization/companyTaxIdText'
import {settingsAttemptStoreKey,useCommandAttempt} from '../settings/useCommandAttempt'
import type {CommandOutcome} from '../settings/discountLimitApi'

afterEach(()=>{cleanup();sessionStorage.clear()})
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
