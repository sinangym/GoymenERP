import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { parseQuantityInput } from '../sales/wirePrimitives'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { CountableLabel } from './StockCountPanel'
import { decideStockScrap, fetchLabelStockScraps, recordStockScrap, type StockScrapCommandOutcome } from './stockScrapApi'
import {
  isReason,
  isScrapKilogramText,
  REASON_MAX_LENGTH,
  remainingKilograms,
  STOCK_SCRAP_STATUS_TEXT,
  stockScrapRefusalText,
  type StockScrapDecisionKind,
  type StockScrapDecisionRequest,
  type StockScrapRequest,
  type StockScrapView,
} from './stockScrapContract'
import styles from './inventory.module.css'

/** One command whose outcome is not known yet: resent only with the same key and payload. */
type Pending =
  | { readonly kind: 'record'; readonly idempotencyKey: string; readonly payload: StockScrapRequest }
  | { readonly kind: 'decide'; readonly idempotencyKey: string; readonly scrapId: string; readonly payload: StockScrapDecisionRequest }

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly pending: Pending }
  | { readonly phase: 'unknown'; readonly pending: Pending }
  | { readonly phase: 'failed'; readonly message: string }
  | { readonly phase: 'done'; readonly message: string }

const UNKNOWN_TEXT = 'Hurda işleminin sonucu bilinmiyor. Aynı anahtar ve içerikle yeniden gönderin; yeni bir işlem başlatmayın.'
const ABANDON_NOTE = 'Bırakırsanız hurda kayıtları yeniden okunur. Önceki gönderim kaydedildiyse listede görünür; aynı hurda veya karar ikinci kez stoktan düşülmez.'

function newId(): string { return globalThis.crypto.randomUUID() }

/** A scrap amount is positive; "1.250" is ambiguous and refused, as everywhere else. */
function scrapKilograms(raw: string): string | null {
  const parsed = parseQuantityInput(raw)
  return parsed.ok && isScrapKilogramText(parsed.value) ? parsed.value : null
}

function completionText(outcome: Extract<StockScrapCommandOutcome, { type: 'applied' }>, pending: Pending): string {
  const view = outcome.response
  const replay = outcome.replayed ? ' (önceki kayıtlı sonuç yeniden gösterildi)' : ''
  if (pending.kind === 'record') {
    return `Hurda kaydedildi: ${view.scrapKg} kg, başka bir yetkilinin onayını bekliyor; stok henüz değişmedi${replay}.`
  }
  return view.status === 'Approved'
    ? `Hurda onaylandı ve stoktan düşüldü: ${view.scrapKg} kg; etikette ${view.remainingKg} kg kaldı${replay}.`
    : `Hurda reddedildi; stok değişmedi${replay}.`
}

