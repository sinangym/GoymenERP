/**
 * The §7 rule for a counterparty with no commercial role, tested where it is
 * decided rather than in each screen that consumes it.
 */

import { describe, expect, it } from 'vitest'
import {
  NO_COMMERCIAL_ROLE_DETAIL,
  NO_COMMERCIAL_ROLE_LIST,
  ROLE_LABEL,
  rolesText,
} from './roleText'
import { PARTY_ROLE_FLAG } from './contract'

describe('role text (ILK_DILIM §7)', () => {
  it('names the roles a legacy flag integer carries, in declaration order', () => {
    expect(rolesText(PARTY_ROLE_FLAG.Customer, NO_COMMERCIAL_ROLE_LIST)).toBe('Müşteri')
    expect(
      rolesText(PARTY_ROLE_FLAG.Supplier | PARTY_ROLE_FLAG.Customer, NO_COMMERCIAL_ROLE_LIST),
    ).toBe('Müşteri, Tedarikçi')
    expect(rolesText(15, NO_COMMERCIAL_ROLE_LIST)).toBe('Müşteri, Tedarikçi, Fason, Hizmet')
  })

  it('states the absence instead of leaving it blank', () => {
    // An empty cell reads as "unknown", which is the reading §7 forbids just as
    // much as "Eksik" does.
    expect(rolesText(0, NO_COMMERCIAL_ROLE_LIST)).toBe('Ticari rol yok')
    expect(rolesText(0, NO_COMMERCIAL_ROLE_DETAIL)).toBe('Ticari rol yok — bu alan zorunlu değil')
  })

  it('never calls a role-less record incomplete, unauthorized or passive', () => {
    const forbidden = /eksik|belirtilmemiş|sınıflandırılmamış|yetkisiz|pasif|tanımsız/i
    expect(NO_COMMERCIAL_ROLE_LIST).not.toMatch(forbidden)
    expect(NO_COMMERCIAL_ROLE_DETAIL).not.toMatch(forbidden)
  })

  it('covers every declared role flag, so a new bit cannot render as blank', () => {
    for (const name of Object.keys(PARTY_ROLE_FLAG) as (keyof typeof PARTY_ROLE_FLAG)[]) {
      expect(ROLE_LABEL[name]).toBeTruthy()
    }
  })
})
