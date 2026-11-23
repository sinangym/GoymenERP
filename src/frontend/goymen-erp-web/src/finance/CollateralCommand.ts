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
 * - A decoded owner result releases the key. A status-only refusal can settle a first request, but cannot
 *   resolve an earlier UNKNOWN result: that request keeps its exact command and key.
 */

import { useCallback, useLayoutEffect, useRef, useState } from 'react'
import { useAuthBoundary } from '../shell/useAuthBoundary'
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
  | { readonly phase: 'unknown'; readonly reason: CollateralUnknownReason | 'recalled'; readonly refusalStatus?: number }
  | { readonly phase: 'answered'; readonly result: CollateralCommandResult }
  | { readonly phase: 'refused'; readonly status: number }

export interface PendingCollateralAttempt {
  readonly key: string
  readonly command: CollateralCommand
  /** A dispatch owns completion only while this claim is still current. Older journals have no claim. */
  readonly claim?: string
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
  const candidate = value as { key?: unknown; command?: unknown; claim?: unknown }
  if (typeof candidate.key !== 'string' || !KEY_RE.test(candidate.key)) return null
  if (candidate.claim !== undefined && (typeof candidate.claim !== 'string' || !KEY_RE.test(candidate.claim))) return null
  const command = candidate.command as { operation?: unknown; body?: unknown; collateralId?: unknown } | null | undefined
  if (typeof command !== 'object' || command === null || command.operation !== operation) return null
  if (typeof command.body !== 'object' || command.body === null || Array.isArray(command.body)) return null
  // The stored command belongs to this target: the account of a record, the collateral of a change or release.
  const named = operation === 'record'
    ? (command.body as { companyAccountId?: unknown }).companyAccountId
    : command.collateralId
  if (named !== target) return null
  return { key: candidate.key, command: command as CollateralCommand, claim: candidate.claim }
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

function reserve(storeKey: string | null, operation: CollateralOperation, target: string,
  attempt: PendingCollateralAttempt): boolean {
  if (storeKey === null) return true
  try {
    const current = recallCollateralAttempt(storeKey, operation, target)
    if ((current !== null && (current.key !== attempt.key || !sameCommand(current.command, attempt.command)))
      || (current === null && sessionStorage.getItem(storeKey) !== null)) return false
    sessionStorage.setItem(storeKey, JSON.stringify(attempt))
    return true
  } catch {
    // A bound command must survive an auth-driven remount before any request can leave.
    return false
  }
}

function release(storeKey: string | null, operation: CollateralOperation, target: string,
  attempt: PendingCollateralAttempt): boolean {
  if (storeKey === null) return true
  try {
    const current = recallCollateralAttempt(storeKey, operation, target)
    if (current === null) return sessionStorage.getItem(storeKey) === null
    if (current.key !== attempt.key || current.claim !== attempt.claim) return false
    sessionStorage.removeItem(storeKey)
    return true
  } catch {
    // Keep the unresolved attempt if durable storage cannot confirm release.
    return false
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
  const boundary = useAuthBoundary()
  const inFlight = useRef(false)
  const storeKey = binding === null ? null : collateralAttemptStoreKey(binding, operation, target)
  const [pending, setPending] = useState<PendingCollateralAttempt | null>(() =>
    storeKey === null ? null : recallCollateralAttempt(storeKey, operation, target))
  const [state, setState] = useState<CollateralAttemptState>(() =>
    pending === null ? { phase: 'idle' } : { phase: 'unknown', reason: 'recalled' })
  const pendingRef = useRef(pending)
  // K3-REVIEW-B P3-2: true while the pending attempt has never been reserved, so it has never left this tab.
  const unsentRef = useRef(false)
  const bindingKey = JSON.stringify([binding?.sessionBinding, binding?.actorId, companyId, operation, target])
  const originalBinding = useRef(bindingKey)
  const liveBinding = useRef(bindingKey)
  const mounted = useRef(false)
  const mountGeneration = useRef(0)
  useLayoutEffect(() => {
    mounted.current = true
    liveBinding.current = bindingKey
    mountGeneration.current += 1
    return () => {
      mounted.current = false
      mountGeneration.current += 1
    }
  }, [bindingKey])

  const run = useCallback(async (attempt: PendingCollateralAttempt, previouslyUnknown: boolean) => {
    if (companyId === null || inFlight.current || !mounted.current || liveBinding.current !== bindingKey
      || originalBinding.current !== bindingKey) return
    inFlight.current = true
    const generation = mountGeneration.current
    const claimed = { ...attempt, claim: generateAttemptKey() }
    const captured = boundary.capture()
    pendingRef.current = claimed
    setPending(claimed)
    if (!reserve(storeKey, operation, target, claimed)) {
      inFlight.current = false
      if (!previouslyUnknown) unsentRef.current = true
      setState({ phase: 'unknown', reason: 'recalled' })
      return
    }
    unsentRef.current = false
    setState({ phase: 'submitting' })
    const reply = await sendCollateralCommand(companyId, attempt.command, attempt.key).finally(() => {
      inFlight.current = false
    })
    // The shell checks the captured authorization fence even if this form has since unmounted.
    if (reply.kind === 'refused' && (reply.status === 401 || reply.status === 403)) boundary.report(reply.status, captured)
    if (!mounted.current || liveBinding.current !== bindingKey || mountGeneration.current !== generation) return
    // A late result from an older dispatch cannot clear a retry's journal or call its old form callbacks.
    if (storeKey !== null && recallCollateralAttempt(storeKey, operation, target)?.claim !== claimed.claim) {
      setState({ phase: 'unknown', reason: 'recalled' })
      return
    }
    if (reply.kind === 'unknown') {
      setState({ phase: 'unknown', reason: reply.reason })
      return
    }
    if (reply.kind === 'refused') {
      if (previouslyUnknown) {
        setState({ phase: 'unknown', reason: 'recalled', refusalStatus: reply.status })
        return
      }
    }
    if (!release(storeKey, operation, target, claimed)) {
      setState({ phase: 'unknown', reason: 'recalled' })
      return
    }
    pendingRef.current = null
    setPending(null)
    if (reply.kind === 'refused') {
      setState({ phase: 'refused', status: reply.status })
      return
    }
    setState({ phase: 'answered', result: reply.result })
    onSettled?.(operation, reply.result)
  }, [bindingKey, boundary, companyId, onSettled, operation, storeKey, target])

  const submit = useCallback(async (command: CollateralCommand): Promise<CollateralSubmitResult> => {
    if (companyId === null || inFlight.current || !mounted.current || liveBinding.current !== bindingKey
      || originalBinding.current !== bindingKey || state.phase === 'submitting') return 'blocked'
    const current = pendingRef.current
    if (current !== null) {
      if (!sameCommand(current.command, command)) return 'blocked'
      await run(current, true)
      return 'started'
    }
    await run({ key: generateAttemptKey(), command }, false)
    return 'started'
  }, [bindingKey, companyId, run, state.phase])

  const resend = useCallback(async () => {
    const current = pendingRef.current
    if (current !== null && !inFlight.current && state.phase !== 'submitting') await run(current, true)
  }, [run, state.phase])

  const abandon = useCallback(() => {
    if (inFlight.current || !mounted.current || liveBinding.current !== bindingKey || originalBinding.current !== bindingKey) return
    const current = pendingRef.current
    // An attempt that was never reserved never left; dropping it cannot lose an outcome, even when the slot holds
    // another owner's unreadable record that this tab must not release.
    if (current !== null && !unsentRef.current && !release(storeKey, operation, target, current)) return
    unsentRef.current = false
    pendingRef.current = null
    setPending(null)
    setState({ phase: 'idle' })
  }, [bindingKey, operation, storeKey, target])

  const reset = useCallback(() => {
    if (pendingRef.current === null && !inFlight.current && mounted.current && liveBinding.current === bindingKey
      && originalBinding.current === bindingKey) setState({ phase: 'idle' })
  }, [bindingKey])

  return { state, submit, resend, abandon, reset }
}
