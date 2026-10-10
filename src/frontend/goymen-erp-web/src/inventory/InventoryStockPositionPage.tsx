import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { fetchStockDisplayPage, isStockClassification, isStockId, normalizedLabelSearch, type StockPositionFilters } from './stockDisplayApi'
import type { StockClassification, StockOwnerDisplay, StockOwnerDisplayRow as StockDisplayRow, StockPartyKind } from './stockDisplayContract'
import { createRequestFence } from '../purchasing/requestFence'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { CAPABILITY, CAPABILITY_ROUTE } from '../shell/capabilities'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { DataTable, type DataTableColumn } from '../ui/DataTable'
import { productDetailPath } from '../product/capabilities'
import { locationDetailPath } from '../organization/capabilities'
import { inventoryLabelDetailPath } from './labelDetailRoute'
import { PurchaseSupplySection } from './PurchaseSupplySection'
import { StockRowActionMenu } from './StockRowActionMenu'
import {
  normalizeStockSearch,
  permittedStockRowActions,
  STOCK_SEARCH_PARAM,
  stockRowActionsFor,
  stockRowActionState,
  type StockRowAction,
} from './stockRowActions'
import {
  CLASSIFICATION_TEXT,
  classificationText,
  custodianText,
  dimensionText,
  formatKg,
  ownerText,
  STOCK_TEXT,
} from './stockPositionText'
import styles from './stockPosition.module.css'

/**
 * ST-01 "Stok / Malzeme Pozisyonu" on the #597 opt-in contract (Stok UX FAZ-1; handoff STOK-UX-d821e6b8 r7).
 *
 * The URL holds every filter, so the browser's Back from a label returns to the same filtered list. `?etiket=` keeps
 * its meaning; the other filters only ever hold ids a row of this list returned (no picker, no typed id, no guess):
 * `urun`, `lokasyon`, `sinif`, and `sahipTuru` + `sahip` together. Paging follows the server's forward cursor; the
 * client never reads ahead or totals. STOCK-LOCATION-2: the location cell shows the Organization owner's code and name
 * that the `2.0` read (#607) publishes for the row's own location — never looked up per row or guessed; the existing
 * card link stays, with the same location id, only where the capability allows it. STOCK-OWNER-DISPLAY-3: a Cari
 * owner's code and legal name come with the same `3.0` read (#664) and link to the Cari card only with that card's
 * own capability; a company's own stock stays "Şirket malı".
 */

/** `KOD — Unvan`: the Cari owner's identity exactly as the read published it. */
function ownerIdentityText(owner: StockOwnerDisplay): string {
  return `${owner.code} — ${owner.legalName}`
}

/** The trade name, only when it says something the legal name does not. */
function distinctTradeName(owner: StockOwnerDisplay): string | null {
  return owner.tradeName !== null && owner.tradeName.trim() !== '' && owner.tradeName !== owner.legalName
    ? owner.tradeName
    : null
}

const PRODUCT_PARAM = 'urun'
const LOCATION_PARAM = 'lokasyon'
const CLASSIFICATION_PARAM = 'sinif'
const OWNER_KIND_PARAM = 'sahipTuru'
const OWNER_PARAM = 'sahip'
const FILTER_PARAMS = [STOCK_SEARCH_PARAM, PRODUCT_PARAM, LOCATION_PARAM, CLASSIFICATION_PARAM, OWNER_KIND_PARAM, OWNER_PARAM]

/** URL tokens, so no API value is shown in the address bar. */
const CLASSIFICATION_TOKEN: Record<StockClassification, string> = {
  Standard: 'standart',
  SecondQuality: 'ikinci-kalite',
  Scrap: 'hurda',
}
const OWNER_KIND_TOKEN: Record<StockPartyKind, string> = { Company: 'sirket', Counterparty: 'cari' }

function tokenKey<T extends string>(tokens: Record<T, string>, token: string | null): T | null {
  return (Object.keys(tokens) as T[]).find((key) => tokens[key] === token) ?? null
}

