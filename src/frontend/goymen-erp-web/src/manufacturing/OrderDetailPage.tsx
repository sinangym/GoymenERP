/**
 * One production order: each parent roll's mass balance, and — for a holder of the
 * facts capability — the currently authorized fact forms (MFG-UI card, coordinator-approved).
 *
 * A technical, functional sub-view (card D1): published on the server since MFG-UI-F (#349,
 * after the `mfg` browser acceptance), and not a locked visual reference for the canonical
 * production screens (PR-09/13/14/15 are `DECISION_ONLY_REFERENCE_MISSING`).
 *
 * THE RULES THAT SHAPE THIS FILE
 *
 *  - The table is always the server's READ. A command's 200 body is the order as that
 *    command first saw it, and a replay repeats it verbatim; so after an accepted fact
 *    the order is re-read, and nothing is predicted locally (no `version + 1`, no sums).
 *  - Kilograms stay text end to end (Ö2). The client checks only the plain-decimal
 *    grammar; the company's kilogram profile decides the scale on the server, which
 *    refuses — never rounds — more decimals.
 *  - The unexplained difference is shown with its sign and is never called fire; the
 *    cost is a status ("Hesaplanmadı"), never 0.
 *  - A fact whose outcome is unknown freezes the forms until it is re-sent unchanged
 *    (./factAttempt.ts, ./pendingFactStore.ts).
 *  - Without `manufacturing.orders.facts` no form is drawn (card D3), and without
 *    `manufacturing.orders.close` no close button is drawn; the server enforces either way.
 *  - The close (MFG-ORD) sends the version this screen read, under its own frozen key, and
 *    shows the server's refusal by its code — no tolerance and no write-off is offered here.
 */

import { useCallback, useEffect, useReducer, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useParams } from 'react-router'
import {
  fetchManufacturingOutputLocations,
  fetchManufacturingOutputProducts,
  fetchMassBalance,
  fetchOrder,
} from './api'
import { closeOrder, recordFact, type FactFailure, type FactUnknown } from './commandApi'
import { fetchInventoryLabelDetail } from '../inventory/labelDetailApi'
import type { InventoryLabelDetail } from '../inventory/labelDetailContract'
import { INVENTORY_STOCK_POSITIONS_CAPABILITY } from '../inventory/capabilities'
import {
  FACT_KINDS,
  OUTPUT_CLASSES,
  isKilogramText,
  type CloseSnapshot,
  type EngineAnswer,
  type FactKind,
  type FactSnapshot,
  type MassBalanceView,
  type ManufacturingOutputLocationCandidate,
  type ManufacturingOutputProductCandidate,
  type ProductionOrderView,
  type RollBalance,
} from './contract'
import {
  factAttemptReducer,
  initialFactAttempt,
  isUnresolved,
  restoredFactAttempt,
  type FactAttempt,
} from './factAttempt'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import {
  pendingFactKey,
  recallPendingFact,
  releasePendingFact,
  reservePendingFact,
} from './pendingFactStore'
import {
  ACTUAL_UNIT_KG_NOTE,
  APPLIED_DETAIL,
  APPLIED_TITLE,
  CLOSED_ORDER_TEXT,
  CLOSE_TEXT,
  COST_STATUS_TEXT,
  ENGINE_REFUSED,
  ENGINE_UNAVAILABLE,
  FACT_KIND_HELP,
  FACT_KIND_LABEL,
  KG_FORMAT_ERROR,
  KG_FORMAT_HELP,
  INPUT_INVENTORY_TEXT,
  MASS_BALANCE_HEADING,
  MASS_BALANCE_HELP,
  ORDER_STATUS_LABEL,
  OUTPUT_INVENTORY_TEXT,
  OUTPUT_CLASS_LABEL,
  REMAINING_UNAVAILABLE,
  REPLAYED_DETAIL,
  REPLAYED_TITLE,
  ROLL_REQUIRED,
  SCRAP_PERCENT_NOTE,
  TARGET_BASIS_LABEL,
  TIME_REQUIRED,
  UNKNOWN_ACTION_HELP,
  closeFailureText,
  engineParameterLabel,
  failureText,
  unknownText,
} from './manufacturingText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './OrderDetailPage.module.css'

/** Registered with the shell so a company switch with an unsent fact warns first. */
const DIRTY_SCOPE = 'manufacturing.order.fact'

function newId(): string {
  const webCrypto = globalThis.crypto
  if (typeof webCrypto?.randomUUID === 'function') return webCrypto.randomUUID()
  // Fallback only where randomUUID is missing; still a v4-shaped id the server accepts.
  const hex = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16))
  hex[12] = '4'
  hex[16] = ((parseInt(hex[16], 16) & 0x3) | 0x8).toString(16)
  const s = hex.join('')
  return `${s.slice(0, 8)}-${s.slice(8, 12)}-${s.slice(12, 16)}-${s.slice(16, 20)}-${s.slice(20)}`
}

export function OrderDetailPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const { orderId } = useParams()
  // Remounted per context AND per order: a stale render must never put one company's
  // order, or a frozen expectedVersion, under another context.
  return (
    <OrderDetail key={`${namespace ?? 'no-context'}::${orderId ?? ''}`} namespace={namespace} />
  )
}

