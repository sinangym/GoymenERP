import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { ReversalApprovalsPage } from './ReversalApprovalsPage'
import {
  ACTOR,
  COMPANY_A,
  found,
  recordingPort,
  renderFinance,
  shellFor,
  succeeded,
} from './testHarness'

const proposalId = '11111111-1111-4111-8111-111111111111'
const originalSourceId = '22222222-2222-4222-8222-222222222222'
const reversalSourceId = '33333333-3333-4333-8333-333333333333'

function proposal(proposedBy = 'maker-other') {
  return {
    id: proposalId,
    originalSourceId,
    occurredOn: '2026-09-22',
    reason: 'Hatalı tahsilat',
    status: 'Pending',
    proposedBy,
    proposedAtUtc: '2026-09-21T12:00:00+00:00',
    decidedBy: null,
    decidedAtUtc: null,
    decisionReason: null,
    reversalSourceId: null,
    version: '1',
  }
}

describe('FIN-T06 reversal checker screen', () => {
  it('does not offer a decision to the proposal maker', async () => {
    const recorder = recordingPort((method) =>
      method === 'pendingReversals' ? found({ items: [proposal(ACTOR)] }) : found(null))
    renderFinance(<ReversalApprovalsPage />, {
      port: recorder.port,
      shell: shellFor(COMPANY_A, 1, ['finance.receipts.reverse.approve']),
      router: true,
    })

    expect(await screen.findByText(/kararı başka bir yetkili vermeli/i)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /onayla ve ters kaydı oluştur/i })).toBeNull()
    expect(screen.queryByRole('button', { name: /^reddet$/i })).toBeNull()
  })

  it('sends the exact checker approval and links the separately created reversal source', async () => {
    const user = userEvent.setup()
    const recorder = recordingPort((method) =>
      method === 'pendingReversals' ? found({ items: [proposal()] }) : found(null))
    renderFinance(<ReversalApprovalsPage />, {
      port: recorder.port,
      shell: shellFor(COMPANY_A, 1, ['finance.receipts.reverse.approve']),
      router: true,
    })

    await user.click(await screen.findByRole('button', { name: /onayla ve ters kaydı oluştur/i }))
    expect(recorder.commands).toHaveLength(1)
    expect(recorder.commands[0]).toMatchObject({
      method: 'approveReversal',
      scope: { companyId: COMPANY_A },
      request: { proposalId, expectedVersion: '1' },
    })

    await recorder.commands[0].resolve(succeeded({
      ...proposal(),
      status: 'Approved',
      decidedBy: ACTOR,
      decidedAtUtc: '2026-09-21T13:00:00+00:00',
      reversalSourceId,
      version: '2',
    }))
    const link = await screen.findByRole('link', { name: /oluşturulan ters kaydı aç/i })
    expect(link.getAttribute('href')).toBe(`/finans/tahsilatlar/${reversalSourceId}`)
  })

  it('keeps rejection closed until a reason and sends a separate reject command', async () => {
    const user = userEvent.setup()
    const recorder = recordingPort((method) =>
      method === 'pendingReversals' ? found({ items: [proposal()] }) : found(null))
    renderFinance(<ReversalApprovalsPage />, {
      port: recorder.port,
      shell: shellFor(COMPANY_A, 1, ['finance.receipts.reverse.approve']),
      router: true,
    })

    const reject = await screen.findByRole('button', { name: /^reddet$/i })
    expect((reject as HTMLButtonElement).disabled).toBe(true)
    await user.type(screen.getByRole('textbox', { name: /ret gerekçesi/i }), 'Belge doğrulanamadı')
    expect((reject as HTMLButtonElement).disabled).toBe(false)
    await user.click(reject)

    await waitFor(() => expect(recorder.commands).toHaveLength(1))
    expect(recorder.commands[0]).toMatchObject({
      method: 'rejectReversal',
      request: { proposalId, expectedVersion: '1', reason: 'Belge doğrulanamadı' },
    })
  })
})
