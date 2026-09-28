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
 *  - tracking policy: no accepted write adapter. Technical attributes and stock/sales
 *    units come from the released reference-options read (PRD-CARD-1);
 *  - activation: the contract includes no activation command;
 *  - AI drafting and technical-attribute similarity: both need a server surface that
 *    does not exist. Exact collisions are still handled — from the server's answer.
 *
 * Name matches (§10.2, CASE-PRD-U02-014) ARE shown: after the name field is left (or the
 * form is submitted), the server's own list search for that name is read and its matches
 * are listed as a warning to compare against. It never blocks the save, and no similarity
 * measure is invented here — only exact equality after Turkish case folding is labelled
 * "aynı ad", as a reading aid over the server's result. The server matches the term in
 * the name OR the code/alias, so the panel says exactly that.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { SEARCH_MAX, createProductDraft, fetchCodeGroups, fetchProducts, fetchReferenceOptions } from './api'
import type { CreateProductDraftPayload } from './api'
import {
  PRODUCT_PROBLEM_CODE,
  PRODUCT_ROLE,
  productFieldErrorsOf,
  type ProductCodeGroup,
  type ProductMutationResult,
  type ProductPage,
  type ProductReferenceOptions,
  type ProductRoleName,
} from './contract'
import type { ViewOutcome } from '../shell/uiState'
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
import {
  attemptBindingFor,
  createScope,
  forgetAttempt,
  recallAttempt,
  rememberAttempt,
  type AttemptBinding,
} from './pendingAttempt'
import styles from './ProductCreatePage.module.css'
import { DuplicateCandidatesPanel } from './DuplicateCandidates'
import { useDuplicateCandidates } from './duplicateQuery'
import { ProductTechnicalFields } from './ProductTechnicalFields'
import { technicalDraft } from './technicalForm'

type CreateAttempt = AttemptState<CreateProductDraftPayload, ProductMutationResult>

/** Shorter terms match too broadly to be a useful warning. A reading choice, not a rule. */
const NAME_MATCH_MIN = 2
const NAME_MATCH_PAGE = 20

// eslint-disable-next-line no-control-regex -- matching control characters is the point
const NAME_CONTROL_RE = /[\u0000-\u001F\u007F-\u009F]/g

/**
 * The checked name: control characters removed and trimmed, the way the server trims a
 * stored name and a search term. It is NOT bounded here — the list search sends only the
 * first `SEARCH_MAX` characters, but "aynı ad" compares the whole name. Inner whitespace
 * is kept: the server keeps it in stored names and in `q`, so collapsing it would claim
 * matches the server never returns and hide the identically spaced one it does.
 */
function nameMatchTerm(name: string): string {
  return name.replace(NAME_CONTROL_RE, '').trim()
}

function foldName(name: string): string {
  return name.toLocaleLowerCase('tr-TR')
}

/**
 * §10.2 after the save: the server's exact-name candidates for the Draft just created (itself left out), with
 * a link to each, so a duplicate is visible at the moment it was made. The save already happened — a name
 * match never blocks it — and the Draft stays a Draft until someone approves it.
 */
function CreatedDuplicateCheck({
  name,
  productId,
  namespace,
  entryGeneration,
  origin,
  canOpenDetail,
}: {
  readonly name: string
  readonly productId: string
  readonly namespace: string | null
  readonly entryGeneration: number
  readonly origin: string | null
  readonly canOpenDetail: boolean
}) {
  const query = useDuplicateCandidates({
    name: name.trim(),
    excludeProductId: productId,
    namespace,
    entryGeneration,
    origin,
    enabled: true,
  })
  return <DuplicateCandidatesPanel id="product-created-duplicate-check" outcome={query.data} canOpenDetail={canOpenDetail} />
}

/** The shown result's id, for the name field's `aria-describedby`. */
const NAME_MATCH_ID = 'product-name-match-check'

/**
 * The server's matches for the checked name, as a WARNING. It renders INSIDE a polite
 * live region that is already on the page, so the result is announced as an update
 * without interrupting typing or taking focus from the form.
 */
