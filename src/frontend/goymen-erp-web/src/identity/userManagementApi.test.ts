import { describe, expect, it } from 'vitest'
import { decodeManagedUsers } from './userManagementApi'
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
