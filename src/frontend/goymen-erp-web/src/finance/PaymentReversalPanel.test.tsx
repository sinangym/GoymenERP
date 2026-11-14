import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { ManualBankPaymentDetailPage } from './ManualBankPaymentPages'
import {
  ACTOR,
  COMPANY_A,
  failedOutcome,
  found,
  NO_REPLY,
  recordingPort,
  renderFinance,
  shellFor,
  succeeded,
} from './testHarness'

const PAYMENT = '22222222-2222-4222-8222-222222222222'
const PROPOSAL = 'a1111111-1111-4111-8111-111111111111'
const EVENT = 'a3333333-3333-4333-8333-333333333333'

function recorded() {
  return {
    id: PAYMENT, companyId: COMPANY_A, status: 'Recorded',
    linkedDocument: null,
    preparation: {
      partyId: '33333333-3333-4333-8333-333333333333', companyAccountId: '44444444-4444-4444-8444-444444444444',
      treasuryAccountId: '55555555-5555-4555-8555-555555555555',
      money: { amount: '1250.50', currencyId: '66666666-6666-4666-8666-666666666666' },
      profile: { profileId: '77777777-7777-4777-8777-777777777777', version: '1' },
      occurredOn: '2026-09-23', valueOn: null, bankReference: null, description: 'SENTETIK',
    },
    preparedBy: 'maker', preparedAtUtc: '2026-09-23T18:00:00Z',
    approvedBy: 'checker', approvedAtUtc: '2026-09-23T18:10:00Z',
    recordedBy: 'checker', recordedAtUtc: '2026-09-23T18:20:00Z',
    bankResultConfirmed: true, version: '3',
  }
}

function proposal(proposedBy = 'maker-other') {
  return {
    id: PROPOSAL, paymentId: PAYMENT, occurredOn: '2026-09-24', reason: 'SENTETIK hatalı ödeme', status: 'Pending',
    proposedBy, proposedAtUtc: '2026-09-24T09:00:00+00:00', decidedBy: null, decidedAtUtc: null,
    decisionReason: null, reversalEventId: null, version: '1',
  }
}

function setup(pendingItems: () => unknown[], options: { propose?: boolean; approve?: boolean } = {}) {
  const recorder = recordingPort((method) => {
    if (method === 'manualBankPaymentDetail') return found(recorded())
    if (method === 'pendingPaymentReversals') return found({ items: pendingItems() })
    return found(null)
  })
  const capabilities = [
    ...(options.propose ? ['finance.manual-bank-payments.reverse.propose'] : []),
    ...(options.approve ? ['finance.manual-bank-payments.reverse.approve'] : []),
  ]
  renderFinance(
    <ManualBankPaymentDetailPage companyId={COMPANY_A} actorId={ACTOR} paymentId={PAYMENT}
      canEdit={false} canApprove={false} canRecord={false}
      canReversePropose={options.propose ?? false} canReverseApprove={options.approve ?? false}
      onOpen={() => undefined} />,
    { port: recorder.port, shell: shellFor(COMPANY_A, 1, capabilities), router: true },
  )
  return recorder
}

async function fillProposal(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: 'Ters kayıt öner' }))
  await user.type(screen.getByLabelText('Ters kayıt işlem tarihi'), '2026-09-24')
  await user.type(screen.getByLabelText('Ters kayıt gerekçesi'), 'SENTETIK hatalı ödeme')
  await user.click(screen.getByRole('button', { name: 'Öneriyi gönder' }))
}

