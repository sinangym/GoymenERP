/**
 * WP15 (DALGA_PLANI_v2 1.16): the rule-based duplicate warning on the incoming invoice detail. Read-only; it never blocks a
 * step (no decision says a match blocks: NEEDS-SINAN WP15-1). The server decides what matches.
 */
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { fetchInvoiceDuplicates } from './incomingInvoiceApi'
import { INVOICE_DUPLICATE_REASON, INVOICE_DUPLICATE_TEXT as T } from './invoiceDuplicateText'

export function InvoiceDuplicatePanel({ company, namespace, entry, invoiceId }: {
  readonly company: string
  readonly namespace: string
  readonly entry: number
  readonly invoiceId: string
}) {
  const boundary = useAuthBoundary()
  const read = useQuery({ queryKey: [namespace, 'incoming-invoices', entry, 'duplicates', invoiceId], retry: false,
    queryFn: async ({ signal }) => {
      const fence = boundary.capture(), answer = await fetchInvoiceDuplicates(company, invoiceId, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, fence)
      return answer
    } })
  const data = read.data
  return <section aria-labelledby="invoice-duplicate-heading" data-testid="invoice-duplicates">
    <h2 id="invoice-duplicate-heading">{T.heading}</h2>
    {read.isFetching || data === undefined ? <StateBlock subject={T.subject} state={{ kind: 'initial-loading' }} />
      : data.type !== 'ok-single' ? <StateBlock subject={T.subject} state={classifyOutcome(data)} onRetry={() => void read.refetch()} />
        : data.data.warnings.length === 0 ? <p data-testid="invoice-duplicates-none">{T.none}</p>
          : <>
            <p role="alert">{T.warning}</p>
            <table>
              <caption>{T.caption}</caption>
              <thead><tr><th scope="col">{T.number}</th><th scope="col">{T.invoiceDate}</th><th scope="col">{T.reason}</th></tr></thead>
              <tbody>{data.data.warnings.map((warning) => <tr key={warning.invoiceId}>
                <td><Link to={warning.link}>{warning.officialNumber}</Link></td>
                <td>{warning.invoiceDate === null ? '—' : <time dateTime={warning.invoiceDate}>{T.date(warning.invoiceDate)}</time>}</td>
                <td>{INVOICE_DUPLICATE_REASON[warning.code]}</td>
              </tr>)}</tbody>
            </table>
            {data.data.truncated ? <p>{T.truncated}</p> : null}
          </>}
  </section>
}
