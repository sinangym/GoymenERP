/**
 * The Manufacturing order screen — component/DOM tests through the real shell, gate and
 * fence, over synthetic fixtures. NOT real-API, real-SQL or browser acceptance, and
 * never reported as such (the `mfg` browser suite is a later slice).
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { INVENTORY_STOCK_POSITIONS_CAPABILITY } from '../inventory/capabilities'
import type { InventoryLabelIdentity } from '../inventory/labelDetailContract'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import { OUTPUT_PIECE_TEXT } from './outputPieceText'
import {
  CLOSER_CAPABILITIES,
  COMPANY_A,
  COMPANY_B,
  ORDER,
  READER_CAPABILITIES,
  RECORDER_CAPABILITIES,
  ROLL_1,
  SHORTFALL_LOCATION,
  installFetch,
  json,
  massBalance,
  order,
  problem,
  renderAt,
  requestsFor,
  roll,
  rollBalance,
  stockShortfall,
  unavailable,
  type RecordedRequest,
} from './testHarness'

const PATH = `/uretim/emir/${ORDER}`
const PRODUCT = 'e0000000-0000-4000-8000-000000000010'
const NEW_PRODUCT = 'e0000000-0000-4000-8000-000000000014'
const LOT = 'e0000000-0000-4000-8000-000000000011'
const LOCATION = 'e0000000-0000-4000-8000-000000000012'
const ISSUE_LINE = 'e0000000-0000-4000-8000-000000000015'
const ISSUE_MOVEMENT = 'e0000000-0000-4000-8000-000000000016'
const INVENTORY_RECORDER_CAPABILITIES = [...RECORDER_CAPABILITIES, INVENTORY_STOCK_POSITIONS_CAPABILITY]
const EXCEPTION_APPROVER_CAPABILITIES = [
  ...CLOSER_CAPABILITIES,
  MANUFACTURING_CAPABILITY.orderCloseExceptionApprove,
]

type PartyKind = 'Company' | 'Counterparty'

function issueMovement(
  ownerKind: PartyKind = 'Company',
  ownerId = COMPANY_A,
  custodianKind: PartyKind = 'Company',
  custodianId = COMPANY_A,
  overrides: { movementId?: string; sourceId?: string; locationId?: string } = {},
) {
  return {
    movementId: overrides.movementId ?? ISSUE_MOVEMENT,
    kind: 'Issue',
    occurredAtUtc: '2026-09-25T05:00:00Z',
    sourceKind: 'manufacturing.input-issue',
    sourceId: overrides.sourceId ?? ORDER,
    sourceLineId: ISSUE_LINE,
    sourceGrain: 'Line',
    lines: [{
      sequence: 1,
      direction: 'Out',
      ownerKind,
      ownerId,
      custodianKind,
      custodianId,
      locationId: overrides.locationId ?? LOCATION,
      productId: PRODUCT,
      block: 'Free',
      quantityKg: '100',
    }],
  }
}

function labelDetail(
  labelVersion = '7',
  physicalKg = '1000',
  ownerId = COMPANY_A,
  custodianId = COMPANY_A,
  ownerKind: PartyKind = 'Company',
  custodianKind: PartyKind = 'Company',
  movementEntries: readonly ReturnType<typeof issueMovement>[] = [
    issueMovement(ownerKind, ownerId, custodianKind, custodianId),
  ],
  movementWindowFull = false,
) {
  return {
    labelVersion,
    identity: {
      labelId: ROLL_1,
      companyId: COMPANY_A,
      code: 'SYN-MFG-ROLL-1',
      productId: PRODUCT,
      lotId: LOT,
      heatNumber: null,
      coilNumber: null,
      classification: null,
      documentStatus: 'Complete',
      requiredDocuments: [],
      documents: [],
      prints: [],
      lot: { id: LOT, heatNumber: null, coilNumber: null },
    } satisfies InventoryLabelIdentity,
    balances: {
      labelId: ROLL_1,
      physicalKg,
      freeKg: physicalKg,
      blockedKg: '0',
      positions: [{
        ownerKind,
        ownerId,
        custodianKind,
        custodianId,
        locationId: LOCATION,
        productId: PRODUCT,
        block: 'Free',
        quantityKg: physicalKg,
      }],
    },
    genealogy: { labelId: ROLL_1, labelCode: 'SYN-MFG-ROLL-1', edges: [], windowFull: false },
    movementTimeline: { labelId: ROLL_1, entries: movementEntries, windowFull: movementWindowFull },
  }
}

interface Scenario {
  readonly recorded: RecordedRequest[]
}

function stub(
  options: {
    reads?: Array<() => Response>
    /** A command responder may read the request it answers (e.g. to echo the fact id it carried). */
    commands?: Array<(init?: RequestInit) => Response>
    capabilities?: readonly string[] | (() => readonly string[])
    /** GDC-BAL/1 reads (BAL slice 2), answered on their own so they never consume an order read. */
    balances?: Array<() => Response>
    labels?: Array<() => Response>
    products?: Array<() => Response | Promise<Response>>
    locations?: Array<() => Response | Promise<Response>>
  } = {},
): Scenario {
  const reads = options.reads ?? [() => json(order())]
  const commands = options.commands ?? [() => json(order({ version: 2 }))]
  const balances = options.balances ?? [() => json(massBalance())]
  const labels = options.labels ?? [() => json(labelDetail())]
  const products = options.products ?? [() => json({
    items: [{ productId: PRODUCT, productVersion: '12', code: 'PRD-12', name: 'Galvaniz bant', stockUnitCode: 'kg',
      pieceCounting: 'NotCounted' }],
    searchWindowFull: false,
  })]
  const locations = options.locations ?? [() => json({
    items: [{
      locationId: LOCATION,
      locationCode: 'HOL-1',
      locationName: 'Üretim holü',
      siteId: 'e0000000-0000-4000-8000-000000000013',
      siteCode: 'OSM',
      siteName: 'Osmangazi',
    }],
    searchWindowFull: false,
  })]
  let readIndex = 0
  let commandIndex = 0
  let balanceIndex = 0
  let labelIndex = 0
  let productIndex = 0
  let locationIndex = 0
  const recorded = installFetch({
    capabilities: options.capabilities,
    respond: (url, init) => {
      if (url.includes('/manufacturing/references/output-products')) {
        const respond = products[Math.min(productIndex, products.length - 1)]
        productIndex += 1
        return respond()
      }
      if (url.includes('/manufacturing/references/output-locations')) {
        const respond = locations[Math.min(locationIndex, locations.length - 1)]
        locationIndex += 1
        return respond()
      }
      if (url.includes(`/inventory/labels/${ROLL_1}/detail`)) {
        const respond = labels[Math.min(labelIndex, labels.length - 1)]
        labelIndex += 1
        return respond()
      }
      if (!url.includes(`/manufacturing/orders/${ORDER}`)) return undefined
      if (url.endsWith('/mass-balance')) {
        const respond = balances[Math.min(balanceIndex, balances.length - 1)]
        balanceIndex += 1
        return respond()
      }
      if ((init?.method ?? 'GET') === 'POST') {
        const respond = commands[Math.min(commandIndex, commands.length - 1)]
        commandIndex += 1
        return respond(init)
      }
      if (new URL(url, 'http://localhost').pathname !== `/api/v1/manufacturing/orders/${ORDER}`) return undefined
      const respond = reads[Math.min(readIndex, reads.length - 1)]
      readIndex += 1
      return respond()
    },
  })
  return { recorded }
}

async function fillFact(
  user: ReturnType<typeof userEvent.setup>,
  values: { kind?: string; kg?: string } = {},
) {
  if (values.kind !== undefined) await user.click(screen.getByLabelText(values.kind))
  await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
  if (values.kind === 'Kullanılmayan girdi iadesi') await screen.findByTestId('parent-label-state')
  const kg = screen.getByLabelText('Kütle (kg)')
  await user.clear(kg)
  await user.type(kg, values.kg ?? '250.5')
  const time = screen.getByLabelText('Zaman')
  await user.clear(time)
  await user.type(time, '2026-09-18T09:30')
}

async function openOutputSelectors(
  user: ReturnType<typeof userEvent.setup>,
  occurredAt = '2026-09-25T00:30',
) {
  await user.click(screen.getByLabelText('Üretim çıktısı'))
  await user.type(screen.getByLabelText('Zaman'), occurredAt)
}

const posts = (recorded: RecordedRequest[]) => requestsFor(recorded, '/manufacturing/orders/', 'POST')
const reads = (recorded: RecordedRequest[]) =>
  requestsFor(recorded, `/manufacturing/orders/${ORDER}`, 'GET').filter((request) =>
    new URL(request.url, 'http://localhost').pathname === `/api/v1/manufacturing/orders/${ORDER}`)
