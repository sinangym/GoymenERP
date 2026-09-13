/**
 * Test-only VERBATIM copies of the Finance Backend examples — SENTETIK/NOT_ENV.
 *
 * Draft #258 @fb482de5c5eb7b677cf18c75cf32d82c644f7703
 *   src/backend/Modules/Finance/examples/manual-receipt.normal.json   blob 4d649090f100290c919a6e78e0fbd48f944cd67b
 *   src/backend/Modules/Finance/examples/manual-receipt.negative.json blob 594f0b94a59005a7b8061f962bc6d995ea5e93c5
 *
 * Copied, not imported: the files are not on this branch and live in the Backend lane.
 * If the Backend changes an example, this copy must be re-pinned to the new blob; the
 * tests are about THESE bytes. The UUIDs, profile and versions are synthetic test
 * inputs, never registry entries, grants or a money profile.
 */

export const NORMAL_EXAMPLE = {
  label: 'SENTETIK/NOT_ENV',
  contractVersion: 'v1-preparation',
  draftSave: {
    draftId: null,
    expectedVersion: null,
    preparation: {
      partyId: '44444444-4444-4444-4444-444444444444',
      companyAccountId: '55555555-5555-5555-5555-555555555555',
      treasuryAccountId: '33333333-3333-3333-3333-333333333333',
      money: { amount: '1000', currencyId: '22222222-2222-2222-2222-222222222222' },
      profile: { profileId: '77777777-7777-7777-7777-777777777777', version: '9007199254740993' },
      occurredOn: '2026-09-13',
      valueOn: null,
      bankReference: null,
      description: 'SENTETIK/NOT_ENV',
    },
  },
  record: { draftId: '66666666-6666-6666-6666-666666666666', expectedVersion: '9007199254740993' },
  reverse: {
    sourceId: '66666666-6666-6666-6666-666666666666',
    expectedVersion: '9007199254740994',
    occurredOn: '2026-09-13',
    reason: 'SENTETIK correction',
  },
  list: { cursor: null },
  detail: { sourceId: '66666666-6666-6666-6666-666666666666' },
  draftSaveResult: {
    outcome: 'Succeeded',
    isReplay: false,
    originalCorrelationId: 'SENTETIK-attempt-1',
    issues: [],
    resource: {
      source: {
        id: '66666666-6666-6666-6666-666666666666',
        companyId: '11111111-1111-1111-1111-111111111111',
        sourceNumber: 'SENTETIK-DRAFT-1',
        version: '9007199254740993',
        stage: 'Draft',
        partyId: '44444444-4444-4444-4444-444444444444',
        companyAccountId: '55555555-5555-5555-5555-555555555555',
        treasuryAccountId: '33333333-3333-3333-3333-333333333333',
        currencyId: '22222222-2222-2222-2222-222222222222',
        occurredOn: '2026-09-13',
        valueOn: null,
        amount: { disclosure: 'Visible', money: { amount: '1000', currencyId: '22222222-2222-2222-2222-222222222222' } },
      },
      profile: { profileId: '77777777-7777-7777-7777-777777777777', version: '9007199254740993' },
      bankReference: null,
      description: 'SENTETIK/NOT_ENV',
      recordedAtUtc: null,
      reversalSourceId: null,
      reversesSourceId: null,
      unallocated: { knowledge: 'Unavailable', amount: { disclosure: 'Unavailable', money: null } },
    },
  },
} as const

export const NEGATIVE_EXAMPLE = {
  label: 'SENTETIK/NOT_ENV',
  negativeCases: [
    { field: 'money.amount', value: 1000, expected: 'JSON rejection: string required' },
    { field: 'money.amount', value: '-1', expected: 'settings_decimal_invalid' },
    { field: 'money.amount', value: '1e3', expected: 'settings_decimal_invalid' },
    { field: 'occurredOn', value: '2026-09-13T00:00:00Z', expected: 'JSON rejection: date-only required' },
    { field: 'profile', value: null, expected: 'settings_provider_unavailable from owner resolution' },
    { field: 'treasuryAccountId', condition: 'foreign company or currency', expected: 'bank_account_ineligible' },
    { field: 'companyAccountId', condition: 'foreign company or inactive', expected: 'party_account_ineligible' },
    { field: 'money.amount', value: '1.1234', syntheticProfileScale: 3, expected: 'settings_decimal_scale_exceeded' },
  ],
  unavailableResult: { outcome: 'Unavailable', resource: null, isReplay: false, originalCorrelationId: 'SENTETIK-attempt-1', issues: [] },
  conflictResult: { outcome: 'VersionConflict', resource: null, isReplay: false, originalCorrelationId: 'SENTETIK-attempt-1', issues: [] },
  idempotencyConflictResult: {
    outcome: 'IdempotencyConflict',
    resource: null,
    isReplay: false,
    originalCorrelationId: 'SENTETIK-attempt-1',
    issues: [],
  },
  redactedAmount: { disclosure: 'Redacted', money: null },
  unavailableAllocation: { knowledge: 'Unavailable', amount: { disclosure: 'Unavailable', money: null } },
} as const

/** The synthetic company the normal example's resource is bound to. */
export const EXAMPLE_COMPANY = '11111111-1111-1111-1111-111111111111'
export const EXAMPLE_CURRENCY = '22222222-2222-2222-2222-222222222222'
export const EXAMPLE_BANK = '33333333-3333-3333-3333-333333333333'
export const EXAMPLE_PARTY = '44444444-4444-4444-4444-444444444444'
export const EXAMPLE_ACCOUNT = '55555555-5555-5555-5555-555555555555'
export const EXAMPLE_SOURCE = '66666666-6666-6666-6666-666666666666'
export const EXAMPLE_PROFILE = { profileId: '77777777-7777-7777-7777-777777777777', version: '9007199254740993' } as const
