/**
 * The boundary's IDENTITY is stable while the security bindings a snapshot carries are unchanged, and moves the
 * moment one of them does. Before this, the hook answered a new `{ capture, report }` on every render, so a
 * consumer that listed the boundary in an effect's dependencies re-ran that effect after every render — ST-01's
 * initial read looped and its cleanup aborted the user's submitted search (#483, r14 run 36317283526).
 *
 * The states are the REAL reducer's (SESSION_READY, DIRTY_SET, ROUTE_ENTRY, a company switch through REQUEST_SWITCH +
 * REAUTH_OK, SESSION_RESET), and every `capture()` is compared with the pure `captureSecuritySnapshot` of the same
 * state: what a snapshot contains is unchanged, only how often the function is re-created.
 */

import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { renderHook } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ShellDispatchContext, ShellStateContext } from './shellContext'
import { captureSecuritySnapshot } from './authBoundary'
import {
  contextKey,
  contextKeyString,
  initialShellState,
  shellReducer,
  type CompanyContext,
  type ShellEvent,
  type ShellState,
} from './shellState'
import { useAuthBoundary } from './useAuthBoundary'

const USER = '11111111-1111-1111-1111-111111111111'
const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'

function context(companyId: string, capabilityGeneration: string): CompanyContext {
  return { userId: USER, scopeMode: 'single', companyId, capabilityGeneration, capabilities: [] }
}

function reduce(state: ShellState, ...events: ShellEvent[]): ShellState {
  return events.reduce(shellReducer, state)
}

/** What the pure snapshot function answers for a state: the contract `capture()` must keep. */
function expectedSnapshot(state: ShellState) {
  const key = contextKey(state)
  return captureSecuritySnapshot(state, state.activeContext?.userId ?? null, key === null ? null : contextKeyString(key))
}

function mount(initial: ShellState) {
  let state = initial
  const dispatch = vi.fn<(event: ShellEvent) => void>()
  const queryClient = new QueryClient()
  const wrapper = ({ children }: { readonly children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <ShellStateContext.Provider value={state}>
        <ShellDispatchContext.Provider value={dispatch}>{children}</ShellDispatchContext.Provider>
      </ShellStateContext.Provider>
    </QueryClientProvider>
  )
  const hook = renderHook(() => useAuthBoundary(), { wrapper })
  return {
    hook,
    dispatch,
    renderWith(next: ShellState) {
      state = next
      hook.rerender()
    },
  }
}

const ready = reduce(initialShellState, {
  type: 'SESSION_READY',
  forSessionEpoch: initialShellState.sessionEpoch,
  context: context(COMPANY_A, 'gen-1'),
})

describe('useAuthBoundary identity', () => {
  it('is the same object across re-renders and across shell changes that move no snapshot binding', () => {
    const { hook, renderWith } = mount(ready)
    const first = hook.result.current
    expect(first.capture()).toEqual(expectedSnapshot(ready))

    hook.rerender()
    expect(hook.result.current).toBe(first)

    // A new shell STATE OBJECT whose snapshot bindings are unchanged: a dirty scope, then a route entry.
    const dirty = reduce(ready, { type: 'DIRTY_SET', scope: 'sales-draft' })
    const entered = reduce(dirty, { type: 'ROUTE_ENTRY', key: '/stok/pozisyonlar' })
    expect(dirty).not.toBe(ready)
    expect(entered).not.toBe(dirty)
    for (const next of [dirty, entered]) {
      renderWith(next)
      expect(hook.result.current).toBe(first)
      expect(hook.result.current.capture).toBe(first.capture)
      expect(hook.result.current.report).toBe(first.report)
      // The snapshot is still exactly what the pure function answers for the CURRENT state.
      expect(hook.result.current.capture()).toEqual(expectedSnapshot(next))
    }
  })

  it('changes when the company changes, and then captures the new company', () => {
    const { hook, renderWith } = mount(ready)
    const first = hook.result.current

    const switching = reduce(ready, { type: 'REQUEST_SWITCH', target: { scopeMode: 'single', companyId: COMPANY_B } })
    const switched = reduce(switching, {
      type: 'REAUTH_OK',
      forGeneration: switching.requestFenceGeneration,
      context: context(COMPANY_B, 'gen-2'),
    })
    expect(switched.activeContext?.companyId).toBe(COMPANY_B)
    renderWith(switched)

    expect(hook.result.current).not.toBe(first)
    expect(hook.result.current.capture).not.toBe(first.capture)
    expect(hook.result.current.capture()).toEqual(expectedSnapshot(switched))
    expect(hook.result.current.capture().companyId).toBe(COMPANY_B)
    // `report` never depended on render state; only the capture moved.
    expect(hook.result.current.report).toBe(first.report)
    // The first render's capture still answers the bindings IT was created under.
    expect(first.capture().companyId).toBe(COMPANY_A)
  })

  it('changes when the session changes', () => {
    const { hook, renderWith } = mount(ready)
    const first = hook.result.current
    const reset = reduce(ready, { type: 'SESSION_RESET' })
    expect(reset.sessionEpoch).not.toBe(ready.sessionEpoch)
    renderWith(reset)

    expect(hook.result.current).not.toBe(first)
    expect(hook.result.current.capture()).toEqual(expectedSnapshot(reset))
  })

  it('still reports only 401 and 403, with the snapshot it was given', () => {
    const { hook, dispatch } = mount(ready)
    const snapshot = hook.result.current.capture()

    hook.result.current.report(200, snapshot)
    hook.result.current.report(500, snapshot)
    expect(dispatch).not.toHaveBeenCalled()

    hook.result.current.report(403, snapshot)
    expect(dispatch).toHaveBeenCalledTimes(1)
    expect(dispatch).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'BUSINESS_AUTH_OUTCOME', status: 403, snapshot }),
    )
  })
})
