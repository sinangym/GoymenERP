import { JOURNAL_REVERSAL_WORK_KIND } from './workJournalReversalDecision'

/** Exact native sixteen-field numeric Int32 view; the two digests are only Work human GET metadata. */
export function journalReversalFixture(companyId: string,proposalId: string,version = 1) {
 const sourceJournalId='aaaaaaaa-0000-4000-8000-000000000061',periodId='aaaaaaaa-0000-4000-8000-000000000062'
 return {companyId,kind:JOURNAL_REVERSAL_WORK_KIND,itemId:proposalId,documentLabel:proposalId.slice(0,8).toLowerCase(),sourceVersion:String(version),pending:true,
  approvalReasonRequired:false,rejectionSupported:false,
  rules:[{ruleCode:'accounting.journal-reversal-approval-required',threshold:null,actual:null,deviation:null,unit:null}],
  references:[{role:'journal',recordId:sourceJournalId,label:sourceJournalId.slice(0,8),link:`/muhasebe/fisler/${sourceJournalId}`},
   {role:'period',recordId:periodId,label:periodId.slice(0,8),link:null}],
  accountingJournalReversal:{sourceFactsDigest:'A'.repeat(64),previewDigest:'B'.repeat(64),proposal:{proposalId,companyId,sourceJournalId,
   reversalJournalId:null as string|null,periodId,transactionDate:'2026-11-25',accountingDate:'2026-11-26',valueDate:null as string|null,
   reason:'SENTETIK tam ters kayıt gerekçesi',proposedBy:'aaaaaaaa-0000-4000-8000-000000000063',proposedAtUtc:'2026-11-27T09:00:00+00:00',
   status:'Pending',decidedBy:null as string|null,decidedAtUtc:null as string|null,decisionReason:null as string|null,version}},
 }
}
