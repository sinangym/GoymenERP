/**
 * YOL-D31 (WP14) — one group-company link command attempt at a time on the Cari card, under the Cari command rules
 * (./commandAttempt.ts) exactly as ./useGroupCompanyLinkCommand.ts applies them: one Idempotency-Key per submitted snapshot; an
 * unreadable answer keeps the SAME key and body for an explicit retry; after an applied command the caller re-reads.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { beginAttempt, generateAttemptKey, retryAttempt, settleAttempt, type AttemptState } from './commandAttempt'
import type { CommandContext, CommandOutcome } from './commandApi'
import type { ContractContext } from './clientContract'
import { GROUP_COMPANY_LINK_PROBLEM_CODE, GROUP_COMPANY_LINK_TEXT as T, groupCompanyLinkProblemCodeOf } from './groupCompanyLink'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellDispatch } from '../shell/shellContext'

export type GroupCompanyLinkSender = (context: CommandContext) => Promise<CommandOutcome<null>>

interface Frozen {
  readonly companyId: string
  readonly contract: ContractContext
  readonly send: GroupCompanyLinkSender
  readonly okText: string
}

export interface GroupCompanyLinkNotice {
  readonly tone: 'ok' | 'error'
  readonly text: string
}

/** Definite answers after which the link is stale and is read again. */
const REFRESH_CODES = new Set<string>([GROUP_COMPANY_LINK_PROBLEM_CODE.versionConflict, GROUP_COMPANY_LINK_PROBLEM_CODE.notFound])

export function groupCompanyLinkRefusalText(status: number, body: unknown): string {
  const code = groupCompanyLinkProblemCodeOf(body)
  if (code !== null && code in T.refusal) return T.refusal[code]
  if (status === 403) return T.commandForbidden
  return T.genericRefusal
}

export interface GroupCompanyLinkCommand {
  readonly busy: boolean
  /** An earlier attempt's outcome is unknown: only its retry may be sent. */
  readonly unresolved: boolean
  readonly notice: GroupCompanyLinkNotice | null
  readonly run: (operation: string, send: GroupCompanyLinkSender, okText: string) => Promise<boolean>
  readonly retry: () => Promise<boolean>
}

export function useGroupCompanyLinkCommand(
  companyId: string,
  contract: ContractContext | null,
  dirtyScope: string,
  refresh: () => void,
): GroupCompanyLinkCommand {
  const boundary = useAuthBoundary()
  const shellDispatch = useShellDispatch()
  const [attempt, setAttempt] = useState<AttemptState<Frozen, null>>({ phase: 'idle' })
  const attemptRef = useRef(attempt)
  const [notice, setNotice] = useState<GroupCompanyLinkNotice | null>(null)
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
        setNotice({ tone: 'error', text: groupCompanyLinkRefusalText(outcome.status, outcome.body) })
        const code = groupCompanyLinkProblemCodeOf(outcome.body)
        if (code !== null && REFRESH_CODES.has(code)) refresh()
      } else {
        setNotice({ tone: 'error', text: outcome.type === 'contract-not-ready' ? T.contractNotReady : T.genericRefusal })
      }
      return false
    },
    [boundary, refresh],
  )

  const run = useCallback(
    async (operation: string, send: GroupCompanyLinkSender, okText: string): Promise<boolean> => {
      if (attemptRef.current.phase !== 'idle') return false
      if (contract === null) {
        setNotice({ tone: 'error', text: T.contractNotReady })
        return false
      }
      return dispatch(
        beginAttempt<Frozen, null>({
          key: generateAttemptKey(),
          operation: `counterparty.group-company-link:${operation}`,
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

  return {
    busy: attempt.phase === 'submitting',
    unresolved,
    notice: attempt.phase === 'unknown' ? { tone: 'error', text: T.unknownOutcome } : notice,
    run,
    retry,
  }
}
