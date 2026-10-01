import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { type SourceDetail, type SourcePage, type SourceSummary } from './contract'
import { parseAmountInput } from './decimalText'
import { useFinanceBinding } from './financePort'
import { sendFinanceAllocationCommand, type FinanceAllocationWireSnapshot } from './allocationHttp'
import {
  allocationCreateAttemptSlot,
  finishAllocationCreateAttempt,
  markAllocationCreateAttemptUnknown,
  recallAllocationCreateAttempt,
  recoverAllocationCreateAttempt,
  reserveAllocationCreateAttempt,
  resendAllocationCreateAttempt,
  type AllocationCreateAttempt,
} from './allocationCreateAttempt'
import {
  readAllocationReceiptDetail,
  readAllocationReceiptSources,
  readAppliedAllocationOpenItems,
  readApprovedAllocationSources,
  type AllocationSelectionHttpResult,
} from './allocationSelectionHttp'
import type {
  AllocationOpenItem,
  AppliedAllocationOpenItemBatch,
  ApprovedAllocationOpenItemSourceBatch,
  ApprovedAllocationOpenItemSourcePage,
} from './allocationSelectionContract'

export interface AllocationSourceChoice {
  readonly id: string
  readonly label: string
}

export interface AllocationBatchChoice {
  readonly id: string
  readonly label: string
}

export interface AllocationOpenItemChoice {
  readonly id: string
  readonly label: string
}

interface AllocationCreateFieldsProps {
  readonly sources: readonly AllocationSourceChoice[]
  readonly batches: readonly AllocationBatchChoice[]
  readonly openItems: readonly AllocationOpenItemChoice[]
  readonly sourceId: string
  readonly batchId: string
  readonly openItemId: string
  readonly amount: string
  readonly reason: string
  readonly frozen: boolean
  readonly unknown: boolean
  readonly canSubmit: boolean
  readonly message: string | null
  readonly onSource: (value: string) => void
  readonly onBatch: (value: string) => void
  readonly onOpenItem: (value: string) => void
  readonly onAmount: (value: string) => void
  readonly onReason: (value: string) => void
  readonly onSubmit: () => void
  readonly onSourcePrevious?: () => void
  readonly onSourceNext?: () => void
  readonly onBatchPrevious?: () => void
  readonly onBatchNext?: () => void
}

export function AllocationCreateFields(props: AllocationCreateFieldsProps) {
  const submit = (event: FormEvent) => {
    event.preventDefault()
    props.onSubmit()
  }
  return <form aria-label="Finansal dağıtım oluştur" onSubmit={submit} noValidate>
    <label>Gerçekleşmiş tahsilat kaynağı
      <select value={props.sourceId} disabled={props.frozen} onChange={(event) => props.onSource(event.target.value)}>
        <option value="">Seçin</option>
        {props.sources.map((source) => <option key={source.id} value={source.id}>{source.label}</option>)}
      </select>
    </label>
    {props.onSourcePrevious === undefined ? null
      : <button type="button" disabled={props.frozen} onClick={props.onSourcePrevious}>Önceki tahsilat kaynakları</button>}
    {props.onSourceNext === undefined ? null
      : <button type="button" disabled={props.frozen} onClick={props.onSourceNext}>Sonraki tahsilat kaynakları</button>}
    <label>Onaylı fatura kaynağı
      <select value={props.batchId} disabled={props.frozen} onChange={(event) => props.onBatch(event.target.value)}>
        <option value="">Seçin</option>
        {props.batches.map((batch) => <option key={batch.id} value={batch.id}>{batch.label}</option>)}
      </select>
    </label>
    {props.onBatchPrevious === undefined ? null
      : <button type="button" disabled={props.frozen} onClick={props.onBatchPrevious}>Önceki fatura kaynakları</button>}
    {props.onBatchNext === undefined ? null
      : <button type="button" disabled={props.frozen} onClick={props.onBatchNext}>Sonraki fatura kaynakları</button>}
    <label>Açık kalem
      <select value={props.openItemId} disabled={props.frozen} onChange={(event) => props.onOpenItem(event.target.value)}>
        <option value="">Seçin</option>
        {props.openItems.map((item) => <option key={item.id} value={item.id}>{item.label}</option>)}
      </select>
    </label>
    <label>Dağıtım tutarı
      <input value={props.amount} disabled={props.frozen} inputMode="decimal"
        onChange={(event) => props.onAmount(event.target.value)} />
    </label>
    <label>Dağıtım nedeni
      <input value={props.reason} disabled={props.frozen} maxLength={1000}
        onChange={(event) => props.onReason(event.target.value)} />
    </label>
    <button type="submit" disabled={props.frozen ? !props.unknown : !props.canSubmit}>
      {props.unknown ? 'Aynı anahtarla yeniden dene' : 'Dağıtım oluştur'}
    </button>
    {props.message === null ? null : <p role="status">{props.message}</p>}
  </form>
}

