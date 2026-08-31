import { describe, expect, it } from 'vitest'
import { blocksFeatureMount, carriesData, classifyOutcome, type ViewOutcome } from './uiState'

function classify(outcome: ViewOutcome<{ id: string }>) {
  return classifyOutcome(outcome)
}

describe('classifyOutcome — ERP-000K §12 distinct states', () => {
  it('401 / 403 / 404 / 409 / 5xx are all different kinds', () => {
    expect(classify({ type: 'http', status: 401 }).kind).toBe('unauthenticated')
    expect(classify({ type: 'http', status: 403 }).kind).toBe('forbidden')
    expect(classify({ type: 'http', status: 404 }).kind).toBe('not-found')
    expect(classify({ type: 'http', status: 409 }).kind).toBe('conflict')
    expect(classify({ type: 'http', status: 500 }).kind).toBe('service-unavailable')
    expect(classify({ type: 'http', status: 503 }).kind).toBe('service-unavailable')
  })

  it('empty-no-data and empty-filtered are distinct and neither is forbidden', () => {
    const noData = classify({ type: 'ok', data: [], filterActive: false })
    const filtered = classify({ type: 'ok', data: [], filterActive: true })
    expect(noData.kind).toBe('empty-no-data')
    expect(filtered.kind).toBe('empty-filtered')
    expect(noData.kind).not.toBe('forbidden')
  })

  it('an unexpected non-2xx fails closed, not as generic empty', () => {
    expect(classify({ type: 'http', status: 418 }).kind).toBe('malformed-response')
  })

  it('offline and malformed are their own states', () => {
    expect(classify({ type: 'offline' }).kind).toBe('offline')
    expect(classify({ type: 'malformed' }).kind).toBe('malformed-response')
  })

  it('no-authorized-company is distinct from empty', () => {
    expect(classify({ type: 'no-authorized-company' }).kind).toBe('no-authorized-company')
  })
})

describe('fence-stale never carries data (INV-FENCE / §12)', () => {
  it('a stale outcome maps to stale-rejected with no data field', () => {
    const state = classify({ type: 'fence-stale' })
    expect(state.kind).toBe('stale-rejected')
    expect(carriesData(state)).toBe(false)
    expect('data' in state).toBe(false)
  })

  it('even if the transport also had data, a stale classification wins', () => {
    // The caller must classify as fence-stale before looking at the body; this
    // test documents that the state type makes "stale + data" unrepresentable.
    const stale = classify({ type: 'fence-stale' })
    // @ts-expect-error stale-rejected has no data
    expect(stale.data).toBeUndefined()
  })
})

describe('ready is the only data-bearing state', () => {
  it('carriesData is true only for ready', () => {
    const ready = classify({ type: 'ok', data: [{ id: 'a' }], filterActive: false })
    expect(carriesData(ready)).toBe(true)
    expect(blocksFeatureMount(ready)).toBe(false)
  })

  it('every non-ready state blocks feature mount', () => {
    const nonReady: ViewOutcome<{ id: string }>[] = [
      { type: 'loading' },
      { type: 'no-authorized-company' },
      { type: 'fence-stale' },
      { type: 'offline' },
      { type: 'malformed' },
      { type: 'http', status: 403 },
      { type: 'http', status: 503 },
      { type: 'ok', data: [], filterActive: false },
    ]
    for (const outcome of nonReady) {
      expect(blocksFeatureMount(classify(outcome))).toBe(true)
    }
  })
})
