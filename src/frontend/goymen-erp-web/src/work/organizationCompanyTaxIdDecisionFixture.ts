import { ORGANIZATION_COMPANY_TAX_ID_WORK_KIND as kind, type WorkOrganizationCompanyTaxIdPreview } from './workOrganizationCompanyTaxIdDecision'
export const companyTaxIdRequestId='ac590000-0000-4000-8000-000000000001'
export const companyTaxIdMaker='SENTETIK company VKN maker'
export const companyTaxIdActor='SENTETIK company VKN checker'
type MutableHuman={-readonly[K in keyof WorkOrganizationCompanyTaxIdPreview]:WorkOrganizationCompanyTaxIdPreview[K]}
export function organizationCompanyTaxIdHuman(company:string,first=false):MutableHuman {
 return {companyId:company,companyVersion:'9007199254740993',companyVkn:first?null:'1234567890',companyVersionAtRequest:'9007199254740993',isCurrent:true,
  request:{id:companyTaxIdRequestId,currentVkn:first?null:'1234567890',proposedVkn:'9876543217',reason:'SENTETIK full native reason '+ 'g'.repeat(970),status:'pending',
   requestedBy:companyTaxIdMaker,requestedAtUtc:'2026-11-28T10:00:00.0000001Z',decidedBy:null,decidedAtUtc:null,decisionNote:null}}
}
export function organizationCompanyTaxIdFixture(company:string,first=false){
 const human=organizationCompanyTaxIdHuman(company,first)
 return {companyId:company,kind,itemId:companyTaxIdRequestId,documentLabel:human.request.proposedVkn,sourceVersion:human.companyVersion,pending:true,
  approvalReasonRequired:false,rejectionSupported:true,approvalSupported:true,references:[],
  rules:[{ruleCode:'organization.company-tax-id-change-approval-required',threshold:null,actual:null,deviation:null,unit:null},
   ...(first?[{ruleCode:'organization.company-tax-id-first',threshold:null,actual:null,deviation:null,unit:null}]:[])],organizationCompanyTaxId:human}
}
