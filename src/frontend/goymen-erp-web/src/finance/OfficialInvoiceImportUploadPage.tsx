import { useEffect, useMemo, useRef, useState } from 'react'
import type { OfficialInvoicePendingBatch } from './officialInvoiceImportApprovalContract'
import {
  useOfficialInvoiceUploadTransport,
  type OfficialInvoicePreparedFile,
  type OfficialInvoiceUploadAttemptContext,
  type OfficialInvoiceUploadClient,
} from './officialInvoiceImportUploadClient'
import type { OfficialInvoiceImportPreview } from './OfficialInvoiceImportPanel'
import styles from './Finance.module.css'

interface FrozenUpload {
  readonly file: File
  readonly prepared: OfficialInvoicePreparedFile
  readonly preview: OfficialInvoiceImportPreview
  readonly batchId: string
  readonly batchVersion: string
  readonly idempotencyKey: string
}

const unresolved = new Map<string, FrozenUpload>()

function contextKey(context: OfficialInvoiceUploadAttemptContext) {
  return JSON.stringify([context.sessionEpoch, context.actorId, context.companyId])
}

function requestContextKey(context: OfficialInvoiceUploadAttemptContext) {
  return JSON.stringify([context.sessionEpoch, context.actorId, context.companyId, context.committedContextRevision,
    context.requestFenceGeneration, context.capabilityGeneration, context.securityNamespace])
}

export function OfficialInvoiceImportUploadPage() {
  const transport = useOfficialInvoiceUploadTransport()
  if (transport === null) return <p role="alert">Finans güvenlik bağlamı hazır değil.</p>
  return <OfficialInvoiceImportUploadBoundForm context={transport.context} client={transport.client} />
}

export function OfficialInvoiceImportUploadBoundForm({ context, client }: {
  readonly context: OfficialInvoiceUploadAttemptContext
  readonly client: OfficialInvoiceUploadClient
}) {
  return <OfficialInvoiceImportUploadForm key={requestContextKey(context)} context={context} client={client} />
}

