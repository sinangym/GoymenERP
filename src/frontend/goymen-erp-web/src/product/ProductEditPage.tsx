/**
 * U-02 temel düzenleme — Draft basic edit, pinned to
 * `PATCH /api/v1/products/{id}/draft` (`EditProductDraftRequest`).
 *
 * This closes the §19.2 journey: list → card → create Draft → server UUID/code →
 * reopen → correct the basic fields against the CURRENT version with a reason →
 * back.
 *
 * The rules that shape it, each of which is a way to lose someone's work:
 *  - the payload is a DIFF, not a form. An omitted member is unchanged, and the
 *    contract models that as `Optional<T>` with no value;
 *  - `expectedVersion` comes from the card as READ — never from a conflict
 *    response, which would turn a refusal into a blind overwrite;
 *  - a stale refusal shows both versions and stops. No rebase, no force;
 *  - an unknown outcome retries the SAME key and never claims a rollback;
 *  - a committed context change abandons the attempt visibly.
 *
 * Editable here: `name`, `classId`, `familyId` and the MANUAL roles. Not editable:
 * group and code (the contract freezes both in basic edit), and the `references`
 * block — the coordinator keeps attribute/unit completeness in the next slice.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { editProductDraft, fetchProductCard, fetchReferenceOptions } from './api'
import type { EditProductDraftPayload } from './api'
import {
  PRODUCT_ROLE,
  currentVersionOf,
  productFieldErrorsOf,
  type ProductCard,
  type ProductMutationResult,
  type ProductReferenceOptions,
} from './contract'
import {
  EDIT_FIELD,
  EDIT_FIELD_LABEL,
  EDIT_FIELD_ORDER,
  editFormFrom,
  isEditDirty,
  isRoleEditable,
  mergeServerEditErrors,
  roleChoiceOf,
  validateEditForm,
  type EditFieldId,
  type EditFormErrors,
  type EditFormValues,
} from './editForm'
import {
  generateAttemptKey,
  hasUnresolvedWork,
  isFormFrozen,
  isOutcomeStillUnknown,
  isSamePayload,
  liveSnapshot,
  nextState,
  type AttemptSnapshot,
  type AttemptState,
} from './commandAttempt'
import {
  CATALOG_EMPTY,
  CATALOG_UNAVAILABLE,
  PENDING_REFERENCE_NOTE,
  PRODUCT_EDIT_TEXT,
  ROLE_LABEL,
  ROLE_PROVENANCE_LABEL,
  fieldErrorText,
} from './productText'
import { PRODUCT_CAPABILITY, productDetailPath } from './capabilities'
import { classifyOutcome } from '../shell/uiState'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { UnresolvedExitGuard } from './UnresolvedExitGuard'
import { editScope, forgetAttempt, recallAttempt, rememberAttempt } from './pendingAttempt'
import styles from './ProductCreatePage.module.css'

type EditAttempt = AttemptState<EditProductDraftPayload, ProductMutationResult>

/**
 * The shell's dirty scope for this screen — see the create page for why registering
 * matters: without it the company picker switches straight through and the
 * namespace remount discards the form and its idempotency key unasked.
 */
export const PRODUCT_EDIT_DIRTY_SCOPE = 'product.edit'

const ROLE_CHOICES = [
  { value: 'yes', label: 'Evet' },
  { value: 'no', label: 'Hayır' },
  { value: 'unset', label: 'Belirtilmedi' },
] as const

