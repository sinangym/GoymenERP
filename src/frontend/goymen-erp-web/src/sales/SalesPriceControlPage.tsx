/**
 * UI-50 / S-05 "Fiyat–İskonto–Marj–Risk Kontrolü" (master report §27.2, SATIS_HAZIRLIK_PLANI_v1 UI-50): a READ-ONLY
 * comparison of one sales order draft, under the S-04 detail's own published id. It decides nothing: approve, reject and
 * every price change stay on S-04, with that screen's authority and separation of duties.
 *
 * Every figure is the server's, read from the endpoints that exist on main:
 *   GET /api/v1/sales/drafts/{id}               → the draft and its lines (company-bound)
 *   GET /api/v1/sales/drafts/{id}/line-pricing  → reference / floor / unit price / discount / surcharge / net, the floor
 *                                                 exception and the discount-limit check (price read only)
 *   GET /api/v1/sales/drafts/{id}/totals        → the order totals card (reused from S-04)
 *   GET /api/v1/sales/drafts/{id}/risk-check    → the customer risk card (reused from S-04)
 * The screen never computes a price, a net, a total, a margin or a risk; it only counts the server's pending exceptions.
 *
 * MARGIN (SAT-D250 / SAT-D262…D267): the line-pricing read carries the server's margin block. `Withheld` (no
 * `sales.orders.margin.read`) shows no button and no figure; `Visible` puts it behind "Marjı göster", labelled "yalnız alış
 * maliyeti" and "tahmini / önizleme", one row per line from the server's one calculator — an unavailable margin reads
 * "kullanılamıyor" with the server's reason, never zero. The unit cost is the company-wide (all warehouses) weighted
 * moving-average purchase cost that Stok supplies through its versioned read port (SAT-D272; orders carry no warehouse); until
 * that port exists every line is unavailable (`CostSourceMissing`).
 */

import type { ReactNode } from 'react'
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchDraftDetail } from './api'
import type { DraftDetail, DraftLine } from './contract'
import type { DiscountLimitCheck, DraftMargin, LinePricing } from './linePricing'
import { salesDetailPath } from './capabilities'
import { PRICE_CONTROL_TEXT as T } from './priceControlText'
import { SalesOrderTotalsCard } from './SalesOrderTotalsCard'
import { SalesOrderRiskCard } from './SalesOrderRiskCard'
import { useDraftLinePricing } from './useDraftLinePricing'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, type ShellState } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

