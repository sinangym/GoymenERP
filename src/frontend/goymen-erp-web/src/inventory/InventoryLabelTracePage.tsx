import { useCallback, useEffect, useRef, useState, type FormEvent, type RefObject } from 'react'
import { Link, useSearchParams } from 'react-router'
import { createRequestFence, type RequestFence } from '../purchasing/requestFence'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary, type AuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import {
  inventoryLabelTracePath,
  LABEL_TRACE_DIRECTION_PARAM,
  LABEL_TRACE_DIRECTION_PARAM_VALUE,
  LABEL_TRACE_LABEL_PARAM,
} from './capabilities'
import { inventoryLabelDetailPath } from './labelDetailRoute'
import {
  fetchInventoryLabelTrace,
  LABEL_TRACE_INVALID_QUERY,
  labelTraceProblemCode,
  searchInventoryLabelTrace,
} from './labelTraceApi'
import {
  normalizeLabelTraceSearchText,
  partyRoleOf,
  type InventoryLabelTrace,
  type InventoryLabelTraceSearch,
  type LabelTraceDirection,
  type LabelTraceEvent,
  type LabelTraceParty,
} from './labelTraceContract'
import {
  LABEL_TRACE_DIRECTION_TEXT,
  LABEL_TRACE_LINK_KIND_TEXT,
  LABEL_TRACE_STEP_TEXT,
  LABEL_TRACE_TEXT as TEXT,
} from './labelTraceText'
import styles from './inventory.module.css'
import timeline from './movementTimeline.module.css'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

type TraceTarget =
  | { readonly kind: 'none' }
  | { readonly kind: 'invalid' }
  | { readonly kind: 'trace'; readonly labelId: string; readonly direction: LabelTraceDirection }

/** A failed owner read: the shared state, or the owner's closed 400 (`invalid-query`). */
type FailedView = RemoteViewState<unknown> | { readonly kind: 'invalid-query' }

/**
 * The trace target from the URL: exactly one label identity and at most one direction (`ileri` / `geri`, `ileri` when
 * absent). Anything else is an invalid link and reads nothing.
 */
export function parseLabelTraceTarget(params: URLSearchParams): TraceTarget {
  const labels = params.getAll(LABEL_TRACE_LABEL_PARAM)
  const directions = params.getAll(LABEL_TRACE_DIRECTION_PARAM)
  if (labels.length === 0 && directions.length === 0) return { kind: 'none' }
  if (labels.length !== 1 || !GUID.test(labels[0]) || directions.length > 1) return { kind: 'invalid' }
  const value = directions[0] ?? LABEL_TRACE_DIRECTION_PARAM_VALUE.forward
  if (value === LABEL_TRACE_DIRECTION_PARAM_VALUE.forward) return { kind: 'trace', labelId: labels[0], direction: 'forward' }
  if (value === LABEL_TRACE_DIRECTION_PARAM_VALUE.backward) return { kind: 'trace', labelId: labels[0], direction: 'backward' }
  return { kind: 'invalid' }
}

/** One owner read under a request fence; null when the answer belongs to a superseded request or context. */
async function guardedRead<T>(
  fence: RequestFence,
  boundary: RefObject<AuthBoundary>,
  run: (signal: AbortSignal) => Promise<ViewOutcome<T>>,
): Promise<ViewOutcome<T> | null> {
  const token = fence.capture()
  const security = boundary.current.capture()
  const outcome = await run(token.signal)
  if (!fence.isCurrent(token)) return null
  if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
    boundary.current.report(outcome.status, security)
  }
  return outcome
}

function failedView<T>(outcome: ViewOutcome<T>): FailedView {
  // Only the owner's closed code is an invalid query; any other 400 stays the shared fail-closed state.
  if (outcome.type === 'http' && outcome.status === 400 && labelTraceProblemCode(outcome.body) === LABEL_TRACE_INVALID_QUERY) {
    return { kind: 'invalid-query' }
  }
  return classifyOutcome(outcome) as RemoteViewState<unknown>
}

