import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useLocation } from 'react-router'
import { createRequestFence } from '../purchasing/requestFence'
import { CAPABILITY } from '../shell/capabilities'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { inventoryLabelDetailPath } from './labelDetailRoute'
import {
  fetchInventoryMovementTimeline,
  MOVEMENT_TIMELINE_INVALID_QUERY,
  movementTimelineProblemCode,
} from './movementTimelineApi'
import {
  continuesTimeline,
  type InventoryMovement,
  type InventoryMovementLine,
  type InventoryMovementTimelinePage,
} from './movementTimelineContract'
import {
  MOVEMENT_BLOCK_TEXT,
  MOVEMENT_DIRECTION_TEXT,
  MOVEMENT_KIND_TEXT,
  MOVEMENT_TIMELINE_TEXT as TEXT,
} from './movementTimelineText'
import { carriedStockSearch, stockRowActionState } from './stockRowActions'
import styles from './inventory.module.css'
import timeline from './movementTimeline.module.css'

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** Why a continuation did not extend the list; the pages already shown stay on screen. */
type MoreProblem = 'cursor-invalid' | 'unavailable' | 'malformed'

type State =
  | { readonly type: 'loading' }
  /** The first page failed: nothing is shown. `invalid-query` is the owner's closed 400. */
  | { readonly type: 'failed'; readonly view: RemoteViewState<unknown> | { readonly kind: 'invalid-query' } }
  | {
    readonly type: 'ready'
    readonly items: readonly InventoryMovement[]
    readonly nextCursor: string | null
    readonly more: 'idle' | 'loading' | MoreProblem
  }

/** The label the user came from, read only from router state the label detail handed over. Never a filter. */
function carriedLabelId(state: unknown): string | null {
  if (typeof state !== 'object' || state === null || !('movementTimelineFromLabelId' in state)) return null
  const value = (state as { readonly movementTimelineFromLabelId: unknown }).movementTimelineFromLabelId
  return typeof value === 'string' && GUID.test(value) ? value : null
}

