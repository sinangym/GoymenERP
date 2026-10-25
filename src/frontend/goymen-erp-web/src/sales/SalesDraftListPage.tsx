/**
 * S-02 — sales order drafts of the committed company, pinned to
 * `GET /api/v1/sales/drafts?pageSize&cursor` (#257). Newest first, as the server orders.
 *
 * The columns are what `SalesOrderDraftSummaryView` carries and nothing more: no
 * amount, no KPI, no derived "overall" status. The Cari column is the record-bound party
 * display of the opt-in read contract (#626): the code and name the Cari owner published for
 * THIS row's party — never a name looked up elsewhere. It links to the Cari card only when the
 * shell publishes that card's own capability; otherwise it is plain text.
 * Continuation follows the contract: `nextCursor = null` is the end, there is no
 * total count, and a failed next page keeps the rows already shown and offers an
 * explicit retry of the SAME cursor — never an automatic resend.
 *
 * SALES-DS-01 (GOY-DS-001): the same facts on the design system — the shared DataTable on a wide
 * screen, one card per row on a phone (the shell's compact width), the filter in a Field, the
 * continuation on Button and the state on StatusIndicator. Long Cari names wrap; nothing is cut.
 */

import { useCallback, useRef, useState, useSyncExternalStore } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchDraftPage } from './api'
import { APPROVAL_STATE, CANCELLED_STATE, CLOSED_STATE, isTerminalState, type ApprovalState, type DraftSummaryWithParty, type PartyDisplay } from './contract'
import { SALES_CAPABILITY, SALES_ROUTE, salesDetailPath } from './capabilities'
import { APPROVAL_STATE_TEXT, COMMERCIAL_STAGE_TEXT, ORDER_DETAIL_TEXT, ORDER_LIST_TEXT, partyDisplayText } from './salesText'
import { CAPABILITY, CAPABILITY_ROUTE } from '../shell/capabilities'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { Field } from '../ui/Field'
import { StatusIndicator, type StatusTone } from '../ui/StatusIndicator'
import { formatBusinessDate, formatInstant } from './format'
import styles from './SalesDraftListPage.module.css'

/** The record-bound Cari: a link to its card only with the card's own capability, plain text otherwise. */
export function SalesPartyDisplay({ party, canOpen }: { readonly party: PartyDisplay; readonly canOpen: boolean }) {
  const text = partyDisplayText(party)
  return canOpen
    ? <Link to={CAPABILITY_ROUTE[CAPABILITY.cariPartiesDetail].replace(':partyId', encodeURIComponent(party.partyId))}>{text}</Link>
    : <>{text}</>
}

/** The shell's phone width (`AppShell` COMPACT_VIEWPORT): rows become cards instead of a squeezed table. */
const CARD_VIEWPORT = '(max-width: 767px)'

function subscribeCards(onChange: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => {}
  const query = window.matchMedia(CARD_VIEWPORT)
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
}

function readCards(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia(CARD_VIEWPORT).matches
}

/** The row's own two facts, as one text: the lifecycle beside the internal approval, never merged into a new state. */
function stateText(row: DraftSummaryWithParty): string {
  return `${row.state === CANCELLED_STATE ? ORDER_DETAIL_TEXT.stateCancelled : row.state === CLOSED_STATE ? ORDER_DETAIL_TEXT.stateClosed : ORDER_DETAIL_TEXT.stateDraft} · ${APPROVAL_STATE_TEXT[row.approvalState]}`
}

/**
 * The dot's colour only — the text above carries the meaning. A cancelled draft is neutral whatever its approval;
 * otherwise the approval alone picks the colour. No value here is derived from anything but those two fields.
 */
const APPROVAL_TONE: Record<ApprovalState, StatusTone> = {
  [APPROVAL_STATE.preparing]: 'neutral',
  [APPROVAL_STATE.pendingApproval]: 'warning',
  [APPROVAL_STATE.approved]: 'success',
  [APPROVAL_STATE.rejected]: 'danger',
  [APPROVAL_STATE.withdrawn]: 'neutral',
}

