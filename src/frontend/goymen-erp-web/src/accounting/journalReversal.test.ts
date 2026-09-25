import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  decodeJournalReversalProposal,
  proposalRequest,
  validateProposalForm,
  type ProposalForm,
} from './journalReversalContract'
import {
  APPROVE_RULE_CODES,
  PROPOSE_RULE_CODES,
  classifyJournalReversalFailure,
  fetchJournalReversalProposal,
  sendJournalReversalCommand,
  type JournalReversalSnapshot,
} from './journalReversalApi'
import {
  APPROVER,
  COMPANY_A,
  COMPANY_B,
  JOURNAL,
  PERIOD_SEPTEMBER,
  PREPARER,
  PROPOSAL,
  approvedReversalProposal,
  json,
  problem,
  reversalProposal,
} from './testHarness'

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

const FORM: ProposalForm = {
  periodId: PERIOD_SEPTEMBER,
  transactionDate: '2026-09-23',
  accountingDate: '2026-09-24',
  valueDate: '',
  reason: '  SENTETIK gerekçe  ',
}

const PROPOSE: JournalReversalSnapshot = {
  operation: 'propose',
  idempotencyKey: 'SENTETIK-key-1',
  request: proposalRequest(PROPOSAL, JOURNAL, FORM),
}

describe('journal reversal contract', () => {
  it('decodes a pending and an approved proposal of the bound company only', () => {
    expect(decodeJournalReversalProposal(reversalProposal(), COMPANY_A)?.status).toBe('Pending')
    expect(decodeJournalReversalProposal(approvedReversalProposal(), COMPANY_A)?.reversalJournalId).not.toBeNull()
    expect(decodeJournalReversalProposal(reversalProposal(), COMPANY_B)).toBeNull()
  })

  it('refuses extra or missing keys, unknown status and status-inconsistent shapes', () => {
    expect(decodeJournalReversalProposal({ ...reversalProposal(), extra: 1 }, COMPANY_A)).toBeNull()
    const { reason: _dropped, ...missing } = reversalProposal()
    expect(decodeJournalReversalProposal(missing, COMPANY_A)).toBeNull()
    expect(decodeJournalReversalProposal(reversalProposal({ status: 'Posted' }), COMPANY_A)).toBeNull()
    // Pending must carry no decision and no reversal journal.
    expect(decodeJournalReversalProposal(reversalProposal({ reversalJournalId: JOURNAL }), COMPANY_A)).toBeNull()
    // Approved must name exactly one reversal journal distinct from its source...
    expect(decodeJournalReversalProposal(approvedReversalProposal({ reversalJournalId: null }), COMPANY_A)).toBeNull()
    expect(decodeJournalReversalProposal(approvedReversalProposal({ reversalJournalId: JOURNAL }), COMPANY_A)).toBeNull()
    // ...and a checker who is not the proposer (maker ≠ checker is never shown as satisfied by the proposer).
    expect(decodeJournalReversalProposal(approvedReversalProposal({ decidedBy: PREPARER }), COMPANY_A)).toBeNull()
    expect(decodeJournalReversalProposal(reversalProposal({ version: 0 }), COMPANY_A)).toBeNull()
    expect(decodeJournalReversalProposal(reversalProposal({ reason: 'x'.repeat(1025) }), COMPANY_A)).toBeNull()
  })

  it('validates only the public syntax and trims the reason into the request', () => {
    expect(validateProposalForm(FORM)).toBeNull()
    expect(validateProposalForm({ ...FORM, periodId: 'not-a-guid' })).toBe('periodId')
    expect(validateProposalForm({ ...FORM, accountingDate: '' })).toBe('accountingDate')
    expect(validateProposalForm({ ...FORM, valueDate: '2026-02-30' })).toBe('valueDate')
    expect(validateProposalForm({ ...FORM, reason: '   ' })).toBe('reason')
    expect(validateProposalForm({ ...FORM, reason: 'x'.repeat(1025) })).toBe('reason')
    expect(proposalRequest(PROPOSAL, JOURNAL, FORM)).toEqual({
      proposalId: PROPOSAL,
      sourceJournalId: JOURNAL,
      periodId: PERIOD_SEPTEMBER,
      transactionDate: '2026-09-23',
      accountingDate: '2026-09-24',
      valueDate: null,
      reason: 'SENTETIK gerekçe',
    })
  })
})

