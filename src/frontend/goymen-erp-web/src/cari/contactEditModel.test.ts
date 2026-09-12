/**
 * The account-scoped contact/address edit semantics (CHATGPT-BUSINESS handoff,
 * 12 September: issue #66 comment `5642102538`, field dictionary #232 comment
 * `5642117532`), checked against Backend's WIP DTO shape: omission retains — for
 * scalars and for whole arrays — an explicit null clears an optional scalar,
 * passivity is an explicit operation, and every child element carries an id.
 *
 * The rules below are TEST-LOCAL on purpose. Backend owns the real property names
 * and the one authoritative length per field; ./contactInformationContract.ts is the
 * single place that names them. What is under test is the semantics, which do not
 * depend on either.
 */

import { describe, expect, it } from 'vitest'
import {
  buildChanges,
  buildCollectionPatch,
  buildFieldPatch,
  canSubmitChanges,
  checkField,
  generateRowId,
  isEmailShaped,
  isRowEmpty,
  isSupportedWebUrl,
  MAX_OPERATIONS_PER_COLLECTION,
  normalizePhone,
  trimToNull,
  type ChangesSpec,
  type EditedRow,
  type FieldRule,
  type OriginalRow,
} from './contactEditModel'

const optionalText = (maxLength?: number): FieldRule => ({ scope: 'basic', required: false, maxLength, kind: 'text' })
const requiredText = (maxLength?: number): FieldRule => ({ scope: 'basic', required: true, maxLength, kind: 'text' })

/** The flat operational scalars, as Backend's WIP `changes` carries them. */
const SCALARS: Record<string, FieldRule> = {
  phone: { scope: 'basic', required: false, kind: 'phone' },
  phoneExtension: optionalText(),
  email: { scope: 'basic', required: false, kind: 'email' },
  website: { scope: 'basic', required: false, kind: 'url' },
  // Critical by CARI-DEC-004: proposal + single approver, never the basic save.
  legalName: { scope: 'critical', required: true, kind: 'text' },
}

const ADDRESS_RULES: Record<string, FieldRule> = {
  purpose: requiredText(),
  countryCode: requiredText(),
  line1: requiredText(),
  line2: optionalText(),
  city: optionalText(),
  district: optionalText(),
  postalCode: optionalText(),
}

const CONTACT_RULES: Record<string, FieldRule> = {
  displayName: requiredText(),
  jobTitle: optionalText(),
  purpose: requiredText(),
}

const CHANNEL_RULES: Record<string, FieldRule> = {
  kind: requiredText(),
  value: requiredText(),
  extension: optionalText(),
}

const SPEC: ChangesSpec = {
  scalars: SCALARS,
  collections: [
    { property: 'addresses', rules: ADDRESS_RULES },
    { property: 'contacts', rules: CONTACT_RULES, nested: { property: 'channels', rules: CHANNEL_RULES } },
  ],
}

const ADDR_1 = 'a1111111-1111-4111-8111-111111111111'
const ADDR_2 = 'a2222222-2222-4222-8222-222222222222'
const CONTACT_1 = 'c1111111-1111-4111-8111-111111111111'
const CHANNEL_1 = 'd1111111-1111-4111-8111-111111111111'

/** Every field of a row as an empty input, so a case only states what it fills in. */
function blank(rules: Readonly<Record<string, FieldRule>>): Record<string, string> {
  return Object.fromEntries(Object.keys(rules).map((key) => [key, '']))
}

/** The last read's values as they would sit in the inputs. */
function asInput(values: Readonly<Record<string, string | null>>): Record<string, string> {
  return Object.fromEntries(Object.entries(values).map(([key, value]) => [key, value ?? '']))
}