/** The filters in a URL. A value outside its grammar is no filter, so a hand-edited address never sends a 400. */
function filtersFromSearch(search: string): StockPositionFilters {
  const params = new URLSearchParams(search)
  const productId = params.get(PRODUCT_PARAM)?.toLowerCase() ?? null
  const locationId = params.get(LOCATION_PARAM)?.toLowerCase() ?? null
  const classification = tokenKey(CLASSIFICATION_TOKEN, params.get(CLASSIFICATION_PARAM))
  const ownerKind = tokenKey(OWNER_KIND_TOKEN, params.get(OWNER_KIND_PARAM))
  const ownerId = params.get(OWNER_PARAM)?.toLowerCase() ?? null
  return {
    q: normalizeStockSearch(params.get(STOCK_SEARCH_PARAM)),
    productId: isStockId(productId) ? productId : null,
    locationId: isStockId(locationId) ? locationId : null,
    classification: isStockClassification(classification) ? classification : null,
    owner: ownerKind !== null && isStockId(ownerId) ? { kind: ownerKind, id: ownerId } : null,
  }
}

/** The canonical URL search of a filter set (also the key the read follows). */
function searchOfFilters(filters: StockPositionFilters): string {
  const params = new URLSearchParams()
  if (filters.q !== '') params.set(STOCK_SEARCH_PARAM, filters.q)
  if (filters.productId !== null) params.set(PRODUCT_PARAM, filters.productId)
  if (filters.locationId !== null) params.set(LOCATION_PARAM, filters.locationId)
  if (filters.classification !== null) params.set(CLASSIFICATION_PARAM, CLASSIFICATION_TOKEN[filters.classification])
  if (filters.owner !== null) {
    params.set(OWNER_KIND_PARAM, OWNER_KIND_TOKEN[filters.owner.kind])
    params.set(OWNER_PARAM, filters.owner.id)
  }
  return params.toString()
}

