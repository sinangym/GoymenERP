/**
 * ORG-03 — Ayarlar > Organizasyon > İş Merkezleri: the caller's authorized work centers,
 * and the step from a row to the location it names.
 *
 * THE SLICE IS EXACTLY THIS: authorized list → (only with the location-card capability)
 * the existing location card → back to the list. Deliberately absent, because no merged
 * endpoint answers them: a work-center card, a work-center status change, capacity,
 * production movement, any KPI, and any new permission or business rule. There is no
 * work-center detail or status endpoint at `main@eacbfe7`, so this screen renders a list
 * and one outbound link and nothing else.
 *
 * WHY THIS IS ITS OWN ROUTE, NOT A SECTION OF ORG-01. The reads carry different
 * authorities: `/work-centers` authorizes `organization.work-centers.read`, while
 * ORG-01's route stands on `organization.locations.read`. A user holding the first and
 * not the second is entitled to this list, and hanging it off ORG-01 would close it for
 * exactly that user.
 *
 * NO SEARCH BOX AND NO FILTER, for the same reason ORG-01 has none: the endpoint accepts
 * no parameters at all (it is unpaged too), so a filter here could only ever filter rows
 * already in hand while looking like it had asked the server.
 *
 * PASSIVE WORK CENTERS STAY IN THE LIST. The server does not hide them and neither does
 * this screen; `isActive` is a state, not a delete.
 */

import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchWorkCenters } from './api'
import type { WorkCenterSummary } from './contract'
import { ORGANIZATION_CAPABILITY, locationDetailPath } from './capabilities'
import { workCenterReturnState } from './detailReturn'
import {
  WORK_CENTER_HEADING,
  WORK_CENTER_LOCATION_LINK_TEXT,
  WORK_CENTER_LOCATION_UNAVAILABLE,
  activeStateLabel,
  workCenterCountText,
  workCenterKindLabel,
  workCenterLeadText,
  workCenterLocationLinkLabel,
} from './organizationText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './WorkCenterListPage.module.css'

/**
 * The context fence, keyed exactly like ORG-01's.
 *
 * Remounting on every committed-context change is what guarantees company A's work
 * centers can never be left on screen under company B's label, and it is also what
 * makes the late-response case safe without a second mechanism: the query key carries
 * the namespace, so A's in-flight read resolves into a cache entry B never observes.
 */
export function WorkCenterListPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <WorkCenterList key={namespace ?? 'no-context'} namespace={namespace} />
}

