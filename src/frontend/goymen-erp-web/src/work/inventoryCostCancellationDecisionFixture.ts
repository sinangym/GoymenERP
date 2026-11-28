import { inventoryCostFixture, inventoryCostId, inventoryCostGuid as g } from './inventoryCostDecisionFixture'
import type { WorkInventoryCostRetainedProposal, WorkInventoryCostCancellation } from './workInventoryCostDecision'
import { INVENTORY_COST_CANCELLATION_WORK_KIND, type WorkInventoryCostCancellationPreview } from './workInventoryCostCancellationDecision'

export type CancellationOriginalStatus='AwaitingPreparation'|'NoCost'|'AlreadyAccounted'|'Prepared'|'Posted'|'RoundedNoCost'|'Stale'
export const cancellationWorkId=inventoryCostId
const at='2026-11-28T10:00:00.0000001Z',date='2026-11-28'
export function inventoryCostCancellationFixture(company:string,status:'Prepared'|'Approved'='Prepared',originalStatus:CancellationOriginalStatus='AwaitingPreparation',m='7',n='1') {
  const unprepared=originalStatus==='AwaitingPreparation'||originalStatus==='NoCost'||originalStatus==='AlreadyAccounted'
  const full=inventoryCostFixture(company,undefined,unprepared?'Prepared':originalStatus,n,originalStatus!=='NoCost').inventoryCost.proposal
  const {preparation:sourcePreparation,...source}=full
  const originalProposal:WorkInventoryCostRetainedProposal={...source,status:originalStatus,...(unprepared?{}:{preparation:sourcePreparation})}
  if(originalStatus==='NoCost')Object.assign(originalProposal,{exactTlAmount:'0',component:{...originalProposal.component,signedTlAmount:{numerator:'0',denominator:'1'}}})
  if(originalStatus==='AlreadyAccounted')Object.assign(originalProposal,{unpostedTlAmountText:'0',component:{...originalProposal.component,unpostedTlAmount:{numerator:'0',denominator:'1'}}})
  const preparedCancellation:WorkInventoryCostCancellation={id:g(56),companyId:company,inventoryCostProposalId:inventoryCostId,cancellationId:g(57),version:m,status:'Prepared',invoiceLineRevision:'9007199254740993',periodId:g(58),accountingDate:date,transactionDate:date,
    reason:'SENTETIK kayıtlı maliyet paylarının iptal gerekçesi',preparedBy:g(59),preparedAtUtc:at,
    components:[{origin:'Known',sourceJournalId:g(60),journalSourceId:g(61),postingPurpose:'SENTETIK kayıtlı maliyet',currencyId:g(12),journalTotal:'100.01',signedAmount:'100.01',cogsAccountId:g(14),counterAccountId:g(15)},
      {origin:'Manufacturing',sourceJournalId:g(62),journalSourceId:g(63),postingPurpose:'SENTETIK üretim payı',currencyId:g(12),journalTotal:'200',signedAmount:'-0.01',cogsAccountId:g(14),counterAccountId:g(15)},
      {origin:'Purchase',journalSourceId:g(64),postingPurpose:'SENTETIK sıfır alış payı',currencyId:g(12),journalTotal:'0',signedAmount:'0',cogsAccountId:g(14),counterAccountId:g(15)}],
    moneyProfiles:[{currencyId:g(12),profileId:g(13),version:'9223372036854775807',scale:2}]}
  const cancellation:WorkInventoryCostCancellation=status==='Prepared'?structuredClone(preparedCancellation):{...structuredClone(preparedCancellation),status:'Approved',version:(BigInt(m)+1n).toString(),approvedBy:g(65),approvedAtUtc:at,
    components:preparedCancellation.components.map((c,i)=>c.signedAmount==='0'?{...c}:{...c,reversingJournalId:g(70+i)})}
  const human:WorkInventoryCostCancellationPreview={originalProposal,preparedCancellation,cancellation,preparedRecordedAtUtc:at,
    sourceCancellation:{companyId:company,cancellationId:g(57),documentKind:'sales-invoice',sourceDocumentId:originalProposal.admissionId,invoiceId:originalProposal.invoiceId,finalizedVersion:originalProposal.invoiceVersion,
      cancelledBy:g(66),cancelledAtUtc:at,reason:'SENTETIK yasal fatura iptal dayanağı\n'+ 'Açıklamanın tamamı korunur. '.repeat(100),recordedAtUtc:at}}
  return {companyId:company,kind:INVENTORY_COST_CANCELLATION_WORK_KIND,itemId:inventoryCostId,documentLabel:inventoryCostId.slice(0,8),sourceVersion:m,pending:status==='Prepared',approvalReasonRequired:false,rejectionSupported:false,
    references:[{role:'inventory-cost-proposal',recordId:inventoryCostId,label:inventoryCostId.slice(0,8),link:null},{role:'invoice-cancellation',recordId:g(57),label:g(57).slice(0,8),link:null},{role:'period',recordId:g(58),label:g(58).slice(0,8),link:null}],
    rules:[{ruleCode:'accounting.inventory-cost-cancellation-approval-required',threshold:null,actual:null,deviation:null,unit:null}],inventoryCostCancellation:human}
}
