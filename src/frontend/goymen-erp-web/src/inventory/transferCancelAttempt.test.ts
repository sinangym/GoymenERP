import { afterEach, describe, expect, it, vi } from 'vitest'
import { isTransferCancelRequest, recallTransferCancel, reserveTransferCancel, settleTransferCancel,
  transferCancelBinding, type TransferCancelAttempt } from './transferCancelAttempt'
import type { ShellState } from '../shell/shellState'

const company = '11111111-1111-4111-8111-111111111111'
const transfer = '22222222-2222-4222-8222-222222222222'
const attempt: TransferCancelAttempt = { binding: 'SENTETIK-server-session', companyId: company, transferId: transfer,
  key: '33333333-3333-4333-8333-333333333333', payload: { expectedTransferVersion: '2', cancelledAtUtc: '2026-11-16T12:00:00.000Z', reason: 'SENTETIK gerekçe' } }
afterEach(() => { vi.restoreAllMocks(); sessionStorage.clear() })
describe('transfer cancel unresolved attempt, SENTETIK NOT_ENV', () => {
  it('recalls exactly one attempt and refuses to overwrite its key or payload', () => {
    expect(reserveTransferCancel(attempt)).toBe(true)
    expect(recallTransferCancel(attempt.binding, company, transfer)).toEqual({ type: 'pending', attempt })
    expect(reserveTransferCancel({ ...attempt, key: transfer })).toBe(false)
    expect(reserveTransferCancel({ ...attempt, payload: { ...attempt.payload, reason: 'SENTETIK changed' } })).toBe(false)
    settleTransferCancel({ ...attempt, key: transfer })
    expect(recallTransferCancel(attempt.binding, company, transfer).type).toBe('pending')
    settleTransferCancel(attempt)
    expect(recallTransferCancel(attempt.binding, company, transfer)).toEqual({ type: 'none' })
  })
  it('cannot disclose or reuse an attempt under another actor/session, company or transfer', () => {
    expect(reserveTransferCancel(attempt)).toBe(true)
    expect(recallTransferCancel('SENTETIK-other-server-session', company, transfer)).toEqual({ type: 'none' })
    expect(recallTransferCancel(attempt.binding, transfer, transfer)).toEqual({ type: 'unavailable' })
    expect(recallTransferCancel(attempt.binding, company, company)).toEqual({ type: 'unavailable' })
  })
  it('uses the real server session binding and fails closed when that identity is absent', () => {
    const shell = { activeContext: { userId: 'SENTETIK-actor', scopeMode: 'single', companyId: company },
      sessionBinding: 'SENTETIK-real-server-session' } as Pick<ShellState, 'activeContext' | 'sessionBinding'>
    const binding = transferCancelBinding(shell, transfer)
    expect(binding).toContain('SENTETIK-real-server-session')
    expect(transferCancelBinding({ ...shell, sessionBinding: null }, transfer)).toBeNull()
    expect(transferCancelBinding({ ...shell, sessionBinding: 'SENTETIK-other-server-session' }, transfer)).not.toBe(binding)
  })
  it('does not send when storage fails or an existing attempt cannot be read safely', () => {
    const write = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('SENTETIK storage') })
    expect(reserveTransferCancel(attempt)).toBe(false)
    write.mockRestore()
    vi.spyOn(Storage.prototype, 'getItem').mockReturnValue('{SENTETIK-broken')
    expect(recallTransferCancel(attempt.binding, company, transfer)).toEqual({ type: 'unavailable' })
    expect(reserveTransferCancel(attempt)).toBe(false)
  })
  it.each(['version', 'non-utc', 'blank', 'too-long', 'control', 'extra'])('refuses a %s stored request rather than repairing it', fault => {
    expect(isTransferCancelRequest({ ...attempt.payload,
      ...(fault === 'version' ? { expectedTransferVersion: '02' } : {}),
      ...(fault === 'non-utc' ? { cancelledAtUtc: '2026-11-16T12:00:00+03:00' } : {}),
      ...(fault === 'blank' ? { reason: ' ' } : {}), ...(fault === 'too-long' ? { reason: 'x'.repeat(501) } : {}),
      ...(fault === 'control' ? { reason: 'SENTETIK\nreason' } : {}), ...(fault === 'extra' ? { permission: 'SENTETIK' } : {}) })).toBe(false)
  })
})
