import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { OfficialInvoiceImportUploadBoundForm, OfficialInvoiceImportUploadForm } from './OfficialInvoiceImportUploadPage'
import type { OfficialInvoiceUploadAttemptContext, OfficialInvoiceUploadClient } from './officialInvoiceImportUploadClient'

const companyId = '10000000-0000-0000-0000-000000000001'
const batchId = '11111111-1111-4111-8111-111111111111'
const idempotencyKey = '22222222-2222-4222-8222-222222222222'
const context: OfficialInvoiceUploadAttemptContext = {
  sessionEpoch: 27, actorId: 'maker', companyId, committedContextRevision: 3,
  requestFenceGeneration: 5, capabilityGeneration: '11', securityNamespace: 'finance-maker-page-test',
}
const pending = {
  batchId, companyId, batchVersion: 'invoice-v1', batchChecksum: 'a'.repeat(64),
  templateVersion: 'finance-official-invoice-installment-csv.v1', status: 'Pending', version: '1', rowCount: 1,
  uploadedBy: 'maker', uploadedAtUtc: '2026-09-30T12:00:00Z', approvedBy: null, approvedAtUtc: null,
} as const

function chooseFileAndVersion() {
  const file = new File(['invoice,csv'], 'invoice.csv', { type: 'text/csv' })
  fireEvent.change(screen.getByLabelText('Kaynak sürümü'), { target: { value: 'invoice-v1' } })
  fireEvent.change(screen.getByLabelText('UTF-8 CSV dosyası'), { target: { files: [file] } })
  return file
}

describe('OfficialInvoiceImportUploadForm', () => {
  afterEach(() => vi.restoreAllMocks())

  it('shows a complete pending result and explains the separate checker step', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(batchId).mockReturnValueOnce(idempotencyKey)
    const client: OfficialInvoiceUploadClient = { upload: vi.fn().mockResolvedValue({ kind: 'succeeded', replayed: false, batch: pending }) }
    const file = chooseFileAndVersionAfterRender(client)

    fireEvent.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    await screen.findByRole('heading', { name: 'Onay bekliyor' })
    expect(screen.getByText(/başka bir yetkilinin onayını bekliyor/)).toBeInTheDocument()
    expect(client.upload).toHaveBeenCalledWith(batchId, 'invoice-v1', file, idempotencyKey, expect.any(AbortSignal))
  })

  it('retries an unknown result with the exact frozen batch, file and key', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(batchId).mockReturnValueOnce(idempotencyKey)
    const upload = vi.fn()
      .mockResolvedValueOnce({ kind: 'unknown' })
      .mockResolvedValueOnce({ kind: 'succeeded', replayed: true, batch: pending })
    const client: OfficialInvoiceUploadClient = { upload }
    const file = chooseFileAndVersionAfterRender(client)

    fireEvent.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    await screen.findByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' })
    fireEvent.click(screen.getByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' }))
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(2))
    expect(upload.mock.calls[0].slice(0, 4)).toEqual([batchId, 'invoice-v1', file, idempotencyKey])
    expect(upload.mock.calls[1].slice(0, 4)).toEqual([batchId, 'invoice-v1', file, idempotencyKey])
  })

  it('recovers the frozen upload after a security re-fence without showing the old generation result', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(batchId).mockReturnValueOnce(idempotencyKey)
    const firstUpload = vi.fn().mockResolvedValue({ kind: 'unknown' })
    const firstClient: OfficialInvoiceUploadClient = { upload: firstUpload }
    const firstContext = { ...context, sessionEpoch: 31, securityNamespace: 'finance-maker-before-refence' }
    const view = render(<OfficialInvoiceImportUploadBoundForm context={firstContext} client={firstClient} />)
    const file = chooseFileAndVersion()
    fireEvent.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    await screen.findByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' })

    const replay = vi.fn().mockResolvedValue({ kind: 'succeeded', replayed: true, batch: pending })
    const refenced = { ...firstContext, requestFenceGeneration: 6, capabilityGeneration: '12',
      securityNamespace: 'finance-maker-after-refence' }
    view.rerender(<OfficialInvoiceImportUploadBoundForm context={refenced} client={{ upload: replay }} />)
    expect(screen.queryByRole('heading', { name: 'Onay bekliyor' })).not.toBeInTheDocument()
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' }))
    await screen.findByRole('heading', { name: 'Onay bekliyor' })
    expect(replay).toHaveBeenCalledWith(batchId, 'invoice-v1', file, idempotencyKey, expect.any(AbortSignal))
  })

  it('does not carry a frozen file or body to another actor or company', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(batchId).mockReturnValueOnce(idempotencyKey)
    const firstContext = { ...context, sessionEpoch: 32 }
    const view = render(<OfficialInvoiceImportUploadBoundForm context={firstContext}
      client={{ upload: vi.fn().mockResolvedValue({ kind: 'unknown' }) }} />)
    chooseFileAndVersion()
    fireEvent.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    await screen.findByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' })

    view.rerender(<OfficialInvoiceImportUploadBoundForm context={{ ...firstContext, actorId: 'other-actor',
      companyId: '90000000-0000-4000-8000-000000000009', securityNamespace: 'other-company' }}
      client={{ upload: vi.fn() }} />)
    expect(screen.queryByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Onaya gönder' })).toBeDisabled()
  })
})

function chooseFileAndVersionAfterRender(client: OfficialInvoiceUploadClient) {
  render(<OfficialInvoiceImportUploadForm context={context} client={client} />)
  return chooseFileAndVersion()
}
