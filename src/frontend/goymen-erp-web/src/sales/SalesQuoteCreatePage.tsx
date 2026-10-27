/**
 * S-01 — "Yeni satış teklifi": `POST /api/v1/sales/quotes` (API-22, #828).
 *
 * A quote may cover one or more requests of ONE customer (No 163). The requests come from the pool (`?talep=<id>…`, chosen
 * on the requests list or a request's detail); each is read afresh from the server, and the quote is offered only when
 * every one was read, none is rejected and all are the same customer account — the server checks the same again
 * (`sales_quote_request_*`). Their lines are copied as the quote's starting lines (product, unit, quantity, dates, note);
 * the person adds prices. Without requests the customer is chosen from the Cari owner's accounts.
 *
 * Price, price unit and discount are optional texts; the server judges scale, range and who may write a price (no
 * permission or discount policy is chosen here). The validity date is stored as typed — no default period, no expiry rule.
 */

import { useEffect, useState } from 'react'
import { useQueries } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { createQuote, fetchRequestDetail } from './poolApi'
import {
  QUOTE_MAXIMUM_REQUESTS,
  REQUEST_STATE,
  SALES_QUOTE_PATH,
  type CreateSalesQuoteBody,
  type SalesQuoteMutation,
  type SalesRequestDetail,
} from './poolContract'
import {
  POOL_FIELD,
  emptyPoolForm,
  emptyPoolLine,
  lineFromRequest,
  validateQuoteForm,
  type PoolFormErrors,
  type PoolFormValues,
} from './poolForm'
import { POOL_TEXT } from './poolText'
import {
  QUOTE_FROM_REQUEST_PARAM,
  SALES_POOL_ROUTE,
  salesQuoteScreenPath,
  salesRequestScreenPath,
  useSalesPoolCapability,
} from './poolCapabilities'
import { poolNotice, usePoolAttempt, usePoolDirty } from './poolAttempt'
import { usePoolCurrencies, usePoolOptions } from './poolOptions'
import {
  PoolAttemptNotice,
  PoolCustomerField,
  PoolErrorSummary,
  PoolLinesEditor,
  PoolPartyCell
} from './SalesPoolShared'
import { cssId, serverFieldErrors } from './poolView'
import { browserIds } from './orderDraftForm'
import { orderAttemptBinding } from './orderAttempt'
import { isReferenceId } from './wirePrimitives'
import { classifyOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import styles from './SalesPool.module.css'

const T = POOL_TEXT.quoteCreate
const F = POOL_TEXT.form

export const SALES_QUOTE_CREATE_SCOPE = 'sales.quote.create'

export function SalesQuoteCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const [params] = useSearchParams()
  const requested = params.getAll(QUOTE_FROM_REQUEST_PARAM)
  if (orderAttemptBinding(shell, SALES_QUOTE_CREATE_SCOPE) === null || companyId === null || namespace === null) {
    return (
      <section className={styles.page} aria-labelledby="pool-quote-create-heading">
        <h1 id="pool-quote-create-heading" className={styles.title}>{T.heading}</h1>
        <p role="status">{POOL_TEXT.common.singleCompanyOnly}</p>
      </section>
    )
  }
  return <CoveredRequests key={`${namespace}|${requested.join(',')}`} namespace={namespace} companyId={companyId} requested={requested} />
}

/** Reads every named request afresh; the form opens only on a coherent, readable set. */
function CoveredRequests({
  namespace,
  companyId,
  requested,
}: {
  readonly namespace: string
  readonly companyId: string
  readonly requested: readonly string[]
}) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const ids = [...new Set(requested)]
  const malformed = ids.some((id) => !isReferenceId(id))
  const reads = useQueries({
    queries: ids.map((id) => ({
      queryKey: [namespace, shell.routeEntryGeneration, 'sales-request', id],
      enabled: !malformed,
      queryFn: async ({ signal }: { readonly signal: AbortSignal }) => {
        const snapshot = boundary.capture()
        const outcome = await fetchRequestDetail(id, companyId, signal)
        if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
        return outcome
      },
    })),
  })

  const heading = (
    <header className={styles.header}>
      <h1 id="pool-quote-create-heading" className={styles.title}>{T.heading}</h1>
      <p className={styles.muted}>{T.intro}</p>
    </header>
  )
  const blocked = (message: string) => (
    <section className={styles.page} aria-labelledby="pool-quote-create-heading">
      {heading}
      <p role="alert" className={styles.notice} data-testid="pool-quote-requests-blocked">{message}</p>
    </section>
  )

  if (malformed) return blocked(T.requestsUnreadable)
  if (ids.length > QUOTE_MAXIMUM_REQUESTS) return blocked(T.requestsTooMany)
  const failed = reads.find((read) => read.data !== undefined && read.data.type !== 'ok-single')
  if (failed?.data !== undefined) {
    return (
      <section className={styles.page} aria-labelledby="pool-quote-create-heading">
        {heading}
        <p role="alert" className={styles.notice} data-testid="pool-quote-requests-blocked">{T.requestsUnreadable}</p>
        <StateBlock state={classifyOutcome(failed.data)} subject="seçilen talep" onRetry={() => reads.forEach((read) => void read.refetch())} />
      </section>
    )
  }
  if (reads.some((read) => read.data === undefined)) {
    return (
      <section className={styles.page} aria-labelledby="pool-quote-create-heading">
        {heading}
        <p role="status" className={styles.muted}>{T.requestsLoading}</p>
      </section>
    )
  }
  const requests = reads.map((read) => (read.data as { readonly type: 'ok-single'; readonly data: SalesRequestDetail }).data)
  const rejected = requests.find((detail) => detail.request.state === REQUEST_STATE.rejected)
  if (rejected !== undefined) return blocked(T.requestRejected(rejected.request.documentNo))
  const customers = new Set(requests.map((detail) => `${detail.request.partyId}|${detail.request.companyAccountId}`))
  if (customers.size > 1) return blocked(T.requestsMixed)
  return <QuoteCreateForm companyId={companyId} requests={requests} />
}