function NameMatches({
  term,
  outcome,
  canOpenDetail,
}: {
  readonly term: string
  readonly outcome: ViewOutcome<ProductPage> | undefined
  readonly canOpenDetail: boolean
}) {
  let body: React.ReactNode
  if (outcome === undefined || outcome.type === 'fence-stale') {
    body = <p>{PRODUCT_CREATE_TEXT.nameMatchesChecking}</p>
  } else if (outcome.type === 'ok-single') {
    const { items, nextCursor } = outcome.data
    if (items.length === 0) {
      body = <p>{PRODUCT_CREATE_TEXT.nameMatchesNone}</p>
    } else {
      const folded = foldName(term)
      body = (
        <>
          <p className={styles.errorSummaryTitle}>{PRODUCT_CREATE_TEXT.nameMatchesTitle}</p>
          <p>{PRODUCT_CREATE_TEXT.nameMatchesDetail}</p>
          <ul data-testid="product-name-matches">
            {items.map((item) => (
              <li key={item.id}>
                {canOpenDetail ? (
                  <Link to={productDetailPath(item.id)}>{item.code}</Link>
                ) : (
                  <span>{item.code}</span>
                )}{' '}
                {item.name}
                {foldName(item.name) === folded ? ` (${PRODUCT_CREATE_TEXT.nameMatchesSameName})` : null}
              </li>
            ))}
          </ul>
          {nextCursor !== null ? <p>{PRODUCT_CREATE_TEXT.nameMatchesMore}</p> : null}
        </>
      )
    }
  } else {
    body = <p>{PRODUCT_CREATE_TEXT.nameMatchesFailed}</p>
  }
  return (
    <div id={NAME_MATCH_ID} className={styles.note} data-testid="product-name-match-check">
      {body}
    </div>
  )
}

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
   * The operation's origin: the committed company, sent as `X-Company-Id` and
   * validated by the server (§15). Not an owner, not a grant — and frozen into every
   * attempt so a retry goes out under the origin of the first send.
   */
  const origin = shell.activeContext?.companyId ?? null
  /**
   * The attempt's DURABLE identity — actor, real session epoch, origin, committed
   * context, screen. Deliberately not the cache namespace: that carries the
   * capability generation and the committed revision, both of which a re-fence and
   * a fresh authorized entry legitimately change. See `pendingAttempt.ts`.
   */
  const binding = useMemo(() => attemptBindingFor(shell, createScope()), [shell])

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
    enabled: namespace !== null && origin !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchCodeGroups({ companyId: origin as string }, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const groups =
    groupQuery.data?.type === 'ok' ? (groupQuery.data.data as readonly ProductCodeGroup[]) : null

  return (
    <ProductCreateForm
      // Remounted per attempt binding: a new actor, session, origin or committed
      // context is a new form. A capability re-verification is NOT.
      key={binding?.key ?? 'no-context'}
      namespace={namespace}
      origin={origin}
      binding={binding}
      groups={groups}
      groupsLoading={groupQuery.isPending}
      entryGeneration={entryGeneration}
      onRetryGroups={() => void groupQuery.refetch()}
    />
  )
}

