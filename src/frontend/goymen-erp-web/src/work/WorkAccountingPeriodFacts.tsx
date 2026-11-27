import { Fragment } from 'react'
import { PERIOD_STATUS_LABEL } from '../accounting/accountingText'
import type { WorkAccountingPeriodOpening, WorkAccountingPeriodReopening } from './workAccountingPeriodDecision'
import { WORK_DECISION_TEXT as T } from './workDecisionText'

export function WorkAccountingPeriodFacts({opening,reopening}: { readonly opening?: WorkAccountingPeriodOpening; readonly reopening?: WorkAccountingPeriodReopening }) {
  const human = reopening ?? opening
  if (human === undefined) return null
  const facts = (value: object) => <dl>{Object.entries(value).filter(([key]) => key !== 'sourceHash').map(([key,value]) =>
    <Fragment key={key}><dt>{T.accountingPeriodFields[key as keyof typeof T.accountingPeriodFields]}</dt><dd>{key === 'status' ? PERIOD_STATUS_LABEL[human.period.status]
      : value === null ? T.packageAbsent : typeof value === 'boolean' ? value ? T.accountingYes : T.accountingNo : String(value)}</dd></Fragment>)}</dl>
  const title = reopening ? T.accountingReopeningTitle : T.accountingPeriodTitle
  return <section aria-label={title}><h3>{title}</h3>{facts(human.period)}
    {reopening ? <><h4>{T.accountingReopeningProposal}</h4>{facts(reopening.reopening)}
      <h4>{T.accountingClosureTitle}</h4>{facts(reopening.closure)}{reopening.laterPeriodClosed ? <p>{T.accountingLaterClosed}</p> : null}</> : null}
  </section>
}
