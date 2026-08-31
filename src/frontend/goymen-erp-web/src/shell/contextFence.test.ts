import { describe, expect, it } from 'vitest'
import { createRequestFence } from './contextFence'

describe('request-generation fence', () => {
  it('a response captured before a bump is stale', () => {
    const fence = createRequestFence()
    const captured = fence.capture()
    fence.bump()
    expect(fence.isStale(captured)).toBe(true)
  })

  it('a response captured after the last bump is fresh', () => {
    const fence = createRequestFence()
    fence.bump()
    const captured = fence.capture()
    expect(fence.isStale(captured)).toBe(false)
  })

  it('stays stale across multiple bumps (failed switch then restore)', () => {
    const fence = createRequestFence()
    const captured = fence.capture()
    fence.bump() // switch start
    fence.bump() // switch failed, restore former context
    expect(fence.isStale(captured)).toBe(true)
  })

  it('out-of-order: an old-company response arriving late is rejected', () => {
    const fence = createRequestFence()
    const oldRequest = fence.capture() // request issued against company A
    fence.bump() // user switches to B
    const newRequest = fence.capture() // request issued against company B
    // B response settles first, then the slow A response arrives:
    expect(fence.isStale(newRequest)).toBe(false)
    expect(fence.isStale(oldRequest)).toBe(true)
  })
})
