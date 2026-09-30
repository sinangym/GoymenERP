/**
 * AT-055 on S-04, under "Teslimat durumu" (AT-024): the dispatch-note (irsaliye) DRAFTS that document this order's definite
 * shipments, as the Logistics owner answers them (`GET /api/v1/logistics/dispatch-notes?salesOrderId=…`, measured under
 * `logistics.loading-instructions.read`), each with a link to the loading instruction's detail where the draft is shown in
 * full (`#irsaliye-{id}`). Sales stores nothing of it and reads no Logistics table. Shown only to a holder of the Logistics
 * instruction detail (the link's own screen, the operations-work rule "a row opens its owner's screen only with that
 * screen's grant"); nothing is read otherwise. Read afresh on every open (`gcTime` 0); a body that does not describe this
 * order is not shown. Each note shows its state (Taslak / Onaylandı / İptal edildi), its ERP number once approved and its
 * e-İrsaliye number when entered; a cancelled note stays listed. The link names the note by its short reference.
 */
import { useId } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchSalesOrderDispatchNotes } from '../logistics/api'
import { LOGISTICS_CAPABILITY, instructionDetailRoute } from '../logistics/capabilities'
import type { DraftDetail } from './contract'
import { formatInstant } from './format'
import { ORDER_DETAIL_TEXT } from './salesText'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './SalesPages.module.css'

const T = ORDER_DETAIL_TEXT.dispatchNotes

export function SalesDraftDispatchNotes({ detail }: { readonly detail: DraftDetail }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canRead = namespace !== null && companyId !== null && companyId === detail.draft.companyId
    && hasCapability(shell, LOGISTICS_CAPABILITY.instructionsDetail)
  const boundary = useAuthBoundary()
  const headingId = useId()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-dispatch-notes', detail.draft.id],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchSalesOrderDispatchNotes(detail.draft.id, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  const lineNo = (orderLineId: string) => detail.lines.find((line) => line.lineId === orderLineId)?.lineNo ?? '?'
  const outcome = query.data
  return (
    <div role="group" data-testid="sales-dispatch-notes" aria-labelledby={headingId}>
      <h3 id={headingId} className={styles.lineTitle}>{T.heading}</h3>
      <p className={styles.muted}>{T.note}</p>
      {outcome?.type === 'ok-single' ? (
        outcome.data.items.length === 0 ? (
          <p className={styles.muted} data-testid="sales-dispatch-notes-none">{T.none}</p>
        ) : (
          <ul data-testid="sales-dispatch-notes-list">
            {outcome.data.items.map((note) => (
              <li key={note.dispatchNoteId} data-testid={`sales-dispatch-note-${note.dispatchNoteId}`}>
                <Link to={`${instructionDetailRoute(note.instructionId)}#irsaliye-${note.dispatchNoteId}`}
                  data-testid={`sales-dispatch-note-link-${note.dispatchNoteId}`}>
                  {T.draft} …{note.dispatchNoteId.slice(-8)}
                </Link>{' '}
                · <span data-testid={`sales-dispatch-note-status-${note.dispatchNoteId}`}>{T.status[note.status]}</span>
                {note.number === null ? null : (
                  <> · {T.number}: <span data-testid={`sales-dispatch-note-number-${note.dispatchNoteId}`}>{note.number}</span></>
                )}
                {note.eDispatchNumber === null ? null : (
                  <> · {T.eDispatchNumber}: <span data-testid={`sales-dispatch-note-e-number-${note.dispatchNoteId}`}>{note.eDispatchNumber}</span></>
                )}
                {' '}· {formatInstant(note.createdAtUtc)} ·{' '}
                {note.lines.map((line) => `${T.line} ${lineNo(line.sourceOrderLineId)}: ${line.quantity}`).join(', ')}
              </li>
            ))}
            {outcome.data.hasMore ? <li className={styles.muted} data-testid="sales-dispatch-notes-more">{T.more}</li> : null}
          </ul>
        )
      ) : (
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={() => void query.refetch()}
        />
      )}
    </div>
  )
}
