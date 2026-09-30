/**
 * AT-025 wire contract: each section's decoder takes exactly its own members, refuses a page naming the other section,
 * refuses a row of the other section's shape, and refuses any price member. Pinned to the backend's wire records
 * (`LogisticsOperationsWorkContractsV1.cs`) by their TEXT, so a backend rename turns this red. NOT_ENV.
 */

import { describe, expect, it } from 'vitest'
import {
  PURCHASE_WORK_KEYS,
  SALES_WORK_KEYS,
  decodePurchaseWorkPage,
  decodeSalesWorkPage,
} from './operationsWorkContract'
import { purchaseRow, salesRow } from './testHarness'

const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const fs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const path = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }
const contractText = fs.readFileSync(
  path.resolve(
    nodeProcess.cwd(),
    '../../backend/Modules/Logistics/GoymenERP.Modules.Logistics.Application/Contracts/LogisticsOperationsWorkContractsV1.cs',
  ),
  'utf8',
)

/** The camelCase member names of a positional C# record, as System.Text.Json's web defaults write them. */
function recordMembers(name: string): string[] {
  const start = contractText.indexOf(`public sealed record ${name}(`)
  if (start < 0) throw new Error(`no record ${name}`)
  const body = contractText.slice(start + `public sealed record ${name}(`.length, contractText.indexOf(');', start))
  return body
    .split(',')
    .map((part) => part.trim().split(/\s+/).at(-1) as string)
    .map((member) => member.charAt(0).toLowerCase() + member.slice(1))
}

describe('AT-025 operations work wire contract', () => {
  it('takes exactly the members the backend records declare, and the page names its section', () => {
    expect([...SALES_WORK_KEYS]).toEqual(recordMembers('LogisticsSalesWorkItemView'))
    expect([...PURCHASE_WORK_KEYS]).toEqual(recordMembers('LogisticsPurchaseWorkItemView'))
    expect(recordMembers('LogisticsSalesWorkPage')).toEqual(['section', 'items', 'nextCursor'])
    expect(recordMembers('LogisticsPurchaseWorkPage')).toEqual(['section', 'items', 'nextCursor'])
    expect(contractText).toContain('public const string Sales = "Sales";')
    expect(contractText).toContain('public const string Purchase = "Purchase";')
  })

  it('decodes each section only as itself', () => {
    const sales = decodeSalesWorkPage({ section: 'Sales', items: [salesRow()], nextCursor: 'c2' })
    expect(sales?.items[0].customerName).toBe('Sentetik Müşteri')
    expect(sales?.nextCursor).toBe('c2')
    const purchase = decodePurchaseWorkPage({ section: 'Purchase', items: [purchaseRow()], nextCursor: null })
    expect(purchase?.items[0].remaining).toBe('60')
  })

  it('never shows a row in the wrong list: the other section, the other shape, or a mixed page is refused', () => {
    // A purchase page answered to the sales read, and the reverse.
    expect(decodeSalesWorkPage({ section: 'Purchase', items: [], nextCursor: null })).toBeNull()
    expect(decodePurchaseWorkPage({ section: 'Sales', items: [], nextCursor: null })).toBeNull()
    // A row of the other section's shape under the right section name.
    expect(decodeSalesWorkPage({ section: 'Sales', items: [purchaseRow()], nextCursor: null })).toBeNull()
    expect(decodePurchaseWorkPage({ section: 'Purchase', items: [salesRow()], nextCursor: null })).toBeNull()
    // One mixed page.
    expect(decodeSalesWorkPage({ section: 'Sales', items: [salesRow(), purchaseRow()], nextCursor: null })).toBeNull()
    // A page without its section.
    expect(decodeSalesWorkPage({ items: [salesRow()], nextCursor: null })).toBeNull()
  })

  it('refuses a price member, a repeated line and a purchase line with nothing left to receive', () => {
    expect(decodeSalesWorkPage({ section: 'Sales', items: [salesRow({ unitPrice: '30000' })], nextCursor: null })).toBeNull()
    expect(decodePurchaseWorkPage({ section: 'Purchase', items: [purchaseRow({ price: '1' })], nextCursor: null })).toBeNull()
    expect(decodeSalesWorkPage({ section: 'Sales', items: [salesRow(), salesRow()], nextCursor: null })).toBeNull()
    expect(decodePurchaseWorkPage({ section: 'Purchase', items: [purchaseRow({ remaining: '0' })], nextCursor: null })).toBeNull()
    expect(decodeSalesWorkPage({ section: 'Sales', items: [salesRow({ delivered: '-1' })], nextCursor: null })).toBeNull()
  })
})
