import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { isDateOnly } from '../finance/dateOnly'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { fetchPurchasingSupplierOptions } from './api'
import type { PurchasingSupplierOption } from './contract'
import { PURCHASING_CAPABILITY as C } from './capabilities'
import { useReceiptAdjustmentAttempt } from './useReceiptAdjustmentAttempt'
import type { ImportPreviewRequest, SourceInvoice, SourceLine } from './invoiceImportContract'
import type { InvoiceCommandResponse } from './incomingInvoiceContract'
import { fetchImportMappingReferences, fetchImportProducts, recordInvoiceImport, resolveImportSupplier } from './invoiceImportAdmissionApi'
import { importAmount, type ImportLineMapping, type ImportInstallment, type ImportMappingReferences, type ImportProduct,
  type ImportReference, type ImportRecordSnapshot } from './invoiceImportAdmissionContract'
import { INVOICE_IMPORT_TEXT as T } from './invoiceImportText'
import type { InvoiceImportBinding } from './InvoiceImportPanel'
import styles from './IncomingInvoicePages.module.css'

interface Props { readonly binding: InvoiceImportBinding; readonly id: string; readonly source: ImportPreviewRequest; readonly invoice: SourceInvoice }
const EMPTY = '00000000-0000-0000-0000-000000000000'
export function InvoiceImportAdmissionPanel(props: Props) {
  const [open, setOpen] = useState(false), shell = useShellState()
  if (!hasCapability(shell, C.invoiceImport)) return null
  return open ? <Admission {...props} /> : <Button type="button" onClick={() => setOpen(true)}>{T.mapAndRecord}</Button>
}
function useRead<TData>(props: Props, key: readonly unknown[], fetcher: (signal: AbortSignal) => Promise<ViewOutcome<TData>>, enabled = true) {
  const boundary = useAuthBoundary()
  return useQuery({ queryKey: [props.binding.namespace, props.binding.entry, 'import-admission', props.id, props.source, ...key], retry: false,
    refetchOnWindowFocus: false, enabled, queryFn: async ({ signal }) => {
      const fence = boundary.capture(), answer = await fetcher(signal)
      if (answer.type === 'http' && [401, 403].includes(answer.status)) boundary.report(answer.status, fence)
      return answer
    } })
}
function ReadState<TData>({ value, loading, refresh }: { readonly value?: ViewOutcome<TData>; readonly loading: boolean; readonly refresh: () => void }) {
  if (loading || value === undefined) return <StateBlock subject={T.admissionHeading} state={{ kind: 'initial-loading' }} />
  return value.type === 'ok-single' ? null : <StateBlock subject={T.admissionHeading} state={classifyOutcome(value)} onRetry={refresh} />
}
function plans(invoice: SourceInvoice): ImportInstallment[] {
  const source = invoice.payments.filter(x => x.amount !== null)
  const dates = source.length > 0 ? source : invoice.payments.filter(x => x.installmentDueOn !== null || x.dueOn !== null)
  return (dates.length > 0 ? dates : [null]).map((item, index) => ({ id: crypto.randomUUID(), openItemId: crypto.randomUUID(), sequence: index + 1,
    dueOn: item?.installmentDueOn ?? item?.dueOn ?? '', amount: item?.amount?.value ?? '', functionalAmount: null }))
}
function Admission(props: Props) {
  const { binding, id, source, invoice } = props, shell = useShellState(), client = useQueryClient()
  const [cursor, setCursor] = useState<string | undefined>(), [previous, setPrevious] = useState<readonly (string | undefined)[]>([])
  const [search, setSearch] = useState(''), [q, setQ] = useState(''), [supplier, setSupplier] = useState<PurchasingSupplierOption | null>(null)
  const [currency, setCurrency] = useState(''), [mapping, setMapping] = useState<Readonly<Record<string, ImportLineMapping | undefined>>>({})
  const [installments, setInstallments] = useState<readonly ImportInstallment[]>(() => plans(invoice)), [error, setError] = useState(false)
  const refs = useRead(props, ['references'], signal => fetchImportMappingReferences(binding.company, id, source, invoice.invoiceDate, signal))
  const suppliers = useRead(props, ['suppliers', q, cursor], signal => fetchPurchasingSupplierOptions(binding.company, { q, cursor }, signal))
  const resolved = useRead(props, ['supplier', supplier], signal => resolveImportSupplier(binding.company, id, source,
    supplier!.supplierPartyId, supplier!.companyAccountId, signal), supplier !== null)
  const choices = !refs.isFetching && refs.data?.type === 'ok-single' ? refs.data.data : null
  const offered = !suppliers.isFetching && suppliers.data?.type === 'ok-single' ? suppliers.data.data : null
  const actualSupplier = !resolved.isFetching && resolved.data?.type === 'ok-single' ? resolved.data.data : null
  const frozenSourceAmounts = invoice.payments.some(x => x.amount !== null)
  const sourcePayments = frozenSourceAmounts ? invoice.payments.filter(x => x.amount !== null)
    : invoice.payments.filter(x => x.installmentDueOn !== null || x.dueOn !== null)
  const foreign = invoice.currency !== 'TRY'
  const attempt = useReceiptAdjustmentAttempt<ImportRecordSnapshot, InvoiceCommandResponse>(JSON.stringify([binding.namespace, 'invoice-import-record', id]),
    `${binding.namespace}:${binding.entry}`, (payload, key) => recordInvoiceImport(binding.company, id, payload, key),
    () => { void client.invalidateQueries({ queryKey: [binding.namespace, 'incoming-invoices'] }) })
  function setPlan(index: number, patch: Partial<ImportInstallment>) { setInstallments(values => values.map((x, i) => i === index ? { ...x, ...patch } : x)) }
  function submit() {
    const lines = invoice.lines.map(line => mapping[line.id])
    if (choices === null || actualSupplier === null || !choices.currencies.some(x => x.currencyId === currency) || lines.some(x => x === undefined)
      || installments.length === 0 || installments.some(x => !isDateOnly(x.dueOn) || !importAmount(x.amount)
        || foreign && installments.length > 1 && !importAmount(x.functionalAmount))) { setError(true); return }
    setError(false)
    attempt.submit({ facts: invoice, request: { source, expectedInvoiceVersion: '0', supplierPartyId: actualSupplier.supplierPartyId,
      companyAccountId: actualSupplier.companyAccountId, expectedPartyVersion: actualSupplier.partyVersion, expectedAccountVersion: actualSupplier.accountVersion,
      currencyId: currency, lines: lines as ImportLineMapping[], installments: installments.map((x, index) => ({ ...x, sequence: index + 1,
        functionalAmount: foreign ? installments.length === 1 ? null : x.functionalAmount : null })) } })
  }
  const outcome = attempt.outcome
  if (outcome?.type === 'applied') return <section className={styles.card} role="status"><h3>{T.recorded}</h3><p>{outcome.response.invoice.officialNumber}</p>
    <p>{T.recordedHint}</p>{hasCapability(shell, C.invoiceDetail) ? <Link to={`/satinalma/faturalar/${encodeURIComponent(outcome.response.invoice.id)}`}>{T.openRecorded}</Link> : null}</section>
  return <section className={styles.card} aria-label={T.admissionHeading}><h3>{T.admissionHeading}</h3><p>{T.admissionHint}</p>
    <ReadState value={refs.data} loading={refs.isFetching} refresh={() => void refs.refetch()} />
    <fieldset disabled={attempt.locked}><legend>{T.supplierSelection}</legend>
      <Field label={T.supplierSearch}>{p => <input {...p} value={search} maxLength={200} onChange={e => setSearch(e.target.value)} />}</Field>
      <Button type="button" onClick={() => { setQ(search.trim()); setCursor(undefined); setPrevious([]) }}>{T.find}</Button>
      <ReadState value={suppliers.data} loading={suppliers.isFetching} refresh={() => void suppliers.refetch()} />
      {offered?.items.length === 0 ? <StateBlock subject={T.supplierSelection} state={{ kind: q ? 'empty-filtered' : 'empty-no-data' }} /> : null}
      <Field label={T.supplierSelection}>{p => <select {...p} value={supplier === null ? '' : `${supplier.supplierPartyId}:${supplier.companyAccountId}`}
        onChange={e => setSupplier(offered?.items.find(x => `${x.supplierPartyId}:${x.companyAccountId}` === e.target.value) ?? null)}>
        <option value="">{T.choose}</option>{offered?.items.map(x => <option key={x.companyAccountId} value={`${x.supplierPartyId}:${x.companyAccountId}`}>{x.label}</option>)}</select>}</Field>
      {supplier === null ? null : <><p>{supplier.label}</p><ReadState value={resolved.data} loading={resolved.isFetching} refresh={() => void resolved.refetch()} /></>}
      <Button type="button" disabled={previous.length === 0 || suppliers.isFetching} onClick={() => { setCursor(previous.at(-1)); setPrevious(previous.slice(0, -1)) }}>{T.previous}</Button>
      <Button type="button" disabled={!offered?.nextCursor || suppliers.isFetching} onClick={() => { setPrevious([...previous, cursor]); setCursor(offered?.nextCursor ?? undefined) }}>{T.next}</Button>
    </fieldset>
    {choices === null ? null : <fieldset disabled={attempt.locked}><legend>{T.lineMapping}</legend>
      {choices.currencies.length === 0 ? <StateBlock subject={T.currency} state={{ kind: 'empty-no-data' }} /> : null}
      <Field label={T.currencySelection}>{p => <select {...p} value={currency} onChange={e => setCurrency(e.target.value)}><option value="">{T.choose}</option>
        {choices.currencies.map(x => <option key={x.currencyId} value={x.currencyId}>{x.code}</option>)}</select>}</Field>
      {invoice.lines.map(line => <Line key={line.id} {...props} line={line} choices={choices} onChange={value => setMapping(values => ({ ...values, [line.id]: value }))} />)}
      <h4>{T.installmentPlan}</h4><p>{T.planHint}</p><p>{T.amount}: {choices.payable} {invoice.currency}</p>
      {choices.functionalPayable === null ? null : <p>{T.functionalPayable}: {choices.functionalPayable} TRY</p>}
      {installments.map((plan, index) => <fieldset key={plan.id}><legend>{T.installment(index + 1)}</legend>
        <Field label={T.due}>{p => <input {...p} type="date" value={plan.dueOn} readOnly={(sourcePayments[index]?.installmentDueOn ?? sourcePayments[index]?.dueOn) != null}
          onChange={e => setPlan(index, { dueOn: e.target.value })} />}</Field>
        <Field label={T.amount}>{p => <input {...p} inputMode="decimal" value={plan.amount} readOnly={frozenSourceAmounts} onChange={e => setPlan(index, { amount: e.target.value })} />}</Field>
        {foreign && installments.length > 1 ? <Field label={T.installmentFunctional}>{p => <input {...p} inputMode="decimal" value={plan.functionalAmount ?? ''}
          onChange={e => setPlan(index, { functionalAmount: e.target.value })} />}</Field> : null}
        {!frozenSourceAmounts && installments.length > 1 ? <Button type="button" onClick={() => setInstallments(values => values.filter(x => x.id !== plan.id))}>{T.removePlan}</Button> : null}
      </fieldset>)}
      {!frozenSourceAmounts ? <><Button type="button" disabled={installments.length >= 2000} onClick={() => setInstallments(values => [...values,
        { id: crypto.randomUUID(), openItemId: crypto.randomUUID(), sequence: values.length + 1, dueOn: '', amount: '', functionalAmount: null }])}>{T.addPlan}</Button>
        {installments.length === 1 ? <Button type="button" onClick={() => setPlan(0, { amount: choices.payable })}>{T.useTotal}</Button> : null}</> : null}
      {error ? <p role="alert">{T.admissionInvalid}</p> : null}<Button type="button" disabled={actualSupplier === null} onClick={submit}>{T.recordInvoice}</Button>
    </fieldset>}
    {outcome?.type === 'failed' ? <><p role="alert">{T.rejected}</p><StateBlock subject={T.admissionHeading} state={classifyOutcome({ type: 'http', status: outcome.status })} /></> : null}
    {outcome?.type === 'unknown' ? <StateBlock subject={T.admissionHeading} state={{ kind: outcome.reason === 'malformed' ? 'malformed-response' : outcome.reason }} /> : null}
    {attempt.locked ? <div role="status"><p>{attempt.sending ? T.recording : T.unknown}</p><p>{T.frozenImport}</p>
      <Button type="button" disabled={attempt.sending} onClick={attempt.retry}>{T.retry}</Button></div> : null}
  </section>
}