describe('journal reversal transport', () => {
  it('sends the frozen snapshot with its key and company, and reads a replay header', async () => {
    const fetchMock = vi.fn(async () => json(reversalProposal(), 201, { 'Idempotency-Replayed': 'true' }))
    vi.stubGlobal('fetch', fetchMock)
    const outcome = await sendJournalReversalCommand(COMPANY_A, PROPOSE)
    expect(outcome).toMatchObject({ type: 'applied', replayed: true })
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toMatch(/\/accounting\/journal-reversal-proposals$/)
    expect(init.method).toBe('POST')
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('SENTETIK-key-1')
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY_A)
    expect(JSON.parse(init.body as string)).toEqual(PROPOSE.request)
  })

  it('treats no answer, 5xx, an undescribed code or another proposal as UNKNOWN', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new TypeError('network')
    }))
    expect((await sendJournalReversalCommand(COMPANY_A, PROPOSE)).type).toBe('unknown')
    vi.stubGlobal('fetch', vi.fn(async () => problem(503)))
    expect(await sendJournalReversalCommand(COMPANY_A, PROPOSE)).toEqual({ type: 'unknown', reason: 'service' })
    vi.stubGlobal('fetch', vi.fn(async () => problem(422, { code: 'accounting_period_closed' })))
    // A code the propose route does not publish is not guessed.
    expect(await sendJournalReversalCommand(COMPANY_A, PROPOSE)).toEqual({ type: 'unknown', reason: 'malformed' })
    vi.stubGlobal('fetch', vi.fn(async () => json(reversalProposal({ proposalId: JOURNAL }), 201)))
    expect(await sendJournalReversalCommand(COMPANY_A, PROPOSE)).toEqual({ type: 'unknown', reason: 'malformed' })
  })

  it('classifies route-exact 422 codes and definite refusals', () => {
    for (const code of PROPOSE_RULE_CODES) {
      expect(classifyJournalReversalFailure('propose', 422, { code })).toEqual({ kind: 'rule', code })
      expect(classifyJournalReversalFailure('approve', 422, { code })).toBeNull()
    }
    for (const code of APPROVE_RULE_CODES) {
      expect(classifyJournalReversalFailure('approve', 422, { code })).toEqual({ kind: 'rule', code })
      expect(classifyJournalReversalFailure('propose', 422, { code })).toBeNull()
    }
    expect(classifyJournalReversalFailure('approve', 403, { code: 'forbidden' })).toEqual({ kind: 'forbidden' })
    expect(classifyJournalReversalFailure('approve', 404, null)).toEqual({ kind: 'not-found' })
    expect(classifyJournalReversalFailure('propose', 404, null)).toBeNull()
    expect(classifyJournalReversalFailure('propose', 409, { code: 'accounting_journal_reversal_proposal_content_conflict' }))
      .toEqual({ kind: 'content-conflict' })
    expect(classifyJournalReversalFailure('approve', 409, {
      code: 'accounting_version_conflict', expectedVersion: '1', currentVersion: '2',
    })).toEqual({ kind: 'version-conflict', expectedVersion: 1, currentVersion: 2 })
  })

  it('approves with only the expected version in the body', async () => {
    const fetchMock = vi.fn(async () => json(approvedReversalProposal(), 200))
    vi.stubGlobal('fetch', fetchMock)
    const outcome = await sendJournalReversalCommand(COMPANY_A, {
      operation: 'approve', idempotencyKey: 'SENTETIK-approve', proposalId: PROPOSAL, expectedVersion: 1,
    })
    expect(outcome).toMatchObject({ type: 'applied', proposal: { status: 'Approved', decidedBy: APPROVER } })
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toMatch(new RegExp(`/journal-reversal-proposals/${PROPOSAL}/approval$`))
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: 1 })
  })

  it('reads one proposal as exactly 200 of the bound company', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json(reversalProposal())))
    expect((await fetchJournalReversalProposal(COMPANY_A, PROPOSAL)).type).toBe('ok-single')
    vi.stubGlobal('fetch', vi.fn(async () => json(reversalProposal({ companyId: COMPANY_B }))))
    expect((await fetchJournalReversalProposal(COMPANY_A, PROPOSAL)).type).toBe('malformed')
    vi.stubGlobal('fetch', vi.fn(async () => problem(404)))
    expect(await fetchJournalReversalProposal(COMPANY_A, PROPOSAL)).toMatchObject({ type: 'http', status: 404 })
  })
})
