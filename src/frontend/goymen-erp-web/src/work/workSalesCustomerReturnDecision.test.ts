import {describe, expect, it} from 'vitest'
import {decodeCustomerReturnView, CUSTOMER_RETURN_VIEW_FIELDS, CUSTOMER_RETURN_LINE_FIELDS} from '../sales/customerReturnContract'
import {salesCustomerReturnHuman as wire, salesCustomerReturnFixture as fixture, salesCustomerReturnId as id} from './salesCustomerReturnDecisionFixture'
import {decodeWorkSalesCustomerReturnPreview as decode, validSalesCustomerReturnPreview as valid, isSalesCustomerReturnWorkCommand as command,
  salesCustomerReturnWorkCommand as prepare, validSalesCustomerReturnReason as reason} from './workSalesCustomerReturnDecision'
import {exactNativeClrAmount as amount, isWorkPackageEffectiveDate as date} from './workDecisionApi'
import {COMPANY_A, COMPANY_B} from '../settings/testHarness'
function human() {const value = decode(wire(), COMPANY_A, id, date, amount); if (value === null) throw new Error('Native synthetic wire must decode'); return value}
const body = (approve = false, note: string | null = '  SENTETIK raw note  ') => ({expectedVersion: '9007199254740993', approve, reason: approve ? null : note, delegationId: null})