export function OfficialInvoiceImportUploadForm({ context, client }: {
  readonly context: OfficialInvoiceUploadAttemptContext
  readonly client: OfficialInvoiceUploadClient
}) {
  const key = contextKey(context)
  const remembered = useMemo(() => unresolved.get(key) ?? null, [key])
  const [file, setFile] = useState<File | null>(remembered?.file ?? null)
  const [batchVersion, setBatchVersion] = useState(remembered?.batchVersion ?? '')
  const [attempt, setAttempt] = useState<FrozenUpload | null>(remembered)
  const [phase, setPhase] = useState<'idle' | 'previewing' | 'ready' | 'sending' | 'unknown' | 'conflicted' | 'refused' | 'succeeded'>(
    remembered === null ? 'idle' : 'unknown')
  const [preview, setPreview] = useState<OfficialInvoiceImportPreview | null>(remembered?.preview ?? null)
  const [batch, setBatch] = useState<OfficialInvoicePendingBatch | null>(null)
  const [issues, setIssues] = useState<readonly { readonly field: string; readonly code: string }[]>([])
  const active = useRef<AbortController | null>(null)

  useEffect(() => () => {
    active.current?.abort()
    active.current = null
  }, [])

  async function runPreview() {
    const selectedFile = file
    const selectedBatchVersion = batchVersion.trim()
    if (selectedFile === null || selectedBatchVersion.length === 0 || attempt !== null) return
    setPhase('previewing')
    setIssues([])
    const controller = new AbortController()
    active.current?.abort()
    active.current = controller
    const batchId = crypto.randomUUID()
    const idempotencyKey = crypto.randomUUID()
    try {
      const prepared = await client.prepare(selectedFile)
      if (controller.signal.aborted || active.current !== controller) return
      const reply = await client.preview(batchId, selectedBatchVersion, prepared, controller.signal)
      if (controller.signal.aborted || active.current !== controller) return
      active.current = null
      if (reply.kind === 'ready') {
        const frozen = { file: selectedFile, prepared, preview: reply.preview, batchId,
          batchVersion: selectedBatchVersion, idempotencyKey }
        setAttempt(frozen)
        setPreview(reply.preview)
        setPhase('ready')
      } else if (reply.kind === 'refused') {
        setIssues(reply.issues)
        setPhase('refused')
      } else {
        setPhase('idle')
      }
    } catch {
      if (!controller.signal.aborted) setPhase('idle')
    }
  }

  async function upload() {
    const frozen = attempt
    if (frozen === null) return
    unresolved.set(key, frozen)
    setAttempt(frozen)
    setPhase('sending')
    setIssues([])
    const controller = new AbortController()
    active.current?.abort()
    active.current = controller
    const reply = await client.upload(frozen.batchId, frozen.batchVersion, frozen.prepared, frozen.idempotencyKey, controller.signal)
    if (controller.signal.aborted || active.current !== controller) return
    active.current = null
    if (reply.kind === 'succeeded') {
      unresolved.delete(key)
      setAttempt(null)
      setPreview(null)
      setBatch(reply.batch)
      setPhase('succeeded')
    } else if (reply.kind === 'refused') {
      unresolved.delete(key)
      setAttempt(null)
      setPreview(null)
      setIssues(reply.issues)
      setPhase('refused')
    } else if (reply.kind === 'conflicted') {
      unresolved.delete(key)
      setAttempt(null)
      setPreview(null)
      setPhase('conflicted')
    } else {
      setPhase(reply.kind)
    }
  }

  const frozen = attempt !== null
  const editingLocked = frozen || phase === 'previewing'
  return (
    <section aria-labelledby="official-invoice-upload-title">
      <h1 id="official-invoice-upload-title">Onaylı fatura taksit kaynağı yükle</h1>
      <p className={styles.notice}>Dosya önce bekleyen kaynak olarak kaydedilir; başka bir yetkili onaylamadan Finans açık kalemi oluşmaz.</p>
      <div className={styles.form}>
        <label htmlFor="official-invoice-upload-version">Kaynak sürümü</label>
        <input id="official-invoice-upload-version" value={batchVersion} disabled={editingLocked}
          onChange={(event) => { setBatchVersion(event.target.value); setPreview(null); setPhase('idle') }} />
        <label htmlFor="official-invoice-upload-file">UTF-8 CSV dosyası</label>
        <input id="official-invoice-upload-file" type="file" accept=".csv,text/csv" disabled={editingLocked}
          onChange={(event) => { setFile(event.target.files?.[0] ?? null); setPreview(null); setPhase('idle') }} />
        <button type="button" disabled={frozen || phase === 'previewing' || file === null || batchVersion.trim().length === 0}
          onClick={runPreview}>Önizle</button>
        <button type="button" disabled={attempt === null || phase === 'sending'} onClick={upload}>
          {phase === 'unknown' ? 'Aynı anahtar ve dosyayla yeniden gönder' : 'Onaya gönder'}
        </button>
      </div>
      {phase === 'previewing' ? <p role="status">Seçilen dosya doğrulanıyor…</p> : null}
      {preview === null ? null : <p role="status">Önizleme hazır. SHA-256: {preview.sourceSha256}</p>}
      {phase === 'sending' ? <p role="status">Fatura kaynağı bekleyen onaya kaydediliyor…</p> : null}
      {phase === 'unknown' ? <p role="status">Sonuç bilinmiyor; yeni işlem üretmeden aynı istek yeniden gönderilebilir.</p> : null}
      {phase === 'conflicted' ? <p role="alert">Aynı işlem anahtarı farklı içerikle çakıştı; yeni kayıt yapılmadı.</p> : null}
      {issues.map((issue, index) => <p role="alert" key={`${issue.field}-${issue.code}-${index}`}>{issue.field}: {issue.code}</p>)}
      {batch === null ? null : (
        <article aria-label="Bekleyen fatura kaynağı">
          <h2>Onay bekliyor</h2>
          <p>Batch: {batch.batchId}</p>
          <p>Sürüm: {batch.batchVersion}</p>
          <p>SHA-256: {batch.batchChecksum}</p>
          <p>{batch.rowCount} taksit satırı başka bir yetkilinin onayını bekliyor.</p>
        </article>
      )}
    </section>
  )
}
