/**
 * The account-scoped contact/address edit semantics (CHATGPT-BUSINESS handoff,
 * 12 September: issue #66 comment `5642102538`, field dictionary #232 comment
 * `5642117532`). Covers the four wire rules, the basic/critical boundary, and the
 * value checks that must not quietly change what the user typed.
 *
 * The rules below are TEST-LOCAL on purpose. Backend owns the real property names
 * and the one authoritative length per field in its OpenAPI; a table here would be a
 * second truth. What is under test is the semantics, which do not depend on either.
 */

import { describe, expect, it } from 'vitest'
import {
  buildBasicPatch,
  buildCollectionPatch,
  canSubmitBasic,
  checkField,
  isEmailShaped,
  isRowEmpty,
  isSupportedWebUrl,
  normalizePhone,
  trimToNull,
  type EditedRow,
  type FieldRule,
} from './contactEditModel'

const optionalText = (maxLength = 250): FieldRule => ({
  scope: 'basic',
  required: false,
  maxLength,
  kind: 'text',
})
const requiredText = (maxLength = 250): FieldRule => ({
  scope: 'basic',
  required: true,
  maxLength,
  kind: 'text',
})

/** Shaped like the operational contact block: a phone, an e-mail, a web address. */
const CONTACT_RULES: Record<string, FieldRule> = {
  phone: { scope: 'basic', required: false, maxLength: 32, kind: 'phone' },
  phoneExtension: { scope: 'basic', required: false, maxLength: 10, kind: 'text' },
  email: { scope: 'basic', required: false, maxLength: 254, kind: 'email' },
  website: { scope: 'basic', required: false, maxLength: 2048, kind: 'url' },
  // Critical by CARI-DEC-004: proposal + single approver, never the basic save.
  legalName: { scope: 'critical', required: true, maxLength: 250, kind: 'text' },
}

/** Shaped like one operational address row. */
const ADDRESS_RULES: Record<string, FieldRule> = {
  usage: requiredText(60),
  country: requiredText(60),
  line1: requiredText(250),
  line2: optionalText(250),
  city: optionalText(100),
  district: optionalText(100),
  postalCode: optionalText(20),
}

describe('value checks — the client never improves on what the user typed', () => {
  it('keeps a phone exactly as typed, including the leading +, the 0 and the extension', () => {
    // Every one of these would be "helpfully" rewritten by a normalizer that assumes a
    // country or an operator, and each rewrite changes who gets called.
    expect(normalizePhone('  +90 (212) 555 00 00 / 123  ')).toBe('+90 (212) 555 00 00 / 123')
    expect(normalizePhone('0212 555 00 00')).toBe('0212 555 00 00')
    expect(normalizePhone('+905551112233')).toBe('+905551112233')
    expect(normalizePhone('555 00 00 dahili 4')).toBe('555 00 00 dahili 4')
    // Only the surrounding whitespace goes; the inner spacing is the user's data.
    expect(normalizePhone(' 0 212  555 ')).toBe('0 212  555')
  })

  it('turns an empty or blank string into null after trimming (rule 3)', () => {
    expect(trimToNull('')).toBeNull()
    expect(trimToNull('   ')).toBeNull()
    expect(trimToNull('\t\n ')).toBeNull()
    expect(trimToNull(' Merkez ')).toBe('Merkez')
    expect(normalizePhone('   ')).toBeNull()
  })

  it('checks the SHAPE of an e-mail and claims nothing more', () => {
    expect(isEmailShaped('operasyon@example.test')).toBe(true)
    expect(isEmailShaped('a.b-c+d@alt.example.test')).toBe(true)
    // No dotted domain, whitespace, empty local part, two @, dangling dots.
    expect(isEmailShaped('operasyon@example')).toBe(false)
    expect(isEmailShaped('ope rasyon@example.test')).toBe(false)
    expect(isEmailShaped('@example.test')).toBe(false)
    expect(isEmailShaped('a@@example.test')).toBe(false)
    expect(isEmailShaped('a@.example.test')).toBe(false)
    expect(isEmailShaped('a@example.test.')).toBe(false)
    expect(isEmailShaped('a@example..test')).toBe(false)
    expect(isEmailShaped('')).toBe(false)
  })

  it('accepts only http and https for a web address', () => {
    expect(isSupportedWebUrl('https://example.test/iletisim')).toBe(true)
    expect(isSupportedWebUrl('http://example.test')).toBe(true)
    expect(isSupportedWebUrl('ftp://example.test')).toBe(false)
    expect(isSupportedWebUrl('javascript:alert(1)')).toBe(false)
    expect(isSupportedWebUrl('data:text/html,x')).toBe(false)
    // Not absolute: a relative string is not a web address we can store as one.
    expect(isSupportedWebUrl('www.example.test')).toBe(false)
    expect(isSupportedWebUrl('')).toBe(false)
  })

  it('refuses an over-long value instead of truncating it', () => {
    const rule = optionalText(20)
    const tooLong = 'x'.repeat(21)
    expect(checkField(rule, tooLong)).toEqual({ ok: false, problem: 'too-long' })
    // The boundary itself is fine, and it is measured after trimming.
    expect(checkField(rule, 'x'.repeat(20))).toEqual({ ok: true, value: 'x'.repeat(20) })
    expect(checkField(rule, `  ${'x'.repeat(20)}  `)).toEqual({ ok: true, value: 'x'.repeat(20) })
  })

  it('distinguishes an empty required field from an empty optional one', () => {
    expect(checkField(requiredText(), '   ')).toEqual({ ok: false, problem: 'required' })
    expect(checkField(optionalText(), '   ')).toEqual({ ok: true, value: null })
    expect(checkField(CONTACT_RULES.email, 'yanlis-eposta')).toEqual({ ok: false, problem: 'email-format' })
    expect(checkField(CONTACT_RULES.website, 'ftp://x.test')).toEqual({ ok: false, problem: 'url-unsupported' })
  })

  it('knows an untouched row from an invalid one', () => {
    expect(isRowEmpty({ usage: '', country: '  ', line1: '' })).toBe(true)
    expect(isRowEmpty({ usage: '', country: 'TR', line1: '' })).toBe(false)
  })
})

