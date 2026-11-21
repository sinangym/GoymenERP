import { useLayoutEffect, useRef, useState, useSyncExternalStore } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { DOCUMENTS_API_BASE, uploadDocument } from './api'
import { OWNER_KIND, type DocumentRow } from './contract'
import { BRANDING_MANAGE, TEMPLATE_CATALOG, type AssetReference, type BrandingAsset, type BrandingCurrent,
  type TemplateCurrent, type TemplateModule } from './brandingContract'
import { fetchBranding, fetchTemplate, writeBranding, type BrandingCommand } from './brandingApi'
import { beginBrandingFlight, brandingAttempt, endBrandingFlight, setBrandingAttempt, subscribeBrandingAttempt,
  } from './brandingAttempt'
import { BRANDING_TEXT as T } from './brandingText'

export function CompanyDocumentBrandingPage() { return <BrandingRoute module={null} /> }
export function DocumentTemplatePage({ module }: { readonly module: TemplateModule }) { return <BrandingRoute module={module} /> }

function BrandingRoute({ module }: { readonly module: TemplateModule | null }) {
  const shell = useShellState(), key = contextKey(shell)
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  if (key === null || company === null) return <StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} />
  const allowed = module === null ? hasCapability(shell, BRANDING_MANAGE)
    : hasCapability(shell, TEMPLATE_CATALOG[module].prepare) || hasCapability(shell, TEMPLATE_CATALOG[module].publish)
  if (!allowed) return <StateBlock subject={T.subject} state={{ kind: 'forbidden' }} />
  const namespace = contextKeyString(key)
  return <BrandingEditor key={JSON.stringify([namespace, shell.sessionEpoch, shell.routeEntryGeneration, module])}
    shell={shell} company={company} module={module} namespace={namespace} />
}

