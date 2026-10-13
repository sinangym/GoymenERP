/**
 * FI04 — Finans → Açık kalemler: the company's admitted open items, read-only (#700).
 *
 * The list is `GET /api/v1/finance/open-items?cursor` and the detail `GET /api/v1/finance/open-items/{id}`; both
 * need only the open-item read authority. Money and the Open/Closed status belong to a separate amount authority:
 * without it the server sends neither, and this screen then shows neither — no zero, no "Kapalı", no total and no
 * overdue mark is ever worked out here.
 *
 * The read carries no party name, document number or currency name, so none is shown or looked up per row. The main
 * view keeps to the source kind, the due date and the disclosed facts; the record's identities live under
 * "Teknik bilgiler" on the detail. The currency is shown as the reference the read carries: this screen makes no
 * other read to name it.
 *
 * The server measures the amount authority on every read. Rows are therefore kept only while they share one
 * disclosure: an answer that discloses differently from what is on screen drops everything shown before anything
 * else is drawn, so money or a status the caller may no longer read never stays behind.
 *
 * Continuation follows the contract: 50 per page, newest first, `nextCursor = null` is the end, there is no filter,
 * sort or total. A failed next page keeps the rows already shown and retries the SAME cursor only when asked.
 * A company, actor or permission change remounts the screen, so rows, detail and a late answer of the previous
 * context are never shown.
 */

import { useCallback, useEffect, useRef, useState, useSyncExternalStore, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE } from '../shell/capabilities'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { StatusIndicator, type StatusTone } from '../ui/StatusIndicator'
import { FINANCE_CAPABILITY, FINANCE_ROUTE, openItemDetailPath } from './capabilities'
import { isBoundUuid, type AmountView } from './contract'
import { formatDecimalTextTr } from './decimalText'
import type { OpenItem, OpenItemStatus } from './openItemReadContract'
import { fetchOpenItem, fetchOpenItems } from './openItemReadHttp'
import { OPEN_ITEM_TEXT as T, shortReference, sourceKindLabel } from './openItemReadText'
import styles from './openItemRead.module.css'

const LIST_HEADING_ID = 'finance-open-item-list-heading'
const DETAIL_HEADING_ID = 'finance-open-item-detail-heading'

const STATUS_TEXT: Record<OpenItemStatus, string> = { Open: T.statusOpen, Closed: T.statusClosed }
/** The dot's colour only — the status text carries the meaning. */
const STATUS_TONE: Record<OpenItemStatus, StatusTone> = { Open: 'info', Closed: 'neutral' }

/**
 * Below a desktop width the rows are cards: with the sidebar open the seven-column table would only fit by
 * scrolling sideways, which hides the amounts a reader came for.
 */
const CARD_VIEWPORT = '(max-width: 1023px)'

function subscribeCards(onChange: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => {}
  const query = window.matchMedia(CARD_VIEWPORT)
  query.addEventListener('change', onChange)
  return () => query.removeEventListener('change', onChange)
}

function readCards(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia(CARD_VIEWPORT).matches
}

/** Presentation only: the wire's `yyyy-MM-dd` in Turkish order. */
function formatDate(value: string): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  return parts === null ? value : `${parts[3]}.${parts[2]}.${parts[1]}`
}

function Instant({ value }: { readonly value: string }) {
  const parsed = new Date(value)
  return <time dateTime={value}>
    {Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })}
  </time>
}

/** The currency as the read carries it: a reference. No code or name is looked up or guessed. */
const currencyText = (currencyId: string) => T.currencyReference(shortReference(currencyId))

/** The one disclosure a server answer was made under (the decoder refuses a mixed answer); `null` for no rows. */
const disclosureOf = (items: readonly OpenItem[]) => items.length === 0 ? null : items[0].initialAmount.disclosure

const dueOnText = (item: OpenItem) => item.dueOn === null ? T.noDueOn : formatDate(item.dueOn)

/** The amount exactly as stored, in Turkish digits grouping; a hidden amount says that it is hidden. */
function Amount({ amount }: { readonly amount: AmountView }) {
  if (amount.disclosure === 'Visible') {
    return <span className={styles.amount}>{formatDecimalTextTr(amount.money.amount) ?? amount.money.amount}</span>
  }
  return <span className={styles.withheld}>{amount.disclosure === 'Redacted' ? T.redacted : T.unavailable}</span>
}

function Status({ item }: { readonly item: OpenItem }) {
  if (item.status !== null) return <StatusIndicator tone={STATUS_TONE[item.status]}>{STATUS_TEXT[item.status]}</StatusIndicator>
  return <span className={styles.withheld}>{item.initialAmount.disclosure === 'Redacted' ? T.redacted : T.unavailable}</span>
}

