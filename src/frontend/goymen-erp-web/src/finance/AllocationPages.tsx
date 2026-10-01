import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { FINANCE_CAPABILITY } from './capabilities'
import { isBoundUuid } from './contract'
import { useFinanceBinding } from './financePort'
import { readFinanceAllocation, readFinanceAllocations, sendFinanceAllocationCommand } from './allocationHttp'
import type { FinanceAllocationWireSnapshot } from './allocationHttp'
import {
  allocationReleaseAttemptSlot,
  finishAllocationReleaseAttempt,
  markAllocationReleaseAttemptUnknown,
  recallAllocationReleaseAttempt,
  recoverAllocationReleaseAttempt,
  reserveAllocationReleaseAttempt,
  resendAllocationReleaseAttempt,
  type AllocationReleaseAttempt,
} from './allocationReleaseAttempt'
import type { FinanceAllocationDetail, FinanceAllocationPage } from './allocationContract'
import styles from './Finance.module.css'

const SELECTED = 'dagitim'

export function AllocationPage() {
  const bound = useFinanceBinding()
  const boundary = useAuthBoundary()
  const shell = useShellState()
  const [search, setSearch] = useSearchParams()
  const [detailOpenGeneration, setDetailOpenGeneration] = useState(0)
  const selected = search.get(SELECTED)
  const allocationId = isBoundUuid(selected) ? selected : null
  const canWrite = hasCapability(shell, FINANCE_CAPABILITY.allocationsWrite)
  const namespace = bound?.namespace ?? 'unbound'
  const list = useQuery({
    queryKey: [namespace, 'finance', 'allocations'], enabled: bound !== null,
    staleTime: 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await readFinanceAllocations(bound!.binding, signal)
      if (result.kind === 'authorization') boundary.report(result.status, snapshot)
      return result
    },
  })
  const detail = useQuery({
    queryKey: [namespace, 'finance', 'allocation', allocationId, detailOpenGeneration],
    enabled: bound !== null && allocationId !== null,
    staleTime: 0,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await readFinanceAllocation(bound!.binding, allocationId!, signal)
      if (result.kind === 'authorization') boundary.report(result.status, snapshot)
      return result
    },
  })

  const openAllocation = (id: string) => {
    setDetailOpenGeneration((current) => current + 1)
    setSearch({ [SELECTED]: id })
  }

  return <section aria-labelledby="finance-allocations-title" className={styles.page}>
    <h1 id="finance-allocations-title">Finansal dağıtımlar</h1>
    <p className={styles.notice}>Mevcut finans kaynağının açık kaleme dağıtımıdır; ikinci bir para hareketi oluşturmaz.</p>
    <AllocationList result={list.data} loading={list.data === undefined} onOpen={openAllocation} />
    {allocationId === null ? null : <AllocationDetailPanel result={detail.data}
      loading={detail.data === undefined || detail.isFetching}
      canWrite={canWrite} detailKey={`${namespace}:${allocationId}:${detailOpenGeneration}`}
      onClose={() => setSearch({})} onChanged={() => void Promise.all([list.refetch(), detail.refetch()])} />}
  </section>
}

function AllocationList({ result, loading, onOpen }: {
  readonly result: Awaited<ReturnType<typeof readFinanceAllocations>> | undefined
  readonly loading: boolean
  readonly onOpen: (id: string) => void
}) {
  if (loading) return <StateBlock state={{ kind: 'initial-loading' }} subject="dağıtım" />
  if (result?.kind !== 'found') return <ReadFailure kind={result?.kind} subject="dağıtım" />
  const page: FinanceAllocationPage = result.resource
  if (page.items.length === 0) return <StateBlock state={{ kind: 'empty-filtered' }} subject="dağıtım" />
  return <div className={styles.tableWrap}><table className={styles.table}><thead><tr>
    <th>Kaynak</th><th>Açık kalem</th><th>Tutar</th><th>Durum</th><th>İşlem</th>
  </tr></thead><tbody>{page.items.map((item) => <tr key={item.id}>
    <td className={styles.mono}>{item.sourceId}</td><td className={styles.mono}>{item.openItemId}</td>
    <td>{item.amount}</td><td>{item.status === 'Active' ? 'Aktif' : 'Bırakıldı'}</td>
    <td><button type="button" onClick={() => onOpen(item.id)}>Aç</button></td>
  </tr>)}</tbody></table>{page.windowFull ? <p role="status">En yeni 50 dağıtım gösteriliyor.</p> : null}</div>
}

