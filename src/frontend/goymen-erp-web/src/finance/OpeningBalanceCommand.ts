/**
 * FIN-S9 command attempts: one Idempotency-Key per attempt, kept while the outcome is UNKNOWN (as
 * ./CollateralCommand.ts).
 *
 * - The key is reserved in `sessionStorage` under
 *   `goymen.finance.opening-balance.<operation>.v1:<sessionBinding>:<actorId>:<companyId>:<target>` BEFORE the request
 *   leaves, together with the exact command it was minted for. The target is `PROPOSAL_TARGET` for a proposal (one
 *   proposal form per screen) and the opening balance for a decision. While `sessionBinding` is null nothing is
 *   persisted.
 * - An unknown outcome keeps the key: "resend" sends the SAME command with the SAME key, so the server answers the first
 *   attempt. While an attempt is unknown, a DIFFERENT command for the same target is not sent.
 * - Abandoning is safe for every operation: a decision names the opening's version, and the server keeps one live
 *   opening per target and currency, so a second proposal for the same target is refused, never recorded twice.
 * - A definite answer (a decoded result or a status-only refusal) releases the key.
 */

import { useCallback, useState } from 'react'
import { generateAttemptKey } from './receiptCommand'
import type { OpeningBalanceCommandResult } from './OpeningBalanceContract'
import {
  sendOpeningBalanceCommand,
  type OpeningBalanceCommand,
  type OpeningBalanceOperation,
  type OpeningBalanceUnknownReason,
} from './OpeningBalanceHttp'

export const PROPOSAL_TARGET = 'proposal'

export type OpeningBalanceAttemptState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting' }
  | { readonly phase: 'unknown'; readonly reason: OpeningBalanceUnknownReason | 'recalled' }
  | { readonly phase: 'answered'; readonly result: OpeningBalanceCommandResult }
  | { readonly phase: 'refused'; readonly status: number }

export interface PendingOpeningBalanceAttempt {
  readonly key: string
  readonly command: OpeningBalanceCommand
}

const KEY_RE = /^[0-9a-f]{32}$/

export interface OpeningBalanceAttemptBinding {
  readonly sessionBinding: string | null
  readonly actorId: string
  readonly companyId: string
}

/** `null` while the server gave no session binding: nothing is persisted then. */
export function openingBalanceAttemptStoreKey(binding: OpeningBalanceAttemptBinding,
  operation: OpeningBalanceOperation, target: string): string | null {
  if (binding.sessionBinding === null || binding.sessionBinding.length === 0) return null
  return `goymen.finance.opening-balance.${operation}.v1:${binding.sessionBinding}:${binding.actorId}:` +
    `${binding.companyId}:${target}`
}

function decodePending(value: unknown, operation: OpeningBalanceOperation,
  target: string): PendingOpeningBalanceAttempt | null {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null
  const candidate = value as { key?: unknown; command?: unknown }
  if (typeof candidate.key !== 'string' || !KEY_RE.test(candidate.key)) return null
  const command = candidate.command as { operation?: unknown; body?: unknown; openingBalanceId?: unknown } | null | undefined
  if (typeof command !== 'object' || command === null || command.operation !== operation) return null
  if (typeof command.body !== 'object' || command.body === null || Array.isArray(command.body)) return null
  const named = operation === 'propose' ? PROPOSAL_TARGET : command.openingBalanceId
  if (named !== target) return null
  return { key: candidate.key, command: command as OpeningBalanceCommand }
}

export function recallOpeningBalanceAttempt(storeKey: string, operation: OpeningBalanceOperation,
  target: string): PendingOpeningBalanceAttempt | null {
  try {
    const raw = sessionStorage.getItem(storeKey)
    return raw === null ? null : decodePending(JSON.parse(raw), operation, target)
  } catch {
    return null
  }
}

function reserve(storeKey: string | null, attempt: PendingOpeningBalanceAttempt): void {
  if (storeKey === null) return
  try {
    sessionStorage.setItem(storeKey, JSON.stringify(attempt))
  } catch {
    // Storage refused: the attempt still lives in the component's state for this screen.
  }
}

function release(storeKey: string | null, operation: OpeningBalanceOperation, target: string, key: string): void {
  if (storeKey === null) return
  try {
    const current = recallOpeningBalanceAttempt(storeKey, operation, target)
    if (current === null || current.key === key) sessionStorage.removeItem(storeKey)
  } catch {
    // Nothing to release.
  }
}

const sameCommand = (a: OpeningBalanceCommand, b: OpeningBalanceCommand) => JSON.stringify(a) === JSON.stringify(b)

export type OpeningBalanceSubmitResult = 'started' | 'blocked'

export interface OpeningBalanceAttempt {
  readonly state: OpeningBalanceAttemptState
  readonly submit: (command: OpeningBalanceCommand) => Promise<OpeningBalanceSubmitResult>
  readonly resend: () => Promise<void>
  readonly abandon: () => void
  readonly reset: () => void
}

export function useOpeningBalanceAttempt(binding: OpeningBalanceAttemptBinding | null,
  operation: OpeningBalanceOperation, target: string,
  onSettled?: (operation: OpeningBalanceOperation, result: OpeningBalanceCommandResult) => void): OpeningBalanceAttempt {
  const companyId = binding?.companyId ?? null
  const storeKey = binding === null ? null : openingBalanceAttemptStoreKey(binding, operation, target)
  const [pending, setPending] = useState<PendingOpeningBalanceAttempt | null>(() =>
    storeKey === null ? null : recallOpeningBalanceAttempt(storeKey, operation, target))
  const [state, setState] = useState<OpeningBalanceAttemptState>(() =>
    pending === null ? { phase: 'idle' } : { phase: 'unknown', reason: 'recalled' })

  const run = useCallback(async (attempt: PendingOpeningBalanceAttempt) => {
    if (companyId === null) return
    reserve(storeKey, attempt)
    setPending(attempt)
    setState({ phase: 'submitting' })
    const reply = await sendOpeningBalanceCommand(companyId, attempt.command, attempt.key)
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

  const submit = useCallback(async (command: OpeningBalanceCommand): Promise<OpeningBalanceSubmitResult> => {
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
