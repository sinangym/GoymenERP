/**
 * D-05 "Onaylarım" — the approval inbox: the native approvals waiting for the signed-in actor in the committed company,
 * read through the Work read (#639/#644/#645), one connected source at a time, each record opening its owner's own
 * screen. Connected: Finance official-invoice-import batches (Finance's approval screen, #640 `?approvalId=`) and Sales
 * order drafts awaiting internal approval (the Sales order screen, `/satis/siparisler/{draftId}`).
 *
 * Read only. There is no approve button here and no approval rule: who sees a record, what may be approved and how
 * (authority, separation of duties, versions) all stay with the owner. A source is listed only for the owner's own
 * approve capability; no shared permission is introduced. The page says plainly which sources are connected.
 *
 * Each source is its own list — its own rows, cursor, errors and late answers. Switching the source (or the company or
 * actor) remounts the list, so nothing of one source is ever shown under another.
 */

import { useCallback, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router'
import { fetchApprovalDetail, fetchApprovalPage } from './approvalInboxApi'
import type { ApprovalItem, FinanceApprovalItem, SalesApprovalItem } from './approvalInboxContract'
import {
  APPROVAL_KIND_TEXT,
  APPROVAL_SOURCES,
  APPROVALS_TEXT as T,
  SOURCE_PARAM,
  WORK_ROUTE,
  approvalDetailRoute,
  formatInstant,
  sourceOfKind,
  type ApprovalSource,
} from './workText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
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

const grantedSources = (shell: ShellState) => APPROVAL_SOURCES.filter((source) => hasCapability(shell, source.capability))

const sourceListPath = (source: ApprovalSource) =>
  `${WORK_ROUTE.approvals}?${new URLSearchParams({ [SOURCE_PARAM]: source.token }).toString()}`

function PilotScope() {
  return <div className={styles.scope}>
    <p>{T.pilotScope}</p>
    <p>{T.nativeOnly}</p>
  </div>
}

/** The record's own screen, when the actor holds that screen's own capability; otherwise no link is drawn. */
function NativeLink({ source, approvalId, className }: { readonly source: ApprovalSource; readonly approvalId: string; readonly className?: string }) {
  const shell = useShellState()
  if (!hasCapability(shell, source.nativeCapability)) return null
  return <Link className={className} to={source.nativePath(approvalId)}>{source.openNative}</Link>
}

export function ApprovalInboxPage() {
  const { shell, namespace } = useBoundCompany()
  const [searchParams] = useSearchParams()
  const granted = grantedSources(shell)
  const source = granted.find((one) => one.token === searchParams.get(SOURCE_PARAM)) ?? granted[0] ?? null
  if (source === null) return <StateBlock state={{ kind: 'not-found' }} subject="sayfa" />
  return <section className={styles.page} aria-labelledby="work-approvals-heading">
    <header className={styles.header}>
      <h1 id="work-approvals-heading">{T.heading}</h1>
      <PilotScope />
    </header>
    {granted.length > 1
      ? <nav aria-label={T.sources} className={styles.sources}>
        {granted.map((one) => <Link key={one.kind} to={sourceListPath(one)} aria-current={one === source ? 'page' : undefined}
          className={one === source ? styles.sourceCurrent : styles.source}>{one.label}</Link>)}
      </nav>
      : null}
    {/* Remounted per context AND per source: rows, cursors, errors and late answers never cross either. */}
    <SourceInbox key={`${namespace ?? 'no-context'}::${source.kind}`} source={source} />
  </section>
}

/**
 * A next page that fails only for now (no response, offline, 5xx) leaves the rows already shown, which were verified,
 * and retries the SAME cursor. Any other answer — one that does not verify (another company, another kind or state,
 * a broken body), or an authority/record refusal — closes the list: the earlier rows and their links are no longer
 * shown as the current result, the cursor is dropped, and the only way on is a fresh first page.
 */
const TRANSIENT_CONTINUATION: ReadonlySet<RemoteViewState<unknown>['kind']> = new Set(['offline', 'network-error', 'service-unavailable'])

function SourceInbox({ source }: { readonly source: ApprovalSource }) {
  const { shell, namespace, companyId } = useBoundCompany()
  const boundary = useAuthBoundary()
  // A restart is a new read of the first page under a new key: nothing of the closed list is shown while it loads.
  const [generation, setGeneration] = useState(0)
  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'work-approvals', source.kind, 'first', generation],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchApprovalPage(source.kind, null, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const [more, setMore] = useState<readonly ApprovalItem[]>([])
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  const [closed, setClosed] = useState<RemoteViewState<unknown> | null>(null)
  const loadingRef = useRef(false)
  // Bumped by a restart: a next-page answer of the list before it is dropped.
  const attemptRef = useRef(0)
  const firstPage = first.data?.type === 'ok-single' ? first.data.data : null
  const nextCursor = cursor === undefined ? (firstPage?.nextCursor ?? null) : cursor

  const loadMore = useCallback(async () => {
    if (loadingRef.current || nextCursor === null || companyId === null) return
    loadingRef.current = true
    const attempt = attemptRef.current
    setContinuation('loading')
    const snapshot = boundary.capture()
    const outcome = await fetchApprovalPage(source.kind, nextCursor, companyId)
    if (attempt !== attemptRef.current) return
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
    const state = classifyOutcome(outcome) as RemoteViewState<unknown>
    if (state.kind === 'stale-rejected') {
      setContinuation('idle')
    } else if (TRANSIENT_CONTINUATION.has(state.kind)) {
      setContinuation(state)
    } else {
      setContinuation('idle')
      setClosed(state)
    }
  }, [boundary, companyId, firstPage, nextCursor, source.kind])

  const restart = () => {
    attemptRef.current += 1
    loadingRef.current = false
    setMore([])
    setCursor(undefined)
    setContinuation('idle')
    setClosed(null)
    setGeneration((value) => value + 1)
  }

  if (firstPage === null) {
    const state: RemoteViewState<unknown> = first.data === undefined
      ? { kind: 'initial-loading' }
      : classifyOutcome(first.data) as RemoteViewState<unknown>
    return <StateBlock state={state} subject={source.subject} onRetry={() => void first.refetch()} />
  }

  if (closed !== null) {
    return <>
      <div role="alert" data-testid="work-approvals-closed">
        <p>{T.listClosed}</p>
        <StateBlock state={closed} subject={source.subject} />
      </div>
      <button type="button" onClick={restart}>{T.restart}</button>
    </>
  }

  const rows = [...firstPage.items, ...more]
  return <>
    {rows.length === 0
      ? <StateBlock state={{ kind: 'empty-no-data' }} subject={source.subject} />
      : <div className={styles.tableWrap} tabIndex={0} role="region" aria-label={source.caption}>
        <table className={styles.table} data-testid="work-approvals-table" data-kind={source.kind}>
          <caption className={styles.caption}>{source.caption}</caption>
          {source.kind === 'sales.order-draft.v1'
            ? <SalesRows rows={rows as readonly SalesApprovalItem[]} source={source} />
            : <FinanceRows rows={rows as readonly FinanceApprovalItem[]} source={source} />}
        </table>
      </div>}
    {rows.length === 0 ? null : <div className={styles.continuation}>
      {typeof continuation === 'object'
        ? <div role="alert">
          <p>{T.nextPageFailed}</p>
          <StateBlock state={continuation} subject={source.subject} onRetry={() => void loadMore()} />
        </div>
        : nextCursor === null
          ? <p className={styles.muted}>{T.end}</p>
          : <button type="button" onClick={() => void loadMore()} disabled={continuation === 'loading'}
            aria-busy={continuation === 'loading'}>
            {continuation === 'loading' ? T.loadingMore : T.loadMore}
          </button>}
    </div>}
  </>
}

function RecordLinks({ source, approvalId }: { readonly source: ApprovalSource; readonly approvalId: string }) {
  return <td className={styles.actions}>
    <Link to={approvalDetailRoute(source.kind, approvalId)}>{T.openDetail}</Link>
    <NativeLink source={source} approvalId={approvalId} />
  </td>
}

function FinanceRows({ rows, source }: { readonly rows: readonly FinanceApprovalItem[]; readonly source: ApprovalSource }) {
  return <>
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
      <RecordLinks source={source} approvalId={row.approvalId} />
    </tr>)}</tbody>
  </>
}

