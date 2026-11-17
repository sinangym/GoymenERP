import { useEffect, useRef, useState } from 'react'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useShellState } from '../shell/shellContext'
import type { RemoteViewState } from '../shell/uiState'
import { Button } from '../ui/Button'
import { API_BASE, logisticsHeaders } from './api'
import { DISPATCH_PDF_TEXT as T } from './dispatchPdfText'

interface DispatchPdfProps {
  readonly instructionId: string; readonly dispatchNoteId: string; readonly companyId: string
}
export function DispatchNotePdfButton(props: DispatchPdfProps) {
  const shell = useShellState()
  return <DispatchPdfControl key={JSON.stringify([shell.activeContext?.userId, shell.sessionEpoch, shell.committedContextRevision,
    shell.activeContext?.capabilityGeneration, props.companyId, props.instructionId, props.dispatchNoteId])} {...props} />
}
function DispatchPdfControl({ instructionId, dispatchNoteId, companyId }: DispatchPdfProps) {
  const boundary = useAuthBoundary(), generation = useRef(0), flight = useRef(false)
  const [busy, setBusy] = useState(false), [url, setUrl] = useState<string | null>(null)
  const [state, setState] = useState<RemoteViewState<never> | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  useEffect(() => () => { generation.current += 1 }, [])
  useEffect(() => () => { if (url !== null) URL.revokeObjectURL(url) }, [url])
  async function download() {
    if (flight.current) return
    flight.current = true; const current = ++generation.current, snapshot = boundary.capture()
    setBusy(true); setState(null); setUrl(null); setMessage(null)
    try {
      const response = await fetch(`${API_BASE}/logistics/loading-instructions/${encodeURIComponent(instructionId)}/dispatch-notes/${encodeURIComponent(dispatchNoteId)}/pdf`,
        { headers: logisticsHeaders(companyId) })
      if (current !== generation.current) return
      if (response.status === 200) {
        if (!response.headers.get('Content-Type')?.startsWith('application/pdf')) { setState({ kind: 'malformed-response' }); return }
        const blob = await response.blob()
        if (current !== generation.current) return
        const signature = await blob.slice(0, 5).text()
        if (current !== generation.current) return
        if (signature !== '%PDF-') { setState({ kind: 'malformed-response' }); return }
        if (current === generation.current) setUrl(URL.createObjectURL(blob))
      } else if (response.status === 422) {
        let raw: unknown = null
        try { raw = await response.json() } catch { /* malformed error payload */ }
        if (current !== generation.current) return
        if (raw !== null && typeof raw === 'object' && 'code' in raw && raw.code === 'logistics_dispatch_pdf_too_large') setMessage(T.tooLarge)
        else setState({ kind: 'malformed-response' })
      } else {
        boundary.report(response.status, snapshot)
        setState({ kind: response.status === 401 ? 'unauthenticated' : response.status === 403 ? 'forbidden'
          : response.status === 404 ? 'not-found' : response.status === 409 ? 'conflict' : response.status >= 500 ? 'service-unavailable' : 'malformed-response' })
      }
    } catch { if (current === generation.current) setState({ kind: navigator.onLine ? 'network-error' : 'offline' }) }
    finally { if (current === generation.current) { flight.current = false; setBusy(false) } }
  }
  return <span><Button type="button" disabled={busy} onClick={() => void download()}>{busy ? T.busy : T.generate}</Button>
    {url === null ? null : <a href={url} download="Sevk_Irsaliyesi.pdf">{T.download}</a>}
    {message === null ? null : <p role="status">{message}</p>}
    {state === null ? null : <StateBlock subject={T.subject} state={state} onRetry={() => void download()} />}</span>
}