/** True when the server withheld money and status from this caller on every row: the columns are then left out. */
const allRedacted = (items: readonly OpenItem[]) => items.every((item) => item.initialAmount.disclosure === 'Redacted')

const NO_ROWS: readonly OpenItem[] = []

function useBinding() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  return { shell, namespace, companyId, mountKey: `${namespace ?? 'no-context'}::${shell.routeEntryGeneration}` }
}

export function OpenItemListPage() {
  const { mountKey } = useBinding()
  return <OpenItemList key={mountKey} />
}

function OpenItemList() {
  const { shell, namespace, companyId } = useBinding()
  const boundary = useAuthBoundary()
  const canOpen = hasCapability(shell, FINANCE_CAPABILITY.openItemsDetail as never)
  /** Cards or table is a layout choice only: the same rows, the same order, no new read when it flips. */
  const cards = useSyncExternalStore(subscribeCards, readCards, () => false)
  /**
   * One disclosure epoch: every row on screen was answered under the same amount authority. A new epoch drops all of
   * them at once and reads the first page afresh — rows of two authorities are never shown together.
   */
  const [epoch, setEpoch] = useState(0)
  const [more, setMore] = useState<readonly OpenItem[]>([])
  const [cursor, setCursor] = useState<string | null | undefined>(undefined)
  const [continuation, setContinuation] = useState<'idle' | 'loading' | RemoteViewState<unknown>>('idle')
  /** A next page the server refused outright: the list is closed rather than left on screen above the refusal. */
  const [refused, setRefused] = useState<RemoteViewState<unknown> | null>(null)
  const loading = useRef(false)
  /** The next-page read in flight; a new first-page answer, a new epoch or leaving the screen aborts it. */
  const inFlight = useRef<AbortController | null>(null)
  const dropInFlight = useCallback(() => {
    inFlight.current?.abort()
    inFlight.current = null
    loading.current = false
  }, [])
  useEffect(() => dropInFlight, [dropInFlight])

  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'finance-open-items', epoch],
    enabled: namespace !== null && companyId !== null,
    // Nothing of this read outlives the screen that shows it: no disclosed amount waits in a cache.
    gcTime: 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchOpenItems(companyId ?? '', null, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const page = first.data?.type === 'ok-single' ? first.data.data : null

  // The later pages continue ONE first-page answer. When the first page is answered again, they and their cursor
  // belong to the answer before it: they are dropped in the same render, before the new answer is drawn.
  const answeredAt = first.dataUpdatedAt
  const [continues, setContinues] = useState(answeredAt)
  if (continues !== answeredAt) {
    setContinues(answeredAt)
    setMore([])
    setCursor(undefined)
    setContinuation('idle')
    setRefused(null)
  }
  useEffect(() => dropInFlight, [answeredAt, dropInFlight])
  const current = continues === answeredAt
  const shownMore = current ? more : NO_ROWS
  const nextCursor = !current || cursor === undefined ? page?.nextCursor ?? null : cursor

  const loadMore = useCallback(async () => {
    if (loading.current || companyId === null || page === null || nextCursor === null) return
    loading.current = true
    setContinuation('loading')
    const controller = new AbortController()
    inFlight.current = controller
    const snapshot = boundary.capture()
    const outcome = await fetchOpenItems(companyId, nextCursor, controller.signal)
    if (inFlight.current !== controller || controller.signal.aborted) return
    inFlight.current = null
    loading.current = false
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundary.report(outcome.status, snapshot)
      setMore([])
      setRefused(classifyOutcome(outcome) as RemoteViewState<unknown>)
      return
    }
    if (outcome.type !== 'ok-single') {
      setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
      return
    }
    const shown = disclosureOf([...page.items, ...shownMore])
    const answered = disclosureOf(outcome.data.items)
    if (shown !== null && answered !== null && shown !== answered) {
      // The amount authority changed between two pages. Nothing already shown is kept and this page is not added.
      setMore([])
      setCursor(undefined)
      setContinuation('idle')
      setEpoch((value) => value + 1)
      return
    }
    setMore((existing) => {
      const seen = new Set([...page.items, ...existing].map((item) => item.id))
      return [...existing, ...outcome.data.items.filter((item) => !seen.has(item.id))]
    })
    setCursor(outcome.data.nextCursor)
    setContinuation('idle')
  }, [boundary, companyId, nextCursor, page, shownMore])

  const frame = (body: ReactNode) => (
    <section className={styles.page} aria-labelledby={LIST_HEADING_ID} data-testid="finance-open-item-list">
      <h1 id={LIST_HEADING_ID} className={styles.title}>{T.heading}</h1>
      {body}
    </section>
  )

  if (companyId === null) return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />)
  if (refused !== null && current) return frame(<StateBlock state={refused} subject={T.subject} />)
  if (page === null) {
    const state = first.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(first.data)
    return frame(<StateBlock state={state as RemoteViewState<unknown>} subject={T.subject} onRetry={() => void first.refetch()} />)
  }

  // Fail closed: later pages are drawn only while they share the first page's disclosure.
  const sameAuthority = shownMore.length === 0 || page.items.length === 0 || disclosureOf(shownMore) === disclosureOf(page.items)
  const rows = sameAuthority ? [...page.items, ...shownMore] : page.items
  if (rows.length === 0 && nextCursor === null) return frame(<StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} />)
  const withheld = allRedacted(rows)

  return frame(<>
    <p className={styles.note}>{T.order}{withheld ? ` ${T.amountsHidden}` : ''}</p>
    {cards
      ? <OpenItemCards rows={rows} canOpen={canOpen} withheld={withheld} />
      : <OpenItemTable rows={rows} canOpen={canOpen} withheld={withheld} />}
    {nextCursor !== null ? (
      <div className={styles.continuation}>
        {typeof continuation === 'object'
          ? <StateBlock state={continuation} subject={T.nextPageSubject} onRetry={() => void loadMore()} />
          : <Button type="button" variant="secondary" onClick={() => void loadMore()}
              disabled={continuation === 'loading'} aria-busy={continuation === 'loading'}>
              {continuation === 'loading' ? T.loadingMore : T.loadMore}
            </Button>}
      </div>
    ) : null}
  </>)
}