export function SalesPriceControlPage() {
  const shell = useShellState()
  const { draftId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()

  // The S-04 detail's own key: the two screens share one read of the same draft in the same context entry.
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft', draftId ?? ''],
    enabled: namespace !== null && companyId !== null && (draftId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftDetail(draftId as string, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const backLink = (draftId ?? '').length > 0 ? (
    <Link className={styles.backLink} to={salesDetailPath(draftId as string)} data-testid="sales-price-control-back">
      ← {T.backToOrder}
    </Link>
  ) : null

  const outcome = query.data
  // A draft of another company is not this company's record: fail closed, never painted.
  const state: RemoteViewState<unknown> | null =
    outcome === undefined ? { kind: 'initial-loading' }
      : outcome.type !== 'ok-single' ? (classifyOutcome(outcome) as RemoteViewState<unknown>)
        : outcome.data.draft.companyId !== companyId ? { kind: 'malformed-response' }
          : null
  if (state !== null || outcome?.type !== 'ok-single' || namespace === null || companyId === null) {
    return (
      <section className={styles.page} aria-label={T.pageLabel}>
        {backLink}
        <StateBlock state={state ?? { kind: 'initial-loading' }} subject={T.stateSubject} onRetry={() => void query.refetch()} />
      </section>
    )
  }
  const detail = outcome.data
  return (
    <PriceControlView
      key={`price-control:${namespace}:${shell.routeEntryGeneration}:${detail.draft.id}`}
      shell={shell}
      detail={detail}
      companyId={companyId}
      backLink={backLink}
      onRefreshDetail={() => void query.refetch()}
    />
  )
}

function PriceControlView({ shell, detail, companyId, backLink, onRefreshDetail }: {
  readonly shell: ShellState
  readonly detail: DraftDetail
  readonly companyId: string
  readonly backLink: ReactNode
  readonly onRefreshDetail: () => void
}) {
  const pricing = useDraftLinePricing(shell, detail, companyId)
  const pricedLines = pricing.data?.type === 'ok-single' ? pricing.data.data : null
  // The pricing answer must describe exactly THIS version's lines; otherwise it is not this draft's comparison.
  const stale = pricedLines !== null && pricedLines.version !== detail.draft.version
  const bound = pricedLines !== null && !stale && sameLines(detail.lines, pricedLines.lines)

  return (
    <article className={styles.page} aria-labelledby="sales-price-control-heading">
      {backLink}
      <header className={styles.header}>
        <div>
          <h1 id="sales-price-control-heading" className={styles.title}>{T.heading(detail.draft.documentNo)}</h1>
          <p className={styles.muted} data-testid="sales-price-control-note">{T.note}</p>
        </div>
      </header>

      <section className={styles.section} aria-labelledby="sales-price-control-lines-heading" data-testid="sales-price-control-lines">
        <h2 id="sales-price-control-lines-heading">{T.lines.heading}</h2>
        <p className={styles.muted}>{T.lines.note}</p>
        {detail.lines.length === 0 ? (
          <StateBlock state={{ kind: 'empty-no-data' }} subject={T.stateSubject} />
        ) : detail.lines.some((line) => line.priceDisclosure === 'Withheld') ? (
          <p className={styles.muted} data-testid="sales-price-control-withheld">{T.lines.withheld}</p>
        ) : pricing.data === undefined ? (
          <StateBlock state={{ kind: 'initial-loading' }} subject={T.stateSubject} />
        ) : pricing.data.type !== 'ok-single' ? (
          <div data-testid="sales-price-control-unreadable">
            <p className={styles.muted}>{T.lines.unreadable}</p>
            <StateBlock
              state={classifyOutcome(pricing.data) as RemoteViewState<unknown>}
              subject={T.stateSubject}
              onRetry={() => void pricing.refetch()}
            />
          </div>
        ) : stale ? (
          <div className={styles.notice} data-tone="unknown" role="status" data-testid="sales-price-control-stale">
            <p>{T.lines.stale}</p>
            <Button type="button" variant="secondary" onClick={() => { onRefreshDetail(); void pricing.refetch() }}>
              {T.lines.refresh}
            </Button>
          </div>
        ) : !bound ? (
          <div data-testid="sales-price-control-unreadable">
            <StateBlock state={{ kind: 'malformed-response' }} subject={T.stateSubject} onRetry={() => void pricing.refetch()} />
          </div>
        ) : (
          <>
            <PendingSummary lines={pricedLines.lines} />
            <ComparisonTable detail={detail} lines={pricedLines.lines} />
          </>
        )}
      </section>

      <section className={styles.section} aria-labelledby="sales-price-control-margin-heading" data-testid="sales-price-control-margin">
        <h2 id="sales-price-control-margin-heading">{T.margin.heading}</h2>
        {bound && pricedLines !== null
          ? <MarginSection margin={pricedLines.margin} />
          : <p className={styles.muted} data-testid="sales-price-control-margin-not-read">{T.margin.notRead}</p>}
      </section>

      <SalesOrderTotalsCard detail={detail} />
      <SalesOrderRiskCard detail={detail} />
    </article>
  )
}

/** The server's margin block as it says it: withheld ⇒ no button; visible ⇒ behind "Marjı göster", nothing computed here. */
function MarginSection({ margin }: { readonly margin: DraftMargin }) {
  const [open, setOpen] = useState(false)
  if (margin.disclosure === 'Withheld') {
    return <p className={styles.muted} data-testid="sales-price-control-margin-withheld">{T.margin.withheld}</p>
  }
  const ordered = [...margin.lines].sort((a, b) => a.lineNo - b.lineNo)
  return (
    <>
      <Button type="button" variant="secondary" aria-expanded={open} aria-controls="sales-price-control-margin-preview"
        data-testid="sales-price-control-margin-toggle" onClick={() => setOpen((value) => !value)}>
        {open ? T.margin.hide : T.margin.show}
      </Button>
      {open ? (
        <div id="sales-price-control-margin-preview" data-testid="sales-price-control-margin-preview">
          <ul className={styles.muted} aria-label={T.margin.heading} data-testid="sales-price-control-margin-labels">
            <li data-testid="sales-price-control-margin-cost-basis">{T.margin.costBasis}</li>
            <li data-testid="sales-price-control-margin-kind">{T.margin.kind}</li>
          </ul>
          <p className={styles.muted}>{T.margin.note}</p>
          <div className={styles.tableWrap}>
            <table className={styles.table} data-testid="sales-price-control-margin-table">
              <caption className={styles.caption}>{T.margin.caption}</caption>
              <thead>
                <tr>
                  <th scope="col">{T.margin.line}</th>
                  <th scope="col">{T.margin.state}</th>
                  <th scope="col" className={styles.numeric}>{T.margin.amount}</th>
                  <th scope="col" className={styles.numeric}>{T.margin.percent}</th>
                </tr>
              </thead>
              <tbody>
                {ordered.map((line) => (
                  <tr key={line.lineId} data-testid={`sales-price-control-margin-line-${line.lineNo}`}>
                    <th scope="row">{line.lineNo}</th>
                    {line.state === 'Unavailable' ? (
                      <>
                        <td data-testid={`sales-price-control-margin-line-${line.lineNo}-state`}>
                          {`${T.margin.unavailable}: ${T.margin.reason[line.unavailableReason]}`}
                        </td>
                        <td className={styles.numeric}>{T.status.none}</td>
                        <td className={styles.numeric}>{T.status.none}</td>
                      </>
                    ) : (
                      <>
                        <td data-testid={`sales-price-control-margin-line-${line.lineNo}-state`}>{T.margin.computed}</td>
                        <td className={styles.numeric} data-testid={`sales-price-control-margin-line-${line.lineNo}-amount`}>{line.amount}</td>
                        <td className={styles.numeric} data-testid={`sales-price-control-margin-line-${line.lineNo}-percent`}>
                          {line.percent === null ? T.margin.noPercent : T.margin.percentValue(line.percent)}
                        </td>
                      </>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}
    </>
  )
}

/** Same line ids, each once: a pricing line of another draft (or a missing line) is not this draft's comparison. */
function sameLines(detailLines: readonly DraftLine[], priced: readonly LinePricing[]): boolean {
  if (detailLines.length !== priced.length) return false
  const ids = new Set(detailLines.map((line) => line.lineId))
  const seen = new Set<string>()
  for (const line of priced) {
    if (!ids.has(line.lineId) || seen.has(line.lineId)) return false
    seen.add(line.lineId)
  }
  return true
}

const isFloorPending = (line: LinePricing) => line.floorException !== null && line.floorException.approvedBy === null
const isDiscountPending = (line: LinePricing) =>
  line.discountCheck !== null && line.discountCheck.requiresApproval && line.discountCheck.approvedBy === null

/** Counts of the server's own pending flags — a count, never a price. */
function PendingSummary({ lines }: { readonly lines: readonly LinePricing[] }) {
  return (
    <dl className={styles.facts} aria-label={T.summary.heading} data-testid="sales-price-control-summary">
      <div>
        <dt>{T.summary.floorPending}</dt>
        <dd data-testid="sales-price-control-floor-pending-count">{lines.filter(isFloorPending).length}</dd>
      </div>
      <div>
        <dt>{T.summary.discountPending}</dt>
        <dd data-testid="sales-price-control-discount-pending-count">{lines.filter(isDiscountPending).length}</dd>
      </div>
    </dl>
  )
}

function adjustment(percent: string | null, amount: string | null): string {
  if (percent !== null) return T.percent(percent)
  if (amount !== null) return T.amount(amount)
  return T.status.none
}

function floorStatus(line: LinePricing): string {
  if (line.floorException === null) return T.status.none
  return line.floorException.approvedBy === null ? T.status.floorPending : T.status.floorApproved
}

/** The server's check, as it says it: no limit is applied or compared here. */
function discountStatus(check: DiscountLimitCheck | null): string {
  if (check === null) return T.status.none
  if (!check.requiresApproval) return check.limitPercent === null ? T.status.none : T.status.discountWithin(check.limitPercent)
  if (check.approvedBy !== null) return T.status.discountApproved
  if (check.listUnitPrice === null) return T.status.discountUnmeasured
  if (check.limitPercent === null) return T.status.discountNoLimit
  return T.status.discountOverPending(check.limitPercent)
}

function ComparisonTable({ detail, lines }: { readonly detail: DraftDetail; readonly lines: readonly LinePricing[] }) {
  const byId = new Map(detail.lines.map((line) => [line.lineId, line]))
  const ordered = [...lines].sort((a, b) => a.lineNo - b.lineNo)
  return (
    <div className={styles.tableWrap}>
      <table className={styles.table} data-testid="sales-price-control-table">
        <caption className={styles.caption}>{T.lines.caption}</caption>
        <thead>
          <tr>
            <th scope="col">{T.lines.line}</th>
            <th scope="col">{T.lines.product}</th>
            <th scope="col" className={styles.numeric}>{T.lines.quantity}</th>
            <th scope="col" className={styles.numeric}>{T.lines.reference}</th>
            <th scope="col" className={styles.numeric}>{T.lines.floor}</th>
            <th scope="col" className={styles.numeric}>{T.lines.unitPrice}</th>
            <th scope="col">{T.lines.source}</th>
            <th scope="col">{T.lines.discount}</th>
            <th scope="col">{T.lines.surcharge}</th>
            <th scope="col" className={styles.numeric}>{T.lines.net}</th>
            <th scope="col">{T.lines.floorStatus}</th>
            <th scope="col">{T.lines.discountStatus}</th>
          </tr>
        </thead>
        <tbody>
          {ordered.map((line) => {
            const order = byId.get(line.lineId) as DraftLine
            const reference = line.priceReference
            const currency = reference?.currencyCode ?? null
            const money = (value: string | null) =>
              value === null ? T.status.none : currency === null ? value : T.priceWithCurrency(value, currency)
            return (
              <tr key={line.lineId} data-testid={`sales-price-control-line-${line.lineNo}`}>
                <th scope="row">{line.lineNo}</th>
                <td>{`${order.productCode} · ${order.productName}`}</td>
                <td className={styles.numeric}>{order.orderUnitCode === undefined ? order.quantity : `${order.quantity} ${order.orderUnitCode}`}</td>
                <td className={styles.numeric} data-testid={`sales-price-control-line-${line.lineNo}-reference`}>{money(reference?.unitPrice ?? null)}</td>
                <td className={styles.numeric} data-testid={`sales-price-control-line-${line.lineNo}-floor`}>{money(reference?.floorUnitPrice ?? null)}</td>
                <td className={styles.numeric} data-testid={`sales-price-control-line-${line.lineNo}-unit-price`}>{money(line.unitPrice)}</td>
                <td>{line.priceSource === null ? T.status.none : T.source[line.priceSource]}</td>
                <td data-testid={`sales-price-control-line-${line.lineNo}-discount`}>{adjustment(line.discountPercent, line.discountAmount)}</td>
                <td>{adjustment(line.surchargePercent, line.surchargeAmount)}</td>
                <td className={styles.numeric} data-testid={`sales-price-control-line-${line.lineNo}-net`}>{money(line.netUnitPrice)}</td>
                <td data-testid={`sales-price-control-line-${line.lineNo}-floor-status`}>{floorStatus(line)}</td>
                <td data-testid={`sales-price-control-line-${line.lineNo}-discount-status`}>{discountStatus(line.discountCheck)}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
