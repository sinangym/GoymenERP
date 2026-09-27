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

import { useCallback, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useNavigate } from 'react-router'
import { fetchLocationPage, fetchWorkCenters, PAGE_SIZE_MAX } from './api'
import type { LocationSummary, WorkCenterSummary } from './contract'
import { MasterFormLauncher, MasterRecordForm } from './MasterRecordForm'
import { refreshOrganizationReads } from './refreshOrganizationReads'
import type { ViewOutcome } from '../shell/uiState'
import { locationDetailPath, ORGANIZATION_CAPABILITY, workCenterDetailPath, workCenterListPath } from './capabilities'
import {
  filterWorkCenters, filteredWorkCenterDetailReturn, readWorkCenterLocationFilter,
  WORK_CENTER_LOCATION_TEXT,
} from './workCenterLocationFilter'
import {
  LOCATION_NOT_OPENABLE,
  MASTER_TEXT,
  WORK_CENTER_CARD_TEXT,
  WORK_CENTER_KIND_OPTIONS,
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

  // The create control is its own grant (work-centers.write + the location read its picker lists from).
  const canCreate = hasCapability(shell, ORGANIZATION_CAPABILITY.workCenterCreate)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const refreshAll = useCallback(() => refreshOrganizationReads(queryClient, namespace), [namespace, queryClient])

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
  // ORG-04: the card is its own route capability, published on the same grant as this list.
  const canOpenCard = hasCapability(shell, ORGANIZATION_CAPABILITY.workCenterDetail)
  return (
    <section aria-labelledby="org-wc-heading" className={styles.page}>
      <header className={styles.header}>
        <h1 id="org-wc-heading">{WORK_CENTERS_HEADING}</h1>
        <p className={styles.lead}>{WORK_CENTERS_LEAD}</p>
      </header>

      {canCreate && namespace !== null ? (
        <WorkCenterCreateSection
          namespace={namespace}
          companyId={activeCompanyId ?? ''}
          initialLocationId={filter.kind === 'location' ? filter.locationId : ''}
          onCreated={async (id) => {
            await refreshAll()
            // Save, then reopen: the new work centre's own card, whose way back is this list.
            await navigate(workCenterDetailPath(id), {
              state: filteredWorkCenterDetailReturn(namespace, filter.kind === 'location' ? filter.locationId : null),
            })
          }}
          onReload={refreshAll}
        />
      ) : null}

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
        <WorkCenterTable
          rows={rows}
          namespace={namespace}
          filterLocationId={filter.kind === 'location' ? filter.locationId : null}
          canOpenCard={canOpenCard}
        />
      )}
    </section>
  )
}

function WorkCenterTable({
  rows,
  namespace,
  filterLocationId,
  canOpenCard,
}: {
  readonly rows: readonly WorkCenterSummary[]
  readonly namespace: string | null
  readonly filterLocationId: string | null
  readonly canOpenCard: boolean
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
              <td className={styles.codeCell}>
                {/* ORG-04: the row opens its own card, carrying the same allowlisted return token as the
                    location link, so the card's way back restores this list and its location filter. */}
                {canOpenCard ? (
                  <Link
                    className={styles.rowLink}
                    to={workCenterDetailPath(row.id)}
                    state={filteredWorkCenterDetailReturn(namespace, filterLocationId)}
                    aria-label={`${WORK_CENTER_CARD_TEXT.openCard}: ${row.code}`}
                  >
                    {row.code}
                  </Link>
                ) : (
                  row.code
                )}
              </td>
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

/** The picker never shows a partial location list: past this many rows it says so instead. */
const LOCATION_PICKER_MAX_PAGES = 10

type LocationChoices = { readonly kind: 'ready'; readonly items: readonly LocationSummary[] } | { readonly kind: 'too-many' }

/**
 * Every location the caller may read, for the new work centre's location choice. The location endpoint pages, so this
 * reads page after page at the server's maximum size until the server's own `totalCount` is reached; a failure on
 * any page fails the whole list, and a set too large for one select is refused rather than truncated.
 */
async function fetchLocationChoices(
  companyId: string,
  signal: AbortSignal,
): Promise<ViewOutcome<LocationChoices>> {
  const items: LocationSummary[] = []
  for (let page = 1; page <= LOCATION_PICKER_MAX_PAGES; page += 1) {
    const outcome = await fetchLocationPage(companyId, { page, pageSize: PAGE_SIZE_MAX }, signal)
    if (outcome.type !== 'ok-single') return outcome as ViewOutcome<LocationChoices>
    items.push(...outcome.data.items)
    if (items.length >= outcome.data.totalCount || outcome.data.items.length === 0) {
      return { type: 'ok-single', data: { kind: 'ready', items } }
    }
  }
  return { type: 'ok-single', data: { kind: 'too-many' } }
}

/**
 * A new work centre on one of the caller's locations. The location list is read only when the form opens. The kind is
 * chosen once: it is the Manufacturing planning reference's immutable fact. After the 201 the user is taken to the new
 * record's own card.
 */
function WorkCenterCreateSection({
  namespace,
  companyId,
  initialLocationId,
  onCreated,
  onReload,
}: {
  readonly namespace: string
  readonly companyId: string
  readonly initialLocationId: string
  readonly onCreated: (id: string) => Promise<void>
  readonly onReload: () => Promise<void>
}) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const [open, setOpen] = useState(false)
  const choicesQuery = useQuery({
    queryKey: [namespace, shell.routeEntryGeneration, 'organization-location-choices'],
    enabled: open,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchLocationChoices(companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  if (!open) {
    return <MasterFormLauncher label={MASTER_TEXT.newWorkCenter} saved={null} onOpen={() => setOpen(true)} />
  }

  if (choicesQuery.isPending) {
    // The form opens with the list it chooses from, so a filter's location can be preselected and nothing typed is lost.
    return (
      <p className={styles.lead} role="status">
        {MASTER_TEXT.locationsLoading}
      </p>
    )
  }

  const choices = classifyOutcome(choicesQuery.data ?? { type: 'malformed' }) as RemoteViewState<LocationChoices>
  const ready = choices.kind === 'ready' && choices.data.kind === 'ready' ? choices.data.items : null
  const unavailable =
    choices.kind === 'ready' && choices.data.kind === 'too-many'
        ? MASTER_TEXT.locationsTooMany
      : ready === null
        ? MASTER_TEXT.locationsUnavailable
        : undefined

  return (
    <MasterRecordForm

      heading={MASTER_TEXT.newWorkCenter}
      companyId={companyId}
      dirtyScope="organization.work-center.create"
      initial={{
        code: '',
        name: '',
        choice: ready !== null && ready.some((location) => location.id === initialLocationId) ? initialLocationId : '',
      }}
      choice={{
        label: MASTER_TEXT.location,
        options: (ready ?? []).map((location) => ({
          value: location.id,
          label: `${location.code} — ${location.name}${location.isActive ? '' : ` (${activeStateLabel(false)})`}`,
        })),
        unavailable,
      }}
      note={MASTER_TEXT.kindFixed}
      buildCommand={(values) => ({
        kind: 'create-work-center',
        locationId: values.choice,
        code: values.code,
        name: values.name,
        workCenterKind: values.secondChoice,
        reason: values.reason,
      })}
      secondChoice={{
        label: MASTER_TEXT.kind,
        options: WORK_CENTER_KIND_OPTIONS.map((kind) => ({ value: kind, label: workCenterKindLabel(kind) })),
      }}
      onCreated={async (record) => {
        await onCreated(record.id)
      }}
      onReload={onReload}
      onCancel={() => setOpen(false)}
    />
  )
}
