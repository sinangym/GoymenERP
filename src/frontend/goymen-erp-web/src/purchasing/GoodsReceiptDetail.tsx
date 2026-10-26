import { useId, useLayoutEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useInRouterContext, useSearchParams } from 'react-router'
import { fetchGoodsReceiptDetail } from './goodsReceiptApi'
import { stockQuantityOf, type GoodsReceipt } from './goodsReceiptContract'
import { GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'
import type { PurchaseOrderDetail } from './contract'
import { INVENTORY_STOCK_POSITIONS_CAPABILITY } from '../inventory/capabilities'
import { inventoryLabelDetailPath } from '../inventory/labelDetailRoute'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './goodsReceiptDetail.module.css'

/** Address parameter naming the one receipt whose detail is open, so Back from its label reopens the same receipt. */
const GOODS_RECEIPT_DETAIL_PARAM = 'kabul'

interface Props {
  /** The caller's company/actor/session key: a read made under another key is never shown. */
  readonly binding: string
  readonly detail: PurchaseOrderDetail
  readonly acceptanceId: string
  /** The control's accessible name: which receipt it opens. The detail itself shows only what its own read answers. */
  readonly name: string
}

/**
 * "Ayrıntı" on one recorded receipt of the order that is open: the accepted detail read (`GET …/goods-receipts/{acceptanceId}`) on demand, shown
 * in place. Nothing is read until the user asks, and the read records nothing.
 */
export function GoodsReceiptDetail(props: Props) {
  return useInRouterContext() ? <AddressedDetail {...props} /> : <LocalDetail {...props} />
}

/** The detail as a table row of its own, under its acceptance row in the order page's acceptance table. */
export function GoodsReceiptDetailRow({ colSpan, ...props }: Props & { readonly colSpan: number }) {
  return <tr role="row" className={styles.row} data-testid={`purchase-order-acceptance-detail-${props.acceptanceId}`}>
    <td role="cell" className={styles.rowCell} colSpan={colSpan}><GoodsReceiptDetail {...props} /></td>
  </tr>
}

function AddressedDetail(props: Props) {
  const [params, setParams] = useSearchParams()
  const open = params.get(GOODS_RECEIPT_DETAIL_PARAM) === props.acceptanceId
  const toggle = () => setParams((current) => {
    const next = new URLSearchParams(current)
    if (open) next.delete(GOODS_RECEIPT_DETAIL_PARAM)
    else next.set(GOODS_RECEIPT_DETAIL_PARAM, props.acceptanceId)
    return next
  }, { replace: true })
  return <Disclosure {...props} open={open} onToggle={toggle} linked />
}

function LocalDetail(props: Props) {
  const [open, setOpen] = useState(false)
  return <Disclosure {...props} open={open} onToggle={() => setOpen((current) => !current)} linked={false} />
}

function Disclosure({ binding, detail, acceptanceId, name, open, onToggle, linked }: Props & {
  readonly open: boolean
  readonly onToggle: () => void
  readonly linked: boolean
}) {
  const regionId = useId()
  const frame = useRef<HTMLDivElement>(null)
  useVisibleWidth(frame)
  return <div className={styles.detail} ref={frame}>
    <Button type="button" variant="ghost" size="sm" aria-expanded={open} aria-controls={regionId}
      aria-label={name} onClick={onToggle}>{T.detail}</Button>
    <div id={regionId} hidden={!open}>
      {open ? <DetailRead binding={binding} detail={detail} acceptanceId={acceptanceId} linked={linked} /> : null}
    </div>
  </div>
}

/**
 * Inside a table that scrolls sideways the row is as wide as the whole table, so the detail would run off the visible
 * part. The frame is pinned to the visible edge by its stylesheet; this gives it the visible width to wrap within.
 */
function useVisibleWidth(frame: React.RefObject<HTMLDivElement | null>) {
  useLayoutEffect(() => {
    const element = frame.current
    const cell = element?.closest('td') ?? null
    if (element === null || cell === null || typeof ResizeObserver !== 'function') return
    let port: HTMLElement | null = cell.parentElement
    while (port !== null && !/auto|scroll/.test(getComputedStyle(port).overflowX)) port = port.parentElement
    if (port === null) return
    const scroller = port
    const fit = () => {
      const style = getComputedStyle(cell)
      const room = scroller.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight)
      element.style.setProperty('--goods-receipt-detail-width', `${Math.max(0, room)}px`)
    }
    fit()
    const observer = new ResizeObserver(fit)
    observer.observe(scroller)
    return () => observer.disconnect()
  }, [frame])
}

