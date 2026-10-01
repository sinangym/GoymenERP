import { describe, expect, it } from 'vitest'
import { decodeNormalJournalDraftSourcePreparation } from './contract'

const COMPANY = 'a0000000-0000-4000-8000-000000000001'
const SOURCE = 'a0000000-0000-4000-8000-000000000002'
const ACCOUNT_1 = 'a0000000-0000-4000-8000-000000000003'
const ACCOUNT_2 = 'a0000000-0000-4000-8000-000000000004'
const CURRENCY = 'a0000000-0000-4000-8000-000000000005'

function body() {
  return {
    companyId: COMPANY, sourceId: SOURCE, preparationVersion: 'b'.repeat(64), sourceVersion: '2',
    ownerProfileId: 'finance-profile', ownerProfileVersion: '7', currencyId: CURRENCY,
    postingPurpose: 'manual-receipt', postingRuleVersion: 'rule-v1',
    accountPlanSourceVersion: 'mikro-v1', accountPlanSourceSha256: 'a'.repeat(64),
    mappings: [
      { mappingKey: 'debit', side: 'Debit', accountId: ACCOUNT_1, accountCode: '100', accountName: 'Kasa' },
      { mappingKey: 'credit', side: 'Credit', accountId: ACCOUNT_2, accountCode: '120', accountName: 'Alicilar' },
    ],
  }
}

describe('normal journal source preparation decoder', () => {
  it('keeps the exact source-bound approved choices', () => {
    expect(decodeNormalJournalDraftSourcePreparation(body(), COMPANY, SOURCE)).toEqual(body())
  })

  it.each([
    ['foreign company', (value: ReturnType<typeof body>) => { value.companyId = 'a0000000-0000-4000-8000-000000000099' }],
    ['wrong fingerprint', (value: ReturnType<typeof body>) => { value.preparationVersion = 'short' }],
    ['duplicate choice', (value: ReturnType<typeof body>) => { value.mappings[1] = { ...value.mappings[0] } }],
    ['extra field', (value: ReturnType<typeof body>) => { Object.assign(value, { invented: true }) }],
  ])('refuses %s', (_, mutate) => {
    const value = body()
    mutate(value)
    expect(decodeNormalJournalDraftSourcePreparation(value, COMPANY, SOURCE)).toBeNull()
  })
})
