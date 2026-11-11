import { Fragment, useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { approvePurchaseOrder, type ApprovalOutcome } from './approvalApi'
import { fetchPurchaseOrder } from './api'
import { purchaseRequestDetailPath, PURCHASING_CAPABILITY } from './capabilities'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { pendingApprovalKey, recallPendingApproval, releasePendingApproval, reservePendingApproval, type ApprovalSnapshot } from './pendingApprovalStore'
import styles from './PurchaseOrderDetailPage.module.css'
import { GoodsReceiptDetailRow } from './GoodsReceiptDetail'
import { GoodsReceiptPanel } from './GoodsReceiptPanel'
import { receiptAttemptBinding } from './goodsReceiptAttempt'
import { GOODS_RECEIPT_TEXT } from './goodsReceiptText'
import { PURCHASE_GOODS_RECEIPT_CAPABILITY } from './goodsReceiptContract'
import { ORDER_APPROVAL_PROBLEM_TEXT, PURCHASE_ORDER_DETAIL_TEXT as T } from './purchaseOrderDetailText'
import { PurchaseOrderSupplierIdentity } from './PurchaseOrderListPage'
import { CAPABILITY } from '../shell/capabilities'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { OWNER_KIND } from '../documents/contract'
import { Button } from '../ui/Button'
import { StatusIndicator } from '../ui/StatusIndicator'
import { PurchaseOrderCancellationPanel } from './PurchaseOrderCancellationPanel'
import { ORDER_CANCELLATION_TEXT, PURCHASE_ORDER_STATUS_LABEL } from './purchasingText'
import { PURCHASE_ORDER_STATUS_TONE } from './purchaseOrderStatus'
import type { PurchaseOrderCurrency, PurchaseOrderLine } from './contract'

const HEADING_ID = 'purchase-order-detail-heading'

/**
 * Presentation only: the wire's `yyyy-MM-dd` business date in Turkish order, as the order list shows it. The value
 * is not reinterpreted — anything that is not a wire date is shown as received.
 */
function formatBusinessDate(value: string): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  return parts === null ? value : `${parts[3]}.${parts[2]}.${parts[1]}`
}

/**
 * One real table for both widths. The roles are written on the elements because below 768 px the stylesheet draws
 * each row as a labelled card (`data-label` before each value) and a restyled table would otherwise lose its
 * semantics; the cells stay cells for assistive technology and for the acceptance specs alike.
 */
function RecordTable({ label, columns, children }: {
  readonly label: string
  readonly columns: readonly { readonly name: string; readonly numeric?: boolean }[]
  readonly children: ReactNode
}) {
  return <div className={styles.tableWrap}>
    <table className={styles.table} role="table" aria-label={label}>
      <thead role="rowgroup"><tr role="row">{columns.map((column) =>
        <th key={column.name} role="columnheader" scope="col" className={column.numeric ? styles.number : undefined}>{column.name}</th>)}</tr></thead>
      <tbody role="rowgroup">{children}</tbody>
    </table>
  </div>
}

function Cell({ label, numeric, identifier, children }: {
  readonly label: string
  readonly numeric?: boolean
  readonly identifier?: boolean
  readonly children: ReactNode
}) {
  return <td role="cell" data-label={label} className={numeric ? styles.number : identifier ? styles.identifier : undefined}>{children}</td>
}

