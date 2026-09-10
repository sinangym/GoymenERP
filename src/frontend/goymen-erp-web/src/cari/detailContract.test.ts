import { describe, expect, it } from 'vitest'
import { decodeCounterpartyDetail } from './detailContract'
import { PARTY_KIND, PARTY_ROLE_FLAG, rolesFromFlags } from './contract'

const COMPANY_A = 'aaaaaaaa-0000-0000-0000-000000000001'
const COMPANY_B = 'bbbbbbbb-0000-0000-0000-000000000002'
const PARTY_1 = '11111111-1111-1111-1111-111111111111'
const ACCOUNT_1 = 'cccccccc-1111-1111-1111-111111111111'
const ACCOUNT_2 = 'dddddddd-2222-2222-2222-222222222222'

/** The shape the merged API actually returns, including the sensitive field. */
function account(over: Record<string, unknown> = {}) {
  return {
    id: ACCOUNT_1,
    companyId: COMPANY_A,
    roles: PARTY_ROLE_FLAG.Customer,
    externalIntegrationCode: 'SAP-4711',
    isActive: true,
    version: '1',
    ...over,
  }
}

function detail(over: Record<string, unknown> = {}) {
  return {
    id: PARTY_1,
    code: 'CAR-000001',
    kind: PARTY_KIND.LegalEntity,
    legalName: 'Örnek Metal Ltd. Şti.',
    tradeName: 'Örnek',
    isActive: true,
    version: '3',
    companyAccounts: [account()],
    ...over,
  }
}

describe('decodeCounterpartyDetail — happy path', () => {
  it('accepts a well-formed detail bound to the active company', () => {
    const decoded = decodeCounterpartyDetail(detail(), COMPANY_A)
    expect(decoded).not.toBeNull()
    expect(decoded!.code).toBe('CAR-000001')
    expect(decoded!.account.companyId).toBe(COMPANY_A)
  })

  it('keeps the global Party state and the account state as separate facts', () => {
    const decoded = decodeCounterpartyDetail(
      detail({ isActive: true, companyAccounts: [account({ isActive: false })] }),
      COMPANY_A,
    )
    expect(decoded!.isActive).toBe(true)
    expect(decoded!.account.isActive).toBe(false)
  })

  it('carries combined role flags on the one account, not a row per role', () => {
    const decoded = decodeCounterpartyDetail(
      detail({
        companyAccounts: [account({ roles: PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier })],
      }),
      COMPANY_A,
    )
    expect(rolesFromFlags(decoded!.account.roles)).toEqual(['Customer', 'Supplier'])
  })
})

describe('decodeCounterpartyDetail — unknown server fields stay out of the model (§5.2)', () => {
  /**
   * The boundary these tests prove, stated precisely: the field does not enter the
   * decoded business model, anything derived from it, or JSON.stringify(decoded).
   *
   * It DOES reach the browser — it is in the fetch response, in transport memory
   * and visible in the Network panel. This is model hygiene and defense in depth.
   * It is NOT a substitute for the real boundary: a narrow server projection that
   * does not emit the field until a dedicated read permission and a redaction
   * owner exist (§5.2).
   */
  it('is absent from the decoded account even though the server sent it', () => {
    const decoded = decodeCounterpartyDetail(detail(), COMPANY_A)
    // Not "not rendered" — not present. A field on the view model reaches React
    // state, devtools, a future child component and anything that serializes it.
    expect(Object.hasOwn(decoded!.account, 'externalIntegrationCode')).toBe(false)
    expect(Object.keys(decoded!.account).sort()).toEqual([
      'companyId',
      'id',
      'isActive',
      'roles',
      'version',
    ])
  })

  it('does not survive serialization of the whole decoded object', () => {
    // The realistic leak path: something logs or posts the decoded value.
    const decoded = decodeCounterpartyDetail(detail(), COMPANY_A)
    expect(JSON.stringify(decoded)).not.toContain('SAP-4711')
    expect(JSON.stringify(decoded)).not.toContain('externalIntegrationCode')
  })

  it('is dropped whatever the server puts in it, including nested objects', () => {
    const decoded = decodeCounterpartyDetail(
      detail({ companyAccounts: [account({ externalIntegrationCode: { erp: 'X', ref: 'Y' } })] }),
      COMPANY_A,
    )
    // Its VALUE is never validated, because it is never taken — a malformed
    // sensitive field must not be a reason to reject an otherwise valid record.
    expect(decoded).not.toBeNull()
    expect(JSON.stringify(decoded)).not.toContain('erp')
  })

  it('an unknown extra field the server adds later is also left behind', () => {
    const decoded = decodeCounterpartyDetail(
      detail({ companyAccounts: [account({ creditLimit: 500000, riskScore: 'A' })] }),
      COMPANY_A,
    )
    expect(JSON.stringify(decoded)).not.toContain('creditLimit')
    expect(JSON.stringify(decoded)).not.toContain('riskScore')
  })
})

