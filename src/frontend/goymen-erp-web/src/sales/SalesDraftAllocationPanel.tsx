import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome } from '../shell/uiState'
import type { DraftLine } from './contract'
import {
  createPhysicalAllocation,
  fetchPhysicalAllocations,
  releasePhysicalAllocation,
  type AllocationMutationOutcome,
} from './allocationApi'
import {
  CREATE_ALLOCATION_422_CODES,
  isGuid,
  isPositiveVersion,
  type AllocationScope,
  type CreatePhysicalAllocationBody,
  type PhysicalAllocation,
  type ReleasePhysicalAllocationBody,
} from './allocationContract'
import {
  beginAllocationAttempt,
  finishAllocationAttempt,
  loadAllocationAttempt,
  sameAttemptBody,
  type AllocationAttemptScope,
} from './allocationAttempt'
import { ALLOCATION_TEXT as T } from './allocationText'
import { AllocationLabelPicker, type AllocationLabelChoice } from './AllocationLabelPicker'
import styles from './SalesPages.module.css'

export interface SalesDraftAllocationPanelProps {
  readonly enabled: boolean
  readonly companyId: string
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly salesOrderId: string
  readonly lines: readonly DraftLine[]
}

export function SalesDraftAllocationPanel(props: SalesDraftAllocationPanelProps) {
  if (!props.enabled) return null
  return (
    <section className={styles.section} aria-labelledby="sales-allocations-heading" data-testid="sales-allocations">
      <h2 id="sales-allocations-heading">{T.heading}</h2>
      <p className={styles.muted}>{T.note}</p>
      {props.lines.map((line) => <AllocationLine
        key={`${props.namespace}:${props.routeEntryGeneration}:${props.companyId}:${props.salesOrderId}:${line.lineId}:${line.productId}:${line.productVersion}`}
        {...props} line={line} />)}
    </section>
  )
}

function httpRefusalText(status: number, code: string | null): string {
  if (status === 409) return T.conflict
  if (status === 401 || status === 403) return T.forbidden
  // Definite refusal: the label's Free position is not both owned and held by the company.
  if (status === 422 && code === CREATE_ALLOCATION_422_CODES.ownerNotCompany) return T.ownerNotCompany
  return T.failed
}

