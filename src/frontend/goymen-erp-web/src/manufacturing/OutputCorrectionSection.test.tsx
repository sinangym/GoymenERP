/**
 * F-009 D6 (UI-14, API-25) output correction and order cancel — component/DOM tests through the real shell, gate and
 * fence, over synthetic fixtures. NOT real-API, real-SQL or browser acceptance.
 *
 * The correction consumes the URT-S2 contract (`POST …/orders/{orderId}/facts/{outputId}/correction`,
 * `CorrectOutputRequestV1`): one frozen snapshot per attempt, the label version from Inventory's detail read, the
 * order re-read after an accepted answer, the correction's own 409 reasons told in Turkish, and an UNKNOWN outcome
 * re-sent unchanged. Inventory's label detail transport is replaced by a stub here; its own decoder has its own tests.
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { INVENTORY_STOCK_POSITIONS_CAPABILITY } from '../inventory/capabilities'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { OUTPUT_1, OUTPUT_LABEL_1, orderLineage } from './lineageFixture'
import {
  APPROVER,
  COMPANY_A,
  ORDER,
  READER_CAPABILITIES,
  installFetch,
  json,
  massBalance,
  order,
  problem,
  renderAt,
  requestsFor,
  type RecordedRequest,
} from './testHarness'

const labelDetailCalls = vi.hoisted(() => [] as string[])

vi.mock('../inventory/labelDetailApi', () => ({
  fetchInventoryLabelDetail: async (_companyId: string, labelId: string) => {
    labelDetailCalls.push(labelId)
    return { type: 'ok-single', data: { labelVersion: '7' } }
  },
}))

const PATH = `/uretim/emir/${ORDER}`
const HEADING = 'Çıktı düzeltme'
const CORRECTION_URL = `/manufacturing/orders/${ORDER}/facts/${OUTPUT_1}/correction`
const CORRECTOR_CAPABILITIES = [
  MANUFACTURING_CAPABILITY.orderDetail,
  MANUFACTURING_CAPABILITY.orderOutputCorrection,
  INVENTORY_STOCK_POSITIONS_CAPABILITY,
]

function stub(
  options: {
    readonly capabilities?: readonly string[]
    readonly reads?: Array<() => Response>
    readonly commands?: Array<() => Response>
  } = {},
): RecordedRequest[] {
  const reads = options.reads ?? [() => json(order())]
  const commands = options.commands ?? [() => json(order({ version: 2 }))]
  let read = 0
  let command = 0
  return installFetch({
    capabilities: options.capabilities ?? CORRECTOR_CAPABILITIES,
    respond: (url, init) => {
      if (!url.includes(`/manufacturing/orders/${ORDER}`)) return undefined
      if (url.endsWith('/mass-balance')) return json(massBalance())
      if (url.endsWith('/lineage')) return json(orderLineage())
      if ((init?.method ?? 'GET') === 'POST') {
        const respond = commands[Math.min(command, commands.length - 1)]
        command += 1
        return respond()
      }
      const respond = reads[Math.min(read, reads.length - 1)]
      read += 1
      return respond()
    },
  })
}

const posts = (recorded: readonly RecordedRequest[]) => requestsFor(recorded, CORRECTION_URL, 'POST')
const orderReads = (recorded: readonly RecordedRequest[]) =>
  requestsFor(recorded, `/manufacturing/orders/${ORDER}`, 'GET').filter((request) => request.url.endsWith(ORDER))

async function fillCorrection(user: ReturnType<typeof userEvent.setup>, kg = '12.5') {
  const section = await screen.findByRole('region', { name: HEADING })
  const select = await within(section).findByLabelText('Düzeltilecek çıktı')
  await user.selectOptions(select, OUTPUT_1)
  await within(section).findByTestId('output-correction-label-version')
  await user.type(within(section).getByLabelText('Geri alınacak kg'), kg)
  await user.type(within(section).getByLabelText('Düzeltme gerekçesi'), '  Yanlış tartım  ')
  await user.type(within(section).getByLabelText('Düzeltme zamanı'), '2026-11-16T10:30')
  return section
}

async function submitAndConfirm(user: ReturnType<typeof userEvent.setup>, section: HTMLElement) {
  await user.click(within(section).getByRole('button', { name: 'Düzeltmeyi gönder' }))
  await user.click(await within(section).findByRole('button', { name: 'Evet, düzeltmeyi kaydet' }))
}

beforeEach(() => {
  labelDetailCalls.length = 0
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('output correction (UI-14)', () => {
  it('draws no correction form without its own control', async () => {
    const recorded = stub({ capabilities: READER_CAPABILITIES })
    renderAt(PATH)
    await screen.findByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })
    expect(screen.queryByRole('region', { name: HEADING })).toBeNull()
    expect(screen.queryByTestId('order-cancel')).toBeNull()
    expect(posts(recorded)).toHaveLength(0)
  })

  it('sends one frozen correction under the URT-S2 contract and re-reads the order', async () => {
    const user = userEvent.setup()
    const recorded = stub({ reads: [() => json(order()), () => json(order({ version: 2 }))] })
    renderAt(PATH)

    const section = await fillCorrection(user)
    expect(labelDetailCalls).toContain(OUTPUT_LABEL_1)
    await submitAndConfirm(user, section)

    await within(section).findByText('Düzeltme kaydedildi')
    const [sent] = posts(recorded)
    expect(posts(recorded)).toHaveLength(1)
    expect(sent.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(sent.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    const body = sent.body as Record<string, unknown>
    expect(Object.keys(body).sort()).toEqual(
      ['expectedLabelVersion', 'expectedVersion', 'kg', 'labelId', 'movementId', 'occurredAtUtc', 'reason', 'reversalId'],
    )
    expect(body.expectedVersion).toBe(1)
    expect(body.labelId).toBe(OUTPUT_LABEL_1)
    expect(body.expectedLabelVersion).toBe('7')
    expect(body.kg).toBe('12.5')
    expect(body.reason).toBe('Yanlış tartım')
    expect(body.occurredAtUtc).toMatch(/Z$/)
    expect(body.reversalId).not.toBe(body.movementId)
    expect(body.reversalId).not.toBe(OUTPUT_1)
    expect(body.movementId).not.toBe(OUTPUT_1)
    await waitFor(() => expect(orderReads(recorded).length).toBeGreaterThanOrEqual(2))
  })

  it('tells a correction refusal in Turkish and sends nothing else', async () => {
    const user = userEvent.setup()
    const recorded = stub({
      commands: [() => problem(409, { code: 'manufacturing_output_correction_shipped' })],
    })
    renderAt(PATH)

    const section = await fillCorrection(user)
    await submitAndConfirm(user, section)

    const refusal = await within(section).findByTestId('output-correction-refusal')
    expect(refusal.textContent).toContain('sevkiyat yapılmış')
    expect(refusal.textContent).not.toContain('manufacturing_output_correction_shipped')
    expect(posts(recorded)).toHaveLength(1)
  })

  it('re-sends an unknown outcome unchanged, with the same key and ids', async () => {
    const user = userEvent.setup()
    const recorded = stub({
      commands: [() => problem(503, { code: 'manufacturing_unavailable' }), () => json(order({ version: 2 }))],
    })
    renderAt(PATH)

    const section = await fillCorrection(user)
    await submitAndConfirm(user, section)

    await within(section).findByText('Düzeltmenin sonucu bilinmiyor')
    expect((within(section).getByLabelText('Geri alınacak kg') as HTMLInputElement).disabled).toBe(true)
    await user.click(within(section).getByRole('button', { name: 'Aynı işlem anahtarıyla yeniden gönder' }))
    await within(section).findByText('Düzeltme kaydedildi')

    const [first, second] = posts(recorded)
    expect(posts(recorded)).toHaveLength(2)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('keeps an unknown correction across leaving and reopening the order: frozen, re-sent with the same key and ids only', async () => {
    const user = userEvent.setup()
    const recorded = stub({
      commands: [() => problem(503, { code: 'manufacturing_unavailable' }), () => json(order({ version: 2 }))],
    })
    const { router } = renderAt(PATH)
    const section = await fillCorrection(user)
    await submitAndConfirm(user, section)
    await within(section).findByText('Düzeltmenin sonucu bilinmiyor')

    await router.navigate('/')
    await router.navigate(PATH)
    const reopened = await screen.findByRole('region', { name: HEADING })
    await within(reopened).findByText('Düzeltmenin sonucu bilinmiyor')
    expect(within(reopened).queryByRole('button', { name: 'Düzeltmeyi gönder' })).toBeNull()
    await user.click(within(reopened).getByRole('button', { name: 'Aynı işlem anahtarıyla yeniden gönder' }))
    await within(reopened).findByText('Düzeltme kaydedildi')

    const [first, second] = posts(recorded)
    expect(posts(recorded)).toHaveLength(2)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('refuses to send without a reason, a valid kg or a time', async () => {
    const user = userEvent.setup()
    const recorded = stub()
    renderAt(PATH)

    const section = await screen.findByRole('region', { name: HEADING })
    await user.selectOptions(await within(section).findByLabelText('Düzeltilecek çıktı'), OUTPUT_1)
    await within(section).findByTestId('output-correction-label-version')
    await user.type(within(section).getByLabelText('Geri alınacak kg'), '-1')
    await user.click(within(section).getByRole('button', { name: 'Düzeltmeyi gönder' }))

    const errors = await within(section).findByTestId('output-correction-errors')
    expect(errors.textContent).toContain('Kilogram biçimi geçersiz')
    expect(errors.textContent).toContain('Düzeltme gerekçesi zorunludur.')
    expect(errors.textContent).toContain('Olgunun zamanını girin.')
    expect(within(section).queryByRole('button', { name: 'Evet, düzeltmeyi kaydet' })).toBeNull()
    expect(posts(recorded)).toHaveLength(0)
  })

  it('draws no form on an order that is not open and reads no outputs for it', async () => {
    const recorded = stub({
      reads: [() => json(order({ status: 'Closed', closedByUserId: APPROVER, closedAtUtc: '2026-11-16T08:00:00.000Z' }))],
    })
    renderAt(PATH)

    const section = await screen.findByRole('region', { name: HEADING })
    expect(within(section).getByTestId('output-correction-not-open')).toBeTruthy()
    expect(within(section).queryByLabelText('Düzeltilecek çıktı')).toBeNull()
    expect(requestsFor(recorded, `/manufacturing/orders/${ORDER}/lineage`, 'GET')).toHaveLength(0)
  })
})

describe('order cancel (URT-S4 draft)', () => {
  const CANCEL_URL = `/manufacturing/orders/${ORDER}/cancellation`
  const CANCELLER_CAPABILITIES = [MANUFACTURING_CAPABILITY.orderDetail, MANUFACTURING_CAPABILITY.orderCancel]

  it('requires a reason and sends the version the screen read', async () => {
    const user = userEvent.setup()
    const recorded = stub({ capabilities: CANCELLER_CAPABILITIES })
    renderAt(PATH)

    const section = await screen.findByTestId('order-cancel')
    await user.click(within(section).getByRole('button', { name: 'Emri iptal et' }))
    await user.click(within(section).getByRole('button', { name: 'Evet, emri iptal et' }))
    expect(within(section).getByText('İptal gerekçesi zorunludur.')).toBeTruthy()
    expect(requestsFor(recorded, CANCEL_URL, 'POST')).toHaveLength(0)

    await user.type(within(section).getByLabelText('İptal gerekçesi'), 'Plan değişti')
    await user.click(within(section).getByRole('button', { name: 'Evet, emri iptal et' }))
    await within(section).findByText('Emir iptal edildi')

    const [sent] = requestsFor(recorded, CANCEL_URL, 'POST')
    expect(sent.body).toEqual({ expectedVersion: 1, reason: 'Plan değişti' })
    expect(sent.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
  })
})

// AK-7 (URT-D112): a counted output's correction names the pieces it takes back; an uncounted one never sends pieces.
describe('output correction pieces (AK-7)', () => {
  function countedOrder(overrides: Record<string, unknown> = {}) {
    return order({
      outputPieces: [{
        outputId: OUTPUT_1, parentRollId: 'a0000000-0000-4000-8000-000000000001', outputClass: 'GoodProduct',
        declaredPieceCount: 48, netPieceCount: 48, netKg: '20', theoreticalKgPerPiece: '0.4', realKgPerPiece: '0.417',
        deviationKgPerPiece: '0.017', deviationPercent: '4.17', formulaVersion: 'manufacturing.output-piece-deviation.v1',
        deviationThresholdApplied: false,
      }],
      ...overrides,
    })
  }

  it('asks a counted output for the pieces taken back and sends them with the kg', async () => {
    const user = userEvent.setup()
    const recorded = stub({ reads: [() => json(countedOrder()), () => json(countedOrder({ version: 2 }))] })
    renderAt(PATH)
    const section = await fillCorrection(user)
    await user.type(within(section).getByLabelText('Geri alınan adet'), '0')
    await submitAndConfirm(user, section)

    await waitFor(() => expect(posts(recorded)).toHaveLength(1))
    expect(posts(recorded)[0].body).toMatchObject({ kg: '12.5', pieceCount: 0 })
  })

  it('refuses a counted output correction without pieces before sending anything', async () => {
    const user = userEvent.setup()
    const recorded = stub({ reads: [() => json(countedOrder())] })
    renderAt(PATH)
    const section = await fillCorrection(user)
    await user.click(within(section).getByRole('button', { name: 'Düzeltmeyi gönder' }))

    expect(await within(section).findByText(/geri alınan adedi girin/)).toBeTruthy()
    expect(posts(recorded)).toHaveLength(0)
  })

  it('marks every field the correction needs as required for assistive technology', async () => {
    const user = userEvent.setup()
    stub({ reads: [() => json(countedOrder())] })
    renderAt(PATH)
    const section = await fillCorrection(user)
    for (const name of ['Düzeltilecek çıktı', 'Geri alınacak kg', 'Geri alınan adet']) {
      expect(within(section).getByLabelText(name).getAttribute('aria-required')).toBe('true')
    }
    for (const id of ['mfg-correction-reason', 'mfg-correction-occurred-at']) {
      expect(section.querySelector(`#${id}`)?.getAttribute('aria-required')).toBe('true')
    }
  })

  it('draws no piece field for an uncounted output and sends no pieces', async () => {
    const user = userEvent.setup()
    const recorded = stub({ reads: [() => json(order()), () => json(order({ version: 2 }))] })
    renderAt(PATH)
    const section = await fillCorrection(user)
    expect(within(section).queryByLabelText('Geri alınan adet')).toBeNull()
    await submitAndConfirm(user, section)

    await waitFor(() => expect(posts(recorded)).toHaveLength(1))
    expect(posts(recorded)[0].body).not.toHaveProperty('pieceCount')
  })

  it('tells the piece reversal refusals in Turkish', async () => {
    const user = userEvent.setup()
    stub({
      reads: [() => json(countedOrder())],
      commands: [() => problem(422, { code: 'manufacturing_output_piece_reversal_inconsistent' })],
    })
    renderAt(PATH)
    const section = await fillCorrection(user)
    await user.type(within(section).getByLabelText('Geri alınan adet'), '48')
    await submitAndConfirm(user, section)

    expect(await within(section).findByText(/net kg ve net adet birlikte sıfıra inmelidir/)).toBeTruthy()
  })
})
