import { useCallback, useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { applyProductImport, previewProductImport } from './importApi'
import {
  productImportErrorsCsv,
  type ProductImportColumnMapping,
  type ProductImportPreview,
  type ProductImportResult,
} from './importContract'
import { PRODUCT_CAPABILITY_ROUTE, PRODUCT_CAPABILITY, productDetailPath } from './capabilities'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import {
  generateAttemptKey,
  hasUnresolvedWork,
  isFormFrozen,
  liveSnapshot,
  nextState,
  type AttemptSnapshot,
  type AttemptState,
} from './commandAttempt'
import {
  attemptBindingFor,
  importScope,
  recallAttempt,
  rememberAttempt,
  type AttemptBinding,
} from './pendingAttempt'
import styles from './ProductImportPage.module.css'

const EMPTY_MAPPING: ProductImportColumnMapping = { legacyCode: '', name: '', groupId: '' }

type RemoteState = 'idle' | 'loading' | 'ready' | 'error'
type ImportPayload = {
  readonly file: File
  readonly mapping: ProductImportColumnMapping
  readonly preview: ProductImportPreview
}
type ImportAttempt = AttemptState<ImportPayload, ProductImportResult>
const PRODUCT_IMPORT_DIRTY_SCOPE = 'product-import-form'

function restoredImport(binding: AttemptBinding | null): {
  readonly attempt: ImportAttempt
  readonly payload: ImportPayload | null
} {
  const attempt = recallAttempt<ImportPayload, ProductImportResult>(binding) ?? { phase: 'idle' as const }
  return { attempt, payload: attempt.phase === 'idle' ? null : attempt.snapshot.payload }
}

function messageFor(attempt: ImportAttempt): string | null {
  if (attempt.phase === 'completed') return `${attempt.result.products.length} ürün taslağı kaydedildi.`
  if (attempt.phase === 'submitting') return 'İçe aktarma uygulanıyor…'
  if (attempt.phase === 'unknown') return 'Sonuç doğrulanamadı. Aynı istek anahtarı ve dosyayla yeniden deneyin.'
  if (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined)
    return 'Önceki sonucun durumu bilinmiyor. Yetki yeniden doğrulandıktan sonra aynı isteği deneyin.'
  if (attempt.phase === 'failed') return 'İçe aktarma reddedildi. Hiçbir satır kaydedilmedi.'
  return null
}

export function ProductImportPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const binding = attemptBindingFor(shell, importScope())
  return <ProductImportForm key={namespace ?? 'no-context'} binding={binding}
    origin={shell.activeContext?.companyId ?? null} />
}

