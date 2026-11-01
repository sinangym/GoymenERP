import { describe, expect, it } from 'vitest'
import { E_DOCUMENT_TEXT as T, decodeAccountEDocumentProfile, eDocumentPath, effectiveScenarioText } from './eDocument'

const PARTY = '11111111-1111-1111-1111-111111111111'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const ACCOUNT = '44444444-4444-4444-4444-444444444444'
const expected = { partyId: PARTY, companyId: COMPANY }

const profile = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  accountId: ACCOUNT,
  companyId: COMPANY,
  version: '0',
  profileToken: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
  accountIsActive: true,
  taxIdentityNumberPresent: true,
  status: 'Unknown',
  statusCheckedAtUtc: null,
  statusCheckedInActiveCompany: null,
  statusCheckedByDisplayName: null,
  scenarioOverrideCode: null,
  evaluatedOn: '2026-10-29',
  scenarioCodes: { state: 'NotSet', codes: [] },
  settingsDefault: { state: 'NotSet', scenarioCode: null },
  effectiveScenario: { outcome: 'Unresolved', source: 'None', scenarioCode: null, unresolvedReason: 'SettingsDefaultNotSet' },
  viewer: { canUpdate: true },
  ...over,
})

describe('SAT-D102 e-belge profile decoder', () => {
  it('accepts the starting answer: version 0, Unknown, no default, unresolved', () => {
    const decoded = decodeAccountEDocumentProfile(profile(), expected)
    expect(decoded?.version).toBe('0')
    expect(decoded?.status).toBe('Unknown')
    expect(decoded?.taxIdentityNumberPresent).toBe(true)
    expect(decoded?.effectiveScenario.outcome).toBe('Unresolved')
  })

  it('accepts a customer with no VKN (status Unknown, version 0)', () => {
    const decoded = decodeAccountEDocumentProfile(
      profile({ taxIdentityNumberPresent: false, profileToken: null }),
      expected,
    )
    expect(decoded?.taxIdentityNumberPresent).toBe(false)
    expect(decoded?.version).toBe('0')
  })

  it('accepts a check recorded in another group company, without the checker', () => {
    const decoded = decodeAccountEDocumentProfile(
      profile({
        status: 'EArchive',
        version: '2',
        statusCheckedAtUtc: '2026-10-29T07:30:00+00:00',
        statusCheckedInActiveCompany: false,
      }),
      expected,
    )
    expect(decoded?.statusCheckedInActiveCompany).toBe(false)
    expect(decoded?.statusCheckedByDisplayName).toBeNull()
  })

  it('accepts a shared profile at a positive version', () => {
    const decoded = decodeAccountEDocumentProfile(
      profile({ version: '3', scenarioCodes: { state: 'Set', codes: ['TEMELFATURA'] } }),
      expected,
    )
    expect(decoded?.version).toBe('3')
  })

  it('accepts a recorded check with a listed override', () => {
    const decoded = decodeAccountEDocumentProfile(
      profile({
        status: 'EInvoice',
        statusCheckedAtUtc: '2026-10-29T07:30:00+00:00',
        statusCheckedInActiveCompany: true,
        statusCheckedByDisplayName: 'Ayşe Yılmaz',
        scenarioOverrideCode: 'CODE_A',
        scenarioCodes: { state: 'Set', codes: ['CODE_A', 'CODE_B'] },
        effectiveScenario: { outcome: 'Resolved', source: 'CustomerOverride', scenarioCode: 'CODE_A', unresolvedReason: 'None' },
      }),
      expected,
    )
    expect(decoded?.scenarioOverrideCode).toBe('CODE_A')
    expect(decoded?.statusCheckedByDisplayName).toBe('Ayşe Yılmaz')
    expect(decoded?.profileToken).toBe('a'.repeat(64))
  })

  it.each([
    ['another party', { partyId: '22222222-2222-2222-2222-222222222222' }],
    ['another company', { companyId: 'bbbbbbbb-0000-0000-0000-000000000002' }],
    ['an unknown status', { status: 'Maybe' }],
    ['a number version', { version: 1 }],
    ['an Unknown status with a check', { statusCheckedAtUtc: '2026-10-29T07:30:00Z', statusCheckedInActiveCompany: true }],
    ['a checker name for an Unknown status', { statusCheckedByDisplayName: 'Ayşe' }],
    [
      "another company's checker name",
      {
        status: 'EInvoice',
        statusCheckedAtUtc: '2026-10-29T07:30:00Z',
        statusCheckedInActiveCompany: false,
        statusCheckedByDisplayName: 'Ayşe',
      },
    ],
    ['a token without a VKN', { taxIdentityNumberPresent: false }],
    ['a VKN without a token', { profileToken: null }],
    ['a malformed token', { profileToken: 'ABC' }],
    ['a known status without its check', { status: 'EArchive' }],
    ['a lower-case override', { scenarioOverrideCode: 'code_a' }],
    ['a Set list without codes', { scenarioCodes: { state: 'Set', codes: [] } }],
    ['a NotSet list with codes', { scenarioCodes: { state: 'NotSet', codes: ['CODE_A'] } }],
    ['a repeated code', { scenarioCodes: { state: 'Set', codes: ['CODE_A', 'CODE_A'] } }],
    ['a Set default without a code', { settingsDefault: { state: 'Set', scenarioCode: null } }],
    [
      'a Resolved scenario without a code',
      { effectiveScenario: { outcome: 'Resolved', source: 'SettingsDefault', scenarioCode: null, unresolvedReason: 'None' } },
    ],
    ['a missing viewer', { viewer: null }],
    ['a missing taxIdentityNumberPresent', { taxIdentityNumberPresent: undefined }],
    ['a non-canonical version', { version: '01' }],
    // Like the Sales decoders: an unknown member anywhere is off-contract, never ignored.
    ['an unknown top-level member', { vatIdentityNumber: '1234567890' }],
    ['an unknown member in the code list', { scenarioCodes: { state: 'NotSet', codes: [], source: 'x' } }],
    ['an unknown member in the default', { settingsDefault: { state: 'NotSet', scenarioCode: null, extra: true } }],
    [
      'an unknown member in the effective scenario',
      { effectiveScenario: { outcome: 'Unresolved', source: 'None', scenarioCode: null, unresolvedReason: 'SettingsDefaultNotSet', x: 1 } },
    ],
    ['an unknown member in the viewer', { viewer: { canUpdate: true, canDelete: true } }],
    ['an array as the viewer', { viewer: [true] }],
  ])('refuses %s', (_name, over) => {
    expect(decodeAccountEDocumentProfile(profile(over), expected)).toBeNull()
  })

  it('names the effective scenario without inventing a code', () => {
    expect(
      effectiveScenarioText({ outcome: 'Resolved', source: 'SettingsDefault', scenarioCode: 'CODE_B', unresolvedReason: 'None' }),
    ).toBe(T.effectiveFromDefault('CODE_B'))
    expect(
      effectiveScenarioText({ outcome: 'Unresolved', source: 'None', scenarioCode: null, unresolvedReason: 'SettingsDefaultNotSet' }),
    ).toBe(T.effectiveUnresolved)
    expect(
      effectiveScenarioText({
        outcome: 'Unresolved',
        source: 'CustomerOverride',
        scenarioCode: null,
        unresolvedReason: 'CustomerOverrideNotListed',
      }),
    ).toBe(T.effectiveOverrideNotListed)
    expect(
      effectiveScenarioText({ outcome: 'Unavailable', source: 'None', scenarioCode: null, unresolvedReason: 'None' }),
    ).toBe(T.effectiveUnavailable)
  })

  it('builds the account path from the party only (the company travels in X-Company-Id)', () => {
    expect(eDocumentPath(PARTY)).toBe(`/counterparties/${PARTY}/company-account/e-document`)
  })
})
