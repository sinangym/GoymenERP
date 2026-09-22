import { afterEach, describe, expect, it, vi } from 'vitest'
import { createHttpFinancePort, MANUAL_RECEIPTS_PATH, REVERSAL_PROPOSALS_PATH } from './httpPort'

const companyId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const sourceId = '11111111-1111-4111-8111-111111111111'
const proposalId = '22222222-2222-4222-8222-222222222222'

afterEach(() => vi.unstubAllGlobals())

describe('FIN-T06 HTTP port', () => {
  it('uses the published proposal, decision and read routes without a legacy reverse request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('{}', {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)
    const port = createHttpFinancePort()
    const scope = { companyId }
    const signal = new AbortController().signal

    await port.proposeReversal(scope, 'key-propose', {
      sourceId, expectedVersion: '1', occurredOn: '2026-09-22', reason: 'Hatalı tahsilat',
    })
    await port.approveReversal(scope, 'key-approve', { proposalId, expectedVersion: '1' })
    await port.rejectReversal(scope, 'key-reject', { proposalId, expectedVersion: '1', reason: 'Belge uygun değil' })
    await port.pendingReversals(scope, signal)
    await port.reversalProposal(scope, proposalId, signal)

    const calls = fetchMock.mock.calls as [string, RequestInit][]
    expect(calls[0][0]).toContain(`${MANUAL_RECEIPTS_PATH}/${sourceId}/reversal-proposals`)
    expect(calls[1][0]).toContain(`${REVERSAL_PROPOSALS_PATH}/${proposalId}/approval`)
    expect(calls[2][0]).toContain(`${REVERSAL_PROPOSALS_PATH}/${proposalId}/rejection`)
    expect(calls[3][0]).toContain(`${REVERSAL_PROPOSALS_PATH}/pending`)
    expect(calls[4][0]).toContain(`${REVERSAL_PROPOSALS_PATH}/${proposalId}`)
    expect(new Headers(calls[0][1].headers).get('Idempotency-Key')).toBe('key-propose')
    expect(JSON.parse(calls[0][1].body as string)).toEqual({
      sourceId, expectedVersion: '1', occurredOn: '2026-09-22', reason: 'Hatalı tahsilat',
    })
  })
})
