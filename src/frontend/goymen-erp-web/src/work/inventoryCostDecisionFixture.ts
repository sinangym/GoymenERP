import type { WorkDecisionPreview } from './workDecisionApi'
import { invoiceLineCogsFixture } from './invoiceLineCogsDecisionFixture'
import type { InventoryCostWorkKind, WorkInventoryCostPreview, WorkInventoryCostProposal } from './workInventoryCostDecision'
export const inventoryCostId='ac550000-0000-4000-8000-000000000002'
export const inventoryCostGuid=(n:number)=>`ac550000-0000-4000-8000-${String(n).padStart(12,'0')}`
const at='2026-11-28T10:00:00.0000001Z',date='2026-11-28',ratio={numerator:'10001',denominator:'100'}
// Native codec fixture facts; only the Work contract's explicit omissions/version representation differ.
export function inventoryCostFixture(company:string,kind:InventoryCostWorkKind='accounting.invoiced-shipment-cost.v1',status:'Prepared'|'Posted'|'RoundedNoCost'|'Stale'='Prepared',version='7',mixed=true) {
  const native=invoiceLineCogsFixture(company).invoiceLineCogs.proposal
  const {fingerprint:_fingerprint,sourcePayloadSha256:_source,lineageSha256:_lineage,historySha256:_history,shipment:ms,...m}=native.attributedEvidence[0]!.source
  const {physicalProofJson:_physical,...manufacturingShipment}=ms
  const {fingerprint:_purchaseFingerprint,approval:pa,candidates:pc,...q}=native.purchaseAttributedEvidence[0]!.source
  const {sourceProofSha256:_purchaseSource,fingerprint:_approvalFingerprint,...approval}=pa
  const candidates=pc.map(c=>{const {sourceProofSha256:_proof,physicalSourceSha256:_physicalSource,fingerprint:_candidateFingerprint,shipment,...rest}=c
    const {physicalProofJson:_json,...physical}=shipment;return {...rest,shipment:physical}})
  const purchase=kind==='accounting.purchase-cost-difference.v1',includeShares=!purchase&&mixed
  const rounded=status==='RoundedNoCost',signed=rounded?'0':'100.01'
  const rounding={exactSignedAmount:rounded?{numerator:'1',denominator:'1000'}:ratio,roundedSignedAmount:signed,exactSignedResidue:rounded?{numerator:'1',denominator:'1000'}:{numerator:'0',denominator:'1'},
    ruleVersion:'accounting.inventory-cost-component-total.v1',exactSignedAmountText:rounded?'0.001':'100.01',exactSignedResidueText:rounded?'0.001':'0'}
  const proposal:WorkInventoryCostProposal={proposalId:inventoryCostId,companyId:company,eventId:inventoryCostGuid(3),kind:purchase?2:1,invoiceId:inventoryCostGuid(5),invoiceVersion:'9007199254740993',admissionId:inventoryCostGuid(6),invoiceDate:date,
    officialInvoiceNumber:'SENTETIK INV-55',version,status,sourceOccurredAtUtc:at,exactTlAmount:rounded?'0.001':'100.01',
    component:{componentKey:purchase?`product-location:${inventoryCostGuid(9)}:${inventoryCostGuid(10)}`:`invoice-line:${inventoryCostGuid(8)}`,productId:includeShares?manufacturingShipment.productId:inventoryCostGuid(9),
      ...(purchase?{locationId:inventoryCostGuid(10)}:{invoiceLineId:inventoryCostGuid(8),invoiceLineNumber:1}),signedTlAmount:rounded?{numerator:'1',denominator:'1000'}:ratio,
      ...(includeShares?{alreadyPostedComponents:[{...m,shipment:manufacturingShipment}],alreadyPostedPurchaseComponents:[{...q,approval,candidates}],unpostedTlAmount:rounded?{numerator:'1',denominator:'1000'}:ratio}:{})},
    preparation:{periodId:inventoryCostGuid(11),accountingDate:date,transactionDate:date,currencyId:inventoryCostGuid(12),signedAmount:signed,moneyProfileId:inventoryCostGuid(13),moneyProfileVersion:'9223372036854775807',moneyScale:2,
      classification:{classification:1,accountId:inventoryCostGuid(14),origin:'Product',productVersion:'3',classificationVersion:'2',mappingVersion:'9007199254740993',invoiceLineRevision:'0'},
      counterAccount:{accountId:inventoryCostGuid(15),code:'153',name:'SENTETIK maliyet karşı hesabı'},accountPlanPackageId:inventoryCostGuid(16),accountPlanPackageVersion:'9007199254740993',accountPlanCatalogVersion:'9223372036854775807',
      accountPlanSourceVersion:'SENTETIK kayıtlı plan',preparedBy:inventoryCostGuid(17),preparedAtUtc:at,reason:'SENTETIK maliyet hazırlama gerekçesi',...(includeShares||rounded?{rounding}:{})},
    ...(includeShares?{unpostedTlAmountText:rounded?'0.001':'100.01'}:{}),
    ...(status==='Posted'||rounded?{approvedBy:inventoryCostGuid(18),approvedAtUtc:at,...(status==='Posted'?{journalId:inventoryCostGuid(19)}:{})}:{}),
    ...(status==='Stale'?{staleClosure:{eventId:inventoryCostGuid(20),preparedVersion:(BigInt(version)-1n).toString(),preparedBy:inventoryCostGuid(17),closedAtUtc:at,reasonCode:'inventory_cost_preparation_stale',reasonText:'Bayatladı',correlationId:'SENTETIK stale original'}}:{})}
  return {companyId:company,kind,itemId:inventoryCostId,documentLabel:'SENTETIK INV-55',sourceVersion:version,pending:status==='Prepared',approvalReasonRequired:false,rejectionSupported:false,
    references:[{role:'invoice',label:'SENTETIK INV-55',recordId:proposal.invoiceId,link:null},{role:'period',label:proposal.preparation.periodId.slice(0,8),recordId:proposal.preparation.periodId,link:null}],
    rules:[{ruleCode:kind.replace('.v1','-approval-required'),threshold:null,actual:signed,deviation:null,unit:'TRY'}],inventoryCost:{proposal,preparedRecordedAtUtc:at}} satisfies WorkDecisionPreview & {readonly inventoryCost:WorkInventoryCostPreview}
}

