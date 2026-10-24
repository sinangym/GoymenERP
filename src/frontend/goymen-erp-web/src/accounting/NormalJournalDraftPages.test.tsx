import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ACCOUNTING_CAPABILITY, ACCOUNTING_ROUTE, normalDraftApprovalPath } from './capabilities'
import { COMPANY_A, installFetch, json, renderAt, requestsFor } from './testHarness'

const COMPANY = COMPANY_A
const DRAFT = 'a0000000-0000-4000-8000-000000000002'
const SOURCE = 'a0000000-0000-4000-8000-000000000003'
const ACTOR = 'a0000000-0000-4000-8000-000000000004'
const PERIOD = 'a0000000-0000-4000-8000-000000000005'
const ACCOUNT_1 = 'a0000000-0000-4000-8000-000000000006'
const ACCOUNT_2 = 'a0000000-0000-4000-8000-000000000007'
const CURRENCY = 'a0000000-0000-4000-8000-000000000008'
const PREPARATION = 'B'.repeat(64)

function sourcePage() {
  return {
    companyId: COMPANY,
    items: [{
      id: SOURCE, companyId: COMPANY, sourceNumber: 'SENTETIK-TAHSILAT-1', version: '2', stage: 'Recorded',
      partyId: 'a0000000-0000-4000-8000-000000000009',
      companyAccountId: 'a0000000-0000-4000-8000-000000000010',
      treasuryAccountId: 'a0000000-0000-4000-8000-000000000011', currencyId: CURRENCY,
      occurredOn: '2026-09-24', valueOn: null,
      amount: { disclosure: 'Visible', money: { amount: '10.25', currencyId: CURRENCY } },
    }],
    nextCursor: null,
  }
}

function preparation(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY, sourceId: SOURCE, preparationVersion: PREPARATION, sourceVersion: '2',
    ownerProfileId: 'finance-profile', ownerProfileVersion: '7', currencyId: CURRENCY,
    postingPurpose: 'approved-purpose', postingRuleVersion: 'approved-rule-v1',
    accountPlanSourceVersion: 'mikro-v1', accountPlanSourceSha256: 'a'.repeat(64),
    mappings: [
      { mappingKey: 'debit', side: 'Debit', accountId: ACCOUNT_1, accountCode: '100', accountName: 'Kasa' },
      { mappingKey: 'credit', side: 'Credit', accountId: ACCOUNT_2, accountCode: '120', accountName: 'Alicilar' },
    ], ...overrides,
  }
}

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

