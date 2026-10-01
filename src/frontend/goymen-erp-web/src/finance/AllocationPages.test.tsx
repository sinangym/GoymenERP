// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { AllocationPage } from './AllocationPages'
import { FINANCE_CAPABILITY, FINANCE_ROUTE } from './capabilities'
import { clearAllocationReleaseAttemptStore } from './allocationReleaseAttempt'
import { COMPANY_A, COMPANY_B, commitContext, currentShell, renderFinance, shellFor } from './testHarness'

const ID = '22222222-2222-4222-8222-222222222222'
const SOURCE = '33333333-3333-4333-8333-333333333333'
const ITEM = '44444444-4444-4444-8444-444444444444'
const CURRENCY = '55555555-5555-4555-8555-555555555555'
const BATCH = '66666666-6666-4666-8666-666666666666'
const DOCUMENT = '77777777-7777-4777-8777-777777777777'
const INSTALLMENT = '88888888-8888-4888-8888-888888888888'
const ACCOUNT = '99999999-9999-4999-8999-999999999999'
const CHECKSUM = 'a'.repeat(64)

const allocation = {
  id: ID, companyId: COMPANY_A, sourceId: SOURCE, openItemId: ITEM, currencyId: CURRENCY,
  amount: '125.50', status: 'Active', version: '1', reason: 'SENTETIK dağıtım', createdBy: 'maker',
  createdAtUtc: '2026-09-30T10:00:00Z', releasedBy: null, releasedAtUtc: null, releaseReason: null,
}

afterEach(() => {
  cleanup()
  clearAllocationReleaseAttemptStore()
  vi.unstubAllGlobals()
})

function page(companyId = COMPANY_A) {
  return { companyId, items: companyId === COMPANY_A ? [allocation] : [], windowFull: false }
}

