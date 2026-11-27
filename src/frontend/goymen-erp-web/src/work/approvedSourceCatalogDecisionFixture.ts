import type { WorkApprovedSourceCatalogPreview } from './workApprovedSourceCatalogDecision'
import { APPROVED_SOURCE_CATALOG_KIND } from './workApprovedSourceCatalogDecision'

const maker = 'aaaaaaaa-0000-4000-8000-000000000051', checker = 'aaaaaaaa-0000-4000-8000-000000000052'
export function approvedSourceCatalogFixture(companyId: string,packageId: string,version = '1',active = '0',status: 'Prepared'|'Active'|'Superseded' = 'Prepared') {
  const accounts = Array.from({length:13},(_,i)=>({accountId:`cccccccc-0000-4000-8000-${String(i+1).padStart(12,'0')}`,code:`SENTETIK-${i+1}`,name:`SENTETIK hesap ${i+1}`}))
  const human: WorkApprovedSourceCatalogPreview = {catalog:{packageId,companyId,status,version,catalogVersion:status==='Prepared'?null:'1',
    templateVersion:'accounting-approved-source-package-csv.v1',accountPlanSourceVersion:'SENTETIK plan v1',accountsSourceSha256:'E'.repeat(64),postingMappingsSourceSha256:'F'.repeat(64),
    preparedByUserId:maker,preparedAtUtc:'2026-11-27T09:00:00.1234567+00:00',activatedByUserId:status==='Prepared'?null:checker,
    activatedAtUtc:status==='Prepared'?null:'2026-11-27T10:00:00Z',accountCount:accounts.length,bindingCount:1},
    package:{accounts,bindings:[{sourceId:packageId,sourceKind:'finance.manual-receipt.recorded.v1',sourceVersion:'SENTETIK original v7',
      ownerProfileId:'SENTETIK receipt profile',ownerProfileVersion:'SENTETIK profile v1',currencyId:companyId,postingPurpose:'SENTETIK receipt posting',postingRuleVersion:'SENTETIK rule v3',
      mappings:accounts.map((a,i)=>({mappingKey:`SENTETIK eşleme ${i+1}`,side:i%2===0?'Debit':'Credit',accountId:a.accountId}))}]},
    expectedPackageVersion:version,expectedActiveCatalogVersion:active,sourceFactsDigest:'A'.repeat(64),previewDigest:'B'.repeat(64)}
  return {companyId,kind:APPROVED_SOURCE_CATALOG_KIND,itemId:packageId,documentLabel:packageId.slice(0,8).toLowerCase(),sourceVersion:version,
    pending:status==='Prepared',approvalReasonRequired:false,rejectionSupported:false,
    rules:[{ruleCode:'accounting.approved-source-catalog-activation-required',threshold:null,actual:null,deviation:null,unit:null}],
    references:[{role:'approved-source-catalog-package',recordId:packageId,label:packageId.slice(0,8).toLowerCase(),link:null}],approvedSourceCatalog:human}
}
