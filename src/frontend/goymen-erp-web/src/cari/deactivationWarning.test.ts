/**
 * ALS-D99 / ALS-D100 decoders — strict, fail-closed. A shape the backend contract does not name is `null`, never a
 * partly trusted warning, preview or per-company result.
 */

import { describe, expect, it } from 'vitest'
import {
  buildGroupDeactivationBody,
  decodeDeactivationWarningView,
  decodeGroupDeactivationPreview,
  decodeGroupDeactivationResult,
  deactivationWarningPath,
  groupDeactivationPath,
  groupDeactivationPreviewPath,
} from './deactivationWarning'
import { A_ID, B_ID, PARTY, PARTY_2, deactivationWarningView } from './commandPageHarness'

const BOUND = { partyId: PARTY, companyId: A_ID }

function openView(over: Record<string, unknown> = {}) {
  return deactivationWarningView(PARTY, A_ID, {
    items: [
      { kind: 'finance-open-balance', outcome: 'open', openItemCount: 2 },
      { kind: 'sales-open-documents', outcome: 'unavailable', openItemCount: null },
      { kind: 'purchasing-open-documents', outcome: 'denied', openItemCount: null },
    ],
    acknowledgementRequired: true,
    ...over,
  })
}

describe('paths', () => {
  it('names the three endpoints under the party', () => {
    expect(deactivationWarningPath(PARTY)).toBe(`/counterparties/${PARTY}/company-account/deactivation-warning`)
    expect(groupDeactivationPreviewPath(PARTY)).toBe(`/counterparties/${PARTY}/group-deactivation-preview`)
    expect(groupDeactivationPath(PARTY)).toBe(`/counterparties/${PARTY}/group-deactivation`)
  })
})

