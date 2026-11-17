import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { createRequestFence } from '../purchasing/requestFence'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { INVENTORY_STOCK_POSITIONS_CAPABILITY, INVENTORY_STOCK_POSITIONS_ROUTE } from './capabilities'
import { inventoryLabelDetailPath } from './labelDetailRoute'
import { fetchNegativeStockAttempts, negativeStockAttemptProblemCode, NEGATIVE_STOCK_ATTEMPT_INVALID_QUERY } from './NegativeStockAttemptApi'
import { isNegativeStockAttemptId, type NegativeStockAttempt, type NegativeStockAttemptPage as AttemptPage, type NegativeStockAttemptQuery } from './NegativeStockAttemptContract'
import { NEGATIVE_STOCK_ATTEMPT_TEXT as T } from './NegativeStockAttemptText'
import styles from './inventory.module.css'
import report from './NegativeStockAttemptPage.module.css'

type State = { readonly type: 'loading' } | { readonly type: 'ready'; readonly data: AttemptPage }
  | { readonly type: 'failed'; readonly view: RemoteViewState<unknown> | { readonly kind: 'invalid-query' } }
function filtersOf(search: string): { product: string; location: string; openOnly: boolean; query: NegativeStockAttemptQuery | null } {
  const params = new URLSearchParams(search)
  const product = params.get('productId') ?? ''; const location = params.get('locationId') ?? ''
  const openText = params.get('openOnly'); const openOnly = openText !== 'false'
  const names = ['productId', 'locationId', 'openOnly']
  const invalid = [...params.keys()].some((name) => !names.includes(name) || params.getAll(name).length !== 1)
    || (openText !== null && openText !== 'true' && openText !== 'false')
    || (product !== '' && !isNegativeStockAttemptId(product)) || (location !== '' && !isNegativeStockAttemptId(location))
  return { product, location, openOnly, query: invalid ? null : { productId: product === '' ? null : product,
    locationId: location === '' ? null : location, openOnly } }
}
export function NegativeStockAttemptPage() {
  const shell = useShellState(); const context = contextKey(shell)
  const [search, setSearch] = useSearchParams()
  const companyId = context !== null && shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  if (companyId === null || !isNegativeStockAttemptId(companyId) || shell.sessionInvalid) {
    return <main className={styles.page}><StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} /></main>
  }
  if (!hasCapability(shell, INVENTORY_STOCK_POSITIONS_CAPABILITY)) {
    return <main className={styles.page}><StateBlock subject={T.subject} state={{ kind: 'forbidden' }} /></main>
  }
  const namespace = contextKeyString(context!)
  return <AttemptView key={JSON.stringify([namespace, shell.sessionEpoch, shell.routeEntryGeneration, shell.requestFenceGeneration, search.toString()])}
    companyId={companyId} search={search.toString()} applySearch={(next) => setSearch(next)} />
}
function AttemptView({ companyId, search, applySearch }: { readonly companyId: string; readonly search: string;
  readonly applySearch: (next: URLSearchParams) => void }) {
  const parsed = useMemo(() => filtersOf(search), [search])
  const boundary = useAuthBoundary(); const boundaryRef = useRef(boundary)
  useLayoutEffect(() => { boundaryRef.current = boundary }, [boundary])
  const fence = useRef(createRequestFence())
  const [state, setState] = useState<State>({ type: 'loading' })
  const [product, setProduct] = useState(parsed.product); const [location, setLocation] = useState(parsed.location)
  const [openOnly, setOpenOnly] = useState(parsed.openOnly)
  const [filterError, setFilterError] = useState<string | null>(parsed.query === null ? T.invalidId : null)
  useLayoutEffect(() => { const owned = fence.current; return () => owned.invalidate() }, [])
  const read = useCallback(async () => {
    if (parsed.query === null) return
    const token = fence.current.capture(); const security = boundaryRef.current.capture()
    const outcome = await fetchNegativeStockAttempts(companyId, parsed.query, token.signal)
    if (!fence.current.isCurrent(token)) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundaryRef.current.report(outcome.status, security)
    if (!fence.current.isCurrent(token)) return
    if (outcome.type === 'ok-single') setState({ type: 'ready', data: outcome.data })
    else if (outcome.type === 'http' && outcome.status === 400 && negativeStockAttemptProblemCode(outcome.body) === NEGATIVE_STOCK_ATTEMPT_INVALID_QUERY) {
      setState({ type: 'failed', view: { kind: 'invalid-query' } })
    } else setState({ type: 'failed', view: classifyOutcome(outcome) })
  }, [companyId, parsed])
  useEffect(() => { void read() }, [read])
  function submit(event: FormEvent) {
    event.preventDefault()
    const productId = product.trim(); const locationId = location.trim()
    if ((productId !== '' && !isNegativeStockAttemptId(productId)) || (locationId !== '' && !isNegativeStockAttemptId(locationId))) {
      setFilterError(T.invalidId); return
    }
    setFilterError(null)
    const next = new URLSearchParams({ openOnly: openOnly ? 'true' : 'false' })
    if (productId !== '') next.set('productId', productId)
    if (locationId !== '') next.set('locationId', locationId)
    if (next.toString() === search) { setState({ type: 'loading' }); void read() }
    else applySearch(next)
  }
  function clear() {
    setProduct(''); setLocation(''); setOpenOnly(true); setFilterError(null)
    const next = new URLSearchParams({ openOnly: 'true' })
    if (next.toString() === search) { setState({ type: 'loading' }); void read() }
    else applySearch(next)
  }
  const reload = () => { setState({ type: 'loading' }); void read() }
  return <main className={styles.page} data-screen="STK-S21">
    <header className={styles.header}><div><p className={styles.eyebrow}>{T.eyebrow}</p><h1>{T.title}</h1></div>
      <Link to={INVENTORY_STOCK_POSITIONS_ROUTE}>{T.back}</Link></header>
    <div className={report.notes}><p>{T.scope}</p><p>{T.retention}</p><p>{T.actorNote}</p></div>
    <form className={report.filters} aria-label={T.filters} onSubmit={submit}>
      <Field label={T.product} help={T.optionalId}>{(control) => <input {...control} type="text" maxLength={36} value={product}
        onChange={(event) => setProduct(event.currentTarget.value)} />}</Field>
      <Field label={T.location} help={T.optionalId}>{(control) => <input {...control} type="text" maxLength={36} value={location}
        onChange={(event) => setLocation(event.currentTarget.value)} />}</Field>
      <label className={report.check}><input type="checkbox" checked={openOnly} onChange={(event) => setOpenOnly(event.currentTarget.checked)} />{T.openOnly}</label>
      <Button type="submit" variant="primary">{T.apply}</Button><Button type="button" onClick={clear}>{T.clear}</Button>
    </form>
    {filterError !== null ? <p role="alert" className={styles.error}>{filterError}</p> : null}
    {parsed.query === null ? null : <>
      {state.type === 'loading' ? <StateBlock state={{ kind: 'initial-loading' }} subject={T.subject} /> : null}
      {state.type === 'failed' && state.view.kind === 'invalid-query' ? <div role="alert"><p>{T.invalidQuery}</p><Button type="button" onClick={reload}>{T.refresh}</Button></div> : null}
      {state.type === 'failed' && state.view.kind !== 'invalid-query' ? <StateBlock state={state.view as RemoteViewState<unknown>} subject={T.subject} onRetry={reload} /> : null}
      {state.type === 'ready' ? <>
        <Button type="button" onClick={reload}>{T.refresh}</Button>
        {state.data.items.length === 0 ? <p role="status" data-state={parsed.query.openOnly || parsed.query.productId !== null || parsed.query.locationId !== null ? 'empty-filtered' : 'empty-no-data'}>
          {parsed.query.openOnly || parsed.query.productId !== null || parsed.query.locationId !== null ? T.emptyFiltered : T.empty}</p>
          : <><p role="status">{T.shown(state.data.items.length)}</p><AttemptTable items={state.data.items} /></>}
        {state.data.windowFull ? <p role="note">{T.windowFull}</p> : null}
      </> : null}
    </>}
  </main>
}
function AttemptTable({ items }: { readonly items: readonly NegativeStockAttempt[] }) {
  const columns: readonly DataTableColumn<NegativeStockAttempt>[] = [
    { key: 'time', header: T.headers.time, cell: (row) => <time dateTime={row.occurredAtUtc}>{row.occurredAtUtc}</time> },
    { key: 'operation', header: T.headers.operation, cell: (row) => <div className={report.details}><span>{row.commandName}</span><small>{row.movementKind}</small>
      <small>{T.movement}: {row.movementId}</small><small>{T.source}: {row.sourceKind} · {row.sourceId}</small>
      {row.sourceLineId === null ? null : <small>{T.sourceLine}: {row.sourceLineId}</small>}</div> },
    { key: 'label', header: T.headers.label, cell: (row) => <div className={report.details}><Link to={inventoryLabelDetailPath(row.labelId)}>{row.labelCode}</Link><small>{row.productId}</small></div> },
    { key: 'location', header: T.headers.location, cell: (row) => row.locationId },
    { key: 'actor', header: T.headers.actor, cell: (row) => row.actorId },
    { key: 'requested', header: T.headers.requested, align: 'end', cell: (row) => T.kg(row.requestedKg) },
    { key: 'available', header: T.headers.available, align: 'end', cell: (row) => T.kg(row.availableKg) },
    { key: 'missing', header: T.headers.missing, align: 'end', cell: (row) => T.kg(row.missingKg) },
    { key: 'status', header: T.headers.status, cell: (row) => row.resolution === null ? T.open : <div className={report.details}><strong>{T.resolved}</strong>
      <small>{T.count}: {row.resolution.stockCountId}</small><time dateTime={row.resolution.decidedAtUtc}>{row.resolution.decidedAtUtc}</time>
      <small>{T.decidedBy}: {row.resolution.decidedBy}</small></div> },
  ]
  return <DataTable label={T.caption} className={report.table} columns={columns} rows={items} rowKey={(row) => row.attemptId}
    rowAttributes={(row) => ({ 'data-attempt-id': row.attemptId })} />
}