function initialValues(requests: readonly SalesRequestDetail[]): PoolFormValues {
  const empty = emptyPoolForm(browserIds())
  if (requests.length === 0) return empty
  const first = requests[0].request
  return {
    ...empty,
    partyId: first.partyId,
    companyAccountId: first.companyAccountId,
    lines: requests.flatMap((detail) => detail.lines.map((line) => lineFromRequest(browserIds(), line))),
  }
}

function QuoteCreateForm({ companyId, requests }: { readonly companyId: string; readonly requests: readonly SalesRequestDetail[] }) {
  const navigate = useNavigate()
  const canOpen = useSalesPoolCapability('quotesDetail')
  const canList = useSalesPoolCapability('quotesList')
  const canOpenRequest = useSalesPoolCapability('requestsDetail')
  const [initial] = useState<PoolFormValues>(() => initialValues(requests))
  const [values, setValues] = useState<PoolFormValues>(initial)
  const [errors, setErrors] = useState<PoolFormErrors>({})
  const [summaryRequest, setSummaryRequest] = useState(0)
  const [accountSearch, setAccountSearch] = useState('')
  const [productSearch, setProductSearch] = useState('')
  const options = usePoolOptions(companyId, values.businessDate, accountSearch, productSearch)
  const currencies = usePoolCurrencies(companyId, values.businessDate)
  const fromRequests = requests.length > 0
  const requestIds = requests.map((detail) => detail.request.id)

  const attempt = usePoolAttempt<CreateSalesQuoteBody, SalesQuoteMutation>(
    SALES_QUOTE_CREATE_SCOPE,
    (snapshot) => createQuote(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId }),
    (next) => {
      if (next.phase === 'completed' && canOpen) navigate(salesQuoteScreenPath(next.result.detail.quote.id))
      if (next.phase === 'failed' && next.unresolvedSince === undefined && next.failure === 'validation') {
        setErrors(serverFieldErrors(next.problem, next.snapshot.payload.lines))
        setSummaryRequest((n) => n + 1)
      }
    },
  )
  usePoolDirty(SALES_QUOTE_CREATE_SCOPE, attempt.frozen || (attempt.state.phase !== 'completed' && values !== initial))

  useEffect(() => {
    if (summaryRequest > 0) document.getElementById('pool-error-summary')?.focus()
  }, [summaryRequest])

  const disabled = attempt.frozen || attempt.state.phase === 'completed'
  const update = (next: Partial<PoolFormValues>) => setValues((current) => ({ ...current, ...next }))

  function submit() {
    // The customer of covered requests is the server's own record, not a choice from the accounts list.
    const accounts = fromRequests ? [{ partyId: values.partyId, companyAccountId: values.companyAccountId, label: '' }] : options.accounts
    const result = validateQuoteForm(values, accounts, currencies.options, requestIds)
    if (!result.ok) {
      setErrors(result.errors)
      setSummaryRequest((n) => n + 1)
      return
    }
    setErrors({})
    attempt.submit(SALES_QUOTE_PATH, result.payload)
  }

  const notice = poolNotice(attempt.state, {
    noun: T.noun,
    onRecord: false,
    blockedChange: attempt.blockedChange,
    completed: (result) => T.completed(result.detail.quote.documentNo),
  })

  const currencyHelp =
    currencies.state.status === 'idle'
      ? F.currencyNeedsDate
      : currencies.state.status === 'loading'
        ? F.currencyLoading
        : currencies.state.status === 'unavailable'
          ? F.referencesUnavailable(currencies.state.reason)
          : undefined

  return (
    <section className={`${styles.page} ${styles.form}`} aria-labelledby="pool-quote-create-heading">
      <header className={styles.header}>
        <h1 id="pool-quote-create-heading" className={styles.title}>{T.heading}</h1>
        {canList ? (
          <Link className={styles.link} to={SALES_POOL_ROUTE.quotes}>
            ← {POOL_TEXT.quoteDetail.back}
          </Link>
        ) : null}
        <p className={styles.muted}>{T.intro}</p>
      </header>
      <PoolErrorSummary errors={errors} />
      <section className={styles.section} aria-labelledby="pool-quote-requests-heading">
        <h2 id="pool-quote-requests-heading" className={styles.subtitle}>{T.coveredRequests}</h2>
        {errors[POOL_FIELD.requests] === undefined ? null : (
          <p className={styles.rowError} id={cssId(POOL_FIELD.requests)}>{errors[POOL_FIELD.requests]}</p>
        )}
        {fromRequests ? (
          <ul data-testid="pool-quote-covered-requests">
            {requests.map((detail) => (
              <li key={detail.request.id}>
                {canOpenRequest ? (
                  <Link className={styles.link} to={salesRequestScreenPath(detail.request.id)}>{detail.request.documentNo}</Link>
                ) : (
                  detail.request.documentNo
                )}
              </li>
            ))}
          </ul>
        ) : (
          <p className={styles.muted}>{T.noRequests}</p>
        )}
      </section>
      <form
        noValidate
        className={styles.page}
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <div className={`${styles.section} ${styles.grid}`}>
          {fromRequests ? (
            <div className={styles.wide} id={cssId(POOL_FIELD.customer)}>
              <p className={styles.muted}>{F.customer}</p>
              <p><PoolPartyCell partyId={values.partyId} /></p>
              <p className={styles.muted}>{T.customerFromRequests}</p>
              {errors[POOL_FIELD.customer] === undefined ? null : <p className={styles.rowError}>{errors[POOL_FIELD.customer]}</p>}
            </div>
          ) : (
            <PoolCustomerField
              options={options}
              partyId={values.partyId}
              companyAccountId={values.companyAccountId}
              error={errors[POOL_FIELD.customer]}
              disabled={disabled}
              search={accountSearch}
              onSearch={setAccountSearch}
              onChoose={(partyId, companyAccountId) => update({ partyId, companyAccountId })}
            />
          )}
          <Field id={cssId(POOL_FIELD.businessDate)} label={F.businessDate} error={errors[POOL_FIELD.businessDate]}>
            {(control) => (
              <input {...control} className={styles.control} type="date" value={values.businessDate} disabled={disabled}
                onChange={(event) => update({ businessDate: event.target.value })} />
            )}
          </Field>
          <Field id={cssId(POOL_FIELD.currency)} label={F.currency} error={errors[POOL_FIELD.currency]} help={currencyHelp}>
            {(control) => (
              <select {...control} className={styles.control} value={values.currencyId} disabled={disabled || currencies.state.status !== 'ready'}
                onChange={(event) => update({ currencyId: event.target.value })}>
                <option value="">{F.currencyPlaceholder}</option>
                {currencies.options.map((currency) => (
                  <option key={currency.currencyId} value={currency.currencyId}>{currency.label}</option>
                ))}
              </select>
            )}
          </Field>
          <Field id={cssId(POOL_FIELD.validUntil)} label={F.validUntil} error={errors[POOL_FIELD.validUntil]}>
            {(control) => (
              <input {...control} className={styles.control} type="date" value={values.validUntil} disabled={disabled}
                onChange={(event) => update({ validUntil: event.target.value })} />
            )}
          </Field>
          <Field id={cssId(POOL_FIELD.requestedDeliveryDate)} label={F.requestedDeliveryDate} error={errors[POOL_FIELD.requestedDeliveryDate]}>
            {(control) => (
              <input {...control} className={styles.control} type="date" value={values.requestedDeliveryDate} disabled={disabled}
                onChange={(event) => update({ requestedDeliveryDate: event.target.value })} />
            )}
          </Field>
          <Field id={cssId(POOL_FIELD.note)} label={F.note} error={errors[POOL_FIELD.note]} className={styles.wide}>
            {(control) => (
              <textarea {...control} className={styles.control} value={values.note} disabled={disabled}
                onChange={(event) => update({ note: event.target.value })} />
            )}
          </Field>
          <Field id={cssId('productSearch')} label={F.productSearch}>
            {(control) => (
              <input {...control} className={styles.control} type="search" value={productSearch} disabled={disabled}
                onChange={(event) => setProductSearch(event.target.value)} />
            )}
          </Field>
        </div>
        <PoolLinesEditor
          lines={values.lines}
          errors={errors}
          withPrices
          products={options.products}
          productsUnavailable={options.productsUnavailable}
          disabled={disabled}
          onChange={(lineId, next) => update({ lines: values.lines.map((line) => (line.lineId === lineId ? next : line)) })}
          onAdd={() => update({ lines: [...values.lines, emptyPoolLine(browserIds())] })}
          onRemove={(lineId) => update({ lines: values.lines.filter((line) => line.lineId !== lineId) })}
        />
        <PoolAttemptNotice attempt={attempt} notice={notice} testId="pool-quote-create-notice" />
        <div className={styles.actions}>
          <Button type="submit" variant="primary" disabled={disabled}>
            {attempt.state.phase === 'submitting' ? T.submitting : T.submit}
          </Button>
        </div>
      </form>
    </section>
  )
}
