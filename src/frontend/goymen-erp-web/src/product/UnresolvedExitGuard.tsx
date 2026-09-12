/**
 * Guards every way OUT of a form that still holds unresolved work.
 *
 * There are three, and the screens previously covered only one and a half:
 *
 *  - a full page unload (`beforeunload`) — the browser shows its own prompt; we can
 *    only make it ask at all;
 *  - an in-app navigation the screen initiates (its own Cancel button) — handled by
 *    the form's own dialog;
 *  - an in-app navigation the screen does NOT initiate: browser Back, a sidebar
 *    link, a row link. That one used to pass straight through, taking the form and
 *    an unresolved idempotency key with it.
 *
 * `useBlocker` closes the third. It works because the shell uses a data router
 * (`createBrowserRouter` / `createMemoryRouter`), which is the same router the
 * Product routes mount into — no second router and no second history stack.
 *
 * The COMPANY switch is deliberately not handled here: it is not a navigation. It
 * goes through the shell's own `DIRTY_SET` gate, which turns it into
 * `confirming-switch` before any context is committed. Two guards over one action
 * would ask twice and teach people to dismiss both.
 */

import { useEffect } from 'react'
import { useBlocker } from 'react-router'
import { UnsavedDialog } from './UnsavedDialog'

export function UnresolvedExitGuard({
  active,
  title,
  detail,
  stayLabel,
  leaveLabel,
  onLeave,
}: {
  readonly active: boolean
  readonly title: string
  readonly detail: string
  readonly stayLabel: string
  readonly leaveLabel: string
  /** Called when the user chooses to leave, before the navigation proceeds. */
  readonly onLeave?: () => void
}) {
  /**
   * Blocks only a real location change. A re-render, a search-param tweak or a
   * navigation to the same path is not a departure, and prompting on one would
   * train the user to dismiss the dialog without reading it.
   */
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      active && currentLocation.pathname !== nextLocation.pathname,
  )

  useEffect(() => {
    if (!active) return
    function onBeforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault()
      // Assigning returnValue is what still triggers the prompt in Chromium; the
      // string itself has been ignored by every browser for years.
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [active])

  /**
   * A blocked navigation that is no longer needed must be RESET, not left pending:
   * a blocker stuck in `blocked` swallows every later navigation silently, which
   * would be a worse failure than the one it was added to prevent.
   */
  useEffect(() => {
    if (blocker.state === 'blocked' && !active) blocker.reset()
  }, [blocker, active])

  if (blocker.state !== 'blocked') return null

  return (
    <UnsavedDialog
      title={title}
      detail={detail}
      stayLabel={stayLabel}
      leaveLabel={leaveLabel}
      onStay={() => blocker.reset()}
      onLeave={() => {
        onLeave?.()
        blocker.proceed()
      }}
    />
  )
}