function detail() {
  return { allocation, openItemVersion: '7' }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

const sourceSummary = {
  id: SOURCE, companyId: COMPANY_A, sourceNumber: 'RC-MOUNT-1', version: '2', stage: 'Recorded',
  partyId: DOCUMENT, companyAccountId: ACCOUNT, treasuryAccountId: BATCH, currencyId: CURRENCY,
  occurredOn: '2026-09-18', valueOn: null,
  amount: { disclosure: 'Visible', money: { amount: '125.50', currencyId: CURRENCY } },
}

const sourceDetail = {
  source: sourceSummary,
  profile: { profileId: SOURCE, version: '1' },
  bankReference: null,
  description: null,
  recordedAtUtc: '2026-09-18T10:00:00Z',
  reversalSourceId: null,
  reversesSourceId: null,
  unallocated: { knowledge: 'Known', amount: {
    disclosure: 'Visible', money: { amount: '125.50', currencyId: CURRENCY },
  } },
  pos: null,
}

const approvedBatch = {
  batchId: BATCH, companyId: COMPANY_A, batchVersion: 'invoice-batch-v1', batchChecksum: CHECKSUM,
  rows: [{
    openItemId: ITEM, sourceDocumentId: DOCUMENT, sourceInstallmentId: INSTALLMENT,
    sourceVersion: 'invoice-v1', documentKind: 'official-invoice-installment', partyId: DOCUMENT,
    companyAccountId: ACCOUNT, currencyId: CURRENCY, amount: '125.50', dueOn: '2026-10-31',
  }],
}

const appliedBatch = {
  batchId: BATCH, companyId: COMPANY_A, batchVersion: approvedBatch.batchVersion, batchChecksum: CHECKSUM,
  openItems: [{
    id: ITEM, companyId: COMPANY_A, sourceKind: 'official.invoice-installment.v1',
    sourceDocumentId: DOCUMENT, sourceInstallmentId: INSTALLMENT, sourceVersion: 'invoice-v1',
    sourceBatchId: BATCH, sourceBatchVersion: approvedBatch.batchVersion, sourceBatchChecksum: CHECKSUM,
    partyId: DOCUMENT, companyAccountId: ACCOUNT, currencyId: CURRENCY,
    originalAmount: '125.50', outstandingAmount: '125.50', dueOn: '2026-10-31', status: 1, version: '1',
  }],
}

describe('AllocationPage', () => {
  it('lets an allocation-read-only actor list and reopen without receipt capabilities', async () => {
    const secondDetail = deferred<Response>()
    let detailReads = 0
    const fetchMock = vi.fn(async (request: RequestInfo | URL) => {
      const url = String(request)
      if (url.endsWith(`/${ID}`)) {
        detailReads += 1
        if (detailReads === 2) return secondDetail.promise
        return new Response(JSON.stringify(detail()), { status: 200 })
      }
      return new Response(JSON.stringify(page()), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(
      <MemoryRouter initialEntries={[FINANCE_ROUTE.allocationsList]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsList]) },
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Aç' }))
    expect((await screen.findByRole('region', { name: 'Dağıtım detayı' })).textContent).toContain(ID)
    expect(screen.queryByRole('button', { name: 'Dağıtımı bırak' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Kapat' }))
    expect(screen.queryByRole('region', { name: 'Dağıtım detayı' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Aç' }))
    await waitFor(() => expect(detailReads).toBe(2))
    expect(screen.queryByRole('region', { name: 'Dağıtım detayı' })).toBeNull()
    await act(async () => secondDetail.resolve(new Response(JSON.stringify(detail()), { status: 200 })))
    expect((await screen.findByRole('region', { name: 'Dağıtım detayı' })).textContent).toContain(ID)
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
  })

  it.each([
    [FINANCE_CAPABILITY.allocationsList, FINANCE_CAPABILITY.receiptsList],
    [FINANCE_CAPABILITY.allocationsList, FINANCE_CAPABILITY.allocationsWrite],
  ])('does not mount create unless both allocation-write and receipt-read capabilities are current', async (...capabilities) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(page()), { status: 200 })))
    renderFinance(
      <MemoryRouter initialEntries={[FINANCE_ROUTE.allocationsList]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: shellFor(COMPANY_A, 1, capabilities) },
    )
    await screen.findByText(SOURCE)
    expect(screen.queryByRole('form', { name: 'Finansal dağıtım oluştur' })).toBeNull()
  })

  it('mounts with existing capabilities and opens the exact created id through a fresh detail GET', async () => {
    let created: typeof allocation | null = null
    const sentBody: { current: Record<string, string> | null } = { current: null }
    let detailReads = 0
    const fetchMock = vi.fn(async (request: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(request), 'http://localhost')
      const method = init?.method ?? 'GET'
      if (url.pathname === '/api/v1/finance/allocations' && method === 'POST') {
        sentBody.current = JSON.parse(String(init?.body)) as Record<string, string>
        created = {
          ...allocation,
          id: sentBody.current.allocationId,
          sourceId: sentBody.current.sourceId,
          openItemId: sentBody.current.openItemId,
          amount: sentBody.current.amount,
          reason: sentBody.current.reason,
        }
        return new Response(JSON.stringify({
          outcome: 'Succeeded', resource: created, isReplay: false, originalCorrelationId: '', issues: [],
        }), { status: 200 })
      }
      if (created !== null && url.pathname === `/api/v1/finance/allocations/${created.id}`) {
        detailReads += 1
        return new Response(JSON.stringify({ allocation: created, openItemVersion: '2' }), { status: 200 })
      }
      if (url.pathname === '/api/v1/finance/allocations') {
        return new Response(JSON.stringify({
          companyId: COMPANY_A, items: created === null ? [] : [created], windowFull: false,
        }), { status: 200 })
      }
      if (url.pathname === `/api/v1/finance/manual-receipts/${SOURCE}`) {
        return new Response(JSON.stringify(sourceDetail), { status: 200 })
      }
      if (url.pathname === '/api/v1/finance/manual-receipts') {
        return new Response(JSON.stringify({ companyId: COMPANY_A, items: [sourceSummary], nextCursor: null }), { status: 200 })
      }
      if (url.pathname === '/api/v1/finance/official-invoice-imports/approved-sources') {
        return new Response(JSON.stringify({ companyId: COMPANY_A, items: [approvedBatch], nextCursor: null }), { status: 200 })
      }
      if (url.pathname === `/api/v1/finance/official-invoice-imports/${BATCH}`) {
        return new Response(JSON.stringify(appliedBatch), { status: 200 })
      }
      throw new Error(`Unexpected request: ${url.pathname}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(
      <MemoryRouter initialEntries={[FINANCE_ROUTE.allocationsList]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: shellFor(COMPANY_A, 1, [
        FINANCE_CAPABILITY.allocationsList,
        FINANCE_CAPABILITY.allocationsWrite,
        FINANCE_CAPABILITY.receiptsList,
      ]) },
    )

    const form = await screen.findByRole('form', { name: 'Finansal dağıtım oluştur' })
    fireEvent.change(screen.getByLabelText('Gerçekleşmiş tahsilat kaynağı'), { target: { value: SOURCE } })
    fireEvent.change(await screen.findByLabelText('Onaylı fatura kaynağı'), { target: { value: BATCH } })
    await screen.findByRole('option', { name: '2026-10-31 - 125.50' })
    fireEvent.change(screen.getByLabelText('Açık kalem'), { target: { value: ITEM } })
    fireEvent.change(screen.getByLabelText('Dağıtım tutarı'), { target: { value: '125,50' } })
    fireEvent.change(screen.getByLabelText('Dağıtım nedeni'), { target: { value: 'SENTETIK parent mount' } })
    fireEvent.submit(form)

    const region = await screen.findByRole('region', { name: 'Dağıtım detayı' })
    const submitted = sentBody.current
    expect(submitted).not.toBeNull()
    if (submitted === null) throw new Error('The allocation POST body was not captured.')
    expect(submitted.allocationId).toMatch(/^[0-9a-f-]{36}$/i)
    expect(region.textContent).toContain(submitted.allocationId)
    expect(detailReads).toBe(1)
  })

  it.each([401, 403] as const)('reports a current list %i to the shared auth boundary', async (status) => {
    const initial = shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsList])
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status })))
    renderFinance(
      <MemoryRouter initialEntries={[FINANCE_ROUTE.allocationsList]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: initial },
    )
    await waitFor(() => expect(status === 401
      ? currentShell().sessionInvalid
      : currentShell().requestFenceGeneration > initial.requestFenceGeneration).toBe(true))
    expect(screen.queryByText(SOURCE)).toBeNull()
  })

  it('ignores a late detail 403 after the company context has changed', async () => {
    const late = deferred<Response>()
    const detailCompanies: string[] = []
    const fetchMock = vi.fn(async (request: RequestInfo | URL, init?: RequestInit) => {
      const url = String(request)
      const company =
        request instanceof Request
          ? request.headers.get('X-Company-Id')
          : new Headers(init?.headers).get('X-Company-Id')
      if (url.endsWith(`/${ID}`)) {
        detailCompanies.push(company ?? '')
        return company === COMPANY_A ? late.promise : new Response(null, { status: 404 })
      }
      return new Response(JSON.stringify(page(company === COMPANY_B ? COMPANY_B : COMPANY_A)), {
        status: 200,
      })
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(
      <MemoryRouter initialEntries={[FINANCE_ROUTE.allocationsList]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsList]) },
    )
    fireEvent.click(await screen.findByRole('button', { name: 'Aç' }))
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    const companyB = shellFor(COMPANY_B, 2, [FINANCE_CAPABILITY.allocationsList])
    await commitContext(companyB)
    await waitFor(() => expect(detailCompanies).toContain(COMPANY_B))
    await act(async () => late.resolve(new Response(null, { status: 403 })))
    await waitFor(() => expect(currentShell().activeContext?.companyId).toBe(COMPANY_B))
    expect(currentShell().requestFenceGeneration).toBe(companyB.requestFenceGeneration)
    expect(detailCompanies).toContain(COMPANY_A)
    expect(screen.queryByText(SOURCE)).toBeNull()
  })

  it.each([401, 403] as const)('reports a current release %i and sends no replacement key', async (status) => {
    const initial = shellFor(COMPANY_A, 1,
      [FINANCE_CAPABILITY.allocationsList, FINANCE_CAPABILITY.allocationsWrite])
    const fetchMock = vi.fn(async (request: RequestInfo | URL, init?: RequestInit) => {
      const url = String(request)
      if (init?.method === 'POST') return new Response(null, { status })
      return new Response(JSON.stringify(url.endsWith(`/${ID}`) ? detail() : page()), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(
      <MemoryRouter initialEntries={[`${FINANCE_ROUTE.allocationsList}?dagitim=${ID}`]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: initial },
    )
    fireEvent.change(await screen.findByLabelText('Bırakma nedeni'), { target: { value: 'Yanlış dağıtım' } })
    fireEvent.click(screen.getByRole('button', { name: 'Dağıtımı bırak' }))
    await waitFor(() => expect(status === 401
      ? currentShell().sessionInvalid
      : currentShell().requestFenceGeneration > initial.requestFenceGeneration).toBe(true))
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
  })

  it('recovers an in-flight release after remount with its immutable key and body', async () => {
    const firstPost = deferred<Response>()
    const postCalls: RequestInit[] = []
    const fetchMock = vi.fn(async (request: RequestInfo | URL, init?: RequestInit) => {
      const url = String(request)
      if (init?.method === 'POST') {
        postCalls.push(init)
        if (postCalls.length === 1) return firstPost.promise
        return new Response(null, { status: 503 })
      }
      return new Response(JSON.stringify(url.endsWith(`/${ID}`) ? detail() : page()), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    const ui = () => <MemoryRouter initialEntries={[`${FINANCE_ROUTE.allocationsList}?dagitim=${ID}`]}>
      <AllocationPage />
    </MemoryRouter>
    const view = renderFinance(ui(), { port: null, shell: shellFor(COMPANY_A, 1,
      [FINANCE_CAPABILITY.allocationsList, FINANCE_CAPABILITY.allocationsWrite]) })

    fireEvent.change(await screen.findByLabelText('Bırakma nedeni'), { target: { value: 'Yanlış dağıtım' } })
    fireEvent.click(screen.getByRole('button', { name: 'Dağıtımı bırak' }))
    await waitFor(() => expect(postCalls).toHaveLength(1))
    view.remount(ui())

    const frozenReason = await screen.findByLabelText('Bırakma nedeni')
    expect((frozenReason as HTMLInputElement).disabled).toBe(true)
    expect((frozenReason as HTMLInputElement).value).toBe('Yanlış dağıtım')
    fireEvent.click(screen.getByRole('button', { name: 'Aynı anahtarla yeniden dene' }))
    await waitFor(() => expect(postCalls).toHaveLength(2))

    const firstHeaders = new Headers(postCalls[0].headers)
    const secondHeaders = new Headers(postCalls[1].headers)
    expect(secondHeaders.get('Idempotency-Key')).toBe(firstHeaders.get('Idempotency-Key'))
    expect(postCalls[1].body).toBe(postCalls[0].body)
  })

  it('keeps an unknown release through 403 and restores its same-key retry only after current capability returns', async () => {
    const postStatuses = [503, 403, 503]
    const postCalls: RequestInit[] = []
    const initial = shellFor(COMPANY_A, 1,
      [FINANCE_CAPABILITY.allocationsList, FINANCE_CAPABILITY.allocationsWrite])
    const fetchMock = vi.fn(async (request: RequestInfo | URL, init?: RequestInit) => {
      const url = String(request)
      if (init?.method === 'POST') {
        postCalls.push(init)
        return new Response(null, { status: postStatuses[postCalls.length - 1] })
      }
      return new Response(JSON.stringify(url.endsWith(`/${ID}`) ? detail() : page()), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(
      <MemoryRouter initialEntries={[`${FINANCE_ROUTE.allocationsList}?dagitim=${ID}`]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: initial },
    )

    fireEvent.change(await screen.findByLabelText('Bırakma nedeni'), { target: { value: 'Yanlış dağıtım' } })
    fireEvent.click(screen.getByRole('button', { name: 'Dağıtımı bırak' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden dene' }))
    await waitFor(() => expect(postCalls).toHaveLength(2))
    await waitFor(() => expect(currentShell().requestFenceGeneration).toBeGreaterThan(initial.requestFenceGeneration))

    await commitContext(shellFor(COMPANY_A, 2,
      [FINANCE_CAPABILITY.allocationsList, FINANCE_CAPABILITY.allocationsWrite]))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden dene' }))
    await waitFor(() => expect(postCalls).toHaveLength(3))

    const keys = postCalls.map((call) => new Headers(call.headers).get('Idempotency-Key'))
    expect(new Set(keys).size).toBe(1)
    expect(postCalls.map((call) => call.body)).toEqual([postCalls[0].body, postCalls[0].body, postCalls[0].body])
  })

  it.each([400, 415] as const)('keeps an unknown release after a retry receives %i', async (status) => {
    const postStatuses = [503, status, 503]
    const postCalls: RequestInit[] = []
    const fetchMock = vi.fn(async (request: RequestInfo | URL, init?: RequestInit) => {
      const url = String(request)
      if (init?.method === 'POST') {
        postCalls.push(init)
        return new Response(null, { status: postStatuses[postCalls.length - 1] })
      }
      return new Response(JSON.stringify(url.endsWith(`/${ID}`) ? detail() : page()), { status: 200 })
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(
      <MemoryRouter initialEntries={[`${FINANCE_ROUTE.allocationsList}?dagitim=${ID}`]}><AllocationPage /></MemoryRouter>,
      { port: null, shell: shellFor(COMPANY_A, 1,
        [FINANCE_CAPABILITY.allocationsList, FINANCE_CAPABILITY.allocationsWrite]) },
    )

    fireEvent.change(await screen.findByLabelText('Bırakma nedeni'), { target: { value: 'Yanlış dağıtım' } })
    fireEvent.click(screen.getByRole('button', { name: 'Dağıtımı bırak' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden dene' }))
    await waitFor(() => expect(postCalls).toHaveLength(2))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden dene' }))
    await waitFor(() => expect(postCalls).toHaveLength(3))

    const keys = postCalls.map((call) => new Headers(call.headers).get('Idempotency-Key'))
    expect(new Set(keys).size).toBe(1)
    expect(postCalls.map((call) => call.body)).toEqual([postCalls[0].body, postCalls[0].body, postCalls[0].body])
  })
})
