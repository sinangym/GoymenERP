import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { currencyShown, WithCurrencyCodes } from '../finance/currencyCodes'
import { useReceiptAdjustmentAttempt } from './useReceiptAdjustmentAttempt'
import { fetchIncomingInvoice, fetchIncomingInvoices, fetchInvoiceReceipts, fetchInvoiceAdditionalCosts, submitInvoiceCommand } from './incomingInvoiceApi'
import { invoiceQuantity, type IncomingInvoice, type IncomingInvoiceLine, type InvoiceCommand, type InvoiceCommandResponse,
  type InvoiceReceiptSelection, type InvoiceAdditionalCostSelection } from './incomingInvoiceContract'
import { INCOMING_INVOICE_PROBLEM_TEXT, INCOMING_INVOICE_TEXT as T, INCOMING_INVOICE_STATUS as STATUS } from './incomingInvoiceText'
import { PURCHASE_KG_PRICE_UNAVAILABLE_TEXT } from './purchaseOrderDetailText'
import { PURCHASING_CAPABILITY as C } from './capabilities'
import { isPurchasingGuid, type PurchaseKgPrice } from './contract'
import { InvoiceImportPanel } from './InvoiceImportPanel'
import styles from './IncomingInvoicePages.module.css'
import { RecordNotesSection } from '../work/RecordNotesSection'

const listPath = '/satinalma/faturalar'
export const incomingInvoicePath = (id: string) => `${listPath}/${encodeURIComponent(id)}`
function useBinding() {
  const shell = useShellState(), key = contextKey(shell)
  return key !== null && shell.activeContext?.scopeMode === 'single' && shell.activeContext.companyId !== null
    ? { company: shell.activeContext.companyId, namespace: contextKeyString(key), entry: shell.routeEntryGeneration } : null
}
type Binding = NonNullable<ReturnType<typeof useBinding>>
function useInvoiceRead<T>(binding: Binding, suffix: readonly unknown[], fetcher: (signal: AbortSignal) => Promise<ViewOutcome<T>>) {
  const boundary = useAuthBoundary()
  return useQuery({ queryKey: [binding.namespace, 'incoming-invoices', binding.entry, ...suffix], retry: false,
    queryFn: async ({ signal }) => {
      const fence = boundary.capture(), answer = await fetcher(signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, fence)
      return answer
    } })
}
function ReadState<T>({ data, loading, refresh }: { readonly data?: ViewOutcome<T>; readonly loading: boolean; readonly refresh: () => void }) {
  if (loading || data === undefined) return <StateBlock subject={T.heading} state={{ kind: 'initial-loading' }} />
  return data.type === 'ok-single' || data.type === 'ok' ? null
    : <StateBlock subject={T.heading} state={classifyOutcome(data)} onRetry={refresh} />
}
/**
 * AK-3b (SAT-D436, SAT-D439): a kg price indicator exactly as the server answered it — the 2-decimal text with the invoice's own
 * currency, or the reason of its code in words. Nothing is computed or converted here.
 */
