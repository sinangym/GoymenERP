/**
 * The Manufacturing order screen — component/DOM tests through the real shell, gate and
 * fence, over synthetic fixtures. NOT real-API, real-SQL or browser acceptance, and
 * never reported as such (the `mfg` browser suite is a later slice).
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  CLOSER_CAPABILITIES,
  COMPANY_A,
  COMPANY_B,
  ORDER,
  READER_CAPABILITIES,
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
const LOT = 'e0000000-0000-4000-8000-000000000011'
const LOCATION = 'e0000000-0000-4000-8000-000000000012'

function labelDetail(
  labelVersion = '7',
  physicalKg = '1000',
  ownerId = COMPANY_A,
  custodianId = COMPANY_A,
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
        ownerKind: 'Company',
        ownerId,
        custodianKind: 'Company',
        custodianId,
        locationId: LOCATION,
        productId: PRODUCT,
        block: 'Free',
        quantityKg: physicalKg,
      }],
    },
    genealogy: { labelId: ROLL_1, labelCode: 'SYN-MFG-ROLL-1', edges: [], windowFull: false },
  }
}

interface Scenario {
  readonly recorded: RecordedRequest[]
}

function stub(
  options: {
    reads?: Array<() => Response>
    commands?: Array<() => Response>
    capabilities?: readonly string[]
    /** GDC-BAL/1 reads (BAL slice 2), answered on their own so they never consume an order read. */
    balances?: Array<() => Response>
    labels?: Array<() => Response>
  } = {},
): Scenario {
  const reads = options.reads ?? [() => json(order())]
  const commands = options.commands ?? [() => json(order({ version: 2 }))]
  const balances = options.balances ?? [() => json(massBalance())]
  const labels = options.labels ?? [() => json(labelDetail())]
  let readIndex = 0
  let commandIndex = 0
  let balanceIndex = 0
  let labelIndex = 0
  const recorded = installFetch({
    capabilities: options.capabilities,
    respond: (url, init) => {
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
  if (values.kind !== 'WIP ölçümü') await screen.findByTestId('parent-label-state')
  const kg = screen.getByLabelText('Kütle (kg)')
  await user.clear(kg)
  await user.type(kg, values.kg ?? '250.5')
  const time = screen.getByLabelText('Zaman')
  await user.clear(time)
  await user.type(time, '2026-09-18T09:30')
}

const posts = (recorded: RecordedRequest[]) => requestsFor(recorded, '/manufacturing/orders/', 'POST')
const reads = (recorded: RecordedRequest[]) =>
  requestsFor(recorded, `/manufacturing/orders/${ORDER}`, 'GET').filter((request) => !request.url.endsWith('/mass-balance'))
const balanceReads = (recorded: RecordedRequest[]) => requestsFor(recorded, `/manufacturing/orders/${ORDER}/mass-balance`, 'GET')
const labelReads = (recorded: RecordedRequest[]) => requestsFor(recorded, `/inventory/labels/${ROLL_1}/detail`, 'GET')

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
    expect(screen.queryByLabelText('Çıktı bildirimi')).toBeNull()
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
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(2))
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
        () => json(labelDetail('9', '800')),
      ],
      commands: [() => json(order({ version: 2 })), () => json(order({ version: 3 }))],
    })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })

    await fillFact(user)
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await waitFor(() => expect(reads(recorded)).toHaveLength(2))
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(2))
    await waitFor(() => expect(within(screen.getByTestId('parent-label-state')).getAllByText('750 kg')).toHaveLength(2))

    await fillFact(user, { kind: 'Kullanılmayan girdi iadesi', kg: '50' })
    await user.click(screen.getByRole('button', { name: 'Kaydet' }))
    await waitFor(() => expect(posts(recorded)).toHaveLength(2))
    await waitFor(() => expect(reads(recorded)).toHaveLength(3))
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(3))
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

  it('keeps customer/fason ownership fail-closed and sends no input command', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({ labels: [() => json(labelDetail('7', '1000', COMPANY_B, COMPANY_B))] })
    renderAt(PATH)
    await screen.findByRole('heading', { name: 'Gerçekleşme kaydı' })
    await user.selectOptions(screen.getByLabelText('Ana rulo'), ROLL_1)
    await waitFor(() => expect(labelReads(recorded)).toHaveLength(1))

    expect(await screen.findByText(/yalnız şirketin kendi malı/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Kaydet' }) as HTMLButtonElement).disabled).toBe(true)
    expect(posts(recorded)).toHaveLength(0)
  })

  it('refreshes both owner reads after a parent-label version conflict and keeps the typed return', async () => {
    const user = userEvent.setup()
    const { recorded } = stub({
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
