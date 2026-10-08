/**
 * Decoders against the Sales Backend's own examples (#257 @ 2c1239e), copied verbatim
 * into ./examples. The first test pins each copy to its blob id, so a silently edited
 * copy cannot keep the rest of this file green. SALES-A1 added `approvalState` to every
 * summary and `approval` to every detail (a contract delta of the Sales owner); the four
 * bodies that carry them are pinned to their blob ids at that change.
 */

import { describe, expect, it } from 'vitest'
import createRequest from './examples/create-request.json?raw'
import createResponse from './examples/create-response.json?raw'
import detailResponse from './examples/detail-response.json?raw'
import errorAccount from './examples/error-account.json?raw'
import errorCompany from './examples/error-company.json?raw'
import errorForbidden from './examples/error-forbidden.json?raw'
import errorProduct from './examples/error-product.json?raw'
import errorProvider from './examples/error-provider.json?raw'
import errorReplay from './examples/error-replay.json?raw'
import errorSettingsAccess from './examples/error-settings-access.json?raw'
import listResponse from './examples/list-response.json?raw'
import productOwnerSelection from './examples/product-owner-selection.json?raw'
import replayResponse from './examples/replay-response.json?raw'
// SALES-A2a: the two reference LIST reads (FE-authored examples of the Sales owner's shapes; not #257 copies).
import contactInformationResponse from './examples/contact-information-response.json?raw'
import currenciesResponse from './examples/currencies-response.json?raw'
import {
  COMMERCIAL_STAGE,
  SALES_PROBLEM_CODE,
  decodeContactInformationOptions,
  decodeCurrencyOptions,
  decodeDraftDetail,
  decodeDraftMutation,
  decodeDraftPage,
  decodeProblem,
  hasPriceLikeKey,
} from './contract'
import { PROBLEM_CODE_TEXT } from './salesText'
import revisionsResponse from './examples/revisions-response.json?raw'
import { decodeDraftRevisions } from './contract'

const EXAMPLES: Record<string, { raw: string; blob: string }> = {
  'create-request.json': { raw: createRequest, blob: 'a78328681578e18627a25a0cd200177d309a534b' },
  'create-response.json': { raw: createResponse, blob: 'fc2f730beecafb21a10667275dfe8fdf584850f9' },
  'detail-response.json': { raw: detailResponse, blob: '6284a85d1519eef19e3e3f97febe642cfab27f30' },
  'error-account.json': { raw: errorAccount, blob: '6b3e1ea7f2a986d8f1f401e4217a7ff42436a24e' },
  'error-company.json': { raw: errorCompany, blob: 'aa227cc06a73326e4db1d579c27f32795a8a41a1' },
  'error-forbidden.json': { raw: errorForbidden, blob: 'bd328f735086931db968d2efb2345e2637efadfd' },
  'error-product.json': { raw: errorProduct, blob: '2b9da9f8de5edb7c07b27a3e23a7d4409842c61d' },
  'error-provider.json': { raw: errorProvider, blob: '1a8d74000d85b0cb99f772b9359ae29e666d6c23' },
  'error-replay.json': { raw: errorReplay, blob: 'a7728cfecd86c90528151327bebe7abe59d79d74' },
  'error-settings-access.json': { raw: errorSettingsAccess, blob: 'e2aec636bd687380cfdb520339c28c45f496ad5a' },
  'list-response.json': { raw: listResponse, blob: '34aa8532ca37af792bc50f0fbfa66f80a8e43eb5' },
  'product-owner-selection.json': { raw: productOwnerSelection, blob: 'a1e8a3aee04f385cccc9d3815e9cac6f78fbd620' },
  'replay-response.json': { raw: replayResponse, blob: '402d282b1e21fd6c23636f2f1cdd55454d8124e8' },
}

async function gitBlobId(text: string): Promise<string> {
  const bytes = new TextEncoder().encode(text)
  const header = new TextEncoder().encode(`blob ${bytes.length}\0`)
  const all = new Uint8Array(header.length + bytes.length)
  all.set(header)
  all.set(bytes, header.length)
  const digest = await globalThis.crypto.subtle.digest('SHA-1', all)
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('')
}

const parse = (raw: string) => JSON.parse(raw) as Record<string, unknown>
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T

