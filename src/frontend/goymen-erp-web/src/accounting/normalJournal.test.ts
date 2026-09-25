import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeNormalJournalDraft } from './contract'
import { sendNormalJournalCommand } from './normalJournalApi'

const COMPANY = 'a0000000-0000-4000-8000-000000000001'
const DRAFT = 'a0000000-0000-4000-8000-000000000002'
const SOURCE = 'a0000000-0000-4000-8000-000000000003'
const ACTOR = 'a0000000-0000-4000-8000-000000000004'
const ACCOUNT_1 = 'a0000000-0000-4000-8000-000000000005'
const ACCOUNT_2 = 'a0000000-0000-4000-8000-000000000006'
const CURRENCY = 'a0000000-0000-4000-8000-000000000007'

function draft(overrides: Record<string, unknown> = {}) {
  return {
    draftId: DRAFT, companyId: COMPANY, version: 1, status: 'Draft', preparedByUserId: ACTOR,
    preparedAtUtc: '2026-09-24T13:00:00Z', approvedByUserId: null, approvedAtUtc: null,
    periodId: null, postedJournalId: null, sourceId: SOURCE, postingPurpose: 'approved-purpose',
    postingRuleVersion: 'approved-rule-v1', accountPlanSourceVersion: 'mikro-v1',
    accountPlanSourceSha256: 'a'.repeat(64), currencyId: CURRENCY, transactionDate: '2026-09-24',
    accountingDate: '2026-09-24', valueDate: null,
    lines: [
      { lineNumber: 1, mappingKey: 'debit', side: 'Debit', accountId: ACCOUNT_1, amount: '10.25' },
      { lineNumber: 2, mappingKey: 'credit', side: 'Credit', accountId: ACCOUNT_2, amount: '10.25' },
    ], ...overrides,
  }
}

afterEach(() => vi.unstubAllGlobals())

describe('normal journal contract', () => {
  it('decodes exact Draft and Approved states, including permitted self approval', () => {
    expect(decodeNormalJournalDraft(draft(), COMPANY)?.status).toBe('Draft')
    expect(decodeNormalJournalDraft(draft({ status: 'Approved', version: 2, approvedByUserId: ACTOR,
      approvedAtUtc: '2026-09-24T14:00:00Z', periodId: SOURCE, postedJournalId: DRAFT }), COMPANY)?.status).toBe('Approved')
  })

  it.each([
    draft({ extra: true }),
    draft({ companyId: SOURCE }),
    draft({ lines: [{ lineNumber: 1, mappingKey: 'debit', side: 'Debit', accountId: ACCOUNT_1, amount: 10.25 }] }),
    draft({ status: 'Approved' }),
  ])('refuses malformed or cross-company answers', value => {
    expect(decodeNormalJournalDraft(value, COMPANY)).toBeNull()
  })
})

describe('normal journal commands', () => {
  it('posts only the approved public create fields and decodes exact 201', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(draft()), { status: 201, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const request = { draftId: DRAFT, sourceId: SOURCE, transactionDate: '2026-09-24', accountingDate: '2026-09-24', valueDate: null,
      lines: [{ accountId: ACCOUNT_1, side: 'Debit' as const, amount: '10.25' }, { accountId: ACCOUNT_2, side: 'Credit' as const, amount: '10.25' }] }
    const result = await sendNormalJournalCommand(COMPANY, { operation: 'create', idempotencyKey: 'key-1', request })
    expect(result.type).toBe('applied')
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/api/v1/accounting/normal-journal-drafts')
    expect(JSON.parse(init.body as string)).toEqual(request)
    expect(init.headers).toMatchObject({ 'X-Company-Id': COMPANY, 'Idempotency-Key': 'key-1' })
  })

  it('keeps provider failure unknown and classifies a known not-pending refusal', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ code: 'service_unavailable' }), { status: 503 })))
    const approve = { operation: 'approve' as const, idempotencyKey: 'key-2', draftId: DRAFT, expectedVersion: 1, periodId: SOURCE }
    expect(await sendNormalJournalCommand(COMPANY, approve)).toEqual({ type: 'unknown', reason: 'service' })
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ code: 'accounting_normal_journal_draft_not_pending' }), { status: 409 })))
    expect(await sendNormalJournalCommand(COMPANY, approve)).toEqual({ type: 'failed', failure: { kind: 'not-pending' } })
  })

  it.each([
    ['accounting_concurrent_change', 'concurrent-change'],
    ['accounting_normal_journal_draft_content_conflict', 'content-conflict'],
  ] as const)('classifies documented 409 code %s as %s', async (code, kind) => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ code }), { status: 409 })))
    const approve = { operation: 'approve' as const, idempotencyKey: 'key-3', draftId: DRAFT, expectedVersion: 1, periodId: SOURCE }
    expect(await sendNormalJournalCommand(COMPANY, approve)).toEqual({ type: 'failed', failure: { kind } })
  })
})
