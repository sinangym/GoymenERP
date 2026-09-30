import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { applyProductImport, previewProductImport } from './importApi'
import {
  productImportErrorsCsv,
  type ProductImportColumnMapping,
  type ProductImportPreview,
  type ProductImportResult,
} from './importContract'
import { PRODUCT_CAPABILITY_ROUTE, PRODUCT_CAPABILITY, productDetailPath } from './capabilities'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './ProductImportPage.module.css'

const EMPTY_MAPPING: ProductImportColumnMapping = { legacyCode: '', name: '', groupId: '' }

type RemoteState = 'idle' | 'loading' | 'ready' | 'error'

export function ProductImportPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <ProductImportForm key={namespace ?? 'no-context'} />
}

function ProductImportForm() {
  const shell = useShellState()
  const origin = shell.activeContext?.companyId ?? null
  const boundary = useAuthBoundary()
  const [file, setFile] = useState<File | null>(null)
  const [mapping, setMapping] = useState<ProductImportColumnMapping>(EMPTY_MAPPING)
  const [preview, setPreview] = useState<ProductImportPreview | null>(null)
  const [result, setResult] = useState<ProductImportResult | null>(null)
  const [previewState, setPreviewState] = useState<RemoteState>('idle')
  const [applyState, setApplyState] = useState<RemoteState>('idle')
  const [message, setMessage] = useState<string | null>(null)
  const [attemptKey, setAttemptKey] = useState<string | null>(null)
  const active = useRef<AbortController | null>(null)

  useEffect(() => () => active.current?.abort(), [])

  const mappingReady = [mapping.legacyCode, mapping.name, mapping.groupId].every((value) => value.trim().length > 0)
    && new Set([mapping.legacyCode.trim(), mapping.name.trim(), mapping.groupId.trim()]).size === 3

  function resetServerState() {
    active.current?.abort()
    active.current = null
    setPreview(null)
    setResult(null)
    setPreviewState('idle')
    setApplyState('idle')
    setMessage(null)
    setAttemptKey(null)
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
    setResult(null)
    setAttemptKey(null)
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

  async function apply() {
    if (file === null || preview?.ready !== true || origin === null) return
    const key = attemptKey ?? `product-import-${crypto.randomUUID()}`
    setAttemptKey(key)
    active.current?.abort()
    const controller = new AbortController()
    active.current = controller
    const auth = boundary.capture()
    setApplyState('loading')
    setMessage('İçe aktarma uygulanıyor…')
    const outcome = await applyProductImport(file, mapping, preview, key, { companyId: origin }, controller.signal)
    if (controller.signal.aborted) return
    if (outcome.type === 'http') boundary.report(outcome.status, auth)
    if (outcome.type === 'succeeded') {
      setResult(outcome.data)
      setApplyState('ready')
      setMessage(`${outcome.data.products.length} ürün taslağı kaydedildi.`)
      setAttemptKey(null)
      return
    }
    setApplyState('error')
    setMessage(outcome.type === 'offline' ? 'Sonuç bilinmiyor. Aynı istek anahtarıyla yeniden deneyin.'
      : outcome.type === 'network-error' || outcome.type === 'aborted' || outcome.type === 'malformed'
        ? 'Sonuç doğrulanamadı. Yeni istek oluşturmadan aynı işlemi yeniden deneyin.'
        : 'İçe aktarma reddedildi. Hiçbir satır kaydedilmedi.')
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
            onChange={(event) => { resetServerState(); setFile(event.target.files?.[0] ?? null) }} />
        </label>
        <div className={styles.mapping}>
          <label>Eski kod sütunu<input value={mapping.legacyCode}
            onChange={(event) => updateMapping('legacyCode', event.target.value)} /></label>
          <label>Ürün adı sütunu<input value={mapping.name}
            onChange={(event) => updateMapping('name', event.target.value)} /></label>
          <label>Grup kimliği sütunu<input value={mapping.groupId}
            onChange={(event) => updateMapping('groupId', event.target.value)} /></label>
        </div>
        <button type="button" onClick={() => void runPreview()}
          disabled={file === null || !mappingReady || previewState === 'loading' || origin === null}>
          {previewState === 'loading' ? 'Önizleniyor…' : 'Önizle'}
        </button>
      </div>

      {message !== null ? <p role={previewState === 'error' || applyState === 'error' ? 'alert' : 'status'}>{message}</p> : null}

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
            <button type="button" onClick={() => void apply()} disabled={applyState === 'loading'}>
              {applyState === 'loading' ? 'Kaydediliyor…' : attemptKey === null ? 'Tümünü içe aktar' : 'Aynı isteği yeniden dene'}
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
