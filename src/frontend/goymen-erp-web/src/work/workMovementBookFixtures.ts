import { COMPANY_A } from '../finance/testHarness'
export const MOVEMENT_BOOK_ID='bc430000-0000-4000-8000-000000000001'
export const TRANSFER_ID='bc430000-0000-4000-8000-000000000002'
export const ACCOUNT_ID='bc430000-0000-4000-8000-000000000003'
export const OTHER_ID='bc430000-0000-4000-8000-000000000004'
export const CURRENCY_ID='bc430000-0000-4000-8000-000000000005'
export const TL_ID='bc430000-0000-4000-8000-000000000006'
const at='0001-01-02T09:00:00Z',date='0001-01-02'
const ref=(role:string,recordId:string)=>({role,recordId,label:'SENTETIK '+role,link:null})
const rule=(ruleCode:string,actual:string|null)=>({ruleCode,actual,threshold:null,deviation:null,unit:null})
const base=(kind:string)=>({companyId:COMPANY_A,kind,itemId:MOVEMENT_BOOK_ID,documentLabel:'SENTETIK owner caption',sourceVersion:'1',pending:true,approvalReasonRequired:false,rejectionSupported:true})
// Full recorded native wire fixtures; no calculation or copied mutable parent account.
export function movementPreview(){return {...base('finance.credit-movement.v1'),
  references:[ref('credit-account',ACCOUNT_ID),ref('currency',CURRENCY_ID),ref('bank-account',OTHER_ID)],rules:[rule('finance.credit-movement-drawdown','95')],
  creditMovement:{movement:{id:MOVEMENT_BOOK_ID,companyId:COMPANY_A,version:'1',status:'Proposed',currencyId:CURRENCY_ID,isOwnProposal:false,proposedAtUtc:at,decidedAtUtc:null,rejectionReason:null,reversedByMovementId:null,openingPeriodVersion:null,
    source:{creditAccountId:ACCOUNT_ID,expectedCreditVersion:'2',kind:'Drawdown',sourceDocument:{reference:'SENTETIK immutable movement document',documentDate:date,revision:'1'},transactionDate:date,accountingDate:date,
      components:[{kind:'principal',amount:'001250.5000',functionalAmount:null},{kind:'interest',amount:'0.0001',functionalAmount:'0000.01'}],bankEffect:{bankAccountId:OTHER_ID,amount:'00095.00',sourceReference:'SENTETIK bank reference'},replacesOpeningMovementId:null,expectedOpeningMovementVersion:null,reason:'SENTETIK movement reason'},
    moneyProfile:{profileId:OTHER_ID,profileVersion:1,currencyScale:2,smallestCurrencyUnit:'0.01'}},proposedBy:'SENTETIK maker',bankDisplayName:'SENTETIK bank caption',sourceFactsDigest:'a'.repeat(64)}}}
export function bookPreview(){
  const profile={profileId:OTHER_ID,version:'1'},sourceMoney={amount:'100',currencyId:CURRENCY_ID},targetMoney={amount:'3300',currencyId:TL_ID}
  return {...base('finance.cash-exchange-book-value.v1'),approvalSupported:true,
    references:[ref('cash-transfer',TRANSFER_ID),ref('evidence-document',OTHER_ID)],rules:[rule('finance.cash-exchange-source-book-value','3500'),rule('finance.cash-exchange-target-functional-value','3300')],
    cashExchangeBookValue:{transfer:{id:TRANSFER_ID,companyId:COMPANY_A,documentNumber:{number:'SENTETIK-CEX-1',policyVersion:'1'},
      preparation:{kind:'CurrencyExchange',sourceKind:'Bank',sourceAccountId:ACCOUNT_ID,sourceMoney,sourceProfile:profile,targetKind:'Cash',targetAccountId:OTHER_ID,targetMoney,targetProfile:profile,rate:'33.00',occurredOn:date,valueOn:null,reference:'SENTETIK FX reference',description:'SENTETIK full transfer description'},
      recordedBy:'SENTETIK treasurer',recordedAtUtc:at,transferConfirmed:true,version:'1',
      exchange:{companyId:COMPANY_A,occurredOn:date,sourceMoney,targetMoney,sourceProfile:profile,targetProfile:profile,transactionRate:'33.00',comparisonRate:'34.00',rateDeviation:'-1.00',
        exactTransactionRate:{numerator:'33',denominator:'1'},exactComparisonRate:{numerator:'34',denominator:'1'},
        sourceComparison:{currencyId:CURRENCY_ID,sellingRate:'34',rateDay:date,source:'Tcmb',recordId:OTHER_ID,recordVersion:'1',bulletinDate:'0001-01-01',provenance:'SENTETIK immutable bulletin'},
        targetComparison:{currencyId:TL_ID,sellingRate:'1',rateDay:date,source:'FunctionalCurrency',recordId:null,recordVersion:null,bulletinDate:null,provenance:null},
        sourceReferenceTl:{currencyId:TL_ID,amount:'3400',sourceVersion:'finance.cash-transfer.tl-reference.v1'},targetReferenceTl:{currencyId:TL_ID,amount:'3300',sourceVersion:'finance.cash-transfer.tl-reference.v1'}}},
      proposal:{id:MOVEMENT_BOOK_ID,companyId:COMPANY_A,transferId:TRANSFER_ID,transferVersion:'1',transferSourceHash:'b'.repeat(64),sourceBookValueTl:'3500',targetFunctionalValueTl:'3300',evidence:{documentId:OTHER_ID,versionNo:2,sha256:'c'.repeat(64)},
        reason:'SENTETIK recorded book reason',preparedBy:'SENTETIK maker',preparedAtUtc:at,status:'Pending',version:'1',decidedBy:null,decidedAtUtc:null,decisionReason:null}}}
}
