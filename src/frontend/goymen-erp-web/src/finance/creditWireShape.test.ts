import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeCreditAccount, sendCreditAccount, type CreditAccount } from './creditAccountApi'
import { decodeCreditMovement, sendCreditMovement } from './creditMovementApi'
import { decodeCreditLimitWorkspace, sendCreditLimit } from './creditLimitApi'
import { decideOfficialInvoice, decodeOfficialInvoice } from './officialSalesInvoiceApi'

// K3 P2-3: credit and official sales invoice decoders are exact-key and trust a 200 only when the replay headers agree.
const Company = 'c3a11111-1111-4111-8111-111111111111'
const Id = 'c3a12222-2222-4222-8222-222222222222'
const Currency = 'c3a13333-3333-4333-8333-333333333333'
const Party = 'c3a14444-4444-4444-8444-444444444444'
const Pdf = 'c3a15555-5555-4555-8555-555555555555'

afterEach(() => { vi.unstubAllGlobals() })

const account = (patch: Record<string, unknown> = {}) => ({ id: Id, companyId: Company, version: '1', group: 'Cash',
  lenderName: 'SENTETIK kurum', contractReference: 'SENTETIK-1', currencyId: Currency, principalAmount: '1000.00', startDate: '2026-11-17',
  status: 'Proposed', isOwnProposal: false, proposedAtUtc: '2026-11-17T10:00:00Z', decidedAtUtc: null, rejectionReason: null,
  installments: [], planTotals: null, profile: { lenderPartyId: Party, lenderCompanyPartyAccountId: Id,
    sourceDocument: { reference: 'SENTETIK', documentDate: '2026-11-16', revision: '1' }, planNature: 'SourceMovements',
    cash: { kind: 'Overdraft', maturityDate: null } }, tolerance: { profileId: Currency, profileVersion: 1, currencyScale: 2, smallestCurrencyUnit: '0.01' },
  ...patch })
const movementSource = { creditAccountId: Id, expectedCreditVersion: '2', kind: 'Drawdown',
  sourceDocument: { reference: 'SENTETIK', documentDate: '2026-11-17', revision: '1' }, transactionDate: '2026-11-17', accountingDate: '2026-11-17',
  components: [{ kind: 'principal', amount: '100.00', functionalAmount: null }], bankEffect: null, replacesOpeningMovementId: null,
  expectedOpeningMovementVersion: null, reason: 'SENTETIK' }
const movement = (patch: Record<string, unknown> = {}) => ({ id: Id, companyId: Company, version: '1', status: 'Proposed', source: movementSource,
  currencyId: Currency, isOwnProposal: false, proposedAtUtc: '2026-11-17T09:00:00Z', decidedAtUtc: null, rejectionReason: null,
  reversedByMovementId: null, openingPeriodVersion: null,
  moneyProfile: { profileId: Currency, profileVersion: 1, currencyScale: 2, smallestCurrencyUnit: '0.01' }, ...patch })
const limitAccount = { id: Id, companyId: Company, version: '2', status: 'Approved', currencyId: Currency, group: 'Cash',
  profile: { lenderPartyId: Party, lenderCompanyPartyAccountId: Company } } as CreditAccount
const limit = (patch: Record<string, unknown> = {}) => ({ id: Id, companyId: Company, lenderPartyId: Party, lenderCompanyPartyAccountId: Company,
  currencyId: Currency, group: 'Cash', version: '1', status: 'Proposed',
  source: { creditAccountId: Id, expectedCreditVersion: '2', expectedCurrentLimitId: null, expectedCurrentLimitVersion: null, amount: '50.000',
    sourceDocument: { reference: 'SENTETIK', documentDate: '2026-11-17', revision: '1' }, reason: 'SENTETIK' },
  isOwnProposal: false, proposedAtUtc: '2026-11-17T10:00:00Z', decidedAtUtc: null, rejectionReason: null, supersededById: null, ...patch })
