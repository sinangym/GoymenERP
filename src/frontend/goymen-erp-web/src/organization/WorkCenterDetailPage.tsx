/**
 * ORG-04 — one work centre's read-only card (W1-ORG / AV-01, Business #66 5702892195).
 *
 * The slice is list → card → (when the server says so) location card → back, and NOTHING that writes:
 * `GET /organization/work-centers/{id}` is the only request this screen makes. It shows the Organization record's
 * own facts — code, name, canonical kind, active state — and keeps its `version` for a later stale check without
 * showing it as a figure. Deliberately absent, because the record has no source for them: capacity, shift, load,
 * ownership, production facts and the location's NAME.
 *
 * THE RULES THAT SHAPE THIS FILE (the location card's, ORG-02, and the #434 return pattern)
 *
 *  - Remounted per committed context, route entry and id. A detail screen is the dangerous one: a stale render
 *    would put one company's work centre under another company's label. The read's own query key carries the
 *    namespace, and an answer that is not the requested record is malformed, never "a card".
 *  - The location link renders the record's `canOpenLocationDetail`, which the server computes with the location
 *    card's own read predicate. The screen never derives it and never probes: when it is false there is no link
 *    and no location request, only a plain statement of scope.
 *  - The back link returns to the ORG-03 origin the row recorded — the whole list or its location filter — and
 *    only under the same committed context. Router state is caller-controlled, so only the list's exact tokens
 *    are honoured, and a location filter only when it is the loaded record's own location (the location card's
 *    rule for the same token); anything else returns to the whole list.
 *  - A passive work centre stays readable: passive is a state, not a delete.
 */

import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useParams } from 'react-router'
import { fetchWorkCenter } from './api'
import type { WorkCenterDetail } from './contract'
import { locationDetailPath, ORGANIZATION_CAPABILITY } from './capabilities'
import {
  workCenterCardBackTarget,
  workCenterCardOrigin,
  workCenterDetailReturn,
  type WorkCenterCardOrigin,
} from './detailReturn'
import {
  LOCATION_NOT_OPENABLE,
  WORK_CENTER_CARD_TEXT,
  activeStateLabel,
  workCenterKindLabel,
} from './organizationText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './WorkCenterDetailPage.module.css'

/** Only an id the Organization routes could have emitted is ever sent; anything else is not a record here. */
const WORK_CENTER_ID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

