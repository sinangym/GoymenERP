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
 * account, product, version, unit or currency is ever offered. SALES-A2a: the header
 * references are real on main — the accounts list, the currencies list for the business
 * date, and the chosen account's delivery addresses / contact persons (the Cari owner's
 * contact information) — while the lines and the submit stay withheld until the product
 * list (E1); the withheld notice says so honestly.
 */

import { useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router'
import { createDraft, type AccountOption, type ProductOffer, type ProductOption } from './api'
import { SALES_DRAFT_PATH, SALES_PROBLEM_CODE } from './contract'
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
  setAccount,
  setLineProduct,
  sliceFieldId,
  updateLine,
  updateSlice,
  validateOrderDraft,
  type DeliveryReferenceOptions,
  type LineValues,
  type OrderDraftErrors,
  type OrderDraftFormValues,
  type OrderDraftOptions,
} from './orderDraftForm'
import { rememberDraftAttempt, recallDraftAttempt, type DraftAttempt } from './pendingDrafts'
import {
  SalesContactInformationSourceContext,
  SalesReferenceSourceContext,
  currencyOptionsOf,
  type CurrencyReadState,
  type MissingReference,
  type ProductReadState,
  type ReferenceReadState,
} from './referenceSource'
import { SALES_CAPABILITY, SALES_ROUTE, salesDetailPath } from './capabilities'
import { contactLabel } from './format'
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
    />
  )
}

/** A form remembered by an earlier build may predate the SALES-A2a members: nothing chosen, never undefined. */
function withDeliveryMembers(values: OrderDraftFormValues): OrderDraftFormValues {
  const remembered: Partial<Pick<OrderDraftFormValues, 'deliveryAddressId' | 'contactId'>> = values
  return { ...values, deliveryAddressId: remembered.deliveryAddressId ?? '', contactId: remembered.contactId ?? '' }
}

