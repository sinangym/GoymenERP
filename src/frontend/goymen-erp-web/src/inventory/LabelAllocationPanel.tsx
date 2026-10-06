import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { salesDetailPath } from '../sales/capabilities'
import { parseQuantityInput } from '../sales/wirePrimitives'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import {
  changeAllocation,
  fetchLabelAllocations,
  releaseAllocation,
  type AllocationChangeOutcome,
  type AllocationReleaseOutcome,
} from './allocationChangeApi'
import {
  ALLOCATION_STATUS_TEXT,
  allocationChangeRefusalText,
  allocationReleaseRefusalText,
  isPositiveKilogramText,
  isReason,
  REASON_MAX_LENGTH,
  type AllocationChangeEntry,
  type AllocationChangeRequest,
  type AllocationReleaseRequest,
  type LabelAllocationItem,
} from './allocationChangeContract'
import { isGuid } from './labelTransformationContract'
import styles from './inventory.module.css'

/** One change or release whose outcome is not known yet: resent only with the same key and payload. */
type Pending =
  | {
    readonly kind: 'change'
    readonly allocationId: string
    readonly idempotencyKey: string
    readonly payload: AllocationChangeRequest
  }
  | {
    readonly kind: 'release'
    readonly allocationId: string
    readonly idempotencyKey: string
    readonly payload: AllocationReleaseRequest
  }

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly pending: Pending }
  | { readonly phase: 'unknown'; readonly pending: Pending }
  | { readonly phase: 'failed'; readonly message: string }
  | { readonly phase: 'done'; readonly message: string }

interface ChangeDraft {
  readonly mode: 'change'
  readonly allocationId: string
  readonly quantityKg: string
  readonly moveTo: boolean
  readonly targetLabelId: string
  readonly targetLabelVersion: string
  readonly reason: string
}

/** AT-063 "Tahsisi serbest bırak": the whole remaining reservation, with a mandatory reason. */
interface ReleaseDraft {
  readonly mode: 'release'
  readonly allocationId: string
  readonly reason: string
}

type Draft = ChangeDraft | ReleaseDraft

const UNKNOWN_TEXT = 'Tahsis değişikliğinin sonucu bilinmiyor. Aynı anahtar ve içerikle yeniden gönderin; yeni bir işlem başlatmayın.'
const RELEASE_UNKNOWN_TEXT = 'Tahsis serbest bırakmanın sonucu bilinmiyor. Aynı anahtar ve içerikle yeniden gönderin; yeni bir işlem başlatmayın.'
const ABANDON_NOTE = 'Bırakırsanız tahsisler yeniden okunur. Önceki gönderim kaydedildiyse listede görünür; aynı değişiklik ikinci kez uygulanmaz.'

function newId(): string { return globalThis.crypto.randomUUID() }

function short(id: string): string { return `…${id.slice(-8)}` }

function changeText(entry: AllocationChangeEntry, allocationId: string): string {
  if (entry.kind === 'Resize') return `Miktar ${entry.sourceRemainingKg} kg → ${entry.targetRemainingKg} kg`
  return entry.sourceAllocationId.toLowerCase() === allocationId.toLowerCase()
    ? `Başka etikete taşındı: ${entry.sourceRemainingKg} kg → yeni tahsis ${short(entry.targetAllocationId)} (${entry.targetRemainingKg} kg)`
    : `Önceki tahsisten taşındı: ${short(entry.sourceAllocationId)} (${entry.sourceRemainingKg} kg) → ${entry.targetRemainingKg} kg`
}

function completionText(outcome: Extract<AllocationChangeOutcome, { type: 'applied' }>): string {
  const replay = outcome.replayed ? ' (önceki kayıtlı sonuç yeniden gösterildi)' : ''
  const current = outcome.response.allocation
  return outcome.response.kind === 'Resize'
    ? `Tahsis güncellendi: ${current.remainingKg} kg ayrılmış durumda; stok hareketi oluşmadı${replay}.`
    : `Tahsis başka etikete taşındı: yeni tahsis ${short(current.allocationId)} ${current.remainingKg} kg; bu etiketteki tahsis serbest bırakıldı${replay}.`
}

