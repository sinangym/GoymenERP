import { account, costRatio, roundingProof, equalRounding, physicalClaim, type InventoryCostProposal } from '../accounting/inventoryCostApi'
import type { InventoryCostClaimSuccession } from '../accounting/inventoryCostSuccession'
import type { InventoryCostCancellation, InventoryCostCancellationComponent } from '../accounting/inventoryCostCancellation'
import type { InventoryCostStaleClosure, InventoryCostRenewal } from '../accounting/inventoryCostStaleContract'
import type { PostedManufacturingCostShare, ManufacturingCostShipment, CostProofRatio } from '../accounting/postedManufacturingCostProof'
import type { PostedPurchaseCostShare, PurchaseCostCandidate, PurchaseCostShipment } from '../accounting/postedPurchaseCostProof'
import { hasExactKeys } from '../finance/wireShape'
import { isBoundUuid } from '../finance/contract'
import { decodeJournalReversalProposal } from '../accounting/journalReversalContract'
import { transferDispatchUtc } from '../inventory/transferDispatchApi'

// Transport-ready only. Root pairs the published kind/capability/route catalogs separately.
export const INVENTORY_COST_WORK_PURPOSES = {
  'accounting.invoiced-shipment-cost.v1': {capabilityId:'accounting.invoiced-shipment-cost.work-decision',routePath:'/onaylarim/accounting.invoiced-shipment-cost.v1/:approvalId'},
  'accounting.purchase-cost-difference.v1': {capabilityId:'accounting.purchase-cost-difference.work-decision',routePath:'/onaylarim/accounting.purchase-cost-difference.v1/:approvalId'},
} as const
export type InventoryCostWorkKind = keyof typeof INVENTORY_COST_WORK_PURPOSES
export const isInventoryCostWorkKind=(v:string):v is InventoryCostWorkKind=>Object.hasOwn(INVENTORY_COST_WORK_PURPOSES,v)
type Optional<T,K extends keyof T> = Omit<T,K> & Partial<Pick<T,K>>
type Preparation=NonNullable<InventoryCostProposal['preparation']>
export type WorkInventoryCostClassification=Omit<Preparation['classification'],'mappingVersion'|'productVersion'|'classificationVersion'|'invoiceLineRevision'> & {
  readonly mappingVersion?:string;readonly productVersion?:string;readonly classificationVersion?:string;readonly invoiceLineRevision:string}
export type WorkInventoryCostPreparation=Omit<Preparation,'moneyProfileVersion'|'classification'|'accountPlanPackageVersion'|'accountPlanCatalogVersion'|'accountPlanSha256'> & {
  readonly moneyProfileVersion:string;readonly classification:WorkInventoryCostClassification;readonly accountPlanPackageVersion:string;readonly accountPlanCatalogVersion?:string}
export type WorkInventoryCostManufacturingShipment=Omit<ManufacturingCostShipment<string>,'physicalProofJson'|'provenOriginalSequence'|'hasReturns'> & {readonly provenOriginalSequence?:string;readonly hasReturns:boolean}
export type WorkInventoryCostManufacturingShare=Omit<PostedManufacturingCostShare<string>,'sourcePayloadSha256'|'lineageSha256'|'historySha256'|'fingerprint'|'shipment'> & {readonly shipment:WorkInventoryCostManufacturingShipment}
export type WorkInventoryCostRecordedRounding=Omit<NonNullable<InventoryCostProposal['component']['recordedRounding']>,'moneyProfileVersion'> & {readonly moneyProfileVersion:string}
export type WorkInventoryCostPurchaseShipment=Omit<PurchaseCostShipment<string>,'physicalProofJson'|'provenOriginalSequence'> & {readonly provenOriginalSequence?:string}
export type WorkInventoryCostPurchaseApproval=Omit<PostedPurchaseCostShare<string>['approval'],'sourceProofSha256'|'fingerprint'|'journalId'> & {readonly journalId?:string}
export type WorkInventoryCostPurchaseCandidate=Omit<PurchaseCostCandidate<string>,'sourceProofSha256'|'physicalSourceSha256'|'fingerprint'|'shipment'> & {readonly shipment:WorkInventoryCostPurchaseShipment}
export type WorkInventoryCostPurchaseShare=Omit<PostedPurchaseCostShare<string>,'approval'|'candidates'|'fingerprint'> & {readonly approval:WorkInventoryCostPurchaseApproval;readonly candidates:readonly WorkInventoryCostPurchaseCandidate[]}
export type WorkInventoryCostCancellationComponent=Optional<Omit<InventoryCostCancellationComponent,'economicKey'|'sourceFingerprint'|'sourceJournalId'> & {readonly sourceJournalId?:string},'sourceJournalId'>
export type WorkInventoryCostClaimGeneration=Omit<InventoryCostClaimSuccession['claims'][number],'previousGeneration'|'previousClaimSha256'> & {readonly previousGeneration:string}
export type WorkInventoryCostClaimSuccession=Omit<InventoryCostClaimSuccession,'previousFinalizedVersion'|'previousProposalVersion'|'invoiceLineRevision'|'previousSourceSha256'|'capturedCostFingerprints'|'claims'|'reversedComponents'> & {
  readonly previousFinalizedVersion:string;readonly previousProposalVersion:string;readonly invoiceLineRevision:string;readonly claims:readonly WorkInventoryCostClaimGeneration[];readonly reversedComponents:readonly WorkInventoryCostCancellationComponent[]}