describe('Sales customer return Work leaf reuses the native full wire (SOURCE_ONLY)', () => {
  it('retains every native field and raw money without adding a private proof or a fake command envelope', () => {
    const w = wire(), h = human()
    expect(CUSTOMER_RETURN_VIEW_FIELDS).toHaveLength(34); expect(CUSTOMER_RETURN_LINE_FIELDS).toHaveLength(14)
    expect(h.customerReturn).toEqual(decodeCustomerReturnView(w.customerReturn, id)); expect(h.customerReturn.totals.netAmount).toBe('200.00')
    expect(valid(fixture(COMPANY_A), COMPANY_A, id, date, amount)).toBe(true)
  })
  it.each(['Approved', 'Finalized', 'Rejected', 'Cancelled'] as const)('requires false booleans for native terminal %s without enabling a new decision', state => {
    const w = wire(state), h = decode(w, COMPANY_A, id, date, amount)
    expect(h).not.toBeNull(); expect(h?.approvalSupported).toBe(false); expect(h?.rejectionSupported).toBe(false)
    expect(valid(fixture(COMPANY_A, state), COMPANY_A, id, date, amount)).toBe(true)
    expect(decode({...w, approvalSupported: true}, COMPANY_A, id, date, amount)).toBeNull()
    expect(decode({...w, rejectionSupported: undefined}, COMPANY_A, id, date, amount)).toBeNull()
    if (h !== null) {expect(prepare(h, true, '', null)).toBeNull(); expect(prepare(h, false, '', null)).toBeNull()}
  })
  it('keeps the complete foreign currency side and signed drift as native text', () => {
    const w = wire('PendingApproval', '2', true), c = decodeCustomerReturnView(w.customerReturn, id)
    expect(c).not.toBeNull(); if (c === null) return
    const h = decode({...w, customerReturn: {...c, totals: {...c.totals, driftAmount: '-0.0100'}}}, COMPANY_A, id, date, amount)
    expect(h?.customerReturn.rate).toBe('34.5000'); expect(h?.customerReturn.totals.driftAmount).toBe('-0.0100')
    expect(h?.customerReturn.functionalTotals?.lineTotalAmount).toBe('8280.00')
  })
  it.each(['0', '01', '9223372036854775807', '9223372036854775808', '2\n', '2\r', '2\r\n'])('rejects a nonincrementable or noncanonical pending version %s', version => {
    expect(decode(wire('PendingApproval', version), COMPANY_A, id, date, amount)).toBeNull(); expect(command({...body(), expectedVersion: version})).toBe(false)
  })
  it('keeps full incrementable Int64 text, without converting a version through Number', () => {
    const h = decode(wire('PendingApproval', '9223372036854775806'), COMPANY_A, id, date, amount)
    expect(h).not.toBeNull(); if (h !== null) expect(prepare(h, true, '', null)?.expectedVersion).toBe('9223372036854775806')
  })
  it.each(['LF', 'CR', 'CRLF', 'CLRmax', 'scale29', 'zeroQuantity', 'lineInt32', 'scaleInt32', 'offset', 'private', 'selfMaker'])('rejects native-boundary malformed %s without partial facts', fault => {
    const h = human(), c = h.customerReturn, l = c.lines[0]
    const next = fault === 'LF' || fault === 'CR' || fault === 'CRLF'
      ? {...c, totals: {...c.totals, netAmount: '200' + (fault === 'LF' ? '\n' : fault === 'CR' ? '\r' : '\r\n')}}
      : fault === 'CLRmax' ? {...c, totals: {...c.totals, netAmount: '79228162514264337593543950336'}}
      : fault === 'scale29' ? {...c, totals: {...c.totals, netAmount: '0.00000000000000000000000000001'}}
      : fault === 'zeroQuantity' ? {...c, lines: [{...l, quantity: '0'}]}
      : fault === 'lineInt32' ? {...c, lines: [{...l, originalLineNo: 2147483648}]}
      : fault === 'scaleInt32' ? {...c, amountScale: 29}
      : fault === 'offset' ? {...c, submittedAtUtc: '2026-11-05T09:10:00+01:00'}
      : fault === 'private' ? {...c, originalDecisionReceipt: {}}
      : {...c, state: 'Approved', approvedBy: c.createdBy, approvedAtUtc: c.submittedAtUtc}
    expect(decode({...h, customerReturn: next}, COMPANY_A, id, date, amount)).toBeNull()
  })
  it.each(['company', 'kind', 'id', 'version', 'pending', 'caption', 'rule', 'ref', 'invoiceLink', 'foreignHuman'])('binds the complete generic source %s', fault => {
    const v = fixture(COMPANY_A)
    const broken = fault === 'company' ? {...v, companyId: COMPANY_B} : fault === 'kind' ? {...v, kind: 'logistics.customer-return.v1'}
      : fault === 'id' ? {...v, itemId: COMPANY_B} : fault === 'version' ? {...v, sourceVersion: '3'} : fault === 'pending' ? {...v, pending: false}
      : fault === 'caption' ? {...v, documentLabel: 'other'} : fault === 'rule' ? {...v, rules: [{...v.rules[0], actual: '1'}]}
      : fault === 'ref' ? {...v, references: [{...v.references[0], recordId: COMPANY_B}, v.references[1]]}
      : fault === 'invoiceLink' ? {...v, references: [v.references[0], {...v.references[1], link: '/satis/siparisler'}]}
      : {...v, logisticsCustomerReturn: {}}
    expect(valid(broken, COMPANY_A, id, date, amount)).toBe(false)
  })
  it('keeps optional rejection notes RAW, including native whitespace, and validates only the native trimmed bound', () => {
    const h = human(), raw = '\u0085  SENTETIK raw note  \u0085'
    expect(prepare(h, false, raw, null)).toEqual(body(false, raw)); expect(prepare(h, true, raw, null)).toEqual(body(true))
    for (const value of [null, '', '  ', 'x'.repeat(4000), '  ' + 'x'.repeat(4000) + '  ']) expect(reason(false, value)).toBe(true)
    expect(reason(false, 'x'.repeat(4001))).toBe(false); expect(reason(false, '\uFEFF' + 'x'.repeat(4000))).toBe(false)
    expect(reason(true, '')).toBe(false); expect(command({...body(), reason: undefined})).toBe(false)
  })
  it('rejects every extra selector and keeps the delegation identity in the frozen four-field request', () => {
    expect(command({...body(), salesPriceFloor: {}})).toBe(false); expect(command({...body(), logisticsCustomerReturn: {}})).toBe(false)
    expect(command({...body(), delegationId: COMPANY_B})).toBe(true); expect(command({...body(), delegationId: 'not-a-guid'})).toBe(false)
  })
})
