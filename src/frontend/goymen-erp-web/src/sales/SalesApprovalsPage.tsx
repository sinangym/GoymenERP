/**
 * UI-51 / S-06 "Satış Onayları" (master report §27.2, SATIS_HAZIRLIK_PLANI_v1 UI-51: "Work kutusunda — Satış filtresi"):
 * the Sales approval queue, read from the Work approval read the D-05 inbox already uses,
 *   GET /api/v1/work/approvals?kind=sales.order-draft.v1&cursor=
 * under the EXISTING approve control's id (`sales.orders.approve`) — no new capability, no nav entry. Read-only: each
 * draft is decided on S-04 with Sales' own authority and separation of duties; `isOwnDraft` is shown as the server's fact.
 *
 * Continuation follows the Work contract: `nextCursor = null` is the end; a transient failure of the next page keeps the
 * rows and retries the SAME cursor on request; a terminal one closes the list and offers a fresh start.
 *
 * The other Sales approvals (price floor, discount limit, remainder change, over-delivery, invoice, expired quote,
 * request, risk exception) have no pending-queue read on main: they are listed as a labelled unavailable section.
 *
 * SAT-D243: the same rows are also listed in the Work inbox (`/onaylarim?kaynak=satis`) — one record, two views. That
 * inbox links each row here as `?onay={draftId}`; the matching row, if it is on the page the server returned, is marked
 * as the current one. The address only selects among verified rows: it never adds or reads a record.
 */

import { useCallback, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { fetchApprovalPage } from '../work/approvalInboxApi'
import { SALES_ORDER_DRAFT_KIND, type SalesApprovalItem } from '../work/approvalInboxContract'
import { SALES_APPROVAL_PARAM, SALES_CAPABILITY, salesDetailPath, salesPriceControlPath } from './capabilities'
import { SALES_APPROVALS_TEXT as T } from './approvalsText'
import { formatInstant } from './format'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

/** A next-page failure that may pass by itself: the rows stay, the same cursor may be asked again. */
const TRANSIENT = new Set(['offline', 'network-error', 'service-unavailable'])

export function SalesApprovalsPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context: rows and cursors of one company never survive into another.
  return <ApprovalQueue key={namespace ?? 'no-context'} namespace={namespace} />
}

