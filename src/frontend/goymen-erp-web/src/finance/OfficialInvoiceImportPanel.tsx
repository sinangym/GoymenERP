import { useMemo, useState } from 'react'
import styles from './Finance.module.css'

export interface OfficialInvoiceImportSourceSelection {
  readonly batchId: string
  readonly batchVersion: string
  readonly displayName: string
}

export interface OfficialInvoiceImportIssue {
  readonly rowNumber: number
  readonly field: string
  readonly code: string
}

export interface OfficialInvoiceImportPreview {
  readonly templateVersion: string
  readonly sourceSha256: string
  readonly batchId: string
  readonly batchVersion: string
  readonly rowCount: number
  readonly issues: readonly OfficialInvoiceImportIssue[]
}

export interface OfficialInvoiceOpenItem {
  readonly id: string
  readonly sourceDocumentId: string
  readonly sourceInstallmentId: string
  readonly partyId: string
  readonly companyAccountId: string
  readonly currencyId: string
  readonly originalAmount: string
  readonly outstandingAmount: string
  readonly dueOn: string | null
  readonly status: number
  readonly version: string
}

export interface OfficialInvoiceAppliedBatch {
  readonly batchId: string
  readonly batchVersion: string
  readonly batchChecksum: string
  readonly openItems: readonly OfficialInvoiceOpenItem[]
}

export type OfficialInvoiceApplyReply =
  | { readonly kind: 'succeeded'; readonly replayed: boolean; readonly batch: OfficialInvoiceAppliedBatch }
  | { readonly kind: 'refused'; readonly issues: readonly OfficialInvoiceImportIssue[] }
  | { readonly kind: 'unknown' }
  | { readonly kind: 'conflicted' }

export interface OfficialInvoiceImportClient {
  preview(
    source: OfficialInvoiceImportSourceSelection,
    file: File,
    signal: AbortSignal,
  ): Promise<OfficialInvoiceImportPreview>

  apply(
    source: OfficialInvoiceImportSourceSelection,
    file: File,
    idempotencyKey: string,
    signal: AbortSignal,
  ): Promise<OfficialInvoiceApplyReply>

  reopen(batchId: string, signal: AbortSignal): Promise<OfficialInvoiceAppliedBatch>
}

type Phase = 'idle' | 'previewing' | 'ready' | 'applying' | 'unknown' | 'conflicted' | 'refused' | 'succeeded'