function WorkCenterList({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const entryGeneration = shell.routeEntryGeneration
  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scopeMode = shell.activeContext?.scopeMode ?? 'single'
  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null
  const enabled = namespace !== null && singleCompanyContext

  /**
   * Whether the location-card ROUTE exists for this user at all — the ORG-02 capability.
   * It is necessary, not sufficient: Business #66 5700319744 puts the decision at ROW
   * level, so a row links only when this holds AND the server marked that row
   * `canOpenLocationDetail` (see `LocationCell`). Without the capability no row links,
   * whatever the rows say, and NO REQUEST IS MADE: the `locationId` on a work-center row
   * is an identifier the caller happens to hold, never an entitlement to read it.
   */
  const canOpenLocation = hasCapability(shell, ORGANIZATION_CAPABILITY.locationDetail)

  const query = useQuery({
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
    query.isPending
      ? { kind: 'initial-loading' }
      : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<readonly WorkCenterSummary[]>

  // The lead points at "the link in the row" only when a row on screen actually has one.
  const leadOffersLink =
    canOpenLocation && state.kind === 'ready' && state.data.some(isLocationOpenable)

  if (!singleCompanyContext) {
    return (
      <section aria-labelledby="org-wc-heading" className={styles.page}>
        <h1 id="org-wc-heading">{WORK_CENTER_HEADING}</h1>
        <StateBlock
          state={{ kind: 'no-authorized-company' }}
          subject="İş merkezleri için tek şirket bağlamı"
        />
      </section>
    )
  }

  return (
    <section aria-labelledby="org-wc-heading" className={styles.page}>
      <header className={styles.header}>
        <h1 id="org-wc-heading">{WORK_CENTER_HEADING}</h1>
        <p className={styles.lead}>{workCenterLeadText(leadOffersLink)}</p>
      </header>

      {state.kind !== 'ready' ? (
        <StateBlock
          state={state}
          subject="iş merkezleri"
          onRetry={() => void query.refetch()}
        />
      ) : state.data.length === 0 ? (
        /* A successful read of zero rows. The endpoint is unpaged, so there is no
           "this page is empty" variant to confuse this with — it is the whole set. */
        <StateBlock state={{ kind: 'empty-no-data' }} subject="iş merkezleri" />
      ) : (
        <>
          <WorkCenterTable
            rows={state.data}
            namespace={namespace}
            canOpenLocation={canOpenLocation}
          />
          <p className={styles.summary}>{workCenterCountText(state.data.length)}</p>
        </>
      )}
    </section>
  )
}

function WorkCenterTable({
  rows,
  namespace,
  canOpenLocation,
}: {
  readonly rows: readonly WorkCenterSummary[]
  readonly namespace: string | null
  readonly canOpenLocation: boolean
}) {
  return (
    <div className={styles.tableScroll}>
      <table className={styles.table}>
        <caption className={styles.caption}>{WORK_CENTER_HEADING}</caption>
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
          {rows.map((workCenter) => (
            <tr key={workCenter.id}>
              <th scope="row" className={styles.codeCell}>
                {workCenter.code}
              </th>
              <td>{workCenter.name}</td>
              <td>{workCenterKindLabel(workCenter.kind)}</td>
              <td>
                <StateBadge isActive={workCenter.isActive} />
              </td>
              <td>
                <LocationCell
                  workCenter={workCenter}
                  namespace={namespace}
                  canOpenLocation={canOpenLocation}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/** The row's own server-computed eligibility; never derived from scope on this side. */
function isLocationOpenable(workCenter: WorkCenterSummary): boolean {
  return workCenter.canOpenLocationDetail === true
}

/**
 * The location column.
 *
 * ROW-LEVEL, FAIL-CLOSED (#66 5700319744). The link exists only when the card route is
 * granted AND this row's `canOpenLocationDetail` is true — the Organization owner's
 * answer, computed with the card read's own scope predicate, so a link here is a card
 * that answers 200. A row outside the caller's location scope gets the scope note and
 * no request: the screen neither parses scope nor probes the card to learn it. The flag
 * arrives in the same response as the row, so a row can never be paired with another
 * read's (older, other-company or other-generation) eligibility.
 *
 * With both, it is a real link — an anchor, so Tab reaches it and Enter follows
 * it with no key handler of our own. Without it, a plain scope statement: not a disabled
 * control (which would still be announced as an action the user might earn), and not the
 * raw `locationId` (a UUID is not a label a person can use).
 *
 * The link's accessible name STARTS WITH THE VISIBLE TEXT and then names the work center:
 * WCAG 2.2 SC 2.5.3 wants the visible label inside the name, and "Lokasyonu aç" repeated
 * down a column tells a screen-reader user nothing about which row they are on.
 */
function LocationCell({
  workCenter,
  namespace,
  canOpenLocation,
}: {
  readonly workCenter: WorkCenterSummary
  readonly namespace: string | null
  readonly canOpenLocation: boolean
}) {
  if (!canOpenLocation || !isLocationOpenable(workCenter)) {
    return <span className={styles.scopeNote}>{WORK_CENTER_LOCATION_UNAVAILABLE}</span>
  }
  return (
    <Link
      className={styles.rowLink}
      to={locationDetailPath(workCenter.locationId)}
      aria-label={workCenterLocationLinkLabel(workCenter.code)}
      // Tells the card to come BACK to THIS list rather than to ORG-01. Both screens
      // stand on `organization.locations.read`, so this is a usability decision, not an
      // authority one: whoever can open the card can open ORG-01 too, and would simply
      // land somewhere they did not come from. Namespaced, so a company switch drops it.
      state={workCenterReturnState(namespace)}
    >
      {WORK_CENTER_LOCATION_LINK_TEXT}
    </Link>
  )
}

/** Status is never carried by colour alone (UI_DESIGN_SYSTEM §2, §9). */
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
