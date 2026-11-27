import { NORMAL_JOURNAL_WORK_KIND } from './workNormalJournalDecision'

/** Work-only projection: the native twenty fields, with only Version serialized as lossless text. */
export function normalJournalFixture(companyId: string, draftId: string, version = '7') {
  const source = 'aaaaaaaa-0000-4000-8000-000000000071', currency = 'aaaaaaaa-0000-4000-8000-000000000072'
  return { companyId, kind:NORMAL_JOURNAL_WORK_KIND, itemId:draftId, documentLabel:'SENTETIK normal fiş',
    sourceVersion:version, pending:true, approvalReasonRequired:false, rejectionSupported:false,
    references:[{role:'source-record',label:'SENTETIK kaynak',recordId:source,link:null},{role:'currency',label:'SENTETIK döviz',recordId:currency,link:null}],
    rules:[{ruleCode:'accounting.normal-journal-draft-approval-required',threshold:null,actual:null,deviation:null,unit:null}],
    normalJournalDraft:{sourceFactsDigest:'A'.repeat(64),previewDigest:'B'.repeat(64),draft:{
      draftId,companyId,version,status:'Draft',preparedByUserId:'aaaaaaaa-0000-4000-8000-000000000073',
      preparedAtUtc:'2026-11-26T08:00:00+00:00',approvedByUserId:null,approvedAtUtc:null,periodId:null,postedJournalId:null,
      sourceId:source,postingPurpose:'SENTETIK amaç',postingRuleVersion:'SENTETIK kural-7',accountPlanSourceVersion:'SENTETIK plan-3',
      accountPlanSourceSha256:'C'.repeat(64),currencyId:currency,transactionDate:'2026-11-26',accountingDate:'2026-11-26',valueDate:null,
      lines:[{lineNumber:1,mappingKey:'SENTETIK borç',side:'Debit',accountId:'aaaaaaaa-0000-4000-8000-000000000074',amount:'1250.50'},
        {lineNumber:2,mappingKey:'SENTETIK alacak',side:'Credit',accountId:'aaaaaaaa-0000-4000-8000-000000000075',amount:'1250.50'}],
    }},
  }
}