function SalesRows({ rows, source }: { readonly rows: readonly SalesApprovalItem[]; readonly source: ApprovalSource }) {
  return <>
    <thead><tr>
      <th scope="col">{T.documentNo}</th>
      <th scope="col">{T.revision}</th>
      <th scope="col">{T.submittedBy}</th>
      <th scope="col">{T.submittedAt}</th>
      <th scope="col">{T.ownDraft}</th>
      <th scope="col">{T.record}</th>
    </tr></thead>
    <tbody>{rows.map((row) => <tr key={row.approvalId} data-approval-id={row.approvalId}>
      <td>{row.documentNo}</td>
      <td>{row.revision}</td>
      <td>{row.submittedBy}</td>
      <td>{formatInstant(row.submittedAtUtc)}</td>
      <td>{row.isOwnDraft ? T.ownDraftYes : '—'}</td>
      <RecordLinks source={source} approvalId={row.approvalId} />
    </tr>)}</tbody>
  </>
}

export function ApprovalInboxDetailPage() {
  const { shell, namespace } = useBoundCompany()
  const { kind, approvalId } = useParams()
  const source = sourceOfKind(kind ?? '')
  // A kind that is not connected, or one whose owner capability this actor lacks, is the neutral block — nothing read.
  if (source === null || !hasCapability(shell, source.capability) || (approvalId ?? '').length === 0) {
    return <StateBlock state={{ kind: 'not-found' }} subject="sayfa" />
  }
  return <ApprovalDetail key={`${namespace ?? 'no-context'}:${source.kind}:${approvalId ?? ''}`}
    source={source} approvalId={approvalId ?? ''} />
}

