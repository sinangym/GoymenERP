/**
 * D-05 "Onaylarım" — the Finance pilot of the approval inbox: the native Finance approvals waiting for the signed-in
 * actor in the committed company, read through the Work read (#639), each opening Finance's own approval screen (#640
 * reopens it from `?approvalId=`).
 *
 * Read only. There is no approve button here and no approval rule: who sees a record (Finance's approve authority,
 * the company, never one's own upload), what may be approved and how, all stay in Finance. Mounted behind that same
 * native Finance capability; no shared permission is introduced. The page says plainly that only Finance is connected.
 */

import { useCallback, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchApprovalDetail, fetchApprovalPage } from './approvalInboxApi'
import type { ApprovalItem } from './approvalInboxContract'
import { APPROVAL_KIND_TEXT, APPROVALS_TEXT as T, WORK_ROUTE, approvalDetailRoute, formatInstant, nativeApprovalPath } from './workText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './Work.module.css'

function useBoundCompany() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  return { shell, namespace, companyId }
}

function PilotScope() {
  return <div className={styles.scope}>
    <p>{T.pilotScope}</p>
    <p>{T.nativeOnly}</p>
  </div>
}

export function ApprovalInboxPage() {
  const { namespace } = useBoundCompany()
  // Remounted per context: rows and cursors of one company or actor never survive into another.
  return <ApprovalInbox key={namespace ?? 'no-context'} />
}

function ApprovalInbox() {
  const { shell, namespace, companyId } = useBoundCompany()
  const boundary = useAuthBoundary()
  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'work-approvals', 'first'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchApprovalPage(null, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const [more, setMore] = useState<readonly ApprovalItem[]>([])
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
    const outcome = await fetchApprovalPage(nextCursor, companyId)
    loadingRef.current = false
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    if (outcome.type === 'ok-single') {
      setMore((rows) => {
        const seen = new Set([...(firstPage?.items ?? []), ...rows].map((row) => row.approvalId))
        return [...rows, ...outcome.data.items.filter((row) => !seen.has(row.approvalId))]
      })
      setCursor(outcome.data.nextCursor)
      setContinuation('idle')
      return
    }
    setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, firstPage, nextCursor])

  const header = <header className={styles.header}>
    <h1 id="work-approvals-heading">{T.heading}</h1>
    <PilotScope />
  </header>

  if (firstPage === null) {
    const state: RemoteViewState<unknown> = first.data === undefined
      ? { kind: 'initial-loading' }
      : classifyOutcome(first.data) as RemoteViewState<unknown>
    return <section className={styles.page} aria-labelledby="work-approvals-heading">
      {header}
      <StateBlock state={state} subject={T.subject} onRetry={() => void first.refetch()} />
    </section>
  }

  const rows = [...firstPage.items, ...more]
  return <section className={styles.page} aria-labelledby="work-approvals-heading">
    {header}
    {rows.length === 0
      ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} />
      : <div className={styles.tableWrap} tabIndex={0} role="region" aria-label={T.caption}>
        <table className={styles.table} data-testid="work-approvals-table">
          <caption className={styles.caption}>{T.caption}</caption>
          <thead><tr>
            <th scope="col">{T.kind}</th>
            <th scope="col">{T.submittedBy}</th>
            <th scope="col">{T.submittedAt}</th>
            <th scope="col" className={styles.numeric}>{T.itemCount}</th>
            <th scope="col">{T.record}</th>
          </tr></thead>
          <tbody>{rows.map((row) => <tr key={row.approvalId} data-approval-id={row.approvalId}>
            <td>{APPROVAL_KIND_TEXT[row.kind]}</td>
            <td>{row.submittedBy}</td>
            <td>{formatInstant(row.submittedAtUtc)}</td>
            <td className={styles.numeric}>{row.itemCount}</td>
            <td className={styles.actions}>
              <Link to={approvalDetailRoute(row.kind, row.approvalId)}>{T.openDetail}</Link>
              <Link to={nativeApprovalPath(row)}>{T.openNative}</Link>
            </td>
          </tr>)}</tbody>
        </table>
      </div>}
    {rows.length === 0 ? null : <div className={styles.continuation}>
      {typeof continuation === 'object'
        ? <div role="alert">
          <p>{T.nextPageFailed}</p>
          <StateBlock state={continuation} subject={T.subject} onRetry={() => void loadMore()} />
        </div>
        : nextCursor === null
          ? <p className={styles.muted}>{T.end}</p>
          : <button type="button" onClick={() => void loadMore()} disabled={continuation === 'loading'}
            aria-busy={continuation === 'loading'}>
            {continuation === 'loading' ? T.loadingMore : T.loadMore}
          </button>}
    </div>}
  </section>
}

export function ApprovalInboxDetailPage() {
  const { namespace } = useBoundCompany()
  const { kind, approvalId } = useParams()
  return <ApprovalDetail key={`${namespace ?? 'no-context'}:${kind ?? ''}:${approvalId ?? ''}`}
    kind={kind ?? ''} approvalId={approvalId ?? ''} />
}

function ApprovalDetail({ kind, approvalId }: { readonly kind: string; readonly approvalId: string }) {
  const { shell, namespace, companyId } = useBoundCompany()
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'work-approval', kind, approvalId],
    enabled: namespace !== null && companyId !== null && kind.length > 0 && approvalId.length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchApprovalDetail(kind, approvalId, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const backLink = <Link className={styles.backLink} to={WORK_ROUTE.approvals}>← {T.backToList}</Link>
  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single') {
    return <section className={styles.page} aria-label={T.detailSubject}>
      {backLink}
      <StateBlock state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
        subject={T.detailSubject} onRetry={() => void query.refetch()} />
    </section>
  }
  const { item } = outcome.data
  return <article className={styles.page} aria-labelledby="work-approval-heading">
    {backLink}
    <header className={styles.header}>
      <h1 id="work-approval-heading">{T.detailHeading}</h1>
      <PilotScope />
    </header>
    <dl className={styles.facts} data-testid="work-approval-facts">
      <div><dt>{T.kind}</dt><dd>{APPROVAL_KIND_TEXT[item.kind]}</dd></div>
      <div><dt>{T.status}</dt><dd>{T.statusPending}</dd></div>
      <div><dt>{T.submittedBy}</dt><dd>{item.submittedBy}</dd></div>
      <div><dt>{T.submittedAt}</dt><dd>{formatInstant(item.submittedAtUtc)}</dd></div>
      <div><dt>{T.itemCount}</dt><dd>{item.itemCount}</dd></div>
      <div><dt>{T.templateVersion}</dt><dd>{item.templateVersion}</dd></div>
      <div><dt>{T.batchVersion}</dt><dd>{item.batchVersion}</dd></div>
    </dl>
    <p><Link className={styles.primaryLink} to={nativeApprovalPath(item)}>{T.openNative}</Link></p>
  </article>
}
