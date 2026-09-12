/**
 * U-02 Yeni Ürün — Draft create, pinned to `POST /api/v1/products`.
 *
 * Offers exactly the create minimum the contract states — `name` plus a real
 * `groupId` from `GET /products/code-groups` — and the roles, whose vocabulary the
 * contract fully defines.
 *
 * Deliberately absent, each for a stated reason:
 *  - a product CODE field: the server mints it from the group's prefix, and the
 *    admin manual-code exception is a later explicit command;
 *  - `references` (template + version, attributes, the six units, tracking policy):
 *    §19.6 and the coordinator keep attribute/unit completeness in the NEXT slice;
 *  - activation: the contract includes no activation command;
 *  - AI drafting and duplicate-similarity warnings: both need a server surface that
 *    does not exist. Exact collisions are still handled — from the server's answer.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { createProductDraft, fetchCodeGroups, fetchReferenceOptions } from './api'
import type { CreateProductDraftPayload } from './api'
import {
  PRODUCT_PROBLEM_CODE,
  PRODUCT_ROLE,
  productFieldErrorsOf,
  type ProductCodeGroup,
  type ProductMutationResult,
  type ProductReferenceOptions,
  type ProductRoleName,
} from './contract'
import {
  CREATE_FIELD,
  CREATE_FIELD_LABEL,
  CREATE_FIELD_ORDER,
  EMPTY_CREATE_FORM,
  isCreateFormPristine,
  mergeServerErrors,
  validateCreateForm,
  type CreateFieldId,
  type CreateFormErrors,
  type CreateFormValues,
  type RoleChoice,
} from './createForm'
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
  PRODUCT_CREATE_TEXT,
  ROLE_LABEL,
  fieldErrorText,
} from './productText'
import {
  PRODUCT_CAPABILITY,
  PRODUCT_CAPABILITY_ROUTE,
  productDetailPath,
} from './capabilities'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { UnresolvedExitGuard } from './UnresolvedExitGuard'
import { UnsavedDialog } from './UnsavedDialog'
import styles from './ProductCreatePage.module.css'

type CreateAttempt = AttemptState<CreateProductDraftPayload, ProductMutationResult>

/**
 * The shell's dirty scope for this screen.
 *
 * Registering here is what makes a company switch ASK instead of proceeding: the
 * reducer turns `REQUEST_SWITCH` into `confirming-switch` while any scope is dirty.
 * Without it the picker switched straight through, and the remount below then wiped
 * the form and its idempotency key with no one being asked — which is precisely the
 * silent abandonment §19.5 forbids.
 */
export const PRODUCT_CREATE_DIRTY_SCOPE = 'product.create'

const ROLE_CHOICES: readonly { readonly value: RoleChoice; readonly label: string }[] = [
  { value: 'yes', label: PRODUCT_CREATE_TEXT.roleYes },
  { value: 'no', label: PRODUCT_CREATE_TEXT.roleNo },
  // Last, and the initial value: an unanswered role stays unanswered.
  { value: 'unset', label: PRODUCT_CREATE_TEXT.roleUnset },
]

export function ProductCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const entryGeneration = shell.routeEntryGeneration

  /**
   * Reference reads go through the SAME security boundary as the business reads.
   *
   * They are authenticated reads against the same global authority, so a 401/403
   * here is the same fact as a 401/403 on the list — and letting it pass unreported
   * left the capability snapshot and the card cache looking valid under authority
   * the server had just refused.
   */
  const boundary = useAuthBoundary()
  const groupQuery = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'product-code-groups'],
    enabled: namespace !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchCodeGroups(signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const groups =
    groupQuery.data?.type === 'ok' ? (groupQuery.data.data as readonly ProductCodeGroup[]) : null

  return (
    <ProductCreateForm
      key={namespace ?? 'no-context'}
      namespace={namespace}
      groups={groups}
      groupsLoading={groupQuery.isPending}
      entryGeneration={entryGeneration}
      onRetryGroups={() => void groupQuery.refetch()}
    />
  )
}

