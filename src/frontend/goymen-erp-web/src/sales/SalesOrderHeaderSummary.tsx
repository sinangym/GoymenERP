/**
 * API-04 / S-04: the read-only header summary on Sipariş 360 — the order's due terms ("vade") and its sales owner, as the
 * revisable header read answers them (`GET …/drafts/{id}/header`, under the draft read). Before this, a read-only viewer saw
 * neither: both were read only when the header revision form opened.
 *
 * The due terms are price information: without `sales.orders.price.read` the server answers them `Withheld` and the summary
 * says so (SAT-D150: the shipper sees no vade). The sales owner is shown by the name the server resolved for it; an owner the
 * directory does not name is shown by its stored id. Nothing is computed: no due date, no share sum (SAT-D86).
 */
import { useQuery } from '@tanstack/react-query'
import type { DraftDetail } from './contract'
import type { DueTerms } from './headerRevision'
import { fetchDraftHeader } from './headerRevisionApi'
import { HEADER_SUMMARY_TEXT as T } from './salesText'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './SalesDraftDetailPage.module.css'

export function SalesOrderHeaderSummary({ detail, companyId }: { readonly detail: DraftDetail; readonly companyId: string }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const boundary = useAuthBoundary()
  const query = useQuery({
    // The header revision form reads the same header under the same key, so an open form and this summary share one answer.
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-header', detail.draft.id, detail.draft.version],
    enabled: namespace !== null && companyId === detail.draft.companyId,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftHeader(detail.draft.id, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const outcome = query.data
  const header = outcome?.type === 'ok-single' && outcome.data.draftVersion === detail.draft.version ? outcome.data : null
  const failed = outcome !== undefined && header === null
  const failure = !failed ? null : outcome?.type === 'http' && outcome.status === 403 ? T.forbidden : T.unreadable
  return (
    <>
      <div>
        <dt>{T.dueTerms}</dt>
        <dd data-testid="sales-draft-due-terms">
          {header === null ? (failure ?? T.loading)
            : header.dueTermsDisclosure === 'Withheld' ? T.dueTermsWithheld
              : header.dueTerms === null ? T.noDueTerms : <DueTermsList terms={header.dueTerms} />}
        </dd>
      </div>
      <div>
        <dt>{T.salesOwner}</dt>
        <dd data-testid="sales-draft-sales-owner">
          {header === null ? (failure ?? T.loading)
            : header.salesOwnerUserId === null ? T.noSalesOwner
              : header.salesOwnerDisplayName ?? <span className={styles.identifier}>{header.salesOwnerUserId}</span>}
        </dd>
      </div>
    </>
  )
}

function DueTermsList({ terms }: { readonly terms: DueTerms }) {
  if (terms.instalments.length === 1) {
    const [only] = terms.instalments
    return <>{T.instalment(only.sharePercent, only.basis, only.daysAfter)}</>
  }
  return (
    <ol className={styles.compactList}>
      {terms.instalments.map((instalment) => (
        <li key={instalment.sequenceNo}>{T.instalment(instalment.sharePercent, instalment.basis, instalment.daysAfter)}</li>
      ))}
    </ol>
  )
}
