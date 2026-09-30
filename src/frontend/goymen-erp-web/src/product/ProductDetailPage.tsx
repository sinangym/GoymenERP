/**
 * U-01 Ürün 360 — card, pinned to `GET /api/v1/products/{id}` (`ProductCardView`).
 *
 * Shows exactly what the card carries: the product core, the reference block and
 * the server's completeness verdict.
 *
 * WHAT IT REFUSES TO DO:
 *  - resolve a reference id itself. The labels come from the SERVER, on the
 *    projection; a client-side lookup would be the fan-out §19.5 forbids. When a
 *    label is null while the id is present, that says the catalogue could not be
 *    read — a different fact from "nothing assigned", and shown differently;
 *  - present `NotEvaluated` completeness as complete or activatable. README states
 *    that rule explicitly, and an unknown evaluation token is not coerced either.
 *
 * Not pulled in, per §9: physical stock/lot, price, accounting detail.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { REASON_MAX, activateProduct, changeProductLifecycle, fetchProductCard, fetchProductHistory } from './api'
import type { ActivateProductPayload, ChangeProductLifecyclePayload, ProductLifecycleMove } from './api'
import { PRODUCT_ROLE, UNIT_ROLE, isComplete, type ProductCard, type ProductMutationResult } from './contract'
import {
  COMPLETENESS_LABEL,
  COMPLETENESS_UNKNOWN_LABEL,
  PENDING_REFERENCE_NOTE,
  PRODUCT_ACTIVATION_TEXT,
  PRODUCT_DETAIL_TEXT,
  PRODUCT_DUPLICATE_TEXT,
  PRODUCT_HISTORY_TEXT,
  PRODUCT_LIFECYCLE_TEXT,
  ROLE_LABEL,
  historyFieldLabel,
  historyOperationLabel,
  historyVersionText,
  ROLE_PROVENANCE_LABEL,
  UNASSIGNED,
  UNIT_ROLE_LABEL,
  labelOrUnavailable,
  lifecycleLabel,
  lifecycleMeaning,
} from './productText'
import {
  PRODUCT_ACTIVATE_CAPABILITY,
  PRODUCT_CAPABILITY,
  PRODUCT_DEACTIVATE_CAPABILITY,
  PRODUCT_REACTIVATE_CAPABILITY,
  PRODUCT_CAPABILITY_ROUTE,
  productEditPath,
} from './capabilities'
import { classifyOutcome } from '../shell/uiState'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import {
  generateAttemptKey,
  isOutcomeStillUnknown,
  liveSnapshot,
  nextState,
  type AttemptSnapshot,
  type AttemptState,
} from './commandAttempt'
import {
  activationScope,
  attemptBindingFor,
  lifecycleScope,
  forgetAttempt,
  recallAttempt,
  rememberAttempt,
} from './pendingAttempt'
import { UnresolvedExitGuard } from './UnresolvedExitGuard'
import { DuplicateCandidatesPanel } from './DuplicateCandidates'
import {
  duplicateIds,
  duplicateQueryKey,
  useDuplicateCandidates,
  type DuplicateOutcome,
} from './duplicateQuery'
import styles from './ProductDetailPage.module.css'

/** The id of the Draft card's duplicate warning, which the approval control is described by. */
const DUPLICATE_CHECK_ID = 'product-duplicate-check'

