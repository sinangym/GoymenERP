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
import { activateProduct, fetchProductCard, fetchProductHistory } from './api'
import type { ActivateProductPayload } from './api'
import { PRODUCT_ROLE, UNIT_ROLE, isComplete, type ProductCard, type ProductMutationResult } from './contract'
import {
  COMPLETENESS_LABEL,
  COMPLETENESS_UNKNOWN_LABEL,
  PENDING_REFERENCE_NOTE,
  PRODUCT_ACTIVATION_TEXT,
  PRODUCT_DETAIL_TEXT,
  PRODUCT_HISTORY_TEXT,
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
  forgetAttempt,
  recallAttempt,
  rememberAttempt,
} from './pendingAttempt'
import { UnresolvedExitGuard } from './UnresolvedExitGuard'
import styles from './ProductDetailPage.module.css'

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
      activationControl={
        canActivate ? (
          <ProductActivationAction
            key={card.product.id}
            card={card}
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

type ActivationAttempt = AttemptState<ActivateProductPayload, ProductMutationResult>
const PRODUCT_ACTIVATION_DIRTY_SCOPE = 'product.activation'

function ProductActivationAction({
  card,
  onActivated,
  onReload,
}: {
  readonly card: ProductCard
  readonly onActivated: (card: ProductCard) => void
  /** Re-reads the card from its own endpoint; resolves once that read has answered. */
  readonly onReload: () => Promise<unknown>
}) {
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

  function startOrRetry() {
    const pending = liveSnapshot(attempt)
    if (pending !== null) {
      void send(pending)
      return
    }
    if (origin === null) return
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
        <button type="button" className={styles.activateButton} onClick={startOrRetry}>
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
        <button
          type="button"
          className={styles.activateButton}
          disabled={attempt.phase === 'submitting'}
          onClick={startOrRetry}
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
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace, entryGeneration, 'product-history', productId],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchProductHistory(productId, { companyId: origin }, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

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
}: {
  readonly card: ProductCard
  readonly history: React.ReactNode
  readonly backLink: React.ReactNode
  readonly editLink: React.ReactNode
  readonly activationControl: React.ReactNode
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
          <span
            className={styles.badge}
            data-lifecycle={product.lifecycle.known ? product.lifecycle.value : 'unknown'}
          >
            {lifecycleLabel(product.lifecycle)}
          </span>
          <span className={styles.lifecycleMeaning}>{lifecycleMeaning(product.lifecycle)}</span>
        </div>
      </header>

      {/* Only for a KNOWN Inactive: an unrecognised status must not inherit this claim. */}
      {product.lifecycle.known && product.lifecycle.value === 'Inactive' ? (
        <p className={styles.inactiveBanner} role="status">
          Bu ürün her iki şirkette de yeni kullanıma kapalıdır. Geçmiş kayıtlar görünür kalır.
        </p>
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