describe('example provenance', () => {
  it('every copied example is byte-identical to its pinned source', async () => {
    for (const [name, { raw, blob }] of Object.entries(EXAMPLES)) {
      expect(await gitBlobId(raw), name).toBe(blob)
    }
  })

  // D2 (coordinator): a pin that only accepts is not shown to reject — one byte of each copy, changed, must miss its pin.
  it('a one-byte mutation of any copy misses its pin', async () => {
    for (const [name, { raw, blob }] of Object.entries(EXAMPLES)) {
      const at = Math.floor(raw.length / 2)
      const mutated = raw.slice(0, at) + (raw[at] === ' ' ? '\t' : ' ') + raw.slice(at + 1)
      expect(mutated, name).not.toBe(raw)
      expect(await gitBlobId(mutated), name).not.toBe(blob)
    }
  })
})

describe('decodeDraftMutation', () => {
  it('reads the 201 create example and the 200 replay example, telling them apart', () => {
    const created = decodeDraftMutation(parse(createResponse))
    const replayed = decodeDraftMutation(parse(replayResponse))
    expect(created?.isReplay).toBe(false)
    expect(replayed?.isReplay).toBe(true)
    // The stored legacy replay may omit only the additive stage; identity and all older facts remain unchanged.
    expect(replayed?.detail.draft.id).toBe(created?.detail.draft.id)
    expect(replayed?.detail.draft.version).toBe(created?.detail.draft.version)
    expect(created?.detail.draft).toMatchObject({
      revision: 'Rev.0', version: '9007199254740993', state: 'Draft', commercialStage: COMMERCIAL_STAGE.order,
    })
    expect(replayed?.detail.draft.commercialStage).toBe(COMMERCIAL_STAGE.unavailable)
  })

  it('keeps a version above 2^53 as exact text', () => {
    expect(decodeDraftMutation(parse(createResponse))?.detail.draft.version).toBe('9007199254740993')
  })

  it('accepts absent or nullable additive stage as unavailable and refuses an unknown stage', () => {
    const absent = clone(parse(createResponse)) as any
    delete absent.detail.draft.commercialStage
    expect(decodeDraftMutation(absent)?.detail.draft.commercialStage).toBe(COMMERCIAL_STAGE.unavailable)

    const nullable = clone(parse(createResponse)) as any
    nullable.detail.draft.commercialStage = null
    expect(decodeDraftMutation(nullable)?.detail.draft.commercialStage).toBe(COMMERCIAL_STAGE.unavailable)

    const unknown = clone(parse(createResponse)) as any
    unknown.detail.draft.commercialStage = 'Accepted'
    expect(decodeDraftMutation(unknown)).toBeNull()
  })

  it.each([
    ['a missing nullable member', (b: any) => delete b.detail.note],
    ['an unknown member', (b: any) => (b.detail.draft.total = '0')],
    ['wrong case', (b: any) => ((b.detail.Draft = b.detail.draft), delete b.detail.draft)],
    ['a numeric version', (b: any) => (b.detail.draft.version = 7)],
    ['a numeric quantity', (b: any) => (b.detail.lines[0].quantity = 1000)],
    ['a zero version', (b: any) => (b.detail.draft.version = '0')],
    ['an empty server id', (b: any) => (b.detail.draft.id = '00000000-0000-0000-0000-000000000000')],
    ['another state', (b: any) => (b.detail.draft.state = 'Approved')],
    ['another kind', (b: any) => (b.detail.draft.kind = 'Request')],
    ['a timestamp as business date', (b: any) => (b.detail.draft.businessDate = '2026-09-13T00:00:00Z')],
    ['a lineCount that disagrees with the lines', (b: any) => (b.detail.draft.lineCount = 2)],
    ['a duplicate line id', (b: any) => {
      const copy = clone(b.detail.lines[0])
      copy.lineNo = 2
      b.detail.lines.push(copy)
      b.detail.draft.lineCount = 2
    }],
    ['slices that exceed the line', (b: any) => (b.detail.lines[0].requestedDeliverySlices[1].quantity = '700')],
    // K3 P2: the disclosure marker is never absent, never a third word, and a withheld line carries no price text.
    ['a line without the price disclosure', (b: any) => delete b.detail.lines[0].priceDisclosure],
    ['a disclosure the contract does not name', (b: any) => (b.detail.lines[0].priceDisclosure = 'Redacted')],
    ['a withheld line that still carries a price', (b: any) => {
      b.detail.lines[0].priceDisclosure = 'Withheld'
      b.detail.lines[0].unitPrice = '12.50'
      b.detail.lines[0].priceUnitId = '44444444-4444-4444-4444-444444444444'
    }],
    ['a slice in another unit', (b: any) => (b.detail.lines[0].requestedDeliverySlices[0].orderUnitId = '44444444-4444-4444-4444-444444444445')],
    ['a price without its unit', (b: any) => (b.detail.lines[0].unitPrice = '10')],
    ['a non-boolean isReplay', (b: any) => (b.isReplay = 'false')],
    // SALES-A1: the approval axis is a fourth fact; its facts must fit it.
    ['a missing approvalState', (b: any) => delete b.detail.draft.approvalState],
    ['an unknown approvalState', (b: any) => (b.detail.draft.approvalState = 'Onaylı')],
    ['a missing approval block', (b: any) => delete b.detail.approval],
    ['an approval block with an extra member', (b: any) => (b.detail.approval.approvedBy = 'x')],
    ['a preparing draft with a submitter', (b: any) => (b.detail.approval.submittedBy = 'someone')],
    ['an approved draft without a decision', (b: any) => (b.detail.draft.approvalState = 'Approved')],
    // D2: the own-draft relationship is a boolean of every detail; a withdrawn draft carries its decision and a whole-or-absent submission.
    ['a detail without isOwnDraft', (b: any) => delete b.detail.isOwnDraft],
    ['a detail whose isOwnDraft is not a boolean', (b: any) => (b.detail.isOwnDraft = 'true')],
    ['a detail without isOwnDecisionRestricted', (b: any) => delete b.detail.isOwnDecisionRestricted],
    ['a detail whose isOwnDecisionRestricted is not a boolean', (b: any) => (b.detail.isOwnDecisionRestricted = 'true')],
    ['a withdrawn draft without a decision', (b: any) => (b.detail.draft.approvalState = 'Withdrawn')],
    ['a withdrawn draft with half a submission', (b: any) => {
      b.detail.draft.approvalState = 'Withdrawn'
      b.detail.approval = { submittedBy: 'a', submittedAtUtc: null, decidedBy: 'a', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: null }
    }],
    ['an approved draft with a rejection reason', (b: any) => {
      b.detail.draft.approvalState = 'Approved'
      b.detail.approval = { submittedBy: 'a', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'b', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: 'x' }
    }],
    ['a pending draft with a decision', (b: any) => {
      b.detail.draft.approvalState = 'PendingApproval'
      b.detail.approval = { submittedBy: 'a', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'b', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: null }
    }],
  ])('refuses %s', (_, mutate) => {
    const body = clone(parse(createResponse))
    mutate(body)
    expect(decodeDraftMutation(body)).toBeNull()
  })

  it('accepts a zero price with its unit: zero is not unknown', () => {
    const body = clone(parse(createResponse)) as any
    body.detail.lines[0].unitPrice = '0'
    body.detail.lines[0].priceUnitId = '44444444-4444-4444-4444-444444444444'
    expect(decodeDraftMutation(body)?.detail.lines[0].unitPrice).toBe('0')
  })
})

