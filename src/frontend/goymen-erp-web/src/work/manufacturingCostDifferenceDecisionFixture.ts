import { MANUFACTURING_COST_WORK_KIND as kind } from './workManufacturingCostDifferenceDecision'
export const manufacturingCostId='ac530000-0000-4000-8000-000000000002'
export function manufacturingCostFixture(company:string,version='9007199254740993',status:'Prepared'|'Posted'='Prepared',rows=13){
 const id=manufacturingCostId,maker='ac530000-0000-4000-8000-000000000004',debit='ac530000-0000-4000-8000-000000000006',credit='ac530000-0000-4000-8000-000000000007'
 const order='ac530000-0000-4000-8000-000000000008',period='ac530000-0000-4000-8000-000000000009'
 return {companyId:company,kind,itemId:id,documentLabel:id.slice(0,8),sourceVersion:version,pending:status==='Prepared',approvalReasonRequired:false,rejectionSupported:false,
  rules:[{ruleCode:'accounting.manufacturing-cost-difference-approval-required',threshold:null,actual:'600',deviation:null,unit:'TRY'}],
  references:[['manufacturing-order',order],['period',period],['debit-ledger-account',debit],['credit-ledger-account',credit]].map(([role,recordId])=>({role:role!,recordId:recordId!,label:recordId!.slice(0,8),link:null})),
  manufacturingCostDifference:{preparedAtUtc:'2026-11-28T09:00:00.1234567Z',sourceFactsDigest:'A'.repeat(64),previewDigest:'B'.repeat(64),
   proposal:{proposalId:id,companyId:company,manufacturingOrderId:order,generationId:'ac530000-0000-4000-8000-000000000010',generationVersion:'9223372036854775807',status,version,
    signedTlDifference:'600',outputs:Array.from({length:rows},(_,i)=>({outputId:`ac530000-0000-4000-8000-${(i+100).toString().padStart(12,'0')}`,
     receiptMovementId:'ac530000-0000-4000-8000-000000000011',labelId:'ac530000-0000-4000-8000-000000000012',netQuantityKg:'100',remainingQuantityKg:'2 / 3',consumedQuantityKg:'1 / 3',
     tlUnitCostPerKg:'20',remainingTlValue:'1400',consumedTlDifference:i===0?'600':'0',usdUnitCostPerKg:i===0?null:'4',originalTlAmount:'2000'})),
    sourceOccurredAtUtc:'2026-11-27T09:00:00Z',periodId:period,accountingDate:'2026-11-28',transactionDate:'2026-11-27',debitAccountId:debit,creditAccountId:credit,preparedBy:maker,
    approvedBy:status==='Posted'?'ac530000-0000-4000-8000-000000000003':null,journalId:status==='Posted'?'ac530000-0000-4000-8000-000000000013':null,reason:'SENTETIK source reason',
    accountPlan:{packageId:'ac530000-0000-4000-8000-000000000014',packageVersion:'9007199254740993',catalogVersion:'9223372036854775807' as string|null,
     sourceVersion:'SENTETIK retained plan',sha256:'c'.repeat(64),debit:{accountId:debit,code:'620.01',name:'SENTETIK debit'},credit:{accountId:credit,code:'151.01',name:'SENTETIK credit'}}}}
 }
}
