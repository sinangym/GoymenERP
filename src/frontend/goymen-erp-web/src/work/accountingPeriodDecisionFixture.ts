export const openingPeriodKind = 'accounting.period-opening.v1'
export const reopeningPeriodKind = 'accounting.period-reopening.v1'
export const periodFixtureId = 'aaaaaaaa-0000-4000-8000-000000000047'
export const closureFixtureId = 'aaaaaaaa-0000-4000-8000-000000000048'
const maker = 'aaaaaaaa-0000-4000-8000-000000000049', checker = 'aaaaaaaa-0000-4000-8000-000000000050'
const at = '2026-11-27T09:00:00+00:00'
export function accountingOpeningFixture(companyId: string,periodId: string,version = '1') {
  return {companyId,kind:openingPeriodKind,itemId:periodId,documentLabel:'2026-11-01 / 2026-11-30',sourceVersion:version,pending:true,
    approvalReasonRequired:false,rejectionSupported:false,
    references:[{role:'period',recordId:periodId,label:'2026-11-01 / 2026-11-30',link:'/muhasebe/donemler'}],
    rules:[{ruleCode:'accounting.period-opening-approval-required',threshold:null,actual:null,deviation:null,unit:null}],
    accountingPeriodOpening:{sourceFactsDigest:'A'.repeat(64),previewDigest:'B'.repeat(64),period:{periodId,companyId,startsOn:'2026-11-01',endsOn:'2026-11-30',
      status:'Proposed',version,preparedByUserId:maker,preparedAtUtc:at,approvedByUserId:null as string|null,approvedAtUtc:null as string|null,
      closedByUserId:null as string|null,closedAtUtc:null as string|null,isOwnProposal:false}},
  }
}
export function accountingReopeningFixture(companyId: string,proposalId: string,closedVersion = '3') {
  const initial = accountingOpeningFixture(companyId,periodFixtureId,closedVersion)
  return {companyId,kind:reopeningPeriodKind,itemId:proposalId,documentLabel:initial.documentLabel,sourceVersion:'1',pending:true,
    approvalReasonRequired:false,rejectionSupported:false,approvalSupported:true,
    references:[...initial.references,{role:'period-closure',recordId:closureFixtureId,label:closureFixtureId.slice(0,8),link:null}],
    rules:[{ruleCode:'accounting.period-reopening-requested',threshold:null,actual:null,deviation:null,unit:null}],
    accountingPeriodReopening:{sourceFactsDigest:'C'.repeat(64),previewDigest:'D'.repeat(64),laterPeriodClosed:false,
      period:{...initial.accountingPeriodOpening.period,status:'Closed',approvedByUserId:checker,approvedAtUtc:at,closedByUserId:checker,closedAtUtc:at},
      reopening:{id:proposalId,companyId,periodId:periodFixtureId,closureId:closureFixtureId,closedPeriodVersion:closedVersion,proposedBy:maker,proposedAtUtc:at,
        reason:'SENTETIK yeniden açma gerekçesi',approvedBy:null as string|null,approvedAtUtc:null as string|null,version:'1'},
      closure:{id:closureFixtureId,companyId,periodId:periodFixtureId,closedPeriodVersion:closedVersion,targetPeriodId:'aaaaaaaa-0000-4000-8000-000000000051',
        carryVoucherId:'aaaaaaaa-0000-4000-8000-000000000052',sourceHash:'E'.repeat(64),lineCount:0,closedBy:checker,closedAtUtc:at,
        reopeningId:null as string|null,reopenedBy:null as string|null,reopenedAtUtc:null as string|null}},
  }
}