function DetailRead({ binding, detail, acceptanceId, linked }: {
  readonly binding: string
  readonly detail: PurchaseOrderDetail
  readonly acceptanceId: string
  readonly linked: boolean
}) {
  const boundary = useAuthBoundary()
  const read = useQuery({
    queryKey: [binding, 'purchase-goods-receipt-detail', detail.order.companyId, detail.order.id, acceptanceId],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchGoodsReceiptDetail(detail, acceptanceId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  return <section className={styles.body} aria-label={T.detailHeading} data-testid={`goods-receipt-detail-${acceptanceId}`}>
    {read.data?.type === 'ok-single'
      ? <Facts detail={detail} receipt={read.data.data} linked={linked} />
      : <StateBlock subject={T.detailHeading}
          state={read.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(read.data)}
          onRetry={() => void read.refetch()} />}
  </section>
}

function Facts({ detail, receipt, linked }: {
  readonly detail: PurchaseOrderDetail
  readonly receipt: GoodsReceipt
  readonly linked: boolean
}) {
  const shell = useShellState()
  // The label card is Inventory's own screen: its link is offered only to a caller Inventory already lets in.
  const canOpenLabel = linked && hasCapability(shell, INVENTORY_STOCK_POSITIONS_CAPABILITY)
  const line = detail.lines.find((candidate) => candidate.lineId === receipt.purchaseOrderLineId)
  const occurred = new Date(receipt.occurredAtUtc)
  return <dl className={styles.facts}>
    <div className={styles.wide}>
      <dt>{T.detailLabel}</dt>
      <dd>{canOpenLabel
        ? <Link to={inventoryLabelDetailPath(receipt.labelId)} aria-label={T.detailLabelLink(receipt.labelCode)}>{receipt.labelCode}</Link>
        : receipt.labelCode}</dd>
    </div>
    <div><dt>{T.detailLine}</dt><dd>{line === undefined ? T.detailLineUnknown : line.lineNo}</dd></div>
    <div>
      <dt>{T.detailOccurred}</dt>
      <dd><time dateTime={receipt.occurredAtUtc}>
        {occurred.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })}
      </time></dd>
    </div>
    <div><dt>{T.detailAccepted}</dt><dd className={styles.quantity}>{receipt.acceptedQuantity}</dd></div>
    <div><dt>{T.detailRemaining}</dt><dd className={styles.quantity}>{receipt.remainingQuantity}</dd></div>
    <div><dt>{T.detailMeasured}</dt><dd className={styles.quantity}>{receipt.measuredQuantityKg} kg</dd></div>
    <div><dt>{T.detailInitial}</dt><dd>{receipt.initialBlock === 'Free' ? T.detailFree : T.detailBlocked}</dd></div>
    <StockQuantityFact receipt={receipt} />
  </dl>
}

/**
 * P05.1D: Inventory's stored stock quantity for this receipt, exactly as the decoder validated it. Nothing is reread
 * from Product or Settings; a legacy receipt says so instead of showing zero or a value derived from today's masters.
 * No raw id is shown: the profile is named by its stored version, the source by this acceptance and its order version.
 */
function StockQuantityFact({ receipt }: { readonly receipt: GoodsReceipt }) {
  const state = stockQuantityOf(receipt)
  return <div className={styles.wide} data-stock-quantity={state.kind}>
    <dt>{T.stockHeading}</dt>
    {state.kind === 'snapshot'
      ? <dd>
          <div className={styles.quantity}>{T.stockValue(state.quantity.baseQuantity.value)}</div>
          <div>{T.stockNote}</div>
          <div>{T.stockSource(state.quantity.source.version)}</div>
          <div>{T.stockProfile(state.quantity.baseQuantity.profileVersion)}</div>
          <div>{T.stockScale(state.quantity.baseQuantity.scale)}</div>
          <div>{T.stockAlternate}</div>
        </dd>
      : <dd>{state.kind === 'legacy' ? T.stockLegacy : T.stockUnreadable}</dd>}
  </div>
}
