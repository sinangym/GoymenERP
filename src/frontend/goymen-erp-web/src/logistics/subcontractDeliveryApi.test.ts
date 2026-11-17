import { afterEach, describe, expect, it, vi } from 'vitest'
import { beginSubcontractAttempt, finishSubcontractAttempt, leaveSubcontractAttempt, resetSubcontractAttemptsForTests, retrySubcontractAttempt,
  sendSubcontractAttempt, subcontractAttempt, subcontractFrozen } from './subcontractDeliveryApi'
import { decodeSubcontractInstruction, decodeSubcontractList, decodeSubcontractSalesLines, subcontractKg, subcontractVersion, SUBCONTRACT_DELIVERY_PATH as PATH } from './subcontractDeliveryContract'

const id = (n: number) => `11111111-1111-4111-8111-${String(n).padStart(12, '0')}`
const company = id(1); const instruction = id(2)
const source = () => ({ companyId: company, customerPartyId: id(3), customerAccountId: id(4), salesOrderId: id(5), salesOrderLineId: id(6), salesOrderVersion: '4', salesLineRevision: 0,
  productId: id(7), productVersion: '2', salesServiceProductId: id(8), manufacturingOrderId: id(9), manufacturingOrderVersion: '7', manufacturingPlanId: id(10), manufacturingPlanVersion: '5',
  manufacturingSalesLinkRevision: '1', outputFactId: id(11), labelId: id(12), outputNetKg: '80', manufacturingSourceVersion: 'A'.repeat(64), labelCode: 'SYN-LABEL', salesDocumentNo: 'SYN-SALES', serviceCode: 'SYN-SERVICE', serviceName: 'SENTETIK hizmet' })
const view = () => ({ companyId: company, instructionId: instruction, version: '2', status: 'Draft', customerPartyId: id(3), customerAccountId: id(4),
  lines: [{ lineId: id(13), source: source(), allocationId: id(14), instructedKg: '80', loadedKg: '0', shippedKg: '0', deliveredKg: '0', allocationVersion: '1', labelVersion: '2' }], weighing: null, originalWeighing: null, recordedLoadId: null, corrections: [], isReplay: false, originalCorrelationId: 'SENTETIK-correlation' })
const create = () => ({ instructionId: instruction, effectiveOn: '2026-11-17', lines: [{ lineId: id(13), allocationId: id(14), salesOrderId: id(5), salesOrderLineId: id(6), quantityKg: '80', labelId: id(12), outputFactId: id(11) }] })
afterEach(() => { resetSubcontractAttemptsForTests(); vi.unstubAllGlobals() })

describe('subcontract delivery strict source frames', () => {
  it('retains different service and material product identities without recreating kilograms', () => {
    expect(decodeSubcontractInstruction(view(), company, instruction)).toEqual(view())
    expect(subcontractKg('0.001')).toBe(true); expect(subcontractKg('1.000')).toBe(false)
    expect(subcontractVersion('9223372036854775807')).toBe(true); expect(subcontractVersion('9223372036854775808')).toBe(false)
  })
  it.each([ { ...view(), companyId: id(20) }, { ...view(), lines: [{ ...view().lines[0], source: { ...source(), companyId: id(20) } }] },
    { ...view(), lines: [{ ...view().lines[0], source: { ...source(), customerPartyId: id(20) } }] },
    { ...view(), version: 2 }, { ...view(), version: '02' }, { ...view(), lines: [...view().lines, ...view().lines] },
    { ...view(), price: '100' }, { ...view(), lines: [{ ...view().lines[0], loadedKg: '-1' }] },
  ])('rejects foreign, duplicate, numeric-version or extended data %#', raw => expect(decodeSubcontractInstruction(raw, company, instruction)).toBeNull())
  it('distinguishes explicit empty pages from missing owner frames', () => {
    expect(decodeSubcontractList({ companyId: company, items: [], nextCursor: null }, company)?.items).toEqual([])
    expect(decodeSubcontractSalesLines({ companyId: company, items: [], nextCursor: null }, company)?.items).toEqual([])
    expect(decodeSubcontractList({}, company)).toBeNull(); expect(decodeSubcontractSalesLines({ companyId: id(20), items: [], nextCursor: null }, company)).toBeNull()
  })
})
describe('unknown subcontract writes preserve their exact key and body', () => {
  it('retains the frozen payload across unmount and a manual retry and isolates actor/company/session bindings', async () => {
    const fetch = vi.fn(async () => new Response('{}', { status: 503 })); vi.stubGlobal('fetch', fetch)
    const sent = beginSubcontractAttempt('SENTETIK-actor-company-epoch1', company, instruction, PATH, 'subcontract', create())!
    finishSubcontractAttempt(sent, await sendSubcontractAttempt(sent))
    expect(subcontractFrozen(subcontractAttempt(sent.binding))).toBe(true)
    expect(beginSubcontractAttempt(sent.binding, company, instruction, PATH, 'subcontract', create())).toBeNull()
    leaveSubcontractAttempt(sent.binding); const retried = retrySubcontractAttempt(sent.binding)!
    expect(retried).toBe(sent); await sendSubcontractAttempt(retried)
    expect(fetch.mock.calls[1]).toEqual(fetch.mock.calls[0])
    expect(subcontractAttempt('different-actor-company-epoch')).toEqual({ phase: 'idle' })
  })
  it('never upgrades a successful HTTP response about another source into a completed command', async () => {
    const sent = beginSubcontractAttempt('SENTETIK-malformed', company, instruction, PATH, 'subcontract', create())!
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ ...view(), originalCorrelationId: sent.correlation, lines: [{ ...view().lines[0], source: { ...source(), labelId: id(99) } }] }), { status: 201 })))
    expect(await sendSubcontractAttempt(sent)).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it('accepts a matching create and an exact original-correlation replay while 409 busy remains unresolved', async () => {
    const sent = beginSubcontractAttempt('SENTETIK-success', company, instruction, PATH, 'subcontract', create())!
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ ...view(), originalCorrelationId: sent.correlation }), { status: 201 })))
    expect((await sendSubcontractAttempt(sent)).type).toBe('applied')
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ ...view(), isReplay: true }), { status: 201, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-correlation' } })))
    expect(await sendSubcontractAttempt(sent)).toMatchObject({ type: 'applied', replayed: true })
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{"code":"sales_order_line_busy"}', { status: 409 })))
    expect(await sendSubcontractAttempt(sent)).toEqual({ type: 'unknown', reason: 'conflict' })
  })
})

