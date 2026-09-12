/**
 * U-01 Ürünler — list, pinned to `GET /api/v1/products` (`ProductPage`).
 *
 * COLUMN SET. Narrower than §8.2 asks for, and deliberately so: the published
 * `ProductDraftView` carries `groupId/classId/familyId` as IDS, and carries no
 * primary stock unit, no per-area completeness and no alias/match information.
 * Resolving ids to labels in the browser would be the client fan-out §19.5 forbids,
 * and inventing the rest is the fabricated summary §9 forbids. So the screen shows
 * what the contract gives, SAYS that it is narrower, and the gap stays open with
 * the contract owner rather than being papered over.
 *
 * Forbidden here and absent: stock quantity, price, package instances, lot/label
 * values, supplier part codes, recipes, AI.
 */

import { useEffect, useMemo, useRef, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchProducts, type LifecycleFilter } from './api'
import {
  PRODUCT_LIFECYCLE,
  PRODUCT_PROBLEM_CODE,
  PRODUCT_ROLE,
  productProblemCodeOf,
  type ProductCore,
  type ProductPage,
  type ProductRoleName,
} from './contract'
import {
  LIFECYCLE_LABEL,
  PRODUCT_LIST_TEXT,
  ROLE_LABEL,
  lifecycleLabel,
  rolesText,
} from './productText'
import {
  PRODUCT_CAPABILITY,
  PRODUCT_CAPABILITY_ROUTE,
  productDetailPath,
} from './capabilities'
import {
  PAGE_SIZE_OPTIONS,
  clearedFilters,
  isFiltered,
  readProductListView,
  rememberOpenedRecord,
  takeOpenedRecord,
  writeProductListView,
  type ProductListView,
} from './listViewState'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './ProductListPage.module.css'

const LIFECYCLE_FILTER_OPTIONS: readonly LifecycleFilter[] = ['all', ...PRODUCT_LIFECYCLE]

const LIFECYCLE_FILTER_LABEL: Record<LifecycleFilter, string> = {
  all: 'Hepsi',
  Draft: LIFECYCLE_LABEL.Draft,
  Active: LIFECYCLE_LABEL.Active,
  Inactive: LIFECYCLE_LABEL.Inactive,
}

/**
 * The filters the user has applied, in their own words.
 *
 * A filter the user cannot see is a filter they cannot undo — and the most likely
 * cause of an unexpectedly empty list is a lifecycle or role filter set two screens
 * ago.
 */
function activeFilterLabels(view: ProductListView): string[] {
  const labels: string[] = []
  if (view.q.trim().length > 0) labels.push(`Arama: “${view.q.trim()}”`)
  if (view.lifecycle !== 'all') labels.push(`Durum: ${LIFECYCLE_FILTER_LABEL[view.lifecycle]}`)
  if (view.role !== '') labels.push(`Rol: ${ROLE_LABEL[view.role]}`)
  return labels
}

function formatInstant(value: string): string {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

export function ProductListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  // Remounted per context, so every "once per mount" below is "once per context".
  return <ProductList key={namespace ?? 'no-context'} namespace={namespace} />
}