export function successionCostFixture(company:string) {
  const v=inventoryCostFixture(company,undefined,'Prepared','7',false),p=v.inventoryCost.proposal
  const physical={shipmentId:inventoryCostGuid(30),movementId:inventoryCostGuid(31),movementLineNumber:1,productId:p.component.productId,quantityKg:1}
  Object.assign(p.component,{salesPhysicalClaims:[physical],claimSuccession:{previousEventId:inventoryCostGuid(32),previousInvoiceId:inventoryCostGuid(33),previousInvoiceLineId:inventoryCostGuid(34),previousAdmissionId:inventoryCostGuid(35),
    previousFinalizedVersion:'9007199254740993',previousProposalId:inventoryCostGuid(36),previousProposalVersion:'9223372036854775807',cancellationId:inventoryCostGuid(37),invoiceLineRevision:'0',
    claims:[{physical:{...physical},previousGeneration:'9223372036854775807'}],reversedComponents:[{origin:'Known',sourceJournalId:inventoryCostGuid(38),journalSourceId:inventoryCostGuid(39),postingPurpose:'SENTETIK original COGS',currencyId:inventoryCostGuid(12),journalTotal:'100.01',signedAmount:'100.01',cogsAccountId:inventoryCostGuid(14),counterAccountId:inventoryCostGuid(15),reversingJournalId:inventoryCostGuid(40)}],
    originalSourceAmount:ratio,knownBaseAmount:ratio,restoredAmount:ratio}})
  Object.assign(p,{renewedFrom:{previousProposalId:inventoryCostGuid(41),previousClosedVersion:'6',closureEventId:inventoryCostGuid(42)}})
  return v
}

export function firstPurchaseCostFixture(company:string) {
  const v=inventoryCostFixture(company,'accounting.purchase-cost-difference.v1'),p=v.inventoryCost.proposal
  Object.assign(p.component,{componentKey:`purchase-first-line/${inventoryCostGuid(50)}`,recordedRounding:{currencyId:p.preparation.currencyId,moneyProfileId:p.preparation.moneyProfileId,
    moneyProfileVersion:'9007199254740993',moneyScale:2,rounding:{exactSignedAmount:ratio,roundedSignedAmount:'100.01',exactSignedResidue:{numerator:'0',denominator:'1'},
      ruleVersion:'accounting.inventory-cost-component-total.v1',exactSignedAmountText:'100.01',exactSignedResidueText:'0'}}})
  Reflect.deleteProperty(p.component,'locationId')
  Object.assign(p.preparation,{rounding:p.component.recordedRounding!.rounding})
  return v
}

export function cancellationCostFixture(company:string) {
  const v=successionCostFixture(company),p=v.inventoryCost.proposal
  Object.assign(p,{cancellationId:inventoryCostGuid(37),componentCancellation:{id:inventoryCostGuid(51),companyId:company,inventoryCostProposalId:p.proposalId,cancellationId:inventoryCostGuid(37),
    version:'9007199254740993',status:'Approved',invoiceLineRevision:'0',periodId:p.preparation.periodId,accountingDate:date,transactionDate:date,reason:'SENTETIK kayıtlı bileşen iptali',
    preparedBy:inventoryCostGuid(52),preparedAtUtc:at,approvedBy:inventoryCostGuid(53),approvedAtUtc:at,components:p.component.claimSuccession!.reversedComponents,
    moneyProfiles:[{currencyId:inventoryCostGuid(12),profileId:inventoryCostGuid(13),version:'9223372036854775807',scale:2}]}})
  return v
}
