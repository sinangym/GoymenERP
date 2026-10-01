// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AllocationCreateFields, AllocationCreateForm, selectableAllocationOpenItems } from './AllocationCreateForm'
import { FINANCE_CAPABILITY } from './capabilities'
import type { SourceDetail } from './contract'
import { clearAllocationCreateAttemptStore } from './allocationCreateAttempt'
import type {
  AppliedAllocationOpenItemBatch,
  ApprovedAllocationOpenItemSourceBatch,
} from './allocationSelectionContract'
import {
  ACTOR,
  COMPANY_A,
  COMPANY_B,
  currentShell,
  renderFinance,
  shellFor,
  commitContext,
} from './testHarness'

const SOURCE = '11111111-1111-4111-8111-111111111111'
const BATCH = '22222222-2222-4222-8222-222222222222'
const ITEM = '33333333-3333-4333-8333-333333333333'
const ACCOUNT = '44444444-4444-4444-8444-444444444444'
const CURRENCY = '55555555-5555-4555-8555-555555555555'
const DOCUMENT = '66666666-6666-4666-8666-666666666666'
const INSTALLMENT = '77777777-7777-4777-8777-777777777777'
const ALLOCATION = '88888888-8888-4888-8888-888888888888'
const CHECKSUM = 'a'.repeat(64)

afterEach(() => {
  cleanup()
  clearAllocationCreateAttemptStore()
  vi.unstubAllGlobals()
})

const sourceSummary = {
  id: SOURCE,
  companyId: COMPANY_A,
  sourceNumber: 'RC-2',
  version: '2',
  stage: 'Recorded',
  partyId: DOCUMENT,
  companyAccountId: ACCOUNT,
  treasuryAccountId: BATCH,
  currencyId: CURRENCY,
  occurredOn: '2026-09-18',
  valueOn: null,
  amount: { disclosure: 'Visible', money: { amount: '432.10', currencyId: CURRENCY } },
}

const sourceDetail = {
  source: sourceSummary,
  profile: { profileId: SOURCE, version: '1' },
  bankReference: null,
  description: null,
  recordedAtUtc: '2026-09-18T10:00:00Z',
  reversalSourceId: null,
  reversesSourceId: null,
  unallocated: { knowledge: 'Known', amount: { disclosure: 'Visible', money: { amount: '432.10', currencyId: CURRENCY } } },
  pos: null,
}

const approvedBatch: ApprovedAllocationOpenItemSourceBatch = {
  batchId: BATCH,
  companyId: COMPANY_A,
  batchVersion: 'invoice-batch-v1',
  batchChecksum: CHECKSUM,
  rows: [{
    openItemId: ITEM,
    sourceDocumentId: DOCUMENT,
    sourceInstallmentId: INSTALLMENT,
    sourceVersion: 'invoice-v1',
    documentKind: 'official-invoice-installment',
    partyId: DOCUMENT,
    companyAccountId: ACCOUNT,
    currencyId: CURRENCY,
    amount: '432.10',
    dueOn: '2026-10-31',
  }],
}

const appliedBatch: AppliedAllocationOpenItemBatch = {
  batchId: BATCH,
  companyId: COMPANY_A,
  batchVersion: approvedBatch.batchVersion,
  batchChecksum: CHECKSUM,
  openItems: [{
    id: ITEM,
    companyId: COMPANY_A,
    sourceKind: 'official.invoice-installment.v1',
    sourceDocumentId: DOCUMENT,
    sourceInstallmentId: INSTALLMENT,
    sourceVersion: 'invoice-v1',
    sourceBatchId: BATCH,
    sourceBatchVersion: approvedBatch.batchVersion,
    sourceBatchChecksum: CHECKSUM,
    partyId: DOCUMENT,
    companyAccountId: ACCOUNT,
    currencyId: CURRENCY,
    originalAmount: '432.10',
    outstandingAmount: '432.10',
    dueOn: '2026-10-31',
    status: 1,
    version: '1',
  }],
}