function useBoundaryRef(): RefObject<AuthBoundary> {
  const boundary = useAuthBoundary()
  const boundaryRef = useRef(boundary)
  useEffect(() => {
    boundaryRef.current = boundary
  }, [boundary])
  return boundaryRef
}

export function InventoryLabelTracePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const [params] = useSearchParams()
  // A context change (actor, company, session) or a new route entry remounts the view: its fences are invalidated and
  // every late answer of the previous context is dropped before it can paint.
  return <InventoryLabelTraceView
    key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`}
    companyId={namespace !== null && shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null}
    target={parseLabelTraceTarget(params)}
  />
}

function InventoryLabelTraceView({ companyId, target }: {
  readonly companyId: string | null
  readonly target: TraceTarget
}) {
  if (companyId === null) {
    return <section className={styles.page}><p role="alert">{TEXT.noCompany}</p></section>
  }

  return <section className={styles.page} data-screen="STK-S33" aria-labelledby="label-trace-title">
    <header className={styles.header}>
      <div><p className={styles.eyebrow}>{TEXT.eyebrow}</p><h1 id="label-trace-title">{TEXT.title}</h1></div>
    </header>
    <p className={timeline.scope}>{TEXT.scope}</p>
    <LabelTraceSearch companyId={companyId} direction={target.kind === 'trace' ? target.direction : 'forward'} />
    {target.kind === 'none' ? <p role="status">{TEXT.noTrace}</p> : null}
    {target.kind === 'invalid' ? <p role="alert">{TEXT.invalidLink}</p> : null}
    {target.kind === 'trace'
      ? <LabelTraceResult
        key={`${target.labelId.toLowerCase()}:${target.direction}`}
        companyId={companyId}
        labelId={target.labelId}
        direction={target.direction}
      />
      : null}
  </section>
}

type SearchState =
  | { readonly type: 'idle' }
  | { readonly type: 'loading' }
  | { readonly type: 'failed'; readonly view: FailedView }
  | { readonly type: 'ready'; readonly data: InventoryLabelTraceSearch }

function LabelTraceSearch({ companyId, direction }: {
  readonly companyId: string
  readonly direction: LabelTraceDirection
}) {
  const boundaryRef = useBoundaryRef()
  const fence = useRef(createRequestFence())
  const [text, setText] = useState('')
  const [invalid, setInvalid] = useState(false)
  const [submitted, setSubmitted] = useState<string | null>(null)
  const [state, setState] = useState<SearchState>({ type: 'idle' })

  useEffect(() => {
    const currentFence = fence.current
    return () => currentFence.invalidate()
  }, [])

  const search = useCallback(async (value: string) => {
    setState({ type: 'loading' })
    const outcome = await guardedRead(fence.current, boundaryRef,
      (signal) => searchInventoryLabelTrace(companyId, value, signal))
    if (outcome === null) return
    setState(outcome.type === 'ok-single' ? { type: 'ready', data: outcome.data } : { type: 'failed', view: failedView(outcome) })
  }, [companyId, boundaryRef])

  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const value = normalizeLabelTraceSearchText(text)
    if (value === null) {
      // An invalid text sends nothing; what was found before is dropped so it is not read as this text's answer.
      fence.current.invalidate()
      setInvalid(true)
      setSubmitted(null)
      setState({ type: 'idle' })
      return
    }
    setInvalid(false)
    setSubmitted(value)
    void search(value)
  }

  return <section aria-labelledby="label-trace-search-heading">
    <h2 id="label-trace-search-heading">{TEXT.searchHeading}</h2>
    <form className={styles.form} role="search" onSubmit={submit} noValidate>
      <Field label={TEXT.searchLabel} help={TEXT.searchHelp} error={invalid ? TEXT.searchInvalid : undefined}>
        {(control) => <div className={styles.searchRow}>
          <input
            {...control}
            type="search"
            value={text}
            autoComplete="off"
            onChange={(event) => setText(event.target.value)}
          />
          <Button type="submit" variant="primary" disabled={state.type === 'loading'}>{TEXT.searchSubmit}</Button>
        </div>}
      </Field>
    </form>
    {state.type === 'loading' ? <p role="status" aria-live="polite">{TEXT.searchLoading}</p> : null}
    {state.type === 'failed' && state.view.kind === 'invalid-query'
      ? <p role="alert">{TEXT.searchInvalidQuery}</p>
      : null}
    {state.type === 'failed' && state.view.kind !== 'invalid-query'
      ? <StateBlock
        state={state.view as RemoteViewState<unknown>}
        subject={TEXT.searchSubject}
        onRetry={submitted === null ? undefined : () => void search(submitted)}
      />
      : null}
    {state.type === 'ready' ? <SearchResults data={state.data} direction={direction} /> : null}
  </section>
}

function SearchResults({ data, direction }: {
  readonly data: InventoryLabelTraceSearch
  readonly direction: LabelTraceDirection
}) {
  const supplierNote = TEXT.supplierSearch[data.supplierSearchStatus]
  return <>
    {supplierNote === null ? null : <p className={timeline.secondary} data-supplier-search-status={data.supplierSearchStatus}>
      {supplierNote}
    </p>}
    {data.items.length === 0
      ? <p role="status">{TEXT.searchEmpty}</p>
      : <div className={styles.tableWrap}><table>
        <caption className={timeline.caption}>{TEXT.searchCaption}</caption>
        <thead><tr>
          <th scope="col">{TEXT.searchHeaders.code}</th>
          <th scope="col">{TEXT.searchHeaders.supplierNumber}</th>
          <th scope="col">{TEXT.searchHeaders.matchedBy}</th>
          <th scope="col">{TEXT.searchHeaders.action}</th>
        </tr></thead>
        <tbody>{data.items.map((item) => <tr key={item.labelId} data-label-id={item.labelId}>
          <td>{item.code}</td>
          <td>{item.supplierLabelNumber ?? TEXT.notApplicable}</td>
          <td>{item.matchedBy === 'LabelCode' ? TEXT.matchedByLabelCode : TEXT.matchedBySupplierNumber}</td>
          <td><Link to={inventoryLabelTracePath(item.labelId, direction)}>{TEXT.openTrace(item.code)}</Link></td>
        </tr>)}</tbody>
      </table></div>}
    {data.windowFull ? <p role="status">{TEXT.searchWindowFull}</p> : null}
  </>
}

type TraceState =
  | { readonly type: 'loading' }
  | { readonly type: 'failed'; readonly view: FailedView }
  | { readonly type: 'ready'; readonly data: InventoryLabelTrace }

function LabelTraceResult({ companyId, labelId, direction }: {
  readonly companyId: string
  readonly labelId: string
  readonly direction: LabelTraceDirection
}) {
  const boundaryRef = useBoundaryRef()
  const fence = useRef(createRequestFence())
  const [state, setState] = useState<TraceState>({ type: 'loading' })

  const read = useCallback(() => guardedRead(fence.current, boundaryRef,
    (signal) => fetchInventoryLabelTrace(companyId, labelId, direction, signal)), [companyId, labelId, direction, boundaryRef])

  const apply = useCallback((outcome: ViewOutcome<InventoryLabelTrace> | null) => {
    if (outcome === null) return
    setState(outcome.type === 'ok-single' ? { type: 'ready', data: outcome.data } : { type: 'failed', view: failedView(outcome) })
  }, [])

  // The view starts in `loading`, so the mount read sets no state before its answer.
  useEffect(() => {
    const currentFence = fence.current
    void read().then(apply)
    return () => currentFence.invalidate()
  }, [read, apply])

  const retry = () => {
    setState({ type: 'loading' })
    void read().then(apply)
  }

  return <section aria-label={TEXT.traceSubject} data-trace-direction={direction}>
    <nav aria-label={TEXT.directionNav} className={styles.actions}>
      <Link
        to={inventoryLabelTracePath(labelId, 'forward')}
        aria-current={direction === 'forward' ? 'page' : undefined}
      >{TEXT.forward}</Link>
      <Link
        to={inventoryLabelTracePath(labelId, 'backward')}
        aria-current={direction === 'backward' ? 'page' : undefined}
      >{TEXT.backward}</Link>
    </nav>
    {state.type === 'loading' ? <StateBlock state={{ kind: 'initial-loading' }} subject={TEXT.traceSubject} /> : null}
    {state.type === 'failed' && state.view.kind === 'invalid-query'
      ? <p role="alert">{TEXT.traceInvalidQuery}</p>
      : null}
    {state.type === 'failed' && state.view.kind !== 'invalid-query'
      ? <StateBlock state={state.view as RemoteViewState<unknown>} subject={TEXT.traceSubject} onRetry={retry} />
      : null}
    {state.type === 'ready' ? <TraceBody trace={state.data} /> : null}
  </section>
}

function TraceBody({ trace }: { readonly trace: InventoryLabelTrace }) {
  const codes = new Map(trace.labels.map((item) => [item.labelId.toLowerCase(), item.code]))
  const codeOf = (labelId: string) => codes.get(labelId.toLowerCase()) ?? labelId
  const notes = [
    { key: 'production', status: trace.productionStatus, text: TEXT.production[trace.productionStatus] },
    { key: 'party', status: trace.partyStatus, text: TEXT.party[trace.partyStatus] },
    { key: 'party-name', status: trace.partyNameStatus, text: TEXT.partyName[trace.partyNameStatus] },
  ].filter((note) => note.text !== null)

  return <>
    <header className={styles.header}>
      <h2>{TEXT.rootHeading(trace.labelCode)}</h2>
      <Link to={inventoryLabelDetailPath(trace.labelId)}>{TEXT.openLabel}</Link>
    </header>
    {notes.map((note) => <p key={note.key} className={timeline.secondary} data-enrichment={note.key} data-status={note.status}>
      {note.text}
    </p>)}
    {trace.windowFull ? <p role="status" className={timeline.notice}>{TEXT.windowFull}</p> : null}

    <section aria-labelledby="label-trace-events-heading">
      <h3 id="label-trace-events-heading">{TEXT.eventsHeading}</h3>
      {trace.events.length === 0
        ? <p role="status">{TEXT.eventsEmpty}</p>
        : <div className={styles.tableWrap}><table>
          <caption className={timeline.caption}>{TEXT.eventsCaption}</caption>
          <thead><tr>
            <th scope="col">{TEXT.eventsHeaders.time}</th>
            <th scope="col">{TEXT.eventsHeaders.step}</th>
            <th scope="col">{TEXT.eventsHeaders.label}</th>
            <th scope="col">{TEXT.eventsHeaders.direction}</th>
            <th scope="col">{TEXT.eventsHeaders.quantity}</th>
            <th scope="col">{TEXT.eventsHeaders.source}</th>
            <th scope="col">{TEXT.eventsHeaders.party}</th>
            <th scope="col">{TEXT.eventsHeaders.supplierNumber}</th>
          </tr></thead>
          <tbody>{trace.events.map((item) => <tr
            key={`${item.movementId}:${item.lineNumber}`}
            data-movement-id={item.movementId}
            data-line-number={item.lineNumber}
            data-label-id={item.labelId}
          >
            <td>{item.occurredAtUtc}</td>
            <td>{LABEL_TRACE_STEP_TEXT[item.step]}</td>
            <td><Link to={inventoryLabelDetailPath(item.labelId)}>{codeOf(item.labelId)}</Link></td>
            <td>{LABEL_TRACE_DIRECTION_TEXT[item.direction]}</td>
            <td>{TEXT.kg(item.quantityKg)}</td>
            <td>
              {item.sourceKind}<br />
              <span className={timeline.idText}>{item.sourceId}</span><br />
              <span className={timeline.secondary}>{item.sourceLineId === null
                ? TEXT.sourceWhole
                : `${TEXT.sourceLine} …${item.sourceLineId.slice(-8)}`}</span>
            </td>
            <td>{partyText(item, trace.parties)}</td>
            <td>{item.step === 'SupplierReceipt' ? item.supplierLabelNumber ?? TEXT.partyUnknown : TEXT.notApplicable}</td>
          </tr>)}</tbody>
        </table></div>}
    </section>

    <section aria-labelledby="label-trace-links-heading">
      <h3 id="label-trace-links-heading">{TEXT.linksHeading}</h3>
      {trace.links.length === 0
        ? <p role="status">{TEXT.linksEmpty}</p>
        : <div className={styles.tableWrap}><table>
          <caption className={timeline.caption}>{TEXT.linksCaption}</caption>
          <thead><tr>
            <th scope="col">{TEXT.linksHeaders.kind}</th>
            <th scope="col">{TEXT.linksHeaders.time}</th>
            <th scope="col">{TEXT.linksHeaders.source}</th>
            <th scope="col">{TEXT.linksHeaders.output}</th>
            <th scope="col">{TEXT.linksHeaders.order}</th>
            <th scope="col">{TEXT.linksHeaders.consumed}</th>
            <th scope="col">{TEXT.linksHeaders.share}</th>
            <th scope="col">{TEXT.linksHeaders.shareStatus}</th>
          </tr></thead>
          <tbody>{trace.links.map((item) => {
            const manufacturing = item.kind === 'Manufacturing'
            const missing = manufacturing ? TEXT.notAnsweredValue : TEXT.notApplicable
            return <tr
              key={`${item.transformationId}:${item.sourceLabelId}:${item.outputLabelId}`}
              data-transformation-id={item.transformationId}
            >
              <td>{LABEL_TRACE_LINK_KIND_TEXT[item.kind]}</td>
              <td>{item.occurredAtUtc}</td>
              <td><Link to={inventoryLabelDetailPath(item.sourceLabelId)}>{codeOf(item.sourceLabelId)}</Link><br />
                {TEXT.kg(item.sourceQuantityKg)}</td>
              <td><Link to={inventoryLabelDetailPath(item.outputLabelId)}>{codeOf(item.outputLabelId)}</Link><br />
                {TEXT.kg(item.outputQuantityKg)}</td>
              <td className={timeline.idText}>{item.productionOrderId ?? missing}</td>
              <td>{item.consumedKg === null ? missing : TEXT.kg(item.consumedKg)}</td>
              <td>{item.consumedShare ?? missing}</td>
              <td>{item.shareStatus ?? missing}</td>
            </tr>
          })}</tbody>
        </table></div>}
    </section>

    <section aria-labelledby="label-trace-labels-heading">
      <h3 id="label-trace-labels-heading">{TEXT.labelsHeading}</h3>
      <div className={styles.tableWrap}><table>
        <caption className={timeline.caption}>{TEXT.labelsCaption}</caption>
        <thead><tr>
          <th scope="col">{TEXT.labelsHeaders.depth}</th>
          <th scope="col">{TEXT.labelsHeaders.code}</th>
        </tr></thead>
        <tbody>{trace.labels.map((item) => <tr key={item.labelId} data-label-id={item.labelId}>
          <td>{item.depth}</td>
          <td><Link to={inventoryLabelDetailPath(item.labelId)}>{item.code}</Link></td>
        </tr>)}</tbody>
      </table></div>
    </section>
  </>
}

/** The owner's party of a receipt or shipment step, by its code and name; never guessed when unanswered. */
function partyText(item: LabelTraceEvent, parties: readonly LabelTraceParty[]): string {
  const role = partyRoleOf(item.step)
  if (role === null) return TEXT.notApplicable
  if (item.partyId === null) return TEXT.partyUnknown
  const partyId = item.partyId.toLowerCase()
  const named = parties.find((party) => party.role === role && party.partyId.toLowerCase() === partyId)
  return named === undefined ? TEXT.partyUnnamed(item.partyId) : `${named.code} · ${named.tradeName ?? named.legalName}`
}