const balanceReads = (recorded: RecordedRequest[]) => requestsFor(recorded, `/manufacturing/orders/${ORDER}/mass-balance`, 'GET')
const labelReads = (recorded: RecordedRequest[]) => requestsFor(recorded, `/inventory/labels/${ROLL_1}/detail`, 'GET')
const productReads = (recorded: RecordedRequest[]) =>
  requestsFor(recorded, '/manufacturing/references/output-products', 'GET')
const locationReads = (recorded: RecordedRequest[]) =>
  requestsFor(recorded, '/manufacturing/references/output-locations', 'GET')

/** The roll's row in the roll-balance table (the mass-balance panel has a row per roll too). */
async function rollRow(): Promise<HTMLElement> {
  const table = await screen.findByRole('region', { name: 'Ana rulo dengesi (kg)' })
  return within(table).findByRole('row', { name: new RegExp(ROLL_1) })
}

/** The roll's row in the GDC-BAL/1 panel. */
async function balanceRow(): Promise<HTMLElement> {
  const panel = await screen.findByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })
  return within(panel).findByRole('row', { name: new RegExp(ROLL_1) })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('order balances', () => {
  it('shows each roll as the server read it: signed difference, cost as a status, no remaining target invented', async () => {
    stub({
      reads: [
        () =>
          json(
            order({
              process: 'ColdRolling',
              rolls: [
                roll(ROLL_1, {
                  issuedKg: '1000.125',
                  outputsKg: { GoodProduct: '900', SecondQuality: '0', UsableRemnant: '0', Scrap: '0', ProcessLoss: '0' },
                  workInProgressKg: '102.625',
                  unexplainedKg: '-2.5',
                  isFullyAccounted: false,
                }),
              ],
            }),
          ),
      ],
      capabilities: READER_CAPABILITIES,
    })
    renderAt(PATH)

    const row = await rollRow()
    expect(within(row).getByText('1000.125')).toBeTruthy()
    expect(within(row).getByText('-2.5')).toBeTruthy()
    expect(within(row).getByText('Fark var')).toBeTruthy()
    expect(screen.getByTestId('cost-status').textContent).toContain('Hesaplanmadı')
    expect(screen.getByTestId('remaining-target').textContent).toContain('Planda beyan edilmiş bir hedef yok')
    expect(screen.getByText('ColdRolling', { exact: true })).toBeTruthy()
    expect(screen.getByRole('columnheader', { name: 'Açıklanamayan fark' })).toBeTruthy()
  })

  it('draws no form for a reader and never sends a command (card D3)', async () => {
    const { recorded } = stub({ capabilities: READER_CAPABILITIES })
    renderAt(PATH)
    await rollRow()
    expect(screen.queryByRole('heading', { name: 'Gerçekleşme kaydı' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Kaydet' })).toBeNull()
    expect(posts(recorded)).toHaveLength(0)
  })

  it('mounts nothing and reads nothing while the capability is not published (staged)', async () => {
    const { recorded } = stub({ capabilities: [] })
    renderAt(PATH)
    expect(await screen.findByText(/^Bulunamadı/)).toBeTruthy()
    expect(reads(recorded)).toHaveLength(0)
  })

  it('shows a read that met concurrent commits as a retryable unavailable, not a balance', async () => {
    stub({ reads: [() => problem(503, { code: 'service_unavailable' })] })
    renderAt(PATH)
    expect(await screen.findByText(/^Servis geçici olarak kullanılamıyor/)).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })
})

describe('order fact form dirty scope (K3 P2-7)', () => {
  it('an untouched form with its default output class is not dirty; a typed mass is', async () => {
    const user = userEvent.setup()
    stub({ capabilities: RECORDER_CAPABILITIES })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    const company = screen.getByRole('combobox', { name: 'Şirket' }) as HTMLSelectElement
    await user.selectOptions(company, COMPANY_B)
    // No unsaved change: the switch is not asked about.
    await waitFor(() => expect(company.value).toBe(COMPANY_B))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('a typed mass makes the company switch ask', async () => {
    const user = userEvent.setup()
    stub({ capabilities: RECORDER_CAPABILITIES })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await user.type(screen.getByLabelText('Kütle (kg)'), '12')
    await user.selectOptions(screen.getByRole('combobox', { name: 'Şirket' }), COMPANY_B)
    expect(await screen.findByRole('dialog')).toBeTruthy()
  })
})

describe('mass balance panel (GDC-BAL/1)', () => {
  it("shows BAL-003 as the server computed it and BAL-004 / BAL-006 as the engine's Unavailable, never a number", async () => {
    const { recorded } = stub({
      capabilities: READER_CAPABILITIES,
      balances: [
        () =>
          json(
            massBalance({
              rolls: [
                rollBalance(ROLL_1, {
                  difference: { actualInputKg: '1000.125', accountedKg: '1002.625', differenceKg: '-2.5', isFullyAccounted: false },
                }),
              ],
            }),
          ),
      ],
    })
    renderAt(PATH)

    const row = await balanceRow()
    expect(within(row).getByText('1000.125')).toBeTruthy()
    expect(within(row).getByTestId('mass-difference').textContent).toBe('-2.5')
    expect(within(row).getByText('Fark var')).toBeTruthy()
    const scrap = within(row).getByTestId('scrap-percent')
    expect(scrap.textContent).toContain('Hesaplanamıyor')
    expect(scrap.textContent).toContain('fire (kg), hammadde (kg)')
    expect(scrap.querySelector('[data-outcome="unavailable"]')).not.toBeNull()
    const unit = within(row).getByTestId('actual-unit-kg')
    expect(unit.textContent).toContain('gerçek kg, gerçek adet')
    expect(screen.getByTestId('mass-balance-formula').textContent).toBe('GDC-BAL/1 (master v3 §28.9)')
    expect(balanceReads(recorded)).toHaveLength(1)
    expect(balanceReads(recorded)[0].headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('shows a computed engine answer as its text and an unknown input name as it came', async () => {
    stub({
      capabilities: READER_CAPABILITIES,
      balances: [
        () =>
          json(
            massBalance({
              rolls: [
                rollBalance(ROLL_1, {
                  scrapPercent: { outcome: 'Computed', value: '4', missingParameters: [], refusalCode: null, refusedParameter: null },
                  actualUnitKg: unavailable('somethingNew'),
                }),
              ],
            }),
          ),
      ],
    })
    renderAt(PATH)

    const row = await balanceRow()
    expect(within(row).getByTestId('scrap-percent').textContent).toBe('4')
    expect(within(row).getByTestId('actual-unit-kg').textContent).toContain('somethingNew')
  })

  it('refuses a body whose outcome and payload disagree, and keeps the order table', async () => {
    stub({
      capabilities: READER_CAPABILITIES,
      balances: [
        () =>
          json(
            massBalance({
              rolls: [rollBalance(ROLL_1, { scrapPercent: { ...unavailable('fireKg'), value: '4' } })],
            }),
          ),
      ],
    })
    renderAt(PATH)

    await rollRow()
    const panel = await screen.findByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })
    await waitFor(() => expect(within(panel).queryByTestId('mass-difference')).toBeNull())
    await waitFor(() => expect(within(panel).queryByRole('table')).toBeNull())
  })

  it('shows a retryable 503 of its own read without hiding the order', async () => {
    stub({ capabilities: READER_CAPABILITIES, balances: [() => problem(503)] })
    renderAt(PATH)

    await rollRow()
    const panel = await screen.findByRole('region', { name: 'Kütle dengesi (GDC-BAL/1)' })
    await waitFor(() => expect(within(panel).queryByRole('table')).toBeNull())
    expect(within(panel).getByRole('button')).toBeTruthy()
  })

  it('reads the balance again when the order is re-read at a new version after an accepted fact', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      reads: [() => json(order()), () => json(order({ version: 2 }))],
      balances: [() => json(massBalance()), () => json(massBalance({ version: 2 }))],
    })
    renderAt(PATH)
    await balanceRow()
    expect(balanceReads(recorded)).toHaveLength(1)

    await fillFact(user)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(balanceReads(recorded)).toHaveLength(2))
    expect(posts(recorded)).toHaveLength(1)
  })
})

describe('fact forms', () => {
  it('sends a counted input unchanged on an unknown-result retry', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({ capabilities: RECORDER_CAPABILITIES,
      commands: [() => problem(503, {}), () => json(order(), 200, { 'Idempotency-Replayed': 'true' })] })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user)
    await user.type(screen.getByLabelText('Girdi / iade adedi'), '4')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await screen.findByRole('button', { name: 'Aynı olguyu yeniden gönder' })
    expect(posts(recorded)[0].body).toMatchObject({ pieceCount: 4 })
    expect((screen.getByLabelText('Girdi / iade adedi') as HTMLInputElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Aynı olguyu yeniden gönder' }))
    await waitFor(() => expect(posts(recorded)).toHaveLength(2))
    expect(posts(recorded)[1].body).toEqual(posts(recorded)[0].body)
    expect(posts(recorded)[1].headers['Idempotency-Key']).toBe(posts(recorded)[0].headers['Idempotency-Key'])
  })

  it('requires an explicit server-backed plan output line and switches class lines with the output class', async () => {
    const user = userEvent.setup()
    stub({ capabilities: INVENTORY_RECORDER_CAPABILITIES, reads: [() => json(order({ outputLines: [
      { kind: 'SlitLine', slitLineSequence: 1, cutLineId: null },
      { kind: 'SlitLine', slitLineSequence: 2, cutLineId: null },
      { kind: 'ClassLine', slitLineSequence: null, cutLineId: null },
    ] }))] })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await user.click(screen.getByLabelText('Üretim çıktısı'))
    const line = screen.getByLabelText('Plan çıktı satırı')
    expect((line as HTMLSelectElement).value).toBe('')
    expect(within(line).getByRole('option', { name: 'Dilme satırı 2' })).toBeTruthy()
    await user.selectOptions(line, 'SlitLine:2')
    await user.selectOptions(screen.getByLabelText('Çıktı sınıfı'), 'Scrap')
    expect((screen.getByLabelText('Plan çıktı satırı') as HTMLSelectElement).value).toBe('')
    expect(screen.queryByRole('option', { name: 'Dilme satırı 2' })).toBeNull()
    expect(within(screen.getByLabelText('Plan çıktı satırı')).getByRole('option', { name: 'Çıktı sınıfı satırı' })).toBeTruthy()
  })

  it('names each control by its label alone: no option or help text in the accessible name', async () => {
    // The mfg browser gate (run 35436388761) found a wrapping <label> folding the roll options into the
    // select's name. A role query with a string name is an exact match, so any folded text fails here.
    stub()
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    expect(screen.getByRole('combobox', { name: 'Ana rulo' })).toBeTruthy()
    expect(screen.getByRole('textbox', { name: 'Kütle (kg)' })).toBeTruthy()
    expect(screen.getByLabelText('Zaman')).toBeTruthy()
    expect(screen.getByLabelText('WIP ölçümü')).toBeTruthy()
    expect(screen.queryByLabelText('Kullanılmayan girdi iadesi')).toBeNull()
    expect(screen.queryByLabelText('Üretim çıktısı')).toBeNull()
  })

  it('sends one frozen fact with its own key and id, then shows the RE-READ order', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      reads: [
        () => json(order()),
        () => json(order({ version: 2, rolls: [roll(ROLL_1, { issuedKg: '250.5', netIssuedKg: '250.5', unexplainedKg: '250.5', isFullyAccounted: false })] })),
      ],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText('Kaydedildi')).toBeTruthy()
    const [post] = posts(recorded)
    expect(post.url).toBe(`/api/v1/manufacturing/orders/${ORDER}/input-issues`)
    expect(post.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(post.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
    expect(post.body).toMatchObject({ expectedVersion: 1, parentRollId: ROLL_1, kg: '250.5' })
    expect((post.body as { issueId: string }).issueId).toMatch(/^[0-9a-f-]{36}$/)
    expect((post.body as { movementId: string }).movementId).toMatch(/^[0-9a-f-]{36}$/)
    expect((post.body as { movementId: string }).movementId).not.toBe((post.body as { issueId: string }).issueId)
    expect((post.body as { occurredAtUtc: string }).occurredAtUtc).toMatch(/Z$/)
    expect(post.body).not.toHaveProperty('companyId')
    expect(post.body).not.toHaveProperty('factId')
    await waitFor(() => expect(reads(recorded)).toHaveLength(2))
    const row = await rollRow()
    await waitFor(() => expect(within(row).getAllByText('250.5').length).toBeGreaterThan(0))
    expect(labelReads(recorded)).toHaveLength(0)
  })

  it('records explicit process loss without asking for or sending any Inventory output identity', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      reads: [
        () => json(order()),
        () => json(order({
          version: 2,
          rolls: [roll(ROLL_1, {
            issuedKg: '10',
            netIssuedKg: '10',
            outputsKg: { GoodProduct: '0', SecondQuality: '0', UsableRemnant: '0', Scrap: '0', ProcessLoss: '2.5' },
            unexplainedKg: '7.5',
            isFullyAccounted: false,
          })],
        })),
      ],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user, { kind: 'Stok dışı proses kaybı', kg: '2.5' })
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText('Kaydedildi')).toBeTruthy()
    const loss = posts(recorded).find((request) => request.url.endsWith('/process-losses'))!
    expect(loss.body).toMatchObject({ expectedVersion: 1, parentRollId: ROLL_1, kg: '2.5' })
    expect(loss.body).toHaveProperty('factId')
    expect(loss.body).not.toHaveProperty('movementId')
    expect(loss.body).not.toHaveProperty('outputClass')
    expect(loss.body).not.toHaveProperty('productId')
    expect(labelReads(recorded)).toHaveLength(0)
    expect(productReads(recorded)).toHaveLength(0)
    expect(locationReads(recorded)).toHaveLength(0)
  })

  it('says a replayed answer was already recorded', async () => {
    const user = userEvent.setup()
    stub({ commands: [() => json(order(), 200, { 'Idempotency-Replayed': 'true', 'Idempotency-Original-Correlation-Id': 'c1' })] })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('Daha önce kaydedilmiş')).toBeTruthy()
  })

  it('issues a planned company roll, reopens order and label, then returns with the fresh label version', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      reads: [
        () => json(order()),
        () => json(order({
          version: 2,
          rolls: [roll(ROLL_1, {
            issuedKg: '250.5', netIssuedKg: '250.5', unexplainedKg: '250.5', isFullyAccounted: false,
          })],
        })),
        () => json(order({
          version: 3,
          rolls: [roll(ROLL_1, {
            issuedKg: '250.5', returnedKg: '50', netIssuedKg: '200.5', unexplainedKg: '200.5',
            isFullyAccounted: false,
          })],
        })),
      ],
      labels: [
        () => json(labelDetail('7', '1000')),
        () => json(labelDetail('8', '750')),
        () => json(labelDetail('8', '750')),
        () => json(labelDetail('9', '800')),
      ],
      commands: [() => json(order({ version: 2 })), () => json(order({ version: 3 }))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })

    // Cache v7 first. The issue itself must not trust or require it, and its success must
    // refresh this exact label before return can reuse the cache.
    await user.click(screen.getByLabelText('Kullanılmayan girdi iadesi'))
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
    await screen.findByTestId('parent-label-state')
    expect(within(screen.getByTestId('parent-label-state')).getByText('7')).toBeTruthy()
    await user.click(screen.getByLabelText('Girdi çıkışı'))
    await fillFact(user)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await waitFor(() => expect(reads(recorded)).toHaveLength(2))
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(2))

    await fillFact(user, { kind: 'Kullanılmayan girdi iadesi', kg: '50' })
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(3))
    expect(within(screen.getByTestId('parent-label-state')).getByText('8')).toBeTruthy()
    expect(within(screen.getByTestId('parent-label-state')).getAllByText('750 kg')).toHaveLength(2)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await waitFor(() => expect(posts(recorded)).toHaveLength(2))
    await waitFor(() => expect(reads(recorded)).toHaveLength(3))
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(4))
    await waitFor(() => expect(within(screen.getByTestId('parent-label-state')).getAllByText('800 kg')).toHaveLength(2))

    const [issue, returned] = posts(recorded)
    expect(issue.url).toBe(`/api/v1/manufacturing/orders/${ORDER}/input-issues`)
    expect(issue.body).toMatchObject({ expectedVersion: 1, parentRollId: ROLL_1, kg: '250.5' })
    expect(returned.url).toBe(`/api/v1/manufacturing/orders/${ORDER}/input-returns`)
    expect(returned.body).toMatchObject({
      expectedVersion: 2,
      parentRollId: ROLL_1,
      expectedParentLabelVersion: '8',
      kg: '50',
    })
    expect((returned.body as { returnId: string }).returnId).toMatch(/^[0-9a-f-]{36}$/)
    expect((returned.body as { movementId: string }).movementId).toMatch(/^[0-9a-f-]{36}$/)
    expect((returned.body as { movementId: string }).movementId)
      .not.toBe((returned.body as { returnId: string }).returnId)
    expect(returned.body).not.toHaveProperty('factId')
    const row = await rollRow()
    expect(within(row).getByText('50')).toBeTruthy()
  })

  it('selects only this orders real issue movement for a return and freezes the trace across an ambiguous resend', async () => {
    const user = userEvent.setup(); const foreign = 'e0000000-0000-4000-8000-000000000099'
    const { recorded } = stub({ capabilities: INVENTORY_RECORDER_CAPABILITIES,
      labels: [() => json(labelDetail('8', '750', COMPANY_A, COMPANY_A, 'Company', 'Company', [
        issueMovement(), issueMovement('Company', COMPANY_A, 'Company', COMPANY_A, { movementId: foreign, sourceId: COMPANY_B }),
      ], true))], commands: [() => problem(503), () => json(order({ version: 2 }))] })
    renderAt(PATH); await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user, { kind: 'Kullanılmayan girdi iadesi', kg: '2' })
    await screen.findByTestId('parent-label-state')
    const select = screen.getByLabelText('İadenin kaynak sarf hareketi')
    await waitFor(() => expect(within(select).getByRole('option', { name: new RegExp(ISSUE_MOVEMENT) })).toBeTruthy())
    expect(within(select).queryByRole('option', { name: new RegExp(foreign) })).toBeNull()
    await user.selectOptions(select, ISSUE_MOVEMENT)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await screen.findByRole('button', { name: 'Aynı olguyu yeniden gönder' })
    expect((select as HTMLSelectElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Aynı olguyu yeniden gönder' }))
    await waitFor(() => expect(posts(recorded)).toHaveLength(2))
    expect(posts(recorded)[0].body).toHaveProperty('sourceIssueMovementId', ISSUE_MOVEMENT)
    expect(posts(recorded)[1].body).toEqual(posts(recorded)[0].body)
    expect(posts(recorded)[1].headers['Idempotency-Key']).toBe(posts(recorded)[0].headers['Idempotency-Key'])
  })

  it('selects server-backed Product and Organization facts and records one company-owned output atomically', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      reads: [
        () => json(order({
          version: 2,
          rolls: [roll(ROLL_1, { issuedKg: '100', netIssuedKg: '100', unexplainedKg: '100' })],
        })),
        () => json(order({
          version: 3,
          rolls: [roll(ROLL_1, {
            issuedKg: '100', netIssuedKg: '100',
            outputsKg: { GoodProduct: '40', SecondQuality: '0', UsableRemnant: '0', Scrap: '0', ProcessLoss: '0' },
            unexplainedKg: '60', isFullyAccounted: false,
          })],
        })),
      ],
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
      commands: [() => json(order({ version: 3 }))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })

    await user.click(screen.getByLabelText('Üretim çıktısı'))
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
    const time = screen.getByLabelText('Zaman')
    await user.type(time, '2026-09-25T00:30')
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))
    await waitFor(() => expect(productReads(recorded)).toHaveLength(1))
    await waitFor(() => expect(locationReads(recorded)).toHaveLength(1))

    // URT-D43 (2): a new output starts as a good product; only a usable remnant asks for a label classification.
    expect((screen.getByLabelText('Çıktı sınıfı') as HTMLSelectElement).value).toBe('GoodProduct')
    expect(screen.queryByLabelText('Artık etiket sınıfı')).toBeNull()
    await user.selectOptions(screen.getByLabelText('Çıktı sınıfı'), 'GoodProduct')
    await user.selectOptions(screen.getByLabelText('Ürün'), PRODUCT)
    await user.selectOptions(screen.getByLabelText('Lokasyon'), LOCATION)
    await user.type(screen.getByLabelText('Yeni etiket kodu (opsiyonel)'), 'OUT-40')
    await user.type(screen.getByLabelText('Rulo no (opsiyonel)'), 'COIL-40')
    await user.type(screen.getByLabelText('Belge referansları (satır başına)'), 'CERT-1{enter}CERT-2')
    await user.selectOptions(screen.getByLabelText('İlk stok durumu'), 'Blocked')
    await user.type(screen.getByLabelText('Kütle (kg)'), '40')
    const ownership = within(screen.getByTestId('output-form')).getByTestId('output-issued-ownership')
    expect([...ownership.querySelectorAll('[data-party]')].map((node) =>
      [node.getAttribute('data-party'), node.getAttribute('data-kind'), node.textContent]))
      .toEqual([['owner', 'Company', COMPANY_A], ['custodian', 'Company', COMPANY_A]])
    expect(within(ownership).getAllByText(/^Bu şirket/)).toHaveLength(2)
    expect(screen.queryByTestId('company-output-form')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText('Kaydedildi')).toBeTruthy()
    const output = posts(recorded).find((request) => request.url.endsWith('/outputs'))!
    expect(output.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(output.body).toMatchObject({
      expectedVersion: 2,
      parentRollId: ROLL_1,
      expectedParentLabelVersion: '8',
      outputClass: 'GoodProduct',
      productId: PRODUCT,
      expectedProductVersion: '12',
      labelCode: 'OUT-40',
      heatNumber: null,
      coilNumber: 'COIL-40',
      requiredDocuments: ['CERT-1', 'CERT-2'],
      ownerKind: 'Company',
      ownerId: COMPANY_A,
      custodianKind: 'Company',
      custodianId: COMPANY_A,
      locationId: LOCATION,
      initialBlock: 'Blocked',
      kg: '40',
    })
    const ids = ['outputId', 'movementId', 'lotId', 'labelId']
      .map((name) => (output.body as Record<string, string>)[name])
    expect(new Set(ids).size).toBe(4)
    expect(ids.every((id) => /^[0-9a-f-]{36}$/.test(id))).toBe(true)
    expect(output.body).not.toHaveProperty('companyId')
    expect(output.body).not.toHaveProperty('remnantClassification')
    const selectedEffectiveOn = new URL(productReads(recorded)[0].url, 'http://local.test')
      .searchParams.get('effectiveOn')
    expect(selectedEffectiveOn).toBe((output.body as { occurredAtUtc: string }).occurredAtUtc.slice(0, 10))
    await waitFor(() => expect(reads(recorded)).toHaveLength(2))
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(2))
  })

  it.each([
    { chosen: null, sent: 'SecondQuality' },
    { chosen: 'Standard', sent: 'Standard' },
  ] as const)('sends a usable remnant with its label classification ($sent; chosen: $chosen)', async ({ chosen, sent }) => {
    // URT-S16 (BS-5, BS-14): the remnant keeps the chosen classification, second quality preselected; the server
    // adds the separate UsableRemnant production status.
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })

    await user.click(screen.getByLabelText('Üretim çıktısı'))
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
    await user.type(screen.getByLabelText('Zaman'), '2026-09-25T00:30')
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))
    await screen.findByRole('option', { name: /PRD-12/ })
    await screen.findByRole('option', { name: /HOL-1/ })

    expect(screen.queryByLabelText('Artık etiket sınıfı')).toBeNull()
    await user.selectOptions(screen.getByLabelText('Çıktı sınıfı'), 'UsableRemnant')
    const remnant = screen.getByLabelText('Artık etiket sınıfı') as HTMLSelectElement
    expect(remnant.value).toBe('SecondQuality')
    expect([...remnant.options].map((option) => [option.value, option.textContent]))
      .toEqual([['Standard', 'Standart'], ['SecondQuality', 'İkinci kalite']])
    if (chosen !== null) await user.selectOptions(remnant, chosen)
    await user.selectOptions(screen.getByLabelText('Ürün'), PRODUCT)
    await user.selectOptions(screen.getByLabelText('Lokasyon'), LOCATION)
    await user.type(screen.getByLabelText('Yeni etiket kodu (opsiyonel)'), 'REM-40')
    await user.selectOptions(screen.getByLabelText('İlk stok durumu'), 'Free')
    await user.type(screen.getByLabelText('Kütle (kg)'), '40')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(posts(recorded)).toHaveLength(1))
    expect(posts(recorded)[0].body).toMatchObject({
      outputClass: 'UsableRemnant',
      remnantClassification: sent,
      labelCode: 'REM-40',
    })
  })

  it('sends an output without a label code as null so the server numbers it from Ayarlar', async () => {
    // URT-S21 (URT-D62): the output label code is optional; empty or blank is sent as null, never as the input roll.
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })

    await user.click(screen.getByLabelText('Üretim çıktısı'))
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
    await user.type(screen.getByLabelText('Zaman'), '2026-09-25T00:30')
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))
    await screen.findByRole('option', { name: /PRD-12/ })
    await screen.findByRole('option', { name: /HOL-1/ })

    const labelCode = screen.getByLabelText('Yeni etiket kodu (opsiyonel)')
    expect(labelCode.getAttribute('aria-describedby')).toBe('mfg-output-label-code-help')
    expect(document.getElementById('mfg-output-label-code-help')?.textContent).toContain('Ayarlar')
    await user.selectOptions(screen.getByLabelText('Ürün'), PRODUCT)
    await user.selectOptions(screen.getByLabelText('Lokasyon'), LOCATION)
    await user.type(labelCode, '   ')
    await user.selectOptions(screen.getByLabelText('İlk stok durumu'), 'Free')
    await user.type(screen.getByLabelText('Kütle (kg)'), '40')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(posts(recorded)).toHaveLength(1))
    const body = posts(recorded)[0].body as Record<string, unknown>
    expect(body).toHaveProperty('labelCode', null)
    expect(body.labelCode).not.toBe(ROLL_1)
  })

  it('freezes customer/fason owner and custodian from this order input issue into the output command', async () => {
    const user = userEvent.setup()
    const customerIssue = issueMovement('Counterparty', COMPANY_B, 'Company', COMPANY_A)
    const customerLabel = () => labelDetail(
      '8', '900', COMPANY_B, COMPANY_A, 'Counterparty', 'Company', [customerIssue],
    )
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      labels: [() => json(customerLabel()), () => json(customerLabel())],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })

    await user.click(screen.getByLabelText('Üretim çıktısı'))
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
    await user.type(screen.getByLabelText('Zaman'), '2026-09-25T00:30')
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))
    await screen.findByRole('option', { name: /PRD-12/ })
    await screen.findByRole('option', { name: /HOL-1/ })

    await user.selectOptions(screen.getByLabelText('Çıktı sınıfı'), 'GoodProduct')
    await user.selectOptions(screen.getByLabelText('Ürün'), PRODUCT)
    await user.selectOptions(screen.getByLabelText('Lokasyon'), LOCATION)
    await user.type(screen.getByLabelText('Yeni etiket kodu (opsiyonel)'), 'FASON-OUT-40')
    await user.selectOptions(screen.getByLabelText('İlk stok durumu'), 'Free')
    await user.type(screen.getByLabelText('Kütle (kg)'), '40')

    // The derived ownership is SHOWN, read-only, in the output form and in the parent-label state: the owner is
    // the Counterparty of this order's issue, the custodian this company. Nothing there can be edited.
    const form = screen.getByTestId('output-form')
    const shown = within(form).getByTestId('output-issued-ownership')
    expect(within(shown).getByText('Sahip')).toBeTruthy()
    expect(within(shown).getByText('Gözeten')).toBeTruthy()
    expect(shown.querySelector('[data-party="owner"]')?.textContent).toBe(COMPANY_B)
    expect(shown.querySelector('[data-party="owner"]')?.getAttribute('data-kind')).toBe('Counterparty')
    expect(shown.querySelector('[data-party="custodian"]')?.textContent).toBe(COMPANY_A)
    expect(shown.querySelector('[data-party="custodian"]')?.getAttribute('data-kind')).toBe('Company')
    expect(within(shown).getByText(/^Cari/)).toBeTruthy()
    expect(within(shown).getByText(/^Bu şirket/)).toBeTruthy()
    expect(shown.querySelectorAll('input, select, textarea, button')).toHaveLength(0)
    const parentState = screen.getByTestId('parent-label-state')
    expect(within(parentState).getByTestId('parent-label-issued-ownership')
      .querySelector('[data-party="owner"]')?.textContent).toBe(COMPANY_B)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(posts(recorded)).toHaveLength(1))
    expect(posts(recorded)[0].body).toMatchObject({
      ownerKind: 'Counterparty',
      ownerId: COMPANY_B,
      custodianKind: 'Company',
      custodianId: COMPANY_A,
      locationId: LOCATION,
    })
  })

  it.each([
    [
      'no input issue for this order',
      () => labelDetail('7', '1000', COMPANY_B, COMPANY_A, 'Counterparty', 'Company', [
        issueMovement('Counterparty', COMPANY_B, 'Company', COMPANY_A, { sourceId: COMPANY_B }),
      ]),
    ],
    [
      'multiple input issue positions',
      () => labelDetail('7', '1000', COMPANY_B, COMPANY_A, 'Counterparty', 'Company', [
        issueMovement('Counterparty', COMPANY_B, 'Company', COMPANY_A),
        issueMovement('Counterparty', COMPANY_B, 'Company', COMPANY_A, {
          movementId: 'e0000000-0000-4000-8000-000000000017',
          locationId: 'e0000000-0000-4000-8000-000000000018',
        }),
      ]),
    ],
  ])('fails output closed for %s', async (_caseName, detail) => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      labels: [() => json(detail())],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })

    await user.click(screen.getByLabelText('Üretim çıktısı'))
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))

    expect(await screen.findByText(/tek ve eksiksiz Inventory girdi çıkışı sahipliği/)).toBeTruthy()
    expect(screen.queryByTestId('output-issued-ownership')).toBeNull()
    expect(screen.queryByTestId('parent-label-issued-ownership')).toBeNull()
    expect((screen.getByRole('button', { name: 'Kaydet' }) as HTMLButtonElement).disabled).toBe(true)
    expect(posts(recorded)).toHaveLength(0)
  })

  it('names a truncated movement timeline as truncated and fails output closed', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      labels: [() => json(labelDetail('7', '1000', COMPANY_B, COMPANY_A, 'Counterparty', 'Company', undefined, true))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })

    await user.click(screen.getByLabelText('Üretim çıktısı'))
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))

    // The trace may exist beyond the window, so the reason is the truncation, not a missing issue.
    expect(await screen.findByText(/hareket geçmişi bu görünümde kısaltılmış/)).toBeTruthy()
    expect(screen.queryByText(/tek ve eksiksiz Inventory girdi çıkışı sahipliği/)).toBeNull()
    expect(screen.queryByTestId('output-issued-ownership')).toBeNull()
    expect((screen.getByRole('button', { name: 'Kaydet' }) as HTMLButtonElement).disabled).toBe(true)
    expect(posts(recorded)).toHaveLength(0)
  })

  it('re-reads output selectors after route re-entry inside the production stale window', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({ capabilities: INVENTORY_RECORDER_CAPABILITIES })
    const { router } = renderAt(PATH, { staleTime: 30_000 })
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })

    await openOutputSelectors(user)
    await waitFor(() => expect(productReads(recorded)).toHaveLength(1))

    await router.navigate('/')
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Gerçekleşme kaydı' })).toBeNull())
    await router.navigate(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await openOutputSelectors(user)

    await waitFor(() => expect(productReads(recorded)).toHaveLength(2))
  })

  it('removes cached output selectors when the fact capability is revoked before re-entry', async () => {
    const user = userEvent.setup()
    let capabilities: readonly string[] = INVENTORY_RECORDER_CAPABILITIES
    const { recorded } = stub({ capabilities: () => capabilities })
    const { router } = renderAt(PATH, { staleTime: 30_000 })
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await openOutputSelectors(user)
    await screen.findByRole('option', { name: /PRD-12/ })

    await router.navigate('/')
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Gerçekleşme kaydı' })).toBeNull())
    capabilities = READER_CAPABILITIES
    await router.navigate(PATH)
    await screen.findByRole('region', { name: 'Ana rulo dengesi (kg)' })

    expect(screen.queryByRole('heading', { name: 'Gerçekleşme kaydı' })).toBeNull()
    expect(screen.queryByRole('option', { name: /PRD-12/ })).toBeNull()
    expect(productReads(recorded)).toHaveLength(1)
  })

  it('ignores a selector answer that arrives after a newer route entry has loaded', async () => {
    const user = userEvent.setup()
    let releaseOld: (response: Response) => void = () => undefined
    const oldAnswer = new Promise<Response>((resolve) => {
      releaseOld = resolve
    })
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      products: [
        () => oldAnswer,
        () => json({
          items: [{
            productId: NEW_PRODUCT,
            productVersion: '13',
            code: 'PRD-13',
            name: 'Yeni yetkili ürün',
            stockUnitCode: 'kg',
            pieceCounting: 'NotCounted',
          }],
          searchWindowFull: false,
        }),
      ],
    })
    const { router } = renderAt(PATH, { staleTime: 30_000 })
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await openOutputSelectors(user)
    await waitFor(() => expect(productReads(recorded)).toHaveLength(1))

    await router.navigate('/')
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Gerçekleşme kaydı' })).toBeNull())
    await router.navigate(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await openOutputSelectors(user)
    await screen.findByRole('option', { name: /PRD-13/ })

    releaseOld(json({
      items: [{
        productId: PRODUCT,
        productVersion: '12',
        code: 'PRD-12',
        name: 'Eski giriş ürünü',
        stockUnitCode: 'kg',
        pieceCounting: 'NotCounted',
      }],
      searchWindowFull: false,
    }))
    await oldAnswer
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(screen.queryByRole('option', { name: /PRD-12/ })).toBeNull()
    expect(screen.getByRole('option', { name: /PRD-13/ })).toBeTruthy()
    expect(productReads(recorded)).toHaveLength(2)
  })

  it('returns an issued customer/fason label with its fresh version and no invented ownership fields', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      labels: [() => json(labelDetail(
        '7', '1000', COMPANY_B, COMPANY_A, 'Counterparty', 'Company',
      ))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user, { kind: 'Kullanılmayan girdi iadesi', kg: '25' })
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))

    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await waitFor(() => expect(posts(recorded)).toHaveLength(1))
    const [returned] = posts(recorded)
    expect(returned.url).toBe(`/api/v1/manufacturing/orders/${ORDER}/input-returns`)
    expect(returned.body).toMatchObject({
      expectedVersion: 1,
      parentRollId: ROLL_1,
      expectedParentLabelVersion: '7',
      kg: '25',
    })
    expect(returned.body).not.toHaveProperty('ownerKind')
    expect(returned.body).not.toHaveProperty('ownerId')
    expect(returned.body).not.toHaveProperty('custodianKind')
    expect(returned.body).not.toHaveProperty('custodianId')
  })

  it('fences a revoked Inventory label read on 403 and never sends a return', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      labels: [() => problem(403, { code: 'inventory_label_detail_forbidden' })],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await user.click(screen.getByLabelText('Kullanılmayan girdi iadesi'))
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)

    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Gerçekleşme kaydı' })).toBeNull())
    expect(labelReads(recorded)).toHaveLength(1)
    expect(posts(recorded)).toHaveLength(0)
  })

  it('refreshes both owner reads after a parent-label version conflict and keeps the typed return', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      reads: [
        () => json(order({
          version: 2,
          rolls: [roll(ROLL_1, { issuedKg: '250.5', netIssuedKg: '250.5', unexplainedKg: '250.5' })],
        })),
        () => json(order({
          version: 2,
          rolls: [roll(ROLL_1, { issuedKg: '250.5', netIssuedKg: '250.5', unexplainedKg: '250.5' })],
        })),
      ],
      labels: [() => json(labelDetail('8', '750')), () => json(labelDetail('9', '750'))],
      commands: [() => problem(409, { code: 'inventory_manufacturing_parent_label_version_conflict' })],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user, { kind: 'Kullanılmayan girdi iadesi', kg: '50' })
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText(/etiketi siz formu doldururken değişti/)).toBeTruthy()
    await waitFor(() => expect(reads(recorded)).toHaveLength(2))
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(2))
    expect(within(screen.getByTestId('parent-label-state')).getByText('9')).toBeTruthy()
    expect((screen.getByLabelText('Kütle (kg)') as HTMLInputElement).value).toBe('50')
    const [request] = posts(recorded)
    expect(request.body).toMatchObject({ expectedVersion: 2, expectedParentLabelVersion: '8' })
  })

  it('refuses a kilogram outside the plain-decimal grammar without sending anything', async () => {
    const user = userEvent.setup()
    const { recorded } = stub()
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user, { kg: '1,5' })
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText(/Kilogram biçimi geçersiz/)).toBeTruthy()
    expect(posts(recorded)).toHaveLength(0)
  })

  it('re-reads after a version conflict and sends the next attempt as a NEW command at the new version', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      reads: [() => json(order()), () => json(order({ version: 3 }))],
      commands: [
        () => problem(409, { code: 'manufacturing_version_conflict', expectedVersion: '1', currentVersion: '3' }),
        () => json(order({ version: 4 })),
      ],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText(/Emir siz formu doldururken değişti/)).toBeTruthy()
    await waitFor(() => expect(reads(recorded)).toHaveLength(2))
    // The typed values stay for the next submit.
    expect((screen.getByLabelText('Kütle (kg)') as HTMLInputElement).value).toBe('250.5')

    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await screen.findByText('Kaydedildi')
    const [first, second] = posts(recorded)
    expect(second.body).toMatchObject({ expectedVersion: 3 })
    expect(second.headers['Idempotency-Key']).not.toBe(first.headers['Idempotency-Key'])
    expect((second.body as { issueId: string }).issueId).not.toBe((first.body as { issueId: string }).issueId)
  })

  it('freezes an unknown outcome and re-sends the SAME fact id and key until the server settles it', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      commands: [() => problem(503, { code: 'service_unavailable' }), () => json(order({ version: 2 }))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText('Sonuç bilinmiyor')).toBeTruthy()
    expect((screen.getByLabelText('Kütle (kg)') as HTMLInputElement).disabled).toBe(true)
    expect(screen.queryByRole('button', { name: 'Kaydet' })).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Aynı olguyu yeniden gönder' }))
    await screen.findByText('Kaydedildi')
    const [first, second] = posts(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('names a rule refusal by its code and an unlisted code as a generic refusal with the code', async () => {
    const user = userEvent.setup()
    stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      commands: [
        () => problem(409, { code: 'inventory_manufacturing_input_return_exceeds_issued' }),
        () => problem(422, { code: 'manufacturing_new_rule' }),
      ],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user, { kind: 'Kullanılmayan girdi iadesi' })
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('İade, bu rulodan net çıkan kütleyi aşamaz.')).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('İşlem reddedildi (manufacturing_new_rule).')).toBeTruthy()
  })

  it('names the refused input issue, its parent label, the place and the usable and requested kg on a physical-stock refusal (SAT-D318)', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      commands: [
        (init) => {
          // The server names the issue it refused: echo the fact id the screen sent.
          const sent = JSON.parse(String(init?.body)) as { issueId: string }
          return problem(409, {
            code: 'inventory_manufacturing_physical_stock_insufficient',
            stockShortfall: stockShortfall(sent.issueId, '500', '480'),
          })
        },
      ],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user, { kg: '500' })
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    const alert = await screen.findByText(/fiziksel stok sıfırın altına düşürülemez/)
    const [post] = posts(recorded)
    const issueId = (post.body as { issueId: string }).issueId
    expect(alert.textContent).toContain(`olgu ${issueId}`)
    expect(alert.textContent).toContain(`Ana rulo etiketi: ${ROLL_1}.`)
    expect(alert.textContent).toContain(`Konum: ${SHORTFALL_LOCATION}.`)
    // SINAN-CEVAP (8) question 5, in the owner's example words: the usable and the requested kg together.
    expect(alert.textContent).toContain('Etikette kullanılabilir 480 kg var, 500 kg istendi.')
    // Reserved kilograms stay in the server: the sentence states no other stock figure.
    expect(alert.textContent).not.toMatch(/mevcut|rezerve|serbest/i)
    expect(alert.closest('[data-outcome="failed"]')?.getAttribute('data-failure')).toBe('rule')
  })

  it('keeps the plain physical-stock refusal when the 409 carries no shortfall detail', async () => {
    const user = userEvent.setup()
    stub({ commands: [() => problem(409, { code: 'inventory_manufacturing_physical_stock_insufficient' })] })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillFact(user)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText('Ana ruloda istenen kütle kadar serbest fiziksel stok yok.')).toBeTruthy()
    expect(screen.queryByText(/fiziksel stok sıfırın altına düşürülemez/)).toBeNull()
  })

  it('closes the forms on a closed order', async () => {
    stub({ reads: [() => json(order({ status: 'Closed' }))] })
    renderAt(PATH)
    expect(await screen.findAllByText('Emir kapalı; yeni gerçekleşme kaydedilmez.')).not.toHaveLength(0)
    expect((screen.getByRole('button', { name: 'Kaydet' }) as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByLabelText('Kütle (kg)') as HTMLInputElement).disabled).toBe(true)
  })
})

