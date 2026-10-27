/**
 * MOD-07 — "PDF indir" on the sales order detail. One read of `GET …/drafts/{id}/pdf`, saved under the server's
 * DEC-DOC-004 file name. The screen chooses nothing about the content: the server prints the current revision and decides
 * by the caller's price read whether prices appear. States: idle, loading (the button is busy and says so), saved (a polite
 * status line), and a problem — forbidden (403), not found in this company (404), changed meanwhile (409, try again), too
 * long for one PDF (422 `sales_order_pdf_too_large`, no retry) or failed (any other answer, offline or a body that is not a
 * PDF; try again).
 */

import { useEffect, useId, useRef, useState } from 'react'
import { Button } from '../ui/Button'
import { downloadOrderPdf, saveOrderPdf } from './orderPdf'
import { SALES_ORDER_PDF_TEXT as T } from './salesText'
import styles from './SalesOrderPdfAction.module.css'

type Problem = 'forbidden' | 'notFound' | 'changed' | 'tooLarge' | 'failed'

type State =
  | { readonly kind: 'idle' }
  | { readonly kind: 'loading' }
  | { readonly kind: 'saved'; readonly fileName: string }
  | { readonly kind: 'problem'; readonly problem: Problem }

const PROBLEM_TEXT: Record<Problem, string> = {
  forbidden: T.forbidden,
  notFound: T.notFound,
  changed: T.changed,
  tooLarge: T.tooLarge,
  failed: T.failed,
}

/** The server's stable code of an order too long for one PDF (422): a retry gives the same answer. */
const ORDER_PDF_TOO_LARGE = 'sales_order_pdf_too_large'

function problemOf(status: number, code: string | null): Problem {
  if (status === 403) return 'forbidden'
  if (status === 404) return 'notFound'
  if (status === 409) return 'changed'
  if (status === 422 && code === ORDER_PDF_TOO_LARGE) return 'tooLarge'
  return 'failed'
}

export function SalesOrderPdfAction({
  draftId,
  companyId,
  documentNo,
}: {
  readonly draftId: string
  /** The committed shell company the order was read under. */
  readonly companyId: string
  /** Only the fallback file name when the server names none. */
  readonly documentNo: string
}) {
  const noteId = useId()
  const [state, setState] = useState<State>({ kind: 'idle' })
  const inFlight = useRef<AbortController | null>(null)

  // Leaving the page abandons the read; nothing is saved for a page that is gone.
  useEffect(() => () => inFlight.current?.abort(), [])

  const loading = state.kind === 'loading'

  async function download() {
    if (inFlight.current !== null) return
    const controller = new AbortController()
    inFlight.current = controller
    setState({ kind: 'loading' })
    const outcome = await downloadOrderPdf(draftId, companyId, controller.signal)
    if (inFlight.current !== controller || controller.signal.aborted || outcome.type === 'aborted') return
    inFlight.current = null
    if (outcome.type === 'ok') {
      const fileName = outcome.fileName ?? `${documentNo}.pdf`
      saveOrderPdf(outcome.blob, fileName)
      setState({ kind: 'saved', fileName })
      return
    }
    setState({ kind: 'problem', problem: outcome.type === 'refused' ? problemOf(outcome.status, outcome.code) : 'failed' })
  }

  const retryable = state.kind === 'problem' && (state.problem === 'changed' || state.problem === 'failed')

  return (
    <div className={styles.action} data-testid="sales-order-pdf" data-pdf-state={state.kind === 'problem' ? state.problem : state.kind}>
      <Button
        type="button"
        size="sm"
        onClick={() => void download()}
        disabled={loading}
        aria-busy={loading}
        aria-describedby={noteId}
        data-testid="sales-order-pdf-download"
      >
        {loading ? T.preparing : T.download}
      </Button>
      <p id={noteId} className={styles.note}>
        {T.note}
      </p>
      <p role="status" className={styles.status} data-testid="sales-order-pdf-status">
        {loading ? T.preparing : state.kind === 'saved' ? T.saved(state.fileName) : ''}
      </p>
      {state.kind === 'problem' ? (
        <div role="alert" className={styles.problem} data-testid="sales-order-pdf-problem">
          <p>{PROBLEM_TEXT[state.problem]}</p>
          {retryable ? (
            <Button type="button" size="sm" variant="ghost" onClick={() => void download()}>
              {T.retry}
            </Button>
          ) : null}
        </div>
      ) : null}
    </div>
  )
}
