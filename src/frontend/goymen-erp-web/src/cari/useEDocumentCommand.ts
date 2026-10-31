/**
 * SAT-D102 — one command attempt at a time for the Cari card's e-belge panel (status check and scenario override).
 *
 * The rules are the Cari command rules (./commandAttempt.ts): one Idempotency-Key per submitted snapshot, never per
 * click; an answer the client could not read (network, 5xx, missing acknowledgment) keeps the SAME key and body for an
 * explicit retry and blocks a different command until it is settled; a definite answer ends the attempt. After an
 * applied command the caller re-reads — a 204 carries no body, so nothing is painted from what was sent.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { beginAttempt, generateAttemptKey, retryAttempt, settleAttempt, type AttemptState } from './commandAttempt'
import type { CommandContext, CommandOutcome } from './commandApi'
import type { ContractContext } from './clientContract'
import { E_DOCUMENT_TEXT as T, E_DOCUMENT_PROBLEM_CODE, eDocumentProblemCodeOf } from './eDocument'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellDispatch } from '../shell/shellContext'

export type EDocumentSender = (context: CommandContext) => Promise<CommandOutcome<null>>

interface Frozen {
  readonly companyId: string
  readonly contract: ContractContext
  readonly send: EDocumentSender
  readonly okText: string
}

export interface EDocumentNotice {
  readonly tone: 'ok' | 'error'
  readonly text: string
}

/** Definite answers after which the screen's data is stale and is read again. */
const REFRESH_CODES = new Set<string>([
  E_DOCUMENT_PROBLEM_CODE.versionConflict,
  E_DOCUMENT_PROBLEM_CODE.codeListMissing,
  E_DOCUMENT_PROBLEM_CODE.notListed,
  E_DOCUMENT_PROBLEM_CODE.notFound,
  E_DOCUMENT_PROBLEM_CODE.accountInactive,
])

export function eDocumentRefusalText(status: number, body: unknown): string {
  const code = eDocumentProblemCodeOf(body)
  if (code !== null && code in T.refusal) return T.refusal[code]
  if (status === 403) return T.commandForbidden
  return T.genericRefusal
}

export interface EDocumentCommand {
  readonly busy: boolean
  /** An earlier attempt's outcome is unknown: only its retry may be sent. */
  readonly unresolved: boolean
  readonly notice: EDocumentNotice | null
  readonly run: (operation: string, send: EDocumentSender, okText: string) => Promise<boolean>
  readonly retry: () => Promise<boolean>
  readonly clearNotice: () => void
}

export function useEDocumentCommand(
  companyId: string,
  contract: ContractContext | null,
  dirtyScope: string,
  refresh: () => void,
): EDocumentCommand {
  const boundary = useAuthBoundary()
  const shellDispatch = useShellDispatch()
  const [attempt, setAttempt] = useState<AttemptState<Frozen, null>>({ phase: 'idle' })
  const attemptRef = useRef(attempt)
  const [notice, setNotice] = useState<EDocumentNotice | null>(null)
  const unresolved = attempt.phase === 'submitting' || attempt.phase === 'unknown'

  useEffect(() => {
    shellDispatch({ type: unresolved ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: dirtyScope })
  }, [dirtyScope, shellDispatch, unresolved])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: dirtyScope }), [dirtyScope, shellDispatch])

  const dispatch = useCallback(
    async (inFlight: AttemptState<Frozen, null>): Promise<boolean> => {
      if (inFlight.phase !== 'submitting') return false
      const frozen = inFlight.snapshot.payload
      attemptRef.current = inFlight
      setAttempt(inFlight)
      setNotice(null)
      const security = boundary.capture()
      const outcome = await frozen.send({
        companyId: frozen.companyId,
        idempotencyKey: inFlight.snapshot.key,
        contract: frozen.contract,
      })
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      const next = settleAttempt(inFlight, outcome)
      if (next.phase === 'completed') {
        attemptRef.current = { phase: 'idle' }
        setAttempt({ phase: 'idle' })
        setNotice({ tone: 'ok', text: frozen.okText })
        refresh()
        return true
      }
      if (next.phase === 'unknown') {
        attemptRef.current = next
        setAttempt(next)
        return false
      }
      attemptRef.current = { phase: 'idle' }
      setAttempt({ phase: 'idle' })
      if (outcome.type === 'http') {
        setNotice({ tone: 'error', text: eDocumentRefusalText(outcome.status, outcome.body) })
        const code = eDocumentProblemCodeOf(outcome.body)
        if (code !== null && REFRESH_CODES.has(code)) refresh()
      } else {
        setNotice({ tone: 'error', text: outcome.type === 'contract-not-ready' ? T.contractNotReady : T.genericRefusal })
      }
      return false
    },
    [boundary, refresh],
  )

  const run = useCallback(
    async (operation: string, send: EDocumentSender, okText: string): Promise<boolean> => {
      const current = attemptRef.current
      if (current.phase !== 'idle') return false
      if (contract === null) {
        setNotice({ tone: 'error', text: T.contractNotReady })
        return false
      }
      return dispatch(
        beginAttempt<Frozen, null>({
          key: generateAttemptKey(),
          operation: `counterparty.e-document:${operation}`,
          binding: `${contract.origin} ${contract.namespace} ${companyId}`,
          payload: { companyId, contract, send, okText },
        }),
      )
    },
    [companyId, contract, dispatch],
  )

  const retry = useCallback(async (): Promise<boolean> => {
    const next = retryAttempt(attemptRef.current)
    return next.phase === 'submitting' ? dispatch(next) : false
  }, [dispatch])

  const clearNotice = useCallback(() => setNotice(null), [])

  return {
    busy: attempt.phase === 'submitting',
    unresolved,
    notice: attempt.phase === 'unknown' ? { tone: 'error', text: T.unknownOutcome } : notice,
    run,
    retry,
    clearNotice,
  }
}