function AllocationLine({ companyId, namespace, routeEntryGeneration, salesOrderId, line }: SalesDraftAllocationPanelProps & {
  readonly line: DraftLine
}) {
  const boundary = useAuthBoundary()
  const scope: AllocationScope = {
    companyId,
    salesOrderId,
    salesOrderLineId: line.lineId,
    productId: line.productId,
    productVersion: line.productVersion,
  }
  const queryKey = [namespace, routeEntryGeneration, 'physical-allocations', salesOrderId, line.lineId,
    line.productId, line.productVersion] as const
  const query = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPhysicalAllocations(scope, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const [selectedLabel, setSelectedLabel] = useState<AllocationLabelChoice | null>(null)
  // A recorded allocation or a version conflict makes the picked label's version stale: the picker is
  // remounted empty so the next allocation starts from a fresh Inventory read.
  const [pickerGeneration, setPickerGeneration] = useState(0)
  const [quantityKg, setQuantityKg] = useState('')
  const [reason, setReason] = useState('')
  const [releaseReason, setReleaseReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<{ readonly tone: 'success' | 'unknown' | 'error'; readonly text: string } | null>(null)
  const [attemptRevision, setAttemptRevision] = useState(0)
  const [recordedAllocations, setRecordedAllocations] = useState<readonly PhysicalAllocation[]>([])
  const createScope: AllocationAttemptScope = { companyId, salesOrderId, salesOrderLineId: line.lineId, kind: 'create' }
  const pendingCreate = loadAllocationAttempt<CreatePhysicalAllocationBody>(createScope)

  const refresh = async () => {
    return query.refetch()
  }

  const showRecordedAllocation = (allocation: PhysicalAllocation, refreshed: typeof query.data) => {
    if (refreshed?.type !== 'ok-single') return
    const exists = refreshed.data.items.some((candidate) => candidate.allocation.allocationId === allocation.allocationId)
    setRecordedAllocations((current) => exists
      ? current.filter((candidate) => candidate.allocationId !== allocation.allocationId)
      : [...current.filter((candidate) => candidate.allocationId !== allocation.allocationId), allocation])
  }

  const settle = async (outcome: AllocationMutationOutcome, attemptScope: AllocationAttemptScope, key: string,
    authSnapshot: ReturnType<typeof boundary.capture>, success: string) => {
    if (outcome.type === 'recorded') {
      finishAllocationAttempt(attemptScope, key)
      setAttemptRevision((value) => value + 1)
      setNotice({ tone: 'success', text: outcome.replayed ? T.replayed : success })
      const refreshed = await refresh()
      showRecordedAllocation(outcome.allocation, refreshed.data)
      return
    }
    if (outcome.type === 'http') {
      finishAllocationAttempt(attemptScope, key)
      setAttemptRevision((value) => value + 1)
      boundary.report(outcome.status, authSnapshot)
      if (outcome.status === 409) await refresh()
      setNotice({ tone: 'error', text: httpRefusalText(outcome.status, outcome.code) })
      return
    }
    if (outcome.type === 'unknown') setNotice({ tone: 'unknown', text: T.unknown })
  }

  const sendCreate = async (body?: CreatePhysicalAllocationBody) => {
    const candidate = body ?? {
      allocationId: globalThis.crypto.randomUUID(),
      salesOrderId,
      salesOrderLineId: line.lineId,
      labelId: selectedLabel?.labelId ?? '',
      expectedLabelVersion: selectedLabel?.labelVersion ?? '',
      quantityKg: quantityKg.trim(),
      reason: reason.trim(),
      reservedUntilUtc: null,
    }
    if (!isGuid(candidate.labelId) || !isPositiveVersion(candidate.expectedLabelVersion)
      || candidate.quantityKg.length === 0 || candidate.reason.length === 0) {
      setNotice({ tone: 'error', text: T.validation })
      return
    }
    const existing = loadAllocationAttempt<CreatePhysicalAllocationBody>(createScope)
    if (existing !== null && !sameAttemptBody(existing.body, candidate)) {
      setNotice({ tone: 'error', text: T.pendingMismatch })
      return
    }
    const attempt = beginAllocationAttempt(createScope, candidate)
    const authSnapshot = boundary.capture()
    setBusy(true)
    try {
      const outcome = await createPhysicalAllocation(scope, attempt)
      await settle(outcome, createScope, attempt.idempotencyKey, authSnapshot, T.saved)
      if (outcome.type === 'recorded' || (outcome.type === 'http' && outcome.status === 409)) {
        setSelectedLabel(null)
        setPickerGeneration((value) => value + 1)
      }
    } finally {
      setBusy(false)
    }
  }

  const sendRelease = async (allocation: PhysicalAllocation, body?: ReleasePhysicalAllocationBody) => {
    const attemptScope: AllocationAttemptScope = {
      companyId,
      salesOrderId,
      salesOrderLineId: line.lineId,
      kind: 'release',
      allocationId: allocation.allocationId,
    }
    const candidate = body ?? { expectedAllocationVersion: allocation.version, reason: releaseReason.trim() }
    if (candidate.reason.length === 0) {
      setNotice({ tone: 'error', text: T.validation })
      return
    }
    const existing = loadAllocationAttempt<ReleasePhysicalAllocationBody>(attemptScope)
    if (existing !== null && !sameAttemptBody(existing.body, candidate)) {
      setNotice({ tone: 'error', text: T.pendingMismatch })
      return
    }
    const attempt = beginAllocationAttempt(attemptScope, candidate)
    const authSnapshot = boundary.capture()
    setBusy(true)
    try {
      await settle(await releasePhysicalAllocation(scope, allocation.allocationId, attempt), attemptScope,
        attempt.idempotencyKey, authSnapshot, T.releasedNotice)
    } finally {
      setBusy(false)
    }
  }

  const outcome = query.data
  const state = outcome === undefined ? { kind: 'initial-loading' as const } : classifyOutcome(outcome)
  const page = outcome?.type === 'ok-single' ? outcome.data : null
  const allocations = page === null ? [] : [
    ...page.items.map((item) => item.allocation),
    ...recordedAllocations.filter((recorded) => !page.items.some((item) => item.allocation.allocationId === recorded.allocationId)),
  ]
  return (
    <article className={styles.line} data-testid={`sales-allocation-line-${line.lineId}`} data-attempt-revision={attemptRevision}>
      <h3 className={styles.lineTitle}>Satır {line.lineNo}: {line.productName}</h3>
      {state.kind === 'initial-loading' ? <p>{T.loading}</p> : null}
      {state.kind !== 'initial-loading' && state.kind !== 'ready' ? (
        <div className={styles.notice} data-tone="error">
          {state.kind === 'malformed-response' ? T.malformed : T.unavailable}{' '}
          <button type="button" className={styles.linkButton} onClick={() => void refresh()}>{T.retry}</button>
        </div>
      ) : null}
      {state.kind === 'ready' && page !== null ? (
        <>
          {page.searchWindowFull ? <p className={styles.notice} data-tone="unknown">{T.fullWindow}</p> : null}
          {allocations.length === 0 ? <p data-testid="sales-allocation-empty">{T.empty}</p> : (
            <div className={styles.tableWrap}>
              <table className={styles.table}>
                <thead><tr><th>Etiket</th><th>Durum</th><th>Tahsis kg</th><th>Kalan kg</th><th>Sürüm</th><th>İşlem</th></tr></thead>
                <tbody>{allocations.map((allocation) => {
                  const releaseScope: AllocationAttemptScope = { companyId, salesOrderId, salesOrderLineId: line.lineId,
                    kind: 'release', allocationId: allocation.allocationId }
                  const pendingRelease = loadAllocationAttempt<ReleasePhysicalAllocationBody>(releaseScope)
                  return (
                    <tr key={allocation.allocationId} data-testid={`sales-allocation-${allocation.allocationId}`}>
                      <td className={styles.identifier}>{allocation.labelId}</td>
                      <td>{allocation.status === 'Active' ? T.active : allocation.status === 'Consumed' ? T.consumed : T.released}</td>
                      <td>{allocation.allocatedKg}</td><td>{allocation.remainingKg}</td><td>{allocation.version}</td>
                      <td>{allocation.status !== 'Active' ? null : pendingRelease !== null ? (
                        <button type="button" className={styles.secondaryButton} disabled={busy}
                          onClick={() => void sendRelease(allocation, pendingRelease.body)}>{T.retryRelease}</button>
                      ) : (
                        <button type="button" className={styles.secondaryButton} disabled={busy}
                          onClick={() => void sendRelease(allocation)}>{T.release}</button>
                      )}</td>
                    </tr>
                  )
                })}</tbody>
              </table>
            </div>
          )}
          {pendingCreate === null ? (
            <AllocationLabelPicker key={pickerGeneration} companyId={companyId} productId={line.productId}
              lineId={line.lineId} disabled={busy} selected={selectedLabel} onSelect={setSelectedLabel} />
          ) : null}
          <div className={styles.grid}>
            <label htmlFor={`allocation-quantity-${line.lineId}`}>{T.quantity}</label>
            <input id={`allocation-quantity-${line.lineId}`} inputMode="decimal" value={quantityKg} onChange={(event) => setQuantityKg(event.target.value)} />
            <label htmlFor={`allocation-reason-${line.lineId}`}>{T.reason}</label>
            <input id={`allocation-reason-${line.lineId}`} value={reason} onChange={(event) => setReason(event.target.value)} />
            <label htmlFor={`allocation-release-reason-${line.lineId}`}>{T.releaseReason}</label>
            <input id={`allocation-release-reason-${line.lineId}`} value={releaseReason} onChange={(event) => setReleaseReason(event.target.value)} />
          </div>
          <div className={styles.actions}>
            {pendingCreate === null ? (
              <button type="button" className={styles.primaryButton} disabled={busy || page.searchWindowFull}
                onClick={() => void sendCreate()}>{T.create}</button>
            ) : (
              <button type="button" className={styles.primaryButton} disabled={busy}
                onClick={() => void sendCreate(pendingCreate.body)}>{T.retryCreate}</button>
            )}
          </div>
        </>
      ) : null}
      {notice === null ? null : <p className={styles.notice} data-tone={notice.tone} data-testid="sales-allocation-notice">{notice.text}</p>}
    </article>
  )
}
