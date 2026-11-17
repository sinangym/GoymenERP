import { useEffect, useState } from 'react'
import { MANUFACTURING_BASE, devHeaders } from './api'
import { decodePaintCatalog, type PaintCatalog, type PlanPaintSelection } from './paintSelectionContract'
import type { PlanProcessSpecView } from './contract'
import { PAINT_SELECTION_TEXT as T } from './paintSelectionText'
import { useAuthBoundary } from '../shell/useAuthBoundary'

type State = { type: 'loading' } | { type: 'failed'; text: string } | { type: 'ready'; data: PaintCatalog }
export function PaintSelectionEditor({ companyId, value, disabled, onChange }: {
  companyId: string | null; value: PlanProcessSpecView | null; disabled: boolean; onChange: (value: PlanProcessSpecView) => void
}) {
  const { capture, report } = useAuthBoundary()
  const [readState, setState] = useState<State>({ type: 'loading' })
  const state: State = readState.type === 'ready' && readState.data.companyId !== companyId ? { type: 'loading' } : readState
  const [query, setQuery] = useState('')
  const [search, setSearch] = useState('')
  const [reload, setReload] = useState(0)
  const [productId, setProductId] = useState(value?.paintSelection?.productId ?? '')
  const [supplierId, setSupplierId] = useState(value?.paintSelection?.supplierCompanyAccountId ?? '')
  useEffect(() => {
    const controller = new AbortController()
    const security = capture()
    setState({ type: 'loading' })
    if (companyId === null) return () => controller.abort()
    void (async () => {
      try {
        const response = await fetch(`${MANUFACTURING_BASE}/references/paint-selections?q=${encodeURIComponent(search)}`, {
          headers: { ...devHeaders(), 'X-Company-Id': companyId }, cache: 'no-store', signal: controller.signal,
        })
        if (controller.signal.aborted) return
        if (response.status === 401 || response.status === 403) report(response.status, security)
        if (!response.ok) {
          setState({ type: 'failed', text: response.status === 401 || response.status === 403 ? T.denied : response.status === 404 ? T.missing : T.unavailable }); return
        }
        const data = decodePaintCatalog(await response.json(), companyId)
        if (!controller.signal.aborted) setState(data === undefined ? { type: 'failed', text: T.malformed } : { type: 'ready', data })
      } catch { if (!controller.signal.aborted) setState({ type: 'failed', text: navigator.onLine ? T.unavailable : T.offline }) }
    })()
    return () => controller.abort()
  }, [capture, companyId, reload, report, search])

  function choose(product: string, supplier: string, colorId: string | null, note: string | null, changed: 'product' | 'supplier' | 'color' | 'note') {
    if (state.type !== 'ready' || disabled) return
    const p = state.data.products.find(x => x.productId === product)
    const s = state.data.suppliers.find(x => x.companyAccountId === supplier)
    if (p === undefined || s === undefined) {
      onChange({ packageCount: null, piecesPerPackage: null, pieceLengthMm: null, unitKg: null, perimeterMm: null,
        targetThicknessMm: null, paintDescription: null, paintColorCode: null, ...value, paintSelection: undefined }); return
    }
    const prior = value?.paintSelection
    const c = state.data.colors.find(x => x.id === colorId)
    const selection: PlanPaintSelection = {
      productId: p.productId,
      productVersion: changed !== 'product' && prior?.productId === product ? prior.productVersion : p.productVersion,
      membershipVersion: changed !== 'product' && prior?.productId === product ? prior.membershipVersion : p.membershipVersion,
      productSourceVersion: changed !== 'product' && prior?.productId === product ? prior.productSourceVersion : p.sourceVersion,
      supplierPartyId: s.partyId, supplierCompanyAccountId: s.companyAccountId,
      supplierPartyVersion: changed !== 'supplier' && prior?.supplierCompanyAccountId === supplier ? prior.supplierPartyVersion : s.partyVersion,
      supplierAccountVersion: changed !== 'supplier' && prior?.supplierCompanyAccountId === supplier ? prior.supplierAccountVersion : s.accountVersion,
      colorId, colorVersion: changed !== 'color' && prior?.colorId === colorId ? prior.colorVersion : c?.version ?? null,
    }
    onChange({ packageCount: null, piecesPerPackage: null, pieceLengthMm: null, unitKg: null, perimeterMm: null,
      targetThicknessMm: null, ...value, paintDescription: null, paintColorCode: note, paintSelection: selection })
  }
  const current = value?.paintSelection
  const stale = state.type === 'ready' && current !== undefined && (!state.data.products.some(p => p.productId === current.productId
    && p.productVersion === current.productVersion && p.membershipVersion === current.membershipVersion && p.sourceVersion === current.productSourceVersion)
    || !state.data.suppliers.some(s => s.companyAccountId === current.supplierCompanyAccountId && s.partyVersion === current.supplierPartyVersion
      && s.accountVersion === current.supplierAccountVersion) || current.colorId !== null && !state.data.colors.some(c => c.id === current.colorId && c.version === current.colorVersion))
  return <fieldset disabled={disabled}><legend>{T.heading}</legend>
    <label>{T.search}<input value={query} onChange={e => setQuery(e.target.value)} maxLength={100} /></label>
    <button type="button" onClick={() => { setSearch(query); setReload(x => x + 1) }}>{T.retry}</button>
    {state.type === 'loading' ? <p role="status">{T.loading}</p> : null}
    {state.type === 'failed' ? <p role="alert">{state.text}</p> : null}
    {stale ? <p role="alert">{T.stale}</p> : null}
    {state.type === 'ready' ? <>
      {state.data.products.length === 0 ? <p role="status">{T.emptyProducts}</p> : null}
      {state.data.suppliers.length === 0 ? <p role="status">{T.emptySuppliers}</p> : null}
      {state.data.supplierSearchWindowFull ? <p role="status">{T.more}</p> : null}
      <label>{T.product}<select value={productId} onChange={e => { setProductId(e.target.value); choose(e.target.value, supplierId, current?.colorId ?? null, value?.paintColorCode ?? null, 'product') }}>
        <option value="">{T.choose}</option>{state.data.products.map(p => <option key={p.productId} value={p.productId}>{p.code} — {p.name}</option>)}</select></label>
      <label>{T.supplier}<select value={supplierId} onChange={e => { setSupplierId(e.target.value); choose(productId, e.target.value, current?.colorId ?? null, value?.paintColorCode ?? null, 'supplier') }}>
        <option value="">{T.choose}</option>{state.data.suppliers.map(s => <option key={s.companyAccountId} value={s.companyAccountId}>{s.code} — {s.name}</option>)}</select></label>
      <label>{T.color}<select disabled={current === undefined} value={current?.colorId ?? ''} onChange={e => choose(productId, supplierId, e.target.value || null, value?.paintColorCode ?? null, 'color')}>
        <option value="">{T.none}</option>{state.data.colors.map(c => <option key={c.id} value={c.id}>{c.code} — {c.name}</option>)}</select></label>
      <label>{T.note}<input disabled={current === undefined} maxLength={32} value={value?.paintColorCode ?? ''} onChange={e => choose(productId, supplierId, current?.colorId ?? null, e.target.value.trim() || null, 'note')} /></label>
    </> : null}
  </fieldset>
}
