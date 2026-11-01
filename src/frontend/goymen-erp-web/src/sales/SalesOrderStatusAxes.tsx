/**
 * DEC-SAL-001 / API-08 / UI-15: the order's seven status axes on S-04, as the server answers them
 * (`GET …/drafts/{id}/status`, under the draft read). Read only, mounted after the parent detail was read in the same
 * company, and read afresh on every open (`gcTime` 0). Every axis is shown on its own — the screen derives no summary
 * state, re-judges no tolerance and computes no quantity: the realization band, the warning, "Aktif" and
 * "kapanmaya hazır" are the server's (SAT-D5, SAT-D29…D33). Each state is a word (never a colour alone) and each axis is a
 * named list item. A failed, forbidden or foreign answer shows no axis at all: an unread status is never presented as
 * "not started" or "not complete".
 *
 * The totals (API-03) and risk (API-06) cards of UI-15 are separate components mounted beside this one by the detail page
 * once their routes exist; this card neither reserves nor fakes their numbers.
 */
import { useId, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftStatus } from './api'
import { SALES_CAPABILITY } from './capabilities'
import { APPROVAL_STATE, decodeProblem, type ApprovalState, type DraftDetail } from './contract'
import { formatBusinessDate } from './format'
import {
  INVOICING_GATE,
  OWNERLESS_AXES,
  STATUS_AXIS_STATE,
  type AcceptanceAxisState,
  type LifecycleAxisState,
  type LineRealization,
  type OrderStatus,
  type RealizationAxis,
  type RealizationState,
} from './orderStatus'
import { APPROVAL_STATE_TEXT, ORDER_STATUS_TEXT as T } from './salesText'
import { orderUnitCodes } from './unitNames'
import { UnitName } from './UnitName'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StatusIndicator, type StatusTone } from '../ui/StatusIndicator'
import pages from './SalesPages.module.css'
import styles from './SalesOrderStatusAxes.module.css'

/** The container: gates the read on the bound company and the S-04 detail capability, then reads once per open. */
export function SalesOrderStatusAxes({ detail }: { readonly detail: DraftDetail }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  // API-08 publishes no capability of its own: the route is authorised by the draft read, whose shell capability is the
  // S-04 detail. The API still measures the grant afresh on every call.
  const canRead = namespace !== null && companyId !== null && companyId === detail.draft.companyId
    && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-status', detail.draft.id, detail.draft.version],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftStatus(detail, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  return <OrderStatusCard detail={detail} outcome={query.data} onRetry={() => void query.refetch()} />
}

/**
 * The card for one read outcome (undefined while loading). Exported so every outcome — including a 403 the auth boundary
 * would close the route on — can be rendered and checked on its own.
 */
export function OrderStatusCard({
  detail,
  outcome,
  onRetry,
}: {
  readonly detail: DraftDetail
  readonly outcome: ViewOutcome<OrderStatus> | undefined
  readonly onRetry: () => void
}) {
  const headingId = useId()
  let body
  if (outcome?.type === 'ok-single') {
    body = <StatusBody detail={detail} status={outcome.data} />
  } else {
    const problem = outcome?.type === 'http' ? decodeProblem(outcome.body, outcome.status) : null
    body = (
      <>
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={onRetry}
        />
        {outcome === undefined ? null : (
          <p className={pages.muted} data-testid="sales-status-unreadable">
            {outcome.type === 'http' && outcome.status === 503 ? T.serviceUnavailable : T.unreadable}
          </p>
        )}
        {problem?.correlationId == null ? null : (
          <p className={pages.muted} data-testid="sales-status-support-code">{T.supportCode(problem.correlationId)}</p>
        )}
      </>
    )
  }
  return (
    <section className={pages.section} aria-labelledby={headingId} aria-busy={outcome === undefined} data-testid="sales-status-axes">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={pages.muted}>{T.note}</p>
      {body}
    </section>
  )
}

/* ── Presentation of a decoded status ─────────────────────────────────────── */

const LIFECYCLE_TONE: Record<LifecycleAxisState, StatusTone> = {
  Draft: 'neutral',
  Active: 'info',
  Cancelled: 'neutral',
  Closed: 'neutral',
}
const ACCEPTANCE_TONE: Record<AcceptanceAxisState, StatusTone> = { NotRequired: 'neutral', Pending: 'warning', Accepted: 'success', Rejected: 'neutral' }
const REALIZATION_TONE: Record<RealizationState, StatusTone> = {
  NotStarted: 'neutral',
  Partial: 'info',
  Completed: 'success',
  OutsideTolerance: 'danger',
  ClosedWithRemainder: 'neutral',
  AwaitingApproval: 'warning',
  PendingReapproval: 'warning',
  PolicyUnavailable: 'warning',
  Unreadable: 'neutral',
}