function ProductCreateForm({
  namespace,
  groups,
  groupsLoading,
  entryGeneration,
  onRetryGroups,
}: {
  readonly namespace: string | null
  /** `null` means the code-group read has not produced a usable list. */
  readonly groups: readonly ProductCodeGroup[] | null
  readonly groupsLoading: boolean
  readonly entryGeneration: number
  readonly onRetryGroups: () => void
}) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const navigate = useNavigate()
  const canOpenDetail = hasCapability(shell, PRODUCT_CAPABILITY.productsDetail)
  const canReturnToList = hasCapability(shell, PRODUCT_CAPABILITY.productsList)

  const [values, setValues] = useState<CreateFormValues>(EMPTY_CREATE_FORM)
  const [errors, setErrors] = useState<CreateFormErrors>({})
  const [submitted, setSubmitted] = useState(false)
  const [attempt, setAttempt] = useState<CreateAttempt>({ phase: 'idle' })
  const [leaveIntent, setLeaveIntent] = useState<string | null>(null)

  const summaryRef = useRef<HTMLDivElement>(null)
  const fieldRefs = useRef<Partial<Record<CreateFieldId, HTMLElement | null>>>({})

  const dirty = !isCreateFormPristine(values)
  const frozen = isFormFrozen(attempt)
  const unresolved = hasUnresolvedWork(attempt, dirty)

  /**
   * Register with the SHELL's dirty gate, not just with `beforeunload`.
   *
   * §19.4 says an attempt must not be carried into a new context — and this screen
   * enforces that by remounting on the namespace key. But a remount is destruction,
   * not protection: on its own it threw away a dirty form and an unresolved
   * idempotency key without anyone being asked. The dirty scope is what turns the
   * company switch into `confirming-switch` first, so the discard becomes the
   * user's explicit choice and the remount only ever happens after it.
   *
   * A frozen or unresolved attempt counts as dirty even when the fields look
   * pristine: the pending write is the thing a silent switch must not carry.
   */
  useEffect(() => {
    dispatch({ type: unresolved ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: PRODUCT_CREATE_DIRTY_SCOPE })
  }, [unresolved, dispatch])
  useEffect(
    () => () => dispatch({ type: 'DIRTY_CLEAR', scope: PRODUCT_CREATE_DIRTY_SCOPE }),
    [dispatch],
  )

  const groupIds = useMemo(() => (groups ?? []).map((group) => group.id), [groups])
  /** Creation is impossible without a real code group (§19.3). */
  const canCreate = groups !== null && groups.length > 0

  /**
   * Class and family choices, scoped to the chosen group (and then the chosen
   * class), because that is how the server filters them — offering a family from
   * another group would be offering a choice the create then rejects.
   *
   * Not fetched until a group is picked: there is nothing to scope by, and asking
   * without one would be a read whose answer could not be trusted.
   */
  const optionsQuery = useQuery({
    queryKey: [
      namespace ?? 'no-context',
      entryGeneration,
      'product-reference-options',
      values.groupId,
      values.classId,
    ],
    enabled: namespace !== null && values.groupId !== '',
    queryFn: async ({ signal }) =>
      fetchReferenceOptions(values.groupId, values.classId === '' ? null : values.classId, signal),
  })

  const options: ProductReferenceOptions | null =
    optionsQuery.data?.type === 'ok-single' ? optionsQuery.data.data : null
  const classIds = useMemo(() => (options?.classes ?? []).map((entry) => entry.id), [options])
  const familyIds = useMemo(() => (options?.families ?? []).map((entry) => entry.id), [options])
  /**
   * An UNAVAILABLE catalogue is not an empty one. README: it "must not produce
   * made-up choices", so the field is closed and says it could not be read — while
   * a genuinely empty catalogue stays open and says it is empty. Showing both the
   * same way would answer "nothing is defined yet" for something we never saw.
   */
  const classesUnavailable = options?.unavailableCatalogs.includes('classes') ?? false
  const familiesUnavailable = options?.unavailableCatalogs.includes('families') ?? false

  /**
   * A class chosen under one group is meaningless under another, and the family
   * list is scoped by class. Clearing on change prevents submitting a reference the
   * current options never offered — which the validator would reject anyway, but as
   * a confusing error rather than as a field that simply reset.
   */
  function chooseGroup(groupId: string) {
    setValues((current) => ({ ...current, groupId, classId: '', familyId: '' }))
  }
  function chooseClass(classId: string) {
    setValues((current) => ({ ...current, classId, familyId: '' }))
  }

  /**
   * The attempt as it stood when the send began. A ref rather than a closure
   * capture: `send` is memoised, and reading stale state here would lose exactly
   * the history this fix exists to preserve.
   */
  const attemptRef = useRef<CreateAttempt>(attempt)
  useEffect(() => {
    attemptRef.current = attempt
  }, [attempt])

  const send = useCallback(async (snapshot: AttemptSnapshot<CreateProductDraftPayload>) => {
    // Read BEFORE the submitting transition: the ref follows the rendered state, so
    // reading it after would see `submitting` and lose the very history this exists
    // to carry.
    const previous = attemptRef.current
    setAttempt({ phase: 'submitting', snapshot })
    // The write is an authenticated call under the same authority as the reads, so
    // a 401/403 answering it reaches the same boundary rather than staying local.
    const securitySnapshot = boundary.capture()
    const outcome = await createProductDraft(snapshot.payload, {
      idempotencyKey: snapshot.key,
    })
    if (outcome.type === 'http') boundary.report(outcome.status, securitySnapshot)
    // `previous` is passed so a refusal that only answered a RETRY cannot be read
    // as proof that the earlier send did not apply.
    const next = nextState(snapshot, outcome, previous)
    if (next.phase === 'failed' && next.failure === 'validation') {
      /**
       * The server sends machine codes, so the text is produced here. A code this
       * build has never seen still reaches its input with a general sentence —
       * losing the field binding is worse than losing message precision.
       */
      const serverErrors: Record<string, string> = {}
      for (const error of productFieldErrorsOf(outcome.type === 'http' ? outcome.body : null)) {
        serverErrors[error.field] = fieldErrorText(error.code)
      }
      setErrors((current) => mergeServerErrors(current, serverErrors))
      setAttempt({ ...next, fieldErrors: serverErrors })
      return
    }
    setAttempt(next)
  }, [boundary])

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (frozen || !canCreate) return
    setSubmitted(true)

    const validation = validateCreateForm(values, groupIds, classIds, familyIds)
    if (!validation.ok) {
      /**
       * A client-side rejection mints NO key: nothing was sent, so there is nothing
       * to be idempotent about. Focus is not moved to the first field — there is
       * one rule, and it is that the summary takes focus and offers the jump.
       */
      setErrors(validation.errors)
      return
    }
    setErrors({})

    /**
     * An unresolved attempt with the SAME payload is THAT attempt. Re-sending it
     * under a new key is how one click becomes two products — and the contract says
     * so too: "An unknown outcome must not be retried with a new key."
     */
    /**
     * Resubmitting the SAME payload reuses the live snapshot — which now includes a
     * refusal that left an earlier send unresolved. Minting a fresh key there is how
     * one committed record becomes two.
     */
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
      CREATE_FIELD_ORDER.filter((field) => errors[field] !== undefined).map((field) => ({
        field,
        message: errors[field] as string,
      })),
    [errors],
  )

  useEffect(() => {
    if (submitted && errorList.length > 0) summaryRef.current?.focus()
  }, [submitted, errorList.length])

  function guardedNavigate(to: string) {
    if (unresolved) {
      setLeaveIntent(to)
      return
    }
    void navigate(to)
  }

  if (attempt.phase === 'completed') {
    const created = attempt.result.card.product
    return (
      <section className={styles.page} aria-labelledby="product-created-heading">
        <div className={styles.successBlock} role="status">
          <h1 id="product-created-heading" className={styles.title}>
            {PRODUCT_CREATE_TEXT.createdTitle}
          </h1>
          {/* The code is printed only now, from the server's own response. */}
          <p className={styles.createdCode}>
            {created.code} — {created.name}
          </p>
          <p className={styles.note}>{PRODUCT_CREATE_TEXT.draftNote}</p>
          {/* `isReplay` is a response FIELD in this contract, not a header. */}
          {attempt.result.isReplay ? (
            <p className={styles.note}>{PRODUCT_CREATE_TEXT.replayedNote}</p>
          ) : null}
          <div className={styles.actions}>
            {canOpenDetail ? (
              <Link className={styles.primaryAction} to={productDetailPath(created.id)}>
                {PRODUCT_CREATE_TEXT.goToDetail}
              </Link>
            ) : null}
            {canReturnToList ? (
              <Link
                className={styles.secondaryAction}
                to={PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsList]}
              >
                {PRODUCT_CREATE_TEXT.goToList}
              </Link>
            ) : null}
          </div>
        </div>
      </section>
    )
  }

  /**
   * A refusal is only a DEFINITE failure when nothing is left unresolved behind it.
   * Otherwise the screen would tell the user their record was not created while an
   * earlier send may well have created it.
   */
  const definiteFailure =
    attempt.phase === 'failed' &&
    !isOutcomeStillUnknown(attempt) &&
    attempt.failure !== 'validation' &&
    attempt.failure !== 'unauthenticated' &&
    attempt.failure !== 'forbidden'

  return (
    <section className={styles.page} aria-labelledby="product-create-heading">
      <h1 id="product-create-heading" className={styles.title}>
        {PRODUCT_CREATE_TEXT.title}
      </h1>
      <p className={styles.note}>{PRODUCT_CREATE_TEXT.draftNote}</p>
      <p className={styles.note}>{PRODUCT_CREATE_TEXT.codeNote}</p>
      {/* The known gap, said on screen rather than left as a silent absence. */}
      <p className={styles.note}>{PENDING_REFERENCE_NOTE}</p>

      {/*
        The prerequisite, stated before the form rather than as a save failure: the
        code group is a real technical dependency of CREATION, not of the screen.
      */}
      {!groupsLoading && groups === null ? (
        <div className={styles.errorSummary} role="alert" data-testid="product-groups-unavailable">
          <p className={styles.errorSummaryTitle}>{PRODUCT_CREATE_TEXT.groupsUnavailableTitle}</p>
          <p>{PRODUCT_CREATE_TEXT.groupsUnavailableDetail}</p>
          <button type="button" className={styles.secondaryAction} onClick={onRetryGroups}>
            {PRODUCT_CREATE_TEXT.retryGroups}
          </button>
        </div>
      ) : null}
      {groups !== null && groups.length === 0 ? (
        <div className={styles.errorSummary} role="alert" data-testid="product-groups-empty">
          <p className={styles.errorSummaryTitle}>{PRODUCT_CREATE_TEXT.groupsEmptyTitle}</p>
          <p>{PRODUCT_CREATE_TEXT.groupsEmptyDetail}</p>
        </div>
      ) : null}

      {submitted && errorList.length > 0 ? (
        <div
          className={styles.errorSummary}
          role="alert"
          tabIndex={-1}
          ref={summaryRef}
          data-testid="product-create-error-summary"
        >
          <p className={styles.errorSummaryTitle}>{PRODUCT_CREATE_TEXT.errorSummaryTitle}</p>
          <p>{PRODUCT_CREATE_TEXT.errorSummaryIntro}</p>
          <ul>
            {errorList.map(({ field, message }) => (
              <li key={field}>
                <button
                  type="button"
                  className={styles.errorLink}
                  onClick={() => fieldRefs.current[field]?.focus()}
                >
                  {CREATE_FIELD_LABEL[field]}: {message}
                </button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {attempt.phase === 'failed' &&
      !isOutcomeStillUnknown(attempt) &&
      (attempt.failure === 'unauthenticated' || attempt.failure === 'forbidden') ? (
        <div className={styles.errorSummary} role="alert">
          <p className={styles.errorSummaryTitle}>
            {attempt.failure === 'forbidden' ? 'Yetkiniz yok' : 'Oturum sonlandı'}
          </p>
          <p>Kayıt oluşturulmadı.</p>
        </div>
      ) : null}

      {definiteFailure ? (
        <div className={styles.errorSummary} role="alert" data-testid="product-create-failed">
          <p className={styles.errorSummaryTitle}>
            {attempt.phase === 'failed' && attempt.failure === 'idempotency-conflict'
              ? PRODUCT_CREATE_TEXT.codeCollisionTitle
              : PRODUCT_CREATE_TEXT.errorSummaryTitle}
          </p>
          <p>{PRODUCT_CREATE_TEXT.saveFailedRetained}</p>
        </div>
      ) : null}

      {/*
        The unknown outcome. Distinct from a failure on purpose: the Draft MAY exist,
        so the copy must not invite a fresh save, and the retry re-sends the same key.
      */}
      {isOutcomeStillUnknown(attempt) && attempt.phase !== 'submitting' ? (
        <div className={styles.unknownBlock} role="alert" data-testid="product-create-unknown">
          <p className={styles.errorSummaryTitle}>{PRODUCT_CREATE_TEXT.unknownOutcomeTitle}</p>
          <p>{PRODUCT_CREATE_TEXT.unknownOutcomeDetail}</p>
          {/*
            A refusal that only answered the RETRY settles nothing about the first
            send, so the screen keeps saying the outcome is unknown instead of
            reporting the record as not created.
          */}
          {attempt.phase === 'failed' ? (
            <p>{PRODUCT_CREATE_TEXT.retryRefusedStillUnknown}</p>
          ) : null}
          <button
            type="button"
            className={styles.primaryAction}
            onClick={retrySameAttempt}
          >
            {PRODUCT_CREATE_TEXT.retrySameAttempt}
          </button>
        </div>
      ) : null}

      <form className={styles.form} onSubmit={onSubmit} noValidate>
        <div className={styles.field}>
          <label htmlFor={CREATE_FIELD.name}>
            {PRODUCT_CREATE_TEXT.nameLabel}
            <span className={styles.required} aria-hidden="true">
              {' '}
              *
            </span>
          </label>
          <input
            id={CREATE_FIELD.name}
            type="text"
            value={values.name}
            required
            aria-required="true"
            maxLength={250}
            disabled={frozen}
            aria-invalid={errors.name !== undefined || undefined}
            aria-describedby={`${CREATE_FIELD.name}-hint${
              errors.name !== undefined ? ` ${CREATE_FIELD.name}-error` : ''
            }`}
            ref={(el) => {
              fieldRefs.current[CREATE_FIELD.name] = el
            }}
            onChange={(event) => setValues((v) => ({ ...v, name: event.target.value }))}
          />
          <p id={`${CREATE_FIELD.name}-hint`} className={styles.hint}>
            {PRODUCT_CREATE_TEXT.nameHint}
          </p>
          {errors.name !== undefined ? (
            <p id={`${CREATE_FIELD.name}-error`} className={styles.fieldError}>
              {errors.name}
            </p>
          ) : null}
        </div>

        <div className={styles.field}>
          <label htmlFor={CREATE_FIELD.groupId}>
            {PRODUCT_CREATE_TEXT.groupLabel}
            <span className={styles.required} aria-hidden="true">
              {' '}
              *
            </span>
          </label>
          <select
            id={CREATE_FIELD.groupId}
            value={values.groupId}
            required
            aria-required="true"
            disabled={frozen || !canCreate}
            aria-invalid={errors.groupId !== undefined || undefined}
            aria-describedby={`${CREATE_FIELD.groupId}-hint${
              errors.groupId !== undefined ? ` ${CREATE_FIELD.groupId}-error` : ''
            }`}
            ref={(el) => {
              fieldRefs.current[CREATE_FIELD.groupId] = el
            }}
            onChange={(event) => chooseGroup(event.target.value)}
          >
            <option value="">{PRODUCT_CREATE_TEXT.chooseNone}</option>
            {(groups ?? []).map((group) => (
              <option key={group.id} value={group.id}>
                {group.name} ({group.prefix})
              </option>
            ))}
          </select>
          <p id={`${CREATE_FIELD.groupId}-hint`} className={styles.hint}>
            {PRODUCT_CREATE_TEXT.groupHint}
          </p>
          {errors.groupId !== undefined ? (
            <p id={`${CREATE_FIELD.groupId}-error`} className={styles.fieldError}>
              {errors.groupId}
            </p>
          ) : null}
        </div>

        {/*
          Class and family are REFERENCES from `reference-options`, scoped to the
          chosen group. Both may stay empty on a Draft (§19.3), so neither is
          required — but a value that is sent must be one the server offered.
        */}
        <div className={styles.field}>
          <label htmlFor={CREATE_FIELD.classId}>
            {PRODUCT_CREATE_TEXT.classLabel}
            <span className={styles.optional}> ({PRODUCT_CREATE_TEXT.optional})</span>
          </label>
          <select
            id={CREATE_FIELD.classId}
            value={values.classId}
            disabled={frozen || values.groupId === '' || classesUnavailable}
            aria-invalid={errors.classId !== undefined || undefined}
            aria-describedby={`${CREATE_FIELD.classId}-hint`}
            ref={(el) => {
              fieldRefs.current[CREATE_FIELD.classId] = el
            }}
            onChange={(event) => chooseClass(event.target.value)}
          >
            <option value="">{PRODUCT_CREATE_TEXT.chooseNone}</option>
            {(options?.classes ?? []).map((entry) => (
              <option key={entry.id} value={entry.id}>
                {entry.displayName}
              </option>
            ))}
          </select>
          <p id={`${CREATE_FIELD.classId}-hint`} className={styles.hint}>
            {values.groupId === ''
              ? PRODUCT_CREATE_TEXT.chooseGroupFirst
              : classesUnavailable
                ? CATALOG_UNAVAILABLE
                : (options?.classes.length ?? 0) === 0
                  ? CATALOG_EMPTY
                  : PRODUCT_CREATE_TEXT.referenceHint}
          </p>
          {errors.classId !== undefined ? (
            <p className={styles.fieldError}>{errors.classId}</p>
          ) : null}
        </div>

        <div className={styles.field}>
          <label htmlFor={CREATE_FIELD.familyId}>
            {PRODUCT_CREATE_TEXT.familyLabel}
            <span className={styles.optional}> ({PRODUCT_CREATE_TEXT.optional})</span>
          </label>
          <select
            id={CREATE_FIELD.familyId}
            value={values.familyId}
            disabled={frozen || values.groupId === '' || familiesUnavailable}
            aria-invalid={errors.familyId !== undefined || undefined}
            aria-describedby={`${CREATE_FIELD.familyId}-hint`}
            ref={(el) => {
              fieldRefs.current[CREATE_FIELD.familyId] = el
            }}
            onChange={(event) => setValues((v) => ({ ...v, familyId: event.target.value }))}
          >
            <option value="">{PRODUCT_CREATE_TEXT.chooseNone}</option>
            {(options?.families ?? []).map((entry) => (
              <option key={entry.id} value={entry.id}>
                {entry.displayName}
              </option>
            ))}
          </select>
          <p id={`${CREATE_FIELD.familyId}-hint`} className={styles.hint}>
            {values.groupId === ''
              ? PRODUCT_CREATE_TEXT.chooseGroupFirst
              : familiesUnavailable
                ? CATALOG_UNAVAILABLE
                : (options?.families.length ?? 0) === 0
                  ? CATALOG_EMPTY
                  : PRODUCT_CREATE_TEXT.referenceHint}
          </p>
          {errors.familyId !== undefined ? (
            <p className={styles.fieldError}>{errors.familyId}</p>
          ) : null}
        </div>

        {/*
          Tri-state. A checkbox cannot represent "not answered" — an unticked box and
          a deliberate "no" would be the same state, which is the collapse the
          contract's tagged role input exists to prevent.
        */}
        <fieldset
          className={styles.roles}
          ref={(el) => {
            fieldRefs.current[CREATE_FIELD.roles] = el
          }}
          tabIndex={-1}
        >
          <legend>{PRODUCT_CREATE_TEXT.rolesLabel}</legend>
          <p className={styles.hint} id="product-roles-hint">
            {PRODUCT_CREATE_TEXT.rolesHint}
          </p>
          {errors.roles !== undefined ? (
            <p className={styles.fieldError}>{errors.roles}</p>
          ) : null}
          {PRODUCT_ROLE.map((role) => (
            <fieldset key={role} className={styles.roleRow} aria-describedby="product-roles-hint">
              <legend>{ROLE_LABEL[role]}</legend>
              {ROLE_CHOICES.map((choice) => (
                <label key={choice.value} className={styles.radio}>
                  <input
                    type="radio"
                    name={`role-${role}`}
                    value={choice.value}
                    checked={values.roles[role] === choice.value}
                    disabled={frozen}
                    onChange={() => setRole(setValues, role, choice.value)}
                  />
                  {choice.label}
                </label>
              ))}
            </fieldset>
          ))}
        </fieldset>

        <div className={styles.actions}>
          <button
            type="submit"
            className={styles.primaryAction}
            disabled={frozen || !canCreate}
          >
            {attempt.phase === 'submitting'
              ? PRODUCT_CREATE_TEXT.submitting
              : PRODUCT_CREATE_TEXT.submit}
          </button>
          <button
            type="button"
            className={styles.secondaryAction}
            disabled={attempt.phase === 'submitting'}
            onClick={() =>
              guardedNavigate(PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsList])
            }
          >
            {PRODUCT_CREATE_TEXT.cancel}
          </button>
        </div>
      </form>

      {/* SPA exits — Back, a sidebar link, a row link — go through the same guard. */}
      <UnresolvedExitGuard
        active={unresolved}
        title={PRODUCT_CREATE_TEXT.unsavedTitle}
        detail={PRODUCT_CREATE_TEXT.unsavedDetail}
        stayLabel={PRODUCT_CREATE_TEXT.unsavedStay}
        leaveLabel={PRODUCT_CREATE_TEXT.unsavedLeave}
      />

      {leaveIntent !== null ? (
        <UnsavedDialog
          title={PRODUCT_CREATE_TEXT.unsavedTitle}
          detail={PRODUCT_CREATE_TEXT.unsavedDetail}
          stayLabel={PRODUCT_CREATE_TEXT.unsavedStay}
          leaveLabel={PRODUCT_CREATE_TEXT.unsavedLeave}
          onStay={() => setLeaveIntent(null)}
          onLeave={() => {
            const to = leaveIntent
            setLeaveIntent(null)
            void navigate(to)
          }}
        />
      ) : null}
    </section>
  )
}

function setRole(
  setValues: React.Dispatch<React.SetStateAction<CreateFormValues>>,
  role: ProductRoleName,
  choice: RoleChoice,
) {
  setValues((current) => ({ ...current, roles: { ...current.roles, [role]: choice } }))
}

/** Exported so the contract test can assert the create screen knows this code. */
export const CREATE_HANDLED_CODES = PRODUCT_PROBLEM_CODE
