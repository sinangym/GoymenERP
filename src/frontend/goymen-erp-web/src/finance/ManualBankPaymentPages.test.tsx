import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { FinancePortProvider } from './financePort'
import type { ManualBankPaymentView } from './manualBankPaymentContract'
import type { FinanceReceiptPort } from './port'
import {
  ManualBankPaymentCreatePage,
  ManualBankPaymentDetailPage,
  ManualBankPaymentListPage,
} from './ManualBankPaymentPages'

const COMPANY = '11111111-1111-4111-8111-111111111111'
const PAYMENT = '22222222-2222-4222-8222-222222222222'
const PARTY = '33333333-3333-4333-8333-333333333333'
const ACCOUNT = '44444444-4444-4444-8444-444444444444'
const BANK = '55555555-5555-4555-8555-555555555555'
const CURRENCY = '66666666-6666-4666-8666-666666666666'
const PROFILE = '77777777-7777-4777-8777-777777777777'

function view(status: 'Draft' | 'Approved' | 'Recorded') {
  return {
    id: PAYMENT, companyId: COMPANY, status,
    linkedDocument: null,
    preparation: {
      partyId: PARTY, companyAccountId: ACCOUNT, treasuryAccountId: BANK,
      money: { amount: '1250.50', currencyId: CURRENCY }, profile: { profileId: PROFILE, version: '1' },
      occurredOn: '2026-09-23', valueOn: null, bankReference: null, description: 'SENTETIK',
    },
    preparedBy: 'maker', preparedAtUtc: '2026-09-23T18:00:00Z',
    approvedBy: status === 'Draft' ? null : 'checker', approvedAtUtc: status === 'Draft' ? null : '2026-09-23T18:10:00Z',
    recordedBy: status === 'Recorded' ? 'checker' : null, recordedAtUtc: status === 'Recorded' ? '2026-09-23T18:20:00Z' : null,
    bankResultConfirmed: status === 'Recorded', version: status === 'Draft' ? '1' : status === 'Approved' ? '2' : '3',
  }
}

function success(resource: ManualBankPaymentView) {
  return { kind: 'body' as const, body: { outcome: 'Succeeded', resource, isReplay: false, originalCorrelationId: 'c', issues: [] } }
}

function provider(methods: Partial<FinanceReceiptPort>) {
  return methods as FinanceReceiptPort
}

