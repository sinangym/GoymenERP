import { describe, expect, it } from 'vitest'
import { createRequestFence } from './requestFence'

describe('purchasing continuation fence', () => {
  it('rejects a delayed page after the filter generation changes', () => {
    const fence = createRequestFence()
    const delayedPage = fence.capture()
    fence.invalidate()
    expect(delayedPage.signal.aborted).toBe(true)
    expect(fence.isCurrent(delayedPage)).toBe(false)
    expect(fence.isCurrent(fence.capture())).toBe(true)
  })
})