function hasRowFilter(filters: StockPositionFilters): boolean {
  return filters.productId !== null || filters.locationId !== null || filters.classification !== null || filters.owner !== null
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

type MoreState =
  | { readonly type: 'idle' }
  | { readonly type: 'loading' }
  | { readonly type: 'failed'; readonly expired: boolean }

type ListState =
  | { readonly type: 'loading' }
  | {
    readonly type: 'ready'
    readonly rows: readonly StockDisplayRow[]
    readonly nextCursor: string | null
    readonly searchWindowFull: boolean
    readonly more: MoreState
  }
  | { readonly type: 'failed'; readonly view: RemoteViewState<unknown> }

export function InventoryStockPositionPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <InventoryStockPositionView
    key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`}
    companyId={shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null}
    canOpenProduct={hasCapability(shell, CAPABILITY.productItemsDetail)}
    canOpenLocation={hasCapability(shell, CAPABILITY.organizationLocationsDetail)}
    canOpenParty={hasCapability(shell, CAPABILITY.cariPartiesDetail)}
    rowActions={permittedStockRowActions((capability) => hasCapability(shell, capability))}
    canReadPurchaseSupply={hasCapability(shell, CAPABILITY.logisticsOperationsPurchase)}
    canOpenPurchaseOrder={hasCapability(shell, CAPABILITY.purchasingOrdersDetail)}
  />
}

/** GOY-DS-001: Kullanılabilir and the row actions stay at the table's right edge while it scrolls sideways. */
const AVAILABLE_PIN_WIDTH = '7.5rem'
const ACTIONS_PIN_WIDTH = '5.5rem'

function FilterIcon() {
  return <svg className={styles.icon} viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <path d="M4 5h16l-6 7.5V19l-4-2v-4.5z" />
  </svg>
}

function SearchIcon() {
  return <svg className={styles.searchIcon} viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <circle cx="11" cy="11" r="6.5" /><path d="M16 16l4.5 4.5" />
  </svg>
}

function ColumnsIcon() {
  return <svg className={styles.icon} viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <rect x="4" y="5" width="16" height="14" rx="1.5" /><path d="M10 5v14M14 5v14" />
  </svg>
}

function InventoryStockPositionView({
  companyId,
  canOpenProduct,
  canOpenLocation,
  canOpenParty,
  rowActions,
  canReadPurchaseSupply,
  canOpenPurchaseOrder,
}: {
  readonly companyId: string | null
  readonly canOpenProduct: boolean
  readonly canOpenLocation: boolean
  readonly canOpenParty: boolean
  readonly rowActions: readonly StockRowAction[]
  readonly canReadPurchaseSupply: boolean
  readonly canOpenPurchaseOrder: boolean
}) {
  const boundary = useAuthBoundary()
  // `useAuthBoundary` answers a new object on every render. Keyed on it, the read effect re-ran after EVERY render
  // and its cleanup aborted the search just submitted (R19 r14 run 36317283526). The read is keyed on the company and
  // the filters alone; the boundary of the latest committed render is read from a ref.
  const boundaryRef = useRef(boundary)
  useEffect(() => {
    boundaryRef.current = boundary
  }, [boundary])
  const [searchParams, setSearchParams] = useSearchParams()
  const filterSearch = searchOfFilters(filtersFromSearch(searchParams.toString()))
  const filters = useMemo(() => filtersFromSearch(filterSearch), [filterSearch])
  const searchTooLong = normalizedLabelSearch(filters.q) === null
  const [q, setQ] = useState(filters.q)
  const [state, setState] = useState<ListState>({ type: 'loading' })
  const [openMenu, setOpenMenu] = useState<string | null>(null)
  const [openPanel, setOpenPanel] = useState<'filters' | 'columns' | null>(null)
  const [showClassification, setShowClassification] = useState(false)
  const [showFreePhysical, setShowFreePhysical] = useState(false)
  const cards = useSyncExternalStore(subscribeCards, readCards, () => false)
  const listFence = useRef(createRequestFence())
  const moreFence = useRef(createRequestFence())
  // Focus needs a stable place when the control holding it goes away with the list it changed: the result region
  // after a filter, the first appended label after "Daha fazla göster".
  const resultsRef = useRef<HTMLElement>(null)
  const focusLabelRef = useRef<string | null>(null)
  useEffect(() => {
    if (state.type !== 'ready' || focusLabelRef.current === null) return
    const labelId = focusLabelRef.current
    focusLabelRef.current = null
    resultsRef.current?.querySelector<HTMLElement>(`[data-label-id="${labelId}"] a`)?.focus()
  }, [state])

  // The opened row menu is a popover inside the table's scroll area: bring it into view there (a last row's menu
  // would otherwise open below the visible part). Nothing else moves; focus stays on the toggle.
  useEffect(() => {
    if (openMenu === null) return
    resultsRef.current?.querySelector(`[data-label-id="${openMenu}"] ul`)?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
  }, [openMenu])

  const load = useCallback(async (current: StockPositionFilters) => {
    if (companyId === null) return
    moreFence.current.invalidate()
    const token = listFence.current.capture()
    const security = boundaryRef.current.capture()
    const outcome = await fetchStockDisplayPage(companyId, current, null, token.signal)
    if (!listFence.current.isCurrent(token) || outcome.type === 'fence-stale') return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundaryRef.current.report(outcome.status, security)
    }
    setState(outcome.type === 'ok-single'
      ? {
        type: 'ready', rows: outcome.data.items, nextCursor: outcome.data.nextCursor,
        searchWindowFull: outcome.data.searchWindowFull, more: { type: 'idle' },
      }
      : { type: 'failed', view: classifyOutcome(outcome) })
  }, [companyId])

  useEffect(() => {
    const currentFence = listFence.current
    const currentMore = moreFence.current
    if (companyId !== null && !searchTooLong) void load(filters)
    return () => {
      currentFence.invalidate()
      currentMore.invalidate()
    }
  }, [companyId, load, filters, searchTooLong])

  const loadMore = async () => {
    if (companyId === null || state.type !== 'ready' || state.nextCursor === null || state.more.type === 'loading') return
    const cursor = state.nextCursor
    const token = moreFence.current.capture()
    const security = boundaryRef.current.capture()
    setState({ ...state, more: { type: 'loading' } })
    const outcome = await fetchStockDisplayPage(companyId, filters, cursor, token.signal)
    if (!moreFence.current.isCurrent(token) || outcome.type === 'fence-stale') return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
      boundaryRef.current.report(outcome.status, security)
    }
    if (outcome.type === 'ok-single') focusLabelRef.current = outcome.data.items[0]?.labelId ?? null
    setState((previous) => {
      if (previous.type !== 'ready' || previous.nextCursor !== cursor) return previous
      if (outcome.type !== 'ok-single') {
        return { ...previous, more: { type: 'failed', expired: outcome.type === 'http' && outcome.status === 400 } }
      }
      // The server's keyset order continues; a label already on screen would mean the window moved under us.
      const seen = new Set(previous.rows.map((row) => row.labelId))
      if (outcome.data.items.some((row) => seen.has(row.labelId))) {
        return { ...previous, more: { type: 'failed', expired: true } }
      }
      return {
        type: 'ready', rows: [...previous.rows, ...outcome.data.items], nextCursor: outcome.data.nextCursor,
        searchWindowFull: outcome.data.searchWindowFull, more: { type: 'idle' },
      }
    })
  }

  if (companyId === null) {
    return <main className={styles.page}><p role="alert">{STOCK_TEXT.noCompany}</p></main>
  }

  const applyFilters = (next: StockPositionFilters, focusResults = false) => {
    const search = searchOfFilters(next)
    setOpenMenu(null)
    if (focusResults) resultsRef.current?.focus()
    if (search === filterSearch) {
      // The same filters again: the URL does not change, so read once more here.
      setState({ type: 'loading' })
      void load(next)
      return
    }
    setState({ type: 'loading' })
    // Every other parameter is kept; the filters are replaced as one set and the read follows the URL.
    const params = new URLSearchParams(searchParams)
    for (const name of FILTER_PARAMS) params.delete(name)
    new URLSearchParams(search).forEach((value, name) => params.set(name, value))
    setSearchParams(params, { replace: true })
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const next = normalizeStockSearch(q)
    if (normalizedLabelSearch(next) === null) return
    applyFilters({ ...filters, q: next })
  }

  const rows = state.type === 'ready' ? state.rows : []
  const productName = (productId: string): string => {
    const identity = rows.find((row) => row.productId === productId)?.productDisplay.identity
    return identity ? `${identity.code} ${identity.name}` : STOCK_TEXT.selectedProduct
  }
  const chips: { readonly key: string; readonly label: string; readonly remove: StockPositionFilters }[] = []
  if (filters.productId !== null) {
    chips.push({ key: 'product', label: `${STOCK_TEXT.filterChip.product}: ${productName(filters.productId)}`, remove: { ...filters, productId: null } })
  }
  const locationName = (locationId: string): string => {
    const display = rows.find((row) => row.locationId === locationId)?.locationDisplay
    return display ? `${display.locationCode} ${display.locationName}` : STOCK_TEXT.selectedLocation
  }
  if (filters.locationId !== null) {
    chips.push({ key: 'location', label: `${STOCK_TEXT.filterChip.location}: ${locationName(filters.locationId)}`, remove: { ...filters, locationId: null } })
  }
  if (filters.owner !== null) {
    const ownerId = filters.owner.id
    // The chip names the Cari only from a row of this list that carries that exact owner; otherwise it stays generic.
    const counterparty = rows.find((row) => row.ownerKind === 'Counterparty' && row.ownerId === ownerId)?.ownerDisplay
    const owner = filters.owner.kind === 'Counterparty'
      ? counterparty ? ownerIdentityText(counterparty) : STOCK_TEXT.selectedCounterparty
      : filters.owner.id === companyId ? ownerText('Company', filters.owner.id, companyId) : STOCK_TEXT.selectedCompany
    chips.push({
      key: 'owner',
      label: `${STOCK_TEXT.filterChip.owner}: ${owner}`,
      remove: { ...filters, owner: null },
    })
  }
  if (filters.classification !== null) {
    chips.push({
      key: 'classification',
      label: `${STOCK_TEXT.filterChip.classification}: ${CLASSIFICATION_TEXT[filters.classification]}`,
      remove: { ...filters, classification: null },
    })
  }

  const filterButton = (label: string, next: StockPositionFilters) => <button
    type="button"
    className={styles.filterButton}
    aria-label={label}
    title={label}
    onClick={() => applyFilters(next, true)}
  ><FilterIcon /></button>

  const lotToken = (name: string, value: string | null) => value === null
    ? <span className={styles.nowrap}>{name} {STOCK_TEXT.unknown}</span>
    : <span className={styles.nowrap}><span className={styles.visuallyHidden}>{name} </span>{value}</span>

  /** v1.2: a non-standard class stays visible next to Kullanılabilir while its own column is hidden. */
  const classMark = (row: StockDisplayRow) => !showClassification && (row.classification === 'SecondQuality' || row.classification === 'Scrap')
    ? <span className={styles.classMark}>{CLASSIFICATION_TEXT[row.classification]}</span>
    : null

  const labelCell = (row: StockDisplayRow) => <>
    <Link className={styles.labelLink} to={inventoryLabelDetailPath(row.labelId)} state={stockRowActionState(filters.q)}>{row.labelCode}</Link>
    <span className={styles.sub}>
      {lotToken(STOCK_TEXT.heat, row.lot.heatNumber ?? row.heatNumber)}{' · '}{lotToken(STOCK_TEXT.coil, row.lot.coilNumber ?? row.coilNumber)}
    </span>
  </>

  const productCell = (row: StockDisplayRow) => {
    const identity = row.productDisplay.identity
    return <div className={styles.cellLine}>
      <span className={styles.product}>
        {identity === null
          ? STOCK_TEXT.unknown
          : <><span className={styles.code}>{identity.code}</span><span className={styles.name}>{identity.name}</span></>}
        {canOpenProduct ? <Link className={styles.cardLink} to={productDetailPath(row.productId)}>{STOCK_TEXT.productCard}</Link> : null}
      </span>
      {filters.productId === row.productId ? null : filterButton(STOCK_TEXT.filterBy.product(row.labelCode), { ...filters, productId: row.productId })}
    </div>
  }

  const locationCell = (row: StockDisplayRow) => <div className={styles.cellLine}>
    <span className={styles.product}>
      <span className={styles.code}>{row.locationDisplay.locationCode}</span>
      <span className={styles.name}>{row.locationDisplay.locationName}</span>
      {canOpenLocation ? <Link className={styles.cardLink} to={locationDetailPath(row.locationId)}>{STOCK_TEXT.locationCard}</Link> : null}
    </span>
    {filters.locationId === row.locationId ? null : filterButton(STOCK_TEXT.filterBy.location(row.labelCode), { ...filters, locationId: row.locationId })}
  </div>

  const ownerCell = (row: StockDisplayRow) => {
    const holderDiffers = row.custodianKind !== row.ownerKind || row.custodianId !== row.ownerId
    const sameOwner = filters.owner !== null && filters.owner.kind === row.ownerKind && filters.owner.id === row.ownerId
    return <div className={styles.cellLine}>
      <span className={styles.product}>
        <span>{ownerText(row.ownerKind, row.ownerId, companyId)}</span>
        {/* A Cari owner's identity is the read's own `ownerDisplay` (never an id, never a second request): a link to
            the Cari card with that card's capability, the identical text without it. A company owner has none. */}
        {row.ownerKind === 'Counterparty' && row.ownerDisplay
          ? <span className={styles.name} data-testid={`stock-owner-${row.labelId}`}>
            {canOpenParty
              ? <Link className={styles.cardLink} to={CAPABILITY_ROUTE[CAPABILITY.cariPartiesDetail].replace(':partyId', encodeURIComponent(row.ownerDisplay.partyId))}>{ownerIdentityText(row.ownerDisplay)}</Link>
              : ownerIdentityText(row.ownerDisplay)}
          </span>
          : null}
        {row.ownerKind === 'Counterparty' && row.ownerDisplay && distinctTradeName(row.ownerDisplay) !== null
          ? <span className={styles.sub}>{distinctTradeName(row.ownerDisplay)}</span>
          : null}
        {holderDiffers
          ? <span className={styles.sub}>{STOCK_TEXT.custodian}: {custodianText(row.custodianKind, row.custodianId, companyId)}</span>
          : null}
      </span>
      {sameOwner ? null : filterButton(STOCK_TEXT.filterBy.owner(row.labelCode), { ...filters, owner: { kind: row.ownerKind, id: row.ownerId } })}
    </div>
  }

  const menu = (row: StockDisplayRow) => <StockRowActionMenu
    row={row}
    actions={stockRowActionsFor(rowActions, row)}
    search={filters.q}
    open={openMenu === row.labelId}
    onToggle={(open) => setOpenMenu(open ? row.labelId : null)}
  />

  const quantities = (row: StockDisplayRow) => [
    { key: 'physical', label: STOCK_TEXT.column.physical, value: row.physicalKg, shown: true },
    { key: 'blocked', label: STOCK_TEXT.column.blocked, value: row.blockedPhysicalKg, shown: true },
    { key: 'free', label: STOCK_TEXT.column.freePhysical, value: row.freePhysicalKg, shown: showFreePhysical },
    { key: 'allocated', label: STOCK_TEXT.column.allocated, value: row.activeAllocationKg, shown: true },
    { key: 'available', label: STOCK_TEXT.column.available, value: row.availableKg, shown: true },
  ].filter((entry) => entry.shown)

  /** The same facts and order as the cards; only the table's layout comes from the shared DataTable. */
  const quantityColumn = (key: string, label: string, value: (row: StockDisplayRow) => string): DataTableColumn<StockDisplayRow> =>
    key === 'available'
      ? {
        key, header: label, align: 'end', pin: 'end', pinWidth: AVAILABLE_PIN_WIDTH, className: styles.available,
        cell: (row) => <>{formatKg(value(row))}{classMark(row)}</>,
      }
      : { key, header: label, align: 'end', cell: (row) => formatKg(value(row)) }
  const columns: DataTableColumn<StockDisplayRow>[] = [
    { key: 'label', header: STOCK_TEXT.column.label, pin: 'start', className: styles.labelCol, cell: labelCell },
    { key: 'product', header: STOCK_TEXT.column.product, className: styles.productCol, cell: productCell },
    {
      key: 'dimension', header: STOCK_TEXT.column.dimension, className: styles.nowrap,
      cell: (row) => dimensionText(row.productDisplay.technical),
    },
    ...(showClassification
      ? [{ key: 'classification', header: STOCK_TEXT.column.classification, cell: (row: StockDisplayRow) => classificationText(row.classification) }]
      : []),
    { key: 'location', header: STOCK_TEXT.column.location, cell: locationCell },
    { key: 'owner', header: STOCK_TEXT.column.owner, cell: ownerCell },
    quantityColumn('physical', STOCK_TEXT.column.physical, (row) => row.physicalKg),
    quantityColumn('blocked', STOCK_TEXT.column.blocked, (row) => row.blockedPhysicalKg),
    ...(showFreePhysical ? [quantityColumn('free', STOCK_TEXT.column.freePhysical, (row) => row.freePhysicalKg)] : []),
    quantityColumn('allocated', STOCK_TEXT.column.allocated, (row) => row.activeAllocationKg),
    quantityColumn('available', STOCK_TEXT.column.available, (row) => row.availableKg),
    {
      key: 'actions', header: STOCK_TEXT.column.actions, pin: 'end', pinWidth: ACTIONS_PIN_WIDTH,
      className: styles.actionsCol, cell: menu,
    },
  ]

  const restart = () => { setState({ type: 'loading' }); void load(filters) }

  return <main className={styles.page}>
    <header className={styles.header}><h1>{STOCK_TEXT.heading}</h1></header>

    <div className={styles.toolbar}>
      <form className={styles.search} onSubmit={submit} role="search">
        <label htmlFor="stock-position-search" className={styles.visuallyHidden}>{STOCK_TEXT.searchLabel}</label>
        <span className={styles.searchBox}>
          <SearchIcon />
          <input
            id="stock-position-search"
            className={styles.searchInput}
            value={q}
            maxLength={64}
            placeholder={STOCK_TEXT.searchLabel}
            aria-invalid={searchTooLong || undefined}
            aria-describedby={searchTooLong ? 'stock-position-search-error' : undefined}
            onChange={(event) => setQ(event.target.value)}
          />
        </span>
        <Button type="submit" variant="secondary">{STOCK_TEXT.searchButton}</Button>
      </form>
      <div className={styles.disclosures}>
        <Button
          type="button"
          variant={openPanel === 'filters' ? 'secondary' : 'ghost'}
          className={styles.disclosure}
          aria-expanded={openPanel === 'filters'}
          aria-controls="stock-other-filters"
          onClick={() => setOpenPanel(openPanel === 'filters' ? null : 'filters')}
        ><FilterIcon />{STOCK_TEXT.otherFilters}</Button>
        <Button
          type="button"
          variant={openPanel === 'columns' ? 'secondary' : 'ghost'}
          className={styles.disclosure}
          aria-expanded={openPanel === 'columns'}
          aria-controls="stock-columns"
          onClick={() => setOpenPanel(openPanel === 'columns' ? null : 'columns')}
        ><ColumnsIcon />{STOCK_TEXT.columns}</Button>
      </div>
    </div>
    {searchTooLong
      ? <p id="stock-position-search-error" role="alert" className={styles.error}>{STOCK_TEXT.searchTooLong}</p>
      : null}
    <div id="stock-other-filters" className={styles.panel} hidden={openPanel !== 'filters'}>
      <Field label={STOCK_TEXT.classificationFilter} id="stock-classification-filter" className={styles.panelField}>
        {(control) => <select
          {...control}
          className={styles.select}
          value={filters.classification ?? ''}
          onChange={(event) => applyFilters({
            ...filters,
            classification: isStockClassification(event.target.value) ? event.target.value : null,
          })}
        >
          <option value="">{STOCK_TEXT.classificationAll}</option>
          {(Object.keys(CLASSIFICATION_TEXT) as StockClassification[]).map((value) =>
            <option key={value} value={value}>{CLASSIFICATION_TEXT[value]}</option>)}
        </select>}
      </Field>
    </div>
    <fieldset id="stock-columns" className={styles.panel} hidden={openPanel !== 'columns'}>
      <legend>{STOCK_TEXT.visibleColumns}</legend>
      <label className={styles.check}><input type="checkbox" aria-label={STOCK_TEXT.columnToggle(STOCK_TEXT.column.classification)} checked={showClassification} onChange={(event) => setShowClassification(event.target.checked)} /> {STOCK_TEXT.column.classification}</label>
      <label className={styles.check}><input type="checkbox" aria-label={STOCK_TEXT.columnToggle(STOCK_TEXT.column.freePhysical)} checked={showFreePhysical} onChange={(event) => setShowFreePhysical(event.target.checked)} /> {STOCK_TEXT.column.freePhysical}</label>
    </fieldset>

    {chips.length > 0 || filters.q !== ''
      ? <div className={styles.chips}>
        {chips.length > 0
          ? <ul aria-label={STOCK_TEXT.activeFilters}>{chips.map((chip) => <li key={chip.key}>
            <span>{chip.label}</span>
            <button type="button" aria-label={STOCK_TEXT.filterChip.remove(chip.label)} onClick={() => applyFilters(chip.remove, true)}>×</button>
          </li>)}</ul>
          : null}
        <Button type="button" variant="ghost" size="sm" onClick={() => { setQ(''); applyFilters({ q: '', productId: null, locationId: null, classification: null, owner: null }, true) }}>
          {STOCK_TEXT.clearFilters}
        </Button>
      </div>
      : null}

    <section ref={resultsRef} tabIndex={-1} aria-label={STOCK_TEXT.results} className={styles.results}>
    {state.type === 'loading' && !searchTooLong ? <p role="status" className={styles.status}>{STOCK_TEXT.loading}</p> : null}
    {state.type === 'failed'
      ? <>
        <StateBlock state={state.view} subject={STOCK_TEXT.stateSubject} onRetry={restart} />
        {/* A 409 here is a label that moved between the owner's two reads: transient, so the list can simply restart. */}
        {state.view.kind === 'conflict'
          ? <Button type="button" variant="secondary" className={styles.more} onClick={restart}>{STOCK_TEXT.restart}</Button>
          : null}
      </>
      : null}
    {state.type === 'ready' && state.rows.length === 0
      ? <p role="status" className={styles.status}>{filters.q !== '' || hasRowFilter(filters) ? STOCK_TEXT.emptyFiltered : STOCK_TEXT.emptyNoFilter}</p>
      : null}
    {state.type === 'ready' && state.rows.length > 0
      ? <>
        {cards
          ? <ul className={styles.cards} aria-label={STOCK_TEXT.heading} data-testid="stock-position-cards">
            {state.rows.map((row) => <li key={row.labelId} className={styles.card} data-label-id={row.labelId}>
              <div className={styles.cardHead}><div className={styles.cardLabel}>{labelCell(row)}</div>{menu(row)}</div>
              <dl className={styles.cardFacts}>
                <div className={styles.wide}><dt>{STOCK_TEXT.column.product}</dt><dd>{productCell(row)}</dd></div>
                <div><dt>{STOCK_TEXT.column.dimension}</dt><dd>{dimensionText(row.productDisplay.technical)}</dd></div>
                {showClassification ? <div><dt>{STOCK_TEXT.column.classification}</dt><dd>{classificationText(row.classification)}</dd></div> : null}
                <div><dt>{STOCK_TEXT.column.location}</dt><dd>{locationCell(row)}</dd></div>
                <div><dt>{STOCK_TEXT.column.owner}</dt><dd>{ownerCell(row)}</dd></div>
              </dl>
              <dl className={styles.cardQuantities}>
                {quantities(row).map((entry) => <div key={entry.key} className={entry.key === 'available' ? styles.available : undefined}>
                  <dt>{entry.label}</dt><dd>{formatKg(entry.value)}{entry.key === 'available' ? classMark(row) : null}</dd>
                </div>)}
              </dl>
            </li>)}
          </ul>
          : <DataTable
            label={STOCK_TEXT.heading}
            columns={columns}
            rows={state.rows}
            rowKey={(row) => row.labelId}
            rowAttributes={(row) => ({ 'data-label-id': row.labelId })}
            className={styles.tableArea}
          />}
        {state.more.type === 'loading' ? <p role="status" className={styles.status}>{STOCK_TEXT.loadingMore}</p> : null}
        {state.more.type === 'failed'
          ? <div role="alert" className={styles.moreError}>
            <p>{state.more.expired ? STOCK_TEXT.loadMoreExpired : STOCK_TEXT.loadMoreFailed}</p>
            <Button type="button" variant="secondary" size="sm" onClick={restart}>{STOCK_TEXT.restart}</Button>
          </div>
          : null}
        {state.nextCursor !== null && !(state.more.type === 'failed' && state.more.expired)
          ? <Button
            type="button"
            variant="secondary"
            className={styles.more}
            aria-disabled={state.more.type === 'loading' || undefined}
            onClick={() => void loadMore()}
          >{STOCK_TEXT.loadMore}</Button>
          : null}
      </>
      : null}
    </section>
    {canReadPurchaseSupply
      ? <PurchaseSupplySection companyId={companyId} canOpenOrder={canOpenPurchaseOrder} canOpenProduct={canOpenProduct} />
      : null}
  </main>
}
