import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { PurchaseOrderDetail } from './contract'
import { isPurchasingGuid } from './contract'
import { receiptQuantityInput, type GoodsReceipt } from './goodsReceiptContract'
import { receiptIdentity } from './goodsReceiptAttempt'
import type { SupplierReturnResponse } from './receiptAdjustmentContract'
import { createLineageSupplierReturn, fetchSupplierReturnLineagePreview, type SupplierReturnLineagePreviewQuery } from './supplierReturnLineageApi'
import type {
  SupplierReturnLineagePartRequest,
  SupplierReturnLineagePreview,
  SupplierReturnLineagePreviewPart,
  SupplierReturnLineageRequest,
} from './supplierReturnLineageContract'
import { SUPPLIER_RETURN_LINEAGE_TEXT as T } from './supplierReturnLineageText'
import { useReceiptAdjustmentAttempt } from './useReceiptAdjustmentAttempt'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './purchasing.module.css'

export interface SupplierReturnLineagePanelProps {
  readonly detail: PurchaseOrderDetail
  /** The receipt of this order the return document is anchored to (in a quantity return: the receipt it is taken from). */
  readonly receipt: GoodsReceipt
  /** The read fence (context namespace + route entry); a change drops late answers and the open attempt view. */
  readonly binding: string
  /** The session/actor/company scope of the pending idempotency key. */
  readonly attemptBinding: string
  readonly onSettled: () => void
}

interface Asked extends SupplierReturnLineagePreviewQuery { readonly block: 'Free' | 'Blocked'; readonly reason: string }
type Choice = { readonly pending: string; readonly invoiced: Readonly<Record<string, string>>; readonly manual: string }
const EMPTY_CHOICE: Choice = { pending: '', invoiced: {}, manual: '' }

/** A canonical non-negative kg (three decimals at most) from what the preparer typed; '' is not a number. */
function kgInput(value: string): string | null {
  const raw = value.trim().replace(',', '.')
  return /^0+(\.0{1,3})?$/.test(raw) ? '0' : receiptQuantityInput(raw)
}

function costInput(value: string): string | null {
  const raw = value.trim().replace(',', '.')
  if (!/^[0-9]+(\.[0-9]{1,4})?$/.test(raw)) return null
  const [whole, fraction = ''] = raw.split('.'), integer = whole.replace(/^0+(?=\d)/, ''), decimal = fraction.replace(/0+$/, '')
  const canonical = integer + (decimal === '' ? '' : `.${decimal}`)
  return /^(0|[1-9]\d{0,13})(\.[0-9]{0,3}[1-9])?$/.test(canonical) && canonical !== '0' ? canonical : null
}

function problemCode(outcome: ViewOutcome<unknown>): string | null {
  if (outcome.type !== 'http' || outcome.status !== 409 && outcome.status !== 422) return null
  const body = outcome.body
  return typeof body === 'object' && body !== null && 'code' in body && typeof body.code === 'string' ? body.code : null
}

/**
 * AK-8b (SAT-D433, SAT-D440–SAT-D445, K3-B2): return to supplier from a label that is not the receipt's own label (a split or a
 * merged one). The preparer names the source label, the kg (and the pieces of a piece-tracking label), previews the server's
 * per-receipt distribution — kg, pieces, each receipt's open quantity and cost source — chooses the invoiced / invoice-pending
 * part where a receipt has several open, enters a manual cost where the cost cannot be read, and posts ONE return. Every number
 * shown is the server's; a plan the server would refuse cannot be posted.
 */
export function SupplierReturnLineagePanel(props: SupplierReturnLineagePanelProps) {
  const id = useId()
  const [open, setOpen] = useState(false)
  return <section aria-labelledby={`${id}-heading`} data-testid="supplier-return-lineage">
    <h4 id={`${id}-heading`}>{T.heading}: {props.receipt.labelCode}</h4>
    <Button type="button" variant="secondary" aria-expanded={open} aria-controls={`${id}-body`} onClick={() => setOpen(!open)}>
      {open ? T.hide : T.show}
    </Button>
    <div id={`${id}-body`}>{open ? <Body {...props} /> : null}</div>
  </section>
}