function formatInstant(value: string): string {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return parsed.toLocaleString('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
}

export function ProductDetailPage() {
  const shell = useShellState()
  const { productId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const origin = shell.activeContext?.companyId ?? null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const entryGeneration = shell.routeEntryGeneration
  const canReturnToList = hasCapability(shell, PRODUCT_CAPABILITY.productsList)

  const queryKey = [namespace ?? 'no-context', entryGeneration, 'product-card', productId ?? ''] as const
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && origin !== null && productId !== undefined && productId.length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchProductCard(productId as string, { companyId: origin as string }, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const backLink = canReturnToList ? (
    <Link
      className={styles.backLink}
      to={PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsList]}
    >
      ← {PRODUCT_DETAIL_TEXT.backToList}
    </Link>
  ) : null

  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single') {
    return (
      <section className={styles.page} aria-label={PRODUCT_DETAIL_TEXT.identityHeading}>
        {backLink}
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject="ürün"
          onRetry={() => void query.refetch()}
        />
      </section>
    )
  }

  const card = outcome.data
  /**
   * The way into basic edit, gated on its OWN capability AND on the record being a
   * Draft — the domain refuses to basic-edit anything else (`lifecycle_conflict`),
   * so offering the control elsewhere would be offering a refusal.
   */
  const canEdit =
    hasCapability(shell, PRODUCT_CAPABILITY.productsEdit) &&
    card.product.lifecycle.known &&
    card.product.lifecycle.value === 'Draft'
  const canActivate = hasCapability(shell, PRODUCT_ACTIVATE_CAPABILITY)
  /**
   * The lifecycle moves the actor holds, each by ITS OWN grant. Like the approval, a control is
   * mounted by its capability alone and itself decides what to show: the open button only while the
   * card is in the state it moves FROM (an Active card may be taken out of new use, an Inactive one
   * returned; a Draft takes neither — approval is its way forward), and its own confirmed answer
   * until a later move makes that answer stale. The server re-checks all of it.
   */
  const lifecycleMoves: readonly ProductLifecycleMove[] = [
    ...(hasCapability(shell, PRODUCT_DEACTIVATE_CAPABILITY) ? (['deactivate'] as const) : []),
    ...(hasCapability(shell, PRODUCT_REACTIVATE_CAPABILITY) ? (['reactivate'] as const) : []),
  ]
  /**
   * §10.2: a Draft card shows the server's exact-name duplicates on every open, so closing and reopening the
   * card answers what the server holds now. An Active/Inactive card is past approval; it shows none.
   */
  const isDraft = card.product.lifecycle.known && card.product.lifecycle.value === 'Draft'
  const reReadAfterWrite = (productIdWritten: string) => {
    // As for activation: the command's card carries the group label only, so it is never painted.
    // The card, its history and the list are RE-READ; the new state appears when the server says so.
    void queryClient.invalidateQueries({ queryKey, exact: true })
    void queryClient.invalidateQueries({
      queryKey: [namespace, entryGeneration, 'product-history', productIdWritten],
    })
    void queryClient.invalidateQueries({
      queryKey: [namespace, entryGeneration, 'products'],
    })
  }

  return (
    <ProductCardView
      card={card}
      history={
        <ProductHistorySection
          productId={card.product.id}
          namespace={namespace as string}
          entryGeneration={entryGeneration}
          origin={origin as string}
        />
      }
      backLink={backLink}
      editLink={
        canEdit ? (
          <Link className={styles.editLink} to={productEditPath(card.product.id)}>
            {PRODUCT_DETAIL_TEXT.edit}
          </Link>
        ) : null
      }
      duplicateCheck={
        isDraft ? (
          <DraftDuplicateCheck
            key={card.product.id}
            card={card}
            namespace={namespace}
            entryGeneration={entryGeneration}
            origin={origin}
          />
        ) : null
      }
      inactiveDetail={
        card.product.lifecycle.known && card.product.lifecycle.value === 'Inactive' ? (
          <InactiveDetail
            productId={card.product.id}
            namespace={namespace as string}
            entryGeneration={entryGeneration}
            origin={origin as string}
          />
        ) : null
      }
      lifecycleControl={lifecycleMoves.map((move) => (
        <ProductLifecycleChangeAction
          key={`${card.product.id}:${move}`}
          card={card}
          move={move}
          onChanged={(changed) => reReadAfterWrite(changed.product.id)}
          onReload={() => query.refetch()}
        />
      ))}
      activationControl={
        canActivate ? (
          <ProductActivationAction
            key={card.product.id}
            card={card}
            duplicateKey={duplicateQueryKey(namespace, entryGeneration, card.product.name.trim(), card.product.id)}
            onActivated={(activated) => {
              /**
               * The command's answer CONFIRMS Active, but its card is the command's own
               * projection: it carries the group label only (the owner resolves no other
               * catalogue on a write). Painting it would turn every assigned class, family,
               * template and unit into "ad okunamadı" / "atanmadı" — a false claim about a
               * card just approved group-wide. So the answer is never written into the card;
               * the card is RE-READ from its own endpoint, and Active appears when that
               * server read says so. Until then the last server card stays as it was: no
               * optimistic Active, and no partially labelled card.
               */
              void queryClient.invalidateQueries({ queryKey, exact: true })
              void queryClient.invalidateQueries({
                queryKey: [namespace, entryGeneration, 'product-history', activated.product.id],
              })
              void queryClient.invalidateQueries({
                queryKey: [namespace, entryGeneration, 'products'],
              })
            }}
            onReload={() => query.refetch()}
          />
        ) : null
      }
    />
  )
}

/**
 * The Draft card's duplicate warning (§10.2): the server's exact-name candidates, this card left out, with a
 * link to each. It blocks nothing; the approval re-reads the same query before it sends (see below).
 */
function DraftDuplicateCheck({
  card,
  namespace,
  entryGeneration,
  origin,
}: {
  readonly card: ProductCard
  readonly namespace: string | null
  readonly entryGeneration: number
  readonly origin: string | null
}) {
  const shell = useShellState()
  const query = useDuplicateCandidates({
    name: card.product.name.trim(),
    excludeProductId: card.product.id,
    namespace,
    entryGeneration,
    origin,
    enabled: true,
  })
  return (
    <DuplicateCandidatesPanel
      id={DUPLICATE_CHECK_ID}
      outcome={query.data}
      canOpenDetail={hasCapability(shell, PRODUCT_CAPABILITY.productsDetail)}
    />
  )
}

type ActivationAttempt = AttemptState<ActivateProductPayload, ProductMutationResult>
const PRODUCT_ACTIVATION_DIRTY_SCOPE = 'product.activation'

function ProductActivationAction({
  card,
  duplicateKey,
  onActivated,
  onReload,
}: {
  readonly card: ProductCard
  /** The Draft card's duplicate-warning query, re-read before a NEW approval attempt is sent. */
  readonly duplicateKey: readonly unknown[]
  readonly onActivated: (card: ProductCard) => void
  /** Re-reads the card from its own endpoint; resolves once that read has answered. */
  readonly onReload: () => Promise<unknown>
}) {
  const queryClient = useQueryClient()
  const [duplicatesChanged, setDuplicatesChanged] = useState(false)
  const [checking, setChecking] = useState(false)
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const origin = shell.activeContext?.companyId ?? null
  const binding = useMemo(
    () => attemptBindingFor(shell, activationScope(card.product.id)),
    [shell, card.product.id],
  )
  const [attempt, setAttempt] = useState<ActivationAttempt>(
    () => recallAttempt<ActivateProductPayload, ProductMutationResult>(binding) ?? { phase: 'idle' },
  )
  const attemptRef = useRef(attempt)
  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  const unresolved = isOutcomeStillUnknown(attempt)
  useEffect(() => {
    dispatch({ type: unresolved ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: PRODUCT_ACTIVATION_DIRTY_SCOPE })
  }, [dispatch, unresolved])
  useEffect(
    () => () => dispatch({ type: 'DIRTY_CLEAR', scope: PRODUCT_ACTIVATION_DIRTY_SCOPE }),
    [dispatch],
  )
  useEffect(() => {
    if (attempt.phase === 'completed') rememberAttempt(binding, attempt)
  }, [attempt, binding])

  const send = useCallback(
    async (snapshot: AttemptSnapshot<ActivateProductPayload>) => {
      const previous = attemptRef.current
      const inFlight: ActivationAttempt = { phase: 'submitting', snapshot }
      rememberAttempt(binding, inFlight)
      setAttempt(inFlight)
      const securitySnapshot = boundary.capture()
      const result = await activateProduct(card.product.id, snapshot.payload, {
        idempotencyKey: snapshot.key,
        origin: { companyId: snapshot.origin },
      })
      const next = nextState(snapshot, result, previous)
      rememberAttempt(binding, next, false)
      if (result.type === 'http') boundary.report(result.status, securitySnapshot)
      setAttempt(next)
      if (next.phase === 'completed') onActivated(next.result.card)
    },
    [binding, boundary, card.product.id, onActivated],
  )

  /**
   * "Kartı yenile" after a DEFINITE refusal (a stale version, a lifecycle conflict, a
   * rejected request). The refusal applied nothing and answered the card the person had
   * read; once the card is re-read, that answer no longer describes what is on screen. So
   * the control returns to idle over the FRESH card and the person decides again: a new
   * press is a new attempt with a new key and the re-read version. Nothing is retried or
   * rebased here. An attempt whose earlier outcome is still unknown never reaches this —
   * it keeps its key behind the same-attempt retry.
   */
  async function reloadAfterRefusal() {
    const refused = attemptRef.current
    if (refused.phase !== 'failed' || refused.unresolvedSince !== undefined) return
    await onReload()
    if (attemptRef.current === refused) setAttempt({ phase: 'idle' })
  }

  /**
   * §10.2 re-check before approval. The duplicate warning never blocks an approval, but the person must not
   * approve on the strength of a list that is no longer the server's: the warning is re-read first, and when
   * it names other cards than the ones on screen the press does not send — the fresh list is shown and a
   * second press approves. A failed re-read blocks nothing (the warning is not a gate). A same-key retry of
   * an unresolved attempt skips this: it repeats a decision already sent.
   */
  async function duplicatesStillAsShown(): Promise<boolean> {
    const shown = duplicateIds(queryClient.getQueryData<DuplicateOutcome>(duplicateKey))
    await queryClient.refetchQueries({ queryKey: duplicateKey, exact: true })
    const fresh = queryClient.getQueryData<DuplicateOutcome>(duplicateKey)
    if (fresh === undefined || fresh.type !== 'ok-single') return true
    if (fresh.data.exactName.length === 0 && !fresh.data.hasMore) return true
    return duplicateIds(fresh) === shown
  }

  async function startOrRetry() {
    const pending = liveSnapshot(attempt)
    if (pending !== null) {
      void send(pending)
      return
    }
    if (origin === null) return
    setChecking(true)
    let unchanged = true
    try {
      unchanged = await duplicatesStillAsShown()
    } finally {
      setChecking(false)
    }
    setDuplicatesChanged(!unchanged)
    if (!unchanged) return
    void send({
      key: generateAttemptKey(),
      payload: { expectedVersion: card.product.version },
      origin,
      namespace: binding?.key ?? '',
    })
  }

  let content: React.ReactNode = null
  if (attempt.phase === 'completed') {
    content = (
      <p className={styles.activationSuccess} role="status">
        {PRODUCT_ACTIVATION_TEXT.success}
        {attempt.result.isReplay ? ` ${PRODUCT_ACTIVATION_TEXT.replayed}` : ''}
      </p>
    )
  } else if (unresolved && attempt.phase !== 'submitting') {
    content = (
      <div className={styles.activationError} role="alert" data-testid="product-activation-unknown">
        <strong>{PRODUCT_ACTIVATION_TEXT.unknownTitle}</strong>
        <span>{PRODUCT_ACTIVATION_TEXT.unknownDetail}</span>
        <button type="button" className={styles.activateButton} onClick={() => void startOrRetry()}>
          {PRODUCT_ACTIVATION_TEXT.retrySameAttempt}
        </button>
      </div>
    )
  } else if (attempt.phase === 'failed') {
    const message =
      attempt.failure === 'stale-version'
        ? `${PRODUCT_ACTIVATION_TEXT.staleTitle}. ${PRODUCT_ACTIVATION_TEXT.staleDetail}`
        : attempt.failure === 'lifecycle-conflict'
          ? `${PRODUCT_ACTIVATION_TEXT.lifecycleTitle}. ${PRODUCT_ACTIVATION_TEXT.lifecycleDetail}`
          : attempt.failure === 'not-found'
            ? PRODUCT_ACTIVATION_TEXT.notFound
            : attempt.failure === 'forbidden'
              ? PRODUCT_ACTIVATION_TEXT.forbidden
              : attempt.failure === 'unauthenticated'
                ? PRODUCT_ACTIVATION_TEXT.unauthenticated
                : PRODUCT_ACTIVATION_TEXT.failed
    content = (
      <div className={styles.activationError} role="alert">
        <span>{message}</span>
        <button type="button" className={styles.activateButton} onClick={() => void reloadAfterRefusal()}>
          {PRODUCT_ACTIVATION_TEXT.refresh}
        </button>
      </div>
    )
  } else if (card.product.lifecycle.known && card.product.lifecycle.value === 'Draft') {
    content = (
      <div className={styles.activationAction}>
        <span>{PRODUCT_ACTIVATION_TEXT.globalImpact}</span>
        {duplicatesChanged ? (
          <span role="alert" data-testid="product-duplicates-changed">
            {PRODUCT_DUPLICATE_TEXT.changedBeforeApproval}
          </span>
        ) : null}
        <button
          type="button"
          className={styles.activateButton}
          disabled={attempt.phase === 'submitting' || checking}
          aria-describedby={DUPLICATE_CHECK_ID}
          onClick={() => void startOrRetry()}
        >
          {attempt.phase === 'submitting'
            ? PRODUCT_ACTIVATION_TEXT.submitting
            : PRODUCT_ACTIVATION_TEXT.submit}
        </button>
      </div>
    )
  }

  return (
    <>
      {content}
      <UnresolvedExitGuard
        active={unresolved}
        title={PRODUCT_ACTIVATION_TEXT.unsavedTitle}
        detail={PRODUCT_ACTIVATION_TEXT.unsavedDetail}
        stayLabel={PRODUCT_ACTIVATION_TEXT.unsavedStay}
        leaveLabel={PRODUCT_ACTIVATION_TEXT.unsavedLeave}
        onLeave={() => forgetAttempt(binding)}
      />
    </>
  )
}

type ChangeAttempt = AttemptState<ChangeProductLifecyclePayload, ProductMutationResult>

/** Client-side reason check, the server's rule restated for an early answer; the server still decides. */
function reasonProblem(raw: string): string | null {
  const trimmed = raw.trim()
  if (trimmed.length === 0) return PRODUCT_LIFECYCLE_TEXT.reasonRequired
  if (trimmed.length > REASON_MAX) return PRODUCT_LIFECYCLE_TEXT.reasonTooLong
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f\u007f-\u009f]/.test(raw)) return PRODUCT_LIFECYCLE_TEXT.reasonInvalid
  return null
}

