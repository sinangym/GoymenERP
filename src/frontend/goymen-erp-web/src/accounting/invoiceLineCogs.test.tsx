import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, screen, waitFor } from '@testing-library/react'
import { decodeCogsHistory, decodeCogsProposal, decodeCogsSourceSelection, sendCogsCorrection } from './invoiceLineCogsApi'
import { ACCOUNTING_CAPABILITY, invoiceLineCogsPath } from './capabilities'
import { COMPANY_A, COMPANY_B, PREPARER, APPROVER, ACCOUNT_DEBIT, PERIOD_SEPTEMBER, installFetch, json, renderAt, requestsFor } from './testHarness'

const invoice = 'c0871000-0000-4000-8000-000000000001'
const line = 'c0871000-0000-4000-8000-000000000002'
const proposal = 'c0871000-0000-4000-8000-000000000003'
const plan = { packageId: invoice, packageVersion: 2, catalogVersion: 1, sourceVersion: 'SENTETIK-plan', sourceSha256: 'A'.repeat(64),
  account: { accountId: ACCOUNT_DEBIT, code: '620', name: 'SENTETIK account' } }
const proposalFixture = (patch: Record<string, unknown> = {}) => ({ proposalId: proposal, companyId: COMPANY_A, invoiceId: invoice,
  invoiceLineId: line, finalizedVersion: 5, version: 1, expectedLineVersion: 0, status: 'Prepared', classification: 2,
  accountPlan: plan, periodId: PERIOD_SEPTEMBER, transactionDate: '2026-09-17', accountingDate: '2026-09-17', valueDate: null,
  preparedBy: PREPARER, preparedAtUtc: '2026-09-17T09:00:00Z', approvedBy: null, approvedAtUtc: null, reason: 'SENTETIK source correction',
  postedEvidence: null, reversingCorrectionJournalId: null, replacementCorrectionJournalId: null, ...patch })
const historyFixture = (patch: Record<string, unknown> = {}) => ({ companyId: COMPANY_A, invoiceId: invoice, invoiceLineId: line,
  source: { companyId: COMPANY_A, invoiceId: invoice, invoiceLineId: line, finalizedVersion: 5, finalizationAdmissionId: invoice,
    productId: invoice, shipmentId: invoice, dispatchNoteId: invoice, dispatchNoteLineNo: 1, invoiceLineNo: 1, invoiceDate: '2026-09-17',
    officialInvoiceNumber: 'SENTETIK-001', ettn: invoice, fingerprint: 'A'.repeat(64) },
  lineVersion: 0, current: null, revisions: [], proposals: [proposalFixture()], postingEvidenceState: 'NoPostedCogs', postedEvidence: null,
  productReference: { companyId: COMPANY_A, productId: invoice, outcome: 'NotSet', classification: null, productVersion: null, classificationVersion: null }, ...patch })

afterEach(() => { cleanup(); vi.unstubAllGlobals() })
describe('invoice line COGS correction', () => {
  it('keeps explicit unclassified Product references and distinguishes absent from unreadable posted evidence', () => {
    expect(decodeCogsHistory(historyFixture(), COMPANY_A, invoice, line)?.productReference.classification).toBeNull()
    expect(decodeCogsHistory(historyFixture({ postingEvidenceState: 'Unreadable' }), COMPANY_A, invoice, line)?.postingEvidenceState).toBe('Unreadable')
    expect(decodeCogsHistory(historyFixture({ postingEvidenceState: 'PostedCogs' }), COMPANY_A, invoice, line)).toBeNull()
    expect(decodeCogsHistory(historyFixture({ companyId: COMPANY_B }), COMPANY_A, invoice, line)).toBeNull()
    expect(decodeCogsHistory(historyFixture({ lineVersion: 1 }), COMPANY_A, invoice, line)).toBeNull()
    const withPrices = historyFixture(); Object.assign(withPrices.source, { commercialProofJson: '{"syntheticPrice":100}' })
    expect(decodeCogsHistory(withPrices, COMPANY_A, invoice, line)).toBeNull()
  })
  it('rejects self approval, incomplete correction journal pairs and an unrelated invoice line', () => {
    expect(decodeCogsProposal(proposalFixture({ status: 'Approved', version: 2, approvedBy: PREPARER, approvedAtUtc: '2026-09-17T10:00:00Z' }), COMPANY_A, invoice, line)).toBeNull()
    expect(decodeCogsProposal(proposalFixture({ reversingCorrectionJournalId: invoice }), COMPANY_A, invoice, line)).toBeNull()
    expect(decodeCogsProposal(proposalFixture(), COMPANY_A, invoice, invoice)).toBeNull()
  })
  it('rejects a foreign or malformed official-document selector result without exposing prices', () => {
    const source = historyFixture().source
    const selection = { companyId: COMPANY_A, invoiceId: invoice, officialInvoiceNumber: source.officialInvoiceNumber, ettn: source.ettn, lines: [source] }
    expect(decodeCogsSourceSelection(selection, COMPANY_A)?.lines[0].invoiceLineNo).toBe(1)
    expect(decodeCogsSourceSelection({ ...selection, lines: [{ ...source, companyId: COMPANY_B }] }, COMPANY_A)).toBeNull()
    expect(decodeCogsSourceSelection({ ...selection, lines: [{ ...source, commercialProofJson: '{"price":1}' }] }, COMPANY_A)).toBeNull()
  })
  it('does not offer the preparer an approval control', async () => {
    const requests = installFetch({ identityUserId: PREPARER, capabilities: [ACCOUNTING_CAPABILITY.invoiceLineCogsApprove], respond: url =>
      url.includes('/invoice-line-cogs/') ? json(historyFixture()) : undefined })
    renderAt(invoiceLineCogsPath(invoice, line, 'approve'))
    expect(await screen.findByText('Hazırladığınız öneriyi başka bir muhasebe yetkilisi onaylamalı.')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Satır düzeltmesini onayla' })).toBeNull()
    expect(requests.filter(request => request.method === 'POST')).toHaveLength(0)
  })
  it('freezes an unknown approval outcome and retries the identical request and key', async () => {
    let calls = 0
    const requests = installFetch({ identityUserId: APPROVER, capabilities: [ACCOUNTING_CAPABILITY.invoiceLineCogsApprove], respond: (url, init) => {
      if (init?.method === 'POST') { calls++; return calls === 1 ? new Response('{}', { status: 503 })
        : json(proposalFixture({ status: 'Approved', version: 2, approvedBy: APPROVER, approvedAtUtc: '2026-09-17T10:00:00Z' })) }
      return url.includes('/invoice-line-cogs/') ? json(historyFixture()) : undefined
    } })
    renderAt(invoiceLineCogsPath(invoice, line, 'approve'))
    fireEvent.click(await screen.findByRole('button', { name: 'Satır düzeltmesini onayla' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı komutu yeniden gönder' }))
    await waitFor(() => expect(requestsFor(requests, '/approval', 'POST')).toHaveLength(2))
    const attempts = requestsFor(requests, '/approval', 'POST')
    expect(attempts[0].headers['Idempotency-Key']).toBe(attempts[1].headers['Idempotency-Key'])
    expect(attempts[0].body).toEqual(attempts[1].body)
  })
  it('does not treat a malformed successful reply as a completed correction', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ status: 'Approved' })))
    expect(await sendCogsCorrection(COMPANY_A, { invoiceId: invoice, lineId: line, proposalId: proposal, operation: 'approve',
      key: 'SENTETIK-key', request: { expectedVersion: 1 } })).toEqual({ type: 'unknown' })
  })
})