describe('value checks — the client never improves on what the user typed', () => {
  it('keeps a phone exactly as typed, including the leading +, the 0 and the extension', () => {
    // Every one of these would be "helpfully" rewritten by a normalizer that assumes a
    // country or an operator, and each rewrite changes who gets called.
    expect(normalizePhone('  +90 (212) 555 00 00 / 123  ')).toBe('+90 (212) 555 00 00 / 123')
    expect(normalizePhone('0212 555 00 00')).toBe('0212 555 00 00')
    expect(normalizePhone('+905551112233')).toBe('+905551112233')
    expect(normalizePhone(' 0 212  555 ')).toBe('0 212  555')
  })

  it('turns an empty or blank string into null after trimming', () => {
    expect(trimToNull('')).toBeNull()
    expect(trimToNull('   ')).toBeNull()
    expect(trimToNull('\t\n ')).toBeNull()
    expect(trimToNull(' Merkez ')).toBe('Merkez')
    expect(normalizePhone('   ')).toBeNull()
  })

  it('checks the SHAPE of an e-mail and claims nothing more', () => {
    expect(isEmailShaped('operasyon@example.test')).toBe(true)
    expect(isEmailShaped('a.b-c+d@alt.example.test')).toBe(true)
    /**
     * A DOTTED DOMAIN IS NOT REQUIRED. The pinned contract validates one syntactically
     * valid mailbox and its validator accepts this, so refusing it here was the client
     * inventing a second business rule — and the cost was real: a counterparty whose
     * stored e-mail looks like this could not have their PHONE corrected, because the
     * section refused to save over a field the user never touched.
     */
    expect(isEmailShaped('kisi@example')).toBe(true)
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
    expect(isSupportedWebUrl('www.example.test')).toBe(false)
  })

  it('separates unsaved work from sendable work: an invalid-only edit is still dirty', () => {
    /**
     * These answer different questions, and conflating them lost the user's typing: an
     * edit whose only change is invalid produces an EMPTY patch, so a screen deriving
     * "is there unsaved work" from the patch called itself clean — no field error, a
     * closed Save, and no warning on leaving or switching company.
     */
    const original = { scalars: { phone: '0212 555 00 00', phoneExtension: null, email: null, website: null }, collections: {} }
    const invalidOnly = buildChanges(SPEC, original, {
      scalars: { phone: '0212 555 00 00', email: 'operasyon@' },
      collections: {},
    })
    expect(invalidOnly.problems).toEqual({ email: 'email-format' })
    expect(invalidOnly.dirty).toBe(true)
    expect(invalidOnly.hasChanges).toBe(false)
    expect(canSubmitChanges(invalidOnly)).toBe(false)

    // And a value typed then reverted is neither dirty nor sendable.
    const reverted = buildChanges(SPEC, original, {
      scalars: { phone: ' 0212 555 00 00 ', email: '' },
      collections: {},
    })
    expect(reverted.dirty).toBe(false)
    expect(reverted.hasChanges).toBe(false)
  })

  it('counts an added-but-blank row and a status request as unsaved work', () => {
    const blankRow = buildChanges(
      SPEC,
      { scalars: {}, collections: {} },
      { scalars: {}, collections: { addresses: [{ id: generateRowId(), intent: 'upsert', values: blank(ADDRESS_RULES) }] } },
    )
    // The row is on screen and it blocks the save, so the screen must be able to say so
    // and offer to drop it — which it cannot do if the form is reported clean.
    expect(blankRow.problems).toEqual({ 'addresses.0': 'row-empty' })
    expect(blankRow.dirty).toBe(true)
    expect(blankRow.hasChanges).toBe(false)
  })

  it('refuses an over-long value when a limit is published, and checks nothing when it is not', () => {
    expect(checkField(optionalText(20), 'x'.repeat(21))).toEqual({ ok: false, problem: 'too-long' })
    expect(checkField(optionalText(20), 'x'.repeat(20))).toEqual({ ok: true, value: 'x'.repeat(20) })
    // Measured after trimming, and never truncated to fit.
    expect(checkField(optionalText(20), `  ${'x'.repeat(20)}  `)).toEqual({ ok: true, value: 'x'.repeat(20) })
    /**
     * Backend has not published the maximums yet. Until it does there is no client-side
     * length check at all: the server's own validation decides, and a guessed limit
     * would either reject a value the contract accepts or quietly shorten one.
     */
    expect(checkField(optionalText(), 'x'.repeat(5000))).toEqual({ ok: true, value: 'x'.repeat(5000) })
  })

  it('distinguishes an empty required field from an empty optional one', () => {
    expect(checkField(requiredText(), '   ')).toEqual({ ok: false, problem: 'required' })
    expect(checkField(optionalText(), '   ')).toEqual({ ok: true, value: null })
    expect(checkField(SCALARS.email, 'yanlis-eposta')).toEqual({ ok: false, problem: 'email-format' })
    expect(checkField(SCALARS.website, 'ftp://x.test')).toEqual({ ok: false, problem: 'url-unsupported' })
  })

  it('knows an untouched row from an invalid one', () => {
    expect(isRowEmpty({ purpose: '', countryCode: '  ', line1: '' })).toBe(true)
    expect(isRowEmpty({ purpose: '', countryCode: 'TR', line1: '' })).toBe(false)
  })
})

