/**
 * U-01 Ürünler — list.
 *
 * Scope (PRODUCT_U01_U02_IDENTITY_LIFECYCLE_JIT_SPEC_v1 §8): the LIST only.
 * Deliberately absent, and forbidden by §3 and §8.2: stock quantity, price,
 * package instances, lot/label values, supplier/customer part codes, recipes and
 * AI suggestions. §9 also forbids inventing a `0` for a related module that is not
 * mounted — so nothing here summarises a module this app cannot read.
 *
 * Every column maps to a field ./contract.ts declares, and ./contract.ts declares
 * only what the business spec names. No column is computed from a guess.
 */

import { useEffect, useMemo, useRef, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import {
  fetchProducts,
  type CompletenessFilter,
  type LifecycleFilter,
  type ProductSortField,
  type RoleMatch,
  type SortDirection,
} from './api'
import {
  PRODUCT_COUNT_SCOPE,
  PRODUCT_LIFECYCLE,
  PRODUCT_PROBLEM_CODE,
  PRODUCT_ROLE,
  productProblemCodeOf,
  type ProductListItem,
  type ProductPage,
  type ProductRoleName,
} from './contract'
import {
  COMPLETENESS_AREA_LABEL,
  LIFECYCLE_LABEL,
  MATCH_REASON_LABEL,
  lifecycleLabel,
  PRODUCT_LIST_TEXT,
  ROLE_LABEL,
  UNASSIGNED,
  incompleteAreas,
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

/** What the count in the caption covers. The token itself tells an operator nothing. */
const COUNT_SCOPE_LABEL: Record<string, string> = {
  [PRODUCT_COUNT_SCOPE]: PRODUCT_LIST_TEXT.countScopeLabel,
}

/**
 * The filters the user has applied, in their own words.
 *
 * A filter the user cannot see is a filter they cannot undo — and on this screen
 * the most likely cause of an unexpectedly empty list is a lifecycle or
 * completeness filter set two screens ago. Naming them makes the cause readable
 * without inspecting every control.
 */
function activeFilterLabels(view: ProductListView): string[] {
  const labels: string[] = []
  if (view.q.trim().length > 0) labels.push(`Arama: “${view.q.trim()}”`)
  if (view.lifecycle !== 'all') labels.push(`Durum: ${LIFECYCLE_FILTER_LABEL[view.lifecycle]}`)
  if (view.family.trim().length > 0) labels.push(`Aile: ${view.family.trim()}`)
  if (view.roles.length > 0) {
    const names = view.roles.map((role) => ROLE_LABEL[role]).join(', ')
    const match =
      view.roleMatch === 'all' ? PRODUCT_LIST_TEXT.roleMatchAll : PRODUCT_LIST_TEXT.roleMatchAny
    labels.push(`Rol (${match.toLocaleLowerCase('tr')}): ${names}`)
  }
  if (view.completeness === 'incomplete') {
    labels.push(PRODUCT_LIST_TEXT.completenessIncomplete)
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
  // Remounted per context, so every "once per mount" below is "once per context"
  // and a view saved under another actor is unreachable rather than merely unused.
  return <ProductList key={namespace ?? 'no-context'} namespace={namespace} />
}

function ProductList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const [restored] = useState<ProductListView>(() => readProductListView(namespace))
  const [q, setQ] = useState(restored.q)
  const [lifecycle, setLifecycle] = useState<LifecycleFilter>(restored.lifecycle)
  const [family, setFamily] = useState(restored.family)
  const [roles, setRoles] = useState<readonly ProductRoleName[]>(restored.roles)
  const [roleMatch, setRoleMatch] = useState<RoleMatch>(restored.roleMatch)
  const [completeness, setCompleteness] = useState<CompletenessFilter>(restored.completeness)
  const [sort, setSort] = useState<ProductSortField>(restored.sort)
  const [direction, setDirection] = useState<SortDirection>(restored.direction)
  const [pageSize, setPageSize] = useState<number>(restored.pageSize)
  const [cursor, setCursor] = useState<string | undefined>(undefined)
  const [loaded, setLoaded] = useState<readonly ProductListItem[]>([])
  const searchRef = useRef<HTMLInputElement>(null)
  const [returnToId] = useState<string | null>(() => takeOpenedRecord(namespace))
  const returnHandled = useRef(false)

  const boundary = useAuthBoundary()
  /**
   * Opening a 360 is a SEPARATE grant. Holding the list must not imply it — the
   * Cari slice shipped the opposite once and a list-only snapshot could open the
   * detail and call its API (#172). When the grant is absent the code renders as
   * plain text rather than a link: a disabled-looking link that does nothing is a
   * worse answer than a row that simply is not clickable.
   */
  const canOpenDetail = hasCapability(shell, PRODUCT_CAPABILITY.productsDetail)
  const canCreate = hasCapability(shell, PRODUCT_CAPABILITY.productsCreate)
  const entryGeneration = shell.routeEntryGeneration

  const view: ProductListView = useMemo(
    () => ({ q, lifecycle, family, roles, roleMatch, completeness, sort, direction, pageSize }),
    [q, lifecycle, family, roles, roleMatch, completeness, sort, direction, pageSize],
  )
  const filterActive = isFiltered(view)
  const filterLabels = useMemo(() => activeFilterLabels(view), [view])

  useEffect(() => {
    writeProductListView(namespace, view)
  }, [namespace, view])

  /**
   * §8.1: "Filter değişince cursor sıfırlanır." A cursor is bound to the query that
   * produced it, so carrying one across a filter change asks the server to continue
   * a page chain that no longer describes what is being asked for. The accumulated
   * rows go with it — they were read under the previous query.
   */
  const queryShape = JSON.stringify([q, lifecycle, family, roles, roleMatch, completeness, sort, direction, pageSize])
  /**
   * Held in STATE, not a ref. A ref read during render breaks render purity —
   * oxlint flags it, and the Cari list hit exactly this and made three consecutive
   * full runs non-deterministic. Adjusting state during render is the supported
   * React pattern for "derive from a changed prop/value": the re-render happens
   * before anything is committed, so no extra paint shows the stale cursor.
   */
  const [previousShape, setPreviousShape] = useState(queryShape)
  if (previousShape !== queryShape) {
    setPreviousShape(queryShape)
    if (cursor !== undefined) setCursor(undefined)
    if (loaded.length > 0) setLoaded([])
  }

  const query = useQuery({
    queryKey: [
      namespace ?? 'no-context',
      // ENTRY-SCOPED: a revisit cannot be served from the previous entry's rows
      // while its authorization is being revalidated (#146/#148).
      entryGeneration,
      'products',
      { q, lifecycle, family, roles, roleMatch, completeness, sort, direction, pageSize, cursor },
    ],
    /**
     * Bound to a resolved shell context, NOT to a single company.
     *
     * `PRD-SCOPE-001=A` makes the Product master global: one card for both Göymen
     * companies. The C-01 list requires single-company mode because a Cari account
     * is company-scoped and group mode has no accepted per-company representation.
     * Neither reason applies here, and requiring single mode would hide a global
     * master behind a company selector it does not depend on. The context is still
     * required — capabilities are resolved per context, and an unbound context has
     * no grant to read.
     */
    enabled: namespace !== null,
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => {
      // SAB-001: security bindings captured BEFORE the request starts, so a slow
      // 401 from a departed actor cannot reset the session of whoever is here now.
      const snapshot = boundary.capture()
      const outcome = await fetchProducts(
        {
          q,
          lifecycle,
          family,
          role: roles,
          roleMatch,
          completeness,
          sort,
          direction,
          pageSize,
          cursor,
        },
        signal,
      )
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const outcome = query.data
  /**
   * `keepPreviousData` hands back the PREVIOUS page while the new query is in
   * flight. That page answers the previous question, so it is not evidence about
   * what is being asked for now — it must not be appended, and it must not be
   * labelled as an empty result.
   */
  const showingPrevious = query.isPlaceholderData
  const page: ProductPage | null =
    !showingPrevious && outcome !== undefined && outcome.type === 'ok-single' ? outcome.data : null

  /**
   * Append a freshly-read page to what is already on screen.
   *
   * Keyed on the page's own identity rather than on a render count: React may
   * re-run this effect for reasons unrelated to a new page, and appending twice
   * would duplicate rows the server sent once. `page` is already null while the
   * previous page is being shown, which is the other half of the same rule — an
   * earlier version appended the placeholder under the NEW cursor's marker, so
   * page one was rendered twice and page two was then skipped as "already
   * appended".
   */
  const appendedFor = useRef<string | null>(null)
  useEffect(() => {
    if (page === null) return
    const marker = `${entryGeneration}:${queryShape}:${cursor ?? 'first'}`
    if (appendedFor.current === marker) return
    appendedFor.current = marker
    setLoaded((current) => (cursor === undefined ? page.items : [...current, ...page.items]))
  }, [page, cursor, entryGeneration, queryShape])

  // The first page paints from the response directly, before the effect commits,
  // so there is no frame where a loaded list renders as empty.
  const rows =
    loaded.length === 0 && cursor === undefined && page !== null ? page.items : loaded

  /**
   * Move focus to the row the user came back from.
   *
   * Restoring the query without restoring the position leaves a keyboard user at
   * the top of the document after every record. One attempt, on the first page
   * only: walking forward to find a record that may have left the result set would
   * page through the whole catalogue to prove a negative, on a screen the user is
   * waiting to look at.
   */
  useEffect(() => {
    if (returnToId === null || returnHandled.current || rows.length === 0) return
    returnHandled.current = true
    const target = document.getElementById(`product-row-${returnToId}`)
    target?.focus()
  }, [returnToId, rows.length])

  const problemCode =
    outcome !== undefined && outcome.type === 'http' ? productProblemCodeOf(outcome.body) : null

  /**
   * §8.3: a FIRST-page failure is a full error state; a NEXT-page failure keeps the
   * rows already on screen. Discarding what the user has already scrolled through
   * would punish them for the server's failure.
   */
  const isNextPageAttempt = cursor !== undefined
  const nextPageFailed =
    isNextPageAttempt && outcome !== undefined && outcome.type !== 'ok-single'

  /**
   * The §13 state for a list with nothing to show.
   *
   * `classifyOutcome` cannot separate the two empty cases here: the read returns
   * ONE page object, so its `ok-single` branch always classifies as `ready` and
   * "no products exist" would be indistinguishable from "the filter excluded
   * everything". Those are different facts with different things for the user to
   * do, so the distinction is drawn from the view the user actually set.
   */
  const state: RemoteViewState<ProductPage | readonly ProductPage[]> =
    query.isPending || showingPrevious
      ? { kind: 'initial-loading' }
      : outcome === undefined
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
      current.includes(role) ? current.filter((r) => r !== role) : [...current, role],
    )
  }

  function clearAll() {
    const next = clearedFilters(view)
    setQ(next.q)
    setLifecycle(next.lifecycle)
    setFamily(next.family)
    setRoles(next.roles)
    setRoleMatch(next.roleMatch)
    setCompleteness(next.completeness)
    searchRef.current?.focus()
  }

  /**
   * An invalid cursor is recoverable and must say so (§13: "invalid cursor
   * recovery"). The generic 409 text — "the record changed, review the difference"
   * — is false here: nothing changed under the user and there is no difference to
   * review. Branch on the stable CODE, never on the localized text.
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
  const showFullState = !showRows

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
            aria-describedby="product-q-hint"
            onChange={(event) => setQ(event.target.value)}
          />
          <p id="product-q-hint" className={styles.hint}>
            {PRODUCT_LIST_TEXT.searchHint}
          </p>
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

        <div className={styles.field}>
          <label htmlFor="product-family">{PRODUCT_LIST_TEXT.familyLabel}</label>
          <input
            id="product-family"
            type="text"
            value={family}
            onChange={(event) => setFamily(event.target.value)}
          />
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
            §8.1 requires any/all to be EXPLICIT in the UI. "Matches a role filter"
            is ambiguous the moment two roles are ticked, and the two readings
            return different products.
          */}
          <div className={styles.field}>
            <label htmlFor="product-role-match">{PRODUCT_LIST_TEXT.roleMatchLabel}</label>
            <select
              id="product-role-match"
              value={roleMatch}
              onChange={(event) => setRoleMatch(event.target.value as RoleMatch)}
            >
              <option value="any">{PRODUCT_LIST_TEXT.roleMatchAny}</option>
              <option value="all">{PRODUCT_LIST_TEXT.roleMatchAll}</option>
            </select>
          </div>
        </fieldset>

        <div className={styles.field}>
          <label htmlFor="product-completeness">{PRODUCT_LIST_TEXT.completenessLabel}</label>
          <select
            id="product-completeness"
            value={completeness}
            onChange={(event) => setCompleteness(event.target.value as CompletenessFilter)}
          >
            <option value="all">{PRODUCT_LIST_TEXT.completenessAll}</option>
            <option value="incomplete">{PRODUCT_LIST_TEXT.completenessIncomplete}</option>
          </select>
        </div>

        <div className={styles.field}>
          <label htmlFor="product-sort">Sıralama</label>
          <select
            id="product-sort"
            value={sort}
            onChange={(event) => setSort(event.target.value as ProductSortField)}
          >
            <option value="code">Kod</option>
            <option value="name">Ad</option>
            <option value="lastChangedAt">Son değişiklik</option>
          </select>
        </div>

        <div className={styles.field}>
          <label htmlFor="product-direction">Yön</label>
          <select
            id="product-direction"
            value={direction}
            onChange={(event) => setDirection(event.target.value as SortDirection)}
          >
            <option value="asc">Artan</option>
            <option value="desc">Azalan</option>
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

      {showFullState ? (
        <StateBlock state={state} subject="ürün" onRetry={() => void query.refetch()} />
      ) : (
        <>
          <table className={styles.table}>
            <caption className={styles.caption}>
              {page?.totalCount !== null && page?.totalCount !== undefined
                ? `${page.totalCount} kayıt — ${COUNT_SCOPE_LABEL[PRODUCT_COUNT_SCOPE]}`
                : `${rows.length} kayıt görüntüleniyor`}
            </caption>
            <thead>
              <tr>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.code}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.name}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.classification}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.roles}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.stockUnit}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.lifecycle}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.completeness}</th>
                <th scope="col">{PRODUCT_LIST_TEXT.columns.lastChanged}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((item) => {
                const missing = incompleteAreas(item.completeness)
                return (
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
                      {/*
                        §8.1: an alias match resolves to the canonical product as ONE
                        row and says why. Without naming the retired code, "eski kod"
                        is how an operator concludes they opened the wrong record.
                      */}
                      {item.match.reason === 'alias' && item.match.matchedAlias !== null ? (
                        <span className={styles.aliasBadge}>
                          {MATCH_REASON_LABEL.alias}: {item.match.matchedAlias}
                        </span>
                      ) : null}
                    </td>
                    <td>{item.name}</td>
                    <td>
                      {item.classification.class}
                      {item.classification.family !== null
                        ? ` / ${item.classification.family}`
                        : ''}
                    </td>
                    <td>{rolesText(item.roles)}</td>
                    <td>{item.primaryStockUnit ?? UNASSIGNED}</td>
                    <td>
                      <span
                        className={styles.badge}
                        data-lifecycle={item.lifecycle.known ? item.lifecycle.value : 'unknown'}
                      >
                        {lifecycleLabel(item.lifecycle)}
                      </span>
                    </td>
                    <td>
                      {missing.length === 0
                        ? 'Tam'
                        : `Eksik: ${missing
                            .map((area) => COMPLETENESS_AREA_LABEL[area])
                            .join(', ')}`}
                    </td>
                    <td>{formatInstant(item.lastChangedAt)}</td>
                  </tr>
                )
              })}
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

          {page?.hasMore === true && page.nextCursor !== null && !nextPageFailed ? (
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
