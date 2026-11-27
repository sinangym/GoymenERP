/**
 * A re-send's answer resolves the first send ONLY when it is that send's own stored
 * result — `Idempotency-Replayed: true`. Everything else keeps the attempt unresolved.
 */

import { describe, expect, it } from 'vitest'
import { beginAttempt, retryAttempt, settleAttempt, type AttemptState } from './commandAttempt'
import type { CommandOutcome } from './commandApi'
import type { ShellState } from '../shell/shellState'
import type { CounterpartyMutationResult } from './mutationContract'
import { atomicCreateBindingForShell, rememberAtomicCreateAttempt, recallAtomicCreateAttempt, resetPendingTaxIdentityAttempts } from './pendingTaxIdentityAttempt'

const snapshot = {
  key: 'k'.repeat(32),
  operation: 'counterparty.account-status:p',
  binding: 'b',
  payload: { expectedVersion: '7', isActive: false, reason: 'r' },
}

function resent(): AttemptState<typeof snapshot.payload, null> {
  const unknown = settleAttempt(beginAttempt<typeof snapshot.payload, null>(snapshot), { type: 'http', status: 500, code: null, body: null })
  expect(unknown.phase).toBe('unknown')
  return retryAttempt(unknown)
}

const http = (status: number, code: string | null, replayed?: boolean): CommandOutcome<null> => ({
  type: 'http',
  status,
  code: code as CommandOutcome<null> extends { code: infer C } ? C : never,
  body: null,
  ...(replayed === undefined ? {} : { replayed }),
})

describe('settling a re-send of an unresolved attempt', () => {
  it('a REPLAYED version_conflict is the first send’s verdict: definite, terminal', () => {
    const settled = settleAttempt(resent(), http(409, 'version_conflict', true))
    expect(settled.phase).toBe('failed')
    if (settled.phase === 'failed') expect(settled.failure).toBe('version-conflict')
  })

  it('a non-replayed conflict of the re-send says nothing about the first send: still unknown', () => {
    const settled = settleAttempt(resent(), http(409, 'version_conflict', false))
    expect(settled.phase).toBe('unknown')
  })

  it('a compatibility 409, a 500 and an unacknowledged answer leave it unresolved', () => {
    expect(settleAttempt(resent(), http(409, 'client_contract_required')).phase).toBe('unknown')
    expect(settleAttempt(resent(), http(500, null)).phase).toBe('unknown')
    expect(settleAttempt(resent(), { type: 'unacknowledged', status: 204 }).phase).toBe('unknown')
  })

  it('a replayed 204 completes it, marked as a replay', () => {
    const settled = settleAttempt(resent(), {
      type: 'applied',
      data: null,
      replayed: true,
      originalCorrelationId: null,
    })
    expect(settled.phase).toBe('completed')
    if (settled.phase === 'completed') expect(settled.replayed).toBe(true)
  })

  it('a first send’s definite refusal is definite with or without the marker', () => {
    const first = beginAttempt<typeof snapshot.payload, null>(snapshot)
    expect(settleAttempt(first, http(409, 'version_conflict')).phase).toBe('failed')
    expect(settleAttempt(first, http(409, 'version_conflict', true)).phase).toBe('failed')
  })
})

it('keeps an atomic create that may have committed after 403 frozen under its exact original tax body and key',()=>{
 const atomic={...snapshot,operation:'counterparty.create',payload:{kind:1,legalName:'SENTETIK',tradeName:null,roles:0,taxIdentityNumber:'1234567890'}}
 const uncertain=settleAttempt(beginAttempt<typeof atomic.payload,null>(atomic),{type:'http',status:403,code:null,body:null,mayHaveCommitted:true})
 expect(uncertain.phase).toBe('unknown');expect(uncertain.phase!=='idle'&&uncertain.snapshot).toEqual(atomic)
 const retried=retryAttempt(uncertain);expect(retried.phase).toBe('submitting');expect(retried.phase!=='idle'&&retried.snapshot).toEqual(atomic)
})
it.each([undefined,false])('does not widen the normal legacy 403 classification when mayHaveCommitted=%s',mayHaveCommitted=>{
 const refused=settleAttempt(beginAttempt<typeof snapshot.payload,null>(snapshot),{type:'http',status:403,code:null,body:null,mayHaveCommitted})
 expect(refused.phase).toBe('failed');expect(refused.phase==='failed'&&refused.failure).toBe('forbidden')
})



it('atomic create survives a generation remount only inside the same ephemeral owner scope',()=>{
  resetPendingTaxIdentityAttempts()
  const shell=(actor='actor',company='company',epoch=1,generation='old')=>({activeContext:{userId:actor,companyId:company,capabilityGeneration:generation},sessionEpoch:epoch}) as unknown as ShellState
  const original=atomicCreateBindingForShell(shell(),'https://erp.invalid')
  const payload={kind:1 as const,legalName:'Native cari',tradeName:null,roles:1,taxIdentityNumber:'1234567890'}
  const frozen={key:'atomic-original-key',operation:'counterparty.create',binding:'old-generation',payload}
  const initial=beginAttempt<typeof payload,CounterpartyMutationResult>(frozen)
  // Remember before sending: even unmount before the 403 continuation cannot erase this key.
  rememberAtomicCreateAttempt(original,initial)
  const restored=recallAtomicCreateAttempt(atomicCreateBindingForShell(shell('actor','company',1,'new'),'https://erp.invalid'),'fresh-generation')
  expect(restored.phase).toBe('unknown')
  if(restored.phase!=='unknown')throw new Error('Unknown attempt required')
  expect(restored.snapshot).toEqual({...frozen,binding:'fresh-generation'})
  expect(restored.snapshot.payload).toBe(payload)
  for(const owner of [atomicCreateBindingForShell(shell('other'),'https://erp.invalid'),atomicCreateBindingForShell(shell('actor','other'),'https://erp.invalid'),atomicCreateBindingForShell(shell('actor','company',2),'https://erp.invalid'),atomicCreateBindingForShell(shell(),'https://other.invalid')]) {
    expect(recallAtomicCreateAttempt(owner,'fresh-generation')).toEqual({phase:'idle'})
  }
  resetPendingTaxIdentityAttempts()
  expect(recallAtomicCreateAttempt(original,'fresh-generation')).toEqual({phase:'idle'})
})
