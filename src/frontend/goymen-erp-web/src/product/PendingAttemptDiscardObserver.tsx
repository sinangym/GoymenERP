/**
 * Honours the switch dialog's promise for a pending Product attempt.
 *
 * The shell's confirmation says "bu ekrandaki değişiklikler ve bekleyen işlem
 * bırakılır". The form itself cannot keep that promise: the moment the user
 * confirms, the phase leaves `confirming-switch`, the context unbinds and the
 * gated route closes — the form is gone before any effect of its own can run,
 * and the attempt would sit in the pending store, to resurface if this exact
 * context were ever committed again.
 *
 * This observer sits OUTSIDE the capability gate, renders nothing, fetches
 * nothing and discloses nothing. It watches for one thing only: the shell was in
 * `confirming-switch` — the user was being asked about THIS screen's pending work
 * — and then left that phase for anything other than `idle`. `idle` is "Kal";
 * everything else is the switch going ahead, which the reducer produces for
 * exactly one event, CONFIRM_SWITCH_DISCARD. The attempt bound to this screen
 * under the context that was being left is then forgotten.
 *
 * The shell replaces the router outlet while `switching`, so this component is
 * usually unmounted in the same commit and sees the departure from its cleanup
 * rather than from a re-render; both paths apply the same rule.
 *
 * A 403 while the dialog is open does not change the phase, and a session reset
 * makes the binding unreachable anyway; neither is a discard the user chose.
 */

import { useEffect, useRef, type ReactNode } from 'react'
import { useParams } from 'react-router'
import { useShellState } from '../shell/shellContext'
import type { ShellState } from '../shell/shellState'
import { attemptBindingFor, editScope, forgetAttempt } from './pendingAttempt'

function leftTheQuestion(before: ShellState | null, after: ShellState | null): boolean {
  if (before === null || before.phase !== 'confirming-switch') return false
  return after === null || (after.phase !== 'confirming-switch' && after.phase !== 'idle')
}

export function PendingAttemptDiscardObserver({
  scope,
  children,
}: {
  readonly scope: string
  readonly children: ReactNode
}) {
  const shell = useShellState()
  const last = useRef<ShellState | null>(null)

  useEffect(() => {
    const before = last.current
    last.current = shell
    // The binding is taken from the state BEFORE the switch: that is the context
    // the user was asked about, and the only one whose attempt they abandoned.
    if (leftTheQuestion(before, shell)) forgetAttempt(attemptBindingFor(before as ShellState, scope))
  }, [shell, scope])

  useEffect(
    () => () => {
      const before = last.current
      if (leftTheQuestion(before, null)) forgetAttempt(attemptBindingFor(before as ShellState, scope))
    },
    [scope],
  )

  return <>{children}</>
}

/** The edit route's observer: the scope is the record in the URL. */
export function EditPendingAttemptScope({ children }: { readonly children: ReactNode }) {
  const { productId } = useParams()
  return (
    <PendingAttemptDiscardObserver scope={editScope(productId ?? '')}>
      {children}
    </PendingAttemptDiscardObserver>
  )
}
