import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { fetchIdentity, type IdentitySummary } from '../shell/api'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import { StateBlock } from '../shell/StateBlock'
import { COMPANY_SETUP_PATH, isSetupId, record, type AdminCandidate, type CompanySetupCapabilities, type OpenCompanyRequest, type SelectFirstAdminRequest, type SetupCompany } from './companySetupContract'
import { readAdminCandidates, readPendingSetups, readSetupCapabilities, sendCompanySetup, setupHttpState } from './companySetupApi'
import { COMPANY_SETUP_TEXT as T } from './companySetupText'
import styles from './CompanySetupPage.module.css'

type SetupData = { identity: IdentitySummary; capabilities: CompanySetupCapabilities; companies: readonly SetupCompany[]; candidates: readonly AdminCandidate[] }
type Attempt = { readonly kind: 'open'; readonly companyId: string; readonly key: string; readonly request: OpenCompanyRequest }
  | { readonly kind: 'assign'; readonly companyId: string; readonly key: string; readonly request: SelectFirstAdminRequest }
export function restoreSetupAttempt(value: string | null): Attempt | null {
  if (value === null) return null
  try {
    const saved: unknown = JSON.parse(value)
    if (!record(saved) || !isSetupId(saved.companyId) || !isSetupId(saved.key) || !record(saved.request)) return null
    if (Object.keys(saved).length !== 4 || !['kind', 'companyId', 'key', 'request'].every(field => Object.hasOwn(saved, field))) return null
    const request = saved.request
    if (saved.kind === 'open' && Object.keys(request).length === 6 && request.companyId === saved.companyId && request.expectedVersion === '0'
      && ['code', 'legalName', 'vkn', 'reason'].every(field => typeof request[field] === 'string' && (request[field] as string).trim().length > 0)) return saved as Attempt
    if (saved.kind === 'assign' && Object.keys(request).length === 3 && isSetupId(request.userId) && typeof request.reason === 'string'
      && Number.isSafeInteger(request.expectedCompanyVersion) && (request.expectedCompanyVersion as number) > 0) return saved as Attempt
  } catch { /* Untrusted local storage never becomes a command. */ }
  return null
}
async function readSetupIdentity(signal?: AbortSignal): Promise<RemoteViewState<IdentitySummary>> {
  const outcome = await fetchIdentity(signal)
  if (outcome.type === 'ok-single') return { kind: 'ready', data: outcome.data }
  // The context-free identity contract is one authenticated actor, never a list.
  if (outcome.type === 'ok') return { kind: 'malformed-response' }
  const state = classifyOutcome<never>(outcome)
  return state.kind === 'ready' ? { kind: 'malformed-response' } : state
}
const sameIdentity = (left: IdentitySummary, right: IdentitySummary) => left.userId === right.userId
  && left.authenticationSessionBinding === right.authenticationSessionBinding && !right.mustChangePassword