describe('decodeDeactivationWarningView', () => {
  it('accepts an all-clear warning that needs no acknowledgement', () => {
    const view = decodeDeactivationWarningView(deactivationWarningView(PARTY, A_ID), BOUND)
    expect(view).not.toBeNull()
    expect(view?.acknowledgementRequired).toBe(false)
    expect(view?.items.map((item) => item.kind)).toEqual([
      'finance-open-balance',
      'sales-open-documents',
      'purchasing-open-documents',
    ])
  })

  it('accepts open, unavailable and denied lines and requires the acknowledgement', () => {
    const view = decodeDeactivationWarningView(openView(), BOUND)
    expect(view?.acknowledgementRequired).toBe(true)
    expect(view?.items[0]).toEqual({ kind: 'finance-open-balance', outcome: 'open', openItemCount: 2 })
    expect(view?.items[1].openItemCount).toBeNull()
  })

  it('treats an unavailable line alone as a warning, never as nothing open', () => {
    const raw = deactivationWarningView(PARTY, A_ID, {
      items: [
        { kind: 'finance-open-balance', outcome: 'unavailable', openItemCount: null },
        { kind: 'sales-open-documents', outcome: 'clear', openItemCount: null },
        { kind: 'purchasing-open-documents', outcome: 'clear', openItemCount: null },
      ],
      acknowledgementRequired: true,
    })
    expect(decodeDeactivationWarningView(raw, BOUND)?.acknowledgementRequired).toBe(true)
  })

  it.each([
    ['an extra key', { ...openView(), amount: '10.00' }],
    ['a missing key', (() => { const raw = openView(); delete (raw as Record<string, unknown>).isActive; return raw })()],
    ['a numeric version', openView({ accountVersion: 7 })],
    ['a fractional version', openView({ accountVersion: '7.5' })],
    ['a flag that says clear while a line is open', openView({ acknowledgementRequired: false })],
    [
      'a flag that says required while every line is clear',
      deactivationWarningView(PARTY, A_ID, { acknowledgementRequired: true }),
    ],
    ['another party', deactivationWarningView(PARTY_2, A_ID)],
    ['another company', deactivationWarningView(PARTY, B_ID)],
    [
      'lines in another order',
      openView({
        items: [
          { kind: 'sales-open-documents', outcome: 'unavailable', openItemCount: null },
          { kind: 'finance-open-balance', outcome: 'open', openItemCount: 2 },
          { kind: 'purchasing-open-documents', outcome: 'denied', openItemCount: null },
        ],
      }),
    ],
    [
      'a missing line',
      openView({
        items: [
          { kind: 'finance-open-balance', outcome: 'open', openItemCount: 2 },
          { kind: 'sales-open-documents', outcome: 'unavailable', openItemCount: null },
        ],
      }),
    ],
    [
      'an open line without a count',
      openView({
        items: [
          { kind: 'finance-open-balance', outcome: 'open', openItemCount: null },
          { kind: 'sales-open-documents', outcome: 'unavailable', openItemCount: null },
          { kind: 'purchasing-open-documents', outcome: 'denied', openItemCount: null },
        ],
      }),
    ],
    [
      'an open line with a zero count',
      openView({
        items: [
          { kind: 'finance-open-balance', outcome: 'open', openItemCount: 0 },
          { kind: 'sales-open-documents', outcome: 'unavailable', openItemCount: null },
          { kind: 'purchasing-open-documents', outcome: 'denied', openItemCount: null },
        ],
      }),
    ],
    [
      'a clear line with a count',
      deactivationWarningView(PARTY, A_ID, {
        items: [
          { kind: 'finance-open-balance', outcome: 'clear', openItemCount: 0 },
          { kind: 'sales-open-documents', outcome: 'clear', openItemCount: null },
          { kind: 'purchasing-open-documents', outcome: 'clear', openItemCount: null },
        ],
      }),
    ],
    [
      'an unknown outcome',
      openView({
        items: [
          { kind: 'finance-open-balance', outcome: 'blocked', openItemCount: null },
          { kind: 'sales-open-documents', outcome: 'unavailable', openItemCount: null },
          { kind: 'purchasing-open-documents', outcome: 'denied', openItemCount: null },
        ],
      }),
    ],
    [
      'an extra key on a line',
      openView({
        items: [
          { kind: 'finance-open-balance', outcome: 'open', openItemCount: 2, amount: '10.00' },
          { kind: 'sales-open-documents', outcome: 'unavailable', openItemCount: null },
          { kind: 'purchasing-open-documents', outcome: 'denied', openItemCount: null },
        ],
      }),
    ],
  ])('rejects %s', (_label, raw) => {
    expect(decodeDeactivationWarningView(raw, BOUND)).toBeNull()
  })

  it('rejects a non-object answer', () => {
    expect(decodeDeactivationWarningView(null, BOUND)).toBeNull()
    expect(decodeDeactivationWarningView([], BOUND)).toBeNull()
  })
})

describe('decodeGroupDeactivationPreview', () => {
  const preview = (companies: unknown[]) => ({ partyId: PARTY, companies })
  const entry = (companyId: string, granted = true) => ({
    companyId,
    groupPermissionGranted: granted,
    exposure: deactivationWarningView(PARTY, companyId),
  })

  it('accepts every reachable company with its own bound warning', () => {
    const decoded = decodeGroupDeactivationPreview(preview([entry(A_ID), entry(B_ID, false)]), { partyId: PARTY })
    expect(decoded?.companies.map((c) => [c.companyId, c.groupPermissionGranted])).toEqual([
      [A_ID, true],
      [B_ID, false],
    ])
  })

  it('rejects an empty list, a repeated company, another party and an exposure bound to another company', () => {
    expect(decodeGroupDeactivationPreview(preview([]), { partyId: PARTY })).toBeNull()
    expect(decodeGroupDeactivationPreview(preview([entry(A_ID), entry(A_ID)]), { partyId: PARTY })).toBeNull()
    expect(decodeGroupDeactivationPreview(preview([entry(A_ID)]), { partyId: PARTY_2 })).toBeNull()
    expect(
      decodeGroupDeactivationPreview(
        preview([{ ...entry(A_ID), exposure: deactivationWarningView(PARTY, B_ID) }]),
        { partyId: PARTY },
      ),
    ).toBeNull()
    expect(
      decodeGroupDeactivationPreview({ ...preview([entry(A_ID)]), inaccessibleCount: 1 }, { partyId: PARTY }),
    ).toBeNull()
  })
})

