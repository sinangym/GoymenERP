/**
 * L-02 Yükleme Talimatları — the committed company's loading instructions, pinned to
 * `GET /api/v1/logistics/loading-instructions?pageSize&cursor&status` (LOG-4a, #350). Newest first,
 * as the server orders.
 *
 * The columns are what `LoadingInstructionSummaryView` carries and nothing more: no customer name
 * (the summary has none), no amount, no KPI, no price. Continuation follows the contract:
 * `nextCursor = null` is the end, there is no total count, and a failed next page keeps the rows
 * already shown and offers an explicit retry of the SAME cursor — never an automatic resend.
 */

import { useCallback, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchInstructionPage } from './api'
import { INSTRUCTION_STATUS, type InstructionStatus, type InstructionSummary } from './contract'
import { LOGISTICS_CAPABILITY, LOGISTICS_ROUTE, instructionDetailRoute } from './capabilities'
import { INSTRUCTION_LIST_TEXT, INSTRUCTION_STATUS_TEXT } from './logisticsText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { formatBusinessDate, formatInstant } from './format'
import styles from './LogisticsPages.module.css'

export function InstructionListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context: rows and cursors of one company never survive into another.
  return <InstructionList key={namespace ?? 'no-context'} namespace={namespace} />
}

function InstructionList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canOpen = hasCapability(shell, LOGISTICS_CAPABILITY.instructionsDetail)
  const canCreate = hasCapability(shell, LOGISTICS_CAPABILITY.instructionsCreate)
  // The status filter is the server's (`status`); changing it restarts the list.
  const [statusFilter, setStatusFilter] = useState<InstructionStatus | null>(null)

  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'logistics-instructions', 'first', statusFilter ?? 'all'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchInstructionPage(null, companyId as string, signal, statusFilter)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const [more, setMore] = useState<readonly InstructionSummary[]>([])
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const loadingRef = useRef(false)

  const firstPage = first.data?.type === 'ok-single' ? first.data.data : null
  const nextCursor = cursor === undefined ? (firstPage?.nextCursor ?? null) : cursor

  const loadMore = useCallback(async () => {
    if (loadingRef.current || nextCursor === null || companyId === null) return
    loadingRef.current = true
    setContinuation('loading')
    const snapshot = boundary.capture()
    const outcome = await fetchInstructionPage(nextCursor, companyId, undefined, statusFilter)
    loadingRef.current = false
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    if (outcome.type === 'ok-single') {
      // A page that repeats a row already shown is not appended twice.
      setMore((rows) => {
        const seen = new Set([...(firstPage?.items ?? []), ...rows].map((row) => row.id))
        return [...rows, ...outcome.data.items.filter((row) => !seen.has(row.id))]
      })
      setCursor(outcome.data.nextCursor)
      setContinuation('idle')
      return
    }
    setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, firstPage, nextCursor, statusFilter])

  const header = (
    <header className={styles.header}>
      <h1 id="logistics-list-heading" className={styles.title}>
        {INSTRUCTION_LIST_TEXT.heading}
      </h1>
      <label htmlFor="logistics-status-filter">
        {INSTRUCTION_LIST_TEXT.filter}
        <select
          id="logistics-status-filter"
          value={statusFilter ?? ''}
          onChange={(event) => {
            const value = event.target.value
            setMore([])
            setCursor(undefined)
            setContinuation('idle')
            setStatusFilter(value === '' ? null : (value as InstructionStatus))
          }}
        >
          <option value="">{INSTRUCTION_LIST_TEXT.filterAll}</option>
          {Object.values(INSTRUCTION_STATUS).map((status) => (
            <option key={status} value={status}>
              {INSTRUCTION_STATUS_TEXT[status]}
            </option>
          ))}
        </select>
      </label>
      {canCreate ? (
        <Link className={styles.primaryLink} to={LOGISTICS_ROUTE.create}>
          {INSTRUCTION_LIST_TEXT.newInstruction}
        </Link>
      ) : null}
    </header>
  )

  if (firstPage === null) {
    const state: RemoteViewState<unknown> =
      first.data === undefined ? { kind: 'initial-loading' } : (classifyOutcome(first.data) as RemoteViewState<unknown>)
    return (
      <section className={styles.page} aria-labelledby="logistics-list-heading">
        {header}
        <StateBlock state={state} subject="yükleme talimatları" onRetry={() => void first.refetch()} />
      </section>
    )
  }

  const rows = [...firstPage.items, ...more]
  if (rows.length === 0) {
    return (
      <section className={styles.page} aria-labelledby="logistics-list-heading">
        {header}
        <StateBlock state={{ kind: statusFilter === null ? 'empty-no-data' : 'empty-filtered' }} subject="yükleme talimatları" />
      </section>
    )
  }

  return (
    <section className={styles.page} aria-labelledby="logistics-list-heading">
      {header}
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.caption}>{INSTRUCTION_LIST_TEXT.caption}</caption>
          <thead>
            <tr>
              <th scope="col">{INSTRUCTION_LIST_TEXT.instruction}</th>
              <th scope="col">{INSTRUCTION_LIST_TEXT.status}</th>
              <th scope="col">{INSTRUCTION_LIST_TEXT.effectiveOn}</th>
              <th scope="col">{INSTRUCTION_LIST_TEXT.customer}</th>
              <th scope="col" className={styles.numeric}>{INSTRUCTION_LIST_TEXT.lineCount}</th>
              <th scope="col">{INSTRUCTION_LIST_TEXT.createdAt}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id} data-testid={`logistics-row-${row.id}`}>
                <th scope="row" className={styles.identifier}>
                  {canOpen ? <Link to={instructionDetailRoute(row.id)}>{row.id}</Link> : row.id}
                </th>
                <td>
                  <span className={styles.badge} data-testid={`logistics-row-status-${row.id}`}>
                    {INSTRUCTION_STATUS_TEXT[row.status]}
                  </span>
                </td>
                <td>{formatBusinessDate(row.effectiveOn)}</td>
                <td className={styles.identifier}>{row.customerPartyId}</td>
                <td className={styles.numeric}>{row.lineCount}</td>
                <td>{formatInstant(row.createdAtUtc)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className={styles.continuation}>
        {typeof continuation === 'object' ? (
          <div className={styles.inlineError}>
            <p>{INSTRUCTION_LIST_TEXT.nextPageFailed}</p>
            <StateBlock state={continuation} subject="sonraki sayfa" onRetry={() => void loadMore()} />
          </div>
        ) : nextCursor === null ? (
          <p className={styles.muted}>{INSTRUCTION_LIST_TEXT.end}</p>
        ) : (
          <button
            type="button"
            className={styles.secondaryButton}
            onClick={() => void loadMore()}
            disabled={continuation === 'loading'}
            aria-busy={continuation === 'loading'}
          >
            {continuation === 'loading' ? INSTRUCTION_LIST_TEXT.loadingMore : INSTRUCTION_LIST_TEXT.loadMore}
          </button>
        )}
      </div>
    </section>
  )
}
