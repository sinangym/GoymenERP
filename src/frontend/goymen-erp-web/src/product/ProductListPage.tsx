/**
 * U-01 Ürünler — list, pinned to `GET /api/v1/products` (`ProductPage`).
 *
 * The projection now carries server-resolved `labels` {group, class, family,
 * primaryStockUnit} and `completeness` on every row, so the columns §8.2 asks for
 * are real rather than derived: the client never resolves a UUID itself, which is
 * the fan-out §19.5 forbids.
 *
 * Filters and ordering are the contract's own vocabulary — distinct roles combined
 * by `roleMatch`, `sort` over code/name/updatedAtUtc with a direction, and a
 * completeness filter. Nothing here invents a semantics the server does not define.
 *
 * Forbidden and absent, per §3/§9: stock quantity, price, package instances,
 * lot/label values, supplier part codes, recipes, AI.
 */

import { useEffect, useMemo, useRef, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import {
  fetchProducts,
  type CompletenessFilter,
  type LifecycleFilter,
  type ProductSort,
  type RoleMatch,
  type SortDirection,
} from './api'
import {
  COMPLETENESS_EVALUATION,
  PRODUCT_LIFECYCLE,
  PRODUCT_PROBLEM_CODE,
  PRODUCT_ROLE,
  productProblemCodeOf,
  type ProductCore,
  type ProductPage,
  type ProductRoleName,
} from './contract'
import {
  COMPLETENESS_LABEL,
  COMPLETENESS_UNKNOWN_LABEL,
  LIFECYCLE_LABEL,
  PRODUCT_LIST_TEXT,
  ROLE_LABEL,
  UNASSIGNED,
  labelOrUnavailable,
  lifecycleLabel,
  rolesText,
} from './productText'
import {
  PRODUCT_CAPABILITY,
  PRODUCT_CAPABILITY_ROUTE,
  PRODUCT_IMPORT_ROUTE,
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

const COMPLETENESS_FILTER_OPTIONS: readonly CompletenessFilter[] = [
  'all',
  ...COMPLETENESS_EVALUATION,
]

const COMPLETENESS_FILTER_LABEL: Record<CompletenessFilter, string> = {
  all: 'Hepsi',
  Complete: COMPLETENESS_LABEL.Complete,
  Incomplete: COMPLETENESS_LABEL.Incomplete,
  NotEvaluated: COMPLETENESS_LABEL.NotEvaluated,
}

const SORT_LABEL: Record<ProductSort, string> = {
  code: 'Kod',
  name: 'Ad',
  updatedAtUtc: 'Son değişiklik',
}

/**
 * The filters the user has applied, in their own words.
 *
 * A filter the user cannot see is a filter they cannot undo — and the most likely
 * cause of an unexpectedly empty list is a lifecycle or role filter set two screens
 * ago. Sort is not listed: it is not a filter.
 */
function activeFilterLabels(view: ProductListView): string[] {
  const labels: string[] = []
  if (view.q.trim().length > 0) labels.push(`Arama: “${view.q.trim()}”`)
  if (view.lifecycle !== 'all') labels.push(`Durum: ${LIFECYCLE_FILTER_LABEL[view.lifecycle]}`)
  if (view.roles.length > 0) {
    const names = view.roles.map((role) => ROLE_LABEL[role]).join(', ')
    const match = view.roleMatch === 'all' ? PRODUCT_LIST_TEXT.roleAll : PRODUCT_LIST_TEXT.roleAny
    labels.push(`Rol (${match.toLocaleLowerCase('tr')}): ${names}`)
  }
  if (view.completeness !== 'all') {
    labels.push(`Tamlık: ${COMPLETENESS_FILTER_LABEL[view.completeness]}`)
  }
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
  /**
   * The operation's origin: the committed company, sent as `X-Company-Id` and
   * validated by the server (§15). Not the Product's owner and not a grant — the
   * actual endpoints require it (`RequireCompanyContext`), so without one the
   * read is not made.
   */
  const origin = shell.activeContext?.companyId ?? null
  const [restored] = useState<ProductListView>(() => readProductListView(namespace))
  const [q, setQ] = useState(restored.q)
  const [lifecycle, setLifecycle] = useState<LifecycleFilter>(restored.lifecycle)
  const [roles, setRoles] = useState<readonly ProductRoleName[]>(restored.roles)
  const [roleMatch, setRoleMatch] = useState<RoleMatch>(restored.roleMatch)
  const [completeness, setCompleteness] = useState<CompletenessFilter>(restored.completeness)
  const [sort, setSort] = useState<ProductSort>(restored.sort)
  const [direction, setDirection] = useState<SortDirection>(restored.direction)
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
    () => ({ q, lifecycle, roles, roleMatch, completeness, sort, direction, pageSize }),
    [q, lifecycle, roles, roleMatch, completeness, sort, direction, pageSize],
  )
  const filterActive = isFiltered(view)
  const filterLabels = useMemo(() => activeFilterLabels(view), [view])

  useEffect(() => {
    writeProductListView(namespace, view)
  }, [namespace, view])

  /**
   * A cursor is bound to the query that produced it, so ANY query change — filter
   * or ordering — drops it, and the accumulated rows with it: they were read under
   * the previous query and the server's ordering key may have changed underneath
   * them. Held in STATE, not a ref: a ref read during render breaks render purity.
   */
  const queryShape = JSON.stringify([
    q,
    lifecycle,
    roles,
    roleMatch,
    completeness,
    sort,
    direction,
    pageSize,
  ])
  const [previousShape, setPreviousShape] = useState(queryShape)
  if (previousShape !== queryShape) {
    setPreviousShape(queryShape)
    if (cursor !== undefined) setCursor(undefined)
    if (loaded.length > 0) setLoaded([])
  }

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'products', queryShape, cursor ?? ''],
    /**
     * Bound to a resolved shell context WITH a committed company. The Product master
     * is global (`PRD-SCOPE-001=A`) and `IProductService` requires server-resolved
     * global authorization — the company is not a grant. It is the ORIGIN the actual
     * endpoints require (`RequireCompanyContext`) and validate, so without one there
     * is no request to make.
     */
    enabled: namespace !== null && origin !== null,
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => {
      // SAB-001: security bindings captured BEFORE the request starts.
      const snapshot = boundary.capture()
      const outcome = await fetchProducts(
        {
          q,
          lifecycle,
          roles: roles.length > 0 ? roles : undefined,
          roleMatch: roles.length > 0 ? roleMatch : undefined,
          completeness,
          sort,
          direction,
          pageSize,
          cursor,
        },
        { companyId: origin as string },
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

  // A FIRST-page failure is a full error state; a NEXT-page failure keeps the rows
  // already on screen.
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

  function toggleRole(role: ProductRoleName) {
    setRoles((current) =>
      current.includes(role) ? current.filter((name) => name !== role) : [...current, role],
    )
  }

  function clearAll() {
    const next = clearedFilters(view)
    setQ(next.q)
    setLifecycle(next.lifecycle)
    setRoles(next.roles)
    setRoleMatch(next.roleMatch)
    setCompleteness(next.completeness)
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

  return (
    <section className={styles.page} aria-labelledby="product-list-heading">
      <header className={styles.header}>
        <div>
          <h1 id="product-list-heading" className={styles.title}>
            {PRODUCT_LIST_TEXT.title}
          </h1>
          <p className={styles.scopeNote}>{PRODUCT_LIST_TEXT.scopeNote}</p>
        </div>
        {canCreate ? (
          <div className={styles.headerActions}>
            <Link className={styles.secondaryAction} to={PRODUCT_IMPORT_ROUTE}>
              Excel’den içe aktar
            </Link>
            <Link
              className={styles.primaryAction}
              to={PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsCreate]}
            >
              {PRODUCT_LIST_TEXT.createAction}
            </Link>
          </div>
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

        <fieldset className={styles.roleFilter}>
          <legend>{PRODUCT_LIST_TEXT.rolesLabel}</legend>
          {PRODUCT_ROLE.map((role) => (
            <label key={role} className={styles.checkbox}>
              <input
                type="checkbox"
                checked={roles.includes(role)}
                onChange={() => toggleRole(role)}
              />
              {ROLE_LABEL[role]}
            </label>
          ))}
          {/*
            any/all is EXPLICIT, because the two readings return different products
            the moment a second role is ticked — and because the contract defines
            both, the screen is not inventing the choice.
          */}
          <div className={styles.field}>
            <label htmlFor="product-role-match">{PRODUCT_LIST_TEXT.roleMatchLabel}</label>
            <select
              id="product-role-match"
              value={roleMatch}
              aria-describedby="product-role-hint"
              onChange={(event) => setRoleMatch(event.target.value as RoleMatch)}
            >
              <option value="any">{PRODUCT_LIST_TEXT.roleAny}</option>
              <option value="all">{PRODUCT_LIST_TEXT.roleAll}</option>
            </select>
          </div>
          <p id="product-role-hint" className={styles.hint}>
            {PRODUCT_LIST_TEXT.roleHint}
          </p>
        </fieldset>

        <div className={styles.field}>
          <label htmlFor="product-completeness">{PRODUCT_LIST_TEXT.completenessLabel}</label>
          <select
            id="product-completeness"
            value={completeness}
            onChange={(event) => setCompleteness(event.target.value as CompletenessFilter)}
          >
            {COMPLETENESS_FILTER_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {COMPLETENESS_FILTER_LABEL[option]}
              </option>
            ))}
          </select>
        </div>

        <div className={styles.field}>
          <label htmlFor="product-sort">{PRODUCT_LIST_TEXT.sortLabel}</label>
          <select
            id="product-sort"
            value={sort}
            onChange={(event) => setSort(event.target.value as ProductSort)}
          >
            {(Object.keys(SORT_LABEL) as ProductSort[]).map((option) => (
              <option key={option} value={option}>
                {SORT_LABEL[option]}
              </option>
            ))}
          </select>
        </div>

        <div className={styles.field}>
          <label htmlFor="product-direction">{PRODUCT_LIST_TEXT.directionLabel}</label>
          <select
            id="product-direction"
            value={direction}
            onChange={(event) => setDirection(event.target.value as SortDirection)}
          >
            <option value="asc">{PRODUCT_LIST_TEXT.directionAsc}</option>
            <option value="desc">{PRODUCT_LIST_TEXT.directionDesc}</option>
          </select>
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

      {rows.length === 0 ? (
        <StateBlock state={state} subject="ürün" onRetry={() => void query.refetch()} />
      ) : (
        <>
          <table className={styles.table}>
            {/* No total: `ProductPage` advertises none, so the caption says what is shown. */}
            <caption className={styles.caption}>{PRODUCT_LIST_TEXT.shown(rows.length)}</caption>
            <thead>
              <tr>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.code}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.name}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.classification}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.roles}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.stockUnit}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.lifecycle}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.completeness}</th>
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
                  {/*
                    Server-resolved labels. A null label on a product that HAS the id
                    means the catalogue could not be read — a different fact from
                    "nothing assigned", and said differently.
                  */}
                  <td data-testid={`product-class-${item.id}`}>
                    {labelOrUnavailable(item.labels.class, item.classId)}
                    {item.familyId !== null || item.labels.family !== null
                      ? ` / ${labelOrUnavailable(item.labels.family, item.familyId)}`
                      : ''}
                  </td>
                  <td>{rolesText(item.roles)}</td>
                  <td>{item.labels.primaryStockUnit ?? UNASSIGNED}</td>
                  <td>
                    <span
                      className={styles.badge}
                      data-lifecycle={item.lifecycle.known ? item.lifecycle.value : 'unknown'}
                    >
                      {lifecycleLabel(item.lifecycle)}
                    </span>
                  </td>
                  <td data-testid={`product-completeness-${item.id}`}>
                    {item.completeness.evaluation === null
                      ? `${COMPLETENESS_UNKNOWN_LABEL}: ${item.completeness.rawEvaluation}`
                      : COMPLETENESS_LABEL[item.completeness.evaluation]}
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

          {/* `nextCursor !== null` is the only continuation signal the page has. */}
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
