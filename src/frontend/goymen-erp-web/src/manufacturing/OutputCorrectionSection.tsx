/**
 * F-009 D6 (UI-14, API-25): the output correction form INSIDE the order screen, drawn only for the
 * `manufacturing.orders.output-correction` control (order read + `manufacturing.facts.correct`).
 *
 * THE RULES THAT SHAPE THIS FILE
 *
 *  - The outputs to choose from are the order's own outputs as the lineage read names them (output id and its label);
 *    the label's version is Inventory's detail read. Nothing here lists, sums or predicts a stock figure.
 *  - A correction is an append-only reversal: the declaration is never deleted. The kilograms stay text (Ö2); the
 *    client checks only the plain-decimal grammar and the server's kg profile decides the scale.
 *  - One attempt is frozen once — key, reversal id, movement id, order version, label version, kg, reason and time —
 *    and a re-send of an UNKNOWN outcome sends that same snapshot, so the mass is never taken back twice. While an
 *    outcome is unknown the form is frozen; a new correction is a new snapshot.
 *  - After an accepted (or replayed) correction, or a version/label conflict, the order is re-read; the lineage and the
 *    label are keyed by the order's version and read again with it.
 *  - Only an Open order is corrected; for any other status the form is not drawn and the server refuses either way.
 */

import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { correctOutput, type FactFailure, type FactUnknown } from './commandApi'
import { newCommandId } from './commandId'
import { isKilogramText, parsePieceCount, type OutputCorrectionSnapshot, type ProductionOrderView } from './contract'
import { OUTPUT_PIECE_TEXT } from './outputPieceText'
import { recallPendingCorrection, releasePendingCorrection, reservePendingCorrection } from './pendingCorrectionStore'
import { fetchOrderLineage } from './lineageApi'
import type { OrderLineageOutput, OrderLineageView } from './lineageContract'
import {
  KG_FORMAT_ERROR,
  KG_FORMAT_HELP,
  OUTPUT_CORRECTION_TEXT,
  TIME_REQUIRED,
  correctionFailureText,
  unknownText,
} from './manufacturingText'
import { fetchInventoryLabelDetail } from '../inventory/labelDetailApi'
import type { InventoryLabelDetailWithMovementTimeline } from '../inventory/labelDetailContract'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import styles from './OrderDetailPage.module.css'

type CorrectionAttempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'confirming'; readonly snapshot: OutputCorrectionSnapshot }
  | { readonly phase: 'sending'; readonly snapshot: OutputCorrectionSnapshot }
  | { readonly phase: 'applied'; readonly replayed: boolean }
  | { readonly phase: 'refused'; readonly failure: FactFailure }
  | { readonly phase: 'unknown'; readonly snapshot: OutputCorrectionSnapshot; readonly unknown: FactUnknown }

interface CorrectionForm {
  readonly outputId: string
  readonly kg: string
  /** AK-7 (URT-D112): the pieces taken back; asked only for a counted output. */
  readonly pieceCount: string
  readonly reason: string
  /** `datetime-local` value, read as the browser's local time and sent as UTC. */
  readonly occurredAt: string
}

const EMPTY_FORM: CorrectionForm = { outputId: '', kg: '', pieceCount: '', reason: '', occurredAt: '' }

function utcInstant(value: string): string | null {
  const milliseconds = value.length === 0 ? Number.NaN : new Date(value).getTime()
  return Number.isNaN(milliseconds) ? null : new Date(milliseconds).toISOString()
}

export function OutputCorrectionSection({
  order,
  namespace,
  routeEntryGeneration,
  companyId,
  canReadInventoryLabel,
  pendingKey,
  onRefresh,
}: {
  readonly order: ProductionOrderView
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly canReadInventoryLabel: boolean
  /** The order screen's pending slot (`pendingFactKey`): an unknown correction outlives a remount under it. */
  readonly pendingKey: string | null
  readonly onRefresh: () => Promise<void>
}) {
  const open = order.status === 'Open'
  return (
    <section aria-labelledby="mfg-correction-heading" className={styles.section} data-testid="order-output-correction">
      <h2 id="mfg-correction-heading">{OUTPUT_CORRECTION_TEXT.heading}</h2>
      <p className={styles.help}>{OUTPUT_CORRECTION_TEXT.help}</p>
      {open ? (
        <CorrectionBody
          order={order}
          namespace={namespace}
          routeEntryGeneration={routeEntryGeneration}
          companyId={companyId}
          canReadInventoryLabel={canReadInventoryLabel}
          pendingKey={pendingKey}
          onRefresh={onRefresh}
        />
      ) : (
        <p className={styles.help} data-testid="output-correction-not-open">
          {OUTPUT_CORRECTION_TEXT.closedOrder}
        </p>
      )}
    </section>
  )
}

