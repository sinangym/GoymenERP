import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { OfficialInvoiceImportApprovalsPage, type OfficialInvoiceApprovalClient } from './OfficialInvoiceImportApprovalsPage'
import type { OfficialInvoicePendingBatch, OfficialInvoicePendingDetail } from './officialInvoiceImportApprovalContract'

const pending: OfficialInvoicePendingBatch = {
  batchId: '11111111-1111-4111-8111-111111111111', companyId: '22222222-2222-4222-8222-222222222222',
  batchVersion: 'invoice-v1', batchChecksum: 'a'.repeat(64), templateVersion: 'finance-official-invoice-installment-csv.v1',
  status: 'Pending', version: '1', rowCount: 1, uploadedBy: 'maker', uploadedAtUtc: '2026-09-30T12:00:00Z',
  approvedBy: null, approvedAtUtc: null,
}
const detail: OfficialInvoicePendingDetail = { batch: pending, preview: { templateVersion: pending.templateVersion,
  sourceSha256: pending.batchChecksum, isValid: true, issues: [], batch: { batchId: pending.batchId, companyId: pending.companyId,
    batchVersion: pending.batchVersion, batchChecksum: pending.batchChecksum, rows: [{
      openItemId: '33333333-3333-4333-8333-333333333333', sourceDocumentId: '44444444-4444-4444-8444-444444444444',
      sourceInstallmentId: '55555555-5555-4555-8555-555555555555', sourceVersion: 'invoice-v1',
      documentKind: 'official-invoice-installment', partyId: '66666666-6666-4666-8666-666666666666',
      companyAccountId: '77777777-7777-4777-8777-777777777777', currencyId: '88888888-8888-4888-8888-888888888888',
      amount: '1250.50', dueOn: '2026-10-31',
    }] } } }
const approved = { ...pending, status: 'Approved' as const, version: '2' as const,
  approvedBy: 'checker', approvedAtUtc: '2026-09-30T13:00:00Z' }
const context = { sessionEpoch: 1, companyId: pending.companyId, actorId: 'checker', committedContextRevision: 3,
  requestFenceGeneration: 5, capabilityGeneration: 11, securityNamespace: 'finance-checker' }

