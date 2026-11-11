/**
 * UI-70b S-10 "Yeni müşteri iade faturası": `POST /api/v1/sales/customer-returns` (API-40, SAT-D288…SAT-D293). The ERP
 * RECORDS the return invoice the customer issued — it never issues one. The return names its finalized original sales
 * invoice (mandatory), optionally one shipment of that invoice, the customer's document number and date, and a quantity
 * for each returned line. The unit price and every amount come from the server (SAT-D241/SAT-D292); this screen computes
 * nothing and states no remaining quantity — the server refuses an excess (SAT-D293) and the refusal is shown.
 *
 * One keyed attempt per submitted snapshot (./poolAttempt.ts, persisted): an unknown outcome keeps the key and freezes the
 * form until the same snapshot is retried or left. A recorded return opens its detail when the shell publishes it.
 */
import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { createCustomerReturn, type CreateCustomerReturnBody, type CustomerReturnMutation } from './customerReturnApi'
import { CUSTOMER_RETURN_DOCUMENT_NO_MAX, SALES_CUSTOMER_RETURNS_PATH } from './customerReturnContract'
import {
  CUSTOMER_RETURN_FIELD,
  serverCustomerReturnErrors,
  shipmentsOf,
  validateCustomerReturnForm,
  visibleLines,
  type CustomerReturnFormErrors,
  type CustomerReturnFormValues,
} from './customerReturnForm'
import { CUSTOMER_RETURN_INVOICE_PARAM, CUSTOMER_RETURN_SUBJECT, CUSTOMER_RETURN_TEXT } from './customerReturnText'
import { customerReturnNotice } from './customerReturnNotice'
import { SALES_CAPABILITY, SALES_ROUTE, salesCustomerReturnPath } from './capabilities'
import { fetchSalesInvoice } from './invoiceApi'
import { fetchSalesInvoicePage } from './invoiceListApi'
import type { SalesInvoiceDetail } from './invoiceFinalizationContract'
import { INVOICE_STATE_TEXT } from './invoiceText'
import { usePoolAttempt, usePoolDirty } from './poolAttempt'
import { recallPoolAttempt } from './pendingPoolAttempts'
import { PoolAttemptNotice, PoolContinuation, PoolErrorSummary } from './SalesPoolShared'
import { usePoolPages } from './usePoolPages'
import { cssId } from './poolView'
import { orderAttemptBinding } from './orderAttempt'
import { isReferenceId } from './wirePrimitives'
import { classifyOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import styles from './SalesPool.module.css'

const T = CUSTOMER_RETURN_TEXT.create
const C = CUSTOMER_RETURN_TEXT.common

export const SALES_CUSTOMER_RETURN_CREATE_SCOPE = 'sales.customer-return.create'

export function SalesCustomerReturnCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  if (orderAttemptBinding(shell, SALES_CUSTOMER_RETURN_CREATE_SCOPE) === null || companyId === null) {
    return (
      <section className={styles.page} aria-labelledby="customer-return-create-heading">
        <h1 id="customer-return-create-heading" className={styles.title}>{T.heading}</h1>
        <p role="status">{C.singleCompanyOnly}</p>
      </section>
    )
  }
  return <ReturnCreate key={namespace ?? 'no-context'} namespace={namespace} companyId={companyId} />
}

const invoicePage = (cursor: string | null, companyId: string, signal: AbortSignal | undefined) =>
  fetchSalesInvoicePage(cursor, companyId, signal)