/**
 * Deactivation (Active → Inactive) or reactivation (Inactive → Active) of a card in use, with the
 * person's reason. The same attempt discipline as the approval: one key per submitted snapshot, an
 * unknown outcome is retried with the SAME key and payload, a definite refusal is settled and the
 * card re-read, and nothing is painted before the server answers — the new state comes from the
 * card's own re-read.
 */
function ProductLifecycleChangeAction({
  card,
  move,
  onChanged,
  onReload,
}: {
  readonly card: ProductCard
  readonly move: ProductLifecycleMove
  readonly onChanged: (card: ProductCard) => void
  /** Re-reads the card from its own endpoint; resolves once that read has answered. */
  readonly onReload: () => Promise<unknown>
}) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const origin = shell.activeContext?.companyId ?? null
  const text = PRODUCT_LIFECYCLE_TEXT[move]
  // One dirty scope per move, so one control settling never clears the other's unresolved attempt.
  const dirtyScope = `product.${move}`
  const binding = useMemo(
    () => attemptBindingFor(shell, lifecycleScope(card.product.id, move)),
    [shell, card.product.id, move],
  )
  const [attempt, setAttempt] = useState<ChangeAttempt>(
    () => recallAttempt<ChangeProductLifecyclePayload, ProductMutationResult>(binding) ?? { phase: 'idle' },
  )
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const attemptRef = useRef(attempt)
  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  const unresolved = isOutcomeStillUnknown(attempt)
  useEffect(() => {
    dispatch({ type: unresolved ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: dirtyScope })
  }, [dispatch, dirtyScope, unresolved])
  useEffect(
    () => () => dispatch({ type: 'DIRTY_CLEAR', scope: dirtyScope }),
    [dispatch, dirtyScope],
  )
  useEffect(() => {
    if (attempt.phase === 'completed') rememberAttempt(binding, attempt)
  }, [attempt, binding])

  const send = useCallback(
    async (snapshot: AttemptSnapshot<ChangeProductLifecyclePayload>) => {
      const previous = attemptRef.current
      const inFlight: ChangeAttempt = { phase: 'submitting', snapshot }
      rememberAttempt(binding, inFlight)
      setAttempt(inFlight)
      const securitySnapshot = boundary.capture()
      const result = await changeProductLifecycle(card.product.id, move, snapshot.payload, {
        idempotencyKey: snapshot.key,
        origin: { companyId: snapshot.origin },
      })
      const next = nextState(snapshot, result, previous)
      rememberAttempt(binding, next, false)
      if (result.type === 'http') boundary.report(result.status, securitySnapshot)
      setAttempt(next)
      if (next.phase === 'completed') onChanged(next.result.card)
    },
    [binding, boundary, card.product.id, move, onChanged],
  )

  async function reloadAfterRefusal() {
    const refused = attemptRef.current
    if (refused.phase !== 'failed' || refused.unresolvedSince !== undefined) return
    await onReload()
    if (attemptRef.current === refused) {
      setAttempt({ phase: 'idle' })
      setOpen(false)
    }
  }

  function submit() {
    const pending = liveSnapshot(attempt)
    if (pending !== null) {
      void send(pending)
      return
    }
    const problem = reasonProblem(reason)
    setReasonError(problem)
    if (problem !== null || origin === null) return
    void send({
      key: generateAttemptKey(),
      payload: { expectedVersion: card.product.version, reason: reason.trim() },
      origin,
      namespace: binding?.key ?? '',
    })
  }

  const reasonId = `product-lifecycle-reason-${card.product.id}-${move}`
  const lifecycle = card.product.lifecycle.known ? card.product.lifecycle.value : null
  const from = move === 'deactivate' ? 'Active' : 'Inactive'
  let content: React.ReactNode = null
  if (attempt.phase === 'completed') {
    // The confirmed answer stays until a later move (the other control) makes it stale.
    if (lifecycle !== from) content = (
      <p className={styles.activationSuccess} role="status">
        {text.success}
        {attempt.result.isReplay ? ` ${text.replayed}` : ''}
      </p>
    )
  } else if (unresolved && attempt.phase !== 'submitting') {
    content = (
      <div className={styles.activationError} role="alert" data-testid={`product-${move}-unknown`}>
        <strong>{text.unknownTitle}</strong>
        <span>{PRODUCT_LIFECYCLE_TEXT.unknownDetail}</span>
        <button type="button" className={styles.activateButton} onClick={submit}>
          {text.retrySameAttempt}
        </button>
      </div>
    )
  } else if (attempt.phase === 'failed') {
    const message =
      attempt.failure === 'stale-version'
        ? `${PRODUCT_LIFECYCLE_TEXT.staleTitle}. ${PRODUCT_LIFECYCLE_TEXT.staleDetail}`
        : attempt.failure === 'lifecycle-conflict'
          ? `${text.lifecycleTitle}. ${text.lifecycleDetail}`
          : attempt.failure === 'not-found'
            ? PRODUCT_LIFECYCLE_TEXT.notFound
            : attempt.failure === 'forbidden'
              ? text.forbidden
              : attempt.failure === 'unauthenticated'
                ? PRODUCT_LIFECYCLE_TEXT.unauthenticated
                : text.failed
    content = (
      <div className={styles.activationError} role="alert">
        <span>{message}</span>
        <button type="button" className={styles.activateButton} onClick={() => void reloadAfterRefusal()}>
          {PRODUCT_LIFECYCLE_TEXT.refresh}
        </button>
      </div>
    )
  } else if (lifecycle !== from && attempt.phase !== 'submitting') {
    content = null
  } else if (!open) {
    content = (
      <button type="button" className={styles.activateButton} onClick={() => setOpen(true)}>
        {text.open}
      </button>
    )
  } else {
    const submitting = attempt.phase === 'submitting'
    content = (
      <form
        className={styles.lifecycleForm}
        aria-label={text.open}
        noValidate
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <p className={styles.lifecycleImpact}>{text.impact}</p>
        <label htmlFor={reasonId}>{PRODUCT_LIFECYCLE_TEXT.reasonLabel}</label>
        <input
          id={reasonId}
          type="text"
          value={reason}
          maxLength={REASON_MAX}
          required
          disabled={submitting}
          aria-invalid={reasonError !== null}
          aria-describedby={`${reasonId}-hint${reasonError !== null ? ` ${reasonId}-error` : ''}`}
          onChange={(event) => {
            setReason(event.target.value)
            if (reasonError !== null) setReasonError(null)
          }}
        />
        <span id={`${reasonId}-hint`} className={styles.note}>
          {PRODUCT_LIFECYCLE_TEXT.reasonHint}
        </span>
        {reasonError !== null ? (
          <span id={`${reasonId}-error`} className={styles.fieldError} role="alert">
            {reasonError}
          </span>
        ) : null}
        <div className={styles.lifecycleButtons}>
          <button type="submit" className={styles.activateButton} disabled={submitting}>
            {submitting ? text.submitting : text.submit}
          </button>
          <button
            type="button"
            disabled={submitting}
            onClick={() => {
              setOpen(false)
              setReason('')
              setReasonError(null)
            }}
          >
            {PRODUCT_LIFECYCLE_TEXT.cancel}
          </button>
        </div>
      </form>
    )
  }

  return (
    <>
      {content}
      <UnresolvedExitGuard
        active={unresolved}
        title={text.unsavedTitle}
        detail={PRODUCT_LIFECYCLE_TEXT.unsavedDetail}
        stayLabel={PRODUCT_LIFECYCLE_TEXT.unsavedStay}
        leaveLabel={PRODUCT_LIFECYCLE_TEXT.unsavedLeave}
        onLeave={() => forgetAttempt(binding)}
      />
    </>
  )
}

