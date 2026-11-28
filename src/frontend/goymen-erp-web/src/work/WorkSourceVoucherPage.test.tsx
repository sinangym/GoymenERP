import {afterEach,describe,expect,it,vi} from 'vitest'
import {act,cleanup,render,renderHook,screen,within} from '@testing-library/react'
import {WorkSourceVoucherFacts} from './WorkSourceVoucherFacts'
import {SOURCE_VOUCHER_WORK_KIND as kind,SOURCE_VOUCHER_WORK_TEXT as T,isSourceVoucherDecisionRequest} from './workSourceVoucherDecision'
import {sourceVoucherHuman,sourceVoucherReversalHuman,sourceVoucherId as id,sourceVoucherActor as actor} from './sourceVoucherDecisionFixture'
import {SOURCE_VOUCHER_TEXT as N} from '../accounting/sourceVoucherText'
import {COMPANY_A,COMPANY_B} from '../settings/testHarness'
import {settingsAttemptStoreKey,useCommandAttempt} from '../settings/useCommandAttempt'
import type {CommandOutcome} from '../settings/discountLimitApi'

afterEach(()=>{cleanup();vi.restoreAllMocks();sessionStorage.clear()})
// Staged Facts + existing journal protocol only: generic WorkPage/router/capability wiring is root-owned and not exercised here.
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
