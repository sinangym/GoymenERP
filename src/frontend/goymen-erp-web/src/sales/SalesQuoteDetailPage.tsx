/**
 * S-01 — a sales quote as `GET /api/v1/sales/quotes/{id}` describes it (API-22), with its three commands on an OPEN quote:
 *  - "Revizyon oluştur" (API-22 revision): every line and the terms replaced; Rev.N+1 follows on the server;
 *  - "Kaybedildi olarak işaretle" (API-24): with its free-text reason (master §7.2; no closed reason list is decided);
 *  - "Siparişe dönüştür" (API-23, No 160/162): the current revision's lines become ONE order under the business date the
 *    person names; the answer is the ORDER, and the screen opens it when the shell publishes the order detail.
 *
 * Every command is one keyed attempt (./poolAttempt.ts). After a recorded command, or a 409 (the quote moved, is no
 * longer open, or a covered request is not approved — `sales_quote_request_not_approved`, not recorded, may be retried
 * after the approval), the quote is re-read; what the person typed is kept. A withheld price reads "Gizli", never zero.
 * The validity date is shown as stored; whether an expired quote may convert is the server's (no expiry rule is chosen).
 */

import { useContext, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'
import { convertQuote, fetchQuoteDetail, recordQuoteLoss, reviseQuote } from './poolApi'
import {
  QUOTE_STATE,
  salesQuoteCommandPath,
  type SalesQuoteConversionBody,
  type SalesQuoteDetail,
  type SalesQuoteLossBody,
  type SalesQuoteMutation,
  type SalesQuoteRevisionBody,
} from './poolContract'
import type { DraftMutation } from './contract'
import { LIMITS } from './contract'
import {
  POOL_FIELD,
  emptyPoolLine,
  lineFromQuote,
  validateQuoteRevision,
  type PoolFormErrors,
  type PoolFormValues,
} from './poolForm'
import { POOL_TEXT, QUOTE_STATE_TEXT } from './poolText'
import { SALES_POOL_ROUTE, salesRequestScreenPath, useSalesPoolCapability } from './poolCapabilities'
import { SALES_CAPABILITY, salesDetailPath } from './capabilities'
import { poolNotice, usePoolAttempt } from './poolAttempt'
import { usePoolOptions } from './poolOptions'
import { PoolAttemptNotice, PoolErrorSummary, PoolLinesEditor, PoolPartyCell } from './SalesPoolShared'
import { QUOTE_TONE, cssId, serverFieldErrors } from './poolView'
import { SalesCurrencySourceContext } from './referenceSource'
import { browserIds } from './orderDraftForm'
import { formatBusinessDate, formatInstant } from './format'
import { isDateOnly } from './wirePrimitives'
import { classifyOutcome } from '../shell/uiState'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StatusIndicator } from '../ui/StatusIndicator'
import styles from './SalesPool.module.css'

const T = POOL_TEXT.quoteDetail
const C = POOL_TEXT.common
const F = POOL_TEXT.form

