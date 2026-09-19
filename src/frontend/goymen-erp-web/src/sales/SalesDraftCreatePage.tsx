/**
 * S-03 — new sales order draft, pinned to `POST /api/v1/sales/drafts` (#257).
 *
 * The screen is thin on purpose; the rules live in tested modules:
 *  - ./orderDraftForm.ts — what may be sent (chosen references, client line/slice ids,
 *    null for unknown, the exact slice plan);
 *  - ./orderAttempt.ts — one key per snapshot, unknown keeps it, a changed payload
 *    does not open a second attempt while the first is unresolved;
 *  - ./api.ts — 201/false vs 200/true, and "this answer describes THIS attempt";
 *  - ./pendingDrafts.ts — the attempt survives the screen being closed under it.
 *
 * What the screen adds is ordering: the attempt is stored BEFORE the send awaits;
 * a late answer is stored for the binding that sent it but reaches THIS screen only
 * if the screen is still mounted under that same binding — so company A's answer
 * never paints, navigates or announces in company B.
 *
 * Fails closed without a selection source (see ./referenceSource.ts): no guessed
 * account, product, version, unit or currency is ever offered.
 */

import { useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router'
import { createDraft } from './api'
import { SALES_DRAFT_PATH } from './contract'
import {
  abandon,
  applyOutcome,
  beginSend,
  decideSubmit,
  generateOrderAttemptKey,
  isOutcomeStillUnknown,
  liveSnapshot,
  orderAttemptBinding,
  type OrderAttemptSnapshot,
} from './orderAttempt'
import { attemptNotice } from './orderAttemptNotice'
import {
  ORDER_FIELD,
  addLine,
  addSlice,
  browserIds,
  emptyOrderDraftForm,
  formFieldForServerPath,
  isOrderDraftPristine,
  lineFieldId,
  linePlanHint,
  removeLine,
  removeSlice,
  setLineProduct,
  sliceFieldId,
  updateLine,
  updateSlice,
  validateOrderDraft,
  type LineValues,
  type OrderDraftErrors,
  type OrderDraftFormValues,
  type OrderDraftOptions,
} from './orderDraftForm'
import { rememberDraftAttempt, recallDraftAttempt, type DraftAttempt } from './pendingDrafts'
import { SalesReferenceSourceContext } from './referenceSource'
import { SALES_CAPABILITY, SALES_ROUTE, salesDetailPath } from './capabilities'
import { ORDER_DRAFT_TEXT as T, problemCodeText } from './salesText'
import type { DraftCreateRequest, DraftMutation } from './contract'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useCompanyDirectory } from '../shell/useShellOrchestrator'
import styles from './SalesPages.module.css'

/** The shell's dirty scope: a company switch ASKS while this form holds work. */
export const SALES_CREATE_DIRTY_SCOPE = 'sales.order.create'

export function SalesDraftCreatePage() {
  const shell = useShellState()
  const binding = orderAttemptBinding(shell, SALES_CREATE_DIRTY_SCOPE)
  const companyId = shell.activeContext?.companyId ?? null
  const useOptions = useContext(SalesReferenceSourceContext)
  const source = useOptions(companyId ?? '')

  if (binding === null || companyId === null) {
    // A group view is not a write target (#254 §2); nothing can be bound here.
    return (
      <section className={styles.page}>
        <h1 className={styles.title}>{T.heading}</h1>
        <p role="status">Taslak yalnız tek bir şirket seçiliyken oluşturulabilir.</p>
      </section>
    )
  }
  return (
    <CreateForm
      // A new actor, session or company is a new form; a capability re-check is not.
      key={binding}
      binding={binding}
      companyId={companyId}
      options={source.status === 'ready' ? source.options : null}
      missing={source.status === 'unavailable' ? (source.missing ?? null) : null}
    />
  )
}

