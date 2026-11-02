/**
 * MOD-03 → Sales (SAT-D93/D97/D140/D147): the order's tax card on S-04 (Sipariş 360) — the buyer tax situation and every line's
 * tax snapshot as the server answers them (`GET …/drafts/{id}/tax`, under the draft read AND the price read). Read only, read
 * afresh on every open (`gcTime` 0) and keyed by the draft version, so a recorded revision reads the new snapshot.
 *
 * The screen computes no tax (AGENTS.md: "Fiyat/formül/toleransı ekran içinde yeniden yazma"): every rate, code and state is
 * the server's text. A tax the Settings values do not give yet is shown as "Hesaplanamıyor" with the server's reason — never a
 * guessed rate (SAT-D148). A caller who may not see prices (any line `Withheld`, SAT-D150: the Sevkiyatçı) is not asked: the
 * card says why nothing is shown. An order without lines is not asked either.
 */
import { useId, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftTax } from './api'
import { SALES_CAPABILITY } from './capabilities'
import { decodeProblem, type DraftDetail } from './contract'
import { taxCardOutcome, type LineTax, type OrderTax, type TaxCardOutcome } from './orderTax'
import { ORDER_TAX_TEXT as T, taxReasonText } from './salesText'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StatusIndicator } from '../ui/StatusIndicator'
import pages from './SalesPages.module.css'

/** The container: gates the read on the bound company, the S-04 detail capability and the disclosed prices, then reads once per open. */
export function SalesOrderTaxCard({ detail }: { readonly detail: DraftDetail }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canRead = namespace !== null && companyId !== null && companyId === detail.draft.companyId
    && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const pricesDisclosed = detail.lines.length > 0 && detail.lines.every((line) => line.priceDisclosure === 'Visible')
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-tax', detail.draft.id, detail.draft.version],
    enabled: canRead && pricesDisclosed,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftTax(detail, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  return <OrderTaxCard detail={detail} outcome={taxCardOutcome(detail, query.data)} onRetry={() => void query.refetch()} />
}

/** The card for one read outcome (undefined while loading). Exported so every outcome can be rendered and checked on its own. */
export function OrderTaxCard({
  detail,
  outcome,
  onRetry,
}: {
  readonly detail: DraftDetail
  readonly outcome: TaxCardOutcome
  readonly onRetry: () => void
}) {
  const headingId = useId()
  let body
  if (outcome?.type === 'no-lines') {
    body = <p className={pages.muted} role="status" data-testid="sales-tax-no-lines">{T.noLines}</p>
  } else if (outcome?.type === 'ok-single') {
    body = <TaxBody detail={detail} tax={outcome.data} />
  } else if (outcome?.type === 'http' && outcome.status === 403) {
    body = <p className={pages.muted} role="status" data-testid="sales-tax-forbidden">{T.forbidden}</p>
  } else {
    const problem = outcome?.type === 'http' ? decodeProblem(outcome.body, outcome.status) : null
    body = (
      <>
        <StateBlock state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)} subject={T.subject} onRetry={onRetry} />
        {outcome === undefined ? null : (
          <p className={pages.muted} data-testid="sales-tax-unreadable">
            {outcome.type === 'http' && outcome.status === 503 ? T.serviceUnavailable : T.unreadable}
          </p>
        )}
        {problem?.correlationId == null ? null : <p className={pages.muted}>{`Destek kodu: ${problem.correlationId}`}</p>}
      </>
    )
  }
  return (
    <section className={pages.section} aria-labelledby={headingId} aria-busy={outcome === undefined} data-testid="sales-tax">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={pages.muted}>{T.note}</p>
      {body}
    </section>
  )
}