function success(body: Record<string, string>, replayed: boolean) {
  return new Response(JSON.stringify({
    outcome: 'Succeeded',
    resource: {
      id: body.allocationId ?? ALLOCATION,
      companyId: COMPANY_A,
      sourceId: body.sourceId,
      openItemId: body.openItemId,
      currencyId: CURRENCY,
      amount: body.amount,
      status: 'Active',
      version: '1',
      reason: body.reason,
      createdBy: ACTOR,
      createdAtUtc: '2026-10-01T08:00:00Z',
      releasedBy: null,
      releasedAtUtc: null,
      releaseReason: null,
    },
    isReplay: replayed,
    originalCorrelationId: replayed ? 'SENTETIK-allocation-attempt-1' : '',
    issues: [],
  }), {
    status: 200,
    headers: replayed ? { 'Idempotency-Replayed': 'true' } : undefined,
  })
}

describe('AllocationCreateFields', () => {
  it('requires actual selections plus amount and reason before the user clicks create', () => {
    const onSource = vi.fn()
    const onBatch = vi.fn()
    const onOpenItem = vi.fn()
    const onAmount = vi.fn()
    const onReason = vi.fn()
    const onSubmit = vi.fn()
    const view = render(<AllocationCreateFields
      sources={[{ id: SOURCE, label: 'RC-1' }]}
      batches={[{ id: BATCH, label: 'invoice-batch-v1' }]}
      openItems={[{ id: ITEM, label: '2026-10-31 - 432.10' }]}
      sourceId="" batchId="" openItemId="" amount="" reason=""
      frozen={false} unknown={false} canSubmit={false} message={null}
      onSource={onSource} onBatch={onBatch} onOpenItem={onOpenItem}
      onAmount={onAmount} onReason={onReason} onSubmit={onSubmit}
    />)
    expect((screen.getByRole('button', { name: 'Dağıtım oluştur' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByRole('combobox', { name: 'Gerçekleşmiş tahsilat kaynağı' }))
      .toBe(screen.getByLabelText('Gerçekleşmiş tahsilat kaynağı', { exact: true }))
    expect(screen.getByRole('combobox', { name: 'Onaylı fatura kaynağı' }))
      .toBe(screen.getByLabelText('Onaylı fatura kaynağı', { exact: true }))
    expect(screen.getByRole('combobox', { name: 'Açık kalem' }))
      .toBe(screen.getByLabelText('Açık kalem', { exact: true }))
    fireEvent.change(screen.getByLabelText('Gerçekleşmiş tahsilat kaynağı'), { target: { value: SOURCE } })
    fireEvent.change(screen.getByLabelText('Onaylı fatura kaynağı'), { target: { value: BATCH } })
    fireEvent.change(screen.getByLabelText('Açık kalem'), { target: { value: ITEM } })
    fireEvent.change(screen.getByLabelText('Dağıtım tutarı'), { target: { value: '432,10' } })
    fireEvent.change(screen.getByLabelText('Dağıtım nedeni'), { target: { value: 'Fatura kapama' } })
    expect(onSource).toHaveBeenCalledWith(SOURCE)
    expect(onBatch).toHaveBeenCalledWith(BATCH)
    expect(onOpenItem).toHaveBeenCalledWith(ITEM)
    expect(onAmount).toHaveBeenCalledWith('432,10')
    expect(onReason).toHaveBeenCalledWith('Fatura kapama')
    expect(onSubmit).not.toHaveBeenCalled()

    view.rerender(<AllocationCreateFields
      sources={[{ id: SOURCE, label: 'RC-1' }]}
      batches={[{ id: BATCH, label: 'invoice-batch-v1' }]}
      openItems={[{ id: ITEM, label: '2026-10-31 - 432.10' }]}
      sourceId={SOURCE} batchId={BATCH} openItemId={ITEM} amount="432,10" reason="Fatura kapama"
      frozen={false} unknown={false} canSubmit message={null}
      onSource={onSource} onBatch={onBatch} onOpenItem={onOpenItem}
      onAmount={onAmount} onReason={onReason} onSubmit={onSubmit}
    />)
    fireEvent.click(screen.getByRole('button', { name: 'Dağıtım oluştur' }))
    expect(onSubmit).toHaveBeenCalledTimes(1)
  })

  it('offers only same-account same-currency open items and exposes no unknown-attempt discard', () => {
    const source = {
      source: { id: SOURCE, companyId: SOURCE, sourceNumber: 'RC-1', version: '2', stage: 'Recorded',
        partyId: SOURCE, companyAccountId: ACCOUNT, treasuryAccountId: SOURCE, currencyId: CURRENCY,
        occurredOn: '2026-09-18', valueOn: null,
        amount: { disclosure: 'Visible', money: { amount: '432.10', currencyId: CURRENCY } } },
      profile: { profileId: SOURCE, version: '1' }, bankReference: null, description: null,
      recordedAtUtc: '2026-09-18T10:00:00Z', reversalSourceId: null, reversesSourceId: null,
      unallocated: { knowledge: 'Known', amount: { disclosure: 'Visible', money: { amount: '432.10', currencyId: CURRENCY } } },
      pos: null,
    } as SourceDetail
    const item = (id: string, account = ACCOUNT, currency = CURRENCY) => ({
      id, companyId: SOURCE, sourceKind: 'official.invoice-installment.v1' as const,
      sourceDocumentId: SOURCE, sourceInstallmentId: BATCH, sourceVersion: 'v1', sourceBatchId: BATCH,
      sourceBatchVersion: 'b1', sourceBatchChecksum: 'a'.repeat(64), partyId: SOURCE,
      companyAccountId: account, currencyId: currency, originalAmount: '432.10', outstandingAmount: '432.10',
      dueOn: null, status: 1 as const, version: '1',
    })
    const batch = { batchId: BATCH, companyId: SOURCE, batchVersion: 'b1', batchChecksum: 'a'.repeat(64),
      openItems: [item(ITEM), item('66666666-6666-4666-8666-666666666666', SOURCE)] } as AppliedAllocationOpenItemBatch
    expect(selectableAllocationOpenItems(batch, source).map((candidate) => candidate.id)).toEqual([ITEM])

    render(<AllocationCreateFields
      sources={[{ id: SOURCE, label: 'RC-1' }]} batches={[]} openItems={[{ id: ITEM, label: ITEM }]}
      sourceId={SOURCE} batchId="" openItemId={ITEM} amount="432,10" reason="Fatura kapama"
      frozen unknown canSubmit={false} message="Sonuç bilinmiyor"
      onSource={vi.fn()} onBatch={vi.fn()} onOpenItem={vi.fn()} onAmount={vi.fn()} onReason={vi.fn()}
      onSubmit={vi.fn()}
    />)
    expect((screen.getByRole('button', { name: 'Aynı anahtarla yeniden dene' }) as HTMLButtonElement).disabled).toBe(false)
    expect(screen.queryByRole('button', { name: /vazgeç|iptal|sıfırla/i })).toBeNull()
    expect((screen.getByLabelText('Dağıtım tutarı') as HTMLInputElement).disabled).toBe(true)
  })
})

describe('AllocationCreateForm', () => {
  it('reports a current receipt-list 403 to the shell capability fence', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      const companyId = new Headers(init?.headers).get('X-Company-Id') ?? COMPANY_A
      if (url.includes('/finance/manual-receipts')) return new Response(null, { status: 403 })
      if (url.includes('/approved-sources')) {
        return new Response(JSON.stringify({ companyId, items: [], nextCursor: null }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(<AllocationCreateForm onCreated={vi.fn()} />, {
      port: null,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsWrite, FINANCE_CAPABILITY.receiptsList]),
    })
    await waitFor(() => expect(currentShell().activeContext?.capabilityGeneration).toBeNull())
    expect(currentShell().activeContext?.capabilities).toEqual([])
  })

  it('reports a current receipt-detail 401 to the shell session fence', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      const companyId = new Headers(init?.headers).get('X-Company-Id') ?? COMPANY_A
      if (url.endsWith(`/finance/manual-receipts/${SOURCE}`)) return new Response(null, { status: 401 })
      if (url.includes('/finance/manual-receipts')) {
        return new Response(JSON.stringify({ companyId, items: [sourceSummary], nextCursor: null }), { status: 200 })
      }
      if (url.includes('/approved-sources')) {
        return new Response(JSON.stringify({ companyId, items: [], nextCursor: null }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(<AllocationCreateForm onCreated={vi.fn()} />, {
      port: null,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsWrite, FINANCE_CAPABILITY.receiptsList]),
    })
    await screen.findByRole('option', { name: 'RC-2' })
    fireEvent.change(screen.getByLabelText('Gerçekleşmiş tahsilat kaynağı'), { target: { value: SOURCE } })
    await waitFor(() => expect(currentShell().sessionInvalid).toBe(true))
    expect(currentShell().activeContext).toBeNull()
  })

  it('ignores a former-company receipt rejection that arrives after the next context is committed', async () => {
    let resolveFormer!: (response: Response) => void
    const former = new Promise<Response>((resolve) => { resolveFormer = resolve })
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      const url = String(input)
      const companyId = new Headers(init?.headers).get('X-Company-Id') ?? COMPANY_A
      if (url.includes('/finance/manual-receipts')) {
        return companyId === COMPANY_A
          ? former
          : Promise.resolve(new Response(JSON.stringify({ companyId, items: [], nextCursor: null }), { status: 200 }))
      }
      if (url.includes('/approved-sources')) {
        return Promise.resolve(new Response(JSON.stringify({ companyId, items: [], nextCursor: null }), { status: 200 }))
      }
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)
    renderFinance(<AllocationCreateForm onCreated={vi.fn()} />, {
      port: null,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsWrite, FINANCE_CAPABILITY.receiptsList]),
    })
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    await commitContext(shellFor(COMPANY_B, 1,
      [FINANCE_CAPABILITY.allocationsWrite, FINANCE_CAPABILITY.receiptsList]))
    await act(async () => {
      resolveFormer(new Response(null, { status: 403 }))
      await former
    })
    await waitFor(() => expect(currentShell().activeContext?.companyId).toBe(COMPANY_B))
    expect(currentShell().activeContext?.capabilityGeneration).toBe('finance-gen-1')
    expect(currentShell().activeContext?.capabilities).toEqual([
      FINANCE_CAPABILITY.allocationsWrite,
      FINANCE_CAPABILITY.receiptsList,
    ])
  })

  it('waits for second detail and applied GETs after same-namespace reopen despite a fresh 30-second cache', async () => {
    let detailReads = 0
    let appliedReads = 0
    let resolveSecondDetail!: (response: Response) => void
    let resolveSecondApplied!: (response: Response) => void
    const secondDetail = new Promise<Response>((resolve) => { resolveSecondDetail = resolve })
    const secondApplied = new Promise<Response>((resolve) => { resolveSecondApplied = resolve })
    const fetchMock = vi.fn((input: RequestInfo | URL): Promise<Response> => {
      const url = String(input)
      if (url.endsWith(`/finance/manual-receipts/${SOURCE}`)) {
        detailReads += 1
        return detailReads === 1
          ? Promise.resolve(new Response(JSON.stringify(sourceDetail), { status: 200 }))
          : secondDetail
      }
      if (url.includes('/finance/manual-receipts')) {
        return Promise.resolve(new Response(JSON.stringify({
          companyId: COMPANY_A, items: [sourceSummary], nextCursor: null,
        }), { status: 200 }))
      }
      if (url.includes('/approved-sources')) {
        return Promise.resolve(new Response(JSON.stringify({
          companyId: COMPANY_A, items: [approvedBatch], nextCursor: null,
        }), { status: 200 }))
      }
      if (url.endsWith(`/official-invoice-imports/${BATCH}`)) {
        appliedReads += 1
        return appliedReads === 1
          ? Promise.resolve(new Response(JSON.stringify(appliedBatch), { status: 200 }))
          : secondApplied
      }
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)
    const form = <AllocationCreateForm onCreated={vi.fn()} />
    const view = renderFinance(<div>Kapalı</div>, {
      port: null,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsWrite, FINANCE_CAPABILITY.receiptsList]),
    })
    view.queryClient.setDefaultOptions({ queries: { retry: false, gcTime: 60_000, staleTime: 30_000 } })
    view.remount(form)

    await screen.findByRole('option', { name: 'RC-2' })
    fireEvent.change(screen.getByLabelText('Gerçekleşmiş tahsilat kaynağı'), { target: { value: SOURCE } })
    await screen.findByRole('option', { name: approvedBatch.batchVersion })
    fireEvent.change(screen.getByLabelText('Onaylı fatura kaynağı'), { target: { value: BATCH } })
    await screen.findByRole('option', { name: '2026-10-31 - 432.10' })
    expect(detailReads).toBe(1)
    expect(appliedReads).toBe(1)

    view.remount(<div>Kapalı</div>)
    view.remount(form)
    await screen.findByRole('option', { name: 'RC-2' })
    fireEvent.change(screen.getByLabelText('Gerçekleşmiş tahsilat kaynağı'), { target: { value: SOURCE } })
    await screen.findByRole('option', { name: approvedBatch.batchVersion })
    fireEvent.change(screen.getByLabelText('Onaylı fatura kaynağı'), { target: { value: BATCH } })
    await waitFor(() => expect(detailReads).toBe(2))
    await waitFor(() => expect(appliedReads).toBe(2))
    fireEvent.change(screen.getByLabelText('Dağıtım tutarı'), { target: { value: '432,10' } })
    fireEvent.change(screen.getByLabelText('Dağıtım nedeni'), { target: { value: 'Fatura kapama' } })
    expect(screen.queryByRole('option', { name: '2026-10-31 - 432.10' })).toBeNull()
    expect((screen.getByRole('button', { name: 'Dağıtım oluştur' }) as HTMLButtonElement).disabled).toBe(true)

    await act(async () => {
      resolveSecondDetail(new Response(JSON.stringify(sourceDetail), { status: 200 }))
      resolveSecondApplied(new Response(JSON.stringify(appliedBatch), { status: 200 }))
      await Promise.all([secondDetail, secondApplied])
    })
    await screen.findByRole('option', { name: '2026-10-31 - 432.10' })
    fireEvent.change(screen.getByLabelText('Açık kalem'), { target: { value: ITEM } })
    expect((screen.getByRole('button', { name: 'Dağıtım oluştur' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('moves through both catalogs and enables a second-page eligible open item only after fresh reads', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url.endsWith(`/finance/manual-receipts/${SOURCE}`)) {
        return new Response(JSON.stringify(sourceDetail), { status: 200 })
      }
      if (url.includes('/finance/manual-receipts')) {
        const second = url.includes('cursor=source-next')
        return new Response(JSON.stringify({
          companyId: COMPANY_A,
          items: second ? [sourceSummary] : [],
          nextCursor: second ? null : 'source-next',
        }), { status: 200 })
      }
      if (url.includes('/approved-sources')) {
        const second = url.includes('cursor=batch-next')
        return new Response(JSON.stringify({
          companyId: COMPANY_A,
          items: second ? [approvedBatch] : [],
          nextCursor: second ? null : 'batch-next',
        }), { status: 200 })
      }
      if (url.endsWith(`/official-invoice-imports/${BATCH}`)) {
        return new Response(JSON.stringify(appliedBatch), { status: 200 })
      }
      throw new Error(`Unexpected request: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    renderFinance(<AllocationCreateForm onCreated={vi.fn()} />, {
      port: null,
      shell: shellFor(COMPANY_A),
    })
    fireEvent.click(await screen.findByRole('button', { name: 'Sonraki tahsilat kaynakları' }))
    await screen.findByRole('option', { name: 'RC-2' })
    fireEvent.change(screen.getByLabelText('Gerçekleşmiş tahsilat kaynağı'), { target: { value: SOURCE } })
    fireEvent.click(await screen.findByRole('button', { name: 'Sonraki fatura kaynakları' }))
    await screen.findByRole('option', { name: approvedBatch.batchVersion })
    fireEvent.change(screen.getByLabelText('Onaylı fatura kaynağı'), { target: { value: BATCH } })
    await screen.findByRole('option', { name: '2026-10-31 - 432.10' })
    fireEvent.change(screen.getByLabelText('Açık kalem'), { target: { value: ITEM } })
    fireEvent.change(screen.getByLabelText('Dağıtım tutarı'), { target: { value: '432,10' } })
    fireEvent.change(screen.getByLabelText('Dağıtım nedeni'), { target: { value: 'Fatura kapama' } })
    expect((screen.getByRole('button', { name: 'Dağıtım oluştur' }) as HTMLButtonElement).disabled).toBe(false)
  })

  it('keeps the first identity unknown after unmount and replays the exact key and body after remount', async () => {
    let resolveFirst!: (response: Response) => void
    const first = new Promise<Response>((resolve) => { resolveFirst = resolve })
    const posts: { readonly key: string | null; readonly body: string }[] = []
    let receiptListReads = 0
    let approvedListReads = 0
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
      const url = String(input)
      if (init?.method === 'POST') {
        const wire = String(init.body)
        posts.push({ key: new Headers(init.headers).get('Idempotency-Key'), body: wire })
        if (posts.length === 1) return first
        return Promise.resolve(success(JSON.parse(wire) as Record<string, string>, true))
      }
      if (url.endsWith(`/finance/manual-receipts/${SOURCE}`)) {
        return Promise.resolve(new Response(JSON.stringify(sourceDetail), { status: 200 }))
      }
      if (url.includes('/finance/manual-receipts')) {
        receiptListReads += 1
        return Promise.resolve(new Response(JSON.stringify({
          companyId: COMPANY_A, items: [sourceSummary], nextCursor: null,
        }), { status: 200 }))
      }
      if (url.includes('/approved-sources')) {
        approvedListReads += 1
        return Promise.resolve(new Response(JSON.stringify({
          companyId: COMPANY_A, items: [approvedBatch], nextCursor: null,
        }), { status: 200 }))
      }
      if (url.endsWith(`/official-invoice-imports/${BATCH}`)) {
        return Promise.resolve(new Response(JSON.stringify(appliedBatch), { status: 200 }))
      }
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)
    const onCreated = vi.fn()
    const form = <AllocationCreateForm onCreated={onCreated} />
    const view = renderFinance(form, {
      port: null,
      shell: shellFor(COMPANY_A, 1, [FINANCE_CAPABILITY.allocationsWrite, FINANCE_CAPABILITY.receiptsList]),
    })

    await screen.findByRole('option', { name: 'RC-2' })
    fireEvent.change(screen.getByLabelText('Gerçekleşmiş tahsilat kaynağı'), { target: { value: SOURCE } })
    await screen.findByRole('option', { name: approvedBatch.batchVersion })
    fireEvent.change(screen.getByLabelText('Onaylı fatura kaynağı'), { target: { value: BATCH } })
    await screen.findByRole('option', { name: '2026-10-31 - 432.10' })
    fireEvent.change(screen.getByLabelText('Açık kalem'), { target: { value: ITEM } })
    fireEvent.change(screen.getByLabelText('Dağıtım tutarı'), { target: { value: '432,10' } })
    fireEvent.change(screen.getByLabelText('Dağıtım nedeni'), { target: { value: 'Fatura kapama' } })
    fireEvent.click(screen.getByRole('button', { name: 'Dağıtım oluştur' }))
    await waitFor(() => expect(posts).toHaveLength(1))
    const receiptReadsBeforeClose = receiptListReads
    const approvedReadsBeforeClose = approvedListReads

    view.remount(<div>Kapalı</div>)
    view.queryClient.setDefaultOptions({ queries: { retry: false, gcTime: 0, staleTime: 30_000 } })
    await commitContext(shellFor(COMPANY_A, 2, []))
    await act(async () => {
      resolveFirst(success(JSON.parse(posts[0].body) as Record<string, string>, false))
      await first
    })
    expect(onCreated).not.toHaveBeenCalled()

    await commitContext(shellFor(COMPANY_A, 3,
      [FINANCE_CAPABILITY.allocationsWrite, FINANCE_CAPABILITY.receiptsList]))
    view.remount(form)
    await waitFor(() => expect(receiptListReads).toBeGreaterThan(receiptReadsBeforeClose))
    await waitFor(() => expect(approvedListReads).toBeGreaterThan(approvedReadsBeforeClose))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden dene' }))
    await waitFor(() => expect(onCreated).toHaveBeenCalledTimes(1))
    expect(posts).toHaveLength(2)
    expect(posts[1]).toEqual(posts[0])
  })
})
