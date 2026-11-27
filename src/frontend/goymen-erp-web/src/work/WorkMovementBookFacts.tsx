import { CREDIT_MOVEMENT_TEXT } from '../finance/creditMovementText'
import { BOOK_VALUE_TEXT } from '../finance/cashExchangeBookValueText'
import { ACCOUNT_KIND_LABEL, TRANSFER_KIND_LABEL } from '../finance/cashTransferText'
import type { WorkCreditMovementPreview, WorkCashExchangeBookValuePreview } from './workMovementBookDecision'
import { WORK_DECISION_TEXT as T } from './workDecisionText'
import styles from './Work.module.css'
const hidden=new Set(['sourceFactsDigest','transferSourceHash'])
function valueText(key:string,value:unknown):string {
  if(value===null)return T.reversalAbsent
  if(typeof value==='boolean')return value?T.bankYes:T.bankNo
  if(key==='kind'&&typeof value==='string'){
    if(Object.hasOwn(CREDIT_MOVEMENT_TEXT.kinds,value))return CREDIT_MOVEMENT_TEXT.kinds[value as keyof typeof CREDIT_MOVEMENT_TEXT.kinds]
    if(Object.hasOwn(CREDIT_MOVEMENT_TEXT.components,value))return CREDIT_MOVEMENT_TEXT.components[value as keyof typeof CREDIT_MOVEMENT_TEXT.components]
    if(Object.hasOwn(TRANSFER_KIND_LABEL,value))return TRANSFER_KIND_LABEL[value as keyof typeof TRANSFER_KIND_LABEL]
  }
  if((key==='sourceKind'||key==='targetKind')&&typeof value==='string'&&Object.hasOwn(ACCOUNT_KIND_LABEL,value))return ACCOUNT_KIND_LABEL[value as keyof typeof ACCOUNT_KIND_LABEL]
  if(key==='status')return value==='Pending'||value==='Proposed'?BOOK_VALUE_TEXT.pending:value==='Approved'?BOOK_VALUE_TEXT.approved:BOOK_VALUE_TEXT.rejected
  if(key==='source'&&value==='Tcmb')return T.movementBookFields.tcmb
  if(key==='source'&&value==='FunctionalCurrency')return T.movementBookFields.functionalCurrency
  return String(value)
}
function Facts({value}:{readonly value:unknown}){
  if(Array.isArray(value))return <ol>{value.map((item,index)=><li key={index}><Facts value={item}/></li>)}</ol>
  if(value!==null&&typeof value==='object')return <dl className={styles.facts}>{Object.entries(value).filter(([key])=>!hidden.has(key)).map(([key,item])=><div key={key}>
    <dt>{T.movementBookFields[key as keyof typeof T.movementBookFields]??T.nativeFinanceFields[key as keyof typeof T.nativeFinanceFields]}</dt>
    <dd>{item!==null&&typeof item==='object'?<Facts value={item}/>:valueText(key,item)}</dd></div>)}</dl>
  return null
}
export function WorkMovementBookFacts({movement,book}:{readonly movement?:WorkCreditMovementPreview;readonly book?:WorkCashExchangeBookValuePreview}){
  return <section aria-label={movement?T.creditMovementProposal:T.cashBookProposal}><h3>{movement?T.creditMovementProposal:T.cashBookProposal}</h3><p>{T.nativeFinanceReadOnly}</p>
    <Facts value={movement??(book?{...book,proposal:{...book.proposal,targetFunctionalValueTl:book.proposal.targetFunctionalValueTl??null}}:undefined)}/>{book&&book.proposal.targetFunctionalValueTl==null?<p role="status">{BOOK_VALUE_TEXT.targetValueMissing}</p>:null}
  </section>
}
