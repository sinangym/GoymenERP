/**
 * C-01 Cari Kartlar — list.
 *
 * Scope (CARI_UI_VERTICAL_SLICE_v1 screenScope + DEC-CAR-001..003): the LIST only.
 * Deliberately absent, and forbidden by the accepted contract's `mockTruthForbidden`:
 * balance, open items, risk, credit limit, bank/tax/registry, documents, orders, stock,
 * allocations, CRM, AI. No create (C-02), no 360 (C-03).
 *
 * Every column below maps to a field the merged API actually returns.
 */

import { useEffect, useMemo, useRef, useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import {
  fetchCounterparties,
  type AccountStateFilter,
  type CounterpartyScope,
  type KindFilter,
  type PartyStateFilter,
  type RoleFilter,
  type RoleMatch,
  type SortDirection,
  type SortField,
} from './api'
import {
  COUNT_SCOPE_FILTERED_AUTHORIZED,
  PARTY_KIND,
  PROBLEM_CODE,
  problemCodeOf,
  type CounterpartyListItem,
  type CounterpartyPage,
} from './contract'
import {
  LEGACY_ROLE_FILTER_HEADING,
  LEGACY_ROLE_HEADING,
  NO_COMMERCIAL_ROLE_LIST,
  ROLE_LABEL,
  rolesText,
} from './roleText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKeyString, contextKey, hasCapability } from '../shell/shellState'
import { CAPABILITY } from '../shell/capabilities'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { allowed, isRendered, notEntitled, type ActionGate } from './actionGating'
import {
  clearedFilters,
  DEFAULT_CARI_LIST_VIEW,
  isFiltered,
  PAGE_SIZE_OPTIONS,
  readCariListView,
  writeCariListView,
  type CariListView,
} from './listViewState'
import styles from './CariListPage.module.css'

const KIND_LABEL: Record<number, string> = {
  [PARTY_KIND.LegalEntity]: 'Tüzel kişi',
  [PARTY_KIND.NaturalPerson]: 'Gerçek kişi',
}

const ROLE_OPTIONS: readonly RoleFilter[] = ['Customer', 'Supplier', 'Tolling', 'Service']

const PARTY_STATE_LABEL: Record<PartyStateFilter, string> = {
  active: 'Aktif',
  inactive: 'Pasif',
  all: 'Hepsi',
}
const ACCOUNT_STATE_LABEL: Record<AccountStateFilter, string> = {
  all: 'Hepsi',
  active: 'Aktif',
  inactive: 'Pasif',
}

/**
 * What the number in the caption counts (UX review §8.7/65: "toplam kapsamı").
 *
 * ASSUMPTION-CARI-001. `filtered-authorized-visible-parties` is the ONLY scope the
 * C-01 slice accepts, and it is a machine token — printing it tells an operator
 * nothing, which is why the count was previously not requested at all. This is the
 * Turkish reading of the token's three parts, chosen rather than asked: the count
 * covers the records the current FILTERS match, that this user is AUTHORIZED for,
 * and that are VISIBLE in the active company.
 *
 * It matters because the alternative reading — "the company has 128 counterparties"
 * — is a claim about data the caller may not be entitled to see. Reversible text
 * decision; the token it labels is contract-fixed and is asserted against the
 * decoder's constant, so a new scope cannot silently inherit this sentence.
 */
const COUNT_SCOPE_LABEL: Record<string, string> = {
  [COUNT_SCOPE_FILTERED_AUTHORIZED]: 'Filtrelenmiş, yetkili ve görünür cariler',
}

/**
 * What the user has narrowed the list by, in their own words.
 *
 * A filter the user cannot see is a filter they cannot undo: the UX walkthrough hit
 * an "empty" list whose cause — a party-state left on Pasif two records ago — was
 * three controls away and looked exactly like having no counterparties at all.
 * Naming each active filter makes the cause of an empty result readable without
 * inspecting every control.
 *
 * Defaults are deliberately absent from this list. Announcing "Taraf durumu: Aktif"
 * on a screen nobody has touched would make the normal state look like something the
 * user did.
 */
function activeFilterLabels(view: CariListView): readonly string[] {
  const labels: string[] = []
  if (view.q.trim().length > 0) labels.push(`Arama: “${view.q.trim()}”`)
  if (view.kind !== '') labels.push(`Tür: ${KIND_LABEL[PARTY_KIND[view.kind]]}`)
  if (view.roles.length > 0) {
    const names = view.roles.map((role) => ROLE_LABEL[role]).join(', ')
    /**
     * "Geçmiş rol", not "Rol". The chip is often the ONLY place an applied role
     * filter is visible — the control itself lives in a block that can be folded
     * away — so it is the place that has to say what the filter actually selects:
     * a classification recorded earlier, not today's commercial direction
     * (ILK_DILIM §4.1).
     *
     * The match mode only changes the query with 2+ roles, so it is only named there.
     */
    labels.push(
      view.roles.length > 1 && view.roleMatch === 'all'
        ? `Geçmiş rol (hepsi birden): ${names}`
        : `Geçmiş rol: ${names}`,
    )
  }
  if (view.partyState !== DEFAULT_CARI_LIST_VIEW.partyState) {
    labels.push(`Taraf durumu: ${PARTY_STATE_LABEL[view.partyState]}`)
  }
  if (view.accountState !== DEFAULT_CARI_LIST_VIEW.accountState) {
    labels.push(`Şirket hesabı durumu: ${ACCOUNT_STATE_LABEL[view.accountState]}`)
  }
  return labels
}

/**
 * The ONLY failures a continuation may survive: the request did not get through, or
 * the service is temporarily down. Everything else must fail closed.
 *
 * This is an allowlist on purpose (DECISION_PACK Q8=A, "kapsam dışı/korunan güvenlik").
 * `state.kind !== 'ready'` would have kept the rows on screen for a 401 — showing
 * data from a session the server has already rejected — for a 403 after
 * authorization was revoked mid-session, and for a malformed page, burying a
 * broken server behind a reassuring "mevcut kayıtlar korunuyor". Keeping what the
 * user already has is a courtesy for transport failures, not a place to hide any
 * of those.
 */
const CONTINUATION_SURVIVABLE = new Set(['offline', 'network-error', 'service-unavailable'])

/**
 * The context fence for this screen (Codex/ChatGPT #147 P1-2).
 *
 * The list is REMOUNTED whenever the context namespace changes — a different actor,
 * company, capability generation or committed revision. That destroys both the
 * component's accumulated rows/cursor and the query observer, so nothing from company
 * A can survive into company B. Doing this with a `key` rather than a reset effect
 * makes the bridge structurally unrepresentable instead of merely repaired: there is
 * no render in which the old rows and the new context coexist.
 */
export function CariListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <CariList key={namespace ?? 'no-context'} namespace={namespace} />
}

