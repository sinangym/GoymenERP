/**
 * S-01 — "Yeni satış talebi": `POST /api/v1/sales/requests` (API-20, #824). A request records the customer's demand —
 * customer, date, product lines with quantities — with no price; it is born `Submitted` and waits for its approval.
 *
 * One keyed attempt per submitted snapshot (./poolAttempt.ts): an unknown outcome keeps the key and freezes the form
 * until the same snapshot is retried or left; a refusal shows the server's field errors on the fields they name and
 * keeps every value (LF-13). A recorded request opens its detail when the shell publishes it.
 */

import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { createRequest } from './poolApi'
import { SALES_REQUEST_PATH, type CreateSalesRequestBody, type SalesRequestMutation } from './poolContract'
import { POOL_FIELD, emptyPoolForm, emptyPoolLine, validateRequestForm, type PoolFormErrors, type PoolFormValues } from './poolForm'
import { POOL_TEXT } from './poolText'
import { SALES_POOL_ROUTE, salesRequestScreenPath, useSalesPoolCapability } from './poolCapabilities'
import { poolNotice, usePoolAttempt, usePoolDirty } from './poolAttempt'
import { recallPoolAttempt } from './pendingPoolAttempts'
import { usePoolOptions } from './poolOptions'
import { PoolAttemptNotice, PoolCustomerField, PoolErrorSummary, PoolLinesEditor } from './SalesPoolShared'
import { cssId, serverFieldErrors } from './poolView'
import { browserIds } from './orderDraftForm'
import { orderAttemptBinding } from './orderAttempt'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import styles from './SalesPool.module.css'

const T = POOL_TEXT.requestCreate
const F = POOL_TEXT.form

export const SALES_REQUEST_CREATE_SCOPE = 'sales.request.create'

export function SalesRequestCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  if (orderAttemptBinding(shell, SALES_REQUEST_CREATE_SCOPE) === null || companyId === null) {
    return (
      <section className={styles.page} aria-labelledby="pool-request-create-heading">
        <h1 id="pool-request-create-heading" className={styles.title}>{T.heading}</h1>
        <p role="status">{POOL_TEXT.common.singleCompanyOnly}</p>
      </section>
    )
  }
  return <RequestCreate key={namespace ?? 'no-context'} companyId={companyId} />
}

function RequestCreate({ companyId }: { readonly companyId: string }) {
  const navigate = useNavigate()
  const canOpen = useSalesPoolCapability('requestsDetail')
  const canList = useSalesPoolCapability('requestsList')
  const shell = useShellState()
  // An attempt of this binding whose outcome is unknown comes back with the values it was sent from (its key is kept).
  const [values, setValues] = useState<PoolFormValues>(
    () => recallPoolAttempt<CreateSalesRequestBody, SalesRequestMutation, PoolFormValues>(orderAttemptBinding(shell, SALES_REQUEST_CREATE_SCOPE))?.values ?? emptyPoolForm(browserIds()),
  )
  const [errors, setErrors] = useState<PoolFormErrors>({})
  const [summaryRequest, setSummaryRequest] = useState(0)
  const [accountSearch, setAccountSearch] = useState('')
  const [productSearch, setProductSearch] = useState('')
  const options = usePoolOptions(companyId, values.businessDate, accountSearch, productSearch)

  const attempt = usePoolAttempt<CreateSalesRequestBody, SalesRequestMutation>(
    SALES_REQUEST_CREATE_SCOPE,
    (snapshot) => createRequest(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId }),
    (next) => {
      if (next.phase === 'completed' && canOpen) navigate(salesRequestScreenPath(next.result.detail.request.id))
      if (next.phase === 'failed' && next.unresolvedSince === undefined && next.failure === 'validation') {
        setErrors(serverFieldErrors(next.problem, next.snapshot.payload.lines))
        setSummaryRequest((n) => n + 1)
      }
    },
    { values },
  )
  usePoolDirty(SALES_REQUEST_CREATE_SCOPE, attempt.frozen || (attempt.state.phase !== 'completed' && isDirty(values)))

  useEffect(() => {
    if (summaryRequest > 0) document.getElementById('pool-error-summary')?.focus()
  }, [summaryRequest])

  const disabled = attempt.frozen || attempt.state.phase === 'completed'
  const update = (next: Partial<PoolFormValues>) => setValues((current) => ({ ...current, ...next }))

  function submit() {
    const result = validateRequestForm(values, options.accounts)
    if (!result.ok) {
      setErrors(result.errors)
      setSummaryRequest((n) => n + 1)
      return
    }
    setErrors({})
    attempt.submit(SALES_REQUEST_PATH, result.payload)
  }

  const notice = poolNotice(attempt.state, {
    noun: T.noun,
    onRecord: false,
    blockedChange: attempt.blockedChange,
    completed: (result) => T.completed(result.detail.request.documentNo),
  })

  return (
    <section className={`${styles.page} ${styles.form}`} aria-labelledby="pool-request-create-heading">
      <header className={styles.header}>
        <h1 id="pool-request-create-heading" className={styles.title}>
          {T.heading}
        </h1>
        {canList ? (
          <Link className={styles.link} to={SALES_POOL_ROUTE.requests}>
            ← {POOL_TEXT.requestDetail.back}
          </Link>
        ) : null}
        <p className={styles.muted}>{T.intro}</p>
      </header>
      <PoolErrorSummary errors={errors} />
      <form
        noValidate
        className={styles.page}
        onSubmit={(event) => {
          event.preventDefault()
          submit()
        }}
      >
        <div className={`${styles.section} ${styles.grid}`}>
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
          <Field id={cssId(POOL_FIELD.businessDate)} label={F.businessDate} error={errors[POOL_FIELD.businessDate]}>
            {(control) => (
              <input {...control} className={styles.control} type="date" value={values.businessDate} disabled={disabled}
                onChange={(event) => update({ businessDate: event.target.value })} />
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
          withPrices={false}
          products={options.products}
          productsUnavailable={options.productsUnavailable}
          disabled={disabled}
          onChange={(lineId, next) => update({ lines: values.lines.map((line) => (line.lineId === lineId ? next : line)) })}
          onAdd={() => update({ lines: [...values.lines, emptyPoolLine(browserIds())] })}
          onRemove={(lineId) => update({ lines: values.lines.filter((line) => line.lineId !== lineId) })}
        />
        <PoolAttemptNotice attempt={attempt} notice={notice} testId="pool-request-create-notice" />
        <div className={styles.actions}>
          <Button type="submit" variant="primary" disabled={disabled}>
            {attempt.state.phase === 'submitting' ? T.submitting : T.submit}
          </Button>
        </div>
      </form>
    </section>
  )
}

/** Whether the form holds anything the person typed or chose. */
function isDirty(values: PoolFormValues): boolean {
  return (
    values.partyId !== '' ||
    values.businessDate !== '' ||
    values.requestedDeliveryDate !== '' ||
    values.note !== '' ||
    values.lines.length !== 1 ||
    values.lines.some((line) => line.productId !== '' || line.quantityText !== '' || line.orderUnitId !== '' || line.note !== '' || line.requestedDeliveryDate !== '')
  )
}