export type WorkInventoryCostCancellationProfile=Omit<InventoryCostCancellation['moneyProfiles'][number],'version'> & {readonly version:string}
export type WorkInventoryCostCancellation=Omit<InventoryCostCancellation,'version'|'invoiceLineRevision'|'components'|'moneyProfiles'|'approvedBy'|'approvedAtUtc'> & {
  readonly version:string;readonly invoiceLineRevision:string;readonly components:readonly WorkInventoryCostCancellationComponent[];readonly moneyProfiles:readonly WorkInventoryCostCancellationProfile[];readonly approvedBy?:string;readonly approvedAtUtc?:string}
export type WorkInventoryCostStaleClosure=Omit<InventoryCostStaleClosure,'preparedVersion'|'causationId'> & {readonly preparedVersion:string;readonly causationId?:string}
export type WorkInventoryCostRenewal=Omit<InventoryCostRenewal,'previousClosedVersion'> & {readonly previousClosedVersion:string}
export type WorkInventoryCostComponent=Omit<InventoryCostProposal['component'],'sourceProofSha256'|'invoiceLineId'|'invoiceLineNumber'|'locationId'|'alreadyPostedComponents'|'alreadyPostedPurchaseComponents'|'recordedRounding'|'claimSuccession'> & {
  readonly invoiceLineId?:string;readonly invoiceLineNumber?:number;readonly locationId?:string;readonly salesPhysicalClaims?:readonly InventoryCostClaimSuccession['claims'][number]['physical'][]
  readonly alreadyPostedComponents?:readonly WorkInventoryCostManufacturingShare[];readonly alreadyPostedPurchaseComponents?:readonly WorkInventoryCostPurchaseShare[]
  readonly recordedRounding?:WorkInventoryCostRecordedRounding;readonly claimSuccession?:WorkInventoryCostClaimSuccession}
export type WorkInventoryCostProposal=Omit<InventoryCostProposal,'invoiceVersion'|'version'|'component'|'preparation'|'officialInvoiceNumber'|'approvedBy'|'approvedAtUtc'|'journalId'|'cancellationId'|'reversalProposal'|'currentPostedJournalId'|'componentCancellation'|'staleClosure'|'renewedFrom'> & {
  readonly invoiceVersion:string;readonly version:string;readonly component:WorkInventoryCostComponent;readonly preparation:WorkInventoryCostPreparation
  readonly officialInvoiceNumber?:string;readonly approvedBy?:string;readonly approvedAtUtc?:string;readonly journalId?:string;readonly cancellationId?:string
  readonly reversalProposal?:NonNullable<InventoryCostProposal['reversalProposal']>;readonly currentPostedJournalId?:string;readonly componentCancellation?:WorkInventoryCostCancellation
  readonly staleClosure?:WorkInventoryCostStaleClosure;readonly renewedFrom?:WorkInventoryCostRenewal}
export interface WorkInventoryCostPreview {readonly proposal:WorkInventoryCostProposal;readonly preparedRecordedAtUtc:string}

