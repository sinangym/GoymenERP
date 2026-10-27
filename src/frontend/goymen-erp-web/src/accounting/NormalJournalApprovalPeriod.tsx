/**
 * BLG-05 — the approval period of a normal journal draft, chosen from the company's OPEN periods.
 *
 * The approver no longer types a period id. The list is the server's own period read (`GET /accounting/periods`,
 * the M-07 read the period list screen uses), so it is gated by the same mount id (`accounting.periods.list`); without
 * it the picker says so and nothing can be approved — never a free-text id as a fallback (fail closed).
 *
 * Only OPEN periods are offered, in the server's order, each named by its date range. When exactly one loaded open
 * period holds the draft's accounting date it is preselected for convenience; the server still decides whether that
 * period may take the draft (closed meanwhile, date not covered) and answers with a stable rule code, after which the
 * caller re-reads the list and clears the choice. Later pages load on request, as on the period list.
 */

import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchPeriods } from './api'
import { NORMAL_JOURNAL_TEXT as T, approvalPeriodLabel } from './accountingText'
import { ACCOUNTING_CAPABILITY } from './capabilities'
import type { PeriodPage, PeriodView } from './contract'
import styles from './Accounting.module.css'

const PERIOD_SELECT_ID = 'normal-journal-approval-period'

const holds = (period: PeriodView, date: string) => period.startsOn <= date && date <= period.endsOn

// A later-page cursor the server no longer accepts (the list changed under it): start again from the first page.
const CURSOR_REFUSED = new Set([400, 409, 410, 422])

export function NormalJournalApprovalPeriod({
  namespace,
  companyId,
  accountingDate,
  value,
  onChange,
  disabled,
  reloadToken,
}: {
  /** The shell query namespace; first in the key so a fenced context drops this read with the rest. */
  readonly namespace: string
  readonly companyId: string
  readonly accountingDate: string
  readonly value: string
  readonly onChange: (periodId: string) => void
  readonly disabled: boolean
  /**
   * Changes when the caller needs a fresh read (a period refusal). The caller also uses it as the component `key`, so
   * the loaded later pages are dropped and the list restarts from the first page.
   */
  readonly reloadToken: number
}) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const canList = hasCapability(shell, ACCOUNTING_CAPABILITY.periodsList)
  const query = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'accounting', 'normal-journal-approval-periods', companyId, reloadToken],
    enabled: canList,
    retry: false,
    staleTime: 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPeriods(companyId, null, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  // Later pages, appended in order; a re-read starts again from the first page.
  const [more, setMore] = useState<readonly PeriodView[]>([])
  const [nextCursor, setNextCursor] = useState<string | null | undefined>(undefined)
  const [loadingMore, setLoadingMore] = useState(false)
  // A failed later page is said, never swallowed; the same button retries it.
  const [moreFailure, setMoreFailure] = useState<'failed' | 'restarted' | null>(null)

  const state = (canList
    ? (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' }))
    : { kind: 'initial-loading' }) as RemoteViewState<PeriodPage>
  const loaded = state.kind === 'ready' ? [...state.data.items, ...more] : []
  const open = loaded.filter((period) => period.status === 'Open')
  const holding = open.filter((period) => holds(period, accountingDate))
  const preselect = value === '' && holding.length === 1 ? holding[0].periodId : null

  useEffect(() => {
    if (preselect !== null) onChange(preselect)
  }, [preselect, onChange])

  if (!canList) return <p role="note" className={styles.noticeWarn}>{T.periodListUnavailable}</p>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.periodsSubject} onRetry={() => void query.refetch()} />

  const cursor = nextCursor === undefined ? state.data.nextCursor : nextCursor
  const loadMore = async () => {
    if (cursor === null || loadingMore) return
    setLoadingMore(true)
    setMoreFailure(null)
    const snapshot = boundary.capture()
    const outcome = await fetchPeriods(companyId, cursor)
    setLoadingMore(false)
    if (outcome.type === 'ok-single') {
      setMore((current) => [...current, ...outcome.data.items])
      setNextCursor(outcome.data.nextCursor)
    } else if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, snapshot)
    } else if (outcome.type === 'http' && CURSOR_REFUSED.has(outcome.status)) {
      setMore([])
      setNextCursor(undefined)
      setMoreFailure('restarted')
      void query.refetch()
    } else {
      setMoreFailure('failed')
    }
  }

  return <div className={styles.control}>
    <label htmlFor={PERIOD_SELECT_ID}>{T.period}</label>
    <select id={PERIOD_SELECT_ID} value={value} disabled={disabled || open.length === 0}
      onChange={(event) => onChange(event.target.value)}>
      <option value="">{T.choosePeriod}</option>
      {open.map((period) => <option key={period.periodId} value={period.periodId}>
        {approvalPeriodLabel(period.startsOn, period.endsOn, holds(period, accountingDate))}
      </option>)}
    </select>
    {open.length === 0 && cursor === null ? <p role="note">{T.noOpenPeriod}</p> : null}
    {moreFailure === 'failed' ? <p role="alert">{T.morePeriodsFailed}</p> : null}
    {moreFailure === 'restarted' ? <p role="status">{T.periodsRestarted}</p> : null}
    {cursor !== null
      ? <button type="button" disabled={disabled || loadingMore} onClick={() => void loadMore()}>{T.morePeriods}</button>
      : null}
  </div>
}