describe('decodeGroupDeactivationResult', () => {
  const outcome = (companyId: string, value: string, currentVersion: string | null = null, replayed = false) => ({
    companyId,
    outcome: value,
    replayed,
    currentVersion,
  })
  const result = (companies: unknown[]) => ({ partyId: PARTY, companies })

  it('accepts one answer per requested company and not-requested for the others', () => {
    const decoded = decodeGroupDeactivationResult(
      result([outcome(A_ID, 'deactivated'), outcome(B_ID, 'not-requested')]),
      { partyId: PARTY, requestedCompanyIds: [A_ID] },
    )
    expect(decoded?.companies.map((c) => c.outcome)).toEqual(['deactivated', 'not-requested'])
  })

  it('accepts a version conflict with the lossless current version', () => {
    const decoded = decodeGroupDeactivationResult(result([outcome(A_ID, 'version-conflict', '9223372036854775807')]), {
      partyId: PARTY,
      requestedCompanyIds: [A_ID],
    })
    expect(decoded?.companies[0].currentVersion).toBe('9223372036854775807')
  })

  it.each([
    ['a requested company left unanswered', [outcome(B_ID, 'not-requested')]],
    ['a requested company reported as not requested', [outcome(A_ID, 'not-requested')]],
    ['an unrequested company reported as changed', [outcome(A_ID, 'deactivated'), outcome(B_ID, 'deactivated')]],
    ['a repeated company', [outcome(A_ID, 'deactivated'), outcome(A_ID, 'already-passive')]],
    ['a version on a success', [outcome(A_ID, 'deactivated', '8')]],
    ['a numeric version on a conflict', [{ ...outcome(A_ID, 'version-conflict'), currentVersion: 8 }]],
    ['an unknown outcome', [outcome(A_ID, 'skipped')]],
  ])('rejects %s', (_label, companies) => {
    expect(
      decodeGroupDeactivationResult(result(companies), { partyId: PARTY, requestedCompanyIds: [A_ID] }),
    ).toBeNull()
  })
})

describe('buildGroupDeactivationBody', () => {
  it('carries each fence verbatim as decimal text, the reason and the acknowledgement', () => {
    expect(
      buildGroupDeactivationBody(
        [
          { companyId: A_ID, expectedVersion: '7' },
          { companyId: B_ID, expectedVersion: '9223372036854775807' },
        ],
        'Grup çapında çalışma sona erdi.',
        true,
      ),
    ).toEqual({
      targets: [
        { companyId: A_ID, expectedVersion: '7' },
        { companyId: B_ID, expectedVersion: '9223372036854775807' },
      ],
      reason: 'Grup çapında çalışma sona erdi.',
      exposureWarningAcknowledged: true,
    })
  })

  it('refuses no target, an empty reason, a repeated company and a bad fence', () => {
    expect(buildGroupDeactivationBody([], 'Gerekçe', false)).toBeNull()
    expect(buildGroupDeactivationBody([{ companyId: A_ID, expectedVersion: '1' }], '', false)).toBeNull()
    expect(
      buildGroupDeactivationBody(
        [
          { companyId: A_ID, expectedVersion: '1' },
          { companyId: A_ID.toUpperCase(), expectedVersion: '2' },
        ],
        'Gerekçe',
        false,
      ),
    ).toBeNull()
    expect(buildGroupDeactivationBody([{ companyId: A_ID, expectedVersion: '1.0' }], 'Gerekçe', false)).toBeNull()
  })
})
