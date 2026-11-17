import { describe, expect, it } from 'vitest'
import detailWire from './examples/detail-response.json'
import riskWire from './examples/risk-check-response.json'
import { decodeDraftDetail } from './contract'
import { decodeRiskCheck } from './riskCheck'

const detail = decodeDraftDetail(detailWire, detailWire.draft.id)!
const proof = { companyId: detail.draft.companyId, collateralId: '12345678-1111-4111-8111-123456789011', collateralRevision: '4',
  collateralTypeId: '12345678-1111-4111-8111-123456789012', typeVersion: '7', bankInstitutionId: null, bankSourceVersion: null,
  ratePercent: '0', sourceVersion: 'settings.collateral-type:synthetic:7', scope: 'Type', businessDate: '2026-11-18' }
const answer = () => ({ ...structuredClone(riskWire), draftVersion: detail.draft.version, partyId: detail.draft.partyId,
  companyAccountId: detail.draft.companyAccountId, collateral: { kind: 'Collateral', source: 'finance.party-collateral', state: 'Available',
    taxBasis: null, amounts: [], reasonCode: null, policyProofs: [proof] } })

describe('K6 risk policy provenance', () => {
  it('keeps actual rate and source version without treating a real zero rate as unavailable', () => {
    expect(decodeRiskCheck(answer(), detail)?.collateral.policyProofs).toEqual([proof])
    const legacy = answer() as Record<string, any>
    delete legacy.collateral.policyProofs
    expect(decodeRiskCheck(legacy, detail)?.collateral.policyProofs).toBeUndefined()
  })
  it.each([
    { companyId: '12345678-1111-4111-8111-123456789099' }, { ratePercent: '100.01' }, { scope: 'TypeBank' },
    { sourceVersion: '' }, { bankInstitutionId: '12345678-1111-4111-8111-123456789013' }, { collateralRevision: '0' },
  ])('refuses foreign, missing or contradictory real policy evidence %o', fault => {
    const body = answer(); body.collateral.policyProofs = [{ ...proof, ...fault }] as typeof body.collateral.policyProofs
    expect(decodeRiskCheck(body, detail)).toBeNull()
  })
  it('refuses leaked policy evidence when prices are withheld', () => {
    const body = answer() as Record<string, any>
    body.amountDisclosure = 'Withheld'
    for (const part of [...body.components, body.thisOrder, body.creditLimit, body.collateral]) part.amounts = null
    if (body.inLocalCurrency !== null) body.inLocalCurrency = { ...body.inLocalCurrency, grossRisk: null, grossRiskWithThisOrder: null }
    if (body.inLimitCurrency !== null) body.inLimitCurrency = { ...body.inLimitCurrency, grossRisk: null, grossRiskWithThisOrder: null }
    expect(decodeRiskCheck(body, detail)).toBeNull()
  })
})
