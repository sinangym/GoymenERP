/**
 * Form rules for C-02 create (spec §3.1, §4.1) and CARI-LINK (§7.3), without a
 * DOM. G-002 check 4: exactly kind/legalName/tradeName/roles, numeric enums,
 * trim-then-limit 250/200, at least one known role. G-002 check 3: the link
 * payload is roles only. G-002 check 2: a selection that is not in the fresh
 * candidate page is not a selection.
 */

import { describe, expect, it } from 'vitest'
import {
  EMPTY_CREATE_FORM,
  LEGAL_NAME_MAX,
  TRADE_NAME_MAX,
  isCreateFormPristine,
  validateCreateForm,
} from './createForm'
import { validateLinkForm } from './linkForm'
import { PARTY_KIND, PARTY_ROLE_FLAG, type PartyKind } from './contract'
import { CARI_CREATE_TEXT, CARI_LINK_TEXT } from './cariCommandText'

const VALID = {
  kind: PARTY_KIND.LegalEntity,
  legalName: 'Örnek Metal Sanayi ve Ticaret A.Ş.',
  tradeName: 'Örnek Metal',
  roles: PARTY_ROLE_FLAG.Customer | PARTY_ROLE_FLAG.Supplier,
} as const

describe('validateCreateForm — C-02A', () => {
  it('produces exactly the four wire fields, numeric kind and roles, trimmed names', () => {
    const result = validateCreateForm({
      ...VALID,
      legalName: `  ${VALID.legalName}  `,
      tradeName: '\t Örnek Metal \n',
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(Object.keys(result.payload)).toEqual(['kind', 'legalName', 'tradeName', 'roles'])
    expect(result.payload).toEqual({ kind: 1, legalName: VALID.legalName, tradeName: 'Örnek Metal', roles: 3 })
    expect(typeof result.payload.kind).toBe('number')
    expect(typeof result.payload.roles).toBe('number')
  })

  it('an empty or whitespace-only trade name is sent as null, never as an empty string', () => {
    const blank = validateCreateForm({ ...VALID, tradeName: '   ' })
    expect(blank.ok && blank.payload.tradeName).toBeNull()
    const none = validateCreateForm({ ...VALID, tradeName: '' })
    expect(none.ok && none.payload.tradeName).toBeNull()
  })

  it('the empty form fails on kind, legal name and roles — with the localized messages', () => {
    const result = validateCreateForm(EMPTY_CREATE_FORM)
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.errors).toEqual({
      kind: CARI_CREATE_TEXT.validation.kind,
      legalName: CARI_CREATE_TEXT.validation.legalNameRequired,
      roles: CARI_CREATE_TEXT.validation.roles,
    })
    expect(isCreateFormPristine(EMPTY_CREATE_FORM)).toBe(true)
    expect(isCreateFormPristine({ ...EMPTY_CREATE_FORM, legalName: ' ' })).toBe(false)
  })

  it('legal name: 1–250 AFTER trim; trade name ≤ 200 AFTER trim', () => {
    const okName = validateCreateForm({ ...VALID, legalName: `  ${'a'.repeat(LEGAL_NAME_MAX)}  ` })
    expect(okName.ok).toBe(true)
    const longName = validateCreateForm({ ...VALID, legalName: 'a'.repeat(LEGAL_NAME_MAX + 1) })
    expect(!longName.ok && longName.errors.legalName).toBe(CARI_CREATE_TEXT.validation.legalNameTooLong)
    const spaces = validateCreateForm({ ...VALID, legalName: '   ' })
    expect(!spaces.ok && spaces.errors.legalName).toBe(CARI_CREATE_TEXT.validation.legalNameRequired)

    const okTrade = validateCreateForm({ ...VALID, tradeName: ` ${'b'.repeat(TRADE_NAME_MAX)} ` })
    expect(okTrade.ok).toBe(true)
    const longTrade = validateCreateForm({ ...VALID, tradeName: 'b'.repeat(TRADE_NAME_MAX + 1) })
    expect(!longTrade.ok && longTrade.errors.tradeName).toBe(CARI_CREATE_TEXT.validation.tradeNameTooLong)
  })

  it('roles: at least one known bit (1–15), nothing outside the flag range', () => {
    for (const roles of [1, 2, 4, 8, 15, 10]) {
      expect(validateCreateForm({ ...VALID, roles }).ok, `roles=${roles}`).toBe(true)
    }
    for (const roles of [0, 16, 17, 32, -1]) {
      const result = validateCreateForm({ ...VALID, roles })
      expect(!result.ok && result.errors.roles, `roles=${roles}`).toBe(CARI_CREATE_TEXT.validation.roles)
    }
  })

  it('kind must be one of the two numeric enum values', () => {
    expect(validateCreateForm({ ...VALID, kind: PARTY_KIND.NaturalPerson }).ok).toBe(true)
    const unknownKind = validateCreateForm({ ...VALID, kind: 3 as PartyKind })
    expect(!unknownKind.ok && unknownKind.errors.kind).toBe(CARI_CREATE_TEXT.validation.kind)
    const noKind = validateCreateForm({ ...VALID, kind: '' })
    expect(!noKind.ok && noKind.errors.kind).toBe(CARI_CREATE_TEXT.validation.kind)
  })
})

describe('validateLinkForm — CARI-LINK', () => {
  it('sends roles and nothing else', () => {
    const result = validateLinkForm({ hasFreshSelection: true, roles: PARTY_ROLE_FLAG.Supplier | PARTY_ROLE_FLAG.Service })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(Object.keys(result.payload)).toEqual(['roles'])
    expect(result.payload.roles).toBe(10)
  })

  it('a hint or a stale row is not a selection; roles must carry a known bit', () => {
    const noSelection = validateLinkForm({ hasFreshSelection: false, roles: 2 })
    expect(!noSelection.ok && noSelection.errors).toEqual({ selection: CARI_LINK_TEXT.validation.selection })
    const noRoles = validateLinkForm({ hasFreshSelection: true, roles: 0 })
    expect(!noRoles.ok && noRoles.errors).toEqual({ roles: CARI_LINK_TEXT.validation.roles })
    const outOfRange = validateLinkForm({ hasFreshSelection: true, roles: 16 })
    expect(!outOfRange.ok && outOfRange.errors.roles).toBe(CARI_LINK_TEXT.validation.roles)
    const both = validateLinkForm({ hasFreshSelection: false, roles: 0 })
    expect(!both.ok && Object.keys(both.errors)).toEqual(['selection', 'roles'])
  })
})