function ProductList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const [restored] = useState<ProductListView>(() => readProductListView(namespace))
  const [q, setQ] = useState(restored.q)
  const [lifecycle, setLifecycle] = useState<LifecycleFilter>(restored.lifecycle)
  const [role, setRole] = useState<ProductRoleName | ''>(restored.role)
  const [pageSize, setPageSize] = useState<number>(restored.pageSize)
  const [cursor, setCursor] = useState<string | undefined>(undefined)
  const [loaded, setLoaded] = useState<readonly ProductCore[]>([])
  const searchRef = useRef<HTMLInputElement>(null)
  const [returnToId] = useState<string | null>(() => takeOpenedRecord(namespace))
  const returnHandled = useRef(false)

  const boundary = useAuthBoundary()
  /**
   * Opening a card is a SEPARATE grant. When it is absent the code renders as plain
   * text rather than a link: a link that does nothing is a worse answer than a row
   * that simply is not clickable.
   */
  const canOpenDetail = hasCapability(shell, PRODUCT_CAPABILITY.productsDetail)
  const canCreate = hasCapability(shell, PRODUCT_CAPABILITY.productsCreate)
  const entryGeneration = shell.routeEntryGeneration

  const view: ProductListView = useMemo(
    () => ({ q, lifecycle, role, pageSize }),
    [q, lifecycle, role, pageSize],
  )
  const filterActive = isFiltered(view)
  const filterLabels = useMemo(() => activeFilterLabels(view), [view])

  useEffect(() => {
    writeProductListView(namespace, view)
  }, [namespace, view])

  /**
   * §8.1: a cursor is bound to the query that produced it, so a filter change drops
   * it — and the accumulated rows with it, since they were read under the previous
   * query. Held in STATE, not a ref: a ref read during render breaks render purity.
   */
  const queryShape = JSON.stringify([q, lifecycle, role, pageSize])
  const [previousShape, setPreviousShape] = useState(queryShape)
  if (previousShape !== queryShape) {
    setPreviousShape(queryShape)
    if (cursor !== undefined) setCursor(undefined)
    if (loaded.length > 0) setLoaded([])
  }

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'products', queryShape, cursor ?? ''],
    /**
     * Bound to a resolved shell context, NOT to a single company. The Product master
     * is global (`PRD-SCOPE-001=A`), and `IProductService` requires server-resolved
     * global authorization — "No company-scoped grant satisfies this contract". The
     * context is still required: capabilities resolve per context.
     */
    enabled: namespace !== null,
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => {
      // SAB-001: security bindings captured BEFORE the request starts.
      const snapshot = boundary.capture()
      const outcome = await fetchProducts(
        { q, lifecycle, role: role === '' ? undefined : role, pageSize, cursor },
        signal,
      )
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const outcome = query.data
  /**
   * `keepPreviousData` hands back the PREVIOUS page while the new query is in
   * flight. That page answers the previous question, so it is neither appended nor
   * labelled as an empty result.
   */
  const showingPrevious = query.isPlaceholderData
  const page: ProductPage | null =
    !showingPrevious && outcome !== undefined && outcome.type === 'ok-single' ? outcome.data : null

  const appendedFor = useRef<string | null>(null)
  useEffect(() => {
    if (page === null) return
    const marker = `${entryGeneration}:${queryShape}:${cursor ?? 'first'}`
    if (appendedFor.current === marker) return
    appendedFor.current = marker
    setLoaded((current) => (cursor === undefined ? page.items : [...current, ...page.items]))
  }, [page, cursor, entryGeneration, queryShape])

  const rows = loaded.length === 0 && cursor === undefined && page !== null ? page.items : loaded

  /**
   * Move focus to the row the user came back from. One attempt, first page only:
   * walking forward to find a record that may have left the result set would page
   * through the whole catalogue to prove a negative.
   */
  useEffect(() => {
    if (returnToId === null || returnHandled.current || rows.length === 0) return
    returnHandled.current = true
    document.getElementById(`product-row-${returnToId}`)?.focus()
  }, [returnToId, rows.length])

  const problemCode =
    outcome !== undefined && outcome.type === 'http' ? productProblemCodeOf(outcome.body) : null

  // §8.3: a FIRST-page failure is a full error state; a NEXT-page failure keeps the
  // rows already on screen.
  const nextPageFailed =
    cursor !== undefined && outcome !== undefined && outcome.type !== 'ok-single'

  /**
   * `classifyOutcome` cannot separate the two empty cases: the read returns ONE page
   * object, so `ok-single` always classifies as `ready`. "No products exist" and
   * "the filter excluded everything" are different facts with different things for
   * the user to do, so the distinction comes from the view the user set.
   */
  const state: RemoteViewState<ProductPage | readonly ProductPage[]> =
    query.isPending || showingPrevious || outcome === undefined
      ? { kind: 'initial-loading' }
      : outcome.type === 'ok-single' && outcome.data.items.length === 0
        ? { kind: filterActive ? 'empty-filtered' : 'empty-no-data' }
        : classifyOutcome(outcome)

  function restartList() {
    setCursor(undefined)
    setLoaded([])
    void query.refetch()
  }

  function clearAll() {
    const next = clearedFilters(view)
    setQ(next.q)
    setLifecycle(next.lifecycle)
    setRole(next.role)
    searchRef.current?.focus()
  }

  /**
   * An invalid cursor is recoverable and must say so. The generic conflict text —
   * "the record changed, review the difference" — is false here: nothing changed
   * under the user. Branch on the stable CODE, never on localized text.
   */
  if (problemCode === PRODUCT_PROBLEM_CODE.invalidCursor) {
    return (
      <section className={styles.page} aria-labelledby="product-list-heading">
        <h1 id="product-list-heading" className={styles.title}>
          {PRODUCT_LIST_TEXT.title}
        </h1>
        <div className={styles.block} role="alert">
          <p className={styles.blockTitle}>{PRODUCT_LIST_TEXT.invalidCursor}</p>
          <p className={styles.blockDetail}>{PRODUCT_LIST_TEXT.invalidCursorDetail}</p>
          <button type="button" className={styles.primaryAction} onClick={restartList}>
            {PRODUCT_LIST_TEXT.invalidCursorAction}
          </button>
        </div>
      </section>
    )
  }

  const showRows = rows.length > 0

  return (
    <section className={styles.page} aria-labelledby="product-list-heading">
      <header className={styles.header}>
        <div>
          <h1 id="product-list-heading" className={styles.title}>
            {PRODUCT_LIST_TEXT.title}
          </h1>
          <p className={styles.scopeNote}>{PRODUCT_LIST_TEXT.scopeNote}</p>
          <p className={styles.scopeNote}>{PRODUCT_LIST_TEXT.projectionNote}</p>
        </div>
        {canCreate ? (
          <Link
            className={styles.primaryAction}
            to={PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsCreate]}
          >
            {PRODUCT_LIST_TEXT.createAction}
          </Link>
        ) : null}
      </header>

      <form
        className={styles.filters}
        aria-label="Ürün filtreleri"
        onSubmit={(event) => event.preventDefault()}
      >
        <div className={styles.field}>
          <label htmlFor="product-q">{PRODUCT_LIST_TEXT.searchLabel}</label>
          <input
            id="product-q"
            ref={searchRef}
            type="search"
            value={q}
            maxLength={200}
            onChange={(event) => setQ(event.target.value)}
          />
        </div>

        <div className={styles.field}>
          <label htmlFor="product-lifecycle">{PRODUCT_LIST_TEXT.lifecycleLabel}</label>
          <select
            id="product-lifecycle"
            value={lifecycle}
            onChange={(event) => setLifecycle(event.target.value as LifecycleFilter)}
          >
            {LIFECYCLE_FILTER_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {LIFECYCLE_FILTER_LABEL[option]}
              </option>
            ))}
          </select>
        </div>

        {/*
          ONE role. `ProductListQuery.Role` is a single value and there is no
          `roleMatch`, so a multi-select with any/all semantics would be a meaning
          this screen invented. The hint says what the filter actually does.
        */}
        <div className={styles.field}>
          <label htmlFor="product-role">{PRODUCT_LIST_TEXT.roleLabel}</label>
          <select
            id="product-role"
            value={role}
            aria-describedby="product-role-hint"
            onChange={(event) => setRole(event.target.value as ProductRoleName | '')}
          >
            <option value="">{PRODUCT_LIST_TEXT.roleAll}</option>
            {PRODUCT_ROLE.map((name) => (
              <option key={name} value={name}>
                {ROLE_LABEL[name]}
              </option>
            ))}
          </select>
          <p id="product-role-hint" className={styles.hint}>
            {PRODUCT_LIST_TEXT.roleHint}
          </p>
        </div>

        <div className={styles.field}>
          <label htmlFor="product-page-size">{PRODUCT_LIST_TEXT.pageSizeLabel}</label>
          <select
            id="product-page-size"
            value={pageSize}
            onChange={(event) => setPageSize(Number(event.target.value))}
          >
            {PAGE_SIZE_OPTIONS.map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </div>

        {filterActive ? (
          <button type="button" className={styles.secondaryAction} onClick={clearAll}>
            {PRODUCT_LIST_TEXT.clearFilters}
          </button>
        ) : null}
      </form>

      {filterLabels.length > 0 ? (
        <p className={styles.activeFilters} data-testid="product-active-filters">
          <span className={styles.activeFiltersLead}>Uygulanan filtreler:</span>{' '}
          {filterLabels.join(' · ')}
        </p>
      ) : null}

      {!showRows ? (
        <StateBlock state={state} subject="ürün" onRetry={() => void query.refetch()} />
      ) : (
        <>
          <table className={styles.table}>
            {/* No total: `ProductPage` publishes none, so the caption says what is shown. */}
            <caption className={styles.caption}>{PRODUCT_LIST_TEXT.shown(rows.length)}</caption>
            <thead>
              <tr>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.code}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.name}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.roles}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.lifecycle}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.updatedAt}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((item) => (
                <tr key={item.id}>
                  <td>
                    {canOpenDetail ? (
                      <Link
                        id={`product-row-${item.id}`}
                        to={productDetailPath(item.id)}
                        onClick={() => rememberOpenedRecord(namespace, item.id)}
                      >
                        {item.code}
                      </Link>
                    ) : (
                      <span id={`product-row-${item.id}`}>{item.code}</span>
                    )}
                  </td>
                  <td>{item.name}</td>
                  <td>{rolesText(item.roles)}</td>
                  <td>
                    <span
                      className={styles.badge}
                      data-lifecycle={item.lifecycle.known ? item.lifecycle.value : 'unknown'}
                    >
                      {lifecycleLabel(item.lifecycle)}
                    </span>
                  </td>
                  <td>{formatInstant(item.updatedAtUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>

          {nextPageFailed ? (
            <div className={styles.block} role="alert">
              <p className={styles.blockTitle}>{PRODUCT_LIST_TEXT.nextPageFailed}</p>
              <button
                type="button"
                className={styles.secondaryAction}
                onClick={() => void query.refetch()}
              >
                {PRODUCT_LIST_TEXT.nextPageRetry}
              </button>
            </div>
          ) : null}

          {/* `nextCursor !== null` is the only "there is more" signal the page has. */}
          {page?.nextCursor !== null && page?.nextCursor !== undefined && !nextPageFailed ? (
            <button
              type="button"
              className={styles.secondaryAction}
              disabled={query.isFetching}
              onClick={() => setCursor(page.nextCursor ?? undefined)}
            >
              {query.isFetching ? PRODUCT_LIST_TEXT.loadingMore : PRODUCT_LIST_TEXT.loadMore}
            </button>
          ) : null}
        </>
      )}
    </section>
  )
}
