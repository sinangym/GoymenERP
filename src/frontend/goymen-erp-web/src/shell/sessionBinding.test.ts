import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  SESSION_BINDING_STORAGE_KEY,
  establishSessionBinding,
  forgetSessionBindingMemory,
  retireSessionBinding,
} from './sessionBinding'

const ACTOR_A = '11111111-1111-1111-1111-111111111111'
const ACTOR_B = '22222222-2222-2222-2222-222222222222'
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** An F5: the document's memory is gone, the tab's sessionStorage is not. */
function reloadDocument() {
  forgetSessionBindingMemory()
}

beforeEach(() => {
  sessionStorage.clear()
  forgetSessionBindingMemory()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('shell session binding (M01.3 K2 #728/6180459736)', () => {
  it('survives an ordinary reload of the same authenticated actor', () => {
    const first = establishSessionBinding(ACTOR_A)
    expect(first).toMatch(UUID)
    expect(establishSessionBinding(ACTOR_A)).toBe(first)

    reloadDocument()
    expect(establishSessionBinding(ACTOR_A)).toBe(first)
  })

  it('stores an opaque id and the actor it belongs to — no token or secret', () => {
    const id = establishSessionBinding(ACTOR_A)
    const stored = JSON.parse(sessionStorage.getItem(SESSION_BINDING_STORAGE_KEY) ?? 'null') as Record<string, unknown>
    expect(Object.keys(stored).sort()).toEqual(['actorId', 'id', 'v'])
    expect(stored).toEqual({ v: 1, id, actorId: ACTOR_A })
  })

  it('rotates on an actor transition, also across a reload, and never returns to an earlier id', () => {
    const a1 = establishSessionBinding(ACTOR_A)
    reloadDocument()
    const b = establishSessionBinding(ACTOR_B)
    expect(b).toMatch(UUID)
    expect(b).not.toBe(a1)

    const a2 = establishSessionBinding(ACTOR_A)
    expect(a2).toMatch(UUID)
    expect(a2).not.toBe(a1)
    expect(a2).not.toBe(b)
  })

  it('rotates at a session boundary: the same actor re-entering gets a new id, before and after a reload', () => {
    const before = establishSessionBinding(ACTOR_A)
    retireSessionBinding()
    const reentered = establishSessionBinding(ACTOR_A)
    expect(reentered).toMatch(UUID)
    expect(reentered).not.toBe(before)

    retireSessionBinding()
    reloadDocument()
    const afterReload = establishSessionBinding(ACTOR_A)
    expect(afterReload).toMatch(UUID)
    expect(afterReload).not.toBe(before)
    expect(afterReload).not.toBe(reentered)
  })

  it('a retired id is never handed out again in this document, even when retiring could not write', () => {
    const before = establishSessionBinding(ACTOR_A)
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    const removeItem = vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('denied') })
    retireSessionBinding()
    // Storage still holds the retired id and cannot take a new one: no binding at all, not the old one.
    expect(establishSessionBinding(ACTOR_A)).toBeNull()
    setItem.mockRestore()
    removeItem.mockRestore()
    const next = establishSessionBinding(ACTOR_A)
    expect(next).toMatch(UUID)
    expect(next).not.toBe(before)
  })

  it.each([
    ['not JSON', '{not json'],
    ['the wrong version', JSON.stringify({ v: 2, id: '33333333-3333-4333-8333-333333333333', actorId: ACTOR_A })],
    ['an id that is not a UUID', JSON.stringify({ v: 1, id: 'epoch-0', actorId: ACTOR_A })],
    ['extra fields', JSON.stringify({ v: 1, id: '33333333-3333-4333-8333-333333333333', actorId: ACTOR_A, epoch: 0 })],
  ])('a stored record with %s is never adopted: a new id is issued', (_case, raw) => {
    sessionStorage.setItem(SESSION_BINDING_STORAGE_KEY, raw)
    const id = establishSessionBinding(ACTOR_A)
    expect(id).toMatch(UUID)
    expect(id).not.toBe('33333333-3333-4333-8333-333333333333')
  })

  it('an unreadable store gives no binding', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied') })
    expect(establishSessionBinding(ACTOR_A)).toBeNull()
  })

  it('a binding that cannot be stored durably is not issued', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota') })
    expect(establishSessionBinding(ACTOR_A)).toBeNull()
  })

  it('a binding whose read-back differs is not issued', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => undefined)
    expect(establishSessionBinding(ACTOR_A)).toBeNull()
  })
})
