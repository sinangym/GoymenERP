import type { WorkSourceVoucherPreview } from './workSourceVoucherDecision'
import { SOURCE_VOUCHER_WORK_KIND } from './workSourceVoucherDecision'
export const sourceVoucherId='ac570000-0000-4000-8000-000000000001'
export const sourceVoucherMaker='ac570000-0000-4000-8000-000000000002'
export const sourceVoucherActor='ac570000-0000-4000-8000-000000000003'
const source='ac570000-0000-4000-8000-000000000004',invoice='ac570000-0000-4000-8000-000000000005',party='ac570000-0000-4000-8000-000000000006',currency='ac570000-0000-4000-8000-000000000007'
type MutableWrapper={-readonly[K in keyof WorkSourceVoucherPreview]:WorkSourceVoucherPreview[K]}
export function sourceVoucherHuman(company:string,periodOpen=true):MutableWrapper {
 return {voucher:{id:sourceVoucherId,companyId:company,documentKind:'sales-invoice',sourceDocumentId:source,invoiceId:invoice,finalizedVersion:'9007199254740993',
  sourceActorId:sourceVoucherMaker,partyId:party,documentCurrencyId:currency,exchangeRate:'1.0000',documentDate:'0001-01-02',dueDate:'9999-12-31',status:periodOpen?'AwaitingApproval':'PeriodWaiting',
  version:'1',mappingVersion:'9223372036854775807',periodId:null,journalId:null,approvedBy:null,approvedAtUtc:null,cancellationReason:null,officialInvoiceNo:'SENTETIK-57',functionalPayable:'120',
  lines:Array.from({length:137},(_,index)=>({lineNumber:index+1,mappingKey:'SENTETIK mapping '+index,side:index%2===0?1 as const:2 as const,accountId:'ac570000-0000-4000-8000-000000000008',
   functionalAmount:'120',sourceLineNumber:index+1,vatRateCode:'20',accountCode:'120',accountName:'SENTETIK full account '+index}))},createdAtUtc:'2026-11-28T09:00:00.0000001Z',sourceCancelled:false,periodOpen}
}
export function sourceVoucherFixture(company:string,periodOpen=true){
 const human=sourceVoucherHuman(company,periodOpen),v=human.voucher
 return {companyId:company,kind:SOURCE_VOUCHER_WORK_KIND,itemId:sourceVoucherId,documentLabel:v.officialInvoiceNo,sourceVersion:'1',pending:true,approvalReasonRequired:false,rejectionSupported:false,
  rules:[{ruleCode:'accounting.source-voucher-approval-required',threshold:null,actual:'120',deviation:null,unit:null},
   ...(periodOpen?[]:[{ruleCode:'accounting.source-voucher-period-waiting',threshold:null,actual:null,deviation:null,unit:null}])],
  references:[{role:'invoice',label:v.officialInvoiceNo,recordId:invoice,link:null},{role:'source-document',label:source.slice(0,8),recordId:source,link:null},
   {role:'party',label:party.slice(0,8),recordId:party,link:null},{role:'currency',label:currency.slice(0,8),recordId:currency,link:null}],sourceVoucher:human}
}

export function sourceVoucherReversalHuman(company:string,approved=true):MutableWrapper {
 const human=sourceVoucherHuman(company,false),journal='ac570000-0000-4000-8000-000000000009'
 human.sourceCancelled=true;human.voucher={...human.voucher,status:approved?'CancelledByReversal':'CancellationReversalPending',version:'3',periodId:party,journalId:sourceVoucherId,
  approvedBy:sourceVoucherActor,approvedAtUtc:'2026-11-28T08:00:00Z',cancellationId:source,cancellationReason:'SENTETIK full cancellation reason '+ 'g'.repeat(2048),
  reversalProposal:{proposalId:invoice,companyId:company,sourceJournalId:sourceVoucherId,reversalJournalId:approved?journal:null,periodId:party,transactionDate:'0001-01-02',accountingDate:'9999-12-31',valueDate:null,
   reason:'SENTETIK retained reversal reason',proposedBy:sourceVoucherMaker,proposedAtUtc:'2026-11-28T11:00:00Z',status:approved?'Approved':'Pending',decidedBy:approved?sourceVoucherActor:null,
   decidedAtUtc:approved?'2026-11-28T10:00:00Z':null,decisionReason:null,version:2147483647}}
 return human
}
