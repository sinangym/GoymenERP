/**
 * ORG-03 — Ayarlar > Organizasyon > İş Merkezleri: the company's work centres.
 *
 * Scope is exactly what `GET /organization/work-centers` answers and nothing beyond it:
 * code, name, kind, active state, and the server's own per-row answer to "may this
 * caller open this row's location card". Deliberately absent, because the endpoint
 * cannot answer them and an empty box reads as a claim: capacity, shift calendars,
 * current load, throughput, the location's NAME, and any production or ownership fact.
 *
 * NO SEARCH BOX AND NO PAGER, ON PURPOSE — and for opposite reasons to ORG-01's. That
 * screen omits search because its endpoint pages without filtering; this one is UNPAGED,
 * so the array in hand IS the caller's whole authorized set. A pager here would have one
 * page forever, and a search box would be honest — which is precisely why adding one is
 * a decision for whoever measures that the list has grown too long to scan, not a
 * default. HYBRID-05 now adds only an exact location filter over that complete
 * authorized array. It is not a server query, a new permission, or a pager.
 *
 * Passive work centres STAY in the list: the server does not hide them and neither does
 * this screen. Deactivation is a state, not a delete.
 */

import { useQuery } from '@tanstack/react-query'
import { Link, useLocation } from 'react-router'
import { fetchWorkCenters } from './api'
import type { WorkCenterSummary } from './contract'
import { locationDetailPath, ORGANIZATION_CAPABILITY, workCenterListPath } from './capabilities'
import {
  filterWorkCenters, filteredWorkCenterDetailReturn, readWorkCenterLocationFilter,
  WORK_CENTER_LOCATION_TEXT,
} from './workCenterLocationFilter'
import {
  LOCATION_NOT_OPENABLE,
  WORK_CENTERS_HEADING,
  WORK_CENTERS_LEAD,
  activeStateLabel,
  workCenterKindLabel,
} from './organizationText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './WorkCenterListPage.module.css'

/**
 * The context fence, keyed exactly as ORG-01's is (#147 P1-2): the screen REMOUNTS on
 * every committed-context change. A company switch must not leave company A's work
 * centres on screen under company B's label, and remounting is what guarantees that
 * rather than a best-effort refetch racing the render.
 */
export function WorkCenterListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <WorkCenterList key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function WorkCenterList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const entryGeneration = shell.routeEntryGeneration
  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scopeMode = shell.activeContext?.scopeMode ?? 'single'
  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null
  const filter = readWorkCenterLocationFilter(useLocation().search)
  const canRead = hasCapability(shell, ORGANIZATION_CAPABILITY.workCentersList)
  const enabled = namespace !== null && singleCompanyContext && canRead && filter.kind !== 'invalid'

  const workCentersQuery = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'organization-work-centers'],
    enabled,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchWorkCenters(activeCompanyId ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const state = (
    workCentersQuery.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(workCentersQuery.data ?? { type: 'malformed' })
  ) as RemoteViewState<readonly WorkCenterSummary[]>

  if (!singleCompanyContext) {
    return (
      <section aria-labelledby="org-wc-heading" className={styles.page}>
        <h1 id="org-wc-heading">{WORK_CENTERS_HEADING}</h1>
        <StateBlock
          state={{ kind: 'no-authorized-company' }}
          subject="İş merkezleri için tek şirket bağlamı"
        />
      </section>
    )
  }

  if (namespace === null || !canRead) {
    return <StateBlock state={{ kind: 'not-found' }} subject="iş merkezleri" />
  }

  const rows = state.kind === 'ready' ? filterWorkCenters(state.data, filter) : []
  return (
    <section aria-labelledby="org-wc-heading" className={styles.page}>
      <header className={styles.header}>
        <h1 id="org-wc-heading">{WORK_CENTERS_HEADING}</h1>
        <p className={styles.lead}>{WORK_CENTERS_LEAD}</p>
      </header>

      {filter.kind !== 'all' ? <nav aria-label={WORK_CENTER_LOCATION_TEXT.label}>
        {filter.kind === 'location' ? <p>{WORK_CENTER_LOCATION_TEXT.label}: {filter.locationId}</p> : null}
        <Link to={workCenterListPath()}>{WORK_CENTER_LOCATION_TEXT.clear}</Link>
      </nav> : null}

      {filter.kind === 'invalid' ? (
        <StateBlock state={{ kind: 'malformed-response' }} subject={WORK_CENTER_LOCATION_TEXT.invalidSubject} />
      ) : state.kind !== 'ready' ? (
        <StateBlock
          state={state}
          subject="iş merkezleri"
          onRetry={() => void workCentersQuery.refetch()}
        />
      ) : rows.length === 0 ? (
        <StateBlock
          state={{ kind: filter.kind === 'location' ? 'empty-filtered' : 'empty-no-data' }}
          subject={filter.kind === 'location' ? WORK_CENTER_LOCATION_TEXT.filteredSubject : 'iş merkezleri'}
        />
      ) : (
        <WorkCenterTable rows={rows} namespace={namespace} filterLocationId={filter.kind === 'location' ? filter.locationId : null} />
      )}
    </section>
  )
}

