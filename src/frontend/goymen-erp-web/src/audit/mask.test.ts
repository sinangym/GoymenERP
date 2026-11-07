import { describe, expect, it } from 'vitest'
import { MASK, MAX_DEPTH, maskAuditJson, maskFreeText, maskKeepLastFour, sensitiveKeyRule } from './mask'

// SENTETIK values only: the IBAN has a valid TR shape, nothing here is a real account, person or number.
const IBAN = 'TR330006100519786457841326'
const IBAN_SPACED = 'TR33 0006 1005 1978 6457 8413 26'

describe('audit payload masking (Decision No 135)', () => {
  it('classifies key names case-insensitively, with separators removed, at any spelling', () => {
    expect(sensitiveKeyRule('iban')).toBe('last-four')
    expect(sensitiveKeyRule('CounterpartyIBAN')).toBe('last-four')
    expect(sensitiveKeyRule('vkn')).toBe('last-four')
    expect(sensitiveKeyRule('TCKN')).toBe('last-four')
    expect(sensitiveKeyRule('tax_number')).toBe('last-four')
    expect(sensitiveKeyRule('identityNumber')).toBe('last-four')
    expect(sensitiveKeyRule('phone')).toBe('full')
    expect(sensitiveKeyRule('mobilePhoneNumber')).toBe('full')
    expect(sensitiveKeyRule('E-Mail')).toBe('full')
    expect(sensitiveKeyRule('billingAddress')).toBe('full')
    expect(sensitiveKeyRule('status')).toBeNull()
    expect(sensitiveKeyRule('amount')).toBeNull()
  })

  it('keeps only the last four characters, and masks a value of four or fewer entirely', () => {
    expect(maskKeepLastFour('1234567890')).toBe(`${MASK}7890`)
    expect(maskKeepLastFour(IBAN_SPACED)).toBe(`${MASK}1326`)
    expect(maskKeepLastFour('1234')).toBe(MASK)
    expect(maskKeepLastFour('')).toBe(MASK)
  })

  it('masks IBAN- and e-mail-shaped parts of free text wherever they appear', () => {
    expect(maskFreeText(`Ödeme ${IBAN} hesabına`)).toBe(`Ödeme ${MASK}1326 hesabına`)
    expect(maskFreeText(`Ödeme ${IBAN_SPACED} hesabına`)).toBe(`Ödeme ${MASK}1326 hesabına`)
    expect(maskFreeText('tr330006100519786457841326')).toBe(`${MASK}1326`)
    expect(maskFreeText('iletişim: sentetik.kisi@example.invalid')).toBe(`iletişim: ${MASK}`)
    expect(maskFreeText('Onaylandı')).toBe('Onaylandı')
  })

  it('masks by key recursively, under nested objects and arrays, and pretty-prints in stored order', () => {
    const text = JSON.stringify({
      status: 'Approved',
      party: {
        name: 'SENTETIK Cari',
        vkn: '1234567890',
        tckn: 12345678901,
        contacts: [{ phone: '+90 555 000 00 00', email: 'a@b.invalid' }],
        address: { line1: 'SENTETIK Sok. 1', city: 'İstanbul', postalCode: 34000 },
        bank: { iban: IBAN_SPACED, note: `yedek ${IBAN}` },
      },
      isActive: true,
      removedPhone: null,
    })

    const masked = maskAuditJson(text)

    expect(masked).not.toBeNull()
    const parsed = JSON.parse(masked as string)
    expect(parsed).toEqual({
      status: 'Approved',
      party: {
        name: 'SENTETIK Cari',
        vkn: `${MASK}7890`,
        tckn: `${MASK}8901`,
        contacts: [{ phone: MASK, email: MASK }],
        address: { line1: MASK, city: MASK, postalCode: MASK },
        bank: { iban: `${MASK}1326`, note: `yedek ${MASK}1326` },
      },
      isActive: true,
      removedPhone: null,
    })
    expect(masked).toContain('\n  "status": "Approved",\n  "party": {\n    "name"')
    for (const secret of ['1234567890', '12345678901', '555 000', 'a@b.invalid', 'SENTETIK Sok', IBAN, '0006 1005']) {
      expect(masked).not.toContain(secret)
    }
  })

  it('keeps numbers digit for digit (never through Number) and keeps duplicate keys as stored', () => {
    const masked = maskAuditJson('{"amount":12345678901234567890.123456789,"amount":1e-7,"list":[0,-1.50]}')
    expect(masked).toBe('{\n  "amount": 12345678901234567890.123456789,\n  "amount": 1e-7,\n  "list": [\n    0,\n    -1.50\n  ]\n}')
  })

  it('handles scalars, empties and escapes, and refuses anything that is not exactly one JSON value', () => {
    expect(maskAuditJson('"plain"')).toBe('"plain"')
    expect(maskAuditJson('{}')).toBe('{}')
    expect(maskAuditJson('[]')).toBe('[]')
    expect(maskAuditJson('{"a":"x\\"y\\u00e7"}')).toBe('{\n  "a": "x\\"yç"\n}')
    expect(maskAuditJson('{"email\\u0041ddress":"z@y.invalid"}')).toBe(`{\n  "emailAddress": "${MASK}"\n}`)
    for (const bad of ['', 'not json', '{"a":1,}', '{"a":1} x', "{'a':1}", '[1,2', '01', '{"a":"\u0001"}', 'NaN']) {
      expect(maskAuditJson(bad), bad).toBeNull()
    }
  })
  it('masks every member of a contact channel record, as the Cari contact editor stores it (K3 review 2/2 P1)', () => {
    const masked = maskAuditJson(
      '{"channels":[{"kind":"phone","value":"0532 111 22 33","extension":"12"},{"kind":"email","value":"a@b.invalid"},'
        + '{"Kind":"Address","value":"Atatürk Cd. 5","isPrimary":true}],"note":"kept"}',
    )
    expect(masked).not.toContain('0532')
    expect(masked).not.toContain('"12"')
    expect(masked).not.toContain('a@b.invalid')
    expect(masked).not.toContain('Atatürk')
    expect(masked).toContain('"kind": "phone"')
    expect(masked).toContain('"isPrimary": true')
    expect(masked).toContain('"note": "kept"')
  })

  it('masks Turkish and secret key names, but not short look-alike words', () => {
    for (const key of ['telefon', 'gsm', 'cep', 'ePosta', 'adres', 'password', 'apiToken', 'clientSecret']) {
      expect(sensitiveKeyRule(key), key).toBe('full')
    }
    for (const key of ['tcKimlikNo', 'vergiNo']) expect(sensitiveKeyRule(key), key).toBe('last-four')
    for (const key of ['accepted', 'recipe', 'hotel', 'kind']) expect(sensitiveKeyRule(key), key).toBeNull()
  })

  it('masks TR phone and TCKN shapes in free text, and JSON stored inside a string', () => {
    expect(maskFreeText('Ara: 0532 111 22 33 / +90 (212) 555-12-34')).toBe(`Ara: ${MASK} / ${MASK}`)
    expect(maskFreeText('TCKN 12345678901 bitti')).toBe(`TCKN ${MASK}8901 bitti`)
    expect(maskFreeText('Sipariş 2026-0001, adet 25')).toBe('Sipariş 2026-0001, adet 25')
    const masked = maskAuditJson('{"payload":"{\\"phone\\":\\"05321112233\\",\\"qty\\":3}"}')
    expect(masked).not.toContain('05321112233')
    expect(masked).toContain('qty')
  })

  it('does not show a payload nested deeper than the limit instead of exhausting the stack', () => {
    expect(maskAuditJson('['.repeat(MAX_DEPTH) + ']'.repeat(MAX_DEPTH))).not.toBeNull()
    expect(maskAuditJson('['.repeat(MAX_DEPTH + 1) + ']'.repeat(MAX_DEPTH + 1))).toBeNull()
    expect(maskAuditJson('['.repeat(5000) + ']'.repeat(5000))).toBeNull()
  })
})