export function OfficialInvoiceImportPanel({
  source,
  client,
}: {
  readonly source: OfficialInvoiceImportSourceSelection
  readonly client: OfficialInvoiceImportClient
}) {
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<OfficialInvoiceImportPreview | null>(null)
  const [batch, setBatch] = useState<OfficialInvoiceAppliedBatch | null>(null)
  const [issues, setIssues] = useState<readonly OfficialInvoiceImportIssue[]>([])
  const [phase, setPhase] = useState<Phase>('idle')
  const [closedBatchId, setClosedBatchId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const idempotencyKey = useMemo(() => {
    if (preview === null) return null
    return `official-invoice:${source.batchId}:${preview.sourceSha256}`
  }, [preview, source.batchId])

  async function runPreview() {
    if (file === null) return
    const controller = new AbortController()
    setPhase('previewing')
    setError(null)
    setIssues([])
    setBatch(null)
    try {
      const result = await client.preview(source, file, controller.signal)
      if (result.batchId !== source.batchId || result.batchVersion !== source.batchVersion) {
        setPhase('refused')
        setError('Önizleme, seçilen onaylı kaynak kimliğiyle eşleşmiyor.')
        return
      }
      setPreview(result)
      setIssues(result.issues)
      setPhase(result.issues.length === 0 ? 'ready' : 'refused')
    } catch {
      setPhase('idle')
      setError('Dosya önizlemesi alınamadı; kayıt yapılmadı.')
    }
  }

  async function apply() {
    if (file === null || preview === null || idempotencyKey === null || preview.issues.length > 0) return
    const controller = new AbortController()
    setPhase('applying')
    setError(null)
    try {
      const result = await client.apply(source, file, idempotencyKey, controller.signal)
      if (result.kind === 'succeeded') {
        if (result.batch.batchId !== source.batchId
          || result.batch.batchChecksum !== preview.sourceSha256) {
          setPhase('refused')
          setError('Kaydedilen açık kalem sonucu önizlemeyle eşleşmiyor.')
          return
        }
        setBatch(result.batch)
        setClosedBatchId(null)
        setIssues([])
        setPhase('succeeded')
        return
      }
      if (result.kind === 'refused') {
        setIssues(result.issues)
        setPhase('refused')
        return
      }
      setPhase(result.kind)
    } catch {
      setPhase('unknown')
    }
  }

  async function reopen() {
    if (closedBatchId === null) return
    const controller = new AbortController()
    setError(null)
    try {
      const result = await client.reopen(closedBatchId, controller.signal)
      if (result.batchId !== closedBatchId) {
        setError('Yeniden açılan kayıt beklenen kaynak kimliğiyle eşleşmiyor.')
        return
      }
      setBatch(result)
      setClosedBatchId(null)
      setPhase('succeeded')
    } catch {
      setError('Kaydedilmiş açık kalemler yeniden açılamadı.')
    }
  }

  return (
    <section aria-labelledby="official-invoice-import-title">
      <h1 id="official-invoice-import-title">Onaylı fatura taksitlerini Finans'a aktar</h1>
      <p className={styles.notice}>
        Yalnız seçilen onaylı kaynak önizlenir. Önizleme kayıt değildir; açık kalemler yetkili uygulama
        tamamlandıktan sonra Finans'tan yeniden okunur.
      </p>
      <dl>
        <dt>Onaylı kaynak</dt>
        <dd>{source.displayName}</dd>
        <dt>Kaynak sürümü</dt>
        <dd>{source.batchVersion}</dd>
      </dl>
      <div className={styles.form}>
        <label htmlFor="official-invoice-import-file">UTF-8 CSV dosyası</label>
        <input
          id="official-invoice-import-file"
          type="file"
          accept=".csv,text/csv"
          disabled={phase === 'previewing' || phase === 'applying'}
          onChange={(event) => {
            setFile(event.target.files?.[0] ?? null)
            setPreview(null)
            setBatch(null)
            setClosedBatchId(null)
            setIssues([])
            setError(null)
            setPhase('idle')
          }}
        />
        <div className={styles.actions}>
          <button type="button" disabled={file === null || phase === 'previewing' || phase === 'applying'} onClick={runPreview}>
            Önizle
          </button>
          <button type="button" disabled={phase !== 'ready'} onClick={apply}>
            Yetkili uygula ve açık kalemleri oku
          </button>
        </div>
      </div>
      {phase === 'previewing' ? <p role="status">Dosya doğrulanıyor…</p> : null}
      {phase === 'applying' ? <p role="status">Açık kalemler atomik olarak kaydediliyor…</p> : null}
      {preview === null ? null : (
        <article className={styles.command} aria-label="Fatura içe aktarma önizlemesi">
          <p>Şablon: {preview.templateVersion}</p>
          <p>Satır: {preview.rowCount}</p>
          <p>SHA-256: {preview.sourceSha256}</p>
        </article>
      )}
      {issues.length === 0 ? null : (
        <div className={styles.attempt} role="alert">
          <p>Dosya uygulanmadı.</p>
          {issues.map((issue, index) => (
            <p key={`${issue.rowNumber}-${issue.field}-${issue.code}-${index}`}>
              Satır {issue.rowNumber}, {issue.field}: {issue.code}
            </p>
          ))}
        </div>
      )}
      {phase === 'unknown' ? (
        <div className={styles.attempt} role="status">
          <p>Sonuç bilinmiyor; yeni işlem üretmeden aynı dosya ve anahtarla yeniden gönderin.</p>
          <button type="button" onClick={apply}>Aynı anahtarla yeniden gönder</button>
        </div>
      ) : null}
      {phase === 'conflicted' ? <p role="alert">Aynı işlem anahtarı farklı içerikle çakıştı; kayıt yapılmadı.</p> : null}
      {error === null ? null : <p role="alert">{error}</p>}
      {batch === null ? null : (
        <article className={styles.command} aria-label="Kaydedilmiş Finans açık kalemleri">
          <h2>Kaydedilmiş açık kalemler</h2>
          <p>{batch.openItems.length} açık kalem Finans'tan yeniden okundu.</p>
          <ul>
            {batch.openItems.map((item) => (
              <li key={item.id}>
                <strong>{item.originalAmount}</strong> / kalan {item.outstandingAmount} / sürüm {item.version}
              </li>
            ))}
          </ul>
          <button type="button" onClick={() => {
            setClosedBatchId(batch.batchId)
            setBatch(null)
          }}>
            Sonucu kapat
          </button>
        </article>
      )}
      {closedBatchId === null ? null : (
        <button type="button" onClick={reopen}>Kaydedilmiş açık kalemleri yeniden aç</button>
      )}
    </section>
  )
}
