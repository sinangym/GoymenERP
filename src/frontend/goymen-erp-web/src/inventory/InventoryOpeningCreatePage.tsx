import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import {
  fetchOpeningLocations,
  fetchOpeningProducts,
  recordOpeningCount,
  type RecordOpeningCountOutcome,
} from './api'
import { INVENTORY_OPENING_ROUTE, inventoryOpeningDetailPath } from './capabilities'
import type {
  OpeningCountRequest,
  OrganizationLocationCandidates,
  ProductStockCandidate,
  ProductStockCandidates,
} from './contract'
import { isDateOnly } from './contract'
import {
  lastOpeningCountKey,
  pendingOpeningCountKey,
  recallPendingOpeningCount,
  releasePendingOpeningCount,
  rememberLastOpeningCount,
  reservePendingOpeningCount,
  type PendingOpeningCount,
} from './pendingOpeningStore'
import { parseQuantityInput } from '../sales/wirePrimitives'
import { createRequestFence } from '../purchasing/requestFence'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import styles from './inventory.module.css'

type ReadState<T> =
  | { readonly type: 'idle' | 'loading' }
  | { readonly type: 'ready'; readonly data: T }
  | { readonly type: 'failed'; readonly message: string }

type DraftLine = {
  readonly lineId: string
  readonly labelId: string
  readonly lotId: string
  readonly labelCode: string
  readonly heatNumber: string
  readonly coilNumber: string
  readonly productId: string
  readonly quantityKg: string
}

type Attempt =
  | { readonly phase: 'idle' }
  | { readonly phase: 'submitting'; readonly snapshot: PendingOpeningCount }
  | { readonly phase: 'unknown'; readonly snapshot: PendingOpeningCount; readonly reason: string }
  | { readonly phase: 'failed'; readonly message: string; readonly snapshot?: PendingOpeningCount; readonly unresolvedSince?: string }
  | { readonly phase: 'completed'; readonly result: Extract<RecordOpeningCountOutcome, { type: 'applied' }> }

function newId(): string { return globalThis.crypto.randomUUID() }
function newLine(): DraftLine {
  return { lineId: newId(), labelId: newId(), lotId: newId(), labelCode: '', heatNumber: '', coilNumber: '', productId: '', quantityKg: '' }
}
function selectedProduct(items: readonly ProductStockCandidate[], id: string) {
  return items.find((item) => item.productId === id)
}
function cleanOptional(value: string): string | null {
  const clean = value.trim()
  return clean.length === 0 ? null : clean
}

