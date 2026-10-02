import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyInventoryLabel } from './labelClassificationApi'
import {
  labelClassificationRefusal,
  type LabelClassification,
} from './labelClassificationContract'
import {
  pendingLabelClassificationKey,
  recallPendingLabelClassification,
  releasePendingLabelClassification,
  reservePendingLabelClassification,
  type PendingLabelClassification,
} from './pendingLabelClassificationStore'
import styles from './inventory.module.css'

export function LabelClassificationPanel({ labelId, labelVersion, current, namespace, actorId, companyId, onChanged }: {
  readonly labelId: string
  readonly labelVersion: string
  readonly current: LabelClassification | null
  readonly namespace: string
  readonly actorId: string
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const boundary = useAuthBoundary()
  const pendingKey = pendingLabelClassificationKey(namespace, actorId, companyId, labelId)
  const [selected, setSelected] = useState<LabelClassification>(current ?? 'Standard')
  const [pending, setPending] = useState<PendingLabelClassification | null>(() => {
    const recalled = recallPendingLabelClassification(pendingKey)
    return recalled !== null && recalled.namespace === namespace && recalled.actorId === actorId
      && recalled.companyId === companyId && recalled.labelId === labelId
      ? recalled
      : null
  })
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const generation = useRef(0)
  const inFlight = useRef(false)

  useEffect(() => () => { generation.current += 1 }, [])

  async function send(attempt: PendingLabelClassification) {
    if (inFlight.current || attempt.namespace !== namespace || attempt.actorId !== actorId
      || attempt.companyId !== companyId || attempt.labelId !== labelId) return
    inFlight.current = true
    const commandGeneration = ++generation.current
    reservePendingLabelClassification(pendingKey, attempt)
    setBusy(true); setMessage(null)
    const snapshot = boundary.capture()
    const outcome = await classifyInventoryLabel(labelId, attempt.request, {
      companyId, idempotencyKey: attempt.idempotencyKey,
    })
    if (outcome.type === 'applied' || (outcome.type === 'failed' && outcome.status !== 401 && outcome.status !== 403)) {
      releasePendingLabelClassification(pendingKey, attempt.idempotencyKey)
    }
    if (commandGeneration !== generation.current) return
    inFlight.current = false
    setBusy(false)
    if (outcome.type === 'applied') {
      setPending(null)
      setMessage(outcome.replayed ? 'Önceki sınıflandırma sonucu doğrulandı.' : 'Etiket sınıflandırıldı.')
      onChanged()
      return
    }
    if (outcome.type === 'failed') {
      if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, snapshot)
      setPending(outcome.status === 401 || outcome.status === 403 ? attempt : null)
      setMessage(labelClassificationRefusal(outcome.status, outcome.code))
      return
    }
    setPending(attempt)
    setMessage('Sonuç bilinmiyor. Aynı anahtar ve içerikle yeniden deneyin.')
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    const attempt: PendingLabelClassification = pending ?? {
      namespace, actorId, companyId, labelId,
      idempotencyKey: crypto.randomUUID(),
      request: { expectedLabelVersion: labelVersion, classification: selected },
    }
    void send(attempt)
  }

  return <section aria-labelledby="label-classification-heading">
    <h2 id="label-classification-heading">Kalite sınıfı</h2>
    <p>Mevcut sınıf: {current ?? 'Sınıflandırılmamış'}. Bu işlem stok miktarı veya blok durumu oluşturmaz.</p>
    <form className={styles.form} onSubmit={submit}>
      <label htmlFor="label-classification">Yeni sınıf</label>
      <select id="label-classification" value={selected}
        disabled={busy || pending !== null}
        onChange={(event) => setSelected(event.target.value as LabelClassification)}>
        <option value="Standard">Standart</option>
        <option value="SecondQuality">İkinci kalite</option>
        <option value="Scrap">Hurda sınıfı</option>
      </select>
      <button type="submit" disabled={busy || (pending === null && selected === current)}>
        {pending === null ? 'Sınıfı kaydet' : 'Aynı anahtar ve içerikle yeniden gönder'}
      </button>
    </form>
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
