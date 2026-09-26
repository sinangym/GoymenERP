/**
 * The Manufacturing order screen — component/DOM tests through the real shell, gate and
 * fence, over synthetic fixtures. NOT real-API, real-SQL or browser acceptance, and
 * never reported as such (the `mfg` browser suite is a later slice).
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { INVENTORY_STOCK_POSITIONS_CAPABILITY } from '../inventory/capabilities'
import {
  CLOSER_CAPABILITIES,
  COMPANY_A,
  COMPANY_B,
  ORDER,
  READER_CAPABILITIES,
  RECORDER_CAPABILITIES,
  ROLL_1,
  installFetch,
  json,
  massBalance,
  order,
  problem,
  renderAt,
  requestsFor,
  roll,
  rollBalance,
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
      documentStatus: 'Complete',
      requiredDocuments: [],
      documents: [],
      prints: [],
      lot: { id: LOT, heatNumber: null, coilNumber: null },
    },
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
    commands?: Array<() => Response>
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
    items: [{ productId: PRODUCT, productVersion: '12', code: 'PRD-12', name: 'Galvaniz bant', stockUnitCode: 'kg' }],
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
        return respond()
      }
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
  requestsFor(recorded, `/manufacturing/orders/${ORDER}`, 'GET').filter((request) => !request.url.endsWith('/mass-balance'))
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

    await user.selectOptions(screen.getByLabelText('Çıktı sınıfı'), 'GoodProduct')
    await user.selectOptions(screen.getByLabelText('Ürün'), PRODUCT)
    await user.selectOptions(screen.getByLabelText('Lokasyon'), LOCATION)
    await user.type(screen.getByLabelText('Yeni etiket kodu'), 'OUT-40')
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
    const selectedEffectiveOn = new URL(productReads(recorded)[0].url, 'http://local.test')
      .searchParams.get('effectiveOn')
    expect(selectedEffectiveOn).toBe((output.body as { occurredAtUtc: string }).occurredAtUtc.slice(0, 10))
    await waitFor(() => expect(reads(recorded)).toHaveLength(2))
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(2))
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
    await user.type(screen.getByLabelText('Yeni etiket kodu'), 'FASON-OUT-40')
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
    expect(post.body).toEqual({ expectedVersion: 4 })
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