function ProductCreateForm({
  namespace,
  origin,
  binding,
  groups,
  groupsLoading,
  entryGeneration,
  onRetryGroups,
}: {
  readonly namespace: string | null
  /** The committed company — the validated origin header; null while no company is committed. */
  readonly origin: string | null
  readonly binding: AttemptBinding | null
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
  /**
   * Seeded from the pending store, so an attempt whose outcome is unknown survives
   * the unmount a capability re-fence causes. Only this exact binding can see it;
   * nothing is retried on its own — the person sees the unresolved state and
   * decides, behind a capability the entry gate has just re-verified.
   */
  const [attempt, setAttempt] = useState<CreateAttempt>(
    () =>
      recallAttempt<CreateProductDraftPayload, ProductMutationResult>(binding) ?? {
        phase: 'idle',
      },
  )

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
  const optionsQuery = useQuery<ViewOutcome<ProductReferenceOptions>>({
    queryKey: [
      namespace ?? 'no-context',
      entryGeneration,
      'product-reference-options',
      values.groupId,
      values.classId,
    ],
    enabled: namespace !== null && origin !== null && values.groupId !== '',
    /**
     * While a class-scoped read is out, the classes the owner offered for the SAME group
     * (same company context) stay on screen, so the chosen class does not vanish into
     * "— seçilmedi —". Only the class list is carried: families, catalogue availability and
     * validation read the settled answer alone. Another group or context never carries.
     */
    placeholderData: (previous, previousQuery) =>
      previous?.type === 'ok-single' &&
      previousQuery !== undefined &&
      previousQuery.queryKey[0] === (namespace ?? 'no-context') &&
      previousQuery.queryKey[1] === entryGeneration &&
      previousQuery.queryKey[3] === values.groupId
        ? previous
        : undefined,
    queryFn: async ({ signal }) => {
      // Same authority as every other read on this screen — a refusal here is the
      // same fact and must reach the same boundary. This one was missed when the
      // code-group read was wired up, which is exactly how a gap survives a fix.
      const snapshot = boundary.capture()
      const outcome = await fetchReferenceOptions(
        values.groupId,
        values.classId === '' ? null : values.classId,
        { companyId: origin as string },
        signal,
      )
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  /**
   * Business #66 5706740724: a read that has not settled, or that failed, is not an
   * answer about the chosen class. `settled` is the answer for exactly the current group
   * and class; a placeholder is never one.
   */
  const settled = optionsQuery.isPlaceholderData ? undefined : optionsQuery.data
  const options: ProductReferenceOptions | null = settled?.type === 'ok-single' ? settled.data : null
  const referencesPending =
    values.groupId !== '' && (settled === undefined || settled.type === 'fence-stale')
  const referencesFailed =
    values.groupId !== '' &&
    settled !== undefined &&
    settled.type !== 'ok-single' &&
    settled.type !== 'fence-stale'
  const shownClasses =
    options?.classes ??
    (optionsQuery.isPlaceholderData && optionsQuery.data?.type === 'ok-single'
      ? optionsQuery.data.data.classes
      : [])

  /**
   * The name the match check runs for: set when the name field is LEFT, not on every
   * keystroke, so typing does not fire a read per letter. Only with the list
   * capability — the check is a list read, and a create-only actor has no business
   * provoking a list refusal that would close the screen it is working in.
   */
  const [checkedName, setCheckedName] = useState('')
  const canCheckNames = hasCapability(shell, PRODUCT_CAPABILITY.productsList)
  /**
   * Shown only while the field still holds the checked name: once it is edited, that
   * result describes another name, and nothing has been checked for this one yet.
   */
  const showNameMatches =
    canCheckNames && checkedName.length >= NAME_MATCH_MIN && nameMatchTerm(values.name) === checkedName
  const nameMatchesQuery = useQuery({
    queryKey: [namespace ?? 'no-context', entryGeneration, 'product-name-matches', checkedName],
    enabled: canCheckNames && namespace !== null && origin !== null && checkedName.length >= NAME_MATCH_MIN,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchProducts(
        { q: checkedName.slice(0, SEARCH_MAX), sort: 'name', pageSize: NAME_MATCH_PAGE },
        { companyId: origin as string },
        signal,
      )
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
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
   * The chosen class as it was OFFERED, so it stays visible — in error if a fresh answer
   * dropped it, waiting if the answer is out — instead of silently reading as unchosen.
   */
  const [chosenClass, setChosenClass] = useState<{ id: string; displayName: string } | null>(null)
  /** Only a SETTLED, available catalogue can say the chosen class is no longer offered. */
  const classStale =
    options !== null &&
    !classesUnavailable &&
    values.classId !== '' &&
    !classIds.includes(values.classId)
  const classMissingFromShown =
    values.classId !== '' && !shownClasses.some((entry) => entry.id === values.classId)
  /** A save tried while the chosen references could not be checked yet. */
  const [referenceHold, setReferenceHold] = useState(false)
  const referencesChosen = values.classId !== '' || values.familyId !== ''
  const chosenReferencesUnavailable =
    (values.classId !== '' && classesUnavailable) ||
    (values.familyId !== '' && familiesUnavailable)
  const referencesUnsettled =
    referencesPending || referencesFailed || chosenReferencesUnavailable

  /**
   * A class chosen under one group is meaningless under another, and the family
   * list is scoped by class. Clearing on change prevents submitting a reference the
   * current options never offered — which the validator would reject anyway, but as
   * a confusing error rather than as a field that simply reset.
   */
  function chooseGroup(groupId: string) {
    setValues((current) => ({ ...current, groupId, classId: '', familyId: '', technical: technicalDraft() }))
    setChosenClass(null)
    setReferenceHold(false)
  }
  function chooseClass(classId: string) {
    setValues((current) => ({ ...current, classId, familyId: '', technical: technicalDraft() }))
    setChosenClass(shownClasses.find((entry) => entry.id === classId) ?? null)
    setReferenceHold(false)
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

  /**
   * THE acknowledgement that a success was shown — and the only one.
   *
   * A send can outlive its screen: a reference read answered 403 while the POST
   * was in flight closes the route, and the canonical 201 then lands in a
   * continuation nobody can see. "Still mounted" at that moment proves nothing
   * either: when the refusal and the success resolve in the same React batch, the
   * component is mounted while the continuation runs and gone by the commit, and
   * the success screen never exists. So the continuation never settles a success;
   * this effect does, after a completed screen has actually rendered under this
   * binding and key. Until then the store keeps it for the next screen.
   */
  useEffect(() => {
    if (attempt.phase === 'completed') rememberAttempt(binding, attempt)
  }, [attempt, binding])

  const send = useCallback(async (snapshot: AttemptSnapshot<CreateProductDraftPayload>) => {
    // Read BEFORE the submitting transition: the ref follows the rendered state, so
    // reading it after would see `submitting` and lose the very history this exists
    // to carry.
    const previous = attemptRef.current
    const inFlight: CreateAttempt = { phase: 'submitting', snapshot }
    /**
     * The key is protected from HERE — synchronously, before the await, in the
     * store rather than only in component state. An effect would run after the
     * commit, and a 403 answering this very send closes the route before that
     * commit: the state that mattered most was the one never written.
     */
    rememberAttempt(binding, inFlight)
    setAttempt(inFlight)
    // The write is an authenticated call under the same authority as the reads, so
    // a 401/403 answering it reaches the same boundary rather than staying local.
    const securitySnapshot = boundary.capture()
    // The origin travels FROM THE SNAPSHOT, never from the shell's current state: a
    // retry is the same attempt under the same origin, key and payload.
    const outcome = await createProductDraft(snapshot.payload, {
      idempotencyKey: snapshot.key,
      origin: { companyId: snapshot.origin },
    })
    // `previous` is passed so a refusal that only answered a RETRY cannot be read
    // as proof that the earlier send did not apply.
    const next = nextState(snapshot, outcome, previous)
    // Written BEFORE the boundary hears about a refusal: the report is what
    // unmounts this screen, and the outcome must already be safe when it does.
    // A success is written UNSEEN here, always: whether a screen will render it
    // is not known in this continuation. The post-render effect above is the ack.
    rememberAttempt(binding, next, false)
    if (outcome.type === 'http') boundary.report(outcome.status, securitySnapshot)
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
  }, [boundary, binding])

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (frozen || !canCreate || origin === null) return
    setSubmitted(true)
    // An Enter-submit never leaves the name field: check the name being sent, too. The
    // check is still only a warning — the send below does not wait for it.
    setCheckedName(nameMatchTerm(values.name))

    /**
     * A chosen class or family cannot be judged while its read is out or failed. The save
     * waits with a neutral note — never a false "no longer on the list", never a payload
     * with the choice dropped. The other fields are still checked now, with the chosen
     * references taken as they are. Nothing chosen: an incomplete Draft saves as before.
     */
    if (referencesChosen && referencesUnsettled) {
      const others = validateCreateForm(
        {
          name: values.name,
          groupId: values.groupId,
          classId: values.classId,
          familyId: values.familyId,
          roles: values.roles,
        },
        groupIds,
        [values.classId],
        [values.familyId],
      )
      setErrors(others.ok ? {} : others.errors)
      setReferenceHold(true)
      return
    }
    setReferenceHold(false)

    const validation = validateCreateForm(values, groupIds, classIds, familyIds, options)
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
      origin,
      namespace: binding?.key ?? '',
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

  /**
   * Cancel navigates plainly and lets the BLOCKER decide.
   *
   * It used to open its own dialog and then call `navigate`, which the blocker
   * caught a second time — the user answered "leave" and was asked again. One exit,
   * one guard, one question.
   */
  function leave(to: string) {
    void navigate(to)
  }

  if (attempt.phase === 'completed') {
    // Resolved and shown: the effect above settles the key; nothing left to protect.
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
          {canCheckNames ? (
            <CreatedDuplicateCheck
              name={created.name}
              productId={created.id}
              namespace={namespace}
              entryGeneration={entryGeneration}
              origin={origin}
              canOpenDetail={canOpenDetail}
            />
          ) : null}
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
            aria-describedby={`${CREATE_FIELD.name}-hint${showNameMatches ? ` ${NAME_MATCH_ID}` : ''}${
              errors.name !== undefined ? ` ${CREATE_FIELD.name}-error` : ''
            }`}
            ref={(el) => {
              fieldRefs.current[CREATE_FIELD.name] = el
            }}
            onChange={(event) => setValues((v) => ({ ...v, name: event.target.value }))}
            onBlur={() => setCheckedName(nameMatchTerm(values.name))}
          />
          <p id={`${CREATE_FIELD.name}-hint`} className={styles.hint}>
            {PRODUCT_CREATE_TEXT.nameHint}
          </p>
          {canCheckNames ? (
            <div role="status" aria-live="polite" data-testid="product-name-match-live">
              {showNameMatches ? (
                <NameMatches
                  term={checkedName}
                  outcome={nameMatchesQuery.data}
                  canOpenDetail={canOpenDetail}
                />
              ) : null}
            </div>
          ) : null}
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
            aria-invalid={errors.classId !== undefined || classStale || undefined}
            aria-describedby={`${CREATE_FIELD.classId}-hint`}
            ref={(el) => {
              fieldRefs.current[CREATE_FIELD.classId] = el
            }}
            onChange={(event) => chooseClass(event.target.value)}
          >
            <option value="">{PRODUCT_CREATE_TEXT.chooseNone}</option>
            {shownClasses.map((entry) => (
              <option key={entry.id} value={entry.id}>
                {entry.displayName}
              </option>
            ))}
            {classMissingFromShown && chosenClass?.id === values.classId ? (
              <option value={chosenClass.id}>{chosenClass.displayName}</option>
            ) : null}
          </select>
          <p id={`${CREATE_FIELD.classId}-hint`} className={styles.hint}>
            {values.groupId === ''
              ? PRODUCT_CREATE_TEXT.chooseGroupFirst
              : referencesFailed && values.classId !== ''
                ? PRODUCT_CREATE_TEXT.referenceOptionsFailed
                : referencesFailed || classesUnavailable
                  ? CATALOG_UNAVAILABLE
                  : options !== null && options.classes.length === 0
                    ? CATALOG_EMPTY
                    : PRODUCT_CREATE_TEXT.referenceHint}
          </p>
          {referencesFailed || chosenReferencesUnavailable ? (
            <button
              type="button"
              className={styles.secondaryAction}
              onClick={() => void optionsQuery.refetch()}
            >
              {PRODUCT_CREATE_TEXT.retryReferenceOptions}
            </button>
          ) : null}
          {errors.classId !== undefined ? (
            <p className={styles.fieldError}>{errors.classId}</p>
          ) : classStale ? (
            <p className={styles.fieldError}>{PRODUCT_CREATE_TEXT.validation.classUnknown}</p>
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
            disabled={
              frozen ||
              values.groupId === '' ||
              familiesUnavailable ||
              referencesUnsettled ||
              classStale
            }
            aria-invalid={errors.familyId !== undefined || undefined}
            aria-describedby={`${CREATE_FIELD.familyId}-hint`}
            ref={(el) => {
              fieldRefs.current[CREATE_FIELD.familyId] = el
            }}
            onChange={(event) => setValues((v) => ({ ...v, familyId: event.target.value,
              technical: { ...(v.technical ?? technicalDraft()), attributes: {}, attributesChanged: event.target.value !== '' } }))}
          >
            <option value="">{PRODUCT_CREATE_TEXT.chooseNone}</option>
            {(classStale ? [] : (options?.families ?? [])).map((entry) => (
              <option key={entry.id} value={entry.id}>
                {entry.displayName}
              </option>
            ))}
          </select>
          <p id={`${CREATE_FIELD.familyId}-hint`} className={styles.hint}>
            {values.groupId === ''
              ? PRODUCT_CREATE_TEXT.chooseGroupFirst
              : referencesPending
                ? PRODUCT_CREATE_TEXT.referenceOptionsChecking
                : referencesFailed || familiesUnavailable
                  ? CATALOG_UNAVAILABLE
                  : options !== null && options.families.length === 0
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
        <ProductTechnicalFields familyId={values.familyId} options={options} value={values.technical}
          fieldRef={el => { fieldRefs.current.references = el }}
          frozen={frozen} onChange={technical => setValues(v => ({ ...v, technical }))}
          errors={attempt.phase === 'failed' ? attempt.fieldErrors : undefined} />
        {errors.references ? <p role="alert">{errors.references}</p> : null}
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

        {referenceHold && referencesChosen && referencesUnsettled ? (
          <p className={styles.note} role="status" data-testid="product-create-reference-hold">
            {referencesPending
              ? PRODUCT_CREATE_TEXT.referenceOptionsHold
              : PRODUCT_CREATE_TEXT.referenceOptionsFailedHold}
          </p>
        ) : null}

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
              leave(PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsList])
            }
          >
            {PRODUCT_CREATE_TEXT.cancel}
          </button>
        </div>
      </form>

      {/*
        EVERY router exit — Cancel, Back, a sidebar link — goes through this one
        guard. Leaving deliberately drops the pending key: the user was asked and
        said yes, so keeping it would resurrect a decision they just made.
      */}
      <UnresolvedExitGuard
        active={unresolved}
        title={PRODUCT_CREATE_TEXT.unsavedTitle}
        detail={PRODUCT_CREATE_TEXT.unsavedDetail}
        stayLabel={PRODUCT_CREATE_TEXT.unsavedStay}
        leaveLabel={PRODUCT_CREATE_TEXT.unsavedLeave}
        onLeave={() => forgetAttempt(binding)}
      />
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
