/**
 * MFG-02 — the company's production orders, pinned to `GET /api/v1/manufacturing/orders`
 * (MFG-ORD). Newest first, as the server orders them.
 *
 * THE RULES THAT SHAPE THIS FILE
 *
 *  - The columns are exactly what `OrderSummaryViewV1` carries: no balance, no difference and no
 *    KPI. A balance belongs to one order's own read, roll by roll, and is not summed here.
 *  - There is no roll or order LABEL yet: the label read is Inventory's (ÜRÜN / T15). The id is
 *    shown as a technical reference, never as the final presentation, and the note says so.
 *  - Continuation follows the contract: `nextCursor = null` is the end, there is no total count,
 *    the cursor is the server's opaque value sent back unchanged, and a failed next page keeps
 *    the rows already shown and offers an explicit retry of the SAME cursor.
 *  - Opening one order needs the detail capability; without it the row shows no link, and the
 *    server enforces either way.
 */

import { useCallback, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchOrders } from './api'
import type { OrderStatus, OrderSummary } from './contract'
import { MANUFACTURING_CAPABILITY, orderDetailPath, planCreatePath, planListPath } from './capabilities'
import { ORDER_LIST_TEXT, ORDER_STATUS_LABEL, TARGET_BASIS_LABEL } from './manufacturingText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './OrderListPage.module.css'

const ORDER_STATUSES: readonly OrderStatus[] = ['Open', 'Closed']

/** Presentation only: an unreadable instant is shown as it came, never rewritten. */
function formatInstant(value: string): string {
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime())
    ? value
    : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

/** The last 8 characters of a technical id, labelled as a reference at the call site. */
function reference(id: string): string {
  return `…${id.slice(-8)}`
}

export function OrderListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context: one company's page must never be shown under another context.
  return <OrderList key={namespace ?? 'no-context'} namespace={namespace} />
}

