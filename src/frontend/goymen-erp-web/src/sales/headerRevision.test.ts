import { describe, expect, it } from 'vitest'
import { buildHeaderRevisionRequest, decodeDraftHeader, decodeSalesOwnerOptions, headerFormValues, type DraftHeader } from './headerRevision'

const ID = (n: number) => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`

const wire = (overrides: Record<string, unknown> = {}) => ({
  draftVersion: '4', revision: 'Rev.1', partyId: ID(1), companyAccountId: ID(2), currencyId: ID(3),
  deliveryAddress: { id: ID(4), expectedVersion: '2' }, contact: null, salesOwnerUserId: ID(5), salesOwnerDisplayName: 'Satışçı',
  dueTermsDisclosure: 'Visible',
  dueTerms: { instalments: [{ sequenceNo: 1, basis: 'InvoiceDate', daysAfter: 30, sharePercent: '100' }] },
  customerOrCurrencyRevisionPending: false, ...overrides,
})

describe('API-04 header contract', () => {
  it('decodes the revisable header exactly and refuses what it cannot vouch for', () => {
    const decoded = decodeDraftHeader(wire())
    expect(decoded?.dueTerms?.instalments[0]).toEqual({ sequenceNo: 1, basis: 'InvoiceDate', daysAfter: 30, sharePercent: '100' })
    expect(decodeDraftHeader({ ...wire(), extra: 1 })).toBeNull()
    expect(decodeDraftHeader(wire({ draftVersion: 4 }))).toBeNull()
    expect(decodeDraftHeader(wire({ dueTermsDisclosure: 'Withheld' }))).toBeNull()
    expect(decodeDraftHeader(wire({ dueTermsDisclosure: 'Withheld', dueTerms: null }))?.dueTerms).toBeNull()
    expect(decodeDraftHeader(wire({ dueTerms: { instalments: [{ sequenceNo: 2, basis: 'InvoiceDate', daysAfter: 30, sharePercent: '100' }] } }))).toBeNull()
    expect(decodeDraftHeader(wire({ dueTerms: { instalments: [{ sequenceNo: 1, basis: 'invoice', daysAfter: 30, sharePercent: '100' }] } }))).toBeNull()
    expect(decodeDraftHeader(wire({ dueTerms: { instalments: [{ sequenceNo: 1, basis: 'InvoiceDate', daysAfter: -1, sharePercent: '100' }] } }))).toBeNull()
    expect(decodeDraftHeader(wire({ salesOwnerUserId: null }))).toBeNull()
  })

  it('decodes the sales owners of this company only, each once', () => {
    expect(decodeSalesOwnerOptions({ companyId: ID(9), owners: [{ userId: ID(5), displayName: 'A' }], hasMore: true }, ID(9)))
      .toEqual({ owners: [{ userId: ID(5), displayName: 'A' }], hasMore: true })
    expect(decodeSalesOwnerOptions({ companyId: ID(8), owners: [], hasMore: false }, ID(9))).toBeNull()
    expect(decodeSalesOwnerOptions({ companyId: ID(9), owners: [{ userId: ID(5), displayName: 'A' }, { userId: ID(5), displayName: 'B' }], hasMore: false }, ID(9))).toBeNull()
  })

  it('builds the whole header, reads decimal commas as text and refuses empty or unchanged revisions locally', () => {
    const header = decodeDraftHeader(wire()) as DraftHeader
    let n = 0
    const values = headerFormValues(header, () => `k${(n += 1)}`)
    expect(buildHeaderRevisionRequest(header, values)).toEqual({ ok: false, error: { kind: 'reason-required' } })
    expect(buildHeaderRevisionRequest(header, { ...values, reason: 'r' })).toEqual({ ok: false, error: { kind: 'no-change' } })
    const instalments = [
      { key: 'a', basis: 'InvoiceDate' as const, daysAfter: '30', sharePercent: '33,5' },
      { key: 'b', basis: 'OrderDate' as const, daysAfter: '0', sharePercent: '66.5' },
    ]
    const built = buildHeaderRevisionRequest(header, { ...values, reason: 'r', instalments })
    expect(built).toEqual({ ok: true, payload: expect.objectContaining({
      expectedVersion: '4', partyId: ID(1), deliveryAddress: { id: ID(4), expectedVersion: '2' }, salesOwnerUserId: ID(5),
      dueTerms: { instalments: [
        { sequenceNo: 1, basis: 'InvoiceDate', daysAfter: 30, sharePercent: '33.5' },
        { sequenceNo: 2, basis: 'OrderDate', daysAfter: 0, sharePercent: '66.5' },
      ] },
    }) })
    expect(buildHeaderRevisionRequest(header, { ...values, reason: 'r', instalments: [{ ...instalments[0]!, daysAfter: '-1' }] }))
      .toEqual({ ok: false, error: { kind: 'days', row: 1 } })
    expect(buildHeaderRevisionRequest(header, { ...values, reason: 'r', instalments: [{ ...instalments[0]!, sharePercent: '0' }] }))
      .toEqual({ ok: false, error: { kind: 'share', row: 1 } })
    expect(buildHeaderRevisionRequest(header, { ...values, reason: 'r', instalments: [] }))
      .toEqual({ ok: false, error: { kind: 'no-instalment' } })
    // Clearing the due terms is a change, sent as an explicit null.
    const cleared = buildHeaderRevisionRequest(header, { ...values, reason: 'r', instalments: null })
    expect(cleared.ok && cleared.payload.dueTerms).toBeNull()
  })
})
