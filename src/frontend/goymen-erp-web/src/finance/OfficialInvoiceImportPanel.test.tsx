import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  OfficialInvoiceImportPanel,
  type OfficialInvoiceAppliedBatch,
  type OfficialInvoiceApplyReply,
  type OfficialInvoiceImportClient,
  type OfficialInvoiceImportPreview,
} from './OfficialInvoiceImportPanel'

const source = {
  batchId: '11111111-1111-4111-8111-111111111111',
  batchVersion: 'official-file-v1',
  displayName: 'Eylül onaylı fatura taksitleri',
}
const checksum = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
let nextSessionEpoch = 0
let attemptContext = {
  sessionEpoch: 1,
  actorId: 'SENTETIK-finance-user',
  companyId: '99999999-9999-4999-8999-999999999999',
}
const otherSource = {
  batchId: '88888888-8888-4888-8888-888888888888',
  batchVersion: 'official-file-v2',
  displayName: 'Ekim onaylı fatura taksitleri',
}

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

function batch(
  outstandingAmount = '1250.50',
  version = '1',
  overrides: Partial<OfficialInvoiceAppliedBatch> = {},
): OfficialInvoiceAppliedBatch {
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
    ...overrides,
  }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
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

beforeEach(() => {
  nextSessionEpoch += 1
  attemptContext = { ...attemptContext, sessionEpoch: nextSessionEpoch }
})