describe('the close (MFG-ORD)', () => {
  it('is not drawn for a reader: closing is its own authority', async () => {
    stub({ capabilities: READER_CAPABILITIES })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Üretim emri' })
    expect(screen.queryByRole('button', { name: 'Emri kapat' })).toBeNull()
  })

  it('is not drawn for a recorder either: recording a fact does not close an order', async () => {
    stub()
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    expect(screen.queryByRole('button', { name: 'Emri kapat' })).toBeNull()
  })

  it('confirms first, then sends the version it read under one key, and re-reads the order', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: CLOSER_CAPABILITIES,
      reads: [() => json(order({ version: 4 })), () => json(order({ version: 5, status: 'Closed', closedAtUtc: '2026-09-18T12:00:00.000Z', closedByUserId: '99999999-9999-9999-9999-999999999999' }))],
      commands: [() => json(order({ version: 5, status: 'Closed' }))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Emri kapat' })

    // Nothing is sent before the confirmation.
    await user.click(screen.getByRole('button', { name: 'Emri kapat' }))
    expect(posts(recorded)).toHaveLength(0)
    await user.click(screen.getByRole('button', { name: 'Evet, emri kapat' }))

    await screen.findByText('Emir kapatıldı')
    const post = posts(recorded).find((request) => request.url.endsWith('/close'))!
    expect(post.body).toEqual({ expectedVersion: 4, exceptionReason: null })
    expect(post.headers['Idempotency-Key']).toBeTruthy()
    // The screen re-reads instead of predicting the closed order.
    await waitFor(() => expect(reads(recorded).length).toBeGreaterThan(1))
    expect(await screen.findByTestId('order-already-closed')).toBeTruthy()
  })

  it('shows the decided refusal reason and does not close the order', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: CLOSER_CAPABILITIES,
      commands: [() => problem(422, { code: 'manufacturing_unexplained_difference' })],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Emri kapat' })
    await user.click(screen.getByRole('button', { name: 'Emri kapat' }))
    await user.click(screen.getByRole('button', { name: 'Evet, emri kapat' }))

    const refusal = await screen.findByTestId('close-refusal')
    expect(refusal.textContent).toContain('açıklanamayan kütle farkı')
    // No tolerance is offered anywhere on the screen: the rule says so in the section's help text AND in
    // the refusal itself, so the phrase is expected twice, never once and never nowhere.
    expect(screen.getAllByText(/tolerans ile geçilmez/)).toHaveLength(2)
    expect(posts(recorded).filter((request) => request.url.endsWith('/close'))).toHaveLength(1)
    expect(screen.queryByTestId('order-already-closed')).toBeNull()
  })

  it('does not draw the exception reason control without its dedicated capability', async () => {
    const user = userEvent.setup()
    stub({
      capabilities: CLOSER_CAPABILITIES,
      commands: [() => problem(422, { code: 'manufacturing_closure_approval_required' })],
    })
    renderAt(PATH)

    await screen.findByRole('heading', { name: 'Emri kapat' })
    await user.click(screen.getByRole('button', { name: 'Emri kapat' }))
    await user.click(screen.getByRole('button', { name: 'Evet, emri kapat' }))

    expect(await screen.findByText(/ayrı yetki ve gerekçeli onay/)).toBeTruthy()
    expect(screen.queryByLabelText('Onay gerekçesi')).toBeNull()
  })

  it('shows output above input as a plain refusal with no approval path, even for the exception approver', async () => {
    // URT-D41/URT-D42: more kg out than in on a parent roll never closes; no tolerance and no reasoned approval.
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: EXCEPTION_APPROVER_CAPABILITIES,
      commands: [() => problem(422, { code: 'manufacturing_closure_output_exceeds_input' })],
    })
    renderAt(PATH)

    await screen.findByRole('heading', { name: 'Emri kapat' })
    await user.click(screen.getByRole('button', { name: 'Emri kapat' }))
    await user.click(screen.getByRole('button', { name: 'Evet, emri kapat' }))

    const refusal = await screen.findByTestId('close-refusal')
    expect(refusal.textContent).toContain('çıkan kg girenden fazla')
    expect(refusal.textContent).toContain('tolerans ve onay yolu yoktur')
    expect(refusal.textContent).not.toContain('manufacturing_closure_output_exceeds_input')
    expect(screen.queryByTestId('close-exception-approval')).toBeNull()
    expect(screen.queryByLabelText('Onay gerekçesi')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Gerekçeyle onayla ve kapat' })).toBeNull()
    expect(posts(recorded).filter((request) => request.url.endsWith('/close'))).toHaveLength(1)
    expect(screen.queryByTestId('order-already-closed')).toBeNull()
  })

  it('answers an approval-required result with a new frozen reasoned attempt only for its dedicated capability', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: EXCEPTION_APPROVER_CAPABILITIES,
      reads: [
        () => json(order({ version: 4 })),
        () => json(order({ version: 5, status: 'Closed', closedAtUtc: '2026-09-18T12:00:00.000Z', closedByUserId: '99999999-9999-9999-9999-999999999999' })),
      ],
      commands: [
        () => problem(422, { code: 'manufacturing_closure_approval_required' }),
        () => json(order({ version: 5, status: 'Closed' })),
      ],
    })
    renderAt(PATH)

    await screen.findByRole('heading', { name: 'Emri kapat' })
    await user.click(screen.getByRole('button', { name: 'Emri kapat' }))
    await user.click(screen.getByRole('button', { name: 'Evet, emri kapat' }))
    expect(await screen.findByTestId('close-exception-approval')).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Gerekçeyle onayla ve kapat' }))
    expect(await screen.findByText('Onay gerekçesi zorunludur.')).toBeTruthy()
    await user.type(screen.getByLabelText('Onay gerekçesi'), '  Teknik inceleme tamamlandı.  ')
    await user.click(screen.getByRole('button', { name: 'Gerekçeyle onayla ve kapat' }))

    await screen.findByText('Emir kapatıldı')
    const closes = posts(recorded).filter((request) => request.url.endsWith('/close'))
    expect(closes).toHaveLength(2)
    expect(closes[0].body).toEqual({ expectedVersion: 4, exceptionReason: null })
    expect(closes[1].body).toEqual({
      expectedVersion: 4,
      exceptionReason: 'Teknik inceleme tamamlandı.',
    })
    expect(closes[1].headers['Idempotency-Key']).not.toBe(closes[0].headers['Idempotency-Key'])
  })

  it('re-sends the exact reason and key when the exception close outcome is unknown', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: EXCEPTION_APPROVER_CAPABILITIES,
      commands: [
        () => problem(422, { code: 'manufacturing_closure_approval_required' }),
        () => problem(503, { code: 'service_unavailable' }),
        () => json(order({ version: 2, status: 'Closed' })),
      ],
    })
    renderAt(PATH)

    await screen.findByRole('heading', { name: 'Emri kapat' })
    await user.click(screen.getByRole('button', { name: 'Emri kapat' }))
    await user.click(screen.getByRole('button', { name: 'Evet, emri kapat' }))
    await user.type(await screen.findByLabelText('Onay gerekçesi'), 'Ölçüm teyit edildi.')
    await user.click(screen.getByRole('button', { name: 'Gerekçeyle onayla ve kapat' }))
    await screen.findByText('Kapanışın sonucu bilinmiyor')
    await user.click(screen.getByRole('button', { name: 'Aynı işlem anahtarıyla yeniden gönder' }))
    await screen.findByText('Emir kapatıldı')

    const closes = posts(recorded).filter((request) => request.url.endsWith('/close'))
    expect(closes).toHaveLength(3)
    expect(closes[2].headers['Idempotency-Key']).toBe(closes[1].headers['Idempotency-Key'])
    expect(closes[2].body).toEqual(closes[1].body)
  })

  it('keeps one key for an unknown outcome and sends the same command again', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: CLOSER_CAPABILITIES,
      commands: [() => problem(503, { code: 'service_unavailable' }), () => json(order({ version: 2, status: 'Closed' }))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Emri kapat' })
    await user.click(screen.getByRole('button', { name: 'Emri kapat' }))
    await user.click(screen.getByRole('button', { name: 'Evet, emri kapat' }))

    await screen.findByText('Kapanışın sonucu bilinmiyor')
    await user.click(screen.getByRole('button', { name: 'Aynı işlem anahtarıyla yeniden gönder' }))
    await screen.findByText('Emir kapatıldı')
    const closes = posts(recorded).filter((request) => request.url.endsWith('/close'))
    expect(closes).toHaveLength(2)
    expect(closes[1].headers['Idempotency-Key']).toBe(closes[0].headers['Idempotency-Key'])
    expect(closes[1].body).toEqual(closes[0].body)
  })

  it('reports a replayed close as history, not as a second close', async () => {
    const user = userEvent.setup()
    stub({
      capabilities: CLOSER_CAPABILITIES,
      commands: [() => json(order({ version: 2, status: 'Closed' }), 200, { 'Idempotency-Replayed': 'true' })],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Emri kapat' })
    await user.click(screen.getByRole('button', { name: 'Emri kapat' }))
    await user.click(screen.getByRole('button', { name: 'Evet, emri kapat' }))

    expect(await screen.findByText('Emir zaten kapatılmıştı')).toBeTruthy()
  })
})

