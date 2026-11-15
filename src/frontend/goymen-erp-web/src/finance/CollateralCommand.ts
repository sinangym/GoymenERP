/**
 * API-85b command attempts: one Idempotency-Key per attempt, kept while the outcome is UNKNOWN (as
 * ./CreditLimitCommand.ts).
 *
 * - The key is reserved in `sessionStorage` under
 *   `goymen.finance.collateral.<operation>.v1:<sessionBinding>:<actorId>:<companyId>:<target>` BEFORE the request
 *   leaves, together with the exact command it was minted for. The target is the company account for a record and the
 *   collateral for a change or a release. While `sessionBinding` is null nothing is persisted.
 * - An unknown outcome (no reply, offline, 5xx, an undecodable body, replay headers that disagree with the body) keeps
 *   the key: "resend" sends the SAME command with the SAME key, so the server answers the first attempt.
 * - While an attempt is unknown, a DIFFERENT command for the same target is not sent; the person resends it or
 *   abandons it.
 * - Abandoning a change or a release is safe: each names the collateral revision, so a second one from the same
 *   revision is refused by the server, never applied twice. A RECORD names no revision: abandoning an unknown record
 *   and recording again could hold the same collateral twice, so the screen warns to check the account first
 *   (`requiresCheckBeforeAbandon`).
 * - A definite answer (a decoded result or a status-only refusal) releases the key.
 */

import { useCallback, useState } from 'react'
import { generateAttemptKey } from './receiptCommand'
import type { CollateralCommandResult } from './CollateralContract'
import {
  sendCollateralCommand,
  type CollateralCommand,
  type CollateralOperation,
  type CollateralUnknownReason,
} from './CollateralHttp'

export type CollateralAttemptState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting' }
  | { readonly phase: 'unknown'; readonly reason: CollateralUnknownReason | 'recalled' }
  | { readonly phase: 'answered'; readonly result: CollateralCommandResult }
  | { readonly phase: 'refused'; readonly status: number }

export interface PendingCollateralAttempt {
  readonly key: string
  readonly command: CollateralCommand
}

const KEY_RE = /^[0-9a-f]{32}$/

export interface CollateralAttemptBinding {
  readonly sessionBinding: string | null
  readonly actorId: string
  readonly companyId: string
}

/** Only a record cannot be abandoned blindly: it names no revision the server could refuse a second time. */
export const requiresCheckBeforeAbandon = (operation: CollateralOperation): boolean => operation === 'record'

/** `null` while the server gave no session binding: nothing is persisted then. */
export function collateralAttemptStoreKey(binding: CollateralAttemptBinding, operation: CollateralOperation,
  target: string): string | null {
  if (binding.sessionBinding === null || binding.sessionBinding.length === 0) return null
  return `goymen.finance.collateral.${operation}.v1:${binding.sessionBinding}:${binding.actorId}:${binding.companyId}:${target}`
}

function decodePending(value: unknown, operation: CollateralOperation, target: string): PendingCollateralAttempt | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as { key?: unknown; command?: unknown }
  if (typeof candidate.key !== 'string' || !KEY_RE.test(candidate.key)) return null
  const command = candidate.command as { operation?: unknown; body?: unknown; collateralId?: unknown } | null | undefined
  if (typeof command !== 'object' || command === null || command.operation !== operation) return null
  if (typeof command.body !== 'object' || command.body === null || Array.isArray(command.body)) return null
  // The stored command belongs to this target: the account of a record, the collateral of a change or release.
  const named = operation === 'record'
    ? (command.body as { companyAccountId?: unknown }).companyAccountId
    : command.collateralId
  if (named !== target) return null
  return { key: candidate.key, command: command as CollateralCommand }
}

export function recallCollateralAttempt(storeKey: string, operation: CollateralOperation,
  target: string): PendingCollateralAttempt | null {
  try {
    const raw = sessionStorage.getItem(storeKey)
    return raw === null ? null : decodePending(JSON.parse(raw), operation, target)
  } catch {
    return null
  }
}

function reserve(storeKey: string | null, attempt: PendingCollateralAttempt): void {
  if (storeKey === null) return
  try {
    sessionStorage.setItem(storeKey, JSON.stringify(attempt))
  } catch {
    // Storage refused: the attempt still lives in the component's state for this screen.
  }
}

function release(storeKey: string | null, operation: CollateralOperation, target: string, key: string): void {
  if (storeKey === null) return
  try {
    const current = recallCollateralAttempt(storeKey, operation, target)
    if (current === null || current.key === key) sessionStorage.removeItem(storeKey)
  } catch {
    // Nothing to release.
  }
}

const sameCommand = (a: CollateralCommand, b: CollateralCommand) => JSON.stringify(a) === JSON.stringify(b)

export type CollateralSubmitResult = 'started' | 'blocked'

export interface CollateralAttempt {
  readonly state: CollateralAttemptState
  /** Sends `command`; `blocked` while another command for this target is unknown or in flight. */
  readonly submit: (command: CollateralCommand) => Promise<CollateralSubmitResult>
  /** Resends the unknown attempt with its own key and command. */
  readonly resend: () => Promise<void>
  /** Drops an unknown attempt (see the module note: a record needs the account checked first). */
  readonly abandon: () => void
  /** Clears a settled answer from the screen. */
  readonly reset: () => void
}

export function useCollateralAttempt(binding: CollateralAttemptBinding | null, operation: CollateralOperation,
  target: string, onSettled?: (operation: CollateralOperation, result: CollateralCommandResult) => void): CollateralAttempt {
  const companyId = binding?.companyId ?? null
  const storeKey = binding === null ? null : collateralAttemptStoreKey(binding, operation, target)
  const [pending, setPending] = useState<PendingCollateralAttempt | null>(() =>
    storeKey === null ? null : recallCollateralAttempt(storeKey, operation, target))
  const [state, setState] = useState<CollateralAttemptState>(() =>
    pending === null ? { phase: 'idle' } : { phase: 'unknown', reason: 'recalled' })

  const run = useCallback(async (attempt: PendingCollateralAttempt) => {
    if (companyId === null) return
    reserve(storeKey, attempt)
    setPending(attempt)
    setState({ phase: 'submitting' })
    const reply = await sendCollateralCommand(companyId, attempt.command, attempt.key)
    if (reply.kind === 'unknown') {
      setState({ phase: 'unknown', reason: reply.reason })
      return
    }
    release(storeKey, operation, target, attempt.key)
    setPending(null)
    if (reply.kind === 'refused') {
      setState({ phase: 'refused', status: reply.status })
      return
    }
    setState({ phase: 'answered', result: reply.result })
    onSettled?.(operation, reply.result)
  }, [companyId, onSettled, operation, storeKey, target])

  const submit = useCallback(async (command: CollateralCommand): Promise<CollateralSubmitResult> => {
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
    if (pending !== null) release(storeKey, operation, target, pending.key)
    setPending(null)
    setState({ phase: 'idle' })
  }, [operation, pending, storeKey, target])

  const reset = useCallback(() => {
    if (pending === null) setState({ phase: 'idle' })
  }, [pending])

  return { state, submit, resend, abandon, reset }
}