function AllocationDetailPanel({ result, loading, canWrite, detailKey, onClose, onChanged }: {
  readonly result: Awaited<ReturnType<typeof readFinanceAllocation>> | undefined
  readonly loading: boolean
  readonly canWrite: boolean
  readonly detailKey: string
  readonly onClose: () => void
  readonly onChanged: () => void
}) {
  if (loading) return <StateBlock state={{ kind: 'initial-loading' }} subject="dağıtım detayı" />
  if (result?.kind !== 'found') return <ReadFailure kind={result?.kind} subject="dağıtım detayı" />
  return <section aria-label="Dağıtım detayı" className={styles.form}>
    <AllocationFacts detail={result.resource} />
    {result.resource.allocation.status === 'Active' && canWrite
      ? <ReleaseAllocation key={detailKey} detail={result.resource} onChanged={onChanged} /> : null}
    <button type="button" onClick={onClose}>Kapat</button>
  </section>
}

function AllocationFacts({ detail }: { readonly detail: FinanceAllocationDetail }) {
  const item = detail.allocation
  return <dl className={styles.facts}><div><dt>Dağıtım</dt><dd className={styles.mono}>{item.id}</dd></div>
    <div><dt>Kaynak</dt><dd className={styles.mono}>{item.sourceId}</dd></div>
    <div><dt>Açık kalem</dt><dd className={styles.mono}>{item.openItemId}</dd></div>
    <div><dt>Tutar</dt><dd>{item.amount}</dd></div><div><dt>Sürüm</dt><dd>{item.version}</dd></div></dl>
}

