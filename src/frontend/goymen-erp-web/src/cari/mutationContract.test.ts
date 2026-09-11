import { describe, expect, it } from 'vitest'
import { decodeCounterpartyMutation } from './mutationContract'
import { PARTY_KIND, PARTY_ROLE_FLAG } from './contract'

const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
const PARTY_1 = '11111111-1111-1111-1111-111111111111'
const PARTY_2 = '22222222-2222-2222-2222-222222222222'
const ACCOUNT_1 = 'cccccccc-1111-1111-1111-111111111111'
const ACCOUNT_2 = 'dddddddd-2222-2222-2222-222222222222'
const NIL = '00000000-0000-0000-0000-000000000000'

function account(over: Record<string, unknown> = {}) {
  return {
    id: ACCOUNT_1,
    companyId: COMPANY_A,
    roles: PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier,
    isActive: true,
    version: '1',
    ...over,
  }
}

/** The 201 body from the real SQL journey run (handoff §8.1), synthetic ids. */
function view(over: Record<string, unknown> = {}) {
  return {
    id: PARTY_1,
    code: 'CAR-000001',
    kind: PARTY_KIND.LegalEntity,
    legalName: 'Yolculuk Test Metal A.Ş.',
    tradeName: 'Yolculuk Metal',
    isActive: true,
    version: '1',
    companyAccounts: [account()],
    ...over,
  }
}

describe('decodeCounterpartyMutation — C-02 create (spec §3.3)', () => {
  it('accepts the journey 201 body bound to the active company', () => {
    const decoded = decodeCounterpartyMutation(view(), { activeCompanyId: COMPANY_A })
    expect(decoded).not.toBeNull()
    expect(decoded!.code).toBe('CAR-000001')
    expect(decoded!.account.companyId).toBe(COMPANY_A)
    expect(decoded!.account.roles).toBe(3)
    expect(decoded!.version).toBe('1')
    expect(decoded!.account.version).toBe('1')
  })

  it('keeps the version tokens as strings and never as numbers', () => {
    const decoded = decodeCounterpartyMutation(
      view({ version: '9007199254740993', companyAccounts: [account({ version: '9007199254740993' })] }),
      { activeCompanyId: COMPANY_A },
    )
    expect(decoded!.version).toBe('9007199254740993')
    expect(decoded!.account.version).toBe('9007199254740993')
    expect(typeof decoded!.version).toBe('string')
  })

  it('rejects a numeric version — the list wire type is not the mutation wire type', () => {
    expect(decodeCounterpartyMutation(view({ version: 1 }), { activeCompanyId: COMPANY_A })).toBeNull()
    expect(
      decodeCounterpartyMutation(view({ companyAccounts: [account({ version: 1 })] }), { activeCompanyId: COMPANY_A }),
    ).toBeNull()
    expect(decodeCounterpartyMutation(view({ version: '0' }), { activeCompanyId: COMPANY_A })).toBeNull()
    expect(decodeCounterpartyMutation(view({ version: '01' }), { activeCompanyId: COMPANY_A })).toBeNull()
  })

  it('rejects a missing, foreign or duplicate account (exact account binding)', () => {
    expect(decodeCounterpartyMutation(view({ companyAccounts: [] }), { activeCompanyId: COMPANY_A })).toBeNull()
    expect(
      decodeCounterpartyMutation(view({ companyAccounts: [account({ companyId: COMPANY_B })] }), {
        activeCompanyId: COMPANY_A,
      }),
    ).toBeNull()
    expect(
      decodeCounterpartyMutation(
        view({ companyAccounts: [account(), account({ id: ACCOUNT_2, companyId: COMPANY_B })] }),
        { activeCompanyId: COMPANY_A },
      ),
    ).toBeNull()
  })

  it('resolves roles=0 and roles=15 — the mutation view carries the whole 0..15 range', () => {
    // A role-less create answers 201 with roles=0. Rejecting it here made the write
    // succeed while the SCREEN reported a malformed response.
    for (const roles of [0, 15]) {
      const decoded = decodeCounterpartyMutation(
        view({ companyAccounts: [account({ roles })] }),
        { activeCompanyId: COMPANY_A },
      )
      expect(decoded, `roles=${String(roles)}`).not.toBeNull()
      expect(decoded!.account.roles).toBe(roles)
    }
  })

  it('rejects an unknown role bit, non-integers, negatives, strings and null', () => {
    for (const roles of [16, 3.5, -1, '3', 31, null, undefined]) {
      expect(
        decodeCounterpartyMutation(view({ companyAccounts: [account({ roles })] }), { activeCompanyId: COMPANY_A }),
        `roles=${String(roles)}`,
      ).toBeNull()
    }
  })

  it('rejects nil/invalid UUIDs and a bad kind', () => {
    expect(decodeCounterpartyMutation(view({ id: NIL }), { activeCompanyId: COMPANY_A })).toBeNull()
    expect(decodeCounterpartyMutation(view({ id: 'not-a-uuid' }), { activeCompanyId: COMPANY_A })).toBeNull()
    expect(decodeCounterpartyMutation(view({ kind: 3 }), { activeCompanyId: COMPANY_A })).toBeNull()
    expect(decodeCounterpartyMutation(view({ kind: '1' }), { activeCompanyId: COMPANY_A })).toBeNull()
    expect(decodeCounterpartyMutation(view(), { activeCompanyId: NIL })).toBeNull()
  })

  it('carries nothing the contract does not name (externalIntegrationCode stays out)', () => {
    const decoded = decodeCounterpartyMutation(
      view({ externalIntegrationCode: 'SAP-1', companyAccounts: [account({ externalIntegrationCode: 'SAP-2' })] }),
      { activeCompanyId: COMPANY_A },
    )
    expect(decoded).not.toBeNull()
    expect(JSON.stringify(decoded)).not.toContain('SAP-')
    expect(Object.keys(decoded!)).toEqual(['id', 'code', 'kind', 'legalName', 'tradeName', 'isActive', 'version', 'account'])
  })

  it('a blank trade name decodes as null; a non-string one is rejected', () => {
    expect(decodeCounterpartyMutation(view({ tradeName: null }), { activeCompanyId: COMPANY_A })!.tradeName).toBeNull()
    expect(decodeCounterpartyMutation(view({ tradeName: 5 }), { activeCompanyId: COMPANY_A })).toBeNull()
  })
})

