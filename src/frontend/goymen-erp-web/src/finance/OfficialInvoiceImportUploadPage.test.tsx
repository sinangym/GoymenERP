import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { OfficialInvoiceImportUploadBoundForm, OfficialInvoiceImportUploadForm } from './OfficialInvoiceImportUploadPage'
import type { OfficialInvoiceUploadAttemptContext, OfficialInvoiceUploadClient } from './officialInvoiceImportUploadClient'

const companyId = '10000000-0000-0000-0000-000000000001'
const batchId = '11111111-1111-4111-8111-111111111111'
const idempotencyKey = '22222222-2222-4222-8222-222222222222'
const checksum = 'a'.repeat(64)
const context: OfficialInvoiceUploadAttemptContext = {
  sessionEpoch: 27, actorId: 'maker', companyId, committedContextRevision: 3,
  requestFenceGeneration: 5, capabilityGeneration: '11', securityNamespace: 'finance-maker-page-test',
}
const prepared = { utf8Csv: 'aW52b2ljZSxjc3Y=', sha256: checksum }
const preview = {
  templateVersion: 'finance-official-invoice-installment-csv.v1', sourceSha256: checksum,
  batchId, batchVersion: 'invoice-v1', rowCount: 1, issues: [],
} as const
const pending = {
  batchId, companyId, batchVersion: 'invoice-v1', batchChecksum: checksum,
  templateVersion: preview.templateVersion, status: 'Pending', version: '1', rowCount: 1,
  uploadedBy: 'maker', uploadedAtUtc: '2026-09-30T12:00:00Z', approvedBy: null, approvedAtUtc: null,
} as const

function client(upload: OfficialInvoiceUploadClient['upload']): OfficialInvoiceUploadClient {
  return {
    prepare: vi.fn().mockResolvedValue(prepared),
    preview: vi.fn().mockResolvedValue({ kind: 'ready', preview }),
    upload,
  }
}

function chooseFileAndVersion() {
  const file = new File(['invoice,csv'], 'invoice.csv', { type: 'text/csv' })
  fireEvent.change(screen.getByLabelText('Kaynak sürümü'), { target: { value: 'invoice-v1' } })
  fireEvent.change(screen.getByLabelText('UTF-8 CSV dosyası'), { target: { files: [file] } })
  return file
}

async function previewSelectedFile() {
  fireEvent.click(screen.getByRole('button', { name: 'Önizle' }))
  const upload = await screen.findByRole('button', { name: 'Onaya gönder' }) as HTMLButtonElement
  await waitFor(() => expect(upload.disabled).toBe(false))
}

