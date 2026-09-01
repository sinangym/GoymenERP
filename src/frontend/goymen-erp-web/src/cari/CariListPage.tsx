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
  PARTY_KIND,
  PROBLEM_CODE,
  problemCodeOf,
  rolesFromFlags,
  type CounterpartyListItem,
  type CounterpartyPage,
} from './contract'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKeyString, contextKey } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './CariListPage.module.css'

const KIND_LABEL: Record<number, string> = {
  [PARTY_KIND.LegalEntity]: 'Tüzel kişi',
  [PARTY_KIND.NaturalPerson]: 'Gerçek kişi',
}

const ROLE_LABEL: Record<string, string> = {
  Customer: 'Müşteri',
  Supplier: 'Tedarikçi',
  Tolling: 'Fason',
  Service: 'Hizmet',
}

const ROLE_OPTIONS: readonly RoleFilter[] = ['Customer', 'Supplier', 'Tolling', 'Service']

/**
 * NEEDS-SINAN (via ChatGPT C-01 spec): the default party-state filter. `active` is used
 * here because DEC-CAR-003 keeps used records visible but inactive, and an operator
 * listing counterparties normally works with active ones. Passives remain reachable via
 * the filter (AT-013).
 */
const DEFAULT_PARTY_STATE: PartyStateFilter = 'active'
/** Contract default. The company-account state is filtered independently of the Party. */
const DEFAULT_ACCOUNT_STATE: AccountStateFilter = 'all'
const DEFAULT_PAGE_SIZE = 50

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
  const [q, setQ] = useState('')
  const [kind, setKind] = useState<KindFilter | ''>('')
  const [roles, setRoles] = useState<readonly RoleFilter[]>([])
  const [roleMatch, setRoleMatch] = useState<RoleMatch>('any')
  const [partyState, setPartyState] = useState<PartyStateFilter>(DEFAULT_PARTY_STATE)
  const [accountState, setAccountState] = useState<AccountStateFilter>(DEFAULT_ACCOUNT_STATE)
  const [sort, setSort] = useState<SortField>('code')
  const [direction, setDirection] = useState<SortDirection>('asc')
  const [cursor, setCursor] = useState<string | undefined>(undefined)
  const [loaded, setLoaded] = useState<readonly CounterpartyListItem[]>([])

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
  const boundary = useAuthBoundary(shell.activeContext?.userId ?? null)

  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null

  const filterActive =
    q.trim().length > 0 ||
    kind !== '' ||
    roles.length > 0 ||
    partyState !== DEFAULT_PARTY_STATE ||
    accountState !== DEFAULT_ACCOUNT_STATE

  const query = useQuery({
    // INV-NS: the company context owns the cache namespace, so a company switch can
    // never surface another company's page.
    queryKey: [
      namespace ?? 'no-context',
      'counterparties',
      { q, kind, roles, roleMatch, partyState, accountState, sort, direction, cursor },
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
          pageSize: DEFAULT_PAGE_SIZE,
          cursor,
        },
        signal,
      )
      // SAB-006: first page, continuation and refetch all report here. There is
      // no weaker per-path variant — this IS the path.
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        const action = boundary.report(outcome.status, snapshot)
        // SAB-008/009: a result that is no longer current produces no security
        // effect and no data — it is discarded, not rendered as an error.
        if (action.kind === 'stale-discard') {
          return { type: 'fence-stale' } satisfies ViewOutcome<CounterpartyPage>
        }
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

        <fieldset className={styles.roles}>
          <legend>Rol</legend>
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
            <span>Rol eşleşmesi</span>
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
      </div>

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
        <StateBlock
          state={{ kind: filterActive ? 'empty-filtered' : 'empty-no-data' }}
          subject="cari"
        />
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
              {/* The scope is VALIDATED, not displayed: `filtered-authorized-visible-parties`
                  is a machine token, and printing it tells the user nothing. */}
              {page?.totalCount != null
                ? `${page.totalCount} kayıt`
                : `${items.length} kayıt yüklendi`}
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
                <th scope="col">Bu şirketteki roller</th>
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
                    <td>{item.code}</td>
                    <td>
                      <span className={styles.legalName}>{item.legalName}</span>
                      {item.tradeName !== null && item.tradeName !== item.legalName ? (
                        <span className={styles.tradeName}>{item.tradeName}</span>
                      ) : null}
                    </td>
                    <td>{KIND_LABEL[item.kind]}</td>
                    <td>
                      {rolesFromFlags(account.roles)
                        .map((role) => ROLE_LABEL[role])
                        .join(', ')}
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