describe('decodeCounterpartyMutation — CARI-LINK (spec §7.4)', () => {
  const linked = view({
    companyAccounts: [{ id: ACCOUNT_2, companyId: COMPANY_B, roles: PARTY_ROLE_FLAG.Supplier, isActive: true, version: '1' }],
  })

  it('accepts a 201 about the Party in the request path, carrying only the target account', () => {
    const decoded = decodeCounterpartyMutation(linked, { activeCompanyId: COMPANY_B, expectedPartyId: PARTY_1 })
    expect(decoded).not.toBeNull()
    expect(decoded!.id).toBe(PARTY_1)
    expect(decoded!.account.companyId).toBe(COMPANY_B)
    expect(decoded!.account.roles).toBe(PARTY_ROLE_FLAG.Supplier)
  })

  it('rejects a 201 about a DIFFERENT Party than the one linked', () => {
    expect(decodeCounterpartyMutation(linked, { activeCompanyId: COMPANY_B, expectedPartyId: PARTY_2 })).toBeNull()
  })

  it('rejects a response that still carries the source account instead of the target', () => {
    expect(decodeCounterpartyMutation(view(), { activeCompanyId: COMPANY_B, expectedPartyId: PARTY_1 })).toBeNull()
  })

  it('rejects an invalid expected id rather than skipping the binding', () => {
    expect(decodeCounterpartyMutation(linked, { activeCompanyId: COMPANY_B, expectedPartyId: 'x' })).toBeNull()
  })
})