export function SalesQuoteDetailPage() {
  const shell = useShellState()
  const { quoteId } = useParams()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const canList = useSalesPoolCapability('quotesList')

  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-quote', quoteId ?? '']
  const query = useQuery({
    queryKey,
    enabled: namespace !== null && companyId !== null && (quoteId ?? '').length > 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchQuoteDetail(quoteId as string, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const backLink = canList ? (
    <Link className={styles.link} to={SALES_POOL_ROUTE.quotes}>
      ← {T.back}
    </Link>
  ) : null

  const outcome = query.data
  if (outcome === undefined || outcome.type !== 'ok-single' || companyId === null) {
    return (
      <section className={styles.page} aria-label={T.region}>
        {backLink}
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject="satış teklifi"
          onRetry={() => void query.refetch()}
        />
      </section>
    )
  }
  return (
    <QuoteView
      key={`${namespace ?? ''}:${outcome.data.quote.id}`}
      detail={outcome.data}
      companyId={companyId}
      backLink={backLink}
      onReread={() => void queryClient.invalidateQueries({ queryKey })}
    />
  )
}

function QuoteView({
  detail,
  companyId,
  backLink,
  onReread,
}: {
  readonly detail: SalesQuoteDetail
  readonly companyId: string
  readonly backLink: React.ReactNode
  readonly onReread: () => void
}) {
  const shell = useShellState()
  const { quote } = detail
  const canOpenRequest = useSalesPoolCapability('requestsDetail')
  const canOpenOrder = hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const useCurrencies = useContext(SalesCurrencySourceContext)
  const currencies = useCurrencies(companyId, quote.businessDate)
  const currencyCode = currencies.status === 'ready' ? currencies.data.find((c) => c.currencyId === quote.currencyId)?.code ?? null : null

  return (
    <section className={styles.page} aria-labelledby="pool-quote-heading">
      {backLink}
      <header className={styles.header}>
        <div className={styles.titleRow}>
          <h1 id="pool-quote-heading" className={styles.title}>
            {T.heading(quote.documentNo, quote.revision)}
          </h1>
          <StatusIndicator tone={QUOTE_TONE[quote.state]}>
            <span data-testid="pool-quote-state">{QUOTE_STATE_TEXT[quote.state]}</span>
          </StatusIndicator>
        </div>
      </header>
      <dl className={styles.facts}>
        <div><dt>{C.party}</dt><dd><PoolPartyCell partyId={quote.partyId} /></dd></div>
        <div><dt>{C.businessDate}</dt><dd>{formatBusinessDate(quote.businessDate)}</dd></div>
        <div>
          <dt>{T.currency}</dt>
          <dd>{currencyCode ?? <span className={styles.identifier}>{quote.currencyId}</span>}</dd>
        </div>
        <div>
          <dt>{T.validUntil}</dt>
          <dd data-testid="pool-quote-valid-until">{quote.validUntil === null ? C.unknown : formatBusinessDate(quote.validUntil)}</dd>
        </div>
        <div>
          <dt>{C.requestedDeliveryDate}</dt>
          <dd>{detail.requestedDeliveryDate === null ? C.unknown : formatBusinessDate(detail.requestedDeliveryDate)}</dd>
        </div>
        <div><dt>{C.version}</dt><dd className={styles.identifier}>{quote.version}</dd></div>
        <div><dt>{C.createdAt}</dt><dd>{formatInstant(quote.createdAtUtc)}</dd></div>
        <div className={styles.wide}><dt>{C.note}</dt><dd>{detail.note ?? C.unknown}</dd></div>
        <div className={styles.wide}>
          <dt>{T.coveredRequests}</dt>
          <dd data-testid="pool-quote-requests">
            {detail.requestIds.length === 0 ? (
              C.none
            ) : (
              <ul>
                {detail.requestIds.map((id) => (
                  <li key={id}>
                    {canOpenRequest ? (
                      <Link className={`${styles.link} ${styles.identifier}`} to={salesRequestScreenPath(id)}>{id}</Link>
                    ) : (
                      <span className={styles.identifier}>{id}</span>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </dd>
        </div>
      </dl>
      {quote.validUntil === null ? null : <p className={styles.muted}>{T.validUntilNote}</p>}

      {detail.loss === null ? null : (
        <section className={styles.section} aria-labelledby="pool-quote-loss-heading" data-testid="pool-quote-loss">
          <h2 id="pool-quote-loss-heading" className={styles.subtitle}>{T.loss}</h2>
          <dl className={styles.facts}>
            <div className={styles.wide}><dt>{T.lossReason}</dt><dd>{detail.loss.reason}</dd></div>
            <div><dt>{T.lostBy}</dt><dd>{detail.loss.lostBy}</dd></div>
            <div><dt>{T.lostAt}</dt><dd>{formatInstant(detail.loss.lostAtUtc)}</dd></div>
          </dl>
        </section>
      )}
      {detail.conversion === null ? null : (
        <section className={styles.section} aria-labelledby="pool-quote-conversion-heading" data-testid="pool-quote-conversion">
          <h2 id="pool-quote-conversion-heading" className={styles.subtitle}>{T.conversion}</h2>
          <dl className={styles.facts}>
            <div><dt>{T.convertedBy}</dt><dd>{detail.conversion.convertedBy}</dd></div>
            <div><dt>{T.convertedAt}</dt><dd>{formatInstant(detail.conversion.convertedAtUtc)}</dd></div>
          </dl>
          {canOpenOrder ? (
            <Link className={styles.link} to={salesDetailPath(detail.conversion.draftId)}>{T.openOrder}</Link>
          ) : (
            <p className={styles.muted}>{T.orderWithoutAccess}</p>
          )}
        </section>
      )}

      <QuoteLines detail={detail} />

      {quote.state === QUOTE_STATE.open ? <QuoteCommands detail={detail} companyId={companyId} onReread={onReread} /> : null}
    </section>
  )
}

function QuoteLines({ detail }: { readonly detail: SalesQuoteDetail }) {
  const unitCode = (id: string | null) => detail.lines.find((line) => line.orderUnitId === id)?.orderUnitCode ?? id
  return (
    <section className={styles.section} aria-labelledby="pool-quote-lines-heading">
      <h2 id="pool-quote-lines-heading" className={styles.subtitle}>{C.lines}</h2>
      <div className={styles.scroll}>
        <table aria-labelledby="pool-quote-lines-heading">
          <thead>
            <tr>
              <th scope="col">{C.lineNo}</th>
              <th scope="col">{C.product}</th>
              <th scope="col" className={styles.numeric}>{C.quantity}</th>
              <th scope="col">{C.unit}</th>
              <th scope="col" className={styles.numeric}>{T.price}</th>
              <th scope="col">{T.priceUnit}</th>
              <th scope="col" className={styles.numeric}>{T.discount}</th>
              <th scope="col">{C.lineDeliveryDate}</th>
              <th scope="col">{C.note}</th>
            </tr>
          </thead>
          <tbody>
            {detail.lines.map((line) => {
              const withheld = line.priceDisclosure === 'Withheld'
              return (
                <tr key={line.lineId} data-testid={`pool-quote-line-${line.lineNo}`}>
                  <td>{line.lineNo}</td>
                  <td>{line.productCode} — {line.productName}</td>
                  <td className={styles.numeric}>{line.quantity}</td>
                  <td>{line.orderUnitCode}</td>
                  <td className={styles.numeric} data-testid={`pool-quote-line-${line.lineNo}-price`}>
                    {withheld ? T.priceWithheld : line.unitPrice ?? T.notPriced}
                  </td>
                  <td>{withheld ? T.priceWithheld : line.priceUnitId === null ? C.none : unitCode(line.priceUnitId)}</td>
                  <td className={styles.numeric}>{withheld ? T.priceWithheld : line.discountPercent ?? C.none}</td>
                  <td>{line.requestedDeliveryDate === null ? C.unknown : formatBusinessDate(line.requestedDeliveryDate)}</td>
                  <td>{line.note ?? C.unknown}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </section>
  )
}

type OpenPanel = 'none' | 'revision' | 'loss' | 'conversion'

function QuoteCommands({ detail, companyId, onReread }: { readonly detail: SalesQuoteDetail; readonly companyId: string; readonly onReread: () => void }) {
  const canWrite = useSalesPoolCapability('quotesWrite')
  const canConvert = useSalesPoolCapability('quotesConvert')
  const [panel, setPanel] = useState<OpenPanel>('none')
  if (!canWrite && !canConvert) return null
  return (
    <section className={styles.section} aria-labelledby="pool-quote-commands-heading" data-testid="pool-quote-commands">
      <h2 id="pool-quote-commands-heading" className={styles.subtitle}>{T.commands}</h2>
      <div className={styles.actions}>
        {canWrite ? (
          <>
            <Button type="button" variant="secondary" aria-expanded={panel === 'revision'} onClick={() => setPanel(panel === 'revision' ? 'none' : 'revision')}>
              {T.revise}
            </Button>
            <Button type="button" variant="secondary" aria-expanded={panel === 'loss'} onClick={() => setPanel(panel === 'loss' ? 'none' : 'loss')}>
              {T.lose}
            </Button>
          </>
        ) : null}
        {canConvert ? (
          <Button type="button" variant="primary" aria-expanded={panel === 'conversion'} onClick={() => setPanel(panel === 'conversion' ? 'none' : 'conversion')}>
            {T.convert}
          </Button>
        ) : null}
      </div>
      {panel === 'revision' && canWrite ? <RevisionForm detail={detail} companyId={companyId} onReread={onReread} onClose={() => setPanel('none')} /> : null}
      {panel === 'loss' && canWrite ? <LossForm detail={detail} onReread={onReread} /> : null}
      {panel === 'conversion' && canConvert ? <ConversionForm detail={detail} onReread={onReread} /> : null}
    </section>
  )
}

function revisionValues(detail: SalesQuoteDetail): PoolFormValues {
  return {
    partyId: detail.quote.partyId,
    companyAccountId: detail.quote.companyAccountId,
    businessDate: detail.quote.businessDate,
    currencyId: detail.quote.currencyId,
    validUntil: detail.quote.validUntil ?? '',
    requestedDeliveryDate: detail.requestedDeliveryDate ?? '',
    note: detail.note ?? '',
    lines: detail.lines.map(lineFromQuote),
  }
}

function RevisionForm({
  detail,
  companyId,
  onReread,
  onClose,
}: {
  readonly detail: SalesQuoteDetail
  readonly companyId: string
  readonly onReread: () => void
  readonly onClose: () => void
}) {
  const [values, setValues] = useState<PoolFormValues>(() => revisionValues(detail))
  const [errors, setErrors] = useState<PoolFormErrors>({})
  const [productSearch, setProductSearch] = useState('')
  const options = usePoolOptions(companyId, detail.quote.businessDate, '', productSearch)
  const attempt = usePoolAttempt<SalesQuoteRevisionBody, SalesQuoteMutation>(
    `sales.quote.revision:${detail.quote.id}`,
    (snapshot) => reviseQuote(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId, quoteId: detail.quote.id }),
    (next) => {
      if (next.phase === 'completed') onReread()
      if (next.phase === 'failed' && next.unresolvedSince === undefined) {
        if (next.failure === 'conflict') onReread()
        if (next.failure === 'validation') setErrors(serverFieldErrors(next.problem, next.snapshot.payload.lines))
      }
    },
  )
  const disabled = attempt.frozen || attempt.state.phase === 'completed'
  const update = (next: Partial<PoolFormValues>) => setValues((current) => ({ ...current, ...next }))
  const notice = poolNotice(attempt.state, {
    noun: 'revizyon',
    onRecord: true,
    blockedChange: attempt.blockedChange,
    completed: (result) => T.reviseCompleted(result.detail.quote.revision),
  })

  function submit() {
    // The version this form was opened on: a quote that moved since answers 409 and is re-read (the typed rows stay).
    const result = validateQuoteRevision(values, detail.quote.version)
    if (!result.ok) {
      setErrors(result.errors)
      return
    }
    setErrors({})
    attempt.submit(salesQuoteCommandPath(detail.quote.id, 'revision'), result.payload)
  }

  return (
    <form
      noValidate
      className={styles.page}
      aria-labelledby="pool-quote-revision-heading"
      onSubmit={(event) => {
        event.preventDefault()
        submit()
      }}
    >
      <h3 id="pool-quote-revision-heading" className={styles.lineTitle}>{T.reviseHeading}</h3>
      <PoolErrorSummary errors={errors} />
      <div className={styles.grid}>
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
        <Field id={cssId('revisionProductSearch')} label={F.productSearch}>
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
      <PoolAttemptNotice attempt={attempt} notice={notice} testId="pool-quote-revision-notice" />
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={disabled}>{T.reviseSubmit}</Button>
        <Button type="button" variant="ghost" disabled={attempt.frozen} onClick={onClose}>{T.reviseCancel}</Button>
      </div>
    </form>
  )
}

function LossForm({ detail, onReread }: { readonly detail: SalesQuoteDetail; readonly onReread: () => void }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const attempt = usePoolAttempt<SalesQuoteLossBody, SalesQuoteMutation>(
    `sales.quote.loss:${detail.quote.id}`,
    (snapshot) => recordQuoteLoss(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId, quoteId: detail.quote.id }),
    (next) => {
      if (next.phase === 'completed' || (next.phase === 'failed' && next.failure === 'conflict')) onReread()
    },
  )
  const notice = poolNotice(attempt.state, { noun: 'kayıp kaydı', onRecord: true, blockedChange: attempt.blockedChange, completed: () => T.lossCompleted })

  function submit() {
    if (reason.trim() === '') {
      setError(T.lossReasonRequired)
      document.getElementById('pool-quote-loss-reason')?.focus()
      return
    }
    if (reason.length > LIMITS.note) {
      setError(POOL_TEXT.validation.noteTooLong)
      return
    }
    setError(null)
    attempt.submit(salesQuoteCommandPath(detail.quote.id, 'loss'), { expectedVersion: detail.quote.version, reason: reason.trim() })
  }

  return (
    <form
      noValidate
      className={styles.page}
      aria-labelledby="pool-quote-loss-form-heading"
      onSubmit={(event) => {
        event.preventDefault()
        submit()
      }}
    >
      <h3 id="pool-quote-loss-form-heading" className={styles.lineTitle}>{T.loseHeading}</h3>
      <Field id="pool-quote-loss-reason" label={T.lossReasonLabel} error={error ?? undefined}>
        {(control) => (
          <textarea {...control} className={styles.control} value={reason} disabled={attempt.frozen || attempt.state.phase === 'completed'}
            onChange={(event) => setReason(event.target.value)} />
        )}
      </Field>
      <PoolAttemptNotice attempt={attempt} notice={notice} testId="pool-quote-loss-notice" />
      <div>
        <Button type="submit" variant="danger" disabled={attempt.frozen || attempt.state.phase === 'completed'}>{T.lose}</Button>
      </div>
    </form>
  )
}

function ConversionForm({ detail, onReread }: { readonly detail: SalesQuoteDetail; readonly onReread: () => void }) {
  const shell = useShellState()
  const navigate = useNavigate()
  const canOpenOrder = hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const [businessDate, setBusinessDate] = useState('')
  const [error, setError] = useState<string | null>(null)
  const attempt = usePoolAttempt<SalesQuoteConversionBody, DraftMutation>(
    `sales.quote.conversion:${detail.quote.id}`,
    // The quote as this screen read it is what the order must describe (its lines, customer, currency).
    (snapshot) => convertQuote(snapshot.payload, detail, { idempotencyKey: snapshot.key, companyId: snapshot.companyId }),
    (next) => {
      if (next.phase === 'completed') {
        if (canOpenOrder) navigate(salesDetailPath(next.result.detail.draft.id))
        else onReread()
      }
      if (next.phase === 'failed' && next.failure === 'conflict') onReread()
    },
  )
  const notice = poolNotice(attempt.state, {
    noun: 'sipariş',
    onRecord: true,
    blockedChange: attempt.blockedChange,
    completed: (result) => T.convertCompleted(result.detail.draft.documentNo),
  })

  function submit() {
    if (businessDate === '') {
      setError(T.convertDateRequired)
      document.getElementById('pool-quote-convert-date')?.focus()
      return
    }
    if (!isDateOnly(businessDate)) {
      setError(POOL_TEXT.validation.dateInvalid)
      return
    }
    setError(null)
    attempt.submit(salesQuoteCommandPath(detail.quote.id, 'conversion'), { expectedVersion: detail.quote.version, businessDate })
  }

  return (
    <form
      noValidate
      className={styles.page}
      aria-labelledby="pool-quote-convert-heading"
      onSubmit={(event) => {
        event.preventDefault()
        submit()
      }}
    >
      <h3 id="pool-quote-convert-heading" className={styles.lineTitle}>{T.convertHeading}</h3>
      <p className={styles.muted}>{T.convertIntro}</p>
      <Field id="pool-quote-convert-date" label={T.convertDate} error={error ?? undefined}>
        {(control) => (
          <input {...control} className={styles.control} type="date" value={businessDate}
            disabled={attempt.frozen || attempt.state.phase === 'completed'}
            onChange={(event) => setBusinessDate(event.target.value)} />
        )}
      </Field>
      <PoolAttemptNotice attempt={attempt} notice={notice} testId="pool-quote-convert-notice" />
      <div>
        <Button type="submit" variant="primary" disabled={attempt.frozen || attempt.state.phase === 'completed'}>{T.convert}</Button>
      </div>
    </form>
  )
}
