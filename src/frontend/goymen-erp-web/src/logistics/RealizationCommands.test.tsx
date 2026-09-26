import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { decodeInstructionDetail, type LoadingShare } from './contract'
import { decodeShipmentAllocations, REALIZATION_CAPABILITY as C, shipmentRemainder } from './realizationContract'
import { INSTRUCTION_DETAIL_TEXT as D, REALIZATION_TEXT as T } from './logisticsText'
import { resetPendingInstructionAttempts } from './pendingInstructionAttempts'
import { ALL_LOGISTICS, COMPANY_A, COMPANY_B, DRAFT, DRAFT_LINE, INSTRUCTION, LINE, PRODUCT,
  installFetch, instructionDetail, instructionLine, json, mutation, renderLogisticsApp, type RecordedCall } from './testHarness'

const ALLOCATION = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee'
const LABEL = 'ffffffff-ffff-ffff-ffff-ffffffffffff'
const SHIPMENT = '12345678-1234-1234-1234-123456789abc'
const path = `/lojistik/yukleme-talimatlari/${INSTRUCTION}`
const loadedShare = { shareId: '12345678-1234-1234-1234-123456789abd', lineId: LINE, kind: 'Loaded', quantity: '4',
  measurement: 'Measured', sourceShipmentId: null, recordedAtUtc: '2026-09-25T10:00:00Z' }
const shippedShare = { shareId: SHIPMENT, lineId: LINE, kind: 'Shipped', quantity: '2',
  measurement: null, sourceShipmentId: null, recordedAtUtc: '2026-09-25T11:00:00Z' }
const loaded = () => instructionDetail({ status: 'Issued', version: '4' },
  [instructionLine({ loaded: '4', remainingToLoad: '0.5', loadedNotShipped: '4' })], [loadedShare])
const shipped = (share = shippedShare) => instructionDetail({ status: 'Issued', version: '5' },
  [instructionLine({ loaded: '4', remainingToLoad: '0.5', shipped: '2', loadedNotShipped: '2', shippedNotDelivered: '2' })], [loadedShare, share])
const allocations = (overrides: Record<string, unknown> = {}) => ({ items: [{ allocation: {
  allocationId: ALLOCATION, companyId: COMPANY_A, version: '7', status: 'Active', labelId: LABEL, productId: PRODUCT,
  demandKind: 'sales.order-line', demandId: DRAFT, demandLineId: DRAFT_LINE, demandProductVersion: '9', allocatedKg: '4', remainingKg: '4', ...overrides,
}, labelVersion: '11' }], searchWindowFull: false })

afterEach(() => { cleanup(); resetPendingInstructionAttempts(); vi.unstubAllGlobals(); vi.restoreAllMocks() })