export function WorkCenterDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { workCenterId } = useParams()
  return (
    <WorkCenterCardScreen
      key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}::${workCenterId ?? ''}`}
      namespace={namespace}
    />
  )
}

function WorkCenterCardScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const routerLocation = useLocation()
  const { workCenterId = '' } = useParams()
  const entryGeneration = shell.routeEntryGeneration
  const activeCompanyId = shell.activeContext?.companyId ?? null
  const scopeMode = shell.activeContext?.scopeMode ?? 'single'
  const singleCompanyContext = scopeMode === 'single' && activeCompanyId !== null
  const canRead = hasCapability(shell, ORGANIZATION_CAPABILITY.workCenterDetail)
  const idIsWellFormed = WORK_CENTER_ID.test(workCenterId)
  const origin = workCenterCardOrigin(routerLocation.state, namespace)

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'organization-work-center', workCenterId],
    enabled: namespace !== null && singleCompanyContext && canRead && idIsWellFormed,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchWorkCenter(activeCompanyId ?? '', workCenterId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  if (!singleCompanyContext) {
    return (
      <Frame origin={origin}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={WORK_CENTER_CARD_TEXT.contextSubject} />
      </Frame>
    )
  }

  if (namespace === null || !canRead || !idIsWellFormed) {
    // No request is made for an id this surface could not have produced: it is simply not a record here.
    return (
      <Frame origin={origin}>
        <StateBlock state={{ kind: 'not-found' }} subject={WORK_CENTER_CARD_TEXT.subject} />
      </Frame>
    )
  }

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<WorkCenterDetail>

  if (state.kind !== 'ready') {
    return (
      <Frame origin={origin}>
        <StateBlock state={state} subject={WORK_CENTER_CARD_TEXT.subject} onRetry={() => void query.refetch()} />
      </Frame>
    )
  }

  return (
    <Frame origin={origin} cardLocationId={state.data.locationId}>
      <WorkCenterCard workCenter={state.data} namespace={namespace} origin={origin} />
    </Frame>
  )
}

function Frame({
  origin,
  cardLocationId,
  children,
}: {
  readonly origin: WorkCenterCardOrigin | undefined
  /** The loaded record's location; absent until the card is ready, so the back link is then the whole list. */
  readonly cardLocationId?: string
  readonly children: React.ReactNode
}) {
  const shell = useShellState()
  const back = workCenterCardBackTarget(origin, cardLocationId)
  // The list is its own grant; without it the way back is stated, not offered as a link that would only 404.
  const canGoBack = hasCapability(shell, ORGANIZATION_CAPABILITY.workCentersList)
  return (
    <section aria-labelledby="org-wc-card-heading" className={styles.page}>
      <nav aria-label="Geri" className={styles.breadcrumb}>
        {canGoBack ? <Link to={back.path}>← {back.label}</Link> : <span>← {back.label}</span>}
      </nav>
      <h1 id="org-wc-card-heading">{WORK_CENTER_CARD_TEXT.heading}</h1>
      <p className={styles.lead}>{WORK_CENTER_CARD_TEXT.lead}</p>
      {children}
    </section>
  )
}

function WorkCenterCard({
  workCenter,
  namespace,
  origin,
}: {
  readonly workCenter: WorkCenterDetail
  readonly namespace: string | null
  readonly origin: WorkCenterCardOrigin | undefined
}) {
  return (
    <dl className={styles.card} data-work-center={workCenter.id}>
      <div className={styles.field}>
        <dt>{WORK_CENTER_CARD_TEXT.code}</dt>
        <dd className={styles.code}>{workCenter.code}</dd>
      </div>
      <div className={styles.field}>
        <dt>{WORK_CENTER_CARD_TEXT.name}</dt>
        <dd>{workCenter.name}</dd>
      </div>
      <div className={styles.field}>
        <dt>{WORK_CENTER_CARD_TEXT.kind}</dt>
        <dd>{workCenterKindLabel(workCenter.kind)}</dd>
      </div>
      <div className={styles.field}>
        <dt>{WORK_CENTER_CARD_TEXT.status}</dt>
        <dd>
          {/* Status is never colour alone: the word is the signal and the colour is redundant. */}
          <span
            className={workCenter.isActive ? styles.badgeActive : styles.badgePassive}
            data-state={workCenter.isActive ? 'active' : 'passive'}
          >
            {activeStateLabel(workCenter.isActive)}
          </span>
        </dd>
      </div>
      <div className={styles.field}>
        <dt>{WORK_CENTER_CARD_TEXT.location}</dt>
        <dd>
          <LocationLink workCenter={workCenter} namespace={namespace} origin={origin} />
        </dd>
      </div>
    </dl>
  )
}

/**
 * The server's `canOpenLocationDetail`, rendered — never derived, as on the ORG-03 row. The location card is also
 * its own route capability, so both must hold; either missing leaves a plain statement of scope and no request.
 *
 * The link carries the ORG-03 return token onward, so the location card's own back link (#434) returns to the
 * work-centre list the user came from — narrowed to this location only when that is the filter they came from.
 */
function LocationLink({
  workCenter,
  namespace,
  origin,
}: {
  readonly workCenter: WorkCenterDetail
  readonly namespace: string | null
  readonly origin: WorkCenterCardOrigin | undefined
}) {
  const shell = useShellState()
  if (!workCenter.canOpenLocationDetail || !hasCapability(shell, ORGANIZATION_CAPABILITY.locationDetail)) {
    return <span className={styles.locationClosed}>{LOCATION_NOT_OPENABLE}</span>
  }
  const onward =
    origin !== undefined && (origin.locationId === undefined || origin.locationId === workCenter.locationId)
      ? origin
      : workCenterDetailReturn(namespace)
  return (
    <Link className={styles.link} to={locationDetailPath(workCenter.locationId)} state={onward}>
      {WORK_CENTER_CARD_TEXT.openLocation}
    </Link>
  )
}