function approvalTone(state: ApprovalState): StatusTone {
  if (state === APPROVAL_STATE.approved) return 'success'
  if (state === APPROVAL_STATE.pendingApproval) return 'warning'
  if (state === APPROVAL_STATE.rejected) return 'danger'
  return 'neutral'
}

/** A mark beside "Tamamlandı": a glyph for the eye and a word for everyone (the glyph is hidden from assistive tech). */
function Mark({ text, testId }: { readonly text: string; readonly testId: string }) {
  return (
    <span className={styles.mark} data-testid={testId}>
      <span aria-hidden="true">⚠</span>
      {text}
    </span>
  )
}

/** The state word and its SAT-D29/D30 marks — one function for the axis and for a line. */
function RealizationStateText({
  state,
  warning,
  approvedOverDelivery,
  testId,
}: {
  readonly state: RealizationState
  readonly warning: boolean
  readonly approvedOverDelivery: boolean
  readonly testId: string
}) {
  return (
    <>
      <StatusIndicator tone={warning ? 'warning' : REALIZATION_TONE[state]}>
        <span data-testid={`${testId}-text`}>{T.realization[state]}</span>
      </StatusIndicator>
      {/* The spaces keep the words apart in the accessible name ("Tamamlandı Tolerans uyarısı"), not only on screen. */}
      {warning ? <>{' '}<Mark text={T.warningMark} testId={`${testId}-warning`} /></> : null}
      {approvedOverDelivery ? <>{' '}<Mark text={T.approvedOverDeliveryMark} testId={`${testId}-approved-over`} /></> : null}
    </>
  )
}

/** One named axis: its label and state, as a list item whose accessible name is "label state". */
function Axis({
  axisKey,
  label,
  state,
  children,
  note,
}: {
  readonly axisKey: string
  readonly label: string
  readonly state: string
  readonly children: React.ReactNode
  readonly note?: React.ReactNode
}) {
  const labelId = useId()
  const stateId = useId()
  return (
    <li className={styles.axis} aria-labelledby={`${labelId} ${stateId}`} data-testid={`sales-status-axis-${axisKey}`} data-state={state}>
      <span id={labelId} className={styles.axisLabel}>{label}</span>
      <span id={stateId} className={styles.axisState}>{children}</span>
      {note === undefined || note === null ? null : <p className={styles.axisNote}>{note}</p>}
    </li>
  )
}

function StatusBody({ detail, status }: { readonly detail: DraftDetail; readonly status: OrderStatus }) {
  const ownerless = OWNERLESS_AXES.filter((axis) => status[axis] === STATUS_AXIS_STATE.noSource)
  const acceptedRevision = status.customerAcceptance.acceptedRevision
  return (
    <>
      {/* role="list": `list-style: none` drops the list role in Safari/VoiceOver. */}
      <ul className={styles.axes} role="list" aria-label={T.axesLabel} data-testid="sales-status-axes-list">
        <Axis axisKey="lifecycle" label={T.axis.lifecycle} state={status.lifecycle}>
          <StatusIndicator tone={LIFECYCLE_TONE[status.lifecycle]}>{T.lifecycle[status.lifecycle]}</StatusIndicator>
        </Axis>
        <Axis axisKey="internal-approval" label={T.axis.internalApproval} state={status.internalApproval}>
          <StatusIndicator tone={approvalTone(status.internalApproval)}>{APPROVAL_STATE_TEXT[status.internalApproval]}</StatusIndicator>
        </Axis>
        <Axis
          axisKey="customer-acceptance"
          label={T.axis.customerAcceptance}
          state={status.customerAcceptance.state}
          note={acceptedRevision === null ? null : T.acceptedRevision(acceptedRevision)}
        >
          <StatusIndicator tone={ACCEPTANCE_TONE[status.customerAcceptance.state]}>
            {T.acceptance[status.customerAcceptance.state]}
          </StatusIndicator>
        </Axis>
        <OwnerlessAxisItem axis="source" status={status} />
        <Axis axisKey="realization" label={T.axis.realization} state={status.realization.state}>
          <RealizationStateText
            state={status.realization.state}
            warning={status.realization.warning}
            approvedOverDelivery={false}
            testId="sales-status-realization"
          />
        </Axis>
        <InvoicingAxisItem status={status} />
        <OwnerlessAxisItem axis="financialClosure" status={status} />
      </ul>

      <dl className={styles.ready} data-testid="sales-status-ready">
        <dt>{T.readyToClose.label}</dt>
        <dd data-testid="sales-status-ready-value">{status.readyToClose ? T.readyToClose.yes : T.readyToClose.no}</dd>
        <dd className={pages.muted} data-testid="sales-status-ready-note">
          {status.readyToClose
            ? T.readyToClose.yesNote
            : ownerless.length > 0
              ? T.readyToClose.noSourceNote(ownerless.map((axis) => T.axis[axis]).join(', '))
              : T.readyToClose.openNote}
        </dd>
      </dl>

      <RealizationDetail detail={detail} realization={status.realization} revision={status.revision} />
    </>
  )
}

