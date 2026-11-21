import { afterEach, describe, expect, it } from 'vitest'
import { initialShellState, type ShellState } from '../../shell/shellState'
import { beginPaintAttempt, finishPaintAttempt, leavePaintAttempt, paintAttempt, paintAttemptFrozen,
  paintBinding, resetPaintAttemptsForTests } from './paintAttempt'
import type { PaintCommand, PaintMutation } from './contract'

const COMPANY = '11111111-1111-1111-1111-111111111111'
const PRODUCT = '22222222-2222-2222-2222-222222222222'
const command: PaintCommand = { kind: 'set', productId: PRODUCT, payload: { expectedVersion: '1', expectedProductVersion: '7', expectedMembershipVersion: '1', isMember: false, reason: 'SENTETIK' } }
const result: PaintMutation = { companyId: COMPANY, version: '2', changedEntries: [], removedProductIds: [PRODUCT], isReplay: false, originalCorrelationId: 'synthetic-original' }
function shell(): ShellState {
  return { ...initialShellState, phase: 'idle', sessionEpoch: 1, activeContext: { userId: 'synthetic-actor', scopeMode: 'single', companyId: COMPANY, capabilityGeneration: 'g1', capabilities: [] } }
}
afterEach(resetPaintAttemptsForTests)
describe('paint membership retained command identity', () => {
  it('separates actor, session and company without rotating identity for a route revisit', () => {
    const original = shell()
    const binding = paintBinding(original)
    expect(paintBinding({ ...original, routeEntryGeneration: 9, committedContextRevision: 9 })).toBe(binding)
    expect(paintBinding({ ...original, sessionEpoch: 2 })).not.toBe(binding)
    expect(paintBinding({ ...original, activeContext: { ...original.activeContext!, userId: 'other-actor' } })).not.toBe(binding)
    expect(paintBinding({ ...original, activeContext: { ...original.activeContext!, companyId: PRODUCT } })).not.toBe(binding)
    expect(paintBinding({ ...original, sessionInvalid: true })).toBeNull()
  })
  it('refuses commands without a company context and a second simultaneous submit', () => {
    expect(beginPaintAttempt(null, COMPANY, command)).toBeNull()
    expect(beginPaintAttempt('binding', null, command)).toBeNull()
    expect(beginPaintAttempt('binding', COMPANY, command, () => 'key1')?.key).toBe('key1')
    expect(beginPaintAttempt('binding', COMPANY, command, () => 'key2')).toBeNull()
  })
  it('keeps the same frozen key and payload after an unknown outcome', () => {
    const first = beginPaintAttempt('binding', COMPANY, command, () => 'key1')!
    finishPaintAttempt(first, { type: 'unknown', reason: 'network-error' })
    expect(beginPaintAttempt('binding', COMPANY, { ...command, payload: { ...command.payload, reason: 'different' } }, () => 'key2')).toBeNull()
    const retry = beginPaintAttempt('binding', COMPANY, command, () => 'key2')!
    expect(retry).toBe(first)
    expect(Object.isFrozen(retry.command.payload)).toBe(true)
    expect(paintAttemptFrozen(paintAttempt('binding'))).toBe(true)
  })
  it('retains an in-flight snapshot across departure and allows an identical replay', () => {
    const sent = beginPaintAttempt('binding', COMPANY, command, () => 'key1')!
    leavePaintAttempt('binding')
    expect(paintAttempt('binding').phase).toBe('unknown')
    expect(beginPaintAttempt('binding', COMPANY, command, () => 'key2')).toBe(sent)
  })
  it('does not apply the old company result to another company attempt', () => {
    const a = beginPaintAttempt('company-a', COMPANY, command, () => 'a')!
    beginPaintAttempt('company-b', PRODUCT, command, () => 'b')
    finishPaintAttempt(a, { type: 'applied', data: result })
    expect(paintAttempt('company-a').phase).toBe('completed')
    expect(paintAttempt('company-b')).toMatchObject({ phase: 'sending', snapshot: { key: 'b' } })
  })
  it('does not settle a previous unknown with a later permission refusal', () => {
    const first = beginPaintAttempt('binding', COMPANY, command, () => 'key1')!
    finishPaintAttempt(first, { type: 'unknown', reason: 'service-unavailable' })
    beginPaintAttempt('binding', COMPANY, command, () => 'key2')
    finishPaintAttempt(first, { type: 'refused', status: 403, body: null })
    expect(paintAttempt('binding')).toMatchObject({ phase: 'failed', unresolved: true })
    expect(paintAttemptFrozen(paintAttempt('binding'))).toBe(true)
  })
  it('settles a proven stored refusal replay and permits an explicit corrected command', () => {
    const first = beginPaintAttempt('binding', COMPANY, command, () => 'key1')!
    finishPaintAttempt(first, { type: 'unknown', reason: 'network-error' })
    beginPaintAttempt('binding', COMPANY, command, () => 'key2')
    finishPaintAttempt(first, { type: 'refused', status: 409, body: null, terminalReplay: true })
    expect(paintAttemptFrozen(paintAttempt('binding'))).toBe(false)
    expect(beginPaintAttempt('binding', COMPANY, { ...command, payload: { ...command.payload, expectedVersion: '2' } }, () => 'key3')?.key).toBe('key3')
  })
  it('never lets a late unknown overwrite a proven success for the same key', () => {
    const first = beginPaintAttempt('binding', COMPANY, command, () => 'key1')!
    leavePaintAttempt('binding')
    beginPaintAttempt('binding', COMPANY, command, () => 'key2')
    finishPaintAttempt(first, { type: 'applied', data: result })
    finishPaintAttempt(first, { type: 'unknown', reason: 'network-error' })
    expect(paintAttempt('binding')).toMatchObject({ phase: 'completed', result })
  })
  it('does not overwrite a new command with a late old-key response', () => {
    const first = beginPaintAttempt('binding', COMPANY, command, () => 'key1')!
    finishPaintAttempt(first, { type: 'refused', status: 409, body: null })
    beginPaintAttempt('binding', COMPANY, { ...command, payload: { ...command.payload, expectedVersion: '2' } }, () => 'key2')
    finishPaintAttempt(first, { type: 'applied', data: result })
    expect(paintAttempt('binding')).toMatchObject({ phase: 'sending', snapshot: { key: 'key2' } })
  })
})
