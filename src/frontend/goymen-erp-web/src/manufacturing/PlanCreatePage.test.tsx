import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MANUFACTURING_CAPABILITY } from './capabilities'
import {
  COMPANY_A,
  ROLL_1,
  ROLL_2,
  WORK_CENTER,
  installFetch,
  json,
  plan,
  renderAt,
  requestsFor,
} from './testHarness'

const PRODUCT = '10000000-0000-4000-8000-000000000001'
const LOT = '20000000-0000-4000-8000-000000000001'
const LOCATION = '40000000-0000-4000-8000-000000000001'

function workCenter() {
  return {
    id: WORK_CENTER,
    locationId: LOCATION,
    code: 'SLT-01',
    name: 'Dilme Hattı',
    kind: 'Slitting',
    isActive: true,
    canOpenLocationDetail: false,
  }
}

function candidate(labelId = ROLL_1, overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY_A,
    labelId,
    labelVersion: labelId === ROLL_1 ? '7' : '12',
    labelCode: labelId === ROLL_1 ? 'LBL-0007' : 'LBL-0012',
    productId: PRODUCT,
    lot: { id: LOT, heatNumber: 'H-12', coilNumber: null },
    heatNumber: 'H-12',
    coilNumber: null,
    documentStatus: 'Complete',
    requiredDocuments: ['MillCertificate'],
    attachedDocuments: ['MillCertificate'],
    physicalKg: '1200.5',
    unblockedPhysicalKg: '1000.5',
    blockedPhysicalKg: '200',
    positions: [{
      ownerKind: 'Company', ownerId: COMPANY_A, custodianKind: 'Company', custodianId: COMPANY_A,
      locationId: LOCATION, productId: PRODUCT, block: 'Free', quantityKg: '1000.5',
    }],
    draftSelectable: true,
    draftRefusalCode: null,
    productionUseRequiresRevalidation: true,
    ...overrides,
  }
}

function canonicalDecimal(value: string): string {
  return value.includes('.') ? value.replace(/0+$/, '').replace(/\.$/, '') : value
}

function installPlanApi(options: { readonly failFirstSave?: boolean } = {}) {
  let saves = 0
  let stored: Record<string, unknown> | null = null
  const recorded = installFetch({
    capabilities: [MANUFACTURING_CAPABILITY.planCreate, MANUFACTURING_CAPABILITY.planDetail],
    respond: (url, init) => {
      if (url.includes('/organization/work-centers')) return json([workCenter()])
      if (url.includes('/inventory/references/parent-rolls')) {
        return json({
          items: [
            candidate(),
            candidate(ROLL_2, {
              draftSelectable: false,
              draftRefusalCode: 'inventory_parent_roll_context_required',
              positions: [{ ...candidate().positions[0], ownerKind: 'Customer' }],
            }),
          ],
          searchWindowFull: false,
        })
      }
      if (url.endsWith('/manufacturing/plans') && init?.method === 'POST') {
        saves += 1
        if (options.failFirstSave && saves === 1) throw new TypeError('connection lost')
        const request = JSON.parse(init.body as string) as {
          planId: string
          workCenterId: string
          inputs: Array<{ parentRollId: string; plannedKg: string }>
          target: { basis: string; kg: string } | null
        }
        stored = plan({
          planId: request.planId,
          workCenterId: request.workCenterId,
          inputs: request.inputs.map(({ parentRollId, plannedKg }) => ({
            parentRollId,
            plannedKg: canonicalDecimal(plannedKg),
          })),
          target: request.target,
        })
        return json(stored, 201, { Location: `/api/v1/manufacturing/plans/${request.planId}` })
      }
      if (url.includes('/manufacturing/plans/') && init?.method !== 'POST' && stored !== null) return json(stored)
      return undefined
    },
  })
  return recorded
}

async function fillPlan(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole('option', { name: 'SLT-01 — Dilme Hattı' })
  await user.selectOptions(screen.getByLabelText('İş merkezi'), WORK_CENTER)
  await user.click(screen.getByRole('button', { name: 'Ara' }))
  const own = await screen.findByRole('row', { name: /LBL-0007/ })
  await user.click(within(own).getByRole('button', { name: 'Ekle' }))
  await user.type(screen.getByLabelText('LBL-0007 planlanan kg'), '900.50')
  await user.selectOptions(screen.getByLabelText('Hedef türü'), 'GoodOutputMass')
  await user.type(screen.getByLabelText('Hedef kg'), '850')
}

afterEach(() => {
  vi.unstubAllGlobals()
  sessionStorage.clear()
})

describe('plan create and reopen', () => {
  it('selects real work-center and Inventory facts, saves frozen label version, then reopens with GET', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi()
    renderAt('/uretim/plan/yeni')
    await fillPlan(user)

    const customer = screen.getByRole('row', { name: /LBL-0012/ })
    expect(within(customer).getByRole('button', { name: 'Seçilemez' }).hasAttribute('disabled')).toBe(true)
    expect(within(customer).getByText('inventory_parent_roll_context_required')).toBeTruthy()
    expect(screen.getByText(/kullanılabilirlik veya rezervasyon garantisi değildir/)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))
    const reopen = await screen.findByRole('link', { name: 'Formu kapat ve planı yeniden aç' })

    const save = requestsFor(recorded, '/manufacturing/plans', 'POST')[0]
    expect(save.headers).toMatchObject({ 'X-Company-Id': COMPANY_A })
    expect(save.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i)
    expect(save.body).toMatchObject({
      workCenterId: WORK_CENTER,
      inputs: [{ parentRollId: ROLL_1, plannedKg: '900.50', expectedLabelVersion: '7' }],
      target: { basis: 'GoodOutputMass', kg: '850' },
    })

    await user.click(reopen)
    expect(await screen.findByRole('heading', { name: 'Plan yeniden açıldı' })).toBeTruthy()
    expect(screen.getByText('900.5')).toBeTruthy()
    expect(requestsFor(recorded, '/manufacturing/plans/', 'GET')).toHaveLength(1)
  })

  it('retries an unknown write with exactly the same request and idempotency key', async () => {
    const user = userEvent.setup()
    const recorded = installPlanApi({ failFirstSave: true })
    renderAt('/uretim/plan/yeni')
    await fillPlan(user)
    await user.click(screen.getByRole('button', { name: 'Taslak planı kaydet' }))

    const retry = await screen.findByRole('button', { name: 'Aynı anahtarla yeniden gönder' })
    await user.click(retry)
    await screen.findByText('Taslak plan kaydedildi')

    const [first, second] = requestsFor(recorded, '/manufacturing/plans', 'POST')
    expect(second.body).toEqual(first.body)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
  })

  it('keeps the create route closed when its server capability is absent', async () => {
    installFetch({ capabilities: [MANUFACTURING_CAPABILITY.planDetail] })
    renderAt('/uretim/plan/yeni')
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByRole('heading', { name: 'Yeni taslak plan' })).toBeNull()
  })
})