export function InventoryOpeningCreatePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <InventoryOpeningCreate key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function InventoryOpeningCreate({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const actorId = shell.activeContext?.userId ?? null
  const pendingKey = pendingOpeningCountKey(shell.sessionEpoch, actorId, companyId)
  const lastKey = lastOpeningCountKey(shell.sessionEpoch, actorId, companyId)
  const recalled = recallPendingOpeningCount(pendingKey)
  const [attempt, setAttempt] = useState<Attempt>(() => recalled === null
    ? { phase: 'idle' }
    : { phase: 'unknown', snapshot: recalled, reason: 'Önceki açılış kaydının sonucu bilinmiyor.' })
  const [countedOn, setCountedOn] = useState('')
  const [sheetReference, setSheetReference] = useState('')
  const [locationSearch, setLocationSearch] = useState('')
  const [productSearch, setProductSearch] = useState('')
  const [locationId, setLocationId] = useState('')
  const [locations, setLocations] = useState<ReadState<OrganizationLocationCandidates>>({ type: 'idle' })
  const [products, setProducts] = useState<ReadState<ProductStockCandidates>>({ type: 'idle' })
  const [lines, setLines] = useState<readonly DraftLine[]>(() => [newLine()])
  const [formError, setFormError] = useState<string | null>(null)
  const locationFence = useRef(createRequestFence())
  const productFence = useRef(createRequestFence())
  const commandGeneration = useRef(0)
  const inFlight = useRef(false)

  const readLocations = useCallback(async () => {
    if (companyId === null) return
    const token = locationFence.current.capture()
    const security = boundary.capture()
    setLocations({ type: 'loading' })
    const outcome = await fetchOpeningLocations(companyId, locationSearch, token.signal)
    if (!locationFence.current.isCurrent(token)) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    setLocations(outcome.type === 'ok-single'
      ? { type: 'ready', data: outcome.data }
      : { type: 'failed', message: outcome.type === 'http' ? `Lokasyon kaynağı yanıt vermedi (${outcome.status}).` : 'Lokasyon kaynağına ulaşılamadı veya yanıt doğrulanamadı.' })
  }, [boundary, companyId, locationSearch])

  const readProducts = useCallback(async () => {
    if (companyId === null || !isDateOnly(countedOn)) {
      setFormError('Ürün aramadan önce geçerli bir sayım tarihi seçin.')
      return
    }
    const token = productFence.current.capture()
    const security = boundary.capture()
    setProducts({ type: 'loading' })
    const outcome = await fetchOpeningProducts(companyId, countedOn, productSearch, token.signal)
    if (!productFence.current.isCurrent(token)) return
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    setProducts(outcome.type === 'ok-single'
      ? { type: 'ready', data: outcome.data }
      : { type: 'failed', message: outcome.type === 'http' ? `Ürün kaynağı yanıt vermedi (${outcome.status}).` : 'Ürün kaynağına ulaşılamadı veya yanıt doğrulanamadı.' })
  }, [boundary, companyId, countedOn, productSearch])

  useEffect(() => () => {
    locationFence.current.invalidate()
    productFence.current.invalidate()
    commandGeneration.current += 1
  }, [namespace, shell.routeEntryGeneration])

  const send = useCallback(async (snapshot: PendingOpeningCount, unresolvedSince?: string) => {
    if (companyId === null || actorId === null || snapshot.companyId !== companyId || snapshot.actorId !== actorId || inFlight.current) return
    inFlight.current = true
    const generation = ++commandGeneration.current
    reservePendingOpeningCount(pendingKey, snapshot)
    setAttempt({ phase: 'submitting', snapshot })
    const security = boundary.capture()
    const outcome = await recordOpeningCount(snapshot.payload, { companyId, idempotencyKey: snapshot.idempotencyKey })
    if (generation !== commandGeneration.current) return
    inFlight.current = false
    if (outcome.type === 'failed' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    if (outcome.type === 'unknown') {
      setAttempt({ phase: 'unknown', snapshot, reason: unresolvedSince ?? 'Sonuç bilinmiyor. Aynı anahtar ve içerikle yeniden gönderin.' })
      return
    }
    if (outcome.type === 'failed' && unresolvedSince !== undefined) {
      setAttempt({ phase: 'failed', snapshot, unresolvedSince, message: `Açılış sayımı kaydedilmedi (${outcome.code ?? outcome.status}).` })
      return
    }
    releasePendingOpeningCount(pendingKey, snapshot.idempotencyKey)
    if (outcome.type === 'applied') {
      rememberLastOpeningCount(lastKey, { actorId, companyId, countId: outcome.response.countId })
      setAttempt({ phase: 'completed', result: outcome })
      return
    }
    setAttempt({ phase: 'failed', message: `Açılış sayımı kaydedilmedi (${outcome.code ?? outcome.status}).` })
  }, [actorId, boundary, companyId, lastKey, pendingKey])

  function submit(event: FormEvent) {
    event.preventDefault()
    if ((attempt.phase !== 'idle' && attempt.phase !== 'failed') || companyId === null || actorId === null ||
      locations.type !== 'ready' || products.type !== 'ready') return
    if (!locations.data.items.some((item) => item.locationId === locationId) ||
      !isDateOnly(countedOn) || sheetReference.trim().length === 0) {
      setFormError('Sayım tarihi, belge referansı ve yetkili lokasyon zorunludur.')
      return
    }
    const payloadLines: OpeningCountRequest['lines'][number][] = []
    const labels = new Set<string>()
    for (const [index, line] of lines.entries()) {
      const product = selectedProduct(products.data.items, line.productId)
      const parsed = parseQuantityInput(line.quantityKg)
      const labelCode = line.labelCode.trim()
      if (product === undefined || !parsed.ok || labelCode.length === 0 || labels.has(labelCode)) {
        setFormError(`Parça ${index + 1}: gerçek ürün, benzersiz etiket kodu ve sıfırdan büyük kg zorunludur.`)
        return
      }
      labels.add(labelCode)
      payloadLines.push({
        lineId: line.lineId,
        labelId: line.labelId,
        labelCode,
        lotId: line.lotId,
        heatNumber: cleanOptional(line.heatNumber),
        coilNumber: cleanOptional(line.coilNumber),
        productId: product.productId,
        productVersion: product.productVersion,
        quantityKg: parsed.value,
      })
    }
    setFormError(null)
    const payload: OpeningCountRequest = {
      countId: newId(), locationId, countedOn, sheetReference: sheetReference.trim(), lines: payloadLines,
    }
    void send({ actorId, companyId, idempotencyKey: newId(), payload })
  }

  if (companyId === null || actorId === null) {
    return <main className={styles.page}><p role="alert">Yetkili tek şirket bağlamı seçilmeden açılış stoku kaydedilemez.</p></main>
  }
  if (attempt.phase === 'completed') {
    const count = attempt.result.response
    return <main className={styles.page}><section className={styles.successCard} role="status">
      <p className={styles.eyebrow}>ST-05-OPENING</p><h1>Açılış sayımı kaydedildi</h1>
      <p className={styles.mono}>{count.countId} · {count.lines.length} parça</p>
      {attempt.result.replayed ? <p>Önceki kayıtlı sonuç yeniden gösterildi.</p> : null}
      <div className={styles.actions}>
        <Link className={styles.primaryLink} to={inventoryOpeningDetailPath(count.countId)}>Aynı kaydı aç</Link>
        <Link to={INVENTORY_OPENING_ROUTE.home}>Formu kapat</Link>
      </div>
    </section></main>
  }
  const frozen = attempt.phase === 'submitting' || attempt.phase === 'unknown' ||
    (attempt.phase === 'failed' && attempt.unresolvedSince !== undefined)
  return <main className={styles.page}>
    <header className={styles.header}><div><p className={styles.eyebrow}>ST-05-OPENING</p><h1>Yeni açılış sayımı</h1></div><Link to={INVENTORY_OPENING_ROUTE.home}>← İlk stok</Link></header>
    {attempt.phase === 'unknown' ? <section className={styles.warningCard} role="alert"><p>{attempt.reason}</p><div className={styles.actions}><button type="button" onClick={() => void send(attempt.snapshot, attempt.reason)}>Aynı anahtarla yeniden gönder</button><Link to={INVENTORY_OPENING_ROUTE.home}>Formu kapat</Link></div></section> : null}
    {attempt.phase === 'failed' && attempt.unresolvedSince !== undefined && attempt.snapshot !== undefined
      ? <section className={styles.warningCard} role="alert"><p>{attempt.message} Önceki gönderimin sonucu hâlâ bilinmiyor.</p><div className={styles.actions}><button type="button" onClick={() => void send(attempt.snapshot!, attempt.unresolvedSince)}>Aynı anahtarla yeniden gönder</button><Link to={INVENTORY_OPENING_ROUTE.home}>Formu kapat</Link></div></section>
      : attempt.phase === 'failed' ? <p role="alert" className={styles.error}>{attempt.message}</p> : null}
    {formError === null ? null : <p role="alert" className={styles.error}>{formError}</p>}
    <form className={styles.form} onSubmit={submit} noValidate>
      <fieldset disabled={frozen}><legend>Sayım kaynağı</legend>
        <div className={styles.grid}>
          <label htmlFor="opening-date">Sayım tarihi</label><input id="opening-date" type="date" value={countedOn} onChange={(event) => { productFence.current.invalidate(); setCountedOn(event.target.value); setProducts({ type: 'idle' }); setLines((current) => current.map((line) => ({ ...line, productId: '' }))) }} />
          <label htmlFor="opening-sheet">Belge / sayım referansı</label><input id="opening-sheet" value={sheetReference} onChange={(event) => setSheetReference(event.target.value)} />
          <label htmlFor="opening-location-search">Lokasyon ara</label><div className={styles.searchRow}><input id="opening-location-search" value={locationSearch} onChange={(event) => setLocationSearch(event.target.value)} /><button type="button" onClick={() => void readLocations()}>Lokasyonları getir</button></div>
          <label htmlFor="opening-location">Lokasyon</label><select id="opening-location" value={locationId} onChange={(event) => setLocationId(event.target.value)}><option value="">Seçiniz</option>{locations.type === 'ready' ? locations.data.items.map((item) => <option key={item.locationId} value={item.locationId}>{item.siteCode} / {item.locationCode} — {item.locationName}</option>) : null}</select>
          <label htmlFor="opening-product-search">Ürün ara</label><div className={styles.searchRow}><input id="opening-product-search" value={productSearch} onChange={(event) => setProductSearch(event.target.value)} /><button type="button" onClick={() => void readProducts()}>Ürünleri getir</button></div>
        </div>
        {locations.type === 'loading' ? <p role="status">Lokasyonlar yükleniyor…</p> : locations.type === 'failed' ? <p role="alert">{locations.message}</p> : null}
        {products.type === 'loading' ? <p role="status">Ürünler yükleniyor…</p> : products.type === 'failed' ? <p role="alert">{products.message}</p> : null}
      </fieldset>
      <fieldset disabled={frozen}><legend>Fiziksel parçalar</legend>
        {lines.map((line, index) => <section className={styles.lineCard} key={line.lineId} aria-label={`Parça ${index + 1}`}>
          <div className={styles.grid}>
            <label htmlFor={`opening-product-${line.lineId}`}>Ürün</label><select id={`opening-product-${line.lineId}`} value={line.productId} onChange={(event) => setLines((current) => current.map((item) => item.lineId === line.lineId ? { ...item, productId: event.target.value } : item))}><option value="">Seçiniz</option>{products.type === 'ready' ? products.data.items.map((item) => <option key={item.productId} value={item.productId}>{item.code} — {item.name} · {item.stockUnitCode}</option>) : null}</select>
            <label htmlFor={`opening-label-${line.lineId}`}>Etiket kodu</label><input id={`opening-label-${line.lineId}`} value={line.labelCode} onChange={(event) => setLines((current) => current.map((item) => item.lineId === line.lineId ? { ...item, labelCode: event.target.value } : item))} />
            <label htmlFor={`opening-quantity-${line.lineId}`}>Sayım kg</label><input id={`opening-quantity-${line.lineId}`} inputMode="decimal" value={line.quantityKg} onChange={(event) => setLines((current) => current.map((item) => item.lineId === line.lineId ? { ...item, quantityKg: event.target.value } : item))} />
            <label htmlFor={`opening-heat-${line.lineId}`}>Döküm no (opsiyonel)</label><input id={`opening-heat-${line.lineId}`} value={line.heatNumber} onChange={(event) => setLines((current) => current.map((item) => item.lineId === line.lineId ? { ...item, heatNumber: event.target.value } : item))} />
            <label htmlFor={`opening-coil-${line.lineId}`}>Rulo no (opsiyonel)</label><input id={`opening-coil-${line.lineId}`} value={line.coilNumber} onChange={(event) => setLines((current) => current.map((item) => item.lineId === line.lineId ? { ...item, coilNumber: event.target.value } : item))} />
          </div>
          {lines.length > 1 ? <button type="button" className={styles.secondary} onClick={() => setLines((current) => current.filter((item) => item.lineId !== line.lineId))}>Parçayı kaldır</button> : null}
        </section>)}
        <button type="button" className={styles.secondary} onClick={() => setLines((current) => [...current, newLine()])}>Parça ekle</button>
      </fieldset>
      <div className={styles.actions}><button type="submit" disabled={frozen}>{attempt.phase === 'submitting' ? 'Kaydediliyor…' : 'Açılış sayımını kaydet'}</button></div>
    </form>
  </main>
}