function visibleUnallocated(detail: SourceDetail | null): string | null {
  if (detail?.source.stage !== 'Recorded' || detail.unallocated.knowledge !== 'Known'
    || detail.unallocated.amount.disclosure !== 'Visible') return null
  return detail.unallocated.amount.money.amount
}

export function selectableAllocationOpenItems(
  batch: AppliedAllocationOpenItemBatch | null,
  source: SourceDetail | null,
): readonly AllocationOpenItem[] {
  if (batch === null || source === null || visibleUnallocated(source) === null) return []
  return batch.openItems.filter((item) => item.status === 1
    && item.companyAccountId === source.source.companyAccountId
    && item.currencyId === source.source.currencyId)
}

let allocationCreateEntrySeed = 0

function nextAllocationCreateEntry(): number {
  allocationCreateEntrySeed += 1
  return allocationCreateEntrySeed
}

export function AllocationCreateForm({ onCreated }: { readonly onCreated: (allocationId: string) => void }) {
  const bound = useFinanceBinding()
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const [entry] = useState(nextAllocationCreateEntry)
  const [sourceCursors, setSourceCursors] = useState<readonly (string | null)[]>([null])
  const [batchCursors, setBatchCursors] = useState<readonly (string | null)[]>([null])
  const [sourceGeneration, setSourceGeneration] = useState(0)
  const [batchGeneration, setBatchGeneration] = useState(0)
  const [sourceId, setSourceId] = useState('')
  const [batchId, setBatchId] = useState('')
  const [openItemId, setOpenItemId] = useState('')
  const [amount, setAmount] = useState('')
  const [reason, setReason] = useState('')
  const [pending, setPending] = useState<AllocationCreateAttempt | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const namespace = bound?.namespace ?? 'unbound'
  const slot = bound === null ? null : allocationCreateAttemptSlot(bound.binding)
  const sourceCursor = sourceCursors[sourceCursors.length - 1] ?? null
  const batchCursor = batchCursors[batchCursors.length - 1] ?? null
  const shownSlot = useRef(slot)
  const shownNamespace = useRef(namespace)
  const mounted = useRef(false)
  const runGeneration = useRef(0)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      runGeneration.current += 1
    }
  }, [])
  useEffect(() => {
    shownSlot.current = slot
    shownNamespace.current = namespace
  })

  const sources = useQuery({
    queryKey: [namespace, 'finance', 'allocation-create-sources', entry, sourceCursor, sourceGeneration],
    enabled: bound !== null,
    queryFn: async ({ signal }): Promise<AllocationSelectionHttpResult<SourcePage>> => {
      const security = boundary.capture()
      const result = await readAllocationReceiptSources(bound!.binding, sourceCursor, signal)
      if (result.kind === 'authorization') boundary.report(result.status, security)
      return result
    },
  })
  const detail = useQuery({
    queryKey: [namespace, 'finance', 'allocation-create-source', entry, sourceId, sourceGeneration],
    enabled: bound !== null && sourceId !== '',
    queryFn: async ({ signal }): Promise<AllocationSelectionHttpResult<SourceDetail>> => {
      const security = boundary.capture()
      const result = await readAllocationReceiptDetail(bound!.binding, sourceId, signal)
      if (result.kind === 'authorization') boundary.report(result.status, security)
      return result
    },
  })
  const catalog = useQuery({
    queryKey: [namespace, 'finance', 'allocation-create-approved-sources', entry, batchCursor, batchGeneration],
    enabled: bound !== null,
    queryFn: async ({ signal }): Promise<AllocationSelectionHttpResult<ApprovedAllocationOpenItemSourcePage>> => {
      const security = boundary.capture()
      const result = await readApprovedAllocationSources(bound!.binding, batchCursor, signal)
      if (result.kind === 'authorization') boundary.report(result.status, security)
      return result
    },
  })
  const catalogPage = !catalog.isFetching && catalog.data?.kind === 'found' ? catalog.data.resource : null
  const approvedBatch = catalogPage === null
    ? null
    : catalogPage.items.find((item) => item.batchId === batchId) ?? null
  const applied = useQuery({
    queryKey: [namespace, 'finance', 'allocation-create-open-items', entry, batchId, batchGeneration],
    enabled: bound !== null && approvedBatch !== null,
    queryFn: async ({ signal }): Promise<AllocationSelectionHttpResult<AppliedAllocationOpenItemBatch>> => {
      const security = boundary.capture()
      const result = await readAppliedAllocationOpenItems(bound!.binding, approvedBatch!, signal)
      if (result.kind === 'authorization') boundary.report(result.status, security)
      return result
    },
  })

  useEffect(() => {
    if (slot === null) {
      setPending(null)
      return
    }
    const recovered = recoverAllocationCreateAttempt(slot)
    setPending(recovered)
    if (recovered !== null && recovered.snapshot.request.operation === 'allocation-create') {
      const body = recovered.snapshot.request.body
      setSourceId(body.sourceId)
      setOpenItemId(body.openItemId)
      setAmount(body.amount.replace('.', ','))
      setReason(body.reason)
      setMessage('Önceki gönderimin sonucu bilinmiyor; yalnız aynı anahtar ve içerikle yeniden deneyebilirsiniz.')
    }
  }, [slot])

  const sourcePage = !sources.isFetching && sources.data?.kind === 'found' ? sources.data.resource : null
  const sourceDetail = !detail.isFetching && detail.data?.kind === 'found' ? detail.data.resource : null
  const appliedBatch = !applied.isFetching && applied.data?.kind === 'found' ? applied.data.resource : null
  const openItems = selectableAllocationOpenItems(appliedBatch, sourceDetail)
  const selectedOpenItem = openItems.find((item) => item.id === openItemId) ?? null
  const parsedAmount = parseAmountInput(amount)
  const frozen = pending !== null

  const run = async (attempt: AllocationCreateAttempt, wasUnknown: boolean) => {
    if (slot === null || bound === null) return
    const generation = ++runGeneration.current
    const security = boundary.capture()
    const result = await sendFinanceAllocationCommand(attempt.snapshot)
    if (!mounted.current || runGeneration.current !== generation
      || shownSlot.current !== slot || shownNamespace.current !== bound.namespace) return
    if (result.kind === 'authorization') boundary.report(result.status, security)
    if (result.kind === 'settlement' && result.settlement.type === 'succeeded') {
      await queryClient.invalidateQueries({ queryKey: [bound.namespace, 'finance'] })
      if (!mounted.current || runGeneration.current !== generation
        || shownSlot.current !== slot || shownNamespace.current !== bound.namespace
        || recallAllocationCreateAttempt(slot)?.snapshot.key !== attempt.snapshot.key) return
      finishAllocationCreateAttempt(slot, attempt.snapshot.key)
      setPending(null)
      setMessage('Dağıtım oluşturuldu; kaydedilmiş sonuç yeniden okundu.')
      onCreated(result.settlement.resource.id)
      return
    }
    if (result.kind === 'settlement' && result.settlement.type === 'unknown') {
      setPending(markAllocationCreateAttemptUnknown(slot, attempt.snapshot.key))
      setMessage('Sonuç bilinmiyor; aynı anahtar ve içerik korunuyor.')
      return
    }
    if (wasUnknown && ((result.kind === 'settlement' && result.settlement.type === 'refused'
      && !result.settlement.replayed) || result.kind === 'authorization' || result.kind === 'http-rejection')) {
      setPending(markAllocationCreateAttemptUnknown(slot, attempt.snapshot.key))
      setMessage('İlk gönderimin sonucu hâlâ bilinmiyor; yeni işlem başlatılamaz.')
      return
    }
    finishAllocationCreateAttempt(slot, attempt.snapshot.key)
    setPending(null)
    setMessage('Dağıtım oluşturulmadı; seçimleri güncel kayıtlardan yeniden yapın.')
  }

  const submit = () => {
    if (slot === null || bound === null || pending !== null || sourceDetail === null || selectedOpenItem === null
      || !parsedAmount.ok || reason.trim() === '') return
    const snapshot: FinanceAllocationWireSnapshot = {
      key: globalThis.crypto.randomUUID(),
      binding: bound.binding,
      request: {
        operation: 'allocation-create',
        body: {
          allocationId: globalThis.crypto.randomUUID(),
          sourceId: sourceDetail.source.id,
          expectedSourceVersion: sourceDetail.source.version,
          openItemId: selectedOpenItem.id,
          expectedOpenItemVersion: selectedOpenItem.version,
          amount: parsedAmount.wire,
          reason: reason.trim(),
        },
      },
    }
    const attempt = reserveAllocationCreateAttempt(slot, snapshot)
    if (attempt === null) {
      setPending(recoverAllocationCreateAttempt(slot))
      return
    }
    setPending(attempt)
    setMessage(null)
    void run(attempt, false)
  }

  const resend = () => {
    if (slot === null || pending?.phase !== 'unknown') return
    const attempt = resendAllocationCreateAttempt(slot, pending.snapshot.key)
    if (attempt === null) return
    setPending(attempt)
    void run(attempt, true)
  }

  const sourceChoices = sourcePage === null
    ? []
    : sourcePage.items.filter((source: SourceSummary) => source.stage === 'Recorded')
      .map((source) => ({ id: source.id, label: source.sourceNumber }))
  const batchChoices = catalogPage === null
    ? []
    : catalogPage.items.map((batch: ApprovedAllocationOpenItemSourceBatch) => ({
      id: batch.batchId,
      label: batch.batchVersion,
    }))
  const openItemChoices = openItems.map((item) => ({
    id: item.id,
    label: `${item.dueOn ?? 'Vadesiz'} - ${item.outstandingAmount}`,
  }))
  if (pending?.snapshot.request.operation === 'allocation-create') {
    const body = pending.snapshot.request.body
    if (!sourceChoices.some((choice) => choice.id === body.sourceId)) {
      sourceChoices.push({ id: body.sourceId, label: `Kaynak ${body.sourceId}` })
    }
    if (!openItemChoices.some((choice) => choice.id === body.openItemId)) {
      openItemChoices.push({ id: body.openItemId, label: `Açık kalem ${body.openItemId}` })
    }
  }

  return <AllocationCreateFields
    sources={sourceChoices}
    batches={batchChoices}
    openItems={openItemChoices}
    sourceId={sourceId}
    batchId={batchId}
    openItemId={openItemId}
    amount={amount}
    reason={reason}
    frozen={frozen}
    unknown={pending?.phase === 'unknown'}
    canSubmit={bound !== null && sourceDetail !== null && selectedOpenItem !== null
      && visibleUnallocated(sourceDetail) !== null && parsedAmount.ok && reason.trim() !== ''}
    message={message}
    onSource={(value) => {
      setSourceId(value)
      setOpenItemId('')
      setSourceGeneration((current) => current + 1)
      setMessage(null)
    }}
    onBatch={(value) => {
      setBatchId(value)
      setOpenItemId('')
      setBatchGeneration((current) => current + 1)
      setMessage(null)
    }}
    onOpenItem={setOpenItemId}
    onAmount={setAmount}
    onReason={setReason}
    onSubmit={pending?.phase === 'unknown' ? resend : submit}
    onSourcePrevious={sourceCursors.length <= 1 ? undefined : () => {
      setSourceCursors((current) => current.slice(0, -1))
      setSourceId('')
      setOpenItemId('')
      setSourceGeneration((current) => current + 1)
    }}
    onSourceNext={sourcePage?.nextCursor === null || sourcePage?.nextCursor === undefined ? undefined : () => {
      setSourceCursors((current) => [...current, sourcePage.nextCursor])
      setSourceId('')
      setOpenItemId('')
      setSourceGeneration((current) => current + 1)
    }}
    onBatchPrevious={batchCursors.length <= 1 ? undefined : () => {
      setBatchCursors((current) => current.slice(0, -1))
      setBatchId('')
      setOpenItemId('')
      setBatchGeneration((current) => current + 1)
    }}
    onBatchNext={catalogPage?.nextCursor === null || catalogPage?.nextCursor === undefined ? undefined : () => {
      setBatchCursors((current) => [...current, catalogPage.nextCursor])
      setBatchId('')
      setOpenItemId('')
      setBatchGeneration((current) => current + 1)
    }}
  />
}