interface RowsProps {
  readonly rows: readonly OpenItem[]
  readonly canOpen: boolean
  readonly withheld: boolean
}

/** The record's short reference; a link only for a caller who may open the detail. Its name says which item it is. */
function RecordCell({ item, canOpen }: { readonly item: OpenItem; readonly canOpen: boolean }) {
  const reference = shortReference(item.id)
  return canOpen
    ? <Link className={styles.recordLink} to={openItemDetailPath(item.id)}
        aria-label={T.openName(reference, sourceKindLabel(item.sourceKind), T.dueOnName(dueOnText(item)))}>{reference}</Link>
    : <span className={styles.record}>{reference}</span>
}

function OpenItemTable({ rows, canOpen, withheld }: RowsProps) {
  const columns: readonly DataTableColumn<OpenItem>[] = [
    { key: 'record', header: T.record, cell: (item) => <RecordCell item={item} canOpen={canOpen} />, pin: 'start', className: styles.nowrap },
    { key: 'sourceKind', header: T.sourceKind, cell: (item) => sourceKindLabel(item.sourceKind), className: styles.kind },
    { key: 'dueOn', header: T.dueOn, cell: dueOnText, className: styles.nowrap },
    ...(withheld ? [] : [
      { key: 'status', header: T.status, cell: (item: OpenItem) => <Status item={item} /> },
      { key: 'initial', header: T.initialAmount, cell: (item: OpenItem) => <Amount amount={item.initialAmount} />, align: 'end' as const },
      { key: 'outstanding', header: T.outstandingAmount, cell: (item: OpenItem) => <Amount amount={item.outstandingAmount} />, align: 'end' as const },
      { key: 'currency', header: T.currency, cell: (item: OpenItem) => currencyText(item.currencyId) },
    ]),
    { key: 'createdAt', header: T.createdAt, cell: (item) => <Instant value={item.createdAtUtc} />, className: styles.nowrap },
  ]
  return <DataTable label={T.tableLabel} columns={columns} rows={rows} rowKey={(item) => item.id}
    rowAttributes={(item) => ({ 'data-open-item-id': item.id })} />
}

function OpenItemCards({ rows, canOpen, withheld }: RowsProps) {
  return <ul className={styles.cards} aria-label={T.tableLabel} data-testid="finance-open-item-cards">
    {rows.map((item) => (
      <li key={item.id} className={styles.card} data-open-item-id={item.id}>
        <div className={styles.cardHead}>
          <span className={styles.cardRecord}><span className={styles.label}>{T.record}</span> <RecordCell item={item} canOpen={canOpen} /></span>
          {withheld ? null : <Status item={item} />}
        </div>
        <dl className={`${styles.facts} ${styles.cardFacts}`}>
          <div className={styles.wide}><dt>{T.sourceKind}</dt><dd>{sourceKindLabel(item.sourceKind)}</dd></div>
          <div><dt>{T.dueOn}</dt><dd>{dueOnText(item)}</dd></div>
          <div><dt>{T.createdAt}</dt><dd><Instant value={item.createdAtUtc} /></dd></div>
          {withheld ? null : <>
            <div><dt>{T.initialAmount}</dt><dd><Amount amount={item.initialAmount} /></dd></div>
            <div><dt>{T.outstandingAmount}</dt><dd><Amount amount={item.outstandingAmount} /></dd></div>
            <div className={styles.wide}><dt>{T.currency}</dt><dd>{currencyText(item.currencyId)}</dd></div>
          </>}
        </dl>
      </li>
    ))}
  </ul>
}

