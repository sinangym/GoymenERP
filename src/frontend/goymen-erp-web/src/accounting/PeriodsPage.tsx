/**
 * M-07 Dönem — the company's accounting periods (ACC-V0), newest first.
 *
 * THE RULES THAT SHAPE THIS FILE
 *  - Ö4: a proposal is opened only by ANOTHER authorised person. The caller's own proposal shows a
 *    note and no approval button (`isOwnProposal`, the server's per-caller answer); the server
 *    refuses a self-approval anyway.
 *  - Ö5: an open period is closed by one authorised act, after a confirm step.
 *  - The approval and the close are CONTROLS of this list (`hasCapability`), not routes.
 *  - The list is always the server's READ: after a settled command it is re-read; no status or
 *    version is predicted. A command sends the version the row was read at (`expectedVersion`).
 *  - One command at a time; an UNKNOWN outcome freezes every control until the same snapshot is
 *    re-sent (./usePeriodCommand.ts).
 */

import { useCallback, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchPeriods } from './api'
import { ACCOUNTING_CAPABILITY, ACCOUNTING_ROUTE, periodJournalsPath } from './capabilities'
import type { PeriodPage, PeriodView } from './contract'
import { pendingPeriodKey } from './pendingPeriodCommands'
import { newId, usePeriodCommand } from './usePeriodCommand'
import { PeriodAttemptNotice } from './PeriodAttemptNotice'
import {
  APPROVE,
  APPROVE_CONFIRM,
  APPROVE_CONFIRM_TEXT,
  APPROVED_NOTICE,
  CANCEL,
  CLOSE,
  CLOSE_CONFIRM,
  CLOSE_CONFIRM_TEXT,
  CLOSED_NOTICE,
  JOURNALS_OF_PERIOD,
  MORE,
  OWN_PROPOSAL,
  PERIODS_INTRO,
  PERIODS_TITLE,
  PERIOD_STATUS_LABEL,
  PROPOSE_LINK,
} from './accountingText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './Accounting.module.css'

const DIRTY_SCOPE = 'accounting.periods.command'

export function PeriodsPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context: one company's rows or frozen versions never show under another.
  return <Periods key={namespace ?? 'no-context'} namespace={namespace} />
}