const rec=(v:unknown):v is Record<string,unknown>=>typeof v==='object'&&v!==null&&!Array.isArray(v)
type Check=(v:unknown)=>boolean
// Closed, explicit DTO field sets. This gate validates transport facts, never computes costs/shares/rounding.
const shape=(v:unknown,required:Readonly<Record<string,Check>>,optional:Readonly<Record<string,Check>>={}):v is Record<string,unknown>=>rec(v)
  &&hasExactKeys(v,Object.keys(required),Object.keys(optional))&&Object.entries(required).every(([k,test])=>test(v[k]))
  &&Object.entries(optional).every(([k,test])=>!Object.hasOwn(v,k)||test(v[k]))
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(a:unknown,b:unknown)=>guid(a)&&guid(b)&&a.toLowerCase()===b.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.length>0&&v===v.trim()
const int=(v:unknown,min=1,max=2147483647):v is number=>typeof v==='number'&&Number.isInteger(v)&&v>=min&&v<=max
export const inventoryCostWorkLong=(v:unknown,min=1):v is string=>typeof v==='string'&&v.length<=19&&/^(0|[1-9][0-9]*)$/.test(v)&&v===v.trim()&&BigInt(v)>=BigInt(min)&&BigInt(v)<=9223372036854775807n
export const inventoryCostWorkIncrementable=(v:unknown)=>inventoryCostWorkLong(v,2)&&v!=='9223372036854775807'
const long:Check=inventoryCostWorkLong,zeroLong:Check=v=>inventoryCostWorkLong(v,0),scale:Check=v=>int(v,0,28)
const money=(v:unknown):v is string=>typeof v==='string'&&v===v.trim()&&/^-?(0|[1-9][0-9]*)(\.[0-9]{0,27}[1-9])?$/.test(v)&&v!=='-0'
const ratio=(v:unknown):v is CostProofRatio=>costRatio(v)&&v.numerator===v.numerator.trim()&&v.denominator===v.denominator.trim()
const positiveRatio:Check=v=>ratio(v)&&!v.numerator.startsWith('-')
const quantity:Check=v=>typeof v==='number'&&Number.isFinite(v)&&v>0
const utc=(v:unknown):v is string=>transferDispatchUtc(v)&&v===v.trim()&&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
const list=(check:Check,min=0,max?:number):Check=>v=>Array.isArray(v)&&v.length>=min&&(max===undefined||v.length<=max)&&v.every(check)
const unique=(rows:readonly unknown[],key:string)=>rows.every(rec)&&new Set(rows.map(r=>rec(r)?String(r[key]).toLowerCase():'')).size===rows.length
const rounding:Check=v=>roundingProof(v)&&ratio(v.exactSignedAmount)&&ratio(v.exactSignedResidue)&&money(v.roundedSignedAmount)
  &&text(v.exactSignedAmountText)&&text(v.exactSignedResidueText)
const physical:Check=v=>physicalClaim(v)&&rec(v)&&int(v.movementLineNumber)&&quantity(v.quantityKg)&&['shipmentId','movementId','productId'].every(k=>guid(v[k]))
const recorded:Check=v=>shape(v,{currencyId:guid,moneyProfileId:guid,moneyProfileVersion:long,moneyScale:scale,rounding})
const cancelledComponent:Check=v=>shape(v,{origin:x=>x==='Known'||x==='Manufacturing'||x==='Purchase',journalSourceId:guid,postingPurpose:text,currencyId:guid,journalTotal:money,signedAmount:money,cogsAccountId:guid,counterAccountId:guid},{sourceJournalId:guid,reversingJournalId:guid})
  &&!same(v.cogsAccountId,v.counterAccountId)&&typeof v.journalTotal==='string'&&!v.journalTotal.startsWith('-')
  &&(v.signedAmount==='0'?!Object.hasOwn(v,'reversingJournalId'):guid(v.sourceJournalId)&&guid(v.reversingJournalId)&&!same(v.sourceJournalId,v.reversingJournalId))