function TaxBody({ detail, tax }: { readonly detail: DraftDetail; readonly tax: OrderTax }) {
  const linesHeadingId = useId()
  const products = useMemo(
    () => new Map(detail.lines.map((line) => [line.lineId.toLowerCase(), `${line.productCode} — ${line.productName}`])),
    [detail],
  )
  const buyer = tax.buyerTax
  return (
    <>
      <dl className={pages.facts} data-testid="sales-tax-buyer">
        <div>
          <dt>{T.buyerSituation}</dt>
          <dd data-testid="sales-tax-buyer-situation">{buyer === null ? T.buyerSituationMissing : buyer.situation}</dd>
        </div>
        <div>
          <dt>{T.exemptionCode}</dt>
          <dd data-testid="sales-tax-buyer-exemption">{buyer?.exemptionCode ?? T.none}</dd>
        </div>
        <div>
          <dt>{T.document}</dt>
          <dd data-testid="sales-tax-buyer-document">
            {buyer?.documentNo != null && buyer.documentDate !== null ? T.documentValue(buyer.documentNo, buyer.documentDate) : T.none}
          </dd>
        </div>
      </dl>
      <h3 id={linesHeadingId} className={pages.caption}>{T.linesCaption}</h3>
      <div className={pages.tableWrap}>
        <table className={pages.table} aria-labelledby={linesHeadingId} data-testid="sales-tax-lines">
          <thead><tr>
            <th scope="col">{T.column.lineNo}</th>
            <th scope="col">{T.column.product}</th>
            <th scope="col">{T.column.basis}</th>
            <th scope="col">{T.column.vat}</th>
            <th scope="col">{T.column.withholding}</th>
            <th scope="col">{T.column.codes}</th>
            <th scope="col">{T.column.state}</th>
            <th scope="col">{T.column.invoice}</th>
          </tr></thead>
          <tbody>{tax.lines.map((line) => (
            <TaxRow key={line.lineId} line={line} product={products.get(line.lineId.toLowerCase())} />
          ))}</tbody>
        </table>
      </div>
    </>
  )
}

function TaxRow({ line, product }: { readonly line: LineTax; readonly product: string | undefined }) {
  const id = `sales-tax-line-${line.lineNo}`
  const codes = [
    T.code(T.exemptionCode, line.exemptionCode, line.exemptionCodeState),
    T.code('Tevkifat kodu', line.withholdingCode, line.withholdingCodeState),
  ].filter((part): part is string => part !== null)
  return (
    <tr data-testid={id} data-origin={line.origin ?? 'none'}>
      <th scope="row">{line.lineNo}</th>
      <td>{product}</td>
      <td data-testid={`${id}-basis`}>
        {line.hasSnapshot ? (
          <>
            {T.origin[line.origin ?? ''] ?? line.origin}
            {' · '}
            {line.buyerSituation}
            {line.itemType === null ? null : ` · ${T.itemType[line.itemType] ?? line.itemType}`}
            {line.override === null ? null : (
              <span className={pages.muted} data-testid={`${id}-override`}>{' '}{T.overrideBy(line.override.reason, line.override.changedBy)}</span>
            )}
          </>
        ) : T.none}
      </td>
      <td className={pages.numericValue} data-testid={`${id}-vat`}>{T.vatState(line.vatState, line.vatPercent)}</td>
      <td data-testid={`${id}-withholding`}>{T.withholdingState(line.withholdingState, line.withholdingPercent)}</td>
      <td data-testid={`${id}-codes`}>{codes.length === 0 ? T.none : codes.join(' · ')}</td>
      <td data-testid={`${id}-state`}>
        <StatusIndicator tone={line.rateState === 'Computed' ? 'success' : 'warning'}>
          {line.rateState === 'Computed' ? T.rateComputed : T.rateNoSource}
        </StatusIndicator>
        {line.unavailableReason === null ? null : (
          <span className={pages.muted} data-testid={`${id}-reason`}>{' '}{taxReasonText(line.unavailableReason)}</span>
        )}
      </td>
      <td data-testid={`${id}-invoice`}>
        {line.invoiceBlockers.length === 0 ? T.invoiceReady : line.invoiceBlockers.map(taxReasonText).join('; ')}
      </td>
    </tr>
  )
}