function OrderList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompanyContext = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const canOpen = hasCapability(shell, MANUFACTURING_CAPABILITY.orderDetail)
  const canCreatePlan = hasCapability(shell, MANUFACTURING_CAPABILITY.planCreate)
  // MFG-PLAN-LIST: the saved plans, only where that list is published to this actor.
  const canOpenPlanList = hasCapability(shell, MANUFACTURING_CAPABILITY.planList)
  const [statusFilter, setStatusFilter] = useState<OrderStatus | null>(null)
  const [more, setMore] = useState<readonly OrderSummary[]>([])
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const loadingRef = useRef(false)

  const first = useQuery({
    queryKey: [
      namespace ?? 'no-context',
      shell.routeEntryGeneration,
      'manufacturing-orders',
      'first',
      statusFilter ?? 'all',
    ],
    enabled: namespace !== null && singleCompanyContext,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchOrders(companyId ?? '', { status: statusFilter }, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const firstPage = first.data?.type === 'ok-single' ? first.data.data : null
  const nextCursor = cursor === undefined ? (firstPage?.nextCursor ?? null) : cursor

  const loadMore = useCallback(async () => {
    if (loadingRef.current || nextCursor === null || companyId === null) return
    loadingRef.current = true
    setContinuation('loading')
    const snapshot = boundary.capture()
    const outcome = await fetchOrders(companyId, { cursor: nextCursor, status: statusFilter })
    loadingRef.current = false
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, snapshot)
    }
    if (outcome.type === 'ok-single') {
      // A page that repeats a row already shown is not appended twice.
      setMore((rows) => {
        const seen = new Set([...(firstPage?.orders ?? []), ...rows].map((row) => row.orderId))
        return [...rows, ...outcome.data.orders.filter((row) => !seen.has(row.orderId))]
      })
      setCursor(outcome.data.nextCursor)
      setContinuation('idle')
      return
    }
    setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, firstPage, nextCursor, statusFilter])

  const header = (
    <header className={styles.header}>
      <h1 id="mfg-order-list-heading" className={styles.title}>
        {ORDER_LIST_TEXT.heading}
      </h1>
      <div className={styles.headerActions}>
        <label className={styles.filter} htmlFor="mfg-status-filter">
          {ORDER_LIST_TEXT.filter}
          <select
            id="mfg-status-filter"
            value={statusFilter ?? ''}
            onChange={(event) => {
              const value = event.target.value
              setMore([])
              setCursor(undefined)
              setContinuation('idle')
              setStatusFilter(value === '' ? null : (value as OrderStatus))
            }}
          >
            <option value="">{ORDER_LIST_TEXT.filterAll}</option>
            {ORDER_STATUSES.map((status) => (
              <option key={status} value={status}>
                {ORDER_STATUS_LABEL[status]}
              </option>
            ))}
          </select>
        </label>
        {canOpenPlanList ? <Link to={planListPath()}>Üretim planları</Link> : null}
        {canCreatePlan ? (
          <Link className={styles.primaryLink} to={planCreatePath()}>
            Yeni üretim planı
          </Link>
        ) : null}
      </div>
    </header>
  )

  if (!singleCompanyContext) {
    return (
      <section className={styles.page} aria-labelledby="mfg-order-list-heading">
        {header}
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="üretim emirleri için tek şirket bağlamı" />
      </section>
    )
  }

  if (firstPage === null) {
    const state: RemoteViewState<unknown> =
      first.data === undefined
        ? { kind: 'initial-loading' }
        : (classifyOutcome(first.data) as RemoteViewState<unknown>)
    return (
      <section className={styles.page} aria-labelledby="mfg-order-list-heading">
        {header}
        <StateBlock state={state} subject="üretim emirleri" onRetry={() => void first.refetch()} />
      </section>
    )
  }

  const rows = [...firstPage.orders, ...more]
  if (rows.length === 0) {
    return (
      <section className={styles.page} aria-labelledby="mfg-order-list-heading">
        {header}
        <StateBlock state={{ kind: 'empty-no-data' }} subject="üretim emirleri" />
      </section>
    )
  }

  return (
    <section className={styles.page} aria-labelledby="mfg-order-list-heading" data-testid="order-list">
      {header}
      <p className={styles.help}>{ORDER_LIST_TEXT.labelNote}</p>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.caption}>{ORDER_LIST_TEXT.caption}</caption>
          <thead>
            <tr>
              <th scope="col">{ORDER_LIST_TEXT.order}</th>
              <th scope="col">{ORDER_LIST_TEXT.createdAt}</th>
              <th scope="col">{ORDER_LIST_TEXT.status}</th>
              <th scope="col">{ORDER_LIST_TEXT.process}</th>
              <th scope="col" className={styles.numeric}>
                {ORDER_LIST_TEXT.rollCount}
              </th>
              <th scope="col" className={styles.numeric}>
                {ORDER_LIST_TEXT.plannedInput}
              </th>
              <th scope="col">{ORDER_LIST_TEXT.target}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.orderId} data-order-row={row.orderId}>
                <th scope="row" className={styles.mono}>
                  {canOpen ? (
                    <Link to={orderDetailPath(row.orderId)} aria-label={`${ORDER_LIST_TEXT.open} ${reference(row.orderId)}`}>
                      {reference(row.orderId)}
                    </Link>
                  ) : (
                    reference(row.orderId)
                  )}
                </th>
                <td>{formatInstant(row.createdAtUtc)}</td>
                <td>
                  <span
                    className={row.status === 'Open' ? styles.badgeOpen : styles.badgeClosed}
                    data-status={row.status}
                  >
                    {ORDER_STATUS_LABEL[row.status]}
                  </span>
                  {row.closedAtUtc !== null ? (
                    <div className={styles.muted}>{formatInstant(row.closedAtUtc)}</div>
                  ) : null}
                </td>
                <td>{row.process}</td>
                <td className={styles.numeric}>{row.plannedRollCount}</td>
                <td className={styles.numeric} data-testid="planned-input-kg">
                  {row.plannedInputKg}
                </td>
                <td>
                  {row.target === null
                    ? ORDER_LIST_TEXT.targetNone
                    : `${TARGET_BASIS_LABEL[row.target.basis] ?? row.target.basis}: ${row.target.kg} kg`}
                </td>
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
