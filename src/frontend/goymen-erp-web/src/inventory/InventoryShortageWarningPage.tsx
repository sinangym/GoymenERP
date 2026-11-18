import { useCallback, useEffect, useRef, useState } from 'react'
import { createRequestFence } from '../purchasing/requestFence'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import {
  fetchInventoryShortageWarnings,
  SHORTAGE_WARNING_INVALID_QUERY,
  shortageWarningProblemCode,
} from './shortageWarningApi'
import type { ShortageLine, ShortageProduct, ShortageSimilarStock, ShortageWarnings } from './shortageWarningContract'
import {
  SHORTAGE_COVERAGE_TEXT,
  SHORTAGE_STATUS_TEXT,
  SHORTAGE_WARNING_TEXT as TEXT,
} from './shortageWarningText'
import styles from './inventory.module.css'
import shortage from './shortageWarning.module.css'

type State =
  | { readonly type: 'loading' }
  /** The read failed: nothing is shown. `invalid-query` is the owner's closed 400. */
  | { readonly type: 'failed'; readonly view: RemoteViewState<unknown> | { readonly kind: 'invalid-query' } }
  | { readonly type: 'ready'; readonly data: ShortageWarnings }

/**
 * KLN-D42 dated shortage warning. It replaces minimum stock: every figure, cover and date is the backend calculator's
 * answer, shown as read. The screen computes nothing and creates nothing (no purchase request, no reservation).
 */