describe('decodeCounterpartyDetail — company binding (§5.3)', () => {
  it('rejects an account belonging to another company', () => {
    expect(
      decodeCounterpartyDetail(
        detail({ companyAccounts: [account({ companyId: COMPANY_B })] }),
        COMPANY_A,
      ),
    ).toBeNull()
  })

  it('rejects zero accounts and more than one account', () => {
    expect(decodeCounterpartyDetail(detail({ companyAccounts: [] }), COMPANY_A)).toBeNull()
    expect(
      decodeCounterpartyDetail(
        detail({ companyAccounts: [account(), account({ id: ACCOUNT_2, companyId: COMPANY_B })] }),
        COMPANY_A,
      ),
    ).toBeNull()
  })

  it('rejects a caller with no bound company', () => {
    expect(decodeCounterpartyDetail(detail(), '')).toBeNull()
    expect(decodeCounterpartyDetail(detail(), '00000000-0000-0000-0000-000000000000')).toBeNull()
  })

  it('never returns a partial record — a bad account rejects the NAME too', () => {
    // Rendering the legal name while rejecting the account would confirm that a
    // record exists to a caller who may not be entitled to know it.
    const decoded = decodeCounterpartyDetail(
      detail({ companyAccounts: [account({ companyId: COMPANY_B })] }),
      COMPANY_A,
    )
    expect(decoded).toBeNull()
    expect(JSON.stringify(decoded)).not.toContain('Örnek Metal')
  })
})

describe('decodeCounterpartyDetail — fails closed', () => {
  it('rejects an out-of-range PartyKind', () => {
    expect(decodeCounterpartyDetail(detail({ kind: 0 }), COMPANY_A)).toBeNull()
    expect(decodeCounterpartyDetail(detail({ kind: 3 }), COMPANY_A)).toBeNull()
  })

  it('resolves roles=0 — a role-less counterparty has a readable record', () => {
    const decoded = decodeCounterpartyDetail(
      detail({ companyAccounts: [account({ roles: 0 })] }),
      COMPANY_A,
    )
    expect(decoded).not.toBeNull()
    expect(decoded!.account.roles).toBe(0)
    expect(rolesFromFlags(decoded!.account.roles)).toEqual([])
  })

  it('still rejects unknown bits, negatives and non-integers', () => {
    for (const roles of [16, 32, -1, 1.5]) {
      expect(
        decodeCounterpartyDetail(detail({ companyAccounts: [account({ roles })] }), COMPANY_A),
      ).toBeNull()
    }
  })

  it('rejects nil or malformed UUIDs', () => {
    expect(
      decodeCounterpartyDetail(detail({ id: '00000000-0000-0000-0000-000000000000' }), COMPANY_A),
    ).toBeNull()
    expect(decodeCounterpartyDetail(detail({ id: 'not-a-uuid' }), COMPANY_A)).toBeNull()
    expect(
      decodeCounterpartyDetail(detail({ companyAccounts: [account({ id: 'nope' })] }), COMPANY_A),
    ).toBeNull()
  })

  it('rejects an empty code or legalName', () => {
    expect(decodeCounterpartyDetail(detail({ code: '   ' }), COMPANY_A)).toBeNull()
    expect(decodeCounterpartyDetail(detail({ legalName: '' }), COMPANY_A)).toBeNull()
  })

  it('rejects non-objects and a missing companyAccounts array', () => {
    expect(decodeCounterpartyDetail(null, COMPANY_A)).toBeNull()
    expect(decodeCounterpartyDetail('nope', COMPANY_A)).toBeNull()
    expect(decodeCounterpartyDetail({ id: PARTY_1 }, COMPANY_A)).toBeNull()
  })
})


describe('version is an int64 carried losslessly (contract #170)', () => {
  it('keeps a value past 2^53 exactly, digit for digit', () => {
    // 9007199254740993 is Number.MAX_SAFE_INTEGER + 2. As a JSON number it would
    // arrive as ...992 — a concurrency token that silently equals its neighbour.
    const huge = '9007199254740993'
    const decoded = decodeCounterpartyDetail(
      detail({ version: huge, companyAccounts: [account({ version: huge })] }),
      COMPANY_A,
    )
    expect(decoded!.version).toBe(huge)
    expect(decoded!.account.version).toBe(huge)
    // and it is never turned into a number anywhere along the way
    expect(typeof decoded!.version).toBe('string')
    expect(Number(decoded!.version)).not.toBe(Number(huge) - 1)
  })

  it('rejects a JSON number, which is what the old contract sent', () => {
    // Accepting one would reintroduce exactly the precision loss the string form
    // exists to prevent.
    expect(decodeCounterpartyDetail(detail({ version: 3 }), COMPANY_A)).toBeNull()
    expect(
      decodeCounterpartyDetail(detail({ companyAccounts: [account({ version: 1 })] }), COMPANY_A),
    ).toBeNull()
  })

  it('rejects a version longer than the contract allows', () => {
    // The contract pins `maxLength: 19` because Int64.MaxValue is 19 digits.
    // The pattern alone would accept any length, so a 20-digit value would enter
    // the model as a version the server cannot have issued. Harmless today —
    // version is opaque and read-only — but the decoder should accept exactly
    // what the contract can emit, not a superset of it.
    const maxInt64 = '9223372036854775807' // 19 digits, must still be accepted
    expect(decodeCounterpartyDetail(detail({ version: maxInt64 }), COMPANY_A)).not.toBeNull()

    const tooLong = '1'.repeat(20)
    expect(decodeCounterpartyDetail(detail({ version: tooLong }), COMPANY_A)).toBeNull()
    expect(
      decodeCounterpartyDetail(detail({ companyAccounts: [account({ version: tooLong })] }), COMPANY_A),
    ).toBeNull()
  })

  it('rejects shapes the contract pattern forbids', () => {
    for (const bad of ['0', '01', '-1', '1.5', '', ' 1', '1e3', 'abc', null]) {
      expect(decodeCounterpartyDetail(detail({ version: bad }), COMPANY_A)).toBeNull()
    }
  })
})