function releaseCompletionText(outcome: Extract<AllocationReleaseOutcome, { type: 'applied' }>): string {
  const replay = outcome.replayed ? ' (önceki kayıtlı sonuç yeniden gösterildi)' : ''
  return `Tahsis serbest bırakıldı: ${short(outcome.response.allocationId)} artık aktif değil; ayrılan miktar etiketin serbest stokuna döndü, stok hareketi oluşmadı${replay}.`
}

/**
 * INV-AL-1 change on the ST-02 label detail (Master v3 §25.4, AT-063 "Tahsis Revize"): the label's sales allocations,
 * their history, and — for an active one — a change of its kilograms or a move to another label of the same product.
 * Every rule (free stock, ownership, versions, the approved Sales line) is the server's; nothing here is final until
 * the server answers.
 */
export function LabelAllocationPanel({ labelId, companyId, namespace, routeEntry, onChanged, canReadSalesOrder, startRelease = false }: {
  readonly labelId: string
  readonly companyId: string
  /** The shell context namespace and route entry the label detail read is keyed by: a reopen (e.g. an arrival from the
   * ST-01 release action) never bases the release form on another entry's cached list. */
  readonly namespace: string
  readonly routeEntry: number
  readonly onChanged: () => void
  /** Advertised by the current shell context; the Sales route performs its own authorization. */
  readonly canReadSalesOrder: boolean
  /** Opened from the ST-01 "Tahsisi serbest bırak" row action: the release form of the ONE active allocation opens. */
  readonly startRelease?: boolean
}) {
  const boundary = useAuthBoundary()
  const [attempt, setAttempt] = useState<Attempt>({ phase: 'idle' })
  const [chosenDraft, setChosenDraft] = useState<Draft | null>(null)
  // Arrived from the ST-01 release action and the user has not chosen anything yet.
  const [releaseArrival, setReleaseArrival] = useState(startRelease)
  const [formError, setFormError] = useState<string | null>(null)
  const generation = useRef(0)
  const inFlight = useRef(false)

  useEffect(() => () => { generation.current += 1 }, [])

  const allocations = useQuery({
    queryKey: ['inventory-label-allocations', namespace, companyId, labelId, routeEntry],
    retry: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchLabelAllocations(companyId, labelId, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })

  const send = useCallback(async (pending: Pending) => {
    if (inFlight.current) return
    inFlight.current = true
    const current = ++generation.current
    setReleaseArrival(false)
    setAttempt({ phase: 'submitting', pending })
    const security = boundary.capture()
    const context = { companyId, idempotencyKey: pending.idempotencyKey }
    const outcome: AllocationChangeOutcome | AllocationReleaseOutcome = pending.kind === 'change'
      ? await changeAllocation(pending.allocationId, pending.payload, context)
      : await releaseAllocation(pending.allocationId, pending.payload, context)
    if (current !== generation.current) return
    inFlight.current = false
    if (outcome.type === 'unknown') {
      setAttempt({ phase: 'unknown', pending })
      return
    }
    if (outcome.type === 'failed') {
      if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, security)
      setAttempt({ phase: 'failed', message: pending.kind === 'change'
        ? allocationChangeRefusalText(outcome.status, outcome.code)
        : allocationReleaseRefusalText(outcome.status, outcome.code) })
      void allocations.refetch()
      return
    }
    setAttempt({ phase: 'done', message: pending.kind === 'change'
      ? completionText(outcome as Extract<AllocationChangeOutcome, { type: 'applied' }>)
      : releaseCompletionText(outcome as Extract<AllocationReleaseOutcome, { type: 'applied' }>) })
    setChosenDraft(null)
    void allocations.refetch()
    onChanged()
  }, [allocations, boundary, companyId, onChanged])

  function abandon() {
    generation.current += 1
    inFlight.current = false
    setAttempt({ phase: 'idle' })
    void allocations.refetch()
    onChanged()
  }

  const frozen = attempt.phase === 'submitting' || attempt.phase === 'unknown'
  const data = allocations.data?.type === 'ok-single' ? allocations.data.data : null

  const activeItems = data?.items.filter((item) => item.allocation.status === 'Active') ?? []
  const onlyActiveId = activeItems.length === 1 ? activeItems[0].allocation.allocationId : null
  // On arrival from the ST-01 release action, until the user chooses anything: with exactly one active allocation its
  // release form is open; with several the user picks one below; with none the panel says so. Nothing is sent until
  // the user confirms with a reason.
  const draft: Draft | null = chosenDraft ??
    (releaseArrival && onlyActiveId !== null ? { mode: 'release', allocationId: onlyActiveId, reason: '' } : null)

  function setDraft(next: Draft | null) {
    setReleaseArrival(false)
    setChosenDraft(next)
  }

  function open(item: LabelAllocationItem) {
    if (frozen) return
    setFormError(null)
    setAttempt({ phase: 'idle' })
    setDraft({
      mode: 'change', allocationId: item.allocation.allocationId, quantityKg: '', moveTo: false,
      targetLabelId: '', targetLabelVersion: '', reason: '',
    })
  }

  function openRelease(item: LabelAllocationItem) {
    if (frozen) return
    setFormError(null)
    setAttempt({ phase: 'idle' })
    setDraft({ mode: 'release', allocationId: item.allocation.allocationId, reason: '' })
  }

  function submitRelease(event: FormEvent, item: LabelAllocationItem) {
    event.preventDefault()
    if (frozen || draft === null || draft.mode !== 'release') return
    const reason = draft.reason.trim()
    if (!isReason(reason)) {
      setFormError(`Serbest bırakma gerekçesi zorunludur (en fazla ${REASON_MAX_LENGTH} karakter).`)
      return
    }
    setFormError(null)
    void send({
      kind: 'release',
      allocationId: item.allocation.allocationId,
      idempotencyKey: newId(),
      payload: { expectedAllocationVersion: item.allocation.version, reason },
    })
  }

  function submit(event: FormEvent, item: LabelAllocationItem) {
    event.preventDefault()
    if (frozen || draft === null || draft.mode !== 'change' || data === null) return
    const parsed = parseQuantityInput(draft.quantityKg)
    const quantity = parsed.ok && isPositiveKilogramText(parsed.value) ? parsed.value : null
    if (quantity === null) {
      setFormError('Yeni tahsis miktarı zorunludur: 0\'dan büyük, en fazla 3 ondalıklı kg. Ondalık için virgül kullanın (örn. 97,5).')
      return
    }
    const target = draft.moveTo ? draft.targetLabelId.trim() : labelId
    const targetVersion = draft.moveTo ? draft.targetLabelVersion.trim() : data.labelVersion
    if (draft.moveTo && (!isGuid(target) || target.toLowerCase() === labelId.toLowerCase() || !/^[1-9]\d*$/.test(targetVersion))) {
      setFormError('Hedef etiket kimliği (bu etiketten farklı) ve hedef etiket sürümü zorunludur.')
      return
    }
    const reason = draft.reason.trim()
    if (!isReason(reason)) {
      setFormError(`Değişiklik gerekçesi zorunludur (en fazla ${REASON_MAX_LENGTH} karakter).`)
      return
    }
    if (!draft.moveTo && quantity === item.allocation.remainingKg) {
      setFormError('Değişiklik yok: miktar şu anki tahsisle aynı.')
      return
    }
    setFormError(null)
    void send({
      kind: 'change',
      allocationId: item.allocation.allocationId,
      idempotencyKey: newId(),
      payload: {
        expectedAllocationVersion: item.allocation.version,
        salesOrderId: item.allocation.demandId,
        salesOrderLineId: item.allocation.demandLineId,
        targetLabelId: target,
        expectedTargetLabelVersion: targetVersion,
        quantityKg: quantity,
        reason,
        newAllocationId: draft.moveTo ? newId() : null,
      },
    })
  }

  return <section aria-labelledby="label-allocation-heading">
    <h2 id="label-allocation-heading">Satış tahsisleri</h2>
    <p>Tahsis stok hareketi değildir. Revize, aynı tahsisin miktarını değiştirir ya da tahsisi aynı ürünün başka bir etiketine taşır; serbest bırakma kalan ayrılmış miktarın tamamını etiketin serbest stokuna döndürür. Geçmiş silinmez. Serbest miktar ve sipariş satırının onayı sunucuda yeniden doğrulanır.</p>

    {attempt.phase === 'submitting' ? <p role="status">Kaydediliyor…</p> : null}
    {attempt.phase === 'unknown'
      ? <div className={styles.warningCard}>
        <p role="alert">{attempt.pending.kind === 'change' ? UNKNOWN_TEXT : RELEASE_UNKNOWN_TEXT}</p>
        <p>{ABANDON_NOTE}</p>
        <div className={styles.actions}>
          <button type="button" onClick={() => void send(attempt.pending)}>Aynı anahtarla yeniden gönder</button>
          <button type="button" onClick={abandon}>Denemeyi bırak</button>
        </div>
      </div>
      : null}
    {attempt.phase === 'failed' ? <p role="alert" className={styles.error}>{attempt.message}</p> : null}
    {attempt.phase === 'done' ? <p role="status" className={styles.successCard}>{attempt.message}</p> : null}

    {allocations.isPending ? <p role="status">Tahsisler yükleniyor…</p> : null}
    {allocations.data !== undefined && allocations.data.type !== 'ok-single'
      ? <p role="alert">Tahsisler okunamadı.{' '}<button type="button" onClick={() => void allocations.refetch()}>Yeniden dene</button></p>
      : null}
    {data !== null && data.items.length === 0 ? <p>Bu etiket için satış tahsisi yok.</p> : null}
    {releaseArrival && data !== null && data.items.length > 0 && activeItems.length === 0
      ? <p role="status">Bu etikette serbest bırakılacak aktif tahsis yok.</p>
      : null}
    {releaseArrival && activeItems.length > 1 && draft === null
      ? <p role="status">Serbest bırakılacak tahsisi aşağıdan seçin.</p>
      : null}
    {data !== null && data.items.length > 0
      ? <div className={styles.tableWrap}><table>
        <thead><tr><th>Tahsis</th><th>Sipariş satırı</th><th>Durum</th><th>Tahsisli / kalan</th><th>Gerekçe</th><th>Geçmiş</th></tr></thead>
        <tbody>{data.items.map((item) => <tr key={item.allocation.allocationId} data-testid={`inventory-allocation-${item.allocation.allocationId}`}>
          <td className={styles.mono}>{short(item.allocation.allocationId)}<br />sürüm {item.allocation.version}</td>
          <td className={styles.mono}>{canReadSalesOrder && item.allocation.demandKind === 'sales.order-line' &&
            item.allocation.companyId === companyId && item.allocation.labelId === labelId && isGuid(item.allocation.demandId)
              ? <Link to={salesDetailPath(item.allocation.demandId)} aria-label="Satış siparişini aç">{short(item.allocation.demandId)}</Link>
              : short(item.allocation.demandId)} / {short(item.allocation.demandLineId)}</td>
          <td>{ALLOCATION_STATUS_TEXT[item.allocation.status]}</td>
          <td>{item.allocation.allocatedKg} kg / {item.allocation.remainingKg} kg</td>
          <td>{item.allocation.reason}{item.releaseReason === null ? null : <><br />Serbest bırakma: {item.releaseReason}</>}</td>
          <td>
            {item.changes.length === 0
              ? '—'
              : <ul>{item.changes.map((entry) => <li key={entry.changeId}>{entry.changedAtUtc}: {changeText(entry, item.allocation.allocationId)} — {entry.reason}</li>)}</ul>}
            {item.allocation.status === 'Active' && draft?.allocationId !== item.allocation.allocationId
              ? <div className={styles.actions}>
                <button type="button" disabled={frozen} onClick={() => open(item)}>Tahsisi revize et</button>
                <button type="button" disabled={frozen} onClick={() => openRelease(item)}>Tahsisi serbest bırak</button>
              </div>
              : null}
            {item.allocation.status === 'Active' && draft?.mode === 'release' && draft.allocationId === item.allocation.allocationId
              ? <form className={styles.form} onSubmit={(event) => submitRelease(event, item)} noValidate>
                <fieldset disabled={frozen}>
                  <legend>Tahsisi serbest bırak</legend>
                  <p>Kalan {item.allocation.remainingKg} kg ayrılmış miktarın tamamı serbest bırakılır; tahsis kaydı silinmez.</p>
                  <div className={styles.grid}>
                    <label htmlFor={`allocation-release-reason-${item.allocation.allocationId}`}>Serbest bırakma gerekçesi</label>
                    <textarea id={`allocation-release-reason-${item.allocation.allocationId}`} value={draft.reason} maxLength={REASON_MAX_LENGTH}
                      onChange={(event) => setDraft({ ...draft, reason: event.target.value })} />
                  </div>
                </fieldset>
                {formError === null ? null : <p role="alert" className={styles.error}>{formError}</p>}
                <div className={styles.actions}>
                  <button type="submit" disabled={frozen}>Serbest bırakmayı onayla</button>
                  <button type="button" className={styles.secondary} disabled={frozen} onClick={() => { setDraft(null); setFormError(null) }}>Vazgeç</button>
                </div>
              </form>
              : null}
            {item.allocation.status === 'Active' && draft?.mode === 'change' && draft.allocationId === item.allocation.allocationId
              ? <form className={styles.form} onSubmit={(event) => submit(event, item)} noValidate>
                <fieldset disabled={frozen}>
                  <legend>Tahsisi revize et</legend>
                  <div className={styles.grid}>
                    <label htmlFor={`allocation-change-kg-${item.allocation.allocationId}`}>Yeni tahsis kg</label>
                    <input id={`allocation-change-kg-${item.allocation.allocationId}`} inputMode="decimal" value={draft.quantityKg}
                      onChange={(event) => setDraft({ ...draft, quantityKg: event.target.value })} />
                    <label htmlFor={`allocation-change-move-${item.allocation.allocationId}`}>Başka etikete taşı</label>
                    <input id={`allocation-change-move-${item.allocation.allocationId}`} type="checkbox" checked={draft.moveTo}
                      onChange={(event) => setDraft({ ...draft, moveTo: event.target.checked })} />
                    {draft.moveTo
                      ? <>
                        <label htmlFor={`allocation-change-target-${item.allocation.allocationId}`}>Hedef etiket kimliği</label>
                        <input id={`allocation-change-target-${item.allocation.allocationId}`} value={draft.targetLabelId}
                          onChange={(event) => setDraft({ ...draft, targetLabelId: event.target.value })} />
                        <label htmlFor={`allocation-change-target-version-${item.allocation.allocationId}`}>Hedef etiket sürümü</label>
                        <input id={`allocation-change-target-version-${item.allocation.allocationId}`} inputMode="numeric" value={draft.targetLabelVersion}
                          onChange={(event) => setDraft({ ...draft, targetLabelVersion: event.target.value })} />
                      </>
                      : null}
                    <label htmlFor={`allocation-change-reason-${item.allocation.allocationId}`}>Değişiklik gerekçesi</label>
                    <textarea id={`allocation-change-reason-${item.allocation.allocationId}`} value={draft.reason} maxLength={REASON_MAX_LENGTH}
                      onChange={(event) => setDraft({ ...draft, reason: event.target.value })} />
                  </div>
                </fieldset>
                {formError === null ? null : <p role="alert" className={styles.error}>{formError}</p>}
                <div className={styles.actions}>
                  <button type="submit" disabled={frozen}>Tahsisi değiştir</button>
                  <button type="button" className={styles.secondary} disabled={frozen} onClick={() => { setDraft(null); setFormError(null) }}>Vazgeç</button>
                </div>
              </form>
              : null}
          </td>
        </tr>)}</tbody>
      </table></div>
      : null}
    {data?.windowFull === true ? <p>Daha eski tahsisler bu görünümde yer almıyor.</p> : null}
  </section>
}
