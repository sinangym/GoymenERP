import { Fragment } from 'react'
import { REVERSAL_STATUS_LABEL } from '../accounting/accountingText'
import type { WorkJournalReversalPreview } from './workJournalReversalDecision'
import { WORK_DECISION_TEXT as T } from './workDecisionText'

export function WorkJournalReversalFacts({human}: { readonly human: WorkJournalReversalPreview }) {
 return <section aria-label={T.journalReversalTitle}><h3>{T.journalReversalTitle}</h3><dl>
  {Object.entries(human.proposal).map(([key,value])=><Fragment key={key}><dt>{T.journalReversalFields[key as keyof typeof T.journalReversalFields]}</dt>
   <dd>{key==='status'?REVERSAL_STATUS_LABEL[human.proposal.status]:value===null?T.packageAbsent:String(value)}</dd></Fragment>)}
 </dl></section>
}
