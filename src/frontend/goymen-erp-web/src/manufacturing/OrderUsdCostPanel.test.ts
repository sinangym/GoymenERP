import { describe, expect, it } from 'vitest'
import { decodeUsdCost } from './OrderUsdCostPanel'
const order = 'a0000000-0000-0000-0000-00000000000a'
describe('independent production USD display', () => {
  it('keeps unavailable USD empty and passes exact recorded decimal text without computing TL or SMM', () => {
    const missing = { orderId: order, orderVersion: 4, status: 'unavailable', costPerKgUsd: null, inputTotalUsd: null, distributedTotalUsd: null, lines: [], scrapRates: [], reasonCode: 'scrap_usd_exchange_rate_unavailable' }
    expect(decodeUsdCost(missing, order)).not.toBeNull()
    expect(decodeUsdCost({ ...missing, costPerKgUsd: '0' }, order)).toBeNull()
    const actual = { ...missing, status: 'available', costPerKgUsd: '1.1056', inputTotalUsd: '100', distributedTotalUsd: '100', reasonCode: null,
      lines: [{ outputLabelId: order, outputClass: 'GoodProduct', kilograms: '90', unitCostPerKgUsd: '1.1056', amountUsd: '99.5', separateScrapExpenseUsd: null, remainderUsd: '0' }] }
    expect(decodeUsdCost(actual, order)?.costPerKgUsd).toBe('1.1056')
    expect(decodeUsdCost({ ...actual, orderId: 'different' }, order)).toBeNull()
  })
})
const without = (value: object, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))
describe('production USD exact key set (SENTETIK)', () => {
  it('rejects an extra or a missing key on the view, every line and every rate', () => {
    const line = { outputLabelId: order, outputClass: 'GoodProduct', kilograms: '90', unitCostPerKgUsd: '1.1056', amountUsd: '99.5', separateScrapExpenseUsd: null, remainderUsd: '0' }
    const rate = { weighingDay: '2026-11-17', bulletinDay: '2026-11-16', tryPerUsd: '40.5', recordId: 'b0000000-0000-0000-0000-00000000000b', recordVersion: '1', source: 'Tcmb' }
    const view = { orderId: order, orderVersion: 4, status: 'available', costPerKgUsd: '1.1056', inputTotalUsd: '100', distributedTotalUsd: '100', lines: [line], scrapRates: [rate], reasonCode: null }
    expect(decodeUsdCost(view, order)).toEqual(view)
    expect(decodeUsdCost({ ...view, extra: 1 }, order)).toBeNull()
    for (const key of Object.keys(view)) expect(decodeUsdCost(without(view, key), order)).toBeNull()
    expect(decodeUsdCost({ ...view, lines: [{ ...line, extra: 1 }] }, order)).toBeNull()
    for (const key of Object.keys(line)) expect(decodeUsdCost({ ...view, lines: [without(line, key)] }, order)).toBeNull()
    expect(decodeUsdCost({ ...view, scrapRates: [{ ...rate, extra: 1 }] }, order)).toBeNull()
    for (const key of Object.keys(rate)) expect(decodeUsdCost({ ...view, scrapRates: [without(rate, key)] }, order)).toBeNull()
  })
})