function ApprovalDetail({ source, approvalId }: { readonly source: ApprovalSource; readonly approvalId: string }) {
  const { shell, namespace, companyId } = useBoundCompany()
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'work-approval', source.kind, approvalId],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchApprovalDetail(source.kind, approvalId, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const backLink = <Link className={styles.backLink} to={sourceListPath(source)}>← {T.backToList}</Link>
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
      <h1 id="work-approval-heading">{item.kind === 'sales.order-draft.v1' ? T.detailHeadingSales : T.detailHeading}</h1>
      <PilotScope />
    </header>
    <dl className={styles.facts} data-testid="work-approval-facts">
      <div><dt>{T.kind}</dt><dd>{APPROVAL_KIND_TEXT[item.kind]}</dd></div>
      {item.kind === 'sales.order-draft.v1'
        ? <>
          <div><dt>{T.documentNo}</dt><dd>{item.documentNo}</dd></div>
          <div><dt>{T.status}</dt><dd>{T.statusPending}</dd></div>
          <div><dt>{T.revision}</dt><dd>{item.revision}</dd></div>
          <div><dt>{T.version}</dt><dd>{item.version}</dd></div>
          <div><dt>{T.submittedBy}</dt><dd>{item.submittedBy}</dd></div>
          <div><dt>{T.submittedAt}</dt><dd>{formatInstant(item.submittedAtUtc)}</dd></div>
          {item.isOwnDraft ? <div><dt>{T.ownDraft}</dt><dd>{T.ownDraftYes}</dd></div> : null}
        </>
        : <>
          <div><dt>{T.status}</dt><dd>{T.statusPending}</dd></div>
          <div><dt>{T.submittedBy}</dt><dd>{item.submittedBy}</dd></div>
          <div><dt>{T.submittedAt}</dt><dd>{formatInstant(item.submittedAtUtc)}</dd></div>
          <div><dt>{T.itemCount}</dt><dd>{item.itemCount}</dd></div>
          <div><dt>{T.templateVersion}</dt><dd>{item.templateVersion}</dd></div>
          <div><dt>{T.batchVersion}</dt><dd>{item.batchVersion}</dd></div>
        </>}
    </dl>
    <p><NativeLink className={styles.primaryLink} source={source} approvalId={item.approvalId} /></p>
  </article>
}
