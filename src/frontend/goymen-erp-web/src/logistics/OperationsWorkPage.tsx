/**
 * AT-025 — "Operasyon işleri": the operations work list's two SEPARATE sections, one route each (master v3 §27.8):
 *
 *   /lojistik/operasyon-isleri/satis      L-01  GET /api/v1/logistics/operations-work/sales     (its own grant)
 *   /lojistik/operasyon-isleri/satinalma  L-09  GET /api/v1/logistics/operations-work/purchase  (its own grant)
 *
 * The sections are never one list with a filter: each reads its own route, decodes only its own row type and refuses a
 * page naming the other section, so a row cannot land in the wrong list. The section switcher shows only the sections
 * the actor's shell snapshot carries. A row opens its OWNER's screen (the Sales order S-04, the purchase order P-03-DETAIL)
 * only when the actor holds that screen; otherwise the reference is text. The in-page search lives in the URL (`?ara=`),
 * so going back from an owner screen returns to the same section and search. Price-free: neither wire type has a price
 * member (Sinan §4, #66 5738113623). Continuation follows the contract: `nextCursor = null` is the end, no total count,
 * a failed next page keeps the rows shown and offers the same cursor again — never an automatic resend.
 */

import { useCallback, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { fetchPurchaseWork, fetchSalesWork } from './api'
import type { PurchaseWorkRow, SalesWorkRow, WorkPage } from './operationsWorkContract'
import type { OperationsSection } from './operationsWorkContract'
import { LOGISTICS_CAPABILITY, LOGISTICS_ROUTE } from './capabilities'
import { OPERATIONS_WORK_TEXT as T, PURCHASE_LINE_KIND_TEXT } from './logisticsText'
import { formatBusinessDate } from './format'
import { SALES_CAPABILITY, salesDetailPath } from '../sales/capabilities'
import { PURCHASING_CAPABILITY, purchaseOrderDetailPath } from '../purchasing/capabilities'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './LogisticsPages.module.css'

const SEARCH_PARAM = 'ara'

export function OperationsWorkPage({ section }: { readonly section: OperationsSection }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context AND per section: rows and cursors of one company or one section never survive into another.
  return section === 'sales' ? (
    <SalesWork key={`${namespace ?? 'no-context'}:sales`} namespace={namespace} />
  ) : (
    <PurchaseWork key={`${namespace ?? 'no-context'}:purchase`} namespace={namespace} />
  )
}

/* ── The paged read, shared by both sections (each with its own fetcher and row type) ── */

function useWorkPages<T extends { readonly lineId: string }>(
  namespace: string | null,
  section: OperationsSection,
  fetchPage: (cursor: string | null, companyId: string, signal?: AbortSignal) => Promise<ViewOutcome<WorkPage<T>>>,
) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const first = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'logistics-operations-work', section, 'first'],
    enabled: namespace !== null && companyId !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPage(null, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const [more, setMore] = useState<readonly T[]>([])
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
    const outcome = await fetchPage(nextCursor, companyId)
    loadingRef.current = false
    if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
    if (outcome.type === 'ok-single') {
      // A page that repeats a line already shown is not appended twice.
      setMore((rows) => {
        const seen = new Set([...(firstPage?.items ?? []), ...rows].map((row) => row.lineId))
        return [...rows, ...outcome.data.items.filter((row) => !seen.has(row.lineId))]
      })
      setCursor(outcome.data.nextCursor)
      setContinuation('idle')
      return
    }
    setContinuation(classifyOutcome(outcome) as RemoteViewState<unknown>)
  }, [boundary, companyId, fetchPage, firstPage, nextCursor])

  return { first, firstPage, rows: firstPage === null ? [] : [...firstPage.items, ...more], nextCursor, continuation, loadMore }
}

/* ── The page frame: heading, section switcher, search ─────────────────────── */

