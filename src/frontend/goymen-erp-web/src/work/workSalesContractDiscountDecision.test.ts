import {describe, expect, it} from 'vitest'
import {decodeSalesContract} from '../sales/salesContract'
import {COMPANY_A, COMPANY_B} from '../settings/testHarness'
import {exactNativeClrAmount, isWorkPackageEffectiveDate} from './workDecisionApi'
import {decodeWorkSalesContractDiscountPreview as human, validSalesContractDiscountPreview as valid,
  isSalesContractDiscountWorkCommand as command, salesContractDiscountWorkCommand as prepare} from './workSalesContractDiscountDecision'
import {salesContractDiscountFixture as fixture, salesContractDiscountHuman, salesContractDiscountId as id} from './salesContractDiscountDecisionFixture'

const amount = (v: unknown) => exactNativeClrAmount(v, false)
const decode = (v: Record<string, unknown>) => valid(v, COMPANY_A, id, isWorkPackageEffectiveDate, amount)
const record = (v: unknown) => v as Record<string, unknown>
describe('Contract full24/3/4 Work human, native optional prices and canonical four-field decisions', () => {
  it('reuses native shapes while keeping raw text, 100ns timestamps and retained Int64 versions lossless', () => {
    const value = fixture(COMPANY_A), before = JSON.stringify(value), h = human(value.salesContractDiscount, COMPANY_A.toUpperCase(), id.toUpperCase(), isWorkPackageEffectiveDate, amount)
    expect(decode(value)).toBe(true); expect(h).not.toBeNull(); expect(Object.keys(h!)).toHaveLength(4)
    expect(Object.keys(h!.contract)).toHaveLength(24); expect(Object.keys(h!.contract.installments[0])).toHaveLength(3); expect(Object.keys(h!.contract.discountRules[0])).toHaveLength(4)
    expect(h!.contract.version).toBe('9007199254740993'); expect(h!.contract.installments[0].amount).toBe('1500.00')
    expect(h!.contract.clauses).toBe('  SENTETIK sözleşme maddesi\nİkinci satır  '); expect(h!.contract.createdAtUtc).toBe('2026-11-28T09:00:00.0000001Z')
    expect(decodeSalesContract(value.salesContractDiscount.contract)).toBeNull(); expect(decodeSalesContract({...value.salesContractDiscount.contract, version: '1'})).not.toBeNull()
    expect(JSON.stringify(value)).toBe(before)
  })
  it.each(['PendingDiscountApproval', 'Active', 'DiscountRejected'] as const)('shows maximum-version %s without incrementing actions or inventing chronology', state => {
    const v = fixture(COMPANY_A, '9223372036854775807', state)
    expect(decode(v)).toBe(true); expect(v.salesContractDiscount.approvalSupported).toBe(false); expect(v.salesContractDiscount.rejectionSupported).toBe(false)
    expect(prepare(v.salesContractDiscount, true, '', null)).toBeNull(); expect(prepare(v.salesContractDiscount, false, 'ret', null)).toBeNull()
  })
  it.each(['0001-01-01', '0099-12-31', '2000-02-29', '9999-12-31'])('keeps exact native DateOnly boundary %s', date => {
    const v = fixture(COMPANY_A); Object.assign(v.salesContractDiscount.contract, {validFrom: date, validThrough: null})
    v.salesContractDiscount.contract.installments.forEach(x => Object.assign(x, {dueDate: date}))
    expect(decode(v)).toBe(true)
  })
  it('withholds every amount and rate while retaining all rows, labels, dates and presence flags', () => {
    const v = fixture(COMPANY_A, '1', 'PendingDiscountApproval', false); expect(decode(v)).toBe(true)
    expect(v.salesContractDiscount.contract.installments.map(x => x.amount)).toEqual([null, null]); expect(v.salesContractDiscount.contract.discountRules.map(x => x.ratePercent)).toEqual([null, null])
    Object.assign(v.salesContractDiscount.contract, {discountLimitDefined: true})
    Object.assign(v.rules[0], {ruleCode: 'sales.contract-discount-over-limit'})
    expect(decode(v)).toBe(true)
    Object.assign(v.rules[0], {threshold: '1'}); expect(decode(v)).toBe(false)
  })
  it('retains all bounded installments and rule rows while accepting the native truncated rule summary without calculating it', () => {
    const v = fixture(COMPANY_A), c = v.salesContractDiscount.contract
    Object.assign(c, {installments: Array.from({length: 120}, (_, i) => ({installmentNo: i + 1, dueDate: '2026-12-01', amount: '1.00'})),
      discountRules: Array.from({length: 50}, (_, i) => ({ruleNo: i + 1, kind: 'Line', label: `SENTETIK ${i}`, ratePercent: '5'})), discountLimitDefined: true, discountLimitPercent: '1'})
    Object.assign(v, {rules: Array.from({length: 8}, () => ({ruleCode: 'sales.contract-discount-over-limit', threshold: '1', actual: '5', deviation: '4', unit: '%'}))})
    expect(decode(v)).toBe(true); expect(human(v.salesContractDiscount, COMPANY_A, id, isWorkPackageEffectiveDate, amount)?.contract.installments).toHaveLength(120)
    Object.assign(v, {rules: [...v.rules, v.rules[0]]}); expect(decode(v)).toBe(false)
  })
  it.each(['79228162514264337593543950335', '0.0000000000000000000000000001', '1.0000000000000000000000000000'])('accepts exactly representable CLR amount text %s unchanged', value => {
    const v = fixture(COMPANY_A), scale = value.includes('.') ? value.split('.')[1].length : 0
    Object.assign(v.salesContractDiscount.contract, {moneyScale: scale, plannedTotalAmount: value, installments: [{installmentNo: 1, dueDate: '2026-12-01', amount: value}]})
    expect(decode(v)).toBe(true); expect(v.salesContractDiscount.contract.plannedTotalAmount).toBe(value)
  })
  it.each(['root-extra', 'human-extra', 'native-extra', 'installment-extra', 'rule-extra', 'missing', 'company', 'id', 'party', 'contract-zero', 'contract-overflow', 'scale-negative', 'scale29', 'scale-large', 'version-number', 'version-pad', 'version-overflow', 'date-normalized', 'due-normalized', 'utc-normalized', 'utc-24', 'utc-LF', 'creator-long', 'creator-pad', 'state', 'decision', 'pending', 'approve', 'reject', 'source-version', 'caption', 'ref-id', 'ref-link', 'ref-role', 'threshold', 'actual', 'deviation', 'unit', 'rule-code', 'foreign-human', 'amount-overflow', 'amount-zero', 'total-overflow', 'withheld-partial', 'rate-overflow'])('fails closed for %s', fault => {
    const v = structuredClone(fixture(COMPANY_A)) as unknown as Record<string, unknown>, h = record(v.salesContractDiscount), c = record(h.contract)
    const installments = c.installments as Record<string, unknown>[], rules = c.discountRules as Record<string, unknown>[], ref = (v.references as Record<string, unknown>[])[0], rule = (v.rules as Record<string, unknown>[])[0]
    if (fault === 'root-extra') v.originalDecisionReceipt = {}; if (fault === 'human-extra') h.previewDigest = 'a'.repeat(64); if (fault === 'native-extra') c.source = {}; if (fault === 'installment-extra') installments[0].proof = {}; if (fault === 'rule-extra') rules[0].payloadHash = 'a'.repeat(64); if (fault === 'missing') delete c.isInEffectToday
    if (fault === 'company') h.companyId = COMPANY_B; if (fault === 'id') c.id = COMPANY_B; if (fault === 'party') c.partyId = 'not-guid'; if (fault === 'contract-zero') c.contractNo = 0; if (fault === 'contract-overflow') c.contractNo = 2147483648
    if (fault === 'scale-negative') c.moneyScale = -1; if (fault === 'scale29') c.moneyScale = 29; if (fault === 'scale-large') c.moneyScale = 1000000000
    if (fault === 'version-number') c.version = 1; if (fault === 'version-pad') c.version = '01'; if (fault === 'version-overflow') c.version = '9223372036854775808'
    if (fault === 'date-normalized') c.validFrom = '2026-02-30'; if (fault === 'due-normalized') installments[0].dueDate = '2026-02-30'; if (fault === 'utc-normalized') c.createdAtUtc = '2026-02-30T09:00:00Z'; if (fault === 'utc-24') c.createdAtUtc = '2026-11-28T24:00:00Z'; if (fault === 'utc-LF') c.createdAtUtc = String(c.createdAtUtc) + '\n'
    if (fault === 'creator-long') c.createdBy = 'x'.repeat(129); if (fault === 'creator-pad') c.createdBy = ' maker '; if (fault === 'state') c.state = 'Approved'; if (fault === 'decision') c.decidedBy = 'checker'; if (fault === 'pending') v.pending = false; if (fault === 'approve') h.approvalSupported = false; if (fault === 'reject') v.rejectionSupported = false; if (fault === 'source-version') v.sourceVersion = '1'; if (fault === 'caption') v.documentLabel = 'Other'
    if (fault === 'ref-id') ref.recordId = COMPANY_B; if (fault === 'ref-link') ref.link = '/satis/sozlesmeler'; if (fault === 'ref-role') ref.role = 'sales-order-draft'; if (['threshold', 'actual', 'deviation', 'unit'].includes(fault)) rule[fault] = '1'; if (fault === 'rule-code') rule.ruleCode = 'sales.other'; if (fault === 'foreign-human') v.salesDiscount = {}
    if (fault === 'amount-overflow') installments[0].amount = '79228162514264337593543950336.00'; if (fault === 'amount-zero') installments[0].amount = '0.00'; if (fault === 'total-overflow') c.plannedTotalAmount = '79228162514264337593543950336.00'; if (fault === 'withheld-partial') c.amountDisclosure = 'Withheld'; if (fault === 'rate-overflow') rules[0].ratePercent = '101'
    expect(decode(v)).toBe(false)
  })
  it.each(['self', 'missing', 'utc', 'version', 'reason'])('rejects inconsistent terminal decision %s', fault => {
    const v = fixture(COMPANY_A, '2', 'Active'), c = v.salesContractDiscount.contract
    Object.assign(c, fault === 'self' ? {decidedBy: c.createdBy} : fault === 'missing' ? {decidedAtUtc: null} : fault === 'utc' ? {decidedAtUtc: '2026-02-30T10:00:00Z'} : fault === 'version' ? {version: '1'} : {rejectionReason: 'ret'})
    expect(decode(v)).toBe(false)
  })
  it('prepares trim before confirmation and preserves precisely that version/body for retry; direct noncanonical requests fail', () => {
    const h = salesContractDiscountHuman(COMPANY_A), raw = '  SENTETIK gerekçe\nİkinci satır  ', body = prepare(h, false, raw, null)
    expect(body).toEqual({expectedVersion: '9007199254740993', approve: false, reason: raw.trim(), delegationId: null})
    expect(Object.keys(body!)).toHaveLength(4); expect(command(body)).toBe(true); expect(command({...body, reason: raw})).toBe(false)
    expect(prepare(h, true, raw, null)).toEqual({...body, approve: true, reason: null}); expect(prepare(h, false, ' ', null)).toBeNull()
    expect(prepare(h, false, 'x'.repeat(4000), null)).not.toBeNull(); expect(prepare(h, false, 'x'.repeat(4001), null)).toBeNull()
    for (const invalid of [{...body, expectedVersion: '9223372036854775807'}, {...body, salesDiscount: {}}, {...body, reason: null}, {...body, delegationId: 'bad'}, {...body, expectedVersion: '01'}]) expect(command(invalid)).toBe(false)
  })
})