function CreateForm({ binding, companyId }: { readonly binding: string; readonly companyId: string }) {
  const shell = useShellState()
  const dispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const directory = useCompanyDirectory()
  const company = directory.find((entry) => entry.id === companyId)

  const [recalled] = useState(() => recallDraftAttempt(binding))
  const [values, setValues] = useState<OrderDraftFormValues>(() =>
    recalled === null ? emptyOrderDraftForm(browserIds()) : withDeliveryMembers(recalled.values),
  )
  const [attempt, setAttempt] = useState<DraftAttempt>(recalled?.attempt ?? { phase: 'idle' })
  const [errors, setErrors] = useState<OrderDraftErrors>({})
  const [serverMessages, setServerMessages] = useState<readonly string[]>([])
  const [blockedChange, setBlockedChange] = useState(false)
  const [summaryRequest, setSummaryRequest] = useState(0)
  const summaryRef = useRef<HTMLDivElement>(null)

  // The selection sources (S03 / SALES-A2a): the accounts + products composition for the company, the
  // currencies for the business date, and the chosen account's contact information. Each is a real read
  // with its own state; nothing is offered before its owner answered.
  const useOptions = useContext(SalesReferenceSourceContext)
  // #361: the customer select is one server page; the person searches with the server's own `q` (submitted, never
  // typed-through) or continues with the server's cursor. The chosen account is kept across searches in the STATE and
  // on the SCREEN: when the current page does not carry it, the option is merged back in with the label captured at
  // the choice (no extra read), so the select keeps showing it and the submit is validated against the list shown.
  const [accountSearchInput, setAccountSearchInput] = useState('')
  const [accountSearch, setAccountSearch] = useState('')
  const [chosenAccount, setChosenAccount] = useState<AccountOption | null>(null)
  const [productSearchInput, setProductSearchInput] = useState('')
  const [productSearch, setProductSearch] = useState('')
  const [chosenProducts, setChosenProducts] = useState<readonly { readonly effectiveOn: string; readonly option: ProductOption }[]>([])
  const source = useOptions(companyId, values.businessDate, accountSearch, productSearch)
  const useContactInformation = useContext(SalesContactInformationSourceContext)
  const delivery = useContactInformation(companyId, values.partyId, values.companyAccountId)
  /** The full options (lines included) — only when every owner has answered for this company/date/search. */
  const sourceOptions: OrderDraftOptions | null = source.status === 'ready' ? source.options : null
  const productOffer: ProductOffer | null =
    source.status === 'ready'
      ? (source.productOffer ?? { items: source.options.products, searchWindowFull: false })
      : source.header?.products?.status === 'ready'
        ? source.header.products.data
        : null
  const productState: ProductReadState | null =
    source.status === 'ready'
      ? { status: 'ready', data: productOffer as ProductOffer }
      : (source.header?.products ?? null)
  const options = useMemo<OrderDraftOptions | null>(() => {
    if (sourceOptions === null) return null
    const retained = chosenProducts.filter(
      (selected) =>
        selected.effectiveOn === values.businessDate &&
        values.lines.some((line) => line.productId === selected.option.productId),
    )
    // Prefer the explicit selection snapshot even when a fresh result window contains the same id at
    // another version. The effect below then invalidates it; no intervening submit can silently send v8.
    const products = sourceOptions.products.map(
      (product) => retained.find((selected) => selected.option.productId === product.productId)?.option ?? product,
    )
    for (const selected of retained) {
      if (!products.some((product) => product.productId === selected.option.productId)) products.push(selected.option)
    }
    return { ...sourceOptions, products }
  }, [chosenProducts, sourceOptions, values.businessDate, values.lines])
  const pageAccounts = source.status === 'ready' ? source.options.accounts : (source.header?.accounts ?? null)
  const accounts = useMemo<readonly AccountOption[] | null>(() => {
    if (pageAccounts === null) return null
    const chosen =
      chosenAccount !== null && chosenAccount.partyId === values.partyId && chosenAccount.companyAccountId === values.companyAccountId
        ? chosenAccount
        : null
    if (chosen === null || pageAccounts.some((a) => a.partyId === chosen.partyId && a.companyAccountId === chosen.companyAccountId)) return pageAccounts
    return [...pageAccounts, chosen]
  }, [pageAccounts, chosenAccount, values.partyId, values.companyAccountId])
  const accountsOffer = source.status === 'ready' ? null : (source.header?.accountsOffer ?? null)
  const currencyState: CurrencyReadState | null = source.status === 'ready' ? null : (source.header?.currencies ?? null)
  const missing: readonly MissingReference[] | null = source.status === 'unavailable' ? (source.missing ?? null) : null
  const currencyOptions = useMemo<OrderDraftOptions['currencies']>(
    () =>
      source.status === 'ready'
        ? source.options.currencies
        : currencyState?.status === 'ready'
          ? currencyOptionsOf(currencyState.data)
          : [],
    [source, currencyState],
  )
  const currenciesListed = source.status === 'ready' || currencyState?.status === 'ready'
  const deliveryOptions: DeliveryReferenceOptions | null =
    delivery.status === 'ready' ? { addresses: delivery.data.addresses, contacts: delivery.data.contacts } : null

  // Product candidates are effective-date facts. A date change clears the old product/unit selections rather than
  // carrying a candidate into a date for which the owner never offered it.
  const productDateRef = useRef(values.businessDate)
  useEffect(() => {
    if (productDateRef.current === values.businessDate) return
    productDateRef.current = values.businessDate
    setProductSearchInput('')
    setProductSearch('')
    setChosenProducts([])
    setValues((current) => ({
      ...current,
      lines: current.lines.map((line) => ({ ...line, productId: '', orderUnitId: '' })),
    }))
  }, [values.businessDate])

  // A later candidate answer may carry the same product identity at another owner version. Never
  // rewrite the selected snapshot silently: clear product + unit and require an explicit reselect.
  useEffect(() => {
    if (sourceOptions === null) return
    const stale = chosenProducts.filter((selected) => {
      if (selected.effectiveOn !== values.businessDate) return false
      const fresh = sourceOptions.products.find((product) => product.productId === selected.option.productId)
      return fresh !== undefined && fresh.productVersion !== selected.option.productVersion
    })
    if (stale.length === 0) return
    const staleIds = new Set(stale.map((selected) => selected.option.productId))
    const staleLines = values.lines.filter((line) => staleIds.has(line.productId))
    setChosenProducts((current) => current.filter((selected) => !staleIds.has(selected.option.productId)))
    setValues((current) => ({
      ...current,
      lines: current.lines.map((line) =>
        staleIds.has(line.productId) ? { ...line, productId: '', orderUnitId: '' } : line,
      ),
    }))
    setErrors((current) => ({
      ...current,
      ...Object.fromEntries(
        staleLines.map((line) => [lineFieldId(line.lineId, 'productId'), T.validation.productNotOffered]),
      ),
    }))
  }, [chosenProducts, sourceOptions, values.businessDate, values.lines])

  // A chosen currency the real list (for this date) no longer offers is cleared and said so: the form
  // never sends a reference nobody offered, and never keeps one silently.
  useEffect(() => {
    if (!currenciesListed || values.currencyId === '' || currencyOptions.some((c) => c.currencyId === values.currencyId)) return
    setValues((v) => ({ ...v, currencyId: '' }))
    setErrors((e) => ({ ...e, [ORDER_FIELD.currency]: T.validation.currencyNotOffered }))
  }, [currenciesListed, currencyOptions, values.currencyId])

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
          const field = formFieldForServerPath(error.field, snapshot.payload.lines)
          // A stale delivery address / contact is the Cari owner's version moving, not the product's.
          const referenceMoved =
            (field === ORDER_FIELD.deliveryAddress || field === ORDER_FIELD.contact) && error.code === SALES_PROBLEM_CODE.versionConflict
          const message = referenceMoved ? T.references.versionConflict : (problemCodeText(error.code) ?? T.attempt.unexpected)
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
    // The same list the select renders from, the chosen account merged in: screen and payload never disagree.
    const validation = validateOrderDraft(values, accounts === null ? options : { ...options, accounts }, deliveryOptions)
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

      {accounts === null ? (
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
              <label htmlFor="sales-customer-search">{T.references.accountSearch}</label>
              <div className={styles.actions}>
                <input
                  id="sales-customer-search"
                  type="search"
                  maxLength={200}
                  value={accountSearchInput}
                  onChange={(event) => setAccountSearchInput(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      event.preventDefault()
                      setAccountSearch(accountSearchInput)
                    }
                  }}
                  aria-describedby="sales-customer-search-hint"
                  data-testid="sales-customer-search"
                />
                <button type="button" onClick={() => setAccountSearch(accountSearchInput)} data-testid="sales-customer-search-button">
                  {T.references.accountSearchSubmit}
                </button>
                <span id="sales-customer-search-hint" className={styles.muted}>
                  {T.references.accountSearchHint}
                </span>
              </div>
              <label htmlFor={ORDER_FIELD.customer}>{T.field.customer}</label>
              <select
                {...fieldProps(ORDER_FIELD.customer)}
                value={values.partyId === '' ? '' : `${values.partyId}|${values.companyAccountId}`}
                onChange={(event) => {
                  const [partyId = '', companyAccountId = ''] = event.target.value.split('|')
                  setChosenAccount(accounts.find((a) => a.partyId === partyId && a.companyAccountId === companyAccountId) ?? null)
                  setValues((v) => setAccount(v, partyId, companyAccountId))
                }}
              >
                <option value="">{T.choose}</option>
                {accounts.map((a) => (
                  <option key={`${a.partyId}|${a.companyAccountId}`} value={`${a.partyId}|${a.companyAccountId}`}>
                    {a.label}
                  </option>
                ))}
              </select>
              {fieldError(ORDER_FIELD.customer)}
              {accountsOffer !== null ? (
                <p className={styles.muted} style={{ gridColumn: 2 }} data-testid="sales-customer-more-status">
                  {accountsOffer.nextCursor !== null ? (
                    <button
                      type="button"
                      onClick={accountsOffer.loadMore}
                      disabled={accountsOffer.loadingMore}
                      aria-busy={accountsOffer.loadingMore}
                      data-testid="sales-customer-more"
                    >
                      {accountsOffer.loadingMore ? T.references.accountsMoreLoading : T.references.accountsMore}
                    </button>
                  ) : (
                    T.references.accountsEnd
                  )}
                  {accountsOffer.moreFailed !== null ? ` ${T.references.accountsMoreFailed(accountsOffer.moreFailed)}` : ''}
                </p>
              ) : null}

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
                data-testid="sales-currency-select"
                value={values.currencyId}
                disabled={!currenciesListed}
                onChange={(event) => setValues((v) => ({ ...v, currencyId: event.target.value }))}
              >
                <option value="">{T.choose}</option>
                {currencyOptions.map((c) => (
                  <option key={c.currencyId} value={c.currencyId}>
                    {c.label}
                  </option>
                ))}
              </select>
              {fieldError(ORDER_FIELD.currency)}
              {currencyState === null ? null : (
                <ReadStatus
                  testId="sales-currency-status"
                  state={currencyState}
                  idle={T.references.currencyIdle}
                  loading={T.references.currencyLoading}
                  empty={currencyState.status === 'ready' && currencyState.data.length === 0 ? T.references.currencyEmpty : null}
                  unavailable={T.references.currencyUnavailable}
                  retryTestId="sales-currency-retry"
                />
              )}

              <label htmlFor="sales-product-search">{T.references.productSearch}</label>
              <div className={styles.actions}>
                <input
                  id="sales-product-search"
                  type="search"
                  maxLength={200}
                  value={productSearchInput}
                  disabled={values.businessDate === ''}
                  onChange={(event) => setProductSearchInput(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') {
                      event.preventDefault()
                      setProductSearch(productSearchInput)
                    }
                  }}
                  aria-describedby="sales-product-search-hint"
                  data-testid="sales-product-search"
                />
                <button
                  type="button"
                  disabled={values.businessDate === ''}
                  onClick={() => setProductSearch(productSearchInput)}
                  data-testid="sales-product-search-button"
                >
                  {T.references.productSearchSubmit}
                </button>
                <span id="sales-product-search-hint" className={styles.muted}>
                  {T.references.productSearchHint}
                </span>
              </div>
              {productState === null ? null : (
                <ReadStatus
                  testId="sales-product-status"
                  state={productState}
                  idle={T.references.productIdle}
                  loading={T.references.productLoading}
                  empty={
                    productState.status === 'ready' && productState.data.items.length === 0
                      ? T.references.productEmpty
                      : null
                  }
                  unavailable={T.references.productUnavailable}
                  retryTestId="sales-product-retry"
                />
              )}
              {productOffer?.searchWindowFull ? (
                <p className={styles.muted} style={{ gridColumn: 2 }} data-testid="sales-product-window-full">
                  {T.references.productWindowFull}
                </p>
              ) : null}

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

              <label htmlFor={ORDER_FIELD.deliveryAddress}>{T.field.deliveryAddress}</label>
              <select
                {...fieldProps(ORDER_FIELD.deliveryAddress)}
                data-testid="sales-delivery-address-select"
                value={values.deliveryAddressId}
                disabled={delivery.status !== 'ready' || delivery.data.addresses.length === 0}
                onChange={(event) => setValues((v) => ({ ...v, deliveryAddressId: event.target.value }))}
              >
                {delivery.status === 'ready' && delivery.data.addresses.length === 0 ? (
                  <option value="" disabled>
                    {T.references.noAddress}
                  </option>
                ) : (
                  <option value="">{delivery.status === 'loading' ? T.references.deliveryLoading : T.notChosen}</option>
                )}
                {delivery.status === 'ready'
                  ? delivery.data.addresses.map((a) => (
                      <option key={a.id} value={a.id}>
                        {a.text}
                      </option>
                    ))
                  : null}
              </select>
              {fieldError(ORDER_FIELD.deliveryAddress)}

              <label htmlFor={ORDER_FIELD.contact}>{T.field.deliveryContact}</label>
              <select
                {...fieldProps(ORDER_FIELD.contact)}
                data-testid="sales-delivery-contact-select"
                value={values.contactId}
                disabled={delivery.status !== 'ready' || delivery.data.contacts.length === 0}
                onChange={(event) => setValues((v) => ({ ...v, contactId: event.target.value }))}
              >
                {delivery.status === 'ready' && delivery.data.contacts.length === 0 ? (
                  <option value="" disabled>
                    {T.references.noContact}
                  </option>
                ) : (
                  <option value="">{delivery.status === 'loading' ? T.references.deliveryLoading : T.notChosen}</option>
                )}
                {delivery.status === 'ready'
                  ? delivery.data.contacts.map((c) => (
                      <option key={c.id} value={c.id}>
                        {contactLabel(c)}
                      </option>
                    ))
                  : null}
              </select>
              {fieldError(ORDER_FIELD.contact)}
              <ReadStatus
                testId="sales-delivery-status"
                state={delivery}
                idle={T.references.deliveryIdle}
                loading={T.references.deliveryLoading}
                empty={null}
                unavailable={T.references.deliveryUnavailable}
                retryTestId="sales-contact-information-retry"
              />
            </div>
          </fieldset>

          {options === null ? (
            // The header remains usable; lines and submit wait for the current Product owner read. Said plainly.
            <div role="status" className={styles.notice} data-tone="unknown" data-testid="sales-lines-withheld">
              <p>{T.references.linesWithheld}</p>
              {missing !== null && missing.length > 0 ? (
                <p data-testid="sales-references-missing">{T.referencesMissing(missing)}</p>
              ) : null}
            </div>
          ) : (
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
                  onProductChange={(productId) => {
                    const candidate = options.products.find((product) => product.productId === productId)
                    if (candidate !== undefined) {
                      setChosenProducts((current) => [
                        ...current.filter((entry) => entry.option.productId !== candidate.productId),
                        { effectiveOn: values.businessDate, option: candidate },
                      ])
                    }
                    setValues((current) => setLineProduct(current, line.lineId, productId))
                  }}
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
          )}

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

          {options === null ? null : (
            <div className={styles.actions}>
              <button type="submit" className={styles.primaryButton} disabled={frozen} aria-busy={frozen}>
                {T.save}
              </button>
            </div>
          )}
        </form>
      )}
    </section>
  )
}

