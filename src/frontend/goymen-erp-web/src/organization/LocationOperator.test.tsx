/**
 * O02 component/DOM cases — the location operator on the location card.
 *
 * Fixtures through the real shell, gate and fence. NOT real-API, real-SQL or browser acceptance. The two reads are
 * PR #795's (not on main yet); the write is main's `PUT .../operator`. What these cases establish: who sees what,
 * what is and is not sent (one key per snapshot, the read version, a trimmed reason, no page size), and that every
 * outcome of the write has its own state.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ORGANIZATION_CAPABILITY } from './capabilities'
import { decodeLocationOperator, decodeOperatorCandidatePage } from './operatorContract'
import {
  LOC_H0,
  READ_CAPABILITIES,
  SITE_OSM,
  installFetch,
  json,
  location,
  noContent,
  problem,
  renderAt,
  requestsFor,
} from './testHarness'

afterEach(() => {
  vi.unstubAllGlobals()
})

const PATH = `/ayarlar/organizasyon/lokasyon/${LOC_H0}`
const EDIT_CAPABILITIES = [...READ_CAPABILITIES, ORGANIZATION_CAPABILITY.locationEdit]
const PARTY_A = 'c0000000-0000-4000-8000-00000000000a'
const PARTY_B = 'c0000000-0000-4000-8000-00000000000b'
const REASON = 'Fason depo sözleşmesi yenilendi'

function display(partyId: string | null, overrides: Record<string, unknown> = {}) {
  return {
    locationId: LOC_H0,
    operator:
      partyId === null
        ? null
        : { partyId, code: 'TED-001', legalName: 'Anadolu Lojistik A.Ş.', tradeName: null, isCurrentlyEligible: true, ...overrides },
  }
}

function candidate(partyId: string, code: string, legalName: string, tradeName: string | null = null) {
  return { partyId, code, legalName, tradeName }
}

interface Options {
  readonly external?: boolean
  readonly capabilities?: readonly string[]
  readonly operator?: Array<() => Response>
  readonly candidates?: (url: URL) => Response
  readonly command?: Array<() => Response>
  readonly versions?: readonly number[]
}

function stub(options: Options = {}) {
  const operator = options.operator ?? [() => json(display(PARTY_A))]
  const command = options.command ?? [() => noContent()]
  const versions = options.versions ?? [3]
  let operatorIndex = 0
  let commandIndex = 0
  let detailIndex = 0
  return installFetch({
    capabilities: options.capabilities ?? EDIT_CAPABILITIES,
    respond: (raw, init) => {
      const url = new URL(raw, 'http://localhost')
      if (url.pathname.endsWith('/operator-candidates')) {
        return (
          options.candidates?.(url) ??
          json({
            items: [candidate(PARTY_A, 'TED-001', 'Anadolu Lojistik A.Ş.'), candidate(PARTY_B, 'TED-002', 'Ege Depoculuk Ltd.', 'Ege Depo')],
            nextCursor: null,
            hasMore: false,
          })
        )
      }
      if (url.pathname.endsWith('/operator')) {
        if (init?.method === 'PUT') {
          const respond = command[Math.min(commandIndex, command.length - 1)]
          commandIndex += 1
          return respond()
        }
        const respond = operator[Math.min(operatorIndex, operator.length - 1)]
        operatorIndex += 1
        return respond()
      }
      if (url.pathname.endsWith(`/organization/locations/${LOC_H0}`)) {
        const version = versions[Math.min(detailIndex, versions.length - 1)]
        detailIndex += 1
        return json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Fason Depo', { isExternal: options.external ?? true, version }))
      }
      return undefined
    },
  })
}

const section = () => screen.findByRole('region', { name: 'İşletici' })

async function openForm(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await within(await section()).findByRole('button', { name: 'İşleticiyi değiştir' }))
  return screen.findByRole('group', { name: 'Aday cariler' })
}

describe('O02 operator — display', () => {
  it('an internal location has no operator section and sends no operator read', async () => {
    const recorded = stub({ external: false })
    renderAt(PATH)
    await screen.findByText('Fason Depo')
    expect(screen.queryByRole('region', { name: 'İşletici' })).toBeNull()
    expect(requestsFor(recorded, '/operator')).toHaveLength(0)
  })

  it('a reader sees the operator and no change control', async () => {
    const recorded = stub({ capabilities: READ_CAPABILITIES })
    renderAt(PATH)
    const region = await section()
    await waitFor(() => expect(within(region).getByText('Anadolu Lojistik A.Ş.')).toBeTruthy())
    expect(within(region).getByText('TED-001')).toBeTruthy()
    expect(within(region).queryByRole('button')).toBeNull()
    expect(requestsFor(recorded, '/operator-candidates')).toHaveLength(0)
    expect(requestsFor(recorded, '/operator').every((request) => request.method === 'GET')).toBe(true)
  })

  it('shows "no operator" and offers assignment when none is set', async () => {
    stub({ operator: [() => json(display(null))] })
    renderAt(PATH)
    const region = await section()
    expect(await within(region).findByText('Atanmış işletici yok.')).toBeTruthy()
    expect(within(region).getByRole('button', { name: 'İşletici ata' })).toBeTruthy()
  })

  it('an operator that is no longer eligible is shown with that fact, never hidden', async () => {
    stub({ operator: [() => json(display(PARTY_A, { isCurrentlyEligible: false, tradeName: 'Anadolu' }))] })
    renderAt(PATH)
    const region = await section()
    expect(await within(region).findByText('Şu an işletici olarak uygun değil')).toBeTruthy()
    expect(within(region).getByText('Anadolu')).toBeTruthy()
  })

  it('a malformed display fails closed and a 503 is the unavailable state', async () => {
    stub({ operator: [() => json({ ...display(PARTY_A), extra: 1 })] })
    renderAt(PATH)
    await waitFor(async () =>
      expect((await section()).querySelector('[data-state="malformed-response"]')).not.toBeNull(),
    )
  })

  it('a 503 display is the unavailable state with retry', async () => {
    stub({ operator: [() => problem(503), () => json(display(PARTY_A))] })
    const user = userEvent.setup()
    renderAt(PATH)
    const region = await section()
    await waitFor(() => expect(region.querySelector('[data-state="service-unavailable"]')).not.toBeNull())
    await user.click(within(region).getByRole('button', { name: 'Tekrar dene' }))
    expect(await within(region).findByText('Anadolu Lojistik A.Ş.')).toBeTruthy()
  })
})

describe('O02 operator — assignment', () => {
  it('assigns with one key, the read version and a trimmed reason, then re-reads card and operator', async () => {
    const recorded = stub({
      operator: [() => json(display(PARTY_A)), () => json(display(PARTY_B, { code: 'TED-002', legalName: 'Ege Depoculuk Ltd.' }))],
      versions: [3, 4],
    })
    const user = userEvent.setup()
    renderAt(PATH)
    const group = await openForm(user)

    expect(within(group).getByRole('radio', { name: /TED-001/ }).closest('label')?.textContent).toContain('(mevcut işletici)')
    await user.click(within(group).getByRole('radio', { name: /Ege Depoculuk Ltd\. \(Ege Depo\)/ }))
    await user.type(screen.getByLabelText('Gerekçe'), `  ${REASON}  `)
    await user.click(screen.getByRole('button', { name: 'İşleticiyi kaydet' }))

    const region = await section()
    await waitFor(() => expect(within(region).getByText('Ege Depoculuk Ltd.')).toBeTruthy())
    expect(region.querySelector('[data-outcome="saved"]')).not.toBeNull()

    const puts = requestsFor(recorded, '/operator').filter((request) => request.method === 'PUT')
    expect(puts).toHaveLength(1)
    expect(puts[0].body).toEqual({ operatorPartyId: PARTY_B, expectedVersion: '3', reason: REASON })
    expect(puts[0].headers['Idempotency-Key']).toMatch(/.+/)
    // No page size of its own is sent to the candidate search.
    const searches = requestsFor(recorded, '/operator-candidates')
    expect(searches.every((request) => !new URL(request.url, 'http://localhost').searchParams.has('pageSize'))).toBe(true)
  })

  it('refuses to submit without a pick or a reason and sends nothing', async () => {
    const recorded = stub()
    const user = userEvent.setup()
    renderAt(PATH)
    await openForm(user)
    await user.click(screen.getByRole('button', { name: 'İşleticiyi kaydet' }))
    expect(await screen.findByText('Bir cari seçin.')).toBeTruthy()
    expect(screen.getByLabelText('Gerekçe').getAttribute('aria-invalid')).toBe('true')
    expect(requestsFor(recorded, '/operator').filter((request) => request.method === 'PUT')).toHaveLength(0)
  })

  it('searches with the trimmed text and pages with the server cursor', async () => {
    const recorded = stub({
      candidates: (url) =>
        url.searchParams.get('cursor') === 'c-2'
          ? json({ items: [candidate(PARTY_B, 'TED-002', 'Ege Depoculuk Ltd.')], nextCursor: null, hasMore: false })
          : json({ items: [candidate(PARTY_A, 'TED-001', 'Anadolu Lojistik A.Ş.')], nextCursor: 'c-2', hasMore: true }),
    })
    const user = userEvent.setup()
    renderAt(PATH)
    await openForm(user)
    await user.type(screen.getByLabelText('Cari ara'), '  ege  ')
    await user.click(screen.getByRole('button', { name: 'Ara' }))
    await waitFor(() =>
      expect(new URL(requestsFor(recorded, '/operator-candidates').at(-1)!.url, 'http://localhost').searchParams.get('q')).toBe('ege'),
    )
    await user.click(await screen.findByRole('button', { name: 'Sonraki adaylar' }))
    expect(await screen.findByRole('radio', { name: /Ege Depoculuk/ })).toBeTruthy()
    const last = new URL(requestsFor(recorded, '/operator-candidates').at(-1)!.url, 'http://localhost').searchParams
    expect(last.get('cursor')).toBe('c-2')
    expect(last.get('q')).toBe('ege')
  })

  it('an empty search result says so', async () => {
    stub({ candidates: () => json({ items: [], nextCursor: null, hasMore: false }) })
    const user = userEvent.setup()
    renderAt(PATH)
    await user.click(await within(await section()).findByRole('button', { name: 'İşleticiyi değiştir' }))
    expect(await screen.findByText('Bu aramayla işletici olabilecek cari bulunamadı.')).toBeTruthy()
  })

  it('an invalid cursor offers the first page', async () => {
    stub({
      candidates: (url) =>
        url.searchParams.has('cursor')
          ? problem(400, { code: 'invalid_cursor' })
          : json({ items: [candidate(PARTY_A, 'TED-001', 'Anadolu Lojistik A.Ş.')], nextCursor: 'c-2', hasMore: true }),
    })
    const user = userEvent.setup()
    renderAt(PATH)
    await openForm(user)
    await user.click(screen.getByRole('button', { name: 'Sonraki adaylar' }))
    expect(await screen.findByText(/sayfa bilgisi artık geçerli değil/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'İlk adaylara dön' }))
    expect(await screen.findByRole('group', { name: 'Aday cariler' })).toBeTruthy()
  })

  it('422 ineligible is a definite refusal; the next submit is a new command with a new key', async () => {
    const recorded = stub({ command: [() => problem(422, { code: 'organization_location_operator_ineligible' }), () => noContent()] })
    const user = userEvent.setup()
    renderAt(PATH)
    const group = await openForm(user)
    await user.click(within(group).getByRole('radio', { name: /Ege Depoculuk/ }))
    await user.type(screen.getByLabelText('Gerekçe'), REASON)
    await user.click(screen.getByRole('button', { name: 'İşleticiyi kaydet' }))
    expect(await screen.findByText('Bu cari işletici olarak atanamıyor')).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'İşleticiyi kaydet' }))
    await waitFor(() => expect(requestsFor(recorded, '/operator').filter((r) => r.method === 'PUT')).toHaveLength(2))
    const [first, second] = requestsFor(recorded, '/operator').filter((r) => r.method === 'PUT')
    expect(first.headers['Idempotency-Key']).not.toBe(second.headers['Idempotency-Key'])
  })

  it('an unknown outcome freezes the choice and resends with the SAME key', async () => {
    const recorded = stub({ command: [() => problem(502), () => noContent()] })
    const user = userEvent.setup()
    renderAt(PATH)
    const group = await openForm(user)
    await user.click(within(group).getByRole('radio', { name: /Ege Depoculuk/ }))
    await user.type(screen.getByLabelText('Gerekçe'), REASON)
    await user.click(screen.getByRole('button', { name: 'İşleticiyi kaydet' }))

    expect(await screen.findByText(/sonuç bilinmiyor/)).toBeTruthy()
    expect((screen.getByLabelText('Gerekçe') as HTMLTextAreaElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Aynı işlemle tekrar gönder' }))
    await waitFor(async () => expect((await section()).querySelector('[data-outcome="saved"]')).not.toBeNull())
    const puts = requestsFor(recorded, '/operator').filter((r) => r.method === 'PUT')
    expect(puts).toHaveLength(2)
    expect(puts[1].headers['Idempotency-Key']).toBe(puts[0].headers['Idempotency-Key'])
    expect(puts[1].body).toEqual(puts[0].body)
  })

  it('the coded Cari-check 503 recorded nothing and keeps the key for the resend', async () => {
    const recorded = stub({
      command: [() => problem(503, { code: 'organization_location_operator_dependency_unavailable' }), () => noContent()],
    })
    const user = userEvent.setup()
    renderAt(PATH)
    const group = await openForm(user)
    await user.click(within(group).getByRole('radio', { name: /Ege Depoculuk/ }))
    await user.type(screen.getByLabelText('Gerekçe'), REASON)
    await user.click(screen.getByRole('button', { name: 'İşleticiyi kaydet' }))
    expect(await screen.findByText('Cari doğrulaması şu an yapılamıyor')).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Aynı işlemle tekrar gönder' }))
    await waitFor(() => expect(requestsFor(recorded, '/operator').filter((r) => r.method === 'PUT')).toHaveLength(2))
    const puts = requestsFor(recorded, '/operator').filter((r) => r.method === 'PUT')
    expect(puts[1].headers['Idempotency-Key']).toBe(puts[0].headers['Idempotency-Key'])
  })

  it('a version conflict offers the fresh card and the next submit carries the new version', async () => {
    const recorded = stub({
      command: [() => problem(409, { conflict: { expectedVersion: 3, currentVersion: 5 } }), () => noContent()],
      versions: [3, 5],
    })
    const user = userEvent.setup()
    renderAt(PATH)
    const group = await openForm(user)
    await user.click(within(group).getByRole('radio', { name: /Ege Depoculuk/ }))
    await user.type(screen.getByLabelText('Gerekçe'), REASON)
    await user.click(screen.getByRole('button', { name: 'İşleticiyi kaydet' }))
    expect(await screen.findByText('Kayıt bu sırada değişti')).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Güncel kaydı yükle' }))
    await waitFor(() => expect(screen.queryByText('Kayıt bu sırada değişti')).toBeNull())
    await user.click(screen.getByRole('button', { name: 'İşleticiyi kaydet' }))
    await waitFor(() => expect(requestsFor(recorded, '/operator').filter((r) => r.method === 'PUT')).toHaveLength(2))
    const second = requestsFor(recorded, '/operator').filter((r) => r.method === 'PUT')[1]
    expect((second.body as Record<string, unknown>).expectedVersion).toBe('5')
  })
})

describe('O02 operator — decoders', () => {
  it('rejects a display for another location, an extra key and a non-boolean eligibility', () => {
    expect(decodeLocationOperator(display(PARTY_A), LOC_H0)).not.toBeNull()
    expect(decodeLocationOperator({ ...display(PARTY_A), locationId: PARTY_B }, LOC_H0)).toBeNull()
    expect(decodeLocationOperator({ ...display(PARTY_A), extra: 1 }, LOC_H0)).toBeNull()
    expect(decodeLocationOperator(display(PARTY_A, { isCurrentlyEligible: 'yes' }), LOC_H0)).toBeNull()
  })

  it('rejects a candidate page whose cursor and hasMore disagree, duplicates or overflow', () => {
    const one = candidate(PARTY_A, 'TED-001', 'A')
    expect(decodeOperatorCandidatePage({ items: [one], nextCursor: null, hasMore: false })).not.toBeNull()
    expect(decodeOperatorCandidatePage({ items: [one], nextCursor: 'c', hasMore: false })).toBeNull()
    expect(decodeOperatorCandidatePage({ items: [one], nextCursor: null, hasMore: true })).toBeNull()
    expect(decodeOperatorCandidatePage({ items: [one, { ...one }], nextCursor: null, hasMore: false })).toBeNull()
    expect(decodeOperatorCandidatePage({ items: Array.from({ length: 26 }, (_, i) => candidate(`c0000000-0000-4000-8000-${String(i).padStart(12, '0')}`, `T${i}`, 'X')), nextCursor: null, hasMore: false })).toBeNull()
    expect(decodeOperatorCandidatePage({ items: [{ ...one, tradeName: '' }], nextCursor: null, hasMore: false })).toBeNull()
  })
})