describe('buildBasicPatch — only what changed, and only what this save owns', () => {
  const original = { phone: '0212 555 00 00', email: 'eski@example.test', website: null, phoneExtension: null }

  it('omits an untouched field even when the user retyped it with stray whitespace (rule 1)', () => {
    const result = buildBasicPatch(CONTACT_RULES, original, {
      phone: '  0212 555 00 00  ',
      email: 'eski@example.test',
    })
    expect(result.patch).toEqual({})
    expect(result.changed).toEqual([])
    expect(result.problems).toEqual({})
    // Nothing changed, so there is nothing to save and the button stays closed.
    expect(canSubmitBasic(result)).toBe(false)
  })

  it('sends the corrected value, and an explicit null to clear an optional field (rules 2, 3)', () => {
    const result = buildBasicPatch(CONTACT_RULES, original, {
      phone: '+90 212 555 00 01',
      email: '   ',
    })
    expect(result.patch).toEqual({ phone: '+90 212 555 00 01', email: null })
    expect(result.changed).toEqual(['phone', 'email'])
    expect(canSubmitBasic(result)).toBe(true)
  })

  it('refuses to clear a field the contract requires', () => {
    const rules: Record<string, FieldRule> = { usage: requiredText(60) }
    const result = buildBasicPatch(rules, { usage: 'Fatura' }, { usage: '' })
    expect(result.problems).toEqual({ usage: 'required' })
    expect(result.patch).toEqual({})
    expect(canSubmitBasic(result)).toBe(false)
  })

  it('keeps a critical field out of the basic payload entirely', () => {
    /**
     * The user may well edit the legal name in the same sitting — that is legitimate —
     * but it belongs to the proposal command with its own approver. If it rode along
     * here, one "saved" message would report an approval that never happened.
     */
    const result = buildBasicPatch(CONTACT_RULES, { ...original, legalName: 'Eski Unvan A.Ş.' }, {
      phone: '+90 212 555 00 01',
      legalName: 'Yeni Unvan A.Ş.',
    })
    expect(result.patch).toEqual({ phone: '+90 212 555 00 01' })
    expect(result.problems).toEqual({ legalName: 'critical-scope' })
    expect(result.changed).toEqual(['phone'])
    // A scope violation blocks this save rather than sending half of it silently.
    expect(canSubmitBasic(result)).toBe(false)
  })

  it('refuses a key it has no rule for, rather than passing it through', () => {
    const result = buildBasicPatch(CONTACT_RULES, original, { balance: '0', phone: '0212 555 00 00' })
    expect(result.patch).toEqual({})
    expect(result.problems).toEqual({ balance: 'unknown-field' })
  })

  it('an invalid value blocks the save and never reaches the patch', () => {
    const result = buildBasicPatch(CONTACT_RULES, original, {
      phone: '+90 212 555 00 01',
      email: 'gecersiz',
    })
    // The valid change is still recognised — the form is not thrown away — but the
    // section cannot be saved while one of its fields is wrong.
    expect(result.patch).toEqual({ phone: '+90 212 555 00 01' })
    expect(result.problems).toEqual({ email: 'email-format' })
    expect(canSubmitBasic(result)).toBe(false)
  })
})

