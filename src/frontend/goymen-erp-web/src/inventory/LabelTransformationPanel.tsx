import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { fetchStockPositions } from './api'
import type { InventoryStockPosition, InventoryStockPositions } from './contract'
import { recordLabelTransformation, type RecordLabelTransformationOutcome } from './labelTransformationApi'
import {
  hasDuplicateLabelCodes,
  isKilogramText,
  isLabelCode,
  isReason,
  kilogramsToGrams,
  REASON_MAX_LENGTH,
  sameLabelId,
  sumKilograms,
  transformationRefusalText,
  type LabelTransformationKind,
  type LabelTransformationRequest,
} from './labelTransformationContract'
import { inventoryLabelDetailPath } from './labelDetailRoute'
import {
  pendingTransformationKey,
  recallPendingTransformation,
  releasePendingTransformation,
  reservePendingTransformation,
  type PendingLabelTransformation,
} from './pendingTransformationStore'
import { parseQuantityInput } from '../sales/wirePrimitives'
import { createRequestFence } from '../purchasing/requestFence'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './inventory.module.css'

/** The label as the detail read answered it; only a label with one physical position can be transformed. */
export interface TransformableLabel {
  readonly labelId: string
  readonly labelVersion: string
  readonly code: string
  readonly productId: string
  readonly lotId: string
  readonly heatNumber: string | null
  readonly coilNumber: string | null
  readonly physicalKg: string
  readonly positions: readonly {
    readonly ownerKind: string
    readonly ownerId: string
    readonly custodianKind: string
    readonly custodianId: string
    readonly locationId: string
    readonly block: string
    readonly quantityKg: string
  }[]
}

type OutputDraft = { readonly id: string; readonly code: string; readonly quantityKg: string }

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: PendingLabelTransformation }
  | { readonly phase: 'unknown'; readonly snapshot: PendingLabelTransformation; readonly reason: string }
  | { readonly phase: 'failed'; readonly message: string }
  | { readonly phase: 'completed'; readonly result: Extract<RecordLabelTransformationOutcome, { type: 'applied' }>; readonly codes: ReadonlyMap<string, string> }

type Candidates =
  | { readonly type: 'idle' | 'loading' }
  | { readonly type: 'ready'; readonly data: InventoryStockPositions }
  | { readonly type: 'failed'; readonly message: string }

const UNKNOWN_TEXT = 'Etiket dönüşümünün sonucu bilinmiyor. Aynı anahtar ve içerikle yeniden gönderin; yeni bir işlem başlatmayın.'
const ABANDON_NOTE = 'Bırakırsanız etiket yeniden okunur. Önceki gönderim kaydedildiyse kaynak etiket artık tüketilmiş görünür; kaydedilmediyse yeni bir işlem başlatabilirsiniz (eski sürüm tekrar kullanılamaz).'

function quantityError(index: number, raw: string): string {
  const parsed = parseQuantityInput(raw)
  if (!parsed.ok && parsed.error === 'ambiguous-separator') {
    return `Çıktı ${index + 1}: "${raw.trim()}" belirsiz. Ondalık için virgül kullanın (örn. 1,250) veya binlik ayırıcı yazmayın (örn. 1250).`
  }
  return `Çıktı ${index + 1}: etiket kodu ve en fazla 3 ondalıklı, sıfırdan büyük kg zorunludur.`
}

function newId(): string { return globalThis.crypto.randomUUID() }
function newOutput(): OutputDraft { return { id: newId(), code: '', quantityKg: '' } }

function parsedKilograms(raw: string): string | null {
  const parsed = parseQuantityInput(raw)
  return parsed.ok && isKilogramText(parsed.value) ? parsed.value : null
}

function candidateBlock(item: InventoryStockPosition): string | null {
  const free = kilogramsToGrams(item.freePhysicalKg)
  const blocked = kilogramsToGrams(item.blockedPhysicalKg)
  if (free > 0n && blocked === 0n) return 'Free'
  if (blocked > 0n && free === 0n) return 'Blocked'
  return null
}