describe('subcontract physical terminal details are authoritative bounded text', () => {
  const detail = 'Sistemde 20 kg var, 80 kg istendi.'
  const physical = 'inventory_physical_stock_insufficient'
  it.each([false, true])('retains an original physical refusal and its immutable replay=%s without recalculating kilograms', async replayed => {
    const sent = beginSubcontractAttempt('SENTETIK-physical', company, instruction, `${PATH}/${instruction}/ship`, 'subcontract', {})!
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ status: 409, code: physical, detail }), {
      status: 409, headers: replayed ? { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-original' } : {},
    })))
    const outcome = await sendSubcontractAttempt(sent)
    expect(outcome).toEqual({ type: 'refused', status: 409, code: physical, detail, replayed,
      originalCorrelationId: replayed ? 'SENTETIK-original' : null })
    finishSubcontractAttempt(sent, outcome)
    expect(subcontractAttempt('different-company-actor-session')).toEqual({ phase: 'idle' })
  })
  it('keeps legacy physical terminal JSON with no optional detail readable', async () => {
    const sent = beginSubcontractAttempt('SENTETIK-legacy', company, instruction, `${PATH}/${instruction}/ship`, 'subcontract', {})!
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ status: 409, code: physical }), { status: 409 })))
    expect(await sendSubcontractAttempt(sent)).toEqual({ type: 'refused', status: 409, code: physical,
      detail: null, replayed: false, originalCorrelationId: null })
  })
  it.each(['Sistemde -0 kg var, 80 kg istendi.', 'Sistemde 20.000 kg var, 80 kg istendi.',
    'Sistemde 20 kg var, 80 kg istendi.\n', 'SENTETIK arbitrary exception text', '<script>unsafe</script>', 20])(
    'does not admit malformed physical owner detail %s', async value => {
      const sent = beginSubcontractAttempt('SENTETIK-malformed-physical', company, instruction, `${PATH}/${instruction}/ship`, 'subcontract', {})!
      vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ status: 409, code: physical, detail: value }), { status: 409 })))
      expect(await sendSubcontractAttempt(sent)).toEqual({ type: 'unknown', reason: 'malformed-response' })
    })
  it.each([401, 403, 404, 503])('never discloses kilograms or replay provenance on status %s', async status => {
    const sent = beginSubcontractAttempt('SENTETIK-nondisclosable', company, instruction, `${PATH}/${instruction}/ship`, 'subcontract', {})!
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ status, code: physical, detail }), {
      status, headers: { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'SENTETIK-private' },
    })))
    const result = await sendSubcontractAttempt(sent)
    if (status === 503) expect(result).toEqual({ type: 'unknown', reason: 'service-unavailable' })
    else expect(result).toEqual({ type: 'refused', status, code: null, detail: null, replayed: false, originalCorrelationId: null })
  })
  it('never treats a different business refusal message as physical-stock proof', async () => {
    const sent = beginSubcontractAttempt('SENTETIK-not-physical', company, instruction, `${PATH}/${instruction}/ship`, 'subcontract', {})!
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ status: 409, code: 'logistics_subcontract_delivery_active_invoice', detail }), { status: 409 })))
    expect(await sendSubcontractAttempt(sent)).toMatchObject({ type: 'refused', detail: null })
  })
  const replayFaults: readonly Record<string, string>[] = [
    { 'Idempotency-Replayed': 'true' },
    { 'Idempotency-Replayed': 'invalid' },
    { 'Idempotency-Replayed': 'false', 'Idempotency-Original-Correlation-Id': 'SENTETIK-wrong' },
  ]
  it.each(replayFaults)('keeps malformed physical terminal replay metadata unresolved', async headers => {
    const sent = beginSubcontractAttempt('SENTETIK-replay-fault', company, instruction, `${PATH}/${instruction}/ship`, 'subcontract', {})!
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ status: 409, code: physical, detail }), { status: 409, headers })))
    expect(await sendSubcontractAttempt(sent)).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
  it('does not admit a physical terminal whose declared status differs from the HTTP response', async () => {
    const sent = beginSubcontractAttempt('SENTETIK-status-fault', company, instruction, `${PATH}/${instruction}/ship`, 'subcontract', {})!
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ status: 503, code: physical, detail }), { status: 409 })))
    expect(await sendSubcontractAttempt(sent)).toEqual({ type: 'unknown', reason: 'malformed-response' })
  })
})
