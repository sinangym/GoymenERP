import { readJson } from './discountLimitApi'
import { FINANCE_POLICY_PATH } from './financePolicyContract'
import { decodeBankInstitutions, decodeCollateralRates } from './collateralPolicyContract'
export const fetchBankInstitutions = (company: string, signal?: AbortSignal) => readJson(`${FINANCE_POLICY_PATH}/bank-institutions`, company, raw => decodeBankInstitutions(raw, company), signal)
export const fetchCollateralRates = (company: string, signal?: AbortSignal) => readJson(`${FINANCE_POLICY_PATH}/collateral-rates`, company, raw => decodeCollateralRates(raw, company), signal)