function ProductImportForm({ binding, origin }: {
  readonly binding: AttemptBinding | null
  readonly origin: string | null
}) {
  const dispatch = useShellDispatch()
  const boundary = useAuthBoundary()
  const [restored] = useState(() => restoredImport(binding))
  const [file, setFile] = useState<File | null>(restored.payload?.file ?? null)
  const [mapping, setMapping] = useState<ProductImportColumnMapping>(restored.payload?.mapping ?? EMPTY_MAPPING)
  const [preview, setPreview] = useState<ProductImportPreview | null>(restored.payload?.preview ?? null)
  const [previewState, setPreviewState] = useState<RemoteState>(restored.payload === null ? 'idle' : 'ready')
  const [attempt, setAttempt] = useState<ImportAttempt>(restored.attempt)
  const [message, setMessage] = useState<string | null>(() => messageFor(restored.attempt))
  const active = useRef<AbortController | null>(null)
  const attemptRef = useRef<ImportAttempt>(attempt)

  useEffect(() => () => active.current?.abort(), [])
  useEffect(() => { attemptRef.current = attempt }, [attempt])
  useEffect(() => {
    if (attempt.phase === 'completed') rememberAttempt(binding, attempt)
  }, [attempt, binding])

  const mappingReady = [mapping.legacyCode, mapping.name, mapping.groupId].every((value) => value.trim().length > 0)
    && new Set([mapping.legacyCode.trim(), mapping.name.trim(), mapping.groupId.trim()]).size === 3
  const live = liveSnapshot(attempt)
  const frozen = isFormFrozen(attempt) || live !== null
  const dirty = file !== null || Object.values(mapping).some(value => value.length > 0) || preview !== null
  const unresolved = hasUnresolvedWork(attempt, dirty)
  const result = attempt.phase === 'completed' ? attempt.result : null

  useEffect(() => {
    dispatch({ type: unresolved ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: PRODUCT_IMPORT_DIRTY_SCOPE })
  }, [dispatch, unresolved])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope: PRODUCT_IMPORT_DIRTY_SCOPE }), [dispatch])

  function resetServerState() {
    if (frozen) return
    active.current?.abort()
    active.current = null
    setPreview(null)
    setPreviewState('idle')
    setAttempt({ phase: 'idle' })
    setMessage(null)
  }

  function updateMapping(field: keyof ProductImportColumnMapping, value: string) {
    resetServerState()
    setMapping((current) => ({ ...current, [field]: value }))
  }

  async function runPreview() {
    if (file === null || !mappingReady || origin === null) return
    active.current?.abort()
    const controller = new AbortController()
    active.current = controller
    const auth = boundary.capture()
    setPreviewState('loading')
    setMessage(null)
    setAttempt({ phase: 'idle' })
    const outcome = await previewProductImport(file, mapping, { companyId: origin }, controller.signal)
    if (controller.signal.aborted) return
    if (outcome.type === 'http') boundary.report(outcome.status, auth)
    if (outcome.type === 'ok-single') {
      setPreview(outcome.data)
      setPreviewState('ready')
      setMessage(outcome.data.ready
        ? `${outcome.data.sourceRowCount} satır içe aktarmaya hazır.`
        : 'Dosya uygulanmadı. Hataları düzeltip yeniden önizleyin.')
      return
    }
    setPreview(null)
    setPreviewState('error')
    setMessage(outcome.type === 'offline' ? 'Çevrimdışısınız. Bağlantıyı kurup yeniden deneyin.'
      : outcome.type === 'fence-stale' ? 'Oturum bağlamı değişti. Dosyayı yeniden önizleyin.'
        : 'Önizleme alınamadı. Sunucu yanıtını doğrulayıp yeniden deneyin.')
  }

  const send = useCallback(async (snapshot: AttemptSnapshot<ImportPayload>) => {
    const previous = attemptRef.current
    const inFlight: ImportAttempt = { phase: 'submitting', snapshot }
    rememberAttempt(binding, inFlight)
    setAttempt(inFlight)
    active.current?.abort()
    const controller = new AbortController()
    active.current = controller
    const auth = boundary.capture()
    setMessage('İçe aktarma uygulanıyor…')
    const outcome = await applyProductImport(snapshot.payload.file, snapshot.payload.mapping,
      snapshot.payload.preview, snapshot.key, { companyId: snapshot.origin }, controller.signal)
    if (controller.signal.aborted) return
    const next = nextState(snapshot, outcome, previous)
    rememberAttempt(binding, next, false)
    if (outcome.type === 'http') boundary.report(outcome.status, auth)
    setAttempt(next)
    setMessage(messageFor(next))
  }, [binding, boundary])

  function apply() {
    if (live !== null) {
      void send(live)
      return
    }
    if (file === null || preview?.ready !== true || origin === null || binding === null) return
    void send({
      key: generateAttemptKey(),
      payload: {
        file,
        mapping: {
          legacyCode: mapping.legacyCode.trim(),
          name: mapping.name.trim(),
          groupId: mapping.groupId.trim(),
        },
        preview,
      },
      origin,
      namespace: binding.key,
    })
  }

  function downloadErrors() {
    if (preview === null || preview.errors.length === 0) return
    const url = URL.createObjectURL(productImportErrorsCsv(preview.errors))
    const link = document.createElement('a')
    link.href = url
    link.download = 'urun-ice-aktarma-hatalari.csv'
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <section className={styles.page} aria-labelledby="product-import-heading">
      <header className={styles.header}>
        <div>
          <h1 id="product-import-heading">Excel’den ürün içe aktar</h1>
          <p>Dosya önce doğrulanır. Herhangi bir satır hatalıysa hiçbir ürün kaydedilmez.</p>
        </div>
        <Link to={PRODUCT_CAPABILITY_ROUTE[PRODUCT_CAPABILITY.productsList]}>Ürünlere dön</Link>
      </header>

      <div className={styles.panel}>
        <label>
          Excel dosyası (.xlsx)
          <input type="file" accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            disabled={frozen}
            onChange={(event) => { resetServerState(); setFile(event.target.files?.[0] ?? null) }} />
          {file !== null ? <span>{file.name}</span> : null}
        </label>
        <div className={styles.mapping}>
          <label>Eski kod sütunu<input value={mapping.legacyCode} disabled={frozen}
            onChange={(event) => updateMapping('legacyCode', event.target.value)} /></label>
          <label>Ürün adı sütunu<input value={mapping.name} disabled={frozen}
            onChange={(event) => updateMapping('name', event.target.value)} /></label>
          <label>Grup kimliği sütunu<input value={mapping.groupId} disabled={frozen}
            onChange={(event) => updateMapping('groupId', event.target.value)} /></label>
        </div>
        <button type="button" onClick={() => void runPreview()}
          disabled={frozen || file === null || !mappingReady || previewState === 'loading' || origin === null}>
          {previewState === 'loading' ? 'Önizleniyor…' : 'Önizle'}
        </button>
      </div>

      {message !== null ? <p
        role={previewState === 'error' || attempt.phase === 'failed' || attempt.phase === 'unknown' ? 'alert' : 'status'}
        data-testid={attempt.phase === 'unknown' || (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined)
          ? 'product-import-unknown' : undefined}
      >{message}</p> : null}

      {preview !== null && preview.errors.length > 0 ? (
        <section className={styles.panel} aria-labelledby="product-import-errors">
          <div className={styles.panelHeading}>
            <h2 id="product-import-errors">Hatalar ({preview.errors.length})</h2>
            <button type="button" onClick={downloadErrors}>Hata listesini indir</button>
          </div>
          <table><thead><tr><th scope="col">Satır</th><th scope="col">Alan</th><th scope="col">Hata</th></tr></thead>
            <tbody>{preview.errors.map((error, index) => <tr key={`${error.rowNumber}-${error.field}-${error.code}-${index}`}>
              <td>{error.rowNumber ?? 'Dosya'}</td><td>{error.field}</td><td>{error.message}</td>
            </tr>)}</tbody></table>
        </section>
      ) : null}

      {preview?.ready ? (
        <section className={styles.panel} aria-labelledby="product-import-ready">
          <div className={styles.panelHeading}>
            <h2 id="product-import-ready">Kaydedilecek ürünler</h2>
            <button type="button" onClick={apply} disabled={attempt.phase === 'submitting' || attempt.phase === 'completed'}>
              {attempt.phase === 'submitting' ? 'Kaydediliyor…' : live === null ? 'Tümünü içe aktar' : 'Aynı isteği yeniden dene'}
            </button>
          </div>
          <table><thead><tr><th scope="col">Satır</th><th scope="col">Eski kod</th><th scope="col">Ürün adı</th><th scope="col">Grup kimliği</th></tr></thead>
            <tbody>{preview.rows.map((row) => <tr key={row.rowNumber}>
              <td>{row.rowNumber}</td><td>{row.legacyCode}</td><td>{row.name}</td><td>{row.groupId}</td>
            </tr>)}</tbody></table>
        </section>
      ) : null}

      {result !== null ? (
        <section className={styles.panel} aria-labelledby="product-import-result">
          <h2 id="product-import-result">Kaydedilen ürünler</h2>
          <ul>{result.products.map((product) => <li key={product.productId}>
            <Link to={productDetailPath(product.productId)}>{product.code} — {product.name}</Link>
            <span> (eski kod: {product.legacyCode})</span>
          </li>)}</ul>
        </section>
      ) : null}
    </section>
  )
}
