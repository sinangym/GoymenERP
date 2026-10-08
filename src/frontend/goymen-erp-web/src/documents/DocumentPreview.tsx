import { useEffect, useRef, useState } from 'react'
import { downloadDocument } from './api'
import { DOCUMENT_PROBLEM, type DocumentRow } from './contract'
import { DOCUMENTS_TEXT as TEXT } from './documentsText'
import styles from './DocumentsPanel.module.css'

const PREVIEW_TYPES = new Set(['image/png', 'image/jpeg'])

type PreviewState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'loading' }
  | { readonly phase: 'ready'; readonly url: string }
  | { readonly phase: 'problem'; readonly message: string }

export function DocumentPreview({ row, companyId }: { readonly row: DocumentRow; readonly companyId: string }) {
  const [state, setState] = useState<PreviewState>({ phase: 'idle' })
  const controllerRef = useRef<AbortController | null>(null)
  const urlRef = useRef<string | null>(null)

  const releaseUrl = () => {
    if (urlRef.current === null) return
    URL.revokeObjectURL(urlRef.current)
    urlRef.current = null
  }

  useEffect(
    () => () => {
      controllerRef.current?.abort()
      releaseUrl()
    },
  )

  if (!row.downloadable || !PREVIEW_TYPES.has(row.contentType)) return null

  const open = async () => {
    controllerRef.current?.abort()
    releaseUrl()
    const controller = new AbortController()
    controllerRef.current = controller
    setState({ phase: 'loading' })

    const outcome = await downloadDocument(row.id, companyId, controller.signal)
    if (controller.signal.aborted) return
    if (outcome.type !== 'ok') {
      setState({
        phase: 'problem',
        message:
          outcome.type === 'refused' && outcome.code === DOCUMENT_PROBLEM.notScanned
            ? TEXT.notScanned
            : TEXT.previewRefused,
      })
      return
    }
    if (!PREVIEW_TYPES.has(outcome.blob.type) || outcome.blob.type !== row.contentType) {
      setState({ phase: 'problem', message: TEXT.previewTypeRefused })
      return
    }

    const url = URL.createObjectURL(outcome.blob)
    urlRef.current = url
    setState({ phase: 'ready', url })
  }

  const close = () => {
    controllerRef.current?.abort()
    controllerRef.current = null
    releaseUrl()
    setState({ phase: 'idle' })
  }

  return (
    <>
      <button
        type="button"
        className={styles.action}
        aria-label={TEXT.preview(row.fileName)}
        disabled={state.phase === 'loading'}
        onClick={() => void open()}
      >
        {state.phase === 'loading' ? TEXT.previewing : TEXT.previewLabel}
      </button>
      {state.phase === 'problem' ? (
        <p role="alert" className={styles.problem} data-documents-state="preview-refused">
          {state.message}
        </p>
      ) : null}
      {state.phase === 'ready' ? (
        <div role="dialog" aria-modal="true" aria-label={TEXT.previewTitle(row.fileName)} className={styles.previewDialog}>
          <div className={styles.previewHeader}>
            <h3 className={styles.previewTitle}>{TEXT.previewTitle(row.fileName)}</h3>
            <button type="button" className={styles.action} onClick={close}>
              {TEXT.closePreview}
            </button>
          </div>
          <img className={styles.previewImage} src={state.url} alt={TEXT.previewAlt(row.fileName)} />
        </div>
      ) : null}
    </>
  )
}
