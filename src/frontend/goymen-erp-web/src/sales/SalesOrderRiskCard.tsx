/**
 * MOD-01 / API-06 / UI-15 (DEC-FIN-007, DEC-FIN-009; SAT-D46…D53, SAT-D80, SAT-D150): the customer risk card on S-04 (Sipariş
 * 360), as the server answers it (`GET …/drafts/{id}/risk-check`, under the draft read). Read only, mounted after the parent
 * detail was read in the same company, read afresh on every open (`gcTime` 0) and keyed by the draft version.
 *
 * What it shows: the server's result (within / over the limit — a warning — or fail-closed "could not decide" with every
 * reason), the credit limit, the gross risk ("kullanılan") and the gross risk with this order in the limit's currency, the
 * information-only local total and each part of the risk with its state and reason. The screen never adds, subtracts or
 * converts an amount: the remaining limit ("kullanılabilir") is not in the read, so it is named as not served, never derived.
 *
 * Amounts are price information (SAT-D150: the shipper does not hold `sales.orders.price.read`): when the server withholds
 * them, or any line of the detail is withheld from this caller, no amount is shown — the result, states and reasons stay. A
 * failed, forbidden or foreign answer shows neither a result nor an amount.
 */
import { useContext, useId } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftRiskCheck } from './api'
import { SALES_CAPABILITY } from './capabilities'
import { decodeProblem, type DraftDetail } from './contract'
import { SalesCurrencySourceContext } from './referenceSource'
import type { RiskAmount, RiskCheck, RiskFigure, RiskResult, RiskTotals } from './riskCheck'
import { RISK_CHECK_TEXT as T, riskReasonText } from './riskText'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StatusIndicator, type StatusTone } from '../ui/StatusIndicator'
import pages from './SalesPages.module.css'

/** A currency's code when the reference lists it, else its identity. */
export type CurrencyName = (currencyId: string) => string

/** The container: gates the read on the bound company and the S-04 detail capability, then reads once per open. */
export function SalesOrderRiskCard({ detail }: { readonly detail: DraftDetail }) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  // API-06 publishes no capability of its own: the route is authorised by the draft read (the S-04 detail capability). The
  // API measures it afresh on every call and decides the amount disclosure itself.
  const canRead = namespace !== null && companyId !== null && companyId === detail.draft.companyId
    && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const boundary = useAuthBoundary()
  // The currencies reference is the create form's read (sales.drafts.create): without it a currency's identity is shown.
  const useCurrencies = useContext(SalesCurrencySourceContext)
  const currencies = useCurrencies(
    canRead && companyId !== null && hasCapability(shell, SALES_CAPABILITY.draftsCreate) ? companyId : '', detail.draft.businessDate)
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-risk-check', detail.draft.id, detail.draft.version],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftRiskCheck(detail, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  const currencyName: CurrencyName = (currencyId) => (currencies.status === 'ready'
    ? currencies.data.find((candidate) => candidate.currencyId.toLowerCase() === currencyId.toLowerCase())?.code
    : undefined) ?? currencyId
  // A line withheld from this caller means the price read is not held: no risk amount is shown whatever the answer says.
  const pricesWithheld = detail.lines.some((line) => line.priceDisclosure !== 'Visible')
  return (
    <OrderRiskCard
      outcome={query.data}
      currencyName={currencyName}
      pricesWithheld={pricesWithheld}
      onRetry={() => void query.refetch()}
    />
  )
}

