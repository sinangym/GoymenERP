/**
 * UI-62 (API-32) "Fatura taslağı hazırla" on S-08, SENTETIK consumer tests (NOT_ENV) in the real shell: the shell publishes
 * `sales.invoices.write` here as it will once the Sales provider names it. Proves: the control shows only on an invoiceable group
 * and only with the capability; the body names the customer account and the chosen whole shares only (a subset splits the
 * group); a 409 `invoice_not_ready` lists the server's reasons per share, verbatim; a created draft is shown and the list is
 * read again; an unknown outcome keeps the SAME key and freezes the selection.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import candidatesResponse from './examples/invoice-candidates-response.json'
import { SALES_CAPABILITY, SALES_INVOICE_WRITE_CAPABILITY } from './capabilities'
import { DISPATCH_TEXT } from './dispatchText'
import { INVOICE_DRAFT_CREATE_TEXT as CT, invoiceNotReadyReasonText } from './invoiceDraftText'
import { INVOICE_FINALIZATION_TEXT as T } from './invoiceText'
import { resetPendingPoolAttempts } from './pendingPoolAttempts'
import { COMPANY_A, installPoolFetch, json, type PoolCall } from './poolFixtures'
import { renderPool } from './poolTestHarness'
import { INVOICE_ID, INVOICE_NO, invoiceDetail } from './invoiceTestData'

afterEach(() => {
  cleanup()
  resetPendingPoolAttempts()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const ACCOUNT = '44444444-4444-4444-8444-444444444444'
const SHARE_1 = 'f9f9f9f9-0000-4000-8000-000000000001'
const SHARE_2 = 'f9f9f9f9-0000-4000-8000-000000000003'
const READ = [SALES_CAPABILITY.dispatchesList, SALES_CAPABILITY.invoiceCandidatesList, SALES_CAPABILITY.draftsDetail]
const WRITE = [...READ, SALES_INVOICE_WRITE_CAPABILITY]

type Json = Record<string, unknown>
const clone = <V,>(value: V): V => JSON.parse(JSON.stringify(value)) as V

/** The sample with its first group made invoiceable (a due date and an invoice type, nothing unresolved). */
function invoiceable(): Json {
  const body = clone(candidatesResponse) as unknown as { groups: Json[] }
  const group = body.groups[0] as { key: Json } & Json
  group.key.dueDate = '2026-11-30'
  group.key.invoiceType = 'SATIS'
  group.dueDateMissing = false
  group.unresolvedKeyParts = []
  group.invoiceable = true
  return body as unknown as Json
}

const created = (over: Json = {}) => invoiceDetail({ state: 'Draft', version: '1', match: null, companyAccountId: ACCOUNT, ...over })
const problem = (status: number, body: Json = {}) =>
  json({ status, title: 'Problem', ...body }, status, { 'Content-Type': 'application/problem+json' })

const isCandidates = (call: PoolCall) => call.url.includes('/sales/invoice-candidates')
const isCreate = (call: PoolCall) => call.method === 'POST' && /\/sales\/invoices$/.test(call.url)
const isInvoiceRead = (call: PoolCall) => call.method === 'GET' && call.url.includes(`/sales/invoices/${INVOICE_ID}`)

function serve(create: (call: PoolCall, index: number) => Response, candidates: () => Json = invoiceable, capabilities = WRITE) {
  let creates = 0
  return installPoolFetch((call) => {
    if (isCandidates(call)) return json(candidates())
    if (isCreate(call)) return create(call, creates++)
    if (isInvoiceRead(call)) return json(created())
    throw new Error(`unexpected ${call.method} ${call.url}`)
  }, capabilities)
}

async function group1() {
  return screen.findByRole('region', { name: DISPATCH_TEXT.candidates.group(1) })
}

