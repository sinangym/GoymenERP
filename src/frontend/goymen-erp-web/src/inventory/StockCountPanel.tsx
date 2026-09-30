import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { parseQuantityInput } from '../sales/wirePrimitives'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { decideStockCount, fetchLabelStockCounts, recordStockCount, type StockCountCommandOutcome } from './stockCountApi'
import {
  differenceKilograms,
  isCountedKilogramText,
  isReason,
  REASON_MAX_LENGTH,
  STOCK_COUNT_STATUS_TEXT,
  stockCountRefusalText,
  type StockCountDecisionKind,
  type StockCountDecisionRequest,
  type StockCountRequest,
  type StockCountView,
} from './stockCountContract'
import { recallRecordedByViewer, recordedByViewerKey, rememberRecordedByViewer } from './recordedByViewerStore'
import styles from './inventory.module.css'

/** The label as the detail read answered it; a count needs exactly one stored position with stock. */
export interface CountableLabel {
  readonly labelId: string
  readonly labelVersion: string
  readonly positions: readonly { readonly block: string; readonly locationId: string; readonly quantityKg: string }[]
}

/** One command whose outcome is not known yet: resent only with the same key and payload. */
type Pending =
  | { readonly kind: 'record'; readonly idempotencyKey: string; readonly payload: StockCountRequest }
  | { readonly kind: 'decide'; readonly idempotencyKey: string; readonly countId: string; readonly payload: StockCountDecisionRequest }

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly pending: Pending }
  | { readonly phase: 'unknown'; readonly pending: Pending }
  | { readonly phase: 'failed'; readonly message: string }
  | { readonly phase: 'done'; readonly message: string }

const UNKNOWN_TEXT = 'Sayım işleminin sonucu bilinmiyor. Aynı anahtar ve içerikle yeniden gönderin; yeni bir işlem başlatmayın.'
const ABANDON_NOTE = 'Bırakırsanız sayımlar yeniden okunur. Önceki gönderim kaydedildiyse listede görünür; aynı sayım veya karar ikinci kez stoğa işlenmez.'

function newId(): string { return globalThis.crypto.randomUUID() }

/** A counted amount may be zero (the piece is gone); "1.250" is ambiguous and refused, as everywhere else. */
function countedKilograms(raw: string): string | null {
  const parsed = parseQuantityInput(raw)
  if (!parsed.ok) return parsed.error === 'not-positive' ? '0' : null
  return isCountedKilogramText(parsed.value) ? parsed.value : null
}

function completionText(outcome: Extract<StockCountCommandOutcome, { type: 'applied' }>, pending: Pending): string {
  const view = outcome.response
  const replay = outcome.replayed ? ' (önceki kayıtlı sonuç yeniden gösterildi)' : ''
  if (pending.kind === 'record') {
    return view.status === 'NoDifference'
      ? `Sayım kaydedildi: fark yok, stok değişmedi${replay}.`
      : `Sayım kaydedildi: fark ${view.differenceKg} kg, başka bir yetkilinin onayını bekliyor; stok henüz değişmedi${replay}.`
  }
  return view.status === 'Approved'
    ? `Sayım farkı onaylandı ve stoğa işlendi: ${view.differenceKg} kg${replay}.`
    : `Sayım farkı reddedildi; stok değişmedi${replay}.`
}

