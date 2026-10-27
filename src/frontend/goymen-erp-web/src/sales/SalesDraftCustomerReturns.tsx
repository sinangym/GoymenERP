/**
 * SAT-D8 / UI-70a ("İade görünümü — sipariş üzerinde; ayrı menü yok"): per order line the APPROVED customer-return total
 * as the server answers it from the return owner (Logistics) through API-10 (`GET …/drafts/{id}/customer-returns`, under
 * the draft read). Read only, mounted after the parent detail was read in the same company, and read afresh on every
 * open (`gcTime` 0). The owner's port gives only the total per line — no return number, date or status — so none is
 * shown. Quantities stay decimal text in the line's order unit (the unit named by the order itself, TEK-11). A failed,
 * forbidden or foreign answer shows no quantity at all: an unreadable return is never presented as "nothing returned".
 * No credit note, invoice, stock or remaining-quantity effect is derived here.
 */
import { useId, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftCustomerReturns } from './api'
import { SALES_CAPABILITY } from './capabilities'
import { isNonZeroDecimalText, type DraftDetail } from './contract'
import { ORDER_DETAIL_TEXT } from './salesText'
import { orderUnitCodes } from './unitNames'
import { UnitName } from './UnitName'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './SalesPages.module.css'

const T = ORDER_DETAIL_TEXT.customerReturns

export function SalesDraftCustomerReturns({ detail }: { readonly detail: DraftDetail }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  // API-10 publishes no capability of its own: the route is authorised by the draft read, whose shell capability is the
  // S-04 detail. The API still measures the grant afresh on every call.
  const canRead = namespace !== null && companyId !== null && companyId === detail.draft.companyId
    && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const boundary = useAuthBoundary()
  const headingId = useId()
  const unitCodes = useMemo(() => orderUnitCodes(detail), [detail])
  const products = useMemo(
    () => new Map(detail.lines.map((line) => [line.lineId.toLowerCase(), `${line.productCode} — ${line.productName}`])),
    [detail],
  )
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-customer-returns', detail.draft.id, detail.draft.version],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftCustomerReturns(detail, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  const outcome = query.data
  let body
  if (outcome?.type === 'ok-single') {
    body = outcome.data.lines.some((line) => isNonZeroDecimalText(line.approvedReturned)) ? (
      <div className={styles.tableWrap}>
        <table className={styles.table} data-testid="sales-customer-returns-table">
          <caption className={styles.caption}>{T.caption}</caption>
          <thead><tr>
            <th scope="col">{T.lineNo}</th><th scope="col">{T.product}</th><th scope="col">{T.ordered}</th>
            <th scope="col">{T.approvedReturned}</th><th scope="col">{T.unit}</th>
          </tr></thead>
          <tbody>{outcome.data.lines.map((line) => (
            <tr key={line.lineId} data-testid={`sales-customer-returns-line-${line.lineNo}`}>
              <th scope="row">{line.lineNo}</th>
              <td>{products.get(line.lineId.toLowerCase())}</td>
              <td className={styles.numericValue}>{line.ordered}</td>
              <td className={styles.numericValue} data-testid={`sales-customer-returns-line-${line.lineNo}-returned`}>
                {line.approvedReturned}
              </td>
              <td data-testid={`sales-customer-returns-line-${line.lineNo}-unit`}>
                <UnitName id={line.orderUnitId} unitCodes={unitCodes} />
              </td>
            </tr>
          ))}</tbody>
        </table>
      </div>
    ) : (
      <p className={styles.muted} data-testid="sales-customer-returns-empty">{T.empty}</p>
    )
  } else {
    body = (
      <>
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={() => void query.refetch()}
        />
        {outcome === undefined ? null : <p className={styles.muted} data-testid="sales-customer-returns-unreadable">{T.unreadable}</p>}
      </>
    )
  }
  return (
    <section className={styles.section} aria-labelledby={headingId} aria-busy={outcome === undefined} data-testid="sales-customer-returns">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={styles.muted}>{T.note}</p>
      {body}
    </section>
  )
}