function ReturnCreate({ namespace, companyId }: { readonly namespace: string | null; readonly companyId: string }) {
  const shell = useShellState()
  const navigate = useNavigate()
  const boundary = useAuthBoundary()
  const [params] = useSearchParams()
  const canOpen = hasCapability(shell, SALES_CAPABILITY.customerReturnsDetail)
  const canList = hasCapability(shell, SALES_CAPABILITY.customerReturnsList)
  const preselected = params.get(CUSTOMER_RETURN_INVOICE_PARAM)

  const [values, setValues] = useState<CustomerReturnFormValues>(
    () =>
      recallPoolAttempt<CreateCustomerReturnBody, CustomerReturnMutation, CustomerReturnFormValues>(
        orderAttemptBinding(shell, SALES_CUSTOMER_RETURN_CREATE_SCOPE),
      )?.values ?? {
        invoiceId: preselected !== null && isReferenceId(preselected) ? preselected : '',
        documentNo: '',
        documentDate: '',
        shipmentId: '',
        quantities: {},
      },
  )
  const [errors, setErrors] = useState<CustomerReturnFormErrors>({})
  const [summaryRequest, setSummaryRequest] = useState(0)

  const invoices = usePoolPages(namespace, 'sales-customer-return-invoices', invoicePage, null)
  const invoiceKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-customer-return-invoice', values.invoiceId]
  const invoice = useQuery({
    queryKey: invoiceKey,
    enabled: values.invoiceId !== '',
    retry: false,
    refetchOnWindowFocus: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchSalesInvoice(values.invoiceId, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const detail = invoice.data?.type === 'ok-single' ? invoice.data.data : null

  const attempt = usePoolAttempt<CreateCustomerReturnBody, CustomerReturnMutation>(
    SALES_CUSTOMER_RETURN_CREATE_SCOPE,
    (snapshot) => createCustomerReturn(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId }),
    (next) => {
      if (next.phase === 'completed' && canOpen) navigate(salesCustomerReturnPath(next.result.view.id))
      if (next.phase === 'failed' && next.unresolvedSince === undefined && next.failure === 'validation') {
        setErrors(serverCustomerReturnErrors(next.problem?.errors ?? []))
        setSummaryRequest((n) => n + 1)
      }
    },
    { values },
  )
  usePoolDirty(SALES_CUSTOMER_RETURN_CREATE_SCOPE, attempt.frozen || (attempt.state.phase !== 'completed' && isDirty(values)))

  useEffect(() => {
    if (summaryRequest > 0) document.getElementById('pool-error-summary')?.focus()
  }, [summaryRequest])

  const disabled = attempt.frozen || attempt.state.phase === 'completed'
  const update = (next: Partial<CustomerReturnFormValues>) => setValues((current) => ({ ...current, ...next }))

  function submit() {
    const result = validateCustomerReturnForm(values, detail)
    if (!result.ok) {
      setErrors(result.errors)
      setSummaryRequest((n) => n + 1)
      return
    }
    setErrors({})
    attempt.submit(SALES_CUSTOMER_RETURNS_PATH, result.payload)
  }

  const notice = customerReturnNotice(attempt.state, {
    noun: T.noun,
    onRecord: false,
    blockedChange: attempt.blockedChange,
    completed: (result) => T.completed(result.view.customerDocumentNo),
    serviceCode: null,
  })

  const invoiceRows = invoices.rows ?? []
  // A preselected (or recalled) invoice that is not among the read pages still shows as the chosen option.
  const chosenMissing = values.invoiceId !== '' && !invoiceRows.some((row) => row.id === values.invoiceId)

  return (
    <section className={`${styles.page} ${styles.form}`} aria-labelledby="customer-return-create-heading">
      <header className={styles.header}>
        <h1 id="customer-return-create-heading" className={styles.title}>
          {T.heading}
        </h1>
        {canList ? (
          <Link className={styles.link} to={SALES_ROUTE.customerReturns}>
            ← {T.back}
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
          <Field id={cssId(CUSTOMER_RETURN_FIELD.invoice)} label={T.invoice} help={T.invoiceNotFinalized} error={errors[CUSTOMER_RETURN_FIELD.invoice]} className={styles.wide}>
            {(control) => (
              <select
                {...control}
                className={styles.control}
                value={values.invoiceId}
                disabled={disabled || invoices.rows === null}
                onChange={(event) => update({ invoiceId: event.target.value, shipmentId: '', quantities: {} })}
              >
                <option value="">{T.invoicePlaceholder}</option>
                {chosenMissing ? <option value={values.invoiceId}>{values.invoiceId}</option> : null}
                {invoiceRows.map((row) => (
                  <option key={row.id} value={row.id} disabled={row.state !== 'Finalized'}>
                    {T.invoiceOption(row.documentNo, INVOICE_STATE_TEXT[row.state])}
                  </option>
                ))}
              </select>
            )}
          </Field>
          <div className={styles.wide}>
            {invoices.rows === null ? (
              <StateBlock state={invoices.firstState ?? { kind: 'initial-loading' }} subject={CUSTOMER_RETURN_SUBJECT.invoices} onRetry={invoices.retryFirst} />
            ) : (
              <PoolContinuation list={invoices} />
            )}
          </div>
          <Field id={cssId(CUSTOMER_RETURN_FIELD.documentNo)} label={C.customerDocumentNo} error={errors[CUSTOMER_RETURN_FIELD.documentNo]}>
            {(control) => (
              <input {...control} className={styles.control} type="text" value={values.documentNo} disabled={disabled}
                maxLength={CUSTOMER_RETURN_DOCUMENT_NO_MAX} autoComplete="off"
                onChange={(event) => update({ documentNo: event.target.value })} />
            )}
          </Field>
          <Field id={cssId(CUSTOMER_RETURN_FIELD.documentDate)} label={C.customerDocumentDate} error={errors[CUSTOMER_RETURN_FIELD.documentDate]}>
            {(control) => (
              <input {...control} className={styles.control} type="date" value={values.documentDate} disabled={disabled}
                onChange={(event) => update({ documentDate: event.target.value })} />
            )}
          </Field>
        </div>

        {values.invoiceId === '' ? null : detail === null ? (
          <StateBlock
            state={invoice.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(invoice.data)}
            subject={CUSTOMER_RETURN_SUBJECT.invoice}
            onRetry={() => void invoice.refetch()}
          />
        ) : (
          <ReturnLines detail={detail} values={values} errors={errors} disabled={disabled} onChange={update} />
        )}

        <PoolAttemptNotice attempt={attempt} notice={notice} testId="customer-return-create-notice" />
        <div className={styles.actions}>
          <Button type="submit" variant="primary" disabled={disabled}>
            {attempt.state.phase === 'submitting' ? T.submitting : T.submit}
          </Button>
        </div>
      </form>
    </section>
  )
}

function ReturnLines({
  detail,
  values,
  errors,
  disabled,
  onChange,
}: {
  readonly detail: SalesInvoiceDetail
  readonly values: CustomerReturnFormValues
  readonly errors: CustomerReturnFormErrors
  readonly disabled: boolean
  readonly onChange: (next: Partial<CustomerReturnFormValues>) => void
}) {
  const shipments = shipmentsOf(detail)
  const lines = visibleLines(detail, values.shipmentId)
  return (
    <>
      <div className={`${styles.section} ${styles.grid}`}>
        <Field id={cssId(CUSTOMER_RETURN_FIELD.shipment)} label={T.shipment} help={T.shipmentHint} error={errors[CUSTOMER_RETURN_FIELD.shipment]} className={styles.wide}>
          {(control) => (
            <select
              {...control}
              className={styles.control}
              value={values.shipmentId}
              disabled={disabled}
              onChange={(event) => {
                const shipmentId = event.target.value
                // Quantities typed on lines of another shipment are dropped: they would not be sent.
                const kept = new Set(visibleLines(detail, shipmentId).map((line) => line.lineId))
                const quantities = Object.fromEntries(Object.entries(values.quantities).filter(([lineId]) => kept.has(lineId)))
                onChange({ shipmentId, quantities })
              }}
            >
              <option value="">{T.shipmentNone}</option>
              {shipments.map((shipment) => (
                <option key={shipment.shipmentId} value={shipment.shipmentId}>
                  {T.shipmentOption(shipment.dispatchNoteNumber)}
                </option>
              ))}
            </select>
          )}
        </Field>
      </div>
      <section className={styles.section} aria-labelledby="customer-return-create-lines-heading">
        <h2 id="customer-return-create-lines-heading" className={styles.subtitle}>
          {T.linesHeading}
        </h2>
        {errors[CUSTOMER_RETURN_FIELD.lines] === undefined ? null : (
          <p id={cssId(CUSTOMER_RETURN_FIELD.lines)} className={styles.rowError} role="alert" tabIndex={-1}>
            {errors[CUSTOMER_RETURN_FIELD.lines]}
          </p>
        )}
        <div className={styles.scroll}>
          <table aria-labelledby="customer-return-create-lines-heading">
            <caption className={styles.muted}>{T.linesCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{T.lineNo}</th>
                <th scope="col">{T.product}</th>
                <th scope="col" className={styles.numeric}>{T.invoicedQuantity}</th>
                <th scope="col" className={styles.numeric}>{T.unitPrice}</th>
                <th scope="col">{T.returnQuantity}</th>
              </tr>
            </thead>
            <tbody>
              {lines.map((line) => {
                const fieldKey = CUSTOMER_RETURN_FIELD.line(line.lineId)
                const error = errors[fieldKey]
                const errorId = `${cssId(fieldKey)}-error`
                return (
                  <tr key={line.lineId} data-testid={`customer-return-create-line-${line.lineNo}`}>
                    <td>{line.lineNo}</td>
                    <td className={styles.identifier}>{line.productId}</td>
                    <td className={styles.numeric}>
                      {line.quantity} <span className={styles.identifier}>{line.unitId}</span>
                    </td>
                    <td className={styles.numeric}>{line.netUnitPrice}</td>
                    <td>
                      <input
                        id={cssId(fieldKey)}
                        className={styles.control}
                        type="text"
                        inputMode="decimal"
                        autoComplete="off"
                        aria-label={T.returnQuantityLabel(line.lineNo)}
                        aria-invalid={error === undefined ? undefined : true}
                        aria-describedby={error === undefined ? undefined : errorId}
                        value={values.quantities[line.lineId] ?? ''}
                        disabled={disabled}
                        onChange={(event) => onChange({ quantities: { ...values.quantities, [line.lineId]: event.target.value } })}
                      />
                      {error === undefined ? null : (
                        <span id={errorId} className={styles.rowError}>
                          {error}
                        </span>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </section>
    </>
  )
}

function isDirty(values: CustomerReturnFormValues): boolean {
  return (
    values.documentNo !== ''
    || values.documentDate !== ''
    || values.shipmentId !== ''
    || Object.values(values.quantities).some((quantity) => quantity.trim() !== '')
  )
}
