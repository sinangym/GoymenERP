import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { PurchaseOrderCommandResponse, PurchaseOrderSummary } from './contract'
import { fetchMixedServicePricePreview, mixedPriceVersion, sendMixedServicePriceRevision, type MixedPriceService, type MixedServicePricePreview, type MixedServicePriceRequest } from './mixedServicePriceRevisionApi'
import { MIXED_SERVICE_PRICE_TEXT as T } from './mixedServicePriceRevisionText'
import { SERVICE_ORDER_TEXT as N } from './serviceOrderText'
import { useReceiptAdjustmentAttempt } from './useReceiptAdjustmentAttempt'
import { PURCHASING_CAPABILITY } from './capabilities'
import { useShellState } from '../shell/shellContext'
import { contextKey, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { parseDecimalInput } from '../sales/wirePrimitives'
import styles from './IncomingInvoicePages.module.css'

export interface MixedServicePriceRevisionPanelProps {
  readonly fenceKey: string; readonly companyId: string; readonly actorId: string | null; readonly sessionEpoch: number
  readonly order: PurchaseOrderSummary; readonly canEdit: boolean; readonly onSettled: () => Promise<unknown>
}
export function MixedServicePriceRevisionPanel(props: MixedServicePriceRevisionPanelProps) {
  return <PriceRevision key={JSON.stringify([props.sessionEpoch, props.actorId, props.companyId, props.order.id])} {...props} />
}
function PriceRevision(props: MixedServicePriceRevisionPanelProps) {
  const shell = useShellState(), boundary = useAuthBoundary(), heading = useId()
  const [open, setOpen] = useState(false)
  const authorized = props.canEdit && props.actorId !== null && contextKey(shell) !== null && shell.activeContext?.scopeMode === 'single'
    && shell.activeContext.companyId === props.companyId && shell.activeContext.userId === props.actorId && shell.sessionEpoch === props.sessionEpoch
    && hasCapability(shell, PURCHASING_CAPABILITY.orderSupplierRevise)
  const attemptScope = JSON.stringify([location.origin, props.sessionEpoch, props.actorId, props.companyId, props.order.id, 'mixed-service-price-revision'])
  const attempt = useReceiptAdjustmentAttempt<MixedServicePriceRequest, PurchaseOrderCommandResponse>(attemptScope,
    `${props.fenceKey}:${props.order.version}`, async (payload, key) => {
      if (!authorized) return { type: 'unknown', reason: 'service-unavailable', status: 403 }
      const capture = boundary.capture(), result = await sendMixedServicePriceRevision(props.companyId, props.order.id, payload, key)
      if (result.type === 'unknown' && (result.status === 401 || result.status === 403)) boundary.report(result.status, capture)
      return result
    }, () => { void props.onSettled() })
  const pending = attempt.snapshot !== null || attempt.sending
  const applied = attempt.outcome?.type === 'applied'
  const read = useQuery({ queryKey: [props.fenceKey, 'mixed-service-price-preview', props.order.id, props.order.version], retry: false,
    enabled: authorized && open && props.order.status === 'Approved' && !pending && !applied,
    queryFn: async ({ signal }) => {
      const capture = boundary.capture(), result = await fetchMixedServicePricePreview(props.companyId, props.order.id, props.order.version, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, capture)
      return result
    } })
  // Disabling a query does not clear cached or already-running responses. Frozen attempts suppress those human facts explicitly.
  const preview = authorized && open && !pending && !applied && !read.isFetching && read.data?.type === 'ok-single'
    && read.data.data.companyId === props.companyId && read.data.data.parent.order.companyId === props.companyId
    && read.data.data.parent.order.id === props.order.id && read.data.data.parent.order.version === props.order.version ? read.data.data : null
  function submit(payload: MixedServicePriceRequest) {
    if (!authorized || preview === null || pending || applied || !mixedPriceVersion(props.order.version, true)) return
    attempt.submit(payload)
  }
  return <section aria-labelledby={heading} className={styles.card}><h2 id={heading}>{T.heading}</h2><p>{T.note}</p>
    {!authorized ? <p role="status">{T.denied}</p> : <>
      <Button type="button" variant="secondary" aria-expanded={open} onClick={() => setOpen(value => !value)}>{open ? T.close : T.open}</Button>
      {attempt.sending ? <p role="status">{T.busy}</p> : pending ? <div role="alert"><p>{T.unknown}</p><p>{T.retained}</p>
        <Button type="button" onClick={attempt.retry}>{T.retry}</Button></div> : null}
      {applied ? <p role="status">{T.saved}</p> : attempt.outcome?.type === 'failed' ? <p role="alert">{attempt.outcome.status === 409 ? T.stale : T.refused}</p> : null}
      {open && !pending && !applied && props.order.status === 'Approved' && preview === null
        ? <StateBlock subject={T.subject} state={read.isFetching || read.data === undefined ? { kind: 'initial-loading' } : classifyOutcome(read.data)} onRetry={() => void read.refetch()} /> : null}
      {preview !== null ? <PriceRevisionForm key={JSON.stringify([props.fenceKey, props.order.version, preview])} preview={preview}
        disabled={!mixedPriceVersion(props.order.version, true)} onSubmit={submit} /> : null}
    </>}
  </section>
}
// Editable intent belongs only to this complete human preview. Hiding/refetching/closing or changing any preview fact
// unmounts/rekeys this form; the separate scoped native attempt above keeps its original unknown body/key unchanged.
function PriceRevisionForm({ preview, disabled, onSubmit }: { readonly preview: MixedServicePricePreview; readonly disabled: boolean
  readonly onSubmit: (payload: MixedServicePriceRequest) => void }) {
  const [prices, setPrices] = useState<Readonly<Record<string, string>>>({}), [reason, setReason] = useState(''), [invalid, setInvalid] = useState(false)
  function submit() {
    if (disabled) return
    const changes: MixedServicePriceRequest['changes'][number][] = []
    for (const entry of preview.services) {
      const lines: { lineId: string; unitPrice: string }[] = []
      for (const line of entry.service.lines) {
        const input = prices[`${entry.service.orderId}|${line.lineId}`]?.trim()
        if (!input) continue
        const parsed = parseDecimalInput(input)
        if (!parsed.ok || !mixedPriceVersion(entry.service.version, true)) { setInvalid(true); return }
        lines.push({ lineId: line.lineId, unitPrice: parsed.value })
      }
      if (lines.length > 0) changes.push({ serviceOrderId: entry.service.orderId, lines })
    }
    if (changes.length === 0 || !reason.trim() || reason.trim().length > 1000) { setInvalid(true); return }
    setInvalid(false)
    onSubmit({ expectedVersion: preview.parent.order.version, services: preview.services.map(x => x.selection), changes, reason: reason.trim() })
  }
  return <form onSubmit={event => { event.preventDefault(); submit() }}>
    <p>{preview.parent.order.effectiveOn} · {T.version}: {preview.parent.order.version} · {preview.parent.currency?.currencyCode}</p>
    <fieldset disabled={disabled}><legend>{T.heading}</legend>
      {preview.services.map(entry => <ServicePriceFacts key={entry.service.orderId} entry={entry} prices={prices} setPrices={setPrices} />)}
      <Field label={T.reason}>{p => <textarea {...p} required maxLength={1000} value={reason} onChange={event => setReason(event.target.value)} />}</Field>
    </fieldset>{invalid ? <p role="alert">{T.invalid}</p> : null}
    <Button type="submit" disabled={disabled}>{T.save}</Button>
  </form>
}
function ServicePriceFacts({ entry, prices, setPrices }: { readonly entry: MixedPriceService; readonly prices: Readonly<Record<string, string>>
  readonly setPrices: (change: (old: Readonly<Record<string, string>>) => Readonly<Record<string, string>>) => void }) {
  const order = entry.service
  return <article aria-label={order.documentNumber}><h3>{order.documentNumber}</h3><p>{N.kind}: {order.kind === 'ServicePurchase' ? N.service : N.subcontract} · {N.status[order.status]} · {order.businessDate} · {order.currencyCode}</p>
    <dl><dt>{T.maker}</dt><dd>{entry.preparedBy}</dd><dt>{T.created}</dt><dd><time dateTime={entry.createdAtUtc}>{entry.createdAtUtc}</time></dd>
      <dt>{T.updated}</dt><dd><time dateTime={entry.updatedAtUtc}>{entry.updatedAtUtc}</time></dd><dt>{T.version}</dt><dd>{order.version}</dd>
      <dt>{T.initial}</dt><dd>{entry.initialPreparedVersion}</dd><dt>{T.revision}</dt><dd>{entry.priceRevision}</dd></dl>
    {order.priceDisclosure === 'Withheld' ? <p>{N.withheld}</p> : null}
    <table><caption>{order.documentNumber} — {N.selected}</caption><thead><tr>{[T.line, T.quantity, T.accepted, T.unit, T.current, T.amount, N.tax].map(label => <th scope="col" key={label}>{label}</th>)}</tr></thead>
      <tbody>{order.lines.map(line => <tr key={line.lineId}><th scope="row">{line.serviceName}</th><td>{line.quantity}</td><td>{line.acceptedQuantity}</td><td>{line.unitCode}</td>
        <td>{line.unitPrice ?? N.noPrice}</td><td>{line.amount ?? N.noPrice}</td><td>{line.tax === undefined ? N.taxInInvoice : <>{N.vat}: {line.tax.vatPercent ?? line.tax.exemptionCode} · {N.withholding}: {line.tax.withholdingPercent ?? N.noWithholding} {line.tax.withholdingCode}
          {line.tax.change ? <p>{line.tax.change.changedBy} · {line.tax.change.changedAtUtc} · {line.tax.change.reason}</p> : null}</>}</td></tr>)}</tbody></table>
    {order.lines.map(line => { const key = `${order.orderId}|${line.lineId}`; return <Field key={line.lineId} label={`${order.documentNumber} — ${line.serviceName} — ${T.revised}`}>{p => <input {...p} inputMode="decimal"
      value={prices[key] ?? ''} onChange={event => setPrices(old => ({ ...old, [key]: event.target.value }))} />}</Field> })}
    {order.materials.length > 0 ? <><h4>{N.material}</h4><ul>{order.materials.map(item => <li key={item.labelId}>{item.labelId} · {item.productId} · {item.quantityKg} kg</li>)}</ul></> : null}
    {order.expectedOutputs.length > 0 ? <><h4>{N.output}</h4><ul>{order.expectedOutputs.map((item, index) => <li key={`${item.productId}:${index}`}>{item.productId} · {item.expectedQuantityKg} kg</li>)}</ul></> : null}
    {order.acceptances.length > 0 ? <><h4>{N.acceptanceHistory}</h4><ul>{order.acceptances.map(item => <li key={item.acceptanceId}>{item.acceptedOn} · {item.quantity} · {item.recordedBy} · {item.recordedAtUtc} · {item.reason}</li>)}</ul></> : null}
  </article>
}