/**
 * One list read's state beside its select: idle (inputs incomplete), loading, empty (the owner lists
 * none), or unavailable with the real reason and a retry. Nothing is shown for a ready, non-empty list.
 */
function ReadStatus({
  testId,
  state,
  idle,
  loading,
  empty,
  unavailable,
  retryTestId,
}: {
  readonly testId: string
  readonly state: ReferenceReadState<unknown>
  readonly idle: string
  readonly loading: string
  readonly empty: string | null
  readonly unavailable: (reason: string) => string
  readonly retryTestId: string
}) {
  switch (state.status) {
    case 'idle':
      return (
        <p className={styles.muted} style={{ gridColumn: 2 }} data-testid={testId} data-state="idle">
          {idle}
        </p>
      )
    case 'loading':
      return (
        <p className={styles.muted} style={{ gridColumn: 2 }} data-testid={testId} data-state="loading" aria-live="polite">
          {loading}
        </p>
      )
    case 'ready':
      return empty === null ? null : (
        <p className={styles.muted} style={{ gridColumn: 2 }} data-testid={testId} data-state="empty">
          {empty}
        </p>
      )
    case 'unavailable':
      return (
        <p className={styles.fieldError} data-testid={testId} data-state="unavailable" role="status">
          {unavailable(state.reason)}{' '}
          <button type="button" className={styles.linkButton} data-testid={retryTestId} onClick={state.retry}>
            {T.references.retry}
          </button>
        </p>
      )
  }
}

function LineEditor({
  line,
  index,
  options,
  fieldProps,
  fieldError,
  onChange,
  onProductChange,
}: {
  readonly line: LineValues
  readonly index: number
  readonly options: OrderDraftOptions
  readonly fieldProps: (id: string) => Record<string, unknown>
  readonly fieldError: (id: string) => React.ReactNode
  readonly onChange: React.Dispatch<React.SetStateAction<OrderDraftFormValues>>
  readonly onProductChange: (productId: string) => void
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
          onChange={(event) => onProductChange(event.target.value)}
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
