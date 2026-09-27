/**
 * Master edit component/DOM tests — create and edit sites, locations and work centres.
 *
 * Fixtures through the real shell, gate and fence (./testHarness). NOT real-API, real-SQL or browser acceptance,
 * and never reported as such: the persistence, replay and audit facts are the host's SqlServerFact suite.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ORGANIZATION_CAPABILITY } from './capabilities'
import { sendMasterCommand, wireRequest } from './masterDataApi'
import {
  COMPANY_A,
  LOC_H0,
  LOC_H1,
  READ_CAPABILITIES,
  SITE_GAZ,
  SITE_OSM,
  WC_PAINT,
  installFetch,
  json,
  location,
  noContent,
  page,
  problem,
  renderAt,
  requestsFor,
  site,
  workCenter,
  workCenterDetail,
  type RecordedRequest,
} from './testHarness'

const NEW_ID = 'cccccccc-3333-4333-8333-333333333333'

afterEach(() => {
  vi.unstubAllGlobals()
})

function writes(recorded: readonly RecordedRequest[]) {
  return recorded.filter((request) => request.method === 'POST' || request.method === 'PUT')
}

async function fill(label: string, value: string, root: HTMLElement = document.body) {
  const field = within(root).getByLabelText(label)
  await userEvent.clear(field)
  await userEvent.type(field, value)
}

describe('location edit (ORG-02)', () => {
  const PATH = `/ayarlar/organizasyon/lokasyon/${LOC_H0}`

  function stub(options: { command?: Array<() => Response>; capabilities?: readonly string[] } = {}) {
    const details = [
      location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0'),
      location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0 Boya', { version: 2 }),
    ]
    let reads = 0
    let commands = 0
    const command = options.command ?? [() => noContent()]
    const recorded = installFetch({
      capabilities: options.capabilities ?? [...READ_CAPABILITIES, ORGANIZATION_CAPABILITY.locationEdit],
      respond: (url, init) => {
        if (init?.method === 'PUT' && url.endsWith(`/organization/locations/${LOC_H0}`)) {
          const respond = command[Math.min(commands, command.length - 1)]
          commands += 1
          return respond()
        }
        if (url.includes(`/organization/locations/${LOC_H0}`)) {
          const body = details[Math.min(reads, details.length - 1)]
          // The second read only after a command was accepted: the card never predicts the saved state.
          if (commands > 0) reads = 1
          return json(body)
        }
        return undefined
      },
    })
    return recorded
  }

  it('offers no edit surface without the edit control', async () => {
    stub({ capabilities: READ_CAPABILITIES })
    renderAt(PATH)
    await screen.findByText('OSM-H0')
    expect(screen.queryByRole('button', { name: 'Lokasyonu düzenle' })).toBeNull()
  })

  it('saves code and name with the read version and a key, then shows the re-read card', async () => {
    const recorded = stub()
    renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Lokasyonu düzenle' }))
    await fill('Ad', 'Hol-0 Boya')
    await fill('Gerekçe', 'Ad netleştirildi')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText('Değişiklik kaydedildi')).toBeTruthy()
    expect(await screen.findByText('Hol-0 Boya')).toBeTruthy()
    const [put] = writes(recorded)
    expect(put.method).toBe('PUT')
    expect(put.body).toEqual({ code: 'OSM-H0', name: 'Hol-0 Boya', expectedVersion: '1', reason: 'Ad netleştirildi' })
    expect(put.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(put.headers['Idempotency-Key']).toMatch(/.+/)
  })

  it('refuses an empty reason without minting a request', async () => {
    const recorded = stub()
    renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Lokasyonu düzenle' }))
    await fill('Ad', 'Hol-0 Boya')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText(/Gerekçe zorunludur/)).toBeTruthy()
    expect(writes(recorded)).toHaveLength(0)
  })

  it('shows a stale version as its own refusal and keeps what was typed', async () => {
    stub({ command: [() => problem(409, { conflict: { expectedVersion: 1, currentVersion: 3 } })] })
    renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Lokasyonu düzenle' }))
    await fill('Ad', 'Hol-0 Boya')
    await fill('Gerekçe', 'Ad netleştirildi')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    const notice = await screen.findByRole('alert')
    expect(within(notice).getByText('Kayıt bu sırada değişti')).toBeTruthy()
    expect(within(notice).getByText('Beklediğiniz sürüm 1, kaydın güncel sürümü 3.')).toBeTruthy()
    expect((screen.getByLabelText('Ad') as HTMLInputElement).value).toBe('Hol-0 Boya')
  })

  it('keeps the key and freezes the fields when the outcome is unknown, and resends with the same key', async () => {
    const recorded = stub({ command: [() => problem(503), () => noContent({ 'Idempotency-Replayed': 'true' })] })
    renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Lokasyonu düzenle' }))
    await fill('Ad', 'Hol-0 Boya')
    await fill('Gerekçe', 'Ad netleştirildi')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    await screen.findByText(/sonuç bilinmiyor/)
    expect((screen.getByLabelText('Ad') as HTMLInputElement).readOnly).toBe(true)
    expect(screen.queryByRole('button', { name: 'Kaydet' })).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Aynı işlem anahtarıyla tekrar gönder' }))

    expect(await screen.findByText('Aynı işlem zaten kaydedilmişti')).toBeTruthy()
    const [first, second] = writes(recorded)
    expect(second.headers['Idempotency-Key']).toBe(first.headers['Idempotency-Key'])
    expect(second.body).toEqual(first.body)
  })

  it('marks a used code on the field, and the next attempt is a new key', async () => {
    const recorded = stub({
      command: [() => problem(422, { code: 'organization_code_in_use' }), () => noContent()],
    })
    renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Lokasyonu düzenle' }))
    await fill('Kod', 'OSM-H1')
    await fill('Gerekçe', 'Kod değişti')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('Kod kullanımda')).toBeTruthy()

    await fill('Kod', 'OSM-H0B')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    await screen.findByText('Değişiklik kaydedildi')
    const [first, second] = writes(recorded)
    expect(second.headers['Idempotency-Key']).not.toBe(first.headers['Idempotency-Key'])
    expect((second.body as { code: string }).code).toBe('OSM-H0B')
  })
})

describe('site and location create (ORG-01)', () => {
  const PATH = '/ayarlar/organizasyon'
  const MANAGE = [
    ...READ_CAPABILITIES,
    ORGANIZATION_CAPABILITY.sitesManage,
    ORGANIZATION_CAPABILITY.locationCreate,
  ]

  function stub(options: { sites?: unknown[]; capabilities?: readonly string[] } = {}) {
    const sites = options.sites ?? [
      { ...site(SITE_OSM, 'OSM', 'Osmaniye Yerleşkesi'), version: 1 },
      { ...site(SITE_GAZ, 'GAZ', 'Gaziantep Depo', 'ExternalWarehouse'), version: 1 },
    ]
    return installFetch({
      capabilities: options.capabilities ?? MANAGE,
      respond: (url, init) => {
        if (init?.method === 'POST' && url.endsWith('/organization/locations')) {
          return json({ id: NEW_ID, version: 1 }, 201)
        }
        if (init?.method === 'POST' && url.endsWith('/organization/sites')) {
          return json({ id: NEW_ID, version: 1 }, 201)
        }
        if (init?.method === 'PUT' && url.endsWith(`/organization/sites/${SITE_OSM}`)) return noContent()
        if (url.endsWith('/organization/sites')) return json(sites)
        if (url.includes(`/organization/locations/${NEW_ID}`)) {
          return json(location(NEW_ID, SITE_OSM, 'OSM-H9', 'Hol-9'))
        }
        if (url.includes('/organization/locations')) {
          return json(page([location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0')]))
        }
        return undefined
      },
    })
  }

  it('offers no create or edit surface without the manage controls', async () => {
    stub({ capabilities: READ_CAPABILITIES })
    renderAt(PATH)
    await screen.findByText('OSM-H0')
    expect(screen.queryByRole('button', { name: 'Yeni tesis' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Yeni lokasyon' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Tesisi düzenle/ })).toBeNull()
  })

  it('creates a location under a listed site and opens the new record’s card', async () => {
    const recorded = stub()
    const { router } = renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Yeni lokasyon' }))
    await fill('Kod', 'OSM-H9')
    await fill('Ad', 'Hol-9')
    await userEvent.selectOptions(screen.getByLabelText('Tesis'), SITE_OSM)
    await fill('Gerekçe', 'Yeni hol')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() => expect(router.state.location.pathname).toBe(`/ayarlar/organizasyon/lokasyon/${NEW_ID}`))
    expect(await screen.findByText('Hol-9')).toBeTruthy()
    const [post] = writes(recorded)
    expect(post.body).toEqual({ siteId: SITE_OSM, code: 'OSM-H9', name: 'Hol-9', reason: 'Yeni hol' })
  })

  it('creates a site with its type and re-reads the site list', async () => {
    const recorded = stub()
    renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Yeni tesis' }))
    const before = requestsFor(recorded, '/organization/sites').filter((request) => request.method === 'GET').length
    await fill('Kod', 'MER')
    await fill('Ad', 'Mersin Depo')
    await userEvent.selectOptions(screen.getByLabelText('Tesis türü'), 'Warehouse')
    await fill('Gerekçe', 'Yeni depo')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    expect(await screen.findByText('Kayıt oluşturuldu')).toBeTruthy()
    const [post] = writes(recorded)
    expect(post.body).toEqual({ code: 'MER', name: 'Mersin Depo', type: 'Warehouse', reason: 'Yeni depo' })
    await waitFor(() =>
      expect(requestsFor(recorded, '/organization/sites').filter((request) => request.method === 'GET').length)
        .toBeGreaterThan(before),
    )
  })

  it('edits a site with the version the site read published', async () => {
    const recorded = stub()
    renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Tesisi düzenle: OSM' }))
    await fill('Ad', 'Osmaniye Fabrika')
    await fill('Gerekçe', 'Ad değişti')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('Değişiklik kaydedildi')).toBeTruthy()
    const [put] = writes(recorded)
    expect(put.url).toContain(`/organization/sites/${SITE_OSM}`)
    expect(put.body).toEqual({ code: 'OSM', name: 'Osmaniye Fabrika', expectedVersion: '1', reason: 'Ad değişti' })
  })

  it('offers no site edit when the site read carries no version', async () => {
    stub({ sites: [site(SITE_OSM, 'OSM', 'Osmaniye Yerleşkesi')] })
    renderAt(PATH)
    await screen.findByText('Osmaniye Yerleşkesi')
    expect(screen.queryByRole('button', { name: 'Tesisi düzenle: OSM' })).toBeNull()
  })
})

describe('work centre create and edit (ORG-03 / ORG-04)', () => {
  const MANAGE = [
    ORGANIZATION_CAPABILITY.workCentersList,
    ORGANIZATION_CAPABILITY.workCenterDetail,
    ORGANIZATION_CAPABILITY.workCenterCreate,
    ORGANIZATION_CAPABILITY.workCenterEdit,
  ]

  function stub() {
    let edited = false
    return installFetch({
      capabilities: MANAGE,
      respond: (url, init) => {
        if (init?.method === 'POST' && url.endsWith('/organization/work-centers')) {
          return json({ id: NEW_ID, version: 1 }, 201)
        }
        if (init?.method === 'PUT' && url.endsWith(`/organization/work-centers/${WC_PAINT}`)) {
          edited = true
          return noContent()
        }
        if (url.includes(`/organization/work-centers/${NEW_ID}`)) {
          return json(workCenterDetail(NEW_ID, LOC_H1, 'WC-9', 'Dilme 9', { kind: 'Slitting' }))
        }
        if (url.includes(`/organization/work-centers/${WC_PAINT}`)) {
          return json(
            workCenterDetail(WC_PAINT, LOC_H0, 'WC-PAINT', edited ? 'Boya Hattı 2' : 'Boya Hattı', {
              version: edited ? 2 : 1,
            }),
          )
        }
        if (url.endsWith('/organization/work-centers')) {
          return json([workCenter(WC_PAINT, LOC_H0, 'WC-PAINT', 'Boya Hattı')])
        }
        if (url.includes('/organization/locations?')) {
          return json(
            page(
              [
                location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0'),
                location(LOC_H1, SITE_OSM, 'OSM-H1', 'Hol-1', { isActive: false }),
              ],
              { pageSize: 100 },
            ),
          )
        }
        return undefined
      },
    })
  }

  it('creates a work centre on a picked location with a kind and opens its card', async () => {
    const recorded = stub()
    const { router } = renderAt('/ayarlar/organizasyon/is-merkezleri')
    await userEvent.click(await screen.findByRole('button', { name: 'Yeni iş merkezi' }))
    await screen.findByLabelText('Lokasyon')
    expect(requestsFor(recorded, '/organization/locations?page=1&pageSize=100')).toHaveLength(1)
    await fill('Kod', 'WC-9')
    await fill('Ad', 'Dilme 9')
    await userEvent.selectOptions(screen.getByLabelText('Lokasyon'), LOC_H1)
    await userEvent.selectOptions(screen.getByLabelText('İş merkezi türü'), 'Slitting')
    await fill('Gerekçe', 'Yeni hat')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))

    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/ayarlar/organizasyon/is-merkezleri/${NEW_ID}`),
    )
    expect(await screen.findByText('Dilme 9')).toBeTruthy()
    const [post] = writes(recorded)
    expect(post.body).toEqual({ locationId: LOC_H1, code: 'WC-9', name: 'Dilme 9', kind: 'Slitting', reason: 'Yeni hat' })
  })

  it('requires a kind before minting a request', async () => {
    const recorded = stub()
    renderAt('/ayarlar/organizasyon/is-merkezleri')
    await userEvent.click(await screen.findByRole('button', { name: 'Yeni iş merkezi' }))
    await screen.findByLabelText('Lokasyon')
    await fill('Kod', 'WC-9')
    await fill('Ad', 'Dilme 9')
    await userEvent.selectOptions(screen.getByLabelText('Lokasyon'), LOC_H0)
    await fill('Gerekçe', 'Yeni hat')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('Tür seçilmelidir.')).toBeTruthy()
    expect(writes(recorded)).toHaveLength(0)
  })

  it('edits a work centre’s name on its card and shows the re-read record', async () => {
    const recorded = stub()
    renderAt(`/ayarlar/organizasyon/is-merkezleri/${WC_PAINT}`)
    await userEvent.click(await screen.findByRole('button', { name: 'İş merkezini düzenle' }))
    await fill('Ad', 'Boya Hattı 2')
    await fill('Gerekçe', 'Ad değişti')
    await userEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('Değişiklik kaydedildi')).toBeTruthy()
    expect(await screen.findByText('Boya Hattı 2')).toBeTruthy()
    const [put] = writes(recorded)
    expect(put.body).toEqual({ code: 'WC-PAINT', name: 'Boya Hattı 2', expectedVersion: '1', reason: 'Ad değişti' })
  })
})

describe('master command transport', () => {
  const COMMAND = { kind: 'create-site', code: ' MER ', name: ' Mersin ', type: 'Warehouse', reason: ' r ' } as const

  function answer(response: Response) {
    vi.stubGlobal('fetch', vi.fn(async () => response))
  }

  it('sends the trimmed canonical body', () => {
    expect(wireRequest(COMMAND)).toEqual({
      method: 'POST',
      path: '/organization/sites',
      body: { code: 'MER', name: 'Mersin', type: 'Warehouse', reason: 'r' },
      success: 201,
    })
  })

  it('reads a replayed 201 as the original record', async () => {
    answer(json({ id: NEW_ID, version: 1 }, 201, { 'Idempotency-Replayed': 'true' }))
    expect(await sendMasterCommand(COMPANY_A, 'k', COMMAND)).toEqual({
      type: 'created',
      record: { id: NEW_ID, version: 1 },
      replayed: true,
    })
  })

  it('treats a 201 it cannot read as unknown, never as a refusal', async () => {
    answer(json({ id: 'not-a-uuid' }, 201))
    expect((await sendMasterCommand(COMPANY_A, 'k', COMMAND)).type).toBe('unknown')
  })

  it('does not guess "code in use" from a 422 without the code', async () => {
    answer(problem(422))
    expect((await sendMasterCommand(COMPANY_A, 'k', COMMAND)).type).toBe('unknown')
  })

  it('tells an idempotency conflict from a stale version by the conflict object', async () => {
    answer(problem(409))
    expect(await sendMasterCommand(COMPANY_A, 'k', COMMAND)).toEqual({
      type: 'failed',
      failure: { kind: 'idempotency-conflict' },
      replayed: false,
    })
  })

  it('treats a 204 on a create as off-contract', async () => {
    answer(noContent())
    expect(await sendMasterCommand(COMPANY_A, 'k', COMMAND)).toEqual({
      type: 'unknown',
      unknown: { kind: 'unexpected-success', status: 204 },
    })
  })
})
