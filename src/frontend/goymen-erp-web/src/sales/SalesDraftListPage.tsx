/**
 * S-02 — sales order drafts of the committed company, pinned to
 * `GET /api/v1/sales/drafts?pageSize&cursor` (#257). Newest first, as the server orders.
 *
 * The columns are what `SalesOrderDraftSummaryView` carries and nothing more: no
 * amount, no KPI, no party name (the summary has none), no derived "overall" status.
 * Continuation follows the contract: `nextCursor = null` is the end, there is no
 * total count, and a failed next page keeps the rows already shown and offers an
 * explicit retry of the SAME cursor — never an automatic resend.
 */

import { useCallback, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchDraftPage } from './api'
import type { DraftSummary } from './contract'
import { SALES_CAPABILITY, SALES_ROUTE, salesDetailPath } from './capabilities'
import { ORDER_DETAIL_TEXT, ORDER_LIST_TEXT } from './salesText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { formatBusinessDate, formatInstant } from './format'
import styles from './SalesPages.module.css'

export function SalesDraftListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context: rows and cursors of one company never survive into another.
  return <DraftList key={namespace ?? 'no-context'} namespace={namespace} />
}

function DraftList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canOpen = hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const canCreate = hasCapability(shell, SALES_CAPABILITY.draftsCreate)

  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-drafts', 'first'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftPage(null, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const [more, setMore] = useState<readonly DraftSummary[]>([])
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
    const outcome = await fetchDraftPage(nextCursor, companyId)
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
  }, [boundary, companyId, firstPage, nextCursor])

  const header = (
    <header className={styles.header}>
      <h1 id="sales-list-heading" className={styles.title}>
        {ORDER_LIST_TEXT.heading}
      </h1>
      {canCreate ? (
        <Link className={styles.primaryLink} to={SALES_ROUTE.create}>
          {ORDER_LIST_TEXT.newDraft}
        </Link>
      ) : null}
    </header>
  )

  if (firstPage === null) {
    const state: RemoteViewState<unknown> =
      first.data === undefined ? { kind: 'initial-loading' } : (classifyOutcome(first.data) as RemoteViewState<unknown>)
    return (
      <section className={styles.page} aria-labelledby="sales-list-heading">
        {header}
        <StateBlock state={state} subject="sipariş taslakları" onRetry={() => void first.refetch()} />
      </section>
    )
  }

  const rows = [...firstPage.items, ...more]
  if (rows.length === 0) {
    return (
      <section className={styles.page} aria-labelledby="sales-list-heading">
        {header}
        <StateBlock state={{ kind: 'empty-no-data' }} subject="sipariş taslakları" />
      </section>
    )
  }

  return (
    <section className={styles.page} aria-labelledby="sales-list-heading">
      {header}
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.caption}>{ORDER_LIST_TEXT.caption}</caption>
          <thead>
            <tr>
              <th scope="col">{ORDER_LIST_TEXT.documentNo}</th>
              <th scope="col">{ORDER_LIST_TEXT.businessDate}</th>
              <th scope="col">{ORDER_LIST_TEXT.state}</th>
              <th scope="col">{ORDER_LIST_TEXT.revision}</th>
              <th scope="col" className={styles.numeric}>{ORDER_LIST_TEXT.lineCount}</th>
              <th scope="col">{ORDER_LIST_TEXT.createdAt}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.id}>
                <th scope="row">
                  {canOpen ? <Link to={salesDetailPath(row.id)}>{row.documentNo}</Link> : row.documentNo}
                </th>
                <td>{formatBusinessDate(row.businessDate)}</td>
                <td>
                  <span className={styles.badge}>{ORDER_DETAIL_TEXT.stateDraft}</span>
                </td>
                <td>{row.revision}</td>
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
            <p>{ORDER_LIST_TEXT.nextPageFailed}</p>
            <StateBlock state={continuation} subject="sonraki sayfa" onRetry={() => void loadMore()} />
          </div>
        ) : nextCursor === null ? (
          <p className={styles.muted}>{ORDER_LIST_TEXT.end}</p>
        ) : (
          <button
            type="button"
            className={styles.secondaryButton}
            onClick={() => void loadMore()}
            disabled={continuation === 'loading'}
            aria-busy={continuation === 'loading'}
          >
            {continuation === 'loading' ? ORDER_LIST_TEXT.loadingMore : ORDER_LIST_TEXT.loadMore}
          </button>
        )}
      </div>
    </section>
  )
}