function BrandingEditor({ shell, company, module, namespace }: {
  readonly shell: ShellState; readonly company: string; readonly module: TemplateModule | null; readonly namespace: string
}) {
  const boundary = useAuthBoundary()
  const binding = JSON.stringify([DOCUMENTS_API_BASE, namespace, shell.activeContext?.userId, shell.sessionEpoch, company, module])
  const pending = useSyncExternalStore(subscribeBrandingAttempt, () => brandingAttempt(binding), () => null)
  const alive = useRef(false)
  useLayoutEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  const [reason, setReason] = useState('')
  const [message, setMessage] = useState<string | null>(pending === null ? null : T.unknown)
  const [state, setState] = useState<RemoteViewState<never> | null>(null)
  const [selection, setSelection] = useState<{ logo: AssetReference | null; letterhead: AssetReference | null } | null>(null)
  const [uploaded, setUploaded] = useState<Partial<Record<'logo' | 'letterhead', DocumentRow>>>({})
  const [files, setFiles] = useState<Partial<Record<'logo' | 'letterhead', File>>>({})
  const query = useQuery({ queryKey: [namespace, 'documents-branding', module, shell.routeEntryGeneration], retry: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = module === null ? await fetchBranding(company, signal) : await fetchTemplate(company, module, signal)
      if (result.type === 'http') boundary.report(result.status, snapshot)
      return result
    },
  })
  const current = query.data?.type === 'ok-single' ? query.data.data : null
  const branding = module === null ? (current as BrandingCurrent | null)?.branding ?? null : (current as TemplateCurrent | null)?.currentBranding ?? null
  const template = module === null ? null : current as TemplateCurrent | null
  const policy = module === null ? (current as BrandingCurrent | null)?.uploadPolicy ?? null : null
  const picked = selection ?? { logo: ref(branding?.logo), letterhead: ref(branding?.letterhead) }
  const locked = pending !== null || query.isFetching
  const canPrepare = module !== null && hasCapability(shell, TEMPLATE_CATALOG[module].prepare)
  const canPublish = module !== null && hasCapability(shell, TEMPLATE_CATALOG[module].publish)
  const maker = template?.draft?.preparedBy === shell.activeContext?.userId

  async function execute() {
    const attempt = beginBrandingFlight(binding)
    if (attempt === null) return
    const snapshot = boundary.capture()
    if (alive.current) { setState(null); setMessage(null) }
    if (attempt.kind === 'upload') {
      const result = await uploadDocument(attempt.attempt)
      endBrandingFlight(binding)
      if (result.type === 'stored' || result.type === 'refused' && ![401, 403].includes(result.status)) setBrandingAttempt(binding, null)
      if (!alive.current) return
      if (result.type === 'stored') {
        const row = result.upload.document
        if (row.scanState === 'Clean' && row.downloadable) {
          setUploaded((old) => ({ ...old, [attempt.slot]: row }))
          setSelection({ ...picked, [attempt.slot]: { documentId: row.id, versionNo: row.versionNo } })
          setMessage(result.replayed ? T.replayed : T.applied)
        } else setMessage(T.cleanRequired)
      } else if (result.type === 'refused') {
        boundary.report(result.status, snapshot)
        if ([401, 403, 404, 409].includes(result.status)) setState({ kind: result.status === 401 ? 'unauthenticated' : result.status === 403 ? 'forbidden' : result.status === 404 ? 'not-found' : 'conflict' })
        else setMessage(T.fileInvalid)
      } else { setState({ kind: navigator.onLine ? 'service-unavailable' : 'offline' }); setMessage(T.unknown) }
      return
    }
    const result = await writeBranding(company, attempt.key, attempt.command)
    endBrandingFlight(binding)
    if (result.type === 'applied' || result.type === 'refused' && ![401, 403].includes(result.status)) setBrandingAttempt(binding, null)
    if (!alive.current) return
    if (result.type === 'applied') {
      setReason(''); setSelection(null); setUploaded({}); setMessage(result.result.isReplay ? T.replayed : T.applied)
      void query.refetch()
    } else if (result.type === 'unknown') { setState({ kind: result.reason }); setMessage(T.unknown) }
    else {
      boundary.report(result.status, snapshot)
      if ([401, 403, 404, 409].includes(result.status)) setState({ kind: result.status === 401 ? 'unauthenticated' : result.status === 403 ? 'forbidden' : result.status === 404 ? 'not-found' : 'conflict' })
      else setMessage(result.code === 'document_branding_asset_render_invalid' ? T.renderInvalid
        : result.code === 'document_branding_asset_type_not_allowed' ? T.fileInvalid
        : result.code === 'document_template_maker_cannot_publish' ? T.maker : T.invalid)
    }
  }
  function begin(command: BrandingCommand) {
    if (locked || current === null) return
    if (reason.trim() === '' || reason.trim().length > 500 || /[\u0000-\u001f\u007f-\u009f]/.test(reason)) { setMessage(T.invalid); return }
    setBrandingAttempt(binding, { kind: 'command', key: crypto.randomUUID(), command, sending: false }); void execute()
  }
  function upload(slot: 'logo' | 'letterhead') {
    const file = files[slot]
    if (locked || !policy?.canUpload || file === undefined) return
    if (file.size < 1 || file.size > policy.maximumUploadBytes || !policy.allowedContentTypes.includes(file.type)) { setMessage(T.fileInvalid); return }
    setBrandingAttempt(binding, { kind: 'upload', slot, sending: false,
      attempt: { idempotencyKey: crypto.randomUUID(), companyId: company, ownerKind: OWNER_KIND.companyBranding, ownerId: company, file } })
    void execute()
  }
  function slot(name: 'logo' | 'letterhead') {
    const selected = picked[name]
    const asset = branding?.[name], recent = uploaded[name]
    return <fieldset disabled={locked}><legend>{T[name]}</legend>
      <p>{selected === null ? T.noAsset : recent?.id === selected.documentId ? `${recent.fileName} · ${T.version} ${selected.versionNo}`
        : asset?.documentId === selected.documentId ? `${asset.contentType} · ${T.version} ${asset.versionNo} · ${asset.sha256}` : T.noAsset}</p>
      {selected === null ? null : <Button type="button" onClick={() => setSelection({ ...picked, [name]: null })}>{T.clear}</Button>}
      <Field label={`${T[name]} · ${T.selectFile}`}>{(control) => <input {...control} type="file" accept={policy?.allowedContentTypes.join(',')}
        onChange={(event) => setFiles((old) => ({ ...old, [name]: event.target.files?.[0] }))} disabled={locked || !policy?.canUpload} />}</Field>
      <Button type="button" disabled={locked || !policy?.canUpload || files[name] === undefined} onClick={() => upload(name)}>{T.upload}</Button>
    </fieldset>
  }

  return <section aria-labelledby="document-branding-title">
    <h1 id="document-branding-title">{module === null ? T.title : `${T.moduleNames[module]} · ${T.templateTitle}`}</h1>
    <p>{module === null ? T.description : T.templateDescription}</p>
    {query.isLoading ? <StateBlock subject={T.subject} state={{ kind: 'initial-loading' }} />
      : query.data?.type !== 'ok-single' ? <StateBlock subject={T.subject} state={query.data === undefined ? { kind: 'service-unavailable' } : classifyOutcome<BrandingCurrent | TemplateCurrent>(query.data)} onRetry={() => void query.refetch()} />
        : module === null ? <>
          {branding === null ? <p>{T.noBranding}</p> : <p>{T.version}: {branding.version}</p>}
          {policy?.canUpload ? null : <p>{T.uploadForbidden}</p>}{slot('logo')}{slot('letterhead')}
        </> : <>
          <p>{T.version}: {branding?.version ?? '—'}</p>
          {template?.draft === null ? <p>{T.noDraft}</p> : <p>{T.draft}: {template?.draft?.version} · {T.preparedBy}: {template?.draft?.preparedBy}</p>}
          {template?.draft == null ? null : <><AssetProof label={T.logo} asset={template.draft.logo} /><AssetProof label={T.letterhead} asset={template.draft.letterhead} />
            {template.draft.logo === null && template.draft.letterhead === null ? <p>{T.noneSelected}</p> : null}</>}
          {template?.publication === null ? <p>{T.noPublication}</p> : <p>{T.publication}: {template?.publication?.templateVersion} · {T.publishedBy}: {template?.publication?.publishedBy}</p>}
          {template?.publication == null ? null : <details><summary>{T.publication} · {T.version} {template.publication.version}</summary>
            <AssetProof label={T.logo} asset={template.publication.logo} /><AssetProof label={T.letterhead} asset={template.publication.letterhead} /></details>}
          {maker ? <p>{T.maker}</p> : null}
        </>}
    <Field label={T.reason}>{(control) => <textarea {...control} value={reason} maxLength={500} disabled={locked || current === null} onChange={(event) => setReason(event.target.value)} />}</Field>
    {module === null ? <Button type="button" disabled={locked || current === null} onClick={() => begin({ kind: 'branding', payload: {
      expectedVersion: branding?.version ?? '0', logo: picked.logo, letterhead: picked.letterhead, reason: reason.trim() } })}>{T.save}</Button>
      : <>
        {canPrepare ? <Button type="button" disabled={locked || branding === null} onClick={() => begin({ kind: 'prepare', payload: {
          module, templateCode: TEMPLATE_CATALOG[module].code, expectedDraftVersion: template?.draft?.version ?? '0', expectedBrandingVersion: branding!.version, reason: reason.trim() } })}>{T.prepare}</Button> : null}
        {canPublish ? <Button type="button" disabled={locked || template?.draft == null || maker || template.draft.logo === null && template.draft.letterhead === null}
          onClick={() => begin({ kind: 'publish', payload: { module, templateCode: TEMPLATE_CATALOG[module].code,
            expectedDraftVersion: template!.draft!.version, expectedPublicationVersion: template?.publication?.version ?? '0', reason: reason.trim() } })}>{T.publish}</Button> : null}
      </>}
    {pending === null ? null : <Button type="button" disabled={pending.sending} onClick={() => void execute()}>{pending.sending ? T.busy : T.retry}</Button>}
    <Button type="button" disabled={pending !== null || query.isFetching} onClick={() => { setSelection(null); void query.refetch() }}>{T.refresh}</Button>
    {state === null ? null : <StateBlock subject={T.subject} state={state} />}
    {message === null ? null : <p role="status">{message}</p>}
  </section>
}
function ref(asset: BrandingAsset | null | undefined): AssetReference | null { return asset == null ? null : { documentId: asset.documentId, versionNo: asset.versionNo } }
function AssetProof({ label, asset }: { readonly label: string; readonly asset: BrandingAsset | null }) {
  return asset === null ? <p>{label}: {T.noAsset}</p> : <details><summary>{label} · {asset.contentType} · {T.version} {asset.versionNo}</summary>
    <p>{asset.sha256}</p><p>{asset.uploadedAtUtc}</p></details>
}