function Frame({ section, children }: { readonly section: OperationsSection; readonly children: ReactNode }) {
  const shell = useShellState()
  const [params, setParams] = useSearchParams()
  const search = params.get(SEARCH_PARAM) ?? ''
  const tabs: readonly { readonly id: OperationsSection; readonly capability: string; readonly to: string; readonly label: string }[] = [
    { id: 'sales', capability: LOGISTICS_CAPABILITY.operationsSales, to: LOGISTICS_ROUTE.operationsSales, label: T.salesTab },
    { id: 'purchase', capability: LOGISTICS_CAPABILITY.operationsPurchase, to: LOGISTICS_ROUTE.operationsPurchase, label: T.purchaseTab },
  ]
  return (
    <section className={styles.page} aria-labelledby="operations-work-heading">
      <header className={styles.header}>
        <h1 id="operations-work-heading" className={styles.title}>
          {T.heading}
        </h1>
        <nav aria-label={T.sections}>
          <ul className={styles.actions}>
            {tabs
              .filter((tab) => hasCapability(shell, tab.capability))
              .map((tab) => (
                <li key={tab.id}>
                  <Link
                    to={tab.to}
                    aria-current={tab.id === section ? 'page' : undefined}
                    className={tab.id === section ? styles.primaryLink : styles.secondaryButton}
                    data-testid={`operations-tab-${tab.id}`}
                  >
                    {tab.label}
                  </Link>
                </li>
              ))}
          </ul>
        </nav>
        <label htmlFor="operations-work-search">
          {T.filter}
          <input
            id="operations-work-search"
            type="search"
            value={search}
            aria-describedby="operations-work-search-hint"
            onChange={(event) => {
              const value = event.target.value
              // Kept in the URL (replacing the entry, not stacking one per keystroke): back from an owner screen restores it.
              setParams(
                (current) => {
                  const next = new URLSearchParams(current)
                  if (value === '') next.delete(SEARCH_PARAM)
                  else next.set(SEARCH_PARAM, value)
                  return next
                },
                { replace: true },
              )
            }}
          />
        </label>
        <p id="operations-work-search-hint" className={styles.muted}>
          {T.filterHint}
        </p>
      </header>
      <p className={styles.muted} data-testid="operations-section-intro">
        {section === 'sales' ? T.salesIntro : T.purchaseIntro}
      </p>
      {children}
    </section>
  )
}

function useSearch(): string {
  const [params] = useSearchParams()
  return (params.get(SEARCH_PARAM) ?? '').trim().toLocaleLowerCase('tr-TR')
}

const matches = (search: string, ...values: readonly string[]) =>
  search === '' || values.some((value) => value.toLocaleLowerCase('tr-TR').includes(search))

function Continuation({
  nextCursor,
  continuation,
  loadMore,
}: {
  readonly nextCursor: string | null
  readonly continuation: 'idle' | 'loading' | RemoteViewState<unknown>
  readonly loadMore: () => Promise<void>
}) {
  return (
    <div className={styles.continuation}>
      {typeof continuation === 'object' ? (
        <div className={styles.inlineError}>
          <p>{T.nextPageFailed}</p>
          <StateBlock state={continuation} subject="sonraki sayfa" onRetry={() => void loadMore()} />
        </div>
      ) : nextCursor === null ? (
        <p className={styles.muted}>{T.end}</p>
      ) : (
        <button
          type="button"
          className={styles.secondaryButton}
          onClick={() => void loadMore()}
          disabled={continuation === 'loading'}
          aria-busy={continuation === 'loading'}
        >
          {continuation === 'loading' ? T.loadingMore : T.loadMore}
        </button>
      )}
    </div>
  )
}

/** The rows' state before a table can be shown: loading / error / empty — or nothing when there is a table to show. */
function Pending({
  firstData,
  loaded,
  total,
  shown,
  searching,
  subject,
  onRetry,
}: {
  readonly firstData: ViewOutcome<unknown> | undefined
  readonly loaded: boolean
  readonly total: number
  readonly shown: number
  readonly searching: boolean
  readonly subject: string
  readonly onRetry: () => void
}) {
  if (!loaded) {
    const state: RemoteViewState<unknown> =
      firstData === undefined ? { kind: 'initial-loading' } : (classifyOutcome(firstData) as RemoteViewState<unknown>)
    return <StateBlock state={state} subject={subject} onRetry={onRetry} />
  }
  if (total === 0) return <StateBlock state={{ kind: 'empty-no-data' }} subject={subject} />
  if (shown === 0 && searching) return <StateBlock state={{ kind: 'empty-filtered' }} subject={subject} />
  return null
}

/* ── L-01: the sales section ──────────────────────────────────────────────── */

