import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { downloadDocument } from './api'
import { DOCUMENT_PROBLEM, type DocumentRow } from './contract'
import { DOCUMENTS_TEXT as TEXT } from './documentsText'
import styles from './DocumentsPanel.module.css'

const PREVIEW_TYPES = new Set(['image/png', 'image/jpeg'])

type PreviewState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'loading' }
  | { readonly phase: 'ready'; readonly context: string; readonly url: string }
  | { readonly phase: 'problem'; readonly context: string; readonly message: string }

const FOCUSABLE = 'a[href], button, textarea, input, select, [tabindex]:not([tabindex="-1"])'

function focusable(node: HTMLElement): boolean {
  if (node.matches(':disabled')) return false
  if (node.closest('fieldset:disabled') !== null) return false
  return !node.hidden
}

export function DocumentPreview({ row, companyId }: { readonly row: DocumentRow; readonly companyId: string }) {
  const [state, setState] = useState<PreviewState>({ phase: 'idle' })
  const triggerRef = useRef<HTMLButtonElement | null>(null)
  const panelRef = useRef<HTMLDivElement | null>(null)
  const closeRef = useRef<HTMLButtonElement | null>(null)
  const controllerRef = useRef<{ readonly context: string; readonly value: AbortController } | null>(null)
  const urlRef = useRef<{ readonly context: string; readonly value: string } | null>(null)
  const context = `${companyId}|${row.id}|${row.contentType}`
  const contextRef = useRef(context)
  contextRef.current = context

  const releaseUrl = () => {
    if (urlRef.current === null) return
    URL.revokeObjectURL(urlRef.current.value)
    urlRef.current = null
  }

  // A render caused by loading/ready state must not cancel its own request or URL. Only
  // unmounting or replacing this exact document/company/type context owns the cleanup.
  useEffect(() => {
    setState({ phase: 'idle' })
    return () => {
      controllerRef.current?.value.abort()
      controllerRef.current = null
      releaseUrl()
    }
  }, [context])

  const ready = state.phase === 'ready' && state.context === context
  useEffect(() => {
    if (ready) closeRef.current?.focus()
  }, [ready])

  if (!row.downloadable || !PREVIEW_TYPES.has(row.contentType)) return null

  const open = async () => {
    controllerRef.current?.value.abort()
    releaseUrl()
    const controller = new AbortController()
    controllerRef.current = { context, value: controller }
    setState({ phase: 'loading' })

    const outcome = await downloadDocument(row.id, companyId, controller.signal)
    if (
      controller.signal.aborted ||
      contextRef.current !== context ||
      controllerRef.current?.value !== controller
    )
      return
    controllerRef.current = null
    if (outcome.type !== 'ok') {
      setState({
        phase: 'problem',
        context,
        message:
          outcome.type === 'refused' && outcome.code === DOCUMENT_PROBLEM.notScanned
            ? TEXT.notScanned
            : TEXT.previewRefused,
      })
      return
    }
    if (!PREVIEW_TYPES.has(outcome.blob.type) || outcome.blob.type !== row.contentType) {
      setState({ phase: 'problem', context, message: TEXT.previewTypeRefused })
      return
    }

    const url = URL.createObjectURL(outcome.blob)
    urlRef.current = { context, value: url }
    setState({ phase: 'ready', context, url })
  }

  const close = () => {
    controllerRef.current?.value.abort()
    controllerRef.current = null
    releaseUrl()
    setState({ phase: 'idle' })
    triggerRef.current?.focus()
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      close()
      return
    }
    if (event.key !== 'Tab' || panelRef.current === null) return
    const nodes = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(focusable)
    if (nodes.length === 0) {
      event.preventDefault()
      panelRef.current.focus()
      return
    }
    const active = document.activeElement
    if (!(active instanceof HTMLElement) || !nodes.includes(active)) {
      event.preventDefault()
      ;(event.shiftKey ? nodes[nodes.length - 1] : nodes[0]).focus()
      return
    }
    const first = nodes[0]
    const last = nodes[nodes.length - 1]
    if ((event.shiftKey && active === first) || (!event.shiftKey && active === last)) {
      event.preventDefault()
      ;(event.shiftKey ? last : first).focus()
    }
  }

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className={styles.action}
        aria-label={TEXT.preview(row.fileName)}
        disabled={state.phase === 'loading'}
        onClick={() => void open()}
      >
        {state.phase === 'loading' ? TEXT.previewing : TEXT.previewLabel}
      </button>
      {state.phase === 'problem' && state.context === context ? (
        <p role="alert" className={styles.problem} data-documents-state="preview-refused">
          {state.message}
        </p>
      ) : null}
      {ready && state.phase === 'ready' ? (
        <div className={styles.previewBackdrop} data-documents-state="preview-open">
          <div
            ref={panelRef}
            role="dialog"
            aria-modal="true"
            aria-label={TEXT.previewTitle(row.fileName)}
            className={styles.previewDialog}
            onKeyDown={onKeyDown}
            tabIndex={-1}
          >
            <div className={styles.previewHeader}>
              <h3 className={styles.previewTitle}>{TEXT.previewTitle(row.fileName)}</h3>
              <button ref={closeRef} type="button" className={styles.action} onClick={close}>
                {TEXT.closePreview}
              </button>
            </div>
            <img className={styles.previewImage} src={state.url} alt={TEXT.previewAlt(row.fileName)} />
          </div>
        </div>
      ) : null}
    </>
  )
}
