/**
 * FNS-D30 (Sinan 2026-11-12) / SAT-D310 / spec §S3 on S-04: inside the single-shipment risk exception section, why the order's
 * shipment is held (over the limit, no valid limit — "limit tanımlı değil" — or the risk could not be read), from the shared
 * API-06 risk read. A missing limit is never shown as unlimited or zero, and no amount is shown (SAT-D162).
 *
 * The approver's optional shortcut to define the limit on the Cari card (FNS-D30 (2)) appears only on "no valid limit", only to
 * a caller holding the exception approval, the Cari card and the Finance credit-limit list, and only with the limit propose or
 * approve control: with approve the limit takes effect in one step (FNS-D27a (1)), with propose only a proposal is opened
 * (FNS-D27a (2)). Without either it is hidden. The screen decides nothing: the Logistics gate holds the shipment and the server
 * measures every exception and credit-limit command afresh. A failed, forbidden or stale risk read names no category and keeps
 * the exception request open — its decision is the server's.
 */
import { Link } from 'react-router'
import type { RiskCheck } from './riskCheck'
import { riskHoldCategory } from './riskExceptionHold'
import { RISK_EXCEPTION_TEXT } from './riskText'
import type { ViewOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import pages from './SalesPages.module.css'

const T = RISK_EXCEPTION_TEXT.hold
const S = RISK_EXCEPTION_TEXT.shortcut

/** Which credit-limit control the caller holds for the shortcut; 'none' hides it. */
export type CreditLimitShortcut = 'approve' | 'propose' | 'none'

export function RiskExceptionHoldNotice({
  outcome, shortcut, cariHref, onRetry,
}: {
  /** The shared risk read (undefined while loading). */
  readonly outcome: ViewOutcome<RiskCheck> | undefined
  /** Already gated by the container on the exception approval, the Cari card and the credit-limit list. */
  readonly shortcut: CreditLimitShortcut
  /** The order customer's Cari card. */
  readonly cariHref: string
  readonly onRetry: () => void
}) {
  let body
  if (outcome === undefined || outcome.type === 'loading') {
    body = <p className={pages.muted} role="status" data-testid="sales-risk-hold-loading">{T.loading}</p>
  } else if (outcome.type === 'ok-single') {
    const category = riskHoldCategory(outcome.data)
    body = category === null ? (
      <p data-testid="sales-risk-hold-within" data-category="Within">{T.within}</p>
    ) : (
      <>
        <p role="status" data-testid="sales-risk-hold-category" data-category={category}>{T.category[category]}</p>
        {category === 'CreditLimitNotSet' && shortcut !== 'none' ? (
          <div className={pages.actions} data-testid="sales-risk-hold-shortcut" data-shortcut={shortcut}>
            <Link to={cariHref}>{shortcut === 'approve' ? S.defineLimit : S.proposeLimit}</Link>
            <p className={pages.muted}>{shortcut === 'approve' ? S.defineLimitHint : S.proposeLimitHint}</p>
            <p className={pages.muted}>{S.optional}</p>
          </div>
        ) : null}
      </>
    )
  } else {
    const text = outcome.type === 'http' && outcome.status === 403 ? T.forbidden
      : outcome.type === 'http' && outcome.status === 503 ? T.serviceUnavailable
        : outcome.type === 'fence-stale' ? T.stale
          : T.unreadable
    body = (
      <>
        <p className={pages.muted} role="status" data-testid="sales-risk-hold-unreadable">{text}</p>
        {outcome.type === 'http' && outcome.status === 403 ? null : (
          <Button type="button" variant="secondary" onClick={onRetry} data-testid="sales-risk-hold-retry">{T.retry}</Button>
        )}
      </>
    )
  }
  return (
    <div data-testid="sales-risk-hold" aria-busy={outcome === undefined || outcome.type === 'loading'}>
      <h3>{T.heading}</h3>
      {body}
      <p className={pages.muted}>{T.note}</p>
    </div>
  )
}