function CorrectionBody({
  order,
  namespace,
  routeEntryGeneration,
  companyId,
  canReadInventoryLabel,
  pendingKey,
  onRefresh,
}: {
  readonly order: ProductionOrderView
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly canReadInventoryLabel: boolean
  readonly pendingKey: string | null
  readonly onRefresh: () => Promise<void>
}) {
  const boundary = useAuthBoundary()
  const [form, setForm] = useState<CorrectionForm>(EMPTY_FORM)
  const [errors, setErrors] = useState<readonly string[]>([])
  // An unknown (or unmounted in-flight) correction comes back frozen: only its same snapshot may be re-sent.
  const [attempt, setAttempt] = useState<CorrectionAttempt>(() => {
    const pending = recallPendingCorrection(pendingKey)
    return pending === null ? { phase: 'idle' } : { phase: 'unknown', snapshot: pending.snapshot, unknown: pending.unknown }
  })

  const lineageQuery = useQuery({
    queryKey: [namespace, routeEntryGeneration, 'manufacturing-order-correction-outputs', order.orderId, order.version],
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchOrderLineage(companyId, order.orderId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })
  const lineageState = (
    lineageQuery.isPending ? { kind: 'initial-loading' } : classifyOutcome(lineageQuery.data ?? { type: 'malformed' })
  ) as RemoteViewState<OrderLineageView>
  const outputs: readonly OrderLineageOutput[] = lineageState.kind === 'ready' ? lineageState.data.outputs : []
  const selected = outputs.find((output) => output.outputId === form.outputId) ?? null
  // AK-7 (URT-D112): a counted output (listed in the order's piece view) asks for the pieces taken back.
  const counted = selected !== null && order.outputPieces.some((output) => output.outputId === selected.outputId)

  const labelId = selected?.outputLabelId ?? ''
  const labelQuery = useQuery({
    queryKey: [namespace, routeEntryGeneration, 'manufacturing-correction-label', companyId, labelId, order.version],
    enabled: canReadInventoryLabel && labelId.length > 0,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchInventoryLabelDetail(companyId, labelId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })
  const labelState =
    !canReadInventoryLabel || labelId.length === 0
      ? null
      : ((labelQuery.isPending
          ? { kind: 'initial-loading' }
          : classifyOutcome(labelQuery.data ?? { type: 'malformed' })) as RemoteViewState<InventoryLabelDetailWithMovementTimeline>)

  const unresolved = attempt.phase === 'sending' || attempt.phase === 'unknown'
  const frozen = unresolved || attempt.phase === 'confirming'

  const update = (patch: Partial<CorrectionForm>) => {
    setForm((current) => ({ ...current, ...patch }))
    setErrors([])
  }

  const prepare = () => {
    const found: string[] = []
    if (selected === null) found.push(OUTPUT_CORRECTION_TEXT.outputRequired)
    if (selected !== null && !canReadInventoryLabel) found.push(OUTPUT_CORRECTION_TEXT.labelPermission)
    else if (selected !== null && labelState?.kind !== 'ready') found.push(OUTPUT_CORRECTION_TEXT.labelNotReady)
    if (!isKilogramText(form.kg)) found.push(KG_FORMAT_ERROR)
    const pieceCount = counted ? parsePieceCount(form.pieceCount, 0) : null
    if (pieceCount === undefined) found.push(OUTPUT_PIECE_TEXT.correctionPieceCountFormat)
    else if (counted && pieceCount === null) found.push(OUTPUT_PIECE_TEXT.correctionPieceCountRequired)
    const reason = form.reason.trim()
    if (reason.length === 0) found.push(OUTPUT_CORRECTION_TEXT.reasonRequired)
    const occurredAtUtc = utcInstant(form.occurredAt)
    if (occurredAtUtc === null) found.push(TIME_REQUIRED)
    if (found.length > 0 || selected === null || labelState?.kind !== 'ready' || occurredAtUtc === null) {
      setErrors(found)
      return
    }
    setErrors([])
    // Frozen once: every id and both versions are taken now, so a re-send names exactly this correction.
    setAttempt({
      phase: 'confirming',
      snapshot: {
        idempotencyKey: newCommandId(),
        outputId: selected.outputId,
        reversalId: newCommandId(),
        movementId: newCommandId(),
        expectedVersion: order.version,
        labelId: selected.outputLabelId,
        expectedLabelVersion: labelState.data.labelVersion,
        kg: form.kg,
        reason,
        occurredAtUtc,
        ...(counted && pieceCount != null ? { pieceCount } : {}),
      },
    })
  }

  const send = async (snapshot: OutputCorrectionSnapshot) => {
    // A send from the unknown phase is the RETRY of an attempt that may already have taken the kilograms back.
    const held = attempt.phase === 'unknown' ? attempt.unknown : null
    setAttempt({ phase: 'sending', snapshot })
    reservePendingCorrection(pendingKey, { snapshot, unknown: { kind: 'aborted' } })
    const security = boundary.capture()
    const outcome = await correctOutput(companyId, order.orderId, snapshot)
    if (outcome.type === 'failed') {
      const kind = outcome.failure.kind
      if (kind === 'unauthenticated') boundary.report(401, security)
      if (kind === 'forbidden') boundary.report(403, security)
      if (held !== null && (kind === 'unauthenticated' || kind === 'forbidden' || kind === 'not-found' || kind === 'unsupported-media')) {
        // A 401/403 (or code-less 404/415) on the retry is answered before the owner reads the idempotency receipt, so it
        // decides nothing about the first send (virman pattern, frontend-screen-states §3): the SAME key stays held and
        // the attempt stays unknown. Only an applied answer or a decoded owner refusal settles it.
        reservePendingCorrection(pendingKey, { snapshot, unknown: held })
        setAttempt({ phase: 'unknown', snapshot, unknown: held })
        return
      }
    }
    if (outcome.type === 'unknown') reservePendingCorrection(pendingKey, { snapshot, unknown: outcome.unknown })
    else releasePendingCorrection(pendingKey, snapshot.idempotencyKey)
    if (outcome.type === 'applied') {
      setAttempt({ phase: 'applied', replayed: outcome.replayed })
      setForm(EMPTY_FORM)
      await onRefresh()
      return
    }
    if (outcome.type === 'failed') {
      setAttempt({ phase: 'refused', failure: outcome.failure })
      const kind = outcome.failure.kind
      if (kind === 'version-conflict' || kind === 'concurrent-change' || kind === 'label-version-conflict') {
        // The order or its label moved on: both are read again so the next decision is taken on current figures.
        await Promise.all([onRefresh(), labelQuery.refetch()])
      }
      return
    }
    setAttempt({ phase: 'unknown', snapshot, unknown: outcome.unknown })
  }

  if (lineageState.kind !== 'ready') {
    return (
      <StateBlock
        state={lineageState}
        subject={OUTPUT_CORRECTION_TEXT.outputsSubject}
        onRetry={() => void lineageQuery.refetch()}
      />
    )
  }

  return (
    <>
      {lineageState.data.truncated ? (
        <p className={styles.help} data-testid="output-correction-truncated">{OUTPUT_CORRECTION_TEXT.truncated}</p>
      ) : null}
      {outputs.length === 0 && attempt.phase !== 'unknown' ? (
        <p className={styles.help} data-testid="output-correction-empty">{OUTPUT_CORRECTION_TEXT.noOutputs}</p>
      ) : (
        <div className={styles.form}>
          <div className={styles.control}>
            <label htmlFor="mfg-correction-output">{OUTPUT_CORRECTION_TEXT.output}</label>
            <select
              id="mfg-correction-output"
              aria-required="true"
              value={form.outputId}
              disabled={frozen}
              onChange={(event) => update({ outputId: event.target.value })}
            >
              <option value="">{OUTPUT_CORRECTION_TEXT.outputPlaceholder}</option>
              {outputs.map((output) => (
                <option key={output.outputId} value={output.outputId}>
                  {OUTPUT_CORRECTION_TEXT.outputOption(output.outputId, output.outputLabelId)}
                </option>
              ))}
            </select>
          </div>
          {selected !== null && !canReadInventoryLabel ? (
            <p className={styles.help} role="note" data-testid="output-correction-label-permission">
              {OUTPUT_CORRECTION_TEXT.labelPermission}
            </p>
          ) : null}
          {labelState !== null && labelState.kind !== 'ready' ? (
            <StateBlock
              state={labelState}
              subject={OUTPUT_CORRECTION_TEXT.labelSubject}
              onRetry={() => void labelQuery.refetch()}
            />
          ) : null}
          {labelState?.kind === 'ready' ? (
            <p className={styles.help} data-testid="output-correction-label-version">
              {OUTPUT_CORRECTION_TEXT.labelVersion}: <span className={styles.mono}>{labelState.data.labelVersion}</span>
            </p>
          ) : null}
          <div className={styles.control}>
            <label htmlFor="mfg-correction-kg">{OUTPUT_CORRECTION_TEXT.kg}</label>
            <input
              id="mfg-correction-kg"
              aria-required="true"
              inputMode="decimal"
              autoComplete="off"
              value={form.kg}
              disabled={frozen}
              aria-describedby="mfg-correction-kg-help"
              onChange={(event) => update({ kg: event.target.value })}
            />
            <p id="mfg-correction-kg-help" className={styles.help}>{KG_FORMAT_HELP}</p>
          </div>
          {counted ? (
            <div className={styles.control}>
              <label htmlFor="mfg-correction-piece-count">{OUTPUT_PIECE_TEXT.correctionPieceCount}</label>
              <input
                id="mfg-correction-piece-count"
                aria-required="true"
                inputMode="numeric"
                autoComplete="off"
                value={form.pieceCount}
                disabled={frozen}
                aria-describedby="mfg-correction-piece-count-help"
                onChange={(event) => update({ pieceCount: event.target.value })}
              />
              <p id="mfg-correction-piece-count-help" className={styles.help}>
                {OUTPUT_PIECE_TEXT.correctionPieceCountHelp}
              </p>
            </div>
          ) : null}
          <div className={styles.control}>
            <label htmlFor="mfg-correction-reason">{OUTPUT_CORRECTION_TEXT.reason}</label>
            <textarea
              id="mfg-correction-reason"
              aria-required="true"
              value={form.reason}
              maxLength={256}
              rows={3}
              disabled={frozen}
              onChange={(event) => update({ reason: event.target.value })}
            />
          </div>
          <div className={styles.control}>
            <label htmlFor="mfg-correction-occurred-at">{OUTPUT_CORRECTION_TEXT.occurredAt}</label>
            <input
              id="mfg-correction-occurred-at"
              aria-required="true"
              type="datetime-local"
              value={form.occurredAt}
              disabled={frozen}
              onChange={(event) => update({ occurredAt: event.target.value })}
            />
          </div>
        </div>
      )}
      {errors.length > 0 ? (
        <ul role="alert" data-testid="output-correction-errors">
          {errors.map((error) => (
            <li key={error}>{error}</li>
          ))}
        </ul>
      ) : null}
      {attempt.phase === 'confirming' ? (
        <div className={styles.actions}>
          <p role="alert">{OUTPUT_CORRECTION_TEXT.confirmQuestion}</p>
          <Button type="button" onClick={() => void send(attempt.snapshot)}>
            {OUTPUT_CORRECTION_TEXT.confirm}
          </Button>
          <Button type="button" onClick={() => setAttempt({ phase: 'idle' })}>
            {OUTPUT_CORRECTION_TEXT.cancel}
          </Button>
        </div>
      ) : attempt.phase === 'unknown' ? (
        <div className={styles.actions}>
          <Button type="button" onClick={() => void send(attempt.snapshot)}>
            {OUTPUT_CORRECTION_TEXT.resend}
          </Button>
        </div>
      ) : outputs.length > 0 ? (
        <div className={styles.actions}>
          <Button
            type="button"
            onClick={prepare}
            disabled={attempt.phase === 'sending'}
            aria-busy={attempt.phase === 'sending'}
          >
            {attempt.phase === 'sending' ? OUTPUT_CORRECTION_TEXT.sending : OUTPUT_CORRECTION_TEXT.submit}
          </Button>
        </div>
      ) : null}
      {attempt.phase === 'applied' ? (
        <div className={styles.noticeOk} role="status" data-outcome={attempt.replayed ? 'replayed' : 'applied'}>
          <p className={styles.noticeTitle}>
            {attempt.replayed ? OUTPUT_CORRECTION_TEXT.replayedTitle : OUTPUT_CORRECTION_TEXT.appliedTitle}
          </p>
          <p>{attempt.replayed ? OUTPUT_CORRECTION_TEXT.replayedDetail : OUTPUT_CORRECTION_TEXT.appliedDetail}</p>
        </div>
      ) : null}
      {attempt.phase === 'refused' ? (
        <div className={styles.noticeError} role="alert" data-outcome="refused">
          <p className={styles.noticeTitle}>{OUTPUT_CORRECTION_TEXT.refusedTitle}</p>
          <p data-testid="output-correction-refusal">{correctionFailureText(attempt.failure)}</p>
        </div>
      ) : null}
      {attempt.phase === 'unknown' ? (
        <div className={styles.noticeWarn} role="alert" data-outcome="unknown">
          <p className={styles.noticeTitle}>{OUTPUT_CORRECTION_TEXT.unknownTitle}</p>
          <p>{unknownText(attempt.unknown)}</p>
          <p>{OUTPUT_CORRECTION_TEXT.unknownHelp}</p>
        </div>
      ) : null}
    </>
  )
}