function CreateForm({
  binding,
  companyId,
  options,
  missing = null,
}: {
  readonly binding: string
  readonly companyId: string
  readonly options: OrderDraftOptions | null
  /** The unavailable source's own account of what is missing (S03); null when it says nothing. */
  readonly missing?: readonly { readonly source: string; readonly reason: string }[] | null
}) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const directory = useCompanyDirectory()
  const company = directory.find((entry) => entry.id === companyId)

  const [recalled] = useState(() => recallDraftAttempt(binding))
  const [values, setValues] = useState<OrderDraftFormValues>(
    () => recalled?.values ?? emptyOrderDraftForm(browserIds()),
  )
  const [attempt, setAttempt] = useState<DraftAttempt>(recalled?.attempt ?? { phase: 'idle' })
  const [errors, setErrors] = useState<OrderDraftErrors>({})
  const [serverMessages, setServerMessages] = useState<readonly string[]>([])
  const [blockedChange, setBlockedChange] = useState(false)
  const [summaryRequest, setSummaryRequest] = useState(0)
  const summaryRef = useRef<HTMLDivElement>(null)

  // Mirrors for the async continuation, written in effects (render purity).
  const attemptRef = useRef(attempt)
  const valuesRef = useRef(values)
  const currentBindingRef = useRef<string | null>(binding)
  const mountedRef = useRef(false)
  const liveBinding = orderAttemptBinding(shell, SALES_CREATE_DIRTY_SCOPE)
  useEffect(() => {
    attemptRef.current = attempt
    valuesRef.current = values
    currentBindingRef.current = liveBinding
  })
  useEffect(() => {
    mountedRef.current = true
    return () => {
      mountedRef.current = false
    }
  }, [])

  const unresolved =
    isOutcomeStillUnknown(attempt) || (attempt.phase !== 'completed' && !isOrderDraftPristine(values))
  useEffect(() => {
    dispatch({ type: unresolved ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: SALES_CREATE_DIRTY_SCOPE })
  }, [unresolved, dispatch])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope: SALES_CREATE_DIRTY_SCOPE }), [dispatch])

  // A success that has now actually been rendered is settled; until then the store keeps it.
  useEffect(() => {
    if (attempt.phase === 'completed') rememberDraftAttempt(binding, attempt, values, true)
  }, [attempt, binding, values])

  useEffect(() => {
    if (summaryRequest > 0) summaryRef.current?.focus()
  }, [summaryRequest])

  const send = useCallback(
    async (snapshot: OrderAttemptSnapshot<DraftCreateRequest>) => {
      const previous = attemptRef.current
      const inFlight: DraftAttempt = beginSend<DraftCreateRequest, DraftMutation>(snapshot)
      // Stored BEFORE the await: a 403 answering this send can close the route first.
      rememberDraftAttempt(snapshot.binding, inFlight, valuesRef.current, false)
      attemptRef.current = inFlight
      setAttempt(inFlight)
      setServerMessages([])
      const security = boundary.capture()
      // Company and key travel FROM THE SNAPSHOT, never from the shell's current state.
      const outcome = await createDraft(snapshot.payload, {
        idempotencyKey: snapshot.key,
        companyId: snapshot.companyId,
      })
      const next = applyOutcome(inFlight, snapshot, snapshot.binding, outcome, previous)
      // The truth for the binding that sent it, written before any boundary report
      // can unmount this screen. A success is written unseen; the render acknowledges it.
      rememberDraftAttempt(snapshot.binding, next, valuesRef.current, false)
      if (outcome.type === 'http') boundary.report(outcome.status, security)
      // Departed: another company, actor or session, or no screen at all. Nothing is
      // painted, announced or navigated, and no new write is unlocked here.
      if (!mountedRef.current || currentBindingRef.current !== snapshot.binding) return
      attemptRef.current = next
      setAttempt(next)
      if (next.phase === 'failed' && next.problem !== null && next.problem.errors.length > 0) {
        const fieldErrors: Record<string, string> = {}
        const unplaced: string[] = []
        for (const error of next.problem.errors) {
          const message = problemCodeText(error.code) ?? T.attempt.unexpected
          const field = formFieldForServerPath(error.field, snapshot.payload.lines)
          if (field === null) unplaced.push(message)
          else fieldErrors[field] = message
        }
        setErrors(fieldErrors)
        setServerMessages(unplaced)
        setSummaryRequest((n) => n + 1)
      }
    },
    [boundary],
  )

  function onSubmit(event: React.FormEvent) {
    event.preventDefault()
    if (options === null) return
    const validation = validateOrderDraft(values, options)
    if (!validation.ok) {
      // Nothing was sent, so no key is minted.
      setErrors(validation.errors)
      setServerMessages([])
      setSummaryRequest((n) => n + 1)
      return
    }
    setErrors({})
    const decision = decideSubmit(
      attemptRef.current,
      { path: SALES_DRAFT_PATH, binding, companyId, payload: validation.payload },
      generateOrderAttemptKey,
    )
    if (decision.type === 'blocked') {
      if (decision.reason === 'unresolved-other-payload') setBlockedChange(true)
      return
    }
    setBlockedChange(false)
    void send(decision.snapshot)
  }

  function retry() {
    const live = liveSnapshot(attemptRef.current)
    if (live === null || attemptRef.current.phase === 'submitting') return
    setBlockedChange(false)
    void send(live)
  }

  function leaveAttempt() {
    const next = abandon(attemptRef.current)
    attemptRef.current = next
    rememberDraftAttempt(binding, next, values, true)
    setBlockedChange(false)
    setAttempt(next)
  }

  const notice = attemptNotice(attempt, blockedChange)
  const errorEntries = useMemo(() => Object.entries(errors), [errors])

  if (attempt.phase === 'completed') {
    const draft = attempt.result.detail.draft
    return (
      <section className={styles.page} aria-labelledby="sales-created-heading">
        <div role="status" className={styles.success}>
          <h1 id="sales-created-heading" className={styles.title}>
            {T.attempt.completed}
          </h1>
          <p>
            {draft.documentNo} · {draft.revision} · {T.summary.notApproved}
          </p>
          {attempt.result.isReplay ? <p>{T.attempt.replayed}</p> : null}
          <div className={styles.actions}>
            {hasCapability(shell, SALES_CAPABILITY.draftsDetail) ? (
              <Link className={styles.primaryLink} to={salesDetailPath(draft.id)}>
                Taslağı aç
              </Link>
            ) : null}
            {hasCapability(shell, SALES_CAPABILITY.draftsList) ? (
              <Link to={SALES_ROUTE.list}>Sipariş taslakları</Link>
            ) : null}
          </div>
        </div>
      </section>
    )
  }

  const frozen = attempt.phase === 'submitting'
  const fieldProps = (id: string) => ({
    id,
    'aria-invalid': errors[id] !== undefined ? true : undefined,
    'aria-describedby': errors[id] !== undefined ? `${id}-error` : undefined,
  })
  const fieldError = (id: string) =>
    errors[id] === undefined ? null : (
      <p id={`${id}-error`} className={styles.fieldError}>
        {errors[id]}
      </p>
    )

  return (
    <section className={styles.page} aria-labelledby="sales-create-heading">
      <header className={styles.header}>
        <h1 id="sales-create-heading" className={styles.title}>
          {T.heading}
        </h1>
        {hasCapability(shell, SALES_CAPABILITY.draftsList) ? (
          <Link className={styles.backLink} to={SALES_ROUTE.list}>
            ← Sipariş taslakları
          </Link>
        ) : null}
      </header>

      {notice === null ? null : (
        <div
          className={styles.notice}
          data-tone={notice.tone}
          role={notice.live === 'assertive' ? 'alert' : 'status'}
          aria-live={notice.live}
        >
          <p>{notice.message}</p>
          {notice.actions.length > 0 ? (
            <div className={styles.actions}>
              <button type="button" className={styles.primaryButton} onClick={retry}>
                {T.attempt.retry}
              </button>
              <button type="button" className={styles.secondaryButton} onClick={leaveAttempt}>
                {T.attempt.abandon}
              </button>
            </div>
          ) : null}
        </div>
      )}

      {options === null ? (
        <div role="status" className={styles.notice} data-tone="unknown">
          <p>{T.referencesUnavailable}</p>
          {missing !== null && missing.length > 0 ? (
            <p data-testid="sales-references-missing">{T.referencesMissing(missing)}</p>
          ) : null}
        </div>
      ) : (
        <form onSubmit={onSubmit} noValidate>
          {errorEntries.length > 0 || serverMessages.length > 0 ? (
            <div ref={summaryRef} tabIndex={-1} role="alert" className={styles.errorSummary}>
              <p>{isOutcomeStillUnknown(attempt) ? T.errorSummaryUnresolved : T.errorSummary}</p>
              <ul>
                {errorEntries.map(([id, message]) => (
                  <li key={id}>
                    <a
                      href={`#${id}`}
                      onClick={(event) => {
                        event.preventDefault()
                        document.getElementById(id)?.focus()
                      }}
                    >
                      {message}
                    </a>
                  </li>
                ))}
                {serverMessages.map((message, index) => (
                  <li key={`server-${index}`}>{message}</li>
                ))}
              </ul>
            </div>
          ) : null}

          <fieldset disabled={frozen} className={styles.fieldset}>
            <legend>Sipariş bilgileri</legend>
            <div className={styles.grid}>
              <label htmlFor={ORDER_FIELD.customer}>{T.field.customer}</label>
              <select
                {...fieldProps(ORDER_FIELD.customer)}
                value={values.partyId === '' ? '' : `${values.partyId}|${values.companyAccountId}`}
                onChange={(event) => {
                  const [partyId = '', companyAccountId = ''] = event.target.value.split('|')
                  setValues((v) => ({ ...v, partyId, companyAccountId }))
                }}
              >
                <option value="">{T.choose}</option>
                {options.accounts.map((a) => (
                  <option key={`${a.partyId}|${a.companyAccountId}`} value={`${a.partyId}|${a.companyAccountId}`}>
                    {a.label}
                  </option>
                ))}
              </select>
              {fieldError(ORDER_FIELD.customer)}

              <label htmlFor={ORDER_FIELD.businessDate}>{T.field.businessDate}</label>
              <input
                type="date"
                {...fieldProps(ORDER_FIELD.businessDate)}
                value={values.businessDate}
                onChange={(event) => setValues((v) => ({ ...v, businessDate: event.target.value }))}
              />
              {fieldError(ORDER_FIELD.businessDate)}

              <label htmlFor={ORDER_FIELD.currency}>{T.field.currency}</label>
              <select
                {...fieldProps(ORDER_FIELD.currency)}
                value={values.currencyId}
                onChange={(event) => setValues((v) => ({ ...v, currencyId: event.target.value }))}
              >
                <option value="">{T.choose}</option>
                {options.currencies.map((c) => (
                  <option key={c.currencyId} value={c.currencyId}>
                    {c.label}
                  </option>
                ))}
              </select>
              {fieldError(ORDER_FIELD.currency)}

              <label htmlFor={ORDER_FIELD.requestedDeliveryDate}>{T.field.requestedDeliveryDate}</label>
              <input
                type="date"
                {...fieldProps(ORDER_FIELD.requestedDeliveryDate)}
                value={values.requestedDeliveryDate}
                onChange={(event) => setValues((v) => ({ ...v, requestedDeliveryDate: event.target.value }))}
              />
              {fieldError(ORDER_FIELD.requestedDeliveryDate)}

              <label htmlFor={ORDER_FIELD.note}>{T.field.note}</label>
              <textarea
                {...fieldProps(ORDER_FIELD.note)}
                value={values.note}
                onChange={(event) => setValues((v) => ({ ...v, note: event.target.value }))}
              />
              {fieldError(ORDER_FIELD.note)}
            </div>
          </fieldset>

          <fieldset disabled={frozen} className={styles.fieldset}>
            <legend>{T.field.lines}</legend>
            {fieldError(ORDER_FIELD.lines)}
            {values.lines.map((line, index) => (
              <LineEditor
                key={line.lineId}
                line={line}
                index={index}
                options={options}
                fieldProps={fieldProps}
                fieldError={fieldError}
                onChange={setValues}
              />
            ))}
            <button
              type="button"
              className={styles.secondaryButton}
              onClick={() => setValues((v) => addLine(v, browserIds()))}
            >
              {T.addLine}
            </button>
          </fieldset>

          <section className={styles.summary} aria-labelledby="sales-summary-heading">
            <h2 id="sales-summary-heading">{T.summary.heading}</h2>
            <p>
              {T.summary.company}: <strong>{company === undefined ? companyId : `${company.code} — ${company.legalName}`}</strong>
            </p>
            <p>
              <span className={styles.badge}>{T.summary.notPriced}</span>{' '}
              <span className={styles.badge}>{T.summary.notApproved}</span>
            </p>
            <p className={styles.muted}>{T.summary.noEffects}</p>
          </section>

          <div className={styles.actions}>
            <button type="submit" className={styles.primaryButton} disabled={frozen} aria-busy={frozen}>
              {T.save}
            </button>
          </div>
        </form>
      )}
    </section>
  )
}