/**
 * Why a stock row cannot be merged into this label, judged only from facts both reads show. The server
 * still decides (document requirements, versions, allocations at the moment of the save).
 */
function mergeRefusal(label: TransformableLabel, item: InventoryStockPosition,
  rows: readonly InventoryStockPosition[]): string | null {
  const position = label.positions[0]
  if (sameLabelId(item.labelId, label.labelId)) return 'Bu etiketin kendisi'
  if (rows.filter((row) => sameLabelId(row.labelId, item.labelId)).length > 1) return 'Birden fazla fiziksel pozisyonu var'
  if (item.productId !== label.productId || item.lot.id !== label.lotId ||
    item.heatNumber !== label.heatNumber || item.coilNumber !== label.coilNumber) return 'Farklı ürün, lot veya iz bilgisi'
  if (item.ownerKind !== position.ownerKind || item.ownerId !== position.ownerId ||
    item.custodianKind !== position.custodianKind || item.custodianId !== position.custodianId ||
    item.locationId !== position.locationId) return 'Farklı sahip, gözeten veya lokasyon'
  if (candidateBlock(item) !== position.block) return 'Farklı serbest/bloklu durumu'
  if (kilogramsToGrams(item.activeAllocationKg) > 0n) return 'Aktif tahsisi var'
  return null
}