describe('UI-62 invoice draft create on S-08', () => {
  it('offers no create without the capability and only a hint on a group that is not invoiceable', async () => {
    serve(() => problem(500), invoiceable, READ)
    renderPool('/satis/faturaya-hazir')
    const group = await group1()
    expect(within(group).getByTestId('sales-invoice-group-1-draft-unavailable').textContent).toBe(CT.unavailable)
    expect(within(group).queryByTestId('sales-invoice-group-1-draft-create')).toBeNull()
    cleanup()

    serve(() => problem(500), () => clone(candidatesResponse) as unknown as Json)
    renderPool('/satis/faturaya-hazir')
    const blocked = await group1()
    expect(within(blocked).getByTestId('sales-invoice-group-1-draft-blocked').textContent).toBe(CT.notInvoiceableHint)
    expect(within(blocked).queryByTestId('sales-invoice-group-1-draft-create')).toBeNull()
    expect(screen.queryByRole('button', { name: CT.create })).toBeNull()
  })

  it('sends the account and only the chosen whole shares under the company and a key, then shows the draft', async () => {
    const user = userEvent.setup()
    const fetch = serve(() => json(created(), 201))
    renderPool('/satis/faturaya-hazir')
    const group = await group1()
    expect(within(group).getByRole('group', { name: CT.heading }).querySelector('legend')?.textContent).toBe(CT.selectedCount(2, 2))
    await user.click(within(group).getByTestId(`sales-invoice-share-${SHARE_2}-select`))
    expect((within(group).getByTestId(`sales-invoice-share-${SHARE_2}-select`) as HTMLInputElement).checked).toBe(false)
    await user.click(within(group).getByTestId('sales-invoice-group-1-draft-create'))

    const section = await screen.findByTestId('sales-invoice-created')
    expect(within(section).getByTestId('sales-invoice-created-notice').textContent).toBe(CT.completed(INVOICE_NO))
    const [post] = fetch.posts()
    expect(post.companyId).toBe(COMPANY_A)
    expect(post.idempotencyKey).toMatch(/\S{8,}/)
    expect(post.body).toEqual({ companyAccountId: ACCOUNT, shares: [{ shipmentId: SHARE_1 }] })
    // The list is read again after the draft is created.
    await waitFor(() => expect(fetch.calls.filter(isCandidates).length).toBeGreaterThanOrEqual(2))
    await within(section).findByTestId('sales-invoice-state')
  })

  it('refuses an empty selection locally and sends nothing', async () => {
    const user = userEvent.setup()
    const fetch = serve(() => problem(500))
    renderPool('/satis/faturaya-hazir')
    const group = await group1()
    await user.click(within(group).getByTestId('sales-invoice-group-1-select-none'))
    await user.click(within(group).getByTestId('sales-invoice-group-1-draft-create'))
    expect(within(group).getByTestId('sales-invoice-group-1-draft-error').textContent).toBe(CT.noneSelected)
    expect(fetch.posts()).toHaveLength(0)
    await user.click(within(group).getByTestId('sales-invoice-group-1-select-all'))
    expect(within(group).queryByTestId('sales-invoice-group-1-draft-error')).toBeNull()
  })

  it('lists the server reasons of invoice_not_ready per share and keeps the selection open', async () => {
    const user = userEvent.setup()
    const fetch = serve(() =>
      problem(409, {
        code: 'invoice_not_ready',
        errors: [
          { field: 'shares[0]', code: 'rate_unresolved' },
          { field: 'shares[1]', code: 'due_date_mismatch' },
          { field: 'shares[1].shipmentId', code: 'some_future_code' },
        ],
      }))
    renderPool('/satis/faturaya-hazir')
    const group = await group1()
    await user.click(within(group).getByTestId('sales-invoice-group-1-draft-create'))
    const notice = await within(group).findByTestId('sales-invoice-group-1-draft-notice')
    expect(within(notice).getByRole('list', { name: CT.notReady }).textContent).toBeTruthy()
    expect(within(within(notice).getByRole('list', { name: CT.notReady })).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      CT.notReadyItem('IRS-000002 / 1', invoiceNotReadyReasonText('rate_unresolved')),
      CT.notReadyItem('IRS-000003 / 1', invoiceNotReadyReasonText('due_date_mismatch')),
      CT.notReadyItem('IRS-000003 / 1', 'some_future_code'),
    ])
    // A refusal is not an unknown outcome: the selection stays editable and the list is not read again.
    expect(within(group).getByTestId(`sales-invoice-share-${SHARE_1}-select`).matches(':disabled')).toBe(false)
    expect(fetch.calls.filter(isCandidates)).toHaveLength(1)
    expect(screen.queryByTestId('sales-invoice-created')).toBeNull()
  })

  it('reads the list again on another conflict', async () => {
    const user = userEvent.setup()
    const fetch = serve(() => problem(409, { code: 'concurrent_change' }))
    renderPool('/satis/faturaya-hazir')
    await user.click(within(await group1()).getByTestId('sales-invoice-group-1-draft-create'))
    await waitFor(() => expect(fetch.calls.filter(isCandidates).length).toBeGreaterThanOrEqual(2))
    expect((await screen.findByTestId('sales-invoice-group-1-draft-notice')).textContent).toContain(CT.createConflict)
  })

  it('keeps the same key and freezes the selection while the outcome is unknown', async () => {
    const user = userEvent.setup()
    const fetch = serve((_call, index) => (index === 0 ? problem(503) : json(created(), 201)))
    renderPool('/satis/faturaya-hazir')
    const group = await group1()
    await user.click(within(group).getByTestId('sales-invoice-group-1-draft-create'))
    const notice = await within(group).findByTestId('sales-invoice-group-1-draft-notice')
    expect(notice.textContent).toContain(T.unknownServiceUnavailable)
    expect(within(group).getByTestId(`sales-invoice-share-${SHARE_1}-select`).matches(':disabled')).toBe(true)
    expect((within(group).getByTestId('sales-invoice-group-1-draft-create') as HTMLButtonElement).disabled).toBe(true)

    await user.click(within(group).getByTestId('sales-invoice-group-1-draft-retry'))
    await screen.findByTestId('sales-invoice-created')
    const keys = fetch.posts().map((call) => call.idempotencyKey)
    expect(keys).toHaveLength(2)
    expect(keys[0]).toBe(keys[1])
    expect(fetch.posts()[1].body).toEqual(fetch.posts()[0].body)
  })

  it('reports a replayed create as prepared before', async () => {
    const user = userEvent.setup()
    serve(() => json(created({ isReplay: true }), 201, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'corr-1' }))
    renderPool('/satis/faturaya-hazir')
    await user.click(within(await group1()).getByTestId('sales-invoice-group-1-draft-create'))
    expect((await screen.findByTestId('sales-invoice-created-notice')).textContent).toBe(CT.replayed(INVOICE_NO))
  })
})