function ApprovalQueue({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canOpen = hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const [restart, setRestart] = useState(0)
  const [searchParams] = useSearchParams()
  const focused = searchParams.get(SALES_APPROVAL_PARAM)?.toLowerCase() ?? null

  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-approvals', 'first', restart],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchApprovalPage(SALES_ORDER_DRAFT_KIND, null, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const [more, setMore] = useState<readonly SalesApprovalItem[]>([])
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
    const outcome = await fetchApprovalPage(SALES_ORDER_DRAFT_KIND, nextCursor, companyId)
    loadingRef.current = false
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    if (outcome.type === 'ok-single') {
      // A page that repeats a record already shown is not appended twice.
      setMore((rows) => {
        const seen = new Set([...(firstPage?.items ?? []), ...rows].map((row) => row.approvalId.toLowerCase()))
        return [...rows, ...outcome.data.items.filter((row) => !seen.has(row.approvalId.toLowerCase()))]
      })
      setCursor(outcome.data.nextCursor)
      setContinuation('idle')
      return
    }
    setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, firstPage, nextCursor])

  const startOver = () => {
    setMore([])
    setCursor(undefined)
    setContinuation('idle')
    setRestart((value) => value + 1)
  }

  const header = (
    <header className={styles.header}>
      <div>
        <h1 id="sales-approvals-heading" className={styles.title}>{T.heading}</h1>
        <p className={styles.muted}>{T.note}</p>
      </div>
    </header>
  )

  const unavailable = (
    <section className={styles.section} aria-labelledby="sales-approvals-unavailable-heading" data-testid="sales-approvals-unavailable">
      <h2 id="sales-approvals-unavailable-heading">{T.unavailable.heading}</h2>
      <p className={styles.muted}>{T.unavailable.note}</p>
      <ul>
        {T.unavailable.items.map((item) => <li key={item}>{item}</li>)}
      </ul>
    </section>
  )

  const terminal = typeof continuation === 'object' && !TRANSIENT.has(continuation.kind)

  let body: ReactNode
  if (firstPage === null) {
    const state: RemoteViewState<unknown> =
      first.data === undefined ? { kind: 'initial-loading' } : (classifyOutcome(first.data) as RemoteViewState<unknown>)
    body = <StateBlock state={state} subject={T.stateSubject} onRetry={() => void first.refetch()} />
  } else if (terminal) {
    // The queue moved on in a way a retry of the same cursor cannot mend: the rows are no longer shown as current.
    body = (
      <div className={styles.inlineError} data-testid="sales-approvals-terminal">
        <p>{T.terminal}</p>
        <StateBlock state={continuation} subject={T.nextPageSubject} />
        <Button type="button" variant="secondary" onClick={startOver}>{T.restart}</Button>
      </div>
    )
  } else {
    const rows = [...firstPage.items, ...more]
    body = rows.length === 0 ? (
      <StateBlock state={{ kind: 'empty-no-data' }} subject={T.stateSubject} />
    ) : (
      <>
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="sales-approvals-table">
            <caption className={styles.caption}>{T.orders.caption}</caption>
            <thead>
              <tr>
                <th scope="col">{T.orders.documentNo}</th>
                <th scope="col">{T.orders.revision}</th>
                <th scope="col">{T.orders.submittedBy}</th>
                <th scope="col">{T.orders.submittedAt}</th>
                <th scope="col">{T.orders.ownDraft}</th>
                {canOpen ? <th scope="col">{T.orders.actions}</th> : null}
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.approvalId} data-approval-id={row.approvalId}
                  aria-current={row.approvalId.toLowerCase() === focused ? 'true' : undefined}
                  className={row.approvalId.toLowerCase() === focused ? styles.rowCurrent : undefined}>
                  <th scope="row">{row.documentNo}</th>
                  <td>{row.revision}</td>
                  <td>{row.submittedBy}</td>
                  <td>{formatInstant(row.submittedAtUtc)}</td>
                  <td data-testid={`sales-approvals-own-${row.approvalId}`}>{row.isOwnDraft ? T.orders.ownDraftYes : T.orders.ownDraftNo}</td>
                  {canOpen ? (
                    <td>
                      <span className={styles.actions}>
                        <Link to={salesDetailPath(row.approvalId)}>{T.orders.openOrder}</Link>
                        <Link to={salesPriceControlPath(row.approvalId)}>{T.orders.openControl}</Link>
                      </span>
                    </td>
                  ) : null}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div className={styles.continuation}>
          {typeof continuation === 'object' ? (
            <div className={styles.inlineError} data-testid="sales-approvals-next-failed">
              <p>{T.nextPageFailed}</p>
              <StateBlock state={continuation} subject={T.nextPageSubject} onRetry={() => void loadMore()} />
            </div>
          ) : nextCursor === null ? (
            <p className={styles.muted}>{T.end}</p>
          ) : (
            <Button
              type="button"
              variant="secondary"
              onClick={() => void loadMore()}
              disabled={continuation === 'loading'}
              aria-busy={continuation === 'loading'}
            >
              {continuation === 'loading' ? T.loadingMore : T.loadMore}
            </Button>
          )}
        </div>
      </>
    )
  }

  return (
    <section className={styles.page} aria-labelledby="sales-approvals-heading">
      {header}
      <section className={styles.section} aria-label={T.orders.heading} data-testid="sales-approvals-orders">
        <h2>{T.orders.heading}</h2>
        {body}
      </section>
      {unavailable}
    </section>
  )
}
