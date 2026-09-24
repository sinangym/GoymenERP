import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ACCOUNTING_CAPABILITY, normalDraftApprovalPath } from './capabilities'
import { COMPANY_A, installFetch, json, renderAt, requestsFor } from './testHarness'

const COMPANY = COMPANY_A
const DRAFT = 'a0000000-0000-4000-8000-000000000002'
const SOURCE = 'a0000000-0000-4000-8000-000000000003'
const ACTOR = 'a0000000-0000-4000-8000-000000000004'
const PERIOD = 'a0000000-0000-4000-8000-000000000005'
const ACCOUNT_1 = 'a0000000-0000-4000-8000-000000000006'
const ACCOUNT_2 = 'a0000000-0000-4000-8000-000000000007'
const CURRENCY = 'a0000000-0000-4000-8000-000000000008'

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

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('normal journal approval interaction', () => {
  it('freezes the period and primary action so rapid clicks produce one command', async () => {
    let release: ((response: Response) => void) | undefined
    const pending = new Promise<Response>(resolve => { release = resolve })
    const recorded = installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftApprove],
      respond: (url, init) => {
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}`) && (init?.method ?? 'GET') === 'GET') return json(draft())
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}/approval`) && init?.method === 'POST') return pending
        return undefined
      },
    })
    renderAt(normalDraftApprovalPath(DRAFT))
    const period = await screen.findByLabelText('Açık dönem kimliği')
    fireEvent.change(period, { target: { value: PERIOD } })
    const approve = screen.getByRole('button', { name: 'Normal taslağı onayla' })

    fireEvent.click(approve)
    fireEvent.click(approve)

    await waitFor(() => expect(requestsFor(recorded, '/approval', 'POST')).toHaveLength(1))
    expect((period as HTMLInputElement).disabled).toBe(true)
    expect((approve as HTMLButtonElement).disabled).toBe(true)
    release?.(json(draft({ status: 'Approved', version: 2, approvedByUserId: ACTOR,
      approvedAtUtc: '2026-09-24T14:00:00Z', periodId: PERIOD, postedJournalId: DRAFT })))
    await screen.findByText('Approved')
  })

  it('retries an unknown result with the exact frozen key and body', async () => {
    let approvals = 0
    const recorded = installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftApprove],
      respond: (url, init) => {
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}`) && (init?.method ?? 'GET') === 'GET') return json(draft())
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}/approval`) && init?.method === 'POST') {
          approvals += 1
          if (approvals === 1) throw new TypeError('network')
          return json(draft({ status: 'Approved', version: 2, approvedByUserId: ACTOR,
            approvedAtUtc: '2026-09-24T14:00:00Z', periodId: PERIOD, postedJournalId: DRAFT }))
        }
        return undefined
      },
    })
    renderAt(normalDraftApprovalPath(DRAFT))
    fireEvent.change(await screen.findByLabelText('Açık dönem kimliği'), { target: { value: PERIOD } })
    fireEvent.click(screen.getByRole('button', { name: 'Normal taslağı onayla' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı isteği yeniden gönder' }))
    await screen.findByText('Approved')

    const commands = requestsFor(recorded, '/approval', 'POST')
    expect(commands).toHaveLength(2)
    expect(commands[1].headers['Idempotency-Key']).toBe(commands[0].headers['Idempotency-Key'])
    expect(commands[1].body).toEqual(commands[0].body)
  })
})
