/**
 * AT-024 ("Kısmi teslimat sipariş kalanını doğru tutar"): per order line the shipped, delivered and remaining quantity,
 * as the server answers it from the delivery owner's RECORDED deliveries (Logistics). Read under the draft read, mounted
 * only after the parent detail was read in the same context, and read afresh every time the order is opened (no cache
 * across mounts: `gcTime` 0), so a delivery recorded in Logistics shows on the next open. Quantities stay decimal text —
 * nothing is computed here. No completion, tolerance or warning is derived (OPEN-004 is open). A body that does not
 * describe exactly this detail (version, lines, units, quantities) is not shown as numbers.
 */
import { useId } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftDeliveryProgress } from './api'
import { SALES_CAPABILITY } from './capabilities'
import type { DraftDetail } from './contract'
import { ORDER_DETAIL_TEXT } from './salesText'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './SalesPages.module.css'

const T = ORDER_DETAIL_TEXT.deliveryProgress

export function SalesDraftDeliveryProgress({ detail }: { readonly detail: DraftDetail }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canRead = namespace !== null && companyId !== null && companyId === detail.draft.companyId
    && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const boundary = useAuthBoundary()
  const headingId = useId()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-delivery-progress', detail.draft.id, detail.draft.version],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftDeliveryProgress(detail, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  const outcome = query.data
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-delivery-progress">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={styles.muted}>{T.note}</p>
      {outcome?.type === 'ok-single' ? (
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <caption className={styles.caption}>{T.caption}</caption>
            <thead><tr>
              <th scope="col">{T.lineNo}</th><th scope="col">{T.ordered}</th><th scope="col">{T.shipped}</th>
              <th scope="col">{T.delivered}</th><th scope="col">{T.remaining}</th><th scope="col">{T.excess}</th>
            </tr></thead>
            <tbody>{outcome.data.lines.map((line) => (
              <tr key={line.lineId} data-testid={`sales-delivery-progress-line-${line.lineNo}`}>
                <th scope="row">{line.lineNo}</th>
                <td>{line.ordered}</td>
                <td data-testid={`sales-delivery-progress-line-${line.lineNo}-shipped`}>{line.shipped}</td>
                <td data-testid={`sales-delivery-progress-line-${line.lineNo}-delivered`}>{line.delivered}</td>
                <td data-testid={`sales-delivery-progress-line-${line.lineNo}-remaining`}>{line.remaining}</td>
                <td data-testid={`sales-delivery-progress-line-${line.lineNo}-excess`}>{line.excess}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      ) : (
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={() => void query.refetch()}
        />
      )}
    </section>
  )
}