describe('official invoice checker journey', () => {
  it('keeps the terminal approved source after the fresh pending list becomes empty', async () => {
    const selected = vi.fn()
    const client: OfficialInvoiceApprovalClient = {
      listPending: vi.fn().mockResolvedValueOnce({ companyId: pending.companyId, items: [pending], nextCursor: null })
        .mockResolvedValueOnce({ companyId: pending.companyId, items: [], nextCursor: null }),
      getPending: vi.fn().mockResolvedValue(detail),
      approve: vi.fn().mockResolvedValue({ kind: 'succeeded', replayed: false, batch: approved }),
    }
    render(<OfficialInvoiceImportApprovalsPage client={client} context={context}
      createIdempotencyKey={() => 'approval-key'} onApprovedSource={selected} />)

    fireEvent.click(await screen.findByRole('button', { name: 'İncele' }))
    expect(await screen.findByText('1250.50')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Onayla' }))

    expect(await screen.findByText('Kaynak onaylandı. Sürüm 2.')).toBeTruthy()
    expect(await screen.findByText('Onay bekleyen kaynak yok.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Onaylanan kaynağı aktar' }))
    expect(selected).toHaveBeenCalledWith(approved)
  })

  it('retries an unknown approval with the same key and body', async () => {
    const approve = vi.fn().mockResolvedValueOnce({ kind: 'unknown' })
      .mockResolvedValueOnce({ kind: 'succeeded', replayed: true, batch: approved })
    const client: OfficialInvoiceApprovalClient = {
      listPending: vi.fn().mockResolvedValue({ companyId: pending.companyId, items: [pending], nextCursor: null }),
      getPending: vi.fn().mockResolvedValue(detail), approve,
    }
    render(<OfficialInvoiceImportApprovalsPage client={client} context={context}
      createIdempotencyKey={() => 'stable-key'} onApprovedSource={() => undefined} />)
    fireEvent.click(await screen.findByRole('button', { name: 'İncele' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Onayla' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))

    await waitFor(() => expect(approve).toHaveBeenCalledTimes(2))
    expect(approve.mock.calls[0]).toEqual([pending.batchId, '1', 'stable-key', expect.any(AbortSignal)])
    expect(approve.mock.calls[1]).toEqual([pending.batchId, '1', 'stable-key', expect.any(AbortSignal)])
  })

  it('never enables approval for the uploader even if a malformed host page exposes it', async () => {
    const client: OfficialInvoiceApprovalClient = {
      listPending: vi.fn().mockResolvedValue({ companyId: pending.companyId, items: [pending], nextCursor: null }),
      getPending: vi.fn().mockResolvedValue(detail), approve: vi.fn(),
    }
    render(<OfficialInvoiceImportApprovalsPage client={client} context={{ ...context, actorId: 'maker' }}
      onApprovedSource={() => undefined} />)
    fireEvent.click(await screen.findByRole('button', { name: 'İncele' }))
    expect((await screen.findByRole('button', { name: 'Onayla' }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('retires late detail and settled actions immediately when company or actor changes', async () => {
    let resolveDetail!: (value: OfficialInvoicePendingDetail) => void
    const lateDetail = new Promise<OfficialInvoicePendingDetail>((resolve) => { resolveDetail = resolve })
    const client: OfficialInvoiceApprovalClient = {
      listPending: vi.fn()
        .mockResolvedValueOnce({ companyId: pending.companyId, items: [pending], nextCursor: null })
        .mockResolvedValueOnce({ companyId: '99999999-9999-4999-8999-999999999999', items: [], nextCursor: null }),
      getPending: vi.fn().mockReturnValue(lateDetail), approve: vi.fn(),
    }
    const view = render(<OfficialInvoiceImportApprovalsPage client={client} context={context}
      onApprovedSource={() => undefined} />)
    fireEvent.click(await screen.findByRole('button', { name: 'İncele' }))
    view.rerender(<OfficialInvoiceImportApprovalsPage client={client}
      context={{ ...context, sessionEpoch: 2, companyId: '99999999-9999-4999-8999-999999999999', actorId: 'other',
        committedContextRevision: 4, requestFenceGeneration: 6, capabilityGeneration: 12,
        securityNamespace: 'finance-other' }}
      onApprovedSource={() => undefined} />)
    resolveDetail(detail)

    expect(await screen.findByText('Onay bekleyen kaynak yok.')).toBeTruthy()
    expect(screen.queryByText('1250.50')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Onaylanan kaynağı aktar' })).toBeNull()
  })

  it('keeps the same key and body when a wrong-batch success is retried as unknown', async () => {
    const selected = vi.fn()
    const approve = vi.fn()
      .mockResolvedValueOnce({ kind: 'succeeded', replayed: false,
        batch: { ...approved, batchId: '99999999-9999-4999-8999-999999999999' } })
      .mockResolvedValueOnce({ kind: 'succeeded', replayed: true, batch: approved })
    const client: OfficialInvoiceApprovalClient = {
      listPending: vi.fn().mockResolvedValue({ companyId: pending.companyId, items: [pending], nextCursor: null }),
      getPending: vi.fn().mockResolvedValue(detail),
      approve,
    }
    render(<OfficialInvoiceImportApprovalsPage client={client} context={context}
      createIdempotencyKey={() => 'frozen-key'} onApprovedSource={selected} />)
    fireEvent.click(await screen.findByRole('button', { name: 'İncele' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Onayla' }))

    expect(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Onaylanan kaynağı aktar' })).toBeNull()
    expect(selected).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))

    await waitFor(() => expect(approve).toHaveBeenCalledTimes(2))
    expect(approve.mock.calls[0]).toEqual([pending.batchId, '1', 'frozen-key', expect.any(AbortSignal)])
    expect(approve.mock.calls[1]).toEqual([pending.batchId, '1', 'frozen-key', expect.any(AbortSignal)])
    expect(await screen.findByRole('button', { name: 'Onaylanan kaynağı aktar' })).toBeTruthy()
  })

  it('opens the bounded next page and recovers from list failure and command conflict', async () => {
    const second = { ...pending, batchId: '99999999-9999-4999-8999-999999999999', batchVersion: 'invoice-v2' }
    const secondDetail = { ...detail, batch: second, preview: { ...detail.preview,
      batch: { ...detail.preview.batch, batchId: second.batchId, batchVersion: second.batchVersion } } }
    const listPending = vi.fn()
      .mockRejectedValueOnce(new Error('synthetic read failure'))
      .mockResolvedValueOnce({ companyId: pending.companyId, items: [pending], nextCursor: 'page-2' })
      .mockResolvedValueOnce({ companyId: pending.companyId, items: [second], nextCursor: null })
    const getPending = vi.fn().mockResolvedValueOnce(secondDetail).mockResolvedValueOnce(secondDetail)
    const client: OfficialInvoiceApprovalClient = {
      listPending, getPending,
      approve: vi.fn().mockResolvedValue({ kind: 'conflicted' }),
    }
    render(<OfficialInvoiceImportApprovalsPage client={client} context={context}
      onApprovedSource={() => undefined} />)

    fireEvent.click((await screen.findByRole('alert')).querySelector('button')!)
    fireEvent.click(await screen.findByRole('button', { name: 'Sonraki sayfa' }))
    expect(await screen.findByText('invoice-v2')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'İncele' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Onayla' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Kaydı yeniden oku' }))

    await waitFor(() => expect(getPending).toHaveBeenCalledTimes(2))
    expect(await screen.findByRole('button', { name: 'Onay kuyruğuna dön' })).toBeTruthy()
  })

  it('retires state on same-actor re-fence and replays unknown then 403 with the same frozen attempt', async () => {
    const approve = vi.fn()
      .mockResolvedValueOnce({ kind: 'unknown' })
      .mockResolvedValueOnce({ kind: 'unknown' })
      .mockResolvedValueOnce({ kind: 'succeeded', replayed: true, batch: approved })
    const client: OfficialInvoiceApprovalClient = {
      listPending: vi.fn().mockResolvedValue({ companyId: pending.companyId, items: [pending], nextCursor: null }),
      getPending: vi.fn().mockResolvedValue(detail), approve,
    }
    const selected = vi.fn()
    const view = render(<OfficialInvoiceImportApprovalsPage client={client} context={context}
      createIdempotencyKey={() => 'reauth-key'} onApprovedSource={selected} />)
    fireEvent.click(await screen.findByRole('button', { name: 'İncele' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Onayla' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))
    await waitFor(() => expect(approve).toHaveBeenCalledTimes(2))

    view.rerender(<OfficialInvoiceImportApprovalsPage client={client}
      context={{ ...context, requestFenceGeneration: 6, capabilityGeneration: 12,
        securityNamespace: 'finance-checker-refenced' }}
      createIdempotencyKey={() => 'must-not-mint'} onApprovedSource={selected} />)
    expect(screen.queryByText('1250.50')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Onaylanan kaynağı aktar' })).toBeNull()
    fireEvent.click(await screen.findByRole('button', { name: 'İncele' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' }))

    await waitFor(() => expect(approve).toHaveBeenCalledTimes(3))
    for (const call of approve.mock.calls) {
      expect(call.slice(0, 3)).toEqual([pending.batchId, '1', 'reauth-key'])
    }
    expect(await screen.findByRole('button', { name: 'Onaylanan kaynağı aktar' })).toBeTruthy()
  })
})