describe('manual bank payment pages', () => {
  it('creates A-now draft without bank reference and opens the saved record', async () => {
    const save = vi.fn(async () => success(view('Draft')))
    const open = vi.fn()
    render(<FinancePortProvider port={provider({ saveManualBankPaymentDraft: save })}>
      <ManualBankPaymentCreatePage companyId={COMPANY} onOpen={open} entry={{
        partyId: PARTY, companyAccountId: ACCOUNT, treasuryAccountId: BANK, currencyId: CURRENCY,
        profile: { profileId: PROFILE, version: '1' },
      }} />
    </FinancePortProvider>)
    fireEvent.change(screen.getByLabelText('Tutar'), { target: { value: '1250,50' } })
    fireEvent.change(screen.getByLabelText('İşlem tarihi'), { target: { value: '2026-09-23' } })
    fireEvent.click(screen.getByRole('button', { name: 'Taslağı kaydet' }))

    await screen.findByRole('heading', { name: 'Giden ödeme taslağı kaydedildi' })
    expect(save).toHaveBeenCalledWith(
      { companyId: COMPANY }, expect.any(String),
      expect.objectContaining({ preparation: expect.objectContaining({ bankReference: null }), linkedDocument: null }),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Kaydı aç' }))
    expect(open).toHaveBeenCalledWith(PAYMENT)
  })

  it('lists the company record and opens it', async () => {
    const open = vi.fn()
    const create = vi.fn()
    const list = vi.fn(async () => ({ kind: 'body' as const, body: {
      outcome: 'Found', resource: { companyId: COMPANY, items: [view('Draft')], nextCursor: null },
    } }))
    render(<FinancePortProvider port={provider({ listManualBankPayments: list })}>
      <ManualBankPaymentListPage companyId={COMPANY} onOpen={open} onCreate={create} />
    </FinancePortProvider>)
    await screen.findByText('1250.50')
    expect(screen.getAllByRole('button', { name: 'Yeni' })).toHaveLength(1)
    fireEvent.click(screen.getByRole('button', { name: 'Yeni' }))
    expect(create).toHaveBeenCalledOnce()
    fireEvent.click(screen.getByRole('button', { name: 'Aç' }))
    expect(open).toHaveBeenCalledWith(PAYMENT)
  })

  it('does not offer create from the list without its capability callback', async () => {
    const list = vi.fn(async () => ({ kind: 'body' as const, body: {
      outcome: 'Found', resource: { companyId: COMPANY, items: [], nextCursor: null },
    } }))
    render(<FinancePortProvider port={provider({ listManualBankPayments: list })}>
      <ManualBankPaymentListPage companyId={COMPANY} onOpen={() => undefined} />
    </FinancePortProvider>)

    await screen.findByText('Kayıt yok.')
    expect(screen.queryByRole('button', { name: 'Yeni' })).toBeNull()
  })

  it('requires explicit result confirmation and keeps the terminal reopen action', async () => {
    const detail = vi.fn(async () => ({ kind: 'body' as const, body: { outcome: 'Found', resource: view('Approved') } }))
    const record = vi.fn(async () => success(view('Recorded')))
    const open = vi.fn()
    render(<FinancePortProvider port={provider({ manualBankPaymentDetail: detail, recordManualBankPayment: record })}>
      <ManualBankPaymentDetailPage companyId={COMPANY} actorId="checker" paymentId={PAYMENT}
        canEdit canApprove={false} canRecord onOpen={open} />
    </FinancePortProvider>)
    const recordButton = await screen.findByRole('button', { name: 'Kesin kaydet' })
    expect(screen.queryByRole('button', { name: 'Düzenle' })).toBeNull()
    expect((recordButton as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByLabelText('Banka sonucu gerçekleşti'))
    fireEvent.click(recordButton)
    await screen.findByText('Giden ödeme kesin kaydedildi.')
    expect(record).toHaveBeenCalledWith({ companyId: COMPANY }, expect.any(String), {
      paymentId: PAYMENT, expectedVersion: '2', bankResultConfirmed: true,
    })
    fireEvent.click(screen.getByRole('button', { name: 'Kesin kaydı yeniden aç' }))
    expect(open).toHaveBeenCalledWith(PAYMENT)
    expect(screen.queryByRole('button', { name: 'Düzenle' })).toBeNull()
  })

  it('reopens a draft for complete replacement and clears nullable fields explicitly', async () => {
    const original = {
      ...view('Draft'),
      linkedDocument: { documentId: '88888888-8888-4888-8888-888888888888', version: '4' },
      preparation: {
        ...view('Draft').preparation,
        valueOn: '2026-09-24', bankReference: 'BANK-OLD', description: 'Eski açıklama',
      },
    }
    const updated = {
      ...original,
      version: '2',
      linkedDocument: null,
      preparation: {
        ...original.preparation,
        money: { ...original.preparation.money, amount: '1300.75' },
        occurredOn: '2026-09-25', valueOn: null, bankReference: null, description: null,
      },
    }
    const detail = vi.fn(async () => ({ kind: 'body' as const, body: { outcome: 'Found', resource: original } }))
    const save = vi.fn(async () => success(updated))
    render(<FinancePortProvider port={provider({ manualBankPaymentDetail: detail, saveManualBankPaymentDraft: save })}>
      <ManualBankPaymentDetailPage companyId={COMPANY} actorId="maker" paymentId={PAYMENT}
        canEdit canApprove={false} canRecord={false} onOpen={() => undefined} />
    </FinancePortProvider>)

    fireEvent.click(await screen.findByRole('button', { name: 'Düzenle' }))
    expect((screen.getByLabelText('Tutar') as HTMLInputElement).value).toBe('1250,50')
    fireEvent.change(screen.getByLabelText('Tutar'), { target: { value: '1300,75' } })
    fireEvent.change(screen.getByLabelText('İşlem tarihi'), { target: { value: '2026-09-25' } })
    fireEvent.change(screen.getByLabelText('Valör'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Banka referansı (opsiyonel)'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Açıklama'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Belge kimliği (opsiyonel)'), { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Belge sürümü (opsiyonel)'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))

    await screen.findByText('Giden ödeme taslağı güncellendi.')
    expect(save).toHaveBeenCalledWith({ companyId: COMPANY }, expect.any(String), {
      paymentId: PAYMENT,
      expectedVersion: '1',
      preparation: {
        partyId: PARTY,
        companyAccountId: ACCOUNT,
        treasuryAccountId: BANK,
        money: { amount: '1300.75', currencyId: CURRENCY },
        profile: { profileId: PROFILE, version: '1' },
        occurredOn: '2026-09-25',
        valueOn: null,
        bankReference: null,
        description: null,
      },
      linkedDocument: null,
    })
    expect(screen.queryByRole('button', { name: 'Değişiklikleri kaydet' })).toBeNull()
    expect(screen.getByText('1300.75')).toBeTruthy()
  })

  it('freezes and retries an unknown edit with the same body and key, then resets after terminal success', async () => {
    const original = view('Draft')
    const updated = {
      ...original,
      version: '2',
      preparation: { ...original.preparation, money: { ...original.preparation.money, amount: '1300' } },
    }
    const updatedAgain = {
      ...updated,
      version: '3',
      preparation: { ...updated.preparation, money: { ...updated.preparation.money, amount: '1400' } },
    }
    const detail = vi.fn(async () => ({ kind: 'body' as const, body: { outcome: 'Found', resource: original } }))
    const save = vi.fn()
      .mockResolvedValueOnce({ kind: 'no-reply' as const })
      .mockResolvedValueOnce(success(updated))
      .mockResolvedValueOnce(success(updatedAgain))
    render(<FinancePortProvider port={provider({ manualBankPaymentDetail: detail, saveManualBankPaymentDraft: save })}>
      <ManualBankPaymentDetailPage companyId={COMPANY} actorId="maker" paymentId={PAYMENT}
        canEdit canApprove={false} canRecord={false} onOpen={() => undefined} />
    </FinancePortProvider>)

    fireEvent.click(await screen.findByRole('button', { name: 'Düzenle' }))
    fireEvent.change(screen.getByLabelText('Tutar'), { target: { value: '1300' } })
    fireEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))
    await screen.findByText('Sonuç bilinmiyor. Aynı anahtar ve değişmeyen alanlarla tekrar deneyin.')
    expect((screen.getByLabelText('Tutar') as HTMLInputElement).disabled).toBe(true)
    const [firstScope, firstKey, firstBody] = save.mock.calls[0]

    fireEvent.click(screen.getByRole('button', { name: 'Aynı anahtarla tekrar dene' }))
    await screen.findByText('Giden ödeme taslağı güncellendi.')
    expect(save.mock.calls[1][0]).toEqual(firstScope)
    expect(save.mock.calls[1][1]).toBe(firstKey)
    expect(save.mock.calls[1][2]).toBe(firstBody)

    fireEvent.click(screen.getByRole('button', { name: 'Düzenle' }))
    fireEvent.change(screen.getByLabelText('Tutar'), { target: { value: '1400' } })
    fireEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))
    await waitFor(() => expect(save).toHaveBeenCalledTimes(3))
    expect(save.mock.calls[2][1]).not.toBe(firstKey)
    expect(save.mock.calls[2][2]).not.toBe(firstBody)
    expect(save.mock.calls[2][2]).toMatchObject({ paymentId: PAYMENT, expectedVersion: '2' })
  })

  it('shows approval only to a non-maker and carries the current version', async () => {
    const detail = vi.fn(async () => ({ kind: 'body' as const, body: { outcome: 'Found', resource: view('Draft') } }))
    const approve = vi.fn(async () => success(view('Approved')))
    render(<FinancePortProvider port={provider({ manualBankPaymentDetail: detail, approveManualBankPayment: approve })}>
      <ManualBankPaymentDetailPage companyId={COMPANY} actorId="checker" paymentId={PAYMENT}
        canEdit={false} canApprove canRecord onOpen={() => undefined} />
    </FinancePortProvider>)
    fireEvent.click(await screen.findByRole('button', { name: 'Onayla' }))
    await waitFor(() => expect(approve).toHaveBeenCalledWith(
      { companyId: COMPANY }, expect.any(String), { paymentId: PAYMENT, expectedVersion: '1' },
    ))
    await screen.findByRole('button', { name: 'Kesin kaydet' })
  })

  it('hides approval and record controls without their capabilities', async () => {
    const draftDetail = vi.fn(async () => ({ kind: 'body' as const, body: { outcome: 'Found', resource: view('Draft') } }))
    const draft = render(<FinancePortProvider port={provider({ manualBankPaymentDetail: draftDetail })}>
      <ManualBankPaymentDetailPage companyId={COMPANY} actorId="checker" paymentId={PAYMENT}
        canEdit={false} canApprove={false} canRecord={false} onOpen={() => undefined} />
    </FinancePortProvider>)
    await screen.findByText('1250.50')
    expect(screen.queryByRole('button', { name: 'Onayla' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Düzenle' })).toBeNull()
    draft.unmount()

    const approvedDetail = vi.fn(async () => ({ kind: 'body' as const, body: { outcome: 'Found', resource: view('Approved') } }))
    render(<FinancePortProvider port={provider({ manualBankPaymentDetail: approvedDetail })}>
      <ManualBankPaymentDetailPage companyId={COMPANY} actorId="checker" paymentId={PAYMENT}
        canEdit canApprove={false} canRecord={false} onOpen={() => undefined} />
    </FinancePortProvider>)
    await screen.findByText('Approved')
    expect(screen.queryByLabelText('Banka sonucu gerçekleşti')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Kesin kaydet' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Düzenle' })).toBeNull()
  })
})
