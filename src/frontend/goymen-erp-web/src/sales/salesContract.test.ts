/**
 * S-11 (SAT-D28, NS-13, SAT-D346): the strict sales contract decoder. A body is believed only when every key is present and typed,
 * the plan and the rules are numbered in order, amounts are the money profile's text at its scale, the limit flag and value agree,
 * and the state agrees with the approval facts (no rule never needs approval; rules without a configured limit always do; the
 * creator never decides their own discount). SAT-D362: the end date may be null (open-ended) but never before the start. SAT-D361:
 * amounts are present exactly when the disclosure is Visible and null exactly when it is Withheld. Every identity here is synthetic.
 */
import { describe, expect, it } from 'vitest'
import {
  SALES_CONTRACT_LIMITS,
  decodeSalesContract,
  decodeSalesContractList,
  isAmountAtScale,
  isRateText,
  salesContractDecisionPath,
  salesContractPath,
} from './salesContract'

type Body = Record<string, any>

const ID = '5c5c5c5c-5c5c-4c5c-8c5c-5c5c5c5c5c5c'
const OTHER_ID = '6d6d6d6d-6d6d-4d6d-8d6d-6d6d6d6d6d6d'

const activeWithoutDiscount = (overrides: Body = {}): Body => ({
  id: ID,
  contractNo: 1,
  partyId: '11111111-1111-4111-8111-111111111111',
  companyAccountId: '22222222-2222-4222-8222-222222222222',
  currencyId: '33333333-3333-4333-8333-333333333333',
  moneyScale: 2,
  state: 'Active',
  version: '1',
  validFrom: '2026-11-01',
  validThrough: '2027-10-31',
  amountDisclosure: 'Visible',
  installments: [
    { installmentNo: 1, dueDate: '2026-12-01', amount: '1500.00' },
    { installmentNo: 2, dueDate: '2027-01-01', amount: '1500.25' },
  ],
  plannedTotalAmount: '3000.25',
  discountRules: [],
  clauses: 'SENTETIK teslim maddesi',
  discountApprovalRequired: false,
  discountLimitDefined: false,
  discountLimitPercent: null,
  isInEffectToday: true,
  createdBy: 'synthetic-seller',
  createdAtUtc: '2026-11-15T08:00:00Z',
  decidedBy: null,
  decidedAtUtc: null,
  rejectionReason: null,
  ...overrides,
})

const pendingWithoutLimit = (overrides: Body = {}): Body => activeWithoutDiscount({
  state: 'PendingDiscountApproval',
  discountRules: [
    { ruleNo: 1, kind: 'Line', label: 'SENTETIK hacim', ratePercent: '5' },
    { ruleNo: 2, kind: 'Total', label: 'SENTETIK peşin', ratePercent: '2.5' },
  ],
  discountApprovalRequired: true,
  isInEffectToday: false,
  ...overrides,
})

const decidedBy = { decidedBy: 'synthetic-manager', decidedAtUtc: '2026-11-15T09:00:00Z' }