function ReleaseAllocation({ detail, onChanged }: { readonly detail: FinanceAllocationDetail; readonly onChanged: () => void }) {
  const bound = useFinanceBinding()
  const boundary = useAuthBoundary()
  const queryClient = useQueryClient()
  const slot = bound === null ? null : allocationReleaseAttemptSlot(bound.binding, detail.allocation.id)
  const [pending, setPending] = useState<AllocationReleaseAttempt | null>(
    () => slot === null ? null : recoverAllocationReleaseAttempt(slot),
  )
  const [reason, setReason] = useState(() => {
    const recovered = slot === null ? null : recallAllocationReleaseAttempt(slot)
    return recovered?.snapshot.request.operation === 'allocation-release'
      ? recovered.snapshot.request.body.reason
      : ''
  })
  const [message, setMessage] = useState<string | null>(() =>
    slot !== null && recallAllocationReleaseAttempt(slot) !== null
      ? 'Sonuç bilinmiyor; aynı anahtar ve içerikle yeniden deneyin.'
      : null)
  const mounted = useRef(true)
  const pendingRef = useRef(pending)
  const live = useRef({ slot, namespace: bound?.namespace ?? null })
  pendingRef.current = pending
  live.current = { slot, namespace: bound?.namespace ?? null }
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
      const current = pendingRef.current
      if (slot !== null && current?.phase === 'submitting') {
        markAllocationReleaseAttemptUnknown(slot, current.snapshot.key)
      }
    }
  }, [slot])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (bound === null || slot === null || pending?.phase === 'submitting' || reason.trim() === '') return
    const wasUnknown = pending?.phase === 'unknown'
    let attempt: AllocationReleaseAttempt | null
    if (pending === null) {
      const snapshot: FinanceAllocationWireSnapshot = {
        key: globalThis.crypto.randomUUID(),
        binding: bound.binding,
        request: {
          operation: 'allocation-release',
          body: {
            allocationId: detail.allocation.id,
            expectedAllocationVersion: detail.allocation.version,
            expectedOpenItemVersion: detail.openItemVersion,
            reason: reason.trim(),
          },
        },
      }
      attempt = reserveAllocationReleaseAttempt(slot, snapshot)
    } else {
      attempt = resendAllocationReleaseAttempt(slot, pending.snapshot.key)
    }
    if (attempt === null) {
      const existing = recallAllocationReleaseAttempt(slot)
      if (existing !== null) {
        setPending(existing)
        setReason(existing.snapshot.request.operation === 'allocation-release'
          ? existing.snapshot.request.body.reason : reason)
        setMessage('Sonuç bilinmiyor; aynı anahtar ve içerikle yeniden deneyin.')
      }
      return
    }
    setPending(attempt)
    setMessage(null)
    const securitySnapshot = boundary.capture()
    const result = await sendFinanceAllocationCommand(attempt.snapshot)
    if (result.kind === 'authorization') boundary.report(result.status, securitySnapshot)
    if (!mounted.current || live.current.slot !== slot || live.current.namespace !== bound.namespace) {
      markAllocationReleaseAttemptUnknown(slot, attempt.snapshot.key)
      return
    }
    if (result.kind === 'settlement' && result.settlement.type === 'succeeded') {
      finishAllocationReleaseAttempt(slot, attempt.snapshot.key)
      setPending(null)
      setMessage('Dağıtım bırakıldı.')
      await queryClient.invalidateQueries({ queryKey: [bound.namespace, 'finance'] })
      onChanged()
    } else if (result.kind === 'settlement' && result.settlement.type === 'unknown') {
      const unknown = markAllocationReleaseAttemptUnknown(slot, attempt.snapshot.key)
      setPending(unknown)
      setMessage('Sonuç bilinmiyor; aynı anahtar ve içerikle yeniden deneyin.')
    } else if (result.kind === 'settlement' && result.settlement.type === 'refused'
      && wasUnknown && !result.settlement.replayed) {
      const unknown = markAllocationReleaseAttemptUnknown(slot, attempt.snapshot.key)
      setPending(unknown)
      setMessage('İlk denemenin sonucu hâlâ bilinmiyor; aynı anahtar ve içerikle yeniden deneyin.')
    } else if (wasUnknown && (result.kind === 'authorization' || result.kind === 'http-rejection')) {
      const unknown = markAllocationReleaseAttemptUnknown(slot, attempt.snapshot.key)
      setPending(unknown)
      setMessage('İlk denemenin sonucu hâlâ bilinmiyor; aynı anahtar ve içerikle yeniden deneyin.')
    } else {
      finishAllocationReleaseAttempt(slot, attempt.snapshot.key)
      setPending(null)
      setMessage('Dağıtım bırakılamadı; kaydı yeniden açın.')
    }
  }
  const busy = pending?.phase === 'submitting'
  const frozen = pending !== null
  return <form aria-label="Dağıtımı bırak" onSubmit={(event) => void submit(event)}>
    <label>Bırakma nedeni<input value={reason} disabled={frozen}
      onChange={(event) => setReason(event.target.value)} /></label>
    <button type="submit" disabled={busy || reason.trim() === ''}>
      {pending?.phase === 'unknown' ? 'Aynı anahtarla yeniden dene' : 'Dağıtımı bırak'}
    </button>
    {message === null ? null : <p role="status">{message}</p>}
  </form>
}

function ReadFailure({ kind, subject }: { readonly kind: string | undefined; readonly subject: string }) {
  const state = kind === 'authorization' ? { kind: 'forbidden' as const }
    : kind === 'not-found' ? { kind: 'not-found' as const }
      : kind === 'malformed' ? { kind: 'malformed-response' as const }
        : kind === 'unavailable' ? { kind: 'service-unavailable' as const }
          : { kind: 'network-error' as const }
  return <StateBlock state={state} subject={subject} />
}
