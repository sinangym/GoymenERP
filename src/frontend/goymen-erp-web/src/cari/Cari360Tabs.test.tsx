/**
 * ALC-S13 / UI-K09 — the Cari 360 tabs read only their owner ports: Sales' open-order report for this account,
 * Purchasing's supplier-filtered orders and Finance's party-account view. Capability gating, lazy reads, the stated
 * refusals, withheld amounts and foreign rows refused whole. Component evidence only (SENTETIK).
 */

import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Cari360Tabs, type Cari360TabsProps } from './Cari360Tabs'
import { CARI_360_TEXT } from './cari360Text'
import openOrdersResponse from '../sales/examples/open-orders-report-response.json'
import { PARTY_ACCOUNT_TOO_MANY_CODE } from '../finance/partyAccountsHttp'
import { BALANCE_COLUMN_BALANCE, BALANCE_EMPTY } from '../finance/partyLedgerText'
import { ITEMS_EMPTY, ITEMS_TITLE, ITEMS_TOO_MANY_FACTS, ITEMS_TOO_MANY_OPEN } from '../finance/partyAccountsText'
import { LEDGER_TRY } from '../finance/partyLedgerFixtures'
import { PURCHASE_ORDER_STATUS_LABEL } from '../purchasing/purchasingText'
import { COMPANY_A, renderFinance } from '../finance/testHarness'

const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))

const T = CARI_360_TEXT
/** The synthetic report example's customer, so its groups belong to the account asked for. */
const PARTY = '33333333-3333-4333-8333-333333333333'
const ACCOUNT = '44444444-4444-4444-8444-444444444444'
const OTHER_ACCOUNT = '55555555-5555-4555-8555-555555555555'
const ORDER = 'd1000000-0000-4000-8000-0000000000d1'
const ITEM = 'c9000000-0000-4000-8000-000000000009'
const SOURCE = 'ca000000-0000-4000-8000-00000000000a'

const ALL: Pick<Cari360TabsProps, 'canViewSales' | 'canListPurchaseOrders' | 'canOpenPurchaseOrder' | 'canViewFinance'> = {
  canViewSales: true, canListPurchaseOrders: true, canOpenPurchaseOrder: true, canViewFinance: true,
}

function mount(caps: Partial<typeof ALL> = ALL) {
  return renderFinance(
    <Cari360Tabs partyId={PARTY} companyAccountId={ACCOUNT} companyId={COMPANY_A} namespace="ns-a"
      canViewSales={caps.canViewSales ?? false} canListPurchaseOrders={caps.canListPurchaseOrders ?? false}
      canOpenPurchaseOrder={caps.canOpenPurchaseOrder ?? false} canViewFinance={caps.canViewFinance ?? false} />,
    { port: null, router: true },
  )
}

