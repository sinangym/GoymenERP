/**
 * U-02 Yeni Ürün — Draft create.
 *
 * Scope (JIT spec §10, §6.1): create a DRAFT. Activation is a separate command with
 * its own audit record (§6.2) and stays fail-closed until `G-PRD-PLATFORM-01`,
 * `G-PRD-PERM-01` and `G-PRD-PERSIST-01` pass (§6.6) — so this screen offers no
 * activate button at all rather than a disabled one whose gate it cannot explain.
 *
 * Deliberately absent, each for a stated reason:
 *  - a product CODE field. §5.1/§7: the server generates it from a group prefix and
 *    a sequence, and the admin manual-code exception needs a permission string that
 *    does not exist yet;
 *  - AI draft suggestions. U-02's canonical screen card is "AI Destekli Yeni Ürün"
 *    and §12 allows AI to draft fields — but only "Capability açık ve kullanıcı
 *    yetkiliyse", and no AI capability is published. A panel backed by nothing would
 *    be a fabricated feature, so the AI surface is simply not mounted;
 *  - duplicate similarity warnings. §10.2 makes name/technical similarity a
 *    warning produced by a server-side candidate read. There is no such endpoint,
 *    and computing "looks similar" in the browser would be the screen inventing a
 *    judgement. Exact code/alias collision is still handled — from the server's
 *    answer.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { createProductDraft } from './api'
import { PRODUCT_ROLE, productFieldErrorsOf, type ProductRoleName } from './contract'
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
  isSamePayload,
  nextState,
  type AttemptSnapshot,
  type AttemptState,
} from './createAttempt'
import { PRODUCT_CREATE_TEXT, ROLE_LABEL } from './productText'
import {
  PRODUCT_CAPABILITY,
  PRODUCT_CAPABILITY_ROUTE,
  productDetailPath,
} from './capabilities'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import styles from './ProductCreatePage.module.css'

const ROLE_CHOICES: readonly { readonly value: RoleChoice; readonly label: string }[] = [
  { value: 'yes', label: PRODUCT_CREATE_TEXT.roleYes },
  { value: 'no', label: PRODUCT_CREATE_TEXT.roleNo },
  // Last, and the initial value. §5.2: an unanswered role stays unanswered.
  { value: 'unset', label: PRODUCT_CREATE_TEXT.roleUnset },
]

export function ProductCreatePage() {
  const shell = useShellState()
  const navigate = useNavigate()
  const canOpenDetail = hasCapability(shell, PRODUCT_CAPABILITY.productsDetail)
  const canReturnToList = hasCapability(shell, PRODUCT_CAPABILITY.productsList)

  const [values, setValues] = useState<CreateFormValues>(EMPTY_CREATE_FORM)
  const [errors, setErrors] = useState<CreateFormErrors>({})
  const [submitted, setSubmitted] = useState(false)
  const [attempt, setAttempt] = useState<AttemptState>({ phase: 'idle' })
  const [leaveIntent, setLeaveIntent] = useState<string | null>(null)

  const summaryRef = useRef<HTMLDivElement>(null)
  const fieldRefs = useRef<Partial<Record<CreateFieldId, HTMLInputElement | null>>>({})

  const dirty = !isCreateFormPristine(values)
  const frozen = isFormFrozen(attempt)
  const unresolved = hasUnresolvedWork(attempt, dirty)

  /**
   * §10.4, browser half. A full page unload cannot be intercepted with a custom
   * dialog — the browser shows its own — but it can be made to ask at all, which is
   * the difference between losing the form to a stray Cmd-W and being warned.
   */
  useEffect(() => {
    if (!unresolved) return
    function onBeforeUnload(event: BeforeUnloadEvent) {
      event.preventDefault()
      // Assigning returnValue is what still triggers the prompt in Chromium; the
      // string itself has been ignored by every browser for years.
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [unresolved])

  function update<K extends keyof CreateFormValues>(field: K, value: CreateFormValues[K]) {
    setValues((current) => ({ ...current, [field]: value }))
  }

  function setRole(role: ProductRoleName, choice: RoleChoice) {
    setValues((current) => ({ ...current, roles: { ...current.roles, [role]: choice } }))
  }

  /**
   * Send the frozen snapshot. Used by the first submit AND by the same-key retry,
   * so the two cannot drift into sending different things.
   */
  const send = useCallback(async (snapshot: AttemptSnapshot) => {
    setAttempt({ phase: 'submitting', snapshot })
    const outcome = await createProductDraft(snapshot.payload, { idempotencyKey: snapshot.key })
    const next = nextState(snapshot, outcome)
    if (next.phase === 'failed' && next.failure === 'validation') {
      // §10.3: the server is the single truth, and its field errors win over the
      // client's on any field it names.
      const serverErrors = productFieldErrorsOf(
        outcome.type === 'http' ? outcome.body : null,
        CREATE_FIELD_ORDER,
      )
      setErrors((current) => mergeServerErrors(current, serverErrors))
      setAttempt({ ...next, fieldErrors: serverErrors })
      return
    }
    setAttempt(next)
  }, [])

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (frozen) return
    setSubmitted(true)

    const validation = validateCreateForm(values)
    if (!validation.ok) {
      /**
       * A client-side rejection mints NO key: nothing was sent, so there is nothing
       * to be idempotent about.
       *
       * Focus is NOT moved to the first field here. There is exactly one rule —
       * the summary takes focus (see the effect below) and offers a jump to each
       * field. An earlier version did both, and the two fought: the effect ran
       * last, so the field focus was silently undone and a screen-reader user
       * heard neither the summary nor the field cleanly.
       */
      setErrors(validation.errors)
      return
    }
    setErrors({})

    /**
     * An unresolved attempt with the SAME payload is THAT attempt. Re-sending it
     * under a new key is exactly how one click becomes two products, so the
     * existing key is reused and the server replays.
     */
    if (attempt.phase === 'unknown' && isSamePayload(attempt.snapshot.payload, validation.payload)) {
      void send(attempt.snapshot)
      return
    }

    void send({ key: generateAttemptKey(), payload: validation.payload })
  }

  /** The same snapshot, the same key. Never a fresh attempt. */
  function retrySameAttempt() {
    if (attempt.phase !== 'unknown') return
    void send(attempt.snapshot)
  }

  const errorList = useMemo(
    () =>
      CREATE_FIELD_ORDER.filter((field) => errors[field] !== undefined).map((field) => ({
        field,
        message: errors[field] as string,
      })),
    [errors],
  )

  // Move focus to the summary when a submit produced errors, so a screen-reader
  // user is told why nothing happened instead of being left on the button.
  useEffect(() => {
    if (submitted && errorList.length > 0) summaryRef.current?.focus()
  }, [submitted, errorList.length])

  /** Navigate, asking first when there is unresolved work (§10.4). */
  function guardedNavigate(to: string) {
    if (unresolved) {
      setLeaveIntent(to)
      return
    }
    void navigate(to)
  }

  if (attempt.phase === 'completed') {
    const created = attempt.result
    return (
      <section className={styles.page} aria-labelledby="product-created-heading">
        <div className={styles.successBlock} role="status">
          <h1 id="product-created-heading" className={styles.title}>
            {PRODUCT_CREATE_TEXT.createdTitle}
          </h1>
          {/*
            The code is printed only now — §7: the UI must not declare the code
            before the server's response.
          */}
          <p className={styles.createdCode}>
            {created.code} — {created.name}
          </p>
          <p className={styles.note}>{PRODUCT_CREATE_TEXT.draftNote}</p>
          {/*
            A replay is not a second record, and saying so stops the user from
            hunting for a duplicate they think they just made.
          */}
          {attempt.replayed ? (
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

  return (
    <section className={styles.page} aria-labelledby="product-create-heading">
      <h1 id="product-create-heading" className={styles.title}>
        {PRODUCT_CREATE_TEXT.title}
      </h1>
      <p className={styles.note}>{PRODUCT_CREATE_TEXT.draftNote}</p>
      <p className={styles.note}>{PRODUCT_CREATE_TEXT.codeNote}</p>

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
                {/*
                  A button rather than an anchor: this moves focus inside the page,
                  it does not navigate, and a link that does not link is a promise
                  the control cannot keep.
                */}
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

      {attempt.phase === 'failed' && attempt.failure === 'code-collision' ? (
        <div className={styles.errorSummary} role="alert">
          <p className={styles.errorSummaryTitle}>{PRODUCT_CREATE_TEXT.codeCollisionTitle}</p>
          <p>{PRODUCT_CREATE_TEXT.codeCollisionDetail}</p>
          <p>{PRODUCT_CREATE_TEXT.saveFailedRetained}</p>
        </div>
      ) : null}

      {attempt.phase === 'failed' &&
      (attempt.failure === 'unauthenticated' || attempt.failure === 'forbidden') ? (
        <div className={styles.errorSummary} role="alert">
          <p className={styles.errorSummaryTitle}>
            {attempt.failure === 'forbidden' ? 'Yetkiniz yok' : 'Oturum sonlandı'}
          </p>
          <p>
            {attempt.failure === 'forbidden'
              ? 'Bu işlem için yetkiniz bulunmuyor. Kayıt oluşturulmadı.'
              : 'Devam etmek için yeniden giriş yapın. Kayıt oluşturulmadı.'}
          </p>
        </div>
      ) : null}

      {attempt.phase === 'failed' &&
      (attempt.failure === 'unexpected' ||
        attempt.failure === 'metadata' ||
        attempt.failure === 'idempotency-conflict') ? (
        <div className={styles.errorSummary} role="alert">
          <p className={styles.errorSummaryTitle}>{PRODUCT_CREATE_TEXT.errorSummaryTitle}</p>
          <p>{PRODUCT_CREATE_TEXT.saveFailedRetained}</p>
        </div>
      ) : null}

      {/*
        The unknown outcome. Distinct from a failure on purpose: the Draft MAY
        exist, so the copy must not invite a fresh save, and the retry re-sends the
        same key rather than starting a new attempt.
      */}
      {attempt.phase === 'unknown' ? (
        <div className={styles.unknownBlock} role="alert" data-testid="product-create-unknown">
          <p className={styles.errorSummaryTitle}>{PRODUCT_CREATE_TEXT.unknownOutcomeTitle}</p>
          <p>{PRODUCT_CREATE_TEXT.unknownOutcomeDetail}</p>
          <button type="button" className={styles.primaryAction} onClick={retrySameAttempt}>
            {PRODUCT_CREATE_TEXT.retrySameAttempt}
          </button>
        </div>
      ) : null}

      <form className={styles.form} onSubmit={onSubmit} noValidate>
        <TextField
          id={CREATE_FIELD.name}
          label={PRODUCT_CREATE_TEXT.nameLabel}
          hint={PRODUCT_CREATE_TEXT.nameHint}
          required
          value={values.name}
          error={errors.name}
          disabled={frozen}
          inputRef={(el) => {
            fieldRefs.current[CREATE_FIELD.name] = el
          }}
          onChange={(value) => update('name', value)}
        />

        <TextField
          id={CREATE_FIELD.productClass}
          label={PRODUCT_CREATE_TEXT.classLabel}
          required
          value={values.productClass}
          error={errors.productClass}
          disabled={frozen}
          inputRef={(el) => {
            fieldRefs.current[CREATE_FIELD.productClass] = el
          }}
          onChange={(value) => update('productClass', value)}
        />

        <TextField
          id={CREATE_FIELD.productGroup}
          label={PRODUCT_CREATE_TEXT.groupLabel}
          value={values.productGroup}
          error={errors.productGroup}
          disabled={frozen}
          inputRef={(el) => {
            fieldRefs.current[CREATE_FIELD.productGroup] = el
          }}
          onChange={(value) => update('productGroup', value)}
        />

        <TextField
          id={CREATE_FIELD.productFamily}
          label={PRODUCT_CREATE_TEXT.familyLabel}
          value={values.productFamily}
          error={errors.productFamily}
          disabled={frozen}
          inputRef={(el) => {
            fieldRefs.current[CREATE_FIELD.productFamily] = el
          }}
          onChange={(value) => update('productFamily', value)}
        />

        {/*
          The tri-state role picker. A checkbox cannot represent "not answered" —
          an unticked box and a deliberate "no" are the same state — and §5.2
          forbids persisting the first as the second.
        */}
        <fieldset className={styles.roles}>
          <legend>{PRODUCT_CREATE_TEXT.rolesLabel}</legend>
          <p className={styles.hint} id="product-roles-hint">
            {PRODUCT_CREATE_TEXT.rolesHint}
          </p>
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
                    onChange={() => setRole(role, choice.value)}
                  />
                  {choice.label}
                </label>
              ))}
            </fieldset>
          ))}
        </fieldset>

        <TextField
          id={CREATE_FIELD.trackingPolicy}
          label={PRODUCT_CREATE_TEXT.trackingPolicyLabel}
          value={values.trackingPolicy}
          error={errors.trackingPolicy}
          disabled={frozen}
          inputRef={(el) => {
            fieldRefs.current[CREATE_FIELD.trackingPolicy] = el
          }}
          onChange={(value) => update('trackingPolicy', value)}
        />

        <div className={styles.actions}>
          <button type="submit" className={styles.primaryAction} disabled={frozen}>
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

      {/*
        §10.4 unsaved guard, in-app half. Rendered as a dialog with focus moved onto
        it, and Escape means "stay" — the safe choice, because the destructive option
        must never be the one a stray key press picks.
      */}
      {leaveIntent !== null ? (
        <UnsavedDialog
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

function TextField({
  id,
  label,
  hint,
  required = false,
  value,
  error,
  disabled,
  inputRef,
  onChange,
}: {
  readonly id: CreateFieldId
  readonly label: string
  readonly hint?: string
  readonly required?: boolean
  readonly value: string
  readonly error?: string
  readonly disabled: boolean
  readonly inputRef: (el: HTMLInputElement | null) => void
  readonly onChange: (value: string) => void
}) {
  const hintId = hint !== undefined ? `${id}-hint` : undefined
  const errorId = error !== undefined ? `${id}-error` : undefined
  /**
   * §10.3: the error is PROGRAMMATICALLY associated with its input. `aria-describedby`
   * carries both the hint and the error, and `aria-invalid` marks the field itself —
   * a red border is invisible to a screen reader and to anyone who cannot
   * distinguish the colour.
   */
  const describedBy = [hintId, errorId].filter((part) => part !== undefined).join(' ')
  return (
    <div className={styles.field}>
      <label htmlFor={id}>
        {label}
        {required ? (
          <span className={styles.required} aria-hidden="true">
            {' '}
            *
          </span>
        ) : (
          <span className={styles.optional}> ({PRODUCT_CREATE_TEXT.optional})</span>
        )}
      </label>
      <input
        id={id}
        type="text"
        value={value}
        disabled={disabled}
        required={required}
        aria-required={required || undefined}
        aria-invalid={error !== undefined || undefined}
        aria-describedby={describedBy.length > 0 ? describedBy : undefined}
        ref={inputRef}
        onChange={(event) => onChange(event.target.value)}
      />
      {hint !== undefined ? (
        <p id={hintId} className={styles.hint}>
          {hint}
        </p>
      ) : null}
      {error !== undefined ? (
        <p id={errorId} className={styles.fieldError}>
          {error}
        </p>
      ) : null}
    </div>
  )
}

function UnsavedDialog({
  onStay,
  onLeave,
}: {
  readonly onStay: () => void
  readonly onLeave: () => void
}) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const stayRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    // Focus lands on the SAFE option, not the destructive one.
    stayRef.current?.focus()
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        onStay()
        return
      }
      if (event.key !== 'Tab') return
      // A minimal focus trap: the dialog has exactly two controls, so cycling
      // between them is the whole loop.
      const focusable = dialogRef.current?.querySelectorAll<HTMLElement>('button')
      if (focusable === undefined || focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onStay])

  return (
    <div className={styles.dialogBackdrop}>
      <div
        className={styles.dialog}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="unsaved-title"
        aria-describedby="unsaved-detail"
        ref={dialogRef}
      >
        <p id="unsaved-title" className={styles.errorSummaryTitle}>
          {PRODUCT_CREATE_TEXT.unsavedTitle}
        </p>
        <p id="unsaved-detail">{PRODUCT_CREATE_TEXT.unsavedDetail}</p>
        <div className={styles.actions}>
          <button type="button" className={styles.primaryAction} ref={stayRef} onClick={onStay}>
            {PRODUCT_CREATE_TEXT.unsavedStay}
          </button>
          <button type="button" className={styles.secondaryAction} onClick={onLeave}>
            {PRODUCT_CREATE_TEXT.unsavedLeave}
          </button>
        </div>
      </div>
    </div>
  )
}
