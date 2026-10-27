/**
 * AT-018 / AT-021 on S-04 in the real shell: the person fetches the line's list prices, uses one (it becomes the line's
 * reference AND its default price), adds a discount and a maturity surcharge, saves once; the re-read shows the reference,
 * the adjustments and the SERVER's net (the screen computes nothing); an overwritten price reads as manual with the
 * reference still visible; a below-floor refusal is explained on its line. Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY, SALES_PRICE_WRITE_CAPABILITY } from './capabilities'
import { LINE_PRICING_TEXT as P, ORDER_DETAIL_TEXT } from './salesText'
import { decodeDraftLinePricing, decodeLinePriceReferences, decodeReference } from './linePricing'
import { recallPriceFloorApproval, resetPendingPriceFloorApprovals } from './pendingPriceFloorApprovals'
import { ALL_SALES, COMPANY_A, HARNESS_USER_ID, KG, deferred, installFetch, json, renderSalesApp, type RecordedCall } from './testHarness'

type Body = Record<string, unknown> & { draft: Record<string, unknown>; lines: Record<string, unknown>[] }

const LINE = '66666666-6666-6666-6666-666666666666'
const LIST = 'abababab-abab-4bab-8bab-abababababab'

function detail(version: string, line: Record<string, unknown> = {}): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, version, companyId: COMPANY_A }
  body.lines = [{ ...body.lines[0], ...line }]
  return body
}

const reference = {
  priceListId: LIST, priceListName: 'Yurt içi satış', versionNo: 2, validFrom: '2026-09-01', validThrough: null, currencyCode: 'TRY',
  priceUnitId: KG, unitPrice: '42000.50', floorUnitPrice: '40000', vatBasis: 'ExcludingVat',
  lowerQuantity: '10', upperQuantity: '20', tier: 'Company',
}

/** MOD-02: the owner's chain answer for one resolved company-wide list (the shape the server sends beside the items). */
const resolved = {
  outcome: 'Resolved', effectiveOn: '2026-09-13', businessTimeZone: 'Europe/Istanbul', matchedTier: 'Company', priceListId: LIST, versionNo: 2,
  tiers: [
    { tier: 'Agreement', status: 'Unavailable', reason: 'agreement_sales_contract_missing' },
    { tier: 'Customer', status: 'NoMatch', reason: null },
    { tier: 'CustomerGroup', status: 'Unavailable', reason: 'customer_group_cari_field_missing' },
    { tier: 'Company', status: 'Matched', reason: null },
  ],
  packageStandard: { status: 'Unavailable', reason: 'package_standard_owner_missing' },
}

