/**
 * L-02-DETAIL — one loading instruction as `GET /api/v1/logistics/loading-instructions/{id}` describes
 * it (LOG-4a, #350): the header facts, every line's §7.4 shares as exact text (instructed → loaded →
 * shipped → delivered, plus the derived remainders) and the recorded shares in recording order.
 *
 * Commands on the detail, each one idempotent attempt (one key per snapshot, an unknown outcome
 * keeps its key, a retry resends the same snapshot, a replay is shown as a replay):
 *  - "Yayımla" (Draft → Issued): offered to a holder of the create capability; the server measures
 *    `logistics.loading-instructions.create` afresh (#276);
 *  - the record-load form on an Issued instruction's unblocked lines: shown to every holder of the
 *    detail (ASSUMPTION-LOG4B-2 — no shell control exists for it in 4b); the server measures
 *    `logistics.loads.record` and answers a code-less 403 to anyone else, which is shown as a refusal.
 *    One `loadId` is minted per attempt snapshot and travels with it, so a retry never records a
 *    second share.
 * After a recorded transition, and after a refusal that says the record moved (a stale version, a
 * state, the §13 cap, a block, a duplicate share), the detail is re-read — the answer is the server's.
 *
 * LOG-5 adds independently capability-gated definite shipment and delivery; load remains non-physical.
 */

import { useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { fetchInstructionDetail, issueInstruction, recordLoad } from './api'
import {
  INSTRUCTION_STATUS,
  MEASUREMENT,
  parseQuantityInput,
  type InstructionDetail,
  type InstructionLine,
  type InstructionMutation,
  type IssueRequest,
  type Measurement,
  type QuantityInputError,
  type RecordLoadRequest,
} from './contract'
import { formatBusinessDate, formatInstant } from './format'
import {
  abandon,
  applyOutcome,
  attemptBinding,
  beginSend,
  decideSubmit,
  generateAttemptKey,
  generateClientId,
  isOutcomeStillUnknown,
  liveSnapshot,
  type AttemptSnapshot,
  type AttemptState,
} from './instructionAttempt'
import { commandNotice, refusalMovesRecord } from './instructionAttemptNotice'
import { recallInstructionAttempt, rememberInstructionAttempt } from './pendingInstructionAttempts'
import { LOGISTICS_CAPABILITY, LOGISTICS_ROUTE } from './capabilities'
import { INSTRUCTION_DETAIL_TEXT as T, INSTRUCTION_CREATE_TEXT, INSTRUCTION_STATUS_TEXT, MEASUREMENT_TEXT, SHARE_KIND_TEXT } from './logisticsText'
import { classifyOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './LogisticsPages.module.css'
import { RealizationCommands } from './RealizationCommands'

export function InstructionDetailPage() {
  const shell = useShellState()
  const { instructionId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()

  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'logistics-instruction', instructionId ?? '']
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && companyId !== null && (instructionId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchInstructionDetail(instructionId as string, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const backLink = hasCapability(shell, LOGISTICS_CAPABILITY.instructionsList) ? (
    <Link className={styles.backLink} to={LOGISTICS_ROUTE.list}>
      ← {T.backToList}
    </Link>
  ) : null

  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single') {
    return (
      <section className={styles.page} aria-label={T.heading}>
        {backLink}
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject="yükleme talimatı"
          onRetry={() => void query.refetch()}
        />
      </section>
    )
  }
  const reread = () => void queryClient.invalidateQueries({ queryKey })
  const detail = outcome.data
  return (
    <InstructionView
      detail={detail}
      backLink={backLink}
      commands={
        companyId === null ? null : (
          <>
            {/* Keyed by namespace + record only (the Sales detail's rule): a re-read after a recorded command must not remount the
                section, or the notice of the command that caused the version step would vanish with it. */}
            <IssueCommand key={`issue:${namespace ?? ''}:${detail.instruction.id}`} shell={shell} detail={detail} companyId={companyId} onRecorded={reread} />
            <LoadForm key={`load:${namespace ?? ''}:${detail.instruction.id}`} shell={shell} detail={detail} companyId={companyId} onRecorded={reread} />
            <RealizationCommands key={`realization:${namespace ?? ''}:${shell.routeEntryGeneration}:${detail.instruction.id}`}
              shell={shell} detail={detail} companyId={companyId} onRecorded={reread} />
          </>
        )
      }
    />
  )
}

export function InstructionView({
  detail,
  backLink,
  commands = null,
}: {
  readonly detail: InstructionDetail
  readonly backLink: React.ReactNode
  readonly commands?: React.ReactNode
}) {
  const { instruction, lines, shares } = detail
  const lineNo = (lineId: string) => lines.find((line) => line.lineId === lineId)?.lineNo ?? '?'
  return (
    <article className={styles.page} aria-labelledby="logistics-detail-heading">
      {backLink}
      <header className={styles.header}>
        <div>
          <h1 id="logistics-detail-heading" className={styles.title}>
            {T.heading}
          </h1>
          <p className={`${styles.muted} ${styles.identifier}`} data-testid="logistics-instruction-id">
            {instruction.id}
          </p>
          <p className={styles.muted}>{T.referenceNote}</p>
        </div>
        <span className={styles.badge} data-testid="logistics-instruction-status">
          {INSTRUCTION_STATUS_TEXT[instruction.status]}
        </span>
      </header>

      <dl className={styles.facts}>
        <div>
          <dt>{T.status}</dt>
          <dd>{INSTRUCTION_STATUS_TEXT[instruction.status]}</dd>
        </div>
        <div>
          <dt>{T.version}</dt>
          <dd data-testid="logistics-instruction-version">{instruction.version}</dd>
        </div>
        <div>
          <dt>{T.effectiveOn}</dt>
          <dd>{formatBusinessDate(instruction.effectiveOn)}</dd>
        </div>
        <div>
          <dt>{T.createdAt}</dt>
          <dd>{formatInstant(instruction.createdAtUtc)}</dd>
        </div>
        <div>
          <dt>{T.updatedAt}</dt>
          <dd>{formatInstant(instruction.updatedAtUtc)}</dd>
        </div>
        <div>
          <dt>{T.party}</dt>
          <dd className={styles.identifier}>{instruction.customerPartyId}</dd>
        </div>
        <div>
          <dt>{T.account}</dt>
          <dd className={styles.identifier}>{instruction.companyAccountId}</dd>
        </div>
      </dl>

      <section className={styles.section} aria-labelledby="logistics-lines-heading">
        <h2 id="logistics-lines-heading">{T.linesHeading}</h2>
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <thead>
              <tr>
                <th scope="col">{T.lineNo}</th>
                <th scope="col">{T.sourceOrder}</th>
                <th scope="col">{T.product}</th>
                <th scope="col">{T.unit}</th>
                <th scope="col" className={styles.numeric}>{T.instructed}</th>
                <th scope="col" className={styles.numeric}>{T.loaded}</th>
                <th scope="col" className={styles.numeric}>{T.shipped}</th>
                <th scope="col" className={styles.numeric}>{T.delivered}</th>
                <th scope="col" className={styles.numeric}>{T.remainingToLoad}</th>
                <th scope="col" className={styles.numeric}>{T.loadedNotShipped}</th>
                <th scope="col" className={styles.numeric}>{T.shippedNotDelivered}</th>
              </tr>
            </thead>
            <tbody>
              {lines.map((line) => (
                <tr key={line.lineId} data-testid={`logistics-line-${line.lineNo}`}>
                  <th scope="row">
                    {line.lineNo}
                    {line.isBlocked ? (
                      <>
                        {' '}
                        <span className={`${styles.badge} ${styles.blocked}`} data-testid={`logistics-line-${line.lineNo}-blocked`}>
                          {T.blocked}
                        </span>
                      </>
                    ) : null}
                  </th>
                  <td className={styles.identifier}>
                    {line.sourceOrderId} / {line.sourceOrderLineId} ({T.revision} {line.sourceOrderRevision})
                  </td>
                  <td className={styles.identifier}>{line.productId}</td>
                  <td className={styles.identifier}>{line.unitId}</td>
                  <td className={styles.numeric} data-testid={`logistics-line-${line.lineNo}-instructed`}>{line.instructed}</td>
                  <td className={styles.numeric} data-testid={`logistics-line-${line.lineNo}-loaded`}>{line.loaded}</td>
                  <td className={styles.numeric}>{line.shipped}</td>
                  <td className={styles.numeric}>{line.delivered}</td>
                  <td className={styles.numeric} data-testid={`logistics-line-${line.lineNo}-remaining`}>{line.remainingToLoad}</td>
                  <td className={styles.numeric} data-testid={`logistics-line-${line.lineNo}-loaded-not-shipped`}>{line.loadedNotShipped}</td>
                  <td className={styles.numeric} data-testid={`logistics-line-${line.lineNo}-shipped-not-delivered`}>{line.shippedNotDelivered}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className={styles.section} aria-labelledby="logistics-shares-heading">
        <h2 id="logistics-shares-heading">{T.sharesHeading}</h2>
        {shares.length === 0 ? (
          <p className={styles.muted} data-testid="logistics-shares-empty">
            {T.noShares}
          </p>
        ) : (
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th scope="col">{T.shareKind}</th>
                  <th scope="col">{T.shareLine}</th>
                  <th scope="col" className={styles.numeric}>{T.shareQuantity}</th>
                  <th scope="col">{T.shareMeasurement}</th>
                  <th scope="col">{T.shareRecordedAt}</th>
                </tr>
              </thead>
              <tbody>
                {shares.map((share) => (
                  <tr key={share.shareId} data-testid={`logistics-share-${share.shareId}`}>
                    <td>{SHARE_KIND_TEXT[share.kind]}</td>
                    <td>{lineNo(share.lineId)}</td>
                    <td className={styles.numeric}>{share.quantity}</td>
                    <td>{share.measurement === null ? T.unknown : MEASUREMENT_TEXT[share.measurement]}</td>
                    <td>{formatInstant(share.recordedAtUtc)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {commands}
    </article>
  )
}

/* ── Commands ─────────────────────────────────────────────────────────────── */

const issueScope = (instructionId: string) => `logistics.instruction.issue:${instructionId}`
const loadScope = (instructionId: string, lineId: string) => `logistics.instruction.load:${instructionId}:${lineId}`
const loadPendingScope = (instructionId: string) => `logistics.instruction.load:${instructionId}`

function NoticeBlock({
  attempt,
  testId,
  onRetry,
  onAbandon,
}: {
  readonly attempt: AttemptState<unknown, InstructionMutation>
  readonly testId: string
  readonly onRetry: () => void
  readonly onAbandon: () => void
}) {
  const notice = commandNotice(attempt)
  if (notice === null) return null
  return (
    <div className={styles.notice} data-tone={notice.tone} role={notice.live === 'assertive' ? 'alert' : 'status'} aria-live={notice.live}>
      <p data-testid={testId}>{notice.message}</p>
      {notice.actions.length > 0 ? (
        <div className={styles.actions}>
          <button type="button" className={styles.secondaryButton} onClick={onRetry}>
            {T.command.retry}
          </button>
          <button type="button" className={styles.linkButton} onClick={onAbandon}>
            {T.command.abandon}
          </button>
        </div>
      ) : null}
    </div>
  )
}

function IssueCommand({
  shell,
  detail,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: InstructionDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const { instruction } = detail
  const canIssue = hasCapability(shell, LOGISTICS_CAPABILITY.instructionsCreate)
  const [initial] = useState(() => recallInstructionAttempt<IssueRequest, null>(attemptBinding(shell, issueScope(instruction.id))))
  const [attempt, setAttempt] = useState<AttemptState<IssueRequest, InstructionMutation>>(() => initial?.attempt ?? { phase: 'idle' })
  const attemptRef = useRef(attempt)
  attemptRef.current = attempt

  const offer = canIssue && instruction.status === INSTRUCTION_STATUS.draft
  if (!offer && attempt.phase === 'idle') return null

  async function send(snapshot: AttemptSnapshot<IssueRequest>) {
    const previous = attemptRef.current
    const inFlight = beginSend<IssueRequest, InstructionMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    rememberInstructionAttempt(snapshot.binding, inFlight, null)
    const outcome = await issueInstruction(snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      instructionId: instruction.id,
    })
    const next = applyOutcome(inFlight, snapshot, attemptBinding(shell, issueScope(instruction.id)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    rememberInstructionAttempt(snapshot.binding, next, null)
    if (next.phase === 'completed' || refusalMovesRecord(next)) onRecorded()
  }

  function start() {
    const binding = attemptBinding(shell, issueScope(instruction.id))
    if (binding === null) return
    const decision = decideSubmit(
      attemptRef.current,
      { path: `${instruction.id}/issue`, binding, companyId, payload: { expectedVersion: instruction.version } },
      generateAttemptKey,
    )
    if (decision.type !== 'send') return
    void send(decision.snapshot)
  }

  function retry() {
    const live = liveSnapshot(attemptRef.current)
    if (live === null || attemptRef.current.phase === 'submitting') return
    void send(live)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase !== 'idle') rememberInstructionAttempt(next.snapshot.binding, next, null)
  }

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  return (
    <section className={styles.section} aria-labelledby="logistics-issue-heading" data-testid="logistics-issue">
      <h2 id="logistics-issue-heading">{T.issue.heading}</h2>
      <p className={styles.muted}>{T.issue.note}</p>
      <NoticeBlock attempt={attempt} testId="logistics-issue-notice" onRetry={retry} onAbandon={leave} />
      {offer ? (
        <div className={styles.actions}>
          <button type="button" className={styles.primaryButton} disabled={frozen} onClick={start} data-testid="logistics-issue-button">
            {T.issue.issue}
          </button>
        </div>
      ) : null}
    </section>
  )
}

type LoadPayload = RecordLoadRequest

interface LoadFormSnapshot {
  readonly lineId: string
  readonly quantityText: string
  readonly measurement: Measurement
}

function LoadForm({
  shell,
  detail,
  companyId,
  onRecorded,
}: {
  readonly shell: ShellState
  readonly detail: InstructionDetail
  readonly companyId: string
  readonly onRecorded: () => void
}) {
  const { instruction, lines } = detail
  const openLines = lines.filter((line) => !line.isBlocked)
  const pendingSlot = attemptBinding(shell, loadPendingScope(instruction.id))
  const [initial] = useState(() => recallInstructionAttempt<LoadPayload, LoadFormSnapshot>(pendingSlot))
  const [lineId, setLineId] = useState<string>(() => initial?.values.lineId ?? openLines[0]?.lineId ?? '')
  const [quantityText, setQuantityText] = useState(() => initial?.values.quantityText ?? '')
  const [measurement, setMeasurement] = useState<Measurement>(() => initial?.values.measurement ?? MEASUREMENT.measured)
  const [error, setError] = useState<string | null>(null)
  const [attempt, setAttempt] = useState<AttemptState<LoadPayload, InstructionMutation>>(() => initial?.attempt ?? { phase: 'idle' })
  const attemptRef = useRef(attempt)
  const pendingSlotRef = useRef(pendingSlot)
  attemptRef.current = attempt

  if (instruction.status !== INSTRUCTION_STATUS.issued) {
    return (
      <section className={styles.section} aria-labelledby="logistics-load-heading" data-testid="logistics-load">
        <h2 id="logistics-load-heading">{T.load.heading}</h2>
        <p className={styles.muted} data-testid="logistics-load-draft-only">
          {T.load.draftOnly}
        </p>
      </section>
    )
  }

  const chosen: InstructionLine | undefined = lines.find((line) => line.lineId === lineId)

  async function send(snapshot: AttemptSnapshot<LoadPayload>, forLine: string, slot: string | null) {
    const previous = attemptRef.current
    const inFlight = beginSend<LoadPayload, InstructionMutation>(snapshot)
    attemptRef.current = inFlight
    setAttempt(inFlight)
    const values: LoadFormSnapshot = { lineId: forLine, quantityText: snapshot.payload.quantity, measurement: snapshot.payload.measurement }
    if (slot !== null) rememberInstructionAttempt(slot, inFlight, values)
    const outcome = await recordLoad(forLine, snapshot.payload, {
      idempotencyKey: snapshot.key,
      companyId: snapshot.companyId,
      instructionId: instruction.id,
    })
    const next = applyOutcome(inFlight, snapshot, attemptBinding(shell, loadScope(instruction.id, forLine)), outcome, previous)
    attemptRef.current = next
    setAttempt(next)
    if (slot !== null) rememberInstructionAttempt(slot, next, values)
    if (next.phase === 'completed' || refusalMovesRecord(next)) onRecorded()
  }

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (chosen === undefined) return
    const parsed = parseQuantityInput(quantityText)
    if (!parsed.ok) {
      setError(quantityError(parsed.error))
      return
    }
    setError(null)
    const binding = attemptBinding(shell, loadScope(instruction.id, chosen.lineId))
    if (binding === null) return
    // A COMPLETED load never blocks the next one (l04 run 35481530144 at dc41a2d2: the section is keyed by record so its
    // notice survives the re-read, which also kept the attempt `completed` and silently blocked every later load). The
    // next submit starts a fresh attempt — new key, new load id — and its outcome replaces the previous notice; an
    // unknown or failed attempt keeps its own rules (retry the same snapshot, or abandon first).
    const current: AttemptState<LoadPayload, InstructionMutation> =
      attemptRef.current.phase === 'completed' ? { phase: 'idle' } : attemptRef.current
    const live = liveSnapshot(current)
    // One load id per snapshot: a retry of the same snapshot resends the same id; a new snapshot mints a new one.
    const payload: LoadPayload = {
      expectedVersion: instruction.version,
      loadId: live !== null && live.payload.quantity === parsed.value && live.payload.measurement === measurement ? live.payload.loadId : generateClientId(),
      quantity: parsed.value,
      measurement,
    }
    const decision = decideSubmit(
      current,
      { path: `${instruction.id}/lines/${chosen.lineId}/loads`, binding, companyId, payload },
      generateAttemptKey,
    )
    if (decision.type !== 'send') return
    pendingSlotRef.current = pendingSlot
    void send(decision.snapshot, chosen.lineId, pendingSlot)
  }

  function retry() {
    const live = liveSnapshot(attemptRef.current)
    if (live === null || lineId === '' || attemptRef.current.phase === 'submitting') return
    void send(live, lineId, pendingSlotRef.current)
  }

  function leave() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    setAttempt(next)
    if (next.phase !== 'idle' && pendingSlotRef.current !== null) {
      rememberInstructionAttempt(pendingSlotRef.current, next, { lineId, quantityText, measurement })
    }
  }

  const frozen = attempt.phase === 'submitting' || isOutcomeStillUnknown(attempt)
  return (
    <section className={styles.section} aria-labelledby="logistics-load-heading" data-testid="logistics-load">
      <h2 id="logistics-load-heading">{T.load.heading}</h2>
      <p className={styles.muted}>{T.load.note}</p>
      <NoticeBlock attempt={attempt} testId="logistics-load-notice" onRetry={retry} onAbandon={leave} />
      <form onSubmit={onSubmit} noValidate>
        <fieldset disabled={frozen} className={styles.fieldset}>
          <legend>{T.load.heading}</legend>
          <div className={styles.grid}>
            <label htmlFor="logistics-load-line">{T.load.line}</label>
            <select id="logistics-load-line" value={lineId} onChange={(event) => setLineId(event.target.value)}>
              {lines.map((line) => (
                <option key={line.lineId} value={line.lineId} disabled={line.isBlocked}>
                  {line.lineNo}. {T.remainingToLoad}: {line.remainingToLoad}
                  {line.isBlocked ? ` — ${T.blocked}` : ''}
                </option>
              ))}
            </select>
            {chosen?.isBlocked ? (
              <p className={styles.fieldError} data-testid="logistics-load-blocked">
                {T.load.blockedLine}
              </p>
            ) : null}

            <label htmlFor="logistics-load-quantity">{T.load.quantity}</label>
            <input
              id="logistics-load-quantity"
              type="text"
              inputMode="decimal"
              autoComplete="off"
              value={quantityText}
              aria-invalid={error !== null ? true : undefined}
              aria-describedby={error !== null ? 'logistics-load-quantity-error' : undefined}
              onChange={(event) => setQuantityText(event.target.value)}
            />
            {error === null ? null : (
              <p id="logistics-load-quantity-error" className={styles.fieldError}>
                {error}
              </p>
            )}

            <label htmlFor="logistics-load-measurement">{T.load.measurement}</label>
            <select id="logistics-load-measurement" value={measurement} onChange={(event) => setMeasurement(event.target.value as Measurement)}>
              {Object.values(MEASUREMENT).map((value) => (
                <option key={value} value={value}>
                  {MEASUREMENT_TEXT[value]}
                </option>
              ))}
            </select>
          </div>
          <div className={styles.actions}>
            <button type="submit" className={styles.primaryButton} disabled={frozen || chosen === undefined || chosen.isBlocked} data-testid="logistics-load-button">
              {T.load.record}
            </button>
          </div>
        </fieldset>
      </form>
    </section>
  )
}

function quantityError(error: QuantityInputError): string {
  const v = INSTRUCTION_CREATE_TEXT.validation
  switch (error) {
    case 'required':
      return v.quantityRequired
    case 'invalid':
      return v.quantityInvalid
    case 'ambiguous-separator':
      return v.quantityAmbiguous
    case 'not-positive':
      return v.quantityNotPositive
    case 'scale':
      return v.quantityScale
    default:
      return v.quantityTooWide
  }
}