// AK-7 (URT-D112): the output declaration carries the counted pieces with the weighed kg; the server decides whether the
// product is counted and refuses by code; the order screen shows the server's real/theoretical kg per piece deviation.
describe('output piece count (AK-7)', () => {
  const COUNTED_OUTPUT = 'e0000000-0000-4000-8000-0000000000a7'

  function pieceView(overrides: Record<string, unknown> = {}) {
    return {
      outputId: COUNTED_OUTPUT, parentRollId: ROLL_1, outputClass: 'GoodProduct', declaredPieceCount: 48,
      netPieceCount: 48, netKg: '20', theoreticalKgPerPiece: '0.4', realKgPerPiece: '0.417',
      deviationKgPerPiece: '0.017', deviationPercent: '4.17',
      formulaVersion: 'manufacturing.output-piece-deviation.v1', deviationThresholdApplied: false, ...overrides,
    }
  }

  /** AK-7b (URT-D112): the selector's answer for the one product, with what the declaration will ask of its pieces. */
  const productsWith = (pieceCounting: string) => [() => json({
    items: [{ productId: PRODUCT, productVersion: '12', code: 'PRD-12', name: 'Galvaniz bant', stockUnitCode: 'kg',
      pieceCounting }],
    searchWindowFull: false,
  })]

  async function fillOutput(user: ReturnType<typeof userEvent.setup>, recorded: RecordedRequest[], pieces: string) {
    await user.click(screen.getByLabelText('Üretim çıktısı'))
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
    await user.type(screen.getByLabelText('Zaman'), '2026-09-25T00:30')
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))
    await screen.findByRole('option', { name: /PRD-12/ })
    await screen.findByRole('option', { name: /HOL-1/ })
    await user.selectOptions(screen.getByLabelText('Ürün'), PRODUCT)
    await user.selectOptions(screen.getByLabelText('Lokasyon'), LOCATION)
    await user.selectOptions(screen.getByLabelText('İlk stok durumu'), 'Free')
    await user.type(screen.getByLabelText('Kütle (kg)'), '20')
    if (pieces.length > 0) await user.type(screen.getByLabelText('Adet'), pieces)
  }

  it('sends the counted pieces with the weighed kg and names the field by its label', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      products: productsWith('Counted'),
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillOutput(user, recorded, '48')
    expect(screen.getByLabelText('Adet').getAttribute('aria-describedby')).toBe('mfg-output-piece-count-help')
    expect(document.getElementById('mfg-output-piece-count-help')?.textContent).toBe(OUTPUT_PIECE_TEXT.pieceCountHelp)
    expect((screen.getByLabelText('Adet') as HTMLInputElement).required).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(posts(recorded)).toHaveLength(1))
    expect(posts(recorded)[0].body).toMatchObject({ kg: '20', pieceCount: 48 })
  })

  it('shows no piece field and sends no piece count for an uncounted product', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      products: productsWith('NotCounted'),
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillOutput(user, recorded, '')
    expect(screen.queryByLabelText('Adet')).toBeNull()
    expect(screen.queryByTestId('mfg-output-piece-source-missing')).toBeNull()
    expect(screen.queryByTestId('mfg-output-piece-unavailable')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(posts(recorded)).toHaveLength(1))
    expect(posts(recorded)[0].body).not.toHaveProperty('pieceCount')
  })

  it('shows no piece field before a product is selected', async () => {
    const user = userEvent.setup()
    stub({ capabilities: INVENTORY_RECORDER_CAPABILITIES, products: productsWith('Counted') })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await user.click(screen.getByLabelText('Üretim çıktısı'))
    await user.type(screen.getByLabelText('Zaman'), '2026-09-25T00:30')
    await screen.findByRole('option', { name: /PRD-12/ })
    expect(screen.queryByLabelText('Adet')).toBeNull()
    await user.selectOptions(screen.getByLabelText('Ürün'), PRODUCT)
    expect(screen.getByLabelText('Adet')).toBeTruthy()
  })

  it('requires the piece count of a counted product and sends nothing without it', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      products: productsWith('Counted'),
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillOutput(user, recorded, '')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText(OUTPUT_PIECE_TEXT.pieceCountRequired)).toBeTruthy()
    expect(posts(recorded)).toHaveLength(0)
  })

  it('warns and blocks the output of a product whose theoretical kg per piece source is missing', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      products: productsWith('SourceMissing'),
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillOutput(user, recorded, '')
    expect(screen.getByTestId('mfg-output-piece-source-missing').textContent).toBe(OUTPUT_PIECE_TEXT.sourceMissing)
    expect(screen.queryByLabelText('Adet')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText(OUTPUT_PIECE_TEXT.sourceMissingSubmit)).toBeTruthy()
    expect(posts(recorded)).toHaveLength(0)
  })

  it('informs, hides the field and lets the server decide when the piece counting could not be read', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      products: productsWith('Unavailable'),
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillOutput(user, recorded, '')
    const notice = screen.getByTestId('mfg-output-piece-unavailable')
    expect(notice.textContent).toBe(OUTPUT_PIECE_TEXT.unavailable)
    expect(notice.getAttribute('role')).toBe('status')
    expect(screen.queryByLabelText('Adet')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(posts(recorded)).toHaveLength(1))
    expect(posts(recorded)[0].body).not.toHaveProperty('pieceCount')
  })

  it('fails the product selector closed on an unknown piece counting', async () => {
    const user = userEvent.setup()
    stub({ capabilities: INVENTORY_RECORDER_CAPABILITIES, products: productsWith('Sometimes') })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await user.click(screen.getByLabelText('Üretim çıktısı'))
    await user.type(screen.getByLabelText('Zaman'), '2026-09-25T00:30')
    await screen.findByRole('option', { name: /HOL-1/ })
    expect(await screen.findByText('Ürün seçenekleri okunamadı. Tekrar deneyin.')).toBeTruthy()
    expect((screen.getByLabelText('Ürün') as HTMLSelectElement).disabled).toBe(true)
    expect(screen.queryByRole('option', { name: /PRD-12/ })).toBeNull()
    expect(screen.queryByLabelText('Adet')).toBeNull()
  })

  it.each(['0', '-3', '2.5', '01'])('refuses the piece count %s without sending anything', async (pieces) => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      products: productsWith('Counted'),
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillOutput(user, recorded, pieces)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText('Adet, sıfırdan büyük bir tam sayı olmalıdır.')).toBeTruthy()
    expect(posts(recorded)).toHaveLength(0)
  })

  it.each([
    ['manufacturing_output_pieces_required', 'Bu ürün adetle izleniyor'],
    ['manufacturing_output_pieces_not_applicable', 'adet alanını boş bırakın'],
    ['manufacturing_output_piece_kg_source_missing', 'teorik kg/adet hesaplanamıyor'],
  ])('tells the server refusal %s in Turkish, never the code', async (code, fragment) => {
    const user = userEvent.setup()
    const { recorded } = stub({
      capabilities: INVENTORY_RECORDER_CAPABILITIES,
      // The selector's answer can be older than the command's: the server's refusal is told whatever the form showed.
      products: productsWith(code === 'manufacturing_output_pieces_required' ? 'NotCounted' : 'Counted'),
      labels: [() => json(labelDetail('8', '900')), () => json(labelDetail('9', '900'))],
      commands: [() => problem(422, { code })],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await fillOutput(user, recorded, code === 'manufacturing_output_pieces_required' ? '' : '4')
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText(new RegExp(fragment))).toBeTruthy()
    expect(screen.queryByText(new RegExp(code))).toBeNull()
  })

  it('shows the server deviation of each counted output with its sign and no threshold', async () => {
    stub({
      reads: [() => json(order({
        outputPieces: [
          pieceView(),
          pieceView({ outputId: 'e0000000-0000-4000-8000-0000000000a8', outputClass: 'Scrap', netPieceCount: 0,
            netKg: '0', realKgPerPiece: null, deviationKgPerPiece: null, deviationPercent: null }),
          pieceView({ outputId: 'e0000000-0000-4000-8000-0000000000a9', realKgPerPiece: '0.39',
            deviationKgPerPiece: '-0.01', deviationPercent: '-2.5' }),
        ],
      }))],
    })
    renderAt(PATH)

    const table = await screen.findByRole('region', { name: 'Adetli çıktılar: gerçek ve teorik kg/adet' })
    expect(within(table).getByText(/eşik uygulanmaz/)).toBeTruthy()
    const heavier = within(table).getByRole('row', { name: new RegExp(COUNTED_OUTPUT) })
    expect(within(heavier).getByTestId('deviation-kg').textContent).toBe('+0.017')
    expect(within(heavier).getByTestId('deviation-percent').textContent).toBe('+4.17')
    expect(within(heavier).getByText('0.417')).toBeTruthy()
    const empty = within(table).getByRole('row', { name: /0000000000a8/ })
    expect(within(empty).getByTestId('no-net-pieces').textContent).toBe('Net adet kalmadı')
    const lighter = within(table).getByRole('row', { name: /0000000000a9/ })
    expect(within(lighter).getByTestId('deviation-percent').textContent).toBe('-2.5')
  })

  it('draws no piece table for an order without a counted output', async () => {
    stub()
    renderAt(PATH)
    await screen.findByRole('region', { name: 'Ana rulo dengesi (kg)' })
    expect(screen.queryByTestId('output-pieces')).toBeNull()
  })

  it('refuses an order whose piece view is malformed instead of showing invented figures', async () => {
    stub({ reads: [() => json(order({ outputPieces: [pieceView({ deviationThresholdApplied: true })] }))] })
    renderAt(PATH)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.queryByTestId('output-pieces')).toBeNull()
  })
})