describe('LOG5 owner UI, synthetic NOT_ENV', () => {
  it('does not offer shipment/delivery or read allocations without their independently published controls', async () => {
    const harness = installFetch({ onDetail: () => json(loaded()) })
    renderLogisticsApp(path)
    await screen.findByTestId('logistics-instruction-id')
    expect(screen.queryByTestId('shipment-1')).toBeNull()
    expect(screen.queryByTestId('delivery-1')).toBeNull()
    expect(harness.calls.some((call) => call.url.includes('/inventory/'))).toBe(false)
  })

  it('selects real-source versions, records shipment, re-reads and preserves the record on close/reopen', async () => {
    let current = loaded()
    const harness = installFetch({ capabilities: [...ALL_LOGISTICS, C.shipment, C.allocation], onDetail: () => json(current),
      onList: () => json({ items: [], nextCursor: null }), onAllocations: () => json(allocations()),
      onShipment: (_, call) => {
        const body = JSON.parse(call.body!)
        current = shipped({ ...shippedShare, shareId: body.shipmentId, recordedAtUtc: body.occurredAtUtc })
        return json(mutation(current))
      } })
    const app = renderLogisticsApp(path)
    const form = await screen.findByTestId('shipment-1')
    await screen.findByRole('option', { name: /Etiket ref/ })
    await userEvent.selectOptions(within(form).getByLabelText(T.allocation), ALLOCATION)
    await userEvent.type(within(form).getByLabelText(T.quantity), '2')
    await userEvent.click(within(form).getByTestId('shipment-1-submit'))
    await waitFor(() => expect(screen.getByTestId('shipment-1-notice').textContent).toBe(D.command.completed))
    const post = harness.calls.find((call) => call.method === 'POST')!
    expect(JSON.parse(post.body!)).toMatchObject({ expectedVersion: '4', allocationId: ALLOCATION,
      expectedAllocationVersion: '7', expectedLabelVersion: '11', quantity: '2' })
    expect(post.body).not.toMatch(/companyId|labelId|productId|positionId/)
    await app.router.navigate('/lojistik/yukleme-talimatlari')
    await screen.findByRole('heading', { name: 'Yükleme talimatları' })
    await app.router.navigate(path)
    await waitFor(() => expect(screen.getByTestId('logistics-instruction-version').textContent).toBe('5'))
    expect(screen.getByTestId('logistics-line-1-loaded-not-shipped').textContent).toBe('2')
  })

  it('unknown shipment retry freezes every identifier/version/event time and retains the same key', async () => {
    let attempts = 0
    const harness = installFetch({ capabilities: [...ALL_LOGISTICS, C.shipment, C.allocation], onDetail: () => json(loaded()),
      onAllocations: () => json(allocations()), onShipment: (_, call) => {
        if (++attempts === 1) throw new TypeError('reply lost')
        const body = JSON.parse(call.body!)
        return json(mutation(shipped({ ...shippedShare, shareId: body.shipmentId, recordedAtUtc: body.occurredAtUtc }), true),
          200, { 'Idempotency-Replayed': 'true' })
      } })
    renderLogisticsApp(path)
    const form = await screen.findByTestId('shipment-1')
    await screen.findByRole('option', { name: /Etiket ref/ })
    await userEvent.selectOptions(within(form).getByLabelText(T.allocation), ALLOCATION)
    await userEvent.type(within(form).getByLabelText(T.quantity), '2')
    await userEvent.click(within(form).getByTestId('shipment-1-submit'))
    await within(form).findByText(D.command.unknown)
    await userEvent.click(within(form).getByRole('button', { name: D.command.retry }))
    await within(form).findByText(D.command.replayed)
    const posts = harness.calls.filter((call) => call.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(posts[1].body).toBe(posts[0].body)
    expect(posts[1].idempotencyKey).toBe(posts[0].idempotencyKey)
  })

  it('delivery selects the exact source shipment without any Inventory capability/read/write', async () => {
    let current = shipped()
    const harness = installFetch({ capabilities: [...ALL_LOGISTICS, C.delivery], onDetail: () => json(current), onDelivery: (_, call: RecordedCall) => {
      const body = JSON.parse(call.body!)
      current = instructionDetail({ status: 'Issued', version: '6' },
        [instructionLine({ loaded: '4', shipped: '2', delivered: '1', remainingToLoad: '0.5', loadedNotShipped: '2', shippedNotDelivered: '1' })],
        [loadedShare, shippedShare, { shareId: body.deliveryId, lineId: LINE, kind: 'Delivered', quantity: '1', measurement: null,
          sourceShipmentId: SHIPMENT, recordedAtUtc: body.occurredAtUtc }])
      return json(mutation(current))
    } })
    renderLogisticsApp(path)
    const form = await screen.findByTestId('delivery-1')
    await userEvent.selectOptions(within(form).getByLabelText(T.sourceShipment), SHIPMENT)
    await userEvent.type(within(form).getByLabelText(T.quantity), '1')
    await userEvent.click(within(form).getByTestId('delivery-1-submit'))
    await within(form).findByText(D.command.completed)
    expect(JSON.parse(harness.calls.find((call) => call.method === 'POST')!.body!)).toMatchObject({ expectedVersion: '5', sourceShipmentId: SHIPMENT, quantity: '1' })
    expect(harness.calls.some((call) => call.url.includes('/inventory/'))).toBe(false)
    await waitFor(() => expect(screen.getByTestId('logistics-line-1-shipped-not-delivered').textContent).toBe('1'))
  })

  it.each([{ companyId: COMPANY_B }, { demandLineId: LINE }, { productId: LABEL }, { demandKind: 'invented' }])(
    'rejects allocation source drift %j instead of displaying a cross-context choice', (fault) => {
      const detail = decodeInstructionDetail(loaded(), INSTRUCTION)!
      expect(decodeShipmentAllocations(allocations(fault), COMPANY_A, detail.lines[0])).toBeNull()
    })

  it('computes a display remainder exactly and only from deliveries naming this shipment', () => {
    const source = shippedShare as LoadingShare
    const delivery = { ...source, shareId: LINE, kind: 'Delivered' as const, sourceShipmentId: SHIPMENT, quantity: '0.9' }
    expect(shipmentRemainder(source, [source, delivery, { ...delivery, sourceShipmentId: ALLOCATION }])).toBe('1.1')
  })
})
