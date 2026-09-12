/**
 * The operational contact/address decoder and the rules it publishes to the editor.
 *
 * Source pinned in ./contactInformationContract.ts. Two things are under test: the
 * decoder refuses a response it must not render a part of, and it stays PERMISSIVE
 * about a value the server may legitimately extend — the `roles = 0` regression was
 * exactly a decoder that rejected a widened vocabulary and turned one ordinary record
 * into a `malformed-response` for the whole screen.
 */

import { describe, expect, it } from 'vitest'
import {
  ADDRESS_FIELD_RULES,
  ADDRESS_PURPOSES,
  CHANNEL_FIELD_RULES,
  CHANNEL_KINDS,
  CONTACT_CHANGES_SPEC,
  CONTACT_FIELD_RULES,
  CONTACT_SCALAR_RULES,
  channelValueRule,
  contactInformationPath,
  decodeContactInformation,
  refineChannelRules,
} from './contactInformationContract'
import { buildChanges, buildCollectionPatch, checkField, type EditedRow, type OriginalRow } from './contactEditModel'

const PARTY = '11111111-1111-4111-8111-111111111111'
const OTHER_PARTY = '22222222-2222-4222-8222-222222222222'
const COMPANY_A = 'aaaaaaaa-0000-4000-8000-000000000001'
const COMPANY_B = 'bbbbbbbb-0000-4000-8000-000000000002'
const ACCOUNT = 'cccccccc-0000-4000-8000-000000000003'
const ADDRESS = 'dddddddd-0000-4000-8000-000000000004'
const CONTACT = 'eeeeeeee-0000-4000-8000-000000000005'
const CHANNEL = 'ffffffff-0000-4000-8000-000000000006'

const EXPECTED = { partyId: PARTY, companyId: COMPANY_A }

function address(over: Record<string, unknown> = {}) {
  return {
    id: ADDRESS,
    version: '3',
    isActive: true,
    purpose: 'billing',
    countryCode: 'TR',
    line1: 'Merkez Cadde 1',
    line2: null,
    city: 'İstanbul',
    district: 'Şişli',
    postalCode: null,
    ...over,
  }
}

function channel(over: Record<string, unknown> = {}) {
  return { id: CHANNEL, version: '2', isActive: true, kind: 'phone', value: '0532 000 00 00', extension: null, ...over }
}

function contact(over: Record<string, unknown> = {}) {
  return {
    id: CONTACT,
    version: '4',
    isActive: true,
    displayName: 'Ayşe Yılmaz',
    jobTitle: null,
    purpose: 'Operations',
    channels: [channel()],
    ...over,
  }
}

function view(over: Record<string, unknown> = {}, information: Record<string, unknown> = {}) {
  return {
    partyId: PARTY,
    accountId: ACCOUNT,
    companyId: COMPANY_A,
    version: '7',
    isActive: true,
    information: {
      phone: '0212 555 00 00',
      phoneExtension: null,
      email: null,
      website: null,
      addresses: [address()],
      contacts: [contact()],
      ...information,
    },
    ...over,
  }
}