describe('S-11 sales contract decoder', () => {
  it('reads an active contract with no discount, which never needs approval', () => {
    const contract = decodeSalesContract(activeWithoutDiscount())
    expect(contract).not.toBeNull()
    expect(contract?.installments.map((item) => item.amount)).toEqual(['1500.00', '1500.25'])
    expect(contract?.discountApprovalRequired).toBe(false)
  })

  it('reads rules recorded with no configured limit as pending approval and refuses them claiming no approval (SAT-D346)', () => {
    expect(decodeSalesContract(pendingWithoutLimit())?.discountLimitPercent).toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({ state: 'Active', discountApprovalRequired: false, isInEffectToday: true }))).toBeNull()
  })

  it('reads a configured limit only when the flag and the value agree', () => {
    const within = activeWithoutDiscount({
      discountRules: [{ ruleNo: 1, kind: 'Line', label: 'SENTETIK hacim', ratePercent: '5' }],
      discountLimitDefined: true,
      discountLimitPercent: '10',
    })
    expect(decodeSalesContract(within)?.discountLimitPercent).toBe('10')
    expect(decodeSalesContract({ ...within, discountLimitPercent: null })).toBeNull()
    expect(decodeSalesContract({ ...within, discountLimitDefined: false })).toBeNull()
    expect(decodeSalesContract({ ...within, discountLimitPercent: '10.0' })).toBeNull()
  })

  it('refuses a body that claims approval with no rule', () => {
    expect(decodeSalesContract(activeWithoutDiscount({ discountApprovalRequired: true }))).toBeNull()
  })

  it('reads an approved and a rejected contract only when decided by someone other than the creator', () => {
    expect(decodeSalesContract(pendingWithoutLimit({ state: 'Active', version: '2', isInEffectToday: true, ...decidedBy }))).not.toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({
      state: 'DiscountRejected', version: '2', rejectionReason: 'SENTETIK oran yüksek', ...decidedBy,
    }))).not.toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({
      state: 'Active', version: '2', isInEffectToday: true, decidedBy: 'synthetic-seller', decidedAtUtc: '2026-11-15T09:00:00Z',
    }))).toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({ state: 'DiscountRejected', version: '2', ...decidedBy }))).toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({ state: 'DiscountRejected', version: '2', rejectionReason: 'x', ...decidedBy,
      isInEffectToday: true }))).toBeNull()
  })

  it('refuses a pending contract that is decided, in effect or past its first version', () => {
    expect(decodeSalesContract(pendingWithoutLimit({ ...decidedBy }))).toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({ isInEffectToday: true }))).toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({ version: '2' }))).toBeNull()
  })

  it('refuses an extra or a missing key, a numeric amount, an amount off the scale and a numeric version', () => {
    expect(decodeSalesContract({ ...activeWithoutDiscount(), extra: 1 })).toBeNull()
    const { clauses: _clauses, ...missing } = activeWithoutDiscount()
    expect(decodeSalesContract(missing)).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ plannedTotalAmount: 3000.25 }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ plannedTotalAmount: '3000.2' }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ version: 1 }))).toBeNull()
  })

  it('refuses a plan out of order, misnumbered, empty, or with a zero amount', () => {
    const [first, second] = activeWithoutDiscount().installments as Body[]
    expect(decodeSalesContract(activeWithoutDiscount({ installments: [{ ...second, installmentNo: 1 }, { ...first, installmentNo: 2 }] })))
      .toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ installments: [first, { ...second, installmentNo: 3 }] }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ installments: [] }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ installments: [{ ...first, amount: '0.00' }] }))).toBeNull()
  })

  it('refuses two total discounts, an unknown kind, a zero rate and a rate above one hundred', () => {
    const rule = { ruleNo: 1, kind: 'Total', label: 'SENTETIK a', ratePercent: '1' }
    expect(decodeSalesContract(pendingWithoutLimit({ discountRules: [rule, { ...rule, ruleNo: 2, label: 'SENTETIK b' }] }))).toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({ discountRules: [{ ...rule, kind: 'Header' }] }))).toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({ discountRules: [{ ...rule, ratePercent: '0' }] }))).toBeNull()
    expect(decodeSalesContract(pendingWithoutLimit({ discountRules: [{ ...rule, ratePercent: '100.5' }] }))).toBeNull()
  })

  it('refuses validity that ends before it starts and clauses above the bound', () => {
    expect(decodeSalesContract(activeWithoutDiscount({ validThrough: '2026-10-31' }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ clauses: 'x'.repeat(SALES_CONTRACT_LIMITS.clauses + 1) }))).toBeNull()
  })

  it('reads an open-ended contract whose end date is null (SAT-D362)', () => {
    const contract = decodeSalesContract(activeWithoutDiscount({ validThrough: null }))
    expect(contract).not.toBeNull()
    expect(contract?.validThrough).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ validThrough: '2026-11-01' }))?.validThrough).toBe('2026-11-01')
    expect(decodeSalesContract(activeWithoutDiscount({ validThrough: '' }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ validThrough: 20271031 }))).toBeNull()
    const { validThrough: _validThrough, ...missing } = activeWithoutDiscount()
    expect(decodeSalesContract(missing)).toBeNull()
  })

  it('reads withheld amounts and discount percentages as null while terms and dates stay visible (SAT-D361/D363)', () => {
    const [first, second] = activeWithoutDiscount().installments as Body[]
    const withheld = pendingWithoutLimit({
      amountDisclosure: 'Withheld',
      installments: [{ ...first, amount: null }, { ...second, amount: null }],
      plannedTotalAmount: null,
      discountRules: (pendingWithoutLimit().discountRules as Body[]).map((rule) => ({ ...rule, ratePercent: null })),
      discountLimitDefined: true,
      discountLimitPercent: null,
    })
    const contract = decodeSalesContract(withheld)
    expect(contract).not.toBeNull()
    expect(contract?.amountDisclosure).toBe('Withheld')
    expect(contract?.plannedTotalAmount).toBeNull()
    expect(contract?.installments.map((item) => [item.dueDate, item.amount])).toEqual([['2026-12-01', null], ['2027-01-01', null]])
    expect(contract?.discountRules.map((rule) => rule.ratePercent)).toEqual([null, null])
    expect(contract?.discountLimitDefined).toBe(true)
    expect(contract?.discountLimitPercent).toBeNull()
    expect(decodeSalesContract({ ...withheld, discountLimitPercent: '10' })).toBeNull()
    expect(decodeSalesContract({ ...withheld, discountRules: pendingWithoutLimit().discountRules })).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount())?.amountDisclosure).toBe('Visible')
  })

  it('refuses amounts that disagree with the disclosure or an unknown disclosure (SAT-D361)', () => {
    const [first, second] = activeWithoutDiscount().installments as Body[]
    const nullPlan = [{ ...first, amount: null }, { ...second, amount: null }]
    expect(decodeSalesContract(activeWithoutDiscount({ amountDisclosure: 'Withheld' }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ amountDisclosure: 'Withheld', installments: nullPlan }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ amountDisclosure: 'Withheld', plannedTotalAmount: null,
      installments: [first, { ...second, amount: null }] }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ plannedTotalAmount: null }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ installments: [first, { ...second, amount: null }] }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ amountDisclosure: 'Hidden' }))).toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount({ amountDisclosure: null }))).toBeNull()
    const { amountDisclosure: _disclosure, ...missing } = activeWithoutDiscount()
    expect(decodeSalesContract(missing)).toBeNull()
  })

  it('pins a detail read to the requested id', () => {
    expect(decodeSalesContract(activeWithoutDiscount(), ID.toUpperCase())).not.toBeNull()
    expect(decodeSalesContract(activeWithoutDiscount(), OTHER_ID)).toBeNull()
  })

  it('reads a list only as exactly { items } with unique ids and contract numbers', () => {
    const second = pendingWithoutLimit({ id: OTHER_ID, contractNo: 2 })
    expect(decodeSalesContractList({ items: [activeWithoutDiscount(), second] })).toHaveLength(2)
    expect(decodeSalesContractList({ items: [] })).toEqual([])
    expect(decodeSalesContractList({ items: [activeWithoutDiscount(), { ...second, id: ID }] })).toBeNull()
    expect(decodeSalesContractList({ items: [activeWithoutDiscount(), { ...second, contractNo: 1 }] })).toBeNull()
    expect(decodeSalesContractList({ items: [], next: null })).toBeNull()
    expect(decodeSalesContractList([activeWithoutDiscount()])).toBeNull()
  })

  it('checks amounts and rates as the server writes them', () => {
    expect(isAmountAtScale('10', 0, true)).toBe(true)
    expect(isAmountAtScale('10.000', 3, true)).toBe(true)
    expect(isAmountAtScale('010.00', 2, true)).toBe(false)
    expect(isAmountAtScale('0.00', 2, true)).toBe(false)
    expect(isRateText('100', true)).toBe(true)
    expect(isRateText('12.3456', true)).toBe(true)
    expect(isRateText('12.34567', true)).toBe(false)
    expect(isRateText('5.50', true)).toBe(false)
    expect(isRateText('0', false)).toBe(true)
    expect(isRateText('0', true)).toBe(false)
  })

  it('encodes the contract id in its paths', () => {
    expect(salesContractPath(ID)).toBe(`/sales/contracts/${ID}`)
    expect(salesContractDecisionPath(ID, 'discount-rejection')).toBe(`/sales/contracts/${ID}/discount-rejection`)
  })
})