describe('generateRowId — the client mints a new child id, once, when the row appears', () => {
  it('is a v4 UUID drawn from the given source, with the version and variant bits set', () => {
    const source = {
      getRandomValues<T extends ArrayBufferView | null>(array: T): T {
        if (array instanceof Uint8Array) array.fill(0xff)
        return array
      },
    }
    // 0xff everywhere: the version nibble must still read 4 and the variant bits 10.
    expect(generateRowId(source)).toBe('ffffffff-ffff-4fff-bfff-ffffffffffff')
    expect(generateRowId()).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(generateRowId()).not.toBe(generateRowId())
  })
})

describe('buildFieldPatch — only what changed, and only what this save owns', () => {
  const original = { phone: '0212 555 00 00', email: 'eski@example.test', website: null, phoneExtension: null }

  it('omits an untouched field even when the user retyped it with stray whitespace', () => {
    const result = buildFieldPatch(SCALARS, original, {
      phone: '  0212 555 00 00  ',
      email: 'eski@example.test',
    })
    expect(result.patch).toEqual({})
    expect(result.changed).toEqual([])
    expect(result.problems).toEqual({})
  })

  it('sends the corrected value, and an explicit null to clear an optional scalar', () => {
    const result = buildFieldPatch(SCALARS, original, { phone: '+90 212 555 00 01', email: '   ' })
    expect(result.patch).toEqual({ phone: '+90 212 555 00 01', email: null })
    expect(result.changed).toEqual(['phone', 'email'])
  })

  it('refuses to clear a field the contract requires', () => {
    const result = buildFieldPatch({ purpose: requiredText() }, { purpose: 'Fatura' }, { purpose: '' })
    expect(result.problems).toEqual({ purpose: 'required' })
    expect(result.patch).toEqual({})
  })

  it('keeps a critical field out of the basic payload entirely', () => {
    /**
     * The user may well edit the legal name in the same sitting — that is legitimate —
     * but it belongs to the proposal command with its own approver. If it rode along
     * here, one "saved" message would report an approval that never happened.
     */
    const result = buildFieldPatch(
      SCALARS,
      { ...original, legalName: 'Eski Unvan A.Ş.' },
      { phone: '+90 212 555 00 01', legalName: 'Yeni Unvan A.Ş.' },
    )
    expect(result.patch).toEqual({ phone: '+90 212 555 00 01' })
    expect(result.problems).toEqual({ legalName: 'critical-scope' })
  })

  it('refuses a key it has no rule for, because the server rejects unknown properties', () => {
    const result = buildFieldPatch(SCALARS, original, { balance: '0', phone: '0212 555 00 00' })
    expect(result.patch).toEqual({})
    expect(result.problems).toEqual({ balance: 'unknown-field' })
  })
})

