import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useRef, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { FINANCE_CAPABILITY } from './capabilities'
import { isBoundUuid } from './contract'
import { useFinanceBinding } from './financePort'
import { readFinanceAllocation, readFinanceAllocations, sendFinanceAllocationCommand } from './allocationHttp'
import type { FinanceAllocationDetail, FinanceAllocationPage } from './allocationContract'
import styles from './Finance.module.css'

const SELECTED = 'dagitim'

export function AllocationPage() {
  const bound = useFinanceBinding()
  const shell = useShellState()
  const [search, setSearch] = useSearchParams()
  const selected = search.get(SELECTED)
  const allocationId = isBoundUuid(selected) ? selected : null
  const canWrite = hasCapability(shell, FINANCE_CAPABILITY.allocationsWrite)
  const namespace = bound?.namespace ?? 'unbound'
  const list = useQuery({
    queryKey: [namespace, 'finance', 'allocations'], enabled: bound !== null,
    queryFn: ({ signal }) => readFinanceAllocations(bound!.binding, signal),
  })
  const detail = useQuery({
    queryKey: [namespace, 'finance', 'allocation', allocationId], enabled: bound !== null && allocationId !== null,
    queryFn: ({ signal }) => readFinanceAllocation(bound!.binding, allocationId!, signal),
  })

  return <section aria-labelledby="finance-allocations-title" className={styles.page}>
    <h1 id="finance-allocations-title">Finansal dağıtımlar</h1>
    <p className={styles.notice}>Mevcut finans kaynağının açık kaleme dağıtımıdır; ikinci bir para hareketi oluşturmaz.</p>
    <AllocationList result={list.data} loading={list.data === undefined} onOpen={(id) => setSearch({ [SELECTED]: id })} />
    {allocationId === null ? null : <AllocationDetailPanel result={detail.data} loading={detail.data === undefined}
      canWrite={canWrite} onClose={() => setSearch({})} onChanged={() => void Promise.all([list.refetch(), detail.refetch()])} />}
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

function AllocationDetailPanel({ result, loading, canWrite, onClose, onChanged }: {
  readonly result: Awaited<ReturnType<typeof readFinanceAllocation>> | undefined
  readonly loading: boolean
  readonly canWrite: boolean
  readonly onClose: () => void
  readonly onChanged: () => void
}) {
  if (loading) return <StateBlock state={{ kind: 'initial-loading' }} subject="dağıtım detayı" />
  if (result?.kind !== 'found') return <ReadFailure kind={result?.kind} subject="dağıtım detayı" />
  return <section aria-label="Dağıtım detayı" className={styles.form}>
    <AllocationFacts detail={result.resource} />
    {result.resource.allocation.status === 'Active' && canWrite
      ? <ReleaseAllocation detail={result.resource} onChanged={onChanged} /> : null}
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
  const queryClient = useQueryClient()
  const key = useRef<string | null>(null)
  const [reason, setReason] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    if (bound === null || busy || reason.trim() === '') return
    key.current ??= globalThis.crypto.randomUUID()
    setBusy(true)
    const result = await sendFinanceAllocationCommand({ key: key.current, binding: bound.binding, request: {
      operation: 'allocation-release', body: { allocationId: detail.allocation.id,
        expectedAllocationVersion: detail.allocation.version, expectedOpenItemVersion: detail.openItemVersion,
        reason: reason.trim() },
    } }).finally(() => setBusy(false))
    if (result.kind === 'settlement' && result.settlement.type === 'succeeded') {
      key.current = null
      setMessage('Dağıtım bırakıldı.')
      await queryClient.invalidateQueries({ queryKey: [bound.namespace, 'finance'] })
      onChanged()
    } else if (result.kind === 'settlement' && result.settlement.type === 'unknown') {
      setMessage('Sonuç bilinmiyor; aynı anahtar ve içerikle yeniden deneyin.')
    } else {
      key.current = null
      setMessage('Dağıtım bırakılamadı; kaydı yeniden açın.')
    }
  }
  return <form aria-label="Dağıtımı bırak" onSubmit={(event) => void submit(event)}>
    <label>Bırakma nedeni<input value={reason} onChange={(event) => setReason(event.target.value)} /></label>
    <button type="submit" disabled={busy || reason.trim() === ''}>Dağıtımı bırak</button>
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