describe('buildCollectionPatch — a row that left the editor is not a deletion (rule 4)', () => {
  const ADDR_1 = 'a1111111-1111-1111-1111-111111111111'
  const ADDR_2 = 'a2222222-2222-2222-2222-222222222222'
  const originals = {
    [ADDR_1]: { usage: 'Fatura', country: 'TR', line1: 'Eski Cadde 1', line2: null, city: 'İstanbul', district: 'Şişli', postalCode: null },
    [ADDR_2]: { usage: 'Teslim', country: 'TR', line1: 'Depo Yolu 2', line2: null, city: 'Kocaeli', district: 'Gebze', postalCode: null },
  }

  it('says nothing about a row it never loaded, and reports it as retained', () => {
    /**
     * The destructive version of this editor sends "the rows I still have" and deletes
     * everything it never saw. Here the second address is not mentioned at all: no
     * element is emitted for it and the screen is told, in so many words, that the
     * server still holds it.
     */
    const rows: EditedRow[] = [
      { id: ADDR_1, intent: 'update', values: { ...blank(ADDRESS_RULES), usage: 'Fatura', country: 'TR', line1: 'Yeni Cadde 5', city: 'İstanbul', district: 'Şişli' } },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)

    expect(result.elements).toEqual([{ action: 'update', id: ADDR_1, fields: { line1: 'Yeni Cadde 5' } }])
    expect(result.retainedIds).toEqual([ADDR_2])
    expect(result.problems).toEqual({})
  })

  it('an unchanged row emits nothing and is not reported as retained either', () => {
    const rows: EditedRow[] = [
      { id: ADDR_1, intent: 'unchanged', values: {} },
      { id: ADDR_2, intent: 'unchanged', values: {} },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.elements).toEqual([])
    expect(result.retainedIds).toEqual([])
  })

  it('deactivation is explicit, carries the identity only, and is not an edit', () => {
    const rows: EditedRow[] = [
      { id: ADDR_1, intent: 'deactivate', values: { line1: 'yazılmayacak' } },
      { id: ADDR_2, intent: 'unchanged', values: {} },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.elements).toEqual([{ action: 'deactivate', id: ADDR_1 }])
    expect(result.retainedIds).toEqual([])
  })

  it('refuses a row whose identity does not match its intent', () => {
    const rows: EditedRow[] = [
      { id: ADDR_1, intent: 'add', values: { usage: 'Fatura', country: 'TR', line1: 'x' } },
      { id: null, intent: 'update', values: { usage: 'Fatura', country: 'TR', line1: 'x' } },
      { id: null, intent: 'deactivate', values: {} },
      { id: null, intent: 'add', values: { usage: '  ', country: '', line1: '   ' } },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.elements).toEqual([])
    expect(result.problems).toEqual({ 0: 'row-identity', 1: 'row-identity', 2: 'row-identity', 3: 'row-empty' })
  })

  it('reports a field problem against its own row and emits nothing for that row', () => {
    const rows: EditedRow[] = [
      // A filled row missing its required country.
      { id: null, intent: 'add', values: { ...blank(ADDRESS_RULES), usage: 'Fatura', line1: 'Yeni Cadde 5' } },
      { id: ADDR_2, intent: 'update', values: { ...originalsAsInput(originals[ADDR_2]), postalCode: '41400' } },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.problems).toEqual({ '0.country': 'required' })
    expect(result.elements).toEqual([{ action: 'update', id: ADDR_2, fields: { postalCode: '41400' } }])
  })

  it('an update with nothing changed sends nothing', () => {
    const rows: EditedRow[] = [
      { id: ADDR_1, intent: 'update', values: originalsAsInput(originals[ADDR_1]) },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.elements).toEqual([])
    expect(result.problems).toEqual({})
  })
})