export function StockCountPanel({ label, companyId, namespace, routeEntry, actorId, canRecord, canApprove, onChanged }: {
  readonly label: CountableLabel
  readonly companyId: string
  /** The shell context namespace and route entry the label detail read is keyed by: a reopen never shows another entry's list. */
  readonly namespace: string
  readonly routeEntry: number
  readonly actorId: string | null
  readonly canRecord: boolean
  readonly canApprove: boolean
  readonly onChanged: () => void
}) {
  const boundary = useAuthBoundary()
  const [attempt, setAttempt] = useState<Attempt>({ phase: 'idle' })
  const [countedKg, setCountedKg] = useState('')
  const [reason, setReason] = useState('')
  const [notes, setNotes] = useState<Readonly<Record<string, string>>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const generation = useRef(0)
  const inFlight = useRef(false)
  const mineKey = recordedByViewerKey('stock-count', actorId, companyId, label.labelId)
  const [mine, setMine] = useState(() => recallRecordedByViewer(mineKey))
  const remember = useCallback((id: string) => {
    setMine((current) => rememberRecordedByViewer(mineKey, id, current))
  }, [mineKey])

  useEffect(() => () => { generation.current += 1 }, [])

  const counts = useQuery({
    queryKey: ['inventory-label-stock-counts', namespace, label.labelId, routeEntry],
    retry: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchLabelStockCounts(companyId, label.labelId, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })

  const send = useCallback(async (pending: Pending) => {
    if (inFlight.current) return
    inFlight.current = true
    const current = ++generation.current
    setAttempt({ phase: 'submitting', pending })
    // The id is the viewer's own from the moment it is sent: even an unknown outcome may have recorded it.
    if (pending.kind === 'record') remember(pending.payload.countId)
    const security = boundary.capture()
    const context = { companyId, idempotencyKey: pending.idempotencyKey }
    const outcome = pending.kind === 'record'
      ? await recordStockCount(pending.payload, context)
      : await decideStockCount(pending.countId, pending.payload, context)
    if (current !== generation.current) return
    inFlight.current = false
    if (outcome.type === 'unknown') {
      setAttempt({ phase: 'unknown', pending })
      return
    }
    if (outcome.type === 'failed') {
      if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, security)
      // The server says the viewer recorded it: stop offering a decision the next click would meet again.
      if (pending.kind === 'decide' && outcome.status === 409 && outcome.code === 'inventory_stock_count_self_approval') remember(pending.countId)
      setAttempt({ phase: 'failed', message: stockCountRefusalText(outcome.status, outcome.code) })
      void counts.refetch()
      return
    }
    setAttempt({ phase: 'done', message: completionText(outcome, pending) })
    if (pending.kind === 'record') {
      setCountedKg('')
      setReason('')
    }
    void counts.refetch()
    onChanged()
  }, [boundary, companyId, counts, onChanged, remember])

  function abandon() {
    generation.current += 1
    inFlight.current = false
    setAttempt({ phase: 'idle' })
    void counts.refetch()
    onChanged()
  }

  const frozen = attempt.phase === 'submitting' || attempt.phase === 'unknown'
  const position = label.positions.length === 1 ? label.positions[0] : null
  const counted = countedKilograms(countedKg)
  const preview = position !== null && counted !== null ? differenceKilograms(position.quantityKg, counted) : null

  function submitCount(event: FormEvent) {
    event.preventDefault()
    if (frozen || position === null) return
    const cleanReason = reason.trim()
    if (counted === null) {
      setFormError('Sayılan miktar zorunludur: 0 veya en fazla 3 ondalıklı kg. Ondalık için virgül kullanın (örn. 97,5 veya 1,250).')
      return
    }
    if (!isReason(cleanReason)) {
      setFormError(`Fark gerekçesi zorunludur (en fazla ${REASON_MAX_LENGTH} karakter).`)
      return
    }
    setFormError(null)
    void send({
      kind: 'record',
      idempotencyKey: newId(),
      payload: {
        countId: newId(),
        labelId: label.labelId,
        expectedLabelVersion: label.labelVersion,
        countedKg: counted,
        countedAtUtc: new Date().toISOString(),
        reason: cleanReason,
      },
    })
  }

  function decide(item: StockCountView, decision: StockCountDecisionKind) {
    if (frozen) return
    const note = (notes[item.countId] ?? '').trim()
    if (!isReason(note)) {
      setFormError(`Karar notu zorunludur (en fazla ${REASON_MAX_LENGTH} karakter).`)
      return
    }
    setFormError(null)
    void send({ kind: 'decide', idempotencyKey: newId(), countId: item.countId, payload: { decision, reason: note } })
  }

  const heading = <h2 id="stock-count-heading">Sayım ve fark (ST-05)</h2>
  const data = counts.data?.type === 'ok-single' ? counts.data.data : null

  return <section aria-labelledby="stock-count-heading">
    {heading}
    <p>Sayım stoğu değiştirmez. Fark (sayılan − defter) gerekçeyle kaydedilir ve ancak sayımı girenden başka bir yetkili onaylayınca stoğa işlenir; geçmiş silinmez.</p>

    {attempt.phase === 'submitting' ? <p role="status">Kaydediliyor…</p> : null}
    {attempt.phase === 'unknown'
      ? <div className={styles.warningCard}>
        <p role="alert">{UNKNOWN_TEXT}</p>
        <p>{ABANDON_NOTE}</p>
        <div className={styles.actions}>
          <button type="button" onClick={() => void send(attempt.pending)}>Aynı anahtarla yeniden gönder</button>
          <button type="button" onClick={abandon}>Denemeyi bırak</button>
        </div>
      </div>
      : null}
    {attempt.phase === 'failed' ? <p role="alert" className={styles.error}>{attempt.message}</p> : null}
    {attempt.phase === 'done' ? <p role="status" className={styles.successCard}>{attempt.message}</p> : null}
    {formError === null ? null : <p role="alert" className={styles.error}>{formError}</p>}

    {canRecord
      ? position === null
        ? <p>Sayım için etiketin tek bir fiziksel pozisyonda stoğu olmalı.</p>
        : <form className={styles.form} onSubmit={submitCount} noValidate>
          <fieldset disabled={frozen}>
            <legend>Yeni sayım</legend>
            <p>Defter: {position.quantityKg} kg · {position.block} · lokasyon <span className={styles.mono}>…{position.locationId.slice(-8)}</span></p>
            <div className={styles.grid}>
              <label htmlFor="stock-count-kg">Sayılan kg</label>
              <input id="stock-count-kg" inputMode="decimal" value={countedKg} onChange={(event) => setCountedKg(event.target.value)} />
              <label htmlFor="stock-count-reason">Fark gerekçesi</label>
              <textarea id="stock-count-reason" value={reason} maxLength={REASON_MAX_LENGTH} onChange={(event) => setReason(event.target.value)} />
            </div>
            <p role="status">Fark: {preview === null ? '—' : `${preview} kg`}</p>
          </fieldset>
          <div className={styles.actions}>
            <button type="submit" disabled={frozen}>Sayımı kaydet</button>
          </div>
        </form>
      : null}

    <h3>Sayım geçmişi</h3>
    {counts.isPending ? <p role="status">Sayımlar yükleniyor…</p> : null}
    {counts.data !== undefined && counts.data.type !== 'ok-single'
      ? <p role="alert">Sayımlar okunamadı.{' '}<button type="button" onClick={() => void counts.refetch()}>Yeniden dene</button></p>
      : null}
    {data !== null && data.items.length === 0 ? <p>Bu etiket için sayım yok.</p> : null}
    {data !== null && data.items.length > 0
      ? <div className={styles.tableWrap}><table>
        <thead><tr><th>Sayım</th><th>Defter</th><th>Sayılan</th><th>Fark</th><th>Gerekçe</th><th>Durum</th></tr></thead>
        <tbody>{data.items.map((item) => <tr key={item.countId}>
          <td>{item.countedAtUtc}<br /><span className={styles.mono}>…{item.countId.slice(-8)}</span></td>
          <td>{item.bookKg} kg<br />sürüm {item.labelVersion}</td>
          <td>{item.countedKg} kg</td>
          <td>{item.differenceKg} kg</td>
          <td>{item.reason}</td>
          <td>
            <strong>{STOCK_COUNT_STATUS_TEXT[item.status]}</strong>
            {item.decision === null ? null : <><br />{item.decision.decidedAtUtc}: {item.decision.reason}</>}
            {canApprove && item.status === 'PendingApproval' && mine.has(item.countId)
              ? <p>Bu sayımı siz kaydettiniz; farkı başka bir yetkili onaylar veya reddeder.</p>
              : null}
            {canApprove && item.status === 'PendingApproval' && !mine.has(item.countId)
              ? <div className={styles.grid}>
                <label htmlFor={`stock-count-note-${item.countId}`}>Karar notu</label>
                <input id={`stock-count-note-${item.countId}`} value={notes[item.countId] ?? ''} maxLength={REASON_MAX_LENGTH} disabled={frozen}
                  onChange={(event) => setNotes((current) => ({ ...current, [item.countId]: event.target.value }))} />
                <div className={styles.actions}>
                  <button type="button" disabled={frozen} onClick={() => decide(item, 'Approve')}>Farkı onayla</button>
                  <button type="button" disabled={frozen} className={styles.secondary} onClick={() => decide(item, 'Reject')}>Reddet</button>
                </div>
              </div>
              : null}
          </td>
        </tr>)}</tbody>
      </table></div>
      : null}
    {data?.windowFull === true ? <p>Daha eski sayımlar bu görünümde yer almıyor.</p> : null}
  </section>
}
