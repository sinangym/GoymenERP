import type { WorkPostingAccountMappingPreview } from './workPostingAccountMappingDecision'
import { POSTING_MAPPING_KIND, postingMappingWorkId } from './workPostingAccountMappingDecision'
export function postingMappingFixture(companyId:string,mappingVersion='9007199254740993',active='0',status:'Prepared'|'Active'|'Superseded'='Prepared') {
 const id=postingMappingWorkId(mappingVersion)
 if(id===null)throw new Error('A genuine native mapping identity is required')
 const version=status==='Prepared'?'1':status==='Active'?'2':'3'
 const human:WorkPostingAccountMappingPreview={mapping:{companyId,mappingVersion,effectiveFrom:'0001-01-02',status,version,
  accountPlanSourceVersion:'SENTETIK prepared plan',accountPlanSourceSha256:'E'.repeat(64),
  preparedByUserId:'aaaaaaaa-0000-4000-8000-000000000051',preparedAtUtc:'2026-11-27T09:00:00.1234567+00:00',
  activatedByUserId:status==='Prepared'?null:'aaaaaaaa-0000-4000-8000-000000000052',activatedAtUtc:status==='Prepared'?null:'2026-11-01T00:00:00Z',
  rows:Array.from({length:13},(_,i)=>({purpose:'purchase-invoice',mappingKey:'vat-input',itemType:`Type${i}`,linePurpose:`Line${i}`,isExport:i%2===0,
   accountId:`cccccccc-0000-4000-8000-${String(i+1).padStart(12,'0')}`,accountCode:`SENTETIK HISTORICAL-${i+1}`,vatRateCode:`VAT-${i}`}))},
  preparedAccountPlan:{sourceVersion:'SENTETIK prepared plan',sourceSha256:'E'.repeat(64)},
  currentActiveAccountPlan:{companyId,reference:{sourceVersion:'SENTETIK current replacement plan',sourceSha256:'F'.repeat(64)},
   accounts:Array.from({length:13},(_,i)=>({accountId:`dddddddd-0000-4000-8000-${String(i+1).padStart(12,'0')}`,code:`CURRENT-${i+1}`,name:`SENTETIK current account ${i+1}`}))},
  expectedVersion:version,expectedActiveMappingVersion:status==='Active'?mappingVersion:active,sourceFactsDigest:'A'.repeat(64),previewDigest:'B'.repeat(64)}
 return {companyId,kind:POSTING_MAPPING_KIND,itemId:id,documentLabel:mappingVersion,sourceVersion:version,pending:status==='Prepared',approvalReasonRequired:false,rejectionSupported:false,
  rules:[{ruleCode:'accounting.posting-mapping-activation-required',threshold:null,actual:null,deviation:null,unit:null}],
  references:[{role:'posting-account-mapping-version',recordId:id,label:mappingVersion,link:'/muhasebe/hesap-eslemeleri'}],postingAccountMapping:human}
}
