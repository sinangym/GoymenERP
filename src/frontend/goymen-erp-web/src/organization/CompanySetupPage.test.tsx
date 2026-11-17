import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CompanySetupPage, restoreSetupAttempt } from './CompanySetupPage'
import { fetchIdentity } from '../shell/api'
import { readAdminCandidates, readPendingSetups, readSetupCapabilities, sendCompanySetup } from './companySetupApi'
import { COMPANY_SETUP_TEXT as T } from './companySetupText'

const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({})), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ sessionEpoch: 1, requestFenceGeneration: 0 }) }))
vi.mock('../shell/api', () => ({ fetchIdentity: vi.fn() }))
vi.mock('./companySetupApi', async original => {
  const source = await original<typeof import('./companySetupApi')>()
  return { ...source, readSetupCapabilities: vi.fn(), readPendingSetups: vi.fn(), readAdminCandidates: vi.fn(), sendCompanySetup: vi.fn() }
})
const companyId = 'aaaaaaaa-0000-0000-0000-000000000001'
const userId = 'bbbbbbbb-0000-0000-0000-000000000002'
const identity = { userId: 'cccccccc-0000-0000-0000-000000000003', displayName: 'SENTETIK yetkili', email: null,
  allowedCompanyIds: [], authenticationSessionBinding: 'SENTETIK-session', mustChangePassword: false, localCredentialVersion: null }
beforeEach(() => {
  sessionStorage.clear(); vi.clearAllMocks()
  vi.mocked(fetchIdentity).mockResolvedValue({ type: 'ok-single', data: identity })
  vi.mocked(readSetupCapabilities).mockResolvedValue({ kind: 'ready', data: { canOpenCompany: false, canAssignFirstAdmin: true } })
  vi.mocked(readPendingSetups).mockResolvedValue({ kind: 'ready', data: [{ companyId, code: 'SENTETIK', legalName: 'SENTETIK şirket', lifecycleState: 'SetupPending', version: 1 }] })
  vi.mocked(readAdminCandidates).mockResolvedValue({ kind: 'ready', data: [{ userId, displayName: 'SENTETIK seçilen kişi' }] })
})
afterEach(() => { cleanup(); sessionStorage.clear() })
const mount = () => render(<MemoryRouter><CompanySetupPage /></MemoryRouter>)
describe('explicit first Admin flow', () => {
  it('never selects the company or opener automatically', async () => {
    mount()
    const company = await screen.findByLabelText(T.company)
    expect((company as HTMLSelectElement).value).toBe('')
    expect((screen.getByLabelText(T.user) as HTMLSelectElement).value).toBe('')
    expect(sendCompanySetup).not.toHaveBeenCalled()
    fireEvent.change(company, { target: { value: companyId } })
    expect((screen.getByLabelText(T.user) as HTMLSelectElement).value).toBe('')
  })
  it('retains the selected target, payload and key after an unknown outcome', async () => {
    vi.mocked(sendCompanySetup).mockResolvedValueOnce({ type: 'unknown', state: { kind: 'service-unavailable' } })
      .mockResolvedValueOnce({ type: 'applied', replayed: true })
    mount()
    fireEvent.change(await screen.findByLabelText(T.company), { target: { value: companyId } })
    fireEvent.change(screen.getByLabelText(T.user), { target: { value: userId } })
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK açık seçim' } })
    fireEvent.click(screen.getByRole('button', { name: T.assign }))
    await waitFor(() => expect(sendCompanySetup).toHaveBeenCalledTimes(1))
    fireEvent.click(await screen.findByRole('button', { name: T.retry }))
    await waitFor(() => expect(sendCompanySetup).toHaveBeenCalledTimes(2))
    expect(vi.mocked(sendCompanySetup).mock.calls[1]).toEqual(vi.mocked(sendCompanySetup).mock.calls[0])
    expect(vi.mocked(sendCompanySetup).mock.calls[0]?.[2]).toEqual({ userId, expectedCompanyVersion: 1, reason: 'SENTETIK açık seçim' })
  })
  it.each(['forbidden', 'not-found', 'offline', 'service-unavailable', 'malformed-response'] as const)('renders %s distinctly without mounting a mutation form', async kind => {
    vi.mocked(readSetupCapabilities).mockResolvedValue({ kind })
    mount()
    await waitFor(() => expect(document.querySelector(`[data-state="${kind}"]`)).not.toBeNull())
    expect(screen.queryByRole('button', { name: T.assign })).toBeNull()
    expect(sendCompanySetup).not.toHaveBeenCalled()
  })
  it('rejects a list-shaped identity before reading group eligibility or rendering a mutation', async () => {
    vi.mocked(fetchIdentity).mockResolvedValue({ type: 'ok', data: [identity], filterActive: false })
    mount()
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(readSetupCapabilities).not.toHaveBeenCalled()
    expect(sendCompanySetup).not.toHaveBeenCalled()
  })
  it('rejects malformed stored commands instead of inventing a replacement target or key', () => {
    expect(restoreSetupAttempt('{')).toBeNull()
    expect(restoreSetupAttempt(JSON.stringify({ kind: 'assign', companyId, key: userId, request: { userId, expectedCompanyVersion: 0, reason: 'SENTETIK' } }))).toBeNull()
  })
})
