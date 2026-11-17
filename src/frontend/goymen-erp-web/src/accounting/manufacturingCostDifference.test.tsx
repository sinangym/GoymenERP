import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { decodeCostDifference, sendCostDifference } from './manufacturingCostDifferenceApi'
import { ACCOUNTING_CAPABILITY } from './capabilities'
import { COMPANY_A, COMPANY_B, PREPARER, APPROVER, ACCOUNT_DEBIT, ACCOUNT_CREDIT, PERIOD_SEPTEMBER, installFetch, json, renderAt, requestsFor } from './testHarness'

const proposalId = 'c0623000-0000-4000-8000-000000000001'
const source = 'c0623000-0000-4000-8000-000000000002'
const fixture = (patch: Record<string, unknown> = {}) => ({ proposalId, companyId: COMPANY_A, manufacturingOrderId: source,
  generationId: source, generationVersion: '2', status: 'Prepared', version: 2, signedTlDifference: '600',
  outputs: [{ outputId: source, receiptMovementId: source, labelId: source, netQuantityKg: '100', remainingQuantityKg: '70',
    consumedQuantityKg: '30', tlUnitCostPerKg: '20', remainingTlValue: '1400', consumedTlDifference: '600', usdUnitCostPerKg: '4', originalTlAmount: '2000' }],
  sourceOccurredAtUtc: '2026-11-17T09:00:00Z', periodId: PERIOD_SEPTEMBER, accountingDate: '2026-09-17', transactionDate: '2026-09-16',
  debitAccountId: ACCOUNT_DEBIT, creditAccountId: ACCOUNT_CREDIT, preparedBy: PREPARER, approvedBy: null, journalId: null,
  reason: 'SENTETIK source cost', accountPlan: { packageId: source, packageVersion: 2, catalogVersion: 1,
    sourceVersion: 'SENTETIK-plan', sha256: 'a'.repeat(64), debit: { accountId: ACCOUNT_DEBIT, code: '620', name: 'SENTETIK debit' },
    credit: { accountId: ACCOUNT_CREDIT, code: '151', name: 'SENTETIK credit' } }, ...patch })

afterEach(() => { cleanup(); vi.unstubAllGlobals() })
describe('manufacturing consumed cost accounting suggestion', () => {
  it('displays the exact backend quantity fraction without computing or accepting a money fraction', () => {
    const output = { ...fixture().outputs[0], remainingQuantityKg: '2 / 3', consumedQuantityKg: '1 / 3' }
    expect(decodeCostDifference(fixture({ outputs: [output] }), COMPANY_A)?.outputs[0].consumedQuantityKg).toBe('1 / 3')
    for (const quantity of ['1/3', '1 / 0', '-1 / 3', '01 / 3', '1 / 03', 'NaN']) {
      expect(decodeCostDifference(fixture({ outputs: [{ ...output, consumedQuantityKg: quantity }] }), COMPANY_A)).toBeNull()
    }
    expect(decodeCostDifference(fixture({ outputs: [{ ...output, consumedTlDifference: '1 / 3' }] }), COMPANY_A)).toBeNull()
  })
  it('preserves decimal text and refuses foreign or incomplete preparation', () => {
    expect(decodeCostDifference(fixture(), COMPANY_A)?.signedTlDifference).toBe('600')
    expect(decodeCostDifference(fixture({ companyId: COMPANY_B }), COMPANY_A)).toBeNull()
    expect(decodeCostDifference(fixture({ periodId: null }), COMPANY_A)).toBeNull()
    expect(decodeCostDifference(fixture({ signedTlDifference: 600 }), COMPANY_A)).toBeNull()
    expect(decodeCostDifference(fixture({ status: 'Posted', approvedBy: PREPARER, journalId: source }), COMPANY_A)).toBeNull()
  })
  it('shows the source without offering the maker an approval', async () => {
    const requests = installFetch({ identityUserId: PREPARER, capabilities: [ACCOUNTING_CAPABILITY.costDifferenceApprove],
      respond: url => url.endsWith('/manufacturing-cost-differences') ? json({ companyId: COMPANY_A, items: [fixture()] }) : undefined })
    renderAt('/muhasebe/uretim-maliyet-farki-onay')
    fireEvent.click(await screen.findByRole('button', { name: 'Öneriyi aç' }))
    expect(await screen.findByText('Hazırladığınız öneriyi başka bir muhasebe yetkilisi onaylamalı.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Onayla ve fişi oluştur' })).toBeNull()
    expect(requests.filter(request => request.method === 'POST')).toHaveLength(0)
  })
  it('keeps the exact command after an unknown response and retries its same key and body', async () => {
    let calls = 0
    const requests = installFetch({ identityUserId: APPROVER, capabilities: [ACCOUNTING_CAPABILITY.costDifferenceApprove], respond: (url, init) => {
      if (init?.method === 'POST') { calls++; return calls === 1 ? new Response('{}', { status: 503 }) : json(fixture({ status: 'Posted', version: 3, approvedBy: APPROVER, journalId: source })) }
      return url.endsWith('/manufacturing-cost-differences') ? json({ companyId: COMPANY_A, items: [fixture()] }) : undefined
    } })
    renderAt('/muhasebe/uretim-maliyet-farki-onay')
    fireEvent.click(await screen.findByRole('button', { name: 'Öneriyi aç' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Onayla ve fişi oluştur' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı komutu yeniden gönder' }))
    await waitFor(() => expect(requestsFor(requests, '/approval', 'POST')).toHaveLength(2))
    const commands = requestsFor(requests, '/approval', 'POST')
    expect(commands[0].headers['Idempotency-Key']).toBe(commands[1].headers['Idempotency-Key'])
    expect(commands[0].body).toEqual(commands[1].body)
  })
  it('does not claim a malformed successful response is a definite refusal or success', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ status: 'Posted' })))
    expect(await sendCostDifference(COMPANY_A, { operation: 'approve', id: proposalId, key: 'same-key', request: { expectedVersion: 2 } })).toEqual({ type: 'unknown' })
  })
})