export function validInventoryCostPreview(value:Record<string,unknown>,company:string,id:string,kind:InventoryCostWorkKind,date:(v:unknown)=>v is string):value is Record<string,unknown>&{readonly inventoryCost:WorkInventoryCostPreview} {
  const validDate:Check=v=>date(v)&&v!=='0001-01-01'
  const returns=(v:unknown,issue:unknown)=>shape(v,{returnId:guid,movementId:guid,movementLineNumber:int,sourceIssueMovementId:guid,quantityKg:quantity,recordedAtUtc:utc})&&same(v.sourceIssueMovementId,issue)
  const shipment=(v:unknown,manufacturing:boolean,product:unknown):v is Record<string,unknown>=>shape(v,{shipmentId:guid,movementId:guid,movementLineNumber:int,loadingInstructionLineId:guid,productId:guid,labelId:guid,physicalQuantityKg:quantity,rootShare:positiveRatio,netRootQuantityKg:positiveRatio,recordedAtUtc:utc,...(manufacturing?{hasReturns:(x:unknown)=>typeof x==='boolean'}:{returns:list(()=>true,0,65536)})},
    {provenOriginalSequence:long,...(manufacturing?{returns:list(()=>true,0,65536)}:{})})&&same(v.productId,product)
    &&(!manufacturing||v.hasReturns===true?Array.isArray(v.returns)&&(!manufacturing||v.returns.length>0):v.returns===undefined||Array.isArray(v.returns)&&v.returns.length===0)
    &&(!Array.isArray(v.returns)||v.returns.every(r=>returns(r,v.movementId))&&unique(v.returns,'returnId')
      &&new Set([`${v.movementId}:${v.movementLineNumber}`,...v.returns.map(r=>`${r.movementId}:${r.movementLineNumber}`)].map(s=>s.toLowerCase())).size===v.returns.length+1)
  const manufacturing=(v:unknown,product:unknown)=>shape(v,{companyId:guid,sourceEventId:guid,outputId:guid,generationId:guid,generationVersion:long,receiptMovementId:guid,journalId:guid,currencyId:guid,cogsAccountId:guid,counterAccountId:guid,journalTotal:money,signedAmount:money,exactSignedAmount:ratio,signedRoundingResidue:money,moneyProfileId:guid,moneyProfileVersion:long,moneyScale:scale,profileAccountingDate:validDate,shipment:x=>shipment(x,true,product)})
    &&same(v.companyId,company)&&!same(v.cogsAccountId,v.counterAccountId)&&typeof v.journalTotal==='string'&&!v.journalTotal.startsWith('-')&&v.journalTotal!=='0'
  const purchase=(v:unknown,product:unknown)=>{
    if(!shape(v,{approval:()=>true,shipmentId:guid,movementId:guid,movementLineNumber:int,productId:guid,physicalQuantityKg:quantity,signedAmount:money,exactSignedAmount:ratio,exactSignedResidue:ratio,candidates:list(()=>true,1,10000)})||!same(v.productId,product))return false
    const a=v.approval
    if(!shape(a,{companyId:guid,sourceEventId:guid,invoiceLineId:guid,proposalId:guid,proposalVersion:x=>inventoryCostWorkLong(x,3),status:x=>x==='Posted'||x==='RoundedNoCost',signedAmount:money,currencyId:guid,moneyProfileId:guid,moneyProfileVersion:long,moneyScale:scale,cogsAccountId:guid,counterAccountId:guid,accountingDate:validDate,preparedBy:guid,approvedBy:guid,approvedAtUtc:utc,recordedRounding:recorded},{journalId:guid})
      ||!same(a.companyId,company)||same(a.preparedBy,a.approvedBy)||same(a.cogsAccountId,a.counterAccountId)
      ||typeof a.signedAmount!=='string'||a.signedAmount.startsWith('-')||typeof v.signedAmount!=='string'||v.signedAmount.startsWith('-')
      ||(a.status==='Posted'?!guid(a.journalId)||a.signedAmount==='0':a.journalId!==undefined||a.signedAmount!=='0'||v.signedAmount!=='0')
      ||!rec(a.recordedRounding)||!rec(a.recordedRounding.rounding)||!same(a.recordedRounding.currencyId,a.currencyId)||a.recordedRounding.rounding.roundedSignedAmount!==a.signedAmount)return false
    if(!Array.isArray(v.candidates)||!unique(v.candidates,'acceptanceId'))return false
    return v.candidates.every(c=>shape(c,{companyId:guid,sourceEventId:guid,invoiceLineId:guid,acceptanceId:guid,receiptMovementId:guid,rootLabelId:guid,productId:guid,invoicedRootShare:positiveRatio,invoicedNetQuantityKg:positiveRatio,exactTlAmount:positiveRatio,shipment:x=>shipment(x,false,product)})
      &&same(c.companyId,company)&&same(c.sourceEventId,a.sourceEventId)&&same(c.invoiceLineId,a.invoiceLineId)&&same(c.productId,product)&&rec(c.shipment)
      &&same(c.shipment.shipmentId,v.shipmentId)&&same(c.shipment.movementId,v.movementId)&&c.shipment.movementLineNumber===v.movementLineNumber&&c.shipment.physicalQuantityKg===v.physicalQuantityKg)
  }
  const classification:Check=v=>shape(v,{classification:x=>x===1||x===2,accountId:guid,origin:x=>x==='Product'||x==='InvoiceLine',invoiceLineRevision:zeroLong},{productVersion:text,classificationVersion:text,mappingVersion:long})
    &&(v.origin==='Product'?text(v.productVersion)&&text(v.classificationVersion)&&inventoryCostWorkLong(v.mappingVersion)&&v.invoiceLineRevision==='0':inventoryCostWorkLong(v.invoiceLineRevision)&&v.productVersion===undefined&&v.classificationVersion===undefined&&v.mappingVersion===undefined)
  const preparation:Check=v=>shape(v,{periodId:guid,accountingDate:validDate,transactionDate:validDate,currencyId:guid,signedAmount:money,moneyProfileId:guid,moneyProfileVersion:long,moneyScale:scale,classification,counterAccount:v=>account(v)&&guid(v.accountId),accountPlanPackageId:guid,accountPlanPackageVersion:long,accountPlanSourceVersion:text,preparedBy:guid,preparedAtUtc:utc,reason:v=>text(v)&&v.length<=1000},{accountPlanCatalogVersion:long,rounding})
    &&rec(v.classification)&&rec(v.counterAccount)&&!same(v.classification.accountId,v.counterAccount.accountId)
  const stale:Check=v=>shape(v,{eventId:guid,preparedVersion:x=>inventoryCostWorkLong(x,2),preparedBy:guid,closedAtUtc:utc,reasonCode:x=>x==='inventory_cost_preparation_stale',reasonText:x=>x==='Bayatladı',correlationId:x=>text(x)&&x.length<=128},{causationId:x=>typeof x==='string'&&x.length<=128})
  const renewal:Check=v=>shape(v,{previousProposalId:guid,previousClosedVersion:x=>inventoryCostWorkLong(x,3),closureEventId:guid})
  const cancellation:Check=v=>shape(v,{id:guid,companyId:guid,inventoryCostProposalId:guid,cancellationId:guid,version:long,status:x=>x==='Prepared'||x==='Approved',invoiceLineRevision:zeroLong,periodId:guid,accountingDate:validDate,transactionDate:validDate,reason:text,preparedBy:guid,preparedAtUtc:utc,components:list(cancelledComponent,1),moneyProfiles:list(x=>shape(x,{currencyId:guid,profileId:guid,version:long,scale}),1)},{approvedBy:guid,approvedAtUtc:utc})
    &&same(v.companyId,company)&&same(v.inventoryCostProposalId,id)&&(v.status==='Prepared'?v.approvedBy===undefined&&v.approvedAtUtc===undefined:guid(v.approvedBy)&&utc(v.approvedAtUtc)&&!same(v.approvedBy,v.preparedBy))
  const human=value.inventoryCost
  if(!shape(human,{proposal:()=>true,preparedRecordedAtUtc:utc}))return false
  const p=human.proposal
  if(!shape(p,{proposalId:guid,companyId:guid,eventId:guid,kind:x=>x===(kind==='accounting.invoiced-shipment-cost.v1'?1:2),invoiceId:guid,invoiceVersion:long,admissionId:guid,invoiceDate:validDate,component:rec,version:x=>inventoryCostWorkLong(x,2),status:x=>x==='Prepared'||x==='Posted'||x==='RoundedNoCost'||x==='Stale',sourceOccurredAtUtc:utc,preparation,exactTlAmount:text},
    {officialInvoiceNumber:text,approvedBy:guid,approvedAtUtc:utc,journalId:guid,cancellationId:guid,reversalProposal:v=>decodeJournalReversalProposal(v,company,date)!==null&&rec(v)&&int(v.version)&&utc(v.proposedAtUtc)&&(v.decidedAtUtc===null||utc(v.decidedAtUtc)),currentPostedJournalId:guid,componentCancellation:cancellation,staleClosure:stale,renewedFrom:renewal,unpostedTlAmountText:text})
    ||!same(p.companyId,company)||!same(p.proposalId,id)||p.version!==value.sourceVersion||value.approvalReasonRequired!==false||value.rejectionSupported!==false)return false
  const c=p.component,prep=p.preparation
  if(!rec(c)||!rec(prep)||!shape(c,{componentKey:text,productId:guid,signedTlAmount:ratio},{invoiceLineId:guid,invoiceLineNumber:int,locationId:guid,salesPhysicalClaims:list(physical),alreadyPostedComponents:list(x=>manufacturing(x,c.productId),1),recordedRounding:recorded,unpostedTlAmount:positiveRatio,alreadyPostedPurchaseComponents:list(x=>purchase(x,c.productId),1),claimSuccession:rec}))return false
  if(!rec(c.signedTlAmount)||!ratio(c.signedTlAmount))return false
  const firstLine=typeof c.componentKey==='string'&&c.componentKey===`purchase-first-line/${String(c.componentKey).split('/')[1]}`&&guid(c.componentKey.split('/')[1])
  const first=firstLine||typeof c.componentKey==='string'&&/^purchase-first\//.test(c.componentKey)&&c.componentKey.split('/').length===3&&c.componentKey.split('/').slice(1).every(guid)
  if(first&&typeof c.componentKey==='string'&&c.componentKey!==c.componentKey.toLowerCase())return false
  if(p.kind===1?!guid(c.invoiceLineId)||!int(c.invoiceLineNumber)||c.locationId!==undefined||c.componentKey!==`invoice-line:${c.invoiceLineId.toLowerCase()}`||c.signedTlAmount.numerator.startsWith('-')
    :c.invoiceLineId!==undefined||c.invoiceLineNumber!==undefined||(firstLine?c.locationId!==undefined:!guid(c.locationId))||!first&&c.componentKey!==`product-location:${String(c.productId).toLowerCase()}:${String(c.locationId).toLowerCase()}`)return false
  if(!money(p.exactTlAmount)&&p.exactTlAmount!==`${c.signedTlAmount.numerator} / ${c.signedTlAmount.denominator}`)return false
  const mixed=c.unpostedTlAmount!==undefined,posted=c.alreadyPostedComponents!==undefined||c.alreadyPostedPurchaseComponents!==undefined
  if(p.kind!==1&&posted||mixed&&!posted||!mixed&&posted||mixed&&(!rec(c.unpostedTlAmount)||!money(p.unpostedTlAmountText)&&p.unpostedTlAmountText!==`${c.unpostedTlAmount.numerator} / ${c.unpostedTlAmount.denominator}`)||!mixed&&p.unpostedTlAmountText!==undefined)return false
  const posting=mixed?c.unpostedTlAmount:c.signedTlAmount,postingText=mixed?p.unpostedTlAmountText:p.exactTlAmount
  if(!ratio(posting))return false
  if(first){if(p.kind!==2||!rec(c.recordedRounding)||!rec(c.recordedRounding.rounding)||c.recordedRounding.rounding.roundedSignedAmount!==p.exactTlAmount||!ratio(c.recordedRounding.rounding.exactSignedAmount)||c.recordedRounding.rounding.exactSignedAmount.numerator.startsWith('-'))return false}
  else if(c.recordedRounding!==undefined)return false
  if(prep.rounding!==undefined){if(!roundingProof(prep.rounding)||prep.rounding.roundedSignedAmount!==prep.signedAmount)return false
    if(rec(c.recordedRounding)){if(!roundingProof(c.recordedRounding.rounding)||!equalRounding(prep.rounding,c.recordedRounding.rounding)||!same(c.recordedRounding.currencyId,prep.currencyId))return false}
    else if(prep.rounding.exactSignedAmount.numerator!==posting.numerator||prep.rounding.exactSignedAmount.denominator!==posting.denominator||prep.rounding.exactSignedAmountText!==postingText)return false
  }else if(first||mixed||p.exactTlAmount!==prep.signedAmount)return false
  if(c.claimSuccession!==undefined){const s=c.claimSuccession
    if(p.kind!==1||posted||mixed||!shape(s,{previousEventId:guid,previousInvoiceId:guid,previousInvoiceLineId:guid,previousAdmissionId:guid,previousFinalizedVersion:long,previousProposalId:guid,previousProposalVersion:long,cancellationId:guid,invoiceLineRevision:zeroLong,claims:list(x=>shape(x,{physical,previousGeneration:long}),1,10000),reversedComponents:list(cancelledComponent),originalSourceAmount:positiveRatio,knownBaseAmount:positiveRatio,restoredAmount:positiveRatio})
      ||same(s.previousInvoiceId,p.invoiceId)||same(s.previousEventId,p.eventId)||same(s.previousAdmissionId,p.admissionId)||!rec(s.restoredAmount)||s.restoredAmount.numerator!==c.signedTlAmount.numerator||s.restoredAmount.denominator!==c.signedTlAmount.denominator
      ||!Array.isArray(s.claims)||!Array.isArray(c.salesPhysicalClaims)||s.claims.length!==c.salesPhysicalClaims.length
      ||new Set(s.claims.map(g=>rec(g)&&rec(g.physical)?`${g.physical.movementId}:${g.physical.movementLineNumber}`.toLowerCase():'')).size!==s.claims.length
      ||!s.claims.every(g=>rec(g)&&rec(g.physical)&&same(g.physical.productId,c.productId)&&Array.isArray(c.salesPhysicalClaims)&&c.salesPhysicalClaims.some(a=>rec(a)&&rec(g.physical)&&same(a.shipmentId,g.physical.shipmentId)&&same(a.movementId,g.physical.movementId)&&a.movementLineNumber===g.physical.movementLineNumber&&a.quantityKg===g.physical.quantityKg)))return false
  }
  if(p.status!=='Prepared'&&!inventoryCostWorkLong(p.version,3))return false
  if(p.status==='Prepared'||p.status==='Stale'){if(p.approvedBy!==undefined||p.approvedAtUtc!==undefined||p.journalId!==undefined)return false}
  else if(!guid(p.approvedBy)||same(p.approvedBy,prep.preparedBy)||!utc(p.approvedAtUtc)||(p.status==='Posted'?!guid(p.journalId)||prep.signedAmount==='0':p.journalId!==undefined||prep.signedAmount!=='0'||!rec(prep.rounding)))return false
  if(p.status==='Stale'){if(!rec(p.staleClosure)||!inventoryCostWorkLong(p.staleClosure.preparedVersion,2)||BigInt(p.staleClosure.preparedVersion)+1n!==BigInt(String(p.version))||!same(p.staleClosure.preparedBy,prep.preparedBy)||p.cancellationId!==undefined||p.reversalProposal!==undefined)return false}
  else if(p.staleClosure!==undefined)return false
  if(rec(p.renewedFrom)&&(!inventoryCostWorkLong(p.renewedFrom.previousClosedVersion,3)||same(p.renewedFrom.previousProposalId,p.proposalId)||BigInt(p.renewedFrom.previousClosedVersion)>=BigInt(String(p.version))))return false
  if(p.status!=='Prepared'&&value.pending!==false||p.currentPostedJournalId!==undefined&&!guid(p.journalId))return false
  if(rec(p.componentCancellation)&&(!same(p.componentCancellation.cancellationId,p.cancellationId)))return false
  if(rec(p.reversalProposal)&&!same(p.reversalProposal.sourceJournalId,p.currentPostedJournalId??p.journalId))return false
  const label=(raw:unknown,fallback:unknown)=>typeof raw==='string'&&raw.trim().length>0&&raw.length<=200?raw:String(fallback).toLowerCase().slice(0,8)
  if(value.documentLabel!==label(p.officialInvoiceNumber,id)||!Array.isArray(value.rules)||value.rules.length!==1||!rec(value.rules[0]))return false
  const rule=value.rules[0],amount=String(prep.signedAmount),formatted=amount.includes('.')?amount.replace(/0+$/,'').replace(/\.$/,''):amount
  if(rule.ruleCode!==kind.replace('.v1','-approval-required')||rule.threshold!==null||rule.actual!==formatted||rule.deviation!==null||rule.unit!=='TRY'||!Array.isArray(value.references)||value.references.length!==2)return false
  const refs=value.references
  return [[p.invoiceId,'invoice',label(p.officialInvoiceNumber,p.invoiceId)],[prep.periodId,'period',String(prep.periodId).toLowerCase().slice(0,8)]].every(([rid,role,name],i)=>rec(refs[i])&&refs[i].role===role&&same(refs[i].recordId,rid)&&refs[i].label===name&&refs[i].link===null)
}
