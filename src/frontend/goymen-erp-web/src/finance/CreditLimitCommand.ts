/**
 * API-85a command attempts: one Idempotency-Key per attempt, kept while the outcome is UNKNOWN.
 *
 * - The key is reserved in `sessionStorage` under
 *   `goymen.finance.credit-limit.<operation>.v1:<sessionBinding>:<actorId>:<companyId>:<target>` BEFORE the request
 *   leaves, together with the exact command it was minted for. `sessionBinding` is the server's session identity
 *   (`ShellState.sessionBinding`), which survives an F5; while it is null nothing is persisted and the attempt lives
 *   only in the screen's state.
 * - An unknown outcome (no reply, offline, 5xx, an undecodable body, replay headers that disagree with the body)
 *   keeps the key: "resend" sends the SAME command with the SAME key, so the server answers the first attempt.
 * - While an attempt is unknown, a DIFFERENT command for the same target is not sent; the person either resends the
 *   same one or abandons it. Abandoning is safe for this master: every change names the account revision (or the
 *   proposal version), so a second change from the same revision is refused by the server, never applied twice.
 * - A definite answer (a decoded result or a status-only refusal) releases the key.
 *
 * A session, actor or company change gives a different store key: an attempt of one context is never resent in
 * another.
 */

import { useCallback, useState } from 'react'
import { generateAttemptKey } from './receiptCommand'
import type { CreditLimitCommandResult } from './CreditLimitContract'
import {
  sendCreditLimitCommand,
  type CreditLimitCommand,
  type CreditLimitOperation,
  type CreditLimitUnknownReason,
} from './CreditLimitHttp'

export type CreditLimitAttemptState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting' }
  | { readonly phase: 'unknown'; readonly reason: CreditLimitUnknownReason | 'recalled' }
  | { readonly phase: 'answered'; readonly result: CreditLimitCommandResult }
  | { readonly phase: 'refused'; readonly status: number }

export interface PendingCreditLimitAttempt {
  readonly key: string
  readonly command: CreditLimitCommand
}

const KEY_RE = /^[0-9a-f]{32}$/

export interface CreditLimitAttemptBinding {
  readonly sessionBinding: string | null
  readonly actorId: string
  readonly companyId: string
}

/** `null` while the server gave no session binding: nothing is persisted then. */
export function creditLimitAttemptStoreKey(binding: CreditLimitAttemptBinding, operation: CreditLimitOperation,
  target: string): string | null {
  if (binding.sessionBinding === null || binding.sessionBinding.length === 0) return null
  return `goymen.finance.credit-limit.${operation}.v1:${binding.sessionBinding}:${binding.actorId}:${binding.companyId}:${target}`
}

function decodePending(value: unknown, operation: CreditLimitOperation): PendingCreditLimitAttempt | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as { key?: unknown; command?: unknown }
  if (typeof candidate.key !== 'string' || !KEY_RE.test(candidate.key)) return null
  const command = candidate.command as { operation?: unknown; body?: unknown } | null | undefined
  if (typeof command !== 'object' || command === null || command.operation !== operation) return null
  if (typeof command.body !== 'object' || command.body === null || Array.isArray(command.body)) return null
  return { key: candidate.key, command: command as CreditLimitCommand }
}

export function recallCreditLimitAttempt(storeKey: string, operation: CreditLimitOperation): PendingCreditLimitAttempt | null {
  try {
    const raw = sessionStorage.getItem(storeKey)
    return raw === null ? null : decodePending(JSON.parse(raw), operation)
  } catch {
    return null
  }
}

function reserve(storeKey: string | null, attempt: PendingCreditLimitAttempt): void {
  if (storeKey === null) return
  try {
    sessionStorage.setItem(storeKey, JSON.stringify(attempt))
  } catch {
    // Storage refused: the attempt still lives in the component's state for this screen.
  }
}

function release(storeKey: string | null, operation: CreditLimitOperation, key: string): void {
  if (storeKey === null) return
  try {
    const current = recallCreditLimitAttempt(storeKey, operation)
    if (current === null || current.key === key) sessionStorage.removeItem(storeKey)
  } catch {
    // Nothing to release.
  }
}

const sameCommand = (a: CreditLimitCommand, b: CreditLimitCommand) => JSON.stringify(a) === JSON.stringify(b)

export type CreditLimitSubmitResult = 'started' | 'blocked'

export interface CreditLimitAttempt {
  readonly state: CreditLimitAttemptState
  /** Sends `command`; `blocked` while another command for this target is unknown or in flight. */
  readonly submit: (command: CreditLimitCommand) => Promise<CreditLimitSubmitResult>
  /** Resends the unknown attempt with its own key and command. */
  readonly resend: () => Promise<void>
  /** Drops an unknown attempt (see the module note on why that is safe here). */
  readonly abandon: () => void
  /** Clears a settled answer from the screen. */
  readonly reset: () => void
}

export function useCreditLimitAttempt(binding: CreditLimitAttemptBinding | null, operation: CreditLimitOperation,
  target: string, onSettled?: (operation: CreditLimitOperation, result: CreditLimitCommandResult) => void): CreditLimitAttempt {
  const companyId = binding?.companyId ?? null
  const storeKey = binding === null ? null : creditLimitAttemptStoreKey(binding, operation, target)
  const [pending, setPending] = useState<PendingCreditLimitAttempt | null>(() =>
    storeKey === null ? null : recallCreditLimitAttempt(storeKey, operation))
  const [state, setState] = useState<CreditLimitAttemptState>(() =>
    pending === null ? { phase: 'idle' } : { phase: 'unknown', reason: 'recalled' })

  const run = useCallback(async (attempt: PendingCreditLimitAttempt) => {
    if (companyId === null) return
    reserve(storeKey, attempt)
    setPending(attempt)
    setState({ phase: 'submitting' })
    const reply = await sendCreditLimitCommand(companyId, attempt.command, attempt.key)
    if (reply.kind === 'unknown') {
      setState({ phase: 'unknown', reason: reply.reason })
      return
    }
    release(storeKey, operation, attempt.key)
    setPending(null)
    if (reply.kind === 'refused') {
      setState({ phase: 'refused', status: reply.status })
      return
    }
    setState({ phase: 'answered', result: reply.result })
    onSettled?.(operation, reply.result)
  }, [companyId, onSettled, operation, storeKey])

  const submit = useCallback(async (command: CreditLimitCommand): Promise<CreditLimitSubmitResult> => {
    if (companyId === null || state.phase === 'submitting') return 'blocked'
    if (pending !== null) {
      if (!sameCommand(pending.command, command)) return 'blocked'
      await run(pending)
      return 'started'
    }
    await run({ key: generateAttemptKey(), command })
    return 'started'
  }, [companyId, pending, run, state.phase])

  const resend = useCallback(async () => {
    if (pending !== null && state.phase !== 'submitting') await run(pending)
  }, [pending, run, state.phase])

  const abandon = useCallback(() => {
    if (pending !== null) release(storeKey, operation, pending.key)
    setPending(null)
    setState({ phase: 'idle' })
  }, [operation, pending, storeKey])

  const reset = useCallback(() => {
    if (pending === null) setState({ phase: 'idle' })
  }, [pending])

  return { state, submit, resend, abandon, reset }
}