describe('decodeContactInformation', () => {
  it('decodes the accepted shape and carries the account version verbatim', () => {
    const decoded = decodeContactInformation(view(), EXPECTED)
    expect(decoded).not.toBeNull()
    expect(decoded?.version).toBe('7')
    expect(decoded?.accountId).toBe(ACCOUNT)
    expect(decoded?.information.phone).toBe('0212 555 00 00')
    expect(decoded?.information.addresses).toHaveLength(1)
    expect(decoded?.information.contacts[0].channels[0].value).toBe('0532 000 00 00')
    // Nullable scalars arrive as null and stay null — never as '' and never dropped.
    expect(decoded?.information.email).toBeNull()
    expect(decoded?.information.addresses[0].postalCode).toBeNull()
  })

  it('carries a 19-digit account version as a string rather than a number', () => {
    const huge = '9223372036854775807'
    expect(decodeContactInformation(view({ version: huge }), EXPECTED)?.version).toBe(huge)
    // A JSON number is refused: past 2^53 it has already lost the value.
    expect(decodeContactInformation(view({ version: 7 }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view({ version: '0' }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view({ version: '' }), EXPECTED)).toBeNull()
  })

  it('refuses a version one past Int64.MaxValue, at every level', () => {
    /**
     * 19 digits is not the bound. `9223372036854775808` is also 19 digits and is one past
     * the largest value an Int64 can hold, so a length-only guard accepted a token the
     * server can never have issued — as the account version, as a child version, and as
     * the fence a write is sent on.
     */
    const max = '9223372036854775807'
    const past = '9223372036854775808'
    expect(decodeContactInformation(view({ version: max }), EXPECTED)?.version).toBe(max)
    expect(decodeContactInformation(view({ version: past }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view({}, { addresses: [address({ version: past })] }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view({}, { contacts: [contact({ version: past })] }), EXPECTED)).toBeNull()
    expect(
      decodeContactInformation(view({}, { contacts: [contact({ channels: [channel({ version: past })] })] }), EXPECTED),
    ).toBeNull()
    // The boundary value itself stays valid everywhere.
    expect(decodeContactInformation(view({}, { addresses: [address({ version: max })] }), EXPECTED)).not.toBeNull()
  })

  it('refuses a body about another company or another Party', () => {
    // Not filtered — refused. A response about someone else is not something to render
    // a part of, and quietly dropping the foreign half would hide the leak.
    expect(decodeContactInformation(view({ companyId: COMPANY_B }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view({ partyId: OTHER_PARTY }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view(), { partyId: PARTY, companyId: '' })).toBeNull()
  })

  it('accepts a purpose or kind it has never seen, because the server may widen them', () => {
    /**
     * `purpose` and `kind` are closed sets for a WRITE. On READ a fourth value must not
     * poison the card: an operator would see `malformed-response` instead of their own
     * data, which is the `roles = 0` failure again.
     */
    const widened = view({}, {
      addresses: [address({ purpose: 'warehouse' })],
      contacts: [contact({ purpose: 'Herhangi bir amaç', channels: [channel({ kind: 'fax' })] })],
    })
    const decoded = decodeContactInformation(widened, EXPECTED)
    expect(decoded?.information.addresses[0].purpose).toBe('warehouse')
    expect(decoded?.information.contacts[0].channels[0].kind).toBe('fax')
  })

  it('refuses a missing field, a wrong type and a broken child', () => {
    expect(decodeContactInformation(view({ isActive: 'true' }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view({ accountId: null }), EXPECTED)).toBeNull()
    // A nullable scalar may be null or a string; a number is neither.
    expect(decodeContactInformation(view({}, { email: 42 }), EXPECTED)).toBeNull()
    // One unusable child refuses the whole response rather than rendering a card that
    // silently omits an address.
    expect(decodeContactInformation(view({}, { addresses: [address({ line1: '  ' })] }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view({}, { addresses: [address({ version: 0 })] }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view({}, { contacts: [contact({ channels: [channel({ value: null })] })] }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(view({}, { addresses: null }), EXPECTED)).toBeNull()
    expect(decodeContactInformation(null, EXPECTED)).toBeNull()
    expect(decodeContactInformation([], EXPECTED)).toBeNull()
  })

  it('builds the path from the party id it was given', () => {
    expect(contactInformationPath(PARTY)).toBe(`/counterparties/${PARTY}/contact-information`)
  })
})

describe('the rules the editor consumes are the API owner published ones', () => {
  it('enforces the closed sets and refuses anything outside them', () => {
    expect([...ADDRESS_PURPOSES]).toEqual(['billing', 'delivery', 'operational'])
    expect([...CHANNEL_KINDS]).toEqual(['phone', 'email'])
    expect(checkField(ADDRESS_FIELD_RULES.purpose, 'delivery')).toEqual({ ok: true, value: 'delivery' })
    expect(checkField(ADDRESS_FIELD_RULES.purpose, 'warehouse')).toEqual({ ok: false, problem: 'not-allowed' })
    expect(checkField(CHANNEL_FIELD_RULES.kind, 'email')).toEqual({ ok: true, value: 'email' })
    expect(checkField(CHANNEL_FIELD_RULES.kind, 'fax')).toEqual({ ok: false, problem: 'not-allowed' })
    // Contact purpose is OPEN text, so it must not behave like a closed set.
    expect(CONTACT_FIELD_RULES.purpose.options).toBeUndefined()
    expect(checkField(CONTACT_FIELD_RULES.purpose, 'Sevkiyat koordinasyonu')).toEqual({
      ok: true,
      value: 'Sevkiyat koordinasyonu',
    })
  })

  it('accepts a country code as two uppercase letters and confirms nothing about it', () => {
    expect(checkField(ADDRESS_FIELD_RULES.countryCode, 'TR')).toEqual({ ok: true, value: 'TR' })
    // Well-formed is all this checks: there is no registry behind it.
    expect(checkField(ADDRESS_FIELD_RULES.countryCode, 'XX')).toEqual({ ok: true, value: 'XX' })
    expect(checkField(ADDRESS_FIELD_RULES.countryCode, 'tr')).toEqual({ ok: false, problem: 'country-format' })
    expect(checkField(ADDRESS_FIELD_RULES.countryCode, 'TUR')).toEqual({ ok: false, problem: 'too-long' })
    expect(checkField(ADDRESS_FIELD_RULES.countryCode, 'T1')).toEqual({ ok: false, problem: 'country-format' })
    expect(checkField(ADDRESS_FIELD_RULES.countryCode, '')).toEqual({ ok: false, problem: 'required' })
  })

  it('carries the pinned exact field matrix, which replaces the earlier draft numbers', () => {
    /**
     * The handoff is explicit that these REPLACE the Business draft's suggestions:
     * phone 32 over 64, address 250 over 500, postal 20 over 32, jobTitle 120 over 100.
     * Carrying a superseded number would reject values the contract accepts.
     */
    expect(CONTACT_SCALAR_RULES.phone.maxLength).toBe(32)
    expect(CONTACT_SCALAR_RULES.phoneExtension.maxLength).toBe(10)
    expect(CONTACT_SCALAR_RULES.email.maxLength).toBe(254)
    expect(CONTACT_SCALAR_RULES.website.maxLength).toBe(2048)
    expect(ADDRESS_FIELD_RULES.countryCode.maxLength).toBe(2)
    expect(ADDRESS_FIELD_RULES.line1.maxLength).toBe(250)
    expect(ADDRESS_FIELD_RULES.line2.maxLength).toBe(250)
    expect(ADDRESS_FIELD_RULES.city.maxLength).toBe(100)
    expect(ADDRESS_FIELD_RULES.district.maxLength).toBe(100)
    expect(ADDRESS_FIELD_RULES.postalCode.maxLength).toBe(20)
    expect(CONTACT_FIELD_RULES.displayName.maxLength).toBe(200)
    expect(CONTACT_FIELD_RULES.jobTitle.maxLength).toBe(120)
    expect(CONTACT_FIELD_RULES.purpose.maxLength).toBe(120)
    expect(CHANNEL_FIELD_RULES.extension.maxLength).toBe(10)
  })

  it('resolves a channel value from its own kind: a phone, a mailbox, or no claim yet', () => {
    expect(checkField(channelValueRule('phone'), '+90 (212) 555 00 00 / 12')).toEqual({
      ok: true,
      value: '+90 (212) 555 00 00 / 12',
    })
    expect(checkField(channelValueRule('phone'), '0'.repeat(33))).toEqual({ ok: false, problem: 'too-long' })
    // A phone channel is not e-mail-checked, and an e-mail channel is.
    expect(checkField(channelValueRule('phone'), 'dahili 5')).toEqual({ ok: true, value: 'dahili 5' })
    expect(checkField(channelValueRule('email'), 'kisi@example.test')).toEqual({
      ok: true,
      value: 'kisi@example.test',
    })
    // An undotted domain is accepted: the pinned contract's own validator takes it, and
    // the client must not be stricter than the contract it consumes.
    expect(checkField(channelValueRule('email'), 'kisi@example')).toEqual({ ok: true, value: 'kisi@example' })
    expect(checkField(channelValueRule('email'), 'kisi@')).toEqual({ ok: false, problem: 'email-format' })
    expect(checkField(channelValueRule('email'), `${'a'.repeat(250)}@ex.test`)).toEqual({
      ok: false,
      problem: 'too-long',
    })
    // Kind not chosen yet: no length and no format is claimed about the value.
    expect(checkField(channelValueRule(''), 'x'.repeat(5000))).toEqual({ ok: true, value: 'x'.repeat(5000) })
  })

  it('an extension applies to a phone and nowhere else', () => {
    const phoneRules = refineChannelRules({ kind: 'phone' }, CHANNEL_FIELD_RULES)
    expect(checkField(phoneRules.extension, '001')).toEqual({ ok: true, value: '001' })
    // On an e-mail channel it has nothing to extend, so a value is refused rather than
    // sent and rejected.
    const mailRules = refineChannelRules({ kind: 'email' }, CHANNEL_FIELD_RULES)
    expect(checkField(mailRules.extension, '001')).toEqual({ ok: false, problem: 'not-applicable' })
    expect(checkField(mailRules.extension, '   ')).toEqual({ ok: true, value: null })
  })

  it('clearing a phone refuses to leave its extension stranded', () => {
    /**
     * The contract warns that changing only the phone must never silently destroy a
     * retained extension. So the refusal is visible: the save stops on the extension,
     * the user clears it, and the payload then carries BOTH nulls — rather than the
     * client deleting the extension on the user's behalf without showing it.
     */
    const original = {
      scalars: { phone: '0212 555 00 00', phoneExtension: '123', email: null, website: null },
      collections: {},
    }
    const stranded = buildChanges(CONTACT_CHANGES_SPEC, original, {
      scalars: { phone: '', phoneExtension: '123', email: '', website: '' },
      collections: {},
    })
    expect(stranded.problems).toEqual({ phoneExtension: 'not-applicable' })

    const cleared = buildChanges(CONTACT_CHANGES_SPEC, original, {
      scalars: { phone: '', phoneExtension: '', email: '', website: '' },
      collections: {},
    })
    expect(cleared.problems).toEqual({})
    expect(cleared.changes).toEqual({ phone: null, phoneExtension: null })
  })

  it('the kind of a channel the server already holds cannot be changed', () => {
    const rows: EditedRow[] = [
      {
        id: CONTACT,
        intent: 'upsert',
        values: { displayName: 'Ayşe Yılmaz', jobTitle: '', purpose: 'Operations' },
        children: [{ id: CHANNEL, intent: 'upsert', values: { kind: 'email', value: 'a@b.test', extension: '' } }],
      },
    ]
    const originals: Record<string, OriginalRow> = {
      [CONTACT]: {
        values: { displayName: 'Ayşe Yılmaz', jobTitle: null, purpose: 'Operations' },
        children: { [CHANNEL]: { values: { kind: 'phone', value: '0532 000 00 00', extension: null } } },
      },
    }
    const result = buildCollectionPatch(CONTACT_FIELD_RULES, rows, originals, {
      property: 'channels',
      rules: CHANNEL_FIELD_RULES,
      refine: refineChannelRules,
    })
    // Refused, not sent: a phone row silently reread as an e-mail row is a different
    // fact about the counterparty. The edit has to be a new channel.
    expect(result.problems).toEqual({ '0.channels.0.kind': 'immutable' })
    expect(result.elements).toEqual([])
  })
  it('refuses an over-long value on a field that HAS a published maximum', () => {
    expect(checkField(CONTACT_SCALAR_RULES.phone, '0'.repeat(33))).toEqual({ ok: false, problem: 'too-long' })
    expect(checkField(ADDRESS_FIELD_RULES.line1, 'x'.repeat(251))).toEqual({ ok: false, problem: 'too-long' })
    expect(checkField(CONTACT_FIELD_RULES.purpose, 'x'.repeat(121))).toEqual({ ok: false, problem: 'too-long' })
    // The boundary is accepted, and the value is returned whole.
    expect(checkField(CONTACT_SCALAR_RULES.phone, '0'.repeat(32))).toEqual({ ok: true, value: '0'.repeat(32) })
  })

  it('every scalar of the block is clearable, because all four are nullable', () => {
    for (const key of ['phone', 'phoneExtension', 'email', 'website']) {
      expect(CONTACT_SCALAR_RULES[key].required).toBe(false)
      expect(checkField(CONTACT_SCALAR_RULES[key], '   ')).toEqual({ ok: true, value: null })
    }
  })
})