function WorkCenterTable({
  rows,
  namespace,
  filterLocationId,
}: {
  readonly rows: readonly WorkCenterSummary[]
  readonly namespace: string | null
  readonly filterLocationId: string | null
}) {
  return (
    <div className={styles.tableScroll}>
      <table className={styles.table}>
        <caption className={styles.caption}>
          {WORK_CENTERS_HEADING}
          <span className={styles.captionMeta}>{rows.length} kayıt</span>
        </caption>
        <thead>
          <tr>
            <th scope="col">Kod</th>
            <th scope="col">Ad</th>
            <th scope="col">Tür</th>
            <th scope="col">Durum</th>
            <th scope="col">Lokasyon</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id}>
              <td className={styles.codeCell}>{row.code}</td>
              <td>{row.name}</td>
              <td>{workCenterKindLabel(row.kind)}</td>
              <td>
                {/* Status is never colour alone (UI_DESIGN_SYSTEM §2, §9): the word is
                    the signal and the colour is redundant. */}
                <span
                  className={row.isActive ? styles.badgeActive : styles.badgePassive}
                  data-state={row.isActive ? 'active' : 'passive'}
                >
                  {activeStateLabel(row.isActive)}
                </span>
              </td>
              <td>
                <LocationCell row={row} namespace={namespace} filterLocationId={filterLocationId} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/**
 * The one place this screen consults `canOpenLocationDetail`, and it renders it — it
 * never derives it.
 *
 * The screen has the `locationId` of every row, so it COULD always draw the link. It
 * does not, because an id is an identifier and not an entitlement: the work-centre read
 * and the location read are separate grants on the server. A link that 404s on click
 * teaches the user that the app is unreliable, when in fact the server answered
 * correctly and the screen asked a question it had already been told the answer to.
 *
 * The alternative shape — draw the link and let the card refuse — would ALSO leak: a
 * card that answers 404 for an out-of-scope location and a card that answers 404 for a
 * location that does not exist are indistinguishable to the caller, which is deliberate,
 * and a link the client drew from a scope it guessed would turn that deliberate silence
 * into a probe. So the flag is the server's statement and this cell is its rendering.
 *
 * Not a link, and not a disabled link either: a disabled control implies "later, or with
 * one more step", and there is neither here. It is a plain statement of scope.
 */
function LocationCell({
  row,
  namespace,
  filterLocationId,
}: {
  readonly row: WorkCenterSummary
  readonly namespace: string | null
  readonly filterLocationId: string | null
}) {
  if (!row.canOpenLocationDetail) {
    return <span className={styles.locationClosed}>{LOCATION_NOT_OPENABLE}</span>
  }
  return (
    <Link
      className={styles.rowLink}
      to={locationDetailPath(row.locationId)}
      state={filteredWorkCenterDetailReturn(namespace, filterLocationId)}
    >
      {/* The location NAME is not in this response, so the id is shown rather than a
          guessed label. Inventing one, or fetching the card just to title a link, would
          both be claims this screen has no source for. */}
      Lokasyon kartını aç
    </Link>
  )
}
