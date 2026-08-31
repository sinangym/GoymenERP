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
  type CounterpartyScope,
  type KindFilter,
  type PartyStateFilter,
  type RoleFilter,
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
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKeyString, contextKey } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
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
const DEFAULT_PAGE_SIZE = 50

export function CariListPage() {
  const shell = useShellState()
  const [q, setQ] = useState('')
  const [kind, setKind] = useState<KindFilter | ''>('')
  const [roles, setRoles] = useState<readonly RoleFilter[]>([])
  const [partyState, setPartyState] = useState<PartyStateFilter>(DEFAULT_PARTY_STATE)
  const [sort, setSort] = useState<SortField>('code')
  const [direction, setDirection] = useState<SortDirection>('asc')
  const [cursor, setCursor] = useState<string | undefined>(undefined)
  const [loaded, setLoaded] = useState<readonly CounterpartyListItem[]>([])

  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scope: CounterpartyScope = useMemo(
    () => ({ mode: shell.activeContext?.scopeMode ?? 'single', companyId: activeCompanyId }),
    [shell.activeContext?.scopeMode, activeCompanyId],
  )

  const key = contextKey(shell)
  const filterActive = q.trim().length > 0 || kind !== '' || roles.length > 0 || partyState !== 'active'

  const query = useQuery({
    // INV-NS: the company context owns the cache namespace, so a company switch can
    // never surface another company's page.
    queryKey: [
      key === null ? 'no-context' : contextKeyString(key),
      'counterparties',
      { q, kind, roles, partyState, sort, direction, cursor },
    ],
    enabled: key !== null,
    placeholderData: keepPreviousData,
    queryFn: ({ signal }) =>
      fetchCounterparties(
        scope,
        {
          q,
          kind: kind === '' ? undefined : kind,
          role: roles.length > 0 ? roles : undefined,
          roleMatch: roles.length > 0 ? 'any' : undefined,
          partyState,
          sort,
          direction,
          pageSize: DEFAULT_PAGE_SIZE,
          cursor,
        },
        signal,
      ),
  })

  const outcome = query.data
  const state = (
    query.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(outcome ?? { type: 'malformed' })
  ) as RemoteViewState<CounterpartyPage>

  const page = state.kind === 'ready' ? state.data : null
  // Accumulate across cursor pages; a filter/sort change resets via resetPaging().
  const items = page === null ? loaded : cursor === undefined ? page.items : [...loaded, ...page.items]

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
  // oxlint-disable-next-line react/set-state-in-effect -- see the note above
  useEffect(() => {
    if (isInvalidCursor && cursor !== undefined && recoveredCursor.current !== cursor) {
      recoveredCursor.current = cursor
      setCursor(undefined)
      setLoaded([])
    }
  }, [isInvalidCursor, cursor])

  const ariaSort = (field: SortField): 'ascending' | 'descending' | 'none' =>
    sort !== field ? 'none' : direction === 'asc' ? 'ascending' : 'descending'

  return (
    <section aria-labelledby="cari-list-heading" className={styles.page}>
      <h1 id="cari-list-heading">Cari Kartlar</h1>

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

        <label className={styles.field}>
          <span>Durum</span>
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
      </div>

      {state.kind !== 'ready' ? (
        <StateBlock state={state} subject="cari listesi" onRetry={() => void query.refetch()} />
      ) : items.length === 0 ? (
        <StateBlock
          state={{ kind: filterActive ? 'empty-filtered' : 'empty-no-data' }}
          subject="cari"
        />
      ) : (
        <>
          <table className={styles.table}>
            <caption className={styles.caption}>
              {page?.totalCount !== null && page?.totalCount !== undefined
                ? `${page.totalCount} kayıt${page.countScope ? ` (${page.countScope})` : ''}`
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
                <th scope="col">Durum</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                // DEC-CAR-002: the account of the ACTIVE company only. One account holds
                // combined role flags; roles are never unioned across companies.
                const account =
                  activeCompanyId === null
                    ? undefined
                    : item.companyAccounts.find((a) => a.companyId === activeCompanyId)
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
                      {account === undefined
                        ? '—'
                        : rolesFromFlags(account.roles)
                            .map((role) => ROLE_LABEL[role])
                            .join(', ')}
                    </td>
                    <td>
                      {/* AT-013 + DEC-CAR-002: the global party state and this company's
                          account state are separate facts and are shown separately. */}
                      <span>{item.isActive ? 'Cari aktif' : 'Cari pasif'}</span>
                      {account !== undefined && !account.isActive ? (
                        <span className={styles.accountInactive}>şirket hesabı pasif</span>
                      ) : null}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>

          {page?.hasMore ? (
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
