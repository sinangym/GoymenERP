/**
 * ORG-01 — Ayarlar > Organizasyon: the company's facilities and their locations.
 *
 * Scope is exactly what the merged Organization API can answer and nothing beyond it.
 * Deliberately absent: stock balances and movements, production capacity, KPI tiles,
 * creating a company/site/location, and any ownership or operator fact the API does
 * not return. A card whose owner API does not exist is not rendered empty — it is not
 * rendered, because an empty box reads as "this facility has none", which is a claim.
 *
 * NO SEARCH BOX, ON PURPOSE. `/locations` accepts only `page` and `pageSize` — there
 * is no `q` and no `siteId` filter (third Backend's source handover). A search input
 * that silently matched only the 25 rows in hand would tell the user they had
 * searched the company when they had not, so the screen offers paging and honest
 * grouping instead of a filter it cannot deliver.
 *
 * Passive locations STAY in the list — the server does not hide them and neither does
 * this screen. Deactivation is a state, not a delete.
 */

import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useLocation } from 'react-router'
import { fetchLocationPage, fetchSites, DEFAULT_PAGE_SIZE } from './api'
import { pageCount, type LocationPage, type SiteSummary } from './contract'
import { locationDetailPath } from './capabilities'
import { groupLocations } from './locationGrouping'
import { activeStateLabel, externalLabel, siteTypeLabel } from './organizationText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './OrganizationSettingsPage.module.css'

/**
 * The context fence for this screen.
 *
 * Remounted on every committed-context change by keying on the context namespace, the
 * same rule C-01/C-03 follow (#147 P1-2). A company switch must not leave one
 * company's facilities on screen under another company's label, and remounting is
 * what guarantees the page number resets too — a page 3 that existed in company A is
 * meaningless in company B.
 */
/**
 * What a return from the location card carries back, so the user lands on the page
 * they left rather than on page 1.
 *
 * It travels in the ROUTER's location state rather than the URL, and it carries the
 * context namespace with it. That namespace is the whole point: a company switch keeps
 * the same history entry, so a bare page number would restore company A's page 3 in
 * company B. Checking it makes "same context returns to its page" and "a switch resets
 * to page 1" the same rule rather than two competing ones.
 */
export interface OrganizationListReturn {
  readonly page: number
  readonly ns: string
}

function returnedPage(state: unknown, namespace: string | null): number {
  if (typeof state !== 'object' || state === null || namespace === null) return 1
  const candidate = state as Partial<OrganizationListReturn>
  if (candidate.ns !== namespace) return 1
  return typeof candidate.page === 'number' && Number.isSafeInteger(candidate.page) && candidate.page >= 1
    ? candidate.page
    : 1
}

export function OrganizationSettingsPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <OrganizationSettings key={namespace ?? 'no-context'} namespace={namespace} />
}

function OrganizationSettings({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const entryGeneration = shell.routeEntryGeneration
  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scopeMode = shell.activeContext?.scopeMode ?? 'single'
  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null
  const enabled = namespace !== null && singleCompanyContext

  // Seeded from the return state for THIS context; 1 for a fresh entry or after a
  // company switch (the namespace in the state will not match).
  const routerLocation = useLocation()
  const [page, setPage] = useState(() => returnedPage(routerLocation.state, namespace))

  const sitesQuery = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'organization-sites'],
    enabled,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchSites(activeCompanyId ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const locationsQuery = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'organization-locations', page],
    enabled,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchLocationPage(
        activeCompanyId ?? '',
        { page, pageSize: DEFAULT_PAGE_SIZE },
        signal,
      )
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const sitesState = (
    sitesQuery.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(sitesQuery.data ?? { type: 'malformed' })
  ) as RemoteViewState<readonly SiteSummary[]>

  const locationsState = (
    locationsQuery.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(locationsQuery.data ?? { type: 'malformed' })
  ) as RemoteViewState<LocationPage>

  const sites = sitesState.kind === 'ready' ? sitesState.data : []
  const locationPage = locationsState.kind === 'ready' ? locationsState.data : null

  if (!singleCompanyContext) {
    return (
      <section aria-labelledby="org-heading" className={styles.page}>
        <h1 id="org-heading">Organizasyon</h1>
        <StateBlock
          state={{ kind: 'no-authorized-company' }}
          subject="Organizasyon için tek şirket bağlamı"
        />
      </section>
    )
  }

  return (
    <section aria-labelledby="org-heading" className={styles.page}>
      <header className={styles.header}>
        <h1 id="org-heading">Organizasyon</h1>
        <p className={styles.lead}>
          Bu şirket bağlamındaki tesisler ve lokasyonlar. Bir lokasyonun kartını açmak için
          koduna tıklayın.
        </p>
      </header>

      <SitesSection state={sitesState} onRetry={() => void sitesQuery.refetch()} />

      <LocationsSection
        state={locationsState}
        sites={sites}
        page={page}
        namespace={namespace}
        onPage={setPage}
        onRetry={() => void locationsQuery.refetch()}
      />

      {locationPage === null ? null : <PageSummary page={locationPage} />}
    </section>
  )
}

