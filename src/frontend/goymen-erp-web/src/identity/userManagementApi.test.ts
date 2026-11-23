import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeManagedUsers, sendUserManagement } from './userManagementApi'
const row = { userId: '10000000-0000-0000-0000-000000000001', displayName: 'Sentetik', userName: 'synthetic', isActive: true, version: '1', mustChangePassword: true, isLocked: false }
describe('managed users contract', () => {
  it('keeps known empty separate from malformed and rejects missing concurrency evidence', () => {
    expect(decodeManagedUsers([])).toEqual([])
    expect(decodeManagedUsers({})).toBeNull()
    expect(decodeManagedUsers([{ ...row, version: 1 }])).toBeNull()
    expect(decodeManagedUsers([{ ...row, version: '0' }])).toBeNull()
  })
  it('rejects duplicate users, unknown fields and credential material', () => {
    expect(decodeManagedUsers([row, row])).toBeNull()
    expect(decodeManagedUsers([{ ...row, passwordHash: 'forbidden' }])).toBeNull()
    expect(decodeManagedUsers([row])).toEqual([row])
  })
})
describe('user management command receipt exact key set', () => {
  afterEach(() => vi.unstubAllGlobals())
  const company = '10000000-0000-0000-0000-000000000002'
  const receipt = { id: row.userId, version: '2', status: 'Inactive', roleAssignmentId: null }
  const send = (body: unknown) => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    return sendUserManagement(company, row.userId, 'deactivate', { expectedVersion: '1' }, 'SENTETIK-key')
  }
  it('applies the exact receipt and keeps one with an unknown or missing key unknown', async () => {
    expect(await send(receipt)).toEqual({ type: 'applied', data: receipt, replayed: false })
    expect(await send({ ...receipt, passwordHash: 'forbidden' })).toEqual({ type: 'unknown', reason: 'unexpected' })
    const { status: _status, ...withoutStatus } = receipt
    expect(await send(withoutStatus)).toEqual({ type: 'unknown', reason: 'unexpected' })
    const { roleAssignmentId: _role, ...withoutRole } = receipt
    expect(await send(withoutRole)).toEqual({ type: 'unknown', reason: 'unexpected' })
  })
})