describe('OfficialInvoiceImportUploadForm', () => {
  afterEach(() => vi.restoreAllMocks())

  it('previews exact bytes before upload and shows the complete pending result', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(batchId).mockReturnValueOnce(idempotencyKey)
    const upload = vi.fn().mockResolvedValue({ kind: 'succeeded', replayed: false, batch: pending })
    const api = client(upload)
    render(<OfficialInvoiceImportUploadForm context={context} client={api} />)
    const file = chooseFileAndVersion()

    await previewSelectedFile()
    fireEvent.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    await screen.findByRole('heading', { name: 'Onay bekliyor' })

    expect(screen.getByText(/başka bir yetkilinin onayını bekliyor/)).not.toBeNull()
    expect(api.prepare).toHaveBeenCalledWith(file)
    expect(api.preview).toHaveBeenCalledWith(batchId, 'invoice-v1', prepared, expect.any(AbortSignal))
    expect(upload).toHaveBeenCalledWith(batchId, 'invoice-v1', prepared, idempotencyKey, expect.any(AbortSignal))
    expect((api.preview as ReturnType<typeof vi.fn>).mock.invocationCallOrder[0])
      .toBeLessThan(upload.mock.invocationCallOrder[0])
  })

  it('freezes the selected file and version while an asynchronous preview is in flight', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(batchId).mockReturnValueOnce(idempotencyKey)
    let release!: (value: typeof prepared) => void
    const preparing = new Promise<typeof prepared>((resolve) => { release = resolve })
    const api = client(vi.fn())
    ;(api.prepare as ReturnType<typeof vi.fn>).mockReturnValueOnce(preparing)
    render(<OfficialInvoiceImportUploadForm context={context} client={api} />)
    const selectedFile = chooseFileAndVersion()

    fireEvent.click(screen.getByRole('button', { name: 'Önizle' }))
    const version = screen.getByLabelText('Kaynak sürümü') as HTMLInputElement
    const file = screen.getByLabelText('UTF-8 CSV dosyası') as HTMLInputElement
    await waitFor(() => {
      expect(version.disabled).toBe(true)
      expect(file.disabled).toBe(true)
    })
    fireEvent.change(version, { target: { value: 'invoice-v2' } })
    fireEvent.change(file, { target: { files: [new File(['other'], 'other.csv')] } })
    release(prepared)

    await screen.findByText(/Önizleme hazır/)
    expect(api.prepare).toHaveBeenCalledWith(selectedFile)
    expect(api.preview).toHaveBeenCalledWith(
      batchId, 'invoice-v1', prepared, expect.any(AbortSignal),
    )
  })

  it('retries an unknown result with the exact frozen payload and key', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(batchId).mockReturnValueOnce(idempotencyKey)
    const upload = vi.fn()
      .mockResolvedValueOnce({ kind: 'unknown' })
      .mockResolvedValueOnce({ kind: 'succeeded', replayed: true, batch: pending })
    render(<OfficialInvoiceImportUploadForm context={context} client={client(upload)} />)
    chooseFileAndVersion()

    await previewSelectedFile()
    fireEvent.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    await screen.findByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' })
    fireEvent.click(screen.getByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' }))
    await waitFor(() => expect(upload).toHaveBeenCalledTimes(2))
    expect(upload.mock.calls[0].slice(0, 4)).toEqual([batchId, 'invoice-v1', prepared, idempotencyKey])
    expect(upload.mock.calls[1].slice(0, 4)).toEqual([batchId, 'invoice-v1', prepared, idempotencyKey])
  })

  it('recovers the frozen upload after a security re-fence without showing the old generation result', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(batchId).mockReturnValueOnce(idempotencyKey)
    const firstUpload = vi.fn().mockResolvedValue({ kind: 'unknown' })
    const firstContext = { ...context, sessionEpoch: 31, securityNamespace: 'finance-maker-before-refence' }
    const view = render(<OfficialInvoiceImportUploadBoundForm context={firstContext} client={client(firstUpload)} />)
    chooseFileAndVersion()
    await previewSelectedFile()
    fireEvent.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    await screen.findByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' })

    const replay = vi.fn().mockResolvedValue({ kind: 'succeeded', replayed: true, batch: pending })
    const refenced = { ...firstContext, requestFenceGeneration: 6, capabilityGeneration: '12',
      securityNamespace: 'finance-maker-after-refence' }
    view.rerender(<OfficialInvoiceImportUploadBoundForm context={refenced} client={client(replay)} />)
    expect(screen.queryByRole('heading', { name: 'Onay bekliyor' })).toBeNull()
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' }))
    await screen.findByRole('heading', { name: 'Onay bekliyor' })
    expect(replay).toHaveBeenCalledWith(batchId, 'invoice-v1', prepared, idempotencyKey, expect.any(AbortSignal))
  })

  it('does not carry a frozen payload to another actor or company', async () => {
    vi.spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(batchId).mockReturnValueOnce(idempotencyKey)
    const firstContext = { ...context, sessionEpoch: 32 }
    const view = render(<OfficialInvoiceImportUploadBoundForm context={firstContext}
      client={client(vi.fn().mockResolvedValue({ kind: 'unknown' }))} />)
    chooseFileAndVersion()
    await previewSelectedFile()
    fireEvent.click(screen.getByRole('button', { name: 'Onaya gönder' }))
    await screen.findByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' })

    view.rerender(<OfficialInvoiceImportUploadBoundForm context={{ ...firstContext, actorId: 'other-actor',
      companyId: '90000000-0000-4000-8000-000000000009', securityNamespace: 'other-company' }}
      client={client(vi.fn())} />)
    expect(screen.queryByRole('button', { name: 'Aynı anahtar ve dosyayla yeniden gönder' })).toBeNull()
    expect((screen.getByRole('button', { name: 'Onaya gönder' }) as HTMLButtonElement).disabled).toBe(true)
  })
})