function CariList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  /**
   * Read ONCE, at mount. This component is remounted whenever the namespace changes
   * (see the `key` above), so "once per mount" is exactly "once per context" — and a
   * view saved under another actor or company is unreachable rather than merely
   * unused.
   */
  const [restored] = useState<CariListView>(() => readCariListView(namespace))
  const [q, setQ] = useState(restored.q)
  const [kind, setKind] = useState<KindFilter | ''>(restored.kind)
  const [roles, setRoles] = useState<readonly RoleFilter[]>(restored.roles)
  const [roleMatch, setRoleMatch] = useState<RoleMatch>(restored.roleMatch)
  const [partyState, setPartyState] = useState<PartyStateFilter>(restored.partyState)
  const [accountState, setAccountState] = useState<AccountStateFilter>(restored.accountState)
  const [sort, setSort] = useState<SortField>(restored.sort)
  const [direction, setDirection] = useState<SortDirection>(restored.direction)
  const [pageSize, setPageSize] = useState<number>(restored.pageSize)
  /**
   * Whether the legacy-classification block is open.
   *
   * Seeded from the RESTORED view, not from a constant: coming back to a list that
   * still carries a role filter must not present a folded block over a silently
   * narrowed list. A filter the user cannot see is the §4.1 failure whether it was
   * just applied or restored from two screens ago.
   */
  const [legacyOpen, setLegacyOpen] = useState(restored.roles.length > 0)
  const [cursor, setCursor] = useState<string | undefined>(undefined)
  const [loaded, setLoaded] = useState<readonly CounterpartyListItem[]>([])
  const searchRef = useRef<HTMLInputElement>(null)

  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scopeMode = shell.activeContext?.scopeMode ?? 'single'
  const scope: CounterpartyScope = useMemo(
    () => ({ mode: scopeMode, companyId: activeCompanyId }),
    [scopeMode, activeCompanyId],
  )

  /**
   * C-01A is the accepted deliverable: ONE selected company. Group mode has no
   * accepted per-company representation yet (Codex/ChatGPT #147 P1-3), and rendering
   * it through the single-company columns would print `—` for roles and account state
   * — silently omitting the facts that differ per company. Fail closed instead of
   * showing an incomplete truth.
   */
  // The central security boundary for every authenticated read on this screen.
  const boundary = useAuthBoundary()
  /**
   * Opening a record is a SEPARATE grant (#182); holding the list does not imply it.
   *
   * Stated as a gate rather than a boolean so the reason it is hidden — rather than
   * disabled with an explanation — is carried by the value itself. See
   * actionGating.ts: this is the `not-entitled` half of the rule, and the type has
   * nowhere to put a message precisely so that no one can add one here later.
   */
  const detailGate: ActionGate = hasCapability(shell, CAPABILITY.cariPartiesDetail)
    ? allowed
    : notEntitled
  // See CapabilityRoute: the entry generation makes a revisit addressable, so it
  // cannot be served from the previous entry's cache (#146).
  const entryGeneration = shell.routeEntryGeneration

  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null

  const view: CariListView = useMemo(
    () => ({ q, kind, roles, roleMatch, partyState, accountState, sort, direction, pageSize }),
    [q, kind, roles, roleMatch, partyState, accountState, sort, direction, pageSize],
  )
  const filterActive = isFiltered(view)
  const filterLabels = useMemo(() => activeFilterLabels(view), [view])

  /**
   * Remember the query the user chose, so leaving for a record and coming back does
   * not silently drop their search. The DATA is not remembered — see listViewState.ts.
   */
  useEffect(() => {
    writeCariListView(namespace, view)
  }, [namespace, view])

  const query = useQuery({
    // INV-NS: the company context owns the cache namespace, so a company switch can
    // never surface another company's page.
    queryKey: [
      namespace ?? 'no-context',
      // ENTRY-SCOPED. A route entry gets its own cache namespace, so a revisit
      // cannot be served from the previous entry's rows while its authorization
      // is being revalidated. Paging and sorting inside one entry still cache
      // normally, because the generation only moves when the route is entered.
      entryGeneration,
      'counterparties',
      { q, kind, roles, roleMatch, partyState, accountState, sort, direction, pageSize, cursor },
    ],
    enabled: namespace !== null && singleCompanyContext,
    /**
     * Safe ONLY because <CariListPage> remounts this component on a namespace change:
     * the observer that holds the previous data is destroyed with it, so "previous"
     * can only ever mean a different filter or sort inside the SAME company.
     */
    placeholderData: keepPreviousData,
    queryFn: async ({ signal }) => {
      // SAB-001: the security bindings are captured BEFORE the request starts.
      // Deriving them at response time would let a slow 401 from a departed
      // actor reset the session of whoever is here now.
      const snapshot = boundary.capture()
      const outcome = await fetchCounterparties(
        scope,
        {
          q,
          kind: kind === '' ? undefined : kind,
          role: roles.length > 0 ? roles : undefined,
          roleMatch: roles.length > 1 ? roleMatch : undefined,
          partyState,
          accountState,
          sort,
          direction,
          pageSize,
          /**
           * ASK for the count (§8.7/65). The decoder binds the response to this
           * flag in both directions — a count that arrives unrequested is still
           * malformed — so the number on screen is always one we asked for and
           * can label.
           */
          includeCount: true,
          cursor,
        },
        signal,
      )
      // SAB-006: first page, continuation and refetch all report here. There is
      // no weaker per-path variant — this IS the path.
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        // The reducer decides whether this is current and what it means; a stale
        // result is discarded there and produces no effect (SAB-008/009).
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const outcome = query.data
  const state = (
    query.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(outcome ?? { type: 'malformed' })
  ) as RemoteViewState<CounterpartyPage>

  const page = state.kind === 'ready' ? state.data : null

  /**
   * While `keepPreviousData` is showing, `page` is the page we ALREADY have, not the
   * one `cursor` asked for (ChatGPT #147 P1). `loaded` holds that same validated page,
   * so appending it would produce every row twice — and the duplicate guard below
   * would then report the server as malformed for a fault entirely of our own making.
   *
   * A stale continuation therefore contributes nothing: we keep exactly the rows we
   * validated and let the button show that more is on the way.
   */
  const awaitingContinuation = cursor !== undefined && query.isPlaceholderData

  // Accumulate across cursor pages; a filter/sort change resets via resetPaging().
  const accumulated =
    page === null || awaitingContinuation
      ? loaded
      : cursor === undefined
        ? page.items
        : [...loaded, ...page.items]

  /**
   * A PartyId that repeats across cursor pages means the continuation is broken —
   * client-side de-duplication would paper over a server contract violation and show a
   * page that is quietly missing records. Reject instead (Codex/ChatGPT #147 P2).
   */
  const crossPageDuplicate = new Set(accumulated.map((item) => item.id)).size !== accumulated.length
  const items = crossPageDuplicate ? [] : accumulated

  /**
   * A CONTINUATION failed. The user asked for more; more did not arrive. What they
   * already had is still valid and server-verified — replacing the whole screen
   * throws away good data as a punishment for asking, and loses their place in the
   * list. The FIRST page is different: there is nothing to keep.
   */
  const continuationFailed =
    cursor !== undefined &&
    loaded.length > 0 &&
    !crossPageDuplicate &&
    CONTINUATION_SURVIVABLE.has(state.kind)

  function resetPaging() {
    setCursor(undefined)
    setLoaded([])
  }

  function toggleRole(role: RoleFilter) {
    setRoles((current) =>
      current.includes(role) ? current.filter((r) => r !== role) : [...current, role],
    )
    resetPaging()
  }

  function clearFilters() {
    const cleared = clearedFilters(view)
    setQ(cleared.q)
    setKind(cleared.kind)
    setRoles(cleared.roles)
    setRoleMatch(cleared.roleMatch)
    setPartyState(cleared.partyState)
    setAccountState(cleared.accountState)
    // Page size is not a filter and survives, like the sort.
    // Nothing legacy is applied any more, so the block goes back to being a place
    // you open on purpose rather than a control standing open over an empty state.
    setLegacyOpen(false)
    // Sort is not a filter and is deliberately left alone (see listViewState.ts).
    resetPaging()
    /**
     * The button that was just activated is the LAST filter, so it unmounts with the
     * summary and focus would fall to <body> — a keyboard user would be returned to
     * the top of the document with no idea where they are. Hand focus to the search
     * box: it is the control this row is about, and it is where a re-narrowed search
     * starts.
     */
    searchRef.current?.focus()
  }

  function toggleSort(field: SortField) {
    if (sort === field) setDirection((d) => (d === 'asc' ? 'desc' : 'asc'))
    else {
      setSort(field)
      setDirection('asc')
    }
    resetPaging()
  }

  function loadMore() {
    if (page === null || !page.hasMore || page.nextCursor === null) return
    setLoaded(items)
    setCursor(page.nextCursor)
  }

  // An expired or otherwise invalid cursor must not strand the user on a broken page:
  // fall back to the first page. Branches on the STABLE code, never the localized text.
  const isInvalidCursor =
    outcome?.type === 'http' &&
    outcome.status === 400 &&
    problemCodeOf(outcome.body) === PROBLEM_CODE.invalidCursor

  // An effect is the right tool here: the trigger is the SERVER rejecting our cursor,
  // not a user action, and it cannot be derived during render — deriving it would flip
  // the query key back and forth forever (bad cursor -> page 1 -> valid -> bad cursor).
  // The ref makes recovery happen at most once per rejected cursor.
  const recoveredCursor = useRef<string | null>(null)
  const [cursorRecovered, setCursorRecovered] = useState(false)
  // oxlint-disable-next-line react/set-state-in-effect -- see the note above
  useEffect(() => {
    if (isInvalidCursor && cursor !== undefined && recoveredCursor.current !== cursor) {
      recoveredCursor.current = cursor
      setCursor(undefined)
      setLoaded([])
      // The list silently jumping back to page 1 is a change the user must be told
      // about, not a repair to hide (Codex/ChatGPT #147 P2).
      setCursorRecovered(true)
    }
  }, [isInvalidCursor, cursor])

  const ariaSort = (field: SortField): 'ascending' | 'descending' | 'none' =>
    sort !== field ? 'none' : direction === 'asc' ? 'ascending' : 'descending'

  // C-01A only. Deliberately before any data render: no request is issued and no row
  // is drawn in a context this screen cannot represent honestly.
  if (!singleCompanyContext) {
    return (
      <section aria-labelledby="cari-list-heading" className={styles.page}>
        <h1 id="cari-list-heading">Cari Kartlar</h1>
        <StateBlock
          state={{ kind: 'no-authorized-company' }}
          subject="cari listesi için tek şirket bağlamı"
        />
      </section>
    )
  }

  return (
    <section aria-labelledby="cari-list-heading" className={styles.page}>
      <h1 id="cari-list-heading">Cari Kartlar</h1>

      <div role="status" aria-live="polite" className={styles.announce}>
        {cursorRecovered ? 'Liste güncellendi; ilk sayfaya dönüldü.' : ''}
      </div>

      <div className={styles.filters} role="search">
        <label className={styles.field}>
          <span>Ara</span>
          <input
            ref={searchRef}
            type="search"
            value={q}
            placeholder="Kod, unvan veya ticari unvan"
            onChange={(event) => {
              setQ(event.target.value)
              resetPaging()
            }}
          />
        </label>

        <label className={styles.field}>
          <span>Tür</span>
          <select
            value={kind}
            onChange={(event) => {
              setKind(event.target.value as KindFilter | '')
              resetPaging()
            }}
          >
            <option value="">Hepsi</option>
            <option value="LegalEntity">Tüzel kişi</option>
            <option value="NaturalPerson">Gerçek kişi</option>
          </select>
        </label>

        {/* Two INDEPENDENT facts, two filters. A Party can be globally active while its
            account in the selected company is passive, and the reverse never happens —
            collapsing them into one control would hide that (DEC-CAR-002). */}
        <label className={styles.field}>
          <span>Taraf durumu</span>
          <select
            value={partyState}
            onChange={(event) => {
              setPartyState(event.target.value as PartyStateFilter)
              resetPaging()
            }}
          >
            <option value="active">Aktif</option>
            <option value="inactive">Pasif</option>
            <option value="all">Hepsi</option>
          </select>
        </label>

        <label className={styles.field}>
          <span>Şirket hesabı durumu</span>
          <select
            value={accountState}
            onChange={(event) => {
              setAccountState(event.target.value as AccountStateFilter)
              resetPaging()
            }}
          >
            <option value="all">Hepsi</option>
            <option value="active">Aktif</option>
            <option value="inactive">Pasif</option>
          </select>
        </label>

        {/* Changing the size restarts paging: the cursor already handed out was
            issued for the OLD size, and continuing it would silently mix two page
            lengths into one accumulated list. */}
        <label className={styles.field}>
          <span>Sayfa boyutu</span>
          <select
            value={String(pageSize)}
            onChange={(event) => {
              setPageSize(Number(event.target.value))
              resetPaging()
            }}
          >
            {PAGE_SIZE_OPTIONS.map((size) => (
              <option key={size} value={String(size)}>
                {size}
              </option>
            ))}
          </select>
        </label>
      </div>

      {/*
        The legacy commercial classification (ILK_DILIM §4.1, §7).

        DEMOTED, not removed. The 10 September decision removes the mandatory
        commercial role, so a role filter presented as a peer of the search box
        would keep teaching the operator that classification is how you find a
        counterparty. The recorded 1..15 markers still exist and still deserve a
        way to be queried, so the surface stays — one fold away, and labelled as
        history.

        A native <details> rather than a bespoke toggle: the open/closed state, the
        keyboard behaviour and the screen-reader announcement all come from the
        element, and there is no way for our own aria-expanded to disagree with what
        is actually rendered.
      */}
      <details
        className={styles.legacy}
        open={legacyOpen}
        onToggle={(event) => setLegacyOpen(event.currentTarget.open)}
      >
        <summary className={styles.legacySummary}>{LEGACY_ROLE_FILTER_HEADING}</summary>
        <p className={styles.legacyNote}>
          Bu işaretler eski kayıtlardan gelir; ticari rol seçmek zorunlu değildir. Bir
          işaret seçtiğinizde, ticari rolü olmayan cariler listeden düşer.
        </p>
        <div className={styles.legacyControls}>
          <fieldset className={styles.roles}>
            <legend>Kayıtlı işaret</legend>
            {ROLE_OPTIONS.map((role) => (
              <label key={role}>
                <input
                  type="checkbox"
                  checked={roles.includes(role)}
                  onChange={() => toggleRole(role)}
                />
                {ROLE_LABEL[role]}
              </label>
            ))}
          </fieldset>

          {/* Only meaningful with 2+ roles — with one role `any` and `all` are the same
              query, so offering the choice would imply a distinction that does not exist. */}
          {roles.length > 1 ? (
            <label className={styles.field}>
              <span>İşaret eşleşmesi</span>
              <select
                value={roleMatch}
                onChange={(event) => {
                  setRoleMatch(event.target.value as RoleMatch)
                  resetPaging()
                }}
              >
                <option value="any">Herhangi biri</option>
                <option value="all">Hepsi birden</option>
              </select>
            </label>
          ) : null}
        </div>
      </details>

      {/* The way OUT of a narrowed list, and the way to see what narrowed it. Rendered
          only when something is actually applied, so an untouched screen carries no
          control that would do nothing. */}
      {filterActive ? (
        <div className={styles.activeFilters}>
          <span className={styles.activeFiltersLabel}>Etkin filtreler:</span>
          <ul className={styles.chips}>
            {filterLabels.map((label) => (
              <li key={label} className={styles.chip}>
                {label}
              </li>
            ))}
          </ul>
          <button type="button" className={styles.clear} onClick={clearFilters}>
            Filtreleri temizle
          </button>
        </div>
      ) : null}

      {(state.kind !== 'ready' && !continuationFailed) || crossPageDuplicate ? (
        <StateBlock
          state={crossPageDuplicate ? { kind: 'malformed-response' } : state}
          subject="cari listesi"
          onRetry={() => {
            resetPaging()
            void query.refetch()
          }}
        />
      ) : items.length === 0 ? (
        /**
         * `keepPreviousData` hands back the PREVIOUS page while the new query is in
         * flight, so an empty result that is still being replaced is not evidence
         * about the filters now on screen. Labelling it anyway produced a real wrong
         * message: clearing the last filter showed "Kayıt yok — bu görünümde henüz
         * kayıt yok" over a company that has counterparties, for as long as the
         * refetch took. Say what is true instead: this is still loading.
         */
        query.isPlaceholderData ? (
          <StateBlock state={{ kind: 'initial-loading' }} subject="cari listesi" />
        ) : (
          <StateBlock
            state={{ kind: filterActive ? 'empty-filtered' : 'empty-no-data' }}
            subject="cari"
          />
        )
      ) : (
        <>
          {/* The rows below are the last VALIDATED set. While a refetch is in flight
              (filter change, or a continuation page) that is a fact the user is
              entitled to, not a detail to hide behind unchanged-looking content. */}
          {/* Six columns do not fit a phone. The TABLE may scroll sideways; the PAGE
              must not (WCAG 1.4.10). `tabindex=0` makes the scroll region reachable
              by keyboard — otherwise the off-screen columns are mouse-only. */}
          <div
            className={styles.tableScroll}
            // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
            tabIndex={0}
            role="region"
            aria-label="Cari listesi tablosu"
          >
          <table className={styles.table} aria-busy={query.isFetching}>
            <caption className={styles.caption}>
              {/*
                The total and WHAT IT COVERS, together (§8.7/65).

                A bare "128 kayıt" over a filtered, authorization-scoped read invites
                the one reading we are not entitled to offer — "this company has 128
                counterparties". The scope sentence is not decoration; it is the
                difference between a number and a claim.

                `totalCount` is carried as a STRING all the way here: it is an int64
                that can exceed the JS safe range, and it is displayed, never math'd.
                That is also why "loaded" is stated separately instead of being
                subtracted from it.
              */}
              <span className={styles.countValue}>
                {page?.totalCount != null
                  ? `${page.totalCount} kayıt`
                  : `${items.length} kayıt yüklendi`}
              </span>
              {page?.totalCount != null && items.length > 0 ? (
                <span className={styles.countLoaded}>{` · ${items.length} tanesi yüklendi`}</span>
              ) : null}
              {page?.countScope != null && COUNT_SCOPE_LABEL[page.countScope] !== undefined ? (
                <span className={styles.countScope}>{COUNT_SCOPE_LABEL[page.countScope]}</span>
              ) : null}
            </caption>
            <thead>
              <tr>
                <th scope="col" aria-sort={ariaSort('code')}>
                  <button type="button" onClick={() => toggleSort('code')}>
                    Kod
                  </button>
                </th>
                <th scope="col" aria-sort={ariaSort('legalName')}>
                  <button type="button" onClick={() => toggleSort('legalName')}>
                    Unvan
                  </button>
                </th>
                <th scope="col">Tür</th>
                {/* Named as history, for the same reason the filter is: a column
                    called "Bu şirketteki roller" states the classification as the
                    counterparty's current commercial direction (ILK_DILIM §7). */}
                <th scope="col">{LEGACY_ROLE_HEADING}</th>
                <th scope="col">Taraf durumu</th>
                <th scope="col">Şirket hesabı durumu</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                // DEC-CAR-002: the account of the ACTIVE company only. One account holds
                // combined role flags; roles are never unioned across companies. The
                // decoder already guarantees this account exists and belongs to the
                // active company in single mode, so there is no "—" case left.
                const account = item.companyAccounts[0]
                return (
                  <tr key={item.id} data-inactive={!item.isActive ? 'true' : undefined}>
                    <td>
                      {/* The code is the row's identity, so it carries the link:
                          short, unique and stable, which makes it a better target
                          than a long legal name that wraps to three lines on a
                          phone. The accessible name includes the legal name so a
                          screen-reader user hearing the link out of context knows
                          which counterparty it opens. */}
                      {isRendered(detailGate) ? (
                        <Link
                          className={styles.rowLink}
                          to={`/cari/${item.id}`}
                          aria-label={`${item.code} — ${item.legalName}`}
                        >
                          {item.code}
                        </Link>
                      ) : (
                        // No detail grant: the code is still shown, but as text —
                        // no link, no disabled link, no explanation. Rendering a
                        // link the route would refuse anyway offers an action the
                        // caller is not entitled to and tells them the screen
                        // exists. Codex caught this on #172 (P1-1); actionGating.ts
                        // now states it as the general rule.
                        item.code
                      )}
                    </td>
                    <td>
                      <span className={styles.legalName}>{item.legalName}</span>
                      {item.tradeName !== null && item.tradeName !== item.legalName ? (
                        <span className={styles.tradeName}>{item.tradeName}</span>
                      ) : null}
                    </td>
                    <td>{KIND_LABEL[item.kind]}</td>
                    {/* An empty cell here would read exactly like the "unknown" the
                        two state columns below were written to avoid. A counterparty
                        with no commercial classification is an ordinary record as of
                        the 10 September decision, and the cell says so. */}
                    <td data-roles={account.roles === 0 ? 'none' : 'legacy'}>
                      {rolesText(account.roles, NO_COMMERCIAL_ROLE_LIST)}
                    </td>
                    {/* AT-013 + DEC-CAR-002: two separate facts, both stated explicitly.
                        Showing only the passive one would leave "active" as an absence,
                        which reads the same as "unknown". */}
                    <td data-party-state={item.isActive ? 'active' : 'inactive'}>
                      {item.isActive ? 'Aktif' : 'Pasif'}
                    </td>
                    <td data-account-state={account.isActive ? 'active' : 'inactive'}>
                      {account.isActive ? 'Aktif' : 'Pasif'}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          </div>

          {continuationFailed ? (
            /* Scoped to the continuation, not the list. Retry re-issues the SAME
               cursor request — it does not reset paging, so the rows above stay
               exactly where they are. */
            <div className={styles.continuationError} role="alert">
              <span>Daha fazla kayıt yüklenemedi; mevcut kayıtlar korunuyor.</span>
              <button type="button" onClick={() => void query.refetch()} disabled={query.isFetching}>
                {query.isFetching ? 'Yükleniyor…' : 'Tekrar dene'}
              </button>
            </div>
          ) : page?.hasMore ? (
            <button
              type="button"
              className={styles.more}
              onClick={loadMore}
              disabled={query.isFetching}
            >
              {query.isFetching ? 'Yükleniyor…' : 'Daha fazla yükle'}
            </button>
          ) : null}
        </>
      )}
    </section>
  )
}