export function StockScrapPanel({ label, companyId, canRecord, canApprove, onChanged }: {
  readonly label: CountableLabel
  readonly companyId: string
  readonly canRecord: boolean
  readonly canApprove: boolean
  readonly onChanged: () => void
}) {
  const boundary = useAuthBoundary()
  const [attempt, setAttempt] = useState<Attempt>({ phase: 'idle' })
  const [scrapKg, setScrapKg] = useState('')
  const [reason, setReason] = useState('')
  const [notes, setNotes] = useState<Readonly<Record<string, string>>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const generation = useRef(0)
  const inFlight = useRef(false)

  useEffect(() => () => { generation.current += 1 }, [])

  const scraps = useQuery({
    queryKey: ['inventory-label-stock-scraps', companyId, label.labelId],
    retry: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchLabelStockScraps(companyId, label.labelId, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })

  const send = useCallback(async (pending: Pending) => {
    if (inFlight.current) return
    inFlight.current = true
    const current = ++generation.current
    setAttempt({ phase: 'submitting', pending })
    const security = boundary.capture()
    const context = { companyId, idempotencyKey: pending.idempotencyKey }
    const outcome = pending.kind === 'record'
      ? await recordStockScrap(pending.payload, context)
      : await decideStockScrap(pending.scrapId, pending.payload, context)
    if (current !== generation.current) return
    inFlight.current = false
    if (outcome.type === 'unknown') {
      setAttempt({ phase: 'unknown', pending })
      return
    }
    if (outcome.type === 'failed') {
      if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, security)
      setAttempt({ phase: 'failed', message: stockScrapRefusalText(outcome.status, outcome.code) })
      void scraps.refetch()
      return
    }
    setAttempt({ phase: 'done', message: completionText(outcome, pending) })
    if (pending.kind === 'record') {
      setScrapKg('')
      setReason('')
    }
    void scraps.refetch()
    onChanged()
  }, [boundary, companyId, scraps, onChanged])

  function abandon() {
    generation.current += 1
    inFlight.current = false
    setAttempt({ phase: 'idle' })
    void scraps.refetch()
    onChanged()
  }

  const frozen = attempt.phase === 'submitting' || attempt.phase === 'unknown'
  const position = label.positions.length === 1 ? label.positions[0] : null
  const scrap = scrapKilograms(scrapKg)
  const preview = position !== null && scrap !== null ? remainingKilograms(position.quantityKg, scrap) : null

  function submitScrap(event: FormEvent) {
    event.preventDefault()
    if (frozen || position === null) return
    const cleanReason = reason.trim()
    if (scrap === null) {
      setFormError('Hurda miktarı zorunludur: sıfırdan büyük, en fazla 3 ondalıklı kg. Ondalık için virgül kullanın (örn. 2,5 veya 1,250).')
      return
    }
    if (remainingKilograms(position.quantityKg, scrap) === null) {
      setFormError(`Hurda miktarı etiketin defter miktarından (${position.quantityKg} kg) fazla olamaz.`)
      return
    }
    if (!isReason(cleanReason)) {
      setFormError(`Hurda gerekçesi zorunludur (en fazla ${REASON_MAX_LENGTH} karakter).`)
      return
    }
    setFormError(null)
    void send({
      kind: 'record',
      idempotencyKey: newId(),
      payload: {
        scrapId: newId(),
        labelId: label.labelId,
        expectedLabelVersion: label.labelVersion,
        scrapKg: scrap,
        scrappedAtUtc: new Date().toISOString(),
        reason: cleanReason,
      },
    })
  }

  function decide(item: StockScrapView, decision: StockScrapDecisionKind) {
    if (frozen) return
    const note = (notes[item.scrapId] ?? '').trim()
    if (!isReason(note)) {
      setFormError(`Karar notu zorunludur (en fazla ${REASON_MAX_LENGTH} karakter).`)
      return
    }
    setFormError(null)
    void send({ kind: 'decide', idempotencyKey: newId(), scrapId: item.scrapId, payload: { decision, reason: note } })
  }

  const data = scraps.data?.type === 'ok-single' ? scraps.data.data : null

  return <section aria-labelledby="stock-scrap-heading">
    <h2 id="stock-scrap-heading">Hurda / fire çıkışı</h2>
    <p>Hurda kaydı stoğu hemen değiştirmez. Hurdaya ayrılan kg gerekçeyle kaydedilir ve ancak kaydı girenden başka bir yetkili onaylayınca tek bir hurda hareketiyle stoktan düşülür; geçmiş silinmez. Aktif tahsislerin altına inen hurda kabul edilmez.</p>

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
        ? <p>Hurda için etiketin tek bir fiziksel pozisyonda stoğu olmalı.</p>
        : <form className={styles.form} onSubmit={submitScrap} noValidate>
          <fieldset disabled={frozen}>
            <legend>Yeni hurda kaydı</legend>
            <p>Defter: {position.quantityKg} kg · {position.block} · lokasyon <span className={styles.mono}>…{position.locationId.slice(-8)}</span></p>
            <div className={styles.grid}>
              <label htmlFor="stock-scrap-kg">Hurda kg</label>
              <input id="stock-scrap-kg" inputMode="decimal" value={scrapKg} onChange={(event) => setScrapKg(event.target.value)} />
              <label htmlFor="stock-scrap-reason">Hurda gerekçesi</label>
              <textarea id="stock-scrap-reason" value={reason} maxLength={REASON_MAX_LENGTH} onChange={(event) => setReason(event.target.value)} />
            </div>
            <p role="status">Onaydan sonra kalan: {preview === null ? '—' : `${preview} kg`}</p>
          </fieldset>
          <div className={styles.actions}>
            <button type="submit" disabled={frozen}>Hurdayı kaydet</button>
          </div>
        </form>
      : null}

    <h3>Hurda geçmişi</h3>
    {scraps.isPending ? <p role="status">Hurda kayıtları yükleniyor…</p> : null}
    {scraps.data !== undefined && scraps.data.type !== 'ok-single'
      ? <p role="alert">Hurda kayıtları okunamadı.{' '}<button type="button" onClick={() => void scraps.refetch()}>Yeniden dene</button></p>
      : null}
    {data !== null && data.items.length === 0 ? <p>Bu etiket için hurda kaydı yok.</p> : null}
    {data !== null && data.items.length > 0
      ? <div className={styles.tableWrap}><table>
        <thead><tr><th>Hurda</th><th>Defter</th><th>Hurda kg</th><th>Kalan</th><th>Gerekçe</th><th>Durum</th></tr></thead>
        <tbody>{data.items.map((item) => <tr key={item.scrapId}>
          <td>{item.scrappedAtUtc}<br /><span className={styles.mono}>…{item.scrapId.slice(-8)}</span></td>
          <td>{item.bookKg} kg<br />sürüm {item.labelVersion}</td>
          <td>{item.scrapKg} kg</td>
          <td>{item.remainingKg} kg</td>
          <td>{item.reason}</td>
          <td>
            <strong>{STOCK_SCRAP_STATUS_TEXT[item.status]}</strong>
            {item.decision === null ? null : <><br />{item.decision.decidedAtUtc}: {item.decision.reason}</>}
            {canApprove && item.status === 'PendingApproval'
              ? <div className={styles.grid}>
                <label htmlFor={`stock-scrap-note-${item.scrapId}`}>Karar notu</label>
                <input id={`stock-scrap-note-${item.scrapId}`} value={notes[item.scrapId] ?? ''} maxLength={REASON_MAX_LENGTH} disabled={frozen}
                  onChange={(event) => setNotes((current) => ({ ...current, [item.scrapId]: event.target.value }))} />
                <div className={styles.actions}>
                  <button type="button" disabled={frozen} onClick={() => decide(item, 'Approve')}>Hurdayı onayla</button>
                  <button type="button" disabled={frozen} className={styles.secondary} onClick={() => decide(item, 'Reject')}>Reddet</button>
                </div>
              </div>
              : null}
          </td>
        </tr>)}</tbody>
      </table></div>
      : null}
    {data?.windowFull === true ? <p>Daha eski hurda kayıtları bu görünümde yer almıyor.</p> : null}
  </section>
}
