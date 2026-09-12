/**
 * The account active-state contract module: field names, the reason rule and the
 * payload the command sends. Pure — no DOM, no fetch.
 */

import { describe, expect, it } from 'vitest'
import {
  ACCOUNT_STATUS_FIELD,
  ACCOUNT_VERSION_MAX,
  CARI_CONTROL_CAPABILITY,
  REASON_MAX_LENGTH,
  accountStatusPath,
  buildAccountStatusPayload,
  canonicalReason,
  intentFor,
  reasonProblem,
  targetIsActive,
} from './accountStatusContract'
import { CAPABILITY } from '../shell/capabilities'
import { isLosslessVersion } from './wireGuards'

describe('account active-state contract — the accepted DTO, spelled once', () => {
  it('names exactly the three properties of SetCompanyPartyAccountActiveState', () => {
    expect(Object.values(ACCOUNT_STATUS_FIELD).sort()).toEqual(['expectedVersion', 'isActive', 'reason'])
  })

  it('addresses the selected company account of the party in the path, encoded', () => {
    expect(accountStatusPath('11111111-1111-1111-1111-111111111111')).toBe(
      '/counterparties/11111111-1111-1111-1111-111111111111/company-account/active-state',
    )
    // A path segment is never trusted raw — an id is user-typed in the URL.
    expect(accountStatusPath('a/b?c')).toBe('/counterparties/a%2Fb%3Fc/company-account/active-state')
  })

  it('is a CONTROL capability: absent from the shell route registry by design', () => {
    // Listing it in `shell/capabilities.ts` would make it a route pointing at nothing.
    // It is read straight from the snapshot by `hasCapability`, like the Organization
    // lane's status control, and the server publishes it or the action does not exist.
    expect(CARI_CONTROL_CAPABILITY.accountStatusUpdate).toBe('cari.accounts.status.update')
    expect(Object.values(CAPABILITY)).not.toContain(CARI_CONTROL_CAPABILITY.accountStatusUpdate)
  })

  it('derives the intent from the state the user was SHOWN, and the boolean from the intent', () => {
    expect(intentFor(true)).toBe('deactivate')
    expect(intentFor(false)).toBe('activate')
    // The command takes an EXPLICIT boolean: false deactivates, true activates.
    expect(targetIsActive('deactivate')).toBe(false)
    expect(targetIsActive('activate')).toBe(true)
  })
})

describe('reason — trimmed, then 1..1000', () => {
  it('rejects empty and whitespace-only, which the server also refuses', () => {
    expect(reasonProblem('')).toBe('required')
    expect(reasonProblem('   \n\t ')).toBe('required')
  })

  it('accepts one character after trim and exactly 1000', () => {
    expect(reasonProblem('  a  ')).toBeNull()
    expect(reasonProblem('x'.repeat(REASON_MAX_LENGTH))).toBeNull()
    expect(reasonProblem('  ' + 'x'.repeat(REASON_MAX_LENGTH) + '  ')).toBeNull()
  })

  it('refuses 1001 after trim, measured in UTF-16 units like the server', () => {
    expect(reasonProblem('x'.repeat(REASON_MAX_LENGTH + 1))).toBe('too-long')
    // 500 astral characters are 1000 code units: at the limit, not past it. The server's
    // `Reason.Trim().Length` counts the same units, so neither side special-cases this.
    const astral = '😀'.repeat(500)
    expect(astral.length).toBe(1000)
    expect(reasonProblem(astral)).toBeNull()
    expect(reasonProblem(astral + '😀')).toBe('too-long')
  })

  it('sends the canonical (trimmed) reason, so surrounding whitespace is ONE payload', () => {
    expect(canonicalReason('  ara verildi \n')).toBe('ara verildi')
    const a = buildAccountStatusPayload('deactivate', '  Çalışmaya ara verildi.  ')
    const b = buildAccountStatusPayload('deactivate', 'Çalışmaya ara verildi.')
    expect(a).toEqual({ isActive: false, reason: 'Çalışmaya ara verildi.' })
    expect(a).toEqual(b)
  })

  it('builds no payload for an unsendable reason', () => {
    expect(buildAccountStatusPayload('activate', '   ')).toBeNull()
    expect(buildAccountStatusPayload('activate', 'x'.repeat(1001))).toBeNull()
  })

  it('does not trim INSIDE the reason — only the ends are canonicalized', () => {
    expect(buildAccountStatusPayload('activate', ' a  b ')?.reason).toBe('a  b')
  })
})

describe('the fence is the account version, an int64 decimal string, never parsed', () => {
  it('accepts the full Int64 range and refuses the value past it', () => {
    expect(ACCOUNT_VERSION_MAX).toBe('9223372036854775807')
    expect(isLosslessVersion('7')).toBe(true)
    expect(isLosslessVersion(ACCOUNT_VERSION_MAX)).toBe(true)
    expect(isLosslessVersion('9223372036854775808')).toBe(false)
    // JSON numbers would round these; the guard is what keeps them off the wire as numbers.
    expect(Number('9223372036854775807')).not.toBe(9223372036854775807n)
  })
})
