// @vitest-environment jsdom
// SENTETIK / NOT_ENV: component evidence over mocked transport, not SQL or browser acceptance.
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { BankStatementPage } from './BankStatementPage'
import { BANK_STATEMENT_TEXT as T } from './BankStatementText'
import { COMPANY_A, renderFinance, shellFor } from './testHarness'
import { confirmStatementMatch, fetchStatementDetail, fetchStatementImports } from './BankStatementHttp'
import { decodeDetail, decodeImport } from './BankStatementContract'
import { detailBody, parsedImport } from './bankStatementFixtures'

vi.mock('./BankStatementHttp', () => ({ fetchStatementImports: vi.fn(), fetchStatementDetail: vi.fn(), reserveStatement: vi.fn(),
  parseStatement: vi.fn(), confirmStatementMatch: vi.fn(), reverseStatementMatch: vi.fn() }))
vi.mock('./creditMovementApi', () => ({ fetchCreditBanks: vi.fn(async () => ({ type: 'ok-single', data: { companyId: '', items: [] } })) }))
vi.mock('../documents/DocumentsPanel', () => ({ DocumentsPanel: () => null }))

const READ = 'finance.bank-statements.read', MATCH = 'finance.bank-statements.match'
afterEach(() => { cleanup(); vi.clearAllMocks() })

function withCompany<T>(value: T): T { return JSON.parse(JSON.stringify(value).replaceAll('11111111-1111-4111-8111-111111111111', COMPANY_A)) as T }

describe('bank statement screen', () => {
  it('is forbidden without the read capability and calls nothing', () => {
    renderFinance(<BankStatementPage />, { port: null, shell: shellFor(COMPANY_A), router: true })
    expect(fetchStatementImports).not.toHaveBeenCalled()
  })

  it('shows the empty state and a 503 with retry', async () => {
    vi.mocked(fetchStatementImports).mockResolvedValueOnce({ type: 'ok-single', data: [] })
    renderFinance(<BankStatementPage />, { port: null, shell: shellFor(COMPANY_A, 1, [READ]), router: true })
    expect(await screen.findByText(T.none)).toBeTruthy()
    cleanup()
    vi.mocked(fetchStatementImports).mockResolvedValueOnce({ type: 'http', status: 503 })
    renderFinance(<BankStatementPage />, { port: null, shell: shellFor(COMPANY_A, 1, [READ]), router: true })
    await waitFor(() => expect(screen.queryByText(T.none)).toBeNull())
  })

  it('opens an import, shows a suggestion and a manual line, and confirms only with the match capability', async () => {
    const header = decodeImport(withCompany(parsedImport), COMPANY_A)!
    const detail = decodeDetail(withCompany(detailBody), COMPANY_A, header.id)!
    vi.mocked(fetchStatementImports).mockResolvedValue({ type: 'ok-single', data: [header] })
    vi.mocked(fetchStatementDetail).mockResolvedValue({ type: 'ok-single', data: detail })
    vi.mocked(confirmStatementMatch).mockResolvedValueOnce({ kind: 'unknown' }).mockResolvedValueOnce({ kind: 'answered',
      result: { outcome: 'Succeeded', code: null, replayed: true, resource: null } as never })
    renderFinance(<BankStatementPage />, { port: null, shell: shellFor(COMPANY_A, 1, [READ, MATCH]), router: true })
    fireEvent.click(await screen.findByRole('button', { name: T.open }))
    expect(await screen.findByText(T.reasons.NoExactCandidate, { exact: false })).toBeTruthy()
    fireEvent.click(await screen.findByRole('button', { name: T.confirm }))
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.confirmed)
    const keys = vi.mocked(confirmStatementMatch).mock.calls.map((call) => call[2])
    expect(keys[0]).toBe(keys[1])
    expect(vi.mocked(confirmStatementMatch).mock.calls[0][3]).toEqual(vi.mocked(confirmStatementMatch).mock.calls[1][3])
  })

  it('offers no confirm button without the match capability', async () => {
    const header = decodeImport(withCompany(parsedImport), COMPANY_A)!
    vi.mocked(fetchStatementImports).mockResolvedValue({ type: 'ok-single', data: [header] })
    vi.mocked(fetchStatementDetail).mockResolvedValue({ type: 'ok-single', data: decodeDetail(withCompany(detailBody), COMPANY_A, header.id)! })
    renderFinance(<BankStatementPage />, { port: null, shell: shellFor(COMPANY_A, 1, [READ]), router: true })
    fireEvent.click(await screen.findByRole('button', { name: T.open }))
    await screen.findByText(T.reasons.NoExactCandidate, { exact: false })
    expect(screen.queryByRole('button', { name: T.confirm })).toBeNull()
  })
})