export function OpenItemDetailPage() {
  const { mountKey } = useBinding()
  const { openItemId } = useParams<{ openItemId: string }>()
  return <OpenItemDetail key={`${mountKey}::${openItemId ?? ''}`} openItemId={openItemId} />
}

function OpenItemDetail({ openItemId }: { readonly openItemId: string | undefined }) {
  const { shell, namespace, companyId } = useBinding()
  const boundary = useAuthBoundary()
  const canList = hasCapability(shell, FINANCE_CAPABILITY.openItemsList as never)
  const valid = isBoundUuid(openItemId)
  const read = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'finance-open-item', openItemId ?? ''],
    // The answer is replaced whole by the next one and kept nowhere once the screen is left.
    gcTime: 0,
    enabled: namespace !== null && companyId !== null && valid,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchOpenItem(companyId ?? '', openItemId ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const item = read.data?.type === 'ok-single' ? read.data.data : null
  const state: RemoteViewState<unknown> = companyId === null
    ? { kind: 'no-authorized-company' }
    : !valid
      ? { kind: 'not-found' }
      : read.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(read.data) as RemoteViewState<unknown>

  return (
    <section className={styles.page} aria-labelledby={DETAIL_HEADING_ID} data-testid="finance-open-item-detail">
      {canList ? <Link className={styles.back} to={FINANCE_ROUTE.openItemsList}>{T.back}</Link> : null}
      <h1 id={DETAIL_HEADING_ID} className={styles.title}>{T.detailHeading}</h1>
      {item === null
        ? <StateBlock state={state} subject={T.detailSubject} onRetry={() => void read.refetch()} />
        : <OpenItemFacts item={item} />}
    </section>
  )
}

function OpenItemFacts({ item }: { readonly item: OpenItem }) {
  const shell = useShellState()
  // The party card is Cari's own screen: its link is offered only to a caller Cari already lets in.
  const canOpenParty = hasCapability(shell, CAPABILITY.cariPartiesDetail)
  const withheld = allRedacted([item])
  return <>
    <dl className={`${styles.facts} ${styles.detailFacts}`}>
      <div className={styles.wide}><dt>{T.sourceKind}</dt><dd>{sourceKindLabel(item.sourceKind)}</dd></div>
      <div><dt>{T.dueOn}</dt><dd>{dueOnText(item)}</dd></div>
      <div><dt>{T.createdAt}</dt><dd><Instant value={item.createdAtUtc} /></dd></div>
      {withheld ? null : <>
        <div><dt>{T.status}</dt><dd><Status item={item} /></dd></div>
        <div><dt>{T.initialAmount}</dt><dd><Amount amount={item.initialAmount} /></dd></div>
        <div><dt>{T.outstandingAmount}</dt><dd><Amount amount={item.outstandingAmount} /></dd></div>
        <div><dt>{T.currency}</dt><dd>{currencyText(item.currencyId)}</dd></div>
      </>}
      {canOpenParty
        ? <div><dt>{T.party}</dt><dd>
            <Link to={CAPABILITY_ROUTE[CAPABILITY.cariPartiesDetail].replace(':partyId', encodeURIComponent(item.partyId))}>{T.openParty}</Link>
          </dd></div>
        : null}
    </dl>
    {withheld ? <p className={styles.note}>{T.amountsHidden}</p> : null}
    <details className={styles.technical}>
      <summary>{T.technical}</summary>
      <p className={styles.note}>{T.technicalNote}</p>
      <dl className={`${styles.facts} ${styles.ids}`}>
        <div><dt>{T.itemId}</dt><dd>{item.id}</dd></div>
        <div><dt>{T.itemVersion}</dt><dd>{item.version}</dd></div>
        <div><dt>{T.sourceKindValue}</dt><dd>{item.sourceKind}</dd></div>
        <div><dt>{T.sourceId}</dt><dd>{item.sourceId}</dd></div>
        <div><dt>{T.sourceLineId}</dt><dd>{item.sourceLineId}</dd></div>
        <div><dt>{T.sourceVersion}</dt><dd>{item.sourceVersion}</dd></div>
        <div><dt>{T.partyId}</dt><dd>{item.partyId}</dd></div>
        <div><dt>{T.companyAccountId}</dt><dd>{item.companyAccountId}</dd></div>
        <div><dt>{T.currencyId}</dt><dd>{item.currencyId}</dd></div>
      </dl>
    </details>
  </>
}
