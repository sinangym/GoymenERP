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
   * Whether a row may offer its location.
   *
   * This is the ORG-02 ROUTE capability, not a locally invented one — the link goes to
   * that route, so the question "may this user open it" has exactly one right answer and
   * the shell already holds it. Without the capability NO REQUEST IS MADE and no link is
   * rendered: the `locationId` on a work-center row is an identifier the caller happens
   * to hold, never an entitlement to read that location, and firing a call we have been
   * told is unavailable would spend a refusal to learn what we already know.
   *
   * THE DECISION IS AT CAPABILITY LEVEL, AND THAT IS THE SPEC, NOT A SHORTCUT. The
   * Business package states it as "lokasyon linki yalnız kendi mevcut izni/capability'siyle"
   * (#66 Org row). So when `work-centers.read` and `locations.read` are granted at
   * DIFFERENT SCOPES — company-wide work centers, one site's locations — every row still
   * offers the link, and following it for a location outside the locations scope answers
   * the server's non-disclosing 404: no row data, no scope disclosure, no write. Hiding
   * the link per row would need a per-row scope signal no merged endpoint publishes, and
   * inventing a response field or a second read for it is outside this slice; it is
   * recorded as a separate slice candidate rather than guessed at here.
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
        <p className={styles.lead}>{workCenterLeadText(canOpenLocation)}</p>
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

/**
 * The location column.
 *
 * With the capability it is a real link — an anchor, so Tab reaches it and Enter follows
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
  if (!canOpenLocation) {
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
