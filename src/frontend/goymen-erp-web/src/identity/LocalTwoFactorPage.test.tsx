import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocalTwoFactorPage } from './LocalTwoFactorPage'
import { TWO_FACTOR_TEXT as T } from './twoFactorText'

const fakes = vi.hoisted(() => ({ change: vi.fn(), prepare: vi.fn(), refresh: vi.fn(), report: vi.fn() }))
vi.mock('../shell/shellContext', () => ({ useShellState: () => ({ activeContext: { companyId: 'synthetic-company', scopeMode: 'single' }, routeEntryGeneration: 1 }) }))
vi.mock('../shell/shellState', () => ({ contextKey: () => ({}), contextKeyString: () => 'synthetic-user:synthetic-company' }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({}), report: fakes.report }) }))
vi.mock('../settings/SalesPolicyParts', async (original) => ({ ...await original<typeof import('../settings/SalesPolicyParts')>(),
  useBoundaryRead: () => ({ isPending: false, data: { type: 'ok-single', data: { credentialVersion: '5', enabled: true, recoveryCodesRemaining: 7 } }, refetch: fakes.refresh }),
}))
vi.mock('./twoFactorApi', () => ({ readTwoFactor: vi.fn(), prepareTwoFactor: fakes.prepare, changeTwoFactor: fakes.change }))

beforeEach(() => { vi.clearAllMocks(); fakes.change.mockReset(); fakes.prepare.mockReset() })
afterEach(() => { cleanup(); vi.restoreAllMocks() })

describe('LocalTwoFactorPage optional self service', () => {
  it('freezes an unknown attempt and retries the same body and key without persisting secrets', async () => {
    const storage = vi.spyOn(Storage.prototype, 'setItem')
    fakes.change.mockResolvedValueOnce({ type: 'unknown' }).mockResolvedValueOnce({ type: 'applied', replayed: true,
      data: { credentialVersion: '6', enabled: true, recoveryCodes: null } })
    render(<LocalTwoFactorPage />)
    const actor = userEvent.setup()
    await actor.type(screen.getByLabelText(T.password), 'SyntheticOnly!Password')
    await actor.type(screen.getByLabelText(T.code), '123456')
    await actor.click(screen.getByRole('button', { name: T.regenerate }))
    expect(await screen.findByText(T.unknown)).toBeTruthy()
    expect(screen.getByLabelText(T.password).matches(':disabled')).toBe(true)
    expect(screen.getByLabelText(T.code).matches(':disabled')).toBe(true)
    await actor.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(fakes.change).toHaveBeenCalledTimes(2))
    expect(fakes.change.mock.calls[1]).toEqual(fakes.change.mock.calls[0])
    expect(fakes.change.mock.calls[0][2]).toEqual({ expectedVersion: '5', currentPassword: 'SyntheticOnly!Password', code: '123456', enrollmentToken: null })
    expect(await screen.findByText(T.replay)).toBeTruthy()
    expect((screen.getByLabelText(T.password) as HTMLInputElement).value).toBe('')
    expect((screen.getByLabelText(T.code) as HTMLInputElement).value).toBe('')
    expect(screen.queryByRole('heading', { name: T.codes })).toBeNull()
    expect(storage).not.toHaveBeenCalled()
  })

  it('reports a confirmed authorization denial and clears the supplied proof without claiming success', async () => {
    fakes.change.mockResolvedValueOnce({ type: 'refused', status: 403, code: null, companyId: null, groupCode: null })
    render(<LocalTwoFactorPage />)
    const actor = userEvent.setup()
    await actor.type(screen.getByLabelText(T.password), 'SyntheticOnly!Password')
    await actor.type(screen.getByLabelText(T.code), '123456')
    await actor.click(screen.getByRole('button', { name: T.disable }))
    expect(await screen.findByText(T.refused)).toBeTruthy()
    expect(fakes.report).toHaveBeenCalledWith(403, {})
    expect((screen.getByLabelText(T.password) as HTMLInputElement).value).toBe('')
    expect(screen.queryByText(T.saved)).toBeNull()
    expect(fakes.refresh).not.toHaveBeenCalled()
  })
})