function OrderDetail({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const { orderId } = useParams()
  const activeCompanyId = shell.activeContext?.companyId ?? null
  const singleCompanyContext =
    (shell.activeContext?.scopeMode ?? 'single') === 'single' && activeCompanyId !== null
  const canRecord = hasCapability(shell, MANUFACTURING_CAPABILITY.orderFacts)
  const canReadInventoryLabel = hasCapability(shell, INVENTORY_STOCK_POSITIONS_CAPABILITY)
  // A third authority (MFG-ORD): recording a fact never closes an order.
  const canClose = hasCapability(shell, MANUFACTURING_CAPABILITY.orderClose)
  const storeKey = pendingFactKey(
    shell.sessionEpoch,
    shell.activeContext?.userId ?? null,
    activeCompanyId,
    orderId ?? '',
  )

  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'manufacturing-order', orderId ?? ''],
    enabled: namespace !== null && singleCompanyContext && (orderId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchOrder(activeCompanyId ?? '', orderId ?? '', signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })

  const refetch = query.refetch
  const refresh = useCallback(async () => {
    await refetch()
  }, [refetch])

  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<ProductionOrderView>

  if (!singleCompanyContext) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="üretim emri için tek şirket bağlamı" />
      </Frame>
    )
  }

  if (state.kind !== 'ready') {
    return (
      <Frame>
        <StateBlock state={state} subject="üretim emri" onRetry={() => void query.refetch()} />
        {recallPendingFact(storeKey) !== null ? (
          <div className={styles.noticeWarn} role="alert" data-outcome="outstanding">
            <p className={styles.noticeTitle}>Sonucu bilinmeyen bir olgunuz var</p>
            <p>
              Gönderdiğiniz olgunun sonucu hâlâ belirsiz ve emir şu an okunamıyor. Olgu kendiliğinden
              tekrarlanmadı. Emir yeniden okunduğunda aynı işlem anahtarıyla devam edebilirsiniz.
            </p>
          </div>
        ) : null}
      </Frame>
    )
  }

  const order = state.data
  return (
    <Frame>
      <OrderSummary order={order} />
      <RollTable rolls={order.rolls} />
      <MassBalancePanel
        namespace={namespace ?? 'no-context'}
        routeEntryGeneration={shell.routeEntryGeneration}
        companyId={activeCompanyId ?? ''}
        orderId={order.orderId}
        orderVersion={order.version}
      />
      {canRecord ? (
        <FactSection
          order={order}
          namespace={namespace ?? 'no-context'}
          routeEntryGeneration={shell.routeEntryGeneration}
          companyId={activeCompanyId ?? ''}
          canReadInventoryLabel={canReadInventoryLabel}
          storeKey={storeKey}
          refreshing={query.isFetching}
          onRefresh={refresh}
        />
      ) : null}
      {canClose ? (
        <CloseSection order={order} companyId={activeCompanyId ?? ''} onRefresh={refresh} />
      ) : null}
    </Frame>
  )
}

/**
 * GDC-BAL/1 (BAL slice 2): the order through the one mass-balance calculator the close uses, read from its own GET.
 * Keyed by the order's VERSION, so every re-read of the order after an accepted fact reads the balance again — the
 * figures are the server's, never summed here. BAL-004 / BAL-006 show the engine's own answer: while the order cannot
 * supply their inputs they say "Hesaplanamıyor" and name the inputs, never a 0 or an estimate.
 */
