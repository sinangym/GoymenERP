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
 * nothing and discloses nothing. It sees the shell's state and does three things:
 *
 *  - while mounted, a departure from `confirming-switch` to anything but `idle`
 *    is the discard itself (the reducer produces it for CONFIRM_SWITCH_DISCARD
 *    alone) and forgets the attempt bound to this screen under the context that
 *    was being left;
 *  - a teardown while the question is open only ARMS a discard. The shell
 *    replaces the router outlet during `switching` — but also whenever the
 *    identity or directory read stops being ready, dialog open or not — so the
 *    teardown is not, by itself, the person's answer;
 *  - on every state it sees, it reconciles armed discards against the shell's
 *    committed revision: the same context at the same revision disarms, a higher
 *    revision in the same session confirms. See `pendingAttempt.ts`.
 *
 * A 403 while the dialog is open does not change the phase, and a session reset
 * makes the binding unreachable anyway; neither is a discard the user chose.
 */

import { useEffect, useRef, type ReactNode } from 'react'
import { useParams } from 'react-router'
import { useShellState } from '../shell/shellContext'
import type { ShellState } from '../shell/shellState'
import {
  armDiscard,
  attemptBindingFor,
  activationScope,
  editScope,
  lifecycleScope,
  forgetAttempt,
  reconcileArmedDiscards,
} from './pendingAttempt'

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
    reconcileArmedDiscards(shell, scope)
    if (
      before !== null &&
      before.phase === 'confirming-switch' &&
      shell.phase !== 'confirming-switch' &&
      shell.phase !== 'idle'
    ) {
      // The binding is taken from the state BEFORE the switch: that is the context
      // the user was asked about, and the only one whose attempt they abandoned.
      forgetAttempt(attemptBindingFor(before, scope))
    }
  }, [shell, scope])

  useEffect(
    () => () => {
      const before = last.current
      if (before !== null && before.phase === 'confirming-switch') {
        armDiscard(attemptBindingFor(before, scope), before.committedContextRevision)
      }
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

/**
 * The detail route also owns the record-scoped activation attempt and the record-scoped
 * deactivation / reactivation attempt.
 */
export function ActivationPendingAttemptScope({ children }: { readonly children: ReactNode }) {
  const { productId } = useParams()
  return (
    <PendingAttemptDiscardObserver scope={activationScope(productId ?? '')}>
      <PendingAttemptDiscardObserver scope={lifecycleScope(productId ?? '', 'deactivate')}>
        <PendingAttemptDiscardObserver scope={lifecycleScope(productId ?? '', 'reactivate')}>
          {children}
        </PendingAttemptDiscardObserver>
      </PendingAttemptDiscardObserver>
    </PendingAttemptDiscardObserver>
  )
}
