import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import {
  OfficialInvoiceImportPanel,
  type OfficialInvoiceAppliedBatch,
  type OfficialInvoiceImportClient,
  type OfficialInvoiceImportPreview,
} from './OfficialInvoiceImportPanel'

const source = {
  batchId: '11111111-1111-4111-8111-111111111111',
  batchVersion: 'official-file-v1',
  displayName: 'Eylül onaylı fatura taksitleri',
}
const checksum = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'

function preview(issues: OfficialInvoiceImportPreview['issues'] = []): OfficialInvoiceImportPreview {
  return {
    templateVersion: 'finance-official-invoice-v1',
    sourceSha256: checksum,
    batchId: source.batchId,
    batchVersion: source.batchVersion,
    rowCount: 1,
    issues,
  }
}

function batch(outstandingAmount = '1250.50', version = '1'): OfficialInvoiceAppliedBatch {
  return {
    batchId: source.batchId,
    batchVersion: source.batchVersion,
    batchChecksum: checksum,
    openItems: [{
      id: '22222222-2222-4222-8222-222222222222',
      sourceDocumentId: '33333333-3333-4333-8333-333333333333',
      sourceInstallmentId: '44444444-4444-4444-8444-444444444444',
      partyId: '55555555-5555-4555-8555-555555555555',
      companyAccountId: '66666666-6666-4666-8666-666666666666',
      currencyId: '77777777-7777-4777-8777-777777777777',
      originalAmount: '1250.50',
      outstandingAmount,
      dueOn: '2026-10-31',
      status: 1,
      version,
    }],
  }
}

function client(overrides: Partial<OfficialInvoiceImportClient> = {}): OfficialInvoiceImportClient {
  return {
    preview: vi.fn().mockResolvedValue(preview()),
    apply: vi.fn().mockResolvedValue({ kind: 'succeeded', replayed: false, batch: batch() }),
    reopen: vi.fn().mockResolvedValue(batch('250.50', '2')),
    ...overrides,
  }
}

async function upload(user: ReturnType<typeof userEvent.setup>) {
  const file = new File(['header\nrow'], 'invoice.csv', { type: 'text/csv' })
  await user.upload(screen.getByLabelText(/utf-8 csv dosyası/i), file)
  return file
}

describe('official invoice import panel', () => {
  it('previews, applies, closes and reopens the same persisted Finance batch', async () => {
    const user = userEvent.setup()
    const api = client()
    render(<OfficialInvoiceImportPanel source={source} client={api} />)
    const file = await upload(user)

    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    expect(await screen.findByText(`SHA-256: ${checksum}`)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: /yetkili uygula/i }))

    await screen.findByText(/1 açık kalem finans'tan yeniden okundu/i)
    expect(api.apply).toHaveBeenCalledWith(
      source,
      file,
      `official-invoice:${source.batchId}:${checksum}`,
      expect.any(AbortSignal),
    )
    await user.click(screen.getByRole('button', { name: /sonucu kapat/i }))
    await user.click(screen.getByRole('button', { name: /yeniden aç/i }))
    expect(await screen.findByText(/kalan 250.50 \/ sürüm 2/i)).toBeTruthy()
    expect(api.reopen).toHaveBeenCalledWith(source.batchId, expect.any(AbortSignal))
  })

  it('shows strict preview issues and never enables apply', async () => {
    const user = userEvent.setup()
    const api = client({
      preview: vi.fn().mockResolvedValue(preview([{ rowNumber: 2, field: 'amount', code: 'decimal_invalid' }])),
    })
    render(<OfficialInvoiceImportPanel source={source} client={api} />)
    await upload(user)

    await user.click(screen.getByRole('button', { name: /^önizle$/i }))

    expect(await screen.findByText(/satır 2, amount: decimal_invalid/i)).toBeTruthy()
    expect((screen.getByRole('button', { name: /yetkili uygula/i }) as HTMLButtonElement).disabled).toBe(true)
    expect(api.apply).not.toHaveBeenCalled()
  })

  it('retries an unknown result with the same file, body identity and idempotency key', async () => {
    const user = userEvent.setup()
    const apply = vi.fn()
      .mockResolvedValueOnce({ kind: 'unknown' })
      .mockResolvedValueOnce({ kind: 'succeeded', replayed: true, batch: batch() })
    const api = client({ apply })
    render(<OfficialInvoiceImportPanel source={source} client={api} />)
    const file = await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    await user.click(await screen.findByRole('button', { name: /yetkili uygula/i }))
    await user.click(await screen.findByRole('button', { name: /aynı anahtarla yeniden gönder/i }))

    await waitFor(() => expect(apply).toHaveBeenCalledTimes(2))
    expect(apply.mock.calls[0][0]).toBe(source)
    expect(apply.mock.calls[1][0]).toBe(source)
    expect(apply.mock.calls[0][1]).toBe(file)
    expect(apply.mock.calls[1][1]).toBe(file)
    expect(apply.mock.calls[0][2]).toBe(apply.mock.calls[1][2])
    expect(await screen.findByText(/1 açık kalem finans'tan yeniden okundu/i)).toBeTruthy()
  })

  it('resets prior preview when a different file is selected', async () => {
    const user = userEvent.setup()
    render(<OfficialInvoiceImportPanel source={source} client={client()} />)
    await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    await screen.findByText(`SHA-256: ${checksum}`)

    fireEvent.change(screen.getByLabelText(/utf-8 csv dosyası/i), {
      target: { files: [new File(['other'], 'other.csv', { type: 'text/csv' })] },
    })

    expect(screen.queryByText(`SHA-256: ${checksum}`)).toBeNull()
    expect((screen.getByRole('button', { name: /yetkili uygula/i }) as HTMLButtonElement).disabled).toBe(true)
  })
})