/** The card's history read. One query key, shared by the timeline and the Inactive detail. */
function useProductHistory(productId: string, namespace: string, entryGeneration: number, origin: string) {
  const boundary = useAuthBoundary()
  return useQuery({
    queryKey: [namespace, entryGeneration, 'product-history', productId],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchProductHistory(productId, { companyId: origin }, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
}

/**
 * When and why the card was taken out of use: the NEWEST history entry, and only when that entry is
 * a deactivation (a newer move would make it stale). Who did it is not on the wire (the history
 * carries no actor, #181 §5.4) and is not guessed here. Nothing is shown while the history is not
 * readable: the banner above already states the fact that matters.
 */
function InactiveDetail({
  productId,
  namespace,
  entryGeneration,
  origin,
}: {
  readonly productId: string
  readonly namespace: string
  readonly entryGeneration: number
  readonly origin: string
}) {
  const query = useProductHistory(productId, namespace, entryGeneration, origin)
  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single') return null
  const newest = outcome.data.entries[0]
  if (newest === undefined || !newest.operation.known || newest.operation.value !== 'Deactivate') return null
  return (
    <dl className={styles.inactiveDetail} data-testid="product-inactive-detail">
      <div>
        <dt>{PRODUCT_LIFECYCLE_TEXT.deactivatedAt}</dt>
        <dd>
          <time dateTime={newest.occurredAtUtc}>{formatInstant(newest.occurredAtUtc)}</time>
        </dd>
      </div>
      <div>
        <dt>{PRODUCT_LIFECYCLE_TEXT.deactivationReason}</dt>
        <dd>{newest.reason}</dd>
      </div>
    </dl>
  )
}

/**
 * U-01 §9 item 6: the version/change timeline, read from the server's projection of
 * the canonical audit record. Its own query, so a history that cannot be read never
 * takes the card down with it — and the card never waits for the history.
 */
function ProductHistorySection({
  productId,
  namespace,
  entryGeneration,
  origin,
}: {
  readonly productId: string
  readonly namespace: string
  readonly entryGeneration: number
  readonly origin: string
}) {
  const query = useProductHistory(productId, namespace, entryGeneration, origin)

  const outcome = query.data
  let body: React.ReactNode
  if (outcome === undefined) {
    body = <p className={styles.note}>{PRODUCT_HISTORY_TEXT.loading}</p>
  } else if (outcome.type === 'ok-single') {
    const history = outcome.data
    body = (
      <>
        <ol className={styles.historyList} data-testid="product-history">
          {history.entries.map((entry) => (
            <li key={entry.eventId} className={styles.historyEntry}>
              <p className={styles.historyTitle}>
                <strong>{historyOperationLabel(entry.operation)}</strong>
                {' · '}
                <span>{historyVersionText(entry.versionBefore, entry.versionAfter)}</span>
                {' · '}
                <time dateTime={entry.occurredAtUtc}>{formatInstant(entry.occurredAtUtc)}</time>
              </p>
              <p className={styles.historyLine}>
                {PRODUCT_HISTORY_TEXT.reason}: {entry.reason}
              </p>
              {entry.operation.known && entry.operation.value === 'DraftCreate' ? null : (
                <p className={styles.historyLine}>
                  {PRODUCT_HISTORY_TEXT.changedFields}:{' '}
                  {entry.changedFields.length > 0
                    ? entry.changedFields.map(historyFieldLabel).join(', ')
                    : PRODUCT_HISTORY_TEXT.noTrackedChange}
                </p>
              )}
            </li>
          ))}
        </ol>
        {history.truncated ? <p className={styles.note}>{PRODUCT_HISTORY_TEXT.truncated}</p> : null}
        <p className={styles.note}>{PRODUCT_HISTORY_TEXT.actorNote}</p>
      </>
    )
  } else if (outcome.type === 'http' && outcome.status === 404) {
    body = <p className={styles.note}>{PRODUCT_HISTORY_TEXT.unavailable}</p>
  } else if (outcome.type === 'fence-stale') {
    body = <p className={styles.note}>{PRODUCT_HISTORY_TEXT.loading}</p>
  } else {
    body = (
      <div role="alert" className={styles.historyError}>
        <p>{outcome.type === 'malformed' ? PRODUCT_HISTORY_TEXT.malformed : PRODUCT_HISTORY_TEXT.failed}</p>
        <button type="button" onClick={() => void query.refetch()}>
          {PRODUCT_HISTORY_TEXT.retry}
        </button>
      </div>
    )
  }

  return (
    <section className={styles.section} aria-labelledby="product-history-heading">
      <h2 id="product-history-heading">{PRODUCT_HISTORY_TEXT.heading}</h2>
      {body}
    </section>
  )
}

function ProductCardView({
  card,
  history,
  backLink,
  editLink,
  activationControl,
  lifecycleControl,
  inactiveDetail,
  duplicateCheck,
}: {
  readonly card: ProductCard
  readonly history: React.ReactNode
  readonly backLink: React.ReactNode
  readonly editLink: React.ReactNode
  readonly activationControl: React.ReactNode
  readonly lifecycleControl: React.ReactNode
  readonly inactiveDetail: React.ReactNode
  readonly duplicateCheck: React.ReactNode
}) {
  const { product, references } = card
  // Single source: the card's own `completeness` is a compatibility projection of
  // this one, so reading the product's removes any chance of the two disagreeing.
  const completeness = product.completeness

  return (
    <article className={styles.page} aria-labelledby="product-detail-heading">
      {backLink}

      <header className={styles.header}>
        <div>
          <p className={styles.code}>{product.code}</p>
          <h1 id="product-detail-heading" className={styles.title}>
            {product.name}
          </h1>
        </div>
        <div className={styles.lifecycle}>
          {editLink}
          {activationControl}
          {lifecycleControl}
          <span
            className={styles.badge}
            data-lifecycle={product.lifecycle.known ? product.lifecycle.value : 'unknown'}
          >
            {lifecycleLabel(product.lifecycle)}
          </span>
          <span className={styles.lifecycleMeaning}>{lifecycleMeaning(product.lifecycle)}</span>
        </div>
      </header>

      {duplicateCheck}

      {/* Only for a KNOWN Inactive: an unrecognised status must not inherit this claim. */}
      {product.lifecycle.known && product.lifecycle.value === 'Inactive' ? (
        <div className={styles.inactiveBanner} role="status">
          <p>Bu ürün her iki şirkette de yeni kullanıma kapalıdır. Geçmiş kayıtlar görünür kalır.</p>
          {inactiveDetail}
        </div>
      ) : null}

      <section className={styles.section} aria-labelledby="product-roles-heading">
        <h2 id="product-roles-heading">{PRODUCT_DETAIL_TEXT.rolesHeading}</h2>
        {/*
          One row per role with its provenance, NOT collapsed into a yes/no column.
          An unevaluated role must not be shown as `false`, and a derived role must
          be distinguishable from one a person typed — only one of the two is safe
          to overwrite by hand.
        */}
        <table className={styles.roleTable}>
          <caption>{PRODUCT_DETAIL_TEXT.rolesHeading}</caption>
          <thead>
            <tr>
              <th scope="col">Rol</th>
              <th scope="col">Durum</th>
              <th scope="col">Kaynak</th>
            </tr>
          </thead>
          <tbody>
            {PRODUCT_ROLE.map((role) => {
              const state = product.roles[role]
              return (
                <tr key={role}>
                  <th scope="row">{ROLE_LABEL[role]}</th>
                  <td data-testid={`product-role-${role}`}>
                    {state.value === null ? '—' : state.value ? 'Evet' : 'Hayır'}
                  </td>
                  <td>{ROLE_PROVENANCE_LABEL[state.provenance]}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </section>

      <section className={styles.section} aria-labelledby="product-completeness-heading">
        <h2 id="product-completeness-heading">{PRODUCT_DETAIL_TEXT.completenessHeading}</h2>
        {/*
          README: "NotEvaluated must not be rendered as complete or activatable." An
          unknown token is not coerced either — it reads as unknown, with its raw
          value, so the report that reaches the contract owner is actionable.
        */}
        <p className={styles.value} data-testid="product-completeness">
          {completeness.evaluation === null
            ? `${COMPLETENESS_UNKNOWN_LABEL}: ${completeness.rawEvaluation}`
            : COMPLETENESS_LABEL[completeness.evaluation]}
        </p>
        {isComplete(completeness) ? null : (
          <>
            <p className={styles.note}>{PRODUCT_DETAIL_TEXT.missingFields}</p>
            {completeness.missingFields.length === 0 ? (
              <p className={styles.empty}>{PRODUCT_DETAIL_TEXT.noMissingFields}</p>
            ) : (
              <ul className={styles.missingList} data-testid="product-missing-fields">
                {completeness.missingFields.map((field) => (
                  <li key={field}>{field}</li>
                ))}
              </ul>
            )}
          </>
        )}
      </section>

      <section className={styles.section} aria-labelledby="product-references-heading">
        <h2 id="product-references-heading">{PRODUCT_DETAIL_TEXT.referencesHeading}</h2>
        {/*
          Server-resolved labels, never a client lookup. A null label beside a
          present id means the catalogue could not be read; a null id means nothing
          is assigned — and the two read differently on purpose.
        */}
        <dl className={styles.definitionList}>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.groupId}</dt>
            <dd data-testid="product-group">
              {labelOrUnavailable(product.labels.group, product.groupId)}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.classId}</dt>
            <dd data-testid="product-class">
              {labelOrUnavailable(product.labels.class, product.classId)}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.familyId}</dt>
            <dd data-testid="product-family">
              {labelOrUnavailable(product.labels.family, product.familyId)}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.primaryStockUnit}</dt>
            <dd data-testid="product-primary-stock-unit">
              {product.labels.primaryStockUnit ?? UNASSIGNED}
            </dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.templateVersion}</dt>
            {/* Carried verbatim: a master change must not rewrite what a record meant. */}
            <dd>{references.templateVersion ?? UNASSIGNED}</dd>
          </div>
          <div><dt>Teknik şablon</dt><dd>{labelOrUnavailable(product.labels.template ?? null, references.templateId)}</dd></div>
          {(['cins', 'tur', 'tip'] as const).map((fact, index) => <div key={fact}>
            <dt>{['Cins', 'Tür', 'Tip'][index]}</dt>
            <dd>{product.labels.familyFacts?.[fact] ?? UNASSIGNED}</dd>
          </div>)}
          {Object.entries(references.attributes ?? {}).map(([key, value]) => <div key={key}>
            <dt>{key}</dt>
            <dd>{product.labels.attributes?.[key] ?? (typeof value === 'number' ? String(value) : 'Katalog adı okunamıyor')}</dd>
          </div>)}
          {UNIT_ROLE.map((role) => (
            <div key={role}>
              <dt>{UNIT_ROLE_LABEL[role]}</dt>
              {/*
                An unassigned unit shows "atanmadı" and never falls back to the stock
                unit. DEC-PRD-006 keeps the six roles separate; a fallback would be
                the screen inventing a conversion.
              */}
              <dd className={styles.identifier} data-testid={`product-unit-${role}`}>
                {labelOrUnavailable(product.labels.units?.[role] ?? null, references.units[role])}
              </dd>
            </div>
          ))}
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.trackingPolicy}</dt>
            <dd className={styles.identifier}>{references.trackingPolicyId ?? UNASSIGNED}</dd>
          </div>
        </dl>
        {/*
          DEC-PRD-003: the card holds the tracking POLICY reference; the lot, the
          origin and the quality/defect status belong to the physical label record.
          Without this an empty policy reads as "this product has no lots".
        */}
        <p className={styles.note}>{PRODUCT_DETAIL_TEXT.trackingPolicyNote}</p>
        <p className={styles.note}>{PENDING_REFERENCE_NOTE}</p>
      </section>

      <section className={styles.section} aria-labelledby="product-audit-heading">
        <h2 id="product-audit-heading">{PRODUCT_DETAIL_TEXT.auditHeading}</h2>
        <dl className={styles.definitionList}>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.createdAt}</dt>
            <dd>{formatInstant(product.createdAtUtc)}</dd>
          </div>
          <div>
            <dt>{PRODUCT_DETAIL_TEXT.updatedAt}</dt>
            <dd>{formatInstant(product.updatedAtUtc)}</dd>
          </div>
          <div>
            <dt>Sürüm</dt>
            <dd>{product.version}</dd>
          </div>
        </dl>
      </section>

      {history}
    </article>
  )
}
