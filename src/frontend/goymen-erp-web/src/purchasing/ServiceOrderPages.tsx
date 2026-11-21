import { useEffect, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { isDateOnly, parseDecimalInput } from '../sales/wirePrimitives'
import { fetchParentRollCandidates, type ParentRollCandidate } from '../manufacturing/planReferences'
import { fetchOpeningProducts } from '../inventory/api'
import type { ProductStockCandidate } from '../inventory/contract'
import { fetchPurchasingCurrencyCandidates, fetchPurchasingSupplierOptions } from './api'
import { useReceiptAdjustmentAttempt } from './useReceiptAdjustmentAttempt'
import { receiptIdentity } from './goodsReceiptAttempt'
import { fetchServiceOrder, fetchServiceOrders, fetchServiceOrderReferences, fetchServiceOrderTaxOptions, submitServiceOrder } from './serviceOrderApi'
import { SubcontractTaxChoiceFields, emptySubcontractTaxChoice } from '../settings/SubcontractTaxChoiceFields'
import { selectedSubcontractTax } from '../settings/subcontractTaxContract'
import { SUBCONTRACT_TAX_TEXT as TAX } from '../settings/subcontractTaxText'
import { ADDITIONAL_COST_CAPABILITY, additionalCostPath } from './additionalCostContract'
import { ADDITIONAL_COST_TEXT } from './additionalCostText'
import { SERVICE_ORDER_CAPABILITY as C, SERVICE_ORDER_PATH, serviceOrderPath, serviceGuid, type ServiceOrder, type ServiceCreate,
  type ServiceMaterial, type ServiceOutput, type ServiceOrderReceipt } from './serviceOrderContract'
import { SERVICE_ORDER_TEXT as T } from './serviceOrderText'
import styles from './IncomingInvoicePages.module.css'

function useScope() {
  const shell = useShellState(), key = contextKey(shell)
  return key !== null && shell.activeContext?.scopeMode === 'single' && shell.activeContext.companyId !== null
    ? { company: shell.activeContext.companyId, namespace: contextKeyString(key), entry: shell.routeEntryGeneration } : null
}
type Scope = NonNullable<ReturnType<typeof useScope>>
function useRead<T>(scope: Scope, key: readonly unknown[], read: (signal: AbortSignal) => Promise<ViewOutcome<T>>, enabled = true) {
  const boundary = useAuthBoundary()
  return useQuery({ queryKey: [scope.namespace, scope.entry, 'service-orders', ...key], retry: false, enabled,
    queryFn: async ({ signal }) => { const capture = boundary.capture(), reply = await read(signal)
      if (reply.type === 'http' && (reply.status === 401 || reply.status === 403)) boundary.report(reply.status, capture)
      return reply } })
}
function ReadState<T>({ value, loading, refresh }: { readonly value?: ViewOutcome<T>; readonly loading: boolean; readonly refresh: () => void }) {
  if (loading || value === undefined) return <StateBlock subject={T.subject} state={{ kind: 'initial-loading' }} />
  return value.type === 'ok-single' || value.type === 'ok' ? null : <StateBlock subject={T.subject} state={classifyOutcome(value)} onRetry={refresh} />
}
export function ServiceOrderListPage() {
  const scope = useScope()
  return scope === null ? <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} /> : <ServiceList key={`${scope.namespace}:${scope.entry}`} scope={scope} />
}
function ServiceList({ scope }: { readonly scope: Scope }) {
  const shell = useShellState(), read = useRead(scope, ['list'], signal => fetchServiceOrders(scope.company, signal))
  const rows = !read.isFetching && read.data?.type === 'ok-single' ? read.data.data : null
  return <section className={styles.page}><h1>{T.title}</h1>
    {hasCapability(shell, C.create) && <Link to={`${SERVICE_ORDER_PATH}/yeni`}>{T.create}</Link>}
    <ReadState value={read.data} loading={read.isFetching} refresh={() => void read.refetch()} />
    {rows?.length === 0 && <StateBlock subject={T.subject} state={{ kind: 'empty-no-data' }} />}
    {rows?.map(order => <article key={order.orderId} className={styles.card}><Link to={serviceOrderPath(order.orderId)}>{order.documentNumber}</Link>
      <p>{order.businessDate} · {T.status[order.status]} · {order.kind === 'OwnMaterialSubcontract' ? T.subcontract : T.service}</p>
      <ul>{order.lines.map(line => <li key={line.lineId}>{line.serviceName} — {line.quantity} {line.unitCode}</li>)}</ul></article>)}
    {rows !== null && <p>{T.window}</p>}
  </section>
}
export function ServiceOrderCreatePage() {
  const scope = useScope()
  return scope === null ? <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} /> : <ServiceCreateForm key={`${scope.namespace}:${scope.entry}`} scope={scope} />
}
type FormLine = { lineId: string; typeId: string; unitId: string; quantity: string; price: string }
const freshLine = (): FormLine => ({ lineId: receiptIdentity(), typeId: '', unitId: '', quantity: '', price: '' })
function ServiceCreateForm({ scope }: { readonly scope: Scope }) {
  const navigate = useNavigate(), [id] = useState(receiptIdentity), [kind, setKind] = useState<1 | 2>(1), [day, setDay] = useState('')
  const [supplier, setSupplier] = useState(''), [currency, setCurrency] = useState(''), [search, setSearch] = useState(''), [query, setQuery] = useState('')
  const [lines, setLines] = useState<FormLine[]>([freshLine()]), [reason, setReason] = useState(''), [error, setError] = useState('')
  const [materials, setMaterials] = useState<readonly ServiceMaterial[]>([]), [outputs, setOutputs] = useState<readonly ServiceOutput[]>([])
  const refs = useRead(scope, ['references'], signal => fetchServiceOrderReferences(scope.company, signal))
  const suppliers = useRead(scope, ['suppliers', query], signal => fetchPurchasingSupplierOptions(scope.company, { q: query }, signal))
  const currencies = useRead(scope, ['currencies'], signal => fetchPurchasingCurrencyCandidates(scope.company, signal))
  const labels = useRead(scope, ['labels', query], signal => fetchParentRollCandidates(scope.company, query, signal), kind === 2)
  const products = useRead(scope, ['outputs', query, day], signal => fetchOpeningProducts(scope.company, day, query, signal), kind === 2 && isDateOnly(day))
  const reference = !refs.isFetching && refs.data?.type === 'ok-single' ? refs.data.data : null
  const supplierRows = !suppliers.isFetching && suppliers.data?.type === 'ok-single' ? suppliers.data.data.items : []
  const currencyRows = !currencies.isFetching && currencies.data?.type === 'ok-single' ? currencies.data.data.items : []
  const attempt = useReceiptAdjustmentAttempt<ServiceCreate, ServiceOrderReceipt>(`${scope.namespace}:service-create`, String(scope.entry),
    (payload, key) => submitServiceOrder(scope.company, payload.orderId, 'create', payload, key), () => {})
  useEffect(() => { if (attempt.outcome?.type === 'applied') navigate(serviceOrderPath(attempt.outcome.response.order.orderId)) }, [attempt.outcome, navigate])
  function submit() {
    if (reference === null || !isDateOnly(day) || !supplierRows.some(x => x.companyAccountId === supplier) || !currencyRows.some(x => x.currencyId === currency)
      || !reason.trim() || kind === 2 && (materials.length === 0 || outputs.length === 0)) { setError(T.invalid); return }
    const party = supplierRows.find(x => x.companyAccountId === supplier)!
    const prepared: ServiceCreate['lines'][number][] = []
    for (const line of lines) {
      const type = reference.serviceTypes.find(x => x.id === line.typeId), unit = reference.units.find(x => x.unitId === line.unitId)
      const qty = parseDecimalInput(line.quantity), price = line.price.trim() === '' ? null : parseDecimalInput(line.price)
      if (!type || !unit || !qty.ok || qty.value === '0' || price !== null && !price.ok) { setError(T.invalid); return }
      prepared.push({ lineId: line.lineId, serviceTypeId: type.id, expectedServiceTypeVersion: type.version, unitId: unit.unitId,
        quantity: qty.value, unitPrice: price?.ok ? price.value : null })
    }
    setError(''); attempt.submit({ orderId: id, expectedVersion: '0', kind, supplierPartyId: party.supplierPartyId, companyAccountId: party.companyAccountId,
      businessDate: day, currencyId: currency, reason: reason.trim(), lines: prepared, materials: kind === 2 ? materials : [], expectedOutputs: kind === 2 ? outputs : [] })
  }
  const frozen = attempt.locked
  return <section className={styles.page}><h1>{T.create}</h1><Link to={SERVICE_ORDER_PATH}>{T.back}</Link>
    <ReadState value={refs.data} loading={refs.isFetching} refresh={() => void refs.refetch()} />
    <ReadState value={suppliers.data} loading={suppliers.isFetching} refresh={() => void suppliers.refetch()} />
    <ReadState value={currencies.data} loading={currencies.isFetching} refresh={() => void currencies.refetch()} />
    <form onSubmit={event => { event.preventDefault(); submit() }}>
      <fieldset disabled={frozen}>
        <Field label={T.kind}>{p => <select {...p} value={kind} onChange={e => setKind(e.target.value === '2' ? 2 : 1)}><option value="1">{T.service}</option><option value="2">{T.subcontract}</option></select>}</Field>
        <Field label={T.date}>{p => <input {...p} type="date" required value={day} onChange={e => setDay(e.target.value)} />}</Field>
        <Field label={T.sourcesSearch}>{p => <input {...p} value={search} onChange={e => setSearch(e.target.value)} maxLength={100} />}</Field>
        <Button type="button" onClick={() => setQuery(search.trim())}>{T.search}</Button>
        <Field label={T.supplier}>{p => <select {...p} required value={supplier} onChange={e => setSupplier(e.target.value)}><option value="">{T.choose}</option>{supplierRows.map(x => <option key={x.companyAccountId} value={x.companyAccountId}>{x.label}</option>)}</select>}</Field>
        <Field label={T.currency}>{p => <select {...p} required value={currency} onChange={e => setCurrency(e.target.value)}><option value="">{T.choose}</option>{currencyRows.map(x => <option key={x.currencyId} value={x.currencyId}>{x.displayLabel}</option>)}</select>}</Field>
        {reference?.serviceTypes.length === 0 && <p role="status">{T.noCatalog}</p>}
        {lines.map((line, index) => <fieldset key={line.lineId}><legend>{T.service} {index + 1}</legend>
          <Field label={T.serviceType}>{p => <select {...p} required value={line.typeId} onChange={e => setLines(v => v.map(x => x.lineId === line.lineId ? { ...x, typeId: e.target.value } : x))}><option value="">{T.choose}</option>{reference?.serviceTypes.map(x => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select>}</Field>
          <Field label={T.unit}>{p => <select {...p} required value={line.unitId} onChange={e => setLines(v => v.map(x => x.lineId === line.lineId ? { ...x, unitId: e.target.value } : x))}><option value="">{T.choose}</option>{reference?.units.filter(x => kind !== 2 || x.isKilogram).map(x => <option key={x.unitId} value={x.unitId}>{x.code}</option>)}</select>}</Field>
          <Field label={T.quantity}>{p => <input {...p} required inputMode="decimal" value={line.quantity} onChange={e => setLines(v => v.map(x => x.lineId === line.lineId ? { ...x, quantity: e.target.value } : x))} />}</Field>
          <Field label={T.price}>{p => <input {...p} inputMode="decimal" value={line.price} onChange={e => setLines(v => v.map(x => x.lineId === line.lineId ? { ...x, price: e.target.value } : x))} />}</Field>
          <Button type="button" disabled={lines.length <= 1} onClick={() => setLines(v => v.filter(x => x.lineId !== line.lineId))}>{T.remove}</Button>
        </fieldset>)}
        <Button type="button" disabled={lines.length >= 100} onClick={() => setLines(v => [...v, freshLine()])}>{T.addLine}</Button>
        {kind === 2 && <><p>{T.physical}</p>
          <ReadState value={labels.data} loading={labels.isFetching} refresh={() => void labels.refetch()} />
          {isDateOnly(day) && <ReadState value={products.data} loading={products.isFetching} refresh={() => void products.refetch()} />}
          <MaterialSelection labels={labels.data?.type === 'ok-single' && !labels.isFetching ? labels.data.data.items : []}
            company={scope.company} selected={materials} setSelected={setMaterials} />
          <OutputSelection products={products.data?.type === 'ok-single' && !products.isFetching ? products.data.data.items : []}
            selected={outputs} setSelected={setOutputs} />
        </>}
        <Field label={T.reason}>{p => <textarea {...p} required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} />}</Field>
      </fieldset>
      {error && <p role="alert">{error}</p>}
      <AttemptState attempt={attempt} />
      <Button type="submit" disabled={frozen || reference === null || reference.serviceTypes.length === 0}>{T.save}</Button>
    </form>
  </section>
}
function MaterialSelection({ labels, selected, setSelected, company }: { readonly labels: readonly ParentRollCandidate[]; readonly selected: readonly ServiceMaterial[]
  readonly setSelected: (v: readonly ServiceMaterial[]) => void; readonly company: string }) {
  const [label, setLabel] = useState(''), [kg, setKg] = useState('')
  const choices = labels.filter(x => x.draftSelectable && x.positions.length > 0 && x.positions.every(p => p.ownerKind === 'Company' && p.ownerId === company))
  return <fieldset><legend>{T.material}</legend><Field label={T.material}>{p => <select {...p} value={label} onChange={e => setLabel(e.target.value)}><option value="">{T.choose}</option>{choices.map(x => <option key={x.labelId} value={x.labelId}>{x.labelCode} — {x.physicalKg}</option>)}</select>}</Field>
    <Field label={T.materialKg}>{p => <input {...p} inputMode="decimal" value={kg} onChange={e => setKg(e.target.value)} />}</Field>
    <Button type="button" onClick={() => { const item = choices.find(x => x.labelId === label), parsed = parseDecimalInput(kg)
      if (item && parsed.ok && parsed.value !== '0' && selected.every(x => x.labelId !== item.labelId)) setSelected([...selected,
        { labelId: item.labelId, labelVersion: item.labelVersion, productId: item.productId, productVersion: item.productVersion, quantityKg: parsed.value }]) }}>{T.addMaterial}</Button>
    <ul>{selected.map(x => <li key={x.labelId}>{labels.find(l => l.labelId === x.labelId)?.labelCode ?? T.selected} — {x.quantityKg}
      <Button type="button" onClick={() => setSelected(selected.filter(v => v.labelId !== x.labelId))}>{T.removeSelected}</Button></li>)}</ul></fieldset>
}
function OutputSelection({ products, selected, setSelected }: { readonly products: readonly ProductStockCandidate[]; readonly selected: readonly ServiceOutput[]; readonly setSelected: (v: readonly ServiceOutput[]) => void }) {
  const [product, setProduct] = useState(''), [kg, setKg] = useState('')
  return <fieldset><legend>{T.output}</legend><Field label={T.output}>{p => <select {...p} value={product} onChange={e => setProduct(e.target.value)}><option value="">{T.choose}</option>{products.map(x => <option key={x.productId} value={x.productId}>{x.code} — {x.name}</option>)}</select>}</Field>
    <Field label={T.outputKg}>{p => <input {...p} inputMode="decimal" value={kg} onChange={e => setKg(e.target.value)} />}</Field>
    <Button type="button" onClick={() => { const item = products.find(x => x.productId === product), parsed = parseDecimalInput(kg)
      if (item && parsed.ok && parsed.value !== '0' && selected.every(x => x.productId !== item.productId)) setSelected([...selected,
        { productId: item.productId, productVersion: item.productVersion, expectedQuantityKg: parsed.value }]) }}>{T.addOutput}</Button>
    <ul>{selected.map(x => <li key={x.productId}>{products.find(p => p.productId === x.productId)?.name ?? T.selected} — {x.expectedQuantityKg}
      <Button type="button" onClick={() => setSelected(selected.filter(v => v.productId !== x.productId))}>{T.removeSelected}</Button></li>)}</ul></fieldset>
}
export function ServiceOrderDetailPage() {
  const scope = useScope(), { serviceOrderId } = useParams()
  if (scope === null) return <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} />
  if (!serviceGuid(serviceOrderId)) return <StateBlock subject={T.subject} state={{ kind: 'not-found' }} />
  return <ServiceDetail key={`${scope.namespace}:${scope.entry}:${serviceOrderId}`} scope={scope} id={serviceOrderId} />
}
function ServiceDetail({ scope, id }: { readonly scope: Scope; readonly id: string }) {
  const shell = useShellState(), queryClient = useQueryClient(), read = useRead(scope, ['detail', id], signal => fetchServiceOrder(scope.company, id, signal))
  const value = !read.isFetching && read.data?.type === 'ok-single' ? read.data.data : null
  const changed = () => { void queryClient.invalidateQueries({ queryKey: [scope.namespace, scope.entry, 'service-orders'] }) }
  return <section className={styles.page}><h1>{value?.documentNumber ?? T.subject}</h1><Link to={SERVICE_ORDER_PATH}>{T.back}</Link>
    <ReadState value={read.data} loading={read.isFetching} refresh={() => void read.refetch()} />
    {value !== null && <><p>{T.status[value.status]} · {value.isOpen ? T.open : value.status === 'Approved' ? T.complete : ''}</p>
      {hasCapability(shell, ADDITIONAL_COST_CAPABILITY.read) && <Link to={additionalCostPath('ServiceOrder', value.orderId)}>{ADDITIONAL_COST_TEXT.title}</Link>}
      {value.kind === 'OwnMaterialSubcontract' && <p>{T.physical}</p>}
      <table><caption>{T.service}</caption><thead><tr><th scope="col">{T.serviceType}</th><th scope="col">{T.quantity}</th><th scope="col">{T.accepted}</th><th scope="col">{T.amount}</th><th scope="col">{T.tax}</th></tr></thead>
        <tbody>{value.lines.map(line => <tr key={line.lineId}><th scope="row">{line.serviceName}</th><td>{line.quantity} {line.unitCode}</td><td>{line.acceptedQuantity}</td>
          <td>{value.priceDisclosure === 'Withheld' ? T.withheld : line.amount === null ? T.noPrice : `${line.amount} ${value.currencyCode}`}</td>
          <td>{line.tax === undefined ? T.taxInInvoice : <>{T.vat}: {line.tax.vatPercent ?? line.tax.exemptionCode} · {T.withholding}: {line.tax.withholdingPercent ?? T.noWithholding} {line.tax.withholdingCode}</>}</td></tr>)}</tbody></table>
      {value.status === 'Draft' && hasCapability(shell, C.approve) && <ServiceAction scope={scope} order={value} kind="approve" changed={changed} />}
      {value.status === 'Approved' && value.isOpen && hasCapability(shell, C.accept) && <ServiceAction scope={scope} order={value} kind="accept" changed={changed} />}
      {value.status === 'Approved' && hasCapability(shell, C.revisePrices) && <ServicePriceRevision scope={scope} order={value} changed={changed} />}
      {value.kind === 'OwnMaterialSubcontract' && value.status !== 'Cancelled' && hasCapability(shell, C.approve)
        && <ServiceTaxRevision key={value.version} scope={scope} order={value} changed={changed} />}
      <h2>{T.acceptanceHistory}</h2><ul>{value.acceptances.map(a => <li key={a.acceptanceId}>{a.acceptedOn} — {a.quantity} — {a.reason}</li>)}</ul>
    </>}
  </section>
}
function ServiceTaxRevision({ scope, order, changed }: { readonly scope: Scope; readonly order: ServiceOrder; readonly changed: () => void }) {
  const source = useRead(scope, ['tax-options', order.orderId, order.version], signal => fetchServiceOrderTaxOptions(scope.company, order.orderId, order.businessDate, signal))
  const options = !source.isFetching && source.data?.type === 'ok-single' ? source.data.data : null
  const [choice, setChoice] = useState(emptySubcontractTaxChoice), [line, setLine] = useState(''), [reason, setReason] = useState(''), [invalid, setInvalid] = useState(false)
  const attempt = useReceiptAdjustmentAttempt<unknown, ServiceOrderReceipt>(`${scope.namespace}:service-tax:${order.orderId}`, `${scope.entry}:${order.version}`,
    (payload, key) => submitServiceOrder(scope.company, order.orderId, 'tax', payload, key), changed)
  function submit() {
    if (options === null || options.defaultVersion === '0' || !order.lines.some(x => x.lineId === line) || !reason.trim() || selectedSubcontractTax(options, choice) === null) { setInvalid(true); return }
    setInvalid(false); attempt.submit({ expectedVersion: order.version, lineId: line, useDefault: choice.useDefault,
      expectedDefaultVersion: options.defaultVersion, expectedSourceVersion: options.sourceVersion,
      vatRateCode: choice.useDefault ? null : choice.vatRateCode, exemptionCode: choice.useDefault || choice.exemptionCode === '' ? null : choice.exemptionCode,
      withholdingRatioCode: choice.useDefault || choice.withholdingRatioCode === '' ? null : choice.withholdingRatioCode, reason: reason.trim() })
  }
  return <form onSubmit={event => { event.preventDefault(); submit() }}><h2>{TAX.taxRevision}</h2><p>{TAX.revisionHelp}</p>
    <ReadState value={source.data} loading={source.isPending || source.isFetching} refresh={() => void source.refetch()} />
    <fieldset disabled={attempt.locked || options === null}><Field label={T.serviceType}>{p => <select {...p} required value={line} onChange={e => setLine(e.target.value)}>
      <option value="">{T.choose}</option>{order.lines.map(x => <option key={x.lineId} value={x.lineId}>{x.serviceName}</option>)}</select>}</Field>
      {options !== null && <SubcontractTaxChoiceFields options={options} value={choice} onChange={setChoice} allowDefault />}
      {options?.defaultVersion === '0' && <p role="status">{TAX.noSource}</p>}
      <Field label={TAX.reason}>{p => <textarea {...p} required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} />}</Field>
    </fieldset>{invalid && <p role="alert">{TAX.invalid}</p>}<AttemptState attempt={attempt} />
    <Button type="submit" disabled={attempt.locked || options === null || options.defaultVersion === '0'}>{TAX.save}</Button></form>
}
function ServicePriceRevision({ scope, order, changed }: { readonly scope: Scope; readonly order: ServiceOrder; readonly changed: () => void }) {
  const [prices, setPrices] = useState<Readonly<Record<string, string>>>({}), [reason, setReason] = useState(''), [invalid, setInvalid] = useState(false)
  const attempt = useReceiptAdjustmentAttempt<unknown, ServiceOrderReceipt>(`${scope.namespace}:service-prices:${order.orderId}`, `${scope.entry}:${order.version}`,
    (payload, key) => submitServiceOrder(scope.company, order.orderId, 'prices', payload, key), changed)
  function submit() {
    const selected: { lineId: string; unitPrice: string }[] = []
    for (const line of order.lines) {
      const text = prices[line.lineId]?.trim()
      if (!text) continue
      const parsed = parseDecimalInput(text)
      if (!parsed.ok) { setInvalid(true); return }
      selected.push({ lineId: line.lineId, unitPrice: parsed.value })
    }
    if (!reason.trim() || selected.length === 0) { setInvalid(true); return }
    setInvalid(false); attempt.submit({ expectedVersion: order.version, lines: selected, reason: reason.trim() })
  }
  return <form onSubmit={event => { event.preventDefault(); submit() }}><fieldset disabled={attempt.locked}>
    <legend>{T.revisePrices}</legend><p>{T.priceRevisionHelp}</p>
    {order.lines.map(line => <Field key={line.lineId} label={`${line.serviceName} — ${T.revisedPrice}`}>{p => <input {...p} inputMode="decimal"
      value={prices[line.lineId] ?? ''} onChange={event => setPrices(old => ({ ...old, [line.lineId]: event.target.value }))} />}</Field>)}
    <Field label={T.reason}>{p => <textarea {...p} required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} />}</Field>
  </fieldset>{invalid && <p role="alert">{T.invalid}</p>}<AttemptState attempt={attempt} />
    <Button type="submit" disabled={attempt.locked}>{T.revisePrices}</Button></form>
}
function ServiceAction({ scope, order, kind, changed }: { readonly scope: Scope; readonly order: ServiceOrder; readonly kind: 'approve' | 'accept'; readonly changed: () => void }) {
  const [reason, setReason] = useState(''), [line, setLine] = useState(''), [quantity, setQuantity] = useState(''), [day, setDay] = useState(''), [invalid, setInvalid] = useState(false)
  const attempt = useReceiptAdjustmentAttempt<unknown, ServiceOrderReceipt>(`${scope.namespace}:service-${kind}:${order.orderId}`, `${scope.entry}:${order.version}`,
    (payload, key) => submitServiceOrder(scope.company, order.orderId, kind, payload, key), changed)
  function submit() {
    const qty = parseDecimalInput(quantity)
    if (!reason.trim() || kind === 'accept' && (!serviceGuid(line) || !qty.ok || qty.value === '0' || !isDateOnly(day))) { setInvalid(true); return }
    attempt.submit(kind === 'approve' ? { expectedVersion: order.version, reason: reason.trim() }
      : { expectedVersion: order.version, acceptanceId: receiptIdentity(), lineId: line, quantity: qty.ok ? qty.value : '', acceptedOn: day, reason: reason.trim() })
  }
  return <form onSubmit={e => { e.preventDefault(); submit() }}><fieldset disabled={attempt.locked}><legend>{kind === 'approve' ? T.approve : T.accept}</legend>
    {kind === 'accept' && <><Field label={T.serviceType}>{p => <select {...p} value={line} required onChange={e => setLine(e.target.value)}><option value="">{T.choose}</option>{order.lines.map(x => <option value={x.lineId} key={x.lineId}>{x.serviceName}</option>)}</select>}</Field>
      <Field label={T.quantity}>{p => <input {...p} value={quantity} required inputMode="decimal" onChange={e => setQuantity(e.target.value)} />}</Field>
      <Field label={T.acceptDate}>{p => <input {...p} value={day} required type="date" onChange={e => setDay(e.target.value)} />}</Field></>}
    <Field label={T.reason}>{p => <textarea {...p} value={reason} required maxLength={1000} onChange={e => setReason(e.target.value)} />}</Field>
  </fieldset>{invalid && <p role="alert">{T.invalid}</p>}<AttemptState attempt={attempt} /><Button type="submit" disabled={attempt.locked}>{kind === 'approve' ? T.approve : T.accept}</Button></form>
}
function AttemptState({ attempt }: { readonly attempt: { readonly sending: boolean; readonly outcome: { readonly type: string; readonly status?: number; readonly code?: string | null } | null; readonly retry: () => void } }): ReactNode {
  if (attempt.sending) return <p role="status">{T.busy}</p>
  if (attempt.outcome?.type === 'unknown') return <div role="alert"><p>{T.unknown}</p><Button type="button" onClick={attempt.retry}>{T.retry}</Button></div>
  if (attempt.outcome?.type === 'failed') return <div role="alert">{attempt.outcome.code === 'purchasing_service_order_same_actor' ? T.sameActor : attempt.outcome.status === 409 ? T.conflict : T.refused}</div>
  return attempt.outcome?.type === 'applied' ? <p role="status">{T.saved}</p> : null
}
