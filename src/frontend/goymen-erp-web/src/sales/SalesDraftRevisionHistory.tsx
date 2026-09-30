import { useId } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftRevisions } from './api'
import { SALES_CAPABILITY } from './capabilities'
import type { DraftRevision } from './contract'
import { formatInstant } from './format'
import { REVISION_HISTORY_TEXT as T } from './salesText'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './SalesPages.module.css'

/** A line's quantity as the current detail carries it (the revision after the newest archived one). */
export interface CurrentLineQuantity {
  readonly lineId: string
  readonly quantity: string
}

/**
 * Mounted only after the parent detail was successfully read in the same context. DEC-SAL-002: each archived revision shows
 * why it was superseded and, per line, the quantity of the revision that followed it (the next archived one, or the current
 * detail for the newest) so the diff is read from the server's own records, never computed from an assumed baseline.
 */
export function SalesDraftRevisionHistory({ draftId, draftVersion, currentLines = [] }: {
  readonly draftId: string
  readonly draftVersion: string
  readonly currentLines?: readonly CurrentLineQuantity[]
}) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canRead = namespace !== null && companyId !== null && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const boundary = useAuthBoundary()
  const headingId = useId()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-revisions', draftId, draftVersion],
    enabled: canRead,
    // Do not retain prices after this reader leaves, or inherit placeholder data
    // from a former context. The current detail version invalidates the archive
    // view when a commercial revision is accepted without navigating away.
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftRevisions(draftId, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  const outcome = query.data
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-revision-history">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={styles.muted}>{T.note}</p>
      {outcome?.type === 'ok-single' ? (
        outcome.data.items.length === 0 ? <p>{T.empty}</p> :
          outcome.data.items.map((revision, index, items) => (
            <ArchivedRevision
              key={revision.revision}
              revision={revision}
              next={index + 1 < items.length ? items[index + 1].lines : currentLines}
            />
          ))
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

function Instant({ value }: { readonly value: string | null }) {
  return value === null ? T.unknown : <time dateTime={value}>{formatInstant(value)}</time>
}

function ArchivedRevision({ revision: r, next }: {
  readonly revision: DraftRevision
  readonly next: readonly CurrentLineQuantity[]
}) {
  const headingId = useId()
  const nextQuantity = (lineId: string) => next.find((line) => line.lineId.toLowerCase() === lineId.toLowerCase())?.quantity ?? null
  return (
    <section className={styles.line} aria-labelledby={headingId} data-testid={`sales-revision-${r.revision}`}>
      <h3 className={styles.lineTitle} id={headingId}>{r.revision}</h3>
      <dl className={styles.facts}>
        <div><dt>{T.submittedBy}</dt><dd>{r.submittedBy ?? T.unknown}</dd></div>
        <div><dt>{T.submittedAt}</dt><dd><Instant value={r.submittedAtUtc} /></dd></div>
        <div><dt>{T.approvedBy}</dt><dd>{r.approvedBy ?? T.unknown}</dd></div>
        <div><dt>{T.approvedAt}</dt><dd><Instant value={r.approvedAtUtc} /></dd></div>
        <div><dt>{T.archivedBy}</dt><dd>{r.archivedBy}</dd></div>
        <div><dt>{T.archivedAt}</dt><dd><Instant value={r.archivedAtUtc} /></dd></div>
        <div><dt>{T.moneyProfile}</dt><dd className={styles.identifier}>{r.moneyProfileId}</dd></div>
        <div><dt>{T.moneyVersion}</dt><dd>{r.moneyProfileVersion}</dd></div>
        <div><dt>{T.moneyScale}</dt><dd>{r.moneyScale}</dd></div>
        <div><dt>{T.reason}</dt><dd data-testid={`sales-revision-${r.revision}-reason`}>{r.reason ?? T.noReason}</dd></div>
      </dl>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.caption}>{T.caption(r.revision)}</caption>
          <thead><tr>
            <th scope="col">{T.lineNo}</th><th scope="col">{T.lineId}</th>
            <th scope="col">{T.product}</th><th scope="col">{T.productVersion}</th>
            <th scope="col">{T.orderUnit}</th><th scope="col">{T.quantity}</th><th scope="col">{T.nextQuantity}</th>
            <th scope="col">{T.unitPrice}</th><th scope="col">{T.priceUnit}</th><th scope="col">{T.discount}</th>
          </tr></thead>
          <tbody>{r.lines.map((line) => {
            const hidden = line.priceDisclosure === 'Withheld'
            const following = nextQuantity(line.lineId)
            return (
              <tr key={line.lineId} data-testid={`sales-revision-${r.revision}-line-${line.lineNo}`}>
                <th scope="row">{line.lineNo}</th>
                <td className={styles.identifier}>{line.lineId}</td>
                <td className={styles.identifier}>{line.productId}</td>
                <td>{line.productVersion}</td>
                <td className={styles.identifier}>{line.orderUnitId}</td>
                <td className={styles.numeric}>{line.quantity}</td>
                <td className={styles.numeric} data-testid={`sales-revision-${r.revision}-line-${line.lineNo}-next`}>
                  {following === null ? T.unknown : following === line.quantity ? following : `${following} (${T.changed})`}
                </td>
                <td className={styles.numeric}>{hidden ? T.withheld : line.unitPrice ?? T.notPriced}</td>
                <td className={styles.identifier}>{hidden ? T.withheld : line.priceUnitId ?? T.notSpecified}</td>
                <td className={styles.numeric}>{hidden ? T.withheld : line.discountPercent ?? T.notSpecified}</td>
              </tr>
            )
          })}</tbody>
        </table>
      </div>
    </section>
  )
}