async function selectRecordedSource() {
  await screen.findByRole('option', { name: /SENTETIK-TAHSILAT-1/ })
  fireEvent.change(screen.getByLabelText('Onaylı finans kaynağı'), { target: { value: SOURCE } })
}

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('normal journal source-bound creation', () => {
  it('submits approved choices and the exact preparation version', async () => {
    const recorded = installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftCreate],
      respond: (url, init) => {
        if (url.includes('/finance/manual-receipts')) return json(sourcePage())
        if (url.endsWith(`/normal-journal-draft-sources/${SOURCE}`)) return json(preparation())
        if (url.endsWith('/normal-journal-drafts') && init?.method === 'POST') return json(draft(), 201)
        return undefined
      },
    })
    renderAt(ACCOUNTING_ROUTE.normalDraftCreate)
    await selectRecordedSource()
    await screen.findByText('Onaylı ayna mikro-v1; kural approved-rule-v1.')
    const sourcePicker = screen.getByLabelText('Onaylı finans kaynağı', { exact: true })
    expect(sourcePicker.id).toBe('normal-journal-source')
    expect(document.querySelector('label[for="normal-journal-source"]')?.textContent).toBe('Onaylı finans kaynağı')
    const accounts = screen.getAllByLabelText('Onaylı hesap', { exact: true })
    expect(accounts.map(account => account.id)).toEqual(['normal-journal-account-0', 'normal-journal-account-1'])
    expect(accounts.every(account =>
      document.querySelector(`label[for="${account.id}"]`)?.textContent === 'Onaylı hesap')).toBe(true)
    fireEvent.change(accounts[0], { target: { value: `Debit:${ACCOUNT_1}` } })
    fireEvent.change(accounts[1], { target: { value: `Credit:${ACCOUNT_2}` } })
    fireEvent.change(screen.getByLabelText('İşlem tarihi'), { target: { value: '2026-09-24' } })
    fireEvent.change(screen.getByLabelText('Muhasebe tarihi'), { target: { value: '2026-09-24' } })
    const amounts = screen.getAllByLabelText('Tutar')
    fireEvent.change(amounts[0], { target: { value: '10,25' } })
    fireEvent.change(amounts[1], { target: { value: '10,25' } })
    fireEvent.click(screen.getByRole('button', { name: 'Taslağı kaydet' }))

    await screen.findByText('Normal fiş taslağı kaydedildi')
    const command = requestsFor(recorded, '/normal-journal-drafts', 'POST').at(-1)
    expect(command?.body).toMatchObject({
      sourceId: SOURCE,
      transactionDate: '2026-09-24',
      accountingDate: '2026-09-24',
      expectedPreparationVersion: PREPARATION,
      lines: [
        { accountId: ACCOUNT_1, side: 'Debit', amount: '10.25' },
        { accountId: ACCOUNT_2, side: 'Credit', amount: '10.25' },
      ],
    })
  })

  it('surfaces stale preparation and refreshes the chooser without changing the sent body', async () => {
    let reads = 0
    const recorded = installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftCreate],
      respond: (url, init) => {
        if (url.includes('/finance/manual-receipts')) return json(sourcePage())
        if (url.endsWith(`/normal-journal-draft-sources/${SOURCE}`)) {
          reads += 1
          return json(preparation({ preparationVersion: (reads === 1 ? 'B' : 'C').repeat(64) }))
        }
        if (url.endsWith('/normal-journal-drafts') && init?.method === 'POST') {
          return json({ code: 'accounting_normal_journal_preparation_version_conflict' }, 409)
        }
        return undefined
      },
    })
    renderAt(ACCOUNTING_ROUTE.normalDraftCreate)
    await selectRecordedSource()
    await screen.findByText('Onaylı ayna mikro-v1; kural approved-rule-v1.')
    const accounts = screen.getAllByLabelText('Onaylı hesap')
    fireEvent.change(accounts[0], { target: { value: `Debit:${ACCOUNT_1}` } })
    fireEvent.change(accounts[1], { target: { value: `Credit:${ACCOUNT_2}` } })
    fireEvent.change(screen.getByLabelText('İşlem tarihi'), { target: { value: '2026-09-24' } })
    fireEvent.change(screen.getByLabelText('Muhasebe tarihi'), { target: { value: '2026-09-24' } })
    screen.getAllByLabelText('Tutar').forEach(input => fireEvent.change(input, { target: { value: '10,25' } }))
    fireEvent.click(screen.getByRole('button', { name: 'Taslağı kaydet' }))

    await screen.findByText('Onaylı hesap hazırlığı değişti. Seçimleri yenileyin.')
    const command = requestsFor(recorded, '/normal-journal-drafts', 'POST')[0]
    expect(command.body).toMatchObject({ expectedPreparationVersion: PREPARATION })
    fireEvent.click(screen.getByRole('button', { name: 'Hazırlığı yenile' }))
    await waitFor(() => expect(reads).toBe(2))
  })

  it('reads every authorized source page before offering a recorded source', async () => {
    const firstSource = sourcePage()
    const recorded = installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftCreate],
      respond: (url) => {
        if (url.includes('/finance/manual-receipts')) {
          const cursor = new URL(url, 'http://test').searchParams.get('cursor')
          if (cursor === null) return json({
            ...firstSource,
            items: firstSource.items.map(source => ({
              ...source,
              id: 'a0000000-0000-4000-8000-000000000099',
              sourceNumber: 'SENTETIK-TASLAK-ILK-SAYFA',
              stage: 'Draft',
            })),
            nextCursor: 'recorded-page',
          })
          if (cursor === 'recorded-page') return json(sourcePage())
        }
        if (url.endsWith(`/normal-journal-draft-sources/${SOURCE}`)) return json(preparation())
        return undefined
      },
    })
    renderAt(ACCOUNTING_ROUTE.normalDraftCreate)
    const picker = await screen.findByLabelText('Onaylı finans kaynağı')
    await screen.findByRole('option', { name: /SENTETIK-TAHSILAT-1/ })
    expect(screen.queryByRole('option', { name: /SENTETIK-TASLAK-ILK-SAYFA/ })).toBeNull()
    expect(requestsFor(recorded, '/finance/manual-receipts', 'GET')).toHaveLength(2)
    fireEvent.change(picker, { target: { value: SOURCE } })
    await screen.findByText('Onaylı ayna mikro-v1; kural approved-rule-v1.')
  })

  it('recalls an unknown create after close and re-entry and retries the exact key and body', async () => {
    let creates = 0
    const recorded = installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftCreate],
      respond: (url, init) => {
        if (url.includes('/finance/manual-receipts')) return json(sourcePage())
        if (url.endsWith(`/normal-journal-draft-sources/${SOURCE}`)) return json(preparation())
        if (url.endsWith('/normal-journal-drafts') && init?.method === 'POST') {
          creates += 1
          if (creates === 1) throw new TypeError('network')
          if (creates === 2) return json({ status: 401 }, 401)
          return json(draft(), 201)
        }
        return undefined
      },
    })
    const first = renderAt(ACCOUNTING_ROUTE.normalDraftCreate)
    await selectRecordedSource()
    await screen.findByText('Onaylı ayna mikro-v1; kural approved-rule-v1.')
    const accounts = screen.getAllByLabelText('Onaylı hesap')
    fireEvent.change(accounts[0], { target: { value: `Debit:${ACCOUNT_1}` } })
    fireEvent.change(accounts[1], { target: { value: `Credit:${ACCOUNT_2}` } })
    fireEvent.change(screen.getByLabelText('İşlem tarihi'), { target: { value: '2026-09-24' } })
    fireEvent.change(screen.getByLabelText('Muhasebe tarihi'), { target: { value: '2026-09-24' } })
    screen.getAllByLabelText('Tutar').forEach(input => fireEvent.change(input, { target: { value: '10,25' } }))
    fireEvent.click(screen.getByRole('button', { name: 'Taslağı kaydet' }))
    await screen.findByRole('button', { name: 'Aynı isteği yeniden gönder' })

    first.view.unmount()
    const second = renderAt(ACCOUNTING_ROUTE.normalDraftCreate)
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı isteği yeniden gönder' }))
    await screen.findByText(/Oturum sonlandı/i)

    second.view.unmount()
    renderAt(ACCOUNTING_ROUTE.normalDraftCreate)
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı isteği yeniden gönder' }))
    await screen.findByText('Normal fiş taslağı kaydedildi')

    const commands = requestsFor(recorded, '/normal-journal-drafts', 'POST')
    expect(commands).toHaveLength(3)
    for (const command of commands.slice(1)) {
      expect(command.headers['Idempotency-Key']).toBe(commands[0].headers['Idempotency-Key'])
      expect(command.body).toEqual(commands[0].body)
    }
  })
})

