import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { Field } from '../ui/Field'
import { Button } from '../ui/Button'
import { isDateOnly, parseDecimalInput } from '../sales/wirePrimitives'
import { serviceGuid, serviceOrderPath } from './serviceOrderContract'
import { purchaseOrderDetailPath } from './capabilities'
import { fetchPurchasingSupplierOptions } from './api'
import { receiptIdentity } from './goodsReceiptAttempt'
import { useReceiptAdjustmentAttempt } from './useReceiptAdjustmentAttempt'
import { fetchAdditionalCostSource, fetchAdditionalCosts, fetchAdditionalCostReferences, recordAdditionalCost } from './additionalCostApi'
import { ADDITIONAL_COST_CAPABILITY as C, type AdditionalCostKind, type AdditionalCostReceipt, type RecordAdditionalCost } from './additionalCostContract'
import { ADDITIONAL_COST_TEXT as T } from './additionalCostText'
import styles from './IncomingInvoicePages.module.css'
interface Scope { readonly company: string; readonly namespace: string; readonly entry: number }
function useRead<T>(scope: Scope, key: readonly unknown[], read: (signal: AbortSignal) => Promise<ViewOutcome<T>>, enabled = true) {
  const boundary = useAuthBoundary()
  return useQuery({ queryKey: [scope.namespace, scope.entry, 'additional-costs', ...key], retry: false, enabled,
    queryFn: async ({ signal }) => { const capture = boundary.capture(), value = await read(signal)
      if (value.type === 'http' && (value.status === 401 || value.status === 403)) boundary.report(value.status, capture)
      return value } })
}
function ReadState<T>({ value, loading, refresh }: { readonly value?: ViewOutcome<T>; readonly loading: boolean; readonly refresh: () => void }) {
  return loading || value === undefined ? <StateBlock subject={T.subject} state={{ kind: 'initial-loading' }} />
    : value.type === 'ok-single' ? null : <StateBlock subject={T.subject} state={classifyOutcome(value)} onRetry={refresh} />
}
export function AdditionalCostPage() {
  const shell = useShellState(), key = contextKey(shell), { sourceKind, orderId } = useParams()
  if (key === null || shell.activeContext?.scopeMode !== 'single' || shell.activeContext.companyId === null)
    return <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} />
  if (!serviceGuid(orderId) || sourceKind !== 'goods' && sourceKind !== 'service') return <StateBlock subject={T.subject} state={{ kind: 'not-found' }} />
  const scope: Scope = { company: shell.activeContext.companyId, namespace: contextKeyString(key), entry: shell.routeEntryGeneration }
  return <AdditionalCosts key={`${scope.namespace}:${scope.entry}:${sourceKind}:${orderId}`} scope={scope} orderId={orderId} kind={sourceKind === 'goods' ? 'GoodsOrder' : 'ServiceOrder'} />
}
function AdditionalCosts({ scope, orderId, kind }: { readonly scope: Scope; readonly orderId: string; readonly kind: AdditionalCostKind }) {
  const shell = useShellState(), queryClient = useQueryClient()
  const source = useRead(scope, ['source', kind, orderId], signal => fetchAdditionalCostSource(scope.company, kind, orderId, signal))
  const costs = useRead(scope, ['list', kind, orderId], signal => fetchAdditionalCosts(scope.company, kind, orderId, signal))
  const [day, setDay] = useState(''), [supplierSearch, setSupplierSearch] = useState(''), [supplierQuery, setSupplierQuery] = useState('')
  const [supplier, setSupplier] = useState(''), [type, setType] = useState(''), [description, setDescription] = useState(''), [reason, setReason] = useState('')
  const [net, setNet] = useState(''), [vat, setVat] = useState(''), [acceptance, setAcceptance] = useState(''), [invalid, setInvalid] = useState(false)
  const canRecord = hasCapability(shell, C.record)
  const refs = useRead(scope, ['references', day], signal => fetchAdditionalCostReferences(scope.company, day, signal), canRecord && isDateOnly(day))
  const suppliers = useRead(scope, ['suppliers', supplierQuery], signal => fetchPurchasingSupplierOptions(scope.company, { q: supplierQuery }, signal), canRecord)
  const selectedSource = !source.isFetching && source.data?.type === 'ok-single' ? source.data.data : null
  const reference = !refs.isFetching && refs.data?.type === 'ok-single' ? refs.data.data : null
  const supplierRows = !suppliers.isFetching && suppliers.data?.type === 'ok-single' ? suppliers.data.data.items : []
  const rows = !costs.isFetching && costs.data?.type === 'ok-single' ? costs.data.data : null
  const attempt = useReceiptAdjustmentAttempt<RecordAdditionalCost, AdditionalCostReceipt>(`${scope.namespace}:additional-cost:${kind}:${orderId}`, String(scope.entry),
    (body, key) => recordAdditionalCost(scope.company, kind, body, key), () => {
      void queryClient.invalidateQueries({ queryKey: [scope.namespace, scope.entry, 'additional-costs'] })
    })
  function submit() {
    const amount = parseDecimalInput(net), account = supplierRows.find(x => x.companyAccountId === supplier)
    const selectedType = reference?.expenseTypes.find(x => x.id === type)
    if (!selectedSource || !reference || !account || !isDateOnly(day) || !amount.ok || !description.trim() || !reason.trim()
      || reference.typeRequired && !selectedType || type !== '' && !selectedType || !reference.vatRates.some(x => x.code === vat)
      || acceptance !== '' && !selectedSource.acceptances.some(x => x.acceptanceId === acceptance)) { setInvalid(true); return }
    setInvalid(false)
    attempt.submit({ costId: receiptIdentity(), expectedVersion: '0', orderId, sourceKind: kind === 'GoodsOrder' ? 1 : 2,
      expectedOrderVersion: selectedSource.orderVersion, acceptanceId: acceptance || null, supplierPartyId: account.supplierPartyId,
      companyAccountId: account.companyAccountId, businessDate: day, expenseTypeId: selectedType?.id ?? null, expectedExpenseTypeVersion: selectedType?.version ?? null,
      description: description.trim(), netAmount: amount.value, vatRateCode: vat, expectedVatListVersion: reference.vatListVersion, reason: reason.trim() })
  }
  return <section className={styles.page}><h1>{T.title}</h1><p>{T.note}</p>
    <Link to={kind === 'GoodsOrder' ? purchaseOrderDetailPath(orderId) : serviceOrderPath(orderId)}>{T.back}</Link>
    <ReadState value={source.data} loading={source.isFetching} refresh={() => void source.refetch()} />
    <ReadState value={costs.data} loading={costs.isFetching} refresh={() => void costs.refetch()} />
    {selectedSource && <h2>{selectedSource.orderNumber ?? T.noNumber}</h2>}
    {rows?.length === 0 && <StateBlock subject={T.subject} state={{ kind: 'empty-no-data' }} />}
    {rows !== null && <><table><caption>{T.title}</caption><thead><tr><th scope="col">{T.description}</th><th scope="col">{T.type}</th><th scope="col">{T.net}</th><th scope="col">{T.vatAmount}</th><th scope="col">{T.gross}</th></tr></thead>
      <tbody>{rows.map(row => <tr key={row.costId}><th scope="row">{row.description}</th><td>{row.expenseTypeName ?? row.description}</td>
        <td>{row.netAmount} {row.currencyCode}</td><td>{row.vatAmount} {row.currencyCode}</td><td>{row.grossAmount} {row.currencyCode}</td></tr>)}</tbody></table><p>{T.window}</p></>}
    {!canRecord && <p>{T.unavailableWrite}</p>}
    {canRecord && selectedSource !== null && <form onSubmit={event => { event.preventDefault(); submit() }}>
      <fieldset disabled={attempt.locked}><legend>{T.record}</legend>
        <Field label={T.date}>{p => <input {...p} type="date" required value={day} onChange={event => setDay(event.target.value)} />}</Field>
        <Field label={T.search}>{p => <input {...p} value={supplierSearch} maxLength={100} onChange={event => setSupplierSearch(event.target.value)} />}</Field>
        <Button type="button" onClick={() => setSupplierQuery(supplierSearch.trim())}>{T.find}</Button>
        <ReadState value={suppliers.data} loading={suppliers.isFetching} refresh={() => void suppliers.refetch()} />
        <Field label={T.supplier}>{p => <select {...p} required value={supplier} onChange={event => setSupplier(event.target.value)}><option value="">{T.select}</option>
          {supplierRows.map(x => <option key={x.companyAccountId} value={x.companyAccountId}>{x.label}</option>)}</select>}</Field>
        {isDateOnly(day) && <ReadState value={refs.data} loading={refs.isFetching} refresh={() => void refs.refetch()} />}
        {reference && <><Field label={T.type}>{p => <select {...p} required={reference.typeRequired} value={type} onChange={event => setType(event.target.value)}><option value="">{T.select}</option>
          {reference.expenseTypes.map(x => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}</select>}</Field>
          {!reference.typeRequired && <p>{T.untyped}</p>}
          <Field label={T.vat}>{p => <select {...p} required value={vat} onChange={event => setVat(event.target.value)}><option value="">{T.select}</option>
            {reference.vatRates.map(x => <option key={x.code} value={x.code}>{x.name} — {x.percent}</option>)}</select>}</Field></>}
        <Field label={T.acceptance}>{p => <select {...p} value={acceptance} onChange={event => setAcceptance(event.target.value)}><option value="">{T.orderOnly}</option>
          {selectedSource.acceptances.map((x, index) => <option key={x.acceptanceId} value={x.acceptanceId}>{index + 1} · {x.recordedAtUtc.slice(0, 10)} · {x.quantity}</option>)}</select>}</Field>
        <Field label={T.description}>{p => <textarea {...p} required value={description} maxLength={1000} onChange={event => setDescription(event.target.value)} />}</Field>
        <Field label={T.net}>{p => <input {...p} required inputMode="decimal" value={net} onChange={event => setNet(event.target.value)} />}</Field>
        <span>{selectedSource.currencyCode}</span>
        <Field label={T.reason}>{p => <textarea {...p} required value={reason} maxLength={1000} onChange={event => setReason(event.target.value)} />}</Field>
      </fieldset>
      {invalid && <p role="alert">{T.invalid}</p>}
      {attempt.sending && <p role="status">{T.saving}</p>}
      {/* The held attempt, not the last answer, offers the retry: a remount or refreshed read keeps the same key reachable. */}
      {!attempt.sending && attempt.snapshot !== null && <div role="alert">{T.unknown}<Button type="button" onClick={attempt.retry}>{T.retry}</Button></div>}
      {attempt.outcome?.type === 'failed' && <p role="alert">{T.refused}</p>}
      {attempt.outcome?.type === 'applied' && <p role="status">{T.saved}</p>}
      <Button type="submit" disabled={attempt.locked || reference === null}>{T.record}</Button>
    </form>}
  </section>
}
