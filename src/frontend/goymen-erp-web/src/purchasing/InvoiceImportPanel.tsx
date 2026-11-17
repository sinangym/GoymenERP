import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { documentsQueryKey, fetchDocuments } from '../documents/api'
import { OWNER_KIND, type DocumentRow } from '../documents/contract'
import { Field } from '../ui/Field'
import { Button } from '../ui/Button'
import { PURCHASING_CAPABILITY as C } from './capabilities'
import { useReceiptAdjustmentAttempt } from './useReceiptAdjustmentAttempt'
import { reserveInvoiceImport, previewInvoiceImport, fetchInvoiceImports } from './invoiceImportApi'
import type { ImportOptions, ImportPreview, ImportPreviewRequest, ImportReservation, ImportReservationResult, SourceInvoice, SourceAmount, SourceTax, SourceAdjustment } from './invoiceImportContract'
import { INVOICE_IMPORT_TEXT as T, REQUIRED_IMPORT_COLUMNS as REQUIRED, OPTIONAL_IMPORT_COLUMNS as OPTIONAL, IMPORT_TOTAL_LABELS as TOTALS, IMPORT_PURPOSE_LABELS as PURPOSES } from './invoiceImportText'
import styles from './IncomingInvoicePages.module.css'

export interface InvoiceImportBinding { readonly company: string; readonly namespace: string; readonly entry: number }
export function InvoiceImportPanel({ binding }: { readonly binding: InvoiceImportBinding }) {
  const shell = useShellState()
  return hasCapability(shell, C.invoiceImport) ? <div key={`${binding.namespace}:${binding.entry}`} className={styles.page}>
    <ImportStart binding={binding} /><PreviousImports binding={binding} /></div> : null
}
function PreviousImports({ binding }: { readonly binding: InvoiceImportBinding }) {
  const boundary = useAuthBoundary(), [page, setPage] = useState(0), [opened, setOpened] = useState<string | null>(null)
  const read = useQuery({ queryKey: [binding.namespace, binding.entry, 'invoice-imports', page], retry: false,
    queryFn: async ({ signal }) => {
      const fence = boundary.capture(), value = await fetchInvoiceImports(binding.company, page, signal)
      if (value.type === 'http' && [401, 403].includes(value.status)) boundary.report(value.status, fence)
      return value
    } })
  const list = !read.isFetching && read.data?.type === 'ok-single' ? read.data.data : null
  const selected = list?.items.find(item => item.id === opened)
  return <section className={styles.card} aria-label={T.previousImports}><h3>{T.previousImports}</h3>
    <ReadState outcome={read.data} fetching={read.isFetching} refresh={() => void read.refetch()} />
    {list?.items.length === 0 ? <StateBlock subject={T.previousImports} state={{ kind: 'empty-no-data' }} /> : null}
    {list?.items.map(item => <p key={item.id}>{T.reservationLabel(item.reservedAtUtc)}{' '}
      <Button type="button" onClick={() => setOpened(item.id)}>{T.open}</Button></p>)}
    <div className={styles.actions}><Button type="button" disabled={page === 0 || read.isFetching} onClick={() => { setOpened(null); setPage(page - 1) }}>{T.previous}</Button>
      <Button type="button" disabled={!list?.hasNext || read.isFetching} onClick={() => { setOpened(null); setPage(page + 1) }}>{T.next}</Button>
      <Button type="button" disabled={read.isFetching} onClick={() => void read.refetch()}>{T.refresh}</Button></div>
    {selected ? <><Button type="button" onClick={() => setOpened(null)}>{T.close}</Button>
      <ImportDocuments key={selected.id} binding={binding} reservation={selected} /></> : null}
  </section>
}
function ImportStart({ binding }: { readonly binding: InvoiceImportBinding }) {
  // Keep an unresolved key across route re-entry, but never across actor/company/capability generations.
  const attempt = useReceiptAdjustmentAttempt<null, ImportReservationResult>(JSON.stringify([binding.namespace, 'invoice-import-reserve']),
    `${binding.namespace}:${binding.entry}`, (_, key) => reserveInvoiceImport(binding.company, key), () => {})
  const answer = attempt.outcome
  return <section className={styles.card} aria-label={T.heading}>
    <h2>{T.heading}</h2><p>{T.hint}</p>
    {answer?.type === 'applied' ? <ImportDocuments key={answer.response.reservation.id} binding={binding} reservation={answer.response.reservation} /> : <>
      <Button type="button" disabled={attempt.locked} onClick={() => attempt.submit(null)}>{T.start}</Button>
      {answer?.type === 'failed' ? <><p role="alert">{T.rejected}</p><StateBlock subject={T.heading} state={classifyOutcome({ type: 'http', status: answer.status })} /></> : null}
      {answer?.type === 'unknown' ? <StateBlock subject={T.heading} state={{ kind: answer.reason === 'malformed' ? 'malformed-response' : answer.reason }} /> : null}
      {attempt.locked ? <div role="status"><p>{attempt.sending ? T.sending : T.unknown}</p>
        <Button type="button" disabled={attempt.sending} onClick={attempt.retry}>{T.retry}</Button></div> : null}
    </>}
  </section>
}
function ReadState<TData>({ outcome, fetching, refresh }: { readonly outcome?: ViewOutcome<TData>; readonly fetching: boolean; readonly refresh: () => void }) {
  if (fetching || outcome === undefined) return <StateBlock subject={T.heading} state={{ kind: 'initial-loading' }} />
  return outcome.type === 'ok-single' ? null : <StateBlock subject={T.heading} state={classifyOutcome(outcome)} onRetry={refresh} />
}
function ImportDocuments({ binding, reservation }: { readonly binding: InvoiceImportBinding; readonly reservation: ImportReservation }) {
  const boundary = useAuthBoundary(), id = reservation.id, owner = OWNER_KIND.purchasingInvoiceImport
  const [selection, setSelection] = useState('')
  const documents = useQuery({ queryKey: documentsQueryKey(binding.namespace, binding.entry, owner, id), retry: false,
    queryFn: async ({ signal }) => {
      const fence = boundary.capture(), value = await fetchDocuments(owner, id, binding.company, signal)
      if (value.type === 'http' && [401, 403].includes(value.status)) boundary.report(value.status, fence)
      return value
    } })
  const list = !documents.isFetching && documents.data?.type === 'ok-single' ? documents.data.data : null
  const selectable = list?.items.filter(item => item.downloadable && item.scanState === 'Clean' && fileFormat(item) !== null) ?? []
  const selected = selectable.find(item => `${item.id}:${item.versionNo}:${item.sha256}` === selection)
  return <>
    <p role="status">{T.reserved}</p>
    <DocumentsPanel ownerKind={owner} ownerId={id} companyId={binding.company} namespace={binding.namespace} entryGeneration={binding.entry} heading={T.documents} />
    <ReadState outcome={documents.data} fetching={documents.isFetching} refresh={() => void documents.refetch()} />
    {list !== null ? <>
      {selectable.length === 0 ? <><StateBlock subject={T.source} state={{ kind: 'empty-no-data' }} /><p>{T.cleanRequired}</p></> : null}
      <Field label={T.source}>{(props) => <select {...props} value={selection} onChange={event => setSelection(event.target.value)}>
        <option value="">{T.choose}</option>{selectable.map(item => <option key={item.id} value={`${item.id}:${item.versionNo}:${item.sha256}`}>
          {item.fileName} — {T.version} {item.versionNo}</option>)}</select>}</Field>
      {selection && !selected ? <p role="alert">{T.changed}</p> : null}
      {selected ? <ImportFile key={selection} binding={binding} id={id} document={selected} /> : null}
    </> : null}
    <Button type="button" disabled={documents.isFetching} onClick={() => void documents.refetch()}>{T.refresh}</Button>
  </>
}
function fileFormat(document: DocumentRow): ImportOptions['format'] | null {
  if (document.contentType === 'application/xml') return 1
  if (document.contentType === 'text/csv') return 2
  if (document.contentType === 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet') return 3
  return null
}
function ImportFile({ binding, id, document }: { readonly binding: InvoiceImportBinding; readonly id: string; readonly document: DocumentRow }) {
  const boundary = useAuthBoundary(), format = fileFormat(document)!
  const [columns, setColumns] = useState<Readonly<Record<string, string>>>({}), [worksheet, setWorksheet] = useState('')
  const [delimiter, setDelimiter] = useState<ImportOptions['delimiter']>(';'), [decimalSeparator, setDecimal] = useState<ImportOptions['decimalSeparator']>('.')
  const [dateFormat, setDateFormat] = useState<ImportOptions['dateFormat']>('yyyy-MM-dd')
  const [request, setRequest] = useState<ImportPreviewRequest | null>(null), [error, setError] = useState<string | null>(null)
  const preview = useQuery<ViewOutcome<ImportPreview>>({ queryKey: [binding.namespace, binding.entry, 'invoice-import-preview', id, request],
    enabled: request !== null, retry: false, refetchOnWindowFocus: false,
    queryFn: async ({ signal }) => {
      if (request === null) return { type: 'fence-stale' }
      const fence = boundary.capture(), value = await previewInvoiceImport(binding.company, id, request, signal)
      if (value.type === 'http' && [401, 403].includes(value.status)) boundary.report(value.status, fence)
      return value
    } })
  function changed(change: () => void) { setRequest(null); setError(null); change() }
  function submit(event: FormEvent) {
    event.preventDefault()
    const mapping = Object.fromEntries(Object.entries(columns).filter(([, value]) => value.trim()).map(([key, value]) => [key, value.trim()]))
    if (format !== 1 && (Object.keys(REQUIRED).some(key => !mapping[key]) || new Set(Object.values(mapping)).size !== Object.keys(mapping).length)) {
      setError(T.invalidMapping); return
    }
    setError(null)
    setRequest({ expectedVersion: '1', documentId: document.id, documentVersion: document.versionNo, documentSha256: document.sha256,
      options: { format, delimiter, decimalSeparator, dateFormat, ...(format === 1 ? {} : { columns: mapping }), ...(format === 3 && worksheet.trim() ? { worksheet: worksheet.trim() } : {}) } })
  }
  function mappingFields(fields: Readonly<Record<string, string>>, required: boolean) {
    return Object.entries(fields).map(([key, label]) => <Field key={key} label={label}>{props => <input {...props} required={required}
      maxLength={4000} value={columns[key] ?? ''} onChange={event => changed(() => setColumns({ ...columns, [key]: event.target.value }))} />}</Field>)
  }
  const result = request !== null && !preview.isFetching && preview.data?.type === 'ok-single' ? preview.data.data : null
  return <>
    <form onSubmit={submit} className={styles.page}>
      <p>{T.format}: {format === 1 ? T.xml : format === 2 ? T.csv : T.xlsx}</p>
      {format === 3 ? <Field label={T.worksheet} help={T.worksheetHint}>{props => <input {...props} value={worksheet} maxLength={128}
        onChange={event => changed(() => setWorksheet(event.target.value))} />}</Field> : null}
      {format !== 1 ? <>
        {format === 2 ? <Field label={T.delimiter}>{props => <select {...props} value={delimiter} onChange={event => changed(() => setDelimiter(event.target.value as ImportOptions['delimiter']))}>
          <option value=";">{T.semicolon}</option><option value=",">{T.comma}</option><option value={'\t'}>{T.tab}</option></select>}</Field> : null}
        <Field label={T.decimal}>{props => <select {...props} value={decimalSeparator} onChange={event => changed(() => setDecimal(event.target.value as ImportOptions['decimalSeparator']))}>
          <option value=".">{T.dot}</option><option value=",">{T.comma}</option></select>}</Field>
        <Field label={T.dateFormat}>{props => <select {...props} value={dateFormat} onChange={event => changed(() => setDateFormat(event.target.value as ImportOptions['dateFormat']))}>
          <option value="yyyy-MM-dd">{T.isoDate}</option><option value="dd.MM.yyyy">{T.localDate}</option></select>}</Field>
        <fieldset><legend>{T.mapping}</legend><p>{T.mappingHint}</p><fieldset><legend>{T.required}</legend>{mappingFields(REQUIRED, true)}</fieldset>
          <details><summary>{T.optional}</summary>{mappingFields(OPTIONAL, false)}</details></fieldset>
      </> : null}
      {error ? <p role="alert">{error}</p> : null}<Button type="submit" disabled={preview.isFetching}>{T.preview}</Button>
    </form>
    {request !== null ? <ReadState outcome={preview.data} fetching={preview.isFetching} refresh={() => void preview.refetch()} /> : null}
    {result?.invoice ? <InvoiceSourceFacts invoice={result.invoice} /> : null}
    {result && result.issues.length > 0 ? <section role="alert"><h3>{T.issueHeading}</h3>{result.issues.map((issue, index) =>
      <p key={index}>{TOTALS[issue.field] ?? T.source}: {T.issue}</p>)}</section> : null}
  </>
}
const shown = (amount: SourceAmount | null) => amount === null ? T.missing : `${amount.value} ${amount.currency}`
function Taxes({ taxes }: { readonly taxes: readonly SourceTax[] }) {
  return taxes.length === 0 ? null : <section><h4>{T.taxes}</h4>{taxes.map((tax, index) => <dl className={styles.amounts} key={index}>
    <dt>{tax.isWithholding ? T.withholding : T.taxCode}</dt><dd>{tax.code ?? T.missing}</dd><dt>{T.rate}</dt><dd>{tax.percent ?? T.missing}</dd>
    <dt>{T.taxBase}</dt><dd>{shown(tax.taxableAmount)}</dd><dt>{T.tax}</dt><dd>{shown(tax.amount)}</dd></dl>)}</section>
}
function Adjustments({ adjustments }: { readonly adjustments: readonly SourceAdjustment[] }) {
  return adjustments.length === 0 ? null : <section><h4>{T.adjustments}</h4>{adjustments.map((item, index) => <dl className={styles.amounts} key={index}>
    <dt>{item.isCharge ? T.charge : T.allowance}</dt><dd>{shown(item.amount)}</dd><dt>{T.reason}</dt><dd>{item.reason ?? T.missing}</dd></dl>)}</section>
}
export function InvoiceSourceFacts({ invoice }: { readonly invoice: SourceInvoice }) {
  return <section aria-label={T.facts}><h3>{T.facts}</h3><dl className={styles.amounts}>
    <dt>{T.number}</dt><dd>{invoice.number}</dd><dt>{T.ettn}</dt><dd>{invoice.ettn}</dd><dt>{T.date}</dt><dd>{invoice.invoiceDate}</dd>
    <dt>{T.type}</dt><dd>{invoice.invoiceType}</dd><dt>{T.currency}</dt><dd>{invoice.currency}</dd></dl>
    <h4>{T.supplier}</h4>{invoice.supplierIdentities.map((value, index) => <p key={index}>{value.scheme}: {value.value}</p>)}
    <h4>{T.buyer}</h4>{invoice.buyerIdentities.map((value, index) => <p key={index}>{value.scheme}: {value.value}</p>)}
    <section><h4>{T.totals}</h4>{Object.entries(invoice.totals).map(([name, amount]) => <p key={name}>{TOTALS[name]}: {shown(amount)}</p>)}</section>
    <Taxes taxes={invoice.taxes} /><Adjustments adjustments={invoice.adjustments} />
    {invoice.rates.length ? <section><h4>{T.rates}</h4>{invoice.rates.map((item, index) => <dl className={styles.amounts} key={index}>
      <dt>{T.purpose}</dt><dd>{PURPOSES[item.purpose] ?? item.purpose}</dd><dt>{T.sourceCurrency}</dt><dd>{item.sourceCurrency}</dd>
      <dt>{T.targetCurrency}</dt><dd>{item.targetCurrency}</dd><dt>{T.rate}</dt><dd>{item.rate}</dd><dt>{T.sourceBase}</dt><dd>{item.sourceBaseRate ?? T.missing}</dd>
      <dt>{T.targetBase}</dt><dd>{item.targetBaseRate ?? T.missing}</dd><dt>{T.operator}</dt><dd>{item.operator ?? T.missing}</dd><dt>{T.date}</dt><dd>{item.date ?? T.missing}</dd></dl>)}</section> : null}
    {invoice.payments.length ? <section><h4>{T.payments}</h4>{invoice.payments.map((item, index) => <dl className={styles.amounts} key={index}>
      <dt>{T.paymentSource}</dt><dd>{PURPOSES[item.source] ?? item.source}</dd><dt>{T.due}</dt><dd>{item.dueOn ?? T.missing}</dd>
      <dt>{T.installmentDue}</dt><dd>{item.installmentDueOn ?? T.missing}</dd><dt>{T.amount}</dt><dd>{shown(item.amount)}</dd></dl>)}</section> : null}
    {invoice.lines.map(line => <article className={styles.card} key={line.id}><h4>{T.line} {line.id}</h4><dl className={styles.amounts}>
      <dt>{T.description}</dt><dd>{line.description}</dd><dt>{T.sellerCode}</dt><dd>{line.sellerCode ?? T.missing}</dd><dt>{T.buyerCode}</dt><dd>{line.buyerCode ?? T.missing}</dd>
      <dt>{T.unit}</dt><dd>{line.unitCode}</dd><dt>{T.quantity}</dt><dd>{line.quantity}</dd><dt>{T.net}</dt><dd>{shown(line.net)}</dd></dl>
      <Taxes taxes={line.taxes} /><Adjustments adjustments={line.adjustments} /></article>)}
  </section>
}