/** The card for one read outcome (undefined while loading). Exported so every outcome can be rendered and checked on its own. */
export function OrderRiskCard({
  outcome,
  currencyName,
  pricesWithheld = false,
  onRetry,
}: {
  readonly outcome: ViewOutcome<RiskCheck> | undefined
  readonly currencyName: CurrencyName
  /** The detail withholds prices from this caller: amounts are hidden even if the answer carried them. */
  readonly pricesWithheld?: boolean
  readonly onRetry: () => void
}) {
  const headingId = useId()
  let body
  if (outcome?.type === 'ok-single') {
    body = <RiskBody check={outcome.data} currencyName={currencyName}
      showAmounts={!pricesWithheld && outcome.data.amountDisclosure === 'Visible'} />
  } else if (outcome?.type === 'http' && outcome.status === 403) {
    body = <p className={pages.muted} role="status" data-testid="sales-risk-forbidden">{T.forbidden}</p>
  } else {
    const problem = outcome?.type === 'http' ? decodeProblem(outcome.body, outcome.status) : null
    body = (
      <>
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={onRetry}
        />
        {outcome === undefined ? null : (
          <p className={pages.muted} data-testid="sales-risk-unreadable">
            {outcome.type === 'http' && outcome.status === 503 ? T.serviceUnavailable : T.unreadable}
          </p>
        )}
        {problem?.correlationId == null ? null : (
          <p className={pages.muted} data-testid="sales-risk-support-code">{T.supportCode(problem.correlationId)}</p>
        )}
      </>
    )
  }
  return (
    <section className={pages.section} aria-labelledby={headingId} aria-busy={outcome === undefined} data-testid="sales-risk">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={pages.muted}>{T.note}</p>
      {body}
    </section>
  )
}

/* ── Presentation of a decoded check ─────────────────────────────────────── */

const RESULT_TONE: Record<RiskResult, StatusTone> = { Within: 'success', Over: 'danger', Unavailable: 'warning' }
const STATE_TONE: Record<RiskFigure['state'], StatusTone> = { Available: 'success', NoSource: 'neutral', Unavailable: 'warning' }