function InvoiceKgPrice({ kgPrice, currencyId, heading, testId }: { readonly kgPrice: PurchaseKgPrice; readonly currencyId: string;
  readonly heading: string; readonly testId: string }) {
  const shown = (value: string | null, code: string | null, currency: string) => value !== null ? T.kgPricePerKg(value, currency)
    : code === null ? T.kgPriceNone : PURCHASE_KG_PRICE_UNAVAILABLE_TEXT[code] ?? code
  return <section aria-label={heading} data-testid={testId}><h4>{heading}</h4>
    <WithCurrencyCodes>{(codes) => <dl className={styles.amounts}>
      <dt>{T.theoreticalKgPrice}</dt><dd>{shown(kgPrice.theoreticalKgPrice, kgPrice.theoreticalUnavailable, currencyShown(currencyId, codes))}</dd>
      <dt>{T.realKgPrice}</dt><dd>{shown(kgPrice.realKgPrice, kgPrice.realUnavailable, currencyShown(currencyId, codes))}</dd>
    </dl>}</WithCurrencyCodes>
  </section>
}
export function IncomingInvoiceListPage() {
  const binding = useBinding()
  return binding === null ? <StateBlock subject={T.heading} state={{ kind: 'no-authorized-company' }} />
    : <InvoiceList key={`${binding.namespace}:${binding.entry}`} binding={binding} />
}
function InvoiceList({ binding }: { readonly binding: Binding }) {
  const [text, setText] = useState(''), [search, setSearch] = useState(''), [page, setPage] = useState(0)
  const read = useInvoiceRead(binding, ['list', search, page], (signal) => fetchIncomingInvoices(binding.company, search, page, signal))
  const data = !read.isFetching && read.data?.type === 'ok-single' ? read.data.data : null
  return <section className={styles.page} aria-labelledby="incoming-invoices-heading">
    <h1 id="incoming-invoices-heading">{T.heading}</h1>
    <InvoiceImportPanel binding={binding} />
    <form onSubmit={(event) => { event.preventDefault(); setSearch(text.trim()); setPage(0) }} className={styles.actions}>
      <Field label={T.number}>{(props) => <input {...props} maxLength={128} value={text} onChange={(event) => setText(event.target.value)} />}</Field>
      <Button type="submit">{T.search}</Button>
    </form>
    <ReadState data={read.data} loading={read.isFetching} refresh={() => void read.refetch()} />
    {data !== null && data.items.length === 0 ? <StateBlock subject={T.heading} state={{ kind: search ? 'empty-filtered' : 'empty-no-data' }} /> : null}
    {data?.items.map((invoice) => <article key={invoice.id} className={styles.card}>
      <h2><Link to={incomingInvoicePath(invoice.id)}>{invoice.officialNumber}</Link></h2>
      <p>{T.date}: {invoice.invoiceDate}</p><p>{T.status}: {STATUS[invoice.status]}</p>
    </article>)}
    <div className={styles.actions}>
      <Button type="button" disabled={page === 0 || read.isFetching} onClick={() => setPage((value) => value - 1)}>{T.previous}</Button>
      <Button type="button" disabled={!data?.hasNext || read.isFetching} onClick={() => setPage((value) => value + 1)}>{T.next}</Button>
    </div>
  </section>
}
export function IncomingInvoiceDetailPage() {
  const binding = useBinding(), { invoiceId = '' } = useParams()
  if (!isPurchasingGuid(invoiceId)) return <StateBlock subject={T.detail} state={{ kind: 'not-found' }} />
  return binding === null ? <StateBlock subject={T.detail} state={{ kind: 'no-authorized-company' }} />
    : <InvoiceDetail key={`${binding.namespace}:${binding.entry}:${invoiceId}`} binding={binding} id={invoiceId} />
}
function InvoiceDetail({ binding, id }: { readonly binding: Binding; readonly id: string }) {
  const read = useInvoiceRead(binding, ['detail', id], (signal) => fetchIncomingInvoice(binding.company, id, signal))
  const invoice = !read.isFetching && read.data?.type === 'ok-single' ? read.data.data : null
  return <section className={styles.page} aria-labelledby="incoming-invoice-heading">
    <Link to={listPath}>{T.back}</Link><h1 id="incoming-invoice-heading">{T.detail}</h1>
    <ReadState data={read.data} loading={read.isFetching} refresh={() => void read.refetch()} />
    {invoice === null ? null : <InvoiceWorkspace key={invoice.version} binding={binding} invoice={invoice} />}
    {invoice === null ? null : <RecordNotesSection recordType="purchase-incoming-invoice" recordId={invoice.id} />}
  </section>
}
function InvoiceWorkspace({ binding, invoice }: { readonly binding: Binding; readonly invoice: IncomingInvoice }) {
  const shell = useShellState(), client = useQueryClient()
  const [selected, setSelected] = useState<readonly InvoiceReceiptSelection[]>([]), [reason, setReason] = useState(''), [error, setError] = useState<string | null>(null)
  const [expenseSelected, setExpenseSelected] = useState<readonly InvoiceAdditionalCostSelection[]>([])
  const refresh = () => { void client.invalidateQueries({ queryKey: [binding.namespace, 'incoming-invoices'] }) }
  const attempt = useReceiptAdjustmentAttempt<InvoiceCommand, InvoiceCommandResponse>(JSON.stringify([binding.namespace, 'invoice-command', invoice.id]),
    binding.namespace, (command, key) => submitInvoiceCommand(binding.company, invoice.id, command, key), refresh)
  const canMatch = hasCapability(shell, C.invoiceMatch), canApprove = hasCapability(shell, C.invoiceApproveVariance), canFinalize = hasCapability(shell, C.invoiceFinalize)
  function match() {
    if (attempt.locked || !canMatch) return
    if (invoice.lines.some(line => line.linePurpose === 'expense'
      ? !expenseSelected.some(item => item.invoiceLineId === line.id) : !selected.some(item => item.invoiceLineId === line.id))
      || selected.some(item => !invoiceQuantity(item.quantity)) || expenseSelected.some(item => !invoiceQuantity(item.netAmount))) {
      setError(T.invalidSelection); return
    }
    // Canonical wire text only; no quantity arithmetic or allocation calculation in the browser.
    const receipts = selected.map((item) => ({ ...item, quantity: item.quantity.includes('.') ? item.quantity.replace(/0+$/, '').replace(/\.$/, '') : item.quantity }))
    const additionalCosts = expenseSelected.map(item => ({ ...item,
      netAmount: item.netAmount.includes('.') ? item.netAmount.replace(/0+$/, '').replace(/\.$/, '') : item.netAmount }))
    setError(null); attempt.submit({ action: 'matching', request: { expectedVersion: invoice.version, receipts,
      ...(additionalCosts.length === 0 ? {} : { additionalCosts }) } })
  }
  const answer = attempt.outcome
  return <>
    <h2>{invoice.officialNumber}</h2><p>{T.status}: {STATUS[invoice.status]}</p><p>{T.date}: {invoice.invoiceDate}</p><p>{T.ettn}: {invoice.ettn}</p>
    <WithCurrencyCodes>{(codes) => <dl className={styles.amounts}>
      <dt>{T.net}</dt><dd>{invoice.amounts.net} {currencyShown(invoice.currencyId, codes)}</dd>
      <dt>{T.vat}</dt><dd>{invoice.amounts.vat} {currencyShown(invoice.currencyId, codes)}</dd>
      <dt>{T.withholding}</dt><dd>{invoice.amounts.withholding} {currencyShown(invoice.currencyId, codes)}</dd>
      <dt>{T.payable}</dt><dd>{invoice.amounts.payable} {currencyShown(invoice.currencyId, codes)}</dd>
    </dl>}</WithCurrencyCodes>
    {invoice.kgPrice === undefined ? null : <InvoiceKgPrice kgPrice={invoice.kgPrice} currencyId={invoice.currencyId}
      heading={T.kgPriceHeading} testId="incoming-invoice-kg-price" />}
    {invoice.lines.map((line) => <article key={line.id} className={styles.card}>
      <h3>{T.line} {line.lineNo}</h3><p>{T.quantity}: {line.quantity}</p>
      {line.kgPrice === undefined ? null : <InvoiceKgPrice kgPrice={line.kgPrice} currencyId={invoice.currencyId}
        heading={T.lineKgPriceHeading} testId={`incoming-invoice-line-${line.lineNo}-kg-price`} />}
      {invoice.status === 'Recorded' && canMatch ? line.linePurpose === 'expense'
        ? <ExpenseChoices binding={binding} invoice={invoice} line={line} selected={expenseSelected} locked={attempt.locked} onSelected={setExpenseSelected} />
        : <ReceiptChoices binding={binding} invoice={invoice} line={line} selected={selected} locked={attempt.locked} onSelected={setSelected} /> : null}
    </article>)}
    {invoice.variances.length === 0 ? null : <section aria-label={T.variances}>
      <h3>{T.variances}</h3>{invoice.variances.map((item, index) => <p key={`${item.invoiceLineId}:${index}`}>
        {T.line} {invoice.lines.find((line) => line.id === item.invoiceLineId)?.lineNo}: {item.kind === 'ExchangeRate' ? T.exchangeRate : item.kind === 'Quantity' ? T.quantity : T.price}
        {' — '}{T.expected}: {item.expected}; {T.actual}: {item.actual}</p>)}
      {invoice.varianceDecision !== null ? <p>{T.approvalDone}</p> : null}
    </section>}
    {error === null ? null : <p role="alert">{error}</p>}
    {answer?.type === 'applied' ? <p role="status">{T.saved}</p> : null}
    {answer?.type === 'failed' ? <><p role="alert">{answer.code !== null && Object.hasOwn(INCOMING_INVOICE_PROBLEM_TEXT, answer.code)
      ? INCOMING_INVOICE_PROBLEM_TEXT[answer.code] : answer.status === 409 ? T.conflict : T.rejected}</p>
      {[401, 403, 404, 409].includes(answer.status) ? <StateBlock subject={T.detail} state={classifyOutcome({ type: 'http', status: answer.status })} /> : null}</> : null}
    {answer?.type === 'unknown' ? <StateBlock subject={T.detail} state={{ kind: answer.reason === 'malformed' ? 'malformed-response' : answer.reason }} /> : null}
    {attempt.locked ? <div role="status"><p>{attempt.sending ? T.sending : T.unknown}</p>
      <Button type="button" disabled={attempt.sending} onClick={attempt.retry}>{T.retry}</Button></div> : null}
    <fieldset disabled={attempt.locked} className={styles.actions}>
      <legend>{T.detail}</legend>
      {invoice.status === 'Recorded' && canMatch ? <><p>{T.matchingHint}</p><Button type="button" onClick={match}>{T.match}</Button></> : null}
      {invoice.status === 'Matched' && invoice.variances.length > 0 && invoice.varianceDecision === null && canApprove ? <>
        <Field label={T.reason}>{(props) => <textarea {...props} maxLength={2000} value={reason} onChange={(event) => setReason(event.target.value)} />}</Field>
        <Button type="button" onClick={() => { if (!reason.trim()) { setError(T.invalidReason); return }
          setError(null); attempt.submit({ action: 'variance-approval', request: { expectedVersion: invoice.version, reason: reason.trim() } }) }}>{T.approve}</Button>
      </> : null}
      {invoice.status === 'Matched' && (invoice.variances.length === 0 || invoice.varianceDecision !== null) && canFinalize
        ? <Button type="button" onClick={() => attempt.submit({ action: 'finalization', request: { expectedVersion: invoice.version } })}>{T.finalize}</Button> : null}
      {invoice.status === 'FinancePending' && canFinalize ? <Button type="button" onClick={() => attempt.submit({ action: 'finance-admission', request: { expectedVersion: invoice.version } })}>{T.retryFinance}</Button> : null}
      <Button type="button" onClick={refresh}>{T.refresh}</Button>
    </fieldset>
  </>
}
function ReceiptChoices({ binding, invoice, line, selected, locked, onSelected }: { readonly binding: Binding; readonly invoice: IncomingInvoice;
  readonly line: IncomingInvoiceLine; readonly selected: readonly InvoiceReceiptSelection[]; readonly locked: boolean;
  readonly onSelected: (value: readonly InvoiceReceiptSelection[]) => void }) {
  const [page, setPage] = useState(0)
  const read = useInvoiceRead(binding, ['receipts', invoice.id, invoice.version, line.id, page],
    (signal) => fetchInvoiceReceipts(binding.company, invoice, line, page, signal))
  const data = !read.isFetching && read.data?.type === 'ok-single' ? read.data.data : null
  return <fieldset disabled={locked}>
    <legend>{T.chooseReceipts}</legend>
    <ReadState data={read.data} loading={read.isFetching} refresh={() => void read.refetch()} />
    {data?.items.length === 0 ? <StateBlock subject={T.receipts} state={{ kind: 'empty-no-data' }} /> : null}
    {data?.items.map((item) => {
      const choice = selected.find((value) => value.acceptanceId === item.acceptanceId), belongs = choice?.invoiceLineId === line.id
      return <div key={item.acceptanceId} className={styles.card}>
        <p>{T.order}: {item.purchaseOrderNumber ?? T.orderUnavailable}; {T.orderLine}: {item.orderLineNo}</p>
        <p>{T.acceptedOn}: {new Date(item.acceptedAtUtc).toLocaleString('tr-TR')}</p><p>{T.available}: {item.availableQuantity}</p>
        <label><input type="checkbox" checked={belongs} disabled={choice !== undefined && !belongs} onChange={(event) => onSelected(event.target.checked
          ? [...selected, { invoiceLineId: line.id, purchaseOrderId: item.purchaseOrderId, acceptanceId: item.acceptanceId, quantity: '' }]
          : selected.filter((value) => value.acceptanceId !== item.acceptanceId))} />{T.select}</label>
        {belongs ? <Field label={T.share}>{(props) => <input {...props} inputMode="decimal" value={choice.quantity}
          onChange={(event) => { const quantity = event.target.value.replace(',', '.')
            onSelected(selected.map((value) => value.acceptanceId === item.acceptanceId ? { ...value, quantity } : value)) }} />}</Field> : null}
      </div>
    })}
    <div className={styles.actions}>
      <Button type="button" disabled={page === 0 || read.isFetching} onClick={() => setPage((value) => value - 1)}>{T.previous}</Button>
      <Button type="button" disabled={!data?.hasNext || read.isFetching} onClick={() => setPage((value) => value + 1)}>{T.next}</Button>
    </div>
  </fieldset>
}
function ExpenseChoices({ binding, invoice, line, selected, locked, onSelected }: { readonly binding: Binding; readonly invoice: IncomingInvoice;
  readonly line: IncomingInvoiceLine; readonly selected: readonly InvoiceAdditionalCostSelection[]; readonly locked: boolean;
  readonly onSelected: (value: readonly InvoiceAdditionalCostSelection[]) => void }) {
  const [page, setPage] = useState(0)
  const read = useInvoiceRead(binding, ['expenses', invoice.id, invoice.version, line.id, page],
    signal => fetchInvoiceAdditionalCosts(binding.company, invoice, line, page, signal))
  const data = !read.isFetching && read.data?.type === 'ok-single' ? read.data.data : null
  return <fieldset disabled={locked}><legend>{T.chooseExpenses}</legend><p>{T.expenseHint}</p>
    <ReadState data={read.data} loading={read.isFetching} refresh={() => void read.refetch()} />
    {data?.items.length === 0 ? <StateBlock subject={T.chooseExpenses} state={{ kind: 'empty-filtered' }} /> : null}
    {data?.items.map(item => {
      const choice = selected.find(x => x.costId === item.costId && x.invoiceLineId === line.id)
      return <div key={item.costId} className={styles.card}>
        <p>{T.order}: {item.purchaseOrderNumber ?? T.orderUnavailable}</p><p>{T.expenseDescription}: {item.description}</p>
        <WithCurrencyCodes>{codes => <><p>{T.expenseTotal}: {item.netAmount} {currencyShown(invoice.currencyId, codes)}</p>
          <p>{T.expenseAvailable}: {item.availableNetAmount} {currencyShown(invoice.currencyId, codes)}</p></>}</WithCurrencyCodes>
        <label><input type="checkbox" checked={choice !== undefined} onChange={event => onSelected(event.target.checked
          ? [...selected, { invoiceLineId: line.id, purchaseOrderId: item.purchaseOrderId, costId: item.costId, netAmount: '' }]
          : selected.filter(x => x.costId !== item.costId || x.invoiceLineId !== line.id))} />{T.select}</label>
        {choice === undefined ? null : <Field label={T.expenseShare}>{props => <input {...props} inputMode="decimal" value={choice.netAmount}
          onChange={event => onSelected(selected.map(x => x.costId === item.costId && x.invoiceLineId === line.id
            ? { ...x, netAmount: event.target.value.replace(',', '.') } : x))} />}</Field>}
      </div>
    })}
    <div className={styles.actions}><Button type="button" disabled={page === 0 || read.isFetching} onClick={() => setPage(x => x - 1)}>{T.previous}</Button>
      <Button type="button" disabled={!data?.hasNext || read.isFetching} onClick={() => setPage(x => x + 1)}>{T.next}</Button></div>
  </fieldset>
}