function MassBalancePanel({
  namespace,
  routeEntryGeneration,
  companyId,
  orderId,
  orderVersion,
}: {
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly orderId: string
  readonly orderVersion: number
}) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace, routeEntryGeneration, 'manufacturing-mass-balance', orderId, orderVersion],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchMassBalance(companyId, orderId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<MassBalanceView>

  return (
    <section aria-labelledby="mfg-balance-heading" className={styles.section} data-testid="mass-balance">
      <h2 id="mfg-balance-heading">{MASS_BALANCE_HEADING}</h2>
      <p className={styles.help}>{MASS_BALANCE_HELP}</p>
      {state.kind !== 'ready' ? (
        <StateBlock state={state} subject="kütle dengesi" onRetry={() => void query.refetch()} />
      ) : (
        <>
          <p className={styles.help} data-testid="mass-balance-formula">
            {state.data.formulaVersion}
          </p>
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead>
                <tr>
                  <th scope="col">Ana rulo</th>
                  <th scope="col">Fiilî girdi (kg)</th>
                  <th scope="col">Hesaplanan (kg)</th>
                  <th scope="col">Kütle farkı (kg)</th>
                  <th scope="col">Mutabakat</th>
                  <th scope="col">Fire %</th>
                  <th scope="col">Fiilî birim kg</th>
                </tr>
              </thead>
              <tbody>
                {state.data.rolls.map((roll) => (
                  <tr key={roll.parentRollId} data-balance-roll={roll.parentRollId}>
                    <th scope="row" className={styles.mono}>
                      {roll.parentRollId}
                    </th>
                    <td>{roll.difference.actualInputKg}</td>
                    <td>{roll.difference.accountedKg}</td>
                    <td data-testid="mass-difference">{roll.difference.differenceKg}</td>
                    <td>
                      <span
                        className={roll.difference.isFullyAccounted ? styles.badgeBalanced : styles.badgeDifference}
                        data-accounted={roll.difference.isFullyAccounted ? 'yes' : 'no'}
                      >
                        {roll.difference.isFullyAccounted ? 'Mutabık' : 'Fark var'}
                      </span>
                    </td>
                    <td data-testid="scrap-percent">
                      <EngineAnswerCell answer={roll.scrapPercent} />
                    </td>
                    <td data-testid="actual-unit-kg">
                      <EngineAnswerCell answer={roll.actualUnitKg} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className={styles.help}>{SCRAP_PERCENT_NOTE}</p>
          <p className={styles.help}>{ACTUAL_UNIT_KG_NOTE}</p>
        </>
      )}
    </section>
  )
}

/**
 * The close (MFG-ORD), a control of this screen for a holder of `manufacturing.orders.close`.
 *
 *  - The rule is the server's and the text only repeats the decided one (MFG-4): no tolerance is
 *    offered here, nothing is written off and no condition is added.
 *  - The version sent is the one this screen READ. The attempt is frozen once — key and version
 *    together — so a re-send of an unknown outcome is the same command (ADR-011), never a second
 *    close. A new attempt is a new key.
 *  - After an accepted (or replayed) close the order is re-read; nothing is predicted locally.
 */
function CloseSection({
  order,
  companyId,
  onRefresh,
}: {
  readonly order: ProductionOrderView
  readonly companyId: string
  readonly onRefresh: () => Promise<void>
}) {
  const [attempt, setAttempt] = useState<CloseAttempt>({ phase: 'idle' })

  const send = async (snapshot: CloseSnapshot) => {
    setAttempt({ phase: 'sending', snapshot })
    const outcome = await closeOrder(companyId, order.orderId, snapshot)
    if (outcome.type === 'applied') {
      setAttempt({ phase: 'applied', replayed: outcome.replayed })
      await onRefresh()
      return
    }
    if (outcome.type === 'failed') {
      setAttempt({ phase: 'refused', failure: outcome.failure })
      // The order moved on, or another writer committed: the screen reads it again so the next
      // decision is taken on the current figures.
      if (outcome.failure.kind === 'version-conflict' || outcome.failure.kind === 'concurrent-change') {
        await onRefresh()
      }
      return
    }
    setAttempt({ phase: 'unknown', snapshot, unknown: outcome.unknown })
  }

  return (
    <section aria-labelledby="mfg-close-heading" className={styles.section} data-testid="order-close">
      <h2 id="mfg-close-heading">{CLOSE_TEXT.heading}</h2>
      <p className={styles.help}>{CLOSE_TEXT.help}</p>
      {order.status === 'Closed' ? (
        <p className={styles.help} data-testid="order-already-closed">
          {CLOSED_ORDER_TEXT}
          {order.closedAtUtc !== null ? ` ${CLOSE_TEXT.closedAt}: ${order.closedAtUtc}` : ''}
        </p>
      ) : attempt.phase === 'confirming' ? (
        <div className={styles.actions}>
          <p role="alert">{CLOSE_TEXT.confirmQuestion}</p>
          <button
            type="button"
            onClick={() => void send({ idempotencyKey: newId(), expectedVersion: order.version })}
          >
            {CLOSE_TEXT.confirm}
          </button>
          <button type="button" onClick={() => setAttempt({ phase: 'idle' })}>
            {CLOSE_TEXT.cancel}
          </button>
        </div>
      ) : attempt.phase === 'unknown' ? (
        <div className={styles.actions}>
          <button type="button" onClick={() => void send(attempt.snapshot)}>
            {CLOSE_TEXT.resend}
          </button>
        </div>
      ) : (
        <div className={styles.actions}>
          <button
            type="button"
            onClick={() => setAttempt({ phase: 'confirming' })}
            disabled={attempt.phase === 'sending'}
            aria-busy={attempt.phase === 'sending'}
          >
            {attempt.phase === 'sending' ? CLOSE_TEXT.sending : CLOSE_TEXT.action}
          </button>
        </div>
      )}
      {attempt.phase === 'applied' ? (
        <div className={styles.noticeOk} role="status" data-outcome={attempt.replayed ? 'replayed' : 'applied'}>
          <p className={styles.noticeTitle}>
            {attempt.replayed ? CLOSE_TEXT.replayedTitle : CLOSE_TEXT.appliedTitle}
          </p>
          <p>{attempt.replayed ? CLOSE_TEXT.replayedDetail : CLOSE_TEXT.appliedDetail}</p>
        </div>
      ) : null}
      {attempt.phase === 'refused' ? (
        <div className={styles.noticeError} role="alert" data-outcome="refused">
          <p className={styles.noticeTitle}>{CLOSE_TEXT.refusedTitle}</p>
          <p data-testid="close-refusal">{closeFailureText(attempt.failure)}</p>
        </div>
      ) : null}
      {attempt.phase === 'unknown' ? (
        <div className={styles.noticeWarn} role="alert" data-outcome="unknown">
          <p className={styles.noticeTitle}>{CLOSE_TEXT.unknownTitle}</p>
          <p>{unknownText(attempt.unknown)}</p>
          <p>{CLOSE_TEXT.unknownHelp}</p>
        </div>
      ) : null}
    </section>
  )
}

/** One close attempt on this screen; the snapshot is kept exactly while its outcome is unknown. */
type CloseAttempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'confirming' }
  | { readonly phase: 'sending'; readonly snapshot: CloseSnapshot }
  | { readonly phase: 'applied'; readonly replayed: boolean }
  | { readonly phase: 'refused'; readonly failure: FactFailure }
  | { readonly phase: 'unknown'; readonly snapshot: CloseSnapshot; readonly unknown: FactUnknown }

function EngineAnswerCell({ answer }: { readonly answer: EngineAnswer }) {
  if (answer.outcome === 'Computed') return <span data-outcome="computed">{answer.value}</span>
  if (answer.outcome === 'Unavailable') {
    return (
      <span data-outcome="unavailable">
        {ENGINE_UNAVAILABLE} {answer.missingParameters.map((name) => engineParameterLabel(name)).join(', ')}
      </span>
    )
  }
  return (
    <span data-outcome="refused">
      {ENGINE_REFUSED} {answer.refusalCode}
      {answer.refusedParameter !== null ? ` (${engineParameterLabel(answer.refusedParameter)})` : ''}
    </span>
  )
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="mfg-order-heading" className={styles.page}>
      <h1 id="mfg-order-heading">Üretim emri</h1>
      {children}
    </section>
  )
}

function OrderSummary({ order }: { readonly order: ProductionOrderView }) {
  const remaining = order.remainingTarget
  return (
    <dl className={styles.card} aria-label="Emir özeti">
      <div className={styles.field}>
        <dt>Emir</dt>
        <dd className={styles.mono}>{order.orderId}</dd>
      </div>
      <div className={styles.field}>
        <dt>Durum</dt>
        <dd>
          <span
            className={order.status === 'Open' ? styles.badgeOpen : styles.badgeClosed}
            data-status={order.status}
          >
            {ORDER_STATUS_LABEL[order.status]}
          </span>
        </dd>
      </div>
      <div className={styles.field}>
        <dt>Plan</dt>
        <dd className={styles.mono}>{order.planId}</dd>
      </div>
      <div className={styles.field}>
        <dt>İş merkezi</dt>
        <dd className={styles.mono}>{order.workCenterId}</dd>
      </div>
      <div className={styles.field}>
        <dt>Proses</dt>
        <dd>{order.process}</dd>
      </div>
      <div className={styles.field}>
        <dt>Hedef</dt>
        <dd>
          {order.target === null
            ? 'Beyan edilmedi'
            : `${TARGET_BASIS_LABEL[order.target.basis] ?? order.target.basis}: ${order.target.kg} kg`}
        </dd>
      </div>
      <div className={styles.field}>
        <dt>Kalan hedef</dt>
        <dd data-testid="remaining-target">
          {remaining.basis === null
            ? REMAINING_UNAVAILABLE
            : `${remaining.remainingKg} kg (gerçekleşen ${remaining.achievedKg} / ${remaining.targetKg} kg)`}
        </dd>
      </div>
      <div className={styles.field}>
        <dt>Maliyet</dt>
        <dd data-testid="cost-status">{COST_STATUS_TEXT}</dd>
      </div>
    </dl>
  )
}

function RollTable({ rolls }: { readonly rolls: readonly RollBalance[] }) {
  return (
    <section aria-labelledby="mfg-rolls-heading" className={styles.section}>
      <h2 id="mfg-rolls-heading">Ana rulo dengesi (kg)</h2>
      <p className={styles.help}>
        Her rulo kendi başına mutabık olur: çıkan − iade = çıktılar + WIP + açıklanamayan fark.
      </p>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead>
            <tr>
              <th scope="col">Ana rulo</th>
              <th scope="col">Planlanan</th>
              <th scope="col">Çıkan</th>
              <th scope="col">İade</th>
              <th scope="col">Net çıkan</th>
              {OUTPUT_CLASSES.map((outputClass) => (
                <th scope="col" key={outputClass}>
                  {OUTPUT_CLASS_LABEL[outputClass]}
                </th>
              ))}
              <th scope="col">WIP</th>
              <th scope="col">Açıklanamayan fark</th>
              <th scope="col">Mutabakat</th>
            </tr>
          </thead>
          <tbody>
            {rolls.map((roll) => (
              <tr key={roll.parentRollId} data-roll={roll.parentRollId}>
                <th scope="row" className={styles.mono}>
                  {roll.parentRollId}
                </th>
                <td>{roll.plannedKg}</td>
                <td>{roll.issuedKg}</td>
                <td>{roll.returnedKg}</td>
                <td>{roll.netIssuedKg}</td>
                {OUTPUT_CLASSES.map((outputClass) => (
                  <td key={outputClass}>{roll.outputsKg[outputClass]}</td>
                ))}
                <td>{roll.workInProgressKg}</td>
                <td data-testid="unexplained">{roll.unexplainedKg}</td>
                <td>
                  <span
                    className={roll.isFullyAccounted ? styles.badgeBalanced : styles.badgeDifference}
                    data-accounted={roll.isFullyAccounted ? 'yes' : 'no'}
                  >
                    {roll.isFullyAccounted ? 'Mutabık' : 'Fark var'}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

interface FormValues {
  readonly kind: FactKind
  readonly parentRollId: string
  readonly kg: string
  /** `datetime-local` value, read as the browser's local time and sent as UTC. */
  readonly occurredAt: string
  readonly outputClass: '' | (typeof OUTPUT_CLASSES)[number]
  readonly productId: string
  readonly locationId: string
  readonly labelCode: string
  readonly heatNumber: string
  readonly coilNumber: string
  readonly requiredDocuments: string
  readonly initialBlock: '' | 'Free' | 'Blocked'
  readonly productSearch: string
  readonly locationSearch: string
}

const EMPTY_FORM: FormValues = {
  kind: 'input-issues',
  parentRollId: '',
  kg: '',
  occurredAt: '',
  outputClass: '',
  productId: '',
  locationId: '',
  labelCode: '',
  heatNumber: '',
  coilNumber: '',
  requiredDocuments: '',
  initialBlock: '',
  productSearch: '',
  locationSearch: '',
}

function formFromSnapshot(snapshot: FactSnapshot): FormValues {
  const at = new Date(snapshot.occurredAtUtc)
  const local = new Date(at.getTime() - at.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
  return {
    kind: snapshot.kind,
    parentRollId: snapshot.parentRollId,
    kg: snapshot.kg,
    occurredAt: local,
    outputClass: snapshot.kind === 'outputs' ? snapshot.outputClass : '',
    productId: snapshot.kind === 'outputs' ? snapshot.productId : '',
    locationId: snapshot.kind === 'outputs' ? snapshot.locationId : '',
    labelCode: snapshot.kind === 'outputs' ? snapshot.labelCode : '',
    heatNumber: snapshot.kind === 'outputs' ? (snapshot.heatNumber ?? '') : '',
    coilNumber: snapshot.kind === 'outputs' ? (snapshot.coilNumber ?? '') : '',
    requiredDocuments: snapshot.kind === 'outputs' ? snapshot.requiredDocuments.join('\n') : '',
    initialBlock: snapshot.kind === 'outputs' ? snapshot.initialBlock : '',
    productSearch: '',
    locationSearch: '',
  }
}

function factTiming(value: string): { readonly occurredAtUtc: string; readonly effectiveOn: string } | null {
  const milliseconds = value.length === 0 ? Number.NaN : new Date(value).getTime()
  if (Number.isNaN(milliseconds)) return null
  const occurredAtUtc = new Date(milliseconds).toISOString()
  return { occurredAtUtc, effectiveOn: occurredAtUtc.slice(0, 10) }
}

function cleanOptional(value: string): string | null {
  const cleaned = value.trim()
  return cleaned.length === 0 ? null : cleaned
}

function documents(value: string): readonly string[] {
  return value.split(/\r?\n/).map((item) => item.trim()).filter((item) => item.length > 0)
}

function isCompanyOwned(detail: InventoryLabelDetail, companyId: string): boolean {
  return detail.balances.positions.length > 0 && detail.balances.positions.every((position) =>
    position.ownerKind === 'Company' && position.ownerId === companyId &&
    position.custodianKind === 'Company' && position.custodianId === companyId)
}

function restored(storeKey: string | null): FactAttempt {
  const pending = recallPendingFact(storeKey)
  return pending === null ? initialFactAttempt : restoredFactAttempt(pending.snapshot, pending.resends)
}

function FactSection({
  order,
  namespace,
  routeEntryGeneration,
  companyId,
  canReadInventoryLabel,
  storeKey,
  refreshing,
  onRefresh,
}: {
  readonly order: ProductionOrderView
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly canReadInventoryLabel: boolean
  readonly storeKey: string | null
  readonly refreshing: boolean
  readonly onRefresh: () => Promise<void>
}) {
  const shellDispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const [attempt, dispatch] = useReducer(factAttemptReducer, storeKey, restored)
  // A restored snapshot shows the values it was sent with, not an empty form.
  const [form, setForm] = useState<FormValues>(() => {
    const pending = recallPendingFact(storeKey)
    return pending === null ? EMPTY_FORM : formFromSnapshot(pending.snapshot)
  })
  const [error, setError] = useState<string | null>(null)
  const noticeRef = useRef<HTMLDivElement>(null)

  const unresolved = isUnresolved(attempt)
  const returnSelected = form.kind === 'input-returns'
  const outputSelected = form.kind === 'outputs'
  const outputEnabled = canReadInventoryLabel
  const needsInventoryState = (returnSelected || outputSelected) && canReadInventoryLabel
  const timing = factTiming(form.occurredAt)
  const effectiveOn = timing?.effectiveOn ?? null
  const labelQuery = useQuery({
    queryKey: ['manufacturing-parent-label', companyId, form.parentRollId],
    enabled: needsInventoryState && form.parentRollId.length > 0,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchInventoryLabelDetail(companyId, form.parentRollId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })
  const labelState = !needsInventoryState || form.parentRollId.length === 0
    ? null
    : (labelQuery.isPending
        ? { kind: 'initial-loading' }
        : classifyOutcome(labelQuery.data ?? { type: 'malformed' })) as RemoteViewState<InventoryLabelDetail>
  const companyOwned = labelState?.kind === 'ready' && isCompanyOwned(labelState.data, companyId)

  const productQuery = useQuery({
    queryKey: [namespace, routeEntryGeneration, 'manufacturing-output-products', effectiveOn, form.productSearch],
    enabled: outputSelected && outputEnabled && effectiveOn !== null,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchManufacturingOutputProducts(companyId, effectiveOn ?? '', form.productSearch, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })
  const locationQuery = useQuery({
    queryKey: [namespace, routeEntryGeneration, 'manufacturing-output-locations', form.locationSearch],
    enabled: outputSelected && outputEnabled,
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchManufacturingOutputLocations(companyId, form.locationSearch, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })
  const products = productQuery.data?.type === 'ok-single' ? productQuery.data.data : null
  const locations = locationQuery.data?.type === 'ok-single' ? locationQuery.data.data : null
  const selectedProduct: ManufacturingOutputProductCandidate | undefined = products?.items.find(
    (item) => item.productId === form.productId,
  )
  const selectedLocation: ManufacturingOutputLocationCandidate | undefined = locations?.items.find(
    (item) => item.locationId === form.locationId,
  )

  const busy = attempt.phase === 'submitting'
  const closed = order.status === 'Closed'

  const dirty = unresolved || Object.entries(form).some(([key, value]) => key !== 'kind' && value.length > 0)
  useEffect(() => {
    shellDispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: DIRTY_SCOPE })
  }, [dirty, shellDispatch])
  useEffect(() => () => shellDispatch({ type: 'DIRTY_CLEAR', scope: DIRTY_SCOPE }), [shellDispatch])

  // Settled snapshots release exactly their own slot.
  useEffect(() => {
    if (attempt.phase === 'applied' || attempt.phase === 'failed') {
      releasePendingFact(storeKey, attempt.snapshot.idempotencyKey)
    } else if (attempt.phase === 'unknown') {
      reservePendingFact(storeKey, { snapshot: attempt.snapshot, resends: attempt.resends })
    }
  }, [attempt, storeKey])

  const phase = attempt.phase
  useEffect(() => {
    if (phase === 'applied' || phase === 'failed' || phase === 'unknown') noticeRef.current?.focus()
  }, [phase])

  const refetchLabel = labelQuery.refetch
  const refreshReads = useCallback(async (snapshot: FactSnapshot) => {
    if ((snapshot.kind !== 'input-issues' && snapshot.kind !== 'input-returns' && snapshot.kind !== 'outputs')
      || !canReadInventoryLabel) {
      await onRefresh()
      return
    }
    await Promise.all([onRefresh(), refetchLabel()])
  }, [canReadInventoryLabel, onRefresh, refetchLabel])

  const send = useCallback(
    async (snapshot: FactSnapshot, resends: number) => {
      // RESERVE FIRST, synchronously, before the request can be in flight.
      reservePendingFact(storeKey, { snapshot, resends })
      const security = boundary.capture()
      const outcome = await recordFact(companyId, order.orderId, snapshot)
      if (outcome.type === 'failed') {
        if (outcome.failure.kind === 'unauthenticated') boundary.report(401, security)
        if (outcome.failure.kind === 'forbidden') boundary.report(403, security)
      }
      dispatch({ type: 'SETTLE', outcome, idempotencyKey: snapshot.idempotencyKey })
      if (outcome.type === 'applied') {
        setForm((current) => ({ ...current, kg: '', labelCode: current.kind === 'outputs' ? '' : current.labelCode }))
        await refreshReads(snapshot)
      } else if (
        outcome.type === 'failed' &&
        (outcome.failure.kind === 'version-conflict' || outcome.failure.kind === 'label-version-conflict' ||
          outcome.failure.kind === 'concurrent-change')
      ) {
        // An owner record moved on: re-read every state this command is authorized to use.
        await refreshReads(snapshot)
      }
    },
    [boundary, companyId, order.orderId, refreshReads, storeKey],
  )

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    if (busy || unresolved || closed) return
    // A submit refused here mints NO id and NO key: nothing was attempted.
    if (form.parentRollId.length === 0) return setError(ROLL_REQUIRED)
    if (!isKilogramText(form.kg)) return setError(KG_FORMAT_ERROR)
    if ((returnSelected || outputSelected) && !canReadInventoryLabel) return
    if (needsInventoryState && labelState?.kind !== 'ready') return setError(INPUT_INVENTORY_TEXT.loadingRequired)
    if (needsInventoryState && !companyOwned) return setError(INPUT_INVENTORY_TEXT.companyOwnedRequired)
    if (timing === null) return setError(TIME_REQUIRED)
    if (outputSelected && form.outputClass === '') return setError(OUTPUT_INVENTORY_TEXT.classRequired)
    if (outputSelected && selectedProduct === undefined) return setError(OUTPUT_INVENTORY_TEXT.productRequired)
    if (outputSelected && selectedLocation === undefined) return setError(OUTPUT_INVENTORY_TEXT.locationRequired)
    if (outputSelected && form.labelCode.trim().length === 0) return setError(OUTPUT_INVENTORY_TEXT.labelRequired)
    if (outputSelected && form.initialBlock === '') return setError(OUTPUT_INVENTORY_TEXT.blockRequired)
    setError(null)
    const common = {
      factId: newId(),
      idempotencyKey: newId(),
      expectedVersion: order.version,
      parentRollId: form.parentRollId,
      kg: form.kg,
      occurredAtUtc: timing.occurredAtUtc,
    }
    const snapshot: FactSnapshot = form.kind === 'input-issues'
      ? { ...common, kind: form.kind, movementId: newId(), expectedParentLabelVersion: null }
      : form.kind === 'input-returns'
        ? {
            ...common,
            kind: form.kind,
            movementId: newId(),
            expectedParentLabelVersion: labelState?.kind === 'ready' ? labelState.data.labelVersion : '',
          }
        : form.kind === 'outputs'
          ? {
              ...common,
              kind: form.kind,
              movementId: newId(),
              expectedParentLabelVersion: labelState?.kind === 'ready' ? labelState.data.labelVersion : '',
              outputClass: form.outputClass as (typeof OUTPUT_CLASSES)[number],
              productId: selectedProduct?.productId ?? '',
              expectedProductVersion: selectedProduct?.productVersion ?? '',
              lotId: newId(),
              labelId: newId(),
              labelCode: form.labelCode.trim(),
              heatNumber: cleanOptional(form.heatNumber),
              coilNumber: cleanOptional(form.coilNumber),
              requiredDocuments: documents(form.requiredDocuments),
              ownerKind: 'Company',
              ownerId: companyId,
              custodianKind: 'Company',
              custodianId: companyId,
              locationId: selectedLocation?.locationId ?? '',
              initialBlock: form.initialBlock as 'Free' | 'Blocked',
            }
          : { ...common, kind: form.kind, movementId: null, expectedParentLabelVersion: null }
    dispatch({ type: 'SUBMIT', snapshot })
    void send(snapshot, 0)
  }

  const resend = useCallback(() => {
    if (attempt.phase !== 'unknown') return
    const { snapshot, resends } = attempt
    dispatch({ type: 'RESEND' })
    void send(snapshot, resends + 1)
  }, [attempt, send])

  const frozen = busy || unresolved || closed
  const update = (patch: Partial<FormValues>) => {
    if (attempt.phase === 'applied' || attempt.phase === 'failed') dispatch({ type: 'RESET' })
    setForm((current) => ({ ...current, ...patch }))
  }

  return (
    <section aria-labelledby="mfg-facts-heading" className={styles.section}>
      <h2 id="mfg-facts-heading">Gerçekleşme kaydı</h2>
      {closed ? <p className={styles.help}>{CLOSED_ORDER_TEXT}</p> : null}

      <AttemptNotice
        attempt={attempt}
        refreshing={refreshing || labelQuery.isFetching || productQuery.isFetching || locationQuery.isFetching}
        noticeRef={noticeRef}
      />

      <form className={styles.section} onSubmit={submit} noValidate aria-describedby="mfg-fact-help">
        <fieldset className={styles.kinds} disabled={frozen}>
          <legend className={styles.help}>Olgu türü</legend>
          {FACT_KINDS.filter((kind) =>
            (kind !== 'input-returns' && kind !== 'outputs') || canReadInventoryLabel).map((kind) => (
            <label key={kind}>
              <input
                type="radio"
                name="fact-kind"
                value={kind}
                checked={form.kind === kind}
                onChange={() => update({ kind })}
              />
              {FACT_KIND_LABEL[kind]}
            </label>
          ))}
        </fieldset>
        <p id="mfg-fact-help" className={styles.help}>
          {FACT_KIND_HELP[form.kind]}
        </p>
        <div className={styles.form}>
          {/* Each label names only its control (htmlFor): a wrapping label would fold the options and
              the help text into the accessible name. */}
          <div className={styles.control}>
            <label htmlFor="mfg-fact-roll">Ana rulo</label>
            <select
              id="mfg-fact-roll"
              value={form.parentRollId}
              disabled={frozen}
              onChange={(event) => update({ parentRollId: event.target.value })}
            >
              <option value="">Seçin</option>
              {order.rolls.map((roll) => (
                <option key={roll.parentRollId} value={roll.parentRollId}>
                  {roll.parentRollId}
                </option>
              ))}
            </select>
          </div>
          <div className={styles.control}>
            <label htmlFor="mfg-fact-kg">Kütle (kg)</label>
            <input
              id="mfg-fact-kg"
              type="text"
              inputMode="decimal"
              autoComplete="off"
              value={form.kg}
              disabled={frozen}
              aria-describedby="mfg-kg-help"
              onChange={(event) => update({ kg: event.target.value })}
            />
            <span id="mfg-kg-help" className={styles.help}>
              {KG_FORMAT_HELP}
            </span>
          </div>
          <div className={styles.control}>
            <label htmlFor="mfg-fact-occurred-at">Zaman</label>
            <input
              id="mfg-fact-occurred-at"
              type="datetime-local"
              value={form.occurredAt}
              disabled={frozen}
              onChange={(event) => update({ occurredAt: event.target.value })}
            />
          </div>
        </div>
        {outputSelected ? (
          <section aria-labelledby="mfg-output-heading" className={styles.section} data-testid="company-output-form">
            <h3 id="mfg-output-heading">Şirket malı çıktı etiketi</h3>
            <p className={styles.help}>{OUTPUT_INVENTORY_TEXT.customerOwnedBlocked}</p>
            <div className={styles.form}>
              <div className={styles.control}>
                <label htmlFor="mfg-output-class">Çıktı sınıfı</label>
                <select
                  id="mfg-output-class"
                  value={form.outputClass}
                  disabled={frozen}
                  onChange={(event) => update({
                    outputClass: event.target.value as FormValues['outputClass'],
                  })}
                >
                  <option value="">Seçin</option>
                  {OUTPUT_CLASSES.map((outputClass) => (
                    <option key={outputClass} value={outputClass}>{OUTPUT_CLASS_LABEL[outputClass]}</option>
                  ))}
                </select>
              </div>
              <div className={styles.control}>
                <label htmlFor="mfg-output-product-search">Ürün ara</label>
                <input
                  id="mfg-output-product-search"
                  value={form.productSearch}
                  disabled={frozen}
                  onChange={(event) => update({ productSearch: event.target.value, productId: '' })}
                />
              </div>
              <div className={styles.control}>
                <label htmlFor="mfg-output-product">Ürün</label>
                <select
                  id="mfg-output-product"
                  value={form.productId}
                  disabled={frozen || products === null}
                  onChange={(event) => update({ productId: event.target.value })}
                >
                  <option value="">Seçin</option>
                  {products?.items.map((product) => (
                    <option key={product.productId} value={product.productId}>
                      {product.code} — {product.name} · {product.stockUnitCode}
                    </option>
                  ))}
                </select>
                {effectiveOn === null ? <span className={styles.help}>Önce geçerli çıktı zamanını girin.</span> : null}
                {productQuery.isFetching ? <span role="status">{OUTPUT_INVENTORY_TEXT.productLoading}</span> : null}
                {productQuery.data !== undefined && productQuery.data.type !== 'ok-single'
                  ? <span role="alert">{OUTPUT_INVENTORY_TEXT.productUnavailable}</span> : null}
                {products?.searchWindowFull ? <span className={styles.help}>Daha fazla eşleşme var; aramayı daraltın.</span> : null}
              </div>
              <div className={styles.control}>
                <label htmlFor="mfg-output-location-search">Lokasyon ara</label>
                <input
                  id="mfg-output-location-search"
                  value={form.locationSearch}
                  disabled={frozen}
                  onChange={(event) => update({ locationSearch: event.target.value, locationId: '' })}
                />
              </div>
              <div className={styles.control}>
                <label htmlFor="mfg-output-location">Lokasyon</label>
                <select
                  id="mfg-output-location"
                  value={form.locationId}
                  disabled={frozen || locations === null}
                  onChange={(event) => update({ locationId: event.target.value })}
                >
                  <option value="">Seçin</option>
                  {locations?.items.map((location) => (
                    <option key={location.locationId} value={location.locationId}>
                      {location.siteCode} / {location.locationCode} — {location.locationName}
                    </option>
                  ))}
                </select>
                {locationQuery.isFetching ? <span role="status">{OUTPUT_INVENTORY_TEXT.locationLoading}</span> : null}
                {locationQuery.data !== undefined && locationQuery.data.type !== 'ok-single'
                  ? <span role="alert">{OUTPUT_INVENTORY_TEXT.locationUnavailable}</span> : null}
                {locations?.searchWindowFull ? <span className={styles.help}>Daha fazla eşleşme var; aramayı daraltın.</span> : null}
              </div>
              <div className={styles.control}>
                <label htmlFor="mfg-output-label-code">Yeni etiket kodu</label>
                <input
                  id="mfg-output-label-code"
                  value={form.labelCode}
                  disabled={frozen}
                  onChange={(event) => update({ labelCode: event.target.value })}
                />
              </div>
              <div className={styles.control}>
                <label htmlFor="mfg-output-heat">Döküm no (opsiyonel)</label>
                <input
                  id="mfg-output-heat"
                  value={form.heatNumber}
                  disabled={frozen}
                  onChange={(event) => update({ heatNumber: event.target.value })}
                />
              </div>
              <div className={styles.control}>
                <label htmlFor="mfg-output-coil">Rulo no (opsiyonel)</label>
                <input
                  id="mfg-output-coil"
                  value={form.coilNumber}
                  disabled={frozen}
                  onChange={(event) => update({ coilNumber: event.target.value })}
                />
              </div>
              <div className={styles.control}>
                <label htmlFor="mfg-output-documents">Belge referansları (satır başına)</label>
                <textarea
                  id="mfg-output-documents"
                  value={form.requiredDocuments}
                  disabled={frozen}
                  onChange={(event) => update({ requiredDocuments: event.target.value })}
                />
              </div>
              <div className={styles.control}>
                <label htmlFor="mfg-output-block">İlk stok durumu</label>
                <select
                  id="mfg-output-block"
                  value={form.initialBlock}
                  disabled={frozen}
                  onChange={(event) => update({ initialBlock: event.target.value as FormValues['initialBlock'] })}
                >
                  <option value="">Seçin</option>
                  <option value="Free">Serbest</option>
                  <option value="Blocked">Blokeli</option>
                </select>
              </div>
            </div>
          </section>
        ) : null}
        {labelState !== null ? (
          <ParentLabelState state={labelState} companyOwned={companyOwned} onRetry={() => void labelQuery.refetch()} />
        ) : null}
        {error !== null ? (
          <p className={styles.error} role="alert">
            {error}
          </p>
        ) : null}
        <div className={styles.actions}>
          {attempt.phase === 'unknown' ? (
            <button type="button" onClick={resend}>
              Aynı olguyu yeniden gönder
            </button>
          ) : (
            <button
              type="submit"
              disabled={frozen || (needsInventoryState && (!companyOwned || labelState?.kind !== 'ready'))}
            >
              {busy ? 'Gönderiliyor…' : 'Kaydet'}
            </button>
          )}
        </div>
      </form>
    </section>
  )
}

function ParentLabelState({
  state,
  companyOwned,
  onRetry,
}: {
  readonly state: RemoteViewState<InventoryLabelDetail>
  readonly companyOwned: boolean
  readonly onRetry: () => void
}) {
  if (state.kind !== 'ready') {
    return (
      <section aria-labelledby="mfg-parent-label-heading" className={styles.section}>
        <h3 id="mfg-parent-label-heading">{INPUT_INVENTORY_TEXT.heading}</h3>
        <StateBlock state={state} subject="ana rulo Inventory durumu" onRetry={onRetry} />
      </section>
    )
  }
  const detail = state.data
  return (
    <section aria-labelledby="mfg-parent-label-heading" className={styles.section} data-testid="parent-label-state">
      <h3 id="mfg-parent-label-heading">{INPUT_INVENTORY_TEXT.heading}</h3>
      <dl>
        <div><dt>Etiket</dt><dd className={styles.mono}>{detail.identity.code}</dd></div>
        <div><dt>{INPUT_INVENTORY_TEXT.version}</dt><dd>{detail.labelVersion}</dd></div>
        <div><dt>{INPUT_INVENTORY_TEXT.physical}</dt><dd>{detail.balances.physicalKg} kg</dd></div>
        <div><dt>{INPUT_INVENTORY_TEXT.free}</dt><dd>{detail.balances.freeKg} kg</dd></div>
        <div><dt>{INPUT_INVENTORY_TEXT.blocked}</dt><dd>{detail.balances.blockedKg} kg</dd></div>
      </dl>
      {!companyOwned ? <p role="alert" className={styles.noticeWarn}>{INPUT_INVENTORY_TEXT.companyOwnedRequired}</p> : null}
    </section>
  )
}

function AttemptNotice({
  attempt,
  refreshing,
  noticeRef,
}: {
  readonly attempt: FactAttempt
  readonly refreshing: boolean
  readonly noticeRef: React.RefObject<HTMLDivElement | null>
}) {
  if (attempt.phase === 'applied') {
    return (
      <div ref={noticeRef} tabIndex={-1} className={styles.noticeOk} role="status" data-outcome={attempt.replayed ? 'replayed' : 'applied'}>
        <p className={styles.noticeTitle}>{attempt.replayed ? REPLAYED_TITLE : APPLIED_TITLE}</p>
        <p>{attempt.replayed ? REPLAYED_DETAIL : APPLIED_DETAIL}</p>
        {refreshing ? <p>Emir yeniden okunuyor…</p> : null}
      </div>
    )
  }
  if (attempt.phase === 'failed') {
    return (
      <div ref={noticeRef} tabIndex={-1} className={styles.noticeError} role="alert" data-outcome="failed" data-failure={attempt.failure.kind}>
        <p className={styles.noticeTitle}>Kaydedilmedi</p>
        <p>{failureText(attempt.failure)}</p>
      </div>
    )
  }
  if (attempt.phase === 'unknown') {
    return (
      <div ref={noticeRef} tabIndex={-1} className={styles.noticeWarn} role="alert" data-outcome="unknown">
        <p className={styles.noticeTitle}>Sonuç bilinmiyor</p>
        <p>{unknownText(attempt.unknown)}</p>
        {attempt.lastRefusal !== null ? <p>Son yeniden gönderim reddedildi: {failureText(attempt.lastRefusal)}</p> : null}
        <p>{UNKNOWN_ACTION_HELP}</p>
      </div>
    )
  }
  return null
}
