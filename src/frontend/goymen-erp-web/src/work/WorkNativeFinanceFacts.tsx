import { CREDIT_TEXT } from '../finance/creditAccountText'
import { OPENING_BALANCE_TARGET, OPENING_BALANCE_DIRECTION, OPENING_BALANCE_STATUS, OPENING_BALANCE_REVERSAL_STATUS } from '../finance/OpeningBalanceText'
import type { WorkBankCreditLimitPreview, WorkOpeningBalancePreview } from './workNativeFinanceDecision'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import styles from './Work.module.css'
function Facts({ values, reversal = false }: { readonly values: Record<string, unknown>; readonly reversal?: boolean }) {
  return <dl className={styles.facts}>{Object.entries(values).map(([key,value]) => <div key={key}>
    <dt>{T.nativeFinanceFields[key as keyof typeof T.nativeFinanceFields]}</dt><dd>{value === null ? T.reversalAbsent
      : key === 'group' ? CREDIT_TEXT.groups[value as keyof typeof CREDIT_TEXT.groups]
      : key === 'targetKind' ? OPENING_BALANCE_TARGET[value as keyof typeof OPENING_BALANCE_TARGET]
      : key === 'direction' ? OPENING_BALANCE_DIRECTION[value as keyof typeof OPENING_BALANCE_DIRECTION]
      : key === 'status' ? reversal ? OPENING_BALANCE_REVERSAL_STATUS[value as keyof typeof OPENING_BALANCE_REVERSAL_STATUS] : OPENING_BALANCE_STATUS[value as keyof typeof OPENING_BALANCE_STATUS]
      : typeof value === 'boolean' ? value ? T.bankYes : T.bankNo : String(value)}</dd>
  </div>)}</dl>
}
export function WorkNativeFinanceFacts({ bank, opening, reversal }: { readonly bank?: WorkBankCreditLimitPreview; readonly opening?: WorkOpeningBalancePreview; readonly reversal: boolean }) {
  const title = bank ? T.bankLimitProposal : reversal ? T.openingReversalProposal : T.openingProposal
  return <section aria-label={title}><h3>{title}</h3><p>{T.nativeFinanceReadOnly}</p>
    {bank ? <><Facts values={Object.fromEntries(Object.entries(bank.limit).filter(([key])=>key!=='source'))} />
      <Facts values={{ proposedBy: bank.proposedBy }} /><h4>{T.nativeFinanceSource}</h4>
      <Facts values={Object.fromEntries(Object.entries(bank.limit.source).filter(([key])=>key!=='sourceDocument'))} />
      <Facts values={{...bank.limit.source.sourceDocument}} /></> : null}
    {opening ? <><Facts values={Object.fromEntries(Object.entries(opening.openingBalance).filter(([key])=>key!=='reversal'))} />
      {opening.openingBalance.reversal ? <Facts values={{...opening.openingBalance.reversal}} reversal /> : null}</> : null}
  </section>
}