function newId(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') return globalThis.crypto.randomUUID()
  const bytes = Array.from({ length: 16 }, () => Math.floor(Math.random() * 256))
  bytes[6] = (bytes[6] & 0x0f) | 0x40; bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = bytes.map((value) => value.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

const LINE_NO = T.lineNoColumn
/** A1-3 (No.161 §2): the cancelled remainder sits between the accepted share and the remaining future supply (INV-006). */
const LINE_COLUMNS = [
  { name: LINE_NO }, { name: T.productId }, { name: T.unitId },
  { name: T.ordered, numeric: true }, { name: T.technical, numeric: true }, { name: T.committed, numeric: true },
  { name: T.accepted, numeric: true }, { name: T.cancelledShare, numeric: true }, { name: T.remaining, numeric: true },
] as const
/** A2a-1 (API-20): drawn only when the server discloses the prices (PRS-02); the values are the server's exact texts. */
const PRICE_COLUMNS = [
  { name: T.unitPrice, numeric: true }, { name: T.discount, numeric: true }, { name: T.netUnitPrice, numeric: true },
  { name: T.lineAmount, numeric: true },
] as const

/** A2a-1: one line's price cells; a general line without a price is "fiyat bekliyor", a technical line is priced on its general line. */
function PriceCells({ line, currency }: { readonly line: PurchaseOrderLine; readonly currency: PurchaseOrderCurrency | null }) {
  const price = line.price ?? null
  if (price === null || currency === null) {
    const text = line.kind === 'Technical' ? T.technicalPriceCell : T.pricePendingCell
    return <>{PRICE_COLUMNS.map((column) => <Cell key={column.name} label={column.name} numeric>{text}</Cell>)}</>
  }
  const discount = price.discountPercent !== null
    ? T.discountPercentValue(price.discountPercent)
    : price.discountAmount !== null ? T.money(price.discountAmount, currency.currencyCode) : T.noDiscount
  // A2a-2 (ALS-D38): the maturity surcharge is shown as given, beside the discount; the net and the amount already include it.
  const deferral = price.deferralPercent != null
    ? T.deferral(T.discountPercentValue(price.deferralPercent))
    : price.deferralAmount != null ? T.deferral(T.money(price.deferralAmount, currency.currencyCode)) : null
  return <>
    <Cell label={T.unitPrice} numeric>{T.money(price.unitPrice, currency.currencyCode)}</Cell>
    <Cell label={T.discount} numeric>{deferral === null ? discount : `${discount} · ${deferral}`}</Cell>
    <Cell label={T.netUnitPrice} numeric>{T.money(price.netUnitPrice, currency.currencyCode)}</Cell>
    <Cell label={T.lineAmount} numeric>{T.money(price.lineAmount, currency.currencyCode)}</Cell>
  </>
}

const ACCEPTANCE_COLUMNS = [
  { name: T.acceptanceId }, { name: T.lineNo }, { name: T.quantity, numeric: true }, { name: T.recordedAtUtc },
] as const

export function PurchaseOrderDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { orderId } = useParams()
  return <PurchaseOrderDetail key={`${namespace ?? 'no-context'}::${orderId ?? ''}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function PurchaseOrderDetail({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const { orderId } = useParams()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const canApprove = hasCapability(shell, PURCHASING_CAPABILITY.orderApprove)
  const canCancel = hasCapability(shell, PURCHASING_CAPABILITY.orderCancel)
  const canReadSourceRequest = hasCapability(shell, PURCHASING_CAPABILITY.requestDetail)
  const canOpenParty = hasCapability(shell, CAPABILITY.cariPartiesDetail)
  const storeKey = pendingApprovalKey(shell.sessionEpoch, actorId, companyId, orderId ?? '')
  // BLG-01: the receipt attempt's identity, NOT the query namespace — see goodsReceiptAttempt.ts.
  const receiptAttempt = receiptAttemptBinding(shell)
  const [attempt, setAttempt] = useState<
    | { readonly phase: 'idle' }
    | { readonly phase: 'submitting'; readonly snapshot: ApprovalSnapshot }
    | { readonly phase: 'unknown'; readonly snapshot: ApprovalSnapshot; readonly outcome: Extract<ApprovalOutcome, { type: 'unknown' }> }
    | { readonly phase: 'applied'; readonly replayed: boolean }
    | { readonly phase: 'failed'; readonly status: number; readonly code: string | null }
  >(() => {
    const snapshot = recallPendingApproval(storeKey)
    return snapshot === null ? { phase: 'idle' } : { phase: 'unknown', snapshot, outcome: { type: 'unknown', reason: 'network-error' } }
  })
  const inFlight = useRef(false)
  const responseGeneration = useRef(0)
  useEffect(() => {
    inFlight.current = false
    return () => { responseGeneration.current += 1 }
  }, [namespace, shell.routeEntryGeneration])
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'purchase-order', orderId ?? ''],
    enabled: namespace !== null && companyId !== null && (orderId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchPurchaseOrder(companyId ?? '', orderId ?? '', signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<import('./contract').PurchaseOrderDetail>
  const sendApproval = useCallback(async (retry?: ApprovalSnapshot) => {
    if (state.kind !== 'ready' || companyId === null || actorId === null || orderId === undefined || inFlight.current) return
    const approvalSnapshot: ApprovalSnapshot = retry ?? {
      actorId, companyId, orderId, expectedVersion: state.data.order.version, idempotencyKey: newId(),
    }
    if (approvalSnapshot.actorId !== actorId || approvalSnapshot.companyId !== companyId || approvalSnapshot.orderId !== orderId) return
    inFlight.current = true
    const generation = ++responseGeneration.current
    reservePendingApproval(storeKey, approvalSnapshot)
    setAttempt({ phase: 'submitting', snapshot: approvalSnapshot })
    const securitySnapshot = boundary.capture()
    const result = await approvePurchaseOrder(
      companyId,
      orderId,
      approvalSnapshot.expectedVersion,
      approvalSnapshot.idempotencyKey,
    )
    if (responseGeneration.current !== generation) return
    inFlight.current = false
    if (result.type === 'failed' && (result.status === 401 || result.status === 403)) boundary.report(result.status, securitySnapshot)
    if (result.type === 'unknown') {
      setAttempt({ phase: 'unknown', snapshot: approvalSnapshot, outcome: result })
      return
    }
    releasePendingApproval(storeKey, approvalSnapshot.idempotencyKey)
    setAttempt(result.type === 'applied'
      ? { phase: 'applied', replayed: result.replayed }
      : { phase: 'failed', status: result.status, code: result.code })
    if (result.type === 'applied' || result.status === 409) await query.refetch()
  }, [actorId, boundary, companyId, orderId, query, state, storeKey])

  const frame = (child: ReactNode, status?: ReactNode) => <section className={styles.page} aria-labelledby={HEADING_ID}>
    <header className={styles.header}>
      <div className={styles.headerCopy}>
        <p className={styles.eyebrow}>{T.eyebrow}</p>
        <h1 id={HEADING_ID} className={styles.title}>{T.heading}</h1>
      </div>
      {status}
    </header>
    {child}
  </section>
  if (companyId === null) return frame(<StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />)
  if (state.kind !== 'ready') return frame(<StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />)
  const { order, lines, acceptances } = state.data
  const cancellation = state.data.cancellation ?? null
  // A2a-1 (PRS-02): prices are drawn only when the server says Visible; a body from before A2a-1 carries none.
  const pricesVisible = state.data.priceDisclosure === 'Visible'
  const currency = state.data.currency ?? null
  const isCurrentDetail = order.id === orderId && order.companyId === companyId
  const lineNoById = new Map(lines.map((line) => [line.lineId, line.lineNo] as const))
  const unresolved = attempt.phase === 'unknown' || attempt.phase === 'submitting'
  const outcomeNotice = attempt.phase === 'applied'
    ? <p role="status" className={styles.success}>{attempt.replayed ? T.approvalReplayed : T.approvalApplied}</p>
    : attempt.phase === 'failed'
      ? <p role="alert" className={styles.error}>{attempt.code !== null && Object.hasOwn(ORDER_APPROVAL_PROBLEM_TEXT, attempt.code)
        ? T.approvalRefused(ORDER_APPROVAL_PROBLEM_TEXT[attempt.code], attempt.code)
        : T.approvalFailed(attempt.code ?? attempt.status)}</p>
      : null
  return frame(<>
    <section className={styles.section} aria-labelledby="purchase-order-summary-heading" data-testid="purchase-order-summary">
      <h2 id="purchase-order-summary-heading">{T.summaryHeading}</h2>
      <dl className={styles.facts} data-testid="purchase-order-status-facts">
        <div><dt>{T.orderId}</dt><dd className={styles.identifier}>{order.id}</dd></div>
        <div><dt>{T.version}</dt><dd>{order.version}</dd></div>
        <div><dt>{T.businessDate}</dt><dd><time dateTime={order.effectiveOn}>{formatBusinessDate(order.effectiveOn)}</time></dd></div>
        {state.data.priceDisclosure === undefined ? null : <div><dt>{T.currency}</dt><dd>{currency === null ? T.currencyNone : currency.currencyCode}</dd></div>}
        <div><dt>{T.sourceRequest}</dt><dd className={styles.identifier}>{order.sourceRequestId === null ? T.sourceNotLinked : canReadSourceRequest && namespace !== null && order.id === orderId && order.companyId === companyId ? <Link className={styles.link} to={purchaseRequestDetailPath(order.sourceRequestId)} state={{ ns: namespace, requestId: order.sourceRequestId, orderId: order.id }}>{order.sourceRequestId}</Link> : order.sourceRequestId}</dd></div>
      </dl>
      <h3>{T.supplierHeading}</h3>
      <dl className={styles.facts} data-testid="purchase-order-supplier-facts">
        <div data-testid="purchase-order-supplier-identity"><dt>{T.supplierIdentity}</dt><dd><PurchaseOrderSupplierIdentity order={order} canOpenParty={canOpenParty && isCurrentDetail} /></dd></div>
        <div><dt>{T.supplierPartyId}</dt><dd className={styles.identifier}>{order.supplierPartyId}</dd></div>
        <div><dt>{T.supplierAccountId}</dt><dd className={styles.identifier}>{order.companyAccountId}</dd></div>
      </dl>
    </section>
    <section className={styles.section} aria-labelledby="purchase-order-lines-heading" data-testid="purchase-order-lines">
      <h2 id="purchase-order-lines-heading">{T.linesHeading}</h2>
      {state.data.priceDisclosure === 'Withheld' ? <p data-testid="purchase-order-prices-withheld">{T.pricesWithheld}</p> : null}
      {pricesVisible && state.data.pricePending === true ? <p role="status" className={styles.warning} data-testid="purchase-order-price-pending">{T.pricePending}</p> : null}
      <RecordTable label={T.linesCaption} columns={pricesVisible ? [...LINE_COLUMNS, ...PRICE_COLUMNS] : LINE_COLUMNS}>
        {lines.map((line) => <tr key={line.lineId} role="row" data-testid={`purchase-order-line-${line.lineNo}`}>
          <Cell label={LINE_NO}>{line.lineNo}</Cell>
          <Cell label={T.productId} identifier>{line.productId}</Cell>
          <Cell label={T.unitId} identifier>{line.unitId}</Cell>
          <Cell label={T.ordered} numeric>{line.ordered}</Cell>
          <Cell label={T.technical} numeric>{line.technical}</Cell>
          <Cell label={T.committed} numeric>{line.committed}</Cell>
          <Cell label={T.accepted} numeric>{line.accepted}</Cell>
          <Cell label={T.cancelledShare} numeric>{line.cancelled ?? '0'}</Cell>
          <Cell label={T.remaining} numeric>{line.remaining}</Cell>
          {pricesVisible ? <PriceCells line={line} currency={currency} /> : null}
        </tr>)}
      </RecordTable>
    </section>
    {isCurrentDetail && cancellation !== null ? <section className={styles.section} aria-labelledby="purchase-order-cancellation-facts-heading" data-testid="purchase-order-cancellation-facts">
      <h2 id="purchase-order-cancellation-facts-heading">{ORDER_CANCELLATION_TEXT.cancellationHeading}</h2>
      <dl className={styles.facts}>
        <div><dt>{ORDER_CANCELLATION_TEXT.cancelledBy}</dt><dd className={styles.identifier}>{cancellation.cancelledBy}</dd></div>
        <div><dt>{ORDER_CANCELLATION_TEXT.cancelledAt}</dt><dd><time dateTime={cancellation.cancelledAtUtc}>{cancellation.cancelledAtUtc}</time></dd></div>
        <div><dt>{ORDER_CANCELLATION_TEXT.cancellationReason}</dt><dd>{cancellation.reason}</dd></div>
      </dl>
    </section> : null}
    {isCurrentDetail ? <section className={styles.section} aria-labelledby="purchase-order-acceptance-history-heading" data-testid="purchase-order-acceptance-history">
      <h2 id="purchase-order-acceptance-history-heading">{T.acceptanceHistoryHeading}</h2>
      {acceptances.length === 0
        ? <p>{T.acceptanceHistoryEmpty}</p>
        : <RecordTable label={T.acceptanceHistoryHeading} columns={ACCEPTANCE_COLUMNS}>
          {acceptances.map((acceptance) => <Fragment key={acceptance.acceptanceId}>
            <tr role="row" data-testid={`purchase-order-acceptance-${acceptance.acceptanceId}`}>
              <Cell label={T.acceptanceId} identifier>{acceptance.acceptanceId}</Cell>
              <Cell label={T.lineNo}>{lineNoById.get(acceptance.lineId) ?? T.lineUnavailable}</Cell>
              <Cell label={T.quantity} numeric>{acceptance.quantity}</Cell>
              <Cell label={T.recordedAtUtc}><time dateTime={acceptance.recordedAtUtc}>{acceptance.recordedAtUtc}</time></Cell>
            </tr>
            {/* The order's reader reopens the immutable receipt here; the record panel below stays a record control. */}
            {namespace !== null ? <GoodsReceiptDetailRow colSpan={ACCEPTANCE_COLUMNS.length} binding={namespace} detail={state.data}
              acceptanceId={acceptance.acceptanceId}
              name={GOODS_RECEIPT_TEXT.detailOf(String(lineNoById.get(acceptance.lineId) ?? T.lineUnavailable),
                acceptance.quantity, acceptance.recordedAtUtc)} /> : null}
          </Fragment>)}
        </RecordTable>}
    </section> : null}
    {isCurrentDetail && namespace !== null ? <DocumentsPanel
      ownerKind={OWNER_KIND.purchasingPurchaseOrder}
      ownerId={order.id}
      companyId={companyId}
      namespace={namespace}
      entryGeneration={shell.routeEntryGeneration}
    /> : null}
    {canApprove ? outcomeNotice : null}
    {canApprove && (order.status === 'Draft' || unresolved) ? <section className={styles.section}><h2>{T.approvalHeading}</h2><p>{T.approvalNote}</p>{attempt.phase === 'unknown' ? <p role="alert" className={styles.warning}>{T.approvalUnknown}</p> : null}<Button type="button" variant="primary" disabled={attempt.phase === 'submitting'} onClick={() => void sendApproval(attempt.phase === 'unknown' ? attempt.snapshot : undefined)}>{attempt.phase === 'submitting' ? T.approvalSending : attempt.phase === 'unknown' ? T.approvalRetry : T.approve}</Button></section> : null}
    {canCancel && isCurrentDetail && namespace !== null && actorId !== null
      ? <div className={styles.section}><PurchaseOrderCancellationPanel sessionEpoch={shell.sessionEpoch}
          fenceKey={`${namespace}::${shell.routeEntryGeneration}`} actorId={actorId} companyId={companyId} order={order}
          onSettled={() => query.refetch()} /></div>
      : null}
    {namespace !== null && receiptAttempt !== null && hasCapability(shell, PURCHASE_GOODS_RECEIPT_CAPABILITY)
      ? <div className={styles.section} data-testid="purchase-order-receipt-action"><GoodsReceiptPanel enabled
          binding={namespace} attemptBinding={receiptAttempt} detail={state.data} onRecorded={() => void query.refetch()} /></div>
      : null}
  </>, <StatusIndicator className={styles.status} tone={PURCHASE_ORDER_STATUS_TONE[order.status]}>{PURCHASE_ORDER_STATUS_LABEL[order.status]}</StatusIndicator>)
}