const invoice = (patch: Record<string, unknown> = {}) => ({ invoiceId: Id, companyId: Company, officialNo: 'SENTETIK-1', ettn: Id,
  customerPartyId: Party, currencyId: Currency, invoiceDate: '2026-11-17', payableAmount: '10.00', payableFunctionalAmount: '10.00',
  taxBaseFunctionalAmount: '10.00', vatFunctionalAmount: '0', withholdingFunctionalAmount: '0', pdfDocumentId: Pdf, pdfDocumentVersionNo: 1,
  pdfSha256: 'a'.repeat(64), version: '1', status: 'PendingApproval', recordedBy: 'SENTETIK-maker', recordedAtUtc: '2026-11-17T10:00:00Z',
  approvedBy: null, approvedAtUtc: null, rejectedBy: null, rejectedAtUtc: null, rejectionReason: null, withdrawnBy: null, withdrawnAtUtc: null, ...patch })

const envelope = (resource: unknown, isReplay: boolean, extra: Record<string, unknown> = {}) =>
  ({ outcome: 'Succeeded', resource, isReplay, originalCorrelationId: 'SENTETIK-original', issues: [], ...extra })
const respond = (body: unknown, headers: Record<string, string> = {}) =>
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json', ...headers } })))
const REPLAY = { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' }

describe('credit account decoder and command (exact keys, replay headers)', () => {
  it('rejects an unknown or missing key at every level and accepts absent optional properties', () => {
    expect(decodeCreditAccount(account(), Company)).not.toBeNull()
    expect(decodeCreditAccount(account({ unexpected: 1 }), Company)).toBeNull()
    const { version: _version, ...missing } = account()
    expect(decodeCreditAccount(missing, Company)).toBeNull()
    expect(decodeCreditAccount(account({ tolerance: { profileId: Currency, profileVersion: 1, currencyScale: 2, smallestCurrencyUnit: '0.01', extra: 'x' } }), Company)).toBeNull()
    expect(decodeCreditAccount(account({ profile: { ...account().profile, cash: { kind: 'Overdraft', maturityDate: null, extra: true } } }), Company)).toBeNull()
    expect(decodeCreditAccount(account({ profile: { ...account().profile, sourceDocument: { reference: 'S', documentDate: '2026-11-16', revision: '1', x: 1 } } }), Company)).toBeNull()
    const { profile: _profile, tolerance: _tolerance, ...withoutOptional } = account()
    expect(decodeCreditAccount(withoutOptional, Company)).not.toBeNull()
  })

  it.each([
    ['a replay body without replay headers', true, {}],
    ['a fresh body with a replay header', false, { 'Idempotency-Replayed': 'true' }],
    ['a replay naming another original correlation', true, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-other' }],
  ] as const)('treats %s as unknown', async (_name, isReplay, headers) => {
    respond(envelope(account(), isReplay), headers)
    expect(await sendCreditAccount(Company, { operation: 'approve', key: 'SENTETIK-key', id: Id, request: { expectedVersion: '1' } }))
      .toEqual({ type: 'unknown' })
  })

  it('accepts a replay whose headers agree, and refuses an envelope with an extra key', async () => {
    const approved = account({ status: 'Approved', decidedAtUtc: '2026-11-17T11:00:00Z' })
    respond(envelope(approved, true), REPLAY)
    const applied = await sendCreditAccount(Company, { operation: 'approve', key: 'SENTETIK-key', id: Id, request: { expectedVersion: '1' } })
    expect(applied.type === 'applied' && applied.replay).toBe(true)
    respond(envelope(approved, false, { extra: null }))
    expect(await sendCreditAccount(Company, { operation: 'approve', key: 'SENTETIK-key', id: Id, request: { expectedVersion: '1' } }))
      .toEqual({ type: 'unknown' })
  })
})

describe('credit movement and bank limit decoders (exact keys, replay headers)', () => {
  it('rejects extra keys in the movement, its source, components and money profile', () => {
    expect(decodeCreditMovement(movement(), Company, Id)).not.toBeNull()
    expect(decodeCreditMovement(movement({ extra: 1 }), Company, Id)).toBeNull()
    expect(decodeCreditMovement(movement({ source: { ...movementSource, extra: 1 } }), Company, Id)).toBeNull()
    expect(decodeCreditMovement(movement({ source: { ...movementSource, components: [{ kind: 'principal', amount: '1', functionalAmount: null, x: 1 }] } }), Company, Id)).toBeNull()
    expect(decodeCreditMovement(movement({ moneyProfile: { profileId: Currency, profileVersion: 1, currencyScale: 2, smallestCurrencyUnit: '0.01', x: 1 } }), Company, Id)).toBeNull()
  })

  it('treats a movement decision replay without agreeing headers as unknown', async () => {
    const approved = movement({ status: 'Approved', version: '2', decidedAtUtc: '2026-11-17T10:00:00Z' })
    const attempt = { key: 'SENTETIK-key', operation: 'decide', id: Id, request: { expectedVersion: '1', approve: true, reason: null } } as const
    respond(envelope(approved, true))
    expect(await sendCreditMovement(Company, Id, attempt)).toEqual({ type: 'unknown' })
    respond(envelope(approved, true), REPLAY)
    expect((await sendCreditMovement(Company, Id, attempt)).type).toBe('applied')
  })

  it('rejects extra keys in the limit workspace and a limit decision replay without agreeing headers', async () => {
    const workspace = (items: unknown[], assessment: Record<string, unknown> = {}) => ({ companyId: Company, creditAccountId: Id, items,
      assessment: { recordedPrincipalNet: '0', exceedsLimit: null, proposedMovements: [], ...assessment } })
    expect(decodeCreditLimitWorkspace(workspace([limit()]), limitAccount)).not.toBeNull()
    expect(decodeCreditLimitWorkspace({ ...workspace([limit()]), extra: 1 }, limitAccount)).toBeNull()
    expect(decodeCreditLimitWorkspace(workspace([limit({ extra: 1 })]), limitAccount)).toBeNull()
    expect(decodeCreditLimitWorkspace(workspace([limit()], { extra: 1 }), limitAccount)).toBeNull()
    const approved = limit({ status: 'Approved', version: '2', decidedAtUtc: '2026-11-17T11:00:00Z' })
    const attempt = { key: 'SENTETIK-key', operation: 'decide', id: Id, request: { expectedVersion: '1', approve: true, reason: null } } as const
    respond(envelope(approved, true))
    expect(await sendCreditLimit(limitAccount, attempt)).toEqual({ type: 'unknown' })
    respond(envelope(approved, true), REPLAY)
    expect((await sendCreditLimit(limitAccount, attempt)).type).toBe('applied')
  })
})

describe('official sales invoice decoder and decision (exact keys, replay headers)', () => {
  it('rejects extra or missing keys, also inside a source difference', () => {
    expect(decodeOfficialInvoice(invoice(), Company)).not.toBeNull()
    expect(decodeOfficialInvoice(invoice({ extra: 1 }), Company)).toBeNull()
    const { pdfSha256: _hash, ...missing } = invoice()
    expect(decodeOfficialInvoice(missing, Company)).toBeNull()
    expect(decodeOfficialInvoice(invoice({ sourceDifferences: [{ importBatchId: Party, sourceDocumentId: Pdf, officialNo: 'SENTETIK-1', ettn: Id,
      fields: ['invoiceDate'], invoiceDate: null, payableFunctionalAmount: null, winner: 'manual', extra: 1 }] }), Company)).toBeNull()
  })

  it('applies a decision only when the replay headers agree with the body', async () => {
    const approved = invoice({ status: 'Approved', approvedBy: 'SENTETIK-checker', approvedAtUtc: '2026-11-17T11:00:00Z', version: '2' })
    const attempt = { key: 'SENTETIK-key', operation: 'approval', invoiceId: Id, expectedVersion: '1' } as const
    respond(envelope(approved, true, { existingOfficialNo: null }))
    expect(await decideOfficialInvoice(Company, 'SENTETIK-checker', attempt)).toEqual({ type: 'unknown' })
    respond(envelope(approved, false, { existingOfficialNo: null }), { 'Idempotency-Replayed': 'true' })
    expect(await decideOfficialInvoice(Company, 'SENTETIK-checker', attempt)).toEqual({ type: 'unknown' })
    respond(envelope(approved, true, { existingOfficialNo: null }), REPLAY)
    expect((await decideOfficialInvoice(Company, 'SENTETIK-checker', attempt)).type).toBe('applied')
    respond(envelope(approved, false))
    expect(await decideOfficialInvoice(Company, 'SENTETIK-checker', attempt)).toEqual({ type: 'unknown' })
  })
})