function RiskBody({ check, currencyName, showAmounts }: {
  readonly check: RiskCheck
  readonly currencyName: CurrencyName
  readonly showAmounts: boolean
}) {
  const figuresId = useId()
  const figures = [...check.components, check.thisOrder, check.creditLimit, check.collateral]
  const nothingAnswered = figures.every((figure) => figure.state !== 'Available')
  const limitCurrency = check.inLimitCurrency?.currencyId ?? null
  return (
    <>
      <p data-testid="sales-risk-result" data-result={check.result} role={check.result === 'Over' ? 'alert' : 'status'}>
        <span className={pages.muted}>{T.resultLabel}: </span>
        <StatusIndicator tone={RESULT_TONE[check.result]}>{T.result[check.result]}</StatusIndicator>
      </p>
      <p className={check.result === 'Within' ? pages.muted : pages.notice} data-testid="sales-risk-result-note">{T.resultNote[check.result]}</p>
      {showAmounts ? null : <p className={pages.muted} role="note" data-testid="sales-risk-withheld">{T.withheld}</p>}
      {nothingAnswered ? <p className={pages.muted} data-testid="sales-risk-empty">{T.noSourceYet}</p> : null}

      <h3 className={pages.caption}>{T.limitHeading}</h3>
      <dl className={pages.facts} data-testid="sales-risk-limit">
        <div>
          <dt>{T.creditLimit}</dt>
          <dd className={pages.numericValue} data-testid="sales-risk-credit-limit">
            <FigureAmounts figure={check.creditLimit} currencyName={currencyName} showAmounts={showAmounts} />
          </dd>
        </div>
        <div>
          <dt>{T.used}</dt>
          <dd className={pages.numericValue} data-testid="sales-risk-used">
            <TotalAmount totals={check.inLimitCurrency} value={check.inLimitCurrency?.grossRisk ?? null}
              currencyName={currencyName} showAmounts={showAmounts} />
          </dd>
        </div>
        <div>
          <dt>{T.usedWithThisOrder}</dt>
          <dd className={pages.numericValue} data-testid="sales-risk-used-with-order">
            <TotalAmount totals={check.inLimitCurrency} value={check.inLimitCurrency?.grossRiskWithThisOrder ?? null}
              currencyName={currencyName} showAmounts={showAmounts} />
          </dd>
        </div>
        <div>
          <dt>{T.available}</dt>
          <dd data-testid="sales-risk-available">{T.availableNotServed}</dd>
        </div>
        <div>
          <dt>{T.limitCurrency}</dt>
          <dd data-testid="sales-risk-limit-currency">{limitCurrency === null ? T.limitCurrencyUnknown : currencyName(limitCurrency)}</dd>
        </div>
      </dl>

      <h3 className={pages.caption}>{T.localHeading}</h3>
      <p className={pages.muted}>{T.localNote}</p>
      {check.inLocalCurrency === null ? (
        <p className={pages.muted} data-testid="sales-risk-local-unknown">{T.localUnknown}</p>
      ) : (
        <dl className={pages.facts} data-testid="sales-risk-local">
          <div>
            <dt>{T.used}</dt>
            <dd className={pages.numericValue} data-testid="sales-risk-local-used">
              <TotalAmount totals={check.inLocalCurrency} value={check.inLocalCurrency.grossRisk} currencyName={currencyName}
                showAmounts={showAmounts} />
            </dd>
          </div>
          <div>
            <dt>{T.usedWithThisOrder}</dt>
            <dd className={pages.numericValue} data-testid="sales-risk-local-used-with-order">
              <TotalAmount totals={check.inLocalCurrency} value={check.inLocalCurrency.grossRiskWithThisOrder}
                currencyName={currencyName} showAmounts={showAmounts} />
            </dd>
          </div>
        </dl>
      )}

      <h3 id={figuresId} className={pages.caption}>{T.figuresCaption}</h3>
      <div className={pages.tableWrap}>
        <table className={pages.table} aria-labelledby={figuresId} data-testid="sales-risk-figures">
          <thead><tr>
            <th scope="col">{T.column.kind}</th>
            <th scope="col">{T.column.state}</th>
            <th scope="col">{T.column.amount}</th>
            <th scope="col">{T.column.taxBasis}</th>
            <th scope="col">{T.column.reason}</th>
          </tr></thead>
          <tbody>{figures.map((figure) => (
            <tr key={figure.kind} data-testid={`sales-risk-figure-${figure.kind}`} data-state={figure.state}>
              <th scope="row">{T.kind[figure.kind]}</th>
              <td><StatusIndicator tone={STATE_TONE[figure.state]}>{T.state[figure.state]}</StatusIndicator></td>
              <td className={pages.numericValue} data-testid={`sales-risk-figure-${figure.kind}-amount`}>
                <FigureAmounts figure={figure} currencyName={currencyName} showAmounts={showAmounts} />
              </td>
              <td>{figure.taxBasis === null ? T.notAvailable : T.taxBasis[figure.taxBasis]}</td>
              <td data-testid={`sales-risk-figure-${figure.kind}-reason`}>
                {figure.reasonCode === null ? T.notAvailable : riskReasonText(figure.reasonCode)}
              </td>
            </tr>
          ))}</tbody>
        </table>
      </div>
      <p className={pages.muted}>{T.collateralNote}</p>

      {check.reasons.length === 0 ? null : (
        <>
          <h3 className={pages.caption}>{T.reasonsHeading}</h3>
          <ul data-testid="sales-risk-reasons">
            {check.reasons.map((reason) => (
              <li key={`${reason.component}:${reason.code}`} data-code={reason.code}>
                {`${T.reasonComponent[reason.component]}: ${riskReasonText(reason.code)}`}
              </li>
            ))}
          </ul>
        </>
      )}
    </>
  )
}

/** A figure's amounts, one per currency; never a number when it is not available or hidden from this caller. */
function FigureAmounts({ figure, currencyName, showAmounts }: {
  readonly figure: RiskFigure
  readonly currencyName: CurrencyName
  readonly showAmounts: boolean
}) {
  if (figure.state !== 'Available') return <>{T.notAvailable}</>
  if (!showAmounts || figure.amounts === null) return <>{T.hidden}</>
  if (figure.amounts.length === 0) return <>{T.recordedZero}</>
  return <>{figure.amounts.map((amount: RiskAmount) => T.amountIn(amount.amount, currencyName(amount.currencyId))).join(' · ')}</>
}

/** One server total in its currency; "Yok" when the server could not make it, "Gizli" when hidden from this caller. */
function TotalAmount({ totals, value, currencyName, showAmounts }: {
  readonly totals: RiskTotals | null
  readonly value: string | null
  readonly currencyName: CurrencyName
  readonly showAmounts: boolean
}) {
  if (totals === null) return <>{T.notAvailable}</>
  if (!showAmounts) return <>{T.hidden}</>
  return <>{value === null ? T.notAvailable : T.amountIn(value, currencyName(totals.currencyId))}</>
}
