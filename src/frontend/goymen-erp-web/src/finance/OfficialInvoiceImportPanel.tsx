import { useEffect, useRef, useState } from 'react'
import styles from './Finance.module.css'

export interface OfficialInvoiceImportSourceSelection {
  readonly batchId: string
  readonly batchVersion: string
  readonly displayName: string
}

export interface OfficialInvoiceImportAttemptContext {
  readonly sessionEpoch: number
  readonly actorId: string
  readonly companyId: string
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

interface BatchIdentity {
  readonly batchId: string
  readonly batchVersion: string
  readonly batchChecksum: string
}

interface PanelState {
  readonly sourceKey: string
  readonly file: File | null
  readonly preview: OfficialInvoiceImportPreview | null
  readonly batch: OfficialInvoiceAppliedBatch | null
  readonly issues: readonly OfficialInvoiceImportIssue[]
  readonly phase: Phase
  readonly closedBatch: BatchIdentity | null
  readonly error: string | null
}

interface StoredAttempt {
  readonly file: File
  readonly preview: OfficialInvoiceImportPreview
  readonly idempotencyKey: string
}

const attemptStore = new Map<string, StoredAttempt>()

function sourceKeyFor(
  context: OfficialInvoiceImportAttemptContext,
  source: OfficialInvoiceImportSourceSelection,
): string {
  return JSON.stringify([
    context.sessionEpoch,
    context.actorId,
    context.companyId,
    source.batchId,
    source.batchVersion,
  ])
}

function emptyState(sourceKey: string): PanelState {
  return {
    sourceKey,
    file: null,
    preview: null,
    batch: null,
    issues: [],
    phase: 'idle',
    closedBatch: null,
    error: null,
  }
}

function recalledState(sourceKey: string): PanelState {
  const attempt = attemptStore.get(sourceKey)
  return attempt === undefined
    ? emptyState(sourceKey)
    : {
        ...emptyState(sourceKey),
        file: attempt.file,
        preview: attempt.preview,
        phase: 'unknown',
      }
}

function identityOf(batch: OfficialInvoiceAppliedBatch): BatchIdentity {
  return {
    batchId: batch.batchId,
    batchVersion: batch.batchVersion,
    batchChecksum: batch.batchChecksum,
  }
}

function sameIdentity(left: BatchIdentity, right: BatchIdentity): boolean {
  return left.batchId === right.batchId
    && left.batchVersion === right.batchVersion
    && left.batchChecksum === right.batchChecksum
}

export function OfficialInvoiceImportPanel({
  source,
  client,
  attemptContext,
}: {
  readonly source: OfficialInvoiceImportSourceSelection
  readonly client: OfficialInvoiceImportClient
  readonly attemptContext: OfficialInvoiceImportAttemptContext
}) {
  const sourceKey = sourceKeyFor(attemptContext, source)
  return <OfficialInvoiceImportPanelState key={sourceKey} source={source} client={client} sourceKey={sourceKey} />
}

function OfficialInvoiceImportPanelState({
  source,
  client,
  sourceKey,
}: {
  readonly source: OfficialInvoiceImportSourceSelection
  readonly client: OfficialInvoiceImportClient
  readonly sourceKey: string
}) {
  const [state, setState] = useState<PanelState>(() => recalledState(sourceKey))
  const controllers = useRef(new Set<AbortController>())
  const current = state
  const frozen = current.phase === 'applying' || current.phase === 'unknown' || current.phase === 'conflicted'

  useEffect(() => {
    const activeControllers = controllers.current
    return () => {
      for (const controller of activeControllers) controller.abort()
      activeControllers.clear()
    }
  }, [])

  const update = (key: string, apply: (value: PanelState) => PanelState) => {
    setState((value) => value.sourceKey === key ? apply(value) : value)
  }

  const startRequest = () => {
    const controller = new AbortController()
    controllers.current.add(controller)
    return controller
  }

  const finishRequest = (controller: AbortController) => {
    controllers.current.delete(controller)
  }

  async function runPreview() {
    if (current.file === null || frozen) return
    const operationKey = sourceKey
    const selectedSource = source
    const selectedFile = current.file
    const controller = startRequest()
    update(operationKey, (value) => ({
      ...value,
      preview: null,
      batch: null,
      closedBatch: null,
      phase: 'previewing',
      error: null,
      issues: [],
    }))
    try {
      const result = await client.preview(selectedSource, selectedFile, controller.signal)
      if (result.batchId !== selectedSource.batchId || result.batchVersion !== selectedSource.batchVersion) {
        update(operationKey, (value) => ({
          ...value,
          phase: 'refused',
          error: 'Önizleme, seçilen onaylı kaynak kimliğiyle eşleşmiyor.',
        }))
        return
      }
      update(operationKey, (value) => ({
        ...value,
        preview: result,
        issues: result.issues,
        phase: result.issues.length === 0 ? 'ready' : 'refused',
      }))
    } catch {
      if (!controller.signal.aborted) {
        update(operationKey, (value) => ({
          ...value,
          phase: 'idle',
          error: 'Dosya önizlemesi alınamadı; kayıt yapılmadı.',
        }))
      }
    } finally {
      finishRequest(controller)
    }
  }

  async function apply() {
    const selectedPreview = current.preview
    if (current.file === null || selectedPreview === null || selectedPreview.issues.length > 0
      || selectedPreview.batchId !== source.batchId || selectedPreview.batchVersion !== source.batchVersion) return
    const operationKey = sourceKey
    const selectedSource = source
    const selectedFile = current.file
    const idempotencyKey = attemptStore.get(operationKey)?.idempotencyKey
      ?? `official-invoice:${selectedSource.batchId}:${selectedPreview.sourceSha256}`
    attemptStore.set(operationKey, { file: selectedFile, preview: selectedPreview, idempotencyKey })
    const controller = startRequest()
    update(operationKey, (value) => ({ ...value, phase: 'applying', error: null }))
    try {
      const result = await client.apply(selectedSource, selectedFile, idempotencyKey, controller.signal)
      if (result.kind === 'succeeded') {
        const expected = {
          batchId: selectedSource.batchId,
          batchVersion: selectedSource.batchVersion,
          batchChecksum: selectedPreview.sourceSha256,
        }
        if (!sameIdentity(identityOf(result.batch), expected)) {
          update(operationKey, (value) => ({
            ...value,
            phase: 'unknown',
            error: 'Kaydedilen açık kalem sonucu önizlemeyle eşleşmiyor.',
          }))
          return
        }
        attemptStore.delete(operationKey)
        update(operationKey, (value) => ({
          ...value,
          batch: result.batch,
          closedBatch: null,
          issues: [],
          phase: 'succeeded',
        }))
        return
      }
      if (result.kind === 'refused') {
        attemptStore.delete(operationKey)
        update(operationKey, (value) => ({ ...value, issues: result.issues, phase: 'refused' }))
        return
      }
      update(operationKey, (value) => ({ ...value, phase: result.kind }))
    } catch {
      update(operationKey, (value) => ({ ...value, phase: 'unknown' }))
    } finally {
      finishRequest(controller)
    }
  }

  async function reopen() {
    if (current.closedBatch === null) return
    const operationKey = sourceKey
    const expected = current.closedBatch
    const controller = startRequest()
    update(operationKey, (value) => ({ ...value, error: null }))
    try {
      const result = await client.reopen(expected.batchId, controller.signal)
      if (!sameIdentity(identityOf(result), expected)) {
        update(operationKey, (value) => ({
          ...value,
          error: 'Yeniden açılan kayıt beklenen kaynak kimliğiyle eşleşmiyor.',
        }))
        return
      }
      update(operationKey, (value) => ({ ...value, batch: result, closedBatch: null, phase: 'succeeded' }))
    } catch {
      if (!controller.signal.aborted) {
        update(operationKey, (value) => ({ ...value, error: 'Kaydedilmiş açık kalemler yeniden açılamadı.' }))
      }
    } finally {
      finishRequest(controller)
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
          key={sourceKey}
          id="official-invoice-import-file"
          type="file"
          accept=".csv,text/csv"
          disabled={current.phase === 'previewing' || frozen}
          onChange={(event) => {
            if (frozen) return
            setState({ ...emptyState(sourceKey), file: event.target.files?.[0] ?? null })
          }}
        />
        <div className={styles.actions}>
          <button type="button" disabled={current.file === null || current.phase === 'previewing' || frozen} onClick={runPreview}>
            Önizle
          </button>
          <button type="button" disabled={current.phase !== 'ready'
            || current.preview?.batchId !== source.batchId
            || current.preview.batchVersion !== source.batchVersion} onClick={apply}>
            Yetkili uygula ve açık kalemleri oku
          </button>
        </div>
      </div>
      {current.phase === 'previewing' ? <p role="status">Dosya doğrulanıyor…</p> : null}
      {current.phase === 'applying' ? <p role="status">Açık kalemler atomik olarak kaydediliyor…</p> : null}
      {current.preview === null ? null : (
        <article className={styles.command} aria-label="Fatura içe aktarma önizlemesi">
          <p>Şablon: {current.preview.templateVersion}</p>
          <p>Satır: {current.preview.rowCount}</p>
          <p>SHA-256: {current.preview.sourceSha256}</p>
        </article>
      )}
      {current.issues.length === 0 ? null : (
        <div className={styles.attempt} role="alert">
          <p>Dosya uygulanmadı.</p>
          {current.issues.map((issue, index) => (
            <p key={`${issue.rowNumber}-${issue.field}-${issue.code}-${index}`}>
              Satır {issue.rowNumber}, {issue.field}: {issue.code}
            </p>
          ))}
        </div>
      )}
      {current.phase === 'unknown' ? (
        <div className={styles.attempt} role="status">
          <p>Sonuç bilinmiyor; yeni işlem üretmeden aynı dosya ve anahtarla yeniden gönderin.</p>
          <button type="button" onClick={apply}>Aynı anahtarla yeniden gönder</button>
        </div>
      ) : null}
      {current.phase === 'conflicted' ? <p role="alert">Aynı işlem anahtarı farklı içerikle çakıştı; kayıt yapılmadı.</p> : null}
      {current.error === null ? null : <p role="alert">{current.error}</p>}
      {current.batch === null ? null : (
        <article className={styles.command} aria-label="Kaydedilmiş Finans açık kalemleri">
          <h2>Kaydedilmiş açık kalemler</h2>
          <p>{current.batch.openItems.length} açık kalem Finans'tan yeniden okundu.</p>
          <ul>
            {current.batch.openItems.map((item) => (
              <li key={item.id}>
                <strong>{item.originalAmount}</strong> / kalan {item.outstandingAmount} / sürüm {item.version}
              </li>
            ))}
          </ul>
          <button type="button" onClick={() => {
            setState((value) => value.sourceKey === sourceKey
              ? { ...value, closedBatch: identityOf(current.batch!), batch: null }
              : value)
          }}>
            Sonucu kapat
          </button>
        </article>
      )}
      {current.closedBatch === null ? null : (
        <button type="button" onClick={reopen}>Kaydedilmiş açık kalemleri yeniden aç</button>
      )}
    </section>
  )
}
