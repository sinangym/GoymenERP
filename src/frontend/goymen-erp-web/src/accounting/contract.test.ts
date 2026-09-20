/** The Accounting wire decoders refuse anything the backend does not send. Unit tests, SENTETIK data. */

import { describe, expect, it } from 'vitest'
import {
  decodeJournalDetail,
  decodeJournalPage,
  decodePeriod,
  decodePeriodPage,
  decodePostingQueues,
  periodCommandBody,
} from './contract'
import { APPROVER, COMPANY_A, COMPANY_B, PREPARER, journalDetail, journalSummary, openPeriod, period } from './testHarness'

describe('decodePeriod', () => {
  it('reads a proposal, an open and a closed period', () => {
    expect(decodePeriod(period({ isOwnProposal: true }), COMPANY_A)).toMatchObject({ status: 'Proposed', isOwnProposal: true })
    expect(decodePeriod(openPeriod(), COMPANY_A)).toMatchObject({ status: 'Open', version: 2 })
    expect(
      decodePeriod(openPeriod({ status: 'Closed', version: 3, closedByUserId: APPROVER, closedAtUtc: '2026-10-01T09:00:00+00:00' }), COMPANY_A),
    ).toMatchObject({ status: 'Closed' })
  })

  it.each([
    ['another company', { companyId: COMPANY_B }],
    ['an unknown status', { status: 'Reopened' }],
    ['a version as text', { version: '1' }],
    ['a zero version', { version: 0 }],
    ['reversed dates', { startsOn: '2026-09-30', endsOn: '2026-09-01' }],
    ['an instant as a date', { startsOn: '2026-09-01T00:00:00Z' }],
    ['a proposal with an approval', { approvedByUserId: APPROVER, approvedAtUtc: '2026-08-31T10:00:00+00:00' }],
  ])('refuses %s', (_, overrides: Record<string, unknown>) => {
    expect(decodePeriod({ ...period(), ...overrides }, COMPANY_A)).toBeNull()
  })

  it('refuses a missing field', () => {
    const body: Record<string, unknown> = period()
    delete body.isOwnProposal
    expect(decodePeriod(body, COMPANY_A)).toBeNull()
  })

  it('refuses an open period approved by its own preparer (Ö4 holds in what is shown too)', () => {
    expect(decodePeriod(openPeriod({ approvedByUserId: PREPARER }), COMPANY_A)).toBeNull()
  })

  it('refuses an unknown extra field and a page with a foreign period', () => {
    expect(decodePeriod({ ...period(), extra: 1 }, COMPANY_A)).toBeNull()
    expect(decodePeriodPage({ items: [period(), period({ companyId: COMPANY_B })], nextCursor: null }, COMPANY_A)).toBeNull()
  })
})

describe('decodeJournal*', () => {
  it('reads a summary page and a detail whose lines match the count', () => {
    expect(decodeJournalPage({ items: [journalSummary()], nextCursor: 'c-1' })).toMatchObject({ nextCursor: 'c-1' })
    expect(decodeJournalDetail(journalDetail())?.lines).toHaveLength(2)
  })

  it.each([
    ['a numeric amount', { totalDebit: 1250.5 }],
    ['a signed amount', { totalCredit: '-1' }],
    ['an exponent', { totalDebit: '1e3' }],
    ['an unknown kind', { kind: 'Voucher' }],
    ['an original with a link', { linkedJournalId: 'a3000000-0000-4000-8000-000000000002' }],
    ['a reversal without a link', { kind: 'Reversal' }],
  ])('refuses %s', (_, overrides: Record<string, unknown>) => {
    expect(decodeJournalPage({ items: [journalSummary(overrides)], nextCursor: null })).toBeNull()
  })

  it('refuses a detail whose lines do not match its own count', () => {
    const body = journalDetail()
    expect(decodeJournalDetail({ ...body, lines: body.lines.slice(0, 1) })).toBeNull()
  })
})

describe('decodePostingQueues', () => {
  it('reads both queues and refuses an exception with no missing key', () => {
    const exception = {
      sourceId: 'a4000000-0000-4000-8000-000000000001',
      postingPurpose: 'synthetic-purpose-a',
      postingRuleVersion: 'synthetic-rule-1',
      missingMappingKeys: ['synthetic-credit-key'],
      firstMessageId: 'm-1',
    }
    const pending = {
      sourceId: exception.sourceId,
      postingPurpose: exception.postingPurpose,
      postingRuleVersion: exception.postingRuleVersion,
      linesAwaitingAmount: 1,
      firstMessageId: 'm-2',
    }
    expect(decodePostingQueues({ exceptions: [exception], pending: [pending] })).toMatchObject({
      exceptions: [{ missingMappingKeys: ['synthetic-credit-key'] }],
      pending: [{ linesAwaitingAmount: 1 }],
    })
    expect(decodePostingQueues({ exceptions: [{ ...exception, missingMappingKeys: [] }], pending: [] })).toBeNull()
    expect(decodePostingQueues({ exceptions: [], pending: [{ ...pending, linesAwaitingAmount: 0 }] })).toBeNull()
  })
})

describe('periodCommandBody', () => {
  it('sends exactly the request shapes and never a company', () => {
    expect(
      periodCommandBody({ operation: 'propose', idempotencyKey: 'k', periodId: 'p', startsOn: '2026-09-01', endsOn: '2026-09-30' }),
    ).toEqual({ periodId: 'p', startsOn: '2026-09-01', endsOn: '2026-09-30' })
    expect(periodCommandBody({ operation: 'approve', idempotencyKey: 'k', periodId: 'p', expectedVersion: 1 })).toEqual({
      expectedVersion: 1,
    })
  })
})
