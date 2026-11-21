import { act, cleanup, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { CommandOutcome } from './discountLimitApi'
import { settingsAttemptStoreKey, useCommandAttempt } from './useCommandAttempt'

afterEach(() => { cleanup(); vi.restoreAllMocks(); sessionStorage.clear() })

const applied: CommandOutcome<true> = { type: 'applied', data: true, replayed: true }
const unknown: CommandOutcome<true> = { type: 'unknown', reason: 'server' }
const binding = { sessionBinding: 'synthetic-session', actorId: 'synthetic-actor', companyId: 'synthetic-company' }
const storeKey = settingsAttemptStoreKey(binding, 'synthetic-operation')!

describe('settings unresolved command attempts', () => {
  it('refuses a changed payload or reset and replays the original closure with its original key', async () => {
    const original = vi.fn<(key: string) => Promise<CommandOutcome<true>>>()
      .mockResolvedValueOnce(unknown).mockResolvedValueOnce(applied)
    const replacement = vi.fn<(key: string) => Promise<CommandOutcome<true>>>().mockResolvedValue(applied)
    const { result } = renderHook(() => useCommandAttempt<true>())
    await act(async () => { await result.current.run({ unit: 'kg', scale: 3 }, original) })
    expect(result.current.hasPendingRetry).toBe(true)
    act(() => result.current.reset())
    await act(async () => { await result.current.run({ unit: 'piece', scale: 0 }, replacement) })
    expect(replacement).not.toHaveBeenCalled()
    expect(result.current.status.kind).toBe('unknown')
    await act(async () => { await result.current.run({ unit: 'kg', scale: 3 }, replacement) })
    expect(original).toHaveBeenCalledTimes(2)
    expect(original.mock.calls[1][0]).toBe(original.mock.calls[0][0])
    expect(replacement).not.toHaveBeenCalled()
    expect(result.current.status.kind).toBe('applied')
    expect(result.current.hasPendingRetry).toBe(false)
  })

  it.each([400, 401, 403, 404, 415])('does not settle an earlier unknown response on an HTTP %i refusal', async status => {
    const send = vi.fn<(key: string) => Promise<CommandOutcome<true>>>()
      .mockResolvedValueOnce(unknown)
      .mockResolvedValueOnce({ type: 'refused', status, code: null, companyId: null, groupCode: null, replayed: false })
      .mockResolvedValueOnce(applied)
    const { result } = renderHook(() => useCommandAttempt<true>())
    await act(async () => { await result.current.run({ value: 'A' }, send) })
    await act(async () => { await result.current.run({ value: 'A' }, send) })
    expect(result.current.hasPendingRetry).toBe(true)
    expect(result.current.status.kind).toBe('unknown')
    await act(async () => { await result.current.run({ value: 'A' }, send) })
    expect(new Set(send.mock.calls.map(([key]) => key)).size).toBe(1)
    expect(result.current.status.kind).toBe('applied')
  })

  it('allows only one in-flight execution and preserves a thrown lost-response attempt', async () => {
    let reject: (reason?: unknown) => void = () => { throw new Error('not started') }
    const send = vi.fn<(key: string) => Promise<CommandOutcome<true>>>()
      .mockImplementationOnce(() => new Promise((_, fail) => { reject = fail }))
      .mockResolvedValueOnce(applied)
    const { result } = renderHook(() => useCommandAttempt<true>())
    let first: Promise<CommandOutcome<true>>
    act(() => { first = result.current.run({ value: 'A' }, send) })
    await act(async () => { await result.current.run({ value: 'A' }, send) })
    expect(send).toHaveBeenCalledTimes(1)
    await act(async () => { reject(new Error('lost response')); await first })
    expect(result.current.status.kind).toBe('unknown')
    await act(async () => { await result.current.run({ value: 'A' }, send) })
    expect(send.mock.calls[1][0]).toBe(send.mock.calls[0][0])
  })

  it('restores the exact body and key after a remount, independently of a changed live version', async () => {
    const original = vi.fn<(key: string) => Promise<CommandOutcome<true>>>().mockResolvedValue(unknown)
    const restored = vi.fn<(payload: unknown, key: string) => Promise<CommandOutcome<true>>>().mockResolvedValue(applied)
    const first = renderHook(() => useCommandAttempt<true>({ storeKey, execute: restored }))
    const body = { expectedVersion: '1', reason: 'SENTETIK' }
    await act(async () => { await first.result.current.run(body, original) })
    const key = original.mock.calls[0][0]
    first.unmount()
    const next = renderHook(() => useCommandAttempt<true>({ storeKey, execute: restored }))
    expect(next.result.current.pendingPayload).toEqual(body)
    await act(async () => { await next.result.current.run({ ...body, expectedVersion: '2' }, original) })
    expect(restored).not.toHaveBeenCalled()
    await act(async () => { await next.result.current.retry() })
    expect(restored).toHaveBeenCalledExactlyOnceWith(body, key)
    expect(sessionStorage.getItem(storeKey)).toBeNull()
  })

  it('separates sessions, actors, companies and operations and refuses to rebind a mounted attempt', async () => {
    const send = vi.fn<(payload: unknown, key: string) => Promise<CommandOutcome<true>>>().mockResolvedValue(unknown)
    const original = renderHook(({ slot }) => useCommandAttempt<true>({ storeKey: slot, execute: send }),
      { initialProps: { slot: storeKey } })
    await act(async () => { await original.result.current.run({ value: 'A' }, key => send({ value: 'A' }, key)) })
    const otherSlots = [
      settingsAttemptStoreKey({ ...binding, sessionBinding: 'other' }, 'synthetic-operation'),
      settingsAttemptStoreKey({ ...binding, actorId: 'other' }, 'synthetic-operation'),
      settingsAttemptStoreKey({ ...binding, companyId: 'other' }, 'synthetic-operation'),
      settingsAttemptStoreKey(binding, 'other-operation'),
    ]
    for (const slot of otherSlots) {
      const other = renderHook(() => useCommandAttempt<true>({ storeKey: slot, execute: send }))
      expect(other.result.current.hasPendingRetry).toBe(false)
      other.unmount()
    }
    original.rerender({ slot: otherSlots[0]! })
    await act(async () => { await original.result.current.retry() })
    expect(send).toHaveBeenCalledTimes(1)
    expect(sessionStorage.getItem(storeKey)).not.toBeNull()
    expect(sessionStorage.getItem(otherSlots[0]!)).toBeNull()
  })

  it('keeps the newer retry journal when an unmounted original request completes late', async () => {
    let complete: (outcome: CommandOutcome<true>) => void = () => { throw new Error('not started') }
    const originalSend = vi.fn<(key: string) => Promise<CommandOutcome<true>>>()
      .mockImplementation(() => new Promise(resolve => { complete = resolve }))
    const retrySend = vi.fn<(payload: unknown, key: string) => Promise<CommandOutcome<true>>>()
      .mockResolvedValueOnce(unknown).mockResolvedValueOnce(applied)
    const first = renderHook(() => useCommandAttempt<true>({ storeKey, execute: retrySend }))
    let sending: Promise<CommandOutcome<true>>
    act(() => { sending = first.result.current.run({ value: 'A' }, originalSend) })
    first.unmount()
    const next = renderHook(() => useCommandAttempt<true>({ storeKey, execute: retrySend }))
    await act(async () => { await next.result.current.retry() })
    const current = sessionStorage.getItem(storeKey)
    await act(async () => { complete(applied); expect((await sending).type).toBe('unknown') })
    expect(sessionStorage.getItem(storeKey)).toBe(current)
    expect(next.result.current.hasPendingRetry).toBe(true)
    await act(async () => { await next.result.current.retry() })
    expect(retrySend.mock.calls[1][1]).toBe(originalSend.mock.calls[0][0])
    expect(sessionStorage.getItem(storeKey)).toBeNull()
  })

  it('does not overwrite an unreadable journal or send when storage rejects a reservation', async () => {
    const send = vi.fn<(key: string) => Promise<CommandOutcome<true>>>().mockResolvedValue(applied)
    sessionStorage.setItem(storeKey, '{broken')
    const broken = renderHook(() => useCommandAttempt<true>({ storeKey, execute: (_, key) => send(key) }))
    await act(async () => { await broken.result.current.run({ value: 'A' }, send) })
    expect(send).not.toHaveBeenCalled()
    expect(sessionStorage.getItem(storeKey)).toBe('{broken')
    broken.unmount()
    sessionStorage.clear()
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('storage unavailable') })
    const denied = renderHook(() => useCommandAttempt<true>({ storeKey, execute: (_, key) => send(key) }))
    await act(async () => { await denied.result.current.run({ value: 'A' }, send) })
    expect(send).not.toHaveBeenCalled()
    expect(denied.result.current.status.kind).toBe('unknown')
  })
})