describe('the first acceptance step — two addresses, one contact, a corrected phone', () => {
  /**
   * CARI-AC-007/009/010/011/030 (AT-006): the authorized user opens the existing
   * role-less Party, adds two addresses and one contact person, corrects the phone and
   * saves. This is the payload shape that step produces — one logical update, no second
   * Party, and nothing said about rows the editor did not touch.
   */
  it('produces one basic patch plus explicit collection elements, and can be submitted', () => {
    const contact = buildBasicPatch(
      CONTACT_RULES,
      { phone: '0212 555 00 00', email: null, website: null, phoneExtension: null },
      { phone: '+90 212 555 00 01', email: 'operasyon@example.test' },
    )
    const addresses = buildCollectionPatch(
      ADDRESS_RULES,
      [
        { id: null, intent: 'add', values: { ...blank(ADDRESS_RULES), usage: 'Fatura', country: 'TR', line1: 'Merkez Cadde 1', city: 'İstanbul', district: 'Şişli', postalCode: '34380' } },
        { id: null, intent: 'add', values: { ...blank(ADDRESS_RULES), usage: 'Teslim', country: 'TR', line1: 'Depo Yolu 2', city: 'Kocaeli', district: 'Gebze' } },
      ],
      {},
    )
    const people = buildCollectionPatch(
      { fullName: requiredText(200), title: optionalText(120), phone: CONTACT_RULES.phone, email: CONTACT_RULES.email },
      [{ id: null, intent: 'add', values: { fullName: 'Ayşe Yılmaz', title: 'Satın alma', phone: '0532 000 00 00', email: '' } }],
      {},
    )

    expect(contact.patch).toEqual({ phone: '+90 212 555 00 01', email: 'operasyon@example.test' })
    expect(addresses.elements).toHaveLength(2)
    expect(addresses.elements[0]).toEqual({
      action: 'add',
      fields: { usage: 'Fatura', country: 'TR', line1: 'Merkez Cadde 1', city: 'İstanbul', district: 'Şişli', postalCode: '34380' },
    })
    // The second address carries no postal code: absent means unset, not cleared.
    expect(addresses.elements[1]).toEqual({
      action: 'add',
      fields: { usage: 'Teslim', country: 'TR', line1: 'Depo Yolu 2', city: 'Kocaeli', district: 'Gebze' },
    })
    expect(people.elements).toEqual([
      { action: 'add', fields: { fullName: 'Ayşe Yılmaz', title: 'Satın alma', phone: '0532 000 00 00' } },
    ])
    expect(canSubmitBasic(contact, [addresses, people])).toBe(true)
  })

  it('one wrong e-mail blocks that save without discarding anything the user typed', () => {
    const contact = buildBasicPatch(
      CONTACT_RULES,
      { phone: '0212 555 00 00', email: null, website: null, phoneExtension: null },
      { phone: '+90 212 555 00 01', email: 'operasyon@' },
    )
    const addresses = buildCollectionPatch(
      ADDRESS_RULES,
      [{ id: null, intent: 'add', values: { ...blank(ADDRESS_RULES), usage: 'Fatura', country: 'TR', line1: 'Merkez Cadde 1' } }],
      {},
    )
    expect(contact.problems).toEqual({ email: 'email-format' })
    expect(canSubmitBasic(contact, [addresses])).toBe(false)
    // The valid edits are still in the result; the screen keeps the form as it is.
    expect(contact.patch).toEqual({ phone: '+90 212 555 00 01' })
    expect(addresses.elements).toHaveLength(1)
  })
})

/** Every field of a row as an empty input, so a case only states what it fills in. */
function blank(rules: Readonly<Record<string, FieldRule>>): Record<string, string> {
  return Object.fromEntries(Object.keys(rules).map((key) => [key, '']))
}

/** The last read's values as they would sit in the inputs. */
function originalsAsInput(values: Readonly<Record<string, string | null>>): Record<string, string> {
  return Object.fromEntries(Object.entries(values).map(([key, value]) => [key, value ?? '']))
}