export function InventoryMovementTimelinePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const location = useLocation()
  // A context change (actor, company, session) or a new route entry remounts the view: its fence is invalidated and
  // every late answer of the previous context is dropped before it can paint.
  return <InventoryMovementTimelineView
    key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`}
    companyId={namespace !== null && shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null}
    canOpenLabel={hasCapability(shell, CAPABILITY.inventoryStockPositionsList)}
    fromLabelId={carriedLabelId(location.state)}
    stockSearch={carriedStockSearch(location.state)}
  />
}

function InventoryMovementTimelineView({ companyId, canOpenLabel, fromLabelId, stockSearch }: {
  readonly companyId: string | null
  readonly canOpenLabel: boolean
  readonly fromLabelId: string | null
  readonly stockSearch: string | null
}) {
  const boundary = useAuthBoundary()
  const boundaryRef = useRef(boundary)
  useEffect(() => {
    boundaryRef.current = boundary
  }, [boundary])
  const fence = useRef(createRequestFence())
  const [state, setState] = useState<State>({ type: 'loading' })

  /** One owner read; null when the answer belongs to a superseded request or context and must not be applied. */
  const read = useCallback(async (cursor: string | null): Promise<ViewOutcome<InventoryMovementTimelinePage> | null> => {
    if (companyId === null) return null
    const token = fence.current.capture()
    const security = boundaryRef.current.capture()
    const outcome = await fetchInventoryMovementTimeline(companyId, cursor, token.signal)
    if (!fence.current.isCurrent(token)) return null
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundaryRef.current.report(outcome.status, security)
    }
    return outcome
  }, [companyId])

  const loadFirst = useCallback(async () => {
    setState({ type: 'loading' })
    const outcome = await read(null)
    if (outcome === null) return
    if (outcome.type === 'ok-single') {
      setState({ type: 'ready', items: outcome.data.items, nextCursor: outcome.data.nextCursor, more: 'idle' })
    } else if (outcome.type === 'http' && outcome.status === 400) {
      setState({ type: 'failed', view: { kind: 'invalid-query' } })
    } else {
      setState({ type: 'failed', view: classifyOutcome(outcome) })
    }
  }, [read])

  useEffect(() => {
    const currentFence = fence.current
    if (companyId !== null) void loadFirst()
    return () => currentFence.invalidate()
  }, [companyId, loadFirst])

  const loadMore = async (shown: readonly InventoryMovement[], cursor: string) => {
    setState({ type: 'ready', items: shown, nextCursor: cursor, more: 'loading' })
    const outcome = await read(cursor)
    if (outcome === null) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      // The authority to read is gone: nothing read under it stays on screen.
      setState({ type: 'failed', view: classifyOutcome(outcome) })
      return
    }
    let problem: MoreProblem | null = null
    if (outcome.type === 'ok-single') {
      if (continuesTimeline(shown, outcome.data.items)) {
        setState({
          type: 'ready', items: [...shown, ...outcome.data.items], nextCursor: outcome.data.nextCursor, more: 'idle',
        })
        return
      }
      problem = 'malformed'
    } else if (outcome.type === 'malformed') {
      problem = 'malformed'
    } else if (outcome.type === 'http' && outcome.status === 400) {
      // The owner answers an expired cursor, or one issued to another actor/company/limit, with its closed code.
      problem = movementTimelineProblemCode(outcome.body) === MOVEMENT_TIMELINE_INVALID_QUERY ? 'cursor-invalid' : 'malformed'
    } else {
      problem = 'unavailable'
    }
    setState({ type: 'ready', items: shown, nextCursor: cursor, more: problem })
  }

  if (companyId === null) {
    return <main className={styles.page}><p role="alert">{TEXT.noCompany}</p></main>
  }

  return <main className={styles.page} data-screen="ST-04">
    {fromLabelId !== null
      ? <nav aria-label="Etiket">
        <Link to={inventoryLabelDetailPath(fromLabelId)} state={stockSearch === null ? undefined : stockRowActionState(stockSearch)}>
          {TEXT.returnToLabel}
        </Link>
      </nav>
      : null}
    <header className={styles.header}>
      <div><p className={styles.eyebrow}>{TEXT.eyebrow}</p><h1>{TEXT.title}</h1></div>
    </header>
    <p className={timeline.scope}>{TEXT.scope}</p>
    <p className={timeline.secondary}>{TEXT.immutable}</p>

    {state.type === 'loading' ? <StateBlock state={{ kind: 'initial-loading' }} subject={TEXT.subject} /> : null}
    {state.type === 'failed' && state.view.kind === 'invalid-query'
      ? <div role="alert" className={timeline.notice}>
        <p>{TEXT.invalidQuery}</p>
        <button type="button" onClick={() => void loadFirst()}>{TEXT.restart}</button>
      </div>
      : null}
    {state.type === 'failed' && state.view.kind !== 'invalid-query'
      ? <StateBlock state={state.view as RemoteViewState<unknown>} subject={TEXT.subject} onRetry={() => void loadFirst()} />
      : null}
    {state.type === 'ready' && state.items.length === 0 ? <p role="status">{TEXT.empty}</p> : null}
    {state.type === 'ready' && state.items.length > 0
      ? <>
        <div className={styles.tableWrap}><table aria-busy={state.more === 'loading'}>
          <caption className={timeline.caption}>{TEXT.tableCaption}</caption>
          <thead><tr>
            <th scope="col">{TEXT.headers.time}</th>
            <th scope="col">{TEXT.headers.movement}</th>
            <th scope="col">{TEXT.headers.source}</th>
            <th scope="col">{TEXT.headers.label}</th>
            <th scope="col">{TEXT.headers.direction}</th>
            <th scope="col">{TEXT.headers.party}</th>
            <th scope="col">{TEXT.headers.location}</th>
            <th scope="col">{TEXT.headers.block}</th>
            <th scope="col">{TEXT.headers.quantity}</th>
          </tr></thead>
          <tbody>{state.items.flatMap((movement) => movement.lines.map((line, index) =>
            <MovementLineRow
              key={`${movement.movementId}:${line.sequence}`}
              movement={movement}
              line={line}
              first={index === 0}
              companyId={companyId}
              canOpenLabel={canOpenLabel}
            />))}</tbody>
        </table></div>
        <div className={timeline.footer}>
          <p role="status">{TEXT.shownCount(state.items.length)}</p>
          {state.nextCursor === null ? <p>{TEXT.end}</p> : null}
          {state.nextCursor !== null && (state.more === 'idle' || state.more === 'loading')
            ? <button
              type="button"
              disabled={state.more === 'loading'}
              onClick={() => void loadMore(state.items, state.nextCursor!)}
            >{state.more === 'loading' ? TEXT.loadingMore : TEXT.loadMore}</button>
            : null}
        </div>
        {state.more === 'cursor-invalid' || state.more === 'malformed'
          ? <div role="alert" className={timeline.notice}>
            <p>{state.more === 'cursor-invalid' ? TEXT.cursorInvalid : TEXT.continuationMalformed}</p>
            <button type="button" onClick={() => void loadFirst()}>{TEXT.restart}</button>
          </div>
          : null}
        {state.more === 'unavailable'
          ? <div role="alert" className={timeline.notice}>
            <p>{TEXT.continuationUnavailable}</p>
            <button type="button" onClick={() => void loadMore(state.items, state.nextCursor!)}>{TEXT.retry}</button>
          </div>
          : null}
      </>
      : null}
  </main>
}

function ownership(line: InventoryMovementLine, companyId: string): string {
  if (line.ownerKind === 'Company') return line.ownerId === companyId ? TEXT.ownedByCompany : TEXT.ownerUnverified
  return TEXT.ownedByCounterparty
}

function custody(line: InventoryMovementLine, companyId: string): string {
  if (line.custodianKind === 'Company') return line.custodianId === companyId ? TEXT.heldByCompany : TEXT.custodyUnverified
  return TEXT.heldByCounterparty
}

function MovementLineRow({ movement, line, first, companyId, canOpenLabel }: {
  readonly movement: InventoryMovement
  readonly line: InventoryMovementLine
  readonly first: boolean
  readonly companyId: string
  readonly canOpenLabel: boolean
}) {
  return <tr
    className={first ? timeline.movementStart : undefined}
    data-movement-id={movement.movementId}
    data-line-sequence={line.sequence}
    data-label-id={line.labelId}
    data-source-id={movement.sourceId}
  >
    <td>{movement.occurredAtUtc}</td>
    <td>{MOVEMENT_KIND_TEXT[movement.kind]}<br /><span className={timeline.idText}>…{movement.movementId.slice(-8)}</span></td>
    <td>
      {movement.sourceKind}<br />
      <span className={timeline.idText}>{movement.sourceId}</span><br />
      <span className={timeline.secondary}>{movement.sourceLineId === null
        ? TEXT.sourceWhole
        : `${TEXT.sourceLine} …${movement.sourceLineId.slice(-8)}`}</span>
    </td>
    <td>{canOpenLabel ? <Link to={inventoryLabelDetailPath(line.labelId)}>{line.labelCode}</Link> : line.labelCode}</td>
    <td>{MOVEMENT_DIRECTION_TEXT[line.direction]}</td>
    <td>{ownership(line, companyId)}<br />{custody(line, companyId)}</td>
    <td className={timeline.idText}>{line.locationId}</td>
    <td>{MOVEMENT_BLOCK_TEXT[line.block]}</td>
    <td>{TEXT.kg(line.quantityKg)}</td>
  </tr>
}