function SitesSection({
  state,
  onRetry,
}: {
  readonly state: RemoteViewState<readonly SiteSummary[]>
  readonly onRetry: () => void
}) {
  return (
    <section aria-labelledby="org-sites-heading" className={styles.section}>
      <h2 id="org-sites-heading" className={styles.sectionHeading}>
        Tesisler
      </h2>
      {state.kind !== 'ready' ? (
        <StateBlock state={state} subject="tesisler" onRetry={onRetry} />
      ) : state.data.length === 0 ? (
        <StateBlock state={{ kind: 'empty-no-data' }} subject="tesisler" />
      ) : (
        <ul className={styles.siteList}>
          {state.data.map((site) => (
            <li key={site.id} className={styles.siteCard}>
              <span className={styles.siteCode}>{site.code}</span>
              <span className={styles.siteName}>{site.name}</span>
              <span className={styles.siteMeta}>{siteTypeLabel(site.type)}</span>
              <StateBadge isActive={site.isActive} />
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/**
 * Status is never carried by colour alone (UI_DESIGN_SYSTEM §2, §9): the badge shows
 * the word, and the colour is a second, redundant signal.
 */
function StateBadge({ isActive }: { readonly isActive: boolean }) {
  return (
    <span
      className={isActive ? styles.badgeActive : styles.badgePassive}
      data-state={isActive ? 'active' : 'passive'}
    >
      {activeStateLabel(isActive)}
    </span>
  )
}

function LocationsSection({
  state,
  sites,
  page,
  namespace,
  onPage,
  onRetry,
}: {
  readonly state: RemoteViewState<LocationPage>
  readonly sites: readonly SiteSummary[]
  readonly page: number
  readonly namespace: string | null
  readonly onPage: (page: number) => void
  readonly onRetry: () => void
}) {
  const groups = useMemo(
    () => (state.kind === 'ready' ? groupLocations(state.data.items, sites) : []),
    [state, sites],
  )

  if (state.kind !== 'ready') {
    return (
      <section aria-labelledby="org-locations-heading" className={styles.section}>
        <h2 id="org-locations-heading" className={styles.sectionHeading}>
          Lokasyonlar
        </h2>
        <StateBlock state={state} subject="lokasyonlar" onRetry={onRetry} />
      </section>
    )
  }

  const total = pageCount(state.data)

  return (
    <section aria-labelledby="org-locations-heading" className={styles.section}>
      <h2 id="org-locations-heading" className={styles.sectionHeading}>
        Lokasyonlar
      </h2>

      {state.data.items.length === 0 ? (
        page > 1 ? (
          // A page past the end is not "no data" — the records exist, this page just
          // has none. Saying "Kayıt yok" here would be false.
          <div className={styles.block} role="status">
            <p>Bu sayfada kayıt yok.</p>
            <button type="button" className={styles.pagerButton} onClick={() => onPage(1)}>
              İlk sayfaya dön
            </button>
          </div>
        ) : (
          <StateBlock state={{ kind: 'empty-no-data' }} subject="lokasyonlar" />
        )
      ) : (
        <div className={styles.tableScroll}>
          {groups.map((group) => (
            <table key={group.siteId} className={styles.table}>
              <caption className={styles.caption}>
                {group.heading}
                {group.subheading === null ? null : (
                  <span className={styles.captionMeta}> · {group.subheading}</span>
                )}
              </caption>
              <thead>
                <tr>
                  <th scope="col">Kod</th>
                  <th scope="col">Ad</th>
                  <th scope="col">Tür</th>
                  <th scope="col">Durum</th>
                </tr>
              </thead>
              <tbody>
                {group.rows.map((location) => (
                  <tr key={location.id}>
                    <th scope="row" className={styles.codeCell}>
                      <Link
                        className={styles.rowLink}
                        to={locationDetailPath(location.id)}
                        // The page to come back to, bound to this context.
                        state={
                          namespace === null
                            ? undefined
                            : ({ page, ns: namespace } satisfies OrganizationListReturn)
                        }
                      >
                        {location.code}
                      </Link>
                    </th>
                    <td>{location.name}</td>
                    <td>{externalLabel(location.isExternal)}</td>
                    <td>
                      <StateBadge isActive={location.isActive} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ))}
        </div>
      )}

      <Pager page={state.data.page} total={total} onPage={onPage} />
    </section>
  )
}

/**
 * Offset pager. `page`/`total` come from the SERVER's echoed values, because the
 * server clamps `pageSize` into [1,100] and could legitimately serve a different page
 * size than we asked for — paging off our own request would then drift.
 */
function Pager({
  page,
  total,
  onPage,
}: {
  readonly page: number
  readonly total: number
  readonly onPage: (page: number) => void
}) {
  if (total <= 1) return null
  return (
    <nav className={styles.pager} aria-label="Lokasyon sayfaları">
      <button
        type="button"
        className={styles.pagerButton}
        onClick={() => onPage(page - 1)}
        disabled={page <= 1}
      >
        Önceki
      </button>
      <span aria-live="polite" className={styles.pagerStatus}>
        Sayfa {page} / {total}
      </span>
      <button
        type="button"
        className={styles.pagerButton}
        onClick={() => onPage(page + 1)}
        disabled={page >= total}
      >
        Sonraki
      </button>
    </nav>
  )
}

/**
 * States the page boundary explicitly. The design system requires a total to name its
 * own scope so a narrow-scope user is never shown a company-wide figure by
 * implication — `totalCount` is already scoped by the caller's permission on the
 * server, and this line says which set it counts.
 */
function PageSummary({ page }: { readonly page: LocationPage }) {
  const first = page.totalCount === 0 ? 0 : (page.page - 1) * page.pageSize + 1
  const last = (page.page - 1) * page.pageSize + page.items.length
  return (
    <p className={styles.summary}>
      Yetkiniz dâhilindeki {page.totalCount} lokasyondan {first}–{last} arası gösteriliyor.
    </p>
  )
}
