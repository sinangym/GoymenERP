/**
 * KIM-D41/KIM-D48 — the staged VKN correction decoders and the apply command's wire rules. Every case is a synthetic
 * fixture; nothing here is evidence of the real endpoint.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  COMPANY_TAX_ID_PROBLEM,
  decodeCompanyTaxIdCorrections,
  decodeTaxIdCorrection,
} from './companyTaxIdContract'
import {
  sendCompanyTaxIdCorrectionApply,
  taxIdCorrectionApplyPath,
  taxIdCorrectionApplyWireBody,
} from './companyTaxIdApi'
import { taxIdDoneNotice, taxIdFailureText } from './companyTaxIdText'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const OTHER_COMPANY = 'bbbbbbbb-0000-0000-0000-000000000002'
const CORRECTION = 'dddddddd-0000-0000-0000-000000000004'
const SECOND = 'eeeeeeee-0000-0000-0000-000000000005'
const KEY = 'ffffffff-0000-0000-0000-000000000006'

function pending(overrides: Record<string, unknown> = {}) {
  return {
    id: CORRECTION,
    source: 'organization.migration.stage-company-tax-id-corrections',
    stagedVkn: '1111111111',
    correctedVkn: null,
    reason: 'Yer tutucu VKN işaretlendi',
    status: 'pending',
    stagedAtUtc: '2026-11-15T20:00:00Z',
    appliedBy: null,
    appliedAtUtc: null,
    isCurrent: true,
    ...overrides,
  }
}

function applied(overrides: Record<string, unknown> = {}) {
  return pending({
    id: SECOND,
    status: 'applied',
    appliedBy: 'Onaylayan B',
    appliedAtUtc: '2026-11-15T21:00:00Z',
    isCurrent: false,
    ...overrides,
  })
}

function list(overrides: Record<string, unknown> = {}) {
  return { companyId: COMPANY, companyVersion: 4, canApply: true, items: [pending()], ...overrides }
}

describe('decodeTaxIdCorrection', () => {
  it('reads a pending clear and an applied correction', () => {
    expect(decodeTaxIdCorrection(pending())?.correctedVkn).toBeNull()
    expect(decodeTaxIdCorrection(applied({ correctedVkn: '1234567890' }))?.appliedBy).toBe('Onaylayan B')
  })

  it('refuses a missing or extra key', () => {
    const { source: _source, ...missing } = pending()
    expect(decodeTaxIdCorrection(missing)).toBeNull()
    expect(decodeTaxIdCorrection({ ...pending(), approver: 'x' })).toBeNull()
  })

  it('refuses a corrected VKN equal to the staged one or not ten digits', () => {
    expect(decodeTaxIdCorrection(pending({ correctedVkn: '1111111111' }))).toBeNull()
    expect(decodeTaxIdCorrection(pending({ correctedVkn: '12345' }))).toBeNull()
  })

  it('refuses an unknown status', () => {
    expect(decodeTaxIdCorrection(pending({ status: 'rejected' }))).toBeNull()
  })

  it('refuses a pending correction with apply facts and an applied one without them', () => {
    expect(decodeTaxIdCorrection(pending({ appliedBy: 'x' }))).toBeNull()
    expect(decodeTaxIdCorrection(applied({ appliedAtUtc: null }))).toBeNull()
  })

  it('refuses an applied correction that claims to be current', () => {
    expect(decodeTaxIdCorrection(applied({ isCurrent: true }))).toBeNull()
  })
})

describe('decodeCompanyTaxIdCorrections', () => {
  it('reads the exact server shape, the empty list included', () => {
    expect(decodeCompanyTaxIdCorrections(list(), COMPANY)?.items).toHaveLength(1)
    expect(decodeCompanyTaxIdCorrections(list({ items: [] }), COMPANY)?.items).toHaveLength(0)
  })

  it('refuses an answer about another company', () => {
    expect(decodeCompanyTaxIdCorrections(list({ companyId: OTHER_COMPANY }), COMPANY)).toBeNull()
  })

  it('refuses the whole list when one item is malformed', () => {
    expect(decodeCompanyTaxIdCorrections(list({ items: [pending(), { id: SECOND }] }), COMPANY)).toBeNull()
  })

  it('refuses two pending corrections and a repeated id', () => {
    expect(decodeCompanyTaxIdCorrections(list({ items: [pending(), pending({ id: SECOND })] }), COMPANY)).toBeNull()
    expect(decodeCompanyTaxIdCorrections(list({ items: [applied(), applied()] }), COMPANY)).toBeNull()
  })

  it('refuses a non-positive company version', () => {
    expect(decodeCompanyTaxIdCorrections(list({ companyVersion: 0 }), COMPANY)).toBeNull()
  })
})

describe('sendCompanyTaxIdCorrectionApply', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  function stub(response: Response) {
    const fetchMock = vi.fn().mockResolvedValue(response)
    vi.stubGlobal('fetch', fetchMock)
    return fetchMock
  }

  function problem(status: number, code: string, headers: Record<string, string> = {}) {
    return new Response(JSON.stringify({ code }), {
      status,
      headers: { 'Content-Type': 'application/problem+json', ...headers },
    })
  }

  it('sends only the read version, with the caller key, to the correction path', async () => {
    const fetchMock = stub(new Response(null, { status: 204 }))
    const outcome = await sendCompanyTaxIdCorrectionApply(COMPANY, KEY, { correctionId: CORRECTION, expectedVersion: 4 })
    expect(outcome).toEqual({ type: 'applied', replayed: false })
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url.endsWith(taxIdCorrectionApplyPath(CORRECTION))).toBe(true)
    expect(JSON.parse(init.body as string)).toEqual({ expectedVersion: 4 })
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe(KEY)
    expect(taxIdCorrectionApplyWireBody({ correctionId: CORRECTION, expectedVersion: 4 })).toEqual({
      expectedVersion: 4,
    })
  })

  it('reports a replayed apply as replayed', async () => {
    stub(new Response(null, { status: 204, headers: { 'Idempotency-Replayed': 'true' } }))
    const outcome = await sendCompanyTaxIdCorrectionApply(COMPANY, KEY, { correctionId: CORRECTION, expectedVersion: 4 })
    expect(outcome).toEqual({ type: 'applied', replayed: true })
  })

  it('classifies the two correction refusals by their stable code', async () => {
    stub(problem(409, COMPANY_TAX_ID_PROBLEM.correctionNotPending))
    expect(
      await sendCompanyTaxIdCorrectionApply(COMPANY, KEY, { correctionId: CORRECTION, expectedVersion: 4 }),
    ).toEqual({ type: 'failed', failure: { kind: 'tax-id-correction-not-pending' }, replayed: false })
    stub(problem(409, COMPANY_TAX_ID_PROBLEM.correctionStale))
    expect(
      await sendCompanyTaxIdCorrectionApply(COMPANY, KEY, { correctionId: CORRECTION, expectedVersion: 4 }),
    ).toEqual({ type: 'failed', failure: { kind: 'tax-id-correction-stale' }, replayed: false })
  })

  it('keeps an unknown 409 code and a 5xx as UNKNOWN', async () => {
    stub(problem(409, 'organization_something_else'))
    expect(
      (await sendCompanyTaxIdCorrectionApply(COMPANY, KEY, { correctionId: CORRECTION, expectedVersion: 4 })).type,
    ).toBe('unknown')
    stub(new Response(null, { status: 503 }))
    expect(
      (await sendCompanyTaxIdCorrectionApply(COMPANY, KEY, { correctionId: CORRECTION, expectedVersion: 4 })).type,
    ).toBe('unknown')
  })
})

describe('correction texts', () => {
  it('gives each correction refusal its own Turkish text', () => {
    const notPending = taxIdFailureText({ kind: 'tax-id-correction-not-pending' })
    const stale = taxIdFailureText({ kind: 'tax-id-correction-stale' })
    expect(notPending.title).not.toBe(stale.title)
    expect(taxIdDoneNotice('corrected', false).outcome).toBe('corrected')
    expect(taxIdDoneNotice('corrected', true).outcome).toBe('replayed')
  })
})
