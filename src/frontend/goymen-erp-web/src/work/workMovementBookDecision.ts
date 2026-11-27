import { decodeCreditMovement, type CreditMovement } from '../finance/creditMovementApi'
import { decodeBookValue, type CashExchangeBookValue } from '../finance/cashExchangeBookValue'
import { decodeCashTransferView, type CashTransferView } from '../finance/cashTransferContract'
import { hasExactKeys } from '../finance/wireShape'
import { isBoundUuid } from '../finance/contract'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'
export interface WorkCreditMovementPreview { readonly movement: CreditMovement; readonly proposedBy: string; readonly bankDisplayName: string | null; readonly sourceFactsDigest: string }
export interface WorkCashExchangeBookValuePreview { readonly transfer: CashTransferView; readonly proposal: Omit<CashExchangeBookValue,'targetFunctionalValueTl'> & {readonly targetFunctionalValueTl?:string|null} }
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const text=(v:unknown,max:number):v is string=>typeof v==='string'&&v.trim().length>0&&v.length<=max
const canonical=(v:unknown,max:number)=>text(v,max)&&v===v.trim()
const amount=(v:unknown,zero=false):v is string=>typeof v==='string'&&v.length<=128&&v===v.trim()&&/^[0-9]+(?:\.[0-9]+)?$/.test(v)&&(zero||/[1-9]/.test(v))
const utc=(v:unknown)=>transferDispatchUtc(v)&&v===v.trim()
const digest=(v:unknown)=>typeof v==='string'&&v.length===64&&/^[a-f0-9]+$/.test(v)
// Syntax and owner identity only: stored money and comparison values are never recalculated.
function exactTransferScalars(v:unknown):boolean {
  if(Array.isArray(v))return v.every(exactTransferScalars)
  if(!record(v))return true
  return Object.entries(v).every(([key,value])=>{
    if(value===null)return true
    if(key==='id'||key.endsWith('Id'))return guid(value)
    if(key==='recordedAtUtc')return utc(value)
    if(key==='recordedBy')return canonical(value,128)
    if(key==='version'||key==='recordVersion')return typeof value==='string'&&value===value.trim()
    if(['amount','transactionRate','comparisonRate','rateDeviation','sellingRate','rate','numerator','denominator'].includes(key))return typeof value==='string'&&value===value.trim()
    return exactTransferScalars(value)
  })
}
export function validMovementBookPreview(v:Record<string,unknown>,company:string,kind:string,id:string,date:(value:string)=>boolean):boolean {
  if(v.approvalReasonRequired!==false||v.rejectionSupported!==true||!Array.isArray(v.rules)||!Array.isArray(v.references))return false
  let targets:readonly(readonly[string,string])[]
  if(kind==='finance.credit-movement.v1'){
    const h=v.creditMovement
    if(!record(h)||!hasExactKeys(h,['movement','proposedBy','bankDisplayName','sourceFactsDigest'])||!text(h.proposedBy,128)||!digest(h.sourceFactsDigest)
      ||h.bankDisplayName!==null&&typeof h.bankDisplayName!=='string'||!record(h.movement)||!record(h.movement.source)||!guid(h.movement.source.creditAccountId))return false
    const m=decodeCreditMovement(h.movement,company,h.movement.source.creditAccountId,date)
    if(m===null||!guid(m.id)||!same(m.id,id)||m.isOwnProposal||!utc(m.proposedAtUtc)||m.decidedAtUtc!==null&&!utc(m.decidedAtUtc)
      ||m.version!==(m.status==='Proposed'?'1':m.reversedByMovementId!==null?'3':'2')||v.sourceVersion!==m.version||v.pending!==(m.status==='Proposed')
      ||m.reversedByMovementId!==null&&(!guid(m.reversedByMovementId)||m.status!=='Approved'||m.source.kind!=='OpeningBalance')
      ||m.status==='Approved'&&m.rejectionReason!==null||m.rejectionReason!==null&&!canonical(m.rejectionReason,1000)
      ||!guid(m.currencyId)||!guid(m.moneyProfile.profileId)||!amount(m.moneyProfile.smallestCurrencyUnit)
      ||m.openingPeriodVersion!==null&&!canonical(m.openingPeriodVersion,64)
      ||(m.source.kind==='OpeningBalance')!==(m.openingPeriodVersion!==null))return false
    const s=m.source
    if(!canonical(s.expectedCreditVersion,64)||!canonical(s.reason,1000)||!canonical(s.sourceDocument.reference,200)||!canonical(s.sourceDocument.revision,100)
      ||[s.sourceDocument.documentDate,s.transactionDate,s.accountingDate].some(d=>d==='0001-01-01')
      ||s.components.some(c=>!amount(c.amount,true)||c.functionalAmount!==null&&!amount(c.functionalAmount,true))
      ||!s.components.some(c=>/[1-9]/.test(c.amount))
      ||(s.kind==='Drawdown'||s.kind==='OpeningBalance')&&!s.components.some(c=>c.kind==='principal'&&/[1-9]/.test(c.amount))
      ||s.kind==='InterestAccrual'&&s.components.some(c=>c.kind==='principal')
      ||(s.replacesOpeningMovementId===null?(s.expectedOpeningMovementVersion!==null||s.accountingDate!==s.transactionDate)
        :(!guid(s.replacesOpeningMovementId)||s.kind!=='OpeningBalance'||!canonical(s.expectedOpeningMovementVersion,64))))return false
    if(s.bankEffect!==null&&(!guid(s.bankEffect.bankAccountId)||!amount(s.bankEffect.amount)||!canonical(s.bankEffect.sourceReference,200)))return false
    const rule=v.rules[0],code={Drawdown:'drawdown',Repayment:'repayment',InterestAccrual:'interest-accrual',OpeningBalance:'opening-balance'}[s.kind]
    if(v.rules.length!==1||!record(rule)||rule.ruleCode!=='finance.credit-movement-'+code||rule.threshold!==null||rule.deviation!==null||rule.unit!==null
      ||(s.bankEffect===null?rule.actual!==null:!amount(rule.actual,true)))return false
    targets=[['credit-account',s.creditAccountId],['currency',m.currencyId],...(s.bankEffect?[['bank-account',s.bankEffect.bankAccountId] as const]:[]),
      ...(s.replacesOpeningMovementId?[['replaced-opening-movement',s.replacesOpeningMovementId] as const]:[])]
  }else{
    const h=v.cashExchangeBookValue
    if(!record(h)||!hasExactKeys(h,['transfer','proposal']))return false
    const transfer=decodeCashTransferView(h.transfer,company,date)
    if(!transfer.ok||!exactTransferScalars(h.transfer)||transfer.value.version!=='1'||transfer.value.preparation.kind!=='CurrencyExchange'||transfer.value.exchange===undefined)return false
    const p=decodeBookValue(h.proposal,company,transfer.value.id,true)
    if(p===null||!guid(p.id)||!same(p.id,id)||!guid(p.transferId)||!utc(p.preparedAtUtc)||p.decidedAtUtc!==null&&!utc(p.decidedAtUtc)
      ||v.sourceVersion!==p.version||v.pending!==(p.status==='Pending')||v.approvalSupported!==(p.targetFunctionalValueTl!==null)
      ||!digest(p.transferSourceHash)||!guid(p.evidence.documentId)||typeof p.evidence.sha256!=='string'||p.evidence.sha256.length!==64
      ||!canonical(p.reason,1000)||!canonical(p.preparedBy,128)||!amount(p.sourceBookValueTl)||p.targetFunctionalValueTl!==null&&!amount(p.targetFunctionalValueTl)
      ||p.decidedBy!==null&&!canonical(p.decidedBy,128)||p.decisionReason!==null&&!canonical(p.decisionReason,1000))return false
    const [source,target]=v.rules
    if(v.rules.length!==2||!record(source)||!record(target)||source.ruleCode!=='finance.cash-exchange-source-book-value'||!amount(source.actual,true)
      ||target.ruleCode!==(p.targetFunctionalValueTl===null?'finance.cash-exchange-target-functional-value-missing':'finance.cash-exchange-target-functional-value')
      ||(p.targetFunctionalValueTl===null?target.actual!==null:!amount(target.actual,true))
      ||[source,target].some(r=>r.threshold!==null||r.deviation!==null||r.unit!==null))return false
    targets=[['cash-transfer',p.transferId],['evidence-document',p.evidence.documentId]]
  }
  return v.references.length===targets.length&&targets.every(([role,target])=>v.references instanceof Array&&v.references.filter(r=>record(r)&&r.role===role&&same(r.recordId,target)&&r.link===null).length===1)
}
