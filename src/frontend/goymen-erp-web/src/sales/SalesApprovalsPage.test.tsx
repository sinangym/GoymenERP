/**
 * UI-51 / S-06 "Satış Onayları" in the real shell: the Sales order-draft approval queue read from the Work approval read
 * (`kind=sales.order-draft.v1`) under the existing approve control. Rows show the server's facts, links open S-04 and
 * S-05, continuation keeps rows on a transient failure and retries the same cursor, a terminal failure closes the list,
 * and the other Sales approvals are listed as unavailable. Read-only. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SALES_CAPABILITY } from './capabilities'
import { SALES_APPROVALS_TEXT as T } from './approvalsText'
import { ALL_SALES, ALL_SALES_WITH_APPROVE, COMPANY_A, COMPANY_B, installFetch, json, renderSalesApp } from './testHarness'

const PATH = '/satis/onaylar'
const FIRST = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const SECOND = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'

function item(approvalId: string, over: Record<string, unknown> = {}) {
  return {
    kind: 'sales.order-draft.v1', approvalId, companyId: COMPANY_A, status: 'PendingApproval', version: '2',
    revision: 'Rev.0', documentNo: `SO-${approvalId.slice(0, 4)}`, isOwnDraft: false, submittedBy: 'maker',
    submittedAtUtc: '2026-10-01T08:00:00Z', ...over,
  }
}

function page(items: unknown[], nextCursor: string | null, companyId = COMPANY_A) {
  return { companyId, items, nextCursor }
}

interface WorkCall { readonly url: string; readonly companyId: string | null }

/** The sales harness plus a Work approvals server; every other request goes to the harness. */
function installWork(onApprovals: (url: URL) => Response | Promise<Response>, capabilities: readonly string[] = ALL_SALES_WITH_APPROVE) {
  const harness = installFetch({ capabilities, onList: () => json({ items: [], nextCursor: null }) })
  const sales = globalThis.fetch
  const work: WorkCall[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const raw = typeof input === 'string' ? input : input.toString()
    if (raw.includes('/work/approvals')) {
      const headers = (init?.headers ?? {}) as Record<string, string>
      work.push({ url: raw, companyId: headers['X-Company-Id'] ?? null })
      return onApprovals(new URL(raw, 'http://localhost'))
    }
    return sales(input, init)
  }))
  return { harness, work }
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('UI-51 S-06 sales approvals', () => {
  it('lists the pending order drafts with the server facts and links, under this company, and sends no command', async () => {
    const { harness, work } = installWork(() => json(page([item(FIRST, { isOwnDraft: true }), item(SECOND)], null)))
    renderSalesApp(PATH)

    const table = await screen.findByTestId('sales-approvals-table')
    expect(within(table).getByText(`SO-${FIRST.slice(0, 4)}`)).toBeTruthy()
    expect(screen.getByTestId(`sales-approvals-own-${FIRST}`).textContent).toBe(T.orders.ownDraftYes)
    expect(screen.getByTestId(`sales-approvals-own-${SECOND}`).textContent).toBe(T.orders.ownDraftNo)
    const row = table.querySelector(`[data-approval-id="${FIRST}"]`) as HTMLElement
    expect(within(row).getByRole('link', { name: T.orders.openOrder }).getAttribute('href')).toBe(`/satis/siparisler/${FIRST}`)
    expect(within(row).getByRole('link', { name: T.orders.openControl }).getAttribute('href')).toBe(`/satis/siparisler/${FIRST}/kontrol`)
    expect(screen.getByText(T.end)).toBeTruthy()
    expect(work[0].companyId).toBe(COMPANY_A)
    expect(new URL(work[0].url, 'http://localhost').searchParams.get('kind')).toBe('sales.order-draft.v1')
    expect(within(screen.getByTestId('sales-approvals-unavailable')).getAllByRole('listitem')).toHaveLength(T.unavailable.items.length)
    expect(harness.calls.filter((call) => call.method !== 'GET')).toEqual([])
  })

  it('shows the empty state when nothing is pending', async () => {
    installWork(() => json(page([], null)))
    renderSalesApp(PATH)

    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    expect(screen.queryByTestId('sales-approvals-table')).toBeNull()
  })

  it('closes on a 403 and shows no row', async () => {
    installWork(() => json({ status: 403 }, 403))
    renderSalesApp(PATH)

    await waitFor(() => expect(document.querySelector('[data-state="forbidden"], [data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByTestId('sales-approvals-table')).toBeNull()
  })

  it('retries a 503 first page and then shows the rows', async () => {
    let attempts = 0
    installWork(() => (++attempts === 1 ? json({ status: 503 }, 503) : json(page([item(FIRST)], null))))
    renderSalesApp(PATH)

    await waitFor(() => expect(document.querySelector('[data-state="service-unavailable"]')).not.toBeNull())
    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByTestId('sales-approvals-table')).toBeTruthy()
  })

  it.each<[string, () => unknown]>([
    ['a malformed page', () => ({ companyId: COMPANY_A, items: [{ approvalId: FIRST }], nextCursor: null })],
    ['a page of another company', () => page([item(FIRST, { companyId: COMPANY_B })], null, COMPANY_B)],
    ['an item of another company', () => page([item(FIRST, { companyId: COMPANY_B })], null)],
  ])('fails closed on %s', async (_name, body) => {
    installWork(() => json(body()))
    renderSalesApp(PATH)

    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByTestId('sales-approvals-table')).toBeNull()
  })

  it('keeps the rows on a transient next-page failure and asks the same cursor again', async () => {
    let secondAttempts = 0
    const { work } = installWork((url) => {
      if (url.searchParams.get('cursor') === null) return json(page([item(FIRST)], 'c2'))
      return ++secondAttempts === 1 ? json({ status: 503 }, 503) : json(page([item(FIRST), item(SECOND)], null))
    })
    renderSalesApp(PATH)

    await userEvent.click(await screen.findByRole('button', { name: T.loadMore }))
    const failed = await screen.findByTestId('sales-approvals-next-failed')
    expect(screen.getByTestId('sales-approvals-table').querySelector(`[data-approval-id="${FIRST}"]`)).not.toBeNull()
    await userEvent.click(within(failed).getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(document.querySelector(`[data-approval-id="${SECOND}"]`)).not.toBeNull())
    // The repeated first row is shown once.
    expect(document.querySelectorAll(`[data-approval-id="${FIRST}"]`)).toHaveLength(1)
    expect(work.filter((call) => call.url.includes('cursor=c2'))).toHaveLength(2)
    expect(screen.getByText(T.end)).toBeTruthy()
  })

  it('closes the list on a terminal next-page failure and starts over on request', async () => {
    let terminal = true
    const { work } = installWork((url) => {
      if (url.searchParams.get('cursor') === null) return json(page([item(FIRST)], 'c2'))
      return terminal ? json({ companyId: COMPANY_A }) : json(page([item(SECOND)], null))
    })
    renderSalesApp(PATH)

    await userEvent.click(await screen.findByRole('button', { name: T.loadMore }))
    const block = await screen.findByTestId('sales-approvals-terminal')
    expect(screen.queryByTestId('sales-approvals-table')).toBeNull()
    terminal = false
    const firstReads = work.filter((call) => !call.url.includes('cursor=')).length
    await userEvent.click(within(block).getByRole('button', { name: T.restart }))
    expect(await screen.findByTestId('sales-approvals-table')).toBeTruthy()
    expect(work.filter((call) => !call.url.includes('cursor=')).length).toBeGreaterThan(firstReads)
  })

  it('opens under the approve control only: without it nothing is read', async () => {
    const { work } = installWork(() => json(page([item(FIRST)], null)), ALL_SALES)
    renderSalesApp(PATH)

    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(work).toEqual([])
  })

  it('offers no order links without the detail capability', async () => {
    installWork(() => json(page([item(FIRST)], null)), [SALES_CAPABILITY.draftsList, SALES_CAPABILITY.ordersApprove])
    renderSalesApp(PATH)

    const table = await screen.findByTestId('sales-approvals-table')
    expect(within(table).queryByRole('link')).toBeNull()
  })

  it.each<[string, readonly string[], boolean]>([
    ['with', ALL_SALES_WITH_APPROVE, true],
    ['without', ALL_SALES, false],
  ])('shows the list-page link %s the approve control', async (_name, capabilities, shown) => {
    installWork(() => json(page([], null)), capabilities)
    renderSalesApp('/satis/siparisler')

    await waitFor(() => expect(document.querySelector('[data-state="empty-no-data"]')).not.toBeNull())
    const link = screen.queryByTestId('sales-open-approvals')
    expect(link !== null).toBe(shown)
    if (link !== null) expect(link.getAttribute('href')).toBe(PATH)
  })
})