function Body({ detail, receipt, binding, attemptBinding, onSettled }: SupplierReturnLineagePanelProps) {
  const id = useId(), boundary = useAuthBoundary()
  const company = detail.order.companyId, order = detail.order.id
  const [label, setLabel] = useState(''), [kg, setKg] = useState(''), [pieces, setPieces] = useState(''), [documentPieces, setDocumentPieces] = useState('')
  const [block, setBlock] = useState<'Free' | 'Blocked'>('Free'), [reason, setReason] = useState('')
  const [asked, setAsked] = useState<Asked | null>(null)
  const [choices, setChoices] = useState<Readonly<Record<string, Choice>>>({})
  const [invalid, setInvalid] = useState(false), [invalidChoice, setInvalidChoice] = useState(false)
  const preview = useQuery({
    queryKey: [binding, 'supplier-return-lineage-preview', order, detail.order.version, asked],
    enabled: asked !== null,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchSupplierReturnLineagePreview(asked!, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const attempt = useReceiptAdjustmentAttempt<SupplierReturnLineageRequest, SupplierReturnResponse>(
    JSON.stringify([attemptBinding, 'supplier-return-lineage', order, receipt.acceptanceId]), binding,
    (payload, key) => createLineageSupplierReturn(company, order, payload, key), onSettled)

  function current(): Asked | null {
    const quantity = receiptQuantityInput(kg)
    const count = pieces.trim() === '' ? null : /^[1-9]\d{0,8}$/.test(pieces.trim()) ? Number(pieces.trim()) : undefined
    const sourceLabelId = label.trim().toLowerCase()
    if (quantity === null || count === undefined || !isPurchasingGuid(sourceLabelId) || reason.trim() === '' || reason.trim().length > 1000) return null
    return { companyId: company, orderId: order, acceptanceId: receipt.acceptanceId, sourceLabelId, quantityKg: quantity, pieceCount: count,
      block, reason: reason.trim() }
  }

  function askPreview(event: React.FormEvent) {
    event.preventDefault()
    if (attempt.locked) return
    const next = current()
    setInvalid(next === null)
    if (next === null) return
    setChoices({}); setInvalidChoice(false)
    if (asked !== null && JSON.stringify(asked) === JSON.stringify(next)) void preview.refetch()
    else setAsked(next)
  }

  const plan = asked !== null && preview.data?.type === 'ok-single' && !preview.isFetching ? preview.data.data : null
  const now = current()
  const stale = asked !== null && (now === null || JSON.stringify(now) !== JSON.stringify(asked))

  function save() {
    if (attempt.locked || plan === null || asked === null || stale || plan.refusal !== undefined) return
    const quantityReturn = plan.lineageMode === 'Quantity'
    const parts: SupplierReturnLineagePartRequest[] = []
    let documentCost: string | null = null
    for (const part of plan.parts) {
      const choice = choices[part.goodsAcceptanceId] ?? EMPTY_CHOICE
      const own = part.goodsAcceptanceId.toLowerCase() === receipt.acceptanceId.toLowerCase()
      let selection: Pick<SupplierReturnLineagePartRequest, 'pendingKg' | 'invoiced'> = {}
      if (part.selectionRequired) {
        const pending = kgInput(choice.pending === '' ? '0' : choice.pending)
        const invoiced = part.invoiced.map((source) => ({ sourceEventId: source.sourceEventId,
          quantityKg: kgInput((choice.invoiced[source.sourceEventId] ?? '') === '' ? '0' : choice.invoiced[source.sourceEventId]) }))
        if (pending === null || invoiced.some((item) => item.quantityKg === null)) { setInvalidChoice(true); return }
        selection = { pendingKg: pending, invoiced: invoiced.filter((item) => item.quantityKg !== '0')
          .map((item) => ({ sourceEventId: item.sourceEventId, quantityKg: item.quantityKg! })) }
      }
      let manual: string | undefined
      if (part.costSource === 'ManualCostException') {
        const cost = costInput(choice.manual)
        if (cost === null) { setInvalidChoice(true); return }
        // A quantity return carries its manual cost on the document; a lineage return on the receipt whose cost is unreadable.
        if (quantityReturn) documentCost = cost
        else manual = cost
      }
      parts.push({ goodsAcceptanceId: part.goodsAcceptanceId,
        returnLineId: own ? '' : receiptIdentity(), inventoryMovementId: own ? '' : receiptIdentity(),
        ...selection, ...(manual === undefined ? {} : { manualUnitCostTryPerKg: manual }) })
    }
    // SAT-D455: the document's pieces, for a quantity return from a piece order line; the server refuses it anywhere else.
    const supplierPieces = documentPieces.trim() === '' ? null : /^[1-9]\d{0,8}$/.test(documentPieces.trim()) ? Number(documentPieces.trim()) : undefined
    if (supplierPieces === undefined) { setInvalidChoice(true); return }
    setInvalidChoice(false)
    const returnLineId = receiptIdentity(), inventoryMovementId = receiptIdentity()
    const lineageParts = parts.map((part) => part.returnLineId === '' ? { ...part, returnLineId, inventoryMovementId } : part)
    attempt.submit({ returnId: receiptIdentity(), returnLineId, goodsAcceptanceId: receipt.acceptanceId, inventoryMovementId,
      sourceBlock: asked.block, quantityKg: asked.quantityKg, manualUnitCostTryPerKg: documentCost, replacementExpected: false,
      reason: asked.reason, occurredAtUtc: new Date().toISOString(), sourceLabelId: asked.sourceLabelId,
      ...(asked.pieceCount === null ? {} : { pieceCount: asked.pieceCount }),
      ...(supplierPieces === null ? {} : { supplierPieceQuantity: supplierPieces }),
      // The document's own receipt is always one of the parts; a receipt outside the lineage is refused by the server (409).
      ...(lineageParts.some((part) => part.goodsAcceptanceId.toLowerCase() === receipt.acceptanceId.toLowerCase()) ? { lineageParts } : {}) })
  }

  function setChoice(acceptance: string, change: (choice: Choice) => Choice) {
    setChoices((value) => ({ ...value, [acceptance]: change(value[acceptance] ?? EMPTY_CHOICE) }))
  }

  const outcome = attempt.outcome
  const previewCode = preview.data === undefined ? null : problemCode(preview.data)
  return <div className={styles.actionCard}>
    <p>{T.note}</p>
    {attempt.sending ? <p role="status">{T.sending}</p> : null}
    {!attempt.sending && attempt.snapshot !== null ? <div role="alert"><p>{T.unknown}</p>
      <Button type="button" onClick={attempt.retry}>{T.retry}</Button></div> : null}
    {outcome?.type === 'failed' ? <>
      <StateBlock subject={T.heading} state={classifyOutcome({ type: 'http', status: outcome.status, body: { code: outcome.code } })} />
      <p role="alert" data-code={outcome.code ?? ''}>{outcome.code !== null && T.code[outcome.code] !== undefined ? T.code[outcome.code] : T.codeUnknown}</p>
      {outcome.detail !== undefined ? <p role="alert">{outcome.detail}</p> : null}
    </> : null}
    {outcome?.type === 'unknown' ? <StateBlock subject={T.heading} state={classifyOutcome(outcome.reason === 'service-unavailable'
      ? { type: 'http', status: outcome.status ?? 503, body: null } : { type: outcome.reason })} /> : null}
    {outcome?.type === 'applied' ? <p role="status" data-testid="supplier-return-lineage-result">
      {outcome.response.return.status === 'PendingCostException' ? T.pending : outcome.response.isReplay ? T.replayed : T.saved}</p> : null}
    <form className={styles.createForm} onSubmit={askPreview} noValidate><fieldset disabled={attempt.locked}>
      <legend>{T.heading}</legend><div className={styles.formGrid}>
        <label htmlFor={`${id}-label`}>{T.sourceLabel}</label>
        <input id={`${id}-label`} value={label} maxLength={36} aria-describedby={`${id}-label-hint`} onChange={(e) => setLabel(e.target.value)} />
        <span /><span id={`${id}-label-hint`}>{T.sourceLabelHint}</span>
        <label htmlFor={`${id}-kg`}>{T.quantityKg}</label><input id={`${id}-kg`} inputMode="decimal" value={kg} onChange={(e) => setKg(e.target.value)} />
        <label htmlFor={`${id}-pieces`}>{T.pieceCount}</label><input id={`${id}-pieces`} inputMode="numeric" value={pieces} onChange={(e) => setPieces(e.target.value)} />
        <label htmlFor={`${id}-block`}>{T.block}</label><select id={`${id}-block`} value={block} onChange={(e) => setBlock(e.target.value as typeof block)}>
          <option value="Free">{T.free}</option><option value="Blocked">{T.blocked}</option></select>
        <label htmlFor={`${id}-reason`}>{T.reason}</label><textarea id={`${id}-reason`} maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} />
      </div>
      {invalid ? <p role="alert">{T.invalid}</p> : null}
      <Button type="submit" variant="secondary">{T.preview}</Button>
    </fieldset></form>
    {asked === null ? <p data-state="empty-no-data">{T.noPreview}</p>
      : plan !== null ? <Plan plan={plan} receipt={receipt} id={id} choices={choices} setChoice={setChoice} disabled={attempt.locked} />
        : previewCode !== null ? <p role="alert" data-state="conflict" data-code={previewCode}>{T.code[previewCode] ?? T.codeUnknown}</p>
          : <StateBlock subject={T.subject} state={preview.data === undefined || preview.isFetching ? { kind: 'initial-loading' }
            : classifyOutcome(preview.data)} onRetry={() => void preview.refetch()} />}
    {plan !== null ? <>
      {stale ? <p role="alert">{T.previewStale}</p> : null}
      {invalidChoice ? <p role="alert">{T.invalidSelection}</p> : null}
      {plan.lineageMode === 'Quantity' ? <div className={styles.formGrid}>
        <label htmlFor={`${id}-document-pieces`}>{T.supplierPieceQuantity}</label>
        <input id={`${id}-document-pieces`} inputMode="numeric" value={documentPieces} disabled={attempt.locked}
          onChange={(e) => setDocumentPieces(e.target.value)} />
      </div> : null}
      <Button type="button" onClick={save} disabled={attempt.locked || stale || plan.refusal !== undefined}>{T.save}</Button>
    </> : null}
  </div>
}

function Plan({ plan, receipt, id, choices, setChoice, disabled }: {
  readonly plan: SupplierReturnLineagePreview; readonly receipt: GoodsReceipt; readonly id: string
  readonly choices: Readonly<Record<string, Choice>>
  readonly setChoice: (acceptance: string, change: (choice: Choice) => Choice) => void; readonly disabled: boolean
}) {
  const name = (part: SupplierReturnLineagePreviewPart, index: number) =>
    part.goodsAcceptanceId.toLowerCase() === receipt.acceptanceId.toLowerCase() ? T.ownReceipt : T.otherReceipt(index + 1)
  return <div data-testid="supplier-return-lineage-plan" data-mode={plan.lineageMode}>
    <p><strong>{T.modeLabel}:</strong> {T.mode[plan.lineageMode] ?? plan.lineageMode}</p>
    <p>{T.labelStock(plan.freeKg, plan.blockedKg)}</p>
    {plan.freePieces !== null && plan.blockedPieces !== null ? <p>{T.labelPieces(plan.freePieces, plan.blockedPieces)}</p> : null}
    {plan.refusal !== undefined ? <p role="alert" data-code={plan.refusal} className={styles.warning}>
      {T.refusal} {T.code[plan.refusal] ?? T.codeUnknown}</p> : null}
    <table>
      <caption>{T.tableCaption}</caption>
      <thead><tr><th scope="col">{T.receipt}</th><th scope="col">{T.partKg}</th><th scope="col">{T.partPieces}</th>
        <th scope="col">{T.costSource}</th><th scope="col">{T.receivedKg}</th><th scope="col">{T.openKg}</th>
        <th scope="col">{T.pendingRemaining}</th><th scope="col">{T.invoicedRemaining}</th></tr></thead>
      <tbody>{plan.parts.map((part, index) => <tr key={part.goodsAcceptanceId} data-testid={`supplier-return-lineage-part-${index + 1}`}>
        <th scope="row">{name(part, index)}</th>
        <td>{part.quantityKg}</td>
        <td>{part.pieceCount ?? T.noPieces}</td>
        <td>{T.costSourceText[part.costSource] ?? part.costSource}</td>
        <td>{part.receivedKg}</td>
        <td>{part.openKg}</td>
        <td>{part.pendingRemainingKg}</td>
        <td>{part.invoiced.length === 0 ? T.noInvoice : part.invoiced.map((source) => source.remainingKg).join('; ')}</td>
      </tr>)}</tbody>
    </table>
    {plan.parts.map((part, index) => {
      const receiptName = name(part, index), choice = choices[part.goodsAcceptanceId] ?? EMPTY_CHOICE
      if (!part.selectionRequired && part.costSource !== 'ManualCostException') return null
      return <fieldset key={part.goodsAcceptanceId} disabled={disabled}>
        <legend>{T.selectionHeading(receiptName)}</legend>
        {part.selectionRequired ? <>
          <p>{T.selectionNote}</p>
          <div className={styles.formGrid}>
            <label htmlFor={`${id}-pending-${index}`}>{T.pendingKg(receiptName)}</label>
            <input id={`${id}-pending-${index}`} inputMode="decimal" value={choice.pending}
              onChange={(e) => setChoice(part.goodsAcceptanceId, (value) => ({ ...value, pending: e.target.value }))} />
            {part.invoiced.map((source, sourceIndex) => <InvoicedInput key={source.sourceEventId} inputId={`${id}-invoiced-${index}-${sourceIndex}`}
              label={T.invoicedKg(receiptName, sourceIndex + 1)} value={choice.invoiced[source.sourceEventId] ?? ''}
              onChange={(text) => setChoice(part.goodsAcceptanceId, (value) => ({ ...value, invoiced: { ...value.invoiced, [source.sourceEventId]: text } }))} />)}
          </div>
        </> : null}
        {part.costSource === 'ManualCostException' ? <>
          <p>{T.manualCostNote}</p>
          <div className={styles.formGrid}>
            <label htmlFor={`${id}-manual-${index}`}>{T.manualCost(receiptName)}</label>
            <input id={`${id}-manual-${index}`} inputMode="decimal" value={choice.manual}
              onChange={(e) => setChoice(part.goodsAcceptanceId, (value) => ({ ...value, manual: e.target.value }))} />
          </div>
        </> : null}
      </fieldset>
    })}
  </div>
}

function InvoicedInput({ inputId, label, value, onChange }: {
  readonly inputId: string; readonly label: string; readonly value: string; readonly onChange: (text: string) => void
}) {
  return <>
    <label htmlFor={inputId}>{label}</label>
    <input id={inputId} inputMode="decimal" value={value} onChange={(e) => onChange(e.target.value)} />
  </>
}
