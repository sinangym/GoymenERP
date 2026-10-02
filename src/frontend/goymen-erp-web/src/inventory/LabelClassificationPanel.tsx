import { useState, type FormEvent } from 'react'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyInventoryLabel } from './labelClassificationApi'
import {
  labelClassificationRefusal,
  type LabelClassification,
  type LabelClassificationRequest,
} from './labelClassificationContract'
import styles from './inventory.module.css'

type Pending = { readonly key: string; readonly request: LabelClassificationRequest }

export function LabelClassificationPanel({ labelId, labelVersion, current, companyId, onChanged }: {
  readonly labelId: string
  readonly labelVersion: string
  readonly current: LabelClassification | null
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const boundary = useAuthBoundary()
  const [selected, setSelected] = useState<LabelClassification>(current ?? 'Standard')
  const [pending, setPending] = useState<Pending | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  async function send(attempt: Pending) {
    setBusy(true); setMessage(null)
    const snapshot = boundary.capture()
    const outcome = await classifyInventoryLabel(labelId, attempt.request, {
      companyId, idempotencyKey: attempt.key,
    })
    setBusy(false)
    if (outcome.type === 'applied') {
      setPending(null)
      setMessage(outcome.replayed ? 'Önceki sınıflandırma sonucu doğrulandı.' : 'Etiket sınıflandırıldı.')
      onChanged()
      return
    }
    if (outcome.type === 'failed') {
      setPending(null)
      if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, snapshot)
      setMessage(labelClassificationRefusal(outcome.status, outcome.code))
      return
    }
    setPending(attempt)
    setMessage('Sonuç bilinmiyor. Aynı anahtar ve içerikle yeniden deneyin.')
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    const attempt: Pending = pending ?? {
      key: crypto.randomUUID(),
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
        {pending === null ? 'Sınıfı kaydet' : 'Aynı isteği yeniden gönder'}
      </button>
    </form>
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