export function LabelTransformationPanel({ label, companyId, actorId, sessionEpoch, canOpenLabels, onRecorded }: {
  readonly label: TransformableLabel
  readonly companyId: string
  readonly actorId: string
  readonly sessionEpoch: number
  readonly canOpenLabels: boolean
  readonly onRecorded: () => void
}) {
  const boundary = useAuthBoundary()
  const pendingKey = pendingTransformationKey(sessionEpoch, actorId, companyId, label.labelId)
  const [attempt, setAttempt] = useState<Attempt>(() => {
    const recalled = recallPendingTransformation(pendingKey)
    return recalled === null || recalled.actorId !== actorId || recalled.companyId !== companyId
      ? { phase: 'idle' }
      : { phase: 'unknown', snapshot: recalled, reason: UNKNOWN_TEXT }
  })
  const [kind, setKind] = useState<LabelTransformationKind>('Split')
  const [outputs, setOutputs] = useState<readonly OutputDraft[]>(() => [newOutput(), newOutput()])
  const [mergeCode, setMergeCode] = useState('')
  const [reason, setReason] = useState('')
  const [search, setSearch] = useState('')
  const [candidates, setCandidates] = useState<Candidates>({ type: 'idle' })
  const [selected, setSelected] = useState<readonly InventoryStockPosition[]>([])
  const [formError, setFormError] = useState<string | null>(null)
  const searchFence = useRef(createRequestFence())
  const commandGeneration = useRef(0)
  const inFlight = useRef(false)

  useEffect(() => {
    const fence = searchFence.current
    return () => {
      fence.invalidate()
      commandGeneration.current += 1
    }
  }, [])

  const readCandidates = useCallback(async () => {
    const token = searchFence.current.capture()
    const security = boundary.capture()
    setCandidates({ type: 'loading' })
    const outcome = await fetchStockPositions(companyId, search, token.signal)
    if (!searchFence.current.isCurrent(token)) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    setCandidates(outcome.type === 'ok-single'
      ? { type: 'ready', data: outcome.data }
      : { type: 'failed', message: outcome.type === 'http' ? `Stok kaynağı yanıt vermedi (${outcome.status}).` : 'Stok kaynağına ulaşılamadı veya yanıt doğrulanamadı.' })
  }, [boundary, companyId, search])

  const send = useCallback(async (snapshot: PendingLabelTransformation) => {
    if (snapshot.companyId !== companyId || snapshot.actorId !== actorId || inFlight.current) return
    inFlight.current = true
    const generation = ++commandGeneration.current
    reservePendingTransformation(pendingKey, snapshot)
    setAttempt({ phase: 'submitting', snapshot })
    const security = boundary.capture()
    const outcome = await recordLabelTransformation(snapshot.payload, { companyId, idempotencyKey: snapshot.idempotencyKey })
    // A definite answer (applied or refused) settles the remembered attempt even when this view is gone, so a
    // reopen does not ask for a same-key resend of a write that already committed or was refused.
    if (outcome.type !== 'unknown') releasePendingTransformation(pendingKey, snapshot.idempotencyKey)
    if (generation !== commandGeneration.current) return
    inFlight.current = false
    if (outcome.type === 'unknown') {
      setAttempt({ phase: 'unknown', snapshot, reason: UNKNOWN_TEXT })
      return
    }
    if (outcome.type === 'failed') {
      if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, security)
      setAttempt({ phase: 'failed', message: transformationRefusalText(outcome.status, outcome.code) })
      return
    }
    setAttempt({
      phase: 'completed',
      result: outcome,
      codes: new Map(snapshot.payload.outputs.map((output) => [output.labelId.toLowerCase(), output.code])),
    })
    onRecorded()
  }, [actorId, boundary, companyId, onRecorded, pendingKey])

  // Safe for split/merge: the attempt carried the source label version, so a later attempt can never repeat a
  // committed one (the writer refuses the stale version). The fresh reread shows which of the two happened.
  function abandon(snapshot: PendingLabelTransformation) {
    commandGeneration.current += 1
    inFlight.current = false
    releasePendingTransformation(pendingKey, snapshot.idempotencyKey)
    setAttempt({ phase: 'idle' })
    onRecorded()
  }

  if (attempt.phase === 'completed') {
    const view = attempt.result.response
    return <section className={styles.successCard} role="status" aria-labelledby="label-transformation-heading">
      <h2 id="label-transformation-heading">{view.kind === 'Split' ? 'Bölme kaydedildi' : 'Birleştirme kaydedildi'}</h2>
      {attempt.result.replayed ? <p>Önceki kayıtlı sonuç yeniden gösterildi.</p> : null}
      <p className={styles.mono}>İşlem …{view.transformationId.slice(-8)}</p>
      <ul>{view.outputLabelIds.map((id) => {
        const code = attempt.codes.get(id.toLowerCase()) ?? id
        return <li key={id}>{canOpenLabels ? <Link to={inventoryLabelDetailPath(id)}>{code}</Link> : code}</li>
      })}</ul>
    </section>
  }

  const frozen = attempt.phase === 'submitting' || attempt.phase === 'unknown'
  const position = label.positions.length === 1 ? label.positions[0] : null
  const heading = <h2 id="label-transformation-heading">Bölme / birleştirme</h2>

  if (attempt.phase === 'unknown' || attempt.phase === 'submitting') {
    return <section className={styles.warningCard} aria-labelledby="label-transformation-heading">
      {heading}
      {attempt.phase === 'submitting'
        ? <p role="status">Kaydediliyor…</p>
        : <><p role="alert">{attempt.reason}</p>
          <p>{ABANDON_NOTE}</p>
          <div className={styles.actions}>
            <button type="button" onClick={() => void send(attempt.snapshot)}>Aynı anahtarla yeniden gönder</button>
            <button type="button" onClick={() => abandon(attempt.snapshot)}>Denemeyi bırak</button>
          </div></>}
    </section>
  }

  if (position === null || kilogramsToGrams(label.physicalKg) === 0n) {
    return <section aria-labelledby="label-transformation-heading">
      {heading}
      <p>Bölme veya birleştirme için etiketin tek bir fiziksel pozisyonda stoğu olmalı.</p>
    </section>
  }

  const splitValues = outputs.map((output) => parsedKilograms(output.quantityKg))
  const splitTotal = splitValues.every((value): value is string => value !== null) ? sumKilograms(splitValues) : null
  const mergeTotal = sumKilograms([label.physicalKg, ...selected.map((item) => item.physicalKg)])

  function submit(event: FormEvent) {
    event.preventDefault()
    if (attempt.phase !== 'idle' && attempt.phase !== 'failed') return
    const cleanReason = reason.trim()
    if (!isReason(cleanReason)) {
      setFormError(`Gerekçe zorunludur (en fazla ${REASON_MAX_LENGTH} karakter).`)
      return
    }
    const source = { labelId: label.labelId, expectedLabelVersion: label.labelVersion }
    let payload: LabelTransformationRequest
    if (kind === 'Split') {
      const built: { labelId: string; code: string; quantityKg: string }[] = []
      for (const [index, output] of outputs.entries()) {
        const quantityKg = parsedKilograms(output.quantityKg)
        const code = output.code.trim()
        if (!isLabelCode(code) || quantityKg === null) {
          setFormError(quantityError(index, output.quantityKg))
          return
        }
        built.push({ labelId: newId(), code, quantityKg })
      }
      if (hasDuplicateLabelCodes(built.map((output) => output.code))) {
        setFormError('Çıktı etiket kodları birbirinden farklı olmalıdır (büyük/küçük harf farkı ayrı kod sayılmaz).')
        return
      }
      const total = sumKilograms(built.map((output) => output.quantityKg))
      if (kilogramsToGrams(total) !== kilogramsToGrams(label.physicalKg)) {
        setFormError(`Çıktıların toplamı (${total} kg) kaynak etiketin fiziksel miktarına (${label.physicalKg} kg) eşit olmalıdır.`)
        return
      }
      payload = { transformationId: newId(), kind, sources: [source], outputs: built, occurredAtUtc: new Date().toISOString(), reason: cleanReason }
    } else {
      const code = mergeCode.trim()
      if (selected.length === 0) {
        setFormError('Birleştirmek için en az bir etiket daha seçin.')
        return
      }
      if (!isLabelCode(code)) {
        setFormError('Birleşik paketin yeni etiket kodu zorunludur.')
        return
      }
      payload = {
        transformationId: newId(),
        kind,
        sources: [source, ...selected.map((item) => ({ labelId: item.labelId, expectedLabelVersion: item.labelVersion }))],
        outputs: [{ labelId: newId(), code, quantityKg: mergeTotal }],
        occurredAtUtc: new Date().toISOString(),
        reason: cleanReason,
      }
    }
    setFormError(null)
    void send({ actorId, companyId, originLabelId: label.labelId, idempotencyKey: newId(), payload })
  }

  const candidateItems = candidates.type === 'ready' ? candidates.data.items : []
  return <section aria-labelledby="label-transformation-heading">
    {heading}
    <p>Yeni etiketler kaynağın ürün, lot, iz bilgisi, sahip, gözeten ve lokasyonunu korur; kaynak etiketin tamamı tüketilir.</p>
    {attempt.phase === 'failed' ? <p role="alert" className={styles.error}>{attempt.message}</p> : null}
    {formError === null ? null : <p role="alert" className={styles.error}>{formError}</p>}
    <form className={styles.form} onSubmit={submit} noValidate>
      <fieldset disabled={frozen}>
        <legend>İşlem türü</legend>
        <div className={styles.actions}>
          <label><input type="radio" name="transformation-kind" checked={kind === 'Split'} onChange={() => setKind('Split')} /> Böl</label>
          <label><input type="radio" name="transformation-kind" checked={kind === 'Merge'} onChange={() => setKind('Merge')} /> Birleştir</label>
        </div>
      </fieldset>

      {kind === 'Split' ? <fieldset disabled={frozen}>
        <legend>Yeni etiketler</legend>
        {outputs.map((output, index) => <div className={styles.lineCard} key={output.id} aria-label={`Çıktı ${index + 1}`}>
          <div className={styles.grid}>
            <label htmlFor={`split-code-${output.id}`}>Çıktı {index + 1} etiket kodu</label>
            <input id={`split-code-${output.id}`} value={output.code} maxLength={64}
              onChange={(event) => setOutputs((current) => current.map((item) => item.id === output.id ? { ...item, code: event.target.value } : item))} />
            <label htmlFor={`split-kg-${output.id}`}>Çıktı {index + 1} kg</label>
            <input id={`split-kg-${output.id}`} inputMode="decimal" value={output.quantityKg}
              onChange={(event) => setOutputs((current) => current.map((item) => item.id === output.id ? { ...item, quantityKg: event.target.value } : item))} />
          </div>
          {outputs.length > 2 ? <button type="button" className={styles.secondary} onClick={() => setOutputs((current) => current.filter((item) => item.id !== output.id))}>Çıktıyı kaldır</button> : null}
        </div>)}
        <button type="button" className={styles.secondary} onClick={() => setOutputs((current) => [...current, newOutput()])}>Çıktı ekle</button>
        <p role="status">Kaynak: {label.physicalKg} kg · Çıktı toplamı: {splitTotal === null ? '—' : `${splitTotal} kg`}</p>
      </fieldset> : <fieldset disabled={frozen}>
        <legend>Birleştirilecek etiketler</legend>
        <div className={styles.grid}>
          <label htmlFor="merge-search">Etiket ara</label>
          <div className={styles.searchRow}>
            <input id="merge-search" value={search} onChange={(event) => setSearch(event.target.value)} />
            <button type="button" onClick={() => void readCandidates()}>Etiketleri getir</button>
          </div>
        </div>
        {candidates.type === 'loading' ? <p role="status">Etiketler yükleniyor…</p> : candidates.type === 'failed' ? <p role="alert">{candidates.message}</p> : null}
        {candidates.type === 'ready' && candidateItems.length === 0 ? <p>Aramaya uyan stok yok.</p> : null}
        {candidateItems.length > 0 ? <ul aria-label="Aday etiketler">{candidateItems.map((item, index) => {
          const refusal = mergeRefusal(label, item, candidateItems)
          const chosen = selected.some((entry) => sameLabelId(entry.labelId, item.labelId))
          return <li key={`${item.labelId}:${item.locationId}:${index}`}>
            <span className={styles.mono}>{item.labelCode}</span> · {item.physicalKg} kg{' '}
            {refusal !== null ? <span>({refusal})</span> : chosen
              ? <span>(seçildi)</span>
              : <button type="button" onClick={() => setSelected((current) =>
                current.some((entry) => sameLabelId(entry.labelId, item.labelId)) ? current : [...current, item])}>Ekle: {item.labelCode}</button>}
          </li>
        })}</ul> : null}
        {selected.length > 0 ? <ul aria-label="Seçilen etiketler">{selected.map((item) => <li key={item.labelId}>
          {item.labelCode} · {item.physicalKg} kg{' '}
          <button type="button" onClick={() => setSelected((current) => current.filter((entry) => !sameLabelId(entry.labelId, item.labelId)))}>Çıkar: {item.labelCode}</button>
        </li>)}</ul> : null}
        <div className={styles.grid}>
          <label htmlFor="merge-code">Birleşik paket etiket kodu</label>
          <input id="merge-code" value={mergeCode} maxLength={64} onChange={(event) => setMergeCode(event.target.value)} />
        </div>
        <p role="status">Birleşik paket: {label.code}{selected.map((item) => ` + ${item.labelCode}`).join('')} = {mergeTotal} kg</p>
      </fieldset>}

      <fieldset disabled={frozen}>
        <legend>Gerekçe</legend>
        <label htmlFor="transformation-reason">Gerekçe</label>
        <textarea id="transformation-reason" value={reason} maxLength={REASON_MAX_LENGTH} onChange={(event) => setReason(event.target.value)} />
      </fieldset>
      <div className={styles.actions}>
        <button type="submit" disabled={frozen}>{kind === 'Split' ? 'Bölmeyi kaydet' : 'Birleştirmeyi kaydet'}</button>
      </div>
    </form>
  </section>
}
