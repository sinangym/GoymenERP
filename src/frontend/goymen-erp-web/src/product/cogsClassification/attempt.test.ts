import { afterEach, describe, expect, it } from 'vitest'
import { initialShellState, type ShellState } from '../../shell/shellState'
import { beginCogsAttempt, cogsAttempt, cogsAttemptFrozen, cogsBinding, finishCogsAttempt, leaveCogsAttempt,
  resetCogsAttemptsForTests, subscribeCogsAttempt } from './attempt'
import { COGS_SOURCE, type CogsMutation, type SetCogsRequest } from './contract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const OTHER = '33333333-3333-3333-3333-333333333333'
const request: SetCogsRequest = { expectedProductVersion: '7', expectedClassificationVersion: '0', classification: 1, reason: 'SENTETIK' }
const result: CogsMutation = { classification: { companyId: COMPANY, productId: PRODUCT, productVersion: '7', classificationVersion: '1',
  classification: 1, changedInCompanyId: COMPANY, updatedAtUtc: '2026-11-17T10:00:00Z', sourceVersion: COGS_SOURCE },
  isReplay: false, originalCorrelationId: 'synthetic-original' }
function shell(): ShellState {
  return { ...initialShellState, sessionEpoch: 1, phase: 'idle', activeContext: { userId: 'synthetic-actor', scopeMode: 'single',
    companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [] } }
}
afterEach(resetCogsAttemptsForTests)

describe('COGS retained command identity', () => {
  it('retains identity over route and authority refresh while separating actor, company, session and product', () => {
    const state = shell(); const binding = cogsBinding(state, PRODUCT)
    expect(binding).toContain('/api/v1')
    expect(cogsBinding({ ...state, routeEntryGeneration: 8, requestFenceGeneration: 8, committedContextRevision: 9 }, PRODUCT)).toBe(binding)
    expect(cogsBinding(state, PRODUCT.toUpperCase())).toBe(binding)
    expect(cogsBinding(state, OTHER)).not.toBe(binding)
    expect(cogsBinding({ ...state, sessionEpoch: 2 }, PRODUCT)).not.toBe(binding)
    expect(cogsBinding({ ...state, activeContext: { ...state.activeContext!, userId: 'synthetic-other' } }, PRODUCT)).not.toBe(binding)
    expect(cogsBinding({ ...state, activeContext: { ...state.activeContext!, companyId: OTHER } }, PRODUCT)).not.toBe(binding)
    expect(cogsBinding({ ...state, sessionInvalid: true }, PRODUCT)).toBeNull()
    expect(cogsBinding({ ...state, activeContext: null }, PRODUCT)).toBeNull()
  })
  it('never starts without a binding or starts two concurrent sends', () => {
    expect(beginCogsAttempt(null, COMPANY, PRODUCT, request)).toBeNull()
    expect(beginCogsAttempt('binding', COMPANY, PRODUCT, request, () => 'key1')?.key).toBe('key1')
    expect(beginCogsAttempt('binding', COMPANY, PRODUCT, request, () => 'key2')).toBeNull()
  })
  it('holds a defensive immutable request copy and reuses exactly its snapshot after unknown', () => {
    const mutable = { ...request }
    const first = beginCogsAttempt('binding', COMPANY, PRODUCT, mutable, () => 'key1')!
    mutable.reason = 'SENTETIK changed'
    expect(first.request.reason).toBe('SENTETIK')
    expect(Object.isFrozen(first)).toBe(true)
    expect(Object.isFrozen(first.request)).toBe(true)
    finishCogsAttempt(first, { type: 'unknown', reason: 'network-error' })
    expect(beginCogsAttempt('binding', COMPANY, PRODUCT, mutable, () => 'key2')).toBeNull()
    expect(beginCogsAttempt('binding', OTHER, PRODUCT, request, () => 'key2')).toBeNull()
    expect(beginCogsAttempt('binding', COMPANY, OTHER, request, () => 'key2')).toBeNull()
    expect(beginCogsAttempt('binding', COMPANY, PRODUCT, request, () => 'key2')).toBe(first)
  })
  it('turns departure into an unresolved outcome that survives remount with the same key', () => {
    const first = beginCogsAttempt('binding', COMPANY, PRODUCT, request, () => 'key1')!
    leaveCogsAttempt('binding')
    expect(cogsAttempt('binding')).toMatchObject({ phase: 'unknown', snapshot: first })
    expect(cogsAttemptFrozen(cogsAttempt('binding'))).toBe(true)
    expect(beginCogsAttempt('binding', COMPANY, PRODUCT, request, () => 'key2')).toBe(first)
  })
  it.each([401, 403, 404, 409, 422])('does not settle a prior unknown from a later unproven refusal %i', (status) => {
    const first = beginCogsAttempt('binding', COMPANY, PRODUCT, request, () => 'key1')!
    finishCogsAttempt(first, { type: 'unknown', reason: 'service-unavailable' })
    beginCogsAttempt('binding', COMPANY, PRODUCT, request, () => 'key2')
    finishCogsAttempt(first, { type: 'refused', status, terminalReplay: false })
    expect(cogsAttempt('binding')).toMatchObject({ phase: 'failed', unresolved: true })
    expect(cogsAttemptFrozen(cogsAttempt('binding'))).toBe(true)
  })
  it('settles a proven refusal replay and permits an explicit corrected command with a new key', () => {
    const first = beginCogsAttempt('binding', COMPANY, PRODUCT, request, () => 'key1')!
    leaveCogsAttempt('binding')
    beginCogsAttempt('binding', COMPANY, PRODUCT, request, () => 'key2')
    finishCogsAttempt(first, { type: 'refused', status: 409, terminalReplay: true })
    expect(cogsAttemptFrozen(cogsAttempt('binding'))).toBe(false)
    expect(beginCogsAttempt('binding', COMPANY, PRODUCT, { ...request, expectedClassificationVersion: '1' }, () => 'key3')?.key).toBe('key3')
    finishCogsAttempt(first, { type: 'applied', data: result })
    expect(cogsAttempt('binding')).toMatchObject({ phase: 'sending', snapshot: { key: 'key3' } })
  })
  it('does not overwrite a proved success or another company result with late uncertainty', () => {
    const first = beginCogsAttempt('company-a', COMPANY, PRODUCT, request, () => 'key1')!
    beginCogsAttempt('company-b', OTHER, PRODUCT, request, () => 'key2')
    finishCogsAttempt(first, { type: 'applied', data: result })
    finishCogsAttempt(first, { type: 'unknown', reason: 'offline' })
    expect(cogsAttempt('company-a')).toMatchObject({ phase: 'completed', result })
    expect(cogsAttempt('company-b')).toMatchObject({ phase: 'sending', snapshot: { key: 'key2' } })
  })
  it('notifies only its own subscribers and supports safe unsubscribe', () => {
    let a = 0; let b = 0
    const leave = subscribeCogsAttempt('a', () => { a++ })
    subscribeCogsAttempt('b', () => { b++ })
    beginCogsAttempt('a', COMPANY, PRODUCT, request, () => 'key1')
    expect([a, b]).toEqual([1, 0])
    leave(); leaveCogsAttempt('a')
    expect([a, b]).toEqual([1, 0])
  })
})