/** This group setup route works without an operational company. Its gate is its own server authority response. */
export function CompanySetupPage() {
  const shell = useShellState()
  return <CompanySetupWorkspace key={`${shell.sessionEpoch}:${shell.requestFenceGeneration}`} />
}
function CompanySetupWorkspace() {
  const boundary = useAuthBoundary()
  const [view, setView] = useState<RemoteViewState<SetupData>>({ kind: 'initial-loading' })
  const [reload, setReload] = useState(0)
  const [code, setCode] = useState(''), [legalName, setLegalName] = useState(''), [vkn, setVkn] = useState('')
  const [openReason, setOpenReason] = useState(''), [assignReason, setAssignReason] = useState('')
  const [companyId, setCompanyId] = useState(''), [userId, setUserId] = useState(''), [search, setSearch] = useState('')
  const [busy, setBusy] = useState(false), [notice, setNotice] = useState<string | null>(null)
  const [commandState, setCommandState] = useState<RemoteViewState<never> | null>(null)
  const [pending, setPending] = useState<Attempt | null>(null)
  const [storageUnsafe, setStorageUnsafe] = useState(false)
  const alive = useRef(true)
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  const report = (state: RemoteViewState<unknown>, snapshot: ReturnType<typeof boundary.capture>) => {
    if (state.kind === 'unauthenticated') boundary.report(401, snapshot)
    if (state.kind === 'forbidden') boundary.report(403, snapshot)
  }
  useEffect(() => {
    const controller = new AbortController(); const snapshot = boundary.capture()
    setView({ kind: 'initial-loading' }); setCompanyId(''); setUserId('')
    void (async () => {
      const identity = await readSetupIdentity(controller.signal)
      if (controller.signal.aborted) return
      if (identity.kind !== 'ready') { report(identity, snapshot); setView(identity); return }
      if (identity.data.mustChangePassword) { setNotice(T.password); setView({ kind: 'forbidden' }); return }
      const capabilities = await readSetupCapabilities(controller.signal)
      if (controller.signal.aborted) return
      if (capabilities.kind !== 'ready') { report(capabilities, snapshot); setView(capabilities); return }
      if (!capabilities.data.canOpenCompany && !capabilities.data.canAssignFirstAdmin) { setView({ kind: 'not-found' }); return }
      const companies = capabilities.data.canAssignFirstAdmin ? await readPendingSetups(controller.signal) : { kind: 'ready' as const, data: [] }
      const candidates = capabilities.data.canAssignFirstAdmin ? await readAdminCandidates('', controller.signal) : { kind: 'ready' as const, data: [] }
      const finalIdentity = await readSetupIdentity(controller.signal)
      if (controller.signal.aborted) return
      if (finalIdentity.kind !== 'ready' || !sameIdentity(identity.data, finalIdentity.data)) {
        setView(finalIdentity.kind === 'ready' ? { kind: 'stale-rejected' } : finalIdentity); return
      }
      if (companies.kind !== 'ready') { report(companies, snapshot); setView(companies); return }
      if (candidates.kind !== 'ready') { report(candidates, snapshot); setView(candidates); return }
      setView({ kind: 'ready', data: { identity: identity.data, capabilities: capabilities.data, companies: companies.data, candidates: candidates.data } })
      const storage = storageKey(identity.data)
      if (storage !== null) {
        try {
          const raw = sessionStorage.getItem(storage); const restored = restoreSetupAttempt(raw)
          setStorageUnsafe(raw !== null && restored === null); setPending(restored)
          if (raw !== null && restored === null) setNotice(T.pendingInvalid)
        } catch { setStorageUnsafe(true); setNotice(T.session) }
      }
    })()
    return () => controller.abort()
    // A new boundary binding aborts the old page read; its response can never fill the new actor's form.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reload, boundary])
  const refresh = () => { setCommandState(null); setReload(value => value + 1) }
  const send = async (attempt: Attempt, identity: IdentitySummary) => {
    if (busy) return
    setBusy(true); setNotice(null); setCommandState(null)
    const snapshot = boundary.capture()
    const before = await readSetupIdentity()
    if (!alive.current) return
    if (before.kind !== 'ready' || !sameIdentity(identity, before.data)) {
      const failed = before.kind === 'ready' ? { kind: 'stale-rejected' as const } : before
      report(failed, snapshot); setView(failed); setBusy(false); return
    }
    const result = await sendCompanySetup(attempt.kind, attempt.companyId, attempt.request, attempt.key)
    const after = await readSetupIdentity()
    if (!alive.current) return
    setBusy(false)
    if (after.kind !== 'ready' || !sameIdentity(identity, after.data)) {
      const failed = after.kind === 'ready' ? { kind: 'stale-rejected' as const } : after
      report(failed, snapshot); setView(failed); return
    }
    if (result.type === 'unknown') { setCommandState(result.state); return }
    const storage = storageKey(identity)
    if (storage !== null) { try { sessionStorage.removeItem(storage) } catch { /* Kept terminal keys replay safely. */ } }
    setPending(null)
    if (result.type === 'applied') { setNotice(result.replayed ? T.replayed : T.saved); refresh(); return }
    if (result.status === 400 || result.status === 422) { setNotice(T.badRequest); return }
    const state = setupHttpState(result.status); report(state, snapshot); setCommandState(state)
  }
  const stage = (attempt: Attempt) => {
    if (view.kind !== 'ready' || pending !== null || busy || storageUnsafe) return
    const storage = storageKey(view.data.identity)
    if (storage === null) { setNotice(T.session); return }
    try { sessionStorage.setItem(storage, JSON.stringify(attempt)) } catch { setNotice(T.session); return }
    setPending(attempt); void send(attempt, view.data.identity)
  }
  const open = (event: FormEvent) => {
    event.preventDefault()
    if (![code, legalName, vkn, openReason].every(value => value.trim())) { setNotice(T.missing); return }
    const id = crypto.randomUUID()
    stage({ kind: 'open', companyId: id, key: crypto.randomUUID(), request: { companyId: id, code, legalName, vkn, expectedVersion: '0', reason: openReason } })
  }
  const assign = (event: FormEvent) => {
    event.preventDefault()
    const company = view.kind === 'ready' ? view.data.companies.find(row => row.companyId === companyId) : null
    if (!company || !userId || !assignReason.trim()) { setNotice(T.missing); return }
    stage({ kind: 'assign', companyId, key: crypto.randomUUID(), request: { userId, expectedCompanyVersion: company.version, reason: assignReason } })
  }
  const searchCandidates = async () => {
    if (view.kind !== 'ready' || busy || pending) return
    const previous = view.data; setBusy(true)
    const candidates = await readAdminCandidates(search)
    const identity = await readSetupIdentity()
    if (!alive.current) return
    setBusy(false)
    if (identity.kind !== 'ready' || !sameIdentity(previous.identity, identity.data)) { setView({ kind: 'stale-rejected' }); return }
    if (candidates.kind !== 'ready') { setCommandState(candidates); return }
    setView({ kind: 'ready', data: { ...previous, candidates: candidates.data } }); setUserId('')
  }
  const disabled = busy || storageUnsafe || pending !== null || view.kind !== 'ready' || storageKey(view.data.identity) === null
  return <main className={styles.page} aria-labelledby="company-setup-heading">
    <h1 id="company-setup-heading">{T.title}</h1><p>{T.lead}</p><Link to="/">{T.home}</Link>
    {notice && <p role="status">{notice}</p>}
    {view.kind !== 'ready' ? <StateBlock state={view} subject={T.title} onRetry={refresh} /> : <>
      {storageKey(view.data.identity) === null && <p role="alert">{T.session}</p>}
      {commandState && <StateBlock state={commandState} subject={T.title} />}
      {pending !== null && <Button type="button" disabled={busy} onClick={() => { if (view.kind === 'ready') void send(pending, view.data.identity) }}>{busy ? T.busy : T.retry}</Button>}
      {view.data.capabilities.canOpenCompany && <section className={styles.section}><h2>{T.open}</h2>
        <form onSubmit={open} className={styles.form} aria-busy={busy}>
          <Field label={T.code}>{props => <input {...props} value={code} onChange={event => setCode(event.target.value)} required maxLength={64} disabled={disabled} />}</Field>
          <Field label={T.legalName}>{props => <input {...props} value={legalName} onChange={event => setLegalName(event.target.value)} required maxLength={250} disabled={disabled} />}</Field>
          <Field label={T.vkn}>{props => <input {...props} value={vkn} onChange={event => setVkn(event.target.value)} required inputMode="numeric" maxLength={10} disabled={disabled} />}</Field>
          <Field label={T.reason}>{props => <textarea {...props} value={openReason} onChange={event => setOpenReason(event.target.value)} required maxLength={500} disabled={disabled} />}</Field>
          <Button type="submit" disabled={disabled}>{T.open}</Button>
        </form></section>}
      {view.data.capabilities.canAssignFirstAdmin && <section className={styles.section}><h2>{T.pending}</h2>
        {view.data.companies.length === 0 ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.pending} /> : <form onSubmit={assign} className={styles.form}>
          <Field label={T.company}>{props => <select {...props} value={companyId} onChange={event => { setCompanyId(event.target.value); setUserId('') }} required disabled={disabled}>
            <option value="">{T.choose}</option>{view.data.companies.map(row => <option key={row.companyId} value={row.companyId}>{row.code} — {row.legalName}</option>)}
          </select>}</Field>
          <Field label={T.search}>{props => <input {...props} value={search} onChange={event => setSearch(event.target.value)} maxLength={100} disabled={disabled} />}</Field>
          <Button type="button" disabled={disabled} onClick={() => void searchCandidates()}>{T.searchAction}</Button>
          {view.data.candidates.length === 0 && <p>{T.noCandidates}</p>}
          <Field label={T.user}>{props => <select {...props} value={userId} onChange={event => setUserId(event.target.value)} required disabled={disabled}>
            <option value="">{T.choose}</option>{view.data.candidates.map(row => <option key={row.userId} value={row.userId}>{row.displayName}</option>)}
          </select>}</Field>
          <Field label={T.reason}>{props => <textarea {...props} value={assignReason} onChange={event => setAssignReason(event.target.value)} required maxLength={500} disabled={disabled} />}</Field>
          <Button type="submit" disabled={disabled}>{T.assign}</Button>
        </form>}
        <Button type="button" disabled={busy || pending !== null} onClick={refresh}>{T.reload}</Button>
      </section>}
    </>}
  </main>
}
function storageKey(identity: IdentitySummary): string | null {
  return identity.authenticationSessionBinding === null ? null
    : `goymen.company-setup.v1:${identity.userId}:${identity.authenticationSessionBinding}`
}

/** Availability is server-resolved group authority, rather than an operational company capability. */
export function CompanySetupLink({ binding }: { readonly binding: string }) {
  const [allowed, setAllowed] = useState(false)
  useEffect(() => {
    const controller = new AbortController(); setAllowed(false)
    void readSetupCapabilities(controller.signal).then(value => {
      if (!controller.signal.aborted) setAllowed(value.kind === 'ready' && (value.data.canOpenCompany || value.data.canAssignFirstAdmin))
    })
    return () => controller.abort()
  }, [binding])
  return allowed ? <Link to={COMPANY_SETUP_PATH}>{T.title}</Link> : null
}