export function InventoryShortageWarningPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // A context change (actor, company, session) or a new route entry remounts the view: its fence is invalidated and
  // every late answer of the previous context is dropped before it can paint.
  return <InventoryShortageWarningView
    key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`}
    companyId={namespace !== null && shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null}
  />
}

function InventoryShortageWarningView({ companyId }: { readonly companyId: string | null }) {
  const boundary = useAuthBoundary()
  const boundaryRef = useRef(boundary)
  useEffect(() => {
    boundaryRef.current = boundary
  }, [boundary])
  const fence = useRef(createRequestFence())
  const [onlyWarnings, setOnlyWarnings] = useState(true)
  const [state, setState] = useState<State>({ type: 'loading' })

  /** One owner read; null when the answer belongs to a superseded request or context and must not be applied. */
  const read = useCallback(async (warningsOnly: boolean): Promise<ViewOutcome<ShortageWarnings> | null> => {
    if (companyId === null) return null
    const token = fence.current.capture()
    const security = boundaryRef.current.capture()
    const outcome = await fetchInventoryShortageWarnings(companyId, { onlyWarnings: warningsOnly }, token.signal)
    if (!fence.current.isCurrent(token)) return null
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundaryRef.current.report(outcome.status, security)
    }
    return outcome
  }, [companyId])

  const apply = useCallback((outcome: ViewOutcome<ShortageWarnings> | null) => {
    if (outcome === null) return
    if (outcome.type === 'ok-single') {
      setState({ type: 'ready', data: outcome.data })
    } else if (outcome.type === 'http' && outcome.status === 400 &&
      shortageWarningProblemCode(outcome.body) === SHORTAGE_WARNING_INVALID_QUERY) {
      // Only the owner's closed code is an invalid query; any other 400 stays the shared fail-closed state.
      setState({ type: 'failed', view: { kind: 'invalid-query' } })
    } else {
      setState({ type: 'failed', view: classifyOutcome(outcome) })
    }
  }, [])

  // The view starts in `loading`, so the mount read sets no state before its answer.
  useEffect(() => {
    const currentFence = fence.current
    if (companyId !== null) void read(true).then(apply)
    return () => currentFence.invalidate()
  }, [companyId, read, apply])

  /** A new read drops what was shown: a previous answer never stays on screen under another filter. */
  const load = (warningsOnly: boolean) => {
    setOnlyWarnings(warningsOnly)
    setState({ type: 'loading' })
    void read(warningsOnly).then(apply)
  }

  if (companyId === null) {
    return <section className={styles.page}><p role="alert">{TEXT.noCompany}</p></section>
  }

  return <section className={styles.page} data-screen="KLN-D42">
    <header className={styles.header}>
      <div><p className={styles.eyebrow}>{TEXT.eyebrow}</p><h1>{TEXT.title}</h1></div>
    </header>
    <p className={shortage.scope}>{TEXT.scope}</p>
    <p className={shortage.secondary}>{TEXT.noRequest}</p>
    <div className={shortage.toolbar}>
      <label>
        <input
          type="checkbox"
          checked={!onlyWarnings}
          disabled={state.type === 'loading'}
          onChange={(event) => load(!event.target.checked)}
        />{' '}{TEXT.showAll}
      </label>
    </div>

    {state.type === 'loading' ? <StateBlock state={{ kind: 'initial-loading' }} subject={TEXT.subject} /> : null}
    {state.type === 'failed' && state.view.kind === 'invalid-query'
      ? <div role="alert" className={shortage.notice}>
        <p>{TEXT.invalidQuery}</p>
        <button type="button" onClick={() => load(onlyWarnings)}>{TEXT.reload}</button>
      </div>
      : null}
    {state.type === 'failed' && state.view.kind !== 'invalid-query'
      ? <StateBlock
        state={state.view as RemoteViewState<unknown>}
        subject={TEXT.subject}
        onRetry={() => load(onlyWarnings)}
      />
      : null}
    {state.type === 'ready' ? <Warnings data={state.data} onlyWarnings={onlyWarnings} /> : null}
  </section>
}

function Warnings({ data, onlyWarnings }: { readonly data: ShortageWarnings; readonly onlyWarnings: boolean }) {
  return <>
    {data.supplyDates === 'NotAnswered'
      ? <div role="note" className={shortage.notice} data-supply-dates="NotAnswered"><p>{TEXT.supplyDatesNotAnswered}</p></div>
      : null}
    {data.items.length === 0
      ? <p role="status" data-state={onlyWarnings ? 'empty-filtered' : 'empty-no-data'}>
        {onlyWarnings ? TEXT.emptyWarnings : TEXT.empty}
      </p>
      : <>
        <p role="status">{TEXT.shownCount(data.items.length)}</p>
        <ul className={shortage.list} aria-label={TEXT.productsCaption}>
          {data.items.map((product) => <ProductCard key={product.productId} product={product} />)}
        </ul>
      </>}
  </>
}

function ProductCard({ product }: { readonly product: ShortageProduct }) {
  return <li className={shortage.product} data-product-id={product.productId} data-status={product.status}>
    <h2 className={shortage.productHeader}>
      <span>{product.productCode} · {product.productName}</span>
      <span>{SHORTAGE_STATUS_TEXT[product.status]}</span>
    </h2>
    {product.status === 'Unconvertible'
      ? <p>{TEXT.unconvertible}</p>
      : <>
        <dl className={shortage.figures}>
          <Figure term={TEXT.headers.earliestDue} value={product.earliestDueDate ?? TEXT.noDueDate} />
          <Figure term={TEXT.headers.openDemand} value={kgOrDash(product.openDemandKg)} />
          <Figure term={TEXT.headers.reserved} value={kgOrDash(product.reservedKg)} />
          <Figure term={TEXT.headers.netDemand} value={kgOrDash(product.netDemandKg)} />
          <Figure term={TEXT.headers.freeStock} value={kgOrDash(product.freeStockKg)} />
          <Figure term={TEXT.headers.openSupply} value={kgOrDash(product.openSupplyKg)} />
          <Figure term={TEXT.headers.shortage} value={kgOrDash(product.shortageKg)} />
        </dl>
        <LineTable product={product} />
      </>}
    <SimilarStock similar={product.similarStock} />
  </li>
}

function kgOrDash(value: string | null): string {
  return value === null ? TEXT.dash : TEXT.kg(value)
}

function Figure({ term, value }: { readonly term: string; readonly value: string }) {
  return <div><dt>{term}</dt><dd>{value}</dd></div>
}

function LineTable({ product }: { readonly product: ShortageProduct }) {
  return <div className={styles.tableWrap}><table>
    <caption className={shortage.caption}>{TEXT.linesCaption(product.productCode)}</caption>
    <thead><tr>
      <th scope="col">{TEXT.lineHeaders.document}</th>
      <th scope="col">{TEXT.lineHeaders.dueDate}</th>
      <th scope="col">{TEXT.lineHeaders.open}</th>
      <th scope="col">{TEXT.lineHeaders.reserved}</th>
      <th scope="col">{TEXT.lineHeaders.fromStock}</th>
      <th scope="col">{TEXT.lineHeaders.fromSupply}</th>
      <th scope="col">{TEXT.lineHeaders.uncovered}</th>
      <th scope="col">{TEXT.lineHeaders.coverage}</th>
      <th scope="col">{TEXT.lineHeaders.latestSupply}</th>
    </tr></thead>
    <tbody>{product.lines.map((line) => <LineRow key={line.lineId} line={line} />)}</tbody>
  </table></div>
}

function LineRow({ line }: { readonly line: ShortageLine }) {
  return <tr data-line-id={line.lineId} data-coverage={line.coverage}>
    <td>{line.documentNo} / {line.lineNo}</td>
    <td>{line.dueDate ?? TEXT.noDueDate}</td>
    <td>{TEXT.kg(line.openKg)}</td>
    <td>{TEXT.kg(line.reservedKg)}</td>
    <td>{TEXT.kg(line.fromStockKg)}</td>
    <td>{TEXT.kg(line.fromSupplyKg)}</td>
    <td>{TEXT.kg(line.uncoveredKg)}</td>
    <td>{SHORTAGE_COVERAGE_TEXT[line.coverage]}</td>
    <td>{line.latestSupplyDate ?? TEXT.dash}</td>
  </tr>
}

function SimilarStock({ similar }: { readonly similar: ShortageSimilarStock }) {
  return <section aria-label={TEXT.similarTitle} data-similar-status={similar.status}>
    <p className={shortage.secondary}>{TEXT.similarTitle}</p>
    {similar.status === 'NotConfigured' ? <p className={shortage.secondary}>{TEXT.similarNotConfigured}</p> : null}
    {similar.status === 'Unavailable' ? <p className={shortage.secondary}>{TEXT.similarUnavailable}</p> : null}
    {similar.status === 'Answered' && similar.items.length === 0
      ? <p className={shortage.secondary}>{TEXT.similarNone}</p>
      : null}
    {similar.status === 'Answered' && similar.items.length > 0
      ? <table aria-label={TEXT.similarTitle}>
        <thead><tr>
          <th scope="col">{TEXT.similarHeaders.product}</th>
          <th scope="col">{TEXT.similarHeaders.freeStock}</th>
        </tr></thead>
        <tbody>{similar.items.map((item) => <tr key={item.productId} data-product-id={item.productId}>
          <td>{item.productCode} · {item.productName}</td>
          <td>{TEXT.kg(item.freeStockKg)}</td>
        </tr>)}</tbody>
      </table>
      : null}
  </section>
}