function OwnerlessAxisItem({ axis, status }: { readonly axis: (typeof OWNERLESS_AXES)[number]; readonly status: OrderStatus }) {
  const state = status[axis]
  return (
    <Axis
      axisKey={axis === 'financialClosure' ? 'financial-closure' : axis}
      label={T.axis[axis]}
      state={state}
      note={state === STATUS_AXIS_STATE.noSource ? T.ownerlessNote[axis] : null}
    >
      <StatusIndicator tone="neutral">{T.ownerless[state]}</StatusIndicator>
    </Axis>
  )
}

/**
 * SAT-D103 (EK13): the invoicing axis has no owner yet (`NoSource`), but Sales owns its gate — while a newer revision waits
 * for re-approval the order's invoicing waits (shipping goes on under the last approved revision); after a rejection the
 * last approved revision's prices apply.
 */
function InvoicingAxisItem({ status }: { readonly status: OrderStatus }) {
  const state = status.invoicing
  const { gate, priceBasisRevision } = status.invoicingGate
  const tone: StatusTone = gate === INVOICING_GATE.awaitingReapproval ? 'warning' : 'neutral'
  return (
    <Axis
      axisKey="invoicing"
      label={T.axis.invoicing}
      state={state}
      note={
        <>
          {state === STATUS_AXIS_STATE.noSource ? <>{T.ownerlessNote.invoicing} </> : null}
          <span data-testid="sales-status-invoicing-gate-note">{T.invoicingGateNote[gate](priceBasisRevision ?? '')}</span>
        </>
      }
    >
      <StatusIndicator tone="neutral">{T.ownerless[state]}</StatusIndicator>{' '}
      <span data-testid="sales-status-invoicing-gate" data-gate={gate}>
        <StatusIndicator tone={tone}>{T.invoicingGate[gate]}</StatusIndicator>
      </span>
    </Axis>
  )
}

