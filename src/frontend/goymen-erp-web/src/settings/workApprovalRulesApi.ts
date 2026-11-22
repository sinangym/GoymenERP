import { readJson } from './discountLimitApi'
import { WORK_APPROVAL_RULES_PATH, decodeWorkApprovalRuleTable } from './workApprovalRulesContract'

export const fetchWorkApprovalRules = (company: string, signal?: AbortSignal) =>
  readJson(WORK_APPROVAL_RULES_PATH, company, (raw) => decodeWorkApprovalRuleTable(raw, company), signal)