describe('buildCollectionPatch — a row that left the editor is not a deletion', () => {
  const originals: Record<string, OriginalRow> = {
    [ADDR_1]: {
      values: { purpose: 'Fatura', countryCode: 'TR', line1: 'Eski Cadde 1', line2: null, city: 'İstanbul', district: 'Şişli', postalCode: null },
    },
    [ADDR_2]: {
      values: { purpose: 'Teslim', countryCode: 'TR', line1: 'Depo Yolu 2', line2: null, city: 'Kocaeli', district: 'Gebze', postalCode: null },
    },
  }

  it('says nothing about a row it never loaded, and reports it as retained', () => {
    /**
     * The destructive version of this editor sends "the rows I still have" and deletes
     * everything it never saw. Here the second address is not mentioned at all: no
     * element is emitted for it and the screen is told, in so many words, that the
     * server still holds it.
     */
    const rows: EditedRow[] = [
      { id: ADDR_1, intent: 'upsert', values: { ...asInput(originals[ADDR_1].values), line1: 'Yeni Cadde 5' } },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)

    expect(result.elements).toEqual([{ id: ADDR_1, operation: 'upsert', line1: 'Yeni Cadde 5' }])
    expect(result.retainedIds).toEqual([ADDR_2])
    expect(result.problems).toEqual({})
  })

  it('emits nothing for a row the user opened and left alone, whatever its flag says', () => {
    // A stale intent must not be able to spend a write: the operation follows what
    // actually changed, not what the form last remembered.
    const rows: EditedRow[] = [
      { id: ADDR_1, intent: 'upsert', values: asInput(originals[ADDR_1].values) },
      { id: ADDR_2, intent: 'unchanged', values: asInput(originals[ADDR_2].values) },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.elements).toEqual([])
    expect(result.retainedIds).toEqual([])
  })

  it('passivity and reactivation are explicit operations, carrying the identity only', () => {
    const rows: EditedRow[] = [
      { id: ADDR_1, intent: 'deactivate', values: { line1: 'gönderilmeyecek' } },
      { id: ADDR_2, intent: 'reactivate', values: {} },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.elements).toEqual([
      { id: ADDR_1, operation: 'deactivate' },
      { id: ADDR_2, operation: 'reactivate' },
    ])
  })

  it('a new row carries the id the form minted and every value it actually has', () => {
    const fresh = generateRowId()
    const rows: EditedRow[] = [
      { id: fresh, intent: 'upsert', values: { ...blank(ADDRESS_RULES), purpose: 'Teslim', countryCode: 'TR', line1: 'Yeni Depo 9' } },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    // The empty optional fields are absent, not null: absent means unset for a row that
    // never had a value, and null is a clear.
    expect(result.elements).toEqual([
      { id: fresh, operation: 'upsert', purpose: 'Teslim', countryCode: 'TR', line1: 'Yeni Depo 9' },
    ])
    expect(result.retainedIds).toEqual([ADDR_1, ADDR_2])
  })

  it('refuses a row with no identity, and an added row left blank', () => {
    const rows: EditedRow[] = [
      { id: '', intent: 'upsert', values: { purpose: 'Fatura', countryCode: 'TR', line1: 'x' } },
      { id: generateRowId(), intent: 'upsert', values: blank(ADDRESS_RULES) },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.elements).toEqual([])
    expect(result.problems).toEqual({ 0: 'row-identity', 1: 'row-empty' })
  })

  it('refuses a second operation for the same row, which would reject the whole command', () => {
    const rows: EditedRow[] = [
      { id: ADDR_1, intent: 'upsert', values: { ...asInput(originals[ADDR_1].values), city: 'Ankara' } },
      { id: ADDR_1, intent: 'deactivate', values: {} },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.problems).toEqual({ 1: 'row-duplicate' })
    // The first operation still stands; only the duplicate is refused, and the save is
    // blocked while it is there.
    expect(result.elements).toEqual([{ id: ADDR_1, operation: 'upsert', city: 'Ankara' }])
  })

  it('refuses a status change for a row the server does not hold yet', () => {
    // Status operations require an existing child. A new row cannot be deactivated —
    // it can simply be removed from the editor before saving.
    const fresh = generateRowId()
    const result = buildCollectionPatch(
      ADDRESS_RULES,
      [{ id: fresh, intent: 'deactivate', values: blank(ADDRESS_RULES) }],
      originals,
    )
    expect(result.problems).toEqual({ 0: 'status-needs-existing' })
    expect(result.elements).toEqual([])
  })

  it('reports a field problem against its own row and emits nothing for that row', () => {
    const rows: EditedRow[] = [
      { id: generateRowId(), intent: 'upsert', values: { ...blank(ADDRESS_RULES), purpose: 'Fatura', line1: 'Yeni Cadde 5' } },
      { id: ADDR_2, intent: 'upsert', values: { ...asInput(originals[ADDR_2].values), postalCode: '41400' } },
    ]
    const result = buildCollectionPatch(ADDRESS_RULES, rows, originals)
    expect(result.problems).toEqual({ '0.countryCode': 'required' })
    expect(result.elements).toEqual([{ id: ADDR_2, operation: 'upsert', postalCode: '41400' }])
  })
})

describe('nested rows — a contact travels for its channels', () => {
  const originals: Record<string, OriginalRow> = {
    [CONTACT_1]: {
      values: { displayName: 'Ayşe Yılmaz', jobTitle: 'Satın alma', purpose: 'Operations' },
      children: { [CHANNEL_1]: { values: { kind: 'phone', value: '0532 000 00 00', extension: null } } },
    },
  }
  const NESTED = { property: 'channels', rules: CHANNEL_RULES }

  it('sends the parent as an upsert with ONLY the channels when its own fields did not move', () => {
    /**
     * Omission retains the parent's scalars, so an element carrying nothing but the
     * nested array says "leave my own fields alone, here is what changed underneath".
     * Without it a corrected phone channel would have no element to travel in.
     */
    const rows: EditedRow[] = [
      {
        id: CONTACT_1,
        intent: 'unchanged',
        values: asInput(originals[CONTACT_1].values),
        children: [{ id: CHANNEL_1, intent: 'upsert', values: { kind: 'phone', value: '0532 000 00 01', extension: '' } }],
      },
    ]
    const result = buildCollectionPatch(CONTACT_RULES, rows, originals, NESTED)
    expect(result.elements).toEqual([
      { id: CONTACT_1, operation: 'upsert', channels: [{ id: CHANNEL_1, operation: 'upsert', value: '0532 000 00 01' }] },
    ])
  })

  it('leaves the channels property out entirely when no channel changed', () => {
    const rows: EditedRow[] = [
      {
        id: CONTACT_1,
        intent: 'upsert',
        values: { ...asInput(originals[CONTACT_1].values), jobTitle: 'Satın alma müdürü' },
        children: [{ id: CHANNEL_1, intent: 'unchanged', values: asInput(originals[CONTACT_1].children![CHANNEL_1].values) }],
      },
    ]
    const result = buildCollectionPatch(CONTACT_RULES, rows, originals, NESTED)
    expect(result.elements).toEqual([{ id: CONTACT_1, operation: 'upsert', jobTitle: 'Satın alma müdürü' }])
  })

  it('keys a nested problem to its own row and channel, and sends nothing for the parent', () => {
    const rows: EditedRow[] = [
      {
        id: CONTACT_1,
        intent: 'upsert',
        values: { ...asInput(originals[CONTACT_1].values), jobTitle: 'Yeni görev' },
        children: [{ id: generateRowId(), intent: 'upsert', values: { kind: 'phone', value: '   ', extension: '' } }],
      },
    ]
    const result = buildCollectionPatch(CONTACT_RULES, rows, originals, NESTED)
    expect(result.problems).toEqual({ '0.channels.0.value': 'required' })
    expect(result.elements).toEqual([])
  })

  it('a channel the editor did not mention is retained, not removed', () => {
    const rows: EditedRow[] = [
      { id: CONTACT_1, intent: 'upsert', values: { ...asInput(originals[CONTACT_1].values), purpose: 'Muhasebe' }, children: [] },
    ]
    const result = buildCollectionPatch(CONTACT_RULES, rows, originals, NESTED)
    expect(result.elements).toEqual([{ id: CONTACT_1, operation: 'upsert', purpose: 'Muhasebe' }])
  })
})

describe('buildChanges — the whole changes object, with omission meaning retain', () => {
  const original = {
    scalars: { phone: '0212 555 00 00', phoneExtension: null, email: null, website: null },
    collections: {
      addresses: { [ADDR_1]: { values: { purpose: 'Fatura', countryCode: 'TR', line1: 'Eski Cadde 1' } } },
      contacts: {},
    },
  }

  it('omits an untouched collection entirely — not null, and not an empty array', () => {
    /**
     * The server rejects a null array, and an empty one would read as an instruction it
     * does not carry. So the only correct way to say "I am not talking about the
     * addresses" is to leave the property out.
     */
    const body = buildChanges(SPEC, original, {
      scalars: { phone: '+90 212 555 00 01' },
      collections: { addresses: [{ id: ADDR_1, intent: 'unchanged', values: asInput(original.collections.addresses[ADDR_1].values) }] },
    })
    expect(body.changes).toEqual({ phone: '+90 212 555 00 01' })
    expect(Object.hasOwn(body.changes, 'addresses')).toBe(false)
    expect(Object.hasOwn(body.changes, 'contacts')).toBe(false)
    expect(body.dirty).toBe(true)
    expect(canSubmitChanges(body)).toBe(true)
  })

  it('nothing changed means nothing to save', () => {
    const body = buildChanges(SPEC, original, {
      scalars: { phone: '  0212 555 00 00 ' },
      collections: {},
    })
    expect(body.changes).toEqual({})
    expect(body.dirty).toBe(false)
    expect(canSubmitChanges(body)).toBe(false)
    // And the address the editor did not speak about is reported as kept.
    expect(body.retained.addresses).toEqual([ADDR_1])
  })

  it('one wrong value blocks the save without discarding what else the user typed', () => {
    const body = buildChanges(SPEC, original, {
      scalars: { phone: '+90 212 555 00 01', email: 'operasyon@' },
      collections: {},
    })
    expect(body.problems).toEqual({ email: 'email-format' })
    expect(canSubmitChanges(body)).toBe(false)
    // The valid change is still in the body; the screen keeps the form as it is.
    expect(body.changes).toEqual({ phone: '+90 212 555 00 01' })
  })

  it('refuses more operations in one collection than a request may carry', () => {
    /**
     * 100 operations per collection per request. Over that the server rejects the whole
     * command, so the refusal is local and names the collection — and the message must
     * not read as a cap on how many addresses the counterparty may have, because it is
     * a request limit and nothing else.
     */
    const rows: EditedRow[] = Array.from({ length: MAX_OPERATIONS_PER_COLLECTION + 1 }, (_, i) => ({
      id: generateRowId(),
      intent: 'upsert' as const,
      values: { ...blank(ADDRESS_RULES), purpose: 'billing', countryCode: 'TR', line1: `Cadde ${i}` },
    }))
    const body = buildChanges(SPEC, { scalars: {}, collections: {} }, { scalars: {}, collections: { addresses: rows } })
    expect(body.problems).toEqual({ addresses: 'too-many-operations' })
    expect(canSubmitChanges(body)).toBe(false)

    const withinLimit = buildChanges(
      SPEC,
      { scalars: {}, collections: {} },
      { scalars: {}, collections: { addresses: rows.slice(0, MAX_OPERATIONS_PER_COLLECTION) } },
    )
    expect(withinLimit.problems).toEqual({})
    expect(canSubmitChanges(withinLimit)).toBe(true)
  })

  it('the first acceptance step: two addresses, one contact with a phone, a corrected number', () => {
    /**
     * CARI-AC-007/009/010/011/030 (AT-006). One logical update, one account version
     * fencing the whole graph, no second Party, and nothing said about anything the
     * editor did not touch.
     */
    const addrA = generateRowId()
    const addrB = generateRowId()
    const person = generateRowId()
    const channel = generateRowId()

    const body = buildChanges(
      SPEC,
      { scalars: { phone: '0212 555 00 00', phoneExtension: null, email: null, website: null }, collections: {} },
      {
        scalars: { phone: '+90 212 555 00 01', email: 'operasyon@example.test' },
        collections: {
          addresses: [
            { id: addrA, intent: 'upsert', values: { ...blank(ADDRESS_RULES), purpose: 'Fatura', countryCode: 'TR', line1: 'Merkez Cadde 1', city: 'İstanbul', district: 'Şişli', postalCode: '34380' } },
            { id: addrB, intent: 'upsert', values: { ...blank(ADDRESS_RULES), purpose: 'Teslim', countryCode: 'TR', line1: 'Depo Yolu 2', city: 'Kocaeli', district: 'Gebze' } },
          ],
          contacts: [
            {
              id: person,
              intent: 'upsert',
              values: { displayName: 'Ayşe Yılmaz', jobTitle: 'Satın alma', purpose: 'Operations' },
              children: [{ id: channel, intent: 'upsert', values: { kind: 'phone', value: '0532 000 00 00', extension: '' } }],
            },
          ],
        },
      },
    )

    expect(body.changes).toEqual({
      phone: '+90 212 555 00 01',
      email: 'operasyon@example.test',
      addresses: [
        { id: addrA, operation: 'upsert', purpose: 'Fatura', countryCode: 'TR', line1: 'Merkez Cadde 1', city: 'İstanbul', district: 'Şişli', postalCode: '34380' },
        // No postal code on the second: absent means unset, not cleared.
        { id: addrB, operation: 'upsert', purpose: 'Teslim', countryCode: 'TR', line1: 'Depo Yolu 2', city: 'Kocaeli', district: 'Gebze' },
      ],
      contacts: [
        {
          id: person,
          operation: 'upsert',
          displayName: 'Ayşe Yılmaz',
          jobTitle: 'Satın alma',
          purpose: 'Operations',
          channels: [{ id: channel, operation: 'upsert', kind: 'phone', value: '0532 000 00 00' }],
        },
      ],
    })
    expect(canSubmitChanges(body)).toBe(true)
  })
})