function RealizationDetail({
  detail,
  realization,
  revision,
}: {
  readonly detail: DraftDetail
  readonly realization: RealizationAxis
  readonly revision: string
}) {
  const headingId = useId()
  const unitCodes = useMemo(() => orderUnitCodes(detail), [detail])
  const products = useMemo(
    () => new Map(detail.lines.map((line) => [line.lineId.toLowerCase(), `${line.productCode} — ${line.productName}`])),
    [detail],
  )
  const { state, tolerance, basisRevision, lines } = realization
  const notes: { readonly key: string; readonly text: string }[] = []
  if (state === STATUS_AXIS_STATE.pendingReapproval) notes.push({ key: 'pending-reapproval', text: T.pendingReapproval })
  else if (basisRevision !== null && basisRevision !== revision) notes.push({ key: 'basis-earlier', text: T.basisEarlier(basisRevision, revision) })
  if (state === STATUS_AXIS_STATE.unreadable) notes.push({ key: 'unreadable', text: T.realizationUnreadable })
  if (state === STATUS_AXIS_STATE.policyUnavailable) {
    notes.push({ key: 'policy-unavailable', text: T.policyUnavailable(formatBusinessDate(detail.draft.businessDate)) })
  }
  if (lines.some((line) => line.warning)) notes.push({ key: 'warning', text: T.warningNote })
  if (lines.some((line) => line.state === STATUS_AXIS_STATE.awaitingApproval)) notes.push({ key: 'awaiting-approval', text: T.awaitingApprovalNote })
  if (lines.some((line) => line.state === STATUS_AXIS_STATE.outsideTolerance)) notes.push({ key: 'outside-tolerance', text: T.outsideToleranceNote })
  const measured = state !== STATUS_AXIS_STATE.unreadable && state !== STATUS_AXIS_STATE.pendingReapproval
  return (
    <section className={styles.realization} aria-labelledby={headingId} data-testid="sales-status-realization-detail">
      <h3 id={headingId}>{T.realizationHeading}</h3>
      <dl className={pages.facts}>
        <div>
          <dt>{T.basis}</dt>
          <dd data-testid="sales-status-basis">{basisRevision ?? T.notApplicable}</dd>
        </div>
        <div>
          <dt>{T.tolerance}</dt>
          <dd data-testid="sales-status-tolerance">
            {tolerance !== null
              ? T.toleranceBand(tolerance.warnPercent, tolerance.approvalPercent, tolerance.blockPercent)
              : state === STATUS_AXIS_STATE.policyUnavailable
                ? T.realization.PolicyUnavailable
                : measured ? T.toleranceNotNeeded : T.notMeasured}
          </dd>
        </div>
        {tolerance === null ? null : (
          <>
            <div>
              <dt>{T.toleranceProfile}</dt>
              <dd data-testid="sales-status-tolerance-version">{tolerance.profileVersion}</dd>
            </div>
            <div>
              <dt>{T.toleranceWindow}</dt>
              <dd data-testid="sales-status-tolerance-window">
                {formatBusinessDate(tolerance.effectiveFrom)} – {tolerance.effectiveThrough === null
                  ? T.toleranceOpenEnded
                  : formatBusinessDate(tolerance.effectiveThrough)}
              </dd>
            </div>
            <div>
              <dt>{T.toleranceEvaluatedOn}</dt>
              <dd data-testid="sales-status-tolerance-evaluated-on">{formatBusinessDate(tolerance.evaluatedOn)}</dd>
            </div>
          </>
        )}
      </dl>
      {notes.map((note) => (
        <p key={note.key} className={pages.muted} data-testid={`sales-status-note-${note.key}`}>{note.text}</p>
      ))}
      {lines.length === 0 ? (
        <p className={pages.muted} data-testid="sales-status-empty">{T.empty}</p>
      ) : (
        <>
          <div className={pages.tableWrap}>
            <table className={pages.table} data-testid="sales-status-lines">
              <caption className={pages.caption}>{T.linesCaption}</caption>
              <thead><tr>
                <th scope="col">{T.column.lineNo}</th>
                <th scope="col">{T.column.product}</th>
                <th scope="col">{T.column.ordered}</th>
                <th scope="col">{T.column.shipped}</th>
                <th scope="col">{T.column.delivered}</th>
                <th scope="col">{T.column.deviation}</th>
                <th scope="col">{T.column.band}</th>
                <th scope="col">{T.column.state}</th>
                <th scope="col">{T.column.closedRemainder}</th>
                <th scope="col">{T.column.approvedReturned}</th>
                <th scope="col">{T.column.unit}</th>
              </tr></thead>
              <tbody>{lines.map((line) => (
                <LineRow key={line.lineId} line={line} product={products.get(line.lineId.toLowerCase())} unitCodes={unitCodes} />
              ))}</tbody>
            </table>
          </div>
          <p className={pages.muted}>{T.returnsNote}</p>
        </>
      )}
    </section>
  )
}

function deviationText(line: LineRealization): string {
  if (line.direction === null || line.deviation === null) return T.notMeasured
  if (line.direction === 'None') return T.deviation.None
  return line.direction === 'Under' ? T.deviation.Under(line.deviation) : T.deviation.Over(line.deviation)
}

function LineRow({
  line,
  product,
  unitCodes,
}: {
  readonly line: LineRealization
  readonly product: string | undefined
  readonly unitCodes: ReadonlyMap<string, string>
}) {
  const id = `sales-status-line-${line.lineNo}`
  // A line with no approved basis was not measured and no owner was asked; an unread owner is "not read" — never zero.
  const missing = line.state === STATUS_AXIS_STATE.pendingReapproval ? T.notMeasured : T.notRead
  return (
    <tr data-testid={id} data-state={line.state}>
      <th scope="row">{line.lineNo}</th>
      <td>{product}</td>
      <td className={pages.numericValue} data-testid={`${id}-ordered`}>{line.ordered}</td>
      <td className={pages.numericValue} data-testid={`${id}-shipped`}>{line.shipped ?? missing}</td>
      <td className={pages.numericValue} data-testid={`${id}-delivered`}>{line.delivered ?? missing}</td>
      <td data-testid={`${id}-deviation`}>{deviationText(line)}</td>
      <td data-testid={`${id}-band`}>{line.band === null ? T.notMeasured : T.band[line.band]}</td>
      <td>
        <span className={styles.stateCell}>
          <RealizationStateText state={line.state} warning={line.warning} approvedOverDelivery={line.approvedOverDelivery} testId={`${id}-state`} />
        </span>
      </td>
      <td className={pages.numericValue} data-testid={`${id}-closed-remainder`}>{line.closedRemainder ?? T.notApplicable}</td>
      <td className={pages.numericValue} data-testid={`${id}-returned`}>{line.approvedReturned ?? missing}</td>
      <td data-testid={`${id}-unit`}><UnitName id={line.orderUnitId} unitCodes={unitCodes} /></td>
    </tr>
  )
}
