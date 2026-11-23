import { describe, expect, it } from 'vitest'
import { decodeBalanceTransferPreview, decodeBalanceTransferResult, type BalanceTransferRecord } from './PartyBalanceTransferContract'
import { balanceTransferSlot, recallBalanceTransfer, recoverBalanceTransfer, reserveBalanceTransfer,
  retryBalanceTransfer, settleBalanceTransfer } from './PartyBalanceTransferAttempt'

const company = '11111111-1111-4111-8111-111111111111'
const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const selection = { predecessorPartyId: id(1), predecessorAccountId: id(2), successorPartyId: id(3), successorAccountId: id(4) }
const proof = { ...selection, companyId: company, predecessorPartyVersion: '2', predecessorAccountVersion: '2', predecessorCode: 'ESKI',
  predecessorName: 'Sentetik eski cari', successorPartyVersion: '1', successorAccountVersion: '1', successorCode: 'YENI',
  successorName: 'Sentetik yeni cari', linkKind: 'Succession', effectiveOn: '2026-11-17', legalBasis: 'Sentetik dayanak', linkRecordedAtUtc: '2026-11-17T08:00:00Z' }
const currencies = [{ currencyId: id(5), side: 'Debit', amount: '12345678901234567890.0001', openReceivable: '12345678901234567890.0001', openPayable: '0.0000' }]
const preview = () => ({ outcome: 'Found', preview: { companyId: company, ownerProof: proof, snapshotVersion: 'a'.repeat(64), currencies,
  openItemCount: 1, receiptCount: 0, paymentCount: 0, canRecord: true }, transfer: null })
const request = (): BalanceTransferRecord => ({ transferId: id(6), selection: { ...selection }, expectedSnapshotVersion: 'a'.repeat(64), occurredOn: '2026-11-17',
  reason: 'Sentetik devir', evidence: { documentId: id(7), versionNo: 1, sha256: 'b'.repeat(64) } })
const success = () => ({ outcome: 'Succeeded', resource: { id: id(6), companyId: company, version: '1', ownerProof: proof, currencies,
  occurredOn: '2026-11-17', reason: 'Sentetik devir', recordedBy: 'SENTETIK', recordedAtUtc: '2026-11-17T08:00:00Z', evidence: request().evidence }, code: null, replayed: false })

describe('Party balance transfer pinned source contract', () => {
  it('preserves exact currency text and pins the expected owner pair and company', () => {
    const result = decodeBalanceTransferPreview(preview(), company, selection)
    expect(result.ok && result.value.currencies[0].amount).toBe('12345678901234567890.0001')
    expect(decodeBalanceTransferPreview(preview(), id(8), selection).ok).toBe(false)
    expect(decodeBalanceTransferPreview(preview(), company, { ...selection, successorAccountId: id(8) }).ok).toBe(false)
  })
  it('rejects duplicate currencies, hidden extra fields, numeric money and incomplete owner versions', () => {
    for (const change of [
      { currencies: [...currencies, ...currencies] }, { currencies: [{ ...currencies[0], amount: 1 }] },
      { ownerProof: { ...proof, successorAccountVersion: '0' } }, { currencies: [{ ...currencies[0], side: 'Zero' }] },
      { ownerProof: { ...proof, extra: true } },
    ]) expect(decodeBalanceTransferPreview({ ...preview(), preview: { ...preview().preview, ...change } }, company, selection).ok).toBe(false)
  })
  it('accepts only a matching immutable voucher and pinned document proof as success', () => {
    expect(decodeBalanceTransferResult(success(), company, request()).ok).toBe(true)
    for (const patch of [{ id: id(9) }, { occurredOn: '2026-11-18' }, { reason: 'Other' },
      { evidence: { ...request().evidence, versionNo: 2 } }, { evidence: { ...request().evidence, sha256: 'c'.repeat(64) } },
      { ownerProof: { ...proof, successorAccountId: id(9) } }]) {
      expect(decodeBalanceTransferResult({ ...success(), resource: { ...success().resource, ...patch } }, company, request()).ok).toBe(false)
    }
  })
  it('retains an unresolved write across remount using the same key and frozen body, scoped to actor and company', () => {
    const binding = { sessionEpoch: 384, actorId: 'sentetik-actor', companyId: company }
    const slot = balanceTransferSlot(binding)
    const body = request(); const first = reserveBalanceTransfer(slot, body)!
    expect(first).not.toBeNull()
    Object.assign(body, { selection: { ...selection, successorAccountId: id(9) } })
    expect(first.request.selection.successorAccountId).toBe(id(4))
    expect(reserveBalanceTransfer(slot, request())).toBeNull()
    expect(recoverBalanceTransfer(slot)?.phase).toBe('unknown')
    const retry = retryBalanceTransfer(slot)!
    expect(retry.key).toBe(first.key); expect(retry.request).toBe(first.request)
    expect(recallBalanceTransfer(balanceTransferSlot({ ...binding, companyId: id(9) }))).toBeNull()
    settleBalanceTransfer(slot, 'different-key'); expect(recallBalanceTransfer(slot)).not.toBeNull()
    settleBalanceTransfer(slot, first.key); expect(recallBalanceTransfer(slot)).toBeNull()
  })
})
