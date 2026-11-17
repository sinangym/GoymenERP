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