describe('decodeDraftDetail / decodeDraftPage', () => {
  const productSnapshot = {
    productId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
    productVersion: ' 9007199254740993 ',
    productCode: ' SENTETIK-P-01 ',
    productName: ' Sentetik Rulo ',
    orderUnitId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1',
    orderUnitCode: ' KG ',
  }

  it('reads the detail example, bound to the id that was asked for', () => {
    const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
    expect(decodeDraftDetail(parse(detailResponse), id)?.lines[0].requestedDeliverySlices).toHaveLength(2)
    expect(decodeDraftDetail(parse(detailResponse), 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab')).toBeNull()
  })

  it('reads the list example; null nextCursor is the end', () => {
    const page = decodeDraftPage(parse(listResponse))
    expect(page?.items).toHaveLength(1)
    expect(page?.nextCursor).toBeNull()
  })

  it('normalizes omitted and explicit-null product snapshots to absent, and preserves a valid stored snapshot verbatim', () => {
    const omitted = decodeDraftPage(parse(listResponse))?.items[0]
    expect(omitted).not.toHaveProperty('productSnapshots')

    const nullable = clone(parse(listResponse)) as any
    nullable.items[0].productSnapshots = null
    expect(decodeDraftPage(nullable)?.items[0]).not.toHaveProperty('productSnapshots')

    const present = clone(parse(listResponse)) as any
    present.items[0].productSnapshots = [productSnapshot]
    expect(decodeDraftPage(present)?.items[0].productSnapshots).toEqual([productSnapshot])
  })

  it('requires all six non-null snapshot fields and refuses invalid text, ids and unknown parsed keys', () => {
    for (const field of Object.keys(productSnapshot)) {
      const missing = clone(parse(listResponse)) as any
      missing.items[0].productSnapshots = [clone(productSnapshot)]
      delete missing.items[0].productSnapshots[0][field]
      expect(decodeDraftPage(missing), field).toBeNull()
    }

    const invalidCases: Array<(snapshot: Record<string, unknown>) => void> = [
      (snapshot) => { snapshot.productId = '00000000-0000-0000-0000-000000000000' },
      (snapshot) => { snapshot.orderUnitId = 'not-an-id' },
      (snapshot) => { snapshot.productVersion = '   ' },
      (snapshot) => { snapshot.productCode = '   ' },
      (snapshot) => { snapshot.productName = '' },
      (snapshot) => { snapshot.orderUnitCode = '\t' },
      (snapshot) => { snapshot.currentProductName = 'live master must not enter the stored snapshot' },
    ]
    for (const mutate of invalidCases) {
      const body = clone(parse(listResponse)) as any
      const snapshot = clone(productSnapshot) as Record<string, unknown>
      mutate(snapshot)
      body.items[0].productSnapshots = [snapshot]
      expect(decodeDraftPage(body)).toBeNull()
    }
  })

  it('normalizes omitted and explicit-null line unit code to absent, preserving only valid nonblank text', () => {
    const omitted = decodeDraftDetail(parse(detailResponse), 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa')?.lines[0]
    expect(omitted).not.toHaveProperty('orderUnitCode')

    const nullable = clone(parse(detailResponse)) as any
    nullable.lines[0].orderUnitCode = null
    expect(decodeDraftDetail(nullable, nullable.draft.id)?.lines[0]).not.toHaveProperty('orderUnitCode')

    const present = clone(parse(detailResponse)) as any
    present.lines[0].orderUnitCode = ' KG '
    expect(decodeDraftDetail(present, present.draft.id)?.lines[0].orderUnitCode).toBe(' KG ')

    for (const invalid of ['', '   ', '\n']) {
      const body = clone(parse(detailResponse)) as any
      body.lines[0].orderUnitCode = invalid
      expect(decodeDraftDetail(body, body.draft.id)).toBeNull()
    }
    const unknown = clone(parse(detailResponse)) as any
    unknown.lines[0].currentOrderUnitCode = 'KG'
    expect(decodeDraftDetail(unknown, unknown.draft.id)).toBeNull()
  })

  it('keeps stored product/unit facts while a Withheld line remains commercially null', () => {
    const body = clone(parse(detailResponse)) as any
    body.draft.productSnapshots = [productSnapshot]
    Object.assign(body.lines[0], {
      orderUnitCode: ' KG ',
      priceDisclosure: 'Withheld',
      unitPrice: null,
      priceUnitId: null,
      discountPercent: null,
    })
    const decoded = decodeDraftDetail(body, body.draft.id)
    expect(decoded?.draft.productSnapshots).toEqual([productSnapshot])
    expect(decoded?.lines[0]).toMatchObject({
      orderUnitCode: ' KG ', unitPrice: null, priceUnitId: null, discountPercent: null, priceDisclosure: 'Withheld',
    })
  })

  it('refuses a list row missing a member, or repeated', () => {
    const missing = clone(parse(listResponse)) as any
    delete missing.items[0].lineCount
    expect(decodeDraftPage(missing)).toBeNull()
    const repeated = clone(parse(listResponse)) as any
    repeated.items.push(clone(repeated.items[0]))
    expect(decodeDraftPage(repeated)).toBeNull()
  })
})

describe('decodeProblem', () => {
  it.each([
    [errorAccount, 409, SALES_PROBLEM_CODE.passiveCompanyAccount, 'companyAccountId'],
    [errorCompany, 409, SALES_PROBLEM_CODE.companyMismatch, 'companyAccountId'],
    [errorProduct, 422, SALES_PROBLEM_CODE.unsupportedProduct, 'lines[0].productId'],
    [errorProvider, 503, SALES_PROBLEM_CODE.settingsProviderUnavailable, 'currencyId'],
    [errorReplay, 409, SALES_PROBLEM_CODE.sameKeyPayloadMismatch, 'idempotencyKey'],
    [errorSettingsAccess, 403, SALES_PROBLEM_CODE.settingsAccessDenied, 'currencyId'],
  ])('reads code and field errors of each Sales problem example', (raw, status, code, field) => {
    const problem = decodeProblem(parse(raw), status)
    expect(problem?.code).toBe(code)
    expect(problem?.errors[0]?.field).toBe(field)
    expect(PROBLEM_CODE_TEXT[code]).toBeTruthy()
  })

  it('reads the platform 403 without a code or errors', () => {
    expect(decodeProblem(parse(errorForbidden), 403)).toEqual({
      status: 403,
      title: 'Forbidden',
      code: null,
      correlationId: null,
      errors: [],
    })
  })

  it('keeps unavailable and denied apart — neither is an empty selection', () => {
    expect(decodeProblem(parse(errorProvider), 503)?.code).not.toBe(decodeProblem(parse(errorSettingsAccess), 403)?.code)
  })

  it('does not trust a body whose status disagrees with the response', () => {
    expect(decodeProblem(parse(errorAccount), 400)).toBeNull()
  })
})

describe('SALES-A1 approval axis', () => {
  it.each([
    ['PendingApproval', { submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: null, decidedAtUtc: null, rejectionReason: null }],
    // D2: withdrawn after a submission, and withdrawn from Preparing (no submission at all).
    ['Withdrawn', { submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'satis-1', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: 'Vazgeçildi' }],
    ['Withdrawn', { submittedBy: null, submittedAtUtc: null, decidedBy: 'satis-1', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: null }],
    ['Approved', { submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: null }],
    ['Rejected', { submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: 'Fiyat yok' }],
    ['Rejected', { submittedBy: 'satis-1', submittedAtUtc: '2026-09-19T09:00:00Z', decidedBy: 'onay-1', decidedAtUtc: '2026-09-19T09:05:00Z', rejectionReason: null }],
  ])('reads a %s draft with facts that fit', (state, approval) => {
    const body = clone(parse(detailResponse)) as any
    body.draft.approvalState = state
    body.approval = approval
    const detail = decodeDraftDetail(body, body.draft.id)
    expect(detail?.draft.approvalState).toBe(state)
    expect(detail?.approval).toEqual(approval)
    // The lifecycle state and the revision are still their own facts.
    expect(detail?.draft).toMatchObject({ state: 'Draft', revision: 'Rev.0' })
  })

  it('the list rows carry the axis too', () => {
    const page = decodeDraftPage(parse(listResponse))
    expect(page?.items[0].approvalState).toBe('Preparing')
    const body = clone(parse(listResponse)) as any
    body.items[0].approvalState = 'approved'
    expect(decodeDraftPage(body)).toBeNull()
  })
})

describe('SALES-A2a reference lists', () => {
  const COMPANY = '99999999-9999-9999-9999-999999999999'
  const PARTY = '11111111-1111-1111-1111-111111111111'
  const ACCOUNT = '55555555-5555-5555-5555-555555555555'
  const ADDRESS = 'aaaaaaaa-1111-4111-8111-000000000401'
  const CONTACT = 'bbbbbbbb-1111-4111-8111-000000000401'
  type Rows = { currencies: Record<string, unknown>[] }
  type Lists = { addresses: Record<string, unknown>[]; contacts: Record<string, unknown>[] }

  it('reads the currencies example bound to its company and date; another company or date is refused', () => {
    expect(decodeCurrencyOptions(parse(currenciesResponse), COMPANY, '2026-09-13')).toEqual([
      { currencyId: '33333333-3333-3333-3333-333333333333', code: 'TRY', profileId: 'cccccccc-1111-4111-8111-000000000401', profileVersion: '3' },
    ])
    expect(decodeCurrencyOptions(parse(currenciesResponse), COMPANY, '2026-09-14')).toBeNull()
    expect(decodeCurrencyOptions(parse(currenciesResponse), '88888888-8888-8888-8888-888888888888', '2026-09-13')).toBeNull()
    expect(decodeCurrencyOptions({ companyId: COMPANY, effectiveOn: '2026-09-13', currencies: [] }, COMPANY, '2026-09-13')).toEqual([])
  })

  it('refuses a currency row missing a member, carrying an unknown one, repeated, or naming a price', () => {
    const missing = parse(currenciesResponse) as unknown as Rows
    delete missing.currencies[0].profileVersion
    expect(decodeCurrencyOptions(missing, COMPANY, '2026-09-13')).toBeNull()
    const unknown = parse(currenciesResponse) as unknown as Rows
    unknown.currencies[0].rate = '1'
    expect(decodeCurrencyOptions(unknown, COMPANY, '2026-09-13')).toBeNull()
    const repeated = parse(currenciesResponse) as unknown as Rows
    repeated.currencies.push(clone(repeated.currencies[0]))
    expect(decodeCurrencyOptions(repeated, COMPANY, '2026-09-13')).toBeNull()
    const priced = parse(currenciesResponse) as unknown as Rows
    priced.currencies[0].unitPrice = '1'
    expect(hasPriceLikeKey(priced)).toBe(true)
    expect(decodeCurrencyOptions(priced, COMPANY, '2026-09-13')).toBeNull()
  })

  it('reads the contact-information example bound to its account, texts and versions verbatim', () => {
    expect(decodeContactInformationOptions(parse(contactInformationResponse), COMPANY, PARTY, ACCOUNT)).toEqual({
      partyId: PARTY,
      companyAccountId: ACCOUNT,
      accountVersion: '2',
      addresses: [{ id: ADDRESS, version: '4', purpose: 'delivery', text: 'SENTETIK Sanayi Cad. 1, Kocaeli, TR' }],
      contacts: [{ id: CONTACT, version: '2', displayName: 'SENTETIK Ayşe Yükleme', jobTitle: 'Sevkiyat', purpose: 'delivery' }],
    })
    expect(decodeContactInformationOptions(parse(contactInformationResponse), COMPANY, PARTY, '55555555-5555-5555-5555-555555555556')).toBeNull()
    expect(decodeContactInformationOptions(parse(contactInformationResponse), '88888888-8888-8888-8888-888888888888', PARTY, ACCOUNT)).toBeNull()
  })

  it('refuses a repeated address, a blank version, a channel member, or a payment-like member anywhere', () => {
    const repeated = parse(contactInformationResponse) as unknown as Lists
    repeated.addresses.push(clone(repeated.addresses[0]))
    expect(decodeContactInformationOptions(repeated, COMPANY, PARTY, ACCOUNT)).toBeNull()
    const blank = parse(contactInformationResponse) as unknown as Lists
    blank.contacts[0].version = ' '
    expect(decodeContactInformationOptions(blank, COMPANY, PARTY, ACCOUNT)).toBeNull()
    const channel = parse(contactInformationResponse) as unknown as Lists
    channel.contacts[0].channels = []
    expect(decodeContactInformationOptions(channel, COMPANY, PARTY, ACCOUNT)).toBeNull()
    const paid = parse(contactInformationResponse) as unknown as Lists
    paid.addresses[0].paymentTerms = null
    expect(decodeContactInformationOptions(paid, COMPANY, PARTY, ACCOUNT)).toBeNull()
    // Empty lists are the owner's honest "none", not a failure.
    expect(
      decodeContactInformationOptions(
        { companyId: COMPANY, partyId: PARTY, companyAccountId: ACCOUNT, accountVersion: '2', addresses: [], contacts: [] },
        COMPANY,
        PARTY,
        ACCOUNT,
      ),
    ).toEqual({ partyId: PARTY, companyAccountId: ACCOUNT, accountVersion: '2', addresses: [], contacts: [] })
  })
})


/** ChatGPT-authored synthetic example of the existing archived-history wire; not production master data. */
describe('decodeDraftRevisions', () => {
  const body = () => JSON.parse(revisionsResponse) as {
    items: Array<Record<string, any> & { lines: Array<Record<string, any>> }>
  }

  it('preserves every archived snapshot and decimal/version text without rounding', () => {
    const input = body()
    expect(decodeDraftRevisions(input)).toEqual(input)
    expect(decodeDraftRevisions(input)?.items[0].lines[0]).toMatchObject({
      quantity: '1000.000', unitPrice: '12.500000', productVersion: '9007199254740993', discountPercent: '0',
    })
  })

  it('keeps empty history empty and nullable historical facts null', () => {
    expect(decodeDraftRevisions({ items: [] })).toEqual({ items: [] })
    const input = body()
    Object.assign(input.items[0], { submittedBy: null, submittedAtUtc: null, approvedBy: null, approvedAtUtc: null, moneyScale: 0 })
    expect(decodeDraftRevisions(input)).toEqual(input)
  })

  it('distinguishes Visible zero, Visible unpriced and Withheld without inventing money', () => {
    const input = body()
    input.items[0].lines[0].unitPrice = '0'
    expect(decodeDraftRevisions(input)?.items[0].lines[0].unitPrice).toBe('0')
    Object.assign(input.items[0].lines[0], { unitPrice: null, priceUnitId: null, discountPercent: null })
    expect(decodeDraftRevisions(input)?.items[0].lines[0].priceDisclosure).toBe('Visible')
    input.items[0].lines[0].priceDisclosure = 'Withheld'
    expect(decodeDraftRevisions(input)?.items[0].lines[0].priceDisclosure).toBe('Withheld')
  })

  it('keeps a quantity revision reason verbatim (DEC-SAL-002) and a price revision without one null', () => {
    const input = body()
    expect(decodeDraftRevisions(input)?.items[0].reason).toBeNull()
    input.items[0].reason = '  SENTETIK müşteri 2 kg istedi '
    expect(decodeDraftRevisions(input)?.items[0].reason).toBe('  SENTETIK müşteri 2 kg istedi ')
  })

  it('accepts numeric revision order through Rev.10, allowing the same line in different snapshots', () => {
    const input = body()
    input.items = ['Rev.0', 'Rev.2', 'Rev.10'].map((revision) => ({ ...clone(input.items[0]), revision }))
    expect(decodeDraftRevisions(input)).toEqual(input)
  })

  it.each([null, [], {}, { items: null }, { items: [], nextCursor: null }])('rejects a malformed history envelope %j', (input) => {
    expect(decodeDraftRevisions(input)).toBeNull()
  })

  it.each([
    ['unknown revision member', (r: any) => { r.total = '0' }],
    ['missing nullable member', (r: any) => { delete r.approvedBy }],
    ['missing reason member', (r: any) => { delete r.reason }],
    ['blank reason', (r: any) => { r.reason = '  ' }],
    ['empty reason', (r: any) => { r.reason = '' }],
    ['numeric reason', (r: any) => { r.reason = 1 }],
    ['over-long reason', (r: any) => { r.reason = 'x'.repeat(4001) }],
    ['numeric revision', (r: any) => { r.revision = 1 }],
    ['noncanonical revision', (r: any) => { r.revision = 'Rev.01' }],
    ['line-terminated revision', (r: any) => { r.revision = 'Rev.0\n' }],
    ['out-of-range revision', (r: any) => { r.revision = 'Rev.2147483648' }],
    ['invalid instant', (r: any) => { r.archivedAtUtc = 'yesterday' }],
    ['nonexistent calendar date', (r: any) => { r.approvedAtUtc = '2026-02-30T09:00:00Z' }],
    ['impossible time', (r: any) => { r.archivedAtUtc = '2026-09-23T24:00:00Z' }],
    ['null archiver', (r: any) => { r.archivedBy = null }],
    ['blank archiver', (r: any) => { r.archivedBy = '  ' }],
    ['empty money id', (r: any) => { r.moneyProfileId = '00000000-0000-0000-0000-000000000000' }],
    ['malformed money id', (r: any) => { r.moneyProfileId = 'not-an-id' }],
    ['blank profile version', (r: any) => { r.moneyProfileVersion = ' ' }],
    ['negative scale', (r: any) => { r.moneyScale = -1 }],
    ['fractional scale', (r: any) => { r.moneyScale = 1.5 }],
    ['string scale', (r: any) => { r.moneyScale = '2' }],
    ['empty lines', (r: any) => { r.lines = [] }],
    ['unknown line member', (r: any) => { r.lines[0].productName = 'current, not archived' }],
    ['missing disclosure', (r: any) => { delete r.lines[0].priceDisclosure }],
    ['unknown disclosure', (r: any) => { r.lines[0].priceDisclosure = 'Hidden' }],
    ['missing nullable price', (r: any) => { delete r.lines[0].unitPrice }],
    ['numeric price', (r: any) => { r.lines[0].unitPrice = 12.5 }],
    ['exponent price', (r: any) => { r.lines[0].unitPrice = '1e3' }],
    ['signed discount', (r: any) => { r.lines[0].discountPercent = '-1' }],
    ['zero quantity', (r: any) => { r.lines[0].quantity = '0.000' }],
    ['newline quantity', (r: any) => { r.lines[0].quantity = '1\n' }],
    ['unpaired price', (r: any) => { r.lines[0].priceUnitId = null }],
    ['blank product version', (r: any) => { r.lines[0].productVersion = '' }],
    ['numeric product version', (r: any) => { r.lines[0].productVersion = 3 }],
    ['invalid product id', (r: any) => { r.lines[0].productId = 'bad' }],
    ['newline id', (r: any) => { r.lines[0].lineId += '\n' }],
    ['zero line number', (r: any) => { r.lines[0].lineNo = 0 }],
    ['duplicate line id', (r: any) => { r.lines.push({ ...r.lines[0], lineNo: 2 }) }],
    ['duplicate line number', (r: any) => { r.lines.push({ ...r.lines[0], lineId: 'ffffffff-ffff-4fff-8fff-ffffffffffff' }) }],
    ['withheld price', (r: any) => { r.lines[0].priceDisclosure = 'Withheld' }],
    ['withheld discount only', (r: any) => { Object.assign(r.lines[0], { priceDisclosure: 'Withheld', unitPrice: null, priceUnitId: null }) }],
  ])('refuses %s', (_, mutate) => {
    const input = body()
    mutate(input.items[0])
    expect(decodeDraftRevisions(input)).toBeNull()
  })

  it('rejects duplicate revisions and out-of-order revisions', () => {
    const input = body()
    input.items.push(clone(input.items[0]))
    expect(decodeDraftRevisions(input)).toBeNull()
    input.items[0].revision = 'Rev.10'
    input.items[1].revision = 'Rev.2'
    expect(decodeDraftRevisions(input)).toBeNull()
  })
})

/* SALES-CARI-OPTIN (#626): the record-bound Cari display, added by the opt-in read contract to the list rows and the detail. */
import {
  decodeDraftDetailWithParty,
  decodeDraftPageWithParty,
  decodeDraftSummary,
  decodeDraftSummaryWithParty,
} from './contract'

describe('the record-bound Cari display (opt-in list/detail only)', () => {
  const display = (partyId: string, tradeName: string | null = null) => ({ partyId, code: 'CARI-000123', legalName: 'ABC Demir Sanayi ve Ticaret A.Ş.', tradeName })
  const page = () => {
    const body = JSON.parse(listResponse)
    for (const item of body.items) item.partyDisplay = display(item.partyId)
    return body
  }
  const detail = () => {
    const body = JSON.parse(detailResponse)
    body.partyDisplay = display(body.draft.partyId)
    return body
  }

  it('decodes the enriched page and detail with the legacy rules unchanged around the display', () => {
    const decodedPage = decodeDraftPageWithParty(page())
    const legacyPage = decodeDraftPage(JSON.parse(listResponse))
    expect(decodedPage?.items.map(({ partyDisplay: _display, ...rest }) => rest)).toEqual(legacyPage?.items)
    expect(decodedPage?.items.every((item) => item.partyDisplay.partyId === item.partyId)).toBe(true)
    const decodedDetail = decodeDraftDetailWithParty(detail(), null)
    const { partyDisplay, ...rest } = decodedDetail!
    expect(rest).toEqual(decodeDraftDetail(JSON.parse(detailResponse), null))
    expect(partyDisplay).toEqual(display(rest.draft.partyId))
  })

  it('keeps the legacy and mutation decoders exactly as they were: no display required, none accepted', () => {
    expect(decodeDraftPage(JSON.parse(listResponse))).not.toBeNull()
    expect(decodeDraftDetail(JSON.parse(detailResponse), null)).not.toBeNull()
    expect(decodeDraftMutation(JSON.parse(createResponse))).not.toBeNull()
    // The legacy contract has no such member, so a legacy read that carried one is not that contract.
    expect(decodeDraftSummary({ ...JSON.parse(listResponse).items[0], partyDisplay: display(JSON.parse(listResponse).items[0].partyId) })).toBeNull()
    expect(decodeDraftDetail(detail(), null)).toBeNull()
  })

  it('requires the display: an enriched row or detail without one is refused', () => {
    expect(decodeDraftSummaryWithParty(JSON.parse(listResponse).items[0])).toBeNull()
    expect(decodeDraftPageWithParty(JSON.parse(listResponse))).toBeNull()
    expect(decodeDraftDetailWithParty(JSON.parse(detailResponse), null)).toBeNull()
  })

  it.each([
    ['another party', { partyId: '11111111-1111-1111-1111-111111111112' }],
    ['a party id that is not a reference', { partyId: 'not-a-uuid' }],
    ['a blank code', { code: ' ' }],
    ['a code that is not text', { code: 123 }],
    ['an empty legal name', { legalName: '' }],
    ['a blank trade name', { tradeName: '   ' }],
  ])('refuses a display with %s', (_, change) => {
    const body = detail()
    Object.assign(body.partyDisplay, change)
    expect(decodeDraftDetailWithParty(body, null)).toBeNull()
  })

  it('refuses a display with a missing or an unknown member', () => {
    const missing = detail()
    delete missing.partyDisplay.tradeName
    expect(decodeDraftDetailWithParty(missing, null)).toBeNull()
    const extra = detail()
    extra.partyDisplay.taxNumber = '1234567890'
    expect(decodeDraftDetailWithParty(extra, null)).toBeNull()
  })

  it('accepts a party id differing only in letter case, and keeps a published trade name', () => {
    const body = detail()
    body.partyDisplay = display(body.draft.partyId.toUpperCase(), 'ABC Demir')
    expect(decodeDraftDetailWithParty(body, null)?.partyDisplay.tradeName).toBe('ABC Demir')
  })

  it('still applies the detail\'s own record binding', () => {
    expect(decodeDraftDetailWithParty(detail(), 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab')).toBeNull()
  })
})
