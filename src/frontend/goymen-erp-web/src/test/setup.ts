import { afterEach } from 'vitest'
import { cleanup, configure } from '@testing-library/react'

// Vitest preserves existing Node globals when installing JSDOM. Newer Node versions expose a native
// localStorage getter which is undefined without a storage file; browser tests must use their isolated
// JSDOM window's real Storage instead. Keep configurable getters so denied-storage tests can still stub
// or spy on access and restore the browser implementation afterward. No Node storage file or fake map.
const browserWindow = (globalThis as typeof globalThis & { jsdom?: { window: Window } }).jsdom?.window
if (browserWindow !== undefined) {
  for (const name of ['localStorage', 'sessionStorage'] as const) {
    Object.defineProperty(globalThis, name, { configurable: true, enumerable: true, get: () => browserWindow[name] })
  }
}

/**
 * Async waits get 3 seconds instead of testing-library's 1.
 *
 * This is not slack for a broken assertion. Since #146/#148 a capability-gated
 * route RE-AUTHORIZES on entry before it renders anything, so mounting one is
 * genuinely asynchronous — and the 1s default was calibrated for mounts that were
 * not. Under full-suite parallel load five different tests lost that race
 * intermittently while passing consistently when run alone.
 *
 * What this does NOT hide: a gate that never opens still fails, three seconds
 * later instead of one. The bound stays; only its size changes, and it changed
 * because the thing being waited for changed.
 */
configure({ asyncUtilTimeout: 3000 })

afterEach(() => {
  cleanup()
})
