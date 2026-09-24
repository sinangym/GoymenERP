import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, configure, fireEvent, screen, waitFor } from '@testing-library/react'
import {
  ACCOUNTING_CAPABILITY,
  ACCOUNTING_ROUTE,
  normalDraftApprovalPath,
} from './capabilities'
import {
  COMPANY_A,
  COMPANY_B,
  installFetch,
  json,
  renderAt,
} from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1'
const SOURCE = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2'
const DEBIT = 'cccccccc-cccc-4ccc-8ccc-ccccccccccc3'
const CREDIT = 'dddddddd-dddd-4ddd-8ddd-ddddddddddd4'
const USER = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeee5'
const CURRENCY = 'ffffffff-ffff-4fff-8fff-fffffffffff6'

function draft() {
  return {
    draftId: DRAFT,
    companyId: COMPANY_A,
    version: 1,
    status: 'Draft',
    preparedByUserId: USER,
    preparedAtUtc: '2026-09-24T10:00:00Z',
    approvedByUserId: null,
    approvedAtUtc: null,
    periodId: null,
    postedJournalId: null,
    sourceId: SOURCE,
    postingPurpose: 'NormalJournal',
    postingRuleVersion: '1',
    accountPlanSourceVersion: '1',
    accountPlanSourceSha256: '0'.repeat(64),
    currencyId: CURRENCY,
    transactionDate: '2026-09-24',
    accountingDate: '2026-09-24',
    valueDate: null,
    lines: [
      { lineNumber: 1, mappingKey: 'debit', side: 'Debit', accountId: DEBIT, amount: '10.00' },
      { lineNumber: 2, mappingKey: 'credit', side: 'Credit', accountId: CREDIT, amount: '10.00' },
    ],
  }
}

function fillCreateForm() {
  fireEvent.change(screen.getByLabelText(/Kaynak kimliği/i), { target: { value: SOURCE } })
  fireEvent.change(screen.getByLabelText(/İşlem tarihi/i), { target: { value: '2026-09-24' } })
  fireEvent.change(screen.getByLabelText(/Muhasebe tarihi/i), { target: { value: '2026-09-24' } })
  const accounts = screen.getAllByLabelText(/Hesap kimliği/i)
  const amounts = screen.getAllByLabelText(/Tutar/i)
  fireEvent.change(accounts[0], { target: { value: DEBIT } })
  fireEvent.change(accounts[1], { target: { value: CREDIT } })
  fireEvent.change(amounts[0], { target: { value: '10.00' } })
  fireEvent.change(amounts[1], { target: { value: '10.00' } })
}

afterEach(() => {
  configure({ reactStrictMode: false })
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('normal journal command security and company fences', () => {
  it('drops a late company-A create result after company B becomes the committed context', async () => {
    installFetch({ capabilities: [ACCOUNTING_CAPABILITY.normalDraftCreate] })
    const normalFetch = globalThis.fetch
    let release: (() => void) | null = null
    let submittedCompany: string | null = null
    const delayed = new Promise<Response>((resolve) => {
      release = () => resolve(json({ status: 409, code: 'accounting_normal_journal_draft_content_conflict' }, 409))
    })
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url.endsWith('/accounting/normal-journal-drafts') && init?.method === 'POST') {
        submittedCompany = new Headers(init.headers).get('X-Company-Id')
        return delayed
      }
      return normalFetch(input, init)
    }))

    renderAt(ACCOUNTING_ROUTE.normalDraftCreate)
    await screen.findByRole('button', { name: /kaydet/i })
    fillCreateForm()
    fireEvent.click(screen.getByRole('button', { name: /kaydet/i }))
    await waitFor(() => expect(submittedCompany).toBe(COMPANY_A))

    fireEvent.change(screen.getByRole('combobox', { name: 'Şirket' }), { target: { value: COMPANY_B } })
    fireEvent.click(await screen.findByRole('button', { name: 'Değişiklikleri bırak' }))
    await waitFor(() => expect((screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement).value).toBe(COMPANY_B))
    expect((screen.getByLabelText(/Kaynak kimliği/i) as HTMLInputElement).value).toBe('')

    await act(async () => {
      release?.()
      await delayed
    })
    expect((screen.getByLabelText(/Kaynak kimliği/i) as HTMLInputElement).value).toBe('')
    expect(screen.queryByText(/accounting_normal_journal_draft_content_conflict/i)).toBeNull()
  })

  it('reports a create 401 to the shell auth boundary', async () => {
    installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftCreate],
      respond: (url, init) => url.endsWith('/accounting/normal-journal-drafts') && init?.method === 'POST'
        ? json({ status: 401 }, 401)
        : undefined,
    })
    renderAt(ACCOUNTING_ROUTE.normalDraftCreate)
    await screen.findByRole('button', { name: /kaydet/i })
    fillCreateForm()
    fireEvent.click(screen.getByRole('button', { name: /kaydet/i }))
    expect(await screen.findByText(/Oturum sonlandı/i)).toBeTruthy()
  })

  it('settles a create result after the StrictMode setup-cleanup-setup cycle', async () => {
    configure({ reactStrictMode: true })
    installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftCreate],
      respond: (url, init) => url.endsWith('/accounting/normal-journal-drafts') && init?.method === 'POST'
        ? json({ status: 503 }, 503)
        : undefined,
    })
    renderAt(ACCOUNTING_ROUTE.normalDraftCreate)
    await screen.findByRole('button', { name: /kaydet/i })
    fillCreateForm()
    fireEvent.click(screen.getByRole('button', { name: /kaydet/i }))
    expect(await screen.findByRole('button', { name: /Aynı istek.*yeniden gönder/i })).toBeTruthy()
  })

  it('reports an approval 403 and suppresses the approval capability', async () => {
    installFetch({
      capabilities: [ACCOUNTING_CAPABILITY.normalDraftDetail, ACCOUNTING_CAPABILITY.normalDraftApprove],
      respond: (url, init) => {
        if (url.endsWith(`/accounting/normal-journal-drafts/${DRAFT}`) && (!init?.method || init.method === 'GET')) return json(draft())
        if (url.endsWith(`/accounting/normal-journal-drafts/${DRAFT}/approval`) && init?.method === 'POST') return json({ status: 403 }, 403)
        return undefined
      },
    })
    renderAt(normalDraftApprovalPath(DRAFT))
    const approve = await screen.findByRole('button', { name: /onayla/i })
    fireEvent.change(screen.getByLabelText(/Dönem kimliği/i), { target: { value: '99999999-9999-4999-8999-999999999999' } })
    fireEvent.click(approve)
    await waitFor(() => expect(screen.queryByRole('button', { name: /onayla/i })).toBeNull())
  })
})
