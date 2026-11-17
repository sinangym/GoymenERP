import { useEffect, useRef, useState } from 'react'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { fetchLabelPdfTemplate, generateLabelPdf, type LabelPdfTemplateSelection } from './labelPrintApi'
import { brandingGuid, brandingVersion } from '../documents/brandingContract'
import { LABEL_PRINT_TEXT as T } from './labelPrintText'

interface Pending { readonly key: string; readonly requestedAtUtc: string; readonly template?: LabelPdfTemplateSelection }

function recall(storageKey: string): Pending | null {
  try {
    const raw: unknown = JSON.parse(sessionStorage.getItem(storageKey) ?? 'null')
    return typeof raw === 'object' && raw !== null && 'key' in raw && 'requestedAtUtc' in raw
      && typeof raw.key === 'string' && raw.key.length > 0 && typeof raw.requestedAtUtc === 'string'
      && /^\d{4}-\d{2}-\d{2}T/.test(raw.requestedAtUtc)
      && (!('template' in raw) || validTemplate(raw.template))
      ? { key: raw.key, requestedAtUtc: raw.requestedAtUtc, ...('template' in raw ? { template: raw.template as LabelPdfTemplateSelection } : {}) } : null
  } catch { return null }
}
function validTemplate(raw: unknown): raw is LabelPdfTemplateSelection {
  return raw !== null && typeof raw === 'object' && 'publicationId' in raw && 'publicationVersion' in raw
    && (raw.publicationId === null && raw.publicationVersion === null || brandingGuid(raw.publicationId) && brandingVersion(raw.publicationVersion))
}

export function LabelPrintPanel({ labelId, companyId, actorId, namespace }: {
  readonly labelId: string; readonly companyId: string; readonly actorId: string; readonly namespace: string
}) {
  const boundary = useAuthBoundary()
  const storageKey = `goymen.inventory.label-pdf.v1:${namespace}:${actorId}:${companyId}:${labelId}`
  const [pending, setPending] = useState<Pending | null>(() => recall(storageKey))
  const [busy, setBusy] = useState(false)
  const [state, setState] = useState<RemoteViewState<never> | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [download, setDownload] = useState<string | null>(null)
  const generation = useRef(0)
  const flight = useRef(false)
  useEffect(() => () => { generation.current += 1 }, [])
  useEffect(() => () => { if (download !== null) URL.revokeObjectURL(download) }, [download])

  async function generate() {
    if (flight.current) return
    flight.current = true
    const current = ++generation.current
    const snapshot = boundary.capture()
    setBusy(true); setState(null); setMessage(null)
    let attempt = pending
    if (attempt === null) {
      const selected = await fetchLabelPdfTemplate(labelId, companyId)
      if (current !== generation.current) return
      if (selected.type !== 'ok-single') {
        flight.current = false; setBusy(false)
        if (selected.type === 'http') boundary.report(selected.status, snapshot)
        const failure = classifyOutcome(selected)
        setState(failure.kind === 'ready' ? { kind: 'malformed-response' } : failure)
        return
      }
      attempt = { key: crypto.randomUUID(), requestedAtUtc: new Date().toISOString(), template: selected.data }
      setPending(attempt)
      try { sessionStorage.setItem(storageKey, JSON.stringify(attempt)) } catch { /* mounted attempt remains available */ }
    }
    const outcome = await generateLabelPdf(labelId, attempt.requestedAtUtc, companyId, attempt.key, attempt.template)
    if (outcome.type === 'applied' || (outcome.type === 'failed' && ![401, 403].includes(outcome.status))) {
      try { sessionStorage.removeItem(storageKey) } catch { /* no transport retry is needed after a definite answer */ }
    }
    if (current !== generation.current) return
    flight.current = false; setBusy(false)
    if (outcome.type === 'applied') {
      setPending(null); setDownload(URL.createObjectURL(outcome.blob))
      setMessage(outcome.replayed ? T.replayed : T.stored)
    } else if (outcome.type === 'unknown') {
      setState({ kind: outcome.reason }); setMessage(T.unknown)
    } else {
      if (outcome.status === 401 || outcome.status === 403) boundary.report(outcome.status, snapshot)
      else setPending(null)
      if (outcome.code === 'inventory_label_pdf_barcode_unsupported') setMessage(T.unsupported)
      else if (outcome.status === 400 || outcome.status === 415 || outcome.status === 422) setMessage(T.invalid)
      else setState({ kind: outcome.status === 401 ? 'unauthenticated' : outcome.status === 403 ? 'forbidden'
        : outcome.status === 404 ? 'not-found' : 'conflict' })
    }
  }

  return <section aria-labelledby="label-print-heading">
    <h2 id="label-print-heading">{T.title}</h2>
    <p>{T.description}</p>
    <Button type="button" onClick={() => void generate()} disabled={busy}>{busy ? T.generating : pending === null ? T.generate : T.retry}</Button>
    {state === null ? null : <StateBlock state={state} subject={T.subject} onRetry={() => void generate()} />}
    {message === null ? null : <p role="status">{message}</p>}
    {download === null ? null : <p><a href={download} download={T.fileName}>{T.download}</a></p>}
  </section>
}