describe('API-23 payment reversal panel', () => {
  it('is not shown without a reversal capability', async () => {
    setup(() => [])
    await screen.findByRole('button', { name: 'Kesin kaydı yeniden aç' })
    expect(screen.queryByRole('region', { name: 'Ödeme ters kaydı' })).toBeNull()
  })

  it('sends the maker proposal with the payment version, date and reason', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorder = setup(() => (reads++ === 0 ? [] : [proposal(ACTOR)]), { propose: true })
    expect(await screen.findByText('Bu ödeme için onay bekleyen ters kayıt önerisi yok.')).toBeTruthy()
    await fillProposal(user)

    expect(recorder.commands).toHaveLength(1)
    expect(recorder.commands[0]).toMatchObject({
      method: 'proposePaymentReversal',
      scope: { companyId: COMPANY_A },
      request: { paymentId: PAYMENT, expectedVersion: '3', occurredOn: '2026-09-24', reason: 'SENTETIK hatalı ödeme' },
    })
    await recorder.commands[0].resolve(succeeded(proposal(ACTOR)))
    expect(await screen.findByText('Bu öneriyi siz hazırladınız; kararı başka bir yetkili vermeli.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Onayla ve ters kaydı oluştur' })).toBeNull()
  })

  it('refuses an empty form in the screen without sending anything', async () => {
    const user = userEvent.setup()
    const recorder = setup(() => [], { propose: true })
    await user.click(await screen.findByRole('button', { name: 'Ters kayıt öner' }))
    await user.click(screen.getByRole('button', { name: 'Öneriyi gönder' }))
    expect(recorder.commands).toHaveLength(0)
    expect(screen.getByLabelText('Ters kayıt işlem tarihi').getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByLabelText('Ters kayıt gerekçesi').getAttribute('aria-invalid')).toBe('true')
  })

  it('does not offer a decision on the actor’s own proposal', async () => {
    setup(() => [proposal(ACTOR)], { approve: true })
    expect(await screen.findByText('Bu öneriyi siz hazırladınız; kararı başka bir yetkili vermeli.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Onayla ve ters kaydı oluştur' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reddet' })).toBeNull()
  })

  it('sends the checker approval with the proposal version', async () => {
    const user = userEvent.setup()
    let reads = 0
    const recorder = setup(() => (reads++ === 0 ? [proposal()] : []), { approve: true })
    await user.click(await screen.findByRole('button', { name: 'Onayla ve ters kaydı oluştur' }))

    expect(recorder.commands).toHaveLength(1)
    expect(recorder.commands[0]).toMatchObject({
      method: 'approvePaymentReversal',
      scope: { companyId: COMPANY_A },
      request: { proposalId: PROPOSAL, expectedVersion: '1' },
    })
    await recorder.commands[0].resolve(succeeded({
      ...proposal(), status: 'Approved', decidedBy: ACTOR, decidedAtUtc: '2026-09-24T10:00:00+00:00',
      reversalEventId: EVENT, version: '2',
    }))
    await waitFor(() => expect(screen.getByText('Bu ödeme için onay bekleyen ters kayıt önerisi yok.')).toBeTruthy())
  })

  it('requires a rejection reason and sends it', async () => {
    const user = userEvent.setup()
    const recorder = setup(() => [proposal()], { approve: true })
    const reject = await screen.findByRole('button', { name: 'Reddet' })
    await user.click(reject)
    expect(recorder.commands).toHaveLength(0)
    await user.type(screen.getByLabelText('Ret gerekçesi'), 'Belge uygun değil')
    await user.click(reject)
    expect(recorder.commands[0]).toMatchObject({
      method: 'rejectPaymentReversal',
      request: { proposalId: PROPOSAL, expectedVersion: '1', reason: 'Belge uygun değil' },
    })
  })

  it('keeps the same idempotency key when an unknown outcome is resent', async () => {
    const user = userEvent.setup()
    const recorder = setup(() => [], { propose: true })
    await fillProposal(user)
    await recorder.commands[0].resolve(NO_REPLY)
    await user.click(await screen.findByRole('button', { name: 'Aynı işlemi tekrar gönder' }))

    expect(recorder.commands).toHaveLength(2)
    expect(recorder.commands[1].key).toBe(recorder.commands[0].key)
    expect(recorder.commands[1].request).toEqual(recorder.commands[0].request)
  })

  it('shows the server refusal by issue code', async () => {
    const user = userEvent.setup()
    const recorder = setup(() => [], { propose: true })
    await fillProposal(user)
    await recorder.commands[0].resolve(failedOutcome('ValidationFailed', {
      issues: [{ field: 'paymentId', code: 'finance_source_already_reversed' }],
    }))
    expect(await screen.findByText('Bu kayıt zaten ters kaydedilmiş.')).toBeTruthy()
  })
})