const PREPARER = 'a0000000-0000-4000-8000-000000000012'
const APPROVER = 'a0000000-0000-4000-8000-000000000013'
const OTHER_PERIOD = 'a0000000-0000-4000-8000-000000000014'
const PROPOSED_PERIOD = 'a0000000-0000-4000-8000-000000000015'
const CLOSED_PERIOD = 'a0000000-0000-4000-8000-000000000016'

/** A period row exactly as the server's M-07 read returns it; the facts follow the status. */
function period(periodId: string, startsOn: string, endsOn: string, status: 'Proposed' | 'Open' | 'Closed') {
  return {
    periodId, companyId: COMPANY, startsOn, endsOn, status, version: 2,
    preparedByUserId: PREPARER, preparedAtUtc: '2026-09-01T08:00:00Z',
    approvedByUserId: status === 'Proposed' ? null : APPROVER,
    approvedAtUtc: status === 'Proposed' ? null : '2026-09-01T09:00:00Z',
    closedByUserId: status === 'Closed' ? APPROVER : null,
    closedAtUtc: status === 'Closed' ? '2026-09-02T09:00:00Z' : null,
    isOwnProposal: false,
  }
}

const SEPTEMBER = period(PERIOD, '2026-09-01', '2026-09-30', 'Open')
const AUGUST = period(OTHER_PERIOD, '2026-08-01', '2026-08-31', 'Open')

