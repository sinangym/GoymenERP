/**
 * MFG-PLAN-LIST — the company's saved production plans, pinned to `GET /api/v1/manufacturing/plans`.
 * Newest first, as the server orders them.
 *
 * WHY IT EXISTS. A saved plan could be reopened only through its URL, so a Draft whose screen was closed was lost to
 * its user. From here every row opens the existing plan detail, where the existing approve and order-create controls
 * continue the journey; the orders that result are in the order list.
 *
 * THE RULES THAT SHAPE THIS FILE (the order list's, MFG-02)
 *
 *  - The columns are exactly what `PlanSummaryViewV1` carries: no engine answer, no spec, no roll label and no
 *    order. One approved plan may open more than one order (#66 5728934213 is open), so no "converted" state is
 *    shown or inferred.
 *  - Continuation follows the contract: `nextCursor = null` is the end, there is no total count, the cursor is the
 *    server's opaque value sent back unchanged, and a failed next page keeps the rows already shown and offers an
 *    explicit retry of the SAME cursor.
 *  - Remounted per company context: one company's rows are never shown under another context, and an answer that
 *    arrives after the context changed is dropped, never written into the new context's screen.
 *  - Opening one plan needs the plan detail capability; without it the row shows no link, and the server enforces
 *    either way.
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchPlans } from './api'
import type { PlanStatus } from './contract'
import { PLAN_STATUSES, type PlanSummary } from './planListContract'
import { MANUFACTURING_CAPABILITY, orderListPath, planCreatePath, planDetailPath } from './capabilities'
import { TARGET_BASIS_LABEL } from './manufacturingText'
import { PLAN_LIST_TEXT, PLAN_STATUS_LABEL } from './planListText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './PlanListPage.module.css'

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

export function PlanListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context: one company's page must never be shown under another context.
  return <PlanList key={namespace ?? 'no-context'} namespace={namespace} />
}

function PlanList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompanyContext = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const canOpen = hasCapability(shell, MANUFACTURING_CAPABILITY.planDetail)
  const canCreatePlan = hasCapability(shell, MANUFACTURING_CAPABILITY.planCreate)
  const canOpenOrders = hasCapability(shell, MANUFACTURING_CAPABILITY.orderList)
  const [statusFilter, setStatusFilter] = useState<PlanStatus | null>(null)
  const [more, setMore] = useState<readonly PlanSummary[]>([])
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const loadingRef = useRef(false)
  // The continuation request of THIS mount and filter. A context switch unmounts this instance (the key above) and a
  // filter change starts a new first page; either aborts the request in flight, so its late answer is never applied.
  const continuationRef = useRef<AbortController | null>(null)

  useEffect(() => () => continuationRef.current?.abort(), [])

  const first = useQuery({
    queryKey: [
      namespace ?? 'no-context',
      shell.routeEntryGeneration,
      'manufacturing-plans',
      'first',
      statusFilter ?? 'all',
    ],
    enabled: namespace !== null && singleCompanyContext,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPlans(companyId ?? '', { status: statusFilter }, signal)
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
    const controller = new AbortController()
    continuationRef.current = controller
    const snapshot = boundary.capture()
    const outcome = await fetchPlans(companyId, { cursor: nextCursor, status: statusFilter }, controller.signal)
    loadingRef.current = false
    if (controller.signal.aborted || outcome.type === 'fence-stale') return
    continuationRef.current = null
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, snapshot)
    }
    if (outcome.type === 'ok-single') {
      // A page that repeats a row already shown is not appended twice.
      setMore((rows) => {
        const seen = new Set([...(firstPage?.plans ?? []), ...rows].map((row) => row.planId))
        return [...rows, ...outcome.data.plans.filter((row) => !seen.has(row.planId))]
      })
      setCursor(outcome.data.nextCursor)
      setContinuation('idle')
      return
    }
    setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, firstPage, nextCursor, statusFilter])

  const header = (
    <header className={styles.header}>
      <h1 id="mfg-plan-list-heading" className={styles.title}>
        {PLAN_LIST_TEXT.heading}
      </h1>
      <div className={styles.headerActions}>
        <label className={styles.filter} htmlFor="mfg-plan-status-filter">
          {PLAN_LIST_TEXT.filter}
          <select
            id="mfg-plan-status-filter"
            value={statusFilter ?? ''}
            onChange={(event) => {
              const value = event.target.value
              continuationRef.current?.abort()
              continuationRef.current = null
              loadingRef.current = false
              setMore([])
              setCursor(undefined)
              setContinuation('idle')
              setStatusFilter(value === '' ? null : (value as PlanStatus))
            }}
          >
            <option value="">{PLAN_LIST_TEXT.filterAll}</option>
            {PLAN_STATUSES.map((status) => (
              <option key={status} value={status}>
                {PLAN_STATUS_LABEL[status]}
              </option>
            ))}
          </select>
        </label>
        {canOpenOrders ? (
          <Link className={styles.secondaryLink} to={orderListPath()}>
            {PLAN_LIST_TEXT.orders}
          </Link>
        ) : null}
        {canCreatePlan ? (
          <Link className={styles.primaryLink} to={planCreatePath()}>
            {PLAN_LIST_TEXT.newPlan}
          </Link>
        ) : null}
      </div>
    </header>
  )

  if (!singleCompanyContext) {
    return (
      <section className={styles.page} aria-labelledby="mfg-plan-list-heading">
        {header}
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={PLAN_LIST_TEXT.contextSubject} />
      </section>
    )
  }

  if (firstPage === null) {
    const state: RemoteViewState<unknown> =
      first.data === undefined
        ? { kind: 'initial-loading' }
        : (classifyOutcome(first.data) as RemoteViewState<unknown>)
    return (
      <section className={styles.page} aria-labelledby="mfg-plan-list-heading">
        {header}
        <StateBlock state={state} subject={PLAN_LIST_TEXT.subject} onRetry={() => void first.refetch()} />
      </section>
    )
  }

  const rows = [...firstPage.plans, ...more]
  if (rows.length === 0) {
    return (
      <section className={styles.page} aria-labelledby="mfg-plan-list-heading">
        {header}
        <StateBlock state={{ kind: 'empty-no-data' }} subject={PLAN_LIST_TEXT.subject} />
      </section>
    )
  }

  return (
    <section className={styles.page} aria-labelledby="mfg-plan-list-heading" data-testid="plan-list">
      {header}
      <p className={styles.help}>{PLAN_LIST_TEXT.labelNote}</p>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.caption}>{PLAN_LIST_TEXT.caption}</caption>
          <thead>
            <tr>
              <th scope="col">{PLAN_LIST_TEXT.plan}</th>
              <th scope="col">{PLAN_LIST_TEXT.createdAt}</th>
              <th scope="col">{PLAN_LIST_TEXT.status}</th>
              <th scope="col">{PLAN_LIST_TEXT.process}</th>
              <th scope="col" className={styles.numeric}>
                {PLAN_LIST_TEXT.rollCount}
              </th>
              <th scope="col" className={styles.numeric}>
                {PLAN_LIST_TEXT.plannedInput}
              </th>
              <th scope="col">{PLAN_LIST_TEXT.target}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.planId} data-plan-row={row.planId}>
                <th scope="row" className={styles.mono}>
                  {canOpen ? (
                    <Link to={planDetailPath(row.planId)} aria-label={`${PLAN_LIST_TEXT.open} ${reference(row.planId)}`}>
                      {reference(row.planId)}
                    </Link>
                  ) : (
                    reference(row.planId)
                  )}
                </th>
                <td>{formatInstant(row.createdAtUtc)}</td>
                <td>
                  <span
                    className={row.status === 'Draft' ? styles.badgeDraft : styles.badgeApproved}
                    data-status={row.status}
                  >
                    {PLAN_STATUS_LABEL[row.status]}
                  </span>
                  {row.approvedAtUtc !== null ? (
                    <div className={styles.muted}>
                      {PLAN_LIST_TEXT.approvedAt}: {formatInstant(row.approvedAtUtc)}
                    </div>
                  ) : null}
                </td>
                <td>{row.process}</td>
                <td className={styles.numeric}>{row.plannedRollCount}</td>
                <td className={styles.numeric} data-testid="plan-planned-input-kg">
                  {row.plannedInputKg}
                </td>
                <td>
                  {row.target === null
                    ? PLAN_LIST_TEXT.targetNone
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
            <p>{PLAN_LIST_TEXT.nextPageFailed}</p>
            <StateBlock state={continuation} subject="sonraki sayfa" onRetry={() => void loadMore()} />
          </div>
        ) : nextCursor === null ? (
          <p className={styles.muted}>{PLAN_LIST_TEXT.end}</p>
        ) : (
          <button
            type="button"
            className={styles.secondaryButton}
            onClick={() => void loadMore()}
            disabled={continuation === 'loading'}
            aria-busy={continuation === 'loading'}
          >
            {continuation === 'loading' ? PLAN_LIST_TEXT.loadingMore : PLAN_LIST_TEXT.loadMore}
          </button>
        )}
      </div>
    </section>
  )
}