describe('MOD-02 tier chain resolution (SAT-D9 / SAT-D10)', () => {
  const body = (overrides: Record<string, unknown> = {}) =>
    ({ lineId: LINE, currencyCode: 'TRY', on: '2026-09-13', items: [reference], resolution: resolved, ...overrides })
  const noPrice = {
    ...resolved, outcome: 'NoPrice', matchedTier: null, priceListId: null, versionNo: null,
    tiers: resolved.tiers.map((tier) => tier.tier === 'Company' ? { ...tier, status: 'NoMatch' } : tier),
  }

  it('decodes a resolved tier, an explicit no-price and an ambiguous tier', () => {
    expect(decodeLinePriceReferences(body(), LINE)?.resolution.matchedTier).toBe('Company')
    const none = decodeLinePriceReferences(body({ items: [], resolution: noPrice }), LINE)
    expect(none && [none.items.length, none.resolution.outcome, none.resolution.matchedTier]).toEqual([0, 'NoPrice', null])
    const second = { ...reference, priceListId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb' }
    const ambiguous = decodeLinePriceReferences(body({ items: [reference, second],
      resolution: { ...resolved, outcome: 'Ambiguous', priceListId: null, versionNo: null } }), LINE)
    expect(ambiguous?.items.map((item) => item.priceListId)).toEqual([LIST, second.priceListId])
  })

  it.each<[string, Record<string, unknown>]>([
    ['no resolution member', { resolution: undefined }],
    ['a price with no tier answering', { resolution: noPrice }],
    ['no price but a resolved list', { items: [] }],
    ['a resolved list that is not the item', { resolution: { ...resolved, versionNo: 3 } }],
    ['an item of another tier', { items: [{ ...reference, tier: 'Customer' }] }],
    ['an item without a tier', { items: [{ ...reference, tier: null }] }],
    ['another business date', { resolution: { ...resolved, effectiveOn: '2026-09-14' } }],
    ['a tier missing from the chain', { resolution: { ...resolved, tiers: resolved.tiers.slice(1) } }],
    ['an unknown tier status', { resolution: { ...resolved, tiers: [{ ...resolved.tiers[0], status: 'Skipped' }, ...resolved.tiers.slice(1)] } }],
    ['a package standard value', { resolution: { ...resolved, packageStandard: { status: 'Available', reason: 'x' } } }],
  ])('refuses %s (fail closed: never painted as a price)', (_name, overrides) => {
    const raw: Record<string, unknown> = body(overrides)
    if ('resolution' in overrides && overrides.resolution === undefined) delete raw.resolution
    expect(decodeLinePriceReferences(raw, LINE)).toBeNull()
  })

  it('accepts a line reference recorded before the chain (tier null) but never an unknown tier', () => {
    expect(decodeReference({ ...reference, tier: null })?.tier).toBeNull()
    expect(decodeReference({ ...reference, tier: 'Region' })).toBeNull()
    const { tier: _tier, ...withoutTier } = reference
    expect(decodeReference(withoutTier)).toBeNull()
  })
})

describe('price tier wire bounds', () => {
  it.each([
    ['0', null, true],
    ['10', '20', true],
    ['1.0001', '2', false],
    ['10', '10', false],
    ['20', '10', false],
    ['1000000000000000', null, false],
  ] as const)('decodes lower %s and upper %s only inside Product quantity bounds', (lowerQuantity, upperQuantity, accepted) => {
    expect(decodeReference({ ...reference, lowerQuantity, upperQuantity }) !== null).toBe(accepted)
  })

  it('preserves accepted quantity text without Number conversion or rounding', () => {
    const decoded = decodeReference({ ...reference, lowerQuantity: '10.000', upperQuantity: '20.000' })
    expect(decoded && [decoded.lowerQuantity, decoded.upperQuantity]).toEqual(['10.000', '20.000'])
  })

  it('accepts only the decided VAT-exclusive net basis', () => {
    expect(decodeReference(reference)?.vatBasis).toBe('ExcludingVat')
    expect(decodeReference({ ...reference, vatBasis: 'IncludingVat' })).toBeNull()
  })
})

function pricing(version: string, over: Record<string, unknown> = {}) {
  return {
    version,
    lines: [{ lineId: LINE, lineNo: 1, unitPrice: null, priceUnitId: null, priceSource: null, priceReference: null, discountPercent: null,
      discountAmount: null, surchargePercent: null, surchargeAmount: null, netUnitPrice: null, floorException: null, ...over }],
  }
}

afterEach(() => {
  resetPendingPriceFloorApprovals()
  cleanup()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const path = '/satis/siparisler/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'

describe('AT-018 list price on the order line', () => {
  it('retries an unknown floor approval with the exact same key and body and accepts the replay', async () => {
    let approved = false
    const posts: RecordedCall[] = []
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY],
      onDetail: () => json(detail(approved ? '4' : '3', { unitPrice: '39000', priceUnitId: KG })),
      onLinePricing: () => json(pricing(approved ? '4' : '3', {
        unitPrice: '39000', priceUnitId: KG, priceSource: 'Manual', priceReference: reference, netUnitPrice: '39000',
        floorException: approved
          ? { requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: 'checker',
              approvedAtUtc: '2026-10-01T08:05:00Z', reason: 'SENTETIK sözleşme' }
          : { requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: null, approvedAtUtc: null, reason: null },
      })),
      onPriceFloorExceptionApproval: (call) => {
        posts.push(call)
        if (posts.length === 1) {
          approved = true
          throw new TypeError('SENTETIK sunucu kaydetti, yanıt kayboldu')
        }
        approved = true
        return json({ detail: detail('4', { unitPrice: '39000', priceUnitId: KG }), isReplay: true,
          originalCorrelationId: 'floor-c1' }, 200, { 'Idempotency-Replayed': 'true' })
      },
    })
    const firstRender = renderSalesApp(path)

    const control = await screen.findByTestId('sales-price-floor-approval')
    await userEvent.type(within(control).getByLabelText(P.floorApproval.reason(1)), 'SENTETIK sözleşme')
    await userEvent.click(within(control).getByRole('button', { name: P.floorApproval.approve }))
    expect(await within(control).findByText(P.floorApproval.unknown)).toBeTruthy()
    firstRender.unmount()
    renderSalesApp(path)
    const reopened = await screen.findByTestId('sales-price-floor-approval')
    await userEvent.click(within(reopened).getByRole('button', { name: P.floorApproval.retry }))

    await waitFor(() => expect(posts).toHaveLength(2))
    expect(posts[1].idempotencyKey).toBe(posts[0].idempotencyKey)
    expect(posts[1].body).toBe(posts[0].body)
  })

  it.each([403, 409])('keeps the unresolved snapshot after a retry refusal %s and never offers a new-key correction', async (status) => {
    let sends = 0
    const posts: RecordedCall[] = []
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY],
      onDetail: () => json(detail('3', { unitPrice: '39000', priceUnitId: KG })),
      onLinePricing: () => json(pricing('3', {
        unitPrice: '39000', priceUnitId: KG, priceSource: 'Manual', priceReference: reference, netUnitPrice: '39000',
        floorException: { requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: null,
          approvedAtUtc: null, reason: null },
      })),
      onPriceFloorExceptionApproval: (call) => {
        posts.push(call)
        sends += 1
        if (sends === 1) throw new TypeError('SENTETIK belirsiz sonuç')
        if (sends === 2) return json({ status, title: 'SENTETIK refusal' }, status)
        return json({ detail: detail('4', { unitPrice: '39000', priceUnitId: KG }), isReplay: true,
          originalCorrelationId: 'floor-c2' }, 200, { 'Idempotency-Replayed': 'true' })
      },
    })
    const app = renderSalesApp(path)

    const control = await screen.findByTestId('sales-price-floor-approval')
    await userEvent.type(within(control).getByLabelText(P.floorApproval.reason(1)), 'SENTETIK sözleşme')
    await userEvent.click(within(control).getByRole('button', { name: P.floorApproval.approve }))
    await userEvent.click(await within(control).findByRole('button', { name: P.floorApproval.retry }))

    let retryControl = control
    if (status === 403) {
      expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
      expect(screen.queryByTestId('sales-price-floor-approval')).toBeNull()
      app.unmount()
      renderSalesApp(path)
      retryControl = await screen.findByTestId('sales-price-floor-approval')
    }
    expect(await within(retryControl).findByText(P.floorApproval.unknown)).toBeTruthy()
    expect(within(retryControl).queryByRole('button', { name: P.floorApproval.correct })).toBeNull()
    await userEvent.click(within(retryControl).getByRole('button', { name: P.floorApproval.retry }))

    await waitFor(() => expect(posts).toHaveLength(3))
    expect(new Set(posts.map((call) => call.idempotencyKey)).size).toBe(1)
    expect(new Set(posts.map((call) => call.body)).size).toBe(1)
  })

  it('ends the session after a retry 401 and does not adopt the unresolved attempt in the new session epoch', async () => {
    const posts: RecordedCall[] = []
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY],
      onDetail: () => json(detail('3', { unitPrice: '39000', priceUnitId: KG })),
      onLinePricing: () => json(pricing('3', {
        unitPrice: '39000', priceUnitId: KG, priceSource: 'Manual', priceReference: reference, netUnitPrice: '39000',
        floorException: { requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: null,
          approvedAtUtc: null, reason: null },
      })),
      onPriceFloorExceptionApproval: (call) => {
        posts.push(call)
        if (posts.length === 1) throw new TypeError('SENTETIK belirsiz sonuç')
        return json({ status: 401, title: 'SENTETIK refusal' }, 401)
      },
    })
    renderSalesApp(path)

    const control = await screen.findByTestId('sales-price-floor-approval')
    await userEvent.type(within(control).getByLabelText(P.floorApproval.reason(1)), 'SENTETIK sözleşme')
    await userEvent.click(within(control).getByRole('button', { name: P.floorApproval.approve }))
    await userEvent.click(await within(control).findByRole('button', { name: P.floorApproval.retry }))

    expect(await screen.findByText('Oturum sonlandı — oturum')).toBeTruthy()
    expect(screen.queryByTestId('sales-price-floor-approval')).toBeNull()
    expect(posts).toHaveLength(2)
    expect(posts[1].idempotencyKey).toBe(posts[0].idempotencyKey)
    expect(posts[1].body).toBe(posts[0].body)
    const scope = `sales.draft.price-floor-exception-approval:${path.slice(path.lastIndexOf('/') + 1)}`
    expect(recallPriceFloorApproval(JSON.stringify([HARNESS_USER_ID, '0', COMPANY_A, scope]))).not.toBeNull()
    expect(recallPriceFloorApproval(JSON.stringify([HARNESS_USER_ID, '1', COMPANY_A, scope]))).toBeNull()
  })

  it.each([401, 403])('reports a current floor-approval %s through AuthBoundary and closes protected detail', async (status) => {
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY],
      onDetail: () => json(detail('3', { unitPrice: '39000', priceUnitId: KG })),
      onLinePricing: () => json(pricing('3', {
        unitPrice: '39000', priceUnitId: KG, priceSource: 'Manual', priceReference: reference, netUnitPrice: '39000',
        floorException: { requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: null,
          approvedAtUtc: null, reason: null },
      })),
      onPriceFloorExceptionApproval: () => json({ status, title: 'SENTETIK refusal' }, status),
    })
    renderSalesApp(path)

    const control = await screen.findByTestId('sales-price-floor-approval')
    await userEvent.type(within(control).getByLabelText(P.floorApproval.reason(1)), 'SENTETIK sözleşme')
    await userEvent.click(within(control).getByRole('button', { name: P.floorApproval.approve }))

    expect(await screen.findByText(status === 401 ? 'Oturum sonlandı — oturum' : 'Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByTestId('sales-price-floor-approval')).toBeNull()
  })

  it('discards a late result from the departed route and keeps the same uncertain key for retry', async () => {
    const late = deferred<Response>()
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY],
      onDetail: () => json(detail('3', { unitPrice: '39000', priceUnitId: KG })),
      onLinePricing: () => json(pricing('3', {
        unitPrice: '39000', priceUnitId: KG, priceSource: 'Manual', priceReference: reference, netUnitPrice: '39000',
        floorException: { requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: null,
          approvedAtUtc: null, reason: null },
      })),
      onPriceFloorExceptionApproval: () => late.promise,
    })
    const app = renderSalesApp(path)
    const control = await screen.findByTestId('sales-price-floor-approval')
    await userEvent.type(within(control).getByLabelText(P.floorApproval.reason(1)), 'SENTETIK sözleşme')
    await userEvent.click(within(control).getByRole('button', { name: P.floorApproval.approve }))

    await act(async () => { await app.router.navigate('/satis/siparisler') })
    await act(async () => {
      late.resolve(json({ detail: detail('4', { unitPrice: '39000', priceUnitId: KG }), isReplay: false,
        originalCorrelationId: 'late-old-route' }, 200))
      await Promise.resolve()
    })
    await act(async () => { await app.router.navigate(path) })

    const reopened = await screen.findByTestId('sales-price-floor-approval')
    expect(await within(reopened).findByText(P.floorApproval.unknown)).toBeTruthy()
    expect(within(reopened).getByRole('button', { name: P.floorApproval.retry })).toBeTruthy()
  })

  it('accepts the existing 4000-character approval reason bound and rejects a larger response', () => {
    const floorException = {
      requestedBy: 'maker', requestedAtUtc: '2026-10-01T08:00:00Z', approvedBy: 'checker',
      approvedAtUtc: '2026-10-01T08:05:00Z', reason: 'x'.repeat(4000),
    }
    expect(decodeDraftLinePricing(pricing('4', { unitPrice: '39000', priceUnitId: KG, priceSource: 'Manual',
      netUnitPrice: '39000', floorException }))).not.toBeNull()
    expect(decodeDraftLinePricing(pricing('4', { unitPrice: '39000', priceUnitId: KG, priceSource: 'Manual',
      netUnitPrice: '39000', floorException: { ...floorException, reason: 'x'.repeat(4001) } }))).toBeNull()
  })

  it('uses the list price as reference and default, adds a discount and a surcharge, and reads back the server net', async () => {
    let saved = false
    const posts: RecordedCall[] = []
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_WRITE_CAPABILITY],
      onDetail: () => json(saved ? detail('3', { unitPrice: '42000.50', priceUnitId: KG, discountPercent: '3' }) : detail('2')),
      onLinePricing: () => json(saved
        ? pricing('3', { unitPrice: '42000.50', priceUnitId: KG, priceSource: 'Reference', priceReference: reference, discountPercent: '3',
          surchargePercent: '1.5', netUnitPrice: '41351.592275' })
        : pricing('2')),
      onPriceReferences: () => json({ lineId: LINE, currencyCode: 'TRY', on: '2026-09-13', items: [reference], resolution: resolved }),
      onCommercialRevision: (call) => {
        posts.push(call)
        saved = true
        return json({ detail: detail('3', { unitPrice: '42000.50', priceUnitId: KG, discountPercent: '3' }), isReplay: false,
          originalCorrelationId: 'c1' })
      },
    })
    renderSalesApp(path)

    const form = await screen.findByTestId('sales-price-revision-form')
    expect((await within(form).findByTestId('sales-price-reference-1')).textContent).toBe(`${P.reference}: ${P.noReference}`)
    await userEvent.click(await within(form).findByTestId('sales-price-fetch-1'))
    expect((await within(form).findByTestId('sales-price-resolution-1')).textContent).toBe(P.resolvedTier(P.priceTier.Company))
    await userEvent.click(await within(form).findByRole('button', { name: P.useReference }))
    // The chosen list price is the reference AND the default price (the person may still type another).
    expect((within(form).getByLabelText(ORDER_DETAIL_TEXT.priceRevision.unitPrice) as HTMLInputElement).value).toBe('42000.50')
    expect(within(form).getByTestId('sales-price-reference-1').textContent).toContain('Yurt içi satış · Sürüm 2: 42000.50 TRY')
    // MOD-02: the line names the tier its list price came from.
    expect(within(form).getByTestId('sales-price-reference-1').textContent).toContain(P.tierSuffix(P.priceTier.Company))
    await userEvent.type(within(form).getByLabelText(ORDER_DETAIL_TEXT.priceRevision.discount), '3')
    await userEvent.type(within(form).getByLabelText(P.surchargeValue), '1.5')
    await userEvent.click(within(form).getByRole('button', { name: ORDER_DETAIL_TEXT.priceRevision.submit }))

    await waitFor(() => expect(screen.getByTestId('sales-line-1-net').textContent).toBe('41351.592275'))
    const sent = JSON.parse(posts[0].body ?? '{}') as { expectedVersion: string; lines: Record<string, unknown>[] }
    expect(sent.expectedVersion).toBe('2')
    expect(sent.lines[0]).toEqual({
      lineId: LINE, unitPrice: '42000.50', priceUnitId: KG, discountPercent: '3',
      priceReference: { priceListId: LIST, versionNo: 2 }, discountAmount: null, surchargePercent: '1.5', surchargeAmount: null,
    })
    // Reopened from the server: the reference, the source, the adjustments and the net are all visible.
    expect(screen.getByTestId('sales-line-1-reference').textContent).toContain('Yurt içi satış · Sürüm 2: 42000.50 TRY')
    expect(screen.getByTestId('sales-line-1-reference').textContent).toContain(P.tierText('10', '20'))
    expect(screen.getByTestId('sales-line-1-reference').textContent).toContain(P.vatExclusive)
    expect(screen.getByTestId('sales-line-1-source').textContent).toBe(P.sourceReference)
    expect(screen.getByTestId('sales-line-1-discount-adjustment').textContent).toBe('%3')
    expect(screen.getByTestId('sales-line-1-surcharge').textContent).toBe('%1.5')
  })

  it('MOD-02: no tier pricing the line is said explicitly and offers nothing to use', async () => {
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_WRITE_CAPABILITY],
      onDetail: () => json(detail('2')),
      onLinePricing: () => json(pricing('2')),
      onPriceReferences: () => json({ lineId: LINE, currencyCode: 'TRY', on: '2026-09-13', items: [],
        resolution: { ...resolved, outcome: 'NoPrice', matchedTier: null, priceListId: null, versionNo: null,
          tiers: resolved.tiers.map((tier) => tier.tier === 'Company' ? { ...tier, status: 'NoMatch' } : tier) } }),
    })
    renderSalesApp(path)
    const form = await screen.findByTestId('sales-price-revision-form')
    await userEvent.click(await within(form).findByTestId('sales-price-fetch-1'))
    expect((await within(form).findByTestId('sales-price-resolution-1')).textContent).toBe(P.noReferences)
    expect(within(form).queryByRole('button', { name: P.useReference })).toBeNull()
    expect((within(form).getByLabelText(ORDER_DETAIL_TEXT.priceRevision.unitPrice) as HTMLInputElement).value).toBe('')
  })

  it('an overwritten price with amounts reads back as manual with the reference still visible', async () => {
    installFetch({
      capabilities: [...ALL_SALES],
      onDetail: () => json(detail('4', { unitPrice: '41000', priceUnitId: KG })),
      onLinePricing: () => json(pricing('4', { unitPrice: '41000', priceUnitId: KG, priceSource: 'Manual', priceReference: reference,
        discountAmount: '500', surchargeAmount: '250', netUnitPrice: '40750' })),
    })
    renderSalesApp(path)
    expect((await screen.findByTestId('sales-line-1-source')).textContent).toBe(P.sourceManual)
    expect(screen.getByTestId('sales-line-1-reference').textContent).toContain('42000.50')
    expect(screen.getByTestId('sales-line-1-discount-adjustment').textContent).toBe(P.amount('500'))
    expect(screen.getByTestId('sales-line-1-net').textContent).toBe('40750')
    expect(screen.queryByTestId('sales-price-revision-form')).toBeNull()
  })

  it('a net below the list floor is refused and explained on its line', async () => {
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_WRITE_CAPABILITY],
      onDetail: () => json(detail('2')),
      onLinePricing: () => json(pricing('2')),
      onPriceReferences: () => json({ lineId: LINE, currencyCode: 'TRY', on: '2026-09-13', items: [reference], resolution: resolved }),
      onCommercialRevision: () => json({ status: 422, title: 'x', code: 'validation_failed', correlationId: 'c',
        errors: [{ field: 'lines[0].unitPrice', code: 'sales_price_below_floor' }] }, 422),
    })
    renderSalesApp(path)
    const form = await screen.findByTestId('sales-price-revision-form')
    await userEvent.click(await within(form).findByTestId('sales-price-fetch-1'))
    await userEvent.click(await within(form).findByRole('button', { name: P.useReference }))
    await userEvent.type(within(form).getByLabelText(ORDER_DETAIL_TEXT.priceRevision.discount), '10')
    await userEvent.click(within(form).getByRole('button', { name: ORDER_DETAIL_TEXT.priceRevision.submit }))
    expect((await screen.findByTestId('sales-price-line-error')).textContent)
      .toBe(P.lineError(1, P.fieldError.sales_price_below_floor))
  })

  it.each([
    ['malformed', () => json({ version: '2', lines: [{ lineId: LINE }] })],
    ['wrong-version', () => json(pricing('1'))],
    ['wrong-line', () => json({ ...pricing('2'), lines: [{ ...pricing('2').lines[0], lineId: '77777777-7777-4777-8777-777777777777' }] })],
  ])('locks a new commercial save when pricing is %s', async (_caseName, answer) => {
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_WRITE_CAPABILITY],
      onDetail: () => json(detail('2', { unitPrice: '41000', priceUnitId: KG })),
      onLinePricing: answer,
    })
    renderSalesApp(path)
    const form = await screen.findByTestId('sales-price-revision-form')
    expect(await within(form).findByTestId('sales-price-pricing-unavailable')).toBeTruthy()
    expect((within(form).getByRole('button', { name: ORDER_DETAIL_TEXT.priceRevision.submit }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('keeps a new save locked while pricing is delayed and after its 503', async () => {
    const pending = deferred<Response>()
    installFetch({
      capabilities: [...ALL_SALES, SALES_PRICE_WRITE_CAPABILITY],
      onDetail: () => json(detail('2', { unitPrice: '41000', priceUnitId: KG })),
      onLinePricing: () => pending.promise,
    })
    renderSalesApp(path)
    const form = await screen.findByTestId('sales-price-revision-form')
    const submit = within(form).getByRole('button', { name: ORDER_DETAIL_TEXT.priceRevision.submit })
    expect((submit as HTMLButtonElement).disabled).toBe(true)
    pending.resolve(json({ status: 503 }, 503))
    expect(await within(form).findByTestId('sales-price-pricing-unavailable')).toBeTruthy()
    expect((submit as HTMLButtonElement).disabled).toBe(true)
  })
})