function Periods({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const canPropose = hasCapability(shell, ACCOUNTING_CAPABILITY.periodsPropose)
  const canApprove = hasCapability(shell, ACCOUNTING_CAPABILITY.periodsApprove)
  const canClose = hasCapability(shell, ACCOUNTING_CAPABILITY.periodsClose)
  const canReadJournals = hasCapability(shell, ACCOUNTING_CAPABILITY.journalsList)
  const storeKey = pendingPeriodKey(shell.sessionEpoch, shell.activeContext?.userId ?? null, companyId, 'periods')

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'accounting-periods'],
    enabled: namespace !== null && singleCompany,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPeriods(companyId ?? '', null, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  // Later pages, appended in order; a re-read starts again from the first page.
  const [more, setMore] = useState<readonly PeriodView[]>([])
  const [nextCursor, setNextCursor] = useState<string | null | undefined>(undefined)
  const [loadingMore, setLoadingMore] = useState(false)

  const refetch = query.refetch
  const refresh = useCallback(async () => {
    setMore([])
    setNextCursor(undefined)
    await refetch()
  }, [refetch])

  const command = usePeriodCommand(storeKey, companyId ?? '', DIRTY_SCOPE, refresh)
  const [confirming, setConfirming] = useState<{ readonly periodId: string; readonly operation: 'approve' | 'close' } | null>(
    null,
  )

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<PeriodPage>

  if (!singleCompany) {
    return (
      <Frame canPropose={false}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="muhasebe dönemleri için tek şirket bağlamı" />
      </Frame>
    )
  }

  if (state.kind !== 'ready') {
    return (
      <Frame canPropose={canPropose}>
        <StateBlock state={state} subject="muhasebe dönemleri" onRetry={() => void query.refetch()} />
        <PeriodAttemptNotice attempt={command.attempt} appliedText={APPROVED_NOTICE} onResend={command.resend} />
      </Frame>
    )
  }

  const rows = [...state.data.items, ...more]
  const cursor = nextCursor === undefined ? state.data.nextCursor : nextCursor
  const frozen = command.unresolved

  const loadMore = async () => {
    if (cursor === null || loadingMore) return
    setLoadingMore(true)
    const snapshot = boundary.capture()
    const outcome = await fetchPeriods(companyId ?? '', cursor)
    setLoadingMore(false)
    if (outcome.type === 'ok-single') {
      setMore((current) => [...current, ...outcome.data.items])
      setNextCursor(outcome.data.nextCursor)
    } else if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, snapshot)
    }
  }

  const confirm = (row: PeriodView, operation: 'approve' | 'close') => {
    setConfirming(null)
    command.reset()
    command.submit({ operation, idempotencyKey: newId(), periodId: row.periodId, expectedVersion: row.version })
  }

  const appliedText =
    command.attempt.phase === 'applied' && command.attempt.snapshot.operation === 'close' ? CLOSED_NOTICE : APPROVED_NOTICE

  return (
    <Frame canPropose={canPropose}>
      <PeriodAttemptNotice attempt={command.attempt} appliedText={appliedText} onResend={command.resend} />
      {rows.length === 0 ? (
        <StateBlock state={{ kind: 'empty-no-data' }} subject="muhasebe dönemi" />
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">Başlangıç</th>
                <th scope="col">Bitiş</th>
                <th scope="col">Durum</th>
                <th scope="col">İşlem</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const approvable = canApprove && row.status === 'Proposed' && !row.isOwnProposal
                const closable = canClose && row.status === 'Open'
                const asking = confirming?.periodId === row.periodId ? confirming.operation : null
                return (
                  <tr key={row.periodId} data-period={row.periodId}>
                    <td>{row.startsOn}</td>
                    <td>{row.endsOn}</td>
                    <td>
                      {PERIOD_STATUS_LABEL[row.status]}
                      {row.status === 'Proposed' && row.isOwnProposal ? (
                        <p className={styles.help}>{OWN_PROPOSAL}</p>
                      ) : null}
                    </td>
                    <td>
                      <div className={styles.actions}>
                        {approvable && asking === null ? (
                          <button
                            type="button"
                            disabled={frozen}
                            aria-label={`${APPROVE}: ${row.startsOn} – ${row.endsOn}`}
                            onClick={() => setConfirming({ periodId: row.periodId, operation: 'approve' })}
                          >
                            {APPROVE}
                          </button>
                        ) : null}
                        {closable && asking === null ? (
                          <button
                            type="button"
                            disabled={frozen}
                            aria-label={`${CLOSE}: ${row.startsOn} – ${row.endsOn}`}
                            onClick={() => setConfirming({ periodId: row.periodId, operation: 'close' })}
                          >
                            {CLOSE}
                          </button>
                        ) : null}
                        {canReadJournals && row.status !== 'Proposed' ? (
                          <Link to={periodJournalsPath(row.periodId)}>{JOURNALS_OF_PERIOD}</Link>
                        ) : null}
                      </div>
                      {asking !== null ? (
                        <div role="group" aria-label={asking === 'approve' ? APPROVE : CLOSE} className={styles.actions}>
                          <p className={styles.help}>{asking === 'approve' ? APPROVE_CONFIRM_TEXT : CLOSE_CONFIRM_TEXT}</p>
                          <button type="button" disabled={frozen} onClick={() => confirm(row, asking)}>
                            {asking === 'approve' ? APPROVE_CONFIRM : CLOSE_CONFIRM}
                          </button>
                          <button type="button" onClick={() => setConfirming(null)}>
                            {CANCEL}
                          </button>
                        </div>
                      ) : null}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
      {cursor !== null ? (
        <div className={styles.actions}>
          <button type="button" disabled={loadingMore} onClick={() => void loadMore()}>
            {MORE}
          </button>
        </div>
      ) : null}
    </Frame>
  )
}

function Frame({ canPropose, children }: { readonly canPropose: boolean; readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="accounting-periods-title" className={styles.page}>
      <h1 id="accounting-periods-title">{PERIODS_TITLE}</h1>
      <p className={styles.help}>{PERIODS_INTRO}</p>
      {canPropose ? (
        <div className={styles.actions}>
          <Link to={ACCOUNTING_ROUTE.periodsPropose}>{PROPOSE_LINK}</Link>
        </div>
      ) : null}
      {children}
    </section>
  )
}