function LineEditor({
  line,
  index,
  options,
  fieldProps,
  fieldError,
  onChange,
}: {
  readonly line: LineValues
  readonly index: number
  readonly options: OrderDraftOptions
  readonly fieldProps: (id: string) => Record<string, unknown>
  readonly fieldError: (id: string) => React.ReactNode
  readonly onChange: React.Dispatch<React.SetStateAction<OrderDraftFormValues>>
}) {
  const product = options.products.find((p) => p.productId === line.productId)
  const hint = linePlanHint(line)
  const id = (field: Parameters<typeof lineFieldId>[1]) => lineFieldId(line.lineId, field)
  return (
    <fieldset className={styles.line} data-testid={`sales-line-editor-${index + 1}`}>
      <legend>{index + 1}. satır</legend>
      <div className={styles.grid}>
        <label htmlFor={id('productId')}>{T.field.product}</label>
        <select
          {...fieldProps(id('productId'))}
          value={line.productId}
          onChange={(event) => onChange((v) => setLineProduct(v, line.lineId, event.target.value))}
        >
          <option value="">{T.choose}</option>
          {options.products.map((p) => (
            <option key={p.productId} value={p.productId}>
              {p.label}
            </option>
          ))}
        </select>
        {fieldError(id('productId'))}

        <label htmlFor={id('quantity')}>{T.field.quantity}</label>
        <input
          type="text"
          inputMode="decimal"
          autoComplete="off"
          {...fieldProps(id('quantity'))}
          value={line.quantityText}
          onChange={(event) => onChange((v) => updateLine(v, line.lineId, { quantityText: event.target.value }))}
        />
        {fieldError(id('quantity'))}

        <label htmlFor={id('orderUnitId')}>{T.field.orderUnit}</label>
        <select
          {...fieldProps(id('orderUnitId'))}
          value={line.orderUnitId}
          onChange={(event) => onChange((v) => updateLine(v, line.lineId, { orderUnitId: event.target.value }))}
        >
          <option value="">{T.choose}</option>
          {(product?.orderUnits ?? []).map((u) => (
            <option key={u.unitId} value={u.unitId}>
              {u.label}
            </option>
          ))}
        </select>
        {fieldError(id('orderUnitId'))}

        <label htmlFor={id('requestedDeliveryDate')}>{T.field.lineDeliveryDate}</label>
        <input
          type="date"
          {...fieldProps(id('requestedDeliveryDate'))}
          value={line.requestedDeliveryDate}
          onChange={(event) =>
            onChange((v) => updateLine(v, line.lineId, { requestedDeliveryDate: event.target.value }))
          }
        />
        {fieldError(id('requestedDeliveryDate'))}

        <label htmlFor={id('note')}>{T.field.lineNote}</label>
        <textarea
          {...fieldProps(id('note'))}
          value={line.note}
          onChange={(event) => onChange((v) => updateLine(v, line.lineId, { note: event.target.value }))}
        />
        {fieldError(id('note'))}
      </div>

      <fieldset className={styles.slices} id={id('slices')} tabIndex={-1} aria-describedby={`${id('slices')}-error`}>
        <legend>{T.field.slices}</legend>
        {(line.slices ?? []).map((slice, sliceIndex) => {
          const q = sliceFieldId(line.lineId, slice.sliceId, 'quantity')
          const d = sliceFieldId(line.lineId, slice.sliceId, 'requestedDeliveryDate')
          return (
            <div key={slice.sliceId} className={styles.sliceRow}>
              <label htmlFor={q}>
                {sliceIndex + 1}. {T.field.sliceQuantity}
              </label>
              <input
                type="text"
                inputMode="decimal"
                autoComplete="off"
                {...fieldProps(q)}
                value={slice.quantityText}
                onChange={(event) =>
                  onChange((v) => updateSlice(v, line.lineId, slice.sliceId, { quantityText: event.target.value }))
                }
              />
              {fieldError(q)}
              <label htmlFor={d}>
                {sliceIndex + 1}. {T.field.sliceDate}
              </label>
              <input
                type="date"
                {...fieldProps(d)}
                value={slice.requestedDeliveryDate}
                onChange={(event) =>
                  onChange((v) =>
                    updateSlice(v, line.lineId, slice.sliceId, { requestedDeliveryDate: event.target.value }),
                  )
                }
              />
              {fieldError(d)}
              <button
                type="button"
                className={styles.linkButton}
                onClick={() => onChange((v) => removeSlice(v, line.lineId, slice.sliceId))}
              >
                {T.removeSlice}
              </button>
            </div>
          )
        })}
        {fieldError(id('slices'))}
        {hint === null ? null : 'exceeds' in hint ? null : (
          <p className={styles.muted} aria-live="polite" data-testid={`sales-line-editor-${index + 1}-unplanned`}>
            {T.unplanned(hint.unplanned)} {T.unplannedNote}
          </p>
        )}
        <button
          type="button"
          className={styles.secondaryButton}
          onClick={() => onChange((v) => addSlice(v, line.lineId, browserIds()))}
        >
          {T.addSlice}
        </button>
      </fieldset>

      <button
        type="button"
        className={styles.linkButton}
        onClick={() => onChange((v) => removeLine(v, line.lineId))}
      >
        {T.removeLine}
      </button>
    </fieldset>
  )
}