function Line(props: Props & { readonly line: SourceLine; readonly choices: ImportMappingReferences; readonly onChange: (v: ImportLineMapping | undefined) => void }) {
  const { line, choices } = props
  const [kind, setKind] = useState(''), [term, setTerm] = useState(''), [q, setQ] = useState<string | null>(null)
  const [product, setProduct] = useState<ImportProduct | null>(null), [reference, setReference] = useState<ImportReference | null>(null)
  const [unit, setUnit] = useState(''), [purpose, setPurpose] = useState(''), [lineId] = useState(() => crypto.randomUUID())
  const search = useRead(props, ['products', line.id, q], signal => fetchImportProducts(props.binding.company, props.id, props.source, q!, signal), q !== null && kind === 'product')
  const products = !search.isFetching && search.data?.type === 'ok-single' ? search.data.data : null
  const units = choices.units.filter(x => x.code === line.unitCode)
  const taxes = line.taxes.filter(x => !x.isWithholding), vat = taxes.length === 0 ? '' : taxes.length === 1 ? taxes[0].code : null
  function change(k: string, p: ImportProduct | null, r: ImportReference | null, u: string, why: string) {
    setKind(k); setProduct(p); setReference(r); setUnit(u); setPurpose(why)
    if (vat === null || !units.some(x => x.unitId === u) || k === 'product' && (p === null || p.itemType === 'Service' || p.unitId !== u || !['resale', 'production-input'].includes(why))
      || k !== 'product' && (r === null || k !== 'service' && k !== 'expense')) { props.onChange(undefined); return }
    props.onChange({ sourceLineId: line.id, lineId, productId: p?.productId ?? EMPTY, expectedProductVersion: p?.productVersion ?? '', unitId: u,
      itemType: k === 'product' ? p!.itemType : k === 'service' ? 'Service' : 'Expense', linePurpose: k === 'product' ? why as 'resale' | 'production-input' : k as 'service' | 'expense',
      vatTaxCode: vat, ...(k === 'service' ? { serviceTypeId: r!.id, expectedServiceTypeVersion: r!.version }
        : k === 'expense' ? { expenseTypeId: r!.id, expectedExpenseTypeVersion: r!.version }
          : { expectedItemTypeVersion: p!.itemTypeVersion }) })
  }
  const list = kind === 'service' ? choices.serviceTypes : choices.expenseTypes
  return <article className={styles.card}><h4>{T.line} {line.id} · {line.description}</h4><p>{T.unit}: {line.unitCode}</p>
    <Field label={T.lineKind}>{p => <select {...p} value={kind} onChange={e => change(e.target.value, null, null, '', '')}><option value="">{T.choose}</option>
      <option value="product">{T.goods}</option><option value="service">{T.service}</option><option value="expense">{T.expense}</option></select>}</Field>
    {kind === 'product' ? <><Field label={T.productSearch}>{p => <input {...p} maxLength={200} value={term} onChange={e => setTerm(e.target.value)} />}</Field>
      <Button type="button" onClick={() => setQ(term.trim())}>{T.find}</Button>{q === null ? null : <ReadState value={search.data} loading={search.isFetching} refresh={() => void search.refetch()} />}
      {products?.items.length === 0 ? <StateBlock subject={T.productSelection} state={{ kind: q ? 'empty-filtered' : 'empty-no-data' }} /> : null}
      {products?.searchWindowFull ? <p>{T.refineSearch}</p> : null}
      <Field label={T.productSelection}>{p => <select {...p} value={product?.productId ?? ''} onChange={e => { const selected = products?.items.find(x => x.productId === e.target.value) ?? null
        change(kind, selected, null, selected?.unitId ?? '', purpose) }}><option value="">{T.choose}</option>
        {products?.items.filter(x => x.unitCode === line.unitCode && units.some(u => u.unitId === x.unitId)).map(x => <option key={`${x.productId}:${x.unitId}`} value={x.productId}>{x.code} · {x.name} · {x.unitCode}</option>)}</select>}</Field>
      {product === null ? null : <p>{T.actualItemType}: {product.itemType === 'Steel' ? T.steel : product.itemType === 'Scrap' ? T.scrap : T.service} · {T.sourceVersion}: {product.itemTypeVersion}</p>}
      {product?.itemType === 'Service' ? <p role="alert">{T.productServiceMapping}</p> : null}
      <Field label={T.purchasePurpose}>{p => <select {...p} disabled={product?.itemType === 'Service'} value={purpose} onChange={e => change(kind, product, null, unit, e.target.value)}><option value="">{T.choose}</option>
        <option value="production-input">{T.productionInput}</option><option value="resale">{T.resale}</option></select>}</Field></> : kind === 'service' || kind === 'expense' ? <>
        {list === null ? <StateBlock subject={T.typeSelection} state={{ kind: 'service-unavailable' }} /> : list.length === 0 ? <StateBlock subject={T.typeSelection} state={{ kind: 'empty-no-data' }} /> : null}
        <Field label={T.typeSelection}>{p => <select {...p} value={reference?.id ?? ''} onChange={e => change(kind, null, list?.find(x => x.id === e.target.value) ?? null, unit, kind)}>
          <option value="">{T.choose}</option>{list?.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</select>}</Field></> : null}
    {kind && kind !== 'product' ? <Field label={T.actualUnit}>{p => <select {...p} value={unit} onChange={e => change(kind, null, reference, e.target.value, kind)}>
      <option value="">{T.choose}</option>{units.map(x => <option key={x.unitId} value={x.unitId}>{x.code}</option>)}</select>}</Field> : null}
    {units.length === 0 || vat === null ? <p role="alert">{T.missingMappingSource}</p> : null}
  </article>
}