function SalesWork({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const work = useWorkPages<SalesWorkRow>(namespace, 'sales', fetchSalesWork)
  const search = useSearch()
  const canOpenOrder = hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const shown = work.rows.filter((row) =>
    matches(search, row.documentNo, row.customerName, row.productCode, row.productName),
  )
  const pending = (
    <Pending
      firstData={work.first.data}
      loaded={work.firstPage !== null}
      total={work.rows.length}
      shown={shown.length}
      searching={search !== ''}
      subject={T.salesSubject}
      onRetry={() => void work.first.refetch()}
    />
  )
  return (
    <Frame section="sales">
      {work.firstPage === null || work.rows.length === 0 || shown.length === 0 ? (
        pending
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="operations-sales-table">
            <caption className={styles.caption}>{T.salesCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{T.order}</th>
                <th scope="col" className={styles.numeric}>{T.line}</th>
                <th scope="col">{T.customer}</th>
                <th scope="col">{T.product}</th>
                <th scope="col">{T.unit}</th>
                <th scope="col" className={styles.numeric}>{T.ordered}</th>
                <th scope="col" className={styles.numeric}>{T.instructed}</th>
                <th scope="col" className={styles.numeric}>{T.loaded}</th>
                <th scope="col" className={styles.numeric}>{T.shipped}</th>
                <th scope="col" className={styles.numeric}>{T.delivered}</th>
                <th scope="col">{T.requestedDelivery}</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((row) => (
                <tr key={row.lineId} data-testid={`operations-sales-row-${row.lineId}`}>
                  <th scope="row">
                    {canOpenOrder ? <Link to={salesDetailPath(row.orderId)}>{row.documentNo}</Link> : row.documentNo}
                  </th>
                  <td className={styles.numeric}>{row.lineNo}</td>
                  <td>{row.customerName}</td>
                  <td>
                    {row.productCode} — {row.productName}
                  </td>
                  <td>{row.unitCode}</td>
                  <td className={styles.numeric}>{row.ordered}</td>
                  <td className={styles.numeric}>{row.instructed}</td>
                  <td className={styles.numeric}>{row.loaded}</td>
                  <td className={styles.numeric}>{row.shipped}</td>
                  <td className={styles.numeric}>{row.delivered}</td>
                  <td>{row.requestedDeliveryDate === null ? T.noDate : formatBusinessDate(row.requestedDeliveryDate)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {work.firstPage !== null && work.rows.length > 0 ? (
        <Continuation nextCursor={work.nextCursor} continuation={work.continuation} loadMore={work.loadMore} />
      ) : null}
    </Frame>
  )
}

/* ── L-09: the purchase section ───────────────────────────────────────────── */

const shortRef = (id: string) => `…${id.slice(-8)}`

function PurchaseWork({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const work = useWorkPages<PurchaseWorkRow>(namespace, 'purchase', fetchPurchaseWork)
  const search = useSearch()
  const canOpenOrder = hasCapability(shell, PURCHASING_CAPABILITY.orderDetail)
  const shown = work.rows.filter((row) => matches(search, row.orderId, row.supplierName, row.productId))
  const pending = (
    <Pending
      firstData={work.first.data}
      loaded={work.firstPage !== null}
      total={work.rows.length}
      shown={shown.length}
      searching={search !== ''}
      subject={T.purchaseSubject}
      onRetry={() => void work.first.refetch()}
    />
  )
  return (
    <Frame section="purchase">
      {work.firstPage === null || work.rows.length === 0 || shown.length === 0 ? (
        pending
      ) : (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="operations-purchase-table">
            <caption className={styles.caption}>{T.purchaseCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{T.order}</th>
                <th scope="col">{T.effectiveOn}</th>
                <th scope="col" className={styles.numeric}>{T.line}</th>
                <th scope="col">{T.kind}</th>
                <th scope="col">{T.supplier}</th>
                <th scope="col">{T.product}</th>
                <th scope="col">{T.unit}</th>
                <th scope="col" className={styles.numeric}>{T.committed}</th>
                <th scope="col" className={styles.numeric}>{T.accepted}</th>
                <th scope="col" className={styles.numeric}>{T.remaining}</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((row) => {
                const label = `${T.purchaseOrder} ${shortRef(row.orderId)}`
                return (
                  <tr key={row.lineId} data-testid={`operations-purchase-row-${row.lineId}`}>
                    <th scope="row" className={styles.identifier}>
                      {canOpenOrder ? <Link to={purchaseOrderDetailPath(row.orderId)}>{label}</Link> : label}
                    </th>
                    <td>{formatBusinessDate(row.orderEffectiveOn)}</td>
                    <td className={styles.numeric}>{row.lineNo}</td>
                    <td>{PURCHASE_LINE_KIND_TEXT[row.kind] ?? row.kind}</td>
                    <td>{row.supplierName}</td>
                    <td className={styles.identifier}>{shortRef(row.productId)}</td>
                    <td className={styles.identifier}>{shortRef(row.unitId)}</td>
                    <td className={styles.numeric}>{row.committed}</td>
                    <td className={styles.numeric}>{row.accepted}</td>
                    <td className={styles.numeric}>{row.remaining}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
      {work.firstPage !== null && work.rows.length > 0 ? (
        <Continuation nextCursor={work.nextCursor} continuation={work.continuation} loadMore={work.loadMore} />
      ) : null}
    </Frame>
  )
}