function reply(body: unknown, status = 200) {
  const fetchMock = vi.fn(async (..._args: unknown[]) => new Response(JSON.stringify(body), {
    status, headers: { 'Content-Type': 'application/json' },
  }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function open(tab: string) {
  fireEvent.click(screen.getByRole('tab', { name: tab }))
}

const money = (amount: string) => ({ amount, currencyId: LEDGER_TRY })
const visible = (amount: string) => ({ disclosure: 'Visible', money: money(amount) })
const hidden = (disclosure: 'Redacted' | 'Unavailable') => ({ disclosure, money: null })

function account(disclosure: 'Visible' | 'Redacted' | 'Unavailable' = 'Visible', openItems: unknown[] = [], withCurrency = true) {
  const amount = (value: string) => (disclosure === 'Visible' ? visible(value) : hidden(disclosure))
  return {
    companyId: COMPANY_A,
    partyId: PARTY,
    companyAccountId: ACCOUNT,
    currencies: withCurrency ? [{
      currencyId: LEDGER_TRY,
      debitTotal: amount('1000.10'),
      creditTotal: amount('0'),
      balance: disclosure === 'Visible' ? { side: 'Debit', amount: visible('1000.10') } : { side: null, amount: hidden(disclosure) },
      openReceivable: amount('1000.10'),
      openPayable: amount('0'),
    }] : [],
    openItemsDisclosure: disclosure,
    openItems,
  }
}

const openItem = {
  openItemId: ITEM, direction: 'Receivable', sourceId: SOURCE, currencyId: LEDGER_TRY, dueOn: '2026-11-30',
  originalAmount: money('1000.10'), outstandingAmount: money('600.00'),
}

function purchaseOrder(over: Record<string, unknown> = {}) {
  return {
    id: ORDER, companyId: COMPANY_A, status: 'Draft', version: '1', supplierPartyId: PARTY, companyAccountId: ACCOUNT,
    sourceRequestId: null, effectiveOn: '2026-09-22', createdAtUtc: '2026-09-22T10:00:00Z',
    updatedAtUtc: '2026-09-22T10:00:00Z', lineCount: 2,
    supplierDisplay: { partyId: PARTY, code: 'CAR-000101', legalName: 'SENTETIK Tedarikçi A.Ş.', tradeName: null },
    ...over,
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
  boundary.report.mockClear()
})

describe('Cari 360 tabs: gating and lazy reads', () => {
  it('renders nothing without any owner capability', () => {
    const fetchMock = reply({})
    const { container } = mount({})
    expect(container.querySelector('[data-section="cari-360"]')).toBeNull()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('offers only the tabs whose owner capability is held, and reads nothing until one is picked', () => {
    const fetchMock = reply({})
    mount({ canViewFinance: true })
    expect(screen.getAllByRole('tab').map((tab) => tab.textContent)).toEqual([T.tab.finance])
    expect(screen.getByTestId('cari-360-pick').textContent).toBe(T.pick)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('moves between tabs with the arrow keys and selects the focused one', async () => {
    reply({ items: [], nextCursor: null })
    mount()
    const tabs = screen.getAllByRole('tab')
    expect(tabs.map((tab) => tab.getAttribute('tabindex'))).toEqual(['0', '-1', '-1'])
    fireEvent.keyDown(tabs[0], { key: 'ArrowRight' })
    await waitFor(() => expect(screen.getByRole('tab', { name: T.tab.purchasing }).getAttribute('aria-selected')).toBe('true'))
    expect(document.activeElement).toBe(screen.getByRole('tab', { name: T.tab.purchasing }))
    expect(screen.getByRole('tabpanel').getAttribute('data-cari-360-panel')).toBe('purchasing')
  })
})

describe('Cari 360 Satış tab', () => {
  it('shows the report groups for this account, the too-many and incomplete marks, and the owner link', async () => {
    const fetchMock = reply(openOrdersResponse)
    mount()
    open(T.tab.sales)
    const table = await screen.findByRole('table', { name: T.sales.caption })
    expect(within(table).getAllByRole('row')).toHaveLength(4)
    expect(table.textContent).toContain('250.5 kg')
    expect(table.textContent).toContain('10 adet')
    expect(table.textContent).toContain(T.sales.tooManyOrders(1500))
    expect(table.textContent).toContain(T.sales.incomplete)
    expect(screen.getByTestId('cari-360-sales-more').textContent).toBe(T.sales.more)
    const link = screen.getByTestId('cari-360-sales-report-link')
    expect(link.getAttribute('href')).toBe(`/satis/raporlar?rapor=open-orders&cari=${ACCOUNT}`)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toContain('/sales/reports/open-orders')
    expect(url).toContain(`companyAccountId=${ACCOUNT}`)
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY_A)
  })

  it('says "none" only for a final empty page, and never when the bounded scan continues', async () => {
    reply({ measure: 'ApprovedMinusShipped', groups: [], nextCursor: null })
    const first = mount({ canViewSales: true })
    open(T.tab.sales)
    expect((await screen.findByTestId('cari-360-sales-empty')).textContent).toBe(T.sales.empty)
    first.unmount()

    reply({ measure: 'ApprovedMinusShipped', groups: [], nextCursor: 'c2Nhbi1jb250aW51ZXM' })
    mount({ canViewSales: true })
    open(T.tab.sales)
    expect((await screen.findByTestId('cari-360-sales-scan-continues')).textContent).toBe(T.sales.scanContinues)
    expect(screen.queryByTestId('cari-360-sales-empty')).toBeNull()
    expect(screen.getByTestId('cari-360-sales-report-link')).toBeTruthy()
  })

  it('refuses a page holding another account\'s group: no rows at all', async () => {
    const foreign = structuredClone(openOrdersResponse)
    foreign.groups[1].customer.companyAccountId = OTHER_ACCOUNT
    reply(foreign)
    mount({ canViewSales: true })
    open(T.tab.sales)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('reports a 403 to the auth boundary and states the missing permission', async () => {
    reply({ status: 403 }, 403)
    mount({ canViewSales: true })
    open(T.tab.sales)
    expect(await screen.findByText(T.sales.forbidden)).toBeTruthy()
    expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 })
  })
})

describe('Cari 360 Alış tab', () => {
  it('lists this supplier\'s orders with status and date, asked by supplierPartyId under the shell company', async () => {
    const fetchMock = reply({ items: [purchaseOrder()], nextCursor: null })
    mount()
    open(T.tab.purchasing)
    const table = await screen.findByRole('table', { name: T.purchasing.caption })
    const row = within(table).getAllByRole('row')[1]
    expect(row.textContent).toContain(PURCHASE_ORDER_STATUS_LABEL.Draft)
    expect(row.textContent).toContain('22.09.2026')
    expect(within(row).getByRole('link').getAttribute('href')).toBe(`/satinalma/siparisler/${ORDER}`)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toContain(`/purchasing/orders?supplierPartyId=${PARTY}`)
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY_A)
    expect(screen.queryByTestId('cari-360-purchase-orders-more')).toBeNull()
  })

  it('shows the order as plain text without the detail capability', async () => {
    reply({ items: [purchaseOrder()], nextCursor: null })
    mount({ canListPurchaseOrders: true })
    open(T.tab.purchasing)
    const table = await screen.findByRole('table', { name: T.purchasing.caption })
    expect(within(table).queryByRole('link')).toBeNull()
    expect(table.textContent).toContain(`…${ORDER.slice(-8)}`)
  })

  it('refuses a page holding an order of another account of the party', async () => {
    reply({ items: [purchaseOrder({ companyAccountId: OTHER_ACCOUNT })], nextCursor: null })
    mount({ canListPurchaseOrders: true })
    open(T.tab.purchasing)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('says the supplier has no order only on a final empty page', async () => {
    reply({ items: [], nextCursor: null })
    mount({ canListPurchaseOrders: true })
    open(T.tab.purchasing)
    expect((await screen.findByTestId('cari-360-purchase-orders-empty')).textContent).toBe(T.purchasing.empty)
  })
})

describe('Cari 360 Finans tab', () => {
  it('shows Finance\'s balance per currency and the open items, never naming an open-item total a balance', async () => {
    const fetchMock = reply(account('Visible', [openItem]))
    mount({ canViewFinance: true })
    open(T.tab.finance)
    const balance = await screen.findByRole('table', { name: /bakiye/i })
    expect(balance.textContent).toContain(BALANCE_COLUMN_BALANCE)
    expect(balance.textContent).toContain('1.000,10')
    const items = screen.getByRole('table', { name: ITEMS_TITLE })
    expect(items.textContent).toContain('600,00')
    expect(items.textContent).toContain('30.11.2026')
    expect(items.textContent).not.toMatch(/bakiye/i)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toMatch(new RegExp(`/finance/party-accounts/${PARTY}/${ACCOUNT}$`))
    expect((init.headers as Record<string, string>)['X-Company-Id']).toBe(COMPANY_A)
  })

  it.each([
    ['Redacted', 'cari-360-finance-redacted', T.finance.redacted],
    ['Unavailable', 'cari-360-finance-withheld', T.finance.withheld],
  ] as const)('gives withheld (%s) amounts their own sentence, never a zero', async (disclosure, testId, text) => {
    // A withheld response contains no open-item amounts; populated hidden rows are a malformed response.
    reply(account(disclosure, []))
    mount({ canViewFinance: true })
    open(T.tab.finance)
    expect((await screen.findByTestId(testId)).textContent).toBe(text)
    expect(screen.queryByRole('table')).toBeNull()
  })

  it.each([
    [PARTY_ACCOUNT_TOO_MANY_CODE, 'TooManyOpenItems', ITEMS_TOO_MANY_OPEN],
    ['finance_party_balance_too_many_facts', 'TooManyFacts', ITEMS_TOO_MANY_FACTS],
  ])('states the %s refusal instead of a cut view', async (code, refusal, text) => {
    reply({ type: 'about:blank', status: 422, code }, 422)
    mount({ canViewFinance: true })
    open(T.tab.finance)
    const alert = await screen.findByTestId('cari-360-finance-refused')
    expect(alert.getAttribute('data-refusal')).toBe(refusal)
    expect(alert.textContent).toBe(text)
  })

  it('says the account has no balance and no open item when Finance holds none', async () => {
    reply(account('Visible', [], false))
    mount({ canViewFinance: true })
    open(T.tab.finance)
    expect((await screen.findByTestId('cari-360-finance-balance-empty')).textContent).toBe(BALANCE_EMPTY)
    expect(screen.getByTestId('cari-360-finance-items-empty').textContent).toBe(ITEMS_EMPTY)
  })

  it('reports a 403 to the auth boundary and shows its own forbidden sentence', async () => {
    reply({}, 403)
    mount({ canViewFinance: true })
    open(T.tab.finance)
    expect((await screen.findByTestId('cari-360-finance-forbidden')).textContent).toBe(T.finance.forbidden)
    expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 })
  })

  it('shows its own sentence when Finance is unavailable (503)', async () => {
    reply({}, 503)
    mount({ canViewFinance: true })
    open(T.tab.finance)
    expect((await screen.findByTestId('cari-360-finance-unavailable')).textContent).toBe(T.finance.unavailable)
  })
})