describe('official invoice import panel', () => {
  it('previews, applies, closes and reopens the same persisted Finance batch', async () => {
    const user = userEvent.setup()
    const api = client()
    render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
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
    render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
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
    render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
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
    render(<OfficialInvoiceImportPanel source={source} client={client()} attemptContext={attemptContext} />)
    await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    await screen.findByText(`SHA-256: ${checksum}`)

    fireEvent.change(screen.getByLabelText(/utf-8 csv dosyası/i), {
      target: { files: [new File(['other'], 'other.csv', { type: 'text/csv' })] },
    })

    expect(screen.queryByText(`SHA-256: ${checksum}`)).toBeNull()
    expect((screen.getByRole('button', { name: /yetkili uygula/i }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('retires a ready preview when the selected approved source changes', async () => {
    const user = userEvent.setup()
    const api = client()
    const view = render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
    await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    await screen.findByText(`SHA-256: ${checksum}`)

    view.rerender(<OfficialInvoiceImportPanel source={otherSource} client={api} attemptContext={attemptContext} />)

    expect(screen.getByText(otherSource.displayName)).toBeTruthy()
    expect(screen.queryByText(`SHA-256: ${checksum}`)).toBeNull()
    expect((screen.getByRole('button', { name: /yetkili uygula/i }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('ignores a late preview from the previous source', async () => {
    const user = userEvent.setup()
    const held = deferred<OfficialInvoiceImportPreview>()
    const api = client({ preview: vi.fn().mockReturnValue(held.promise) })
    const view = render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
    await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))

    view.rerender(<OfficialInvoiceImportPanel source={otherSource} client={api} attemptContext={attemptContext} />)
    held.resolve(preview())

    await waitFor(() => expect(screen.queryByText(`SHA-256: ${checksum}`)).toBeNull())
    expect(screen.getByText(otherSource.displayName)).toBeTruthy()
  })

  it('ignores a late apply success from the previous source', async () => {
    const user = userEvent.setup()
    const held = deferred<OfficialInvoiceApplyReply>()
    const api = client({ apply: vi.fn().mockReturnValue(held.promise) })
    const view = render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
    await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    await user.click(await screen.findByRole('button', { name: /yetkili uygula/i }))

    view.rerender(<OfficialInvoiceImportPanel source={otherSource} client={api} attemptContext={attemptContext} />)
    held.resolve({ kind: 'succeeded', replayed: false, batch: batch() })

    await waitFor(() => expect(screen.queryByText(/açık kalem finans'tan yeniden okundu/i)).toBeNull())
    expect(screen.getByText(otherSource.displayName)).toBeTruthy()
  })

  it('ignores a late reopen from the previous source', async () => {
    const user = userEvent.setup()
    const held = deferred<OfficialInvoiceAppliedBatch>()
    const api = client({ reopen: vi.fn().mockReturnValue(held.promise) })
    const view = render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
    await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    await user.click(await screen.findByRole('button', { name: /yetkili uygula/i }))
    await screen.findByText(/1 açık kalem finans'tan yeniden okundu/i)
    await user.click(screen.getByRole('button', { name: /sonucu kapat/i }))
    await user.click(screen.getByRole('button', { name: /yeniden aç/i }))

    view.rerender(<OfficialInvoiceImportPanel source={otherSource} client={api} attemptContext={attemptContext} />)
    held.resolve(batch('250.50', '2'))

    await waitFor(() => expect(screen.queryByText(/kalan 250.50/i)).toBeNull())
    expect(screen.getByText(otherSource.displayName)).toBeTruthy()
  })

  it('freezes a success whose batch version does not match the preview tuple', async () => {
    const user = userEvent.setup()
    const api = client({
      apply: vi.fn().mockResolvedValue({
        kind: 'succeeded', replayed: false, batch: batch('1250.50', '1', { batchVersion: 'wrong-version' }),
      }),
    })
    render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
    await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    await user.click(await screen.findByRole('button', { name: /yetkili uygula/i }))

    expect(await screen.findByText(/kaydedilen açık kalem sonucu önizlemeyle eşleşmiyor/i)).toBeTruthy()
    expect(screen.queryByText(/açık kalem finans'tan yeniden okundu/i)).toBeNull()
    expect((screen.getByLabelText(/utf-8 csv dosyası/i) as HTMLInputElement).disabled).toBe(true)
  })

  it('keeps the closed tuple when reopen returns a different version or checksum', async () => {
    const user = userEvent.setup()
    const api = client({
      reopen: vi.fn().mockResolvedValue(batch('250.50', '2', {
        batchChecksum: 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb',
      })),
    })
    render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
    await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    await user.click(await screen.findByRole('button', { name: /yetkili uygula/i }))
    await user.click(await screen.findByRole('button', { name: /sonucu kapat/i }))
    await user.click(screen.getByRole('button', { name: /yeniden aç/i }))

    expect(await screen.findByText(/yeniden açılan kayıt beklenen kaynak kimliğiyle eşleşmiyor/i)).toBeTruthy()
    expect(screen.queryByText(/kalan 250.50/i)).toBeNull()
    expect(screen.getByRole('button', { name: /yeniden aç/i })).toBeTruthy()
  })

  it('remounts a pending apply as frozen and resends the same file and key', async () => {
    const user = userEvent.setup()
    const held = deferred<OfficialInvoiceApplyReply>()
    const apply = vi.fn()
      .mockReturnValueOnce(held.promise)
      .mockResolvedValueOnce({ kind: 'succeeded', replayed: true, batch: batch() })
    const api = client({ apply })
    const first = render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
    const file = await upload(user)
    await user.click(screen.getByRole('button', { name: /^önizle$/i }))
    await user.click(await screen.findByRole('button', { name: /yetkili uygula/i }))
    first.unmount()

    render(<OfficialInvoiceImportPanel source={source} client={api} attemptContext={attemptContext} />)
    await user.click(await screen.findByRole('button', { name: /aynı anahtarla yeniden gönder/i }))

    await waitFor(() => expect(apply).toHaveBeenCalledTimes(2))
    expect(apply.mock.calls[1][1]).toBe(file)
    expect(apply.mock.calls[1][2]).toBe(apply.mock.calls[0][2])
    expect(await screen.findByText(/1 açık kalem finans'tan yeniden okundu/i)).toBeTruthy()
  })
})
