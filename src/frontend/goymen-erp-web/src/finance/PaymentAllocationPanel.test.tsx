// @vitest-environment jsdom
// SENTETIK / NOT_ENV: component and actual HTTP decoder evidence, not SQL or browser acceptance.
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { PaymentAllocationPanel } from './PaymentAllocationPanel'
import { PAYMENT_ALLOCATION_TEXT as T } from './PaymentAllocationText'
import { COMPANY_A, renderFinance, shellFor } from './testHarness'
const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const payment = id(2), party = id(3), account = id(4), successor = id(5), successorAccount = id(6), item = id(9)
const candidate = (paymentId = payment) => ({ companyId: COMPANY_A, paymentId, paymentVersion: '3', currencyId: id(8), originalPartyId: party, originalAccountId: account,
  positions: [{ partyId: party, companyAccountId: account, availableAmount: '0.00', assignmentVersion: '0', transferId: null },
    { partyId: successor, companyAccountId: successorAccount, availableAmount: '12.0001', assignmentVersion: '2', transferId: id(7) }],
  openItems: [{ openItemId: item, version: '4', sourceKind: 'official', sourceId: id(10), dueOn: null, originalPartyId: party, originalAccountId: account,
    positions: [{ partyId: successor, companyAccountId: successorAccount, availableAmount: '12.0001', assignmentVersion: '2', transferId: id(7) }] }],
  pageIndex: 0, hasMore: false, allocations: [], hasMoreAllocations: false })
afterEach(() => { cleanup(); vi.unstubAllGlobals() })
describe('Transferred payment allocation workflow', () => {
  it('uses the actual transferred beneficiary and retries the exact write after an unknown response', async () => {
    const posts: { body: string; key: string }[] = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      expect(new Headers(init?.headers).get('X-Company-Id')).toBe(COMPANY_A)
      if (String(input).includes('/candidates/')) return new Response(JSON.stringify(candidate()))
      expect(init?.method).toBe('POST')
      const body = String(init?.body); posts.push({ body, key: new Headers(init?.headers).get('Idempotency-Key')! })
      if (posts.length === 1) return new Response(null, { status: 503 })
      const request = JSON.parse(body) as Record<string, string>
      return new Response(JSON.stringify({ outcome: 'Succeeded', resource: { id: request.allocationId, companyId: COMPANY_A,
        paymentId: request.paymentId, openItemId: request.openItemId, currencyId: id(8), amount: request.amount,
        status: 'Active', version: '1', reason: request.reason, createdBy: 'SENTETIK', createdAtUtc: '2026-11-17T08:00:00Z',
        releasedBy: null, releasedAtUtc: null, releaseReason: null, allocationPartyId: request.allocationPartyId, allocationAccountId: request.allocationAccountId },
        isReplay: true, originalCorrelationId: 'sentetik-correlation', issues: [] }), { headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'sentetik-correlation' } })
    }))
    renderFinance(<PaymentAllocationPanel paymentId={payment} canWrite />, { port: null, shell: shellFor(COMPANY_A), router: true })
    const accountSelect = await screen.findByLabelText(T.account)
    expect(screen.queryByRole('option', { name: /Asıl cari/ })).toBeNull()
    fireEvent.change(accountSelect, { target: { value: successorAccount } })
    fireEvent.change(screen.getByLabelText(T.item), { target: { value: item } })
    fireEvent.change(screen.getByLabelText(T.amount), { target: { value: '10,0001' } })
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK allocation' } })
    fireEvent.click(screen.getByRole('button', { name: T.submit }))
    await screen.findByText(T.unknown)
    expect((screen.getByLabelText(T.amount) as HTMLInputElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: T.retry }))
    await screen.findByText(T.saved)
    expect(posts).toHaveLength(2); expect(posts[1]).toEqual(posts[0])
    expect(JSON.parse(posts[0].body)).toMatchObject({ allocationPartyId: successor, allocationAccountId: successorAccount,
      paymentId: payment, expectedPaymentVersion: '3', openItemId: item, expectedOpenItemVersion: '4', amount: '10.0001' })
  })
  it('K3 P2-1: leaves an unknown attempt only through an explicit same-key final check that gets a decided answer', async () => {
    const own = id(21)
    const posts: { body: string; key: string }[] = []
    const answers: Array<() => Response> = [
      () => new Response(null, { status: 503 }),
      () => { throw new TypeError('failed to fetch') },
      () => new Response(null, { status: 403 }),
      () => new Response(null, { status: 404 }),
    ]
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input).includes('/candidates/')) return new Response(JSON.stringify(candidate(own)))
      posts.push({ body: String(init?.body), key: new Headers(init?.headers).get('Idempotency-Key')! })
      return answers[posts.length - 1]()
    }))
    renderFinance(<PaymentAllocationPanel paymentId={own} canWrite />, { port: null, shell: shellFor(COMPANY_A), router: true })
    fireEvent.change(await screen.findByLabelText(T.account), { target: { value: successorAccount } })
    fireEvent.change(screen.getByLabelText(T.item), { target: { value: item } })
    fireEvent.change(screen.getByLabelText(T.amount), { target: { value: '1' } })
    fireEvent.change(screen.getByLabelText(T.reason), { target: { value: 'SENTETIK allocation' } })
    fireEvent.click(screen.getByRole('button', { name: T.submit }))
    await screen.findByText(T.unknown)
    // No answer to the final check: the attempt stays open and frozen.
    fireEvent.click(screen.getByRole('button', { name: T.finalCheck }))
    await waitFor(() => expect(posts).toHaveLength(2))
    await screen.findByText(T.finalOpen)
    expect((screen.getByLabelText(T.amount) as HTMLInputElement).disabled).toBe(true)
    // A 403 is about the session or authority, not this attempt: the shell refreshes the authority (the panel
    // remounts) and the attempt is still the open, frozen one.
    fireEvent.click(screen.getByRole('button', { name: T.finalCheck }))
    await waitFor(() => expect(posts).toHaveLength(3))
    await screen.findByText(T.unknown)
    expect((await screen.findByLabelText(T.amount) as HTMLInputElement).disabled).toBe(true)
    // A decided answer closes it and releases the form.
    fireEvent.click(screen.getByRole('button', { name: T.finalCheck }))
    await screen.findByText(T.abandoned)
    expect(screen.queryByRole('button', { name: T.finalCheck })).toBeNull()
    expect((screen.getByLabelText(T.amount) as HTMLInputElement).disabled).toBe(false)
    expect(new Set(posts.map((post) => post.key)).size).toBe(1)
    expect(new Set(posts.map((post) => post.body)).size).toBe(1)
  })
  it('keeps the create control disabled for a read-only viewer', async () => {
    const fetch = vi.fn(async () => new Response(JSON.stringify(candidate())))
    vi.stubGlobal('fetch', fetch)
    renderFinance(<PaymentAllocationPanel paymentId={payment} canWrite={false} />, { port: null, shell: shellFor(COMPANY_A), router: true })
    await screen.findByLabelText(T.account)
    expect((screen.getByRole('button', { name: T.submit }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: T.submit }))
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
  })
})