export function ProductEditPage() {
  const shell = useShellState()
  const { productId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  const entryGeneration = shell.routeEntryGeneration

  const cardQuery = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'product-edit-card', productId ?? ''],
    enabled: namespace !== null && productId !== undefined && productId.length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchProductCard(productId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const outcome = cardQuery.data
  if (outcome === undefined || outcome.type !== 'ok-single') {
    return (
      <section className={styles.page} aria-label={PRODUCT_EDIT_TEXT.title}>
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject="ürün"
          onRetry={() => void cardQuery.refetch()}
        />
      </section>
    )
  }

  return (
    <ProductEditForm
      // Remounted per record AND per version AND per context, so a seeded form can
      // never carry a previous revision's values or a previous actor's authority.
      key={`${namespace ?? 'no-context'}:${outcome.data.product.id}:${outcome.data.product.version}`}
      card={outcome.data}
      namespace={namespace}
      entryGeneration={entryGeneration}
      onReload={() => void cardQuery.refetch()}
    />
  )
}

function ProductEditForm({
  card,
  namespace,
  entryGeneration,
  onReload,
}: {
  readonly card: ProductCard
  readonly namespace: string | null
  readonly entryGeneration: number
  readonly onReload: () => void
}) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const navigate = useNavigate()
  const canOpenDetail = hasCapability(shell, PRODUCT_CAPABILITY.productsDetail)

  const [values, setValues] = useState<EditFormValues>(() => editFormFrom(card))
  const [errors, setErrors] = useState<EditFormErrors>({})
  const [submitted, setSubmitted] = useState(false)
  /**
   * Seeded from the pending store — see the create page. Scoped to THIS product, so
   * an unresolved edit of one record cannot surface on another.
   */
  const [attempt, setAttempt] = useState<EditAttempt>(
    () => recallAttempt<EditProductDraftPayload, ProductMutationResult>(
      namespace,
      editScope(card.product.id),
    ) ?? { phase: 'idle' },
  )
  const [staleCurrentVersion, setStaleCurrentVersion] = useState<string | null>(null)

  const summaryRef = useRef<HTMLDivElement>(null)
  const fieldRefs = useRef<Partial<Record<EditFieldId, HTMLElement | null>>>({})

  const dirty = isEditDirty(card, values)
  const frozen = isFormFrozen(attempt)
  const unresolved = hasUnresolvedWork(attempt, dirty)

  /**
   * The shell's dirty gate. A frozen or unresolved attempt counts as dirty even
   * when the fields look pristine: the pending write is the thing a silent company
   * switch must not carry away.
   */
  useEffect(() => {
    dispatch({ type: unresolved ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: PRODUCT_EDIT_DIRTY_SCOPE })
  }, [unresolved, dispatch])

  useEffect(() => {
    rememberAttempt(namespace, editScope(card.product.id), attempt)
  }, [namespace, card.product.id, attempt])
  useEffect(
    () => () => dispatch({ type: 'DIRTY_CLEAR', scope: PRODUCT_EDIT_DIRTY_SCOPE }),
    [dispatch],
  )

  /**
   * Class and family choices, scoped to the record own group — which basic edit
   * cannot change, so the scope is fixed for the life of this form.
   */
  const optionsQuery = useQuery({
    queryKey: [
      namespace ?? 'no-context',
      entryGeneration,
      'product-reference-options',
      card.product.groupId,
      values.classId,
    ],
    enabled: namespace !== null,
    queryFn: async ({ signal }) => {
      // Same authority as the card read, so a refusal reaches the same boundary
      // instead of leaving the open card looking valid under revoked authority.
      const snapshot = boundary.capture()
      const outcome = await fetchReferenceOptions(
        card.product.groupId,
        values.classId === '' ? null : values.classId,
        signal,
      )
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const options: ProductReferenceOptions | null =
    optionsQuery.data?.type === 'ok-single' ? optionsQuery.data.data : null
  const classIds = useMemo(() => (options?.classes ?? []).map((entry) => entry.id), [options])
  const familyIds = useMemo(() => (options?.families ?? []).map((entry) => entry.id), [options])
  /**
   * An UNAVAILABLE catalogue is not an empty one, so the field is closed and says it
   * could not be read rather than offering "nothing defined yet" for something we
   * never saw.
   */
  const classesUnavailable = options?.unavailableCatalogs.includes('classes') ?? false
  const familiesUnavailable = options?.unavailableCatalogs.includes('families') ?? false

  const productId = card.product.id
  /** The attempt as it stood when the send began — see the create page. */
  const attemptRef = useRef<EditAttempt>(attempt)
  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  const send = useCallback(
    async (snapshot: AttemptSnapshot<EditProductDraftPayload>) => {
      // Read BEFORE the submitting transition: the ref follows the rendered state,
      // so reading it after would see `submitting` and lose the very history this
      // exists to carry.
      const previous = attemptRef.current
      setAttempt({ phase: 'submitting', snapshot })
      const securitySnapshot = boundary.capture()
      const result = await editProductDraft(productId, snapshot.payload, {
        idempotencyKey: snapshot.key,
      })
      if (result.type === 'http') boundary.report(result.status, securitySnapshot)
      // `previous` is passed so a refusal that only answered a RETRY cannot be read
      // as proof that the earlier send did not apply.
      const next = nextState(snapshot, result, previous)

      if (next.phase === 'failed' && next.failure === 'stale-version') {
        /**
         * Show the current version, keep the input, and STOP. The client never
         * adopts the returned version as its own `expectedVersion` — that would
         * rebase the edit onto a change this person has not seen, which is an
         * overwrite wearing a conflict's clothes.
         */
        setStaleCurrentVersion(result.type === 'http' ? currentVersionOf(result.body) : null)
        setAttempt(next)
        return
      }
      if (next.phase === 'failed' && next.failure === 'validation') {
        const serverErrors: Record<string, string> = {}
        for (const error of productFieldErrorsOf(result.type === 'http' ? result.body : null)) {
          serverErrors[error.field] = fieldErrorText(error.code)
        }
        setErrors((current) => mergeServerEditErrors(current, serverErrors))
        setAttempt({ ...next, fieldErrors: serverErrors })
        return
      }
      setAttempt(next)
    },
    [productId, boundary],
  )

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (frozen) return
    setSubmitted(true)

    const validation = validateEditForm(card, values, classIds, familyIds)
    if (!validation.ok) {
      setErrors(validation.errors)
      return
    }
    setErrors({})
    setStaleCurrentVersion(null)

    // The live snapshot now includes a refusal that left an earlier send
    // unresolved: resubmitting the same payload must reuse that key.
    const live = liveSnapshot(attempt)
    if (live !== null && isSamePayload(live.payload, validation.payload)) {
      void send(live)
      return
    }
    void send({
      key: generateAttemptKey(),
      payload: validation.payload,
      namespace: namespace ?? '',
    })
  }

  function retrySameAttempt() {
    const live = liveSnapshot(attempt)
    if (live === null) return
    void send(live)
  }

  const errorList = useMemo(
    () =>
      EDIT_FIELD_ORDER.filter((field) => errors[field] !== undefined).map((field) => ({
        field,
        message: errors[field] as string,
      })),
    [errors],
  )

  useEffect(() => {
    if (submitted && errorList.length > 0) summaryRef.current?.focus()
  }, [submitted, errorList.length])

  /** Cancel navigates plainly; the blocker owns the question. See the create page. */
  function leave(to: string) {
    void navigate(to)
  }

  if (attempt.phase === 'completed') {
    const saved = attempt.result.card.product
    forgetAttempt(editScope(card.product.id))
    return (
      <section className={styles.page} aria-labelledby="product-edited-heading">
        <div className={styles.successBlock} role="status">
          <h1 id="product-edited-heading" className={styles.title}>
            {PRODUCT_EDIT_TEXT.savedTitle}
          </h1>
          {/* Canonical values from the SERVER's response, never what was typed. */}
          <p className={styles.createdCode}>
            {saved.code} — {saved.name}
          </p>
          <p className={styles.note}>
            {PRODUCT_EDIT_TEXT.currentVersion}: {saved.version}
          </p>
          {attempt.result.isReplay ? (
            <p className={styles.note}>{PRODUCT_EDIT_TEXT.replayedNote}</p>
          ) : null}
          <div className={styles.actions}>
            {canOpenDetail ? (
              <Link className={styles.primaryAction} to={productDetailPath(saved.id)}>
                {PRODUCT_EDIT_TEXT.backToCard}
              </Link>
            ) : null}
          </div>
        </div>
      </section>
    )
  }

  return (
    <section className={styles.page} aria-labelledby="product-edit-heading">
      <h1 id="product-edit-heading" className={styles.title}>
        {PRODUCT_EDIT_TEXT.title}
      </h1>
      <p className={styles.note}>
        {card.product.code} — {card.product.name}
      </p>
      <p className={styles.note}>{PRODUCT_EDIT_TEXT.globalImpactNote}</p>
      <p className={styles.note}>{PRODUCT_EDIT_TEXT.diffNote}</p>
      <p className={styles.note}>{PRODUCT_EDIT_TEXT.scopeNote}</p>
      <p className={styles.note}>{PENDING_REFERENCE_NOTE}</p>

      {submitted && errorList.length > 0 ? (
        <div
          className={styles.errorSummary}
          role="alert"
          tabIndex={-1}
          ref={summaryRef}
          data-testid="product-edit-error-summary"
        >
          <p className={styles.errorSummaryTitle}>{PRODUCT_EDIT_TEXT.errorSummaryTitle}</p>
          <p>{PRODUCT_EDIT_TEXT.errorSummaryIntro}</p>
          <ul>
            {errorList.map(({ field, message }) => (
              <li key={field}>
                <button
                  type="button"
                  className={styles.errorLink}
                  onClick={() => fieldRefs.current[field]?.focus()}
                >
                  {EDIT_FIELD_LABEL[field]}: {message}
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {attempt.phase === 'failed' &&
      !isOutcomeStillUnknown(attempt) &&
      attempt.failure === 'stale-version' ? (
        <div className={styles.errorSummary} role="alert" data-testid="product-edit-stale">
          <p className={styles.errorSummaryTitle}>{PRODUCT_EDIT_TEXT.staleTitle}</p>
          <p>{PRODUCT_EDIT_TEXT.staleDetail}</p>
          <p>
            {PRODUCT_EDIT_TEXT.staleReadVersion}: {card.product.version}
            {staleCurrentVersion !== null
              ? ` · ${PRODUCT_EDIT_TEXT.staleCurrentVersion}: ${staleCurrentVersion}`
              : ''}
          </p>
          <p>{PRODUCT_EDIT_TEXT.staleKeepDraft}</p>
          {/*
            The ONLY way forward is a fresh read. There is deliberately no "save
            anyway": the other editor's change is a fact this user has not seen.
          */}
          <button type="button" className={styles.primaryAction} onClick={onReload}>
            {PRODUCT_EDIT_TEXT.staleReload}
          </button>
        </div>
      ) : null}

      {attempt.phase === 'failed' &&
      !isOutcomeStillUnknown(attempt) &&
      attempt.failure === 'lifecycle-conflict' ? (
        <div className={styles.errorSummary} role="alert" data-testid="product-edit-lifecycle">
          <p className={styles.errorSummaryTitle}>{PRODUCT_EDIT_TEXT.lifecycleConflictTitle}</p>
          <p>{PRODUCT_EDIT_TEXT.lifecycleConflictDetail}</p>
        </div>
      ) : null}

      {attempt.phase === 'failed' &&
      !isOutcomeStillUnknown(attempt) &&
      attempt.failure === 'not-found' ? (
        <div className={styles.errorSummary} role="alert">
          <p className={styles.errorSummaryTitle}>{PRODUCT_EDIT_TEXT.notFoundTitle}</p>
          <p>{PRODUCT_EDIT_TEXT.notFoundDetail}</p>
        </div>
      ) : null}

      {attempt.phase === 'failed' &&
      !isOutcomeStillUnknown(attempt) &&
      (attempt.failure === 'unauthenticated' || attempt.failure === 'forbidden') ? (
        <div className={styles.errorSummary} role="alert">
          <p className={styles.errorSummaryTitle}>
            {attempt.failure === 'forbidden' ? 'Yetkiniz yok' : 'Oturum sonlandı'}
          </p>
          <p>Değişiklik uygulanmadı.</p>
        </div>
      ) : null}

      {attempt.phase === 'failed' &&
      !isOutcomeStillUnknown(attempt) &&
      (attempt.failure === 'unexpected' ||
        attempt.failure === 'metadata' ||
        attempt.failure === 'idempotency-conflict') ? (
        <div className={styles.errorSummary} role="alert">
          <p className={styles.errorSummaryTitle}>{PRODUCT_EDIT_TEXT.errorSummaryTitle}</p>
          <p>{PRODUCT_EDIT_TEXT.saveFailedRetained}</p>
        </div>
      ) : null}

      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.unknownBlock} role="alert" data-testid="product-edit-unknown">
          <p className={styles.errorSummaryTitle}>{PRODUCT_EDIT_TEXT.unknownOutcomeTitle}</p>
          {/* No rollback is claimed: the change may have applied. */}
          <p>{PRODUCT_EDIT_TEXT.unknownOutcomeDetail}</p>
          {/*
            A refusal that only answered the RETRY settles nothing about the first
            send. Saying "uygulanmadı" there would be a claim the refusal cannot
            support — the server re-authorizes before it can even read a stored
            result, so a 403 is a denial of replay access, not a rollback.
          */}
          {attempt.phase === 'failed' ? (
            <p>{PRODUCT_EDIT_TEXT.retryRefusedStillUnknown}</p>
          ) : null}
          <button
            type="button"
            className={styles.primaryAction}
            onClick={retrySameAttempt}
          >
            {PRODUCT_EDIT_TEXT.retrySameAttempt}
          </button>
        </div>
      ) : null}

      <form className={styles.form} onSubmit={onSubmit} noValidate>
        <div className={styles.field}>
          <label htmlFor={EDIT_FIELD.name}>{PRODUCT_EDIT_TEXT.nameLabel}</label>
          <input
            id={EDIT_FIELD.name}
            type="text"
            value={values.name}
            maxLength={250}
            disabled={frozen}
            aria-invalid={errors.name !== undefined || undefined}
            aria-describedby={errors.name !== undefined ? `${EDIT_FIELD.name}-error` : undefined}
            ref={(el) => {
              fieldRefs.current[EDIT_FIELD.name] = el
            }}
            onChange={(event) => setValues((v) => ({ ...v, name: event.target.value }))}
          />
          {errors.name !== undefined ? (
            <p id={`${EDIT_FIELD.name}-error`} className={styles.fieldError}>
              {errors.name}
            </p>
          ) : null}
        </div>

        {/*
          References basic edit may change. Emptying one sends an explicit `null`,
          which CLEARS it — a distinct operation from leaving the member out, and the
          hint says so rather than leaving the user to guess.
        */}
        <div className={styles.field}>
          <label htmlFor={EDIT_FIELD.classId}>{PRODUCT_EDIT_TEXT.classLabel}</label>
          <select
            id={EDIT_FIELD.classId}
            value={values.classId}
            disabled={frozen || classesUnavailable}
            aria-invalid={errors.classId !== undefined || undefined}
            aria-describedby={`${EDIT_FIELD.classId}-hint`}
            ref={(el) => {
              fieldRefs.current[EDIT_FIELD.classId] = el
            }}
            onChange={(event) =>
              setValues((v) => ({ ...v, classId: event.target.value, familyId: '' }))
            }
          >
            <option value="">{PRODUCT_EDIT_TEXT.chooseNone}</option>
            {/*
              The record OWN value stays selectable even when the catalogue no longer
              offers it: otherwise merely opening the form would silently drop a
              reference the user never touched.
            */}
            {card.product.classId !== null && !classIds.includes(card.product.classId) ? (
              <option value={card.product.classId}>
                {card.product.labels.class ?? card.product.classId}
              </option>
            ) : null}
            {(options?.classes ?? []).map((entry) => (
              <option key={entry.id} value={entry.id}>
                {entry.displayName}
              </option>
            ))}
          </select>
          <p id={`${EDIT_FIELD.classId}-hint`} className={styles.hint}>
            {classesUnavailable
              ? CATALOG_UNAVAILABLE
              : (options?.classes.length ?? 0) === 0
                ? CATALOG_EMPTY
                : PRODUCT_EDIT_TEXT.clearableHint}
          </p>
          {errors.classId !== undefined ? (
            <p className={styles.fieldError}>{errors.classId}</p>
          ) : null}
        </div>

        <div className={styles.field}>
          <label htmlFor={EDIT_FIELD.familyId}>{PRODUCT_EDIT_TEXT.familyLabel}</label>
          <select
            id={EDIT_FIELD.familyId}
            value={values.familyId}
            disabled={frozen || familiesUnavailable}
            aria-invalid={errors.familyId !== undefined || undefined}
            aria-describedby={`${EDIT_FIELD.familyId}-hint`}
            ref={(el) => {
              fieldRefs.current[EDIT_FIELD.familyId] = el
            }}
            onChange={(event) => setValues((v) => ({ ...v, familyId: event.target.value }))}
          >
            <option value="">{PRODUCT_EDIT_TEXT.chooseNone}</option>
            {card.product.familyId !== null && !familyIds.includes(card.product.familyId) ? (
              <option value={card.product.familyId}>
                {card.product.labels.family ?? card.product.familyId}
              </option>
            ) : null}
            {(options?.families ?? []).map((entry) => (
              <option key={entry.id} value={entry.id}>
                {entry.displayName}
              </option>
            ))}
          </select>
          <p id={`${EDIT_FIELD.familyId}-hint`} className={styles.hint}>
            {familiesUnavailable
              ? CATALOG_UNAVAILABLE
              : (options?.families.length ?? 0) === 0
                ? CATALOG_EMPTY
                : PRODUCT_EDIT_TEXT.clearableHint}
          </p>
          {errors.familyId !== undefined ? (
            <p className={styles.fieldError}>{errors.familyId}</p>
          ) : null}
        </div>

        <fieldset
          className={styles.roles}
          ref={(el) => {
            fieldRefs.current[EDIT_FIELD.roles] = el
          }}
          tabIndex={-1}
        >
          <legend>{PRODUCT_EDIT_TEXT.rolesLabel}</legend>
          <p className={styles.hint}>{PRODUCT_EDIT_TEXT.derivedRoleNote}</p>
          {errors.roles !== undefined ? (
            <p className={styles.fieldError}>{errors.roles}</p>
          ) : null}
          {PRODUCT_ROLE.map((role) => {
            const editable = isRoleEditable(card, role)
            return (
              <fieldset key={role} className={styles.roleRow}>
                <legend>{ROLE_LABEL[role]}</legend>
                {editable ? (
                  ROLE_CHOICES.map((choice) => (
                    <label key={choice.value} className={styles.radio}>
                      <input
                        type="radio"
                        name={`edit-role-${role}`}
                        value={choice.value}
                        checked={values.roles[role] === choice.value}
                        disabled={frozen}
                        onChange={() =>
                          setValues((v) => ({
                            ...v,
                            roles: { ...v.roles, [role]: choice.value },
                          }))
                        }
                      />
                      {choice.label}
                    </label>
                  ))
                ) : (
                  /*
                    A derived role is the server's conclusion from a production
                    relationship, and `PreserveDerived` rejects an overwrite. It is
                    shown with its provenance and NO control — a disabled radio group
                    would suggest the value is merely locked right now rather than
                    not this screen's to set.
                  */
                  <span className={styles.hint} data-testid={`edit-role-derived-${role}`}>
                    {roleChoiceOf(card, role) === 'yes' ? 'Evet' : 'Hayır'} —{' '}
                    {ROLE_PROVENANCE_LABEL.derived}
                  </span>
                )}
              </fieldset>
            )
          })}
        </fieldset>

        <div className={styles.field}>
          <label htmlFor={EDIT_FIELD.reason}>{PRODUCT_EDIT_TEXT.reasonLabel}</label>
          <input
            id={EDIT_FIELD.reason}
            type="text"
            value={values.reason}
            maxLength={1000}
            disabled={frozen}
            aria-required="true"
            aria-invalid={errors.reason !== undefined || undefined}
            aria-describedby={`${EDIT_FIELD.reason}-hint${
              errors.reason !== undefined ? ` ${EDIT_FIELD.reason}-error` : ''
            }`}
            ref={(el) => {
              fieldRefs.current[EDIT_FIELD.reason] = el
            }}
            onChange={(event) => setValues((v) => ({ ...v, reason: event.target.value }))}
          />
          <p id={`${EDIT_FIELD.reason}-hint`} className={styles.hint}>
            {PRODUCT_EDIT_TEXT.reasonHint}
          </p>
          {errors.reason !== undefined ? (
            <p id={`${EDIT_FIELD.reason}-error`} className={styles.fieldError}>
              {errors.reason}
            </p>
          ) : null}
        </div>

        <div className={styles.actions}>
          <button
            type="submit"
            className={styles.primaryAction}
            disabled={frozen}
          >
            {attempt.phase === 'submitting'
              ? PRODUCT_EDIT_TEXT.submitting
              : PRODUCT_EDIT_TEXT.submit}
          </button>
          <button
            type="button"
            className={styles.secondaryAction}
            disabled={attempt.phase === 'submitting'}
            onClick={() => leave(productDetailPath(card.product.id))}
          >
            {PRODUCT_EDIT_TEXT.cancel}
          </button>
        </div>
      </form>

      {/* Every router exit goes through this one guard. */}
      <UnresolvedExitGuard
        active={unresolved}
        title={PRODUCT_EDIT_TEXT.unsavedTitle}
        detail={PRODUCT_EDIT_TEXT.unsavedDetail}
        stayLabel={PRODUCT_EDIT_TEXT.unsavedStay}
        leaveLabel={PRODUCT_EDIT_TEXT.unsavedLeave}
        onLeave={() => forgetAttempt(editScope(card.product.id))}
      />
    </section>
  )
}
