import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchPurchaseRequests } from './api'
import { PURCHASING_CAPABILITY, PURCHASING_ROUTE, purchaseRequestDetailPath } from './capabilities'
import type { PurchaseRequestStatus, PurchaseRequestSummary } from './contract'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { createRequestFence } from './requestFence'
import { Button } from '../ui/Button'
import { PURCHASE_REQUEST_SOURCE_LABEL as SOURCE, PURCHASE_REQUEST_STATUS_LABEL as STATUS, REQUEST_LIST_TEXT as T } from './purchasingText'
import styles from './purchasing.module.css'

export function PurchaseRequestListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <PurchaseRequestList key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function PurchaseRequestList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canOpen = hasCapability(shell, PURCHASING_CAPABILITY.requestDetail)
  const canCreate = hasCapability(shell, PURCHASING_CAPABILITY.requestCreate)
  const [status, setStatus] = useState<PurchaseRequestStatus | null>(null)
  const [more, setMore] = useState<readonly PurchaseRequestSummary[]>([])
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const loading = useRef(false)
  const pageFence = useRef(createRequestFence())
  useEffect(() => { const fence = pageFence.current; return () => fence.invalidate() }, [namespace, shell.routeEntryGeneration])
  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'purchase-requests', status ?? 'all'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPurchaseRequests(companyId ?? '', { status }, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const page = first.data?.type === 'ok-single' ? first.data.data : null
  const nextCursor = cursor === undefined ? page?.nextCursor ?? null : cursor
  const loadMore = useCallback(async () => {
    if (loading.current || companyId === null || nextCursor === null) return
    loading.current = true; setContinuation('loading')
    const token = pageFence.current.capture(); const snapshot = boundary.capture()
    const outcome = await fetchPurchaseRequests(companyId, { cursor: nextCursor, status }, token.signal)
    if (!pageFence.current.isCurrent(token)) return
    loading.current = false
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
    if (outcome.type === 'ok-single') {
      setMore((current) => {
        const seen = new Set([...(page?.items ?? []), ...current].map((item) => item.id))
        return [...current, ...outcome.data.items.filter((item) => !seen.has(item.id))]
      })
      setCursor(outcome.data.nextCursor); setContinuation('idle')
    } else setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, nextCursor, page, status])

  const header = <header className={styles.header}><div><p className={styles.eyebrow}>{T.eyebrow}</p><h1>{T.heading}</h1></div><div className={styles.headerActions}>{canCreate ? <Link className={styles.primaryLink} to={PURCHASING_ROUTE[PURCHASING_CAPABILITY.requestCreate]}>{T.create}</Link> : null}<label>{T.filter} <select value={status ?? ''} onChange={(event) => { pageFence.current.invalidate(); loading.current = false; setStatus(event.target.value === '' ? null : event.target.value as PurchaseRequestStatus); setMore([]); setCursor(undefined); setContinuation('idle') }}><option value="">{T.filterAll}</option><option value="Draft">{STATUS.Draft}</option><option value="Submitted">{STATUS.Submitted}</option><option value="Approved">{STATUS.Approved}</option><option value="Rejected">{STATUS.Rejected}</option><option value="Withdrawn">{STATUS.Withdrawn}</option></select></label></div></header>
  if (companyId === null) return <main className={styles.page}>{header}<StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} /></main>
  if (page === null) {
    const state = first.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(first.data)
    return <main className={styles.page}>{header}<StateBlock state={state as RemoteViewState<unknown>} subject={T.subject} onRetry={() => void first.refetch()} /></main>
  }
  const rows = [...page.items, ...more]
  return <main className={styles.page} data-testid="purchase-request-list">{header}<p className={styles.note}>{T.scope}</p>{rows.length === 0 ? <StateBlock state={{ kind: status === null ? 'empty-no-data' : 'empty-filtered' }} subject={T.subject} /> : <div className={styles.tableWrap}><table><caption>{T.caption}</caption><thead><tr><th>{T.request}</th><th>{T.status}</th><th>{T.businessDate}</th><th>{T.lineCount}</th><th>{T.source}</th><th>{T.updatedAt}</th></tr></thead><tbody>{rows.map((row) => <tr key={row.id}><th className={styles.mono}>{canOpen ? <Link to={purchaseRequestDetailPath(row.id)}>…{row.id.slice(-8)}</Link> : `…${row.id.slice(-8)}`}</th><td><span className={row.status === 'Draft' ? styles.draft : row.status === 'Rejected' || row.status === 'Withdrawn' ? styles.rejected : styles.approved}>{STATUS[row.status]}</span></td><td>{row.effectiveOn}</td><td>{row.lineCount}</td><td>{row.sourceKinds === undefined ? T.sourceUnknown : row.sourceKinds.map((kind) => SOURCE[kind]).join(', ')}</td><td>{new Date(row.updatedAtUtc).toLocaleString('tr-TR')}</td></tr>)}</tbody></table></div>}{nextCursor !== null ? <div>{continuation === 'loading' ? <p role="status">{T.loadingMore}</p> : continuation === 'idle' ? <Button type="button" onClick={() => void loadMore()}>{T.loadMore}</Button> : <><StateBlock state={continuation} subject={T.nextPageSubject} /><Button type="button" onClick={() => void loadMore()}>{T.retrySamePage}</Button></>}</div> : null}</main>
}
