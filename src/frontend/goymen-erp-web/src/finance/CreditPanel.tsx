/**
 * F018 plan §L3 — the Finance panel of the Cari card: one company account's credit limit, open receivables, held
 * ("mevcut") and weighted ("sayılan") collateral, collateral utilization and documentary risk (FNS-D27 (2),
 * FNS-D31 (4), FNS-D58).
 *
 * What the panel does, and nothing more:
 *  - READS the server's figures and shows each with its own state. A figure Finance cannot compute exactly is
 *    "hesaplanamadı" (fail-closed, e.g. while the Settings collateral type catalog cannot answer); a missing limit is
 *    "Limit tanımlı değil" (never zero, never unlimited); a figure the caller may not read is withheld.
 *  - Never adds, converts, subtracts or compares amounts: the gross risk and the limit headroom are the Sales risk
 *    calculator's, not this panel's. The documentary risk is shown on its own.
 *  - Edits go to the Finance screens (links drawn only for the capability the server granted).
 *
 * A side read of the card: 401/403 are reported to the shell boundary, like every other read.
 */

import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { FINANCE_ROUTE } from './capabilities'
import { CREDIT_PANEL_FIGURES, type CreditPanelFigure, type CreditPanelFigureName, type CreditPanelView } from './CreditPanelContract'
import { fetchCreditPanel } from './CreditPanelHttp'
import {
  CREDIT_PANEL_HINT,
  CREDIT_PANEL_LABEL,
  CREDIT_PANEL_TEXT as T,
  creditPanelReasonText,
} from './CreditPanelText'
import { WithCurrencyCodes, currencyShown, type CurrencyCodes } from './currencyCodes'
import { formatDecimalTextTr } from './decimalText'
import styles from './Finance.module.css'

interface Props {
  readonly partyId: string
  readonly companyAccountId: string
  readonly companyId: string
  readonly namespace: string
  /** Whether the shell granted the credit limit list; the link is drawn only then. */
  readonly canOpenCreditLimits: boolean
  /** Whether the shell granted the collateral list; the link is drawn only then. */
  readonly canOpenCollaterals: boolean
}

function stateWord(figure: CreditPanelFigure): string {
  switch (figure.state) {
    case 'NotSet':
      return T.notSet
    case 'Withheld':
      return T.withheld
    case 'Unavailable':
      return T.unavailable
    default:
      return T.zero
  }
}

function FigureValue({ name, figure, codes }: {
  readonly name: CreditPanelFigureName
  readonly figure: CreditPanelFigure
  readonly codes: CurrencyCodes
}) {
  const testId = `credit-panel-${name}`
  if (figure.state !== 'Available') {
    return (
      <span data-testid={testId} data-figure-state={figure.state}>
        <strong>{stateWord(figure)}</strong> — {creditPanelReasonText(figure.reasonCode)}
      </span>
    )
  }
  if (figure.amounts.length === 0) {
    return <span data-testid={testId} data-figure-state="Available">{T.zero}</span>
  }
  return (
    <ul data-testid={testId} data-figure-state="Available">
      {figure.amounts.map((line) => (
        <li key={line.currencyId}>
          {`${formatDecimalTextTr(line.amount) ?? line.amount} ${currencyShown(line.currencyId, codes)}`}
        </li>
      ))}
    </ul>
  )
}

function Figures({ view }: { readonly view: CreditPanelView }) {
  return (
    <WithCurrencyCodes>
      {(codes) => (
        <dl data-testid="credit-panel-figures">
          {CREDIT_PANEL_FIGURES.map((name) => (
            <div key={name} className={styles.field}>
              <dt title={CREDIT_PANEL_HINT[name]}>{CREDIT_PANEL_LABEL[name]}</dt>
              <dd><FigureValue name={name} figure={view[name]} codes={codes} /></dd>
            </div>
          ))}
        </dl>
      )}
    </WithCurrencyCodes>
  )
}

export function CreditPanel({
  partyId,
  companyAccountId,
  companyId,
  namespace,
  canOpenCreditLimits,
  canOpenCollaterals,
}: Props) {
  const boundary = useAuthBoundary()
  const panel = useQuery({
    queryKey: [namespace, 'finance-party-credit-panel', companyId, partyId, companyAccountId],
    queryFn: async ({ signal }) => {
      const security = boundary.capture()
      const outcome = await fetchCreditPanel(companyId, partyId, companyAccountId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, security)
      }
      return outcome
    },
  })

  const state = classifyOutcome(panel.data ?? { type: 'loading' }) as RemoteViewState<CreditPanelView>
  const retry = () => void panel.refetch()

  const body = (() => {
    if (state.kind === 'forbidden') {
      return <p role="alert" data-state="forbidden">{T.forbidden}</p>
    }
    if (state.kind !== 'ready') return <StateBlock state={state} subject={T.subject} onRetry={retry} />
    const view = state.data
    return (
      <>
        <p data-testid="credit-panel-as-of">{`${T.asOf}: ${view.asOf}`}</p>
        <Figures view={view} />
      </>
    )
  })()

  return (
    <section aria-label={T.title} className={styles.detail} data-testid="credit-panel"
      data-state={state.kind}>
      <h3>{T.title}</h3>
      <p className={styles.notice}>{T.notice}</p>
      {body}
      {canOpenCreditLimits ? <Link to={FINANCE_ROUTE.creditLimitsList}>{T.openCreditLimits}</Link> : null}
      {canOpenCollaterals ? <Link to={FINANCE_ROUTE.collateralsList}>{T.openCollaterals}</Link> : null}
    </section>
  )
}