function stateTone(row: DraftSummaryWithParty): StatusTone {
  return isTerminalState(row.state) ? 'neutral' : APPROVAL_TONE[row.approvalState]
}

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
  const canOpenParty = hasCapability(shell, CAPABILITY.cariPartiesDetail)
  const cards = useSyncExternalStore(subscribeCards, readCards, () => false)
  // SALES-A1: the internal approval filter is the server's (`approvalState`); changing it restarts the list.
  const [approvalFilter, setApprovalFilter] = useState<ApprovalState | null>(null)

  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-drafts', 'first', approvalFilter ?? 'all'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftPage(null, companyId as string, signal, approvalFilter)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const [more, setMore] = useState<readonly DraftSummaryWithParty[]>([])
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
    const outcome = await fetchDraftPage(nextCursor, companyId, undefined, approvalFilter)
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
  }, [approvalFilter, boundary, companyId, firstPage, nextCursor])

  const header = (
    <header className={styles.header}>
      <div className={styles.titleRow}>
        <h1 id="sales-list-heading" className={styles.title}>
          {ORDER_LIST_TEXT.heading}
        </h1>
        {/* A link styled as the primary action: it navigates, so it stays a link (no button inside it). */}
        {canCreate ? (
          <Link className={styles.createLink} to={SALES_ROUTE.create}>
            {ORDER_LIST_TEXT.newDraft}
          </Link>
        ) : null}
      </div>
      <div className={styles.toolbar}>
        <Field id="sales-approval-filter" label={ORDER_LIST_TEXT.filter} className={styles.filter}>
          {(control) => (
            <select
              {...control}
              className={styles.select}
              value={approvalFilter ?? ''}
              onChange={(event) => {
                const value = event.target.value
                setMore([])
                setCursor(undefined)
                setContinuation('idle')
                setApprovalFilter(value === '' ? null : (value as ApprovalState))
              }}
            >
              <option value="">{ORDER_LIST_TEXT.filterAll}</option>
              {Object.values(APPROVAL_STATE).map((state) => (
                <option key={state} value={state}>
                  {APPROVAL_STATE_TEXT[state]}
                </option>
              ))}
            </select>
          )}
        </Field>
      </div>
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

  // The cells the table and the cards share, so both say exactly the same thing.
  const documentCell = (row: DraftSummaryWithParty) =>
    canOpen ? <Link className={styles.documentLink} to={salesDetailPath(row.id)}>{row.documentNo}</Link> : row.documentNo
  const partyCell = (row: DraftSummaryWithParty) => (
    <span className={styles.party} data-testid={`sales-row-party-${row.id}`}>
      <SalesPartyDisplay party={row.partyDisplay} canOpen={canOpenParty} />
    </span>
  )
  const stageCell = (row: DraftSummaryWithParty) => (
    <span data-testid={`sales-row-commercial-stage-${row.id}`}>{COMMERCIAL_STAGE_TEXT[row.commercialStage]}</span>
  )
  const statusCell = (row: DraftSummaryWithParty) => (
    <StatusIndicator tone={stateTone(row)} className={styles.status}>
      <span data-testid={`sales-row-approval-${row.id}`}>{stateText(row)}</span>
    </StatusIndicator>
  )

  const columns: readonly DataTableColumn<DraftSummaryWithParty>[] = [
    { key: 'documentNo', header: ORDER_LIST_TEXT.documentNo, cell: documentCell, pin: 'start', className: styles.documentColumn },
    { key: 'party', header: ORDER_LIST_TEXT.party, cell: partyCell, className: styles.partyColumn },
    { key: 'businessDate', header: ORDER_LIST_TEXT.businessDate, cell: (row) => formatBusinessDate(row.businessDate), className: styles.nowrap },
    { key: 'commercialStage', header: ORDER_LIST_TEXT.commercialStage, cell: stageCell },
    { key: 'state', header: ORDER_LIST_TEXT.state, cell: statusCell },
    { key: 'revision', header: ORDER_LIST_TEXT.revision, cell: (row) => row.revision, className: styles.nowrap },
    { key: 'lineCount', header: ORDER_LIST_TEXT.lineCount, cell: (row) => row.lineCount, align: 'end' },
    { key: 'createdAt', header: ORDER_LIST_TEXT.createdAt, cell: (row) => formatInstant(row.createdAtUtc), className: styles.nowrap },
  ]

  return (
    <section className={styles.page} aria-labelledby="sales-list-heading">
      {header}
      {cards ? (
        <ul className={styles.cards} aria-label={ORDER_LIST_TEXT.caption} data-testid="sales-draft-cards">
          {rows.map((row) => (
            <li key={row.id} className={styles.card} data-draft-id={row.id}>
              <div className={styles.cardHead}>
                <span className={styles.cardDocument}>{documentCell(row)}</span>
                {statusCell(row)}
              </div>
              <dl className={styles.cardFacts}>
                <div className={styles.wide}><dt>{ORDER_LIST_TEXT.party}</dt><dd>{partyCell(row)}</dd></div>
                <div><dt>{ORDER_LIST_TEXT.businessDate}</dt><dd>{formatBusinessDate(row.businessDate)}</dd></div>
                <div><dt>{ORDER_LIST_TEXT.commercialStage}</dt><dd>{stageCell(row)}</dd></div>
                <div><dt>{ORDER_LIST_TEXT.revision}</dt><dd>{row.revision}</dd></div>
                <div><dt>{ORDER_LIST_TEXT.lineCount}</dt><dd>{row.lineCount}</dd></div>
                <div className={styles.wide}><dt>{ORDER_LIST_TEXT.createdAt}</dt><dd>{formatInstant(row.createdAtUtc)}</dd></div>
              </dl>
            </li>
          ))}
        </ul>
      ) : (
        <DataTable
          label={ORDER_LIST_TEXT.caption}
          columns={columns}
          rows={rows}
          rowKey={(row) => row.id}
          rowAttributes={(row) => ({ 'data-draft-id': row.id })}
        />
      )}
      <div className={styles.continuation}>
        {typeof continuation === 'object' ? (
          <div className={styles.inlineError}>
            <p>{ORDER_LIST_TEXT.nextPageFailed}</p>
            <StateBlock state={continuation} subject="sonraki sayfa" onRetry={() => void loadMore()} />
          </div>
        ) : nextCursor === null ? (
          <p className={styles.muted}>{ORDER_LIST_TEXT.end}</p>
        ) : (
          <Button
            type="button"
            variant="secondary"
            onClick={() => void loadMore()}
            disabled={continuation === 'loading'}
            aria-busy={continuation === 'loading'}
          >
            {continuation === 'loading' ? ORDER_LIST_TEXT.loadingMore : ORDER_LIST_TEXT.loadMore}
          </Button>
        )}
      </div>
    </section>
  )
}