function periodPage(items: readonly unknown[], nextCursor: string | null = null) {
  return { items, nextCursor }
}

const APPROVER_WITH_PERIODS = [ACCOUNTING_CAPABILITY.normalDraftApprove, ACCOUNTING_CAPABILITY.periodsList]

describe('normal journal approval interaction', () => {
  it('freezes the period and primary action so rapid clicks produce one command', async () => {
    let release: ((response: Response) => void) | undefined
    const pending = new Promise<Response>(resolve => { release = resolve })
    const recorded = installFetch({
      capabilities: APPROVER_WITH_PERIODS,
      respond: (url, init) => {
        if (url.includes('/accounting/periods')) return json(periodPage([SEPTEMBER]))
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}`) && (init?.method ?? 'GET') === 'GET') return json(draft())
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}/approval`) && init?.method === 'POST') return pending
        return undefined
      },
    })
    renderAt(normalDraftApprovalPath(DRAFT))
    const choice = await screen.findByLabelText('Onay dönemi')
    await waitFor(() => expect((choice as HTMLSelectElement).value).toBe(PERIOD))
    const approve = screen.getByRole('button', { name: 'Normal taslağı onayla' })

    fireEvent.click(approve)
    fireEvent.click(approve)

    await waitFor(() => expect(requestsFor(recorded, '/approval', 'POST')).toHaveLength(1))
    expect((choice as HTMLSelectElement).disabled).toBe(true)
    expect((approve as HTMLButtonElement).disabled).toBe(true)
    release?.(json(draft({ status: 'Approved', version: 2, approvedByUserId: ACTOR,
      approvedAtUtc: '2026-09-24T14:00:00Z', periodId: PERIOD, postedJournalId: DRAFT })))
    await screen.findByText('Approved')
  })

  it('retries an unknown result with the exact frozen key and body', async () => {
    let approvals = 0
    const recorded = installFetch({
      capabilities: APPROVER_WITH_PERIODS,
      respond: (url, init) => {
        if (url.includes('/accounting/periods')) return json(periodPage([SEPTEMBER]))
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
    const choice = await screen.findByLabelText('Onay dönemi')
    await waitFor(() => expect((choice as HTMLSelectElement).value).toBe(PERIOD))
    fireEvent.click(screen.getByRole('button', { name: 'Normal taslağı onayla' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı isteği yeniden gönder' }))
    await screen.findByText('Approved')

    const commands = requestsFor(recorded, '/approval', 'POST')
    expect(commands).toHaveLength(2)
    expect(commands[1].headers['Idempotency-Key']).toBe(commands[0].headers['Idempotency-Key'])
    expect(commands[1].body).toEqual(commands[0].body)
  })
})

describe('normal journal approval period (BLG-05)', () => {
  it('names open periods by their date range and preselects the one holding the accounting date', async () => {
    const recorded = installFetch({
      capabilities: APPROVER_WITH_PERIODS,
      respond: (url, init) => {
        if (url.includes('/accounting/periods')) return json(periodPage([SEPTEMBER, AUGUST]))
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}`) && (init?.method ?? 'GET') === 'GET') return json(draft())
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}/approval`) && init?.method === 'POST') {
          return json(draft({ status: 'Approved', version: 2, approvedByUserId: ACTOR,
            approvedAtUtc: '2026-09-24T14:00:00Z', periodId: PERIOD, postedJournalId: DRAFT }))
        }
        return undefined
      },
    })
    renderAt(normalDraftApprovalPath(DRAFT))
    const choice = await screen.findByLabelText('Onay dönemi') as HTMLSelectElement
    await waitFor(() => expect(choice.value).toBe(PERIOD))
    expect(screen.getByRole('option', { name: '2026-09-01 – 2026-09-30 (taslağın muhasebe tarihi bu dönemde)' })).toBeTruthy()
    expect(screen.getByRole('option', { name: '2026-08-01 – 2026-08-31' })).toBeTruthy()
    // No technical id is asked for or shown.
    expect(screen.queryByLabelText('Açık dönem kimliği')).toBeNull()
    expect(document.body.textContent).not.toContain(PERIOD)

    fireEvent.click(screen.getByRole('button', { name: 'Normal taslağı onayla' }))
    await screen.findByText('Approved')
    const [command] = requestsFor(recorded, '/approval', 'POST')
    expect(command.body).toMatchObject({ periodId: PERIOD, expectedVersion: 1 })
  })

  it('offers only open periods, and waits for a choice when none holds the accounting date', async () => {
    const recorded = installFetch({
      capabilities: APPROVER_WITH_PERIODS,
      respond: (url, init) => {
        if (url.includes('/accounting/periods')) {
          return json(periodPage([AUGUST, period(PROPOSED_PERIOD, '2026-09-01', '2026-09-30', 'Proposed'),
            period(CLOSED_PERIOD, '2026-07-01', '2026-07-31', 'Closed')]))
        }
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}`) && (init?.method ?? 'GET') === 'GET') return json(draft())
        return undefined
      },
    })
    renderAt(normalDraftApprovalPath(DRAFT))
    const choice = await screen.findByLabelText('Onay dönemi') as HTMLSelectElement
    await screen.findByRole('option', { name: '2026-08-01 – 2026-08-31' })
    expect([...choice.options].map((option) => option.value)).toEqual(['', OTHER_PERIOD])
    expect(choice.value).toBe('')
    expect((screen.getByRole('button', { name: 'Normal taslağı onayla' }) as HTMLButtonElement).disabled).toBe(true)
    expect(requestsFor(recorded, '/approval', 'POST')).toHaveLength(0)
  })

  it('says so and offers no approval without the period list, never a typed id', async () => {
    const recorded = installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftApprove],
      respond: (url, init) => {
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}`) && (init?.method ?? 'GET') === 'GET') return json(draft())
        return undefined
      },
    })
    renderAt(normalDraftApprovalPath(DRAFT))
    await screen.findByText(/muhasebe dönemlerini görme yetkisi gerekir/)
    expect(screen.queryByRole('textbox')).toBeNull()
    expect((screen.getByRole('button', { name: 'Normal taslağı onayla' }) as HTMLButtonElement).disabled).toBe(true)
    expect(requestsFor(recorded, '/accounting/periods', 'GET')).toHaveLength(0)
  })

  it('re-reads the open periods and clears the choice when the chosen period closed meanwhile', async () => {
    let periodReads = 0
    installFetch({
      capabilities: APPROVER_WITH_PERIODS,
      respond: (url, init) => {
        if (url.includes('/accounting/periods')) {
          periodReads += 1
          // The second read shows September closed by someone else; only August is still open.
          return json(periodPage(periodReads === 1 ? [SEPTEMBER, AUGUST]
            : [period(PERIOD, '2026-09-01', '2026-09-30', 'Closed'), AUGUST]))
        }
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}`) && (init?.method ?? 'GET') === 'GET') return json(draft())
        if (url.endsWith(`/normal-journal-drafts/${DRAFT}/approval`) && init?.method === 'POST') {
          return json({ code: 'accounting_period_closed' }, 422)
        }
        return undefined
      },
    })
    renderAt(normalDraftApprovalPath(DRAFT))
    const choice = await screen.findByLabelText('Onay dönemi') as HTMLSelectElement
    await waitFor(() => expect(choice.value).toBe(PERIOD))
    fireEvent.click(screen.getByRole('button', { name: 'Normal taslağı onayla' }))

    await screen.findByText(/Seçilen dönem bu sırada kapandı/)
    await waitFor(() => expect(periodReads).toBe(2))
    const fresh = await screen.findByLabelText('Onay dönemi') as HTMLSelectElement
    await waitFor(() => expect([...fresh.options].map((option) => option.value)).toEqual(['', OTHER_PERIOD]))
    expect(fresh.value).toBe('')
  })
})
